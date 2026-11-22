# VR-KoboldKare — BepInEx VR Plugin for KoboldKare

**Author**: @auRose94
**Target**: KoboldKare (Unity, Photon Pun networking, BepInEx already bundled)
**Input**: Meta Quest via WiVRn (OpenXR, Touch controllers)
**Scope**: Full VR support — locomotion, grabbing, interaction, camera switching, UI, inventory, equipment, and more.

---

## 1. Game Architecture Summary

### Core Systems (from source code analysis)

| System | Key Classes | Notes |
|--------|-------------|-------|
| **Player Body** | `Kobold`, `KoboldCharacterController` | Rigidbody-based character controller with step/climb, crouching, jumping, ground detection. All movement is physics-driven via `Rigidbody.velocity`. |
| **Player Data** | `Kobold` (extends `GeneHolder`) | Energy, horniness, equipment slots, belly container, penetration tracking, equipment attach points, size inflation. PhotonView-mine. |
| **Animation** | `CharacterControllerAnimator` | Animator-driven with IK solver (`Vilar.IK.ClassicIK`), eye look, head/hip/chest look targets. Coroutine-based station animations. |
| **Grabbing** | `Grabber`, `GenericGrabbable`, `PrecisionGrabber` | Two grab modes: **Grabber** (spring-based drag via `DriverConstraint`), **PrecisionGrabber** (freeze-raycast grab with hand display). Both use `OrbitCamera.GetPlayerIntendedRotation()` for aim direction. |
| **Camera** | `CameraSwitcher`, `OrbitCamera*` (DLL) | Three modes: FirstPerson, ThirdPerson, FreeCam. Configurations use `OrbitCameraPivotBase` pivots (bone-locked). `OrbitCamera.GetCamera()`, `.GetPlayerIntendedRotation()`, `.GetCurrentCameraData()` are static entry points. |
| **Interaction** | `GenericUsable`, `UsableMachine`, `ActionStation` | `CanUse(Kobold)` check → `LocalUse(Kobold)` → RPC broadcast → `Use()`. Stations have animation clips and durations. |
| **Input** | `PlayerControls` (Unity Input System) | Actions: Move (Vector2), Jump, Walk, Gib, Use, ActivateGrab, Grab, Crouch, Rotate, Look (Vector2), LookJoystick (Vector2), FirstPerson, ThirdPerson, FreeCamera, SwitchCamera, HideHUD, Chat, ViewStats, Unfreeze, Ragdoll, SwitchGrabMode, etc. |
| **Networking** | Photon Pun 2 | `PhotonView`, `PunRPC`, `IPunObservable`, ownership transfer. Player is `PhotonNetwork.LocalPlayer.TagObject` as `Kobold`. |
| **UI** | `PopupHandler`, `Popup`, `Canvas` | Addressable popup system. Main canvas via `GameManager.mainCanvas`. UI uses Unity UI (Canvas, Button, etc.). |
| **Equipment** | `Equipment`, `EquipmentInventory`, `EquipmentUIDisplay` | `EquipmentDatabase` (ScriptableObject), `WearableEquipment`, `GenericEquipment`. |
| **Scenes** | `MainMenu`, `MainMap`, `AddressableLoadingScene`, `ErrorScene` | `GameManager.InLevel()` gates gameplay scenes. |

### External Dependencies (in `KoboldKare_Data/Managed/`)

- `Assembly-CSharp.dll` — compiled game code (contains `OrbitCamera`, `User`, `Ragdoller`, `PrecisionGrabber`, etc.)
- `PhotonUnityNetworking.dll`, `Photon3Unity3D.dll`, `PhotonRealtime.dll`
- `BepInEx.dll`, `0Harmony.dll`
- `Naelstrof.PenetrationTech.dll`
- `KoboldKare.ISavable.dll`
- `UnityScriptableSettings`
- `SimpleJSON`
- `Vilar.*` (IK, AnimationStation)
- `NetStack.*` (serialization)
- `JigglePhysics`
- `Naelstrof.Inflatable`

---

## 2. Design Principles

1. **Non-destructive**: The plugin adds VR support without modifying game code. All integration is via Harmony patches and runtime injection.
2. **Toggleable**: VR mode is opt-in. When disabled, the game plays exactly as normal (keyboard/mouse).
3. **Network-aware**: VR player's body is still a PhotonView-mine `Kobold`. Other clients see normal movement. VR-specific data (hand poses, controller positions) stays local.
4. **Fallback-safe**: If VR input isn't available, the plugin falls back to keyboard/mouse mappings.
5. **Modular**: Following the KK-LLM-NPC pattern — subsystems register via a registry for extensibility.

---

## 3. Plugin Structure

```
VR-KoboldKare/
├── build.sh                  # Build script (mcs/dotnet, like KK-LLM-NPC)
├── VRKoboldKare.csproj       # .NET SDK project (optional)
├── src/
│   ├── Main.cs               # BepInEx plugin entry point, config, lifecycle
│   ├── VRManager.cs          # Central VR state machine, enable/disable, session management
│   ├── VRInput.cs            # OpenXR/SteamVR input abstraction → maps to game actions
│   ├── VRLocomotion.cs       # VR movement: teleport, smooth locomotion, snap turn
│   ├── VRGrab.cs             # VR hand tracking → Grabber/PrecisionGrabber integration
│   ├── VRCamera.cs           # VR headset pose → OrbitCamera configuration switching
│   ├── VRUI.cs               # VR-native UI: floating menus, hand-tracked buttons
│   ├── VREquipment.cs        # VR equipment interaction: wield, wear, inventory
│   ├── VRInteract.cs         # VR interaction rays → GenericUsable activation
│   ├── VRPatches.cs          # Harmony patches for game systems
│   ├── VRHandModel.cs        # VR hand mesh rendering (optional)
│   ├── VRConfig.cs           # Configuration entries
│   └── ModuleRegistry.cs     # Extension registry (tools, hooks, patches)
├── docs/
│   ├── ARCHITECTURE.md       # This document
│   └── CONFIG.md             # Configuration reference
└── README.md
```

---

## 4. Core Systems Design

### 4.1 VRManager — Central State Machine

```
VRManager (singleton, attached to GameManager)
├── Enabled: bool                # VR session active
├── Mode: VRMode                 # None, Immersive, Standalone
├── LocomotionMode: LocoMode     # Teleport, Smooth, SnapTurn
├── GrabMode: GrabMode           # Spring, Direct, Hybrid
├── CameraMode: VRCameraMode     # Mirror (1:1), Arcade, Hybrid
├── RightHand: VRHand            # Controller pose + grip
├── LeftHand: VRHand             # Controller pose + grip
├── HeadPose: Pose               # HMD position + rotation
├── OnVREnabled: Action
├── OnVDDisabled: Action
└── ToggleVR()                    # Enable/disable VR
```

**Lifecycle:**
- `Awake()`: Detect OpenXR/SteamVR availability via Unity XR Interaction Toolkit or OpenXR API.
- `OnEnable()`: Register input subscriptions, apply initial patches.
- `ToggleVR()`: Enable/disable VR overlay, reconfigure camera, swap input routing.

### 4.2 VRInput — Input Abstraction Layer

Maps VR controller inputs to the game's existing `PlayerControls` action map.

**Controller tracking:**
- **HMD**: Position + rotation → head pose, camera origin
- **Left Controller**: Position, rotation, trigger, grip, thumbstick, thumbrest, menu button
- **Right Controller**: Position, rotation, trigger, grip, thumbstick, thumbrest, menu button

**Input mapping table:**

| Game Action | Keyboard/Mouse | VR Input |
|-------------|---------------|----------|
| Move | WASD | Left thumbstick (analog) |
| Look | Mouse | Head tracking (HMD) |
| LookJoystick | — | Left thumbstick (when in menu) |
| Jump | Space | Right trigger (short press) / A button |
| Crouch | Left Shift / C | Thumbrest press / B button |
| Grab | Left mouse / E | Right grip (analog) / X button |
| ActivateGrab | Left mouse hold | Right trigger (analog) / Y button |
| Use | E / Left mouse | Right trigger (tap) / Y button |
| FirstPerson | F | D-pad Up / Y button |
| ThirdPerson | C | D-pad Down / X button |
| FreeCamera | V | D-pad Left / B button |
| SwitchCamera | Tab | D-pad Right / A button |
| HideHUD | H | D-pad Down + B button |
| Chat | Enter | Menu button + Y |
| ViewStats | Ctrl+V | Menu button + X |
| Ragdoll | Q | Menu button + A (hold 1s) |
| SwitchGrabMode | G | Menu button + B |
| Rotate | Right mouse | Right thumbstick (when holding item) |
| Unfreeze | F (on frozen) | Right trigger + grip (on frozen object) |

**Implementation:**
- Wrap `UnityEngine.InputSystem` actions.
- Poll VR controller state each frame via `XRController` APIs.
- When VR is enabled, inject controller values into `PlayerControls` actions.
- When VR is disabled, fall back to keyboard/mouse.

### 4.3 VRLocomotion — Movement System

The game's `KoboldCharacterController` moves via `Rigidbody.velocity`. VR locomotion needs to translate head/controller movement into controller-compatible velocity commands.

**Locomotion modes:**

1. **Teleport** (default, most comfortable):
   - Left thumbstick aims a teleport arc indicator
   - Left trigger initiates teleport destination
   - Teleport target is a raycast from controller forward, filtered by walkable ground
   - On confirm: set `KoboldCharacterController.body.position` to target + offset

2. **Smooth Locomotion**:
   - Left thumbstick → forward velocity on `KoboldCharacterController`
   - Head rotation → movement direction (room-scale) or body rotation (turn-assist)
   - `KoboldCharacterController.inputDir` set from head-forward projected on horizontal plane

3. **Snap Turn** (hybrid):
   - Left trigger + left thumbstick left/right → snap turn body by 30/45/60 degrees
   - Combined with smooth locomotion for directional movement

**Implementation:**
```csharp
// Teleport
void TeleportTo(Vector3 target) {
    var kb = playerKobold.GetComponent<KoboldCharacterController>();
    kb.body.position = target + Vector3.up * kb.body.position.y;
    kb.body.velocity = Vector3.zero;
    // Sync camera to new position
}

// Smooth locomotion
void ApplySmoothLocomotion(Vector2 moveInput, Quaternion headRotation) {
    var kb = playerKobold.GetComponent<KoboldCharacterController>();
    Vector3 forward = headRotation * Vector3.forward;
    forward.y = 0; forward.Normalize();
    Vector3 right = headRotation * Vector3.right;
    right.y = 0; right.Normalize();
    kb.inputDir = (forward * moveInput.y + right * moveInput.x).normalized;
}
```

### 4.4 VRGrab — Hand & Grab Integration

The game has two grab systems:
1. **Grabber** — spring-based grab of `IGrabbable` objects (items, weapons, other kobolds)
2. **PrecisionGrabber** — freeze-raycast grab (freezes objects in place)

**VR Integration:**

**Hand tracking:**
- Each VR controller drives a hand transform
- Right controller → right hand transform (used by `Grabber.view`)
- Left controller → left hand transform (used by `PrecisionGrabber.view`)

**Grab behavior:**
- **Analog grip** (right controller): Maps to `Grabber.TryGrab()` (press) and `Grabber.TryActivate()` (hold)
- **Trigger** (right controller): Alternative grab activation
- **Grip + trigger** (left controller): Precision grab activation
- **Grip release**: Calls `Grabber.TryDrop()` or `PrecisionGrabber.ReleaseGrab()`

**Hand pose:**
- Open hand when not gripping
- Closed/fist when gripping
- Pinch when precision grabbing

**Implementation:**
```csharp
// Grabber integration
void UpdateRightHandGrab() {
    float gripValue = rightController.grip.ReadValue<float>();
    if (gripValue > 0.5f && !grabber.grabbedObjects.Any()) {
        grabber.TryGrab(multiGrabMode: rightController.thumbstick.x > 0.5f);
    } else if (gripValue > 0.5f && grabber.grabbedObjects.Any()) {
        grabber.TryActivate();
    } else if (gripValue < 0.3f) {
        grabber.TryDrop();
        grabber.TryStopActivate();
    }
}

// Precision grabber integration
void UpdateLeftHandGrab() {
    float gripValue = leftController.grip.ReadValue<float>();
    if (gripValue > 0.5f) {
        pGrabber.BeginGrab(leftHand.position, leftHand.forward);
    } else if (gripValue < 0.3f) {
        pGrabber.ReleaseGrab();
    }
}
```

### 4.5 VRCamera — Headset & Camera System

The game's `CameraSwitcher` manages three camera modes via `OrbitCamera` configurations. VR needs to feed HMD pose into this system.

**Camera modes in VR:**

1. **Mirror Mode** (default):
   - HMD pose directly drives the camera origin
   - First-person camera = HMD position (via `OrbitCameraFPSHeadPivot`)
   - Third-person camera = behind the player, offset from HMD position
   - Free camera = HMD position, player body invisible

2. **Arcade Mode**:
   - HMD position = camera origin (same as mirror)
   - Third-person camera uses fixed offset from player body (not HMD)
   - More comfortable for motion sickness

3. **Hybrid Mode**:
   - First-person = HMD position (1:1)
   - Third-person = arcade-style offset
   - Free camera = HMD position

**Implementation:**
```csharp
// Mirror mode: HMD drives camera
void UpdateMirrorCamera() {
    var hmdPose = headset.GetPose();
    var cam = OrbitCamera.GetCamera();
    cam.transform.position = hmdPose.position;
    cam.transform.rotation = hmdPose.rotation;
    
    // When in first-person, the OrbitCameraFPSHeadPivot already tracks head bone
    // We just need to ensure the camera origin is at HMD position
}

// Arcade mode: camera follows body, not HMD
void UpdateArcadeCamera() {
    var cam = OrbitCamera.GetCamera();
    var bodyPos = playerKobold.transform.position;
    var bodyRot = playerKobold.transform.rotation;
    
    // Third-person: behind and above the body
    Vector3 offset = bodyRot * new Vector3(0f, 1f, -2.5f);
    cam.transform.position = bodyPos + offset;
    cam.transform.LookAt(bodyPos + Vector3.up * 1.5f);
}
```

**Camera switching in VR:**
- D-pad or menu button → `CameraSwitcher.SwitchCamera()`
- VR mode automatically adjusts camera behavior per mode
- In first-person VR, the player sees through their "eyes" (HMD position)
- In third-person VR, the player sees a third-person view rendered to HMD

### 4.6 VRUI — VR-Native User Interface

The game uses Unity Canvas-based UI with `PopupHandler` for menus (equipment, chat, etc.). VR needs a different approach.

**VR UI strategies:**

1. **Hand-tracked UI** (primary):
   - Equipment menu: Floating panel that appears when pointing at equipment icon
   - Chat: Text appears as floating speech bubbles above NPCs (same as current speech bubbles)
   - Inventory: Radial menu that appears near the player's hand

2. **Gaze/Point-based UI** (secondary):
   - Raycast from controller forward to detect UI buttons
   - Trigger/activation to select buttons
   - UI panels appear at a fixed distance from the player

3. **World-space UI** (for stations):
   - When interacting with `ActionStation` or `UsableMachine`, show a world-space panel
   - Panel follows the station, faces the player
   - Contains action buttons, progress indicators

**Implementation:**
```csharp
// Floating equipment panel
class VREquipmentPanel : MonoBehaviour {
    void Show() {
        // Create world-space canvas at player's hand position
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.transform.position = rightHand.position + rightHand.forward * 1f;
        canvas.transform.rotation = Quaternion.LookRotation(rightHand.forward);
    }
    
    void Hide() {
        canvas.gameObject.SetActive(false);
    }
}

// Raycast for interaction
bool RaycastInteractable(out GenericUsable usable) {
    var ray = new Ray(rightHand.position, rightHand.forward);
    float maxDist = 5f;
    if (Physics.Raycast(ray, out RaycastHit hit, maxDist, gameManager.usableHitMask)) {
        usable = hit.collider.GetComponentInParent<GenericUsable>();
        return usable != null;
    }
    usable = null;
    return false;
}
```

### 4.7 VREquipment — Equipment Interaction

The game's equipment system (`Equipment`, `EquipmentInventory`, `EquipmentUIDisplay`) needs VR integration.

**Equipment actions in VR:**

1. **Wielding items**: Grabbed items automatically attach to the grabber's hand
   - Weapons: Align to controller forward, fire on trigger
   - Consumables: Bring to mouth on trigger press
   - Tools: Hold in hand, use on target

2. **Wearing equipment**: Point at equipment slot on body, trigger to equip
   - Equipment slots are defined in `Kobold.attachPoints`
   - VR raycast to the attach point transform
   - On confirm: call `EquipmentInventory.Equip()`

3. **Inventory management**: Radial menu near hand
   - 8 slots arranged in a circle
   - Thumbstick selects slot, trigger confirms
   - Drag-and-drop via controller position

### 4.8 VRInteract — Interaction System

The game's interaction system uses `GenericUsable.CanUse()` and `LocalUse()`. VR needs to trigger these via controller rays.

**Interaction flow:**
1. Controller raycast → find `GenericUsable` or `ActionStation`
2. If found, show interaction prompt (floating text near object)
3. On trigger press → call `usable.LocalUse(playerKobold)`
4. For stations: trigger station animation via `ActionStation.clip`

**Implementation:**
```csharp
void UpdateInteraction() {
    if (RaycastInteractable(out var usable)) {
        // Show prompt
        interactionPrompt.text = usable.GetInteractText();
        interactionPrompt.transform.position = usable.transform.position + Vector3.up * 1.5f;
        
        if (rightController.trigger.ReadValue<float>() > 0.5f) {
            usable.LocalUse(playerKobold);
        }
    } else {
        interactionPrompt.gameObject.SetActive(false);
    }
}
```

---

## 5. Harmony Patches

Following the KK-LLM-NPC pattern, Harmony patches are used to intercept and modify game behavior without source modification.

### 5.1 Input Routing Patch

**Target**: `PlayerControls` action callbacks
**Purpose**: When VR is enabled, route VR controller input instead of keyboard/mouse.
**Method**: Prefix patches on `PlayerControls.Player.Move.performed`, `PlayerControls.Player.Look.performed`, etc.

### 5.2 Camera Origin Patch

**Target**: `OrbitCamera.GetCamera()` (via reflection, in Assembly-CSharp.dll)
**Purpose**: Override camera origin with HMD position in VR mirror mode.
**Method**: Transpiler or postfix to replace camera position with HMD pose.

### 5.3 Grabber View Patch

**Target**: `Grabber.SetView()` / `Grabber.view`
**Purpose**: Set grabber's view transform to VR right controller.
**Method**: Postfix on `Grabber.Awake()` to auto-set view to right hand.

### 5.4 PrecisionGrabber View Patch

**Target**: `PrecisionGrabber.SetView()` / `PrecisionGrabber.view`
**Purpose**: Set precision grabber's view transform to VR left controller.
**Method**: Postfix on `PrecisionGrabber.Awake()` to auto-set view to left hand.

### 5.5 Cursor Lock Patch

**Target**: `Cursor.lockState` checks in `CameraSwitcher.SwitchCamera()`
**Purpose**: VR doesn't use cursor locking; remove the lock check.
**Method**: Prefix patch to always return `true` (skip the cursor check).

### 5.6 UI Visibility Patch

**Target**: `GameManager.SetUIVisible()`, `PopupHandler.PopupIsActive()`
**Purpose**: VR UI is always "visible" (world-space), so hide the 2D canvas.
**Method**: Prefix to disable main canvas when VR is active.

### 5.7 Animation Station Patch

**Target**: `CharacterControllerAnimator.AnimationRoutine`
**Purpose**: Ensure VR player animations trigger correctly when using stations.
**Method**: Prefix to detect VR mode and set animation state from VR input.

---

## 6. Build System

Following the KK-LLM-NPC pattern exactly:

```bash
#!/usr/bin/env bash
# Build VR-KoboldKare.dll against BepInEx + game assemblies.

KDIR="${KOBOLDKARE_DIR:-/mnt/gaming/SteamLibrary/steamapps/common/KoboldKare}"
MGMT="$KDIR/KoboldKare_Data/Managed"
CORE="$KDIR/BepInEx/core"

mcs -target:library -out:VRKoboldKare.dll src/*.cs \
  -r:"$CORE/BepInEx.dll" \
  -r:"$CORE/0Harmony.dll" \
  -r:"$MGMT/UnityEngine.dll" \
  -r:"$MGMT/UnityEngine.CoreModule.dll" \
  -r:"$MGMT/UnityEngine.InputSystem.dll" \
  -r:"$MGMT/UnityEngine.UI.dll" \
  -r:"$MGMT/UnityEngine.UIModule.dll" \
  -r:"$MGMT/UnityEngine.PhysicsModule.dll" \
  -r:"$MGMT/UnityEngine.AnimationModule.dll" \
  -r:"$MGMT/UnityEngine.TextRenderingModule.dll" \
  -r:"$MGMT/UnityEngine.AudioModule.dll" \
  -r:"$MGMT/PhotonUnityNetworking.dll" \
  -r:"$MGMT/Photon3Unity3D.dll" \
  -r:"$MGMT/PhotonRealtime.dll" \
  -r:"$MGMT/Assembly-CSharp.dll" \
  -r:"$MGMT/Naelstrof.PenetrationTech.dll" \
  -r:"$MGMT/UnityEngine.IMGUIModule.dll" \
  -r:"$MGMT/KoboldKare.ISavable.dll" \
  -r:"$MGMT/SimpleJSON.dll" \
  -r:"$MGMT/UnityScriptableSettings.dll" \
  -r:"$MGMT/Naelstrof.Inflatable.dll"

cp VRKoboldKare.dll "$KDIR/BepInEx/plugins/VRKoboldKare.dll"
```

---

## 7. Configuration

All config via `BepInEx.Configuration.ConfigEntry<T>`:

```csharp
// VR General
"vr.general.enabled" = true           # Enable VR mode
"vr.general.headset" = "auto"         # auto, openxr, steamvr
"vr.general.mirrorMode" = true        # 1:1 HMD to camera
"vr.general.showHandModels" = true    # Show hand meshes

// Locomotion
"vr.locomotion.mode" = "teleport"     # teleport, smooth, snap
"vr.locomotion.teleportArc" = true    # Show teleport arc indicator
"vr.locomotion.snapAngle" = 45        # Degrees per snap turn
"vr.locomotion.smoothSpeed" = 2.0     # Meters per second

// Grab
"vr.grab.mode" = "spring"             # spring, direct, hybrid
"vr.grab.springStrength" = 200        # N/m spring constant
"vr.grab.dampingStrength" = 30        # Damping coefficient
"vr.grab.maxDistance" = 3.5           # Maximum grab distance (m)
"vr.grab.analogGrip" = true           # Use analog grip value

// Camera
"vr.camera.fov" = 90                  # FOV in mirror mode
"vr.camera.fovScale" = 1.0            # Multiplier for dynamic FOV
"vr.camera.motionSickness" = false    # Reduce FOV during movement

// UI
"vr.ui.scale" = 1.0                   # UI panel scale
"vr.ui.distance" = 1.5                # Default interaction distance (m)
"vr.ui.showReticle" = true            # Show reticle at interaction point
"vr.ui.showPrompt" = true             # Show interaction prompts

// Hand
"vr.hand.showModel" = true            # Show hand mesh
"vr.hand.modelScale" = 1.0            # Hand mesh scale
"vr.hand.poses" = true                # Use hand poses for grip
```

---

## 8. Implementation Phases

### Phase 1: Foundation (Weeks 1-2)
- [ ] Project setup: `VRKoboldKare.csproj`, `build.sh`, `Main.cs`
- [ ] `VRManager` — VR session lifecycle, enable/disable toggle
- [ ] `VRInput` — OpenXR input abstraction, controller tracking
- [ ] `VRConfig` — BepInEx configuration entries
- [ ] Basic Harmony patches: input routing, cursor lock removal
- [ ] **Milestone**: VR headset detected, HMD position drives camera in first-person

### Phase 2: Locomotion (Weeks 3-4)
- [ ] `VRLocomotion` — Teleport locomotion
- [ ] `VRLocomotion` — Smooth locomotion
- [ ] `VRLocomotion` — Snap turn
- [ ] Ground detection for teleport (walkable surface raycast)
- [ ] **Milestone**: Player can move around the game world in VR

### Phase 3: Grabbing (Weeks 5-6)
- [ ] `VRGrab` — Right hand grab via Grip input
- [ ] `VRGrab` — Left hand precision grab
- [ ] `VRGrab` — Hand pose animation (open/closed)
- [ ] `VRGrab` — Integration with `Grabber` and `PrecisionGrabber`
- [ ] **Milestone**: Player can grab and throw items in VR

### Phase 4: Camera & Perspective (Weeks 7-8)
- [ ] `VRCamera` — Mirror mode (HMD = camera)
- [ ] `VRCamera` — Arcade mode (body-relative camera)
- [ ] `VRCamera` — Camera mode switching in VR
- [ ] `VRCamera` — FOV management for motion sickness
- [ ] **Milestone**: All three camera modes work in VR

### Phase 5: Interaction (Weeks 9-10)
- [ ] `VRInteract` — Raycast-based interaction
- [ ] `VRInteract` — Integration with `GenericUsable`
- [ ] `VRInteract` — Integration with `ActionStation`
- [ ] `VRInteract` — Interaction prompts
- [ ] **Milestone**: Player can interact with game objects in VR

### Phase 6: UI & Equipment (Weeks 11-12)
- [ ] `VRUI` — Floating menus (equipment, chat)
- [ ] `VRUI` — Radial inventory menu
- [ ] `VREquipment` — Equipment wielding
- [ ] `VREquipment` — Equipment slot targeting
- [ ] **Milestone**: Full UI and equipment interaction in VR

### Phase 7: Polish & Testing (Weeks 13-14)
- [ ] Hand model rendering (optional, procedural)
- [ ] Haptic feedback integration
- [ ] Motion sickness options (vignette, teleport-only mode)
- [ ] Performance optimization (LOD, culling)
- [ ] WiVRn-specific tuning
- [ ] Documentation and release
- [ ] **Milestone**: Release-ready plugin

---

## 9. Technical Considerations

### 9.1 WiVRn Compatibility
- WiVRn uses OpenXR over Wi-Fi with compression
- Latency is ~20-50ms depending on network conditions
- Frame rate target: 72-90 FPS (Quest settings)
- Use `Time.smoothDeltaTime` for frame-rate-independent movement
- Minimize physics calculations per frame

### 9.2 Photon Networking
- VR player body is still `PhotonNetwork.LocalPlayer.TagObject` as `Kobold`
- Movement is synced via `KoboldCharacterController`'s `IPunObservable`
- Hand positions, controller poses, and VR-specific data are **local only**
- Non-VR players see the VR player as a normal `Kobold` (no visual difference)
- Grabbed objects are synced via `PhotonView` ownership

### 9.3 Performance
- VR requires high, stable frame rates
- Minimize allocations (object pooling for rays, prompts)
- Use `Physics.OverlapSphereNonAlloc` instead of `OverlapSphere`
- Limit raycast layers to only what's needed
- Hand models (if used) should be low-poly (<500 tris per hand)

### 9.4 Motion Sickness
- Teleport locomotion by default (most comfortable)
- Vignette effect during smooth locomotion
- Optional "stationary" mode (no movement, only look)
- Dynamic FOV reduction during movement

### 9.5 Anti-Cheat / Detection
- BepInEx plugins are generally undetectable (load at runtime via DLL injection)
- No file modification — all patches are runtime-only
- Photon networking uses existing channels
- WiVRn is a standard OpenXR runtime

---

## 10. Known Challenges & Mitigations

| Challenge | Mitigation |
|-----------|-----------|
| `OrbitCamera` is in compiled DLL | Use reflection to call `OrbitCamera` static methods; patch camera origin via Harmony postfix on the camera update method |
| `CharacterControllerAnimator` coroutines fight rotation | Copy the KK-LLM-NPC patch pattern (patch `<AnimationRoutine>d__79.MoveNext`) |
| Photon ownership conflicts | Follow KK-LLM-NPC pattern: claim PhotonView ownership, re-assert every 3 seconds |
| Unity Input System + VR input mixing | VR input overrides `PlayerControls` actions; keyboard/mouse disabled when VR active |
| Grabber uses `OrbitCamera.GetPlayerIntendedRotation()` | This already works with VR since it reads from the active camera |
| 2D Canvas UI doesn't work in VR | World-space Canvas for VR UI; hide 2D canvas when VR active |
| WiVRn compression artifacts | Keep UI text large and high-contrast; use simple shapes for reticles |

---

## 11. File References (Source Code)

### Game Scripts (most relevant for VR)
- `Assets/KoboldKare/Scripts/KoboldCharacterController.cs` — Player movement (Rigidbody velocity-based)
- `Assets/KoboldKare/Scripts/Kobold.cs` — Player body, equipment, state
- `Assets/KoboldKare/Scripts/Grabber.cs` — Item grabbing (spring constraint)
- `Assets/KoboldKare/Scripts/PrecisionGrabber.cs` — Freeze grab
- `Assets/KoboldKare/Scripts/IGrabbable.cs` — Grab interface
- `Assets/KoboldKare/Scripts/CameraSwitcher.cs` — Camera mode switching
- `Assets/KoboldKare/Scripts/OrbitCameraFPSHeadPivot.cs` — FPS camera pivot (bone-locked)
- `Assets/KoboldKare/Scripts/OrbitCameraCharacterBaseConfiguration.cs` — Third-person config
- `Assets/KoboldKare/Scripts/SimpleCameraController.cs` — Free camera controller
- `Assets/KoboldKare/Scripts/GameManager.cs` — Game state, controls, layers
- `Assets/KoboldKare/Scripts/PlayerPossession.cs` — Player input aggregation
- `Assets/KoboldKare/Scripts/GenericUsable.cs` — Interaction base class
- `Assets/KoboldKare/Scripts/ActionStation.cs` — Station interaction
- `Assets/KoboldKare/Scripts/CharacterControllerAnimator.cs` — Animation + IK
- `Assets/KoboldKare/Scripts/PopupHandler.cs` — UI popup system
- `Assets/KoboldKare/Input/PlayerControls.cs` — Unity Input System actions

### KK-LLM-NPC References (structure/patterns)
- `src/Main.cs` — BepInEx plugin structure, config, Photon callback
- `src/Patches.cs` — Harmony patch pattern
- `src/Body.cs` — Body possession, camera setup, Photon ownership
- `src/ModuleRegistry.cs` — Extension registry pattern
- `build.sh` — Build script
