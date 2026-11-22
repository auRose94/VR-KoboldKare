# VR-KoboldKare — Architecture Deep Dive

## System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        VR-KoboldKare Plugin                         │
│                                                                     │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐        │
│  │ VRManager│──▶│ VRInput  │──▶│ VRLoco   │──▶│ VRGrab   │        │
│  └──────────┘   └──────────┘   └──────────┘   └──────────┘        │
│       │                                              │              │
│       ▼                                              ▼              │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐        │
│  │ VRCamera │──▶│ VRUI     │──▶│ VREquip  │──▶│ VRInter  │        │
│  └──────────┘   └──────────┘   └──────────┘   └──────────┘        │
│       │                                              │              │
│       └──────────────────┬───────────────────────────┘              │
│                          ▼                                          │
│              ┌─────────────────────┐                                │
│              │   Harmony Patches   │                                │
│              │  (game systems)     │                                │
│              └─────────────────────┘                                │
│                          ▼                                          │
│              ┌─────────────────────┐                                │
│              │   KoboldKare Game   │                                │
│              │  (Kobold, Grabber,  │                                │
│              │   CameraSwitcher,   │                                │
│              │   GameManager, etc) │                                │
│              └─────────────────────┘                                │
└─────────────────────────────────────────────────────────────────────┘
```

## Data Flow

### 1. VR Session Lifecycle

```
Game Launch
    │
    ▼
BepInEx loads VRKoboldKare.dll
    │
    ▼
Main.cs: LLMNPCPlugin.Awake()
    ├── Detect OpenXR/SteamVR
    ├── Load config
    ├── Create VRManager singleton
    ├── Apply Harmony patches
    └── Register for scene changes
    │
    ▼
Player puts on headset
    │
    ▼
VRManager.ToggleVR() → enabled = true
    ├── Hide 2D UI canvas
    ├── Lock camera to HMD
    ├── Route input to controllers
    ├── Spawn hand models
    └── Notify other systems
```

### 2. Input Flow

```
VR Controller (OpenXR)
    │
    ▼
VRInput.PollControllers()  [Every Update()]
    ├── Read HMD pose → HeadPose
    ├── Read Left controller → LeftHand
    │     ├── position, rotation
    │     ├── trigger, grip (analog 0-1)
    │     ├── thumbstick (Vector2)
    │     └── buttons (menu, X, Y, B, A)
    │
    └── Read Right controller → RightHand
          ├── position, rotation
          ├── trigger, grip (analog 0-1)
          ├── thumbstick (Vector2)
          └── buttons (menu, X, Y, B, A)
    │
    ▼
VRInput.MapToGameActions()
    ├── Left thumbstick → PlayerControls.Player.Move
    ├── HMD rotation → PlayerControls.Player.Look
    ├── Right grip → Grabber activation
    ├── Right trigger → Use / ActivateGrab
    └── D-pad → Camera switching
    │
    ▼
PlayerControls actions fire
    │
    ▼
PlayerPossession / CameraSwitcher / Grabber respond
```

### 3. Locomotion Flow

```
VRLocomotion.Update()  [Every FixedUpdate()]
    │
    ├── IF mode == Teleport:
    │     ├── Raycast from right controller forward
    │     ├── Find walkable surface within range
    │     ├── Show arc indicator (if enabled)
    │     ├── If right trigger held:
    │     │     └── Move body to target
    │     │           KoboldCharacterController.body.position = target
    │     │           KoboldCharacterController.body.velocity = 0
    │     │
    │     └── IF mode == Smooth:
    │           ├── Get left thumbstick → moveDir
    │           ├── Project on horizontal plane
    │           ├── Set KoboldCharacterController.inputDir
    │           └── Let game's physics handle movement
    │
    └── IF mode == SnapTurn:
          ├── If left trigger + thumbstick left/right:
          │     └── Rotate body by snapAngle degrees
          └── Else:
                └── Smooth locomotion only
```

### 4. Grab Flow

```
VRGrab.Update()  [Every Update()]
    │
    ├── Right hand:
    │     ├── grip > 0.5 AND no grab → Grabber.TryGrab()
    │     ├── grip > 0.5 AND has grab → Grabber.TryActivate()
    │     └── grip < 0.3 → Grabber.TryDrop() + TryStopActivate()
    │
    └── Left hand:
          ├── grip > 0.5 → PrecisionGrabber.BeginGrab()
          └── grip < 0.3 → PrecisionGrabber.ReleaseGrab()
    │
    ▼
Grabber.LateUpdate()
    └── For each grabbed object:
          └── grab.Set(handPosition, handRotation, offset)
                └── DriverConstraint updates to follow hand
```

### 5. Camera Flow

```
VRCamera.Update()  [Every Update()]
    │
    ├── IF cameraMode == Mirror:
    │     ├── OrbitCamera.GetCamera().position = HeadPose.position
    │     ├── OrbitCamera.GetCamera().rotation = HeadPose.rotation
    │     └── FPS pivot tracks head bone (existing behavior)
    │
    ├── IF cameraMode == Arcade:
    │     ├── Third-person: camera behind body
    │     │     └── cameraPos = bodyPos + bodyRot * (0, 1, -2.5)
    │     └── FPS: same as mirror
    │
    └── IF cameraMode == Hybrid:
          ├── First-person: mirror
          ├── Third-person: arcade
          └── Free cam: mirror
```

### 6. Interaction Flow

```
VRInteract.Update()  [Every Update()]
    │
    ├── Raycast from right controller forward
    │     └── Physics.Raycast(controllerPos, controllerForward, maxDist, usableMask)
    │
    ├── IF hit GenericUsable:
    │     ├── Show interaction prompt at hit point
    │     ├── IF right trigger > 0.5:
    │     │     └── usable.LocalUse(playerKobold)
    │     │           └── RPC broadcast → usable.Use()
    │     └── IF hit ActionStation:
    │           └── station.inUse = true
    │                 └── Play animation clip
    │
    └── IF no hit:
          └── Hide prompt
```

## Component Interactions

### VRManager ↔ VRInput
- VRManager enables/disables VRInput polling
- VRInput reports controller state to VRManager
- VRManager provides player reference to VRInput

### VRManager ↔ VRCamera
- VRManager sets camera mode (mirror/arcade/hybrid)
- VRCamera reads HMD pose from VRInput
- VRCamera notifies VRManager of mode changes

### VRManager ↔ VRGrab
- VRManager provides player Kobold reference
- VRGrab uses Grabber/PrecisionGrabber components
- VRGrab reports grab state to VRManager

### VRInput ↔ VRLocomotion
- VRInput provides thumbstick input
- VRLocomotion sets KoboldCharacterController.inputDir
- VRLocomotion may directly set body position (teleport)

### VRInput ↔ VRInteract
- VRInput provides controller forward vector
- VRInteract uses it for raycasting
- VRInteract reads trigger/grip from VRInput

### VRInput ↔ VRUI
- VRInput provides controller position for hand-tracked UI
- VRInteract raycasting also targets UI elements
- VRUI follows controller position

## Key Game Classes (VR Integration Points)

### KoboldCharacterController
- **VR modifies**: `body.position` (teleport), `inputDir` (smooth move)
- **VR reads**: `grounded`, `crouchAmount`, `body.velocity`
- **Critical**: All movement ultimately goes through `Rigidbody.velocity`

### Kobold
- **VR reads**: `body` (Rigidbody), `grabber`, `sizeInflater`
- **VR reads**: `GetComponent<KoboldCharacterController>()`
- **Photon**: `photonView.IsMine` — VR player IS mine

### Grabber
- **VR modifies**: `view` transform (set to right controller)
- **VR calls**: `TryGrab()`, `TryActivate()`, `TryDrop()`, `TryStopActivate()`
- **VR reads**: `grabbedObjects` list for validation

### PrecisionGrabber
- **VR modifies**: `view` transform (set to left controller)
- **VR calls**: `BeginGrab()`, `ReleaseGrab()`
- **VR reads**: `currentGrab` for frozen objects

### CameraSwitcher
- **VR calls**: `SwitchCamera(CameraMode)` via D-pad
- **VR reads**: `mode` to know current camera state
- **VR modifies**: `FPSCanvas` visibility

### OrbitCamera (DLL)
- **VR reads**: `GetCamera()`, `GetPlayerIntendedRotation()`, `GetCurrentCameraData()`
- **VR modifies**: Camera transform position/rotation directly
- **Critical**: `GetPlayerIntendedRotation()` is used by Grabber for aim direction

### GenericUsable
- **VR calls**: `LocalUse(Kobold)` via trigger
- **VR reads**: `CanUse(Kobold)` for interaction eligibility
- **Photon**: `RPCUse()` broadcasts to all clients

### PlayerPossession
- **VR reads**: `kobold`, `grabber`, `pGrabber`, `controller`
- **VR calls**: `SetMovementEnabled(bool)` for camera mode transitions

## Thread Model

```
Main Thread (Unity):
├── Update()       — VRInput polling, VRGrab, VRCamera, VRUI, VRInteract
├── FixedUpdate()  — VRLocomotion, physics
└── LateUpdate()   — Grabber updates, hand models

BepInEx Thread:
├── Awake()        — Plugin initialization, patch application
└── OnEnabled()    — Scene change handling, VR session start

Background Thread (optional):
└── Raycasting for interaction (if performance is critical)
```

## Memory & Performance

### Allocations to Avoid
- No `new` in Update() — use object pools for raycast results, prompts
- No LINQ in hot paths — use foreach with pre-allocated arrays
- No string concatenation in Update() — cache prompt text

### Object Pools
- Interaction prompts (10 max)
- Teleport arc indicators (1)
- Hand model prefabs (2: left + right)
- Raycast hit arrays (Physics.RaycastNonAlloc)

### Culling
- VR only needs to render what the HMD sees
- Use `Camera.cullingMask` to exclude unnecessary layers
- Disable far-plane rendering for objects beyond 50m
