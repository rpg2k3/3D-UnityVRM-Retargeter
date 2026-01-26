# UiVRM-Retarget

A Unity-based pipeline for retargeting Mixamo animations to VRM avatars and exporting animated GLB files for use in PixelOver.

## What It Does

This tool takes VRM avatar models and Mixamo FBX animations, retargets the animations to the VRM rig, and exports the result as GLB files compatible with PixelOver's sprite rendering pipeline.

**Pipeline:** VRM + Mixamo FBX → Retarget → Animated GLB → PixelOver

## Requirements

- Unity 2022.3 LTS
- [UniVRM](https://github.com/vrm-c/UniVRM) - VRM import/export
- [UnityGLTF](https://github.com/KhronosGroup/UnityGLTF) - GLB export

## Usage

1. **Import Avatar**: Drop your VRM file into the Unity project
2. **Import Animation**: Drop your Mixamo FBX animation file
3. **Retarget**: Use the retargeting tools to apply Mixamo animation to VRM rig
4. **Export**: Bake the animation and export as GLB
5. **Use in PixelOver**: Import the GLB into PixelOver for sprite sheet generation

## Output

Exports animated GLB files that can be imported into [PixelOver](https://pixelover.io/) for automated pixel art sprite sheet generation.

## License

This project is for personal/educational use.
