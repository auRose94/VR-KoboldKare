// VRGhostHud — the swallowed-spectate watch overlay + struggle/point gestures.
//
// While KK-LLM-NPC's spectate runs (the local player eaten), this renders the
// watch status (host, hatch promise, struggle meter) on a small head-follow
// panel, and maps VR gestures to the escape/point routes the flat game polls
// SPACE and the mouse click for:
//   grip mash (either hand, edge-triggered)  → ModHooks.SubmitPlayerStruggle()
//   trigger press (right)                    → gaze raycast → SubmitPreyPoint()
//
// All ModHooks access is reflection + fail-soft: works on KK-LLM-NPC v1.13+,
// silently inert on older builds or without it.
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace VRKoboldKare
{
    public class VRGhostHud : MonoBehaviour
    {
        private VRManager _m;

        private GameObject _panel;
        private Text _text;
        private RectTransform _gaugeFill;
        private Image _gaugeFillImg;

        // reflection cache (ModHooks surface)
        private bool _hooksInit;
        private MethodInfo _mhHudLine, _mhStruggle01, _mhSubmitStruggle, _mhSubmitPoint, _mhSpectating;

        private bool _prevGripL, _prevGripR;
        private bool _prevTrigR;
        private float _nextPoll;

        internal void Initialize(VRManager m)
        {
            _m = m;
            GhostHooks.Ensure();
            Build();
            if (VRKoboldKarePlugin.Log != null)
                VRKoboldKarePlugin.Log.LogInfo("VR: ghost watch HUD armed (grip mash = struggle, trigger = point)");
        }

        // ------------------------------------------------------------------
        //  ModHooks reflection surface (fail-soft)
        // ------------------------------------------------------------------

        private static class GhostHooks
        {
            internal static bool Init;
            private static Type _mh;
            private static MethodInfo _hud, _struggle, _submit, _point, _spectating;

            internal static void Ensure()
            {
                if (Init) return;
                Init = true;
                try
                {
                    _mh = Type.GetType("KKLLMNPC.ModHooks, KKLLMNPC");
                    if (_mh == null) return;
                    _hud = _mh.GetMethod("SpectateHudLine",
                        BindingFlags.Public | BindingFlags.Static);
                    _struggle = _mh.GetMethod("SpectateStruggle01",
                        BindingFlags.Public | BindingFlags.Static);
                    _submit = _mh.GetMethod("SubmitPlayerStruggle",
                        BindingFlags.Public | BindingFlags.Static);
                    _spectating = _mh.GetMethod("PlayerSpectating", // get_ via invoke on the property backing? — property: use GetProperty
                        BindingFlags.Public | BindingFlags.Static);
                }
                catch (Exception) { }
            }

            internal static string HudLine()
            {
                Ensure();
                try { return _hud?.Invoke(null, null) as string; } catch (Exception) { return null; }
            }

            internal static float Struggle01()
            {
                Ensure();
                try
                {
                    var o = _struggle?.Invoke(null, null);
                    return o is float f ? f : 0f;
                }
                catch (Exception) { return 0f; }
            }

            internal static bool SubmitStruggle()
            {
                Ensure();
                try
                {
                    var o = _submit?.Invoke(null, null);
                    return o is bool b && b;
                }
                catch (Exception) { return false; }
            }

            internal static string SubmitPoint(Vector3 pos, string name)
            {
                Ensure();
                try
                {
                    var point = _mh?.GetMethod("SubmitPreyPoint",
                        BindingFlags.Public | BindingFlags.Static);
                    return point?.Invoke(null, new object[] { pos, name }) as string;
                }
                catch (Exception) { return null; }
            }

            internal static bool Spectating()
            {
                Ensure();
                try
                {
                    var p = _mh?.GetProperty("PlayerSpectating", BindingFlags.Public | BindingFlags.Static);
                    var o = p?.GetValue(null, null);
                    return o is bool b && b;
                }
                catch (Exception) { return false; }
            }
        }

        // ------------------------------------------------------------------
        //  Panel construction
        // ------------------------------------------------------------------

        private void Build()
        {
            try
            {
                var ui = _m.UI;
                int layer = ui != null ? ui.UiLayer : -1;

                _panel = new GameObject("VRKK_GhostHud");
                if (layer > 0) _m.UI.SetLayerRecursivelyInternal(_panel, layer);
                var canvas = _panel.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 70;
                var root = _panel.GetComponent<RectTransform>();
                root.sizeDelta = new Vector2(420f, 150f);
                root.localScale = Vector3.one * 0.0007f;

                var bg = new GameObject("VRKK_GhostBG");
                bg.transform.SetParent(_panel.transform, false);
                var bgImg = bg.AddComponent<Image>();
                bgImg.color = new Color(0f, 0f, 0f, 0.72f);
                var bgRect = bg.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero;
                bgRect.offsetMax = Vector2.zero;

                var textObj = new GameObject("VRKK_GhostText");
                textObj.transform.SetParent(_panel.transform, false);
                _text = textObj.AddComponent<Text>();
                _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _text.color = Color.white;
                _text.fontSize = 30;
                _text.alignment = TextAnchor.UpperCenter;
                var tr = textObj.GetComponent<RectTransform>();
                tr.anchorMin = new Vector2(0f, 0.28f);
                tr.anchorMax = Vector2.one;
                tr.offsetMin = Vector2.zero;
                tr.offsetMax = Vector2.zero;

                // Struggle gauge: dark back + fill anchored to the meter value.
                var back = new GameObject("VRKK_GaugeBack");
                back.transform.SetParent(_panel.transform, false);
                var backImg = back.AddComponent<Image>();
                backImg.color = new Color(0.25f, 0.08f, 0.08f, 0.9f);
                var backRect = back.GetComponent<RectTransform>();
                backRect.anchorMin = new Vector2(0.06f, 0.06f);
                backRect.anchorMax = new Vector2(0.94f, 0.24f);
                backRect.offsetMin = Vector2.zero;
                backRect.offsetMax = Vector2.zero;

                var fill = new GameObject("VRKK_GaugeFill");
                fill.transform.SetParent(back.transform, false);
                _gaugeFillImg = fill.AddComponent<Image>();
                _gaugeFillImg.color = new Color(0.9f, 0.25f, 0.15f, 0.95f);
                _gaugeFill = fill.GetComponent<RectTransform>();
                _gaugeFill.anchorMin = Vector2.zero;
                _gaugeFill.anchorMax = new Vector2(0f, 1f);   // width driven per frame
                _gaugeFill.offsetMin = Vector2.zero;
                _gaugeFill.offsetMax = Vector2.zero;

                _panel.SetActive(false);
            }
            catch (Exception e)
            {
                try { VRKoboldKarePlugin.Log.LogWarning("VR: ghost HUD build failed: " + e.Message); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------
        //  Per-frame
        // ------------------------------------------------------------------

        private void Update()
        {
            bool spectating;
            try { spectating = GhostHooks.Spectating(); } catch (Exception) { spectating = false; }
            if (_panel != null && _panel.activeSelf != spectating)
                _panel.SetActive(spectating);
            if (!spectating) return;

            var input = _m != null ? _m.Input : null;
            if (input == null || !input.FrameValid || !input.Frame.headTracked) return;

            var f = input.Frame;
            Vector3 headPos = _m.HeadToWorld(f.headPos, f.headRot);
            Quaternion headRot = Quaternion.Euler(0f, _m.YawOffset, 0f) * f.headRot;

            if (_panel != null)
            {
                var t = _panel.transform;
                t.position = headPos + headRot * new Vector3(0f, -0.12f, 1.1f);
                t.rotation = headRot;
            }

            // HUD text + struggle gauge (polled 4x/s — the meter is smoothed server-side).
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.25f;
                try
                {
                    string line = GhostHooks.HudLine() ?? "";
                    float s = GhostHooks.Struggle01();
                    if (_text != null)
                        _text.text = line + (line.Length > 0 ? "\n" : "") +
                            "STRUGGLE " + (s * 100f).ToString("0") + "% — grip mash to fight";
                    if (_gaugeFillImg != null)
                        _gaugeFillImg.color = Color.Lerp(new Color(0.4f, 0.8f, 0.3f, 0.95f),
                            new Color(0.9f, 0.2f, 0.1f, 0.95f), s);
                    if (_gaugeFill != null)
                        _gaugeFill.anchorMax = new Vector2(Mathf.Clamp01(s), 1f);
                }
                catch (Exception) { }
            }

            // Gestures: grip edge = struggle press; trigger edge = gaze point.
            bool gripL = f.left.grip > 0.5f;
            bool gripR = f.right.grip > 0.5f;
            bool trigR = f.right.trigger > 0.5f;
            bool gripEdge = (gripL && !_prevGripL) || (gripR && !_prevGripR);
            bool trigEdge = (trigR && !_prevTrigR);
            _prevGripL = gripL; _prevGripR = gripR; _prevTrigR = trigR;

            if (gripEdge)
            {
                try
                {
                    if (GhostHooks.SubmitStruggle())
                    {
                        VRXR.Haptic(true, 0.7f, 60);
                        VRXR.Haptic(false, 0.7f, 60);
                    }
                }
                catch (Exception) { }
            }

            if (trigEdge)
            {
                try
                {
                    Vector3 fwd = headRot * Vector3.forward;
                    RaycastHit hit;
                    if (Physics.Raycast(headPos, fwd, out hit, 30f))
                    {
                        string fb = GhostHooks.SubmitPoint(hit.point,
                            hit.collider != null ? hit.collider.gameObject.name : null);
                        if (_text != null && !string.IsNullOrEmpty(fb))
                        {
                            var keep = _text.text;
                            _text.text = fb + "\n" + keep;
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        private void OnDestroy()
        {
            if (_panel != null) Destroy(_panel);
        }
    }
}
