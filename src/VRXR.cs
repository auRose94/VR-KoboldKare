// VR-KoboldKare — VR input bridge
// Real OpenXR 1.0 backend against the Khronos openxr_loader.dll (shipped in
// BepInEx/plugins). Under Proton, the loader resolves wineopenxr.dll via the
// prefix registry and forwards to the native runtime chosen by XR_RUNTIME_JSON
// (WiVRn: /usr/share/openxr/1/openxr_wivrn.json — set in Steam launch options).
// Keyboard backend remains for desktop development.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VRKoboldKare
{
    /// <summary>Normalized state for one controller hand (Unity coords, left-handed).</summary>
    public struct XrHand
    {
        public bool tracked;
        public Vector3 position;
        public Quaternion rotation;
        public float trigger;   // 0..1
        public float grip;      // 0..1
        public Vector2 stick;   // x,y in -1..1
        public bool a;
        public bool b;
        public bool x;
        public bool y;
        public bool menu;
    }

    /// <summary>One located body joint (Unity coords, app-space like hands).</summary>
    public struct XrBodyJoint
    {
        public bool tracked;
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>Upper-body snapshot from XR_FB_body_tracking (hips → spine →
    /// chest → head). The DEFAULT joint set has NO knees/feet — legs stay
    /// procedural in VRBody. Valid only while SlimeVR-style body data streams
    /// (isActive) — empty frame otherwise.</summary>
    public struct XrBodyFrame
    {
        public bool valid;
        public float confidence;
        public XrBodyJoint hips;
        public XrBodyJoint spineLower;
        public XrBodyJoint spineMiddle;
        public XrBodyJoint spineUpper;
        public XrBodyJoint chest;
        public XrBodyJoint head;
    }

    /// <summary>One polled VR frame (head + both hands + body).</summary>
    public struct XrFrame
    {
        public bool valid;         // true if a frame was produced
        public bool headTracked;   // true if head pose is valid
        public Vector3 headPos;
        public Quaternion headRot;
        public XrHand left;
        public XrHand right;
        public XrBodyFrame body;
        public string backend;
        public uint packet;        // increments each poll, for edge detection
    }

    internal interface IVxrBackend
    {
        string Name { get; }
        bool Init(Action<string> log);
        void Poll(out XrFrame frame);
        void Haptic(bool left, float amplitude, int durationMs);
        void Shutdown();
    }

    /// <summary>
    /// Static entry point for VR input. Picks a backend (openxr or keyboard),
    /// exposes the latest polled frame, and haptics.
    /// </summary>
    public static class VRXR
    {
        public static string BackendName { get { return _backend != null ? _backend.Name : "none"; } }
        public static bool Initialized { get { return _backend != null; } }
        internal static OpenXRBackend OpenXR { get { return _backend as OpenXRBackend; } }

        /// <summary>XR_FB_body_tracking tracker exists (real FBT pipeline live
        /// when a body actually streams — see XrFrame.body.valid).</summary>
        public static bool BodyTrackingAvailable { get { var xr = OpenXR; return xr != null && xr.BodyTrackingAvailable; } }

        /// <summary>True when an OpenXR backend owns the session AND frames
        /// are flowing (state ≥ SYNCHRONIZED) — the honest "WiVRn/SteamVR is
        /// up and the headset is found" signal. The VR toggle gates on this:
        /// no more enabling VR into a half-handshake with the keyboard
        /// backend pretending.</summary>
        public static bool SessionRendering { get { var xr = OpenXR; return xr != null && xr.SessionRendering; } }

        private static volatile IVxrBackend _backend;
        private static XrFrame _last;
        private static readonly object _lock = new object();
        private static readonly object _initLock = new object();
        private static volatile bool _initRunning;

        /// <summary>
        /// Try to initialize a backend. "openxr" fails loudly (no silent keyboard
        /// fallback) so WiVRn problems are visible in the BepInEx log.
        /// </summary>
        public static bool TryInit(string preferred, Action<string> log)
        {
            if (log == null) log = delegate { };
            if (_backend != null) return true;
            // Single-flight: the background init thread and an in-game Reinit
            // can race; only one walks the runtime probe.
            if (_initRunning) return false;
            lock (_initLock)
            {
                if (_backend != null || _initRunning) return _backend != null;
                _initRunning = true;
            }
            try
            {
                return TryInitCore(preferred, log);
            }
            finally
            {
                _initRunning = false;
            }
        }

        private static bool TryInitCore(string preferred, Action<string> log)
        {
            if (log == null) log = delegate { };

            string p = (preferred == null) ? "auto" : preferred.ToLowerInvariant();
            IVxrBackend[] candidates;
            if (p == "openxr" || p == "xr")
                candidates = new IVxrBackend[] { new OpenXRBackend() };
            else if (p == "keyboard" || p == "kb" || p == "dev")
                candidates = new IVxrBackend[] { new KeyboardBackend() };
            else
                candidates = new IVxrBackend[] { new OpenXRBackend(), new KeyboardBackend() };

            foreach (IVxrBackend c in candidates)
            {
                try
                {
                    if (c.Init(log))
                    {
                        _backend = c;
                        log("VRXR: backend active: " + c.Name);
                        return true;
                    }
                }
                catch (Exception e)
                {
                    log("VRXR: backend " + c.Name + " threw: " + e);
                }
            }
            log("VRXR: no backend available.");
            return false;
        }

        /// <summary>
        /// Retry initialization (e.g. WiVRn was not running at game start but
        /// the headset connected later).
        /// </summary>
        public static bool Reinit(string preferred, Action<string> log)
        {
            if (_backend != null && _backend is OpenXRBackend) return true; // backend retries internally
            Shutdown();
            return TryInit(preferred, log);
        }

        /// <summary>Re-anchor the keyboard backend's synthetic head to the game
        /// camera (VR enable / scene load). No-op on other backends.</summary>
        public static void ResetKeyboardHead()
        {
            var kb = _backend as KeyboardBackend;
            if (kb != null) kb.ResetHead();
        }

        /// <summary>Get the latest polled frame. Returns an invalid frame if no backend.</summary>
        public static XrFrame Poll()
        {
            lock (_lock)
            {
                if (_backend != null)
                {
                    try
                    {
                        XrFrame f;
                        _backend.Poll(out f);
                        if (f.valid)
                        {
                            _last = f;
                            return _last;
                        }
                    }
                    catch { }
                }
                return _last;
            }
        }

        /// <summary>Fire a haptic pulse on one controller (no-op if unsupported).</summary>
        public static void Haptic(bool left, float amplitude, int durationMs)
        {
            IVxrBackend b = _backend;
            if (b != null)
            {
                try { b.Haptic(left, amplitude, durationMs); }
                catch { }
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_backend != null)
                {
                    try { _backend.Shutdown(); }
                    catch { }
                    _backend = null;
                }
                _last = new XrFrame();
            }
        }
    }

    /// <summary>
    /// Loads the Khronos openxr_loader.dll that build.sh deploys next to the
    /// plugin. Under Proton the loader then finds wineopenxr.dll through the
    /// prefix's OpenXR registry key.
    /// </summary>
    internal static class NativeLib
    {
        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryExW(string name, IntPtr hFile, uint flags);

        public static bool TryLoad(string dllName, Action<string> log)
        {
            try
            {
                if (log != null)
                {
                    log("VRXR env: XR_RUNTIME_JSON=" + Environment.GetEnvironmentVariable("XR_RUNTIME_JSON"));
                }
                if (LoadLibraryW(dllName) != IntPtr.Zero) return true;

                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                {
                    string path = Path.Combine(dir, dllName);
                    if (File.Exists(path))
                    {
                        if (LoadLibraryExW(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) != IntPtr.Zero)
                        {
                            if (log != null) log("NativeLib: loaded " + path);
                            return true;
                        }
                        if (log != null) log("NativeLib: found but failed to load " + path);
                    }
                    else if (log != null) log("NativeLib: " + path + " missing (build.sh deploys it)");
                }
            }
            catch (Exception e)
            {
                if (log != null) log("NativeLib: " + e.Message);
            }
            return false;
        }
    }

    /// <summary>
    /// Real OpenXR 1.0 backend (WiVRn via Proton's wineopenxr, or any runtime).
    ///
    /// Lifecycle (managed on a background thread):
    ///   xrCreateInstance -> xrGetSystem (retry until the headset connects)
    ///   -> xrCreateSession (D3D11 binding, device grabbed from Unity) 
    ///   -> actions/bindings/attach -> begin on READY
    /// Input (main thread, via Poll): xrSyncActions + xrLocateSpace per hand.
    /// Frames: the background thread pumps empty frames while idle; the stereo
    /// renderer claims frame ownership when VR is enabled in-game.
    /// </summary>
    internal sealed unsafe class OpenXRBackend : IVxrBackend
    {
        public string Name { get { return "openxr"; } }

        // ---- handles / state ----
        private Action<string> _log;
        private readonly object _xrLock = new object();   // serializes all session XR calls
        private ulong _instance;
        private bool _hasD3D11;
        private volatile bool _running;                   // thread keep-alive
        private Thread _thread;

        private ulong _systemId;                          // 0 until a headset is found
        private IntPtr _d3dDevice;                        // ID3D11Device* from Unity
        private volatile bool _wantSession;
        private ulong _session;
        private volatile uint _sessionState;              // XrSessionState
        private bool _sessionBegun;
        private volatile bool _framesOwnedByMain;         // renderer claimed frame loop
        private volatile bool _threadInFrame;
        private ulong _appSpace;                          // LOCAL_FLOOR / LOCAL / STAGE
        private uint _appSpaceType;
        private ulong _viewSpace;
        private long _lastDisplayTime;

        // actions
        private ulong _actionSet;
        private readonly Dictionary<string, ulong> _actions = new Dictionary<string, ulong>();
        private ulong _pathHandL, _pathHandR;
        private ulong _spaceGripL, _spaceGripR, _spaceAimL, _spaceAimR;
        private readonly ulong[] _subactionPaths = new ulong[2];

        private uint _packet;
        private XrFrame _latest;
        private readonly object _frameLock = new object();
        private string _runtimeName = "";
        private uint _lastLoggedState = 999;

        // ---------- init ----------

        public bool Init(Action<string> log)
        {
            _log = log;
            if (!NativeLib.TryLoad("openxr_loader.dll", log))
            {
                log("OpenXR: openxr_loader.dll not loadable — deploy it with build.sh.");
                return false;
            }

            // Enumerate instance extensions (great diagnostics for the Proton chain).
            uint extCount;
            int r = XrApi.xrEnumerateInstanceExtensionProperties(null, 0, out extCount, null);
            if (!XrResultCode.Ok(r))
            {
                log("OpenXR: xrEnumerateInstanceExtensionProperties failed: " + XrResultCode.Name(r));
                log("OpenXR: likely the Wine-side runtime chain is broken. Check:");
                log("  - Steam launch options: XR_RUNTIME_JSON=/usr/share/openxr/1/openxr_wivrn.json %command%");
                log("  - wivrn-server running (systemctl --user start wivrn)");
                return false;
            }
            var exts = new XrExtensionProperties[extCount];
            for (int i = 0; i < exts.Length; i++) exts[i].type = XrType.EXTENSION_PROPERTIES;
            r = XrApi.xrEnumerateInstanceExtensionProperties(null, extCount, out extCount, exts);
            var extNames = new List<string>();
            if (XrResultCode.Ok(r))
            {
                for (int i = 0; i < extCount; i++)
                {
                    fixed (byte* p = exts[i].extensionName) extNames.Add(XrStr.Read(p, 128));
                }
            }
            log("OpenXR: runtime exposes " + extCount + " instance extensions" +
                " (d3d11: " + extNames.Contains(XrExt.D3D11) +
                ", hand tracking: " + extNames.Contains(XrExt.HandTracking) + ")");
            for (int i = 0; i < extNames.Count; i += 8)
                log("OpenXR: ext[" + i + "]: " + string.Join(", ",
                    extNames.GetRange(i, Math.Min(8, extNames.Count - i)).ToArray()));
            _hasD3D11 = extNames.Contains(XrExt.D3D11);
            _hasHandTracking = extNames.Contains(XrExt.HandTracking);
            _hasBodyTracking = extNames.Contains(XrExt.BodyTrackingFB);
            log("OpenXR: body tracking (XR_FB_body_tracking): " + _hasBodyTracking +
                " — SlimeVR data flows through the WiVRn server when its bridge is connected");

            // Create the instance.
            var ci = new XrInstanceCreateInfo { type = XrType.INSTANCE_CREATE_INFO };
            fixed (byte* p = ci.applicationName) XrStr.Copy(p, "VRKoboldKare", 128);
            ci.applicationVersion = 1;
            fixed (byte* p = ci.engineName) XrStr.Copy(p, "Unity-Flat-BepInEx", 128);
            ci.engineVersion = 1;
            ci.apiVersion = 1UL << 48;   // XR_API_VERSION_1_0
            ci.enabledApiLayerCount = 0;
            ci.enabledApiLayerNames = IntPtr.Zero;

            IntPtr extPtr = IntPtr.Zero;
            try
            {
                // REQUEST what the runtime advertised: d3d11_enable first
                // (wineopenxr only advertises it once a DXVK device exists
                // in-process — true inside the game, false in bare test exes),
                // plus body tracking for the SlimeVR pipeline. A fallback
                // ladder drops the optional tail on EXTENSION_NOT_PRESENT,
                // ending in an input-only session.
                var wantList = new List<string>();
                if (_hasD3D11) wantList.Add(XrExt.D3D11);
                if (_hasBodyTracking) wantList.Add(XrExt.BodyTrackingFB);

                while (true)
                {
                    if (extPtr != IntPtr.Zero) { FreeStringArray(extPtr, (int)ci.enabledExtensionCount); extPtr = IntPtr.Zero; }
                    ci.enabledExtensionCount = (uint)wantList.Count;
                    extPtr = wantList.Count > 0 ? AllocStringArray(wantList.ToArray()) : IntPtr.Zero;
                    ci.enabledExtensionNames = extPtr;
                    r = XrApi.xrCreateInstance(ref ci, out _instance);
                    if (XrResultCode.Ok(r))
                    {
                        _hasD3D11 = wantList.Contains(XrExt.D3D11);
                        _hasBodyTracking = wantList.Contains(XrExt.BodyTrackingFB);
                        break;
                    }
                    // ANY create failure with extensions still requested drops
                    // the optional tail and retries — an advertised-but-
                    // poisoned extension combination must not take the whole
                    // bridge down (RUNTIME_UNAVAILABLE with body tracking
                    // requested was the 0.4.0 WiVRn regression). Only the
                    // EMPTY list failing is terminal.
                    if (wantList.Count > 0)
                    {
                        log("OpenXR: instance create failed (" + XrResultCode.Name(r) + ") — retrying without '" +
                            wantList[wantList.Count - 1] + "'");
                        wantList.RemoveAt(wantList.Count - 1);
                        continue;
                    }
                    break;   // hard failure — handled below
                }
            }
            finally
            {
                if (extPtr != IntPtr.Zero) FreeStringArray(extPtr, (int)ci.enabledExtensionCount);
            }
            if (!XrResultCode.Ok(r))
            {
                log("OpenXR: xrCreateInstance failed: " + XrResultCode.Name(r) +
                    " — is the runtime service up (systemctl --user status wivrn) and are the" +
                    " pressure-vessel launch options set (PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1)?");
                return false;
            }

            var props = new XrInstanceProperties { type = XrType.INSTANCE_PROPERTIES };
            if (XrResultCode.Ok(XrApi.xrGetInstanceProperties(_instance, ref props)))
            {
                fixed (byte* p = props.runtimeName) _runtimeName = XrStr.Read(p, 128);
                log("OpenXR: runtime = '" + _runtimeName + "' v" + (props.runtimeVersion >> 48) + "." +
                    ((props.runtimeVersion >> 32) & 0xFFFF) + "." + ((props.runtimeVersion >> 16) & 0xFFFF));
                if (_runtimeName.IndexOf("SteamVR", StringComparison.OrdinalIgnoreCase) >= 0)
                    log("OpenXR: note — SteamVR runtime selected. For WiVRn set XR_RUNTIME_JSON=/usr/share/openxr/1/openxr_wivrn.json in Steam launch options.");
            }

            _running = true;
            _wantSession = true;
            _thread = new Thread(SessionThreadMain) { IsBackground = true, Name = "VRKK-OpenXR-Session" };
            _thread.Start();
            return true;
        }

        private static IntPtr AllocStringArray(string[] strs)
        {
            IntPtr arr = Marshal.AllocHGlobal(strs.Length * IntPtr.Size);
            for (int i = 0; i < strs.Length; i++)
                Marshal.WriteIntPtr(arr, i * IntPtr.Size, Marshal.StringToHGlobalAnsi(strs[i]));
            return arr;
        }

        private static void FreeStringArray(IntPtr arr, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
            Marshal.FreeHGlobal(arr);
        }

        private uint Check(int r, string what)
        {
            if (!XrResultCode.Ok(r) && _log != null)
                _log("OpenXR: " + what + " -> " + XrResultCode.Name(r));
            return (uint)r;
        }

        private void Log(string s) { if (_log != null) _log("OpenXR: " + s); }

        // ---------- background thread: system, session, events, idle frames ----------

        private void SessionThreadMain()
        {
            long nextSystemRetry = 0;
            while (_running)
            {
                try { Tick(ref nextSystemRetry); }
                catch (Exception e) { Log("session thread error: " + e.Message); }
                Thread.Sleep(50);
            }
        }

        private void Tick(ref long nextSystemRetry)
        {
            // Events first (cheap, non-blocking).
            if (_instance != 0)
            {
                var ev = new XrEventDataBuffer { type = XrType.EVENT_DATA_BUFFER };
                int r;
                lock (_xrLock)
                {
                    r = XrApi.xrPollEvent(_instance, ref ev);
                }
                if (r == XrResultCode.XR_SUCCESS) HandleEvent(ref ev);
                else if (r != XrResultCode.XR_EVENT_UNAVAILABLE) Check(r, "xrPollEvent");
            }

            // Find the system (retry — WiVRn may connect after game start).
            if (_systemId == 0 && _instance != 0 && Environment.TickCount >= nextSystemRetry)
            {
                nextSystemRetry = Environment.TickCount + 2000;
                var gi = new XrSystemGetInfo
                {
                    type = XrType.SYSTEM_GET_INFO,
                    formFactor = XrEnums.FORM_FACTOR_HEAD_MOUNTED_DISPLAY,
                };
                ulong sys;
                int r;
                lock (_xrLock) { r = XrApi.xrGetSystem(_instance, ref gi, out sys); }
                if (XrResultCode.Ok(r) && sys != XrEnums.NULL_SYSTEM_ID)
                {
                    _systemId = sys;
                    var sp = new XrSystemProperties { type = XrType.SYSTEM_PROPERTIES };
                    var bodyProps = new XrSystemBodyTrackingPropertiesFB { type = XrType.SYSTEM_BODY_TRACKING_PROPERTIES_FB };
                    // Stack-local: &local in unsafe code is already fixed — a
                    // fixed statement on it is a compile error (CS0213).
                    sp.next = (IntPtr)(&bodyProps);   // chain FB body properties into the query
                    lock (_xrLock) { XrApi.xrGetSystemProperties(_instance, _systemId, ref sp); }
                    _bodySystemSupports = bodyProps.supportsBodyTracking != 0;
                    string sysName;
                    fixed (byte* p = sp.systemName) sysName = XrStr.Read(p, 256);
                    Log("system found: '" + sysName +
                        "' maxSwapchain " + sp.graphicsProperties.maxSwapchainImageWidth + "x" +
                        sp.graphicsProperties.maxSwapchainImageHeight +
                        ", bodyTracking " + (_bodySystemSupports ? "yes" : "no"));
                }
                else if (r == XrResultCode.XR_ERROR_FORM_FACTOR_UNAVAILABLE)
                {
                    Log("no XR system yet (WiVRn client not connected / headset off) — retrying every 2s");
                }
                else if (!XrResultCode.Ok(r))
                {
                    Check(r, "xrGetSystem");
                }
            }

            // Create the session once we have system + graphics device.
            if (_session == 0 && _wantSession && _systemId != 0)
            {
                // Wait for the Unity D3D11 device only when it's actually coming.
                if (_hasD3D11 && _d3dDevice == IntPtr.Zero &&
                    (_graphicsApi == null || _graphicsApi == "Direct3D11")) return;
                CreateSession();
            }

            // Idle frame pump: keep the runtime fed while the in-game renderer
            // hasn't claimed frame ownership (empty frames, no layers).
            if (_session != 0 && _sessionBegun && !_framesOwnedByMain && !_threadInFrame)
            {
                lock (_xrLock)
                {
                    if (!_framesOwnedByMain && _sessionBegun)
                    {
                        _threadInFrame = true;
                        try
                        {
                            var wait = new XrFrameWaitInfo { type = XrType.FRAME_WAIT_INFO };
                            var fs = new XrFrameState { type = XrType.FRAME_STATE };
                            if (XrResultCode.Ok(XrApi.xrWaitFrame(_session, ref wait, ref fs)))
                            {
                                _lastDisplayTime = fs.predictedDisplayTime;
                                var begin = new XrFrameBeginInfo { type = XrType.FRAME_BEGIN_INFO };
                                XrApi.xrBeginFrame(_session, ref begin);
                                var end = new XrFrameEndInfo
                                {
                                    type = XrType.FRAME_END_INFO,
                                    displayTime = fs.predictedDisplayTime,
                                    environmentBlendMode = XrEnums.ENVIRONMENT_BLEND_MODE_OPAQUE,
                                    layerCount = 0,
                                    layers = IntPtr.Zero,
                                };
                                XrApi.xrEndFrame(_session, ref end);
                            }
                        }
                        finally { _threadInFrame = false; }
                    }
                }
            }
        }

        private void HandleEvent(ref XrEventDataBuffer ev)
        {
            if (ev.type == XrType.EVENT_DATA_SESSION_STATE_CHANGED)
            {
                // The event struct overlays the WHOLE buffer (type@0, next@8,
                // session@16, state@24, time@32) — reading from &varying[0]
                // (= buffer+16) misaligned every field and garbled the state.
                XrEventDataSessionStateChanged sc;
                fixed (XrEventDataBuffer* p = &ev)
                {
                    sc = *(XrEventDataSessionStateChanged*)p;
                }
                _sessionState = sc.state;
                if (sc.state != _lastLoggedState)
                {
                    _lastLoggedState = sc.state;
                    Log("session state -> " + SessionStateName(sc.state));
                    if (sc.state == XrEnums.SESSION_STATE_SYNCHRONIZED && _wasDisplayed)
                    {
                        _syncFlaps++;
                        if (_syncFlaps == 8 || _syncFlaps == 30)
                            Log("session dropped to SYNCHRONIZED " + _syncFlaps + "x — the compositor is " +
                                "starving the app (frame budget) or the graphics stack is stale. " +
                                "Known good fix: reboot the machine (driver/runtime state), or lower EyeScale.");
                    }
                    if (sc.state == XrEnums.SESSION_STATE_VISIBLE || sc.state == XrEnums.SESSION_STATE_FOCUSED)
                        _wasDisplayed = true;
                }
                if (sc.state == XrEnums.SESSION_STATE_READY && !_sessionBegun)
                {
                    var bi = new XrSessionBeginInfo
                    {
                        type = XrType.SESSION_BEGIN_INFO,
                        primaryViewConfigurationType = XrEnums.VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
                    };
                    lock (_xrLock)
                    {
                        if (XrResultCode.Ok(XrApi.xrBeginSession(_session, ref bi)))
                            _sessionBegun = true;
                    }
                }
                else if (sc.state == XrEnums.SESSION_STATE_STOPPING)
                {
                    lock (_xrLock)
                    {
                        _framesOwnedByMain = false;
                        _sessionBegun = false;
                        if (_session != 0) XrApi.xrEndSession(_session);
                        DestroySpacesAndActions();
                        _session = 0;
                        Log("session ended (headset went away?) — will recreate when it returns");
                    }
                }
                else if (sc.state == XrEnums.SESSION_STATE_EXITING || sc.state == XrEnums.SESSION_STATE_LOSS_PENDING)
                {
                    _running = false;
                    Log("session exiting/lost — OpenXR backend shutting down");
                }
            }
            else if (ev.type == XrType.EVENT_DATA_INSTANCE_LOSS_PENDING)
            {
                Log("instance loss pending — stopping");
                _running = false;
            }
            else if (ev.type == XrType.EVENT_DATA_INTERACTION_PROFILE_CHANGED)
            {
                LogCurrentInteractionProfile();
            }
        }

        private void CreateSession()
        {
            lock (_xrLock)
            {
                var si = new XrSessionCreateInfo { type = XrType.SESSION_CREATE_INFO, systemId = _systemId };
                IntPtr binding = IntPtr.Zero;
                try
                {
                    if (_hasD3D11 && _d3dDevice != IntPtr.Zero)
                    {
                        binding = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(XrGraphicsBindingD3D11KHR)));
                        var gb = new XrGraphicsBindingD3D11KHR
                        {
                            type = XrType.GRAPHICS_BINDING_D3D11_KHR,
                            device = _d3dDevice,
                        };
                        Marshal.StructureToPtr(gb, binding, false);
                        si.next = binding;
                    }
                    int r = XrApi.xrCreateSession(_instance, ref si, out _session);
                    if (!XrResultCode.Ok(r))
                    {
                        _session = 0;
                        Check(r, "xrCreateSession");
                        return;
                    }
                }
                finally
                {
                    if (binding != IntPtr.Zero) Marshal.FreeHGlobal(binding);
                }
            }
            Log("session created" + (_hasD3D11 ? " (D3D11 bound)" : " (input-only, no graphics binding)"));

            if (!CreateSpaces()) return;
            if (!CreateActions()) return;
            if (_hasHandTracking) CreateHandTrackers();
            if (_hasBodyTracking) CreateBodyTracker();
        }

        /// <summary>XR_FB_body_tracking: hips→spine→chest→head poses for the
        /// FBT pipeline. WiVRn feeds this from a connected SlimeVR server
        /// (its bridge), so the extension being advertised + the system
        /// supporting it + tracker creation all succeeding is the signal that
        /// REAL body data will stream. Otherwise VRBody stays procedural.</summary>
        private void CreateBodyTracker()
        {
            lock (_xrLock)
            {
                IntPtr pCreate = IntPtr.Zero, pLocate = IntPtr.Zero, pDestroy = IntPtr.Zero;
                int r1 = XrApi.xrGetInstanceProcAddr(_instance, "xrCreateBodyTrackerFB", out pCreate);
                int r2 = XrApi.xrGetInstanceProcAddr(_instance, "xrLocateBodyJointsFB", out pLocate);
                int r3 = XrApi.xrGetInstanceProcAddr(_instance, "xrDestroyBodyTrackerFB", out pDestroy);
                if (!XrResultCode.Ok(r1) || !XrResultCode.Ok(r2) || !XrResultCode.Ok(r3) ||
                    pCreate == IntPtr.Zero || pLocate == IntPtr.Zero || pDestroy == IntPtr.Zero)
                {
                    Log("body tracking proc resolution failed: create " + XrResultCode.Name(r1) +
                        ", locate " + XrResultCode.Name(r2) + ", destroy " + XrResultCode.Name(r3));
                    return;
                }
                var create = (XrApi.CreateBodyTrackerFB_t)Marshal.GetDelegateForFunctionPointer(
                    pCreate, typeof(XrApi.CreateBodyTrackerFB_t));
                var locate = (XrApi.LocateBodyJointsFB_t)Marshal.GetDelegateForFunctionPointer(
                    pLocate, typeof(XrApi.LocateBodyJointsFB_t));
                var destroy = (XrApi.DestroyBodyTrackerFB_t)Marshal.GetDelegateForFunctionPointer(
                    pDestroy, typeof(XrApi.DestroyBodyTrackerFB_t));

                if (!_bodySystemSupports)
                {
                    Log("body tracking skipped: system properties report no body-tracking support");
                    return;
                }
                var ci = new XrBodyTrackerCreateInfoFB
                {
                    type = XrType.BODY_TRACKER_CREATE_INFO_FB,
                    bodyJointSet = XrEnums.BODY_JOINT_SET_DEFAULT_FB,   // upper body — no knees/feet exist here
                };
                int rb = create(_session, ref ci, out _bodyTracker);
                if (!XrResultCode.Ok(rb))
                {
                    Log("body tracker creation failed: " + XrResultCode.Name(rb) + " — VRBody stays procedural");
                    _bodyTracker = 0;
                    return;
                }
                _locateBodyJoints = locate;
                _destroyBodyTracker = destroy;
                Log("body tracker created (XR_FB_body_tracking via proc-addr) — full-body pipeline live when SlimeVR streams");
            }
        }

        /// <summary>True when the FB body tracker exists (pose data may still be
        /// idle until a body is being tracked — XrBodyFrame.valid covers that).</summary>
        internal bool BodyTrackingAvailable { get { return _bodyTracker != 0; } }

        private void CreateHandTrackers()
        {
            // Resolve the hand-tracking extension functions through
            // xrGetInstanceProcAddr — the Khronos loader does not export
            // extension symbols (56 core exports only; a DllImport here threw
            // EntryPointNotFoundException and silently killed the fallback).
            lock (_xrLock)
            {
                IntPtr pCreate = IntPtr.Zero, pLocate = IntPtr.Zero, pDestroy = IntPtr.Zero;
                int r1 = XrApi.xrGetInstanceProcAddr(_instance, "xrCreateHandTrackerEXT", out pCreate);
                int r2 = XrApi.xrGetInstanceProcAddr(_instance, "xrLocateHandJointsEXT", out pLocate);
                int r3 = XrApi.xrGetInstanceProcAddr(_instance, "xrDestroyHandTrackerEXT", out pDestroy);
                if (!XrResultCode.Ok(r1) || !XrResultCode.Ok(r2) || !XrResultCode.Ok(r3) ||
                    pCreate == IntPtr.Zero || pLocate == IntPtr.Zero || pDestroy == IntPtr.Zero)
                {
                    Log("hand tracking proc resolution failed: create " + XrResultCode.Name(r1) +
                        ", locate " + XrResultCode.Name(r2) + ", destroy " + XrResultCode.Name(r3));
                    return;
                }
                var create = (XrApi.CreateHandTrackerEXT_t)Marshal.GetDelegateForFunctionPointer(
                    pCreate, typeof(XrApi.CreateHandTrackerEXT_t));
                var locate = (XrApi.LocateHandJointsEXT_t)Marshal.GetDelegateForFunctionPointer(
                    pLocate, typeof(XrApi.LocateHandJointsEXT_t));
                var destroy = (XrApi.DestroyHandTrackerEXT_t)Marshal.GetDelegateForFunctionPointer(
                    pDestroy, typeof(XrApi.DestroyHandTrackerEXT_t));

                var ciL = new XrHandTrackerCreateInfoEXT
                {
                    type = XrType.HAND_TRACKER_CREATE_INFO_EXT,
                    hand = XrEnums.HAND_LEFT_EXT,
                    handJointSet = 0,
                };
                var ciR = new XrHandTrackerCreateInfoEXT
                {
                    type = XrType.HAND_TRACKER_CREATE_INFO_EXT,
                    hand = XrEnums.HAND_RIGHT_EXT,
                    handJointSet = 0,
                };
                int rl = create(_session, ref ciL, out _handTrackerL);
                int rr = create(_session, ref ciR, out _handTrackerR);
                if (!XrResultCode.Ok(rl) || !XrResultCode.Ok(rr))
                {
                    Log("hand tracker creation failed: L " + XrResultCode.Name(rl) + ", R " + XrResultCode.Name(rr));
                    _handTrackerL = 0; _handTrackerR = 0;
                    return;
                }
                _locateHandJoints = locate;
                _destroyHandTracker = destroy;
                Log("hand trackers created (XR_EXT_hand_tracking via proc-addr)");
            }
        }

        private bool CreateSpaces()
        {
            // Application space: LOCAL_FLOOR preferred, then STAGE, then LOCAL.
            // LOCAL_FLOOR is an extension enum value (1000426000) — tried first via raw value.
            uint localFloor = 1000426000;
            var attempts = new uint[] { localFloor, XrEnums.REFERENCE_SPACE_STAGE, XrEnums.REFERENCE_SPACE_LOCAL };
            foreach (uint st in attempts)
            {
                var rci = new XrReferenceSpaceCreateInfo
                {
                    type = XrType.REFERENCE_SPACE_CREATE_INFO,
                    referenceSpaceType = st,
                    poseInReferenceSpace = IdentityPose(),
                };
                ulong sp;
                lock (_xrLock)
                {
                    if (XrResultCode.Ok(XrApi.xrCreateReferenceSpace(_session, ref rci, out sp)))
                    {
                        _appSpace = sp; _appSpaceType = st;
                        goto viewSpace;
                    }
                }
            }
            Check(-1, "no supported reference space (STAGE/LOCAL)");
            return false;

        viewSpace:
            var vci = new XrReferenceSpaceCreateInfo
            {
                type = XrType.REFERENCE_SPACE_CREATE_INFO,
                referenceSpaceType = XrEnums.REFERENCE_SPACE_VIEW,
                poseInReferenceSpace = IdentityPose(),
            };
            lock (_xrLock)
            {
                if (!XrResultCode.Ok(XrApi.xrCreateReferenceSpace(_session, ref vci, out _viewSpace)))
                {
                    Check(-1, "VIEW reference space unsupported");
                    return false;
                }
            }
            Log("reference spaces ok (app type " + _appSpaceType + ")");
            return true;
        }

        private static string SessionStateName(uint s)
        {
            switch (s)
            {
                case XrEnums.SESSION_STATE_IDLE: return "IDLE";
                case XrEnums.SESSION_STATE_READY: return "READY";
                case XrEnums.SESSION_STATE_SYNCHRONIZED: return "SYNCHRONIZED";
                case XrEnums.SESSION_STATE_VISIBLE: return "VISIBLE";
                case XrEnums.SESSION_STATE_FOCUSED: return "FOCUSED";
                case XrEnums.SESSION_STATE_STOPPING: return "STOPPING";
                case XrEnums.SESSION_STATE_LOSS_PENDING: return "LOSS_PENDING";
                case XrEnums.SESSION_STATE_EXITING: return "EXITING";
                default: return "state " + s;
            }
        }

        private static XrPosef IdentityPose()
        {
            return new XrPosef
            {
                orientation = new XrQuaternionf { x = 0, y = 0, z = 0, w = 1 },
                position = new XrVector3f { x = 0, y = 0, z = 0 },
            };
        }

        private ulong Path(string s)
        {
            ulong p;
            lock (_xrLock)
            {
                if (!XrResultCode.Ok(XrApi.xrStringToPath(_instance, s, out p))) return 0;
            }
            return p;
        }

        private bool CreateActions()
        {
            lock (_xrLock)
            {
                var asc = new XrActionSetCreateInfo { type = XrType.ACTION_SET_CREATE_INFO, priority = 0 };
                fixed (byte* p = asc.actionSetName) XrStr.Copy(p, "gameplay", 64);
                fixed (byte* p2 = asc.localizedActionSetName) XrStr.Copy(p2, "VRKoboldKare Gameplay", 128);
                if (!XrResultCode.Ok(XrApi.xrCreateActionSet(_instance, ref asc, out _actionSet)))
                {
                    Check(-1, "xrCreateActionSet"); return false;
                }
            }

            _pathHandL = Path("/user/hand/left");
            _pathHandR = Path("/user/hand/right");
            _subactionPaths[0] = _pathHandL;
            _subactionPaths[1] = _pathHandR;

            IntPtr subPtr = Marshal.AllocHGlobal(2 * sizeof(ulong));
            try
            {
                Marshal.WriteInt64(subPtr, 0, (long)_pathHandL);
                Marshal.WriteInt64(subPtr, 8, (long)_pathHandR);

                AddAction("grip_pose", XrEnums.ACTION_TYPE_POSE_INPUT, subPtr);
                AddAction("aim_pose", XrEnums.ACTION_TYPE_POSE_INPUT, subPtr);
                AddAction("trigger", XrEnums.ACTION_TYPE_FLOAT_INPUT, subPtr);
                AddAction("squeeze", XrEnums.ACTION_TYPE_FLOAT_INPUT, subPtr);
                AddAction("stick", XrEnums.ACTION_TYPE_VECTOR2F_INPUT, subPtr);
                AddAction("stick_click", XrEnums.ACTION_TYPE_BOOLEAN_INPUT, subPtr);
                AddAction("button_a", XrEnums.ACTION_TYPE_BOOLEAN_INPUT, subPtr);
                AddAction("button_b", XrEnums.ACTION_TYPE_BOOLEAN_INPUT, subPtr);
                AddAction("button_x", XrEnums.ACTION_TYPE_BOOLEAN_INPUT, subPtr);
                AddAction("button_y", XrEnums.ACTION_TYPE_BOOLEAN_INPUT, subPtr);
                AddAction("menu", XrEnums.ACTION_TYPE_BOOLEAN_INPUT, subPtr);
                AddAction("haptic", XrEnums.ACTION_TYPE_VIBRATION_OUTPUT, subPtr);
            }
            finally { Marshal.FreeHGlobal(subPtr); }

            SuggestBindings();
            CreateActionSpaces();

            lock (_xrLock)
            {
                IntPtr setPtr = Marshal.AllocHGlobal(sizeof(ulong));
                try
                {
                    Marshal.WriteInt64(setPtr, 0, (long)_actionSet);
                    var attach = new XrSessionActionSetsAttachInfo
                    {
                        type = XrType.SESSION_ACTION_SETS_ATTACH_INFO,
                        countActionSets = 1,
                        actionSets = setPtr,
                    };
                    if (!XrResultCode.Ok(XrApi.xrAttachSessionActionSets(_session, ref attach)))
                    {
                        Check(-1, "xrAttachSessionActionSets"); return false;
                    }
                }
                finally { Marshal.FreeHGlobal(setPtr); }
            }
            Log("actions created and attached");
            LogCurrentInteractionProfile();
            return true;
        }

        private void AddAction(string name, uint actionType, IntPtr subactionPaths)
        {
            var aci = new XrActionCreateInfo { type = XrType.ACTION_CREATE_INFO, actionType = actionType };
            fixed (byte* p = aci.actionName) XrStr.Copy(p, name, 64);
            fixed (byte* p2 = aci.localizedActionName) XrStr.Copy(p2, name, 128);
            aci.countSubactionPaths = 2;
            aci.subactionPaths = subactionPaths;
            ulong handle;
            lock (_xrLock)
            {
                if (XrResultCode.Ok(XrApi.xrCreateAction(_actionSet, ref aci, out handle)))
                    _actions[name] = handle;
                else
                    Check(-1, "xrCreateAction " + name);
            }
        }

        /// <summary>Suggest the standard bindings for Quest-style touch
        /// controllers plus the KHR simple fallback.</summary>
        private void SuggestBindings()
        {
            // (action name, hand 0=left 1=right, component path suffix)
            var touchLeft = new[]
            {
                Tuple.Create("button_x", "/user/hand/left/input/x/click"),
                Tuple.Create("button_y", "/user/hand/left/input/y/click"),
                Tuple.Create("menu", "/user/hand/left/input/menu/click"),
                Tuple.Create("trigger", "/user/hand/left/input/trigger/value"),
                Tuple.Create("squeeze", "/user/hand/left/input/squeeze/value"),
                Tuple.Create("stick", "/user/hand/left/input/thumbstick"),
                Tuple.Create("stick_click", "/user/hand/left/input/thumbstick/click"),
                Tuple.Create("grip_pose", "/user/hand/left/input/grip/pose"),
                Tuple.Create("aim_pose", "/user/hand/left/input/aim/pose"),
            };
            var touchRight = new[]
            {
                Tuple.Create("button_a", "/user/hand/right/input/a/click"),
                Tuple.Create("button_b", "/user/hand/right/input/b/click"),
                Tuple.Create("trigger", "/user/hand/right/input/trigger/value"),
                Tuple.Create("squeeze", "/user/hand/right/input/squeeze/value"),
                Tuple.Create("stick", "/user/hand/right/input/thumbstick"),
                Tuple.Create("stick_click", "/user/hand/right/input/thumbstick/click"),
                Tuple.Create("grip_pose", "/user/hand/right/input/grip/pose"),
                Tuple.Create("aim_pose", "/user/hand/right/input/aim/pose"),
            };

            // Widest-coverage suggestion set: Monado's binding layer attaches
            // whatever app-suggested profile it can satisfy with the active
            // devices' inputs — more profiles = more chances one attaches.
            // (index_controller has the broadest Monado binding coverage and
            // Touch Plus exposes trigger/squeeze/thumbstick equivalents.)
            string[] touchProfiles =
            {
                "/interaction_profiles/meta/touch_plus_controller.json",
                "/interaction_profiles/oculus/touch_controller.json",
                "/interaction_profiles/meta/touch_controller_pro.json",
                "/interaction_profiles/valve/index_controller.json",
                "/interaction_profiles/htc/vive_controller.json",
                "/interaction_profiles/bytedance/pico4_controller.json",
            };
            foreach (string profile in touchProfiles)
                SuggestProfile(profile, touchLeft, touchRight);

            // KHR simple fallback: select/menu only.
            SuggestProfile("/interaction_profiles/khr/simple_controller.json",
                new[]
                {
                    Tuple.Create("menu", "/user/hand/left/input/menu/click"),
                    Tuple.Create("trigger", "/user/hand/left/input/select/click"),
                    Tuple.Create("grip_pose", "/user/hand/left/input/grip/pose"),
                    Tuple.Create("aim_pose", "/user/hand/left/input/aim/pose"),
                },
                new[]
                {
                    Tuple.Create("button_a", "/user/hand/right/input/select/click"),
                    Tuple.Create("trigger", "/user/hand/right/input/select/click"),
                    Tuple.Create("grip_pose", "/user/hand/right/input/grip/pose"),
                    Tuple.Create("aim_pose", "/user/hand/right/input/aim/pose"),
                });
        }

        private void SuggestProfile(string profile, Tuple<string, string>[] left, Tuple<string, string>[] right)
        {
            var bindings = new List<XrActionSuggestedBinding>();
            foreach (var t in left) AddBinding(bindings, t.Item1, t.Item2);
            foreach (var t in right) AddBinding(bindings, t.Item1, t.Item2);
            AddBinding(bindings, "haptic", "/user/hand/left/output/haptic");
            AddBinding(bindings, "haptic", "/user/hand/right/output/haptic");

            ulong profilePath = Path(profile);
            if (profilePath == 0) return;

            IntPtr arr = Marshal.AllocHGlobal(bindings.Count * Marshal.SizeOf(typeof(XrActionSuggestedBinding)));
            try
            {
                for (int i = 0; i < bindings.Count; i++)
                    Marshal.StructureToPtr(bindings[i], (IntPtr)((byte*)arr + i * Marshal.SizeOf(typeof(XrActionSuggestedBinding))), false);
                var sug = new XrInteractionProfileSuggestedBinding
                {
                    type = XrType.INTERACTION_PROFILE_SUGGESTED_BINDING,
                    interactionProfile = profilePath,
                    countSuggestedBindings = (uint)bindings.Count,
                    suggestedBindings = arr,
                };
                lock (_xrLock)
                {
                    int r = XrApi.xrSuggestInteractionProfileBindings(_instance, ref sug);
                    if (r == XrResultCode.XR_ERROR_PATH_UNSUPPORTED || r == XrResultCode.XR_ERROR_ACTION_TYPE_MISMATCH)
                    {
                        // Profile known but some components absent — fine.
                    }
                    else if (!XrResultCode.Ok(r))
                    {
                        Check(r, "suggest " + profile);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(arr); }
        }

        private void AddBinding(List<XrActionSuggestedBinding> list, string action, string path)
        {
            ulong a, p;
            if (!_actions.TryGetValue(action, out a)) return;
            p = Path(path);
            if (p == 0) return;
            list.Add(new XrActionSuggestedBinding { action = a, binding = p });
        }

        private void CreateActionSpaces()
        {
            _spaceGripL = MakeActionSpace("grip_pose", _pathHandL);
            _spaceGripR = MakeActionSpace("grip_pose", _pathHandR);
            _spaceAimL = MakeActionSpace("aim_pose", _pathHandL);
            _spaceAimR = MakeActionSpace("aim_pose", _pathHandR);
        }

        private ulong MakeActionSpace(string action, ulong subaction)
        {
            ulong a;
            if (!_actions.TryGetValue(action, out a)) return 0;
            var ci = new XrActionSpaceCreateInfo
            {
                type = XrType.ACTION_SPACE_CREATE_INFO,
                action = a,
                subactionPath = subaction,
                poseInActionSpace = IdentityPose(),
            };
            ulong sp;
            lock (_xrLock)
            {
                if (!XrResultCode.Ok(XrApi.xrCreateActionSpace(_session, ref ci, out sp)))
                {
                    Check(-1, "xrCreateActionSpace " + action);
                    return 0;
                }
            }
            return sp;
        }

        private void LogCurrentInteractionProfile()
        {
            try
            {
                var st = new XrInteractionProfileState { type = XrType.INTERACTION_PROFILE_STATE };
                lock (_xrLock)
                {
                    if (!XrResultCode.Ok(XrApi.xrGetCurrentInteractionProfile(_session, _pathHandR, ref st)))
                    {
                        Log("interaction profile (right): query failed");
                        return;
                    }
                    if (st.interactionProfile != 0)
                    {
                        var sb = new StringBuilder(256);
                        uint n;
                        if (XrResultCode.Ok(XrApi.xrPathToString(_instance, st.interactionProfile, 256, out n, sb)))
                            Log("interaction profile (right): " + sb.ToString());
                    }
                    else
                    {
                        // Explicit zero: the runtime has attached NO profile to
                        // the hand — controllers not streaming to the runtime,
                        // or no suggested profile can be satisfied.
                        Log("interaction profile (right): NONE attached");
                    }
                }
            }
            catch { }
        }

        // ---------- main thread: graphics device + input polling ----------

        /// <summary>
        /// Grab the D3D11 device (main thread). Texture.GetNativeTexturePtr is
        /// stripped from this Unity build, so we go through a native mesh
        /// vertex buffer instead — an ID3D11Buffer QIs to the device the same
        /// way a texture would.
        /// </summary>
        internal void ProvideGraphicsDevice()
        {
            if (_d3dDevice != IntPtr.Zero || _instance == 0 || !_hasD3D11) return;
            if (_graphicsApi == null)
            {
                try { _graphicsApi = SystemInfo.graphicsDeviceType.ToString(); Log("Unity graphics API: " + _graphicsApi); }
                catch { _graphicsApi = "unknown"; }
            }
            if (_graphicsApi != "Direct3D11")
            {
                if (_loggedGraphicsApiMismatch) return;
                _loggedGraphicsApiMismatch = true;
                Log("graphics API is " + _graphicsApi + ", expected Direct3D11 — cannot bind D3D11 (run the game under Proton with DXVK; input-only OpenXR still works).");
                return;
            }
            try
            {
                var mesh = new Mesh();
                mesh.vertices = new Vector3[] { Vector3.zero, Vector3.one, Vector3.right };
                mesh.UploadMeshData(false);
                IntPtr native = mesh.GetNativeVertexBufferPtr(0);
                IntPtr dev = ComInterop.GetD3D11DeviceFromResource(native);
                UnityEngine.Object.Destroy(mesh);
                if (dev != IntPtr.Zero)
                {
                    _d3dDevice = dev;
                    Log("acquired ID3D11Device from Unity renderer");
                }
            }
            catch (Exception e) { Log("device acquisition failed: " + e.Message); }
        }

        private string _graphicsApi;
        private bool _loggedGraphicsApiMismatch;

        /// <summary>True when the OpenXR session is in a rendering-capable state.</summary>
        internal bool SessionRendering
        {
            get
            {
                return _session != 0 && _sessionBegun &&
                    (_sessionState == XrEnums.SESSION_STATE_FOCUSED ||
                     _sessionState == XrEnums.SESSION_STATE_VISIBLE ||
                     _sessionState == XrEnums.SESSION_STATE_SYNCHRONIZED);
            }
        }

        internal string RuntimeName { get { return _runtimeName; } }

        /// <summary>Renderer calls this to take over the frame loop from the idle pump.</summary>
        internal bool ClaimFrames(int timeoutMs)
        {
            _framesOwnedByMain = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_threadInFrame && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(2);
            return _framesOwnedByMain;
        }

        internal void ReleaseFrames() { _framesOwnedByMain = false; }

        public void Poll(out XrFrame frame)
        {
            frame = new XrFrame { backend = Name };
            ProvideGraphicsDevice();   // also detects the graphics API on first call
            if (_session == 0 || !_sessionBegun) { lock (_frameLock) { _latest = frame; } return; }
            if (_sessionState != XrEnums.SESSION_STATE_FOCUSED &&
                _sessionState != XrEnums.SESSION_STATE_VISIBLE &&
                _sessionState != XrEnums.SESSION_STATE_SYNCHRONIZED)
            {
                lock (_frameLock) { _latest = frame; }
                return;
            }

            long t = _lastDisplayTime;

            // Sync actions (rate-limited to game update).
            lock (_xrLock)
            {
                IntPtr active = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(XrActiveActionSet)));
                try
                {
                    Marshal.WriteInt64(active, 0, (long)_actionSet);
                    Marshal.WriteInt64(active, 8, 0);
                    var sync = new XrActionsSyncInfo
                    {
                        type = XrType.ACTIONS_SYNC_INFO,
                        countActiveActionSets = 1,
                        activeActionSets = active,
                    };
                    int sr = XrApi.xrSyncActions(_session, ref sync);
                    if (!XrResultCode.Ok(sr) && _syncFailLog < 10)
                    {
                        _syncFailLog++;
                        Log("xrSyncActions -> " + XrResultCode.Name(sr));
                    }
                }
                finally { Marshal.FreeHGlobal(active); }
            }

            var f = new XrFrame { valid = true, backend = Name, packet = ++_packet };

            // Head: VIEW space located in the app space.
            var headLoc = new XrSpaceLocation { type = XrType.SPACE_LOCATION };
            lock (_xrLock)
            {
                if (XrResultCode.Ok(XrApi.xrLocateSpace(_viewSpace, _appSpace, t, ref headLoc)) &&
                    (headLoc.locationFlags & (XrEnums.SPACE_LOCATION_ORIENTATION_VALID_BIT |
                                              XrEnums.SPACE_LOCATION_POSITION_VALID_BIT)) ==
                       (XrEnums.SPACE_LOCATION_ORIENTATION_VALID_BIT | XrEnums.SPACE_LOCATION_POSITION_VALID_BIT))
                {
                    f.headTracked = true;
                    f.headPos = ToUnityPos(headLoc.pose.position);
                    f.headRot = ToUnityRot(headLoc.pose.orientation);
                }
            }

            f.left = PollHand(true, t);
            f.right = PollHand(false, t);
            f.body = LocateBody(t);

            // Input probes: once at 2s, then every 10s for a minute — WiVRn's
            // interaction profile attach is known to lag several seconds
            // (issue #408), so early probes can show all-zeros that later
            // come alive. Profile state logged alongside.
            if (_packet == 120 || (_packet % 600 == 0 && _packet <= 3600))
            {
                Log(string.Format(
                    "input probe @p{0}: L(t={1:0.00} g={2:0.00} s=({3:0.00},{4:0.00}) tracked={5}) R(t={6:0.00} g={7:0.00} s=({8:0.00},{9:0.00}) tracked={10})",
                    _packet, f.left.trigger, f.left.grip, f.left.stick.x, f.left.stick.y, f.left.tracked,
                    f.right.trigger, f.right.grip, f.right.stick.x, f.right.stick.y, f.right.tracked));
                LogCurrentInteractionProfile();
            }

            lock (_frameLock) { _latest = f; }
            frame = f;
        }

        private XrHand PollHand(bool left, long t)
        {
            var h = new XrHand();
            ulong sub = left ? _pathHandL : _pathHandR;
            ulong grip = left ? _spaceGripL : _spaceGripR;

            var loc = new XrSpaceLocation { type = XrType.SPACE_LOCATION };
            lock (_xrLock)
            {
                if (grip != 0 &&
                    XrResultCode.Ok(XrApi.xrLocateSpace(grip, _appSpace, t, ref loc)) &&
                    (loc.locationFlags & XrEnums.SPACE_LOCATION_ORIENTATION_VALID_BIT) != 0)
                {
                    h.tracked = true;
                    h.position = ToUnityPos(loc.pose.position);
                    h.rotation = ToUnityRot(loc.pose.orientation);
                }

                h.trigger = ReadFloat("trigger", sub);
                h.grip = ReadFloat("squeeze", sub);
                var stick = ReadVec2("stick", sub);
                h.stick = new Vector2(stick.x, stick.y);
                if (left)
                {
                    h.x = ReadBool("button_x", sub);
                    h.y = ReadBool("button_y", sub);
                    h.menu = ReadBool("menu", sub);
                }
                else
                {
                    h.a = ReadBool("button_a", sub);
                    h.b = ReadBool("button_b", sub);
                    h.menu = false; // Quest right system button is reserved
                }

                // Hand-tracking fallback: no controller pose, but the runtime
                // exposes XR_EXT_hand_tracking — drive pose + gestures from
                // hand joints (WiVRn: enable "Hand tracking" in the Quest client).
                if (!h.tracked && _hasHandTracking)
                    ApplyHandJoints(left, t, ref h);
            }
            return h;
        }

        private unsafe void ApplyHandJoints(bool left, long t, ref XrHand h)
        {
            ulong tracker = left ? _handTrackerL : _handTrackerR;
            if (tracker == 0) return;

            XrHandJointLocationEXT* joints = stackalloc XrHandJointLocationEXT[(int)XrEnums.HAND_JOINT_COUNT_EXT];
            var li = new XrHandJointsLocateInfoEXT
            {
                type = XrType.HAND_JOINTS_LOCATE_INFO_EXT,
                baseSpace = _appSpace,
                time = t,
            };
            var locs = new XrHandJointLocationsEXT
            {
                type = XrType.HAND_JOINT_LOCATIONS_EXT,
                jointCount = XrEnums.HAND_JOINT_COUNT_EXT,
                joints = (IntPtr)joints,
            };
            if (_locateHandJoints == null ||
                !XrResultCode.Ok(_locateHandJoints(tracker, ref li, ref locs))) return;

            var palm = joints[(int)XrEnums.HAND_JOINT_PALM_EXT];
            uint want = XrEnums.SPACE_LOCATION_ORIENTATION_VALID_BIT | XrEnums.SPACE_LOCATION_POSITION_VALID_BIT;
            if ((palm.locationFlags & want) != want) return;

            h.tracked = true;
            h.position = ToUnityPos(palm.pose.position);
            h.rotation = ToUnityRot(palm.pose.orientation);

            var thumbTip = joints[(int)XrEnums.HAND_JOINT_THUMB_TIP_EXT];
            var indexTip = joints[(int)XrEnums.HAND_JOINT_INDEX_TIP_EXT];
            var middleTip = joints[(int)XrEnums.HAND_JOINT_MIDDLE_TIP_EXT];
            uint posValid = XrEnums.SPACE_LOCATION_POSITION_VALID_BIT;
            if ((thumbTip.locationFlags & posValid) == 0 ||
                (indexTip.locationFlags & posValid) == 0 ||
                (middleTip.locationFlags & posValid) == 0) return;

            // Gestures with hysteresis: pinch = trigger (teleport/use), fist
            // (middle fingertip near palm) = grip (grab).
            float pinch = Distance(thumbTip.pose.position, indexTip.pose.position);
            float grab = Distance(middleTip.pose.position, palm.pose.position);
            float pinchState = left ? _pinchL : _pinchR;
            if (pinchState == 0 && pinch < 0.030f) pinchState = 1;
            else if (pinchState == 1 && pinch > 0.048f) pinchState = 0;
            float gripState = left ? _gripL : _gripR;
            if (gripState == 0 && grab < 0.060f) gripState = 1;
            else if (gripState == 1 && grab > 0.090f) gripState = 0;
            if (left) { _pinchL = pinchState; _gripL = gripState; }
            else { _pinchR = pinchState; _gripR = gripState; }

            h.trigger = pinchState;
            h.grip = gripState;
        }

        private static float Distance(XrVector3f a, XrVector3f b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // ---------- XR_FB_body_tracking poll ----------
        // Joints locate in APP space (floor-origin, like the head/hand spaces)
        // at the frame's predicted display time; the same Z-flip conversions
        // apply. VRBody maps them onto the kobold post-animation.

        private bool _bodyIdleLogged;

        private XrBodyFrame LocateBody(long t)
        {
            var bf = new XrBodyFrame();
            if (_bodyTracker == 0 || _locateBodyJoints == null) return bf;

            unsafe
            {
                XrBodyJointLocationFB* joints = stackalloc XrBodyJointLocationFB[(int)XrEnums.BODY_JOINT_COUNT_FB];
                var li = new XrBodyJointsLocateInfoFB
                {
                    type = XrType.BODY_JOINTS_LOCATE_INFO_FB,
                    baseSpace = _appSpace,
                    time = t,
                };
                var locs = new XrBodyJointLocationsFB
                {
                    type = XrType.BODY_JOINT_LOCATIONS_FB,
                    jointCount = XrEnums.BODY_JOINT_COUNT_FB,
                    jointLocations = (IntPtr)joints,
                };
                if (!XrResultCode.Ok(_locateBodyJoints(_bodyTracker, ref li, ref locs))) return bf;

                if (locs.isActive == 0)
                {
                    if (!_bodyIdleLogged)
                    {
                        _bodyIdleLogged = true;
                        Log("body tracker idle (no body being tracked — connect SlimeVR to the WiVRn server)");
                    }
                    return bf;
                }
                _bodyIdleLogged = false;

                bf.valid = true;
                bf.confidence = locs.confidence;
                FillJoint(ref bf.hips, joints, XrEnums.BODY_JOINT_HIPS_FB);
                FillJoint(ref bf.spineLower, joints, XrEnums.BODY_JOINT_SPINE_LOWER_FB);
                FillJoint(ref bf.spineMiddle, joints, XrEnums.BODY_JOINT_SPINE_MIDDLE_FB);
                FillJoint(ref bf.spineUpper, joints, XrEnums.BODY_JOINT_SPINE_UPPER_FB);
                FillJoint(ref bf.chest, joints, XrEnums.BODY_JOINT_CHEST_FB);
                FillJoint(ref bf.head, joints, XrEnums.BODY_JOINT_HEAD_FB);
            }
            return bf;
        }

        private static unsafe void FillJoint(ref XrBodyJoint j, XrBodyJointLocationFB* joints, int idx)
        {
            var loc = joints[idx];
            uint want = XrEnums.SPACE_LOCATION_ORIENTATION_VALID_BIT | XrEnums.SPACE_LOCATION_POSITION_VALID_BIT;
            if ((loc.locationFlags & want) != want) return;
            j.tracked = true;
            j.position = ToUnityPos(loc.pose.position);
            j.rotation = ToUnityRot(loc.pose.orientation);
        }

        private XrActionStateGetInfo GetInfo(string action, ulong subaction)
        {
            ulong a;
            _actions.TryGetValue(action, out a);
            return new XrActionStateGetInfo { type = XrType.ACTION_STATE_GET_INFO, action = a, subactionPath = subaction };
        }

        private float ReadFloat(string action, ulong sub)
        {
            var gi = GetInfo(action, sub);
            var st = new XrActionStateFloat { type = XrType.ACTION_STATE_FLOAT };
            if (!XrResultCode.Ok(XrApi.xrGetActionStateFloat(_session, ref gi, ref st)) || st.isActive == 0) return 0f;
            return st.currentState;
        }

        private XrVector2f ReadVec2(string action, ulong sub)
        {
            var gi = GetInfo(action, sub);
            var st = new XrActionStateVector2f { type = XrType.ACTION_STATE_VECTOR2F };
            if (!XrResultCode.Ok(XrApi.xrGetActionStateVector2f(_session, ref gi, ref st)) || st.isActive == 0) return default(XrVector2f);
            return st.currentState;
        }

        private bool ReadBool(string action, ulong sub)
        {
            var gi = GetInfo(action, sub);
            var st = new XrActionStateBoolean { type = XrType.ACTION_STATE_BOOLEAN };
            if (!XrResultCode.Ok(XrApi.xrGetActionStateBoolean(_session, ref gi, ref st)) || st.isActive == 0) return false;
            return st.currentState != 0;
        }

        public void Haptic(bool left, float amplitude, int durationMs)
        {
            ulong a;
            if (!_actions.TryGetValue("haptic", out a) || _session == 0) return;
            var hi = new XrHapticActionInfo
            {
                type = XrType.HAPTIC_ACTION_INFO,
                action = a,
                subactionPath = left ? _pathHandL : _pathHandR,
            };
            var vib = new XrHapticVibration
            {
                type = XrType.HAPTIC_VIBRATION,
                duration = durationMs * 1000000L,
                frequency = 0f,
                amplitude = Mathf.Clamp01(amplitude),
            };
            lock (_xrLock) { XrApi.xrApplyHapticFeedback(_session, ref hi, ref vib); }
        }

        // ---------- renderer-facing frame API (main thread) ----------

        internal bool WaitAndBeginFrame(out XrFrameState fs)
        {
            fs = new XrFrameState { type = XrType.FRAME_STATE };
            if (!SessionRendering) return false;
            lock (_xrLock)
            {
                var wait = new XrFrameWaitInfo { type = XrType.FRAME_WAIT_INFO };
                if (!XrResultCode.Ok(XrApi.xrWaitFrame(_session, ref wait, ref fs))) return false;
                _lastDisplayTime = fs.predictedDisplayTime;
                var begin = new XrFrameBeginInfo { type = XrType.FRAME_BEGIN_INFO };
                return XrResultCode.Ok(XrApi.xrBeginFrame(_session, ref begin));
            }
        }

        internal bool LocateViews(long time, XrView[] views)
        {
            if (_session == 0) return false;
            var vs = new XrViewState { type = XrType.VIEW_STATE };
            var li = new XrViewLocateInfo
            {
                type = XrType.VIEW_LOCATE_INFO,
                viewConfigurationType = XrEnums.VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
                displayTime = time,
                space = _appSpace,
            };
            uint n;
            lock (_xrLock)
            {
                return XrResultCode.Ok(XrApi.xrLocateViews(_session, ref li, ref vs, (uint)views.Length, out n, views)) && n >= 1;
            }
        }

        private int _submitFailLog;
        internal uint FramesSubmitted { get; private set; }
        private System.Diagnostics.Stopwatch _submitWatch = new System.Diagnostics.Stopwatch();
        private uint _lastMilestone;

        internal bool SubmitFrame(long displayTime, IntPtr layerPtr, uint layerCount)
        {
            if (_session == 0) return false;
            var end = new XrFrameEndInfo
            {
                type = XrType.FRAME_END_INFO,
                displayTime = displayTime,
                environmentBlendMode = XrEnums.ENVIRONMENT_BLEND_MODE_OPAQUE,
                layerCount = layerCount,
                layers = layerPtr,
            };
            int r;
            lock (_xrLock) { r = XrApi.xrEndFrame(_session, ref end); }
            if (!XrResultCode.Ok(r))
            {
                if (_submitFailLog < 10)
                {
                    _submitFailLog++;
                    Log("xrEndFrame -> " + XrResultCode.Name(r) + " (layerCount " + layerCount + ")");
                }
                return false;
            }
            FramesSubmitted++;
            if (FramesSubmitted == 1) _submitWatch.Restart();
            if (FramesSubmitted == 300 || FramesSubmitted == 1800 || FramesSubmitted == 3600)
            {
                double avgMs = _submitWatch.Elapsed.TotalMilliseconds /
                               System.Math.Max(1, FramesSubmitted - _lastMilestone);
                Log("submitted " + FramesSubmitted + " frames total (avg " +
                    avgMs.ToString("0.0") + " ms/frame over the last stretch)");
                if (avgMs > 25.0)
                    Log("frame time over compositor budget — the headset may show black/nothing. " +
                        "Known causes: stale graphics-stack state (fix: reboot), heavy EyeScale " +
                        "(lower it), or a driver/runtime update mid-session.");
                _lastMilestone = FramesSubmitted;
                _submitWatch.Restart();
            }
            return true;
        }

        internal XrViewConfigurationView[] GetViewConfigurationViews()
        {
            if (_instance == 0 || _systemId == 0) return null;
            uint n;
            lock (_xrLock)
            {
                int r = XrApi.xrEnumerateViewConfigurationViews(_instance, _systemId,
                    XrEnums.VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0, out n, null);
                if (!XrResultCode.Ok(r) || n == 0) return null;
                var views = new XrViewConfigurationView[n];
                for (int i = 0; i < n; i++) views[i].type = XrType.VIEW_CONFIGURATION_VIEW;
                r = XrApi.xrEnumerateViewConfigurationViews(_instance, _systemId,
                    XrEnums.VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, n, out n, views);
                return XrResultCode.Ok(r) ? views : null;
            }
        }

        internal long[] GetSwapchainFormats()
        {
            if (_session == 0) return null;
            lock (_xrLock)
            {
                uint n;
                int r = XrApi.xrEnumerateSwapchainFormats(_session, 0, out n, null);
                if (!XrResultCode.Ok(r) || n == 0) return null;
                var fmts = new long[n];
                fixed (long* p = fmts)
                {
                    r = XrApi.xrEnumerateSwapchainFormats(_session, n, out n, p);
                }
                return XrResultCode.Ok(r) ? fmts : null;
            }
        }

        internal ulong CreateSwapchain(long format, uint w, uint h)
        {
            if (_session == 0) return 0;
            var ci = new XrSwapchainCreateInfo
            {
                type = XrType.SWAPCHAIN_CREATE_INFO,
                usageFlags = XrEnums.SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XrEnums.SWAPCHAIN_USAGE_SAMPLED_BIT,
                format = format,
                sampleCount = 1,
                width = w,
                height = h,
                faceCount = 1,
                arraySize = 1,
                mipCount = 1,
            };
            ulong sw;
            lock (_xrLock)
            {
                if (!XrResultCode.Ok(XrApi.xrCreateSwapchain(_session, ref ci, out sw))) return 0;
            }
            return sw;
        }

        internal XrSwapchainImageD3D11KHR[] GetSwapchainImages(ulong swapchain, int expected)
        {
            var images = new XrSwapchainImageD3D11KHR[expected];
            for (int i = 0; i < expected; i++) images[i].type = XrType.SWAPCHAIN_IMAGE_D3D11_KHR;
            uint n;
            lock (_xrLock)
            {
                fixed (XrSwapchainImageD3D11KHR* p = images)
                {
                    if (!XrResultCode.Ok(XrApi.xrEnumerateSwapchainImages(swapchain, (uint)expected, out n, p))) return null;
                }
            }
            return images;
        }

        private int _acquireFailLog;
        private int _syncFailLog;
        private bool _hasHandTracking;
        private bool _hasBodyTracking;
        private bool _bodySystemSupports;
        private int _syncFlaps;
        private bool _wasDisplayed;
        private ulong _handTrackerL, _handTrackerR;
        private float _pinchL, _pinchR, _gripL, _gripR;   // gesture hysteresis states
        private XrApi.LocateHandJointsEXT_t _locateHandJoints;
        private XrApi.DestroyHandTrackerEXT_t _destroyHandTracker;
        private ulong _bodyTracker;
        private XrApi.LocateBodyJointsFB_t _locateBodyJoints;
        private XrApi.DestroyBodyTrackerFB_t _destroyBodyTracker;

        internal bool AcquireAndWait(ulong swapchain, out uint index)
        {
            index = 0;
            if (_session == 0) return false;
            var ai = new XrSwapchainImageAcquireInfo { type = XrType.SWAPCHAIN_IMAGE_ACQUIRE_INFO };
            lock (_xrLock)
            {
                int r = XrApi.xrAcquireSwapchainImage(swapchain, ref ai, out index);
                if (!XrResultCode.Ok(r))
                {
                    if (_acquireFailLog < 40)
                    {
                        _acquireFailLog++;
                        Log("xrAcquireSwapchainImage -> " + XrResultCode.Name(r));
                    }
                    return false;   // nothing acquired — caller must NOT release
                }
                var wi = new XrSwapchainImageWaitInfo
                {
                    type = XrType.SWAPCHAIN_IMAGE_WAIT_INFO,
                    timeout = 100 * 1000000L,   // 100ms
                };
                r = XrApi.xrWaitSwapchainImage(swapchain, ref wi);
                if (!XrResultCode.Ok(r))
                {
                    if (_acquireFailLog < 40)
                    {
                        _acquireFailLog++;
                        Log("xrWaitSwapchainImage -> " + XrResultCode.Name(r));
                    }
                    ReleaseSwapchainImage(swapchain);   // acquired but unusable — give it back
                    return false;
                }
                return true;
            }
        }

        internal void DestroySwapchain(ulong swapchain)
        {
            if (swapchain == 0 || _session == 0) return;
            lock (_xrLock) { XrApi.xrDestroySwapchain(swapchain); }
        }

        internal void ReleaseSwapchainImage(ulong swapchain)
        {
            var ri = new XrSwapchainImageReleaseInfo { type = XrType.SWAPCHAIN_IMAGE_RELEASE_INFO };
            lock (_xrLock) { XrApi.xrReleaseSwapchainImage(swapchain, ref ri); }
        }

        internal ulong AppSpace { get { return _appSpace; } }

        // ---------- coordinate conversion ----------

        private static Vector3 ToUnityPos(XrVector3f v)
        {
            XrMath.ConvertPosition(v.x, v.y, v.z, out float ox, out float oy, out float oz);
            return new Vector3(ox, oy, oz);
        }
        private static Quaternion ToUnityRot(XrQuaternionf q)
        {
            // RH(-Z fwd) -> LH(+Z fwd): matrix conjugation D·R·D with
            // D=diag(1,1,-1) maps the quaternion to (x, y, -z, -w).
            // The (x, y, -z, w) variant is the INVERSE rotation — it made
            // head yaw and strafe mirrored (2026-11-21 playtest).
            XrMath.ConvertRotation(q.x, q.y, q.z, q.w, out float ox, out float oy, out float oz, out float ow);
            return new Quaternion(ox, oy, oz, ow);
        }

        // ---------- teardown ----------

        private void DestroySpacesAndActions()
        {
            lock (_xrLock)
            {
                Action<ulong> destroy = (h) => { if (h != 0) XrApi.xrDestroySpace(h); };
                destroy(_spaceGripL); destroy(_spaceGripR); destroy(_spaceAimL); destroy(_spaceAimR);
                destroy(_viewSpace); destroy(_appSpace);
                _spaceGripL = _spaceGripR = _spaceAimL = _spaceAimR = 0;
                _viewSpace = _appSpace = 0;
                if (_handTrackerL != 0 && _destroyHandTracker != null) { _destroyHandTracker(_handTrackerL); _handTrackerL = 0; }
                if (_handTrackerR != 0 && _destroyHandTracker != null) { _destroyHandTracker(_handTrackerR); _handTrackerR = 0; }
                if (_bodyTracker != 0 && _destroyBodyTracker != null) { _destroyBodyTracker(_bodyTracker); _bodyTracker = 0; }
                _actions.Clear();
                if (_actionSet != 0) { XrApi.xrDestroyActionSet(_actionSet); _actionSet = 0; }
            }
        }

        public void Shutdown()
        {
            _running = false;
            if (_thread != null)
            {
                try { _thread.Join(1500); } catch { }
                _thread = null;
            }
            lock (_xrLock)
            {
                DestroySpacesAndActions();
                if (_session != 0) { XrApi.xrDestroySession(_session); _session = 0; }
                if (_instance != 0) { XrApi.xrDestroyInstance(_instance); _instance = 0; }
            }
        }
    }

    /// <summary>
    /// Development fallback: emulates VR state from a synthetic head (mouse
    /// yaw/pitch) anchored to the player's body. Lets you test and PLAY the
    /// whole VR pipeline on a monitor without a headset — the desktop preview
    /// shows the rig view, and VRInput's desktop mapping (LMB grab, RMB use,
    /// WASD, hotkeys) drives everything else.
    ///
    /// v0.4.0 rewrite. The old version polled LEGACY UnityEngine.Input (throws
    /// on this new-Input-System host — VRInput.Update died every frame), fed
    /// the CAMERA's pose back as the head (feedback loop: camera → poll → aim
    /// → camera), and marked the synthetic hands "tracked" with world-space
    /// positions that HeadToWorld then mis-projected. Now: the synthetic head
    /// is independent of the camera, hands are untracked (UpdateHands parks
    /// the grabber views at the game camera — desktop-style grabbing), and
    /// there are no synthetic buttons — VRInput's desktop-authority mapping is
    /// the single input path, with the desktop hotkeys covering the actions
    /// the dead controllers would have done.
    /// </summary>
    internal sealed class KeyboardBackend : IVxrBackend
    {
        public string Name { get { return "keyboard"; } }
        private uint _packet = 0;
        private bool _headInit;
        private float _yaw;
        private float _pitch;

        public bool Init(Action<string> log)
        {
            if (log != null) log("Keyboard backend: active (dev mode). No headset required.");
            return true;
        }

        /// <summary>Re-anchor the synthetic head to the game camera and level
        /// the pitch (VR enable / scene load) so the first frame doesn't snap
        /// the view to a stale yaw.</summary>
        internal void ResetHead()
        {
            _headInit = false;
            _pitch = 0f;
        }

        public void Poll(out XrFrame frame)
        {
            frame = new XrFrame();
            frame.valid = true;
            frame.backend = Name;
            frame.packet = ++_packet;

            Camera cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindObjectOfType<Camera>();
            if (!_headInit)
            {
                _headInit = true;
                _pitch = 0f;
                if (cam != null) _yaw = cam.transform.eulerAngles.y;
            }

            // Mouse look into the synthetic head (new Input System only —
            // legacy Input THROWS on this host). Pitch clamps like an FPS.
            if (Application.isFocused)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    var d = mouse.delta.ReadValue();
                    float sens = 0.15f;
                    try
                    {
                        var mgr = VRKoboldKarePlugin.VRManager;
                        if (mgr != null && !ReferenceEquals(mgr.Plugin, null) && mgr.Plugin.cfgMouseSens != null)
                            sens = mgr.Plugin.cfgMouseSens.Value;
                    }
                    catch { }
                    _yaw += d.x * sens;
                    _pitch = Mathf.Clamp(_pitch + d.y * sens, -85f, 85f);
                }
            }

            frame.headTracked = cam != null;
            // Rig offset: HeadToWorld anchors this onto the player's body at
            // the avatar's ACTUAL head-bone height (neck lock — the old magic
            // 1.2m hovered over the kobold), so the rig rides movement, crouch
            // and ghost flight with the view pivoting at the neck.
            float eyeH = 1.2f;
            try
            {
                var mgr = VRKoboldKarePlugin.VRManager;
                if (mgr != null) eyeH = mgr.KeyboardEyeHeight;
            }
            catch { }
            frame.headPos = new Vector3(0f, eyeH, 0f);
            frame.headRot = Quaternion.Euler(_pitch, _yaw, 0f);

            // Hands untracked on purpose — see the class comment.
            frame.left = new XrHand();
            frame.right = new XrHand();
        }

        public void Haptic(bool left, float amplitude, int durationMs)
        {
        }

        public void Shutdown()
        {
        }
    }
}
