// VRBody — body-pose → kobold-skeleton mapping (the FBT pipeline).
//
// WHY: without body mapping the avatar's hips stay wherever the KCC put them
// while the camera drifts with physical leans/walks — the dissociation she
// flagged ("full body tracking isn't meshing to the avatar").
//
// SOURCES (in priority order):
//   1. XR_FB_body_tracking (WiVRn ← SlimeVR server): hips/spine/chest world
//      poses located in app space at the frame's predicted display time.
//      The DEFAULT joint set is UPPER BODY ONLY (no knees/feet exist in it —
//      verified against openxr.h), so legs stay procedural. World targets
//      come from the same HeadToWorld convention as the hands (stage→world,
//      body-anchored), then are applied POST-ANIMATION as clamped,
//      strength-scaled blends — the game's animator stays authoritative and
//      physics/grabber/Photon sync never see the delta.
//   2. Procedural (no hardware): hips follow the camera's horizontal offset
//      from the physics body at a configurable fraction, feet mirror part of
//      that drift so the kobold leans instead of splitting at the waist.
//
// Config (VR.Body): TrackerStrength scales the tracker blend (0 = trackers
// ignored), BodyFollowXZ/FollowY/MaxOffset keep the procedural mode honest
// and small (shipped 0.3.0 semantics unchanged).
using System;
using UnityEngine;

namespace VRKoboldKare
{
    public class VRBody : MonoBehaviour
    {
        private VRManager _m;
        private Transform _hips;
        private Transform _spine;
        private Transform _chest;
        private Transform _footL;
        private Transform _footR;
        private bool _resolved;

        // Smoothed procedural offsets (kept tiny by the fractions + clamp).
        private Vector3 _hipOffset;

        internal void Initialize(VRManager m)
        {
            _m = m;
            try
            {
                if (!ReferenceEquals(m.Plugin, null) && m.Plugin.Logger != null)
                    m.Plugin.Logger.LogInfo("VR: body mapping armed (FBT source: " +
                        (VRXR.BodyTrackingAvailable ? "XR_FB_body_tracking" : "procedural") + ")");
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
                if (anim == null) return false;
                _hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                _spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                _chest = anim.GetBoneTransform(HumanBodyBones.Chest);
                _footL = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                _footR = anim.GetBoneTransform(HumanBodyBones.RightFoot);
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

                var input = _m.Input;
                var f = input != null && input.FrameValid ? input.Frame : default(XrFrame);
                if (f.body.valid && f.body.hips.tracked) TrackerPose(f);
                else ProceduralPose();
            }
            catch (Exception) { }
        }

        // ---- source 1: XR_FB_body_tracking ----

        private void TrackerPose(XrFrame f)
        {
            float strength = 0.8f;
            float maxOff = 1.0f;
            try
            {
                var p = _m.Plugin;
                if (!ReferenceEquals(p, null))
                {
                    if (p.cfgBodyTrackerStrength != null) strength = Mathf.Clamp01(p.cfgBodyTrackerStrength.Value);
                    if (p.cfgBodyMaxOffset != null) maxOff = Mathf.Clamp(p.cfgBodyMaxOffset.Value, 0.2f, 3f);
                }
            }
            catch (Exception) { }
            if (strength <= 0.01f) return;

            var hips = _hips;
            if (hips == null) return;
            Quaternion headRot = f.headRot;

            // Hips: world target through the body-anchored rig mapping (the
            // same stage→world convention the hands use), clamped so a glitchy
            // tracker frame can teleport the avatar's skeleton away.
            var hipsTarget = _m.HeadToWorld(f.body.hips.position, headRot);
            Vector3 delta = hipsTarget - hips.position;
            if (delta.sqrMagnitude > maxOff * maxOff) delta = delta.normalized * maxOff;
            hips.position += delta * (strength * Mathf.Clamp01(Time.deltaTime * 12f));

            // Rotations: hips fully, spine/chest as blended deltas — the
            // tracker world rotations apply directly (both spaces are world).
            hips.rotation = Quaternion.Slerp(hips.rotation, f.body.hips.rotation, strength * Mathf.Clamp01(Time.deltaTime * 10f));
            BlendBone(_spine, f.body.spineLower.rotation, strength * 0.6f);
            BlendBone(_chest, f.body.chest.tracked ? f.body.chest.rotation : f.body.spineMiddle.rotation, strength * 0.8f);
        }

        private static void BlendBone(Transform bone, Quaternion worldRot, float t)
        {
            if (bone == null || t <= 0f) return;
            bone.rotation = Quaternion.Slerp(bone.rotation, worldRot, t * Mathf.Clamp01(Time.deltaTime * 10f));
        }

        // ---- source 2: procedural (no hardware) ----

        private void ProceduralPose()
        {
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

            // Feet mirror part of the drift so the legs lean with the hips
            // instead of the body splitting at the waist. Half the hip delta,
            // same clamp — visual-only.
            var p2 = _m.Plugin;
            float footFollow = 0.5f;
            try
            {
                if (!ReferenceEquals(p2, null) && p2.cfgBodyFeetFollow != null)
                    footFollow = Mathf.Clamp01(p2.cfgBodyFeetFollow.Value);
            }
            catch (Exception) { }
            if (footFollow > 0.01f)
            {
                Vector3 footDelta = want * footFollow;
                ApplyFoot(_footL, footDelta);
                ApplyFoot(_footR, footDelta);
            }
        }

        private static void ApplyFoot(Transform foot, Vector3 delta)
        {
            if (foot == null) return;
            // The animator rewrites the foot bones every frame, so a constant
            // per-frame delta is the same recompute-from-base contract the
            // hips use — no accumulation, no drift.
            foot.position += delta;
        }

        private bool GhostOrOff()
        {
            try { if (_m.GhostModeActive) return true; } catch (Exception) { }
            return false;
        }

        private void OnDestroy()
        {
            _hips = null;
            _spine = null;
            _chest = null;
            _footL = null;
            _footR = null;
            _hipOffset = Vector3.zero;
        }
    }
}
