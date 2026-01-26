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
        /// <summary>
        /// Settings for the baking process.
        /// </summary>
        public class BakeSettings
        {
            public int FPS = 30;
            public bool IncludeRootMotion = false;
            public float ExportScale = 1f;
            public bool OptimizeStaticCurves = true;
        }

        /// <summary>
        /// Result of baking a single clip.
        /// </summary>
        public class BakeResult
        {
            public AnimationClip SourceClip;
            public AnimationClip BakedClip;
            public string TargetName;
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
                    RetargetApplianceUtil.LogInfo($"Baked: {bakeResult.BakedClip.name}");
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
                TargetName = targetName
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

                // Store initial transforms for optimization
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

                // Sample each frame
                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = frame * deltaTime;
                    if (time > clipLength)
                        time = clipLength;

                    // Set playable time and evaluate
                    clipPlayable.SetTime(time);
                    graph.Evaluate();

                    // Record all transform states
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

                        // Record local rotation
                        curves.RotX.AddKey(time, t.localRotation.x);
                        curves.RotY.AddKey(time, t.localRotation.y);
                        curves.RotZ.AddKey(time, t.localRotation.z);
                        curves.RotW.AddKey(time, t.localRotation.w);

                        // Note: Scale curves intentionally omitted to avoid FBX export warnings
                        // ("no mapping from Unity 'localScale.z' to fbx property")
                    }
                }

                // Clean up the playable graph
                graph.Destroy();

                // Reset the target to initial state
                foreach (var kvp in initialStates)
                {
                    kvp.Value.Apply(kvp.Key);
                }

                // Apply curves to the clip
                foreach (var t in transforms)
                {
                    if (!settings.IncludeRootMotion && t == targetInstance.transform)
                        continue;

                    var curves = curveSets[t];
                    string path = RetargetApplianceUtil.GetTransformPath(targetInstance.transform, t);

                    // Optionally skip static curves
                    if (settings.OptimizeStaticCurves)
                    {
                        var initial = initialStates[t];

                        if (!IsAnimated(curves.PosX) && !IsAnimated(curves.PosY) && !IsAnimated(curves.PosZ) &&
                            !IsAnimated(curves.RotX) && !IsAnimated(curves.RotY) && !IsAnimated(curves.RotZ) && !IsAnimated(curves.RotW))
                        {
                            // Transform never changes, skip it
                            continue;
                        }
                    }

                    // Position
                    SetCurve(result.BakedClip, path, "localPosition.x", curves.PosX);
                    SetCurve(result.BakedClip, path, "localPosition.y", curves.PosY);
                    SetCurve(result.BakedClip, path, "localPosition.z", curves.PosZ);

                    // Rotation (using localRotation quaternion)
                    SetCurve(result.BakedClip, path, "localRotation.x", curves.RotX);
                    SetCurve(result.BakedClip, path, "localRotation.y", curves.RotY);
                    SetCurve(result.BakedClip, path, "localRotation.z", curves.RotZ);
                    SetCurve(result.BakedClip, path, "localRotation.w", curves.RotW);

                    // Note: Scale curves not exported - Unity FBX Exporter has no mapping for localScale
                }

                // Ensure settings are correct
                result.BakedClip.EnsureQuaternionContinuity();

                // Save the baked clip as an asset
                result.SavedAssetPath = $"{outputFolder}/{bakedClipName}.anim";
                AssetDatabase.CreateAsset(result.BakedClip, result.SavedAssetPath);

                RetargetApplianceUtil.LogInfo($"Saved baked clip: {result.SavedAssetPath}");
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Debug.LogException(ex);
            }

            return result;
        }

        /// <summary>
        /// Sets a curve on an animation clip using EditorCurveBinding.
        /// </summary>
        private static void SetCurve(AnimationClip clip, string path, string propertyName, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        /// <summary>
        /// Checks if a curve has any actual animation (values change over time).
        /// </summary>
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

        /// <summary>
        /// Creates a preview prefab with an AnimatorController for testing.
        /// </summary>
        private static string CreatePreviewPrefab(TargetBakeResult bakeResult, string outputFolder)
        {
            try
            {
                // Create animator controller
                string controllerPath = $"{outputFolder}/{bakeResult.TargetName}_Controller.controller";
                var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

                // Add layer if needed
                if (controller.layers.Length == 0)
                {
                    controller.AddLayer("Base Layer");
                }

                var rootStateMachine = controller.layers[0].stateMachine;

                // Add states for each baked clip
                foreach (var clipResult in bakeResult.ClipResults)
                {
                    if (!clipResult.Success || clipResult.BakedClip == null)
                        continue;

                    // Reload the clip from the asset
                    var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipResult.SavedAssetPath);
                    if (savedClip == null)
                        continue;

                    var state = rootStateMachine.AddState(savedClip.name);
                    state.motion = savedClip;
                }

                AssetDatabase.SaveAssets();

                // Assign controller to the instance
                var animator = bakeResult.TargetInstance.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;

                // Save as prefab
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

        /// <summary>
        /// Helper class to store transform state.
        /// </summary>
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

        /// <summary>
        /// Helper class to store animation curves for a transform.
        /// Note: Scale curves intentionally omitted - Unity FBX Exporter has no mapping for localScale.
        /// </summary>
        private class TransformCurves
        {
            public AnimationCurve PosX = new AnimationCurve();
            public AnimationCurve PosY = new AnimationCurve();
            public AnimationCurve PosZ = new AnimationCurve();
            public AnimationCurve RotX = new AnimationCurve();
            public AnimationCurve RotY = new AnimationCurve();
            public AnimationCurve RotZ = new AnimationCurve();
            public AnimationCurve RotW = new AnimationCurve();
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
                    // Load from saved asset to ensure we have the persisted version
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
