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
                Log("stereo renderer: OpenXR backend not active — skipping.");
                enabled = false;
                return;
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
            // Wait until the session is live, then build the stereo rig once.
            if (_rig == null && _xr != null && _xr.SessionRendering)
            {
                try { CreateRig(); }
                catch (Exception e) { LogWarn("rig creation failed: " + e); enabled = false; }
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
            long want =
                PickFormat(formats, 87) ??   // B8G8R8A8_UNORM  == Unity BGRA32
                PickFormat(formats, 28) ??   // R8G8B8A8_UNORM  == Unity RGBA32
                PickFormat(formats, 91) ??   // B8G8R8A8_UNORM_SRGB (fallback)
                PickFormat(formats, 29) ??   // R8G8B8A8_UNORM_SRGB (fallback)
                formats[0];
            TextureFormat unityFmt =
                (want == 91 || want == 87) ? TextureFormat.BGRA32 : TextureFormat.RGBA32;
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

                _swapchain[eye] = _xr.CreateSwapchain(want, w, h);
                if (_swapchain[eye] == 0)
                {
                    LogWarn("swapchain creation failed for eye " + eye);
                    return;
                }

                int imageCount = 3;
                var images = _xr.GetSwapchainImages(_swapchain[eye], imageCount);
                if (images == null)
                {
                    LogWarn("swapchain image enumeration failed for eye " + eye);
                    return;
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
                        return;
                    }
                    _wrapped[eye][i] = Texture2D.CreateExternalTexture(
                        (int)w, (int)h, unityFmt, false /*mips*/, false /*linear=use as sRGB color*/, images[i].texture);
                }
                Log("eye " + eye + ": " + w + "x" + h + ", " + imageCount + " swapchain images");

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
                Log("mono: game camera '" + _gameCam.name + "' -> RT " + _eyeW[0] + "x" + _eyeH[0] +
                    " (desktop window goes black while VR is on)");
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

        private XrFovf _monoFov;

        /// <summary>Vertical FOV (deg) of the current mono render — UI scale
        /// compensation reads this (converted canvases keep desktop-FOV sizing
        /// while the VR projection is far wider).</summary>
        internal static float CurrentVFovDeg = 60f;

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

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (_beginDebug < 10)
            {
                _beginDebug++;
                Log("beginCameraRendering: '" + cam.name + "'" +
                    (cam == _eyeCam[0] ? " [eye0]" : cam == _eyeCam[1] ? " [eye1]" :
                     cam == _gameCam ? " [game]" : ""));
            }
            if (_rig == null || _xr == null) return;

            // Frame-cadence anchor: the game camera in mono mode, eye0 otherwise.
            Camera anchorCam = _renderMode == "mono" ? _gameCam : _eyeCam[0];
            if (anchorCam == null || cam != anchorCam) return;

            if (!_xr.SessionRendering)
            {
                _frameBegun = false;
                _xr.ReleaseFrames();   // idle pump takes over
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
                if (_gameCam != null)
                {
                    // Re-poll the head at THIS frame's predicted display time
                    // and snap the game camera to it. The mirror patch ran at
                    // LateUpdate with last frame's poll — a 1-frame pose/
                    // content lag reads as "looking over your shoulder".
                    var fresh = VRXR.Poll();
                    if (fresh.valid && fresh.headTracked)
                    {
                        _gameCam.transform.position = _m.HeadToWorld(fresh.headPos, fresh.headRot);
                        _gameCam.transform.rotation = Quaternion.Euler(0f, _m.YawOffset, 0f) * fresh.headRot;
                    }
                    // Vertical flip compensation (Unity RT row order vs Vulkan
                    // image row order) + the OpenXR FOV, one matrix; negating
                    // Y mirrors winding so culling must invert.
                    _gameCam.projectionMatrix = FovToProjection(
                        _monoFov, _gameCam.nearClipPlane, _gameCam.farClipPlane, _flipY);
                    try { GL.invertCulling = _flipY; } catch { }
                }
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

        private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
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
                for (int eye = 0; eye < 2; eye++)
                {
                    uint idx;
                    if (!_xr.AcquireWaitAndCopyPrepare(_swapchain[eye], out idx))
                    {
                        if (_frameFailLog < 10) { _frameFailLog++; LogWarn("acquire failed eye " + eye); }
                        allOk = false;
                        break;
                    }
                    // Release ONLY on successful acquire — releasing an image
                    // we never got poisons the swapchain for good.
                    try
                    {
                        if (_renderMode != "nocopy")
                        {
                            Texture2D dst = _wrapped[eye][idx];
                            RenderTexture src = _renderMode == "mono" ? _monoRT : _eyeRT[eye];
                            if (src != null)
                            {
                                Graphics.CopyTexture(src, 0, 0, dst, 0, 0);
                                // CopyTexture only QUEUES GPU work on the render
                                // thread — releasing now would let the compositor
                                // read the image mid-copy (the one-eye/partial-
                                // fill race + the DXVK crash). ReadPixels forces
                                // a full pipeline flush before we release.
                                ForceGPUCompletion(src);
                            }
                        }
                    }
                    finally
                    {
                        _xr.ReleaseSwapchainImage(_swapchain[eye]);
                    }
                }
                rendered = allOk;
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
