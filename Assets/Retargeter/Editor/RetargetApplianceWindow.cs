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

        // VRM Correction Settings (simplified)
        private bool _enableVrmCorrections = true;
        private bool _autoFixFootDirection = true;
        private bool _correctLeftFoot = true;
        private bool _correctToes = true;
        private bool _debugPrintAlignment = false;

        // Toe Stabilization Settings
        private bool _enableToeStabilization = true;
        private ToeStabilizationMode _toeStabilizationMode = ToeStabilizationMode.DampenRotation;
        private float _toeRotationStrength = 0.25f;
        private bool _stabilizeRightToe = true;
        private bool _stabilizeLeftToe = false;

        // Manual offset settings (advanced)
        private bool _showVrmAdvancedFoldout = false;
        private VrmCorrectionProfile _vrmCorrectionProfile = VrmCorrectionProfile.VRoidA_Y90;
        private Vector3 _leftFootOffset = new Vector3(0f, -90f, 0f);
        private Vector3 _rightFootOffset = new Vector3(0f, 90f, 0f);
        private Vector3 _leftToesOffset = Vector3.zero;
        private Vector3 _rightToesOffset = Vector3.zero;

        // UI State
        private Vector2 _mainScrollPos;
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
            window.minSize = new Vector2(400, 400);
            window.Show();
        }

        private void OnEnable()
        {
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
            // Main scroll view wraps everything
            _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos);

            EditorGUILayout.Space(10);

            // Header
            EditorGUILayout.LabelField("Retarget Appliance", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Bake Mixamo animations onto VRM models and export as GLB/FBX", EditorStyles.miniLabel);

            EditorGUILayout.Space(10);

            // Refresh button and status
            DrawHeaderButtons();

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

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeaderButtons()
        {
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
                    _targetsScrollPos = EditorGUILayout.BeginScrollView(_targetsScrollPos, GUILayout.MaxHeight(100));

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
                    _animationsScrollPos = EditorGUILayout.BeginScrollView(_animationsScrollPos, GUILayout.MaxHeight(100));

                    foreach (var fbxPath in _fbxAnimations)
                    {
                        EditorGUILayout.BeginHorizontal();

                        string name = System.IO.Path.GetFileNameWithoutExtension(fbxPath);
                        EditorGUILayout.LabelField(name, EditorStyles.miniLabel);

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
                _includeRootMotion = EditorGUILayout.Toggle("Include Root Motion", _includeRootMotion);

                _exportScale = EditorGUILayout.FloatField("Export Scale", _exportScale);
                if (_exportScale <= 0)
                    _exportScale = 1f;

                EditorGUILayout.Space(5);

                _exportFormat = (RetargetApplianceExporter.ExportFormat)EditorGUILayout.EnumPopup("Export Format", _exportFormat);

                // Show warnings for missing exporters
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

                EditorGUILayout.Space(10);

                // VRM Bone Corrections Section
                DrawVrmCorrectionsSection();

                EditorGUI.indentLevel--;
            }
        }

        private void DrawVrmCorrectionsSection()
        {
            EditorGUILayout.LabelField("VRM Foot Corrections", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            // Master toggle
            _enableVrmCorrections = EditorGUILayout.Toggle("Enable Foot Corrections", _enableVrmCorrections);

            if (_enableVrmCorrections)
            {
                EditorGUILayout.HelpBox(
                    "Fixes feet pointing away/backward in VRM models by automatically aligning foot forward to hips forward.",
                    MessageType.Info);

                // Auto-fix toggle (the key feature)
                _autoFixFootDirection = EditorGUILayout.Toggle("Auto-fix Foot Direction", _autoFixFootDirection);

                if (_autoFixFootDirection)
                {
                    EditorGUI.indentLevel++;
                    _correctLeftFoot = EditorGUILayout.Toggle("Correct Left Foot", _correctLeftFoot);
                    _correctToes = EditorGUILayout.Toggle("Correct Toes", _correctToes);
                    EditorGUI.indentLevel--;
                }

                // Debug toggle
                _debugPrintAlignment = EditorGUILayout.Toggle("Debug: Print Alignment", _debugPrintAlignment);

                EditorGUILayout.Space(10);

                // Toe Stabilization Section
                DrawToeStabilizationSection();

                EditorGUILayout.Space(5);

                // Advanced/Manual foldout
                _showVrmAdvancedFoldout = EditorGUILayout.Foldout(_showVrmAdvancedFoldout, "VRM Corrections (Advanced)", true);

                if (_showVrmAdvancedFoldout)
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.HelpBox(
                        "Manual offsets are only used when 'Auto-fix Foot Direction' is OFF.",
                        MessageType.None);

                    // Profile dropdown
                    EditorGUI.BeginChangeCheck();
                    var newProfile = (VrmCorrectionProfile)EditorGUILayout.EnumPopup("Preset", _vrmCorrectionProfile);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _vrmCorrectionProfile = newProfile;
                        ApplyVrmCorrectionProfile(newProfile);
                    }

                    EditorGUILayout.Space(3);

                    // Foot offsets
                    _rightFootOffset = EditorGUILayout.Vector3Field("Right Foot Offset", _rightFootOffset);
                    _leftFootOffset = EditorGUILayout.Vector3Field("Left Foot Offset", _leftFootOffset);

                    EditorGUILayout.Space(3);

                    // Toe offsets
                    _rightToesOffset = EditorGUILayout.Vector3Field("Right Toes Offset", _rightToesOffset);
                    _leftToesOffset = EditorGUILayout.Vector3Field("Left Toes Offset", _leftToesOffset);

                    EditorGUILayout.Space(3);

                    // Utility buttons
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Mirror L→R", GUILayout.Height(20)))
                    {
                        _rightFootOffset = new Vector3(-_leftFootOffset.x, -_leftFootOffset.y, -_leftFootOffset.z);
                        _rightToesOffset = new Vector3(-_leftToesOffset.x, -_leftToesOffset.y, -_leftToesOffset.z);
                        _vrmCorrectionProfile = VrmCorrectionProfile.Custom;
                    }
                    if (GUILayout.Button("Reset", GUILayout.Height(20)))
                    {
                        ApplyVrmCorrectionProfile(VrmCorrectionProfile.None);
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUI.indentLevel--;
        }

        private void ApplyVrmCorrectionProfile(VrmCorrectionProfile profile)
        {
            _vrmCorrectionProfile = profile;

            switch (profile)
            {
                case VrmCorrectionProfile.None:
                    _leftFootOffset = Vector3.zero;
                    _rightFootOffset = Vector3.zero;
                    _leftToesOffset = Vector3.zero;
                    _rightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.VRoidA_Y90:
                    _leftFootOffset = new Vector3(0f, -90f, 0f);
                    _rightFootOffset = new Vector3(0f, 90f, 0f);
                    _leftToesOffset = Vector3.zero;
                    _rightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.VRoidB_Z90:
                    _leftFootOffset = new Vector3(0f, 0f, -90f);
                    _rightFootOffset = new Vector3(0f, 0f, 90f);
                    _leftToesOffset = Vector3.zero;
                    _rightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.VRoidC_X90:
                    _leftFootOffset = new Vector3(-90f, 0f, 0f);
                    _rightFootOffset = new Vector3(90f, 0f, 0f);
                    _leftToesOffset = Vector3.zero;
                    _rightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.Custom:
                    break;
            }
        }

        private void DrawToeStabilizationSection()
        {
            EditorGUILayout.LabelField("Toe Stabilization", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            _enableToeStabilization = EditorGUILayout.Toggle("Toe Stabilization", _enableToeStabilization);

            if (_enableToeStabilization)
            {
                EditorGUILayout.HelpBox(
                    "Fixes 'toe overdrives foot' look on VRM rigs by reducing or controlling toe rotation during baking.",
                    MessageType.Info);

                _toeStabilizationMode = (ToeStabilizationMode)EditorGUILayout.EnumPopup("Toe Mode", _toeStabilizationMode);

                // Show mode description
                string modeDescription = _toeStabilizationMode == ToeStabilizationMode.DampenRotation
                    ? "Blends toe rotation towards neutral pose."
                    : "Toe follows foot yaw rotation.";
                EditorGUILayout.LabelField(modeDescription, EditorStyles.miniLabel);

                EditorGUILayout.Space(3);

                // Strength slider
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Toe Rotation Strength", GUILayout.Width(150));
                _toeRotationStrength = EditorGUILayout.Slider(_toeRotationStrength, 0f, 1f);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("(0 = no toe rotation, 1 = original animation)", EditorStyles.miniLabel);

                EditorGUILayout.Space(3);

                // Apply to toggles
                EditorGUILayout.LabelField("Apply To:", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                _stabilizeRightToe = EditorGUILayout.Toggle("Right Toe", _stabilizeRightToe);
                _stabilizeLeftToe = EditorGUILayout.Toggle("Left Toe", _stabilizeLeftToe);
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        private VrmCorrectionSettings CreateVrmCorrectionSettings()
        {
            return new VrmCorrectionSettings
            {
                EnableCorrections = _enableVrmCorrections,
                AutoFixFootDirection = _autoFixFootDirection,
                CorrectLeftFoot = _correctLeftFoot,
                CorrectToes = _correctToes,
                DebugPrintAlignment = _debugPrintAlignment,
                Profile = _vrmCorrectionProfile,
                LeftFootOffset = _leftFootOffset,
                RightFootOffset = _rightFootOffset,
                LeftToesOffset = _leftToesOffset,
                RightToesOffset = _rightToesOffset,
                // Toe stabilization settings
                EnableToeStabilization = _enableToeStabilization,
                ToeStabilizationMode = _toeStabilizationMode,
                ToeRotationStrength = _toeRotationStrength,
                StabilizeRightToe = _stabilizeRightToe,
                StabilizeLeftToe = _stabilizeLeftToe
            };
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.Space(5);

            // Validate button
            if (GUILayout.Button("Validate Inputs", GUILayout.Height(28)))
            {
                PerformValidation();
            }

            EditorGUILayout.Space(5);

            // Force reimport button
            EditorGUI.BeginDisabledGroup(_fbxAnimations.Count == 0);
            if (GUILayout.Button("Force Reimport as Humanoid", GUILayout.Height(28)))
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
            if (GUILayout.Button(buttonLabel, mainButtonStyle, GUILayout.Height(36)))
            {
                if (CheckExporterAvailability())
                {
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

            EditorGUILayout.Space(10);
        }

        private bool CheckExporterAvailability()
        {
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
            }

            return canProceed;
        }

        private string GetExportButtonLabel()
        {
            switch (_exportFormat)
            {
                case RetargetApplianceExporter.ExportFormat.GLB:
                    return "Bake + Export GLB";
                case RetargetApplianceExporter.ExportFormat.FBX:
                    return "Bake + Export FBX";
                case RetargetApplianceExporter.ExportFormat.Both:
                default:
                    return "Bake + Export GLB/FBX";
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
                        var tempInstance = Instantiate(prefab);
                        if (!RetargetApplianceUtil.ValidateHumanoidSetup(tempInstance, out string error))
                        {
                            messages.Add($"WARNING: {error}");
                            hasWarnings = true;
                        }
                        else
                        {
                            // Debug: Print foot forward vectors if enabled
                            if (_debugPrintAlignment && _enableVrmCorrections)
                            {
                                var animator = tempInstance.GetComponent<Animator>();
                                if (animator != null)
                                {
                                    string targetName = RetargetApplianceUtil.GetTargetName(vrmPath);
                                    RetargetApplianceVrmCorrections.PrintFootForwardVectors(animator, targetName);
                                }
                            }
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

                foreach (var err in animValidation.Errors)
                {
                    messages.Add($"ERROR: {err}");
                    hasErrors = true;
                }

                foreach (var warning in animValidation.Warnings)
                {
                    messages.Add($"WARNING: {warning}");
                    hasWarnings = true;
                }
            }

            // Check exporters
            if (_exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
            {
                if (!RetargetApplianceExporter.IsUnityGLTFAvailable())
                {
                    messages.Add("WARNING: UnityGLTF not installed. GLB export will fail.");
                    hasWarnings = true;
                }
                else
                {
                    messages.Add("UnityGLTF: OK");
                }
            }

            if (_exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both)
            {
                if (!RetargetApplianceExporter.IsFBXExporterAvailable())
                {
                    messages.Add("WARNING: FBX Exporter not installed. FBX export will fail.");
                    hasWarnings = true;
                }
                else
                {
                    messages.Add("FBX Exporter: OK");
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
                _validationMessage = "Ready to bake and export.\n" + _validationMessage;
            }
        }

        private void PerformBakeAndExport()
        {
            try
            {
                var sourceClips = RetargetApplianceImporter.GetAllHumanoidClips();

                if (sourceClips.Count == 0)
                {
                    EditorUtility.DisplayDialog(
                        "No Clips",
                        "No valid humanoid animation clips found.\n\nMake sure to run 'Force Reimport as Humanoid' first.",
                        "OK");
                    return;
                }

                RetargetApplianceUtil.LogInfo($"Starting bake process with {sourceClips.Count} clips for {_vrmTargets.Count} targets...");

                Scene workspaceScene = EnsureWorkspaceScene();

                var settings = new RetargetApplianceBaker.BakeSettings
                {
                    FPS = _bakeFPS,
                    IncludeRootMotion = _includeRootMotion,
                    ExportScale = _exportScale,
                    VrmCorrections = _enableVrmCorrections ? CreateVrmCorrectionSettings() : null
                };

                int totalTargets = _vrmTargets.Count;
                int totalClipsFound = sourceClips.Count;
                int totalBakedClips = 0;
                int totalExportsAttempted = 0;
                int glbSuccessCount = 0;
                int glbFailCount = 0;
                int fbxSuccessCount = 0;
                int fbxFailCount = 0;
                int bakeFailCount = 0;

                bool exportGLB = _exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both;
                bool exportFBX = _exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both;

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

                    // Export each baked clip individually with unique filenames
                    foreach (var clipResult in bakeResult.ClipResults)
                    {
                        if (!clipResult.Success || clipResult.BakedClip == null)
                            continue;

                        totalBakedClips++;

                        // Load the saved clip from the asset path
                        var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipResult.SavedAssetPath);
                        if (savedClip == null)
                        {
                            RetargetApplianceUtil.LogWarning($"Could not load baked clip from: {clipResult.SavedAssetPath}");
                            continue;
                        }

                        // Create unique export filename using the preserved unique clip name
                        string sourceClipName = !string.IsNullOrEmpty(clipResult.SourceClipName)
                            ? clipResult.SourceClipName
                            : (clipResult.SourceClip != null ? clipResult.SourceClip.name : savedClip.name);
                        string exportFileName = RetargetApplianceUtil.GetExportFileName(targetName, sourceClipName);

                        // Create a list with just this one clip for export
                        var singleClipList = new List<AnimationClip> { savedClip };

                        if (exportGLB)
                        {
                            totalExportsAttempted++;
                            var glbResult = RetargetApplianceExporter.ExportAsGLB(
                                bakeResult.TargetInstance,
                                targetName,
                                singleClipList,
                                settings,
                                exportFileName);

                            if (glbResult.Success)
                            {
                                glbSuccessCount++;
                                RetargetApplianceUtil.LogInfo($"Exported GLB: {glbResult.ExportPath}");
                            }
                            else
                            {
                                glbFailCount++;
                                RetargetApplianceUtil.LogError($"GLB export failed for '{exportFileName}': {glbResult.Error}");
                            }
                        }

                        if (exportFBX)
                        {
                            totalExportsAttempted++;
                            var fbxResult = RetargetApplianceExporter.ExportAsFBX(
                                bakeResult.TargetInstance,
                                targetName,
                                singleClipList,
                                settings,
                                exportFileName);

                            if (fbxResult.Success)
                            {
                                fbxSuccessCount++;
                                RetargetApplianceUtil.LogInfo($"Exported FBX: {fbxResult.ExportPath}");
                            }
                            else
                            {
                                fbxFailCount++;
                                RetargetApplianceUtil.LogError($"FBX export failed for '{exportFileName}': {fbxResult.Error}");
                            }
                        }
                    }

                    CleanupTarget(bakeResult);
                }

                RetargetApplianceUtil.ClearProgress();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Build summary with detailed statistics
                var summaryLines = new List<string>();
                summaryLines.Add("Bake and Export Complete!\n");
                summaryLines.Add($"Source clips found: {totalClipsFound}");
                summaryLines.Add($"Targets processed: {totalTargets}");
                summaryLines.Add($"Total clips baked: {totalBakedClips}");
                summaryLines.Add($"Total exports attempted: {totalExportsAttempted}");

                if (bakeFailCount > 0)
                {
                    summaryLines.Add($"\nBake failures: {bakeFailCount}");
                }

                summaryLines.Add("");

                if (exportGLB)
                {
                    summaryLines.Add($"GLB: {glbSuccessCount} successful, {glbFailCount} failed");
                }

                if (exportFBX)
                {
                    summaryLines.Add($"FBX: {fbxSuccessCount} successful, {fbxFailCount} failed");
                }

                summaryLines.Add($"\nOutput: {RetargetApplianceUtil.OutputExportPath}");

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

            string scenePath = RetargetApplianceUtil.WorkspaceScenePath;

            if (!System.IO.File.Exists(scenePath))
            {
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var lightGO = new GameObject("Directional Light");
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);

                RetargetApplianceUtil.EnsureFolderExists("Assets/Scenes");
                EditorSceneManager.SaveScene(newScene, scenePath);

                RetargetApplianceUtil.LogInfo($"Created workspace scene: {scenePath}");
                return newScene;
            }
            else
            {
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
