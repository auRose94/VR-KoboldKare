# Contributing to VR-KoboldKare

Thanks for helping! This is a BepInEx-5 Mono IL plugin that adds full OpenXR VR
to KoboldKare over WiVRn — a genuinely cursed stack (Proton + wineopenxr +
DXVK + a flatscreen Unity game), so a few rules keep it maintainable.

## Development environment

- **Arch/Ubuntu with mono** (`mcs` + `mono`) — the build compiles against the
  game's own assemblies.
- **A KoboldKare install** — `KOBOLDKARE_DIR` env var or the default Steam
  library path in `build.sh`. The game's Managed assemblies are
  **not redistributable**: they never enter the repo or CI.
- WiVRn + a Quest for actually testing VR. Desktop fallback (keyboard/mouse)
  works without a headset.

## The commands

| Command | What it does |
|---------|--------------|
| `./run_tests.sh` | Pure-C# unit suites (no game needed) — **must pass before any PR** |
| `./check_syntax.sh` | Parse-check every source file (CI-runnable) |
| `./check_build.sh` | Full compile against the game (skips without an install) |
| `./build.sh` | Build + deploy into the game's `BepInEx/plugins` |
| `./make_release.sh` | Package `dist/VRKoboldKare-<version>.zip` |

`run_tests.sh` and `check_syntax.sh` are the CI gate — green locally means
green remotely.

## Project rules (the load-bearing ones)

1. **Never hand-copy an OpenXR constant.** Spec constants go into
   `src/XrNative.cs` **and** `tests/test_xrabi.cs`, verified against
   `refs/openxr.h`. History: four wrong `XR_TYPE_*` values shipped once and
   only surfaced as `XR_ERROR_VALIDATION_FAILURE` two layers later. The
   xrabi suite is the permanent immune system.
2. **Flag fields are 64-bit** — this project builds against the OpenXR 1.1
   ABI (`XrFlags64`). `uint` flag fields corrupt adjacent members silently.
3. **Pure math lives in pure files.** `src/AimConvention.cs`, `src/XrMath.cs`,
   `src/HudMath.cs` reference nothing but `System.*` — if you add logic there,
   add a suite in `tests/`. If you need Unity types for new logic, extract the
   math into a pure helper first.
4. **The plugin host object dies at t=0.** KoboldKare's Addressables bootstrap
   destroys `BepInEx_Manager` on scene load. Anything that must tick lives on
   the persistent `VRKoboldKare_Manager` (`DontDestroyOnLoad` +
   `HideFlags.HideAndDontSave`). Never tear down in the plugin's `OnDestroy`.
5. **Guard plugin references with `ReferenceEquals`**, not `== null` — Unity's
   overloaded `==` reports the destroyed component as null while the C# object
   is alive, and that silently no-ops every log/config access.
6. **Log through `VRKoboldKarePlugin.Log`** (the static source), not the
   instance — same reason as rule 5.
7. **Legacy `UnityEngine.Input` throws** on this host (new Input System only).
   Use `VRKoboldKarePlugin.KeyDownAny/KeyHeldAny` (dual-stack).

## Pull requests

- One logical change per PR; describe the *symptom* you saw, not just the fix.
- New OpenXR structs/constants → extend `test_xrabi.cs` in the same PR.
- New pure math → a suite in `tests/`, same PR.
- Run `./run_tests.sh ./check_syntax.sh` locally; CI mirrors them.
- Keep `src/` flat — `build.sh` compiles `src/*.cs` and nothing deeper.

## Releases

- Version lives in `[BepInPlugin(..., "X.Y.Z")]` in `src/Main.cs` — single
  source of truth.
- Update `CHANGELOG.md`.
- `git tag vX.Y.Z && ./make_release.sh && gh release create vX.Y.Z
  dist/VRKoboldKare-X.Y.Z.zip --notes-file dist/RELEASE_NOTES.md`
- Releases are built on a machine with the game installed (CI can't link the
  game assemblies).

## Reporting bugs

Include the BepInEx log (`KoboldKare/BepInEx/LogOutput.log`), the game's
`output_log.txt`, your launch options, and whether WiVRn was streaming at the
time. Grabbing/aim issues: the `aim probe`/`input probe` lines are gold — the
mod logs its own telemetry.
