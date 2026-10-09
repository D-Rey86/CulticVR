using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    internal interface IHeadCollisionWorld
    {
        bool IsClear(Vector3 center, float radius);
        bool Sweep(Vector3 origin, float radius, Vector3 direction, float distance, out float hitDistance);
    }

    internal static class HeadCollisionSolver
    {
        internal const float Skin = 0.005f;

        // Re-sweep from the native body each frame, not from the last rendered
        // head. Leaning beyond a thin wall must never let the camera emerge on
        // its far side, and moving obstacles must be checked even at rest.
        internal static bool TryResolve<T>(ref T world, Vector3 anchor, Vector3 desired,
            float radius, out Vector3 resolved) where T : struct, IHeadCollisionWorld
        {
            resolved = anchor;
            // SphereCast alone does not report colliders overlapping its start.
            if (!world.IsClear(anchor, radius)) return false;
            var delta = desired - anchor;
            var distance = delta.magnitude;
            if (distance <= 0.00001f) return true;
            var direction = delta / distance;
            if (world.Sweep(anchor, radius, direction, distance, out var hitDistance))
                resolved = anchor + direction * Mathf.Clamp(hitDistance - Skin, 0f, distance);
            else
                resolved = desired;
            return true;
        }
    }
}
