// VR-KoboldKare — Locomotion (smooth move + teleport + snap turn)
//
// Smooth move: a Harmony postfix on PlayerProcessing (VRPatches) calls
//   GetMoveDir() and writes the result to KoboldCharacterController.inputDir
//   AFTER the game's own Move handling, so the VR stick wins.
// Teleport: raycast from the head to a walkable ground, then move the body.
// Snap turn: a hard left-stick push rotates the body in discrete steps.
using System;
using UnityEngine;

namespace VRKoboldKare
{
    public class VRLocomotion : MonoBehaviour
    {
        VRManager _m;

        // Latest VR move input (stick already expressed in the head-forward basis).
        Vector3 _moveDir = Vector3.zero;
        bool _moveActive;

        // Snap-turn edge state (one turn per hard push).
        bool _snapArmed;

        // Cached config.
        float _teleportDist = 8f;
        float _snapAngle = 45f;
        const float SNAP_STICK_THRESHOLD = 0.9f;

        internal void Initialize(VRManager m)
        {
            _m = m;
            if (!ReferenceEquals(m.Plugin, null))
            {
                try { if (m.Plugin.cfgLocoMaxTeleportDistance != null) _teleportDist = m.Plugin.cfgLocoMaxTeleportDistance.Value; } catch { }
                try { if (m.Plugin.cfgLocoSnapAngle != null) _snapAngle = m.Plugin.cfgLocoSnapAngle.Value; } catch { }
            }
        }

        // ------------------------------------------------------------------
        //  Smooth move
        // ------------------------------------------------------------------

        internal void SetMoveInput(Vector2 stick, Quaternion headRot)
        {
            if (stick.sqrMagnitude < 0.0025f)
            {
                _moveDir = Vector3.zero;
                _moveActive = false;
                return;
            }

            // Same basis as the game's own math (PlayerProcessing):
            //   aim.forward * move.z + aim.right * move.x
            // stick.y = forward, stick.x = right.
            Vector3 fwd = headRot * Vector3.forward;
            fwd = new Vector3(fwd.x, 0f, fwd.z);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd = fwd.normalized;
            Vector3 right = headRot * Vector3.right;
            right = new Vector3(right.x, 0f, right.z);

            float mag = Mathf.Clamp01(stick.magnitude);
            Vector3 dir = fwd * stick.y + right * stick.x;
            if (dir.sqrMagnitude > 0.0001f)
                dir = dir.normalized * mag;

            _moveDir = dir;
            _moveActive = true;
        }

        /// <summary>
        /// Called by the PlayerProcessing postfix (VRPatches) after the game's
        /// own Move handling. Returns the VR move direction (magnitude 0..1)
        /// or zero when no VR move is active.
        /// </summary>
        internal Vector3 GetMoveDir()
        {
            return _moveActive ? _moveDir : Vector3.zero;
        }

        /// <summary>True while the VR stick is actively driving movement.</summary>
        internal bool MoveActive { get { return _moveActive; } }
        // ------------------------------------------------------------------
        //  Teleport
        // ------------------------------------------------------------------

        internal void RequestTeleport(XrFrame f)
        {
            if (_m == null) return;
            if (!f.valid || !f.headTracked) return;
            if (_m.Controller == null || _m.Controller.body == null) return;

            Vector3 origin = f.headPos;
            Vector3 dir = f.headRot * Vector3.forward;

            Vector3 target = FindTeleportTarget(origin, dir);
            if (target == default(Vector3))
            {
                return; // no valid landing spot
            }

            Quaternion face = _m.Controller.body.rotation;
            MoveTo(target, face);

            if (VRKoboldKarePlugin.Log != null)
                VRKoboldKarePlugin.Log.LogInfo("VR: teleported to " + target.ToString());
        }

        Vector3 FindTeleportTarget(Vector3 origin, Vector3 dir)
        {
            LayerMask mask = GetGroundMask();

            RaycastHit hit;
            if (Physics.Raycast(origin, dir, out hit, _teleportDist, mask))
            {
                return hit.point + Vector3.up * 0.05f;
            }

            // Fallback: any surface in range.
            if (Physics.Raycast(origin, dir, out hit, _teleportDist))
                return hit.point + Vector3.up * 0.05f;

            return default(Vector3);
        }

        LayerMask GetGroundMask()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm != null)
                {
                    LayerMask wm = gm.walkableGroundMask;
                    if (wm != 0) return wm;
                }
            }
            catch { }
            // Reasonable default: default + terrain + common ground layers.
            return (1 << 0) | (1 << 2) | (1 << 8);
        }

        void MoveTo(Vector3 target, Quaternion face)
        {
            var body = _m.Controller.body;
            if (body != null)
            {
                body.position = target;
                body.rotation = face;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            var koboldBody = _m.Kobold != null ? _m.Kobold.body : null;
            if (koboldBody != null && body != null && !ReferenceEquals(koboldBody, body))
                koboldBody.position = target;

            // Re-sync the ragdoll (if any) to the new position.
            // FixPlayerPosition is private, so invoke via reflection.
            if (_m.Kobold != null)
            {
                try
                {
                    var rag = _m.Kobold.GetRagdoller();
                    if (rag != null)
                    {
                        var fm = rag.GetType().GetMethod("FixPlayerPosition",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (fm != null) fm.Invoke(rag, null);
                    }
                }
                catch { }
            }
        }
        // ------------------------------------------------------------------
        //  Snap turn (hard left-stick push, edge-triggered)
        // ------------------------------------------------------------------

        internal void UpdateSnapTurn(XrFrame f)
        {
            if (_m == null) return;
            if (_m.Controller == null || _m.Controller.body == null) return;
            if (!f.valid || !f.left.tracked) return;

            float x = f.left.stick.x;
            bool hard = Mathf.Abs(x) >= SNAP_STICK_THRESHOLD;

            if (hard && !_snapArmed)
            {
                // Stick right = turn right (negative Y in Unity's left-handed space).
                float angle = (x > 0 ? -1f : 1f) * _snapAngle;
                _m.Controller.body.rotation *= Quaternion.Euler(0f, angle, 0f);

                if (_m.Kobold != null && _m.Kobold.body != null)
                    _m.Kobold.body.rotation = _m.Controller.body.rotation;

                if (VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogInfo("VR: snap turn " + angle + " deg");
            }

            _snapArmed = hard;
        }

        void OnDestroy()
        {
            _moveDir = Vector3.zero;
            _moveActive = false;
            _snapArmed = false;
        }
    }
}