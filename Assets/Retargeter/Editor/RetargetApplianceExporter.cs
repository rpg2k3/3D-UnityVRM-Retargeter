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
        /// Temporarily disables the Animator component to prevent it from interfering with export.
        /// The exporter should only use the Legacy Animation component, not the Animator controller.
        /// </summary>
        private sealed class AnimatorExportOverride : IDisposable
        {
            private readonly Animator _animator;
            private readonly bool _originalEnabled;
            private readonly RuntimeAnimatorController _originalController;

            public AnimatorExportOverride(GameObject target)
            {
                _animator = target != null ? target.GetComponent<Animator>() : null;

                if (_animator != null)
                {
                    // Store original state
                    _originalEnabled = _animator.enabled;
                    _originalController = _animator.runtimeAnimatorController;

                    // Disable animator and clear controller to prevent interference
                    _animator.enabled = false;
                    _animator.runtimeAnimatorController = null;

                    RetargetApplianceUtil.LogInfo($"[RetargetAppliance] AnimatorExportOverride: disabled Animator (was enabled={_originalEnabled}, controller={((_originalController != null) ? _originalController.name : "null")})");
                }
            }

            public bool AnimatorWasEnabled => _originalEnabled;
            public bool ControllerWasNull => _originalController == null;

            public void Dispose()
            {
                if (_animator != null)
                {
                    // Restore original state
                    _animator.runtimeAnimatorController = _originalController;
                    _animator.enabled = _originalEnabled;

                    RetargetApplianceUtil.LogInfo("[RetargetAppliance] AnimatorExportOverride: restored Animator state");
                }
            }
        }

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
        /// <param name="targetInstance">The GameObject to export.</param>
        /// <param name="targetName">The target name (used for result tracking).</param>
        /// <param name="bakedClips">Animation clips to include.</param>
        /// <param name="settings">Bake settings.</param>
        /// <param name="outputFileName">Optional custom output filename (without extension). If null, uses targetName.</param>
        public static ExportResult ExportAsGLB(
            GameObject targetInstance,
            string targetName,
            List<AnimationClip> bakedClips,
            RetargetApplianceBaker.BakeSettings settings,
            string outputFileName = null)
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

                // Export filename (without extension - SaveGLB adds .glb)
                string fileName = outputFileName ?? targetName;
                result.ExportPath = $"{unityRelativePath}/{fileName}.glb";

                // CRITICAL: Disable Animator during export to prevent it from overriding Legacy Animation
                using (var animatorOverride = new AnimatorExportOverride(targetInstance))
                {
                    // Set up the target for export with animation clips
                    // This configures the Legacy Animation component with ONLY the intended clip(s)
                    PrepareTargetForExport(targetInstance, bakedClips);

                    // Perform export using UnityGLTF directly
                    bool exportSuccess = ExportWithUnityGLTF(targetInstance.transform, exportFolder, fileName, bakedClips, animatorOverride);

                    if (exportSuccess)
                    {
                        result.Success = true;
                        RetargetApplianceUtil.LogInfo($"Exported GLB: {result.ExportPath}");
                    }
                    else
                    {
                        result.Error = "Export failed. Check console for details.";
                    }
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
        /// <param name="targetInstance">The GameObject to export.</param>
        /// <param name="targetName">The target name (used for result tracking).</param>
        /// <param name="bakedClips">Animation clips to include.</param>
        /// <param name="settings">Bake settings.</param>
        /// <param name="outputFileName">Optional custom output filename (without extension). If null, uses targetName.</param>
        public static ExportResult ExportAsFBX(
            GameObject targetInstance,
            string targetName,
            List<AnimationClip> bakedClips,
            RetargetApplianceBaker.BakeSettings settings,
            string outputFileName = null)
        {
            var result = new ExportResult
            {
                TargetName = targetName
            };

            // Validate target
            if (targetInstance == null)
            {
                result.Error = "Target GameObject is null.";
                RetargetApplianceUtil.LogError($"[RetargetAppliance] {result.Error}");
                return result;
            }

            // Check if FBX Exporter is available
            if (!IsFBXExporterAvailable())
            {
                result.Error = "FBX Exporter package not installed. Please install 'com.unity.formats.fbx' from Package Manager.";
                RetargetApplianceUtil.LogError(result.Error);
                return result;
            }

            // Track state for cleanup
            bool animationWasAdded = false;
            AnimationClip originalDefaultClip = null;
            List<AnimationClip> tempClips = new List<AnimationClip>();
            Animation animation = null;

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

                // Build the output path using custom filename if provided
                string fileName = outputFileName ?? targetName;
                string fbxFileName = $"{fileName}.fbx";
                string absoluteOutPath = Path.Combine(exportFolder, fbxFileName);
                result.ExportPath = $"{unityRelativePath}/{fbxFileName}";

                // CRITICAL: Disable Animator during export to prevent it from overriding Legacy Animation
                using (var animatorOverride = new AnimatorExportOverride(targetInstance))
                {
                    // Ensure Animation component exists (required for FBX Exporter to find animations)
                    animation = EnsureLegacyAnimation(targetInstance, out animationWasAdded);

                    // Store original default clip if Animation existed before
                    if (!animationWasAdded && animation != null)
                    {
                        originalDefaultClip = animation.clip;
                    }

                    // Attach baked clips if available
                    if (bakedClips != null && bakedClips.Count > 0)
                    {
                        tempClips = AttachClipsForFBXExport(animation, bakedClips, settings);

                        // Force the Animation component to use the intended clip
                        if (tempClips.Count > 0 && animation != null)
                        {
                            animation.clip = tempClips[0];
                            animation.Play(animation.clip.name);
                            animation.Sample();

                            RetargetApplianceUtil.LogInfo($"[RetargetAppliance] FBX exporting using Legacy Animation default clip: {animation.clip.name}, animatorEnabled={animatorOverride.AnimatorWasEnabled}, controllerNull={animatorOverride.ControllerWasNull}");
                        }
                    }
                    else
                    {
                        RetargetApplianceUtil.LogWarning("[RetargetAppliance] No animation clips provided; FBX will export mesh+skeleton only.");
                    }

                    // Call ModelExporter.ExportObject via reflection
                    bool exportSuccess = ExportWithFBXExporter(absoluteOutPath, targetInstance);

                    if (exportSuccess)
                    {
                        result.Success = true;
                        int clipCount = tempClips.Count;
                        RetargetApplianceUtil.LogInfo($"[RetargetAppliance] FBX export completed with embedded takes: {result.ExportPath}");

                        if (clipCount > 0)
                        {
                            RetargetApplianceUtil.LogInfo($"[RetargetAppliance] Exported {clipCount} animation take(s) to FBX.");
                        }
                    }
                    else
                    {
                        result.Error = "FBX export failed. Check console for details.";
                    }
                }

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                RetargetApplianceUtil.LogError($"[RetargetAppliance] FBX export failed: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                // Cleanup: restore original state so scene isn't polluted
                CleanupAfterFBXExport(targetInstance, animationWasAdded, originalDefaultClip, tempClips);
            }

            return result;
        }

        /// <summary>
        /// Ensures a legacy Animation component exists on the root GameObject.
        /// If not present, adds one. Returns the Animation component.
        /// </summary>
        /// <param name="root">The root GameObject to check/modify.</param>
        /// <param name="added">True if a new Animation component was added, false if it already existed.</param>
        /// <returns>The Animation component (never null if root is valid).</returns>
        /// <exception cref="ArgumentNullException">Thrown if root is null.</exception>
        private static Animation EnsureLegacyAnimation(GameObject root, out bool added)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root), "[RetargetAppliance] Cannot ensure Animation on null GameObject.");
            }

            added = false;
            Animation animation = root.GetComponent<Animation>();

            if (animation == null)
            {
                animation = root.AddComponent<Animation>();
                added = true;
            }

            RetargetApplianceUtil.LogInfo($"[RetargetAppliance] Ensured legacy Animation on '{root.name}' (added={added})");

            return animation;
        }

        /// <summary>
        /// Attaches baked animation clips to an Animation component for FBX export.
        /// CRITICAL: Clears ALL existing clips first to prevent contamination.
        /// Returns the list of temporary clip copies for cleanup.
        /// </summary>
        private static List<AnimationClip> AttachClipsForFBXExport(
            Animation animation,
            List<AnimationClip> clips,
            RetargetApplianceBaker.BakeSettings settings)
        {
            var tempClips = new List<AnimationClip>();

            if (animation == null)
            {
                RetargetApplianceUtil.LogError("[RetargetAppliance] Cannot attach clips: Animation component is null.");
                return tempClips;
            }

            if (clips == null || clips.Count == 0)
            {
                return tempClips;
            }

            // CRITICAL: Clear ALL existing clips from the Animation component
            // This prevents clips from previous exports contaminating this export
            ClearAllAnimationClips(animation);

            var clipNames = new List<string>();
            var usedNames = new HashSet<string>();
            AnimationClip firstClip = null;

            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }

                // Create a copy and set to Legacy mode (required for Animation component)
                var clipCopy = UnityEngine.Object.Instantiate(clip);
                clipCopy.legacy = true;

                // Ensure unique name
                string baseName = clip.name;
                string uniqueName = baseName;
                int suffix = 1;
                while (usedNames.Contains(uniqueName))
                {
                    uniqueName = $"{baseName}_{suffix}";
                    suffix++;
                }
                clipCopy.name = uniqueName;
                usedNames.Add(uniqueName);

                // Set wrap mode based on animation name patterns
                if (settings != null && IsLoopableAnimation(clip.name))
                {
                    clipCopy.wrapMode = WrapMode.Loop;
                }
                else
                {
                    clipCopy.wrapMode = WrapMode.Default;
                }

                // Add clip to Animation component
                animation.AddClip(clipCopy, uniqueName);
                clipNames.Add(uniqueName);
                tempClips.Add(clipCopy);

                // Track first clip for default
                if (firstClip == null)
                {
                    firstClip = clipCopy;
                }
            }

            // Set first clip as default
            if (firstClip != null)
            {
                animation.clip = firstClip;
            }

            // Configure playback settings
            animation.playAutomatically = false;

            // Log verification of Animation component state
            int totalClips = CountAnimationClips(animation);
            string defaultClipName = animation.clip != null ? animation.clip.name : "(null)";
            RetargetApplianceUtil.LogInfo($"[RetargetAppliance] ExportRoot default clip = '{defaultClipName}' clipsCount={totalClips}");

            if (clipNames.Count > 0)
            {
                RetargetApplianceUtil.LogInfo($"[RetargetAppliance] Animation takes: {string.Join(", ", clipNames)}");
            }

            return tempClips;
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
        /// Determines if an animation should loop based on its name.
        /// Common looping animations: walk, run, idle, locomotion.
        /// </summary>
        private static bool IsLoopableAnimation(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return false;

            string lowerName = clipName.ToLowerInvariant();

            // Common looping animation patterns
            return lowerName.Contains("walk") ||
                   lowerName.Contains("run") ||
                   lowerName.Contains("idle") ||
                   lowerName.Contains("locomotion") ||
                   lowerName.Contains("loop") ||
                   lowerName.Contains("cycle");
        }

        /// <summary>
        /// Cleans up temporary Animation component and clips after FBX export.
        /// Restores the GameObject to its original state.
        /// </summary>
        /// <param name="target">The target GameObject.</param>
        /// <param name="animationWasAdded">True if we added the Animation component (should be removed).</param>
        /// <param name="originalDefaultClip">The original default clip to restore (if Animation existed before).</param>
        /// <param name="tempClips">List of temporary clip copies to destroy.</param>
        private static void CleanupAfterFBXExport(
            GameObject target,
            bool animationWasAdded,
            AnimationClip originalDefaultClip,
            List<AnimationClip> tempClips)
        {
            if (target == null)
            {
                // Target was destroyed, just clean up temp clips
                DestroyTempClips(tempClips);
                return;
            }

            try
            {
                Animation animation = target.GetComponent<Animation>();

                if (animation != null)
                {
                    // Remove all temporary clips from the Animation component
                    if (tempClips != null)
                    {
                        foreach (var tempClip in tempClips)
                        {
                            if (tempClip != null)
                            {
                                // Use try-catch for each clip removal in case one fails
                                try
                                {
                                    animation.RemoveClip(tempClip);
                                }
                                catch (Exception)
                                {
                                    // Clip may already be removed or invalid, continue
                                }
                            }
                        }
                    }

                    if (animationWasAdded)
                    {
                        // Remove the Animation component entirely since we added it
                        UnityEngine.Object.DestroyImmediate(animation);
                        RetargetApplianceUtil.LogInfo("[RetargetAppliance] Removed temporary Animation component after FBX export.");
                    }
                    else
                    {
                        // Restore original default clip if Animation existed before
                        animation.clip = originalDefaultClip;
                    }
                }

                // Destroy temp clip objects
                DestroyTempClips(tempClips);
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.LogWarning($"[RetargetAppliance] Cleanup after FBX export encountered an issue: {ex.Message}");

                // Still try to clean up temp clips even if something else failed
                DestroyTempClips(tempClips);
            }
        }

        /// <summary>
        /// Destroys temporary animation clip copies.
        /// </summary>
        private static void DestroyTempClips(List<AnimationClip> tempClips)
        {
            if (tempClips == null)
                return;

            foreach (var clip in tempClips)
            {
                if (clip != null)
                {
                    try
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                    }
                    catch (Exception)
                    {
                        // Clip may already be destroyed, continue
                    }
                }
            }
        }

        /// <summary>
        /// Exports a target in both GLB and FBX formats based on the selected format.
        /// </summary>
        /// <param name="targetInstance">The GameObject to export.</param>
        /// <param name="targetName">The target name (used for result tracking).</param>
        /// <param name="bakedClips">Animation clips to include.</param>
        /// <param name="settings">Bake settings.</param>
        /// <param name="format">Export format (GLB, FBX, or Both).</param>
        /// <param name="outputFileName">Optional custom output filename (without extension). If null, uses targetName.</param>
        public static CombinedExportResult ExportTarget(
            GameObject targetInstance,
            string targetName,
            List<AnimationClip> bakedClips,
            RetargetApplianceBaker.BakeSettings settings,
            ExportFormat format,
            string outputFileName = null)
        {
            var result = new CombinedExportResult
            {
                TargetName = targetName
            };

            // Export GLB if requested
            if (format == ExportFormat.GLB || format == ExportFormat.Both)
            {
                result.GLBResult = ExportAsGLB(targetInstance, targetName, bakedClips, settings, outputFileName);
            }

            // Export FBX if requested
            if (format == ExportFormat.FBX || format == ExportFormat.Both)
            {
                result.FBXResult = ExportAsFBX(targetInstance, targetName, bakedClips, settings, outputFileName);
            }

            return result;
        }

        /// <summary>
        /// Prepares a target GameObject for GLB export by adding necessary components.
        /// Adds Animation component with legacy clips so UnityGLTF will export them.
        /// CRITICAL: Clears ALL existing clips to prevent contamination from previous exports.
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

            // CRITICAL: Clear ALL existing clips from the Animation component
            // Simply setting animation.clip = null is NOT enough - we must remove all clips
            ClearAllAnimationClips(animation);

            int addedCount = 0;
            AnimationClip firstClipCopy = null;

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

                    // Track first clip
                    if (firstClipCopy == null)
                    {
                        firstClipCopy = clipCopy;
                    }

                    addedCount++;
                }
            }

            // Set the first clip as the default
            animation.clip = firstClipCopy;

            // CRITICAL: Force Animation component to use this clip by playing and sampling
            // This ensures the exporter sees the correct animation state
            if (firstClipCopy != null)
            {
                animation.playAutomatically = false;
                animation.Play(firstClipCopy.name);
                animation.Sample();
            }

            // Log verification of Animation component state
            int totalClips = CountAnimationClips(animation);
            string defaultClipName = animation.clip != null ? animation.clip.name : "(null)";
            RetargetApplianceUtil.LogInfo($"[RetargetAppliance] PrepareTargetForExport: default clip = '{defaultClipName}' clipsCount={totalClips}");
        }

        /// <summary>
        /// Clears all clips from an Animation component.
        /// </summary>
        private static void ClearAllAnimationClips(Animation animation)
        {
            if (animation == null)
                return;

            // Collect all clip names first (can't modify during enumeration)
            var clipNames = new List<string>();
            foreach (AnimationState state in animation)
            {
                if (state != null && state.clip != null)
                {
                    clipNames.Add(state.clip.name);
                }
            }

            // Remove all clips
            foreach (string clipName in clipNames)
            {
                animation.RemoveClip(clipName);
            }

            // Clear default clip
            animation.clip = null;
        }

        /// <summary>
        /// Counts the number of clips in an Animation component.
        /// </summary>
        private static int CountAnimationClips(Animation animation)
        {
            if (animation == null)
                return 0;

            int count = 0;
            foreach (AnimationState state in animation)
            {
                if (state != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Performs the actual GLB export using UnityGLTF's GLTFSceneExporter directly.
        /// </summary>
        private static bool ExportWithUnityGLTF(Transform rootTransform, string exportFolder, string fileName, List<AnimationClip> clips, AnimatorExportOverride animatorOverride = null)
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
                    // Log the Legacy Animation state before export
                    var animation = rootTransform.GetComponent<Animation>();
                    string defaultClipName = (animation != null && animation.clip != null) ? animation.clip.name : "(null)";
                    bool animatorEnabled = animatorOverride != null ? animatorOverride.AnimatorWasEnabled : false;
                    bool controllerNull = animatorOverride != null ? animatorOverride.ControllerWasNull : true;

                    RetargetApplianceUtil.LogInfo($"[RetargetAppliance] GLB exporting using Legacy Animation default clip: {defaultClipName}, animatorEnabled={animatorEnabled}, controllerNull={controllerNull}");

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
