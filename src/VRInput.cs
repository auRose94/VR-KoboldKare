// VR-KoboldKare — VR input mapping
// Polls the VR bridge (VRXR) and drives the game:
//   head  -> OrbitCamera aim (SetPlayerIntendedFacingDirection)
//   stick -> movement (via VRLocomotion / PlayerProcessing patch)
//   buttons -> PlayerPossession state fields + Grabber/PrecisionGrabber/User APIs
//
using System;
using UnityEngine;

namespace VRKoboldKare
{
    public class VRInput : MonoBehaviour
    {
        private VRManager _m;
        private XrFrame _f;
        private bool _fValid = false;

        // previous-frame buttons for edge detection
        private bool _rGrip, _rTrigger, _rA, _rB, _rX, _rY, _rMenu;
        private bool _lGrip, _lTrigger, _lA, _lB, _lX, _lY, _lMenu;
        private bool _crouchHeld = false;
        private float _lastAimYaw, _lastAimPitch;
        private int _aimProbeFrames = -1;

        internal XrFrame Frame { get { return _f; } }
        internal bool FrameValid { get { return _fValid; } }

        internal void Initialize(VRManager m)
        {
            _m = m;
        }

        private void Update()
        {
            if (_m == null || !_m.Enabled) return;

            _f = VRXR.Poll();
            _fValid = _f.valid && (_f.headTracked || _f.left.tracked || _f.right.tracked);

            if (!_fValid)
            {
                _rGrip = _rTrigger = _rA = _rB = _rX = _rY = _rMenu = false;
                _lGrip = _lTrigger = _lA = _lB = _lX = _lY = _lMenu = false;
                return;
            }

            _m.UpdateHands(_f);

            // ---- INPUT AUTHORITY ----
            // Controllers take over the moment they generate anything; the
            // moment the keyboard or mouse sends anything, the desktop takes
            // over. Detected BEFORE the desktop-mouse synthesis so a desktop
            // click never counts as controller grip.
            try
            {
                bool ctrlActivity =
                    _f.left.trigger > 0.1f || _f.left.grip > 0.1f ||
                    _f.right.trigger > 0.1f || _f.right.grip > 0.1f ||
                    _f.left.stick.sqrMagnitude > 0.01f || _f.right.stick.sqrMagnitude > 0.01f ||
                    _f.left.a || _f.left.b || _f.left.x || _f.left.y ||
                    _f.right.a || _f.right.b || _f.right.x || _f.right.y;
                if (ctrlActivity) _m.SetControllerAuthority(true);

                bool desktopActivity = false;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && Application.isFocused)
                {
                    if (mouse.delta.ReadValue().sqrMagnitude > 4f) desktopActivity = true;
                    if (mouse.leftButton.isPressed || mouse.rightButton.isPressed) desktopActivity = true;
                    if (mouse.scroll.ReadValue().y != 0) desktopActivity = true;
                }
                if (VRKoboldKarePlugin.KeyDownAny(KeyCode.W) || VRKoboldKarePlugin.KeyDownAny(KeyCode.A) || VRKoboldKarePlugin.KeyDownAny(KeyCode.S) ||
                    VRKoboldKarePlugin.KeyDownAny(KeyCode.D) || VRKoboldKarePlugin.KeyDownAny(KeyCode.E) || VRKoboldKarePlugin.KeyDownAny(KeyCode.Space)) desktopActivity = true;
                if (desktopActivity && _m.ControllerAuthority) _m.SetControllerAuthority(false);
            }
            catch (Exception) { }

            // Desktop fallback input, matching the game's own desktop scheme:
            // LMB = grab (grip), RMB = throw/use (trigger). Scroll = push-pull
            // while grabbing.
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && Application.isFocused && !_m.ControllerAuthority)
                {
                    if (mouse.leftButton.isPressed) _f.right.grip = 1f;
                    if (mouse.rightButton.isPressed) _f.right.trigger = 1f;
                }
            }
            catch { }

            // Desktop fallbacks: mouse yaw + Q/E snap turn (seated play).
            // Only while the game window is focused — typing in a terminal
            // must not spin the view.
            try
            {
                if (Application.isFocused)
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse != null)
                    {
                        var d = mouse.delta.ReadValue();
                        float sens = 0.15f;
                        if (!ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgMouseSens != null)
                            sens = _m.Plugin.cfgMouseSens.Value;
                        _m.AddYaw(d.x * sens);
                    }
                    float snap = 45f;
                    if (!ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgLocoSnapAngle != null)
                        snap = _m.Plugin.cfgLocoSnapAngle.Value;
                    // Configurable snap keys (default [ ]) — must not collide
                    // with game bindings like E (interact).
                    KeyCode kl = KeyCode.LeftBracket, kr = KeyCode.RightBracket;
                    try
                    {
                        if (!ReferenceEquals(_m.Plugin, null))
                        {
                            if (_m.Plugin.cfgSnapLeft != null)
                                System.Enum.TryParse(_m.Plugin.cfgSnapLeft.Value, true, out kl);
                            if (_m.Plugin.cfgSnapRight != null)
                                System.Enum.TryParse(_m.Plugin.cfgSnapRight.Value, true, out kr);
                        }
                    }
                    catch { }
                    if (!_m.ControllerAuthority)
                    {
                        if (VRKoboldKarePlugin.KeyDownAny(kl)) _m.AddYaw(-snap);
                        if (VRKoboldKarePlugin.KeyDownAny(kr)) _m.AddYaw(snap);
                    }
                }
            }
            catch { }

            // Mouse wheel = precision push-pull / rotate while grabbing.
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null && Application.isFocused)
                {
                    if (mouse.leftButton.wasPressedThisFrame) _aimProbeFrames = 0;   // arm the per-frame aim probe on each grab
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.1f && _m.Grab != null)
                        _m.Grab.StickInput(new Vector2(0f, scroll * 0.004f));
                }
            }
            catch { }

            // Aim diagnostics: verify the intent write lands. Held items read
            // GetPlayerIntendedRotation for their anchor — "items stuck north"
            // means this write is being lost somewhere.
            try
            {
                if (Time.frameCount % 120 == 0 && VRKoboldKarePlugin.Log != null)
                {
                    var aimFi = typeof(OrbitCamera).GetField("_aim",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var instFi = typeof(OrbitCamera).GetField("instance",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var inst = instFi != null ? instFi.GetValue(null) : null;
                    object aim = aimFi != null && inst != null ? aimFi.GetValue(inst) : default(Vector2);
                    var rot = OrbitCamera.GetPlayerIntendedRotation();
                    VRKoboldKarePlugin.Log.LogInfo(string.Format(
                        "VR: aim probe: wrote=(yaw {0:0} pitch {1:0}) read={2} intentEuler={3}",
                        _lastAimYaw, _lastAimPitch, aim, rot.eulerAngles));
                }
            }
            catch { }

            // Ghost mode (eaten / spectating): hands keep updating, gameplay
            // input is dead — the ghost drifts via VRManager.GhostFly.
            if (_m.GhostModeActive)
            {
                _rGrip = _rTrigger = _rA = _rB = _rX = _rY = _rMenu = false;
                _lGrip = _lTrigger = _lA = _lB = _lX = _lY = _lMenu = false;
                return;
            }

            DriveHeadAim();
            DriveMovement();
            DriveButtons();
        }
        /// <summary>
        /// Feed the head forward into the game's aim system. OrbitCamera uses
        /// _aim (yaw/pitch) for camera rotation, movement direction, and grab aim.
        /// </summary>
        private void DriveHeadAim()
        {
            if (!_f.headTracked) return;
            // Aim/body follow the head PLUS the mouse/snap yaw offset layer.
            // The game's own SetPlayerIntendedFacingDirection is buggy (writes
            // quaternion components as degrees — intent collapses to north),
            // so we drive OrbitCamera._aim directly.
            Quaternion viewRot = Quaternion.Euler(0f, _m.YawOffset, 0f) * _f.headRot;
            float yaw = viewRot.eulerAngles.y;
            float pitch = viewRot.eulerAngles.x;
            if (pitch > 180f) pitch -= 360f;   // Euler x: -180..180
            _m.SetOrbitAim(yaw, pitch);
            // Belt & suspenders: the Harmony-fixed public setter writes the
            // same value through a second path (and logs its interception).
            try { OrbitCamera.SetPlayerIntendedFacingDirection(viewRot * Vector3.forward); } catch { }
            _lastAimYaw = yaw; _lastAimPitch = pitch;

            // Per-frame aim forensics (armed by each LMB grab, 300 frames):
            // catches whatever overwrites the intent between our write and
            // the grabber's read.
            if (_aimProbeFrames >= 0 && _aimProbeFrames < 300)
            {
                _aimProbeFrames++;
                try
                {
                    var aimFi = typeof(OrbitCamera).GetField("_aim",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var instFi = typeof(OrbitCamera).GetField("instance",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var inst = instFi != null ? instFi.GetValue(null) : null;
                    var aim = aimFi != null && inst != null ? aimFi.GetValue(inst) : default(Vector2);
                    float mdx = 0f;
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse != null) mdx = mouse.delta.ReadValue().x;
                    VRKoboldKarePlugin.Log.LogInfo(string.Format(
                        "aimW f{0}: wrote(y{1:0.0} p{2:0.0}) read={3} mdx={4:0.0} yoff={5:0.0}",
                        _aimProbeFrames, yaw, pitch, aim, mdx, _m.YawOffset));
                }
                catch { }
            }
            else _aimProbeFrames = -1;

            Vector3 fwd = viewRot * Vector3.forward;
            fwd.y = 0f;

            // VR expectation: the avatar's body faces where the head points.
            // The game turns the body toward the aim gradually — far too slow
            // in VR, which reads as "camera not aligned with avatar". Fast
            // follow, snapping on large turns.
            if (_m.Controller != null && _m.Controller.body != null)
            {
                const float rad2deg = 57.29578f;
                float headYaw = Mathf.Atan2(fwd.x, fwd.z) * rad2deg;
                var body = _m.Controller.body;
                float cur = body.rotation.eulerAngles.y;
                float delta = Mathf.DeltaAngle(cur, headYaw);
                float t = Mathf.Abs(delta) > 60f ? 1f : Mathf.Clamp01(Time.deltaTime * 15f);
                body.rotation = Quaternion.Euler(0f, cur + delta * t, 0f);
                try { body.angularVelocity = Vector3.zero; } catch { }
                if (_m.Kobold != null && _m.Kobold.body != null && _m.Kobold.body != body)
                    _m.Kobold.body.rotation = body.rotation;
            }
        }

        /// <summary>
        /// Left stick -> movement. The actual inputDir write happens in the
        /// PlayerProcessing Harmony patch (VRLocomotion), so the game's own
        /// code path (acceleration, crouch speed, etc.) still applies.
        /// </summary>
        private void DriveMovement()
        {
            if (_m.Loco == null) return;

            // Stick (controllers) or WASD (keyboard fallback) — both expressed
            // in the CAMERA basis (view yaw offset + head, flattened to XZ):
            // forward = where you look, exactly.
            Vector2 move = _f.left.stick;
            try
            {
                // Keyboard movement counts only under DESKTOP authority — with
                // controllers in charge the stick owns locomotion.
                if (!_m.ControllerAuthority)
                {
                    Vector2 kb = Vector2.zero;
                    if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.W)) kb.y += 1f;
                    if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.S)) kb.y -= 1f;
                    if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.D)) kb.x += 1f;
                    if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.A)) kb.x -= 1f;
                    if (kb.sqrMagnitude > 0.01f) move = kb;
                }
            }
            catch (Exception) { }
            _m.Loco.SetMoveInput(move, Quaternion.Euler(0f, _m.YawOffset, 0f) * _f.headRot);
        }

        private bool Edge(bool now, bool prev) { return now && !prev; }

        private void DriveButtons()
        {
            bool rGrip = _f.right.grip > 0.5f;
            bool rTrig = _f.right.trigger > 0.5f;
            bool rA = _f.right.a;
            bool rB = _f.right.b;
            bool rX = _f.right.x;
            bool rY = _f.right.y;
            bool rMenu = _f.right.menu || _f.left.menu;

            bool lTrig = _f.left.trigger > 0.5f;
            bool lA = _f.left.a;
            bool lB = _f.left.b;
            bool lX = _f.left.x;
            bool lMenu = _f.left.menu || _f.right.menu;

            // ---- grab (right grip) ----
            if (Edge(rGrip, _rGrip) && _m.Grab != null) _m.Grab.GrabPress(rGrip);
            if (!rGrip && _rGrip && _m.Grab != null) _m.Grab.GrabRelease();

            // ---- activate / use (right trigger) ----
            if (Edge(rTrig, _rTrigger) && _m.Grab != null)
            {
                _m.Grab.TriggerPress(rGrip);
            }
            if (!rTrig && _rTrigger && _m.Grab != null)
            {
                _m.Grab.TriggerRelease();
            }

            // ---- jump (right A) ----
            if (Edge(rA, _rA))
            {
                DoJump();
            }

            // ---- crouch (right B, hold) ----
            if (rB != _rB)
            {
                SetCrouch(rB);
            }

            // ---- unfreeze / unfreeze-all (right X / right Y) ----
            if (Edge(rX, _rX) && _m.PGrabber != null)
            {
                try { _m.PGrabber.TryUnfreeze(); } catch { }
                VRXR.Haptic(true, 0.3f, 60);
            }
            if (Edge(rY, _rY) && _m.PGrabber != null)
            {
                try { _m.PGrabber.UnfreezeAll(); } catch { }
                VRXR.Haptic(true, 0.3f, 60);
            }

            // ---- camera switching (left A/B/X) ----
            if (Edge(lA, _lA) && _m.Cam != null) _m.Cam.SwitchTo(CameraSwitcher.CameraMode.FirstPerson);
            if (Edge(lB, _lB) && _m.Cam != null) _m.Cam.SwitchTo(CameraSwitcher.CameraMode.ThirdPerson);
            if (Edge(lX, _lX) && _m.Cam != null) _m.Cam.SwitchTo(CameraSwitcher.CameraMode.FreeCam);

            // ---- teleport (left trigger) ----
            if (Edge(lTrig, _lTrigger) && _m.Loco != null)
            {
                _m.Loco.RequestTeleport(_f);
            }

            // ---- snap turn (left stick x hard, edge) ----
            if (_m.Loco != null)
            {
                _m.Loco.UpdateSnapTurn(_f);
            }

            // ---- menu combos (system button + face button) ----
            bool menu = lMenu;
            if (menu)
            {
                // menu + A : ragdoll
                if (Edge(_f.right.a, _rA) || Edge(_f.left.a, _lA))
                {
                    DoRagdoll();
                }
                // menu + B : precision grab mode toggle
                if (Edge(_f.right.b, _rB) || Edge(_f.left.b, _lB))
                {
                    TogglePrecisionMode();
                }
                // menu + Y : equipment radial
                if (Edge(_f.right.y, _rY) || Edge(_f.left.y, _lY))
                {
                    if (_m.UI != null) _m.UI.ToggleRadial();
                }
                // menu + X : chat (stub)
                if (Edge(_f.right.x, _rX) || Edge(_f.left.x, _lX))
                {
                    if (!ReferenceEquals(_m.Plugin, null))
                    {
                        _m.Plugin.Logger.LogInfo("VR: chat requested (not implemented).");
                    }
                }
            }

            // ---- rotate / push-pull grabbed object (right stick) ----
            if (_f.right.stick.sqrMagnitude > 0.01f && _m.Grab != null)
            {
                _m.Grab.StickInput(_f.right.stick);
            }

            // store prev
            _rGrip = rGrip; _rTrigger = rTrig; _rA = rA; _rB = rB;
            _rX = rX; _rY = rY; _rMenu = rMenu;
            _lGrip = _f.left.grip > 0.5f; _lTrigger = lTrig; _lA = lA; _lB = lB; _lX = lX; _lMenu = menu;
        }

        private void DoJump()
        {
            if (_m.Controller != null)
            {
                try { _m.Controller.inputJump = true; } catch { }
                VRXR.Haptic(false, 0.4f, 80);
            }
        }

        private void SetCrouch(bool crouched)
        {
            if (_m.Controller != null)
            {
                try { _m.Controller.SetInputCrouched(crouched ? 1f : 0f); }
                catch { }
            }
        }

        private void DoRagdoll()
        {
            if (_m.Kobold == null) return;
            var r = _m.Kobold.GetRagdoller();
            if (r == null) return;
            try
            {
                if (r.ragdolled)
                {
                    r.PopRagdoll();
                }
                else
                {
                    r.PushRagdoll();
                }
                VRXR.Haptic(true, 0.6f, 150);
            }
            catch { }
        }

        private void TogglePrecisionMode()
        {
            bool cur = VRManager.GetBoolField(_m.Possession, VRManager.GetFieldInfo("switchedMode"));
            bool next = !cur;
            VRManager.SetBoolField(VRManager.GetFieldInfo("switchedMode"), next);
            try
            {
                PrecisionGrabber.SetPinVisibility(next);
                if (_m.PGrabber != null) _m.PGrabber.SetPreviewState(next);
            }
            catch { }
            if (next)
            {
                if (_m.Possession != null)
                {
                    var f = VRManager.GetFieldInfo("multiGrabSwitchTimer");
                    var def = VRManager.GetFieldInfo("defaultMultiGrabSwitchFrames");
                    if (f != null && def != null)
                    {
                        try { f.SetValue(_m.Possession, def.GetValue(_m.Possession)); } catch { }
                    }
                }
            }
            if (!ReferenceEquals(_m.Plugin, null))
            {
                _m.Plugin.Logger.LogInfo("VR: precision grab mode " + (next ? "ON" : "OFF"));
            }
            VRXR.Haptic(true, 0.5f, 100);
        }

        internal void ResetGrabState()
        {
            SetCrouch(false);
            _rGrip = _rTrigger = _rA = _rB = _rX = _rY = _rMenu = false;
            _lGrip = _lTrigger = _lA = _lB = _lX = _lY = _lMenu = false;
        }
    }
}