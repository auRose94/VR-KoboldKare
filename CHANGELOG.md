# Changelog

All notable changes to VR-KoboldKare are documented here.

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
