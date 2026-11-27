// VR-KoboldKare — Stereo renderer
// Two Unity eye cameras render into RenderTextures; after URP finishes, each
// RT is GPU-copied (Graphics.CopyTexture) into an OpenXR swapchain image that
// was wrapped with Texture2D.CreateExternalTexture, then submitted with
// xrEndFrame as a stereo projection layer. Runs entirely on the main thread
// through URP's begin/endFrameRendering callbacks.
//
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRKoboldKare
{
    public class VRRenderer : MonoBehaviour
    {
        private VRManager _m;
        private OpenXRBackend _xr;

        private GameObject _rig;
        private Camera[] _eyeCam = new Camera[2];
        private RenderTexture[] _eyeRT = new RenderTexture[2];
        private ulong[] _swapchain = new ulong[2];
        private XrSwapchainImageD3D11KHR[][] _images;
        private Texture2D[][] _wrapped;      // per eye, per swapchain image
        private XrView[] _views = new XrView[2];
        private XrViewConfigurationView[] _vcv;

        private bool _frameBegun;
        private long _displayTime;
        private bool _shouldRender;

        // unmanaged layer scratch (freed on teardown)
        private IntPtr _viewsPtr;      // XrCompositionLayerProjectionView[2]
        private IntPtr _viewsPtrArr;   // XrCompositionLayerProjectionView*[]
        private IntPtr _layerPtr;      // XrCompositionLayerProjection
        private int _viewSize, _layerSize;

        private bool _logOnce;
        private uint[] _eyeW;
        private uint[] _eyeH;
        private RenderTexture _monoRT;
        private Camera _gameCam;

        internal void Initialize(VRManager manager)
        {
            _m = manager;
            _xr = VRXR.OpenXR;
            if (_xr == null)
            {
                // No OpenXR (keyboard dev backend): the renderer STAYS ALIVE in
                // headless mode — the desktop-preview pipeline (mono RT + the
                // preview canvas + screenshots) needs no session. Previously
                // this bailed, leaving headset-off debugging blind.
                Log("stereo renderer: no OpenXR backend — headless desktop-preview mode.");
            }

            string mode = "full";
            try
            {
                if (!ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgRenderMode != null)
                    mode = (_m.Plugin.cfgRenderMode.Value ?? "full").ToLowerInvariant();
            }
            catch { }
            if (mode != "full" && mode != "nocopy" && mode != "black" && mode != "mono") mode = "full";
            _renderMode = mode;
            try
            {
                _flipY = !ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgFlipY != null && _m.Plugin.cfgFlipY.Value;
            }
            catch { _flipY = true; }
            Log("stereo renderer initialized (mode=" + mode + ", flipY=" + _flipY + ", waits for OpenXR session).");

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private string _renderMode = "full";
        private bool _flipY = true;
        private Texture2D _syncTex;

        // ---- headless desktop-preview state ----
        private bool _headless;      // mono rig live WITHOUT a rendering session
        private bool _xrLive;        // session submit path active (flip/copy live)
        private bool _projOverridden;
        private UnityEngine.UI.RawImage _previewImage;
        private Canvas _previewCanvas;
        private bool _screenshotQueued;

        /// <summary>Queue one PNG of the VR view (hotkey; fires on Update).</summary>
        internal void RequestScreenshot() { _screenshotQueued = true; }

        private void Log(string s)
        {
            if (_m != null && !ReferenceEquals(_m.Plugin, null)) _m.Plugin.Logger.LogInfo("VRRenderer: " + s);
        }
        private void LogWarn(string s)
        {
            if (_m != null && !ReferenceEquals(_m.Plugin, null)) _m.Plugin.Logger.LogWarning("VRRenderer: " + s);
        }

        private void Update()
        {
            // Screenshots fire between frames — the mono RT then holds the last
            // completed frame (ReadPixels syncs the GPU anyway).
            if (_screenshotQueued && _monoRT != null)
            {
                _screenshotQueued = false;
                try { CaptureScreenshot(); }
                catch (Exception e) { LogWarn("screenshot failed: " + e.Message); }
            }

            if (_renderMode != "mono")
            {
                // Bisect modes unchanged: they only make sense with a session.
                if (_rig == null && _xr != null && _xr.SessionRendering)
                {
                    try { CreateRig(); }
                    catch (Exception e) { LogWarn("rig creation failed: " + e); enabled = false; }
                }
                return;
            }

            // Window resizes re-fit the preview letterbox (cheap — Unity only
            // dirties the RawImage when the values actually change).
            if (_previewImage != null && _monoRT != null && (Time.frameCount & 31) == 0)
                ApplyPreviewFlip();

            if (_rig == null)
            {
                try
                {
                    if (_xr != null && _xr.SessionRendering) CreateRig();
                    else CreateHeadlessMonoRig();   // desktop preview, no session needed
                }
                catch (Exception e) { LogWarn("rig creation failed: " + e); enabled = false; }
                return;
            }

            // The session came online after the headless rig — upgrade in place
            // (swapchains + eye-resolution RT; the preview canvas just swaps
            // the texture it points at).
            if (_headless && _xr != null && _xr.SessionRendering)
            {
                try { UpgradeToSessionRig(); }
                catch (Exception e) { LogWarn("session rig upgrade failed: " + e); }
            }
        }

        // ---------- rig ----------

        private void CreateRig()
        {
            _vcv = _xr.GetViewConfigurationViews();
            if (_vcv == null || _vcv.Length < 2)
            {
                LogWarn("no stereo view configuration available.");
                return;
            }

            var formats = _xr.GetSwapchainFormats();
            if (formats == null || formats.Length == 0)
            {
                LogWarn("no swapchain formats — cannot render to headset.");
                return;
            }

            // Prefer a format Unity can wrap with an EXACT match so
            // Graphics.CopyTexture takes its native-copy path — an sRGB
            // swapchain (91) vs Unity BGRA32 (87) mismatch forces the shader
            // fallback onto the DXVK-imported texture, which crashed d3d11
            // after a few frames (2026-11-21 playtest).
            long want = PickSwapchainFormat(formats, out TextureFormat unityFmt);
            Log("swapchain format DXGI " + want + " -> Unity " + unityFmt);

            _rig = new GameObject("VRKK_Rig");

            // GPU sync sentinel: ReadPixels forces a full pipeline flush, so we
            // can guarantee copies landed before releasing swapchain images.
            _syncTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);

            float scale = 0.5f;
            try
            {
                if (!ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgEyeScale != null)
                    scale = Mathf.Clamp(_m.Plugin.cfgEyeScale.Value, 0.25f, 1f);
            }
            catch { }
            Log("eye scale " + scale.ToString("0.00") + " (recommended " +
                _vcv[0].recommendedImageRectWidth + "x" + _vcv[0].recommendedImageRectHeight + ")");

            _eyeW = new uint[2];
            _eyeH = new uint[2];
            for (int eye = 0; eye < 2; eye++)
            {
                uint w = (uint)(_vcv[eye].recommendedImageRectWidth * scale) & ~1u;
                uint h = (uint)(_vcv[eye].recommendedImageRectHeight * scale) & ~1u;
                _eyeW[eye] = w;
                _eyeH[eye] = h;

                if (!CreateEyeSwapchain(eye, w, h, want, unityFmt)) return;

                if (_renderMode == "mono") continue;   // no extra cameras in mono

                var rt = new RenderTexture((int)w, (int)h, 24,
                    (want == 87 || want == 28) ? RenderTextureFormat.BGRA32 : RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                };
                rt.Create();
                _eyeRT[eye] = rt;

                var camObj = new GameObject("VRKK_Eye" + eye);
                camObj.transform.SetParent(_rig.transform, false);
                var cam = camObj.AddComponent<Camera>();
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.nearClipPlane = 0.05f;
                cam.depth = eye == 0 ? -2 : -1;   // eye0 renders first; copy fires after eye1
                cam.enabled = false;   // enabled below once configured
                cam.allowHDR = false;
                cam.allowMSAA = false;
                // Minimal-survivor URP config: no post, no shadows, no depth/
                // color prepass, no AA — the full game pipeline on these RTs
                // crashed DXVK (2026-11-21 bisect: nocopy crashed, black didn't).
                try
                {
                    var urp = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    if (urp != null)
                    {
                        urp.renderPostProcessing = false;
                        urp.renderShadows = false;
                        urp.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
                        urp.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
                        urp.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
                    }
                }
                catch (Exception e) { Log("URP camera data config: " + e.Message); }
                _eyeCam[eye] = cam;
            }

            if (_renderMode == "mono")
            {
                // MONO: the game's own camera renders into one RT; both eyes
                // receive that texture (per-eye pose/fov in the layer). Zero
                // extra cameras — the bisect proved URP eye-cam rendering is
                // what crashes DXVK in this game.
                _gameCam = Camera.main;
                if (_gameCam == null)
                {
                    LogWarn("mono mode: no main camera found");
                    return;
                }
                _monoRT = new RenderTexture((int)_eyeW[0], (int)_eyeH[0], 24,
                    (want == 87 || want == 28) ? RenderTextureFormat.BGRA32 : RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                };
                _monoRT.Create();
                _gameCam.targetTexture = _monoRT;
                Log("mono: game camera '" + _gameCam.name + "' -> RT " + _eyeW[0] + "x" + _eyeH[0]);
                CreatePreview();
            }

            // Unmanaged layer scratch.
            _viewSize = Marshal.SizeOf(typeof(XrCompositionLayerProjectionView));
            _layerSize = Marshal.SizeOf(typeof(XrCompositionLayerProjection));
            _viewsPtr = Marshal.AllocHGlobal(_viewSize * 2);
            _viewsPtrArr = Marshal.AllocHGlobal(IntPtr.Size);
            _layerPtr = Marshal.AllocHGlobal(_layerSize);

            SyncFromGameCamera();
            if (_renderMode == "full" || _renderMode == "nocopy")
            {
                _eyeCam[0].enabled = true;
                _eyeCam[1].enabled = true;
            }

            _xr.ClaimFrames(2000);
            Log("stereo rig live — frames owned by renderer (mode=" + _renderMode + ").");
        }

        private static long PickSwapchainFormat(long[] formats, out TextureFormat unityFmt)
        {
            long want =
                PickFormat(formats, 87) ??   // B8G8R8A8_UNORM  == Unity BGRA32
                PickFormat(formats, 28) ??   // R8G8B8A8_UNORM  == Unity RGBA32
                PickFormat(formats, 91) ??   // B8G8R8A8_UNORM_SRGB (fallback)
                PickFormat(formats, 29) ??   // R8G8B8A8_UNORM_SRGB (fallback)
                formats[0];
            unityFmt = (want == 91 || want == 87) ? TextureFormat.BGRA32 : TextureFormat.RGBA32;
            return want;
        }

        // ---------- headless mono rig (desktop preview without a session) ----------

        /// <summary>The VR view on the desktop window with NO OpenXR session:
        /// the game camera renders into a screen-sized RT (so the VR rig —
        /// camera pose, world-space HUD, hands — IS what the desktop shows), an
        /// overlay-canvas RawImage presents it. Overlay canvases never render
        /// into render textures (VRUI gotcha 14) — which is exactly what we
        /// want here: the preview reaches the desktop window even though every
        /// camera targets the RT, and it never leaks into the headset feed.</summary>
        private void CreateHeadlessMonoRig()
        {
            _rig = new GameObject("VRKK_Rig");
            _gameCam = Camera.main;
            if (_gameCam == null)
            {
                LogWarn("headless mono: no main camera found");
                return;
            }
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) { w = 1280; h = 720; }
            // sRGB: this RT goes to the SCREEN (RawImage), not through the
            // CopyTexture-to-swapchain path — correct color beats exact bits.
            _monoRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
            };
            _monoRT.Create();
            _gameCam.targetTexture = _monoRT;
            _headless = true;
            CurrentViewW = w;
            CurrentViewH = h;
            CreatePreview();
            Log("headless mono rig: game camera -> RT " + w + "x" + h +
                " — the desktop window IS the VR view now (normal desktop view returns on VR disable)");
        }

        /// <summary>The WiVRn session came online after the headless rig was
        /// already running: add swapchains and resize the RT to the eye
        /// resolution, keeping the preview canvas pointed at the fresh
        /// texture.</summary>
        private void UpgradeToSessionRig()
        {
            _vcv = _xr.GetViewConfigurationViews();
            if (_vcv == null || _vcv.Length < 2) { LogWarn("upgrade: no stereo view configuration"); return; }
            var formats = _xr.GetSwapchainFormats();
            if (formats == null || formats.Length == 0) { LogWarn("upgrade: no swapchain formats"); return; }
            long want = PickSwapchainFormat(formats, out TextureFormat unityFmt);

            // The session copy path needs the GPU sync sentinel (ReadPixels
            // flush between CopyTexture and release — gotcha 8); the headless
            // rig never made one.
            if (_syncTex == null)
                _syncTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);

            _eyeW = new uint[2];
            _eyeH = new uint[2];
            for (int eye = 0; eye < 2; eye++)
            {
                uint w = (uint)(_vcv[eye].recommendedImageRectWidth * Mathf.Clamp(EyeScale(), 0.25f, 1f)) & ~1u;
                uint h = (uint)(_vcv[eye].recommendedImageRectHeight * Mathf.Clamp(EyeScale(), 0.25f, 1f)) & ~1u;
                _eyeW[eye] = w;
                _eyeH[eye] = h;
                if (!CreateEyeSwapchain(eye, w, h, want, unityFmt)) return;
            }

            // Recreate the RT at the eye resolution; the session path needs
            // Linear + the swapchain-matching format for exact-bit CopyTexture.
            if (_gameCam == null) _gameCam = Camera.main;
            if (_gameCam == null) { LogWarn("upgrade: no main camera"); return; }
            if (_monoRT != null) _monoRT.Release();
            _monoRT = new RenderTexture((int)_eyeW[0], (int)_eyeH[0], 24,
                (want == 87 || want == 28) ? RenderTextureFormat.BGRA32 : RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
            };
            _monoRT.Create();
            _gameCam.targetTexture = _monoRT;
            if (_previewImage != null) _previewImage.texture = _monoRT;

            _viewSize = Marshal.SizeOf(typeof(XrCompositionLayerProjectionView));
            _layerSize = Marshal.SizeOf(typeof(XrCompositionLayerProjection));
            _viewsPtr = Marshal.AllocHGlobal(_viewSize * 2);
            _viewsPtrArr = Marshal.AllocHGlobal(IntPtr.Size);
            _layerPtr = Marshal.AllocHGlobal(_layerSize);

            _headless = false;
            _xrLive = true;
            ApplyPreviewFlip();
            CurrentViewW = _eyeW[0];
            CurrentViewH = _eyeH[0];
            _xr.ClaimFrames(2000);
            Log("session rig live — headless preview upgraded to eye textures " + _eyeW[0] + "x" + _eyeH[0]);
        }

        /// <summary>Configured eye scale (0.5 default) — session-RT sizing.</summary>
        private float EyeScale()
        {
            try
            {
                if (!ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgEyeScale != null)
                    return _m.Plugin.cfgEyeScale.Value;
            }
            catch { }
            return 0.5f;
        }

        private bool CreateEyeSwapchain(int eye, uint w, uint h, long want, TextureFormat unityFmt)
        {
            _swapchain[eye] = _xr.CreateSwapchain(want, w, h);
            if (_swapchain[eye] == 0)
            {
                LogWarn("swapchain creation failed for eye " + eye);
                return false;
            }
            int imageCount = 3;
            var images = _xr.GetSwapchainImages(_swapchain[eye], imageCount);
            if (images == null)
            {
                LogWarn("swapchain image enumeration failed for eye " + eye);
                return false;
            }
            imageCount = images.Length;
            if (_images == null) { _images = new XrSwapchainImageD3D11KHR[2][]; _wrapped = new Texture2D[2][]; }
            _images[eye] = images;
            _wrapped[eye] = new Texture2D[imageCount];
            for (int i = 0; i < imageCount; i++)
            {
                if (images[i].texture == IntPtr.Zero)
                {
                    LogWarn("swapchain image " + i + " has no texture");
                    return false;
                }
                _wrapped[eye][i] = Texture2D.CreateExternalTexture(
                    (int)w, (int)h, unityFmt, false /*mips*/, false /*linear=use as sRGB color*/, images[i].texture);
            }
            Log("eye " + eye + ": " + w + "x" + h + ", " + imageCount + " swapchain images");
            return true;
        }

        // ---------- desktop preview canvas ----------

        private void CreatePreview()
        {
            bool on = true;
            try
            {
                if (!ReferenceEquals(_m.Plugin, null) && _m.Plugin.cfgDesktopPreview != null)
                    on = _m.Plugin.cfgDesktopPreview.Value;
            }
            catch { }
            if (!on)
            {
                Log("desktop preview disabled (VR.Render.DesktopPreview) — window stays black in VR");
                return;
            }
            var go = new GameObject("VRKK_PreviewCanvas");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _previewCanvas = go.AddComponent<Canvas>();
            _previewCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // UNDER the game's overlay UI (sorting 0): the main menu and HUD
            // stay readable on top of the mirrored world while debugging.
            _previewCanvas.sortingOrder = -50;
            _previewImage = go.AddComponent<UnityEngine.UI.RawImage>();
            _previewImage.texture = _monoRT;
            _previewImage.raycastTarget = false;
            ApplyPreviewFlip();
            Log("desktop preview live (overlay canvas, under the game UI)");
        }

        private void ApplyPreviewFlip()
        {
            if (_previewImage == null || _monoRT == null) return;
            // Session mono renders with a Y-negated projection (the compositor
            // flips it back) — that RT content is upside down. Headless and
            // flipY=false render upright.
            bool flipped = _xrLive && _flipY;
            _previewImage.uvRect = flipped ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);

            // Letterbox (RawImage has no preserveAspect in this Unity version):
            // shrink the rect along the over-long axis so the VR view is never
            // distorted — the eye RT's aspect rarely matches the window's.
            var rt = _previewImage.rectTransform;
            float winAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            float texAspect = (float)_monoRT.width / _monoRT.height;
            if (Mathf.Abs(winAspect - texAspect) < 0.01f)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
            }
            else if (texAspect > winAspect)
            {
                float fit = winAspect / texAspect;
                rt.anchorMin = new Vector2(0f, (1f - fit) * 0.5f);
                rt.anchorMax = new Vector2(1f, 1f - (1f - fit) * 0.5f);
            }
            else
            {
                float fit = texAspect / winAspect;
                rt.anchorMin = new Vector2((1f - fit) * 0.5f, 0f);
                rt.anchorMax = new Vector2(1f - (1f - fit) * 0.5f, 1f);
            }
        }

        // ---------- screenshot ----------

        /// <summary>Write one PNG of the VR view to BepInEx/screenshots/.
        /// Works headless (the mono RT exists whenever VR is enabled).</summary>
        private void CaptureScreenshot()
        {
            if (_monoRT == null) return;
            string dir;
            try { dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "screenshots"); }
            catch { dir = "screenshots"; }
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            string path = System.IO.Path.Combine(dir, UiMath.ScreenshotFileName(DateTime.Now));

            var prev = RenderTexture.active;
            RenderTexture.active = _monoRT;
            var tex = new Texture2D(_monoRT.width, _monoRT.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _monoRT.width, _monoRT.height), 0, 0);
            RenderTexture.active = prev;
            tex.Apply();

            // Session mono renders Y-negated (the compositor flips it back) —
            // un-flip the rows so the file matches what the headset shows.
            if (_xrLive && _flipY)
            {
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                var row = new Color32[w];
                for (int y = 0; y < h / 2; y++)
                {
                    int a = y * w, b = (h - 1 - y) * w;
                    System.Array.Copy(px, a, row, 0, w);
                    System.Array.Copy(px, b, px, a, w);
                    System.Array.Copy(row, 0, px, b, w);
                }
                tex.SetPixels32(px);
                tex.Apply();
            }

            // This game's Unity build stripped EncodeToPNG (verified in the
            // Managed assemblies) — EncodeToJPG at 95 is the honest encode.
            System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToJPG(tex, 95));
            UnityEngine.Object.Destroy(tex);
            Log("screenshot saved: " + path);
        }

        private XrFovf _monoFov;

        /// <summary>Vertical FOV (deg) of the current mono render — UI scale
        /// compensation reads this (converted canvases keep desktop-FOV sizing
        /// while the VR projection is far wider).</summary>
        internal static float CurrentVFovDeg = 60f;
        internal static float CurrentViewW = 3096f;
        internal static float CurrentViewH = 3242f;

        /// <summary>One symmetric FOV covering both eyes' asymmetric bounds.</summary>
        private XrFovf SymmetricFov()
        {
            var a = _views[0].fov;
            var b = _views[1].fov;
            float l = Mathf.Max(Mathf.Abs(a.angleLeft), Mathf.Abs(b.angleLeft));
            float r = Mathf.Max(Mathf.Abs(a.angleRight), Mathf.Abs(b.angleRight));
            float u = Mathf.Max(a.angleUp, b.angleUp);
            float d = Mathf.Min(a.angleDown, b.angleDown);
            return new XrFovf { angleLeft = -l, angleRight = r, angleUp = u, angleDown = d };
        }

        /// <summary>Midpoint of the two eye poses (the head center) — the pose
        /// a mono image is actually rendered from.</summary>
        private XrPosef HeadCenterPose()
        {
            var p0 = _views[0].pose;
            var p1 = _views[1].pose;
            return new XrPosef
            {
                orientation = p0.orientation,
                position = new XrVector3f
                {
                    x = (p0.position.x + p1.position.x) * 0.5f,
                    y = (p0.position.y + p1.position.y) * 0.5f,
                    z = (p0.position.z + p1.position.z) * 0.5f,
                },
            };
        }

        /// <summary>Full pipeline flush: ReadPixels is synchronous GPU→CPU.</summary>
        private void ForceGPUCompletion(RenderTexture src)
        {
            try
            {
                var prev = RenderTexture.active;
                RenderTexture.active = src;
                _syncTex.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                RenderTexture.active = prev;
            }
            catch { }
        }

        private static long? PickFormat(long[] available, long want)
        {
            foreach (long f in available) if (f == want) return want;
            return null;
        }

        /// <summary>Copy rendering settings from the game's main camera.</summary>
        private void SyncFromGameCamera()
        {
            Camera main = Camera.main;
            if (main == null) return;
            foreach (Camera c in _eyeCam)
            {
                if (c == null) continue;
                c.cullingMask = main.cullingMask;
                // The dedicated UI layer renders via the overlay camera only.
                var ui = _m != null && _m.UI != null ? _m.UI.UiLayer : -1;
                if (ui > 0) c.cullingMask &= ~(1 << ui);
                c.clearFlags = main.clearFlags;
                c.backgroundColor = main.backgroundColor;
                c.farClipPlane = main.farClipPlane;
                c.allowHDR = main.allowHDR;
                c.useOcclusionCulling = main.useOcclusionCulling;
            }
        }

        // ---------- frame callbacks (per-camera, this Unity build has no
        // beginFrameRendering/endFrameRendering) ----------

        private int _beginDebug;
        private int _frameFailLog;
        private int _fovLogged;

        // HARDENING (circuit breaker): a throw inside a URP render callback
        // can break the WHOLE frame — after 30 consecutive failures the
        // callback stands down (logged) instead of taking rendering with it.
        private int _beginStrikes;

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (_beginStrikes >= 30) return;
            try
            {
                OnBeginCameraRenderingInner(ctx, cam);
                _beginStrikes = 0;
            }
            catch (Exception e)
            {
                _beginStrikes++;
                if (_beginStrikes == 1 || _beginStrikes % 300 == 0)
                    LogWarn("begin-callback threw (" + _beginStrikes + " consecutive) — breaker trips at 30: " + e.Message);
            }
        }

        private void OnBeginCameraRenderingInner(ScriptableRenderContext ctx, Camera cam)
        {
            if (_beginDebug < 10)
            {
                _beginDebug++;
                Log("beginCameraRendering: '" + cam.name + "'" +
                    (cam == _eyeCam[0] ? " [eye0]" : cam == _eyeCam[1] ? " [eye1]" :
                     cam == _gameCam ? " [game]" : ""));
            }
            if (_rig == null) return;

            // Frame-cadence anchor: the game camera in mono mode, eye0 otherwise.
            Camera anchorCam = _renderMode == "mono" ? _gameCam : _eyeCam[0];
            if (anchorCam == null || cam != anchorCam) return;

            bool session = _xr != null && _xr.SessionRendering;
            if (!session)
            {
                if (_xr != null) _xr.ReleaseFrames();   // idle pump takes over

                // Headless mono: keep the desktop preview live. Snap the game
                // camera to the rig pose with a FRESH poll (the LateUpdate
                // mirror patch ran with last frame's poll — this is a frame
                // crisper, same as the session path below). The game's own
                // projection stays (upright, no FlipY games headless).
                if (_headless && _gameCam != null)
                {
                    var fresh = VRXR.Poll();
                    if (fresh.valid && fresh.headTracked && _m.Cam != null && _m.Cam.MirrorEnabled)
                    {
                        _gameCam.transform.position = _m.HeadToWorld(fresh.headPos, fresh.headRot);
                        _gameCam.transform.rotation = _m.ViewRotation(fresh.headRot);
                    }
                    // UI-scale compensation reads the vFOV — headless renders
                    // at the game's FOV, not the headset's.
                    try { CurrentVFovDeg = _gameCam.fieldOfView; } catch { }
                    if (_xrLive) { _xrLive = false; ApplyPreviewFlip(); }   // session flapped down
                    if (_projOverridden)
                    {
                        _gameCam.ResetProjectionMatrix();
                        _projOverridden = false;
                        try { GL.invertCulling = false; } catch { }
                    }
                }
                _frameBegun = false;
                return;
            }
            _xr.ClaimFrames(500);

            XrFrameState fs;
            if (!_xr.WaitAndBeginFrame(out fs))
            {
                _frameBegun = false;
                if (_frameFailLog < 5) { _frameFailLog++; LogWarn("WaitAndBeginFrame failed"); }
                return;
            }
            _frameBegun = true;
            _displayTime = fs.predictedDisplayTime;
            _shouldRender = fs.shouldRender != 0;

            if (_renderMode == "mono")
            {
                for (int i = 0; i < _views.Length; i++) _views[i].type = XrType.VIEW;
                _xr.LocateViews(_displayTime, _views);
                // One shared, symmetric FOV for both eyes (mono image can't
                // satisfy per-eye asymmetric FOVs — that misaligned the eyes).
                _monoFov = SymmetricFov();
                const float rad2degF = 57.29578f;
                CurrentVFovDeg = (_monoFov.angleUp - _monoFov.angleDown) * rad2degF;
                CurrentViewW = _eyeW[0];
                CurrentViewH = _eyeH[0];
                if (_gameCam != null)
                {
                    // Re-poll the head at THIS frame's predicted display time
                    // and snap the game camera to it. The mirror patch ran at
                    // LateUpdate with last frame's poll — a 1-frame pose/
                    // content lag reads as "looking over your shoulder".
                    var fresh = VRXR.Poll();
                    if (fresh.valid && fresh.headTracked && _m.Cam != null && _m.Cam.MirrorEnabled)
                    {
                        // Head-locked snap ONLY in mirrored first person. When
                        // the game switches to third person (stations,
                        // animations) or free cam, its own orbit drives —
                        // that's the spectate view she described.
                        _gameCam.transform.position = _m.HeadToWorld(fresh.headPos, fresh.headRot);
                        _gameCam.transform.rotation = _m.ViewRotation(fresh.headRot);
                    }
                    // Vertical flip compensation (Unity RT row order vs Vulkan
                    // image row order) + the OpenXR FOV, one matrix; negating
                    // Y mirrors winding so culling must invert.
                    _gameCam.projectionMatrix = FovToProjection(
                        _monoFov, _gameCam.nearClipPlane, _gameCam.farClipPlane, _flipY);
                    _projOverridden = true;
                    try { GL.invertCulling = _flipY; } catch { }
                }
                if (!_xrLive) { _xrLive = true; ApplyPreviewFlip(); }
                return;
            }

            if (!_shouldRender) return;

            for (int i = 0; i < _views.Length; i++) _views[i].type = XrType.VIEW;
            if (!_xr.LocateViews(_displayTime, _views))
            {
                return;
            }

            SyncFromGameCamera();

            if (_renderMode == "black")
            {
                _frameBegun = true;   // submit path runs, but no cameras render
                return;
            }

            if (_fovLogged < 3)
            {
                _fovLogged++;
                const float rad2deg = 57.29578f;
                for (int eye = 0; eye < 2; eye++)
                {
                    var f = _views[eye].fov;
                    Log("eye " + eye + " fov deg: L" + (f.angleLeft * rad2deg).ToString("0") +
                        " R" + (f.angleRight * rad2deg).ToString("0") +
                        " U" + (f.angleUp * rad2deg).ToString("0") +
                        " D" + (f.angleDown * rad2deg).ToString("0"));
                }
            }

            for (int eye = 0; eye < 2; eye++)
            {
                var c = _eyeCam[eye];
                if (c == null) continue;
                XrPosef p = _views[eye].pose;
                Quaternion viewRot = new Quaternion(p.orientation.x, p.orientation.y, -p.orientation.z, p.orientation.w);
                c.transform.position = _m.HeadToWorld(new Vector3(p.position.x, p.position.y, -p.position.z), viewRot);
                c.transform.rotation = Quaternion.Euler(0f, _m.YawOffset, 0f) * viewRot;
                c.projectionMatrix = FovToProjection(_views[eye].fov, c.nearClipPlane, c.farClipPlane, false);
            }

            // The D3D11→Vulkan interop presents the frame vertically flipped.
            // The correct mechanism is rendering with inverted culling around
            // the eye cameras (negating the projection's Y row mirrors winding
            // and produced the "incomplete scene" + DXVK crash).
            try { GL.invertCulling = true; } catch { }
        }

        private static Matrix4x4 FovToProjection(XrFovf fov, float near, float far, bool negY)
        {
            float tanL = Mathf.Tan(fov.angleLeft);
            float tanR = Mathf.Tan(fov.angleRight);
            float tanU = Mathf.Tan(fov.angleUp);
            float tanD = Mathf.Tan(fov.angleDown);
            var m = Matrix4x4.zero;
            m[0, 0] = 2f / (tanR - tanL);
            m[1, 1] = negY ? -2f / (tanU - tanD) : 2f / (tanU - tanD);
            m[0, 2] = (tanR + tanL) / (tanR - tanL);
            m[1, 2] = negY ? -(tanU + tanD) / (tanU - tanD) : (tanU + tanD) / (tanU - tanD);
            m[2, 2] = -(far + near) / (far - near);
            m[2, 3] = -2f * far * near / (far - near);
            m[3, 2] = -1f;
            return m;
        }

        private int _endStrikes;

        private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (_endStrikes >= 30) return;
            try
            {
                OnEndCameraRenderingInner(ctx, cam);
                _endStrikes = 0;
            }
            catch (Exception e)
            {
                _endStrikes++;
                if (_endStrikes == 1 || _endStrikes % 300 == 0)
                    LogWarn("end-callback threw (" + _endStrikes + " consecutive) — breaker trips at 30: " + e.Message);
            }
        }

        private void OnEndCameraRenderingInner(ScriptableRenderContext ctx, Camera cam)
        {
            if (_rig == null || _xr == null || _viewsPtr == IntPtr.Zero) return;

            bool anchor = _renderMode == "mono" ? cam == _gameCam : cam == _eyeCam[1];
            if (!anchor)
            {
                if (_beginDebug < 12) { _beginDebug++; Log("endCameraRendering: '" + cam.name + "' (waiting for anchor)"); }
                return;
            }
            if (!_frameBegun) return;

            if (cam == _eyeCam[1])
            {
                try { GL.invertCulling = false; } catch { }
            }
            else if (_renderMode == "mono" && cam == _gameCam)
            {
                try { GL.invertCulling = false; } catch { }
            }
            // URP gotcha: endCameraRendering fires BEFORE the camera's queued
            // GPU commands execute (they flush at the context's Submit). Flush
            // here so our immediate CopyTexture reads rendered pixels instead
            // of racing ahead of the pipeline.
            try { ctx.Submit(); } catch (Exception e) { LogWarn("ctx.Submit: " + e.Message); }

            bool rendered = false;
            bool mono = _renderMode == "mono";
            XrPosef monoPose = mono ? HeadCenterPose() : default(XrPosef);
            XrFovf monoFov = mono ? _monoFov : default(XrFovf);
            var vcvViews = new XrCompositionLayerProjectionView[2];
            for (int eye = 0; eye < 2; eye++)
            {
                vcvViews[eye].type = XrType.COMPOSITION_LAYER_PROJECTION_VIEW;
                // Mono: BOTH views get the head-center pose and the shared
                // symmetric fov — per-eye poses on a shared image misalign
                // the eyes by the IPD offset.
                vcvViews[eye].pose = mono ? monoPose : _views[eye].pose;
                vcvViews[eye].fov = mono ? monoFov : _views[eye].fov;
                vcvViews[eye].subImage.swapchain = _swapchain[eye];
                vcvViews[eye].subImage.imageRect = new XrRect2Di
                {
                    offset = new XrOffset2Di { x = 0, y = 0 },
                    extent = new XrExtent2Di
                    {
                        width = (int)(_eyeW != null ? _eyeW[eye] : _vcv[eye].recommendedImageRectWidth),
                        height = (int)(_eyeH != null ? _eyeH[eye] : _vcv[eye].recommendedImageRectHeight),
                    },
                };
                vcvViews[eye].subImage.imageArrayIndex = 0;
            }

            if (_shouldRender)
            {
                bool allOk = true;
                // Acquire BOTH, copy BOTH, ONE fence, release BOTH — a single
                // full-pipeline stall per frame. Two stalls (one per eye) blew
                // the compositor deadline at EyeScale 1.0 and the session
                // flapped to SYNCHRONIZED (black headset).
                var okEye = new bool[2];
                var idxs = new uint[2];
                for (int eye = 0; eye < 2; eye++)
                {
                    if (!_xr.AcquireAndWait(_swapchain[eye], out idxs[eye]))
                    {
                        if (_frameFailLog < 10) { _frameFailLog++; LogWarn("acquire failed eye " + eye); }
                        allOk = false;
                        break;
                    }
                    okEye[eye] = true;   // acquired: caller must release even on copy failure
                }
                if (allOk)
                {
                    try
                    {
                        if (_renderMode != "nocopy")
                        {
                            for (int eye = 0; eye < 2; eye++)
                            {
                                Texture2D dst = _wrapped[eye][idxs[eye]];
                                RenderTexture src = _renderMode == "mono" ? _monoRT : _eyeRT[eye];
                                if (src != null)
                                    Graphics.CopyTexture(src, 0, 0, dst, 0, 0);
                            }
                            // CopyTexture only QUEUES GPU work on the render
                            // thread — releasing before the copies execute lets
                            // the compositor read images mid-write. One
                            // ReadPixels flush guarantees both copies landed
                            // before either release.
                            ForceGPUCompletion(_renderMode == "mono" ? _monoRT : _eyeRT[0]);
                        }
                    }
                    finally
                    {
                        for (int eye = 0; eye < 2; eye++)
                            if (okEye[eye])
                                _xr.ReleaseSwapchainImage(_swapchain[eye]);
                    }
                    rendered = true;
                }
            }

            // Write views + layer into unmanaged memory, then submit.
            for (int eye = 0; eye < 2; eye++)
                Marshal.StructureToPtr(vcvViews[eye], IntPtr.Add(_viewsPtr, eye * _viewSize), false);

            var layer = new XrCompositionLayerProjection
            {
                type = XrType.COMPOSITION_LAYER_PROJECTION,
                layerFlags = XrEnums.COMPOSITION_LAYER_CORRECT_CHROMATIC_ABERRATION_BIT,
                space = _xr.AppSpace,
                viewCount = 2,
                views = _viewsPtr,
            };
            Marshal.WriteIntPtr(_viewsPtrArr, 0, _layerPtr);
            Marshal.StructureToPtr(layer, _layerPtr, false);

            _xr.SubmitFrame(_displayTime, _viewsPtrArr, rendered ? 1u : 0u);

            if (rendered && !_logOnce)
            {
                _logOnce = true;
                Log("first stereo frame submitted.");
            }
        }

        // ---------- teardown ----------

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            if (_xr != null) _xr.ReleaseFrames();

            // Mono mode: give the desktop its view back before tearing down.
            // The flipped VR projection MUST be reset or the desktop world
            // renders upside down with broken culling after VR disable.
            if (_gameCam != null && _monoRT != null)
            {
                try { _gameCam.targetTexture = null; _gameCam.ResetProjectionMatrix(); } catch { }
            }
            try { GL.invertCulling = false; } catch { }

            if (_previewCanvas != null) Destroy(_previewCanvas.gameObject);
            _previewImage = null;
            _previewCanvas = null;

            foreach (var cam in _eyeCam) if (cam != null) Destroy(cam.gameObject);
            foreach (var rt in _eyeRT) if (rt != null) rt.Release();
            if (_monoRT != null) _monoRT.Release();
            if (_syncTex != null) Destroy(_syncTex);
            if (_rig != null) Destroy(_rig);
            if (_wrapped != null)
                foreach (var arr in _wrapped)
                    if (arr != null)
                        foreach (var t in arr) if (t != null) Destroy(t);

            if (_viewsPtr != IntPtr.Zero) Marshal.FreeHGlobal(_viewsPtr);
            if (_viewsPtrArr != IntPtr.Zero) Marshal.FreeHGlobal(_viewsPtrArr);
            if (_layerPtr != IntPtr.Zero) Marshal.FreeHGlobal(_layerPtr);
            _viewsPtr = _viewsPtrArr = _layerPtr = IntPtr.Zero;
        }
    }
}
