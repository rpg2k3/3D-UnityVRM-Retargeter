# Retarget Appliance

A Unity Editor tool for baking Mixamo animations onto VRM models and exporting as GLB files with embedded animations.

## Overview

Retarget Appliance automates the process of:
1. Converting Mixamo FBX animations to Humanoid format
2. Retargeting animations onto VRM character models
3. Baking the retargeted animations as transform curves
4. Exporting the models with baked animations as GLB files

This is designed for workflows where you need VRM characters with Mixamo animations exported to GLB format (e.g., for use with PixelOver or other external tools).

## Requirements

- **Unity 2022.3 LTS** or compatible version
- **UniVRM** (VRM0 or VRM1.0) - for importing VRM files
- **UnityGLTF** - for exporting GLB files (install via Package Manager)

### Installing UnityGLTF

1. Open **Window > Package Manager**
2. Click the **+** button in the top-left corner
3. Select **Add package from git URL...**
4. Enter: `https://github.com/KhronosGroup/UnityGLTF.git`
5. Click **Add**

## Folder Structure

```
Assets/
├── Input/
│   ├── Targets/          # Place VRM files here
│   └── Animations/       # Place Mixamo FBX files here
├── Output/
│   ├── RetargetedPrefabs/  # Baked animation clips and preview prefabs
│   └── Export/             # GLB exports
├── Retargeter/
│   └── Editor/           # Tool scripts (this folder)
└── Scenes/
    └── RetargetWorkspace.unity  # Temporary scene for processing
```

## Quick Start (One-Click Usage)

1. **Prepare your files:**
   - Place VRM character files in `Assets/Input/Targets/`
   - Place Mixamo FBX animation files in `Assets/Input/Animations/`

2. **Open the tool:**
   - Menu: **Tools > Retarget Appliance**

3. **Set up animations (first time only):**
   - Click **"Force Reimport Animations as Humanoid"**
   - This converts all FBX files to Humanoid animation type

4. **Bake and export:**
   - Click **"Bake + Export GLB for ALL Targets"**
   - Wait for the process to complete
   - Find your GLB files in `Assets/Output/Export/`

## Settings

| Setting | Description | Default |
|---------|-------------|---------|
| **Bake FPS** | Frame rate for baked animations | 30 |
| **Include Root Motion** | Include root transform animation | OFF |
| **Export Scale** | Scale multiplier for exported positions | 1.0 |

## VRM Bone Corrections

VRM models (especially those from VRoid Studio or imported via UniVRM) often have local bone axes that differ from standard Mixamo/Unity conventions. This can cause retargeted animations to display incorrectly, with feet appearing rotated outward or inward even though the animation motion itself is correct.

### How It Works

The VRM Bone Corrections feature applies full XYZ Euler rotation offsets to foot and toe bones during the baking process. These corrections are "baked in" to the exported animation curves, so the final GLB/FBX files will display correctly in any viewer.

### Settings

| Setting | Description | Default |
|---------|-------------|---------|
| **Apply VRM Bone Corrections** | Master toggle to enable/disable corrections | ON |
| **Apply Foot Offsets** | Enable correction for LeftFoot/RightFoot | ON |
| **Apply Toe Offsets** | Enable correction for LeftToes/RightToes | ON |
| **Left Foot Offset** | XYZ Euler rotation offset for left foot (degrees) | (0, -90, 0) |
| **Right Foot Offset** | XYZ Euler rotation offset for right foot (degrees) | (0, +90, 0) |
| **Left Toes Offset** | XYZ Euler rotation offset for left toes (degrees) | (0, 0, 0) |
| **Right Toes Offset** | XYZ Euler rotation offset for right toes (degrees) | (0, 0, 0) |

### Quick Presets

| Preset | Description | Left Foot | Right Foot |
|--------|-------------|-----------|------------|
| **VRoid A (Y±90)** | Y-axis correction (most common) | (0, -90, 0) | (0, +90, 0) |
| **VRoid B (Z±90)** | Z-axis correction | (0, 0, -90) | (0, 0, +90) |
| **VRoid C (X±90)** | X-axis correction | (-90, 0, 0) | (+90, 0, 0) |
| **Toe = Foot** | Copies foot offsets to toes | - | - |

### Adjusting Offsets

If feet still point incorrectly after baking with default settings:

1. **Try different presets**: Click VRoid A, B, or C to test different axis corrections
2. **Use Debug mode**: Enable "Debug: Print Foot Forward" and click "Validate Inputs" to see bone orientations in Console
3. **Adjust manually**: Expand "Advanced VRM Offsets" to fine-tune XYZ values
4. **Include toes**: Click "Toe = Foot" to apply the same correction to toe bones
5. **Mirror values**: Use "Mirror Left → Right" to ensure symmetric corrections

### Debug Mode

Enable **"Debug: Print Foot Forward"** checkbox, then click **"Validate Inputs"** to print each foot bone's forward/up/right vectors to the Console. This helps determine which axis is misaligned:

```
[RetargetAppliance] [CatGirl] === Foot Forward Debug ===
[RetargetAppliance] [CatGirl] Root Forward: (0.000, 0.000, 1.000)
[RetargetAppliance] [CatGirl] LeftFoot Forward: (1.000, 0.000, 0.000)  <- pointing sideways!
[RetargetAppliance] [CatGirl] LeftFoot LocalEuler: (0.0, 90.0, 0.0)
```

If foot forward doesn't match root forward, apply an offset to correct it.

### Mirror Button

The "Mirror Left → Right" button copies left side offset values to right side with all components negated:
- Left Foot (0, -90, 0) → Right Foot (0, +90, 0)
- Left Toes (-15, -30, 0) → Right Toes (+15, +30, 0)

### Technical Details

- Corrections are applied to a temporary duplicate of the model during baking
- The original scene/prefab is never modified
- Only VRM targets are corrected (detection is automatic)
- If a non-VRM model is processed, corrections are skipped with a warning
- Missing bones (e.g., no toe bones) are skipped with a warning
- Full XYZ Euler support allows correction of any axis misalignment

## Mixamo Download Settings

For best results when downloading from Mixamo:

- **Format:** FBX Binary (.fbx)
- **Skin:** Without Skin (recommended for animation-only files)
- **Frames per Second:** 30
- **Keyframe Reduction:** None (for quality) or Uniform (for smaller files)

**Note:** "In Place" option is recommended for most animations to avoid root motion issues.

## How It Works

### 1. Import Phase
- Scans `Assets/Input/Animations/` for FBX files
- Sets each FBX to Humanoid animation type
- Extracts animation clips from each FBX

### 2. Target Scanning
- Scans `Assets/Input/Targets/` for VRM files
- Locates the prefab that UniVRM created for each VRM
- Validates that each target has a proper Humanoid Avatar

### 3. Baking Phase
For each target and each animation:
- Creates a PlayableGraph to sample the source Humanoid animation
- Evaluates the animation at each frame interval (1/FPS seconds)
- Records transform curves (position, rotation, scale) for every bone
- Saves the baked clip as a `.anim` asset

### 4. Export Phase
- Attaches baked animations to the target model
- Exports as GLB using UnityGLTF
- Saves to `Assets/Output/Export/<TargetName>.glb`

## Output Files

After processing, you'll find:

- **GLB Files:** `Assets/Output/Export/<TargetName>.glb`
  - Complete model with embedded baked animations
  - Ready for use in external applications

- **Animation Assets:** `Assets/Output/RetargetedPrefabs/<TargetName>/Animations/`
  - Individual `.anim` files for each baked clip
  - Can be used within Unity

- **Preview Prefabs:** `Assets/Output/RetargetedPrefabs/<TargetName>/`
  - Prefab with Animator Controller for previewing animations in Unity
  - AnimatorController with states for each baked clip

## Troubleshooting

### "No VRM targets found"
- Ensure VRM files are in `Assets/Input/Targets/`
- VRM files must have been imported by UniVRM

### "Could not find prefab for VRM"
- UniVRM creates prefabs when importing VRMs
- Try reimporting the VRM file
- Check that UniVRM is properly installed

### "Avatar is not Humanoid"
- The VRM model must have a valid Humanoid Avatar
- Most VRM files should work automatically

### "UnityGLTF is not installed"
- Follow the installation instructions above
- Restart Unity after installing

### Animations look wrong after baking
- Verify the source FBX is set to Humanoid (use "Force Reimport" button)
- Check that the VRM has proper bone mapping
- Try disabling "Include Root Motion" if character moves unexpectedly

### Feet point outward/inward in exported animation
- Enable "Apply VRM Bone Corrections" in settings
- Try different quick presets: VRoid A (Y±90), VRoid B (Z±90), VRoid C (X±90)
- Enable "Debug: Print Foot Forward" and click "Validate Inputs" to see bone orientations
- If still incorrect, expand "Advanced VRM Offsets" and adjust XYZ values manually
- Click "Toe = Foot" if toes also need the same correction
- Use "Mirror Left → Right" to ensure symmetric corrections

### Export fails
- Check Console for detailed error messages
- Ensure UnityGLTF is installed and up to date
- Try exporting a simple test object to verify UnityGLTF works

## Version History

### V1.2
- Enhanced VRM Bone Corrections with full XYZ Euler support
  - Vector3 offsets (X, Y, Z) for each bone instead of Y-only
  - Quick presets: VRoid A (Y±90), VRoid B (Z±90), VRoid C (X±90)
  - "Toe = Foot" button to copy foot offsets to toes
  - Separate toggles for foot and toe correction
  - Debug mode: "Print Foot Forward" to diagnose axis misalignment

### V1.1
- Added VRM Bone Corrections feature
  - Fixes feet rotation issues in VRoid/UniVRM models
  - Configurable yaw offsets for feet and toes
  - Preset profiles for common VRM types
  - Corrections baked into exported animations

### V1.0
- Initial release
- Folder-based VRM and FBX scanning
- Automatic Humanoid FBX conversion
- PlayableGraph-based animation baking
- GLB export with UnityGLTF
- Preview prefab generation

## License

This tool is provided as-is for use within your Unity projects.

## Support

For issues or feature requests, please check:
- Unity Console for detailed error messages
- Validation output in the tool window
- This README for common solutions
