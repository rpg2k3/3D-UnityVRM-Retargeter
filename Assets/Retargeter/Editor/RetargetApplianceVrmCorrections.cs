using System;
using System.Collections.Generic;
using UnityEngine;

namespace RetargetAppliance
{
    /// <summary>
    /// VRM bone correction profile presets.
    /// </summary>
    public enum VrmCorrectionProfile
    {
        /// <summary>No corrections applied.</summary>
        None,

        /// <summary>VRoid A: Y-axis correction. LF=(0,-90,0), RF=(0,+90,0).</summary>
        VRoidA_Y90,

        /// <summary>VRoid B: Z-axis correction. LF=(0,0,-90), RF=(0,0,+90).</summary>
        VRoidB_Z90,

        /// <summary>VRoid C: X-axis correction. LF=(-90,0,0), RF=(+90,0,0).</summary>
        VRoidC_X90,

        /// <summary>Custom offsets (user-defined).</summary>
        Custom
    }

    /// <summary>
    /// Toe stabilization mode for VRM rigs.
    /// </summary>
    public enum ToeStabilizationMode
    {
        /// <summary>Dampen toe rotation by blending towards neutral pose.</summary>
        DampenRotation,

        /// <summary>Make toe follow foot rotation (copy foot yaw to toe).</summary>
        ToeFollowsFoot
    }

    /// <summary>
    /// Serializable settings for VRM bone corrections with full XYZ Euler support.
    /// </summary>
    [Serializable]
    public class VrmCorrectionSettings
    {
        /// <summary>Master toggle for VRM corrections.</summary>
        public bool EnableCorrections = true;

        /// <summary>Use automatic foot direction correction based on hips forward.</summary>
        public bool AutoFixFootDirection = true;

        /// <summary>Apply corrections to left foot as well as right foot.</summary>
        public bool CorrectLeftFoot = true;

        /// <summary>Apply corrections to toes.</summary>
        public bool CorrectToes = true;

        /// <summary>Print debug info about foot alignment during baking.</summary>
        public bool DebugPrintAlignment = false;

        /// <summary>Selected correction profile preset (for manual mode).</summary>
        public VrmCorrectionProfile Profile = VrmCorrectionProfile.VRoidA_Y90;

        /// <summary>Left foot Euler offset in degrees (X, Y, Z) - manual mode.</summary>
        public Vector3 LeftFootOffset = new Vector3(0f, -90f, 0f);

        /// <summary>Right foot Euler offset in degrees (X, Y, Z) - manual mode.</summary>
        public Vector3 RightFootOffset = new Vector3(0f, 90f, 0f);

        /// <summary>Left toes Euler offset in degrees (X, Y, Z) - manual mode.</summary>
        public Vector3 LeftToesOffset = Vector3.zero;

        /// <summary>Right toes Euler offset in degrees (X, Y, Z) - manual mode.</summary>
        public Vector3 RightToesOffset = Vector3.zero;

        // === Toe Stabilization Settings ===

        /// <summary>Enable toe stabilization to fix "toe overdrives foot" look.</summary>
        public bool EnableToeStabilization = true;

        /// <summary>Toe stabilization mode.</summary>
        public ToeStabilizationMode ToeStabilizationMode = ToeStabilizationMode.DampenRotation;

        /// <summary>Toe rotation strength (0 = neutral/no rotation, 1 = original animation).</summary>
        public float ToeRotationStrength = 0.25f;

        /// <summary>Apply toe stabilization to right toe.</summary>
        public bool StabilizeRightToe = true;

        /// <summary>Apply toe stabilization to left toe.</summary>
        public bool StabilizeLeftToe = false;

        /// <summary>Creates default settings with auto-fix enabled.</summary>
        public VrmCorrectionSettings()
        {
            AutoFixFootDirection = true;
        }

        /// <summary>Creates a deep copy of these settings.</summary>
        public VrmCorrectionSettings Clone()
        {
            return new VrmCorrectionSettings
            {
                EnableCorrections = this.EnableCorrections,
                AutoFixFootDirection = this.AutoFixFootDirection,
                CorrectLeftFoot = this.CorrectLeftFoot,
                CorrectToes = this.CorrectToes,
                DebugPrintAlignment = this.DebugPrintAlignment,
                Profile = this.Profile,
                LeftFootOffset = this.LeftFootOffset,
                RightFootOffset = this.RightFootOffset,
                LeftToesOffset = this.LeftToesOffset,
                RightToesOffset = this.RightToesOffset,
                // Toe stabilization settings
                EnableToeStabilization = this.EnableToeStabilization,
                ToeStabilizationMode = this.ToeStabilizationMode,
                ToeRotationStrength = this.ToeRotationStrength,
                StabilizeRightToe = this.StabilizeRightToe,
                StabilizeLeftToe = this.StabilizeLeftToe
            };
        }

        /// <summary>Applies a profile preset, updating all offset values.</summary>
        public void ApplyProfile(VrmCorrectionProfile profile)
        {
            Profile = profile;

            switch (profile)
            {
                case VrmCorrectionProfile.None:
                    LeftFootOffset = Vector3.zero;
                    RightFootOffset = Vector3.zero;
                    LeftToesOffset = Vector3.zero;
                    RightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.VRoidA_Y90:
                    LeftFootOffset = new Vector3(0f, -90f, 0f);
                    RightFootOffset = new Vector3(0f, 90f, 0f);
                    LeftToesOffset = Vector3.zero;
                    RightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.VRoidB_Z90:
                    LeftFootOffset = new Vector3(0f, 0f, -90f);
                    RightFootOffset = new Vector3(0f, 0f, 90f);
                    LeftToesOffset = Vector3.zero;
                    RightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.VRoidC_X90:
                    LeftFootOffset = new Vector3(-90f, 0f, 0f);
                    RightFootOffset = new Vector3(90f, 0f, 0f);
                    LeftToesOffset = Vector3.zero;
                    RightToesOffset = Vector3.zero;
                    break;

                case VrmCorrectionProfile.Custom:
                    // Don't change offsets for custom
                    break;
            }
        }

        /// <summary>Returns true if any correction is active.</summary>
        public bool HasAnyCorrection()
        {
            if (AutoFixFootDirection)
                return true;

            return LeftFootOffset.sqrMagnitude > 0.001f ||
                   RightFootOffset.sqrMagnitude > 0.001f ||
                   LeftToesOffset.sqrMagnitude > 0.001f ||
                   RightToesOffset.sqrMagnitude > 0.001f;
        }
    }

    /// <summary>
    /// Holds computed auto-correction data for a single target.
    /// Computed once on first frame, then applied every frame during baking.
    /// </summary>
    public class FootCorrectionData
    {
        public bool IsComputed = false;

        // Auto-computed yaw corrections (world space rotation around up axis)
        public Quaternion RightFootCorrection = Quaternion.identity;
        public Quaternion LeftFootCorrection = Quaternion.identity;
        public Quaternion RightToesCorrection = Quaternion.identity;
        public Quaternion LeftToesCorrection = Quaternion.identity;

        public bool HasRightToes = false;
        public bool HasLeftToes = false;

        // Debug info
        public float RightFootYaw = 0f;
        public float LeftFootYaw = 0f;
        public float RightFootDot = 0f;
        public float LeftFootDot = 0f;
    }

    /// <summary>
    /// Holds neutral toe poses for toe stabilization.
    /// Captured once at the start of baking.
    /// </summary>
    public class ToeStabilizationData
    {
        public bool IsCaptured = false;

        // Neutral local rotations (from bind/rest pose at time=0)
        public Quaternion RightToeNeutral = Quaternion.identity;
        public Quaternion LeftToeNeutral = Quaternion.identity;

        // Foot local rotations for "toe follows foot" mode
        public Quaternion RightFootNeutral = Quaternion.identity;
        public Quaternion LeftFootNeutral = Quaternion.identity;

        public bool HasRightToes = false;
        public bool HasLeftToes = false;
    }

    /// <summary>
    /// Provides VRM-aware bone corrections for retargeted animations.
    /// Addresses axis misalignment issues common in VRoid/UniVRM models.
    /// </summary>
    public static class RetargetApplianceVrmCorrections
    {
        // Known VRM component type names (UniVRM 0.x and 1.x)
        private static readonly string[] VrmComponentTypeNames = new[]
        {
            "VRM.VRMMeta",
            "UniVRM10.Vrm10Instance",
            "VRM10.Vrm10Instance",
            "UniGLTF.RuntimeGltfInstance",
            "VRMShaders.VRMMaterialDescriptorGenerator"
        };

        /// <summary>
        /// Checks if a GameObject is a VRM target by looking for VRM-related components.
        /// </summary>
        public static bool IsVRMTarget(GameObject root)
        {
            if (root == null)
                return false;

            var allComponents = root.GetComponentsInChildren<Component>(true);

            foreach (var component in allComponents)
            {
                if (component == null)
                    continue;

                string typeName = component.GetType().FullName;

                foreach (var vrmTypeName in VrmComponentTypeNames)
                {
                    if (typeName.Contains(vrmTypeName) || typeName.Contains("VRM"))
                    {
                        return true;
                    }
                }
            }

            string rootName = root.name.ToLowerInvariant();
            if (rootName.Contains("vrm") || rootName.Contains("vroid"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Computes auto-yaw correction data by comparing foot forward to hips forward.
        /// Call this AFTER graph.Evaluate() on the first frame to get the correction values.
        /// </summary>
        public static FootCorrectionData ComputeAutoCorrection(Animator animator, VrmCorrectionSettings settings, string logPrefix = null)
        {
            var data = new FootCorrectionData();

            if (animator == null || !animator.isHuman)
            {
                data.IsComputed = true;
                return data;
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            Transform leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);

            data.HasRightToes = rightToes != null;
            data.HasLeftToes = leftToes != null;

            if (hips == null)
            {
                LogWarning(logPrefix, "Cannot compute auto-correction: Hips bone not found.");
                data.IsComputed = true;
                return data;
            }

            // Get hips forward projected onto ground plane
            Vector3 hipsForward = Vector3.ProjectOnPlane(hips.forward, Vector3.up).normalized;

            // Compute right foot correction
            if (rightFoot != null)
            {
                Vector3 footForward = Vector3.ProjectOnPlane(rightFoot.forward, Vector3.up).normalized;
                data.RightFootDot = Vector3.Dot(footForward, hipsForward);
                data.RightFootYaw = Vector3.SignedAngle(footForward, hipsForward, Vector3.up);

                // Create correction quaternion (world space rotation around up)
                data.RightFootCorrection = Quaternion.AngleAxis(data.RightFootYaw, Vector3.up);

                // Same correction for toes
                if (data.HasRightToes && settings.CorrectToes)
                {
                    data.RightToesCorrection = data.RightFootCorrection;
                }
            }

            // Compute left foot correction
            if (leftFoot != null && settings.CorrectLeftFoot)
            {
                Vector3 footForward = Vector3.ProjectOnPlane(leftFoot.forward, Vector3.up).normalized;
                data.LeftFootDot = Vector3.Dot(footForward, hipsForward);
                data.LeftFootYaw = Vector3.SignedAngle(footForward, hipsForward, Vector3.up);

                data.LeftFootCorrection = Quaternion.AngleAxis(data.LeftFootYaw, Vector3.up);

                if (data.HasLeftToes && settings.CorrectToes)
                {
                    data.LeftToesCorrection = data.LeftFootCorrection;
                }
            }

            // Debug logging
            if (settings.DebugPrintAlignment)
            {
                string prefix = string.IsNullOrEmpty(logPrefix) ? "[RetargetAppliance]" : logPrefix;
                Debug.Log($"{prefix} === Auto-Correction Debug ===");
                Debug.Log($"{prefix} Hips Forward (ground): {hipsForward:F3}");
                Debug.Log($"{prefix} RightFoot: dot={data.RightFootDot:F3}, yaw={data.RightFootYaw:F1}deg");
                Debug.Log($"{prefix} LeftFoot: dot={data.LeftFootDot:F3}, yaw={data.LeftFootYaw:F1}deg");
                Debug.Log($"{prefix} HasRightToes: {data.HasRightToes}, HasLeftToes: {data.HasLeftToes}");
                Debug.Log($"{prefix} ========================");
            }

            data.IsComputed = true;
            return data;
        }

        /// <summary>
        /// Applies the pre-computed correction to the current pose.
        /// Call this AFTER graph.Evaluate() and BEFORE recording curves.
        /// Uses WORLD space rotation to avoid local axis issues.
        /// </summary>
        public static void ApplyCorrection(Animator animator, FootCorrectionData correction, VrmCorrectionSettings settings)
        {
            if (animator == null || !animator.isHuman || correction == null || !correction.IsComputed)
                return;

            if (!settings.EnableCorrections)
                return;

            // Apply right foot correction (world space)
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (rightFoot != null && correction.RightFootCorrection != Quaternion.identity)
            {
                rightFoot.rotation = correction.RightFootCorrection * rightFoot.rotation;
            }

            // Apply right toes correction
            if (settings.CorrectToes && correction.HasRightToes)
            {
                Transform rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
                if (rightToes != null && correction.RightToesCorrection != Quaternion.identity)
                {
                    rightToes.rotation = correction.RightToesCorrection * rightToes.rotation;
                }
            }

            // Apply left foot correction
            if (settings.CorrectLeftFoot)
            {
                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (leftFoot != null && correction.LeftFootCorrection != Quaternion.identity)
                {
                    leftFoot.rotation = correction.LeftFootCorrection * leftFoot.rotation;
                }

                // Apply left toes correction
                if (settings.CorrectToes && correction.HasLeftToes)
                {
                    Transform leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                    if (leftToes != null && correction.LeftToesCorrection != Quaternion.identity)
                    {
                        leftToes.rotation = correction.LeftToesCorrection * leftToes.rotation;
                    }
                }
            }
        }

        /// <summary>
        /// Applies manual Euler offset corrections (fallback when auto-fix is disabled).
        /// Call this AFTER graph.Evaluate() and BEFORE recording curves.
        /// </summary>
        public static void ApplyManualCorrections(Animator animator, VrmCorrectionSettings settings)
        {
            if (animator == null || !animator.isHuman || settings == null)
                return;

            if (!settings.EnableCorrections || settings.AutoFixFootDirection)
                return;

            // Apply right foot
            ApplyEulerOffset(animator, HumanBodyBones.RightFoot, settings.RightFootOffset);

            // Apply left foot
            if (settings.CorrectLeftFoot)
            {
                ApplyEulerOffset(animator, HumanBodyBones.LeftFoot, settings.LeftFootOffset);
            }

            // Apply toes
            if (settings.CorrectToes)
            {
                ApplyEulerOffset(animator, HumanBodyBones.RightToes, settings.RightToesOffset);
                if (settings.CorrectLeftFoot)
                {
                    ApplyEulerOffset(animator, HumanBodyBones.LeftToes, settings.LeftToesOffset);
                }
            }
        }

        private static void ApplyEulerOffset(Animator animator, HumanBodyBones bone, Vector3 eulerDegrees)
        {
            if (eulerDegrees.sqrMagnitude < 0.001f)
                return;

            Transform t = animator.GetBoneTransform(bone);
            if (t == null)
                return;

            Quaternion offsetRotation = Quaternion.Euler(eulerDegrees);
            t.localRotation = offsetRotation * t.localRotation;
        }

        /// <summary>
        /// Prints foot forward vectors for debugging axis alignment.
        /// </summary>
        public static void PrintFootForwardVectors(Animator animator, string targetName)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.Log($"[RetargetAppliance] [{targetName}] Cannot print foot vectors: Invalid animator.");
                return;
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            Debug.Log($"[RetargetAppliance] [{targetName}] === Foot Forward Debug ===");

            if (hips != null)
            {
                Vector3 hipsForward = Vector3.ProjectOnPlane(hips.forward, Vector3.up).normalized;
                Debug.Log($"[RetargetAppliance] [{targetName}] Hips Forward (ground plane): {hipsForward:F3}");
            }

            if (rightFoot != null)
            {
                Vector3 footForward = Vector3.ProjectOnPlane(rightFoot.forward, Vector3.up).normalized;
                float yaw = hips != null ? Vector3.SignedAngle(footForward, Vector3.ProjectOnPlane(hips.forward, Vector3.up).normalized, Vector3.up) : 0;
                Debug.Log($"[RetargetAppliance] [{targetName}] RightFoot Forward: {footForward:F3}, Yaw to hips: {yaw:F1}deg");
            }

            if (leftFoot != null)
            {
                Vector3 footForward = Vector3.ProjectOnPlane(leftFoot.forward, Vector3.up).normalized;
                float yaw = hips != null ? Vector3.SignedAngle(footForward, Vector3.ProjectOnPlane(hips.forward, Vector3.up).normalized, Vector3.up) : 0;
                Debug.Log($"[RetargetAppliance] [{targetName}] LeftFoot Forward: {footForward:F3}, Yaw to hips: {yaw:F1}deg");
            }

            Debug.Log($"[RetargetAppliance] [{targetName}] === End Debug ===");
        }

        /// <summary>
        /// Captures neutral toe poses for toe stabilization.
        /// Call this AFTER graph.Evaluate() on the first frame.
        /// </summary>
        public static ToeStabilizationData CaptureToeNeutralPoses(Animator animator, VrmCorrectionSettings settings, string logPrefix = null)
        {
            var data = new ToeStabilizationData();

            if (animator == null || !animator.isHuman)
            {
                data.IsCaptured = true;
                return data;
            }

            Transform rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            Transform leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            data.HasRightToes = rightToes != null;
            data.HasLeftToes = leftToes != null;

            // Capture neutral rotations
            if (rightToes != null)
            {
                data.RightToeNeutral = rightToes.localRotation;
            }
            if (leftToes != null)
            {
                data.LeftToeNeutral = leftToes.localRotation;
            }
            if (rightFoot != null)
            {
                data.RightFootNeutral = rightFoot.localRotation;
            }
            if (leftFoot != null)
            {
                data.LeftFootNeutral = leftFoot.localRotation;
            }

            if (settings.DebugPrintAlignment)
            {
                string prefix = string.IsNullOrEmpty(logPrefix) ? "[RetargetAppliance]" : logPrefix;
                Debug.Log($"{prefix} === Toe Stabilization Data Captured ===");
                Debug.Log($"{prefix} HasRightToes: {data.HasRightToes}, HasLeftToes: {data.HasLeftToes}");
                if (data.HasRightToes)
                    Debug.Log($"{prefix} RightToeNeutral: {data.RightToeNeutral.eulerAngles:F1}");
                if (data.HasLeftToes)
                    Debug.Log($"{prefix} LeftToeNeutral: {data.LeftToeNeutral.eulerAngles:F1}");
                Debug.Log($"{prefix} =====================================");
            }

            data.IsCaptured = true;
            return data;
        }

        /// <summary>
        /// Applies toe stabilization to reduce "toe overdrives foot" effect.
        /// Call this AFTER foot corrections and BEFORE recording curves.
        /// </summary>
        public static void ApplyToeStabilization(
            Animator animator,
            ToeStabilizationData toeData,
            VrmCorrectionSettings settings,
            bool debugThisFrame = false,
            string logPrefix = null)
        {
            if (animator == null || !animator.isHuman || toeData == null || !toeData.IsCaptured)
                return;

            if (!settings.EnableToeStabilization)
                return;

            float strength = Mathf.Clamp01(settings.ToeRotationStrength);
            string prefix = string.IsNullOrEmpty(logPrefix) ? "[RetargetAppliance]" : logPrefix;

            // Apply to right toe
            if (settings.StabilizeRightToe && toeData.HasRightToes)
            {
                Transform rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

                if (rightToes != null)
                {
                    Quaternion originalRot = rightToes.localRotation;
                    Quaternion newRot;

                    if (settings.ToeStabilizationMode == ToeStabilizationMode.DampenRotation)
                    {
                        // Mode A: Dampen - blend between neutral and animated
                        newRot = Quaternion.Slerp(toeData.RightToeNeutral, originalRot, strength);
                    }
                    else // ToeFollowsFoot
                    {
                        // Mode B: Toe follows foot
                        // Copy foot's yaw to toe, relative to neutral
                        if (rightFoot != null)
                        {
                            Quaternion footDelta = Quaternion.Inverse(toeData.RightFootNeutral) * rightFoot.localRotation;
                            float footYaw = ExtractYaw(footDelta);
                            Quaternion yawOnly = Quaternion.Euler(0f, footYaw, 0f);
                            Quaternion targetRot = yawOnly * toeData.RightToeNeutral;
                            newRot = Quaternion.Slerp(toeData.RightToeNeutral, targetRot, strength);
                        }
                        else
                        {
                            newRot = toeData.RightToeNeutral;
                        }
                    }

                    rightToes.localRotation = newRot;

                    if (debugThisFrame)
                    {
                        Debug.Log($"{prefix} RightToe: original={originalRot.eulerAngles:F1}, new={newRot.eulerAngles:F1}, strength={strength:F2}");
                    }
                }
            }

            // Apply to left toe
            if (settings.StabilizeLeftToe && toeData.HasLeftToes)
            {
                Transform leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

                if (leftToes != null)
                {
                    Quaternion originalRot = leftToes.localRotation;
                    Quaternion newRot;

                    if (settings.ToeStabilizationMode == ToeStabilizationMode.DampenRotation)
                    {
                        // Mode A: Dampen - blend between neutral and animated
                        newRot = Quaternion.Slerp(toeData.LeftToeNeutral, originalRot, strength);
                    }
                    else // ToeFollowsFoot
                    {
                        // Mode B: Toe follows foot
                        if (leftFoot != null)
                        {
                            Quaternion footDelta = Quaternion.Inverse(toeData.LeftFootNeutral) * leftFoot.localRotation;
                            float footYaw = ExtractYaw(footDelta);
                            Quaternion yawOnly = Quaternion.Euler(0f, footYaw, 0f);
                            Quaternion targetRot = yawOnly * toeData.LeftToeNeutral;
                            newRot = Quaternion.Slerp(toeData.LeftToeNeutral, targetRot, strength);
                        }
                        else
                        {
                            newRot = toeData.LeftToeNeutral;
                        }
                    }

                    leftToes.localRotation = newRot;

                    if (debugThisFrame)
                    {
                        Debug.Log($"{prefix} LeftToe: original={originalRot.eulerAngles:F1}, new={newRot.eulerAngles:F1}, strength={strength:F2}");
                    }
                }
            }
        }

        /// <summary>
        /// Extracts yaw (Y-axis rotation) from a quaternion in degrees.
        /// </summary>
        private static float ExtractYaw(Quaternion q)
        {
            Vector3 euler = q.eulerAngles;
            return euler.y;
        }

        private static void LogWarning(string prefix, string message)
        {
            string fullPrefix = string.IsNullOrEmpty(prefix) ? "[RetargetAppliance]" : prefix;
            Debug.LogWarning($"{fullPrefix} {message}");
        }
    }
}
