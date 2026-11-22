// VR-KoboldKare — Central VR state machine
// Owns cached game references (via PlayerPossession) and the subsystems.
//
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRKoboldKare
{
    public class VRManager : MonoBehaviour
    {
        public bool Enabled { get; private set; }
        internal VRKoboldKarePlugin Plugin { get; private set; }

        // ---- cached game references ----
        internal PlayerPossession Possession { get; private set; }
        internal User GameUser { get; private set; }
        internal Kobold Kobold { get; private set; }
        internal KoboldCharacterController Controller { get; private set; }
        internal Grabber Grabber { get; private set; }
        internal PrecisionGrabber PGrabber { get; private set; }
        internal CharacterControllerAnimator Animator { get; private set; }
        internal CameraSwitcher CamSwitcher { get; private set; }
        internal HandIK HandIK { get; private set; }

        // ---- subsystems ----
        internal VRInput Input { get; private set; }
        internal VRLocomotion Loco { get; private set; }
        internal VRGrab Grab { get; private set; }
        internal VRCamera Cam { get; private set; }
        internal VRUI UI { get; private set; }
        internal VREquipment Equip { get; private set; }
        internal VRRenderer Renderer { get; private set; }

        // ---- hand transforms (grabber views + IK targets) ----
        internal Transform LeftHand { get; private set; }
        internal Transform RightHand { get; private set; }

        // ---- body-anchored VR rig ----
        // The camera must ride the avatar. STAGE space is floor-origin, so the
        // head's Y is absolute height; only the XZ is recentered onto the
        // avatar at capture. Physical offsets rotate with body-yaw changes;
        // keyboard/teleport movement carries the camera. Ghost mode freezes
        // the body contribution (the spectate mod parks the body elsewhere).
        private Vector3 _hmdAnchorXZ;    // tracking-space head XZ at capture
        private Vector3 _anchorBodyPos;  // body world pos at capture
        private float _anchorViewYaw;    // view yaw (head + offset) at capture
        private float _yawOffset;        // mouse/snap-turn layer (seated play)
        private bool _offsetCaptured = false;

        /// <summary>Extra view yaw from mouse/snap turn (seated fallbacks).</summary>
        internal float YawOffset { get { return _yawOffset; } }
        internal void AddYaw(float deg) { _yawOffset += deg; }

        // ---- spectate ghost mode (KK-LLM-NPC integration) ----
        // When the NPC mod's SpectateManager takes the camera (the player got
        // eaten), the VR rig detaches into a free-floating ghost instead of
        // riding the predator's eyes — the headset view keeps its own anchor,
        // the sticks drift it around, and gameplay input is suppressed.
        internal bool GhostModeActive { get; private set; }
        private System.Reflection.PropertyInfo _spectateActivePi;

        // ---- reflection cache (private game fields) ----
        private static FieldInfo fGrabber;
        private static FieldInfo fPGrabber;
        private static FieldInfo fGrabbing;
        private static FieldInfo fSwitchedMode;
        private static FieldInfo fAnimator;
        private static FieldInfo fController;
        private static bool _reflInit = false;

        internal void Initialize(VRKoboldKarePlugin plugin)
        {
            Plugin = plugin;
            gameObject.name = "VRKoboldKare_Manager";
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            // Canary (KK-LLM-NPC pattern): a t=0 death here means the bootstrap
            // sweep got us despite HideAndDontSave; @t big = real teardown.
            try { Log("manager destroyed @t=" + Time.unscaledTime.ToString("0.0")); } catch { }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DisableVR();
        }

        public void ToggleVR()
        {
            if (Enabled) DisableVR();
            else EnableVR();
        }
        public void EnableVR()
        {
            if (Enabled) return;
            Log("enabling VR...");

            // Mono mode paints the desktop window black and it's easy to lose
            // focus (dashboard, terminal). The new Input System RESETS the
            // keyboard device when the app is unfocused — keep input alive.
            try { Application.runInBackground = true; } catch { }
            try
            {
                var settings = UnityEngine.InputSystem.InputSystem.settings;
                if (settings != null) settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            }
            catch { }

            // WiVRn may have started after the game did — retry the bridge.
            if (!VRXR.Initialized || VRXR.BackendName == "keyboard")
            {
                string pref = !ReferenceEquals(Plugin, null) && Plugin.cfgHeadset != null ? Plugin.cfgHeadset.Value : "auto";
                VRXR.Reinit(pref, m => { if (VRKoboldKarePlugin.Log != null) VRKoboldKarePlugin.Log.LogWarning("VRXR: " + m); });
            }

            if (!FindPlayer())
            {
                Log("ERROR: player (PlayerPossession) not found. Join a world first, then toggle VR.");
                return;
            }

            EnsureReflection();

            // VR doesn't use the mouse cursor; the game gates input on Cursor.lockState == Locked.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            SpawnHandTransforms();
            if (!ReferenceEquals(Plugin, null) && Plugin.cfgHidePlayerBody != null && Plugin.cfgHidePlayerBody.Value)
                PrepareHeadRenderers();
            Enabled = true;
            _offsetCaptured = false;

            InitSubsystems();
            Log("VR enabled. Backend: " + VRXR.BackendName);
        }

        public void DisableVR()
        {
            if (!Enabled) return;
            Log("disabling VR...");

            if (Input != null) Input.ResetGrabState();
            if (Grab != null) Grab.Reset();
            if (UI != null) UI.HideAll();

            if (LeftHand != null) Destroy(LeftHand.gameObject);
            if (RightHand != null) Destroy(RightHand.gameObject);
            LeftHand = null;
            RightHand = null;

            if (Input != null) Destroy(Input);
            if (Loco != null) Destroy(Loco);
            if (Grab != null) Destroy(Grab);
            if (Cam != null) Destroy(Cam);
            if (UI != null) Destroy(UI);
            if (Equip != null) Destroy(Equip);
            if (Renderer != null) Destroy(Renderer);
            Input = null; Loco = null; Grab = null; Cam = null; UI = null; Equip = null; Renderer = null;

            if (PGrabber != null)
            {
                try { PrecisionGrabber.SetPinVisibility(false); PGrabber.SetPreviewState(false); }
                catch { }
            }
            SetExternalSpectateCamera(false);   // never strand the hand-off flag
            RestoreHeadRenderers();
            SetBoolField(fSwitchedMode, false);
            SetBoolField(fGrabbing, false);
            if (Animator != null)
            {
                Animator.inputGrabbing = false;
                Animator.inputActivate = false;
            }
            Cursor.visible = true;

            Enabled = false;
            Log("VR disabled.");
        }
        private bool FindPlayer()
        {
            PlayerPossession p = null;
            if (!PlayerPossession.TryGetPlayerInstance(out p) || p == null)
            {
                return false;
            }

            Possession = p;
            GameUser = p.user;
            Kobold = p.kobold;
            Controller = GetField(p, fController) as KoboldCharacterController;
            Grabber = GetField(p, fGrabber) as Grabber;
            PGrabber = GetField(p, fPGrabber) as PrecisionGrabber;
            Animator = GetField(p, fAnimator) as CharacterControllerAnimator;

            if (Kobold == null) Kobold = GameUser != null ? GameUser.kobold : null;
            if (Animator == null && Kobold != null)
            {
                Animator = Kobold.GetComponentInChildren<CharacterControllerAnimator>();
            }
            if (Controller == null && Kobold != null)
            {
                Controller = Kobold.GetComponentInChildren<KoboldCharacterController>();
            }
            if (HandIK == null && Kobold != null)
            {
                HandIK = Kobold.GetComponentInChildren<HandIK>();
            }
            if (CamSwitcher == null)
            {
                var list = FindObjectsOfType<CameraSwitcher>();
                if (list != null && list.Length > 0) CamSwitcher = list[0];
            }

            Log(string.Format("found player: kobold={0} controller={1} grabber={2} pGrabber={3} animator={4} camSwitch={5} handIK={6}",
                Kobold != null, Controller != null, Grabber != null, PGrabber != null, Animator != null, CamSwitcher != null, HandIK != null));
            return Kobold != null;
        }

        private static void EnsureReflection()
        {
            if (_reflInit) return;
            _reflInit = true;
            var t = typeof(PlayerPossession);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
            fGrabber = t.GetField("grabber", flags);
            fPGrabber = t.GetField("pGrabber", flags);
            fGrabbing = t.GetField("grabbing", flags);
            fSwitchedMode = t.GetField("switchedMode", flags);
            fAnimator = t.GetField("characterControllerAnimator", flags);
            fController = t.GetField("controller", flags);
            if (fGrabber == null || fPGrabber == null)
            {
                VRKoboldKarePlugin.Log.LogWarning("VRManager: PlayerPossession field layout changed; check 'grabber'/'pGrabber' names.");
            }
        }

        private static object GetField(object target, FieldInfo f)
        {
            if (target == null || f == null) return null;
            try { return f.GetValue(target); }
            catch { return null; }
        }

        internal static bool GetBoolField(object target, FieldInfo f)
        {
            var v = GetField(target, f);
            return v is bool && (bool)v;
        }

        internal static void SetBoolField(FieldInfo f, bool value)
        {
            if (f == null || f.IsStatic) return;
            var target = VRKoboldKarePlugin.VRManager != null ? VRKoboldKarePlugin.VRManager.Possession : null;
            if (target == null) return;
            try { f.SetValue(target, value); }
            catch { }
        }

        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

        internal static FieldInfo GetFieldInfo(string name)
        {
            FieldInfo f;
            if (_fieldCache.TryGetValue(name, out f))
            {
                return f;
            }
            f = typeof(PlayerPossession).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            _fieldCache[name] = f;
            return f;
        }

        internal static Transform GetGrabberView(object grabber)
        {
            try
            {
                var f = grabber.GetType().GetField("view");
                return f != null ? f.GetValue(grabber) as Transform : null;
            }
            catch { return null; }
        }

        internal static void SetGrabberViewField(object grabber, Transform t)
        {
            try
            {
                var f = grabber.GetType().GetField("view");
                if (f != null) f.SetValue(grabber, t);
            }
            catch { }
        }

        private bool _hotkeyLogged;
        private bool _legacyLogged;

        // The REAL hotkey monitor. This GameObject is DontDestroyOnLoad and
        // survives the Addressables bootstrap that kills BepInEx_Manager at
        // t=0 (the KK-LLM-NPC canary lesson) — the plugin component's Update
        // never ticks on this host.
        private void Update()
        {
            if (!_hotkeyLogged)
            {
                _hotkeyLogged = true;
                Log("hotkey monitor live on persistent GO (HOME / INSERT / F8 toggle VR)");
            }
            VRKoboldKarePlugin.ConsumeReloadAndApply();
            try
            {
                if (VRKoboldKarePlugin.KeyDownAny(KeyCode.Home) ||
                    VRKoboldKarePlugin.KeyDownAny(KeyCode.Insert) ||
                    VRKoboldKarePlugin.KeyDownAny(KeyCode.F8))
                {
                    Log("toggle hotkey pressed");
                    ToggleVR();
                }
            }
            catch (System.Exception e) { Log("hotkey update: " + e.Message); }

            if (!Enabled) return;

            // Lazy rebinding: VR enabled before the kobold finished spawning
            // misses Grabber/PrecisionGrabber — retry every 2s until found.
            if ((Grabber == null || PGrabber == null) && Time.unscaledTime >= _rebindAt)
            {
                _rebindAt = Time.unscaledTime + 2f;
                Log("grabber missing — rebinding player references");
                FindPlayer();
            }

            UpdateHeadVisibility();
            TickGhostMode();
        }

        private float _rebindAt;

        private void OnApplicationQuit()
        {
            try { VRXR.Shutdown(); } catch { }
        }

        // True while KK-LLM-NPC's spectate has the player's camera (soft
        // dependency: reflection so this mod works without the NPC mod).
        // Prefers the public ModHooks.PlayerSpectating (v1.13+), falls back to
        // SpectateManager.Active on older builds.
        private bool SpectateActive()
        {
            try
            {
                if (_spectateActivePi == null)
                {
                    var mh = Type.GetType("KKLLMNPC.ModHooks, KKLLMNPC");
                    if (mh != null)
                        _spectateActivePi = mh.GetProperty("PlayerSpectating",
                            BindingFlags.Public | BindingFlags.Static);
                    if (_spectateActivePi == null)
                    {
                        var t = Type.GetType("KKLLMNPC.SpectateManager, KKLLMNPC");
                        if (t == null) return false;
                        _spectateActivePi = t.GetProperty("Active",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    }
                    if (_spectateActivePi == null) return false;
                }
                return (bool)_spectateActivePi.GetValue(null, null);
            }
            catch { return false; }
        }

        /// <summary>
        /// The VR hand-off: while set, KK-LLM-NPC's built-in spectate camera
        /// writer stands down and OUR ghost rig drives the view (their v1.13
        /// ModHooks.ExternalSpectateCamera; silently skipped on older builds).
        /// The manager clears the flag on SpectateEnded; we also clear it on
        /// exit/teardown so it can never strand a player's camera.
        /// </summary>
        private void SetExternalSpectateCamera(bool value)
        {
            try
            {
                if (_externalCamPi == null)
                {
                    var mh = Type.GetType("KKLLMNPC.ModHooks, KKLLMNPC");
                    if (mh == null) return;
                    _externalCamPi = mh.GetField("ExternalSpectateCamera",
                        BindingFlags.Public | BindingFlags.Static) as System.Reflection.FieldInfo;
                    _externalControlFi = mh.GetField("ExternalPlayerControl",
                        BindingFlags.Public | BindingFlags.Static) as System.Reflection.FieldInfo;
                }
                _externalCamPi?.SetValue(null, value);
                // While ghosting, also stand down their input suppression so
                // our WASD ghost flight isn't fought by the component lock.
                if (_externalControlFi != null && value) _externalControlFi.SetValue(null, true);
                if (_externalControlFi != null && !value) _externalControlFi.SetValue(null, false);
            }
            catch { }
        }

        private System.Reflection.FieldInfo _externalCamPi;
        private System.Reflection.FieldInfo _externalControlFi;

        private void TickGhostMode()
        {
            bool want;
            try
            {
                want = !ReferenceEquals(Plugin, null) &&
                       Plugin.cfgGhostOnEaten != null && Plugin.cfgGhostOnEaten.Value &&
                       SpectateActive();
            }
            catch { want = false; }

            if (want && !GhostModeActive) EnterGhostMode();
            else if (!want && GhostModeActive) ExitGhostMode();

            if (GhostModeActive)
            {
                SetExternalSpectateCamera(true);   // their writer stands down
                GhostFly();
            }
        }

        private void EnterGhostMode()
        {
            GhostModeActive = true;
            if (Input != null) Input.ResetGrabState();
            string eater = "";
            try
            {
                var mh = Type.GetType("KKLLMNPC.ModHooks, KKLLMNPC");
                var pi = mh != null ? mh.GetProperty("EaterOfPlayer", BindingFlags.Public | BindingFlags.Static) : null;
                var k = pi != null ? pi.GetValue(null, null) as Kobold : null;
                if (k != null) eater = " — inside " + (k.name.Length > 0 ? k.name : "your predator");
            }
            catch { }
            Log("GHOST: eaten — VR detached into a floating ghost" + eater + " (left stick drifts, right stick rises/sinks).");
        }

        private void ExitGhostMode()
        {
            GhostModeActive = false;
            SetExternalSpectateCamera(false);
            _offsetCaptured = false;   // re-anchor to the revived body (one snap)
            Log("GHOST: back in your body — view re-anchored.");
        }

        // Ghost drift: move the frozen rig anchor, so head + hands fly together
        // without touching the game camera (the spectate writer stands down via
        // ModHooks.ExternalSpectateCamera). Sticks when controllers work;
        // keyboard + mouse (view-relative WASD, Space/Ctrl rise-sink) otherwise.
        private void GhostFly()
        {
            var f = VRXR.Poll();
            if (!f.valid || ReferenceEquals(Plugin, null) || Plugin.cfgGhostFlySpeed == null) return;
            float speed = Plugin.cfgGhostFlySpeed.Value;
            float dt = Time.deltaTime;

            if (!f.headTracked) return;
            Quaternion flyRot = Quaternion.Euler(0f, YawOffset, 0f) * f.headRot;
            Vector3 fwd = flyRot * Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f) fwd.Normalize(); else fwd = Vector3.forward;
            Vector3 right = flyRot * Vector3.right; right.y = 0f;
            if (right.sqrMagnitude > 0.001f) right.Normalize(); else right = Vector3.right;

            Vector3 move = Vector3.zero;
            // Sticks (left = drift, right.y = rise/sink).
            move += fwd * f.left.stick.y * speed * dt;
            move += right * f.left.stick.x * speed * dt;
            move += Vector3.up * f.right.stick.y * speed * dt;

            // Keyboard fallback (dead controllers): WASD view-relative,
            // Space rises, C sinks.
            float rise = 0f;
            try
            {
                Vector2 kb = Vector2.zero;
                if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.W)) kb.y += 1f;
                if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.S)) kb.y -= 1f;
                if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.D)) kb.x += 1f;
                if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.A)) kb.x -= 1f;
                if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.Space)) rise += 1f;
                if (VRKoboldKarePlugin.KeyHeldAny(KeyCode.C)) rise -= 1f;
                move += fwd * kb.y * speed * dt;
                move += right * kb.x * speed * dt;
            }
            catch { }
            move += Vector3.up * rise * speed * dt;

            _anchorBodyPos += move;   // ghost drift moves the frozen rig anchor
        }

        // ---- adaptive first-person body hiding ----
        // The camera sits at the head bone — INSIDE the skull mesh. Skull
        // polygons crossing the near plane flicker giant triangles through
        // the view. But a permanently headless avatar is worse in third-person
        // spectate (stations move the avatar away from a stationary camera).
        // Adaptive: head/body renderers hide ONLY while the camera is inside
        // the head (<0.6m), visible again the moment it's far — held items
        // live on separate meshes and stay visible; other players see their
        // own Photon copy of us regardless.
        private readonly List<Renderer> _headRenderers = new List<Renderer>();
        private bool _headPrepared;
        private bool _headHidden;
        private float _nextHeadCheck;

        private Transform FindHeadBone()
        {
            if (Kobold == null) return null;
            try
            {
                var desc = Kobold.GetComponent<CharacterDescriptor>();
                var anim = desc != null ? desc.GetDisplayAnimator() : null;
                var head = anim != null ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                if (head != null) return head;
            }
            catch { }
            return null;
        }

        // ---- OrbitCamera intent override ----
        // The shipped SetPlayerIntendedFacingDirection has a bug: it writes the
        // quaternion's y/x COMPONENTS into _aim as if they were Euler DEGREES
        // (var euler = lookDir.eulerAngles is computed and never used) — so any
        // facing except ≈north collapses the intent. Desktop never notices
        // (mouse mutates _aim directly); our head-feeding did. Drive _aim
        // directly: GetPlayerIntendedRotation = Euler(-aim.y, aim.x, 0).
        private System.Reflection.FieldInfo _orbitAimFi;
        private System.Reflection.FieldInfo _orbitInstFi;
        private bool _orbitInit;

        internal void SetOrbitAim(float yawDeg, float pitchDeg)
        {
            try
            {
                if (!_orbitInit)
                {
                    _orbitInit = true;
                    _orbitAimFi = typeof(OrbitCamera).GetField("_aim",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _orbitInstFi = typeof(OrbitCamera).GetField("instance",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                }
                if (_orbitAimFi == null || _orbitInstFi == null) return;
                // Re-resolve the instance EVERY call — the game recreates the
                // OrbitCamera per scene; a cached one is Unity-fake-null and
                // SetValue silently no-ops.
                var inst = _orbitInstFi.GetValue(null);
                if (inst == null) return;
                if (inst is UnityEngine.Object uo && uo == null) return;
                AimConvention.EulerToAim(yawDeg, pitchDeg, out float ax, out float ay);
                _orbitAimFi.SetValue(inst, new Vector2(ax, ay));
            }
            catch { }
        }

        private void PrepareHeadRenderers()
        {
            _headRenderers.Clear();
            _headHidden = false;
            if (Kobold == null) return;
            Transform head = FindHeadBone();
            if (head == null) { Log("head bone not found — player mesh stays visible"); return; }

            foreach (var r in Kobold.GetComponentsInChildren<Renderer>(true))
            {
                bool hasHead = false;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null && smr.bones != null)
                {
                    foreach (var b in smr.bones)
                    {
                        if (b == head) { hasHead = true; break; }
                    }
                }
                if (r.transform == head || r.transform.IsChildOf(head)) hasHead = true;
                if (hasHead) _headRenderers.Add(r);
            }
            _headPrepared = true;
            Log("head/body renderers tracked: " + _headRenderers.Count);
        }

        private void RestoreHeadRenderers()
        {
            foreach (var r in _headRenderers) if (r != null) { try { r.enabled = true; } catch { } }
            _headRenderers.Clear();
            _headPrepared = false;
            _headHidden = false;
        }

        private void UpdateHeadVisibility()
        {
            if (!_headPrepared || _headRenderers.Count == 0) return;
            if (Time.unscaledTime < _nextHeadCheck) return;
            _nextHeadCheck = Time.unscaledTime + 0.25f;
            try
            {
                var cam = Camera.main;
                Transform head = FindHeadBone();
                if (cam == null || head == null) return;
                bool near = (cam.transform.position - head.position).sqrMagnitude < 0.36f;   // <0.6m
                if (near == _headHidden) return;
                foreach (var r in _headRenderers) if (r != null) { try { r.enabled = !near; } catch { } }
                _headHidden = near;
            }
            catch { }
        }

        private void SpawnHandTransforms()
        {
            var leftObj = new GameObject("VRKK_LeftHand");
            var rightObj = new GameObject("VRKK_RightHand");
            DontDestroyOnLoad(leftObj);
            DontDestroyOnLoad(rightObj);
            leftObj.hideFlags = HideFlags.HideAndDontSave;
            rightObj.hideFlags = HideFlags.HideAndDontSave;
            LeftHand = leftObj.transform;
            RightHand = rightObj.transform;
        }

        internal void UpdateHands(XrFrame f)
        {
            if (!f.headTracked) return;

            // Untracked hands (dead controllers, hand tracking off): the
            // grabber views take the GAME CAMERA's pose — exactly how desktop
            // grabbing aims. Side offsets put the grab ray off-center (the
            // "can't pick things up" regression).
            Vector3 camPos = HeadToWorld(f.headPos, f.headRot);
            Quaternion camRot = Quaternion.Euler(0f, YawOffset, 0f) * f.headRot;
            try
            {
                var cam = Camera.main;
                if (cam != null) { camPos = cam.transform.position; camRot = cam.transform.rotation; }
            }
            catch { }

            Vector3 lPos = camPos, rPos = camPos;
            Quaternion lRot = camRot, rRot = camRot;
            if (f.left.tracked) { lPos = HeadToWorld(f.left.position, f.headRot); lRot = f.left.rotation; }
            if (f.right.tracked) { rPos = HeadToWorld(f.right.position, f.headRot); rRot = f.right.rotation; }

            if (LeftHand != null) { LeftHand.position = lPos; LeftHand.rotation = lRot; }
            if (RightHand != null) { RightHand.position = rPos; RightHand.rotation = rRot; }
        }

        /// <summary>
        /// Body-anchored HMD transform: world = bodyPos + rot(viewYaw delta) ×
        /// (hmdLocal − anchorXZ). The rotation uses the VIEW yaw (head + yaw
        /// offset), not the lagging body yaw — the camera stays glued to the
        /// view pivot while the avatar swings beneath it. Keyboard/teleport
        /// movement carries the camera via bodyPos; ghost mode freezes it.
        /// </summary>
        internal Vector3 HeadToWorld(Vector3 hmdLocal, Quaternion headRot)
        {
            if (!_offsetCaptured)
            {
                CaptureWorldOffset(hmdLocal, headRot);
            }

            Vector3 bodyPos = _anchorBodyPos;
            if (!GhostModeActive)
            {
                try
                {
                    var body = Controller != null ? Controller.body : null;
                    if (body == null && Kobold != null) body = Kobold.body;
                    if (body != null) bodyPos = body.position;
                }
                catch { }
            }

            float viewYaw = YawOffset + headRot.eulerAngles.y;
            Quaternion yawRot = Quaternion.Euler(0f, Mathf.DeltaAngle(_anchorViewYaw, viewYaw), 0f);
            Vector3 rel = hmdLocal - _hmdAnchorXZ;   // Y stays absolute (STAGE is floor-origin)
            return bodyPos + yawRot * rel;
        }

        private void CaptureWorldOffset(Vector3 hmdLocal, Quaternion headRot)
        {
            _hmdAnchorXZ = new Vector3(hmdLocal.x, 0f, hmdLocal.z);
            _anchorViewYaw = YawOffset + headRot.eulerAngles.y;
            _anchorBodyPos = Vector3.zero;
            try
            {
                var body = Controller != null ? Controller.body : null;
                if (body == null && Kobold != null) body = Kobold.body;
                if (body != null) _anchorBodyPos = body.position;
            }
            catch { }
            _offsetCaptured = true;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Enabled) return;
            if (scene.name == "MainMenu" || scene.name == "ErrorScene" || scene.name == "AddressableLoadingScene")
            {
                return;
            }
            Log("scene loaded: " + scene.name + " — re-binding player references");
            FindPlayer();
            _offsetCaptured = false;
            _headPrepared = false;   // new body instance; re-prepare
            if (!ReferenceEquals(Plugin, null) && Plugin.cfgHidePlayerBody != null && Plugin.cfgHidePlayerBody.Value)
                PrepareHeadRenderers();
        }

        private void Log(string msg)
        {
            // Static logger: the plugin COMPONENT is destroyed at t=0 (host-GO
            // churn) but the static ManualLogSource lives forever. Unity's
            // overloaded == null on the destroyed plugin was silently eating
            // every VR: log line after that.
            var l = VRKoboldKarePlugin.Log;
            if (l != null) l.LogInfo("VR: " + msg);
        }
        private void InitSubsystems()
        {
            Input = gameObject.AddComponent<VRInput>();
            Input.Initialize(this);

            Loco = gameObject.AddComponent<VRLocomotion>();
            Loco.Initialize(this);

            Grab = gameObject.AddComponent<VRGrab>();
            Grab.Initialize(this);

            Cam = gameObject.AddComponent<VRCamera>();
            Cam.Initialize(this);

            UI = gameObject.AddComponent<VRUI>();
            UI.Initialize(this);

            Equip = gameObject.AddComponent<VREquipment>();
            Equip.Initialize(this);

            Renderer = gameObject.AddComponent<VRRenderer>();
            Renderer.Initialize(this);

            VRPatches.EnsurePatched(this);
        }
    }
}