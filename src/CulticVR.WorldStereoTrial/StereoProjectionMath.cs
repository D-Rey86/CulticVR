using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    internal static class StereoProjectionMath
    {
        // Pass an untouched runtime matrix, never the last scaled output.
        internal static bool TryScale(Matrix4x4 runtimeProjection, float scale, out Matrix4x4 result)
        {
            result = runtimeProjection;
            if (!Finite(scale) || scale <= 0f) return false;
            for (var index = 0; index < 16; index++)
                if (!Finite(runtimeProjection[index])) return false;
            if (runtimeProjection.m00 == 0f || runtimeProjection.m11 == 0f) return false;
            result.m00 = runtimeProjection.m00 * scale;
            result.m11 = runtimeProjection.m11 * scale;
            return Finite(result.m00) && Finite(result.m11);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
