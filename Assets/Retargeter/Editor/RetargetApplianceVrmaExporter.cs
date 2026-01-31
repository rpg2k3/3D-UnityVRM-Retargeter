using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RetargetAppliance
{
    /// <summary>
    /// Handles VRMA (VRM Animation) export for baked animation clips.
    /// Uses the AnimationClipToVrma package or UniVRM's VrmAnimationExporter via reflection.
    /// </summary>
    public static class RetargetApplianceVrmaExporter
    {
        /// <summary>
        /// Output folder for VRMA files.
        /// </summary>
        public const string OutputVrmaPath = "Assets/Output/VRMA";

        // Cached reflection data
        private static bool _apiChecked = false;
        private static bool _apiAvailable = false;
        private static Type _coreType = null;
        private static MethodInfo _createMethod = null;

        /// <summary>
        /// Checks if the VRMA export API is available.
        /// </summary>
        public static bool IsVrmaExportAvailable()
        {
            EnsureApiChecked();
            return _apiAvailable;
        }

        /// <summary>
        /// Gets a description of the VRMA export API status.
        /// </summary>
        public static string GetVrmaApiStatus()
        {
            EnsureApiChecked();
            if (_apiAvailable)
            {
                return $"VRMA Export: Available ({_coreType.FullName})";
            }
            return "VRMA Export: Not available (AnimationClipToVrma or UniVRM10 not found)";
        }

        /// <summary>
        /// Attempts to export a baked AnimationClip as a VRMA file.
        /// </summary>
        /// <param name="vrmTargetPrefabOrInstance">The VRM target (prefab or scene instance) with Animator and Avatar.</param>
        /// <param name="bakedClip">The baked AnimationClip to export.</param>
        /// <param name="outPath">The full output path for the .vrma file.</param>
        /// <param name="error">Error message if export fails.</param>
        /// <returns>True if export succeeded, false otherwise.</returns>
        public static bool TryExportVrma(GameObject vrmTargetPrefabOrInstance, AnimationClip bakedClip, string outPath, out string error)
        {
            error = null;

            // Validate inputs
            if (vrmTargetPrefabOrInstance == null)
            {
                error = "VRM target is null";
                return false;
            }

            if (bakedClip == null)
            {
                error = "Baked clip is null";
                return false;
            }

            if (string.IsNullOrEmpty(outPath))
            {
                error = "Output path is empty";
                return false;
            }

            // Check API availability
            EnsureApiChecked();
            if (!_apiAvailable)
            {
                error = "VRMA export API not found. Ensure AnimationClipToVrma or UniVRM10 is installed.";
                return false;
            }

            GameObject exportClone = null;

            try
            {
                // Create a temporary clone for export (HideAndDontSave to avoid scene pollution)
                exportClone = UnityEngine.Object.Instantiate(vrmTargetPrefabOrInstance);
                exportClone.name = $"{vrmTargetPrefabOrInstance.name}_VrmaExport";
                exportClone.hideFlags = HideFlags.HideAndDontSave;

                // Get the Animator component
                var animator = exportClone.GetComponent<Animator>();
                if (animator == null)
                {
                    error = $"No Animator component found on '{vrmTargetPrefabOrInstance.name}'";
                    return false;
                }

                // Validate humanoid Avatar
                if (animator.avatar == null)
                {
                    error = $"Animator on '{vrmTargetPrefabOrInstance.name}' has no Avatar";
                    return false;
                }

                if (!animator.avatar.isHuman)
                {
                    error = $"Avatar on '{vrmTargetPrefabOrInstance.name}' is not Humanoid";
                    return false;
                }

                // CRITICAL: Clear the runtime animator controller to prevent wrong clip selection
                // The VRMA exporter samples the clip directly, not through the controller
                animator.runtimeAnimatorController = null;

                // Ensure the animator is in a valid state
                animator.enabled = true;

                // Convert Unity asset path to absolute path for file writing
                string absolutePath = outPath;
                if (outPath.StartsWith("Assets/"))
                {
                    // Application.dataPath ends with "/Assets", so we need to get the parent
                    string projectRoot = Path.GetDirectoryName(Application.dataPath);
                    absolutePath = Path.Combine(projectRoot, outPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                }

                // Ensure output directory exists
                string directory = Path.GetDirectoryName(absolutePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Call the VRMA export API via reflection
                // AnimationClipToVrmaCore.Create(Animator humanoid, AnimationClip clip) -> byte[]
                object[] parameters = new object[] { animator, bakedClip };
                byte[] vrmaBytes = _createMethod.Invoke(null, parameters) as byte[];

                if (vrmaBytes == null || vrmaBytes.Length == 0)
                {
                    error = "VRMA export returned empty data";
                    return false;
                }

                // Write the VRMA file
                File.WriteAllBytes(absolutePath, vrmaBytes);

                // Import the new asset (caller will do full refresh later)
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);

                return true;
            }
            catch (TargetInvocationException ex)
            {
                // Unwrap reflection exceptions to get the actual error
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                // Always clean up the export clone
                if (exportClone != null)
                {
                    UnityEngine.Object.DestroyImmediate(exportClone);
                }
            }
        }

        /// <summary>
        /// Gets the full output path for a VRMA file.
        /// Format: Assets/Output/VRMA/{TargetName}/{ClipName}.vrma
        /// </summary>
        public static string GetVrmaOutputPath(string targetName, string clipName)
        {
            string sanitizedTarget = RetargetApplianceUtil.SanitizeName(targetName);
            string sanitizedClip = RetargetApplianceUtil.SanitizeName(clipName);
            return $"{OutputVrmaPath}/{sanitizedTarget}/{sanitizedClip}.vrma";
        }

        /// <summary>
        /// Ensures the VRMA output folder exists for a target.
        /// </summary>
        public static void EnsureVrmaOutputFolder(string targetName)
        {
            string sanitizedTarget = RetargetApplianceUtil.SanitizeName(targetName);
            string folderPath = $"{OutputVrmaPath}/{sanitizedTarget}";
            RetargetApplianceUtil.EnsureFolderExists(folderPath);
        }

        /// <summary>
        /// Checks for VRMA export API availability via reflection.
        /// Searches for AnimationClipToVrmaCore or similar classes.
        /// </summary>
        private static void EnsureApiChecked()
        {
            if (_apiChecked)
                return;

            _apiChecked = true;
            _apiAvailable = false;

            try
            {
                // First, try to find AnimationClipToVrmaCore from the Baxter namespace
                // This is the AnimationClipToVrma package
                _coreType = FindTypeByName("Baxter.AnimationClipToVrmaCore");

                if (_coreType != null)
                {
                    // Look for: public static byte[] Create(Animator humanoid, AnimationClip clip)
                    _createMethod = _coreType.GetMethod("Create",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(Animator), typeof(AnimationClip) },
                        null);

                    if (_createMethod != null && _createMethod.ReturnType == typeof(byte[]))
                    {
                        _apiAvailable = true;
                        RetargetApplianceUtil.LogInfo($"VRMA export API found: {_coreType.FullName}.{_createMethod.Name}");
                        return;
                    }
                }

                // Fallback: Search for any type with "VrmAnimation" and a suitable Create/Export method
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.Name.Contains("VrmAnimation") || type.Name.Contains("VrmaCore"))
                            {
                                // Look for a static method that takes (Animator, AnimationClip) and returns byte[]
                                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                                {
                                    if (method.ReturnType != typeof(byte[]))
                                        continue;

                                    var parameters = method.GetParameters();
                                    if (parameters.Length == 2 &&
                                        parameters[0].ParameterType == typeof(Animator) &&
                                        parameters[1].ParameterType == typeof(AnimationClip))
                                    {
                                        _coreType = type;
                                        _createMethod = method;
                                        _apiAvailable = true;
                                        RetargetApplianceUtil.LogInfo($"VRMA export API found (fallback): {type.FullName}.{method.Name}");
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip assemblies that can't be scanned
                    }
                }

                if (!_apiAvailable)
                {
                    RetargetApplianceUtil.LogWarning("VRMA export API not found. AnimationClipToVrma or compatible package required.");
                }
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.LogError($"Error checking for VRMA export API: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a type by its full name across all loaded assemblies.
        /// </summary>
        private static Type FindTypeByName(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Skip assemblies that can't be queried
                }
            }
            return null;
        }
    }
}
