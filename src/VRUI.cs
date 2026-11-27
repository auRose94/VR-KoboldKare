// VR-KoboldKare — VR User Interface
// World-space panels: an interaction prompt (what you're aiming at) and a
// toggleable radial menu. Positions itself in front of the HMD each frame.
// Also converts the game's Screen Space - Overlay canvases (chat, HUD,
// menus) to Screen Space - Camera so they render INTO the headset — URP
// does not draw overlay canvases into render textures.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VRKoboldKare
{
    public class VRUI : MonoBehaviour
    {
        private VRManager _manager;

        private GameObject _canvasRoot;
        private Canvas _canvas;
        private GameObject _promptPanel;
        private Text _promptText;
        private GameObject _radialPanel;
        private bool _radialVisible;

        internal void Initialize(VRManager manager)
        {
            _manager = manager;
            CreateCanvas();
            CreatePrompt();
            CreateRadial();
            if (!ReferenceEquals(manager.Plugin, null) && manager.Plugin.Logger != null)
                manager.Plugin.Logger.LogInfo("VR: UI subsystem initialized.");
        }

        // ------------------------------------------------------------------
        //  Contract methods
        // ------------------------------------------------------------------

        /// <summary>Hide every panel. Called when VR is disabled.</summary>
        public void HideAll()
        {
            if (_promptPanel != null) _promptPanel.SetActive(false);
            if (_radialPanel != null) { _radialPanel.SetActive(false); _radialVisible = false; }
        }

        /// <summary>Show/hide the radial menu. Called on Menu + Y.</summary>
        public void ToggleRadial()
        {
            _radialVisible = !_radialVisible;
            if (_radialPanel != null) _radialPanel.SetActive(_radialVisible);
        }

        // ------------------------------------------------------------------
        //  Per-frame
        // ------------------------------------------------------------------

        private sealed class CanvasOverride { public Canvas C; public RenderMode Mode; public Camera Cam; public float BaseScale; public int OriginalSorting; public int Slot; }
        private readonly List<CanvasOverride> _canvasOverrides = new List<CanvasOverride>();
        private float _nextCanvasScan;
        private const float NearPlaneDistance = 15f;

        // ---- UI overlay layer ----
        // Converted canvases go on a dedicated layer rendered by a URP OVERLAY
        // camera with cleared depth — they draw OVER the world instead of
        // depth-testing against it (the "clips into floor/mountains" bug).
        private Camera _uiOverlayCam;
        private Camera _baseCam;
        private int _uiLayer = -1;
        private bool _uiLayerReady;

        private void EnsureUiLayer()
        {
            if (_uiLayerReady) return;
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                _baseCam = cam;

                // Layer 23 is unused in KoboldKare's TagManager (verified
                // against the project source) — the dedicated UI overlay layer.
                _uiLayer = 23;
                cam.cullingMask &= ~(1 << _uiLayer);   // the base never draws the UI (no depth occlusion)

                var go = new GameObject("VRKK_UICam");
                _uiOverlayCam = go.AddComponent<Camera>();
                _uiOverlayCam.enabled = false;          // driven by the base's camera stack
                _uiOverlayCam.cullingMask = 1 << _uiLayer;
                _uiOverlayCam.nearClipPlane = 0.01f;
                _uiOverlayCam.farClipPlane = 1000f;
                _uiOverlayCam.stereoTargetEye = StereoTargetEyeMask.None;
                go.transform.SetParent(cam.transform, false);

                var data = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
                          ?? go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
                data.renderShadows = false;
                data.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
                data.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
                // clearDepth is get-only in this URP build — write the backing
                // field so the overlay clears depth and draws OVER the world.
                try
                {
                    var cdFi = typeof(UnityEngine.Rendering.Universal.UniversalAdditionalCameraData)
                        .GetField("m_ClearDepth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    cdFi?.SetValue(data, true);
                }
                catch (Exception) { }
                data.renderShadows = false;
                data.requiresDepthOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
                data.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.Off;

                var baseData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (baseData == null) baseData = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (baseData.renderType != UnityEngine.Rendering.Universal.CameraRenderType.Base)
                    baseData.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
                baseData.cameraStack.Add(_uiOverlayCam);

                _uiLayerReady = true;
                try { VRKoboldKarePlugin.Log.LogInfo("VR: UI overlay layer " + _uiLayer + " armed (draws over the world)"); } catch (Exception) { }
            }
            catch (Exception e)
            {
                try { VRKoboldKarePlugin.Log.LogWarning("VR: UI overlay layer failed: " + e.Message); } catch (Exception) { }
                _uiLayerReady = true;   // don't retry every frame
                _uiLayer = -1;
            }
        }

        /// <summary>The dedicated UI layer bit (for excluding it from world cameras).</summary>
        internal int UiLayer => _uiLayer;
        internal void SetLayerRecursivelyInternal(GameObject go, int layer) => SetLayerRecursively(go, layer);

        private void SyncOverlayProjection()
        {
            try
            {
                if (_uiOverlayCam != null && _baseCam != null)
                {
                    _uiOverlayCam.transform.position = _baseCam.transform.position;
                    _uiOverlayCam.transform.rotation = _baseCam.transform.rotation;
                    _uiOverlayCam.projectionMatrix = _baseCam.projectionMatrix;
                }
            }
            catch { }
        }

        internal void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        /// <summary>
        /// Screen Space - Overlay canvases draw to the BACKBUFFER only — they
        /// never appear in the headset. Convert them to Screen Space - Camera
        /// (rendered into the mono RT) and restore on teardown. Rescanned
        /// every 2s: chat windows etc. are created dynamically.
        /// </summary>
        private void ScanCanvases()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || c == _canvas) continue;
                // Overlay never renders into RTs; ScreenSpaceCamera renders
                // but at full-FOV size (unusable in the headset) — convert
                // BOTH to WorldSpace panels.
                if (c.renderMode != RenderMode.ScreenSpaceOverlay &&
                    c.renderMode != RenderMode.ScreenSpaceCamera) continue;
                bool seen = false;
                foreach (var o in _canvasOverrides) if (o.C == c) { seen = true; break; }
                if (seen) continue;
                var overrideRec = new CanvasOverride { C = c, Mode = c.renderMode, Cam = c.worldCamera, BaseScale = c.scaleFactor, OriginalSorting = c.sortingOrder, Slot = _canvasOverrides.Count };
                _canvasOverrides.Add(overrideRec);
                _sharedScale = -1f;   // a new layer changes the reference height
                try
                {
                    EnsureUiLayer();
                    if (_uiLayer > 0) SetLayerRecursively(c.gameObject, _uiLayer);
                    c.renderMode = RenderMode.WorldSpace;
                    c.worldCamera = null;
                    // Deterministic layer order: the desktop screen composites
                    // several canvases by render order — world-space panels
                    // need the same guarantee (sortingOrder sorts the
                    // transparent queue).
                    c.sortingOrder = 10 + overrideRec.Slot;
                    ApplyHudTransform(overrideRec);
                }
                catch { }
            }
        }

        private void RestoreCanvases()
        {
            foreach (var o in _canvasOverrides)
            {
                if (o.C == null) continue;
                try { o.C.renderMode = o.Mode; o.C.worldCamera = o.Cam; o.C.scaleFactor = o.BaseScale; o.C.sortingOrder = o.OriginalSorting; } catch { }
            }
            _canvasOverrides.Clear();
        }

        /// <summary>
        /// World-space HUD conversion: the canvas becomes a physical panel
        /// floating in front of the head, sized so a HudWidth × HudHeight
        /// virtual-pixel window fits in view (she: "shrink everything into
        /// view, tighter toward the center"). World-space scale is NEVER
        /// recomputed by Unity — ScreenSpaceCamera scaleFactor was being
        /// clobbered per-frame, which is why the shrink "didn't work" — and
        /// VRUI.Update places the panel in front of the head every frame.
        /// </summary>
        private void ApplyHudTransform(CanvasOverride o)
        {
            try
            {
                var c = o.C;
                if (c == null) return;
                c.renderMode = RenderMode.WorldSpace;
                c.worldCamera = null;

                var rt = c.GetComponent<RectTransform>();
                if (rt == null) return;
                float dist = HudDistance();
                // VIRTUAL SCREEN (the "everything cleanly in one box" fix):
                // every converted canvas shares ONE pixel-to-world scale —
                // the reference height (the tallest canvas, i.e. the desktop
                // "screen") maps to the box, and every other layer keeps its
                // authored proportions relative to it. The old per-canvas
                // scale (worldH / own height) blew small canvases up to the
                // full box: the game's crosshair canvas rendered 2-4x too
                // big and the layers never aligned into one screen.
                c.scaleFactor = SharedScale(dist);
                var l = VRKoboldKarePlugin.Log;
                if (l != null) l.LogInfo(string.Format("VRUI: '{0}' {1:0}x{2:0}px -> {3:0.00}m tall at {4:0.0}m (shared scale {5:0.0000})",
                    c.name, rt.rect.width, rt.rect.height, rt.rect.height * SharedScale(dist), dist, SharedScale(dist)));
            }
            catch { }
        }

        /// <summary>Live re-apply after a HUD config change.</summary>
        internal void ReapplyHudScale()
        {
            _sharedScale = -1f;   // config changed — recompute the shared scale
            foreach (var o in _canvasOverrides)
            {
                if (o.C == null) continue;
                ApplyHudTransform(o);
            }
        }

        // ---- virtual screen state ----
        private float _sharedScale = -1f;

        private float HudDistance()
        {
            float hudDistCfg = 3f;
            try { if (!ReferenceEquals(_manager.Plugin, null) && _manager.Plugin.cfgHudDistance != null) hudDistCfg = _manager.Plugin.cfgHudDistance.Value; } catch (Exception) { }
            return HudMath.HudFollowDistance(hudDistCfg);
        }

        /// <summary>ONE px-to-world scale for ALL converted canvases: the
        /// tallest canvas (the desktop "screen") maps onto the HUD box — the
        /// fit fraction of the view at the follow distance, times the UI
        /// scale multiplier. Cached until a canvas/config changes.</summary>
        private float SharedScale(float dist)
        {
            if (_sharedScale > 0f) return _sharedScale;
            float refH = 1f;
            foreach (var o in _canvasOverrides)
            {
                if (o.C == null) continue;
                var rt = o.C.GetComponent<RectTransform>();
                if (rt != null) refH = Mathf.Max(refH, rt.rect.height);
            }
            float viewW = VRRenderer.CurrentViewW;
            float viewH = VRRenderer.CurrentViewH;
            float fit = HudMath.UiFitFraction(HudWidth(), HudHeight(), viewW, viewH);
            float uiScale = 1f;
            try { if (!ReferenceEquals(_manager.Plugin, null) && _manager.Plugin.cfgUIScale != null) uiScale = Mathf.Clamp(_manager.Plugin.cfgUIScale.Value, 0.1f, 4f); } catch (Exception) { }
            float worldH = 2f * dist * Mathf.Tan(VRRenderer.CurrentVFovDeg * 0.5f * 0.0174533f) * fit * uiScale;
            _sharedScale = worldH / Mathf.Max(1f, refH);
            return _sharedScale;
        }

        private float HudWidth()
        {
            try { if (!ReferenceEquals(_manager.Plugin, null) && _manager.Plugin.cfgHudWidth != null) return _manager.Plugin.cfgHudWidth.Value; } catch (Exception) { }
            return 900f;
        }
        private float HudHeight()
        {
            try { if (!ReferenceEquals(_manager.Plugin, null) && _manager.Plugin.cfgHudHeight != null) return _manager.Plugin.cfgHudHeight.Value; } catch (Exception) { }
            return 600f;
        }

        private void Update()
        {
            if (_manager == null || !_manager.Enabled) return;

            if (Time.unscaledTime >= _nextCanvasScan)
            {
                _nextCanvasScan = Time.unscaledTime + 2f;
                ScanCanvases();
            }

            SyncOverlayProjection();

            var input = _manager.Input;
            if (input == null || !input.FrameValid || !input.Frame.headTracked) return;

            Vector3 headPos = _manager.HeadToWorld(input.Frame.headPos, input.Frame.headRot);
            Quaternion headRot = _manager.ViewRotation(input.Frame.headRot);

            // Interaction prompt: aim from the head, show the hit object's name.
            UpdatePrompt(headPos, headRot);

            // Keep the radial menu anchored in front of the head.
            if (_radialVisible && _radialPanel != null)
            {
                _radialPanel.transform.position = headPos + headRot * new Vector3(0f, -0.2f, 1.2f);
                _radialPanel.transform.rotation = headRot;
            }

            // Converted HUD panels: one virtual screen — ALL layers at the
            // same follow distance (1mm z-stagger kills flicker between
            // overlapping quads), scaled by the shared px-to-world scale so
            // they composite exactly like the desktop screen. The canvas'
            // +Z along the view direction is the READABLE side.
            float dist = HudDistance();
            foreach (var o in _canvasOverrides)
            {
                if (o.C == null) continue;
                var t = o.C.transform;
                t.position = headPos + headRot * new Vector3(0f, 0f, dist + o.Slot * 0.001f);
                t.rotation = headRot;
            }
        }

        private void UpdatePrompt(Vector3 headPos, Quaternion headRot)
        {
            if (_promptPanel == null || _promptText == null) return;

            string label = "";
            try
            {
                // Nearest hit that is NOT our own body (its colliders surround
                // the camera and would make the prompt permanently visible).
                var kob = _manager.Kobold;
                RaycastHit best = default(RaycastHit);
                float bestDist = float.MaxValue;
                foreach (var hit in Physics.RaycastAll(headPos, headRot * Vector3.forward, 5f))
                {
                    if (kob != null)
                    {
                        var k = hit.collider.GetComponentInParent<Kobold>();
                        if (k == kob) continue;
                    }
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        best = hit;
                    }
                }
                if (best.collider != null && best.collider.gameObject != null)
                    label = best.collider.gameObject.name;
            }
            catch { }

            if (label.Length > 0)
            {
                _promptPanel.SetActive(true);
                _promptText.text = label;
                Vector3 pos = headPos + headRot * new Vector3(0f, -0.15f, 1.0f);
                _promptPanel.transform.position = pos;
                _promptPanel.transform.rotation = headRot;
            }
            else
            {
                _promptPanel.SetActive(false);
            }
        }

        // ------------------------------------------------------------------
        //  Construction
        // ------------------------------------------------------------------

        private void CreateCanvas()
        {
            EnsureUiLayer();
            _canvasRoot = new GameObject("VRKK_UICanvas");
            DontDestroyOnLoad(_canvasRoot);
            _canvas = _canvasRoot.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 50;
            var rect = _canvasRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(800f, 450f);
            // World-space scale: panels are authored in UI units (prompt =
            // 400x80) — at scale 1 that's a 400-METER plane in front of your
            // face. 0.0005 brings a 400-unit panel down to 0.2m.
            rect.localScale = new Vector3(0.0005f, 0.0005f, 0.0005f);
            if (_uiLayer > 0) SetLayerRecursively(_canvasRoot, _uiLayer);
        }

        private void CreatePrompt()
        {
            _promptPanel = new GameObject("VRKK_Prompt");
            _promptPanel.transform.SetParent(_canvasRoot.transform, false);
            var img = _promptPanel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);
            var rect = _promptPanel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 80f);

            // Text must live on a CHILD — one Graphic component per GameObject.
            var textObj = new GameObject("VRKK_PromptText");
            textObj.transform.SetParent(_promptPanel.transform, false);
            _promptText = textObj.AddComponent<Text>();
            _promptText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _promptText.color = Color.white;
            _promptText.fontSize = 32;
            _promptText.alignment = TextAnchor.MiddleCenter;
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            _promptPanel.SetActive(false);
        }

        private void CreateRadial()
        {
            _radialPanel = new GameObject("VRKK_Radial");
            _radialPanel.transform.SetParent(_canvasRoot.transform, false);
            var img = _radialPanel.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);
            var rect = _radialPanel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(500f, 500f);
            _radialPanel.SetActive(false);
        }

        void OnDestroy()
        {
            RestoreCanvases();
            if (_canvasRoot != null) Destroy(_canvasRoot);
        }
    }
}