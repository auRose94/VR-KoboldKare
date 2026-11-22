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

        private sealed class CanvasOverride { public Canvas C; public RenderMode Mode; public Camera Cam; public float BaseScale; }
        private readonly List<CanvasOverride> _canvasOverrides = new List<CanvasOverride>();
        private float _nextCanvasScan;
        private const float NearPlaneDistance = 15f;

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
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                bool seen = false;
                foreach (var o in _canvasOverrides) if (o.C == c) { seen = true; break; }
                if (seen) continue;
                var overrideRec = new CanvasOverride { C = c, Mode = c.renderMode, Cam = c.worldCamera, BaseScale = c.scaleFactor };
                _canvasOverrides.Add(overrideRec);
                try
                {
                    c.worldCamera = cam;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    ApplyHudTransform(c, overrideRec.BaseScale);
                }
                catch { }
            }
        }

        private void RestoreCanvases()
        {
            foreach (var o in _canvasOverrides)
            {
                if (o.C == null) continue;
                try { o.C.renderMode = o.Mode; o.C.worldCamera = o.Cam; o.C.scaleFactor = o.BaseScale; } catch { }
            }
            _canvasOverrides.Clear();
        }

        /// <summary>
        /// Shared HUD transform: occlusion-safe 15m plane + FOV-ratio and
        /// virtual-distance scale. Re-invoked on HudDistance config changes so
        /// tuning is live without restarting.
        /// </summary>
        private void ApplyHudTransform(Canvas c, float baseScale)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                float virtualDist = 400f;
                if (!ReferenceEquals(_manager.Plugin, null) && _manager.Plugin.cfgHudDistance != null)
                    virtualDist = Mathf.Max(HudMath.MinVirtualDistance, _manager.Plugin.cfgHudDistance.Value);
                c.planeDistance = HudMath.NearPlaneDistance;
                c.scaleFactor = HudMath.HudScale(baseScale, VRRenderer.CurrentVFovDeg,
                    cam.fieldOfView, virtualDist, 4f);
            }
            catch { }
        }

        /// <summary>Live re-apply after a HudDistance config change.</summary>
        internal void ReapplyHudScale()
        {
            foreach (var o in _canvasOverrides)
            {
                if (o.C == null) continue;
                ApplyHudTransform(o.C, o.BaseScale);
            }
        }

        private void Update()
        {
            if (_manager == null || !_manager.Enabled) return;

            if (Time.unscaledTime >= _nextCanvasScan)
            {
                _nextCanvasScan = Time.unscaledTime + 2f;
                ScanCanvases();
            }

            var input = _manager.Input;
            if (input == null || !input.FrameValid || !input.Frame.headTracked) return;

            Vector3 headPos = _manager.HeadToWorld(input.Frame.headPos, input.Frame.headRot);
            Quaternion headRot = Quaternion.Euler(0f, _manager.YawOffset, 0f) * input.Frame.headRot;

            // Interaction prompt: aim from the head, show the hit object's name.
            UpdatePrompt(headPos, headRot);

            // Keep the radial menu anchored in front of the head.
            if (_radialVisible && _radialPanel != null)
            {
                _radialPanel.transform.position = headPos + headRot * new Vector3(0f, -0.2f, 1.2f);
                _radialPanel.transform.rotation = headRot;
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