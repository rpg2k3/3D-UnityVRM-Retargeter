using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityGLTF;

namespace RetargetAppliance
{
    /// <summary>
    /// Handles GLB and FBX export with baked animations.
    /// GLB uses UnityGLTF directly; FBX uses reflection to avoid compile-time dependency.
    /// </summary>
    public static class RetargetApplianceExporter
    {
        /// <summary>
        /// Export format options.
        /// </summary>
        public enum ExportFormat
        {
            GLB,
            FBX,
            Both
        }

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
        /// Combined export result for GLB and FBX.
        /// </summary>
        public class CombinedExportResult
        {
            public string TargetName;
            public ExportResult GLBResult;
            public ExportResult FBXResult;

            public bool AnySuccess => (GLBResult?.Success ?? false) || (FBXResult?.Success ?? false);
            public bool AllRequestedSuccess(ExportFormat format)
            {
                switch (format)
                {
                    case ExportFormat.GLB:
                        return GLBResult?.Success ?? false;
                    case ExportFormat.FBX:
                        return FBXResult?.Success ?? false;
                    case ExportFormat.Both:
                        return (GLBResult?.Success ?? false) && (FBXResult?.Success ?? false);
                    default:
                        return false;
                }
            }
        }

        // Cached reflection data for FBX Exporter
        private static bool _fbxExporterChecked = false;
        private static bool _fbxExporterAvailable = false;
        private static Type _modelExporterType = null;
        private static MethodInfo _exportObjectMethod = null;

        /// <summary>
        /// Checks if UnityGLTF is available in the project.
        /// Since we use compile-time references, this always returns true.
        /// </summary>
        public static bool IsUnityGLTFAvailable()
        {
            return true;
        }

        /// <summary>
        /// Checks if the FBX Exporter package is available via reflection.
        /// </summary>
        public static bool IsFBXExporterAvailable()
        {
            if (_fbxExporterChecked)
            {
                return _fbxExporterAvailable;
            }

            _fbxExporterChecked = true;
            _fbxExporterAvailable = false;

            try
            {
                // Try to find the FBX Exporter assembly
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "Unity.Formats.Fbx.Editor")
                    {
                        // Found the assembly, now find ModelExporter type
                        _modelExporterType = assembly.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter");

                        if (_modelExporterType != null)
                        {
                            // Find ExportObject method: static string ExportObject(string filePath, UnityEngine.Object singleObject)
                            _exportObjectMethod = _modelExporterType.GetMethod(
                                "ExportObject",
                                BindingFlags.Public | BindingFlags.Static,
                                null,
                                new Type[] { typeof(string), typeof(UnityEngine.Object) },
                                null
                            );

                            if (_exportObjectMethod != null)
                            {
                                _fbxExporterAvailable = true;
                                RetargetApplianceUtil.LogInfo("FBX Exporter package detected via reflection.");
                            }
                        }
                        break;
                    }
                }

                if (!_fbxExporterAvailable)
                {
                    RetargetApplianceUtil.LogWarning("FBX Exporter package not found. FBX export will be unavailable.");
                }
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.LogWarning($"Error checking for FBX Exporter: {ex.Message}");
            }

            return _fbxExporterAvailable;
        }

        /// <summary>
        /// Gets installation instructions for the FBX Exporter package.
        /// </summary>
        public static string GetFBXExporterInstallInstructions()
        {
            return @"FBX Exporter Installation Instructions:

Option 1 - Package Manager (Recommended):
1. Open Window > Package Manager
2. In the dropdown at the top-left, select 'Unity Registry'
3. Search for 'FBX Exporter'
4. Click 'Install'

Option 2 - Via manifest.json:
1. Open Packages/manifest.json
2. Add to dependencies:
   ""com.unity.formats.fbx"": ""4.2.1""
3. Save and let Unity resolve packages

After installation, restart Unity and try again.";
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
        /// Exports a target GameObject with baked animations as FBX using reflection.
        /// </summary>
        public static ExportResult ExportAsFBX(
            GameObject targetInstance,
            string targetName,
            List<AnimationClip> bakedClips,
            RetargetApplianceBaker.BakeSettings settings)
        {
            var result = new ExportResult
            {
                TargetName = targetName
            };

            // Check if FBX Exporter is available
            if (!IsFBXExporterAvailable())
            {
                result.Error = "FBX Exporter package not installed. Please install 'com.unity.formats.fbx' from Package Manager.";
                RetargetApplianceUtil.LogError(result.Error);
                return result;
            }

            try
            {
                // Ensure export folder exists
                string unityRelativePath = RetargetApplianceUtil.OutputExportPath;
                RetargetApplianceUtil.EnsureFolderExists(unityRelativePath);

                string exportFolder = Path.GetFullPath(unityRelativePath);
                if (!Directory.Exists(exportFolder))
                {
                    Directory.CreateDirectory(exportFolder);
                }

                // Prepare target for FBX export with animations
                PrepareTargetForFBXExport(targetInstance, bakedClips);

                // Build the output path
                string fbxFileName = $"{targetName}.fbx";
                string absoluteOutPath = Path.Combine(exportFolder, fbxFileName);
                result.ExportPath = $"{unityRelativePath}/{fbxFileName}";

                // Call ModelExporter.ExportObject via reflection
                bool exportSuccess = ExportWithFBXExporter(absoluteOutPath, targetInstance);

                if (exportSuccess)
                {
                    result.Success = true;
                    RetargetApplianceUtil.LogInfo($"Exported FBX: {result.ExportPath}");

                    // Note about animations
                    if (bakedClips != null && bakedClips.Count > 0)
                    {
                        RetargetApplianceUtil.LogInfo($"FBX export includes {bakedClips.Count} animation clip(s) via Animation component.");
                    }
                }
                else
                {
                    result.Error = "FBX export failed. Check console for details.";
                }

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                RetargetApplianceUtil.LogError($"FBX export failed: {ex.Message}");
                Debug.LogException(ex);
            }

            return result;
        }

        /// <summary>
        /// Performs the actual FBX export using reflection to call ModelExporter.ExportObject.
        /// </summary>
        private static bool ExportWithFBXExporter(string absoluteOutPath, GameObject targetInstance)
        {
            if (_exportObjectMethod == null)
            {
                RetargetApplianceUtil.LogError("FBX ExportObject method not found.");
                return false;
            }

            try
            {
                // Call: string ModelExporter.ExportObject(string filePath, UnityEngine.Object singleObject)
                object resultPath = _exportObjectMethod.Invoke(null, new object[] { absoluteOutPath, targetInstance });

                if (resultPath is string exportedPath && !string.IsNullOrEmpty(exportedPath))
                {
                    RetargetApplianceUtil.LogInfo($"FBX Exporter returned: {exportedPath}");
                    return true;
                }
                else
                {
                    RetargetApplianceUtil.LogWarning("FBX Exporter returned null or empty path.");
                    return false;
                }
            }
            catch (TargetInvocationException tie)
            {
                RetargetApplianceUtil.LogError($"FBX export invocation error: {tie.InnerException?.Message ?? tie.Message}");
                Debug.LogException(tie.InnerException ?? tie);
                return false;
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.LogError($"FBX export error: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>
        /// Prepares a target GameObject for FBX export.
        /// Attaches animation clips to a legacy Animation component for FBX Exporter to pick up.
        /// </summary>
        private static void PrepareTargetForFBXExport(GameObject target, List<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                RetargetApplianceUtil.LogWarning("No animation clips to include in FBX export.");
                return;
            }

            // FBX Exporter can export animations attached to Animation (legacy) component.
            // Add or get the Animation component
            var animation = target.GetComponent<Animation>();
            if (animation == null)
            {
                animation = target.AddComponent<Animation>();
            }

            // Clear existing clips
            animation.clip = null;

            int addedCount = 0;

            foreach (var clip in clips)
            {
                if (clip != null)
                {
                    // Create a copy and set to Legacy mode
                    var clipCopy = UnityEngine.Object.Instantiate(clip);
                    clipCopy.legacy = true;
                    clipCopy.name = clip.name;

                    animation.AddClip(clipCopy, clip.name);

                    // Set first clip as default
                    if (animation.clip == null)
                    {
                        animation.clip = clipCopy;
                    }

                    addedCount++;
                }
            }

            RetargetApplianceUtil.LogInfo($"Prepared {addedCount} animation clip(s) for FBX export on '{target.name}'");
        }

        /// <summary>
        /// Exports a target in both GLB and FBX formats based on the selected format.
        /// </summary>
        public static CombinedExportResult ExportTarget(
            GameObject targetInstance,
            string targetName,
            List<AnimationClip> bakedClips,
            RetargetApplianceBaker.BakeSettings settings,
            ExportFormat format)
        {
            var result = new CombinedExportResult
            {
                TargetName = targetName
            };

            // Export GLB if requested
            if (format == ExportFormat.GLB || format == ExportFormat.Both)
            {
                result.GLBResult = ExportAsGLB(targetInstance, targetName, bakedClips, settings);
            }

            // Export FBX if requested
            if (format == ExportFormat.FBX || format == ExportFormat.Both)
            {
                result.FBXResult = ExportAsFBX(targetInstance, targetName, bakedClips, settings);
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
