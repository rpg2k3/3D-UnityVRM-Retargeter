using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RetargetAppliance
{
    /// <summary>
    /// Handles retargeted animation baking from source clips to target skeletons.
    /// </summary>
    public static class RetargetApplianceBaker
    {
        // FBX-compatible property names for transform curves
        private const string PositionPropertyX = "m_LocalPosition.x";
        private const string PositionPropertyY = "m_LocalPosition.y";
        private const string PositionPropertyZ = "m_LocalPosition.z";

        // Using "localEulerAnglesRaw" for Euler rotation curves (FBX Exporter compatible)
        private const string RotationPropertyX = "localEulerAnglesRaw.x";
        private const string RotationPropertyY = "localEulerAnglesRaw.y";
        private const string RotationPropertyZ = "localEulerAnglesRaw.z";

        private static bool _hasLoggedPropertyNames = false;

        /// <summary>
        /// Settings for the baking process.
        /// </summary>
        public class BakeSettings
        {
            public int FPS = 30;
            public bool IncludeRootMotion = false;
            public float ExportScale = 1f;
            public bool OptimizeStaticCurves = true;

            /// <summary>VRM bone correction settings. If null, no corrections are applied.</summary>
            public VrmCorrectionSettings VrmCorrections = null;
        }

        /// <summary>
        /// Result of baking a single clip.
        /// </summary>
        public class BakeResult
        {
            public AnimationClip SourceClip;
            public AnimationClip BakedClip;
            public string TargetName;
            /// <summary>Unique source clip name (from AnimationClipInfo.ClipName) for export naming.</summary>
            public string SourceClipName;
            public string SavedAssetPath;
            public string Error;
            public bool Success => string.IsNullOrEmpty(Error) && BakedClip != null;
        }

        /// <summary>
        /// Result of baking all clips for a single target.
        /// </summary>
        public class TargetBakeResult
        {
            public string TargetName;
            public string VRMPath;
            public GameObject TargetInstance;
            public List<BakeResult> ClipResults = new List<BakeResult>();
            public string PrefabPath;
            public string Error;

            public int SuccessCount
            {
                get
                {
                    int count = 0;
                    foreach (var r in ClipResults)
                        if (r.Success) count++;
                    return count;
                }
            }
        }

        /// <summary>
        /// Bakes all source animations onto a target VRM.
        /// </summary>
        public static TargetBakeResult BakeAnimationsForTarget(
            string vrmPath,
            List<RetargetApplianceImporter.AnimationClipInfo> sourceClips,
            BakeSettings settings)
        {
            var result = new TargetBakeResult
            {
                VRMPath = vrmPath,
                TargetName = RetargetApplianceUtil.GetTargetName(vrmPath)
            };

            _hasLoggedPropertyNames = false;

            // Get the VRM prefab
            GameObject prefab = RetargetApplianceUtil.GetVRMPrefab(vrmPath);
            if (prefab == null)
            {
                result.Error = $"Could not find prefab for VRM: {vrmPath}";
                RetargetApplianceUtil.LogError(result.Error);
                return result;
            }

            // Instantiate the target
            result.TargetInstance = UnityEngine.Object.Instantiate(prefab);
            result.TargetInstance.name = result.TargetName;

            // Validate humanoid setup
            if (!RetargetApplianceUtil.ValidateHumanoidSetup(result.TargetInstance, out string validationError))
            {
                result.Error = validationError;
                RetargetApplianceUtil.LogError(result.Error);
                UnityEngine.Object.DestroyImmediate(result.TargetInstance);
                result.TargetInstance = null;
                return result;
            }

            // Ensure output folders exist
            string targetOutputFolder = $"{RetargetApplianceUtil.OutputPrefabsPath}/{result.TargetName}";
            string animationsFolder = $"{targetOutputFolder}/Animations";
            RetargetApplianceUtil.EnsureFolderExists(targetOutputFolder);
            RetargetApplianceUtil.EnsureFolderExists(animationsFolder);

            // Check if VRM corrections should be applied
            bool applyVrmCorrections = settings.VrmCorrections != null &&
                                       settings.VrmCorrections.EnableCorrections &&
                                       settings.VrmCorrections.HasAnyCorrection();

            if (applyVrmCorrections)
            {
                bool isVrm = RetargetApplianceVrmCorrections.IsVRMTarget(result.TargetInstance);
                if (!isVrm)
                {
                    RetargetApplianceUtil.LogWarning($"VRM corrections enabled but '{result.TargetName}' is not detected as a VRM target. Corrections will still be applied.");
                }

                string mode = settings.VrmCorrections.AutoFixFootDirection ? "Auto-fix" : "Manual offsets";
                RetargetApplianceUtil.LogInfo($"VRM corrections enabled for '{result.TargetName}' ({mode})");
            }

            // Bake each source clip
            for (int i = 0; i < sourceClips.Count; i++)
            {
                var clipInfo = sourceClips[i];

                if (!RetargetApplianceUtil.ShowProgress(
                    "Baking Animations",
                    $"[{result.TargetName}] Baking: {clipInfo.ClipName}",
                    (float)i / sourceClips.Count))
                {
                    RetargetApplianceUtil.LogWarning("Baking cancelled by user.");
                    break;
                }

                var bakeResult = BakeSingleClip(
                    result.TargetInstance,
                    clipInfo,
                    result.TargetName,
                    animationsFolder,
                    settings);

                result.ClipResults.Add(bakeResult);

                if (bakeResult.Success)
                {
                    RetargetApplianceUtil.LogInfo($"Baked clip: {clipInfo.ClipName} -> {bakeResult.SavedAssetPath}");
                }
                else
                {
                    RetargetApplianceUtil.LogError($"Failed to bake '{clipInfo.ClipName}': {bakeResult.Error}");
                }
            }

            // Create preview prefab with controller
            if (result.SuccessCount > 0)
            {
                result.PrefabPath = CreatePreviewPrefab(result, targetOutputFolder);
            }

            return result;
        }

        /// <summary>
        /// Bakes a single animation clip onto the target.
        /// CRITICAL: VRM corrections are applied AFTER graph.Evaluate() and BEFORE recording curves.
        /// </summary>
        private static BakeResult BakeSingleClip(
            GameObject targetInstance,
            RetargetApplianceImporter.AnimationClipInfo sourceClipInfo,
            string targetName,
            string outputFolder,
            BakeSettings settings)
        {
            var result = new BakeResult
            {
                SourceClip = sourceClipInfo.Clip,
                TargetName = targetName,
                SourceClipName = sourceClipInfo.ClipName
            };

            try
            {
                AnimationClip sourceClip = sourceClipInfo.Clip;
                Animator animator = targetInstance.GetComponent<Animator>();

                // Create the baked clip
                string bakedClipName = RetargetApplianceUtil.GetBakedClipName(targetName, sourceClipInfo.ClipName);
                result.BakedClip = new AnimationClip
                {
                    name = bakedClipName,
                    frameRate = settings.FPS
                };

                // Calculate frame count
                float clipLength = sourceClip.length;
                int frameCount = Mathf.CeilToInt(clipLength * settings.FPS) + 1;
                float deltaTime = 1f / settings.FPS;

                // Get all transforms to record
                var transforms = new List<Transform>();
                RetargetApplianceUtil.GetAllChildTransforms(targetInstance.transform, transforms);

                // Store initial transforms for reset after baking
                var initialStates = new Dictionary<Transform, TransformState>();
                foreach (var t in transforms)
                {
                    initialStates[t] = new TransformState(t);
                }

                // Create curve dictionaries for each transform
                var curveSets = new Dictionary<Transform, TransformCurves>();
                foreach (var t in transforms)
                {
                    curveSets[t] = new TransformCurves();
                }

                // Create a PlayableGraph to sample the animation
                PlayableGraph graph = PlayableGraph.Create("RetargetBakeGraph");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                var clipPlayable = AnimationClipPlayable.Create(graph, sourceClip);
                var output = AnimationPlayableOutput.Create(graph, "Output", animator);
                output.SetSourcePlayable(clipPlayable);

                // Prepare VRM correction data
                bool applyCorrections = settings.VrmCorrections != null &&
                                        settings.VrmCorrections.EnableCorrections &&
                                        settings.VrmCorrections.HasAnyCorrection();

                bool applyToeStabilization = settings.VrmCorrections != null &&
                                             settings.VrmCorrections.EnableToeStabilization &&
                                             (settings.VrmCorrections.StabilizeRightToe || settings.VrmCorrections.StabilizeLeftToe);

                bool applyToeYawCorrection = settings.VrmCorrections != null &&
                                             settings.VrmCorrections.EnableToeYawCorrection &&
                                             settings.VrmCorrections.ToeYawCorrectionMode != ToeYawCorrectionMode.None &&
                                             (settings.VrmCorrections.CorrectRightToeYaw || settings.VrmCorrections.CorrectLeftToeYaw);

                FootCorrectionData correctionData = null;
                ToeStabilizationData toeStabilizationData = null;
                string logPrefix = $"[RetargetAppliance] [{targetName}]";

                // Diagnostic tracking for toe stabilization
                bool toeStabilizationRan = false;

                // Sample each frame
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = frame * deltaTime;
                    if (time > clipLength)
                        time = clipLength;

                    // STEP 1: Set playable time and evaluate
                    clipPlayable.SetTime(time);
                    graph.Evaluate();

                    // STEP 2: Capture toe neutral poses BEFORE foot corrections (first frame only)
                    // This captures the animation's natural toe pose at t=0
                    if (applyToeStabilization && toeStabilizationData == null)
                    {
                        toeStabilizationData = RetargetApplianceVrmCorrections.CaptureToeNeutralPoses(
                            animator, settings.VrmCorrections, logPrefix);
                    }

                    // STEP 3: Apply VRM foot corrections AFTER sampling, BEFORE recording
                    if (applyCorrections)
                    {
                        if (settings.VrmCorrections.AutoFixFootDirection)
                        {
                            // Compute auto-correction on first frame
                            if (correctionData == null)
                            {
                                correctionData = RetargetApplianceVrmCorrections.ComputeAutoCorrection(
                                    animator, settings.VrmCorrections, logPrefix);
                            }

                            // Apply the pre-computed correction every frame
                            RetargetApplianceVrmCorrections.ApplyCorrection(animator, correctionData, settings.VrmCorrections);
                        }
                        else
                        {
                            // Apply manual Euler offsets
                            RetargetApplianceVrmCorrections.ApplyManualCorrections(animator, settings.VrmCorrections);
                        }
                    }

                    // STEP 4: Apply toe stabilization AFTER foot corrections, BEFORE recording
                    if (applyToeStabilization && toeStabilizationData != null)
                    {
                        toeStabilizationRan = true;
                        // Debug output on first frame only when debug enabled
                        bool debugThisFrame = frame == 0 && settings.VrmCorrections.DebugPrintAlignment;
                        RetargetApplianceVrmCorrections.ApplyToeStabilization(
                            animator, toeStabilizationData, settings.VrmCorrections, debugThisFrame, logPrefix);
                    }

                    // STEP 5: Apply toe yaw correction to ensure foot defines forward direction
                    // This reduces "toe overdrives foot" by clamping or dampening toe yaw
                    if (applyToeYawCorrection)
                    {
                        bool debugThisFrame = frame == 0 && settings.VrmCorrections.DebugPrintAlignment;
                        RetargetApplianceVrmCorrections.ApplyToeYawCorrection(
                            animator, settings.VrmCorrections, debugThisFrame, logPrefix);
                    }

                    // STEP 6: Record all transform states (now with corrections applied)
                    foreach (var t in transforms)
                    {
                        // Skip root if not including root motion
                        if (!settings.IncludeRootMotion && t == targetInstance.transform)
                            continue;

                        var curves = curveSets[t];

                        // Record local position
                        curves.PosX.AddKey(time, t.localPosition.x * settings.ExportScale);
                        curves.PosY.AddKey(time, t.localPosition.y * settings.ExportScale);
                        curves.PosZ.AddKey(time, t.localPosition.z * settings.ExportScale);

                        // Record local rotation as Euler angles (degrees)
                        Vector3 euler = t.localRotation.eulerAngles;

                        // Handle angle continuity to prevent jumps (e.g., 359 -> 1)
                        if (curves.HasPrevEuler)
                        {
                            euler = MakeEulerContinuous(curves.PrevEuler, euler);
                        }
                        curves.PrevEuler = euler;
                        curves.HasPrevEuler = true;

                        curves.EulerX.AddKey(time, euler.x);
                        curves.EulerY.AddKey(time, euler.y);
                        curves.EulerZ.AddKey(time, euler.z);
                    }
                }

                // Clean up the playable graph
                graph.Destroy();

                // Reset the target to initial state
                foreach (var kvp in initialStates)
                {
                    kvp.Value.Apply(kvp.Key);
                }

                // Log property names once per session
                if (!_hasLoggedPropertyNames)
                {
                    RetargetApplianceUtil.LogInfo($"Curve bindings: Position=[{PositionPropertyX}], Rotation=[{RotationPropertyX}]");
                    _hasLoggedPropertyNames = true;
                }

                // Apply curves to the clip
                int toeCurvesWritten = 0;
                foreach (var t in transforms)
                {
                    if (!settings.IncludeRootMotion && t == targetInstance.transform)
                        continue;

                    var curves = curveSets[t];
                    string path = RetargetApplianceUtil.GetTransformPath(targetInstance.transform, t);

                    // Optionally skip static curves
                    if (settings.OptimizeStaticCurves)
                    {
                        if (!IsAnimated(curves.PosX) && !IsAnimated(curves.PosY) && !IsAnimated(curves.PosZ) &&
                            !IsAnimated(curves.EulerX) && !IsAnimated(curves.EulerY) && !IsAnimated(curves.EulerZ))
                        {
                            continue;
                        }
                    }

                    // Track toe curves for diagnostics
                    bool isToeBone = path.Contains("Toes");

                    // Position
                    SetCurve(result.BakedClip, path, PositionPropertyX, curves.PosX);
                    SetCurve(result.BakedClip, path, PositionPropertyY, curves.PosY);
                    SetCurve(result.BakedClip, path, PositionPropertyZ, curves.PosZ);

                    // Rotation
                    SetCurve(result.BakedClip, path, RotationPropertyX, curves.EulerX);
                    SetCurve(result.BakedClip, path, RotationPropertyY, curves.EulerY);
                    SetCurve(result.BakedClip, path, RotationPropertyZ, curves.EulerZ);

                    if (isToeBone)
                    {
                        toeCurvesWritten += 6; // 3 position + 3 rotation curves
                    }
                }

                // Save the baked clip as an asset
                result.SavedAssetPath = $"{outputFolder}/{bakedClipName}.anim";
                AssetDatabase.CreateAsset(result.BakedClip, result.SavedAssetPath);

                // Diagnostic logging for toe stabilization
                RetargetApplianceUtil.LogInfo($"Baked clip {sourceClipInfo.ClipName}: toeCurvesWritten={toeCurvesWritten} toeStabilizationEnabled={applyToeStabilization} toeStabilizationRan={toeStabilizationRan}");
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Debug.LogException(ex);
            }

            return result;
        }

        private static void SetCurve(AnimationClip clip, string path, string propertyName, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static Vector3 MakeEulerContinuous(Vector3 prev, Vector3 current)
        {
            return new Vector3(
                MakeAngleContinuous(prev.x, current.x),
                MakeAngleContinuous(prev.y, current.y),
                MakeAngleContinuous(prev.z, current.z)
            );
        }

        private static float MakeAngleContinuous(float prev, float current)
        {
            float delta = current - prev;

            while (delta > 180f)
            {
                current -= 360f;
                delta = current - prev;
            }
            while (delta < -180f)
            {
                current += 360f;
                delta = current - prev;
            }

            return current;
        }

        private static bool IsAnimated(AnimationCurve curve, float threshold = 0.0001f)
        {
            if (curve.keys.Length < 2)
                return false;

            float firstValue = curve.keys[0].value;
            for (int i = 1; i < curve.keys.Length; i++)
            {
                if (Mathf.Abs(curve.keys[i].value - firstValue) > threshold)
                    return true;
            }

            return false;
        }

        private static string CreatePreviewPrefab(TargetBakeResult bakeResult, string outputFolder)
        {
            try
            {
                string controllerPath = $"{outputFolder}/{bakeResult.TargetName}_Controller.controller";
                var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

                if (controller.layers.Length == 0)
                {
                    controller.AddLayer("Base Layer");
                }

                var rootStateMachine = controller.layers[0].stateMachine;

                foreach (var clipResult in bakeResult.ClipResults)
                {
                    if (!clipResult.Success || clipResult.BakedClip == null)
                        continue;

                    var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipResult.SavedAssetPath);
                    if (savedClip == null)
                        continue;

                    var state = rootStateMachine.AddState(savedClip.name);
                    state.motion = savedClip;
                }

                AssetDatabase.SaveAssets();

                var animator = bakeResult.TargetInstance.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;

                string prefabPath = $"{outputFolder}/{bakeResult.TargetName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(bakeResult.TargetInstance, prefabPath);

                RetargetApplianceUtil.LogInfo($"Created preview prefab: {prefabPath}");
                return prefabPath;
            }
            catch (Exception ex)
            {
                RetargetApplianceUtil.LogError($"Failed to create preview prefab: {ex.Message}");
                return null;
            }
        }

        private class TransformState
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;

            public TransformState(Transform t)
            {
                LocalPosition = t.localPosition;
                LocalRotation = t.localRotation;
                LocalScale = t.localScale;
            }

            public void Apply(Transform t)
            {
                t.localPosition = LocalPosition;
                t.localRotation = LocalRotation;
                t.localScale = LocalScale;
            }
        }

        private class TransformCurves
        {
            public AnimationCurve PosX = new AnimationCurve();
            public AnimationCurve PosY = new AnimationCurve();
            public AnimationCurve PosZ = new AnimationCurve();
            public AnimationCurve EulerX = new AnimationCurve();
            public AnimationCurve EulerY = new AnimationCurve();
            public AnimationCurve EulerZ = new AnimationCurve();
            public Vector3 PrevEuler = Vector3.zero;
            public bool HasPrevEuler = false;
        }

        /// <summary>
        /// Gets all successfully baked clips from a bake result.
        /// </summary>
        public static List<AnimationClip> GetBakedClips(TargetBakeResult result)
        {
            var clips = new List<AnimationClip>();

            foreach (var clipResult in result.ClipResults)
            {
                if (clipResult.Success && clipResult.BakedClip != null)
                {
                    var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipResult.SavedAssetPath);
                    if (savedClip != null)
                    {
                        clips.Add(savedClip);
                    }
                }
            }

            return clips;
        }
    }
}
