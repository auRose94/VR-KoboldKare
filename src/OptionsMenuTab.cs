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
            if (Entry != null) Entry.Value = value;
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
            if (Entry != null) Entry.Value = value;
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
            if (Entry != null) Entry.Value = value;
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
            if (Entry != null) Entry.Value = value >= 1;
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
            if (Entry != null) Entry.Value = value;
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
        private static bool _built;
        private static int _throttle;
        private static bool _failed;

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
            new[] { "KK VR",         "VR.General", "VR.Render" },
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

        // THE MOD DOCTOR (menu no-show, ported from KK-LLM-NPC v1.13.1): runs
        // when the Options screen is actually OPEN (MainMenu.GetCurrentMode is
        // a public static — ShowMenu proved unreliable to patch), when rects
        // are laid out for real. Dumps the live layout, flows EVERY modded
        // button (ours AND KK-LLM-NPC's — both stacked invisibly behind
        // Graphics) into 78px slots below Multiplayer, disables the panel's
        // dead layout group, and expands the ButtonPanel downward-only to fit
        // (boot-time sizeDelta lied; the BG sibling paints the backdrop and
        // must stretch too). Idempotent — measures live on every open.
        private static bool _wasOptions;
        private static FieldInfo _mmInstanceF;

        private static void PollOpenMenu()
        {
            bool isOpen;
            try { isOpen = MainMenu.GetCurrentMode() == MainMenu.MainMenuMode.Options; }
            catch (Exception) { return; } // boot race: singleton not up yet — retried next tick
            try
            {
                if (!isOpen) { _wasOptions = false; return; }
                if (_wasOptions) return; // once per open
                _wasOptions = true;
                if (_mmInstanceF == null)
                    _mmInstanceF = typeof(MainMenu).GetField("instance",
                        BindingFlags.NonPublic | BindingFlags.Static);
                var menu = _mmInstanceF != null ? _mmInstanceF.GetValue(null) as MainMenu : null;
                if (menu == null) { Log("options-dump: MainMenu instance not reachable"); return; }
                var optionsRoot = FindChild(menu.transform, "View - Options");
                if (optionsRoot == null) { Log("options-dump: View - Options NOT FOUND"); return; }
                var body = FindChild(optionsRoot, "Body");
                var bp = body != null ? FindChild(body, "ButtonPanel") : null;
                if (bp == null) { Log("options-dump: ButtonPanel NOT FOUND"); return; }
                try { RelayoutModdedButtons(bp); } catch (Exception re) { Log("options-relayout failed: " + re); }
                try { FitButtonPanel(bp); } catch (Exception fe) { Log("options-fit failed hard: " + fe); }
            }
            catch (Exception e)
            {
                Log("options-dump failed: " + e);
            }
        }

        // The game's six canonical category buttons — everything else in the
        // ButtonPanel is mod-injected (ours, the VR rig's, future mods').
        private const float ModSlotHeight = 78f;   // the game's own button grid spacing

        private static readonly string[] GameButtonNames =
        {
            "Button - Gameplay", "Button - Controls", "Button - Audio",
            "Button - Graphics", "Button - Analytics", "Button - Multiplayer",
        };

        // RELAYOUT: the ButtonPanel has NO functioning runtime layout group —
        // clones spawn at the template's position and stack invisibly behind
        // Graphics. Re-flow every MODDED button (ours AND KK-LLM-NPC's,
        // name-sorted, deterministic) into 78px slots below the last game
        // button, then the fit expands the panel downward-only to cover them.
        private static void RelayoutModdedButtons(Transform bp)
        {
            var rt = bp as RectTransform;
            if (rt == null) return;
            var lg = rt.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (lg != null && lg.enabled)
            {
                lg.enabled = false;
                Log("options-relayout: disabled the panel's VerticalLayoutGroup (it was not laying out at runtime anyway)");
            }
            Transform anchor = null;
            var modded = new List<RectTransform>();
            for (int i = 0; i < bp.childCount; i++)
            {
                var c = bp.GetChild(i);
                bool game = false;
                foreach (var g in GameButtonNames) if (c.name == g) { game = true; break; }
                if (game) { if (c.name == GameButtonNames[GameButtonNames.Length - 1]) anchor = c; continue; }
                var crt = c as RectTransform;
                if (crt != null && c.GetComponent<Button>() != null) modded.Add(crt);
            }
            if (anchor == null) { Log("options-relayout: no game anchor button found — skipped"); return; }
            if (modded.Count == 0) return;
            modded.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var art = (RectTransform)anchor;
            float x = art.anchoredPosition.x;
            for (int i = 0; i < modded.Count; i++)
            {
                var m = modded[i];
                m.anchoredPosition = new Vector2(x, art.anchoredPosition.y - (i + 1) * ModSlotHeight);
                m.localScale = Vector3.one;
            }
            Log("options-relayout: " + modded.Count + " modded buttons flowed below " + anchor.name +
                " at " + ModSlotHeight.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "px slots");
        }

        private static void FitButtonPanel(Transform buttonPanel)
        {
            try
            {
                var rt = buttonPanel as RectTransform;
                if (rt == null) return;
                var lg = buttonPanel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                bool layoutLive = lg != null && lg.enabled;
                if (layoutLive && lg.childControlHeight && lg.childForceExpandHeight)
                {
                    Log("options-fit: layout controls heights — children squeeze automatically, no expansion needed");
                    return;
                }
                float total = 0f;
                int visible = 0;
                for (int i = 0; i < buttonPanel.childCount; i++)
                {
                    var c = buttonPanel.GetChild(i) as RectTransform;
                    if (c == null || !c.gameObject.activeSelf) continue;
                    total += Mathf.Max(0f, c.rect.height);
                    visible++;
                }
                if (layoutLive) total += lg.spacing * Mathf.Max(0, visible - 1)
                    + lg.padding.top + lg.padding.bottom;
                float current = rt.rect.height;
                if (total <= current + 1f)
                {
                    Log("options-fit: ButtonPanel " + current.ToString("0.#") + "px fits " + visible
                        + " buttons (" + total.ToString("0.#") + "px needed)");
                    return;
                }
                float deficit = total - current;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + deficit);
                // DOWNWARD-ONLY GROWTH: a center-pivoted rect grows both ways,
                // shoving the game buttons up into the Back button. Keep the
                // TOP edge fixed.
                rt.anchoredPosition -= new Vector2(0f, (1f - rt.pivot.y) * deficit);
                // The BG sibling paints the panel's backdrop — stretch it too.
                var bgTf = buttonPanel.parent != null ? buttonPanel.parent.Find("ButtonPanelBG") : null;
                var bg = bgTf as RectTransform;
                if (bg != null && Mathf.Approximately(bg.rect.height, current))
                {
                    bg.sizeDelta = new Vector2(bg.sizeDelta.x, bg.sizeDelta.y + deficit);
                    bg.anchoredPosition -= new Vector2(0f, (1f - bg.pivot.y) * deficit);
                }
                Log("options-fit: ButtonPanel expanded " + current.ToString("0.#") + " → " + total.ToString("0.#")
                    + "px for " + visible + " buttons");
            }
            catch (Exception e)
            {
                Log("options-fit failed: " + e.Message);
            }
        }

        private static bool _openStateDumped;

        // Cheap poll from the persistent manager's update loop: build once the
        // menu prefab and the SettingsManager singleton exist (a few seconds
        // after boot — works on the main menu, before any world load).
        internal static void Tick()
        {
            // The mod doctor: runs whether or not the build succeeded — the
            // open-menu relayout/fit is what makes the tabs visible.
            try { PollOpenMenu(); } catch (Exception) { }
            if (!_built || _failed)
            {
                var cfg = !ReferenceEquals(Plugin, null) ? Plugin.cfgOptionsMenuTab : null;
                if (cfg != null && !cfg.Value) { _built = true; return; }
                if (++_throttle % 30 != 0) return; // ~every 0.5s
                try
                {
                    var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                    if (menu == null) return;
                    Build(menu);
                    _built = true;
                    try { VRKoboldKarePlugin.Log.LogInfo("options menu: KK VR tabs injected"); } catch (Exception) { }
                }
                catch (Exception e)
                {
                    _failed = true; // one honest attempt — the config file remains the surface
                    try { VRKoboldKarePlugin.Log.LogWarning("options menu injection failed: " + e.Message); } catch (Exception) { }
                }
                return;
            }

            // OPEN-STATE FORENSICS: when the game's own tabs are visible (the
            // user opened Options), dump every tab button's state — the cloned
            // KK VR buttons exist in the closed-state dump, so the question is
            // what differs when the panel is actually shown (active state,
            // position off-screen, zero size).
            if (_openStateDumped) return;
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                if (menu == null) return;
                var optionsRoot = FindChild(menu.transform, "View - Options");
                var body = optionsRoot != null ? FindChild(optionsRoot, "Body") : null;
                var bp = body != null ? FindChild(body, "ButtonPanel") : null;
                if (bp == null) return;
                Transform gp = null;
                for (int i = 0; i < bp.childCount; i++)
                    if (bp.GetChild(i).name == "Button - Gameplay") { gp = bp.GetChild(i); break; }
                if (gp == null || !gp.gameObject.activeInHierarchy) return;   // menu still closed

                _openStateDumped = true;
                var sb = new System.Text.StringBuilder("options open — ButtonPanel state:");
                for (int i = 0; i < bp.childCount; i++)
                {
                    var b = bp.GetChild(i);
                    var rt = b.GetComponent<RectTransform>();
                    sb.Append(" [").Append(b.name)
                      .Append(" self=").Append(b.gameObject.activeSelf ? 1 : 0)
                      .Append(" hier=").Append(b.gameObject.activeInHierarchy ? 1 : 0);
                    if (rt != null)
                        sb.Append(" pos=(").Append(rt.anchoredPosition.x.ToString("0"))
                          .Append(",").Append(rt.anchoredPosition.y.ToString("0"))
                          .Append(") size=(").Append(rt.rect.width.ToString("0"))
                          .Append("x").Append(rt.rect.height.ToString("0")).Append(")");
                    sb.Append("]");
                }
                VRKoboldKarePlugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                _openStateDumped = true;
                try { VRKoboldKarePlugin.Log.LogWarning("open-state dump failed: " + ex.Message); } catch (Exception) { }
            }
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

        // Labels resolve through the game's own localization table (named
        // "GUID" — what every in-game LocalizeStringEvent references): add a
        // runtime entry, point the LocalizedString at it. English text in every
        // available locale. Failure = blank label, tab still functional.
        private static LocalizedString Loc(string text)
        {
            var ls = new LocalizedString();
            try
            {
                var table = LocalizationSettings.StringDatabase.GetTable("GUID");
                if (table == null || table.SharedData == null) return ls;
                var keyEntry = table.SharedData.AddKey();
                var localeProvider = LocalizationSettings.AvailableLocales;
                var locales = localeProvider != null ? localeProvider.Locales : null;
                if (locales != null)
                {
                    foreach (var loc in locales)
                    {
                        try
                        {
                            var tbl = LocalizationSettings.StringDatabase.GetTable("GUID", loc);
                            if (tbl != null)
                                tbl.AddEntry(keyEntry.Key, text);
                        }
                        catch (Exception) { }
                    }
                }
                ls.SetReference("GUID", keyEntry.Key);
            }
            catch (Exception) { }
            return ls;
        }
    }
}
