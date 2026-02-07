# UiVRM-Retarget

A Unity editor tool for retargeting Mixamo animations to VRM avatars and exporting animated GLB / VRMA files.

**Pipeline:** VRM + Mixamo FBX &rarr; Retarget &rarr; Bake &rarr; GLB / VRMA

---

## Features

### Export Formats
- **GLB** &ndash; Animated model via UnityGLTF, ready for PixelOver or any glTF viewer.
- **VRMA** &ndash; VRM Animation files for use with VRM-compatible applications.
- **FBX** &ndash; Available in legacy mode (requires Unity FBX Exporter).

### Pipeline Modes
- **VRMA-First (default)** &ndash; Bakes animations and exports VRMA and/or GLB independently.
- **Legacy** &ndash; Older round-trip pipeline with GLB, FBX, or VRMA export.

### Animation Baking
- Configurable bake FPS (12&ndash;60, default 30).
- Optional root motion inclusion.
- Adjustable export scale.
- Batch processing &ndash; multiple VRM targets and FBX clips in one run.

### VRM Foot & Toe Corrections
- **Auto-fix foot direction** &ndash; Aligns foot forward vector to match hips.
- **Presets** &ndash; VRoid A (Y&plusmn;90), VRoid B (Z&plusmn;90), VRoid C (X&plusmn;90), or custom per-bone XYZ offsets.
- **Toe correction** toggle and per-side (L/R) toggles.
- **Mirror L&rarr;R** utility for symmetric offsets.
- Debug print mode to inspect foot forward vectors in the console.

### Advanced Stabilization (Legacy Fixes)
- **Toe stabilization** &ndash; DampenRotation or ToeFollowsFoot modes with adjustable strength.
- **Foot stabilization** &ndash; Pitch (up/down) and roll (in/out) clamping or dampening.
- **Toe yaw correction** &ndash; ClampYaw or BlendTowardIdentity with configurable limits.

### Editor UX
- Dependency status indicators (GLB, FBX, VRMA) in the window header.
- Auto-discovery of VRM targets and FBX animations with humanoid status badges.
- One-click "Force Reimport as Humanoid" for all FBX files.
- Input validation with detailed error/warning messages.
- Progress bar during baking.
- Quick-open buttons for input and output folders.

---

## Installation

### Requirements
- **Unity 2022.3 LTS** (tested on 2022.3.14f1)

### 1. Create or open a Unity project

Open Unity Hub, create a new 3D project (or open an existing one) using a **2022.3 LTS** editor version.

### 2. Install UniVRM

UniVRM is required to import VRM models.

1. Download the latest UniVRM `.unitypackage` from the [UniVRM releases page](https://github.com/vrm-c/UniVRM/releases).
2. In Unity: **Assets &rarr; Import Package &rarr; Custom Package...** and select the downloaded file.
3. Let Unity compile. VRM files dropped into the project will now auto-import.

### 3. Install UnityGLTF

UnityGLTF is required for GLB export.

1. In Unity: **Window &rarr; Package Manager**.
2. Click the **+** button &rarr; **Add package from git URL...**
3. Enter:
   ```
   https://github.com/KhronosGroup/UnityGLTF.git
   ```
4. Click **Add** and wait for installation.

### 4. Install FBX Exporter (optional, legacy mode only)

Only needed if you want to export FBX files via the legacy pipeline.

1. In Unity: **Window &rarr; Package Manager**.
2. Search for **FBX Exporter** in the Unity Registry.
3. Click **Install**.

### 5. Add the Retarget Appliance scripts

Copy (or clone) the `Assets/Retargeter` folder into your project's `Assets/` directory. Unity will compile the editor scripts automatically.

### 6. Create the folder structure

The tool expects the following folders (create them if they don't exist):

```
Assets/
  Input/
    Targets/        # VRM files go here
    Animations/     # Mixamo FBX files go here
  Output/           # Created automatically during export
```

---

## How to Use

### Step 1 &ndash; Prepare input files

- **VRM avatars**: Place `.vrm` files in `Assets/Input/Targets/`. UniVRM will auto-import them and create prefabs.
- **Mixamo animations**: Download animations from [Mixamo](https://www.mixamo.com/) as **FBX Binary** (.fbx). Place them in `Assets/Input/Animations/`.

### Step 2 &ndash; Open the tool

**Tools &rarr; Retarget Appliance** from the Unity menu bar.

The window header shows dependency status:
- **GLB: OK** / **FBX: OK** / **VRMA: OK** &ndash; green means the package is installed.
- **N/A** (yellow) means the package is missing.

### Step 3 &ndash; Set animations to Humanoid (first time)

Click **"Force Reimport as Humanoid"** at the bottom of the window. This sets every FBX file in the Animations folder to the Humanoid animation type so Unity can retarget them.

### Step 4 &ndash; Configure settings

Expand **Bake & Export Settings**:

| Setting | Default | Description |
|---------|---------|-------------|
| Bake FPS | 30 | Frame rate of baked animation clips |
| Include Root Motion | Off | Include root transform movement |
| Export Scale | 1.0 | Position scale multiplier |
| Export VRMA library | On | Output `.vrma` files |
| Export GLB | On | Output `.glb` files |

Expand **VRM Foot Corrections** (recommended):

- Enable **foot corrections** and **auto-fix foot direction** to prevent feet from pointing sideways.
- Select a **preset** (VRoid A works for most VRoid-generated models) or enter custom offsets.
- Enable **toe correction** if toe bones are present.

Advanced stabilization options are available under the **Advanced (Legacy Fixes)** foldout for fine-tuning toe and foot rotation.

### Step 5 &ndash; Validate

Click **"Validate Inputs"**. The tool checks that targets, animations, and exporters are correctly configured and reports any issues.

### Step 6 &ndash; Bake and export

Click the main action button (label updates based on your settings, e.g. **"Bake + Export VRMA + GLB"**). A confirmation dialog shows what will be processed. The bake runs with a progress bar.

### Step 7 &ndash; Access results

Use the folder buttons at the bottom of the window or navigate manually:

```
Assets/Output/
  VRMA/<TargetName>/            # .vrma files
  GLB/<TargetName>/             # .glb files
  RetargetedPrefabs/<TargetName>/
    Animations/                 # Baked .anim clips
    <TargetName>.prefab         # Preview prefab
    <TargetName>_Controller.controller
```

GLB files can be imported directly into [PixelOver](https://pixelover.io/) for automated sprite sheet generation.

---

## License

This project is for personal/educational use.
