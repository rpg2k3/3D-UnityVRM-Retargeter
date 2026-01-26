using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RetargetAppliance
{
    /// <summary>
    /// Main Editor Window for the Retarget Appliance tool.
    /// Menu: Tools/Retarget Appliance
    /// </summary>
    public class RetargetApplianceWindow : EditorWindow
    {
        // Cached data
        private List<string> _vrmTargets = new List<string>();
        private List<string> _fbxAnimations = new List<string>();
        private List<RetargetApplianceImporter.AnimationScanResult> _animationScanResults;

        // Settings
        private int _bakeFPS = 30;
        private bool _includeRootMotion = false;
        private float _exportScale = 1f;
        private RetargetApplianceExporter.ExportFormat _exportFormat = RetargetApplianceExporter.ExportFormat.Both;

        // UI State
        private Vector2 _targetsScrollPos;
        private Vector2 _animationsScrollPos;
        private bool _showTargetsFoldout = true;
        private bool _showAnimationsFoldout = true;
        private bool _showSettingsFoldout = true;

        // Validation state
        private bool _isValidated = false;
        private string _validationMessage = "";
        private MessageType _validationMessageType = MessageType.None;

        [MenuItem("Tools/Retarget Appliance")]
        public static void ShowWindow()
        {
            var window = GetWindow<RetargetApplianceWindow>();
            window.titleContent = new GUIContent("Retarget Appliance");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnEnable()
        {
            // Ensure folders exist on window open
            RetargetApplianceUtil.EnsureFoldersExist();
            RefreshData();
        }

        private void OnFocus()
        {
            RefreshData();
        }

        private void RefreshData()
        {
            _vrmTargets = RetargetApplianceUtil.FindVRMTargets();
            _fbxAnimations = RetargetApplianceUtil.FindFBXAnimations();
            _isValidated = false;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // Header
            EditorGUILayout.LabelField("Retarget Appliance", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Bake Mixamo animations onto VRM models and export as GLB/FBX", EditorStyles.miniLabel);

            EditorGUILayout.Space(10);

            // Refresh button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshData();
            }
            GUILayout.FlexibleSpace();

            // UnityGLTF status
            bool hasUnityGLTF = RetargetApplianceExporter.IsUnityGLTFAvailable();
            var gltfStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            gltfStatusStyle.normal.textColor = hasUnityGLTF ? Color.green : Color.red;
            EditorGUILayout.LabelField(hasUnityGLTF ? "GLB: OK" : "GLB: Missing", gltfStatusStyle, GUILayout.Width(70));

            // FBX Exporter status
            bool hasFBXExporter = RetargetApplianceExporter.IsFBXExporterAvailable();
            var fbxStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            fbxStatusStyle.normal.textColor = hasFBXExporter ? Color.green : Color.red;
            EditorGUILayout.LabelField(hasFBXExporter ? "FBX: OK" : "FBX: Missing", fbxStatusStyle, GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Targets section
            DrawTargetsSection();

            EditorGUILayout.Space(5);

            // Animations section
            DrawAnimationsSection();

            EditorGUILayout.Space(5);

            // Settings section
            DrawSettingsSection();

            EditorGUILayout.Space(10);

            // Validation message
            if (!string.IsNullOrEmpty(_validationMessage))
            {
                EditorGUILayout.HelpBox(_validationMessage, _validationMessageType);
                EditorGUILayout.Space(5);
            }

            // Action buttons
            DrawActionButtons();
        }

        private void DrawTargetsSection()
        {
            _showTargetsFoldout = EditorGUILayout.Foldout(_showTargetsFoldout, $"VRM Targets ({_vrmTargets.Count})", true);

            if (_showTargetsFoldout)
            {
                EditorGUI.indentLevel++;

                if (_vrmTargets.Count == 0)
                {
                    EditorGUILayout.HelpBox($"No VRM files found in:\n{RetargetApplianceUtil.InputTargetsPath}", MessageType.Info);
                }
                else
                {
                    _targetsScrollPos = EditorGUILayout.BeginScrollView(_targetsScrollPos, GUILayout.MaxHeight(120));

                    foreach (var vrmPath in _vrmTargets)
                    {
                        EditorGUILayout.BeginHorizontal();

                        string name = RetargetApplianceUtil.GetTargetName(vrmPath);
                        EditorGUILayout.LabelField(name, EditorStyles.miniLabel);

                        if (GUILayout.Button("Select", GUILayout.Width(50)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(vrmPath);
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                // Open folder button
                if (GUILayout.Button("Open Targets Folder", GUILayout.Width(150)))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceUtil.InputTargetsPath);
                    EditorUtility.RevealInFinder(RetargetApplianceUtil.InputTargetsPath);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawAnimationsSection()
        {
            _showAnimationsFoldout = EditorGUILayout.Foldout(_showAnimationsFoldout, $"FBX Animations ({_fbxAnimations.Count})", true);

            if (_showAnimationsFoldout)
            {
                EditorGUI.indentLevel++;

                if (_fbxAnimations.Count == 0)
                {
                    EditorGUILayout.HelpBox($"No FBX files found in:\n{RetargetApplianceUtil.InputAnimationsPath}", MessageType.Info);
                }
                else
                {
                    _animationsScrollPos = EditorGUILayout.BeginScrollView(_animationsScrollPos, GUILayout.MaxHeight(120));

                    foreach (var fbxPath in _fbxAnimations)
                    {
                        EditorGUILayout.BeginHorizontal();

                        string name = System.IO.Path.GetFileNameWithoutExtension(fbxPath);
                        EditorGUILayout.LabelField(name, EditorStyles.miniLabel);

                        // Show humanoid status
                        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                        if (importer != null)
                        {
                            bool isHumanoid = importer.animationType == ModelImporterAnimationType.Human;
                            var statusLabel = isHumanoid ? "(Humanoid)" : "(Not Humanoid)";
                            var style = new GUIStyle(EditorStyles.miniLabel);
                            style.normal.textColor = isHumanoid ? Color.green : Color.yellow;
                            EditorGUILayout.LabelField(statusLabel, style, GUILayout.Width(80));
                        }

                        if (GUILayout.Button("Select", GUILayout.Width(50)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fbxPath);
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }

                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                // Open folder button
                if (GUILayout.Button("Open Animations Folder", GUILayout.Width(150)))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceUtil.InputAnimationsPath);
                    EditorUtility.RevealInFinder(RetargetApplianceUtil.InputAnimationsPath);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawSettingsSection()
        {
            _showSettingsFoldout = EditorGUILayout.Foldout(_showSettingsFoldout, "Bake & Export Settings", true);

            if (_showSettingsFoldout)
            {
                EditorGUI.indentLevel++;

                _bakeFPS = EditorGUILayout.IntSlider("Bake FPS", _bakeFPS, 12, 60);

                EditorGUILayout.LabelField("Clip Length Mode", "Use source clip length", EditorStyles.miniLabel);

                _includeRootMotion = EditorGUILayout.Toggle("Include Root Motion", _includeRootMotion);

                _exportScale = EditorGUILayout.FloatField("Export Scale", _exportScale);
                if (_exportScale <= 0)
                    _exportScale = 1f;

                EditorGUILayout.Space(5);

                // Export format selection
                _exportFormat = (RetargetApplianceExporter.ExportFormat)EditorGUILayout.EnumPopup("Export Format", _exportFormat);

                // Show warnings for missing exporters based on selection
                if (_exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
                {
                    if (!RetargetApplianceExporter.IsUnityGLTFAvailable())
                    {
                        EditorGUILayout.HelpBox("UnityGLTF not installed. GLB export will fail.", MessageType.Warning);
                    }
                }
                if (_exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
                {
                    if (!RetargetApplianceExporter.IsFBXExporterAvailable())
                    {
                        EditorGUILayout.HelpBox("FBX Exporter not installed. FBX export will fail.", MessageType.Warning);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.Space(5);

            // Validate button
            if (GUILayout.Button("Validate Inputs", GUILayout.Height(30)))
            {
                PerformValidation();
            }

            EditorGUILayout.Space(5);

            // Force reimport button
            EditorGUI.BeginDisabledGroup(_fbxAnimations.Count == 0);
            if (GUILayout.Button("Force Reimport Animations as Humanoid", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(
                    "Reimport Animations",
                    $"This will force reimport {_fbxAnimations.Count} FBX file(s) as Humanoid.\n\nContinue?",
                    "Yes", "Cancel"))
                {
                    int count = RetargetApplianceImporter.ForceReimportAsHumanoid();
                    EditorUtility.DisplayDialog("Reimport Complete", $"Reimported {count} FBX file(s) as Humanoid.", "OK");
                    RefreshData();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // Main action button
            bool canProcess = _vrmTargets.Count > 0 && _fbxAnimations.Count > 0;
            EditorGUI.BeginDisabledGroup(!canProcess);

            var mainButtonStyle = new GUIStyle(GUI.skin.button);
            mainButtonStyle.fontStyle = FontStyle.Bold;

            string buttonLabel = GetExportButtonLabel();
            if (GUILayout.Button(buttonLabel, mainButtonStyle, GUILayout.Height(40)))
            {
                // Check exporter availability based on selected format
                bool canProceed = true;
                string missingMessage = "";

                if (_exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
                {
                    if (!RetargetApplianceExporter.IsUnityGLTFAvailable())
                    {
                        missingMessage += "UnityGLTF is required for GLB export but is not installed.\n\n";
                        canProceed = false;
                    }
                }

                if (_exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
                {
                    if (!RetargetApplianceExporter.IsFBXExporterAvailable())
                    {
                        missingMessage += "FBX Exporter package is required for FBX export but is not installed.\n\n";
                        canProceed = false;
                    }
                }

                if (!canProceed)
                {
                    bool showInstructions = EditorUtility.DisplayDialog(
                        "Missing Dependencies",
                        missingMessage + "Would you like to see installation instructions?",
                        "Show Instructions", "Cancel");

                    if (showInstructions)
                    {
                        string instructions = "";
                        if (!RetargetApplianceExporter.IsUnityGLTFAvailable() &&
                            (_exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both))
                        {
                            instructions += RetargetApplianceExporter.GetUnityGLTFInstallInstructions() + "\n\n---\n\n";
                        }
                        if (!RetargetApplianceExporter.IsFBXExporterAvailable() &&
                            (_exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both))
                        {
                            instructions += RetargetApplianceExporter.GetFBXExporterInstallInstructions();
                        }
                        EditorUtility.DisplayDialog("Installation Instructions", instructions, "OK");
                    }
                    return;
                }

                string exportFormatText = _exportFormat == RetargetApplianceExporter.ExportFormat.Both ? "GLB and FBX" : _exportFormat.ToString();
                if (EditorUtility.DisplayDialog(
                    "Bake and Export",
                    $"This will:\n" +
                    $"- Bake {_fbxAnimations.Count} animation(s) onto {_vrmTargets.Count} target(s)\n" +
                    $"- Export each target as {exportFormatText}\n\n" +
                    $"Continue?",
                    "Yes", "Cancel"))
                {
                    PerformBakeAndExport();
                }
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(10);

            // Output folder buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Prefabs Folder"))
            {
                RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceUtil.OutputPrefabsPath);
                EditorUtility.RevealInFinder(RetargetApplianceUtil.OutputPrefabsPath);
            }
            if (GUILayout.Button("Open Export Folder"))
            {
                RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceUtil.OutputExportPath);
                EditorUtility.RevealInFinder(RetargetApplianceUtil.OutputExportPath);
            }
            EditorGUILayout.EndHorizontal();
        }

        private string GetExportButtonLabel()
        {
            switch (_exportFormat)
            {
                case RetargetApplianceExporter.ExportFormat.GLB:
                    return "Bake + Export GLB for ALL Targets";
                case RetargetApplianceExporter.ExportFormat.FBX:
                    return "Bake + Export FBX for ALL Targets";
                case RetargetApplianceExporter.ExportFormat.Both:
                default:
                    return "Bake + Export GLB/FBX for ALL Targets";
            }
        }

        private void PerformValidation()
        {
            RefreshData();

            var messages = new List<string>();
            bool hasErrors = false;
            bool hasWarnings = false;

            // Check targets
            if (_vrmTargets.Count == 0)
            {
                messages.Add("ERROR: No VRM targets found. Add .vrm files to " + RetargetApplianceUtil.InputTargetsPath);
                hasErrors = true;
            }
            else
            {
                messages.Add($"Found {_vrmTargets.Count} VRM target(s)");

                // Validate each target
                foreach (var vrmPath in _vrmTargets)
                {
                    var prefab = RetargetApplianceUtil.GetVRMPrefab(vrmPath);
                    if (prefab == null)
                    {
                        messages.Add($"WARNING: Could not find prefab for '{System.IO.Path.GetFileName(vrmPath)}'");
                        hasWarnings = true;
                    }
                    else
                    {
                        // Check for humanoid setup
                        var tempInstance = Instantiate(prefab);
                        if (!RetargetApplianceUtil.ValidateHumanoidSetup(tempInstance, out string error))
                        {
                            messages.Add($"WARNING: {error}");
                            hasWarnings = true;
                        }
                        DestroyImmediate(tempInstance);
                    }
                }
            }

            // Check animations
            var animValidation = RetargetApplianceImporter.ValidateAnimations();

            if (animValidation.TotalFBXCount == 0)
            {
                messages.Add("ERROR: No FBX animations found. Add Mixamo FBX files to " + RetargetApplianceUtil.InputAnimationsPath);
                hasErrors = true;
            }
            else
            {
                messages.Add($"Found {animValidation.TotalFBXCount} FBX file(s) with {animValidation.ValidClipCount} clip(s)");

                foreach (var error in animValidation.Errors)
                {
                    messages.Add($"ERROR: {error}");
                    hasErrors = true;
                }

                foreach (var warning in animValidation.Warnings)
                {
                    messages.Add($"WARNING: {warning}");
                    hasWarnings = true;
                }
            }

            // Check UnityGLTF (for GLB export)
            if (_exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
            {
                if (!RetargetApplianceExporter.IsUnityGLTFAvailable())
                {
                    messages.Add("WARNING: UnityGLTF not installed. GLB export will fail.");
                    hasWarnings = true;
                }
                else
                {
                    messages.Add("UnityGLTF: Installed and ready for GLB export");
                }
            }

            // Check FBX Exporter (for FBX export)
            if (_exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
            {
                if (!RetargetApplianceExporter.IsFBXExporterAvailable())
                {
                    messages.Add("WARNING: FBX Exporter not installed. FBX export will fail.");
                    hasWarnings = true;
                }
                else
                {
                    messages.Add("FBX Exporter: Installed and ready for FBX export");
                }
            }

            // Set validation state
            _validationMessage = string.Join("\n", messages);
            _isValidated = true;

            if (hasErrors)
            {
                _validationMessageType = MessageType.Error;
            }
            else if (hasWarnings)
            {
                _validationMessageType = MessageType.Warning;
            }
            else
            {
                _validationMessageType = MessageType.Info;
                _validationMessage = "Validation passed! Ready to bake and export.\n" + _validationMessage;
            }
        }

        private void PerformBakeAndExport()
        {
            try
            {
                // Get all humanoid clips
                var sourceClips = RetargetApplianceImporter.GetAllHumanoidClips();

                if (sourceClips.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "No Clips",
                        "No valid humanoid animation clips found.\n\nMake sure to run 'Force Reimport Animations as Humanoid' first.",
                        "OK");
                    return;
                }

                RetargetApplianceUtil.LogInfo($"Starting bake process with {sourceClips.Count} clips for {_vrmTargets.Count} targets...");

                // Create or open workspace scene
                Scene workspaceScene = EnsureWorkspaceScene();

                // Settings
                var settings = new RetargetApplianceBaker.BakeSettings
                {
                    FPS = _bakeFPS,
                    IncludeRootMotion = _includeRootMotion,
                    ExportScale = _exportScale
                };

                int totalTargets = _vrmTargets.Count;
                int glbSuccessCount = 0;
                int glbFailCount = 0;
                int fbxSuccessCount = 0;
                int fbxFailCount = 0;
                int bakeFailCount = 0;

                bool exportGLB = _exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both;
                bool exportFBX = _exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both;

                // Process each target
                for (int i = 0; i < _vrmTargets.Count; i++)
                {
                    string vrmPath = _vrmTargets[i];
                    string targetName = RetargetApplianceUtil.GetTargetName(vrmPath);

                    if (!RetargetApplianceUtil.ShowProgress(
                        "Baking and Exporting",
                        $"Processing: {targetName} ({i + 1}/{totalTargets})",
                        (float)i / totalTargets))
                    {
                        RetargetApplianceUtil.LogWarning("Process cancelled by user.");
                        break;
                    }

                    // Bake
                    var bakeResult = RetargetApplianceBaker.BakeAnimationsForTarget(vrmPath, sourceClips, settings);

                    if (!string.IsNullOrEmpty(bakeResult.Error))
                    {
                        bakeFailCount++;
                        continue;
                    }

                    if (bakeResult.SuccessCount == 0)
                    {
                        RetargetApplianceUtil.LogWarning($"No clips were baked for '{targetName}'");
                        CleanupTarget(bakeResult);
                        bakeFailCount++;
                        continue;
                    }

                    // Export
                    var bakedClips = RetargetApplianceBaker.GetBakedClips(bakeResult);

                    // Export GLB if enabled
                    if (exportGLB)
                    {
                        var glbResult = RetargetApplianceExporter.ExportAsGLB(
                            bakeResult.TargetInstance,
                            targetName,
                            bakedClips,
                            settings);

                        if (glbResult.Success)
                        {
                            glbSuccessCount++;
                            RetargetApplianceUtil.LogInfo($"GLB exported: {glbResult.ExportPath}");
                        }
                        else
                        {
                            glbFailCount++;
                            RetargetApplianceUtil.LogError($"GLB export failed for '{targetName}': {glbResult.Error}");
                        }
                    }

                    // Export FBX if enabled
                    if (exportFBX)
                    {
                        var fbxResult = RetargetApplianceExporter.ExportAsFBX(
                            bakeResult.TargetInstance,
                            targetName,
                            bakedClips,
                            settings);

                        if (fbxResult.Success)
                        {
                            fbxSuccessCount++;
                            RetargetApplianceUtil.LogInfo($"FBX exported: {fbxResult.ExportPath}");
                        }
                        else
                        {
                            fbxFailCount++;
                            RetargetApplianceUtil.LogError($"FBX export failed for '{targetName}': {fbxResult.Error}");
                        }
                    }

                    // Cleanup instantiated target
                    CleanupTarget(bakeResult);
                }

                RetargetApplianceUtil.ClearProgress();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Build summary
                var summaryLines = new List<string>();
                summaryLines.Add("Bake and Export Complete!\n");

                if (bakeFailCount > 0)
                {
                    summaryLines.Add($"Bake failures: {bakeFailCount}");
                }

                if (exportGLB)
                {
                    summaryLines.Add($"GLB: {glbSuccessCount} successful, {glbFailCount} failed");
                    summaryLines.Add($"  -> {RetargetApplianceUtil.OutputExportPath}/<Target>.glb");
                }

                if (exportFBX)
                {
                    summaryLines.Add($"FBX: {fbxSuccessCount} successful, {fbxFailCount} failed");
                    summaryLines.Add($"  -> {RetargetApplianceUtil.OutputExportPath}/<Target>.fbx");
                }

                summaryLines.Add($"\nBaked animations: {RetargetApplianceUtil.OutputPrefabsPath}");

                string summary = string.Join("\n", summaryLines);
                EditorUtility.DisplayDialog("Complete", summary, "OK");

                RetargetApplianceUtil.LogInfo(summary.Replace("\n\n", " | ").Replace("\n", " "));
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.ClearProgress();
                RetargetApplianceUtil.LogError($"Bake and export failed: {ex.Message}");
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Error", $"Process failed: {ex.Message}\n\nCheck console for details.", "OK");
            }
        }

        private Scene EnsureWorkspaceScene()
        {
            // Check if we need to save the current scene
            Scene currentScene = SceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                if (EditorUtility.DisplayDialog(
                    "Save Scene",
                    "The current scene has unsaved changes. Save before continuing?",
                    "Save", "Don't Save"))
                {
                    EditorSceneManager.SaveScene(currentScene);
                }
            }

            // Check if workspace scene exists
            string scenePath = RetargetApplianceUtil.WorkspaceScenePath;

            if (!System.IO.File.Exists(scenePath))
            {
                // Create new scene
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // Add basic lighting
                var lightGO = new GameObject("Directional Light");
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

                // Save the scene
                RetargetApplianceUtil.EnsureFolderExists("Assets/Scenes");
                EditorSceneManager.SaveScene(newScene, scenePath);

                RetargetApplianceUtil.LogInfo($"Created workspace scene: {scenePath}");
                return newScene;
            }
            else
            {
                // Open existing workspace scene
                return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
        }

        private void CleanupTarget(RetargetApplianceBaker.TargetBakeResult result)
        {
            if (result.TargetInstance != null)
            {
                DestroyImmediate(result.TargetInstance);
            }
        }
    }
}
