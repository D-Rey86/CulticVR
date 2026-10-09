using System;

namespace CulticVR.AimPreview
{
    internal static class WristPointerBounds
    {
        internal const float BackgroundSize = 310f;
        internal const float PointerSize = 18f;
        // Inset by the pointer's half diagonal so its WHOLE square, including
        // corners, remains inside the wheel's visible circular footprint.
        internal static readonly float Radius = BackgroundSize * 0.5f -
            PointerSize * 0.5f * (float)Math.Sqrt(2.0);

        internal static bool TryClamp(float x, float y, out float boundedX, out float boundedY)
        {
            boundedX = boundedY = 0f;
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return false;
            // Double intermediates avoid overflow even for a very distant ray.
            var lengthSquared = (double)x * x + (double)y * y;
            var scale = lengthSquared > (double)Radius * Radius ? Radius / Math.Sqrt(lengthSquared) : 1.0;
            boundedX = (float)(x * scale);
            boundedY = (float)(y * scale);
            return true;
        }
    }
}
