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
