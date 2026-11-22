// VR-KoboldKare — Harmony patches to bridge VR into the game's systems.
//
// Two patches:
//   1. PlayerPossession.PlayerProcessing (postfix) — after the game's own Move
//      handling, overwrite KoboldCharacterController.inputDir with the VR stick
//      direction, so the controller drives locomotion.
//   2. OrbitCamera.LateUpdate (postfix) — in first-person, snap the game camera
//      to the HMD pose (true head tracking / mirror mode).
//
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VRKoboldKare
{
    internal static class VRPatches
    {
        private static Harmony _harmony;
        private static bool _patched;

        /// <summary>Apply all patches. Idempotent. Called from VRManager.InitSubsystems.</summary>
        public static void EnsurePatched(VRManager mgr)
        {
            if (_patched) return;
            _patched = true;

            try
            {
                _harmony = new Harmony("vrkk.vrkoboldkare");
                Patch_PlayerProcessing();
                Patch_OrbitCameraLateUpdate();
                Patch_OrbitCameraIntentSetter();
                Patch_SetEyeDir();
                Patch_UserSortGrabbables();
                Patch_GrabberLateUpdate();
                Patch_SpectateWriteCamera();
                if (VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogInfo("VR: Harmony patches applied.");
            }
            catch (Exception e)
            {
                if (VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogError("VR: patching failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        //  Patch 1 — VR movement (inputDir override)
        // ------------------------------------------------------------------

        private static void Patch_PlayerProcessing()
        {
            var t = typeof(PlayerPossession);
            var m = t.GetMethod("PlayerProcessing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            _harmony.Patch(m,
                postfix: new HarmonyMethod(typeof(VRPatches), nameof(PlayerProcessing_Postfix)));
        }

        private static void PlayerProcessing_Postfix(PlayerPossession __instance)
        {
            var mgr = VRKoboldKarePlugin.VRManager;
            if (mgr == null || !mgr.Enabled) return;
            if (mgr.Loco == null || mgr.Controller == null) return;
            try
            {
                // The controller stick wins ONLY while it is actively driving
                // movement — otherwise the game's own keyboard input survives.
                // (Overwriting unconditionally zeroed WASD and stranded the
                // player at spawn when no headset input was arriving.)
                if (mgr.Loco.MoveActive)
                    mgr.Controller.inputDir = mgr.Loco.GetMoveDir();
            }
            catch { }
        }
        // ------------------------------------------------------------------
        //  Patch 2 — VR camera mirror override (head tracking in first-person)
        // ------------------------------------------------------------------

        private static void Patch_OrbitCameraLateUpdate()
        {
            Type t;
            try { t = typeof(OrbitCamera); }
            catch { t = Type.GetType("OrbitCamera, com.naelstrof.orbitcam"); }
            if (t == null) return;

            var m = t.GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            _harmony.Patch(m,
                postfix: new HarmonyMethod(typeof(VRPatches), nameof(OrbitCameraLateUpdate_Postfix)));
        }

        // ------------------------------------------------------------------
        //  Patch 3 — fix the game's broken intent setter (global)
        //
        //  Shipped SetPlayerIntendedFacingDirection writes quaternion
        //  COMPONENTS into _aim as if they were Euler DEGREES (the euler
        //  variable is computed and never used) — intent collapses to north.
        //  Prefix skips the original and writes real Euler angles. Nothing in
        //  the desktop game calls this API, so desktop is unaffected.
        // ------------------------------------------------------------------

        private static void Patch_OrbitCameraIntentSetter()
        {
            var m = typeof(OrbitCamera).GetMethod("SetPlayerIntendedFacingDirection",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            _harmony.Patch(m,
                prefix: new HarmonyMethod(typeof(VRPatches), nameof(OrbitCameraIntentSetter_Prefix)));
        }

        // ------------------------------------------------------------------
        //  Patch 5 — the VR grabber (Grabber.LateUpdate postfix)
        //
        //  Held objects anchor at GetViewPos() + GetPlayerIntendedRotation() ×
        //  offset. Even with the intent verified correct, held objects must
        //  track the VIEW EXACTLY — so this postfix re-drives every held
        //  object with the snapped camera rotation directly, making holds
        //  deterministic in VR (no sweet spots, no north-lock).
        // ------------------------------------------------------------------

        private static System.Reflection.FieldInfo _grabbedObjectsFi;
        private static System.Reflection.FieldInfo _grabDefaultOffsetFi;
        private static System.Reflection.MethodInfo _grabInfoSetMi;
        private static bool _grabPatchInit;

        private static void Patch_GrabberLateUpdate()
        {
            var m = typeof(Grabber).GetMethod("LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) return;
            _harmony.Patch(m,
                postfix: new HarmonyMethod(typeof(VRPatches), nameof(GrabberLateUpdate_Postfix)));
        }

        private static void GrabberLateUpdate_Postfix(Grabber __instance)
        {
            try
            {
                var mgr = VRKoboldKarePlugin.VRManager;
                if (mgr == null || !mgr.Enabled) return;
                var input = mgr.Input;
                if (input == null || !input.FrameValid || !input.Frame.headTracked) return;

                if (!_grabPatchInit)
                {
                    _grabPatchInit = true;
                    _grabbedObjectsFi = typeof(Grabber).GetField("grabbedObjects",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    _grabDefaultOffsetFi = typeof(Grabber).GetField("defaultOffset",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (_grabbedObjectsFi == null) return;

                var list = _grabbedObjectsFi.GetValue(__instance) as System.Collections.IEnumerable;
                if (list == null) return;
                Vector3 offset = Vector3.forward;
                if (_grabDefaultOffsetFi != null)
                {
                    var o = _grabDefaultOffsetFi.GetValue(__instance);
                    if (o is Vector3 v) offset = v;
                }

                // The view: snapped game camera (already carries the mouse/snap
                // yaw layer and the head-anchored position).
                var cam = OrbitCamera.GetCamera();
                if (cam == null) return;
                Vector3 viewPos = cam.transform.position;
                Quaternion viewRot = cam.transform.rotation;

                foreach (var gi in list)
                {
                    if (gi == null) continue;
                    if (_grabInfoSetMi == null)
                    {
                        _grabInfoSetMi = gi.GetType().GetMethod("Set",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (_grabInfoSetMi == null) return;
                    }
                    // anchor = viewPos + viewRot × offset, then the game's own
                    // Set adds the same offset again — replicate its semantics
                    // (Set uses position + viewRot × offset for the anchor):
                    _grabInfoSetMi.Invoke(gi, new object[] { viewPos, viewRot, offset * 2f });
                }
            }
            catch { }
        }

        private static bool _intentPrefixLogged;

        // STATIC target (no __instance available). While VR is enabled, _aim
        // belongs to SetOrbitAim alone: KK-LLM-NPC's eye animator feeds the
        // local player's direction here every frame via SetEyeDir —
        // body-relative, no mouse layer — and overwriting with it was the
        // "held objects stuck north / mouse can't move them" bug (the aimW
        // forensics caught aim.x = head yaw with the mouse offset stripped).
        // The eyeDir bookkeeping is handled by the SetEyeDir patch below; the
        // desktop path still fixes the shipped bug (quaternion components
        // written as degrees) with real Euler angles.
        private static bool OrbitCameraIntentSetter_Prefix(Vector3 dir)
        {
            try
            {
                if (!_intentPrefixLogged)
                {
                    _intentPrefixLogged = true;
                    if (VRKoboldKarePlugin.Log != null)
                        VRKoboldKarePlugin.Log.LogInfo("VR: intent setter patch intercepting (game bug bypassed)");
                }
                var mgr = VRKoboldKarePlugin.VRManager;
                if (mgr != null && mgr.Enabled) return false;   // VR owns _aim

                if (dir.sqrMagnitude < 0.0001f) return false;
                Vector3 n = dir.normalized;
                if (Mathf.Abs(n.y) > 0.999f) return true;   // near-vertical: skip (rare)
                Quaternion q = Quaternion.LookRotation(n, Vector3.up);
                Vector3 e = q.eulerAngles;
                // Convention (from GetPlayerIntendedRotation = Euler(-aim.y, aim.x, 0)):
                // aim.x = +yaw, aim.y = -pitch. The previous (-yaw, +pitch) wrote
                // the INVERSE rotation — held objects mirrored around the player.
                AimConvention.EulerToAim(e.y, e.x, out float ax, out float ay);
                Vector2 aim = new Vector2(ax, ay);
                var instFi = typeof(OrbitCamera).GetField("instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var inst = instFi != null ? instFi.GetValue(null) : null;
                if (inst == null || (inst is UnityEngine.Object uo && uo == null)) return false;
                var aimFi = typeof(OrbitCamera).GetField("_aim",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                aimFi?.SetValue(inst, aim);
                return false;   // skip the buggy original
            }
            catch
            {
                return true;    // on any surprise, let the original run
            }
        }

        // ------------------------------------------------------------------
        //  Patch — CharacterDescriptor.SetEyeDir (instance method).
        //  The eye animator feeds the LOCAL PLAYER's descriptor every frame;
        //  the original forwards that to the intent setter. While VR is on we
        //  keep eyeDir bookkeeping (NPC vision reads it) but block the intent
        //  write — the animated eye direction must not fight the head-locked
        //  aim. AI kobolds pass through untouched.
        // ------------------------------------------------------------------

        private static System.Reflection.FieldInfo _eyeDirFi;
        private static bool _setEyeDirLogged;

        private static void Patch_SetEyeDir()
        {
            var m = typeof(CharacterDescriptor).GetMethod("SetEyeDir",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            _harmony.Patch(m,
                prefix: new HarmonyMethod(typeof(VRPatches), nameof(SetEyeDir_Prefix)));
        }

        private static bool SetEyeDir_Prefix(CharacterDescriptor __instance, Vector3 dir)
        {
            try
            {
                var mgr = VRKoboldKarePlugin.VRManager;
                if (mgr == null || !mgr.Enabled) return true;   // desktop: original behavior

                var kob = mgr.Kobold;
                bool isLocal = false;
                try { isLocal = kob != null && __instance == kob.GetComponent<CharacterDescriptor>(); } catch (Exception) { }
                if (!isLocal) return true;   // AI kobolds: original behavior

                if (!_setEyeDirLogged)
                {
                    _setEyeDirLogged = true;
                    if (VRKoboldKarePlugin.Log != null)
                        VRKoboldKarePlugin.Log.LogInfo("VR: SetEyeDir intercepted for the local player — intent owned by the headset");
                }

                // Keep eyeDir fresh for the NPC vision systems; block the
                // intent write (the original would call the setter).
                try
                {
                    if (_eyeDirFi == null)
                        _eyeDirFi = typeof(CharacterDescriptor).GetField("eyeDir",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                    _eyeDirFi?.SetValue(__instance, dir);
                }
                catch (Exception) { }
                return false;
            }
            catch
            {
                return true;
            }
        }

        // ------------------------------------------------------------------
        //  Patch — KK-LLM-NPC SpectateManager.WriteCamera stand-down.
        //  The INSTALLED KKLLMNPC 1.12 has no ExternalSpectateCamera flag —
        //  its spectate writer fights the ghost rig every pre-cull (last
        //  writer wins = the eater's-eye view, locked). While ghost mode is
        //  active, skip the writer entirely so the VR rig owns the view.
        //  Graceful: the patch only applies when the NPC mod is loaded, and
        //  v1.13's own flag path is honored in parallel.
        // ------------------------------------------------------------------

        private static void Patch_SpectateWriteCamera()
        {
            var t = Type.GetType("KKLLMNPC.SpectateManager, KKLLMNPC");
            if (t == null)
            {
                if (VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogInfo("VR: KKLLMNPC not loaded — spectate writer stand-down not needed");
                return;
            }
            var m = t.GetMethod("WriteCamera",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null)
            {
                if (VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogWarning("VR: KKLLMNPC WriteCamera not found — ghost mode may fight its camera writer (update KK-LLM-NPC)");
                return;
            }
            _harmony.Patch(m,
                prefix: new HarmonyMethod(typeof(VRPatches), nameof(SpectateWriteCamera_Prefix)));
            if (VRKoboldKarePlugin.Log != null)
                VRKoboldKarePlugin.Log.LogInfo("VR: spectate writer stand-down armed (ghost mode owns the camera)");
        }

        private static bool SpectateWriteCamera_Prefix(Camera cam)
        {
            var mgr = VRKoboldKarePlugin.VRManager;
            return !(mgr != null && mgr.GhostModeActive);   // skip the writer while ghosting
        }

        // ------------------------------------------------------------------
        //  Patch 4 — gaze-priority interactions (User.SortGrabbables postfix)
        //
        //  The game picks the CLOSEST GenericUsable to the avatar body.
        //  In VR, pick the one nearest the VIEW CENTER instead (within a 35°
        //  cone of camera forward) — no more hunting sweet spots.
        // ------------------------------------------------------------------

        private static void Patch_UserSortGrabbables()
        {
            var m = typeof(User).GetMethod("SortGrabbables",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) return;
            _harmony.Patch(m,
                postfix: new HarmonyMethod(typeof(VRPatches), nameof(UserSortGrabbables_Postfix)));
        }

        private static void UserSortGrabbables_Postfix(User __instance)
        {
            try
            {
                var mgr = VRKoboldKarePlugin.VRManager;
                if (mgr == null || !mgr.Enabled) return;
                var input = mgr.Input;
                if (input == null || !input.FrameValid || !input.Frame.headTracked) return;

                var piFi = typeof(User).GetField("possibleUsables",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var cuFi = typeof(User).GetField("closestUsable",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var kobFi = typeof(User).GetField("kobold",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (piFi == null || cuFi == null) return;
                var set = piFi.GetValue(__instance) as System.Collections.Generic.IEnumerable<System.Tuple<GenericUsable, GameObject>>;
                if (set == null) return;
                var kob = kobFi != null ? kobFi.GetValue(__instance) as Kobold : null;

                var cam = Camera.main;
                if (cam == null) return;
                Vector3 fwd = Quaternion.Euler(0f, mgr.YawOffset, 0f) * input.Frame.headRot * Vector3.forward;
                Vector3 origin = cam.transform.position;

                GenericUsable best = null;
                const float d2r = 0.0174533f;
                float bestDot = Mathf.Cos(35f * d2r);   // 35° cone
                foreach (var t in set)
                {
                    var g = t?.Item1;
                    if (g == null) continue;
                    if (kob != null && !g.CanUse(kob)) continue;
                    Vector3 to = g.transform.position - origin;
                    float len = to.magnitude;
                    if (len < 0.01f) continue;
                    float dot = Vector3.Dot(to / len, fwd);
                    if (dot > bestDot) { bestDot = dot; best = g; }
                }
                if (best != null) cuFi.SetValue(__instance, best);
            }
            catch { }
        }

        private static void OrbitCameraLateUpdate_Postfix(object __instance)
        {            var mgr = VRKoboldKarePlugin.VRManager;
            if (mgr == null || !mgr.Enabled) return;
            if (mgr.Cam == null || !mgr.Cam.MirrorEnabled) return;
            var input = mgr.Input;
            if (input == null || !input.FrameValid) return;

            var frame = input.Frame;
            if (!frame.headTracked) return;

            Camera cam = null;
            try { cam = OrbitCamera.GetCamera(); } catch { }
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            try
            {
                cam.transform.position = mgr.HeadToWorld(frame.headPos, frame.headRot);
                cam.transform.rotation = Quaternion.Euler(0f, mgr.YawOffset, 0f) * frame.headRot;
            }
            catch { }
        }
    }
}