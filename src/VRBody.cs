// VRBody — experimental body-pose → kobold-skeleton mapping.
//
// WHY: without body tracking the avatar's hips stay wherever the KCC put
// them while the camera drifts with physical leans/walks — the dissociation
// she flagged ("full body tracking isn't meshing to the avatar").
//
// DESIGN (pluggable sources, config-gated, default OFF):
//   Procedural source (no hardware): hips follow the camera's horizontal
//     offset from the physics body at a configurable fraction, clamped —
//     physical leans/room-walk visibly move the avatar without moving the
//     physics body (visual-only, post-animation).
//   SlimeVR source (later): tracker poses from the vive-tracker interaction
//     profile (SlimeVR server → WiVRn server → VIVE_TRACKER devices;
//     "use controllers as trackers" in SlimeVR lets a working controller
//     drive the pipeline with zero extra hardware).
//
// Applied POST-ANIMATION (LateUpdate) as small per-frame offsets on the hip
// bone, so the game's animator stays authoritative and the mod only adds the
// tracking delta. Config fractions keep the visual desync from physics honest
// and small.
using System;
using UnityEngine;

namespace VRKoboldKare
{
    public class VRBody : MonoBehaviour
    {
        private VRManager _m;
        private Transform _hips;
        private bool _resolved;

        // Per-frame smoothed offsets (kept tiny by the fractions + clamp).
        private Vector3 _hipOffset;

        internal void Initialize(VRManager m)
        {
            _m = m;
            try
            {
                if (!ReferenceEquals(m.Plugin, null) && m.Plugin.Logger != null)
                    m.Plugin.Logger.LogInfo("VR: body mapping armed (experimental, " +
                        (m.Plugin.cfgBodyFollowXZ != null && m.Plugin.cfgBodyFollowXZ.Value > 0.01f ? "ON" : "OFF — enable via config") + ")");
            }
            catch (Exception) { }
        }

        private bool ResolveBones()
        {
            if (_hips != null) return true;
            try
            {
                var kob = _m.Kobold;
                if (kob == null) return false;
                var desc = kob.GetComponent<CharacterDescriptor>();
                var anim = desc != null ? desc.GetDisplayAnimator() : null;
                _hips = anim != null ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            }
            catch (Exception) { }
            return _hips != null;
        }

        private void LateUpdate()
        {
            try
            {
                if (_m == null || !_m.Enabled || GhostOrOff()) return;
                bool bodyOn = false;
                try
                {
                    var p = _m.Plugin;
                    bodyOn = !ReferenceEquals(p, null) && p.cfgBodyFollowXZ != null && p.cfgBodyFollowXZ.Value > 0.01f;
                }
                catch (Exception) { }
                if (!bodyOn) return;
                if (!ResolveBones()) return;

                float followXZ = 0.7f, followY = 0.4f, maxOff = 1.0f;
                try
                {
                    var p = _m.Plugin;
                    if (!ReferenceEquals(p, null))
                    {
                        if (p.cfgBodyFollowXZ != null) followXZ = Mathf.Clamp01(p.cfgBodyFollowXZ.Value);
                        if (p.cfgBodyFollowY != null) followY = Mathf.Clamp01(p.cfgBodyFollowY.Value);
                        if (p.cfgBodyMaxOffset != null) maxOff = Mathf.Clamp(p.cfgBodyMaxOffset.Value, 0.2f, 3f);
                    }
                }
                catch (Exception) { }

                var body = _m.Controller != null ? _m.Controller.body : null;
                if (body == null && _m.Kobold != null) body = _m.Kobold.body;
                if (body == null) return;

                var cam = Camera.main;
                if (cam == null) return;

                // The horizontal camera offset from the physics body = the
                // player's physical lean/room-walk (playspace position).
                Vector3 camXZ = cam.transform.position;
                camXZ.y = 0f;
                Vector3 bodyXZ = body.position;
                bodyXZ.y = 0f;
                Vector3 drift = camXZ - bodyXZ;

                // Fraction-follow, clamped — visual only, no physics write.
                Vector3 want = new Vector3(drift.x * followXZ, 0f, drift.z * followXZ);
                if (want.magnitude > maxOff) want = want.normalized * maxOff;

                // Height: keep the hips a fixed fraction of the camera height
                // above the floor so physical crouch = kobold crouch.
                float camH = Mathf.Max(0.2f, cam.transform.position.y);
                float wantY = camH * followY;

                _hipOffset = Vector3.Lerp(_hipOffset, want + Vector3.up * wantY,
                    Time.deltaTime * 10f);

                var hipsT = _hips;
                if (hipsT == null) return;
                // Post-animation delta: offset relative to where the animator
                // put the hips THIS frame (recomputed each frame, no drift).
                Vector3 baseWorld = body.position;
                hipsT.position = new Vector3(
                    baseWorld.x + _hipOffset.x,
                    Mathf.Max(body.position.y + 0.05f, baseWorld.y + _hipOffset.y),
                    baseWorld.z + _hipOffset.z);
            }
            catch (Exception) { }
        }

        private bool GhostOrOff()
        {
            try { if (_m.GhostModeActive) return true; } catch (Exception) { }
            return false;
        }

        private void OnDestroy()
        {
            _hips = null;
            _hipOffset = Vector3.zero;
        }
    }
}
