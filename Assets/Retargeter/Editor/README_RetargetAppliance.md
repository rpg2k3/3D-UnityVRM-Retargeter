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

### Export fails
- Check Console for detailed error messages
- Ensure UnityGLTF is installed and up to date
- Try exporting a simple test object to verify UnityGLTF works

## Version History

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
