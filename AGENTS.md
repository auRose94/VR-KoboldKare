# AGENTS.md — notes for AI coding assistants working on this repo

You are working on VR-KoboldKare: a BepInEx-5 plugin that adds OpenXR VR to
KoboldKare (Unity 2021.3, URP, D3D11 via DXVK under Proton) over WiVRn. The
stack is deep and the failure modes are unlike normal modding — read this
before changing anything load-bearing.

## Repo layout

- `src/` — flat (build.sh compiles `src/*.cs` only). Pure-math helpers
  (`AimConvention`, `XrMath`, `HudMath`, `XrNative`) must stay free of Unity
  references — CI tests them with plain mono.
- `tests/` — hand-rolled pure-C# suites, `run_tests.sh` runs them in CI.
- `native/` — the Khronos `openxr_loader.dll` (redistributable, committed).
- `tools/` — prefix setup + smoke-test sources.
- `refs/openxr.h` — the ABI ground truth. NEVER hand-copy a constant from
  memory: grep this file, then extend `tests/test_xrabi.cs`.

## The hard-won gotchas (each one cost a debug cycle)

1. **OpenXR 1.1 ABI**: all flag fields are 64-bit (`XrFlags64`). Verify struct
   sizes with `tests/test_xrabi.cs` — wrong sizes corrupt adjacent members.
2. **`XR_TYPE_*` constants**: wrong values pass creation-time calls that ignore
   them but explode as `XR_ERROR_VALIDATION_FAILURE` later. Machine-audit,
   never memory-copy.
3. **Wine 11 strips `XR_RUNTIME_JSON`** from the Windows env — runtime
   selection works via `/etc/xdg/openxr/1/active_runtime.json` (host-side).
4. **Proton's wineopenxr needs SteamVR-style registry priming**:
   `HKCU\Software\Wine\VR` — see `tools/setup-prefix.sh`.
5. **Steam's pressure-vessel hides host OpenXR runtimes**: launch options need
   `PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1`.
6. **Only Proton Experimental's wineopenxr implements XR_KHR_D3D11_enable**
   (via DXVK interop). Extension names are CASE-SENSITIVE:
   `XR_KHR_D3D11_enable`.
7. **URP renders extra eye cameras fine on desktop but crashes DXVK in this
   game** — mono mode (game camera → RT → both eyes) is the shipped path.
   Bisect modes (`nocopy`/`black`) remain in config.
8. **CopyTexture only queues GPU work**: a `ReadPixels` sync fence must sit
   between `Graphics.CopyTexture` and `xrReleaseSwapchainImage`, or the
   compositor races the render thread (partial frames, one-eye-only, DXVK
   crash).
9. **`ctx.Submit()` before any blit** in `endCameraRendering` — URP flushes
   queued commands at Submit, not at callback time.
10. **Quaternion RH→LH is (x, y, -z, -w)** — the (x, y, -z, +w) variant is the
    INVERSE rotation (mirrored yaw/strafe). Locked by `tests/test_math.cs`.
11. **The shipped `OrbitCamera.SetPlayerIntendedFacingDirection` is buggy**
    (quaternion components written as degrees — intent collapses to north).
    VRKoboldKare Harmony-patches it; drive `OrbitCamera._aim` via
    `VRManager.SetOrbitAim` instead.
12. **The plugin host object dies at t=0** (Addressables bootstrap). Persistent
    GameObjects need `HideFlags.HideAndDontSave`; guard plugin refs with
    `ReferenceEquals`; log through the static `Log`.
13. **Legacy `UnityEngine.Input` throws** on this new-Input-System host — use
    `KeyDownAny/KeyHeldAny`.
14. **The game's desktop UI is Screen Space - Overlay** and never renders into
    render textures — VRUI converts canvases to Screen Space - Camera (rescan
    every 2 s, restore on teardown).
15. **KK-LLM-NPC integration** (`ModHooks`, v1.13+): ghost mode polls
    `PlayerSpectating`, claims `ExternalSpectateCamera` +
    `ExternalPlayerControl`, releases on SpectateEnded. See
    `/mnt/Matrix/Work/KK-LLM-NPC/docs/MODHOOKS.md`.

## Build/test commands

- `./run_tests.sh` — pure suites, CI gate, no game needed.
- `./check_syntax.sh` — parse-only check, CI-runnable.
- `./check_build.sh` — real compile, needs `KOBOLDKARE_DIR`.
- `./build.sh` — build + deploy to the game.
- Default game path: `/mnt/Matrix/SteamLibrary/steamapps/common/KoboldKare`.

## Style

Match the surrounding code: file-header comments explaining WHY, small
methods, `catch { }` only where a failure is genuinely survivable (and log it
when it isn't). Version bump = edit the `[BepInPlugin]` attribute + CHANGELOG.
