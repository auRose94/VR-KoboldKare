# VR-KoboldKare

[![Build](https://github.com/auRose94/VR-KoboldKare/actions/workflows/build.yml/badge.svg)](https://github.com/auRose94/VR-KoboldKare/actions/workflows/build.yml)

A BepInEx plugin that adds full VR support to **KoboldKare**, enabling one-to-one gameplay with Meta Quest via WiVRn.

## Features

- **Full VR locomotion**: Teleport, smooth movement (stick), and snap turn
- **Head tracking**: 1:1 first-person view + aim, via the game's OrbitCamera
- **VR grabbing**: Game Grabber + PrecisionGrabber driven by controller grip/trigger
- **Camera modes**: First-person (head-locked), third-person, and free cam — switch in VR
- **Interaction**: Use / Activate via right trigger (game `User.Use()` / `Grabber`)
- **VR UI**: World-space interaction prompt + equipment radial
- **Equipment**: List / unequip the player's `KoboldInventory`
- **Ghost mode** (KK-LLM-NPC integration): when an NPC eats you and the spectate
  takeover begins, VR detaches into a free-floating ghost — left stick drifts,
  right stick rises/sinks — then re-anchors when you hatch
- **Backends**: raw OpenXR (WiVRn via Proton's wineopenxr) or keyboard fallback for dev

## Requirements

- **KoboldKare** (Steam) — BepInEx is already bundled
- **Meta Quest** (1 or 2 or 3)
- **WiVRn** — OpenXR streaming over Wi-Fi
- **BepInEx** — Already installed with the game

## Installation

The plugin is built and deployed by `build.sh` (see below). To use it:

1. Build: `./build.sh` (deploys `VRKoboldKare.dll` **and** the Khronos
   `openxr_loader.dll` into `<KoboldKare>/BepInEx/plugins/`)
2. Make WiVRn the system-wide OpenXR runtime (Proton's Wine strips
   `XR_RUNTIME_JSON` from game processes, so launch options do **not** work —
   the loader's `/etc/xdg` fallback is the reliable selection path):
   ```bash
   sudo mkdir -p /etc/xdg/openxr/1
   sudo ln -sfn /usr/share/openxr/1/openxr_wivrn.json /etc/xdg/openxr/1/active_runtime.json
   ```
3. Prime the game's Proton prefix (Proton's wineopenxr expects SteamVR-style
   `HKCU\Software\Wine\VR` registry values; the script forges them for WiVRn —
   no root needed, rerun after any compatdata wipe):
   ```bash
   ./tools/setup-prefix.sh
   ```
4. **Run the game with Proton Experimental** (its wineopenxr implements the
   D3D11 graphics binding via DXVK interop; stock Proton 11 does not), with
   launch options:
   ```
   PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1 PROTON_USE_NTSYNC=1 WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
   - `PRESSURE_VESSEL_IMPORT_OPENXR_1_RUNTIMES=1` — Steam's container does not
     import host OpenXR runtimes by default (SLR ≥ 20250616); without this the
     plugin sees only `XR_EXT_debug_utils` and `xrCreateInstance` fails with
     `XR_ERROR_RUNTIME_UNAVAILABLE`
   - `WINEDLLOVERRIDES="winhttp=n,b"` — lets BepInEx load under Proton
   - the native Linux build of the game has broken shaders (purple
     checkers), so Proton is the way
5. Start the server: `systemctl --user start wivrn` (enable with `--enable-now`)
6. Launch KoboldKare through Steam and join a world (VR needs a possessed player)
7. Put on your Quest, open the WiVRn client, and connect to this machine
8. Press **HOME** in-game to toggle VR on/off (F8 also works; the F-row
   often never reaches Unity under Proton — same reason KK-LLM-NPC uses
   Insert/PageUp/PageDown)

> VR toggling requires a player (`PlayerPossession`). Join a world first, then press F8. The plugin retries the OpenXR session every 2s — you can connect the headset before or after toggling.

## Configuration

All settings are in `<KoboldKare>/BepInEx/config/com.vr.koboldkare.cfg`

See [docs/CONFIG.md](docs/CONFIG.md) for the full configuration reference.

## Controls

> **Desktop (keyboard + mouse) is the designed and fully supported input
> mode.** VR head tracking, grabbing, and interactions all work with it, and
> it's what the settings tabs assume.
>
> **Quest Touch controllers are experimental and NOT yet designed around the
> game.** WiVRn currently does not reliably attach an interaction profile to
> apps on Quest 3 (upstream [WiVRn issue #408](https://github.com/WiVRn/WiVRn/issues/408)),
> so controller sticks/buttons may do nothing until that's fixed upstream.
> Hand-tracking gestures (pinch = trigger, fist = grab) are the controller-free
> path. Desktop input always takes over the moment keyboard or mouse is used.

| Action | VR Input |
|--------|----------|
| Move | Left thumbstick |
| Look / Aim | Head tracking |
| Teleport | Left trigger |
| Camera: First-person | Left A |
| Camera: Third-person | Left B |
| Camera: Free cam | Left X |
| Grab (spring / precision) | Right grip |
| Use / Interact | Right trigger (not grabbing) |
| Activate (fire / freeze) | Right trigger (while grabbing) |
| Rotate / push-pull grab | Right thumbstick |
| Jump | Right A |
| Crouch (hold) | Right B |
| Unfreeze grabbed | Right X |
| Unfreeze all | Right Y |
| Ragdoll toggle | Menu + A |
| Precision-grab toggle | Menu + B |
| Equipment menu | Menu + Y |
| Chat (stub) | Menu + X |
| Ghost drift (while eaten) | Left stick / right stick ↑↓ |
| **Toggle VR on/off** | **HOME** (F8 also works) |

## Building

```bash
# Using Mono compiler (mcs)
KOBOLDKARE_DIR=/path/to/KoboldKare ./build.sh

# Using .NET SDK
KOBOLDKARE_DIR=/path/to/KoboldKare KK_COMPILER=dotnet ./build.sh
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full system architecture.

## Contributing

PRs welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for the dev environment,
the project rules, and the release flow. By participating you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Development

```bash
./run_tests.sh      # pure-C# unit suites (CI gate)
./check_syntax.sh   # parse-check (CI)
./check_build.sh    # full compile (needs the game)
./build.sh          # build + deploy
```

See [AGENTS.md](AGENTS.md) for the architecture gotchas (AI-assistant onboarding
notes), [PLAN.md](../PLAN.md) for the implementation plan and phases.

## License

MIT — See LICENSE.txt

## Credits

- Game: [KoboldKare](https://store.steampowered.com/app/1102930/KoboldKare/) by [Naelstrof](https://github.com/naelstrof)
- Plugin framework: [BepInEx](https://github.com/BepInEx/BepInEx)
- Based on [KK-LLM-NPC](https://github.com/auRose94/KK-LLM-NPC) by @auRose94
- WiVRn: [WiVRn](https://github.com/WiVRn/WiVRn)
