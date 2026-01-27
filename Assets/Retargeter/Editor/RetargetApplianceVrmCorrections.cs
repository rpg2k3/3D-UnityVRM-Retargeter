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
                RightToesOffset = this.RightToesOffset
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

        private static void LogWarning(string prefix, string message)
        {
            string fullPrefix = string.IsNullOrEmpty(prefix) ? "[RetargetAppliance]" : prefix;
            Debug.LogWarning($"{fullPrefix} {message}");
        }
    }
}
