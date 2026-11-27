// VRRemoteRigs — renders other kkvr players' streamed poses as simple
// markers (head/hand/hip spheres), so two VR players can SEE each other's
// rig before real avatar work exists. Spheres on the default layer (visible
// in the headset AND on the desktop preview), per-actor hue for identity,
// colliders stripped. A 1.5s pose timeout removes the markers when a player
// leaves, toggles VR, or stops streaming.
//
// Marker shader: "Sprites/Default" — the game's UI uses sprites, so it
// cannot be stripped from the build (the same reasoning VRUI relied on for
// overlay canvases).
using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

namespace VRKoboldKare
{
    public class VRRemoteRigs : MonoBehaviour
    {
        private Material _actorMat;         // one per actor (hue-tinted)
        private readonly Dictionary<int, Markers> _markers = new Dictionary<int, Markers>();
        private Transform _holder;

        private sealed class Markers
        {
            internal Transform head, handL, handR, hips;
        }

        internal void Initialize(VRManager m)
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null)
            {
                if (VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogWarning("VRNet: no marker shader found — remote VR markers disabled");
                enabled = false;
                return;
            }
            _actorMat = new Material(sh);
            _holder = new GameObject("VRKK_RemoteRigs").transform;
            _holder.SetParent(transform, false);
        }

        private void LateUpdate()
        {
            if (_actorMat == null) return;
            try
            {
                float now = Time.unscaledTime;
                var poses = VRNet.RemotePoses;
                int localActor = 0;
                try { if (PhotonNetwork.LocalPlayer != null) localActor = PhotonNetwork.LocalPlayer.ActorNumber; }
                catch (Exception) { }

                // Upsert markers for fresh remote poses.
                var alive = new HashSet<int>();
                foreach (var kv in poses)
                {
                    if (kv.Key == localActor) continue;
                    if (now - kv.Value.time > VRNet.PoseTimeout) continue;
                    alive.Add(kv.Key);
                    Markers mk;
                    if (!_markers.TryGetValue(kv.Key, out mk))
                    {
                        mk = CreateMarkers(kv.Key);
                        _markers[kv.Key] = mk;
                    }
                    Place(mk, kv.Value);
                }

                // Tear down markers for actors that stopped streaming.
                var dead = new List<int>();
                foreach (var kv in _markers)
                    if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
                foreach (var actor in dead)
                {
                    var mk = _markers[actor];
                    if (mk.head != null) Destroy(mk.head.gameObject);
                    if (mk.handL != null) Destroy(mk.handL.gameObject);
                    if (mk.handR != null) Destroy(mk.handR.gameObject);
                    if (mk.hips != null) Destroy(mk.hips.gameObject);
                    _markers.Remove(actor);
                }

                // Whole-lobby teardown when offline.
                if (!PhotonNetwork.InRoom && _markers.Count > 0)
                    foreach (var actor in new List<int>(_markers.Keys))
                    {
                        var mk = _markers[actor];
                        if (mk.head != null) Destroy(mk.head.gameObject);
                        if (mk.handL != null) Destroy(mk.handL.gameObject);
                        if (mk.handR != null) Destroy(mk.handR.gameObject);
                        if (mk.hips != null) Destroy(mk.hips.gameObject);
                        _markers.Remove(actor);
                    }
            }
            catch (Exception) { }
        }

        private Markers CreateMarkers(int actor)
        {
            var mk = new Markers();
            Color c = Color.HSVToRGB((actor * 0.37f) % 1f, 0.55f, 1f);
            var m = new Material(_actorMat) { color = c };
            mk.head = MakeMarker(m, 0.11f);
            mk.handL = MakeMarker(m, 0.06f);
            mk.handR = MakeMarker(m, 0.06f);
            mk.hips = MakeMarker(m, 0.09f);
            var l = VRKoboldKarePlugin.Log;
            if (l != null) l.LogInfo("VRNet: remote VR rig markers created for actor " + actor);
            return mk;
        }

        private Transform MakeMarker(Material m, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());   // markers must not collide/grab
            go.name = "VRKK_RemoteMarker";
            go.transform.SetParent(_holder, false);
            go.transform.localScale = new Vector3(size, size, size);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        private static void Place(Markers mk, VRNet.RemotePose rp)
        {
            // The sender always streams head; hands/body stream only when
            // they have data — those markers just stay wherever they were
            // until the pose timeout tears the rig down.
            if (mk.head != null) { mk.head.position = rp.headPos; mk.head.rotation = rp.headRot; }
            if (rp.handL && mk.handL != null) mk.handL.position = rp.handLPos;
            if (rp.handR && mk.handR != null) mk.handR.position = rp.handRPos;
            if (rp.bodyValid && mk.hips != null) { mk.hips.position = rp.hipsPos; mk.hips.rotation = rp.hipsRot; }
        }

        private void OnDestroy()
        {
            if (_holder != null) Destroy(_holder.gameObject);
            if (_actorMat != null) Destroy(_actorMat);
            _markers.Clear();
        }
    }
}
