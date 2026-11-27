# VR-KoboldKare — Configuration Reference

All configuration is stored in `<KoboldKare>/BepInEx/config/com.vr.koboldkare.cfg`.

## VR General

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.General:Enabled` | bool | `true` | Enable VR mode. Set to `false` to disable VR (game plays normally). |
| `VR.General:Headset` | string | `auto` | VR runtime to use: `auto`, `openxr`, `steamvr`. |
| `VR.General:MirrorMode` | bool | `true` | Mirror mode: HMD position directly drives camera. Disable for Arcade mode. |
| `VR.General:ShowHandModels` | bool | `true` | Show hand meshes on controllers. |
| `VR.General:HandModelScale` | float | `1.0` | Scale factor for hand models. |

## Locomotion

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Locomotion:Mode` | string | `teleport` | Locomotion mode: `teleport`, `smooth`, `snap`. |
| `VR.Locomotion:TeleportArc` | bool | `true` | Show arc indicator during teleport. |
| `VR.Locomotion:SnapAngle` | int | `45` | Degrees per snap turn. |
| `VR.Locomotion:SmoothSpeed` | float | `2.0` | Meters per second for smooth locomotion. |
| `VR.Locomotion:MaxTeleportDistance` | float | `8.0` | Maximum teleport distance in meters. |
| `VR.Locomotion:GroundCheckLayers` | string | `Walkable` | Layer mask for ground detection (comma-separated layer names). |

## Grab

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Grab:Mode` | string | `spring` | Grab mode: `spring`, `direct`, `hybrid`. |
| `VR.Grab:SpringStrength` | float | `200.0` | Spring constant (N/m) for spring mode. |
| `VR.Grab:DampingStrength` | float | `30.0` | Damping coefficient. |
| `VR.Grab:MaxDistance` | float | `3.5` | Maximum grab distance in meters. |
| `VR.Grab:AnalogGrip` | bool | `true` | Use analog grip value (vs binary on/off). |
| `VR.Grab:GripThreshold` | float | `0.5` | Grip value threshold to activate grab. |
| `VR.Grab:DropThreshold` | float | `0.3` | Grip value threshold to drop item. |
| `VR.Grab:MultiGrab` | bool | `true` | Allow grabbing multiple items at once. |

## Camera

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Camera:FOV` | float | `90.0` | FOV in mirror mode. |
| `VR.Camera:FOVScale` | float | `1.0` | Multiplier for dynamic FOV. |
| `VR.Camera:MotionSickness` | bool | `false` | Reduce FOV during movement to reduce motion sickness. |
| `VR.Camera:ThirdPersonDistance` | float | `2.5` | Distance for third-person camera. |
| `VR.Camera:ThirdPersonHeight` | float | `1.0` | Height offset for third-person camera. |

## UI

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.UI:Scale` | float | `1.0` | UI panel scale factor. |
| `VR.UI:Distance` | float | `1.5` | Default interaction distance in meters. |
| `VR.UI:ShowReticle` | bool | `true` | Show reticle at interaction point. |
| `VR.UI:ShowPrompt` | bool | `true` | Show interaction prompts. |
| `VR.UI:PromptDistance` | float | `1.5` | Distance for interaction prompts above objects. |
| `VR.UI:MenuButton` | string | `menu` | Button to open menus: `menu`, `dpad_down`, `trigger_hold`. |

## Hand

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Hand:ShowModel` | bool | `true` | Show hand mesh models. |
| `VR.Hand:ModelScale` | float | `1.0` | Hand mesh scale. |
| `VR.Hand:Poses` | bool | `true` | Use hand poses for grip animation. |
| `VR.Hand:OpenPoseThreshold` | float | `0.3` | Grip value below which hand is open. |
| `VR.Hand:ClosePoseThreshold` | float | `0.7` | Grip value above which hand is closed. |

## Performance

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Perf:TargetFPS` | int | `72` | Target frame rate. |
| `VR.Perf:MaxRaycastLayers` | int | `16` | Maximum layers for raycasting. |
| `VR.Perf:HandModelLOD` | int | `0` | Hand model LOD: `0`=high, `1`=medium, `2`=low. |
| `VR.Perf:VignetteOnMove` | bool | `false` | Show vignette during smooth locomotion. |
| `VR.Perf:VignetteAmount` | float | `0.3` | Vignette darkness during movement. |

## Network

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Network:SyncBody` | bool | `true` | Sync body position to other players. |
| `VR.Network:SyncHands` | bool | `false` | Sync hand positions to other players. |
| `VR.Network:OwnershipRetry` | float | `3.0` | Seconds between PhotonView ownership re-assertion. |

## Render / Desktop Preview

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Render:DesktopPreview` | bool | `true` | Show the VR view in the desktop window. With a session: mirrors the exact eye texture (under the game's menus). Headset-off: the window IS the VR rig view (head-locked camera, world HUD) for debugging. Toggling this requires a VR disable/enable. |

## Desktop Keys (`VR.DesktopKeys`)

Letter-key defaults: the F-row often never reaches Unity under Proton, and
KK-LLM-NPC already claims Insert/PageUp/PageDown/End. All values are Unity
KeyCode names; changes hot-reload.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.DesktopKeys:ScreenshotKey` | string | `P` | Save a JPG of the VR view to `BepInEx/screenshots/`. Works with the headset off. |
| `VR.DesktopKeys:CameraCycleKey` | string | `B` | Cycle first person → third person → free cam. |
| `VR.DesktopKeys:RadialKey` | string | `H` | Toggle the equipment radial menu. |
| `VR.DesktopKeys:RagdollKey` | string | `K` | Toggle ragdoll. |
| `VR.DesktopKeys:PrecisionModeKey` | string | `J` | Toggle precision-grab (pin) mode. |

Also see `VR.Locomotion:MouseYawSensitivity` — now drives yaw AND pitch for
the desktop-fallback look.

## Body Tracking (`VR.Body`)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `VR.Body:BodyFollowXZ` | float | `0.5` | Master FBT switch: fraction of the camera's horizontal drift the hips follow. `0` disables body mapping entirely. |
| `VR.Body:BodyFollowY` | float | `0.4` | Procedural mode: fraction of the camera height the hips keep above the floor (physical crouch = kobold crouch). |
| `VR.Body:BodyMaxOffset` | float | `1.0` | Max hips offset in meters (clamps lean AND glitched tracker frames). |
| `VR.Body:TrackerStrength` | float | `0.8` | How strongly `XR_FB_body_tracking` (SlimeVR via WiVRn) drives hips/spine/chest. `0` = procedural only. |
| `VR.Body:FeetFollow` | float | `0.5` | Procedural mode: fraction of the hips' drift the feet mirror (the FB body joint set has no knees/feet — legs are always procedural). |

## Networking (`VR.Net` — no config file keys)

VR presence is announced automatically as Photon player properties
(`vrkk_v`/`vrkk_head`/`vrkk_hands`/`vrkk_fbt`) on VR enable/disable and room
join. Pose broadcast (head/hands/hips) runs at 10Hz unreliable on event code
`'V'` while VR is enabled in a room; other kkvr clients render the remote
player's rig as hue-tinted markers. Vanilla clients and other mods ignore all
of it.

`VR.UI:Scale` multiplies the HUD box (0.25 = quarter-size UI); every canvas
conversion logs its numbers, and every options-tab value change logs
`config Section:Key set to value (live)` — config edits apply immediately
(hot-reload effects fire from SettingChanged).
