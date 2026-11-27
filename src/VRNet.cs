// VR-KoboldKare — multiplayer VR presence (Photon custom properties).
//
// WHY: two kkvr players in one lobby currently have no way to know the other
// is running VR — shared systems can't adapt, and debugging "who is VR" means
// reading logs. This module announces the LOCAL player's VR state as
// "vrkk_*" Photon player properties on VR enable/disable (and on room join —
// player properties are room-scoped in PUN2), keeps a small registry of
// which remote players are VR, and logs arrivals. Purely additive: vanilla
// clients and other mods ignore unknown property keys.
//
// The property set doubles as the capability handshake for future pose
// streams (head/hand/FBT): "vrkk_hands"/"vrkk_fbt" tell receivers what to
// expect on the event channel before a single byte of pose data flows.
//
// NOT a fight-risk: we only WRITE our own player's properties and only READ
// others'. No RPCs, no remote transforms touched.
using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace VRKoboldKare
{
    internal static class VRNet
    {
        internal const int ProtocolVersion = 1;

        internal const string KeyVersion = "vrkk_v";    // int: presence protocol version
        internal const string KeyHead = "vrkk_head";    // bool: head pose streamed
        internal const string KeyHands = "vrkk_hands";  // bool: hand poses streamed
        internal const string KeyFbt = "vrkk_fbt";      // bool: full-body tracker poses streamed

        private static bool _inRoomAnnounced;   // properties re-announced per room
        private static float _nextScan;
        private static readonly Dictionary<int, bool> _knownVr = new Dictionary<int, bool>();

        /// <summary>Announce (or retract) the local VR presence. Safe to call
        /// from any main-thread path; silently skips when not in a room — the
        /// Tick re-announces once a room is joined.</summary>
        internal static void SetVR(bool on, bool head, bool hands, bool fbt)
        {
            _inRoomAnnounced = false;   // force a re-announce (new caps or room)
            try
            {
                if (!PhotonNetwork.InRoom) return;
                var props = new Hashtable();
                if (on)
                {
                    props[KeyVersion] = ProtocolVersion;
                    props[KeyHead] = head;
                    props[KeyHands] = hands;
                    props[KeyFbt] = fbt;
                }
                else
                {
                    // Null values remove the keys (PUN2 semantics).
                    props[KeyVersion] = null;
                    props[KeyHead] = null;
                    props[KeyHands] = null;
                    props[KeyFbt] = null;
                }
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
                Log("presence announced: " + (on
                    ? "VR (head=" + head + " hands=" + hands + " fbt=" + fbt + ")"
                    : "desktop"));
            }
            catch (Exception e)
            {
                Log("presence announce failed: " + e.Message);
            }
        }

        /// <summary>True when the given remote player runs kkvr with VR on.</summary>
        internal static bool IsVR(Player p)
        {
            try
            {
                if (p == null || p.CustomProperties == null) return false;
                object v;
                return p.CustomProperties.TryGetValue(KeyVersion, out v) && v is int;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Room-scoped re-announce + remote-player scan (5s cadence).
        /// Called from the persistent manager's update loop.</summary>
        internal static void Tick(VRManager mgr)
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 5f;
            try
            {
                if (!PhotonNetwork.InRoom)
                {
                    _inRoomAnnounced = false;
                    _knownVr.Clear();
                    return;
                }

                // Re-announce after a room join (VR enabled before joining).
                if (mgr != null && mgr.Enabled && !_inRoomAnnounced)
                {
                    bool openxr = VRXR.BackendName == "openxr";
                    SetVR(true, true, openxr, false);
                    return;   // next scan does the player sweep
                }

                // Sweep: log VR players as they appear (and note departures).
                var players = PhotonNetwork.PlayerList;
                if (players == null) return;
                var seen = new HashSet<int>();
                foreach (var p in players)
                {
                    if (p == null) continue;
                    seen.Add(p.ActorNumber);
                    bool vr = IsVR(p);
                    bool known;
                    _knownVr.TryGetValue(p.ActorNumber, out known);
                    if (vr && !known)
                        Log("player '" + p.NickName + "' (actor " + p.ActorNumber + ") runs VR-KoboldKare — pose/IK coexistence active");
                    _knownVr[p.ActorNumber] = vr;
                }
                var stale = new List<int>();
                foreach (var kv in _knownVr)
                    if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var actor in stale)
                    _knownVr.Remove(actor);
            }
            catch (Exception)
            {
                // Photon in a weird state (connecting/leaving) — retry next scan.
            }
        }

        private static void Log(string msg)
        {
            var l = VRKoboldKarePlugin.Log;
            if (l != null) l.LogInfo("VRNet: " + msg);
        }

        // ------------------------------------------------------------------
        // Pose broadcast (bundled with FBT per her call): 10Hz UNRELIABLE
        // Photon events carrying head/hand/body WORLD poses. Event code 'V'
        // (86) — the game claims 'A'/'C'/'H' (chat/instantiation/cheats) and
        // KK-LLM-NPC reuses 'A'. No event caching (late joiners start fresh —
        // presence properties already told them what to expect). Senders map
        // stage→world via their own rig (HeadToWorld); receivers render world
        // positions directly.
        // ------------------------------------------------------------------

        internal const byte PoseEventCode = (byte)'V';
        private const byte PoseSchemaVersion = 1;
        internal const float PoseTimeout = 1.5f;

        public struct RemotePose
        {
            public bool head, handL, handR, bodyValid;
            public Vector3 headPos; public Quaternion headRot;
            public Vector3 handLPos; public Quaternion handLRot;
            public Vector3 handRPos; public Quaternion handRRot;
            public Vector3 hipsPos; public Quaternion hipsRot;
            public Quaternion chestRot;
            public float time;
        }

        private static readonly Dictionary<int, RemotePose> _remote = new Dictionary<int, RemotePose>();
        private static bool _eventsAttached;

        /// <summary>Latest streamed pose per remote actor (actor number → pose).</summary>
        internal static IReadOnlyDictionary<int, RemotePose> RemotePoses { get { return _remote; } }

        /// <summary>Attach the receive handler (once, at manager init).</summary>
        internal static void Initialize()
        {
            if (_eventsAttached) return;
            _eventsAttached = true;
            try { PhotonNetwork.NetworkingClient.EventReceived += OnEventReceived; }
            catch (Exception e) { Log("event attach failed: " + e.Message); }
        }

        internal static void Shutdown()
        {
            if (!_eventsAttached) return;
            _eventsAttached = false;
            try { PhotonNetwork.NetworkingClient.EventReceived -= OnEventReceived; } catch (Exception) { }
            _remote.Clear();
        }

        /// <summary>Compose + raise one pose event from the current VR frame.
        /// Called at 10Hz from the manager tick while VR is enabled.</summary>
        internal static void SendPose(VRManager m)
        {
            try
            {
                if (!PhotonNetwork.InRoom || m == null) return;
                var input = m.Input;
                if (input == null || !input.FrameValid || !input.Frame.headTracked) return;
                var f = input.Frame;

                var floats = new List<float>(32);
                byte flags = 0;

                Vector3 headPos = m.HeadToWorld(f.headPos, f.headRot);
                Quaternion headRot = Quaternion.Euler(m.PitchOffset, m.YawOffset, 0f) * f.headRot;
                flags |= 1;
                AddPose(floats, headPos, headRot);

                // Hands ride the grabber-view transforms (UpdateHands parks
                // them at the camera when untracked — the desktop scheme).
                if (m.LeftHand != null && m.RightHand != null)
                {
                    flags |= 2;
                    AddPose(floats, m.LeftHand.position, m.LeftHand.rotation);
                    AddPose(floats, m.RightHand.position, m.RightHand.rotation);
                }

                if (f.body.valid && f.body.hips.tracked)
                {
                    flags |= 4;
                    AddPose(floats, m.HeadToWorld(f.body.hips.position, f.headRot), f.body.hips.rotation);
                    Quaternion chest = f.body.chest.tracked ? f.body.chest.rotation : f.body.spineMiddle.rotation;
                    floats.Add(chest.x); floats.Add(chest.y); floats.Add(chest.z); floats.Add(chest.w);
                }

                var data = new byte[2 + floats.Count * 4];
                data[0] = PoseSchemaVersion;
                data[1] = flags;
                Buffer.BlockCopy(floats.ToArray(), 0, data, 2, floats.Count * 4);
                PhotonNetwork.RaiseEvent(PoseEventCode, data,
                    new RaiseEventOptions { CachingOption = EventCaching.DoNotCache, Receivers = ReceiverGroup.Others },
                    SendOptions.SendUnreliable);
            }
            catch (Exception)
            {
                // Photon in a weird state — next tick retries.
            }
        }

        private static void AddPose(List<float> list, Vector3 p, Quaternion r)
        {
            list.Add(p.x); list.Add(p.y); list.Add(p.z);
            list.Add(r.x); list.Add(r.y); list.Add(r.z); list.Add(r.w);
        }

        private static void OnEventReceived(EventData ev)
        {
            try
            {
                if (ev.Code != PoseEventCode) return;
                var data = ev.CustomData as byte[];
                if (data == null || data.Length < 2 || data[0] != PoseSchemaVersion) return;
                byte flags = data[1];
                var floats = new float[(data.Length - 2) / 4];
                Buffer.BlockCopy(data, 2, floats, 0, floats.Length * 4);
                int i = 0;
                var rp = new RemotePose { time = Time.unscaledTime };
                if ((flags & 1) != 0) { rp.head = true; rp.headPos = ReadV3(floats, ref i); rp.headRot = ReadQ(floats, ref i); }
                if ((flags & 2) != 0) { rp.handL = true; rp.handLPos = ReadV3(floats, ref i); rp.handLRot = ReadQ(floats, ref i); rp.handR = true; rp.handRPos = ReadV3(floats, ref i); rp.handRRot = ReadQ(floats, ref i); }
                if ((flags & 4) != 0)
                {
                    rp.bodyValid = true;
                    rp.hipsPos = ReadV3(floats, ref i); rp.hipsRot = ReadQ(floats, ref i);
                    rp.chestRot = new Quaternion(floats[i], floats[i + 1], floats[i + 2], floats[i + 3]); i += 4;
                }
                _remote[(int)ev.Sender] = rp;
            }
            catch (Exception)
            {
                // Malformed/foreign payload — drop silently.
            }
        }

        private static Vector3 ReadV3(float[] f, ref int i)
        {
            var v = new Vector3(f[i], f[i + 1], f[i + 2]); i += 3; return v;
        }

        private static Quaternion ReadQ(float[] f, ref int i)
        {
            var q = new Quaternion(f[i], f[i + 1], f[i + 2], f[i + 3]); i += 4; return q;
        }
    }
}
