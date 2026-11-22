// VR-KoboldKare — VR Camera System
// Drives head aim (OrbitCamera.SetPlayerIntendedFacingDirection) every frame,
// handles camera-mode switching (first/third/free), and exposes MirrorEnabled
// (read by the OrbitCamera.LateUpdate patch) so first-person snaps the camera
// to the HMD for true head tracking.
using System;
using UnityEngine;

namespace VRKoboldKare
{
    public class VRCamera : MonoBehaviour
    {
        private VRManager _manager;
        private CameraSwitcher _switcher;
        private Camera _gameCamera;
        private CameraSwitcher.CameraMode _mode = CameraSwitcher.CameraMode.FirstPerson;

        /// <summary>
        /// When true (VR enabled + first-person), the OrbitCamera.LateUpdate
        /// patch snaps the game camera to the HMD pose.
        /// </summary>
        internal bool MirrorEnabled { get; set; }

        internal void Initialize(VRManager manager)
        {
            _manager = manager;
            _switcher = manager.CamSwitcher;
            _gameCamera = Camera.main;
            RefreshMode();
            if (!ReferenceEquals(manager.Plugin, null) && manager.Plugin.Logger != null)
                manager.Plugin.Logger.LogInfo("VR: camera subsystem initialized.");
        }

        private void RefreshMode()
        {
            bool found = false;
            try
            {
                if (_switcher != null)
                {
                    var m = _switcher.GetCameraMode();
                    if (m.HasValue)
                    {
                        _mode = m.Value;
                        found = true;
                    }
                }
            }
            catch { }
            // Unknown mode -> default to first-person (true head tracking).
            MirrorEnabled = found ? (_mode == CameraSwitcher.CameraMode.FirstPerson) : true;
        }

        private void Update()
        {
            if (_manager == null || !_manager.Enabled) return;

            // Ghost mode: the spectate hook owns the game camera — stop
            // mirroring and stop feeding head aim; the VR rig drifts freely.
            if (_manager.GhostModeActive)
            {
                MirrorEnabled = false;
                return;
            }

            // Keep the mode in sync with the game (it may change for other reasons).
            RefreshMode();

            // Head aim: drive the orbit camera yaw/pitch to match the head.
            // This also steers movement direction and grab aim (both read the aim).
            var input = _manager.Input;
            if (input != null && input.FrameValid && input.Frame.headTracked)
            {
                Vector3 fwd = input.Frame.headRot * Vector3.forward;
                if (fwd.sqrMagnitude > 0.001f)
                {
                    try { OrbitCamera.SetPlayerIntendedFacingDirection(fwd.normalized); } catch { }
                }
            }

            UpdateFOV();
        }

        /// <summary>Switch the game camera mode. Called by VRInput (left A/B/X).</summary>
        public void SwitchTo(CameraSwitcher.CameraMode mode)
        {
            try
            {
                if (_switcher != null)
                {
                    _switcher.SwitchCamera(mode);
                    _mode = mode;
                    MirrorEnabled = (mode == CameraSwitcher.CameraMode.FirstPerson);
                }
            }
            catch { }
        }

        private void UpdateFOV()
        {
            if (_gameCamera == null || _manager == null || ReferenceEquals(_manager.Plugin, null)) return;
            try
            {
                float baseFOV = _manager.Plugin.cfgCameraFOV.Value;
                float scale = _manager.Plugin.cfgCameraFOVScale.Value;
                float target = baseFOV * scale;
                _gameCamera.fieldOfView = Mathf.Lerp(_gameCamera.fieldOfView, target, Time.deltaTime * 5f);
            }
            catch { }
        }

        void OnDestroy()
        {
            MirrorEnabled = false;
        }
    }
}