// Ported from KK-LLM-NPC's OptionsMenuTab.cs (MIT, @auRose94) and adapted for
// VR-KoboldKare. The game's options menu is built on naelstrof's
// UnityScriptableSettings framework: each sub-tab ("View - Graphics", "View -
// Audio", …) holds a ScriptableSettingSpawner that renders every Setting
// ScriptableObject whose group matches its targetGroup, and
// SettingsManager.AddSetting() is the framework's own public registration API.
// This class rides that machinery instead of inventing a second UI:
//
//   1. one SettingGroup per category ("KK VR", "KK VR Motion", …),
//   2. config-backed Setting proxies (the BepInEx ConfigEntry stays the source
//      of truth — proxies read it on Load and write it on SetValue, so the
//      config file and this tab edit the same keys; the file watcher picks the
//      change up and applies live effects like the HUD rescale),
//   3. a cloned "View - Graphics" per category (keeps ScrollRect +
//      AutoScrollToSelection + the widget templates) with its inherited rows
//      cleared and targetGroup pointed at our group,
//   4. a cloned "Button - Graphics" per category wired to hide-then-show,
//      registered on the panel's HideAllViews so every OTHER tab hides ours.
//
// The tab appears in the MAIN MENU too (MainMenu hosts the same options
// panel), so settings are reachable before loading a map — on desktop, which
// is the point until a native VR menu exists.
//
// Labels resolve through the game's own localization table ("GUID"): entries
// are added to the loaded StringTable at runtime and the LocalizedString
// points at them (English text; the LLM mod's L10n translator can be grafted
// later). Build is best-effort: any failure logs once and the tab simply
// doesn't appear.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityScriptableSettings;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace VRKoboldKare
{
    // ------------------------------------------------------------------
    // Config-backed Setting proxies. BepInEx's ConfigEntry is the source of
    // truth: Load() reads it (never PlayerPrefs — the game's Save button
    // calls Save() on every Setting, and ours must not stomp the file),
    // SetValue() writes it (firing BepInEx's SettingChanged like any edit).
    // ------------------------------------------------------------------
    internal class VRSetGroup : SettingGroup { }

    internal class VRSetFloat : SettingFloatClamped
    {
        public ConfigEntry<float> Entry;
        private static readonly FieldInfo MinF = typeof(SettingFloatClamped).GetField("min", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo MaxF = typeof(SettingFloatClamped).GetField("max", BindingFlags.NonPublic | BindingFlags.Instance);

        public void Init(ConfigEntry<float> entry, float min, float max, LocalizedString label, SettingGroup group)
        {
            Entry = entry;
            try { MinF?.SetValue(this, min); MaxF?.SetValue(this, max); } catch (Exception) { }
            base.name = entry.Definition.Key + "_" + entry.Definition.Section;
            localizedName = label;
            this.group = group;
        }

        public override void SetValue(float value)
        {
            base.SetValue(value);
            if (Entry != null) { Entry.Value = value; OptionsMenuTab.LogSetting(Entry, value); }
        }

        public override void Load()
        {
            SetValue(Entry != null ? Entry.Value : 0f);
        }

        public override void Save() { } // config already written at SetValue time

        public override void ResetToDefault()
        {
            if (Entry != null) Entry.Value = (float)Convert.ChangeType(Entry.DefaultValue ?? 0f, typeof(float));
        }
    }

    internal class VRSetIntClamped : SettingIntClamped
    {
        public ConfigEntry<int> Entry;
        private static readonly FieldInfo MinF = typeof(SettingIntClamped).GetField("min", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo MaxF = typeof(SettingIntClamped).GetField("max", BindingFlags.NonPublic | BindingFlags.Instance);

        public void Init(ConfigEntry<int> entry, int min, int max, LocalizedString label, SettingGroup group)
        {
            Entry = entry;
            try { MinF?.SetValue(this, min); MaxF?.SetValue(this, max); } catch (Exception) { }
            base.name = entry.Definition.Key + "_" + entry.Definition.Section;
            localizedName = label;
            this.group = group;
        }

        public override void SetValue(int value)
        {
            base.SetValue(value);
            if (Entry != null) { Entry.Value = value; OptionsMenuTab.LogSetting(Entry, value); }
        }

        public override void Load() { SetValue(Entry != null ? Entry.Value : 0); }
        public override void Save() { }

        public override void ResetToDefault()
        {
            if (Entry != null) Entry.Value = (int)Convert.ChangeType(Entry.DefaultValue ?? 0, typeof(int));
        }
    }

    // Free-range int → the spawner renders a TEXT INPUT.
    internal class VRSetIntText : SettingInt
    {
        public ConfigEntry<int> Entry;

        public void Init(ConfigEntry<int> entry, LocalizedString label, SettingGroup group)
        {
            Entry = entry;
            base.name = entry.Definition.Key + "_" + entry.Definition.Section;
            localizedName = label;
            this.group = group;
        }

        public override void SetValue(int value)
        {
            base.SetValue(value);
            if (Entry != null) { Entry.Value = value; OptionsMenuTab.LogSetting(Entry, value); }
        }

        public override void Load() { SetValue(Entry != null ? Entry.Value : 0); }
        public override void Save() { }

        public override void ResetToDefault()
        {
            if (Entry != null) Entry.Value = (int)Convert.ChangeType(Entry.DefaultValue ?? 0, typeof(int));
        }
    }

    // Bool → two-option dropdown (Disabled / Enabled) — the game's framework
    // has no toggle widget; the game's own bool-ish settings use dropdowns too.
    internal class VRSetBool : SettingDropdown
    {
        public ConfigEntry<bool> Entry;
        private static readonly FieldInfo OptsF = typeof(SettingDropdown).GetField("dropdownOptions", BindingFlags.NonPublic | BindingFlags.Instance);

        public void Init(ConfigEntry<bool> entry, LocalizedString label, SettingGroup group)
        {
            Entry = entry;
            try { OptsF?.SetValue(this, new string[] { "Disabled", "Enabled" }); } catch (Exception) { }
            base.name = entry.Definition.Key + "_" + entry.Definition.Section;
            localizedName = label;
            this.group = group;
        }

        public override void SetValue(int value)
        {
            base.SetValue(value);
            if (Entry != null) { Entry.Value = value >= 1; OptionsMenuTab.LogSetting(Entry, value >= 1); }
        }

        public override void Load() { SetValue(Entry != null && Entry.Value ? 1 : 0); }
        public override void Save() { }

        public override void ResetToDefault()
        {
            if (Entry != null) Entry.Value = Convert.ToBoolean(Entry.DefaultValue ?? false);
        }
    }

    internal class VRSetString : SettingString
    {
        public ConfigEntry<string> Entry;

        public void Init(ConfigEntry<string> entry, LocalizedString label, SettingGroup group)
        {
            Entry = entry;
            base.name = entry.Definition.Key + "_" + entry.Definition.Section;
            localizedName = label;
            this.group = group;
        }

        public override void SetValue(string value)
        {
            base.SetValue(value);
            if (Entry != null) { Entry.Value = value; OptionsMenuTab.LogSetting(Entry, value); }
        }

        public override void Load() { SetValue(Entry != null ? Entry.Value ?? "" : ""); }
        public override void Save() { }

        public override void ResetToDefault()
        {
            if (Entry != null) Entry.Value = Convert.ToString(Entry.DefaultValue ?? "");
        }
    }

    // ------------------------------------------------------------------
    // The tab builder.
    // ------------------------------------------------------------------
    internal static class OptionsMenuTab
    {
        private static VRKoboldKarePlugin Plugin;

        // One row per exposed key: (section, key, label, sliderMin, sliderMax,
        // forceTextInput, needsRestart). Rows without a slider range render as
        // text inputs (float/int) or dropdowns (bool). Label "" = the key.
        private struct Opt
        {
            public string S, K, L;
            public float Min, Max;
            public bool Text, Restart;
            public Opt(string s, string k, string l) : this(s, k, l, 0f, 0f, false, false) { }
            public Opt(string s, string k, string l, float min, float max, bool text = false, bool restart = false)
            { S = s; K = k; L = l; Min = min; Max = max; Text = text; Restart = restart; }
        }

        // (tab title, sections) → the curated keys, grouped the way players
        // think about them. The game's own Save button persists edits to the
        // BepInEx config file (identical to a file edit — the file watcher
        // then applies live effects like the HUD rescale).
        private static readonly string[][] Tabs =
        {
            new[] { "KK VR",         "VR.General", "VR.Render", "VR.DesktopKeys" },
            new[] { "KK VR Motion",  "VR.Locomotion" },
            new[] { "KK VR Grabs",   "VR.Grab" },
            new[] { "KK VR Camera",  "VR.Camera" },
            new[] { "KK VR UI",      "VR.UI" },
            new[] { "KK VR Ghost",   "VR.Spectate", "VR.Network" },
        };

        private static readonly Opt[] Opts =
        {
            // ---- General / Render ----
            new Opt("VR.General", "Headset", "Backend preference", 0, 0, true),
            new Opt("VR.General", "MirrorMode", "Mirror mode"),
            new Opt("VR.General", "ShowHandModels", "Show hand models"),
            new Opt("VR.General", "HandModelScale", "Hand model scale", 0.25f, 3f),
            new Opt("VR.Render", "Mode", "Render mode", 0, 0, true, true),
            new Opt("VR.Render", "EyeScale", "Eye scale", 0.25f, 1f),
            new Opt("VR.Render", "FlipY", "Flip image vertically"),
            new Opt("VR.Render", "HidePlayerBody", "Hide player body (FP)"),
            new Opt("VR.Render", "DesktopPreview", "Desktop preview window"),
            // ---- Desktop hotkeys ----
            new Opt("VR.DesktopKeys", "ScreenshotKey", "Screenshot key", 0, 0, true),
            new Opt("VR.DesktopKeys", "CameraCycleKey", "Camera cycle key", 0, 0, true),
            new Opt("VR.DesktopKeys", "RadialKey", "Radial menu key", 0, 0, true),
            new Opt("VR.DesktopKeys", "RagdollKey", "Ragdoll key", 0, 0, true),
            new Opt("VR.DesktopKeys", "PrecisionModeKey", "Precision mode key", 0, 0, true),
            // ---- Locomotion ----
            new Opt("VR.Locomotion", "Mode", "Locomotion mode"),
            new Opt("VR.Locomotion", "TeleportArc", "Teleport arc"),
            new Opt("VR.Locomotion", "SnapAngle", "Snap angle", 15f, 90f),
            new Opt("VR.Locomotion", "SmoothSpeed", "Smooth speed (m/s)", 0.5f, 8f),
            new Opt("VR.Locomotion", "MaxTeleportDistance", "Max teleport (m)", 2f, 30f),
            new Opt("VR.Locomotion", "SnapTurnLeftKey", "Snap turn left key", 0, 0, true),
            new Opt("VR.Locomotion", "SnapTurnRightKey", "Snap turn right key", 0, 0, true),
            new Opt("VR.Locomotion", "MouseYawSensitivity", "Mouse yaw / pixel", 0.02f, 1f),
            // ---- Grab ----
            new Opt("VR.Grab", "Mode", "Grab mode"),
            new Opt("VR.Grab", "SpringStrength", "Spring strength", 20f, 800f),
            new Opt("VR.Grab", "DampingStrength", "Damping strength", 2f, 100f),
            new Opt("VR.Grab", "MaxDistance", "Max grab distance (m)", 0.5f, 10f),
            new Opt("VR.Grab", "AnalogGrip", "Analog grip"),
            new Opt("VR.Grab", "GripThreshold", "Grip threshold", 0.05f, 1f),
            new Opt("VR.Grab", "DropThreshold", "Drop threshold", 0.05f, 1f),
            new Opt("VR.Grab", "MultiGrab", "Multi grab"),
            // ---- Camera ----
            new Opt("VR.Camera", "FOV", "FOV (mirror)", 60f, 120f),
            new Opt("VR.Camera", "FOVScale", "FOV scale", 0.5f, 2f),
            new Opt("VR.Camera", "MotionSickness", "Motion sickness FOV"),
            new Opt("VR.Camera", "ThirdPersonDistance", "Third person dist (m)", 1f, 6f),
            new Opt("VR.Camera", "ThirdPersonHeight", "Third person height (m)", 0f, 3f),
            new Opt("VR.Camera", "Mode", "Camera mode"),
            // ---- UI ----
            new Opt("VR.UI", "Scale", "UI scale", 0.25f, 4f),
            new Opt("VR.UI", "Distance", "UI distance (m)", 0.25f, 5f),
            new Opt("VR.UI", "ShowReticle", "Show reticle"),
            new Opt("VR.UI", "ShowPrompt", "Show interaction prompt"),
            new Opt("VR.UI", "PromptDistance", "Prompt distance (m)", 0.5f, 4f),
            new Opt("VR.UI", "HudDistance", "HUD distance (virtual)", 20f, 2000f, true),
            // ---- Ghost / spectate ----
            new Opt("VR.Spectate", "GhostOnEaten", "Ghost mode when eaten"),
            new Opt("VR.Spectate", "GhostFlySpeed", "Ghost fly speed (m/s)", 0.5f, 10f),
            // ---- Network ----
            new Opt("VR.Network", "SyncBody", "Sync body position"),
            new Opt("VR.Network", "SyncHands", "Sync hand positions"),
            new Opt("VR.Network", "OwnershipRetry", "Ownership retry (s)", 1f, 10f),
        };

        // Entries that only apply on restart — the label says so honestly.
        private static readonly HashSet<string> RestartKeys = new HashSet<string>
        {
            "VR.Render|Mode",
        };

        internal static void Ensure(VRKoboldKarePlugin plugin)
        {
            Plugin = plugin;
        }

        private static void Log(string msg)
        {
            try { VRKoboldKarePlugin.Log.LogInfo("VR: " + msg); } catch (Exception) { }
        }

        // Settings-applied feedback (she couldn't tell whether a value stuck):
        // every proxy write logs the key + value — the config file, the
        // SettingChanged event and any hot-reload effects follow from here.
        internal static void LogSetting(ConfigEntryBase entry, object value)
        {
            try
            {
                if (entry != null && VRKoboldKarePlugin.Log != null)
                    VRKoboldKarePlugin.Log.LogInfo("VR: config " + entry.Definition.Section + ":"
                        + entry.Definition.Key + " set to " + value + " (live)");
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // THE MOD DOCTOR (menu no-show) — v0.4.0 rework after the 2026-11-23
        // log forensics. Two stacked root causes, both convicted from logs +
        // prefab YAML:
        //
        //   1. The old trigger — MainMenu.GetCurrentMode() ==
        //   MainMenuMode.Options — NEVER fires: the game doesn't switch modes
        //   for the options screen (the probe reports 'MainMenu' even while
        //   options are open — logged once per session for the record).
        //   KK-LLM-NPC v1.13.1 ships the same dead gate.
        //
        //   2. The ButtonPanel SHIPS with an active VerticalLayoutGroup that
        //   flows ALL children (6 game + every mod-injected tab) — 18 × 78px
        //   rows overflow the ~475px panel, pushing modded tabs off the bottom
        //   (the Nov-23 dump's "stacked at -277" was the pre-layout baked
        //   positions, not the real failure).
        //
        // New trigger: the PANEL PROBE that provably works — a menu is open
        // when its ButtonPanel's game anchor button is active in hierarchy.
        // Twelve modded tabs then get COMPACT slots (measured growable space,
        // UiMath, unit-tested) instead of flowing off-screen, and the layout
        // self-heals against drift while the menu is open.
        //
        // Per-instance: the menu lives in DontDestroyOnLoad, but injection is
        // still marker-gated per ButtonPanel so any future pause-menu clone
        // gets its own pass.
        // ------------------------------------------------------------------

        private sealed class TabMarker : MonoBehaviour { }

        private sealed class PanelInfo
        {
            internal MainMenu Menu;
            internal Transform ButtonPanel;
            internal bool BuildFailed;   // Build() threw 20× — logged, not retried
            internal int BuildAttempts;
            internal bool WasOpen;       // open-state edge tracking
            // Drift sentinel (self-healing while the menu is open): the first
            // modded button's placement + the panel size. Anything that
            // re-flows the grid (a re-enabled layout group, another mod's
            // relayout) gets reverted within one poll tick.
            internal bool SentinelValid;
            internal Vector2 SentPos;
            internal Vector3 SentScale;
            internal Vector2 SentPanelSize;
            // Full-size tab list scrolling (18 buttons can't fit the ~475px
            // viewport — the mouse wheel pans them inside a masked panel).
            internal float Scroll;
            internal float MaxScroll;
            internal readonly Dictionary<RectTransform, Vector2> BasePositions =
                new Dictionary<RectTransform, Vector2>();
        }

        private static readonly List<PanelInfo> _panels = new List<PanelInfo>();
        private static float _nextPanelPoll;
        private static bool _modeProbeLogged;
        private static bool _locWaitLogged;
        private const float ScrollStep = 45f;   // px per wheel notch

        private static void PollOpenMenu()
        {
            HandleScroll();   // every frame — the wheel must feel live

            if (Time.unscaledTime < _nextPanelPoll) return;
            _nextPanelPoll = Time.unscaledTime + 0.25f;

            // Discover instances. FindObjectsOfType misses inactive roots, so
            // a pause-menu clone joins the cache on its first activation.
            foreach (var menu in UnityEngine.Object.FindObjectsOfType<MainMenu>())
            {
                if (menu == null) continue;
                bool known = false;
                foreach (var p in _panels)
                    if (ReferenceEquals(p.Menu, menu)) { known = true; break; }
                if (known) continue;
                // Canonical path first (View - Options → Body → ButtonPanel);
                // a bare-name fallback catches other panel hosts.
                Transform bp = null;
                var optionsRoot = FindChild(menu.transform, "View - Options");
                var body = optionsRoot != null ? FindChild(optionsRoot, "Body") : null;
                if (body != null) bp = FindChild(body, "ButtonPanel");
                if (bp == null) bp = FindChild(menu.transform, "ButtonPanel");
                _panels.Add(new PanelInfo { Menu = menu, ButtonPanel = bp });
            }
            for (int i = _panels.Count - 1; i >= 0; i--)
                if (_panels[i].Menu == null || _panels[i].ButtonPanel == null)
                    _panels.RemoveAt(i);   // scene churn destroyed this instance

            foreach (var p in _panels)
            {
                if (p.ButtonPanel == null) continue;

                // Inject once per panel instance (marker-gated). The 0.4.0
                // session proved Build must WAIT for the localization system:
                // running it at boot produced empty LocalizedString references,
                // whose GetLocalizedString() threw "Empty Table Reference"
                // inside the game's spawner and aborted the settings rows
                // (her "only shows a label with the text 'Label'").
                if (p.ButtonPanel.GetComponent<TabMarker>() == null)
                {
                    var cfg = !ReferenceEquals(Plugin, null) ? Plugin.cfgOptionsMenuTab : null;
                    if (cfg != null && !cfg.Value)
                    {
                        p.ButtonPanel.gameObject.AddComponent<TabMarker>();   // disabled — claim, don't build
                    }
                    else if (!LocalizationReady())
                    {
                        if (!_locWaitLogged)
                        {
                            _locWaitLogged = true;
                            Log("deferring tab injection until localization finishes initializing");
                        }
                    }
                    else
                    {
                        try
                        {
                            bool wasOpen = p.WasOpen;
                            Build(p.Menu);
                            p.ButtonPanel.gameObject.AddComponent<TabMarker>();
                            Log("KK VR tabs injected into '" + p.Menu.gameObject.scene.name + "' menu");
                            if (wasOpen) { try { ApplyModdedLayout(p); } catch (Exception) { } }
                        }
                        catch (Exception e)
                        {
                            p.BuildAttempts++;
                            if (p.BuildAttempts >= 20)
                            {
                                p.BuildFailed = true;
                                Log("tabs injection failed after 20 attempts (" + p.Menu.gameObject.scene.name + "): " + e.Message);
                            }
                            else if ((p.BuildAttempts & (p.BuildAttempts - 1)) == 0)   // log attempts 1,2,4,8,16
                            {
                                Log("tabs injection deferred (" + p.Menu.gameObject.scene.name + "): " + e.Message);
                            }
                        }
                    }
                }

                HandleOpenEdge(p);
            }
        }

        private static bool LocalizationReady()
        {
            try { return LocalizationSettings.InitializationOperation.IsDone; }
            catch (Exception) { return true; }   // API shape changed — let Loc() try and fail loudly
        }

        // Wheel-scroll the full-size tab list while the cursor is over the
        // ButtonPanel. Runs EVERY frame (the 0.25s poll would feel chunky).
        // Scrolling only fires over the button column — the settings list on
        // the right keeps its own ScrollRect wheel handling.
        private static void HandleScroll()
        {
            UnityEngine.InputSystem.Mouse mouse;
            try { mouse = UnityEngine.InputSystem.Mouse.current; }
            catch (Exception) { return; }
            if (mouse == null) return;
            float dy = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(dy) < 0.001f) return;
            Vector2 mp = mouse.position.ReadValue();
            foreach (var p in _panels)
            {
                if (!p.WasOpen || !p.SentinelValid) continue;
                var rt = p.ButtonPanel as RectTransform;
                if (rt == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, mp, null)) continue;
                // Units vary by Input System settings — treat any nonzero
                // delta as one notch step.
                float next = Mathf.Clamp(p.Scroll + (dy > 0 ? -1f : 1f) * ScrollStep, 0f, p.MaxScroll);
                if (Mathf.Approximately(next, p.Scroll)) continue;
                p.Scroll = next;
                ApplyScroll(p);
            }
        }

        private static void ApplyScroll(PanelInfo p)
        {
            var rt = p.ButtonPanel as RectTransform;
            if (rt == null) return;
            for (int i = 0; i < p.ButtonPanel.childCount; i++)
            {
                var c = p.ButtonPanel.GetChild(i) as RectTransform;
                if (c == null) continue;
                Vector2 basePos;
                if (p.BasePositions.TryGetValue(c, out basePos))
                    c.anchoredPosition = basePos + new Vector2(0f, p.Scroll);
            }
            var first = FirstModdedButton(p.ButtonPanel);
            if (first != null)
            {
                p.SentPos = first.anchoredPosition;
                p.SentScale = first.localScale;
            }
            p.SentPanelSize = rt.sizeDelta;
        }

        private static void HandleOpenEdge(PanelInfo p)
        {
            Transform anchor = FindGameAnchor(p.ButtonPanel);
            bool open = anchor != null && anchor.gameObject.activeInHierarchy;
            if (open != p.WasOpen)
            {
                p.WasOpen = open;
                if (!open) return;

                // For the record, once: what the old (dead) gate actually
                // returns while the options screen is visibly open.
                if (!_modeProbeLogged)
                {
                    _modeProbeLogged = true;
                    try { Log("options open — MainMenu.GetCurrentMode() reports '" + MainMenu.GetCurrentMode() +
                              "' (the old relayout gate — logged for the record)"); }
                    catch (Exception e) { Log("options open — MainMenu.GetCurrentMode() threw: " + e.Message); }
                }

                try
                {
                    p.Scroll = 0f;   // each open starts at the top of the list
                    ApplyModdedLayout(p);
                }
                catch (Exception e) { Log("options relayout failed: " + e); }
                return;
            }

            // While OPEN: the drift sentinel. Hover must NOT be able to move
            // the grid — but anything else that re-flows it (a re-enabled
            // layout group, another mod's pass) is reverted within one poll.
            if (open && p.SentinelValid && LayoutDrifted(p))
            {
                Log("options layout drifted — re-asserting");
                try { ApplyModdedLayout(p); }
                catch (Exception e) { Log("options re-assert failed: " + e); }
            }
        }

        private static bool LayoutDrifted(PanelInfo p)
        {
            var bp = p.ButtonPanel as RectTransform;
            if (bp == null) return false;
            var first = FirstModdedButton(p.ButtonPanel);
            if (first == null) return false;
            const float eps = 0.5f;
            if ((first.anchoredPosition - p.SentPos).sqrMagnitude > eps * eps) return true;
            if ((first.localScale - p.SentScale).sqrMagnitude > eps * eps) return true;
            if ((bp.sizeDelta - p.SentPanelSize).sqrMagnitude > eps * eps) return true;
            return false;
        }

        private static RectTransform FirstModdedButton(Transform bp)
        {
            var modded = CollectModdedButtons(bp);
            if (modded.Count == 0) return null;
            modded.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return modded[0];
        }

        // The game's six canonical category buttons — everything else in the
        // ButtonPanel is mod-injected (ours, KK-LLM-NPC's, future mods').
        private const float ModSlotHeight = 78f;   // the game's own button grid spacing

        private static readonly string[] GameButtonNames =
        {
            "Button - Gameplay", "Button - Controls", "Button - Audio",
            "Button - Graphics", "Button - Analytics", "Button - Multiplayer",
        };

        private static Transform FindGameAnchor(Transform bp)
        {
            string last = GameButtonNames[GameButtonNames.Length - 1];
            for (int i = 0; i < bp.childCount; i++)
                if (bp.GetChild(i).name == last) return bp.GetChild(i);
            return null;
        }

        private static List<RectTransform> CollectModdedButtons(Transform bp)
        {
            var modded = new List<RectTransform>();
            for (int i = 0; i < bp.childCount; i++)
            {
                var c = bp.GetChild(i);
                bool game = false;
                foreach (var g in GameButtonNames) if (c.name == g) { game = true; break; }
                if (game) continue;
                var crt = c as RectTransform;
                if (crt != null && c.GetComponent<Button>() != null) modded.Add(crt);
            }
            return modded;
        }

        // RELAYOUT (runs on every open edge and after any drift — idempotent):
        // the ButtonPanel SHIPS with an active VerticalLayoutGroup (spacing 8 +
        // 70px rows = the game's 78px grid) that flows ALL children — 6 game
        // buttons + every mod-injected one — straight off the bottom of a
        // ~475px panel once mods add more than a couple of tabs. THE Nov-23
        // "invisible tabs" mystery: the clones weren't stacked behind Graphics,
        // they were flowed below the panel by this group. Disable it and place
        // manually.
        //
        // 0.4.0 playtest verdicts folded in: (a) the half-size compaction is
        // OUT — full-size tabs at the game's own grid, and the over-long list
        // WHEEL-SCROLLS inside a RectMask2D-clipped panel instead of shrinking
        // (the old compaction was also fed by a coordinate-space bug: the grow
        // limit was measured in pivot-local space while the anchor Y is in
        // anchored space — apples vs oranges); (b) ButtonMouseOver stays
        // stripped: its default scale is captured by a Start() that runs
        // DEFERRED on runtime-injected clones (injected under the inactive
        // options view), and its OnDisable writes the UNINITIALIZED Vector3.zero
        // into localScale when the view toggles before Start ever ran — hover
        // then lerps the button toward 0 and it collapses into its left pivot.
        // Hover feedback survives via the Button's own color tint.
        private static void ApplyModdedLayout(PanelInfo p)
        {
            Transform bp = p.ButtonPanel;
            var rt = bp as RectTransform;
            if (rt == null) { Log("options-relayout: ButtonPanel is not a RectTransform"); return; }
            var lg = rt.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (lg != null && lg.enabled)
            {
                lg.enabled = false;
                Log("options-relayout: disabled the panel's VerticalLayoutGroup (it flows modded tabs off the panel bottom)");
            }
            Transform anchorT = FindGameAnchor(bp);
            var anchor = anchorT as RectTransform;
            if (anchor == null) { Log("options-relayout: no game anchor button found — skipped"); return; }

            var modded = CollectModdedButtons(bp);
            if (modded.Count == 0) return;   // game-only panel — nothing to do

            int stripped = 0;
            foreach (var m in modded)
            {
                var bmo = m.GetComponent<ButtonMouseOver>();
                if (bmo != null) { UnityEngine.Object.Destroy(bmo); stripped++; }
            }
            if (stripped > 0)
                Log("options-relayout: stripped hover-scale from " + stripped + " modded buttons (ButtonMouseOver zeroes runtime-injected clones)");

            // The mask gives the wheel scroll a hard clip at the panel's rect.
            if (rt.GetComponent<UnityEngine.UI.RectMask2D>() == null)
                rt.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            // FULL-SIZE tabs at the game's own grid spacing, below Multiplayer.
            modded.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            float anchorY = anchor.anchoredPosition.y;
            float x = anchor.anchoredPosition.x;
            for (int i = 0; i < modded.Count; i++)
            {
                modded[i].anchoredPosition = new Vector2(x, anchorY - (i + 1) * ModSlotHeight);
                modded[i].localScale = Vector3.one;
            }

            // Base positions for EVERY button (game + modded) — the scroll
            // pans the whole column together.
            p.BasePositions.Clear();
            int total = 0;
            for (int i = 0; i < bp.childCount; i++)
            {
                var c = bp.GetChild(i) as RectTransform;
                if (c == null || c.GetComponent<Button>() == null) continue;
                p.BasePositions[c] = c.anchoredPosition;
                total++;
            }

            // Scroll range: content span vs the panel viewport.
            p.MaxScroll = Mathf.Max(0f, ModSlotHeight * total - rt.rect.height + 4f);
            p.Scroll = Mathf.Clamp(p.Scroll, 0f, p.MaxScroll);
            ApplyScroll(p);
            p.SentinelValid = true;

            Log("options-relayout: " + modded.Count + " modded tabs at full " + ModSlotHeight.ToString("0") +
                "px slots (scroll range " + p.MaxScroll.ToString("0") + "px)");
        }

        // Cheap poll from the persistent manager's update loop (~0.25s): inject
        // once per menu instance the prefab exists, and re-fit the grid on
        // every options-open edge. Covers the main menu before any world load
        // and in-game pause menus when they are separate instances.
        internal static void Tick()
        {
            try { PollOpenMenu(); } catch (Exception) { }
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindChild(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static void Build(MainMenu menu)
        {
            var optionsRoot = FindChild(menu.transform, "View - Options");
            if (optionsRoot == null) throw new Exception("View - Options not found in the menu hierarchy");
            var body = FindChild(optionsRoot, "Body");
            var panel = body != null ? FindChild(body, "Panel") : null;
            var buttonPanel = body != null ? FindChild(body, "ButtonPanel") : null;
            if (panel == null || buttonPanel == null) throw new Exception("options Panel/ButtonPanel not found");
            Transform gfxView = null, gfxButton = null;
            for (int i = 0; i < panel.childCount; i++)
                if (panel.GetChild(i).name == "View - Graphics") { gfxView = panel.GetChild(i); break; }
            for (int i = 0; i < buttonPanel.childCount; i++)
                if (buttonPanel.GetChild(i).name.StartsWith("Button - Graphics")) { gfxButton = buttonPanel.GetChild(i); break; }
            if (gfxView == null || gfxButton == null) throw new Exception("View/Button - Graphics templates not found");
            var hideAll = buttonPanel.GetComponent<HideAllViews>();
            if (hideAll == null) throw new Exception("HideAllViews missing on ButtonPanel");

            foreach (var tab in Tabs)
            {
                string title = tab[0];
                var group = ScriptableObject.CreateInstance<VRSetGroup>();
                group.name = title.Replace(" ", "");
                group.localizedName = Loc(title);

                // VIEW: clone Graphics (ScrollRect + spawner + widget templates),
                // clear the game's inherited rows, point targetGroup at ours.
                var viewGo = UnityEngine.Object.Instantiate(gfxView.gameObject);
                viewGo.name = "View - " + title;
                viewGo.transform.SetParent(panel, false);
                viewGo.SetActive(false);
                var spawner = viewGo.GetComponentInChildren<ScriptableSettingSpawner>(true);
                if (spawner == null) throw new Exception("cloned view lost its ScriptableSettingSpawner");
                var tgF = typeof(ScriptableSettingSpawner).GetField("targetGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                tgF?.SetValue(spawner, group);
                ClearRows(spawner);

                // BUTTON: clone Graphics' button, label it through the game's own
                // localization (re-renders on language switch), hide-then-show.
                var btnGo = UnityEngine.Object.Instantiate(gfxButton.gameObject);
                btnGo.name = "Button - " + title;
                btnGo.transform.SetParent(buttonPanel, false);
                btnGo.SetActive(true);
                var btnLabelRef = Loc(title);
                var lse = btnGo.GetComponentInChildren<UnityEngine.Localization.Components.LocalizeStringEvent>(true);
                if (lse != null)
                {
                    // The game's localizer re-renders per language; give it OUR entry.
                    lse.StringReference = btnLabelRef;
                }
                var tmp = btnGo.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tmp != null) tmp.text = title; // immediate render (the LSE refreshes on locale change)
                var btn = btnGo.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent(); // drop the cloned persistent calls
                    var v = viewGo;
                    btn.onClick.AddListener(() =>
                    {
                        try { hideAll.HideThem(); } catch (Exception) { }
                        v.SetActive(true);
                    });
                }
                if (hideAll.views == null) hideAll.views = new List<GameObject>();
                hideAll.views.Add(viewGo);

                // SETTINGS: one proxy per curated key in this tab's sections.
                foreach (var opt in Opts)
                {
                    bool inTab = false;
                    foreach (var s in tab) if (s == opt.S) { inTab = true; break; }
                    if (!inTab) continue;
                    ConfigEntryBase entry = null;
                    try { entry = Plugin.Config[opt.S, opt.K]; } catch (Exception) { entry = null; }
                    if (entry == null) continue; // key renamed/removed — skip quietly
                    RegisterProxy(group, opt, entry);
                }
            }

            // If the panel DOES have a layout group, force a rebuild so the
            // new buttons land in flow rather than stacked.
            try { UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(buttonPanel.GetComponent<RectTransform>()); } catch (Exception) { }

            // Forensics for the "tabs injected but not visible" investigation:
            // dump what actually landed in the panels.
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("ButtonPanel children now:");
                for (int i = 0; i < buttonPanel.childCount; i++)
                {
                    var b = buttonPanel.GetChild(i);
                    var btn = b.GetComponent<Button>();
                    sb.Append(" [").Append(b.name)
                      .Append(b.gameObject.activeInHierarchy ? "" : " (inactive)")
                      .Append(btn != null && btn.isActiveAndEnabled ? "" : " (btn disabled)")
                      .Append("]");
                }
                VRKoboldKarePlugin.Log.LogInfo(sb.ToString());
                foreach (var tab in Tabs)
                {
                    var v = FindChild(panel, "View - " + tab[0]);
                    if (v != null)
                        VRKoboldKarePlugin.Log.LogInfo("View - " + tab[0] + ": active=" +
                            v.gameObject.activeInHierarchy + ", spawner rows=" + v.GetComponentInChildren<ScriptableSettingSpawner>(true).transform.childCount);
                }
            }
            catch (Exception ex) { VRKoboldKarePlugin.Log.LogWarning("menu dump failed: " + ex.Message); }
        }

        // Destroy the rows the game's own spawner spawned into the cloned view
        // (everything that is not one of the four widget templates).
        private static void ClearRows(ScriptableSettingSpawner spawner)
        {
            var keep = new HashSet<Transform>();
            foreach (var f in new[] { spawner.slider, spawner.dropdown, spawner.textInput, spawner.groupTitle })
                if (f != null) keep.Add(f.transform);
            for (int i = spawner.transform.childCount - 1; i >= 0; i--)
            {
                var c = spawner.transform.GetChild(i);
                if (!keep.Contains(c)) UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        private static void RegisterProxy(SettingGroup group, Opt opt, ConfigEntryBase entry)
        {
            string label = string.IsNullOrEmpty(opt.L) ? opt.K : opt.L;
            if (opt.Restart || RestartKeys.Contains(opt.S + "|" + opt.K)) label += " (restart)";
            var loc = Loc(label);
            if (entry.SettingType == typeof(bool))
            {
                var p = ScriptableObject.CreateInstance<VRSetBool>();
                p.Init((ConfigEntry<bool>)entry, loc, group);
                SettingsManager.AddSetting(p);
            }
            else if (entry.SettingType == typeof(int))
            {
                if (opt.Max > opt.Min)
                {
                    var p = ScriptableObject.CreateInstance<VRSetIntClamped>();
                    p.Init((ConfigEntry<int>)entry, (int)opt.Min, (int)opt.Max, loc, group);
                    SettingsManager.AddSetting(p);
                }
                else
                {
                    var p = ScriptableObject.CreateInstance<VRSetIntText>();
                    p.Init((ConfigEntry<int>)entry, loc, group);
                    SettingsManager.AddSetting(p);
                }
            }
            else if (entry.SettingType == typeof(float))
            {
                var p = ScriptableObject.CreateInstance<VRSetFloat>();
                float min = opt.Max > opt.Min ? opt.Min : 0f;
                float max = opt.Max > opt.Min ? opt.Max : 1f;
                p.Init((ConfigEntry<float>)entry, min, max, loc, group);
                SettingsManager.AddSetting(p);
            }
            else if (entry.SettingType == typeof(string))
            {
                var p = ScriptableObject.CreateInstance<VRSetString>();
                p.Init((ConfigEntry<string>)entry, loc, group);
                SettingsManager.AddSetting(p);
            }
        }

        // Labels resolve through the game's own localization table. Its
        // SharedData asset is "NewStringDatabase Shared Data" with
        // TableCollectionName "StringDatabase" (asset GUID
        // 7b0547fe1d039e3448e29797d2fc4463) — and the game's own
        // LocalizeStringEvents reference it in GUID MODE: the serialized
        // "m_TableCollectionName: GUID:7b0547..." that the 0.3.0 port misread
        // as a table NAMED "GUID". GetTable("GUID") therefore always returned
        // null, our Build never succeeded, and the tabs seen in the 0.4.0
        // playtests were KK-LLM-NPC's.
        private static readonly System.Guid LocTableGuid =
            new System.Guid("7b0547fe1d039e3448e29797d2fc4463");

        private static LocalizedString Loc(string text)
        {
            if (!TryLoc(text, out LocalizedString ls))
                throw new Exception("localization table 'StringDatabase' unavailable (deferred)");
            return ls;
        }

        private static bool TryLoc(string text, out LocalizedString ls)
        {
            ls = new LocalizedString();
            try
            {
                var op = LocalizationSettings.InitializationOperation;
                if (!op.IsDone) op.WaitForCompletion();   // main-thread block — once, at Build time
                TableReference tableRef = LocTableGuid;   // implicit Guid → GUID-mode reference, same as the game's own UI
                var table = LocalizationSettings.StringDatabase.GetTable(tableRef);
                if (table == null || table.SharedData == null) return false;
                var keyEntry = table.SharedData.AddKey();
                var localeProvider = LocalizationSettings.AvailableLocales;
                var locales = localeProvider != null ? localeProvider.Locales : null;
                if (locales != null)
                {
                    foreach (var loc in locales)
                    {
                        try
                        {
                            var tbl = LocalizationSettings.StringDatabase.GetTable(tableRef, loc);
                            if (tbl != null)
                                tbl.AddEntry(keyEntry.Key, text);
                        }
                        catch (Exception) { }
                    }
                }
                ls.SetReference(tableRef, keyEntry.Key);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
