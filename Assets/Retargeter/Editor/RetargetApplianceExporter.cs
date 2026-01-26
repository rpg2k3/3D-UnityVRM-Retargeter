using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityGLTF;

namespace RetargetAppliance
{
    /// <summary>
    /// Handles GLB export with baked animations using UnityGLTF.
    /// Uses direct compile-time references to UnityGLTF API.
    /// </summary>
    public static class RetargetApplianceExporter
    {
        /// <summary>
        /// Export result for a single target.
        /// </summary>
        public class ExportResult
        {
            public string TargetName;
            public string ExportPath;
            public bool Success;
            public string Error;
        }

        /// <summary>
        /// Checks if UnityGLTF is available in the project.
        /// Since we use compile-time references, this always returns true.
        /// </summary>
        public static bool IsUnityGLTFAvailable()
        {
            return true;
        }

        /// <summary>
        /// Exports a target GameObject with baked animations as GLB.
        /// </summary>
        public static ExportResult ExportAsGLB(
            GameObject targetInstance,
            string targetName,
            List<AnimationClip> bakedClips,
            RetargetApplianceBaker.BakeSettings settings)
        {
            var result = new ExportResult
            {
                TargetName = targetName
            };

            try
            {
                // Ensure export folder exists (convert Unity-relative path to absolute)
                string unityRelativePath = RetargetApplianceUtil.OutputExportPath;
                RetargetApplianceUtil.EnsureFolderExists(unityRelativePath);

                string exportFolder = Path.GetFullPath(unityRelativePath);

                // Also ensure the absolute path directory exists
                if (!Directory.Exists(exportFolder))
                {
                    Directory.CreateDirectory(exportFolder);
                }

                // Set up the target for export with animation clips
                PrepareTargetForExport(targetInstance, bakedClips);

                // Export filename (without extension - SaveGLB adds .glb)
                string fileName = targetName;
                result.ExportPath = $"{unityRelativePath}/{targetName}.glb";

                // Perform export using UnityGLTF directly
                bool exportSuccess = ExportWithUnityGLTF(targetInstance.transform, exportFolder, fileName, bakedClips);

                if (exportSuccess)
                {
                    result.Success = true;
                    RetargetApplianceUtil.LogInfo($"Exported GLB: {result.ExportPath}");
                }
                else
                {
                    result.Error = "Export failed. Check console for details.";
                }

                // Refresh asset database so Unity sees the new file
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                RetargetApplianceUtil.LogError($"Export failed: {ex.Message}");
                Debug.LogException(ex);
            }

            return result;
        }

        /// <summary>
        /// Prepares a target GameObject for GLB export by adding necessary components.
        /// Adds Animation component with legacy clips so UnityGLTF will export them.
        /// </summary>
        private static void PrepareTargetForExport(GameObject target, List<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                RetargetApplianceUtil.LogWarning("No animation clips to export.");
                return;
            }

            // Add Animation component (Legacy) if not present
            var animation = target.GetComponent<Animation>();
            if (animation == null)
            {
                animation = target.AddComponent<Animation>();
            }

            // Clear existing clips
            animation.clip = null;

            int addedCount = 0;

            // Add all baked clips to the Animation component
            foreach (var clip in clips)
            {
                if (clip != null)
                {
                    // Create a copy and set to Legacy mode for the Animation component
                    var clipCopy = UnityEngine.Object.Instantiate(clip);
                    clipCopy.legacy = true;
                    clipCopy.name = clip.name;

                    animation.AddClip(clipCopy, clip.name);

                    // Set the first clip as the default
                    if (animation.clip == null)
                    {
                        animation.clip = clipCopy;
                    }

                    addedCount++;
                }
            }

            RetargetApplianceUtil.LogInfo($"Prepared {addedCount} animation clips for export on '{target.name}'");
        }

        /// <summary>
        /// Performs the actual GLB export using UnityGLTF's GLTFSceneExporter directly.
        /// </summary>
        private static bool ExportWithUnityGLTF(Transform rootTransform, string exportFolder, string fileName, List<AnimationClip> clips)
        {
            try
            {
                // Get or create GLTFSettings and configure for animation export
                var gltfSettings = GLTFSettings.GetOrCreateSettings();

                // Store original settings to restore later
                bool originalExportAnimations = gltfSettings.ExportAnimations;

                // Enable animation export
                gltfSettings.ExportAnimations = true;

                try
                {
                    // Create export context with settings
                    var exportContext = new ExportContext(gltfSettings);

                    // Create the exporter with the root transform
                    var exporter = new GLTFSceneExporter(
                        new Transform[] { rootTransform },
                        exportContext
                    );

                    // Export as GLB
                    // SaveGLB takes (path, fileName) where path is the directory
                    exporter.SaveGLB(exportFolder, fileName);

                    RetargetApplianceUtil.LogInfo($"GLB export completed: {Path.Combine(exportFolder, fileName)}.glb");

                    // Log animation status
                    if (clips != null && clips.Count > 0)
                    {
                        RetargetApplianceUtil.LogInfo($"Animation clips prepared for export: {clips.Count}");
                    }

                    return true;
                }
                finally
                {
                    // Restore original animation export setting
                    gltfSettings.ExportAnimations = originalExportAnimations;
                }
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.LogError($"UnityGLTF export error: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>
        /// Validates that export requirements are met.
        /// With compile-time references, this always succeeds.
        /// </summary>
        public static bool ValidateExportRequirements(out string errorMessage)
        {
            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Gets instructions for installing UnityGLTF (kept for compatibility).
        /// </summary>
        public static string GetUnityGLTFInstallInstructions()
        {
            return @"UnityGLTF Installation Instructions:

Option 1 - Package Manager (Recommended):
1. Open Window > Package Manager
2. Click the '+' button in the top-left
3. Select 'Add package from git URL...'
4. Enter: https://github.com/KhronosGroup/UnityGLTF.git
5. Click 'Add'

Option 2 - Manual Download:
1. Download from: https://github.com/KhronosGroup/UnityGLTF/releases
2. Extract to your project's Packages folder

Option 3 - OpenUPM:
1. Install OpenUPM CLI: npm install -g openupm-cli
2. Run: openupm add org.khronos.unitygltf

After installation, restart Unity and try again.";
        }
    }
}
