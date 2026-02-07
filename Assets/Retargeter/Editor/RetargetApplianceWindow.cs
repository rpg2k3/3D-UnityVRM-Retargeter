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

        // VRMA-first pipeline settings (new defaults)
        private bool _exportVrmaLibrary = true;     // Export VRMA library (default ON)
        private bool _exportGlbFromVrma = true;     // Export GLB via VRMA round-trip (default ON)

        // Legacy mode toggle (safety rollback)
        private bool _legacyMode = false;           // Legacy Mode (debug) - default OFF
        private bool _showLegacyFoldout = false;    // UI foldout for legacy settings

        // Legacy export settings (only used when _legacyMode = true)
        private RetargetApplianceExporter.ExportFormat _exportFormat = RetargetApplianceExporter.ExportFormat.Both;
        private bool _exportVrma = true;
        private bool _exportGlbViaVrmaRoundTrip = false;

        // VRM Correction Settings (simplified)
        private bool _enableVrmCorrections = true;
        private bool _autoFixFootDirection = true;
        private bool _correctLeftFoot = true;
        private bool _correctToes = true;
        private bool _debugPrintAlignment = false;

        // Toe Stabilization Settings (VRMA pipeline: OFF by default)
        private bool _enableToeStabilization = false;
        private ToeStabilizationMode _toeStabilizationMode = ToeStabilizationMode.DampenRotation;
        private float _toeRotationStrength = 0.25f;
        private bool _stabilizeRightToe = true;
        private bool _stabilizeLeftToe = false;

        // Toe Yaw Correction Settings (VRMA pipeline: OFF by default)
        private bool _enableToeYawCorrection = false;
        private ToeYawCorrectionMode _toeYawCorrectionMode = ToeYawCorrectionMode.BlendTowardIdentity;
        private float _maxToeYawDegrees = 10f;
        private float _toeYawBlendFactor = 0.3f;
        private bool _correctRightToeYaw = true;
        private bool _correctLeftToeYaw = true;
        private bool _showToeYawFoldout = false;

        // Foot Stabilization Settings (VRMA pipeline: OFF by default)
        private bool _enableFootStabilization = false;
        private bool _applyRightFoot = true;
        private bool _applyLeftFoot = true;
        private FootStabilizationAxisMode _footPitchMode = FootStabilizationAxisMode.Clamp;
        private FootStabilizationAxisMode _footRollMode = FootStabilizationAxisMode.Dampen;
        private float _pitchClampDeg = 25f;
        private float _rollClampDeg = 15f;
        private float _footDampenStrength = 0.35f;
        private bool _showFootStabilizationFoldout = false;

        // Advanced (Legacy Fixes) foldout - collapsed by default
        private bool _showAdvancedLegacyFixesFoldout = false;

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
            EditorGUILayout.LabelField("VRMA-first pipeline: Mixamo FBX -> VRMA library -> GLB output", EditorStyles.miniLabel);

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

            // VRMA Exporter status
            bool hasVrmaExporter = RetargetApplianceVrmaExporter.IsVrmaExportAvailable();
            var vrmaStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            vrmaStatusStyle.normal.textColor = hasVrmaExporter ? Color.green : Color.yellow;
            EditorGUILayout.LabelField(hasVrmaExporter ? "VRMA: OK" : "VRMA: N/A", vrmaStatusStyle, GUILayout.Width(70));

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

                // VRMA-first pipeline UI (default)
                if (!_legacyMode)
                {
                    EditorGUILayout.LabelField("VRMA Pipeline Output", EditorStyles.boldLabel);

                    // VRMA Library export toggle
                    EditorGUILayout.BeginHorizontal();
                    _exportVrmaLibrary = EditorGUILayout.Toggle("Export VRMA library", _exportVrmaLibrary);
                    if (!RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                    {
                        var warningStyle = new GUIStyle(EditorStyles.miniLabel);
                        warningStyle.normal.textColor = Color.yellow;
                        EditorGUILayout.LabelField("(AnimationClipToVrma not found)", warningStyle);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (_exportVrmaLibrary)
                    {
                        EditorGUILayout.LabelField($"  Output: Assets/Output/VRMA/<Target>/<Target>__<Clip>.vrma", EditorStyles.miniLabel);
                    }

                    // GLB export toggle
                    EditorGUILayout.BeginHorizontal();
                    _exportGlbFromVrma = EditorGUILayout.Toggle("Export GLB", _exportGlbFromVrma);
                    if (!RetargetApplianceExporter.IsUnityGLTFAvailable())
                    {
                        var warningStyle = new GUIStyle(EditorStyles.miniLabel);
                        warningStyle.normal.textColor = Color.yellow;
                        EditorGUILayout.LabelField("(UnityGLTF not found)", warningStyle);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (_exportGlbFromVrma)
                    {
                        EditorGUILayout.LabelField($"  Output: Assets/Output/GLB/<Target>/<Target>__<Clip>.glb", EditorStyles.miniLabel);
                    }

                    // GLB export works independently of VRMA in VRMA-first pipeline
                }
                else
                {
                    // Legacy export UI (only shown when legacyMode = true)
                    EditorGUILayout.LabelField("Legacy Export Format", EditorStyles.boldLabel);

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

                    EditorGUILayout.Space(5);

                    // VRMA Export toggle (legacy)
                    EditorGUILayout.BeginHorizontal();
                    _exportVrma = EditorGUILayout.Toggle("Export VRMA", _exportVrma);
                    if (!RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                    {
                        var warningStyle = new GUIStyle(EditorStyles.miniLabel);
                        warningStyle.normal.textColor = Color.yellow;
                        EditorGUILayout.LabelField("(AnimationClipToVrma not found)", warningStyle);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (_exportVrma)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Output: {RetargetApplianceVrmaExporter.OutputVrmaPath}/", EditorStyles.miniLabel);

                        // VRMA Round-trip GLB export option
                        EditorGUILayout.BeginHorizontal();
                        _exportGlbViaVrmaRoundTrip = EditorGUILayout.Toggle("Export GLB from VRMA (round-trip)", _exportGlbViaVrmaRoundTrip);
                        EditorGUILayout.EndHorizontal();

                        if (_exportGlbViaVrmaRoundTrip)
                        {
                            EditorGUILayout.LabelField($"  GLB Output: {RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath}/", EditorStyles.miniLabel);
                        }

                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.Space(10);

                // VRM Bone Corrections Section
                DrawVrmCorrectionsSection();

                // Legacy Mode toggle at bottom of settings (safety rollback)
                EditorGUILayout.Space(10);
                _showLegacyFoldout = EditorGUILayout.Foldout(_showLegacyFoldout, "Legacy Mode (debug)", true);
                if (_showLegacyFoldout)
                {
                    EditorGUI.indentLevel++;
                    _legacyMode = EditorGUILayout.Toggle("Enable Legacy Mode", _legacyMode);
                    EditorGUILayout.HelpBox(
                        "Legacy Mode re-enables old export toggles (GLB/FBX) and foot/toe fixes UI.\n" +
                        "Keep OFF for VRMA-first pipeline.",
                        MessageType.None);
                    EditorGUI.indentLevel--;
                }

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

                // Advanced (Legacy Fixes) foldout - contains toe/foot stabilization
                // These are OFF by default in VRMA pipeline
                _showAdvancedLegacyFixesFoldout = EditorGUILayout.Foldout(_showAdvancedLegacyFixesFoldout, "Advanced (Legacy Fixes)", true);

                if (_showAdvancedLegacyFixesFoldout)
                {
                    EditorGUI.indentLevel++;

                    // Note about legacy fixes
                    EditorGUILayout.HelpBox(
                        "Legacy fixes OFF by default in VRMA pipeline. Only enable if you see specific issues.",
                        MessageType.None);

                    EditorGUILayout.Space(5);

                    // Foot Stabilization Section (pitch/roll control)
                    DrawFootStabilizationSection();

                    EditorGUILayout.Space(10);

                    // Toe Stabilization Section
                    DrawToeStabilizationSection();

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);

                // Advanced/Manual foldout for VRM corrections
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

                EditorGUILayout.Space(5);

                // Toe Yaw Correction foldout (advanced settings)
                _showToeYawFoldout = EditorGUILayout.Foldout(_showToeYawFoldout, "Toe Yaw Correction (Advanced)", true);
                if (_showToeYawFoldout)
                {
                    EditorGUI.indentLevel++;

                    _enableToeYawCorrection = EditorGUILayout.Toggle("Enable Yaw Correction", _enableToeYawCorrection);

                    if (_enableToeYawCorrection)
                    {
                        EditorGUILayout.HelpBox(
                            "Additional toe yaw adjustment. Applied after stabilization to fine-tune foot direction.",
                            MessageType.None);

                        _toeYawCorrectionMode = (ToeYawCorrectionMode)EditorGUILayout.EnumPopup("Yaw Mode", _toeYawCorrectionMode);

                        if (_toeYawCorrectionMode == ToeYawCorrectionMode.ClampYaw)
                        {
                            _maxToeYawDegrees = EditorGUILayout.Slider("Max Yaw Degrees", _maxToeYawDegrees, 0f, 45f);
                        }
                        else if (_toeYawCorrectionMode == ToeYawCorrectionMode.BlendTowardIdentity)
                        {
                            _toeYawBlendFactor = EditorGUILayout.Slider("Blend Factor", _toeYawBlendFactor, 0f, 1f);
                        }

                        EditorGUILayout.BeginHorizontal();
                        _correctRightToeYaw = EditorGUILayout.Toggle("Right", _correctRightToeYaw, GUILayout.Width(60));
                        _correctLeftToeYaw = EditorGUILayout.Toggle("Left", _correctLeftToeYaw, GUILayout.Width(60));
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.LabelField("(Toe yaw correction is also disabled)", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawFootStabilizationSection()
        {
            _showFootStabilizationFoldout = EditorGUILayout.Foldout(_showFootStabilizationFoldout, "Foot Stabilization (Pitch/Roll)", true);

            if (!_showFootStabilizationFoldout)
                return;

            EditorGUI.indentLevel++;

            _enableFootStabilization = EditorGUILayout.Toggle("Enable Foot Stabilization", _enableFootStabilization);

            if (_enableFootStabilization)
            {
                EditorGUILayout.HelpBox(
                    "Reduces excessive foot pitch (up/down) and roll (in/out) tilting in baked animations. " +
                    "Useful when the source animation has too much foot rotation.",
                    MessageType.Info);

                EditorGUILayout.Space(3);

                // Apply to toggles
                EditorGUILayout.LabelField("Apply To:", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                _applyRightFoot = EditorGUILayout.Toggle("Right Foot", _applyRightFoot);
                _applyLeftFoot = EditorGUILayout.Toggle("Left Foot", _applyLeftFoot);
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(5);

                // Pitch mode
                EditorGUILayout.LabelField("Pitch (Up/Down Tilt):", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                _footPitchMode = (FootStabilizationAxisMode)EditorGUILayout.EnumPopup("Mode", _footPitchMode);
                if (_footPitchMode == FootStabilizationAxisMode.Clamp)
                {
                    _pitchClampDeg = EditorGUILayout.Slider("Max Pitch (deg)", _pitchClampDeg, 5f, 60f);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(3);

                // Roll mode
                EditorGUILayout.LabelField("Roll (In/Out Tilt):", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                _footRollMode = (FootStabilizationAxisMode)EditorGUILayout.EnumPopup("Mode", _footRollMode);
                if (_footRollMode == FootStabilizationAxisMode.Clamp)
                {
                    _rollClampDeg = EditorGUILayout.Slider("Max Roll (deg)", _rollClampDeg, 5f, 45f);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(3);

                // Dampen strength (only show if either mode is Dampen)
                if (_footPitchMode == FootStabilizationAxisMode.Dampen || _footRollMode == FootStabilizationAxisMode.Dampen)
                {
                    EditorGUILayout.LabelField("Dampen Settings:", EditorStyles.miniBoldLabel);
                    EditorGUI.indentLevel++;
                    _footDampenStrength = EditorGUILayout.Slider("Dampen Strength", _footDampenStrength, 0f, 1f);
                    EditorGUILayout.LabelField("(0 = no change, 1 = fully neutral)", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
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
                StabilizeLeftToe = _stabilizeLeftToe,
                // Toe yaw correction settings (only active when stabilization is enabled)
                EnableToeYawCorrection = _enableToeYawCorrection,
                ToeYawCorrectionMode = _toeYawCorrectionMode,
                MaxToeYawDegrees = _maxToeYawDegrees,
                ToeYawBlendFactor = _toeYawBlendFactor,
                CorrectRightToeYaw = _correctRightToeYaw,
                CorrectLeftToeYaw = _correctLeftToeYaw,
                // Foot stabilization settings (pitch/roll control)
                EnableFootStabilization = _enableFootStabilization,
                ApplyRightFoot = _applyRightFoot,
                ApplyLeftFoot = _applyLeftFoot,
                FootPitchMode = _footPitchMode,
                FootRollMode = _footRollMode,
                PitchClampDeg = _pitchClampDeg,
                RollClampDeg = _rollClampDeg,
                FootDampenStrength = _footDampenStrength
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
                    string dialogMessage;
                    if (!_legacyMode)
                    {
                        // VRMA-first pipeline message
                        var outputs = new List<string>();
                        if (_exportVrmaLibrary) outputs.Add("VRMA library");
                        if (_exportGlbFromVrma) outputs.Add("GLB");
                        string outputsText = outputs.Count > 0 ? string.Join(" + ", outputs) : "Bake only";

                        dialogMessage = $"VRMA-first Pipeline:\n" +
                            $"- Bake {_fbxAnimations.Count} animation(s) onto {_vrmTargets.Count} target(s)\n" +
                            $"- Export: {outputsText}\n\n" +
                            $"Output paths:\n" +
                            $"  VRMA: Assets/Output/VRMA/<Target>/\n" +
                            $"  GLB: Assets/Output/GLB/<Target>/\n\n" +
                            $"Continue?";
                    }
                    else
                    {
                        // Legacy pipeline message
                        string exportFormatText = _exportFormat == RetargetApplianceExporter.ExportFormat.Both ? "GLB and FBX" : _exportFormat.ToString();
                        dialogMessage = $"Legacy Mode:\n" +
                            $"- Bake {_fbxAnimations.Count} animation(s) onto {_vrmTargets.Count} target(s)\n" +
                            $"- Export each target as {exportFormatText}\n\n" +
                            $"Continue?";
                    }

                    if (EditorUtility.DisplayDialog("Bake and Export", dialogMessage, "Yes", "Cancel"))
                    {
                        PerformBakeAndExport();
                    }
                }
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(10);

            // Output folder buttons
            EditorGUILayout.BeginHorizontal();

            if (_legacyMode)
            {
                // Legacy mode: show old folder buttons
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
                if (GUILayout.Button("Open VRMA Folder"))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceVrmaExporter.OutputVrmaPath);
                    EditorUtility.RevealInFinder(RetargetApplianceVrmaExporter.OutputVrmaPath);
                }
            }
            else
            {
                // VRMA-first pipeline: show VRMA and GLB folders
                if (GUILayout.Button("Open VRMA Folder"))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceVrmaExporter.OutputVrmaPath);
                    EditorUtility.RevealInFinder(RetargetApplianceVrmaExporter.OutputVrmaPath);
                }
                if (GUILayout.Button("Open GLB Folder"))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath);
                    EditorUtility.RevealInFinder(RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath);
                }
                if (GUILayout.Button("Open Prefabs Folder"))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceUtil.OutputPrefabsPath);
                    EditorUtility.RevealInFinder(RetargetApplianceUtil.OutputPrefabsPath);
                }
            }

            EditorGUILayout.EndHorizontal();

            // Second row of folder buttons (only show in legacy mode if VRMA round-trip is enabled)
            if (_legacyMode && _exportGlbViaVrmaRoundTrip)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open VRMA->GLB Folder"))
                {
                    RetargetApplianceUtil.EnsureFolderExists(RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath);
                    EditorUtility.RevealInFinder(RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10);
        }

        private bool CheckExporterAvailability()
        {
            bool canProceed = true;
            string missingMessage = "";

            // VRMA-first pipeline (new default)
            if (!_legacyMode)
            {
                if (_exportVrmaLibrary && !RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                {
                    missingMessage += "AnimationClipToVrma is required for VRMA export but is not installed.\n\n";
                    canProceed = false;
                }

                if (_exportGlbFromVrma && !RetargetApplianceExporter.IsUnityGLTFAvailable())
                {
                    missingMessage += "UnityGLTF is required for GLB export but is not installed.\n\n";
                    canProceed = false;
                }

                if (!canProceed)
                {
                    EditorUtility.DisplayDialog("Missing Dependencies", missingMessage, "OK");
                }

                return canProceed;
            }

            // Legacy mode checks
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
            // VRMA-first pipeline (new default)
            if (!_legacyMode)
            {
                if (_exportVrmaLibrary && _exportGlbFromVrma)
                    return "Bake + Export VRMA + GLB";
                else if (_exportVrmaLibrary)
                    return "Bake + Export VRMA";
                else if (_exportGlbFromVrma)
                    return "Bake + Export GLB";
                else
                    return "Bake Only";
            }

            // Legacy mode
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

            // Check exporters based on pipeline mode
            if (!_legacyMode)
            {
                // VRMA-first pipeline validation
                messages.Add("\n[VRMA-first Pipeline]");

                if (_exportVrmaLibrary)
                {
                    if (!RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                    {
                        messages.Add("WARNING: VRMA export enabled but AnimationClipToVrma not installed.");
                        hasWarnings = true;
                    }
                    else
                    {
                        messages.Add("VRMA Exporter: OK");
                    }
                }

                if (_exportGlbFromVrma)
                {
                    if (!RetargetApplianceExporter.IsUnityGLTFAvailable())
                    {
                        messages.Add("WARNING: UnityGLTF not installed. GLB export will fail.");
                        hasWarnings = true;
                    }
                    else
                    {
                        messages.Add("UnityGLTF: OK (direct GLB from baked clips)");
                    }
                }
            }
            else
            {
                // Legacy mode validation
                messages.Add("\n[Legacy Mode]");

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

                // Check VRMA exporter in legacy mode
                if (_exportVrma)
                {
                    if (!RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                    {
                        messages.Add("WARNING: VRMA export enabled but AnimationClipToVrma not installed. VRMA export will be skipped.");
                        hasWarnings = true;
                    }
                    else
                    {
                        messages.Add("VRMA Exporter: OK");
                    }
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
                int vrmaSuccessCount = 0;
                int vrmaFailCount = 0;
                int vrmaRoundTripSuccessCount = 0;
                int vrmaRoundTripFailCount = 0;
                int glbDirectSuccessCount = 0;
                int glbDirectFailCount = 0;
                int bakeFailCount = 0;

                // Determine export modes based on legacy vs VRMA-first pipeline
                bool useLegacyPipeline = _legacyMode;
                bool exportLegacyGLB = useLegacyPipeline && (_exportFormat == RetargetApplianceExporter.ExportFormat.GLB || _exportFormat == RetargetApplianceExporter.ExportFormat.Both);
                bool exportFBX = useLegacyPipeline && (_exportFormat == RetargetApplianceExporter.ExportFormat.FBX || _exportFormat == RetargetApplianceExporter.ExportFormat.Both);

                // VRMA-first pipeline exports
                bool exportVRMA = useLegacyPipeline
                    ? (_exportVrma && RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                    : (_exportVrmaLibrary && RetargetApplianceVrmaExporter.IsVrmaExportAvailable());
                bool exportGlbViaVrma = useLegacyPipeline && _exportGlbViaVrmaRoundTrip && exportVRMA;
                bool exportGlbDirect = !useLegacyPipeline && _exportGlbFromVrma && RetargetApplianceExporter.IsUnityGLTFAvailable();

                // Log pipeline mode
                if (useLegacyPipeline)
                {
                    RetargetApplianceUtil.LogInfo("[RetargetAppliance] Running in LEGACY mode");
                }
                else
                {
                    RetargetApplianceUtil.LogInfo("[RetargetAppliance] Running in VRMA-first pipeline mode");
                }

                // Log VRMA status at start
                if (!useLegacyPipeline && _exportVrmaLibrary && !RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                {
                    RetargetApplianceUtil.LogWarning("VRMA export enabled but API not available. VRMA files will not be exported.");
                }
                if (useLegacyPipeline && _exportVrma && !RetargetApplianceVrmaExporter.IsVrmaExportAvailable())
                {
                    RetargetApplianceUtil.LogWarning("VRMA export enabled but API not available. VRMA files will not be exported.");
                }

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
                    // CRITICAL: Clone the target for each export to prevent clip contamination
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

                        // Log the export operation
                        RetargetApplianceUtil.LogInfo($"[RetargetAppliance] ExportClip='{sourceClipName}' SourceAsset='{clipResult.SavedAssetPath}'");

                        // Create a fresh clone for this export to prevent clip contamination
                        var exportRoot = Instantiate(bakeResult.TargetInstance);
                        exportRoot.name = $"{targetName}_Export";
                        exportRoot.hideFlags = HideFlags.HideAndDontSave;

                        // Normalize transform so the export root is at origin with no rotation.
                        // Clones inherit the scene instance's world transform, which causes
                        // GLB viewers to auto-frame incorrectly.
                        exportRoot.transform.SetParent(null);
                        exportRoot.transform.position = Vector3.zero;
                        exportRoot.transform.rotation = Quaternion.identity;

                        // Disable Animator controller influence on the export clone
                        var exportAnimator = exportRoot.GetComponent<Animator>();
                        if (exportAnimator != null)
                        {
                            exportAnimator.runtimeAnimatorController = null;
                        }

                        try
                        {
                            // Legacy GLB export (only in legacy mode)
                            if (exportLegacyGLB)
                            {
                                totalExportsAttempted++;
                                var glbResult = RetargetApplianceExporter.ExportAsGLB(
                                    exportRoot,
                                    targetName,
                                    singleClipList,
                                    settings,
                                    exportFileName);

                                if (glbResult.Success)
                                {
                                    glbSuccessCount++;
                                    RetargetApplianceUtil.LogInfo($"[RetargetAppliance] Legacy GLB: OK -> {glbResult.ExportPath}");
                                }
                                else
                                {
                                    glbFailCount++;
                                    RetargetApplianceUtil.LogError($"[RetargetAppliance] Legacy GLB: FAILED -> {glbResult.Error}");
                                }
                            }

                            // FBX export (only in legacy mode)
                            if (exportFBX)
                            {
                                totalExportsAttempted++;
                                var fbxResult = RetargetApplianceExporter.ExportAsFBX(
                                    exportRoot,
                                    targetName,
                                    singleClipList,
                                    settings,
                                    exportFileName);

                                if (fbxResult.Success)
                                {
                                    fbxSuccessCount++;
                                    RetargetApplianceUtil.LogInfo($"[RetargetAppliance] FBX: OK -> {fbxResult.ExportPath}");
                                }
                                else
                                {
                                    fbxFailCount++;
                                    RetargetApplianceUtil.LogError($"[RetargetAppliance] FBX: FAILED -> {fbxResult.Error}");
                                }
                            }

                            // VRMA Export - uses original prefab, not export clone
                            if (exportVRMA)
                            {
                                totalExportsAttempted++;
                                RetargetApplianceVrmaExporter.EnsureVrmaOutputFolder(targetName);
                                string vrmaOutPath = RetargetApplianceVrmaExporter.GetVrmaOutputPath(targetName, sourceClipName);

                                if (RetargetApplianceVrmaExporter.TryExportVrma(bakeResult.TargetInstance, savedClip, vrmaOutPath, out string vrmaError))
                                {
                                    vrmaSuccessCount++;
                                    RetargetApplianceUtil.LogInfo($"[RetargetAppliance] VRMA: OK -> {vrmaOutPath}");

                                    // GLB via VRMA round-trip (legacy mode only)
                                    if (exportGlbViaVrma)
                                    {
                                        // Create a fresh clone for GLB export
                                        var glbExportClone = Instantiate(bakeResult.TargetInstance);
                                        glbExportClone.name = $"{targetName}_VrmaGlbExport";
                                        glbExportClone.hideFlags = HideFlags.HideAndDontSave;

                                        // Normalize transform before passing to TryExportGlbFromVrma,
                                        // which will clone this again internally. Without this, the clone
                                        // inherits the scene instance's world position/rotation, causing
                                        // the final GLB to be offset from origin.
                                        glbExportClone.transform.SetParent(null);
                                        glbExportClone.transform.position = Vector3.zero;
                                        glbExportClone.transform.rotation = Quaternion.identity;

                                        // Disable Animator controller influence
                                        var glbAnimator = glbExportClone.GetComponent<Animator>();
                                        if (glbAnimator != null)
                                        {
                                            glbAnimator.runtimeAnimatorController = null;
                                        }

                                        try
                                        {
                                            RetargetApplianceVrmaRoundTrip.EnsureGlbOutputFolder(targetName);

                                            string vrmaGlbPath = RetargetApplianceVrmaRoundTrip.GetGlbFromVrmaOutputPath(targetName, sourceClipName);

                                            if (RetargetApplianceVrmaRoundTrip.TryExportGlbFromVrma(
                                                glbExportClone,
                                                vrmaOutPath,
                                                vrmaGlbPath,
                                                _bakeFPS,
                                                out string vrmaGlbError))
                                            {
                                                vrmaRoundTripSuccessCount++;
                                                RetargetApplianceUtil.LogInfo($"[RetargetAppliance] VRMA->GLB OK: {targetName}__{sourceClipName}");
                                            }
                                            else
                                            {
                                                vrmaRoundTripFailCount++;
                                                RetargetApplianceUtil.LogError($"[RetargetAppliance] VRMA->GLB FAILED: {targetName}__{sourceClipName}");
                                                RetargetApplianceUtil.LogError($"[RetargetAppliance] VRMA->GLB Error: {vrmaGlbError}");
                                            }
                                        }
                                        finally
                                        {
                                            DestroyImmediate(glbExportClone);
                                        }
                                    }
                                }
                                else
                                {
                                    vrmaFailCount++;
                                    RetargetApplianceUtil.LogError($"[RetargetAppliance] VRMA: FAILED -> {vrmaError}");
                                }
                            }

                            // Direct GLB export from BAKED clip (VRMA-first pipeline)
                            // Uses the full-fidelity baked clip directly, bypassing the lossy
                            // VRMA round-trip that only samples 22 HumanBodyBones.
                            if (exportGlbDirect)
                            {
                                totalExportsAttempted++;
                                string glbOutputFolder = $"{RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath}/{targetName}";
                                RetargetApplianceUtil.EnsureFolderExists(glbOutputFolder);

                                var glbResult = RetargetApplianceExporter.ExportAsGLB(
                                    exportRoot,
                                    targetName,
                                    singleClipList,
                                    settings,
                                    exportFileName,
                                    glbOutputFolder);

                                if (glbResult.Success)
                                {
                                    glbDirectSuccessCount++;
                                    RetargetApplianceUtil.LogInfo($"[RetargetAppliance] GLB (direct): OK -> {glbResult.ExportPath}");
                                }
                                else
                                {
                                    glbDirectFailCount++;
                                    RetargetApplianceUtil.LogError($"[RetargetAppliance] GLB (direct): FAILED -> {glbResult.Error}");
                                }
                            }
                        }
                        finally
                        {
                            // Always clean up the export clone
                            DestroyImmediate(exportRoot);
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
                summaryLines.Add($"Pipeline: {(useLegacyPipeline ? "Legacy" : "VRMA-first")}");
                summaryLines.Add($"Source clips found: {totalClipsFound}");
                summaryLines.Add($"Targets processed: {totalTargets}");
                summaryLines.Add($"Total clips baked: {totalBakedClips}");
                summaryLines.Add($"Total exports attempted: {totalExportsAttempted}");

                if (bakeFailCount > 0)
                {
                    summaryLines.Add($"\nBake failures: {bakeFailCount}");
                }

                summaryLines.Add("");

                if (exportLegacyGLB)
                {
                    summaryLines.Add($"Legacy GLB: {glbSuccessCount} successful, {glbFailCount} failed");
                }

                if (exportFBX)
                {
                    summaryLines.Add($"FBX: {fbxSuccessCount} successful, {fbxFailCount} failed");
                }

                if (exportVRMA)
                {
                    summaryLines.Add($"VRMA: {vrmaSuccessCount} successful, {vrmaFailCount} failed");

                    if (exportGlbViaVrma)
                    {
                        summaryLines.Add($"VRMA->GLB: {vrmaRoundTripSuccessCount} successful, {vrmaRoundTripFailCount} failed");
                    }
                }

                if (exportGlbDirect)
                {
                    summaryLines.Add($"GLB (direct): {glbDirectSuccessCount} successful, {glbDirectFailCount} failed");
                }

                summaryLines.Add("");

                if (useLegacyPipeline)
                {
                    summaryLines.Add($"Output: {RetargetApplianceUtil.OutputExportPath}");
                }
                if (exportVRMA && vrmaSuccessCount > 0)
                {
                    summaryLines.Add($"VRMA: {RetargetApplianceVrmaExporter.OutputVrmaPath}");
                }
                if ((exportGlbViaVrma && vrmaRoundTripSuccessCount > 0) || (exportGlbDirect && glbDirectSuccessCount > 0))
                {
                    summaryLines.Add($"GLB: {RetargetApplianceVrmaRoundTrip.OutputGlbFromVrmaPath}");
                }

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
