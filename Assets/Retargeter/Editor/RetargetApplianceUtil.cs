using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RetargetAppliance
{
    /// <summary>
    /// Utility functions for the Retarget Appliance tool.
    /// </summary>
    public static class RetargetApplianceUtil
    {
        // Folder paths
        public const string InputTargetsPath = "Assets/Input/Targets";
        public const string InputAnimationsPath = "Assets/Input/Animations";
        public const string OutputPrefabsPath = "Assets/Output/RetargetedPrefabs";
        public const string OutputExportPath = "Assets/Output/Export";
        public const string WorkspaceScenePath = "Assets/Scenes/RetargetWorkspace.unity";
        public const string RetargeterEditorPath = "Assets/Retargeter/Editor";

        /// <summary>
        /// Ensures all required folders exist, creating them if necessary.
        /// </summary>
        public static void EnsureFoldersExist()
        {
            EnsureFolderExists(InputTargetsPath);
            EnsureFolderExists(InputAnimationsPath);
            EnsureFolderExists(OutputPrefabsPath);
            EnsureFolderExists(OutputExportPath);
            EnsureFolderExists("Assets/Scenes");
        }

        /// <summary>
        /// Ensures a single folder exists.
        /// </summary>
        public static void EnsureFolderExists(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] parts = folderPath.Split('/');
                string currentPath = parts[0]; // "Assets"

                for (int i = 1; i < parts.Length; i++)
                {
                    string parentPath = currentPath;
                    currentPath = $"{currentPath}/{parts[i]}";

                    if (!AssetDatabase.IsValidFolder(currentPath))
                    {
                        AssetDatabase.CreateFolder(parentPath, parts[i]);
                        Debug.Log($"[RetargetAppliance] Created folder: {currentPath}");
                    }
                }
            }
        }

        /// <summary>
        /// Finds all VRM files in the targets folder.
        /// </summary>
        public static List<string> FindVRMTargets()
        {
            var results = new List<string>();

            if (!AssetDatabase.IsValidFolder(InputTargetsPath))
            {
                return results;
            }

            string[] guids = AssetDatabase.FindAssets("", new[] { InputTargetsPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(path);
                }
            }

            return results;
        }

        /// <summary>
        /// Finds all FBX files in the animations folder.
        /// </summary>
        public static List<string> FindFBXAnimations()
        {
            var results = new List<string>();

            if (!AssetDatabase.IsValidFolder(InputAnimationsPath))
            {
                return results;
            }

            string[] guids = AssetDatabase.FindAssets("", new[] { InputAnimationsPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(path);
                }
            }

            return results;
        }

        /// <summary>
        /// Gets the prefab associated with a VRM file.
        /// UniVRM typically creates a prefab at the same path or in a subfolder.
        /// </summary>
        public static GameObject GetVRMPrefab(string vrmPath)
        {
            // First, try loading the VRM directly as it might be imported as a prefab
            var directAsset = AssetDatabase.LoadAssetAtPath<GameObject>(vrmPath);
            if (directAsset != null)
            {
                return directAsset;
            }

            // Try finding a prefab in the same directory
            string directory = Path.GetDirectoryName(vrmPath).Replace("\\", "/");
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(vrmPath);

            // Common patterns for VRM prefab locations
            string[] possiblePaths = new[]
            {
                $"{directory}/{fileNameWithoutExt}.prefab",
                $"{directory}/{fileNameWithoutExt}/{fileNameWithoutExt}.prefab",
                $"{vrmPath.Replace(".vrm", ".prefab")}",
            };

            foreach (string possiblePath in possiblePaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(possiblePath);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            // Search for any prefab with similar name in the directory
            string[] prefabGuids = AssetDatabase.FindAssets($"t:Prefab {fileNameWithoutExt}", new[] { directory });
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a clean name for a target (file name without extension).
        /// </summary>
        public static string GetTargetName(string vrmPath)
        {
            return Path.GetFileNameWithoutExtension(vrmPath);
        }

        /// <summary>
        /// Gets a clean name for an animation clip.
        /// </summary>
        public static string GetAnimationName(string fbxPath, AnimationClip clip)
        {
            if (clip != null && !string.IsNullOrEmpty(clip.name) && !clip.name.Contains("__preview__"))
            {
                return SanitizeName(clip.name);
            }
            return SanitizeName(Path.GetFileNameWithoutExtension(fbxPath));
        }

        /// <summary>
        /// Sanitizes a name to be safe for file paths and asset names.
        /// Replaces spaces with underscores and removes Windows forbidden characters.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Unnamed";

            // Replace spaces with underscores
            name = name.Replace(' ', '_');

            // Remove Windows forbidden filename characters: \ / : * ? " < > |
            char[] windowsForbidden = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            foreach (char c in windowsForbidden)
            {
                name = name.Replace(c.ToString(), "");
            }

            // Also remove any other invalid chars from the system
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name;
        }

        /// <summary>
        /// Creates an export filename from target name and clip name.
        /// Format: TargetName__ClipName (sanitized for filenames)
        /// </summary>
        public static string GetExportFileName(string targetName, string clipName)
        {
            return $"{SanitizeName(targetName)}__{SanitizeName(clipName)}";
        }

        /// <summary>
        /// Creates a baked clip name from target and source clip names.
        /// </summary>
        public static string GetBakedClipName(string targetName, string clipName)
        {
            return $"{SanitizeName(targetName)}__{SanitizeName(clipName)}__BAKED";
        }

        /// <summary>
        /// Validates that a GameObject has a valid Humanoid Animator setup.
        /// </summary>
        public static bool ValidateHumanoidSetup(GameObject go, out string errorMessage)
        {
            errorMessage = null;

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                errorMessage = $"GameObject '{go.name}' has no Animator component.";
                return false;
            }

            if (animator.avatar == null)
            {
                errorMessage = $"GameObject '{go.name}' Animator has no Avatar assigned.";
                return false;
            }

            if (!animator.avatar.isValid)
            {
                errorMessage = $"GameObject '{go.name}' Avatar is not valid.";
                return false;
            }

            if (!animator.avatar.isHuman)
            {
                errorMessage = $"GameObject '{go.name}' Avatar is not Humanoid.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets all Transform children recursively.
        /// </summary>
        public static void GetAllChildTransforms(Transform root, List<Transform> results)
        {
            results.Add(root);
            for (int i = 0; i < root.childCount; i++)
            {
                GetAllChildTransforms(root.GetChild(i), results);
            }
        }

        /// <summary>
        /// Gets the relative path from a root transform to a child transform.
        /// </summary>
        public static string GetTransformPath(Transform root, Transform target)
        {
            if (target == root)
                return "";

            var path = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        /// <summary>
        /// Shows a progress bar and returns whether the operation should continue.
        /// </summary>
        public static bool ShowProgress(string title, string info, float progress)
        {
            return !EditorUtility.DisplayCancelableProgressBar(title, info, progress);
        }

        /// <summary>
        /// Clears the progress bar.
        /// </summary>
        public static void ClearProgress()
        {
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// Logs an info message with the tool prefix.
        /// </summary>
        public static void LogInfo(string message)
        {
            Debug.Log($"[RetargetAppliance] {message}");
        }

        /// <summary>
        /// Logs a warning message with the tool prefix.
        /// </summary>
        public static void LogWarning(string message)
        {
            Debug.LogWarning($"[RetargetAppliance] {message}");
        }

        /// <summary>
        /// Logs an error message with the tool prefix.
        /// </summary>
        public static void LogError(string message)
        {
            Debug.LogError($"[RetargetAppliance] {message}");
        }
    }
}
