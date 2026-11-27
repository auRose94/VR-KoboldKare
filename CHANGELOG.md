# Changelog

All notable changes to VR-KoboldKare are documented here.

## Unreleased (0.4.0 batch, 2026-11-27)

### Fixed — the playtest round
- **Mouse Y un-reversed**: Unity X-rotation is positive DOWN (left-handed)
  — the 0.4.0 pitch layer shipped with the sign flipped, so mouse-up looked
  down. Negated at both input sites (OpenXR offset layer + keyboard backend
  synthetic head).
- **The HUD is a real virtual screen now** (she: "can't see everything
  cleanly inside a virtual screen"; crosshair 2-4x too big): converted
  canvases previously each scaled to fill the box INDEPENDENTLY
  (`worldH / own height`), so a small crosshair canvas rendered giant and
  the layers never aligned. All canvases now share ONE pixel-to-world scale
  — the tallest canvas (the desktop "screen") maps onto the HUD box and
  every other layer keeps its authored proportions — placed at the same
  follow distance (1mm z-stagger against flicker) with deterministic
  `sortingOrder` layering (originals restored on teardown). This also makes
  `VR.UI:Scale` behave as an intuitive box multiplier.
- **Gate deadlock fixed (found by direct environment probing)**: the toggle
  gate required a rendering OpenXR session, but session creation needs
  main-thread polls (D3D11 device acquisition) that only ran AFTER VR
  enabled — the gate could never open. VRManager now keep-alive-polls while
  VR is off, so the session comes up on its own and the gate opens when the
  headset connects. Environment probe results on her machine (host-side,
  outside Proton): WiVRn was upgraded (r2892→r2897) 18 minutes before the
  failed session and the systemd service handed over to a
  dashboard-launched server mid-session; the new build's IPC is healthy
  host-side, the Quest 3 was connected, and **`XR_FB_body_tracking` +
  bodyTracking=1 confirmed end-to-end** — the FBT pipeline is armed and the
  extension does NOT poison instance creation.
- **The VR toggle refuses a half-handshake** (regression report: toggling VR
  flipped into the keyboard/headless fallback when OpenXR failed — she was
  left in the WiVRn dashboard while desktop VR pretended). `EnableVR` now
  gates on the session ACTUALLY rendering (runtime found, no keyboard
  fallback, headset found — state ≥ SYNCHRONIZED). When not ready it arms a
  pending-enable that retries the bridge every 2s for 60s and flips VR the
  moment the headset connects; F8 while pending cancels; the timeout logs
  the WiVRn-service / pressure-vessel checks. Desktop gameplay is never
  switched for a dead runtime.
- **Instance-creation ladder hardening**: ANY `xrCreateInstance` failure
  with extensions requested drops the optional tail (body tracking first,
  then d3d11) and retries — an advertised-but-poisoned extension combination
  can no longer take the whole bridge down. Only the empty-list failure is
  terminal, and it now logs the WiVRn service / pressure-vessel checks
  instead of a bare result code.
- **Mouse pitch no longer rolls the camera**: the pitch layer composed about
  the WORLD X axis *after* the yaw layer (`Euler(pitch, yaw, 0) · headRot`),
  so any head yaw turned vertical mouse look into roll. The composition is
  now `yaw · HEAD · pitch` — pitch about the head's local X axis — via a
  single `VRManager.ViewRotation` used by the camera snaps, aim, hands, HUD
  panels and the gaze cone.
- **The desktop rig camera locks to the avatar's neck**: the headset-off rig
  hovered at a magic 1.2m above the body; it now sits at the avatar's actual
  head-bone height (queried from the display animator, cached 0.5s), so the
  view pivots at the neck instead of orbiting over the kobold's head.
- **`VR.UI:Scale` actually scales now** (it was bound but never read — the
  second dead-config kill): it multiplies the HUD box, so 0.25 = quarter-size
  UI. Every canvas conversion also logs its numbers
  (`'name' WxHpx → Zm tall at Dm (fit F, uiScale S)`) — the HUD box
  (`HudWidth/HudHeight/HudDistance/Scale`) is now fully observable, and the
  Scale edit live-applies via SettingChanged like the Hud keys.
- **Settings feedback**: every options-tab proxy write logs
  `config Section:Key set to value (live)` — proof the value stuck (the
  config file writes immediately; hot-reload effects apply from
  `SettingChanged`).

### Added — full-body tracking + online coexistence
- **FBT pipeline (XR_FB_body_tracking)**: WiVRn advertises the extension and
  bridges SlimeVR server data — the mod now requests it at instance creation
  (with a fallback ladder that never breaks the D3D11 path), checks
  `XrSystemBodyTrackingPropertiesFB`, creates the tracker via
  `xrGetInstanceProcAddr` (the loader exports core functions only — same
  gotcha as hand tracking), and locates the 70-joint upper body at predicted
  display time. `VRBody` maps hips (position+rotation) and spine/chest
  (rotation) onto the kobold POST-ANIMATION, clamped and strength-scaled —
  the animator stays authoritative. **Struct sizes gcc-verified against
  openxr.h and locked in the xrabi suite.**
- **Procedural legs**: the FB body DEFAULT joint set is upper-body-only (no
  knees/feet — verified), so the feet mirror part of the hips' lean drift
  (`VR.Body:FeetFollow`) when no trackers stream — kobolds lean without
  splitting at the waist.
- **Pose broadcast**: VR players stream head/hand/body world poses as 10Hz
  unreliable Photon events (event code `'V'`, the game claims `'A'`/`'C'`/
  `'H'` and KK-LLM-NPC reuses `'A'`; sender maps stage→world through its own
  rig). `VRRemoteRigs` renders remote VR players as hue-tinted head/hand/hip
  markers (colliders stripped; `Sprites/Default` — unstrippable, the game's
  UI uses sprites). 1.5s pose timeout tears rigs down.
- **Multiplayer VR presence (`VRNet`)**: `vrkk_v/head/hands/fbt` Photon
  player properties announced on VR enable/disable AND room join (PUN2
  properties are room-scoped), remote VR players logged on arrival, and all
  VR overrides scoped to the local player only (see Hardened).

### Hardened (desktop & multiplayer safety)
- **Three multiplayer fight-bugs gated**: the VR grabber re-anchor, the
  gaze-priority interactable re-pick, and the movement override Harmony
  postfixes ran for EVERY player's grabber/user/possession on the client and
  drove them with the LOCAL camera/input — in online play a VR player would
  yank other players' held items across the map and steer their interactions.
  All three now apply to the local player only.
- **Circuit breakers** on the three always-running per-frame paths
  (VRInput tick, VRManager tick, both URP render callbacks): 30 consecutive
  throws stand the path down with a logged warning instead of spamming
  exceptions or breaking rendering — one game-update incompatibility can no
  longer take desktop play with it. WiVRn down / runtime unavailable /
  headset-less all fall through the audited launch path (background init →
  keyboard backend → headless renderer) without blocking window creation.
- **`[VR.General] Enabled` is now actually read** (it was bound but never
  checked): `false` makes the VR hotkeys no-ops — a config-file kill switch.

## 0.4.0 — the headset-off debugging update (2026-11-27)

### Added
- **Desktop preview**: with VR enabled, the game window now shows the VR view.
  With a session it mirrors the exact eye texture; headset-off it renders the
  VR rig (head-locked camera, world HUD, hands) into the window — the whole
  pipeline is debuggable without wearing it. Implemented as an overlay-canvas
  RawImage (gotcha 14 inverted: overlay UI never renders into render textures,
  so it reaches the desktop window while every camera targets the RT).
- **Screenshot hotkey** (`P`, configurable): writes a timestamped JPG of the (PNG encode is stripped from this game build)
  VR view to `BepInEx/screenshots/`. Works headless — it reads the mono RT
  whenever VR is enabled, session or not.
- **Desktop hotkeys** covering the actions the dead controllers would do:
  camera cycle (`B`), equipment radial (`H`), ragdoll (`K`), precision-grab
  pins (`J`). Letter-key defaults because the F-row often never reaches Unity
  under Proton and KK-LLM-NPC claims Insert/PageUp/PageDown/End. All
  configurable Unity KeyCode strings.
- **Mouse pitch**: the desktop fallback look now drives yaw AND pitch
  (`MouseYawSensitivity` applies to both, aim composes and clamps to ±85°) —
  previously only yaw, leaving the view stuck on the horizon with the headset
  off.

### Fixed
- **Options-menu tabs actually visible now.** Two stacked root causes,
  convicted from the 2026-11-23/27 logs + prefab YAML:
  1. The 0.3.0 relayout trigger (`MainMenu.GetCurrentMode() == Options`) never
  fires — the game doesn't switch modes for the options screen (the probe
  reports `'MainMenu'` even while options are open; logged once per session
  for the record). KK-LLM-NPC v1.13.1 ships the same dead gate. The relayout
  now triggers off the panel state that provably works (the game anchor
  button's active-in-hierarchy state) and re-applies on every open edge.
  2. **The ButtonPanel ships with an active VerticalLayoutGroup** (spacing 8 +
  70px rows = the game's 78px grid) that flows ALL children — with 18 buttons
  the modded tabs land ~900px below the panel: "invisible" meant flowed off
  the bottom, not stacked behind Graphics (the Nov-23 dump's y=-277 positions
  were pre-layout baked values). The relayout disables that group and places
  manually.
- **Hover no longer shrinks tabs out of existence** (first 0.4.0 playtest):
  the game's `ButtonMouseOver` lerps `localScale` toward a default captured by
  a `Start()` that runs *deferred* on runtime-injected clones (injected under
  the inactive options view), and its `OnDisable` writes the uninitialized
  `Vector3.zero` into `localScale` if the view toggles before `Start` ran —
  hover then lerps the button toward `0 × 1.05 = 0`, collapsing it into its
  left pivot. `ButtonMouseOver` is now stripped from every modded button
  (ours and KK-LLM-NPC's); hover feedback survives via the Button's own color
  tint. A drift sentinel re-asserts the layout within one poll tick if
  anything re-flows it while the menu is open.
- **Settings views now spawn their rows** (second 0.4.0 playtest: views showed
  only a raw "Label" title + a wall of `ArgumentException: Empty Table
  Reference` from `ScriptableSettingSpawner.CreateTitle`). TWO stacked bugs:
  1. The table reference was wrong from the ported 0.3.0 code: the game's own
  `LocalizeStringEvent`s serialize `m_TableCollectionName: GUID:7b0547fe...`
  — a **GUID-mode reference to the collection named `StringDatabase`** — which
  the port misread as a table *named* "GUID". `GetTable("GUID")` therefore
  returned null forever, `Loc()` handed out EMPTY `LocalizedString` references,
  the spawner's `GetLocalizedString()` threw, and its spawn coroutine died
  mid-flight — instantiating the title template but never setting its text or
  spawning rows. (In the second playtest our Build never succeeded at all —
  the working tabs were KK-LLM-NPC's, whose own view showed the same
  Label/opaque-title symptom for the same reason.)
  2. Even with a valid table, boot-time injection raced the localization
  system's initialization. Injection now waits for
  `LocalizationSettings.InitializationOperation`, `TryLoc` uses a GUID-mode
  `TableReference` (the same path the game's own UI takes), validates the
  lookup, and a failed `Build` retries (capped at 20 attempts) instead of
  dying silently.
- **Tabs are full-size now** (first 0.4.0 playtest: buttons rendered at half
  width/height). The slot compaction was the wrong tool twice over: it
  measured its grow limit in pivot-local space while comparing against
  anchored-space Ys (coordinate mismatch ⇒ a bogus 92px budget), and even
  measured correctly 18 full-size tabs can't fit the ~475px panel. The tab
  list now **wheel-scrolls** at full 78px slots inside a RectMask2D-clipped
  ButtonPanel (scroll only fires with the cursor over the button column — the
  settings list keeps its own wheel handling); the scroll resets on each open
  and rides the same drift sentinel. Compaction helper removed.
- **Keyboard dev backend rewritten.** It polled legacy `UnityEngine.Input`
  (which THROWS on this new-Input-System host — VRInput died every frame in
  keyboard mode), fed the camera's pose back as the head (poll → aim → camera
  feedback loop), and reported synthetic hands as tracked with world-space
  positions `HeadToWorld` mis-projects. Now: new Input System only, a
  synthetic head (mouse yaw/pitch, clamped) independent of the camera, hands
  untracked (grabber views park at the game camera — desktop-style grabbing),
  no synthetic buttons colliding with native binds (R/C/T/G/Q). The rig rides
  the body at a constant eye height; `VRXR.ResetKeyboardHead()` re-anchors on
  VR enable / scene load.
- Headless mono RT is sRGB (it goes to the screen, not through the exact-bit
  CopyTexture path); the session upgrade recreates it Linear at eye resolution
  and re-parents the preview canvas — the flip/copy/sync-sentinel paths
  (gotcha 8) are preserved.

### Changed
- `VRRenderer` no longer disables itself without an OpenXR backend — it runs
  the headless preview pipeline instead (bisect render modes unchanged).
- New config: `VR.Render:DesktopPreview`, `VR.DesktopKeys:*` (see
  docs/CONFIG.md). Version bump: `[BepInPlugin]` 0.4.0.

## 0.3.0 — the input-ownership update (2026-11-21)

### Added
- Options-menu tabs **made visible**: ported KK-LLM-NPC v1.13.1's open-menu
  relayout + panel fit — every modded tab (ours *and* KK-LLM-NPC's, which
  stacked invisibly behind Graphics) flows into 78px slots below Multiplayer,
  the panel's dead layout group is disabled, and the ButtonPanel (plus its BG
  sibling) expands downward-only to fit. The game's own tabs are untouched.
- **Input authority switch**: controllers take over the moment they generate
  input; the desktop (keyboard+mouse) takes over the moment it sends anything.
  No double-driven grabbing.
- Ghost camera ownership: Harmony stand-down for KK-LLM-NPC's spectate writer
  (the installed 1.12 has no hand-off flag — its writer fought the ghost rig
  every pre-cull), plus the game switching to FreeCam while ghosting and back
  to first person on release.
- Ragdoll auto-switches the camera to third person; recovery to first person.
- Lean-axis fix: the rig offset vector now goes through the STAGE→Unity Z-flip
  (physical leans mapped sideways once mouse yaw rotated the frame).
- HUD panels converted to **WorldSpace** head-follow panels (Unity was
  clobbering the ScreenSpaceCamera scale every frame — the shrink never
  applied), sized to a `HudWidth × HudHeight` window (defaults 900×600) that
  hot-reloads live.
- Compositor-starvation diagnostics: frame-budget warning, SYNCHRONIZED flap
  counter with guidance (the "reboot fixes black vision" class is now
  recognized in the log).
- Hand-tracking gestures un-broken: the hand-tracker functions resolve through
  `xrGetInstanceProcAddr` — the Khronos loader exports core functions only, so
  the DllImport threw `EntryPointNotFoundException` on the session thread.

### Fixed
- Game window creation stalled while the WiVRn server was starting/headset-less
  (the runtime probe blocked the main thread — init now runs on a background
  thread).
- HUD/chat panels clipped into floors and mountains: converted UI renders on a
  dedicated URP overlay camera layer with cleared depth (layer 23, verified
  free in the project TagManager) — drawn over the world, no occlusion.
- Pause menu now converts too (it is Screen Space - Camera natively).
- UI text mirrored on X (a 180° presentation flip showed the canvas' back).
- README: desktop controls documented as the designed mode; controllers marked
  experimental with the upstream WiVRn profile-attach issue referenced.

## 0.2.0 — the rewrite (2026-11-21)

The 0.1.0 VR bridge was written against an OpenXR API that does not exist
(`openxr_api.dll`, `xrBindActions`, `xrPollSessionState` — none are real). This
release replaces the entire runtime layer with an ABI-exact OpenXR 1.1
implementation and adds a complete desktop-fallback control scheme.

### Added
- Real OpenXR 1.1 backend: instance → system (2 s retry) → D3D11-bound session
  → Quest Touch bindings → head/hand poses, over WiVRn through Proton's
  wineopenxr.
- Stereo rendering: eye cameras → render textures → OpenXR swapchains →
  `xrEndFrame` (config `[VR.Render] Mode`, default `mono`; `full`/`nocopy`/
  `black` bisect modes remain).
- Ghost mode (KK-LLM-NPC integration): when an NPC eats the player, VR
  detaches into a free-floating spectate spirit — WASD fly, Space/C rise-sink,
  mouse look — then re-anchors on hatch. Wired to the NPC mod's public
  `ModHooks` API (`PlayerSpectating` polling + `ExternalSpectateCamera` /
  `ExternalPlayerControl` hand-off flags).
- Desktop-fallback controls: mouse aim, LMB grab / RMB throw, scroll push-pull,
  camera-relative WASD, configurable snap keys (`[`/`]`).
- Adaptive first-person body hiding (head/body renderers hide only while the
  camera is within 0.6 m of the head bone — visible again in third-person
  spectate).
- Gaze-priority interactions (35° cone around view center re-picks the game's
  closest-usable choice).
- Game HUD/chat/menus rendered into the headset (Screen Space - Overlay →
  Screen Space - Camera conversion with occlusion-safe 15 m plane and FOV
  compensation).
- Config hot-reload: a file watcher applies config edits 0.5 s after save.
- Hand-tracking gesture fallback (XR_EXT_hand_tracking: pinch = trigger,
  fist = grip) when controllers are absent.
- Unit test suites (`run_tests.sh`): OpenXR ABI regression (struct sizes +
  `XR_TYPE` constants vs the official header) and the pure-math convention
  suites. CI (GitHub Actions) runs them plus a syntax gate on every push.

### Fixed
- The original 0.1.0 could never connect to anything (fictional API surface).
- Proton Wine 11 strips `XR_RUNTIME_JSON` from game processes — runtime
  selection now documented/bundled via `/etc/xdg/openxr/1` (README).
- Proton's wineopenxr needs SteamVR-style registry priming
  (`tools/setup-prefix.sh` forges the `HKCU\Software\Wine\VR` values for
  WiVRn).
- Steam's pressure-vessel hides host OpenXR runtimes —
  `PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1` documented in launch options.
- Held objects now track the view exactly (a Harmony-fixed
  `SetPlayerIntendedFacingDirection` — the shipped one writes quaternion
  components as Euler degrees, collapsing intent to north — plus a
  `Grabber.LateUpdate` postfix re-driving holds from the view).
- Head yaw/strafe mirroring (OpenXR→Unity quaternion is `(x, y, -z, -w)`).
- One-frame pose/content lag (head re-polled at the predicted display time in
  the render callback).
- Giant unscaled VR UI panel blocking vision (the "white plane") — canvas
  scale + occlusion-safe near plane.
- Keyboard WASD zeroed while VR was enabled without headset input.
- Desktop grabbing broken after VR disable (grabber views now restored).
- Compositor race: a `ReadPixels` GPU fence sits between CopyTexture and
  swapchain release (was: one-eye/partial frames, DXVK crash).

## 0.1.0 — initial prototype (2026-09)

First working-ish iteration: BepInEx plugin skeleton, Harmony patches,
world-space UI, SteamVR/OpenXR backends (non-functional), keyboard dev
backend, locomotion/grab/interaction skeletons.
