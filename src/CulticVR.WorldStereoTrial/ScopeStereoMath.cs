using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    public static class ScopeStereoMath
    {
        // Only the additional magnification beyond the accepted normal scope
        // is compensated. Never use a fixed IPD, eye axis, or headset FOV.
        public static float SeparationScale(float magnification, float normalMagnification)
        {
            // Identity roundoff guard, not an optical calibration. Without it,
            // a final Lerp ULP could keep native view overrides alive at normal
            // zoom indefinitely. The recorded restored state exercises this.
            if (!Finite(magnification) || !Finite(normalMagnification) ||
                normalMagnification <= 1f || magnification <= normalMagnification * 1.00001f) return 1f;
            return normalMagnification / magnification;
        }

        public static bool TryGetLocalView(Matrix4x4 runtimeView, Matrix4x4 runtimeLocalToWorld,
            out Matrix4x4 localView)
        {
            localView = default;
            if (!Valid(runtimeView) || !Valid(runtimeLocalToWorld)) return false;
            localView = runtimeView * runtimeLocalToWorld;
            return Valid(localView);
        }

        // Change only the other eye's origin toward the sighting eye. Its full
        // measured basis (including eye cant) stays intact; the sighting view
        // is copied exactly. The two views remain distinct, not monoscopic.
        public static bool TryLimitSeparation(Matrix4x4 left, Matrix4x4 right, float scale,
            bool sightWithLeft, out Matrix4x4 limitedLeft, out Matrix4x4 limitedRight)
        {
            limitedLeft = left;
            limitedRight = right;
            if (!Finite(scale) || scale <= 0f || scale > 1f ||
                !ScopeSightingMath.TryGetEyeOrigin(left, out var leftEye) ||
                !ScopeSightingMath.TryGetEyeOrigin(right, out var rightEye)) return false;
            if (scale == 1f) return true;
            if (sightWithLeft)
                limitedRight = WithEyeOrigin(right, leftEye + (rightEye - leftEye) * scale);
            else
                limitedLeft = WithEyeOrigin(left, rightEye + (leftEye - rightEye) * scale);
            return Valid(limitedLeft) && Valid(limitedRight);
        }

        private static Matrix4x4 WithEyeOrigin(Matrix4x4 view, Vector3 eye)
        {
            view.m03 = -(view.m00 * eye.x + view.m01 * eye.y + view.m02 * eye.z);
            view.m13 = -(view.m10 * eye.x + view.m11 * eye.y + view.m12 * eye.z);
            view.m23 = -(view.m20 * eye.x + view.m21 * eye.y + view.m22 * eye.z);
            return view;
        }

        public static bool Valid(Matrix4x4 matrix)
        {
            for (var n = 0; n < 16; n++) if (!Finite(matrix[n])) return false;
            return matrix.m30 == 0f && matrix.m31 == 0f && matrix.m32 == 0f && matrix.m33 == 1f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
