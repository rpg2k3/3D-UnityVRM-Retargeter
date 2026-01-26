using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RetargetAppliance
{
    /// <summary>
    /// Handles FBX import settings and animation clip extraction.
    /// </summary>
    public static class RetargetApplianceImporter
    {
        /// <summary>
        /// Result of scanning FBX files for animation clips.
        /// </summary>
        public class AnimationScanResult
        {
            public string FBXPath;
            public List<AnimationClip> Clips = new List<AnimationClip>();
            public bool IsHumanoid;
            public string Error;
        }

        /// <summary>
        /// Forces all FBX files in the animations folder to use Humanoid import settings.
        /// </summary>
        /// <returns>Number of files reimported.</returns>
        public static int ForceReimportAsHumanoid()
        {
            var fbxPaths = RetargetApplianceUtil.FindFBXAnimations();
            int reimportCount = 0;

            for (int i = 0; i < fbxPaths.Count; i++)
            {
                string fbxPath = fbxPaths[i];

                if (!RetargetApplianceUtil.ShowProgress(
                    "Reimporting FBX as Humanoid",
                    $"Processing: {Path.GetFileName(fbxPath)}",
                    (float)i / fbxPaths.Count))
                {
                    RetargetApplianceUtil.LogWarning("Reimport cancelled by user.");
                    break;
                }

                try
                {
                    if (SetHumanoidImportSettings(fbxPath))
                    {
                        reimportCount++;
                    }
                }
                catch (Exception ex)
                {
                    RetargetApplianceUtil.LogError($"Failed to reimport '{fbxPath}': {ex.Message}");
                }
            }

            RetargetApplianceUtil.ClearProgress();
            AssetDatabase.Refresh();

            RetargetApplianceUtil.LogInfo($"Reimported {reimportCount} FBX files as Humanoid.");
            return reimportCount;
        }

        /// <summary>
        /// Sets Humanoid import settings for a single FBX file.
        /// </summary>
        /// <returns>True if the file was modified and reimported.</returns>
        public static bool SetHumanoidImportSettings(string fbxPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;

            if (importer == null)
            {
                RetargetApplianceUtil.LogError($"Could not get ModelImporter for: {fbxPath}");
                return false;
            }

            bool needsReimport = false;

            // Check and set animation type to Humanoid
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                needsReimport = true;
            }

            // Ensure avatar setup is correct
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                needsReimport = true;
            }

            // Ensure animation import is enabled
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                needsReimport = true;
            }

            if (needsReimport)
            {
                importer.SaveAndReimport();
                RetargetApplianceUtil.LogInfo($"Reimported as Humanoid: {fbxPath}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Scans all FBX files and extracts their animation clips.
        /// </summary>
        public static List<AnimationScanResult> ScanAnimations()
        {
            var results = new List<AnimationScanResult>();
            var fbxPaths = RetargetApplianceUtil.FindFBXAnimations();

            for (int i = 0; i < fbxPaths.Count; i++)
            {
                string fbxPath = fbxPaths[i];

                if (!RetargetApplianceUtil.ShowProgress(
                    "Scanning Animations",
                    $"Scanning: {Path.GetFileName(fbxPath)}",
                    (float)i / fbxPaths.Count))
                {
                    break;
                }

                var result = ScanFBXAnimations(fbxPath);
                results.Add(result);
            }

            RetargetApplianceUtil.ClearProgress();
            return results;
        }

        /// <summary>
        /// Scans a single FBX file for animation clips.
        /// </summary>
        public static AnimationScanResult ScanFBXAnimations(string fbxPath)
        {
            var result = new AnimationScanResult
            {
                FBXPath = fbxPath
            };

            try
            {
                // Check the importer settings
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null)
                {
                    result.IsHumanoid = importer.animationType == ModelImporterAnimationType.Human;
                }

                // Load all assets from the FBX
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip)
                    {
                        // Skip preview clips
                        if (clip.name.Contains("__preview__"))
                            continue;

                        // Skip empty clips
                        if (clip.empty)
                            continue;

                        result.Clips.Add(clip);
                    }
                }

                if (result.Clips.Count == 0)
                {
                    result.Error = "No animation clips found in FBX.";
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Gets a flat list of all valid humanoid animation clips from the animations folder.
        /// </summary>
        public static List<AnimationClipInfo> GetAllHumanoidClips()
        {
            var results = new List<AnimationClipInfo>();
            var scanResults = ScanAnimations();

            foreach (var scanResult in scanResults)
            {
                if (!string.IsNullOrEmpty(scanResult.Error))
                {
                    RetargetApplianceUtil.LogWarning($"Skipping '{scanResult.FBXPath}': {scanResult.Error}");
                    continue;
                }

                if (!scanResult.IsHumanoid)
                {
                    RetargetApplianceUtil.LogWarning($"Skipping '{scanResult.FBXPath}': Not set to Humanoid. Please run 'Force Reimport Animations as Humanoid' first.");
                    continue;
                }

                foreach (var clip in scanResult.Clips)
                {
                    // Double-check the clip is humanoid motion
                    if (!clip.isHumanMotion)
                    {
                        RetargetApplianceUtil.LogWarning($"Clip '{clip.name}' in '{scanResult.FBXPath}' is not humanoid motion. Skipping.");
                        continue;
                    }

                    results.Add(new AnimationClipInfo
                    {
                        Clip = clip,
                        SourcePath = scanResult.FBXPath,
                        ClipName = RetargetApplianceUtil.GetAnimationName(scanResult.FBXPath, clip)
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Validates that all FBX files are properly set up as Humanoid.
        /// </summary>
        public static ValidationResult ValidateAnimations()
        {
            var result = new ValidationResult();
            var fbxPaths = RetargetApplianceUtil.FindFBXAnimations();

            if (fbxPaths.Count == 0)
            {
                result.Errors.Add($"No FBX files found in '{RetargetApplianceUtil.InputAnimationsPath}'. Please add Mixamo FBX files.");
                return result;
            }

            foreach (string fbxPath in fbxPaths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;

                if (importer == null)
                {
                    result.Errors.Add($"Could not load importer for: {fbxPath}");
                    continue;
                }

                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    result.Warnings.Add($"'{Path.GetFileName(fbxPath)}' is not set to Humanoid. Run 'Force Reimport' to fix.");
                }

                var scanResult = ScanFBXAnimations(fbxPath);
                if (scanResult.Clips.Count == 0)
                {
                    result.Warnings.Add($"'{Path.GetFileName(fbxPath)}' contains no animation clips.");
                }
                else
                {
                    result.ValidClipCount += scanResult.Clips.Count;
                }
            }

            result.TotalFBXCount = fbxPaths.Count;
            return result;
        }

        /// <summary>
        /// Information about a single animation clip.
        /// </summary>
        public class AnimationClipInfo
        {
            public AnimationClip Clip;
            public string SourcePath;
            public string ClipName;
        }

        /// <summary>
        /// Result of validation.
        /// </summary>
        public class ValidationResult
        {
            public List<string> Errors = new List<string>();
            public List<string> Warnings = new List<string>();
            public int TotalFBXCount;
            public int ValidClipCount;

            public bool HasErrors => Errors.Count > 0;
            public bool HasWarnings => Warnings.Count > 0;
            public bool IsValid => !HasErrors;
        }
    }
}
