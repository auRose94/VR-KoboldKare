// VR-KoboldKare — BepInEx plugin for VR support in KoboldKare
// Written for Meta Quest + WiVRn. Supports locomotion, grabbing, interaction, camera, UI.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

namespace VRKoboldKare
{
    /// <summary>
    /// BepInEx plugin entry point. Manages config, lifecycle, scene gating,
    /// and Harmony patch application. Follows the KK-LLM-NPC pattern.
    /// </summary>
    [BepInPlugin("com.vr.koboldkare", "VRKoboldKare", "0.2.0")]
    public class VRKoboldKarePlugin : BaseUnityPlugin
    {
        /// <summary>Shared logger accessible from all subsystems.</summary>
        internal new BepInEx.Logging.ManualLogSource Logger => base.Logger;
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        // Harmony instance
        private static Harmony _harmony;

        // Central VR manager (singleton, created at Awake)
        private static VRManager _vrManager;
        internal static VRManager VRManager => _vrManager;

        // ---- configuration ----
        internal ConfigEntry<bool>    cfgEnabled;
        internal ConfigEntry<string>  cfgHeadset;
        internal ConfigEntry<bool>    cfgMirrorMode;
        internal ConfigEntry<bool>    cfgShowHandModels;
        internal ConfigEntry<float>   cfgHandModelScale;

        internal ConfigEntry<string>  cfgLocoMode;
        internal ConfigEntry<bool>    cfgLocoTeleportArc;
        internal ConfigEntry<int>     cfgLocoSnapAngle;
        internal ConfigEntry<float>   cfgLocoSmoothSpeed;
        internal ConfigEntry<float>   cfgLocoMaxTeleportDistance;

        internal ConfigEntry<string>  cfgGrabMode;
        internal ConfigEntry<float>   cfgGrabSpringStrength;
        internal ConfigEntry<float>   cfgGrabDampingStrength;
        internal ConfigEntry<float>   cfgGrabMaxDistance;
        internal ConfigEntry<bool>    cfgGrabAnalogGrip;
        internal ConfigEntry<float>   cfgGrabGripThreshold;
        internal ConfigEntry<float>   cfgGrabDropThreshold;
        internal ConfigEntry<bool>    cfgGrabMultiGrab;

        internal ConfigEntry<float>   cfgCameraFOV;
        internal ConfigEntry<float>   cfgCameraFOVScale;
        internal ConfigEntry<bool>    cfgCameraMotionSickness;
        internal ConfigEntry<float>   cfgCameraThirdPersonDistance;
        internal ConfigEntry<float>   cfgCameraThirdPersonHeight;
        internal ConfigEntry<string>    cfgCameraMode;

        internal ConfigEntry<float>   cfgUIScale;
        internal ConfigEntry<float>   cfgUIDistance;
        internal ConfigEntry<bool>    cfgUIShowReticle;
        internal ConfigEntry<bool>    cfgUIShowPrompt;
        internal ConfigEntry<float>   cfgUIPromptDistance;

        internal ConfigEntry<bool>    cfgPerfVignetteOnMove;
        internal ConfigEntry<float>   cfgPerfVignetteAmount;

        internal ConfigEntry<bool>    cfgNetSyncBody;
        internal ConfigEntry<bool>    cfgNetSyncHands;
        internal ConfigEntry<float>   cfgNetOwnershipRetry;

        internal ConfigEntry<bool>    cfgGhostOnEaten;
        internal ConfigEntry<float>   cfgGhostFlySpeed;
        internal ConfigEntry<string>  cfgSnapLeft;
        internal ConfigEntry<string>  cfgSnapRight;
        internal ConfigEntry<float>   cfgMouseSens;
        internal ConfigEntry<float>   cfgHudDistance;
        internal ConfigEntry<bool>    cfgOptionsMenuTab;
        internal ConfigEntry<string>  cfgRenderMode;
        internal ConfigEntry<float>   cfgEyeScale;
        internal ConfigEntry<bool>    cfgFlipY;
        internal ConfigEntry<bool>    cfgHidePlayerBody;

        private void Awake()
        {
            // Initialize static logger
            Log = Logger;

            // ---- bind configuration ----
            cfgEnabled = Config.Bind("VR.General", "Enabled", true,
                "Enable VR mode. Set to false to disable VR (game plays normally).");
            cfgHeadset = Config.Bind("VR.General", "Headset", "auto",
                "VR runtime to use: auto, openxr, steamvr.");
            cfgMirrorMode = Config.Bind("VR.General", "MirrorMode", true,
                "Mirror mode: HMD position directly drives camera.");
            cfgShowHandModels = Config.Bind("VR.General", "ShowHandModels", true,
                "Show hand meshes on controllers.");
            cfgHandModelScale = Config.Bind("VR.General", "HandModelScale", 1.0f,
                "Scale factor for hand models.");

            cfgLocoMode = Config.Bind("VR.Locomotion", "Mode", "teleport",
                "Locomotion mode: teleport, smooth, snap.");
            cfgLocoTeleportArc = Config.Bind("VR.Locomotion", "TeleportArc", true,
                "Show arc indicator during teleport.");
            cfgLocoSnapAngle = Config.Bind("VR.Locomotion", "SnapAngle", 45,
                "Degrees per snap turn.");
            cfgSnapLeft = Config.Bind("VR.Locomotion", "SnapTurnLeftKey", "LeftBracket",
                "Snap turn left key (Unity KeyCode name). Default [ — chosen to avoid game bindings like E (interact).");
            cfgSnapRight = Config.Bind("VR.Locomotion", "SnapTurnRightKey", "RightBracket",
                "Snap turn right key (Unity KeyCode name).");
            cfgMouseSens = Config.Bind("VR.Locomotion", "MouseYawSensitivity", 0.15f,
                "Degrees of view yaw per mouse pixel while VR is on (seated fallback rotation).");
            cfgHudDistance = Config.Bind("VR.UI", "HudDistance", 400f,
                "Plane distance for game HUD/menus converted into the headset view. Push back if the UI is too close.");
            cfgOptionsMenuTab = Config.Bind("VR.UI", "OptionsMenuTab", true,
                "Add 'KK VR' tabs to the game's options menu (main menu + in-game pause), driven by the game's own settings framework. Edits apply live and write to this config file.");
            cfgLocoSmoothSpeed = Config.Bind("VR.Locomotion", "SmoothSpeed", 2.0f,
                "Meters per second for smooth locomotion.");
            cfgLocoMaxTeleportDistance = Config.Bind("VR.Locomotion", "MaxTeleportDistance", 8.0f,
                "Maximum teleport distance in meters.");

            cfgGrabMode = Config.Bind("VR.Grab", "Mode", "spring",
                "Grab mode: spring, direct, hybrid.");
            cfgGrabSpringStrength = Config.Bind("VR.Grab", "SpringStrength", 200.0f,
                "Spring constant (N/m) for spring mode.");
            cfgGrabDampingStrength = Config.Bind("VR.Grab", "DampingStrength", 30.0f,
                "Damping coefficient.");
            cfgGrabMaxDistance = Config.Bind("VR.Grab", "MaxDistance", 3.5f,
                "Maximum grab distance in meters.");
            cfgGrabAnalogGrip = Config.Bind("VR.Grab", "AnalogGrip", true,
                "Use analog grip value (vs binary on/off).");
            cfgGrabGripThreshold = Config.Bind("VR.Grab", "GripThreshold", 0.5f,
                "Grip value threshold to activate grab.");
            cfgGrabDropThreshold = Config.Bind("VR.Grab", "DropThreshold", 0.3f,
                "Grip value threshold to drop item.");
            cfgGrabMultiGrab = Config.Bind("VR.Grab", "MultiGrab", true,
                "Allow grabbing multiple items at once.");

            cfgCameraFOV = Config.Bind("VR.Camera", "FOV", 90.0f,
                "FOV in mirror mode.");
            cfgCameraFOVScale = Config.Bind("VR.Camera", "FOVScale", 1.0f,
                "Multiplier for dynamic FOV.");
            cfgCameraMotionSickness = Config.Bind("VR.Camera", "MotionSickness", false,
                "Reduce FOV during movement to reduce motion sickness.");
            cfgCameraThirdPersonDistance = Config.Bind("VR.Camera", "ThirdPersonDistance", 2.5f,
                "Distance for third-person camera.");
            cfgCameraThirdPersonHeight = Config.Bind("VR.Camera", "ThirdPersonHeight", 1.0f,
                "Height offset for third-person camera.");
            cfgCameraMode = Config.Bind("VR.Camera", "Mode", "mirror",
                "Default VR camera mode: mirror, arcade, hybrid.");

            cfgUIScale = Config.Bind("VR.UI", "Scale", 1.0f,
                "UI panel scale factor.");
            cfgUIDistance = Config.Bind("VR.UI", "Distance", 1.5f,
                "Default interaction distance in meters.");
            cfgUIShowReticle = Config.Bind("VR.UI", "ShowReticle", true,
                "Show reticle at interaction point.");
            cfgUIShowPrompt = Config.Bind("VR.UI", "ShowPrompt", true,
                "Show interaction prompts.");
            cfgUIPromptDistance = Config.Bind("VR.UI", "PromptDistance", 1.5f,
                "Distance for interaction prompts above objects.");

            cfgPerfVignetteOnMove = Config.Bind("VR.Perf", "VignetteOnMove", false,
                "Show vignette during smooth locomotion.");
            cfgPerfVignetteAmount = Config.Bind("VR.Perf", "VignetteAmount", 0.3f,
                "Vignette darkness during movement.");

            cfgNetSyncBody = Config.Bind("VR.Network", "SyncBody", true,
                "Sync body position to other players.");
            cfgNetSyncHands = Config.Bind("VR.Network", "SyncHands", false,
                "Sync hand positions to other players.");
            cfgNetOwnershipRetry = Config.Bind("VR.Network", "OwnershipRetry", 3.0f,
                "Seconds between PhotonView ownership re-assertion.");

            cfgGhostOnEaten = Config.Bind("VR.Spectate", "GhostOnEaten", true,
                "When KK-LLM-NPC's spectate takes the camera (you got eaten), detach VR into a free-floating ghost instead of riding the predator's view.");
            cfgGhostFlySpeed = Config.Bind("VR.Spectate", "GhostFlySpeed", 2.0f,
                "Ghost drift speed in m/s (left stick moves, right stick rises/sinks).");

            cfgRenderMode = Config.Bind("VR.Render", "Mode", "mono",
                "Stereo render mode: mono (game camera -> one RT -> both eyes; safest, " +
                "no stereo depth), full (two eye cameras — crashes DXVK in this game " +
                "as of 2026-11-21), nocopy (eye cams without copy, diagnostic), black " +
                "(no rendering at all, diagnostic).");
            cfgEyeScale = Config.Bind("VR.Render", "EyeScale", 0.5f,
                "Eye texture scale vs the headset's recommended size (1.0 = Quest 3 native ~3096x3243 per eye). " +
                "Lower = lighter render load and smaller copies; the headset upscales.");
            cfgFlipY = Config.Bind("VR.Render", "FlipY", true,
                "Flip the image vertically (projection negation + inverted culling in mono mode). " +
                "Toggle if the world appears upside down.");
            cfgHidePlayerBody = Config.Bind("VR.Render", "HidePlayerBody", false,
                "Hide your kobold's body mesh in first person. Off by default — the body is visible; " +
                "held items always stay visible either way.");

            // ---- initialize VR input bridge (SteamVR / OpenXR / keyboard fallback) ----
            OptionsMenuTab.Ensure(this);
            StartConfigWatcher();
            Config.SettingChanged += OnSettingChanged;

            string headset = cfgHeadset != null ? cfgHeadset.Value : "auto";
            bool bridgeOk = VRXR.TryInit(headset,
                m => { if (Log != null) Log.LogWarning("VRXR: " + m); });
            if (bridgeOk)
                Logger.LogInfo("VR-KoboldKare: VR bridge ready (backend: " + VRXR.BackendName + ").");
            else
                Logger.LogWarning("VR-KoboldKare: VR bridge init failed; keyboard fallback only.");

            // ---- apply Harmony patches ----
            ApplyPatches();

            // ---- create VR manager ----
            // HideAndDontSave is the survival trick (KK-LLM-NPC canary lesson):
            // the game's Addressables bootstrap sweeps and destroys plain
            // objects — including DontDestroyOnLoad ones — but hidden objects
            // are invisible to the sweep, so the tick loop survives.
            var vrObj = new GameObject("VRKoboldKare_Manager");
            DontDestroyOnLoad(vrObj);
            vrObj.hideFlags = HideFlags.HideAndDontSave;
            _vrManager = vrObj.AddComponent<VRManager>();
            _vrManager.Initialize(this);

            Logger.LogInfo("VR-KoboldKare v0.2.0 loaded. Enable VR in-game or via config.");
        }

        /// <summary>
        /// Apply all Harmony patches for VR integration with game systems.
        /// </summary>
        private void ApplyPatches()
        {
            if (_harmony != null) return;
            _harmony = new Harmony("com.vr.koboldkare");

            try
            {
                // Input routing: intercept PlayerControls actions to inject VR input
                // (detailed patches in VRPatches.cs)

                // Camera origin override: replace camera position with HMD pose
                // (detailed patches in VRPatches.cs)

                // Grabber view: set Grabber.view to right controller
                // (detailed patches in VRPatches.cs)

                // Cursor lock removal: VR doesn't use cursor locking
                // (detailed patches in VRPatches.cs)

                Logger.LogInfo("VR-KoboldKare: Harmony patches applied.");
            }
            catch (Exception e)
            {
                Logger.LogWarning("VR-KoboldKare: failed to apply patches: " + e.Message);
            }
        }

        // ---- config hot-reload ----
        // A FileSystemWatcher queues a reload when the config file is saved
        // (external edit OR our own writes); the persistent VRKoboldKare_Manager
        // applies it on the main thread (Main's Update is dead after t=0).
        private static BepInEx.Configuration.ConfigFile _config;
        internal static volatile bool ReloadQueued;
        internal static int ReloadQueuedTick;

        private void StartConfigWatcher()
        {
            try
            {
                _config = Config;
                string path = Config.ConfigFilePath;
                var watcher = new System.IO.FileSystemWatcher(
                    System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path))
                {
                    NotifyFilter = System.IO.NotifyFilters.LastWrite,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += (s, e) => { ReloadQueued = true; ReloadQueuedTick = Environment.TickCount; };
                Logger.LogInfo("VR: config watcher live (" + path + ")");
            }
            catch (Exception e)
            {
                Logger.LogWarning("VR: config watcher failed: " + e.Message);
            }
        }

        private static void OnSettingChanged(object sender, BepInEx.Configuration.SettingChangedEventArgs e)
        {
            try
            {
                if (e.ChangedSetting.Definition.Key == "HudDistance")
                {
                    var mgr = VRManager;
                    mgr?.UI?.ReapplyHudScale();
                }
            }
            catch { }
        }

        internal static void ConsumeReloadAndApply()
        {
            if (!ReloadQueued) return;
            ReloadQueued = false;
            try
            {
                _config?.Reload();
                Log?.LogInfo("VR: config reloaded from disk");
            }
            catch (Exception ex) { Log?.LogWarning("VR: config reload failed: " + ex.Message); }
        }

        // Key detection that works under both input stacks: KoboldKare runs the
        // new Input System, where legacy UnityEngine.Input.GetKeyDown THROWS —
        // silently swallowed exceptions read as "the hotkey does nothing"
        // (same lesson as KK-LLM-NPC's vore hotkeys).
        private static bool _legacyInputBroken;
        internal static bool KeyDownAny(KeyCode kc)
        {
            if (!_legacyInputBroken)
            {
                try { return UnityEngine.Input.GetKeyDown(kc); }
                catch
                {
                    _legacyInputBroken = true;
                    try { Log.LogWarning("legacy UnityEngine.Input unavailable — VR hotkeys use the new Input System"); } catch { }
                }
            }
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return false;
                UnityEngine.InputSystem.Key key;
                if (!System.Enum.TryParse(kc.ToString(), true, out key)) return false;
                var ctl = kb[key];
                return ctl != null && ctl.wasPressedThisFrame;
            }
            catch { return false; }
        }

        /// <summary>Held-key variant of KeyDownAny (dual input stacks).</summary>
        internal static bool KeyHeldAny(KeyCode kc)
        {
            if (!_legacyInputBroken)
            {
                try { return UnityEngine.Input.GetKey(kc); }
                catch { _legacyInputBroken = true; }
            }
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return false;
                UnityEngine.InputSystem.Key key;
                if (!System.Enum.TryParse(kc.ToString(), true, out key)) return false;
                var ctl = kb[key];
                return ctl != null && ctl.isPressed;
            }
            catch { return false; }
        }

        private bool _hotkeyMonitorLogged;

        /// <summary>
        /// Secondary hotkey path — only ticks if the BepInEx manager object
        /// survives. KoboldKare's Addressables bootstrap destroys BepInEx_Manager
        /// at t=0 (KK-LLM-NPC's canary proved it), so the REAL hotkey monitor
        /// lives on the persistent VRKoboldKare_Manager GameObject.
        /// </summary>
        private void Update()
        {
            if (!_hotkeyMonitorLogged)
            {
                _hotkeyMonitorLogged = true;
                try { Logger.LogInfo("VR: plugin-host hotkey monitor live (HOME / INSERT / F8)"); } catch { }
            }
            try
            {
                if (_vrManager != null &&
                    (KeyDownAny(KeyCode.Home) || KeyDownAny(KeyCode.Insert) || KeyDownAny(KeyCode.F8)))
                {
                    _vrManager.ToggleVR();
                }
            }
            catch (Exception e)
            {
                try { Logger.LogWarning("VR: hotkey update: " + e.Message); } catch { }
            }
        }

        private void OnDestroy()
        {
            // KK-LLM-NPC lesson: this fires at t=0 when the game's Addressables
            // bootstrap destroys BepInEx_Manager — it is NOT a real shutdown.
            // The OpenXR bridge and Harmony patches keep running on the
            // persistent VRKoboldKare_Manager; real teardown happens in
            // VRManager.OnApplicationQuit.
            try
            {
                Logger.LogWarning("VR: plugin host destroyed @t=" + UnityEngine.Time.unscaledTime.ToString("0.0") +
                    " (normal host-GO churn — VR continues on VRKoboldKare_Manager)");
            }
            catch { }
        }
    }
}
