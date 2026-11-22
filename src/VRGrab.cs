// VR-KoboldKare — VR grab / activate / rotate
// Mirrors the game's own input callbacks (OnGrabInput, OnGrabCancelled,
// OnActivateGrabInput, OnActivateGrabCancelled, OnGrabPushPull) using the
// public Grabber / PrecisionGrabber API.
//
using System;
using UnityEngine;

namespace VRKoboldKare
{
    public class VRGrab : MonoBehaviour
    {
        private VRManager _m;

        private GameObject _grabViewObj;
        private Transform _grabView;
        private GameObject _precViewObj;
        private Transform _precView;
        private Transform _origGrabView;   // the game's own views, restored on VR disable
        private Transform _origPrecView;

        private bool _activating = false;
        private bool _grabbing = false;

        internal void Initialize(VRManager m)
        {
            _m = m;
            SetupViews();
            if (!ReferenceEquals(m.Plugin, null))
            {
                m.Plugin.Logger.LogInfo("VR: grab subsystem ready (grabber view + precision view set).");
            }
        }

        private void SetupViews()
        {
            if (_m.Grabber != null)
            {
                _origGrabView = VRManager.GetGrabberView(_m.Grabber);
                _grabViewObj = new GameObject("VRKK_GrabView");
                _grabView = _grabViewObj.transform;
                _grabViewObj.isStatic = false;
                try
                {
                    _m.Grabber.SetView(_grabView);
                }
                catch (Exception e)
                {
                    if (!ReferenceEquals(_m.Plugin, null)) _m.Plugin.Logger.LogWarning("VR: Grabber.SetView failed: " + e.Message);
                }
            }
            if (_m.PGrabber != null)
            {
                _origPrecView = VRManager.GetGrabberView(_m.PGrabber);
                _precViewObj = new GameObject("VRKK_PrecGrabView");
                _precView = _precViewObj.transform;
                try
                {
                    _m.PGrabber.SetView(_precView);
                }
                catch (Exception e)
                {
                    if (!ReferenceEquals(_m.Plugin, null)) _m.Plugin.Logger.LogWarning("VR: PrecisionGrabber.SetView failed: " + e.Message);
                }
            }
        }

        private void Update()
        {
            if (_m == null || !_m.Enabled) return;

            var f = _m.Input.Frame;
            if (!_m.Input.FrameValid) return;

            if (_grabView != null && _m.RightHand != null)
            {
                _grabView.position = _m.RightHand.position;
                _grabView.rotation = _m.RightHand.rotation;
            }
            if (_precView != null && _m.LeftHand != null)
            {
                _precView.position = _m.LeftHand.position;
                _precView.rotation = _m.LeftHand.rotation;
            }

            UpdateHandIK(f);
        }
        // ================= grab (right grip) =================

        /// <summary>Called on right-grip press edge.</summary>
        internal void GrabPress(bool gripHeld)
        {
            if (_m.Possession == null) return;
            _grabbing = true;

            // Mirror PlayerPossession.OnGrabInput
            try { if (_m.Animator != null) _m.Animator.inputGrabbing = true; } catch { }
            VRManager.SetBoolField(VRManager.GetFieldInfo("grabbing"), true);

            bool switched = VRManager.GetBoolField(_m.Possession, VRManager.GetFieldInfo("switchedMode"));
            if (switched && _m.PGrabber != null)
            {
                try { _m.PGrabber.TryGrab(); VRXR.Haptic(false, 0.5f, 80); }
                catch { }
            }
            else if (_m.Grabber != null)
            {
                // The game auto-calls Grabber.TryGrab while 'grabbing' is set
                // (PlayerProcessing). Nudge it once here as well.
                try
                {
                    bool multi = _m.Possession.multiGrabMode;
                    _m.Grabber.TryGrab(multi);
                    VRXR.Haptic(false, 0.5f, 80);
                }
                catch { }
            }
        }

        /// <summary>Called on right-grip release edge.</summary>
        internal void GrabRelease()
        {
            if (_m.Possession == null) return;
            _grabbing = false;

            // Mirror PlayerPossession.OnGrabCancelled
            try { if (_m.Animator != null) _m.Animator.inputGrabbing = false; } catch { }
            VRManager.SetBoolField(VRManager.GetFieldInfo("grabbing"), false);
            if (_m.Grabber != null) { try { _m.Grabber.TryDrop(); } catch { } }
            if (_m.PGrabber != null) { try { _m.PGrabber.TryDrop(); } catch { } }
            VRXR.Haptic(false, 0.25f, 50);
        }

        // ================= activate / use (right trigger) =================

        /// <summary>Called on right-trigger press edge.</summary>
        internal void TriggerPress(bool whileGrabbing)
        {
            if (_m.Possession == null) return;

            if (whileGrabbing)
            {
                // Mirror PlayerPossession.OnActivateGrabInput
                _activating = true;
                if (_m.Grabber != null) { try { _m.Grabber.TryActivate(); } catch { } }
                if (_m.PGrabber != null) { try { _m.PGrabber.TryFreeze(); } catch { } }
                try { if (_m.Animator != null) _m.Animator.inputActivate = true; } catch { }
                VRXR.Haptic(false, 0.7f, 120);
            }
            else
            {
                // Mirror PlayerPossession.OnUseInput -> User.Use()
                if (_m.GameUser != null)
                {
                    try
                    {
                        _m.GameUser.Use();
                        VRXR.Haptic(false, 0.4f, 80);
                    }
                    catch { }
                }
            }
        }

        /// <summary>Called on right-trigger release edge.</summary>
        internal void TriggerRelease()
        {
            if (!_activating) return;
            _activating = false;

            // Mirror PlayerPossession.OnActivateGrabCancelled
            if (_m.Grabber != null) { try { _m.Grabber.TryStopActivate(); } catch { } }
            try { if (_m.Animator != null) _m.Animator.inputActivate = false; } catch { }
        }

        // ================= rotate / push-pull (right stick) =================

        internal void StickInput(Vector2 stick)
        {
            if (!VRManager.GetBoolField(_m.Possession, VRManager.GetFieldInfo("grabbing"))) return;
            if (_m.PGrabber == null) return;

            float rx = stick.x;
            float ry = stick.y;
            if (Mathf.Abs(rx) > 0.05f)
            {
                try { _m.PGrabber.TryRotate(new Vector2(rx * 0.25f, 0f)); } catch { }
            }
            if (Mathf.Abs(ry) > 0.05f)
            {
                try { _m.PGrabber.TryAdjustDistance(ry * 0.04f); } catch { }
            }
        }

        // ================= hand IK =================

        private void UpdateHandIK(XrFrame f)
        {
            if (_m.HandIK == null) return;
            try
            {
                if (f.left.tracked && _m.LeftHand != null)
                {
                    _m.HandIK.SetIKTarget(0, _m.LeftHand.position, _m.LeftHand.rotation);
                }
                if (f.right.tracked && _m.RightHand != null)
                {
                    _m.HandIK.SetIKTarget(1, _m.RightHand.position, _m.RightHand.rotation);
                }
            }
            catch { }
        }

        // ================= teardown =================

        internal void Reset()
        {
            GrabRelease();
            TriggerRelease();
            // Restore the game's own grabber views — leaving ours in place
            // (destroyed on teardown) broke desktop grabbing after VR disable.
            if (_m.Grabber != null && _origGrabView != null)
                VRManager.SetGrabberViewField(_m.Grabber, _origGrabView);
            if (_m.PGrabber != null && _origPrecView != null)
                VRManager.SetGrabberViewField(_m.PGrabber, _origPrecView);
        }

        private void OnDestroy()
        {
            if (_grabViewObj != null) Destroy(_grabViewObj);
            if (_precViewObj != null) Destroy(_precViewObj);
        }
    }
}