using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    internal static class PhysicalLocomotionMath
    {
        internal static bool CrouchRequested(bool wasCrouched, float drop, float threshold, float hysteresis) =>
            drop >= (wasCrouched ? Mathf.Max(0.01f, threshold - hysteresis) : threshold);

        // Native button/ceiling crouch and physical lowering must not add up.
        // Final drop is max(native, physical), not native + physical.
        internal static float CrouchResidual(float heightDelta, float nativeCrouchDrop) =>
            heightDelta + Mathf.Min(Mathf.Max(0f, nativeCrouchDrop), Mathf.Max(0f, -heightDelta));

        internal static Vector3 BodyRequest(Vector3 trackingResidual, Quaternion trackingYaw, float deadzone)
        {
            trackingResidual.y = 0f;
            float length = trackingResidual.magnitude;
            if (length <= deadzone + 0.001f) return Vector3.zero;
            return trackingYaw * (trackingResidual * ((length - deadzone) / length));
        }
    }
}
