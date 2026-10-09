using System;
using CulticVR.WorldStereoTrial;
using UnityEngine;

internal static class Program
{
    private static void Require(bool value, string name)
    {
        if (!value) throw new Exception(name);
    }

    private static void Main()
    {
        const float radius = 0.12f;
        int checks = 0;
        // Analytic planes: rotated walls, ceilings and floors, inward and
        // diagonal lean, far-side destinations and return to open space.
        foreach (var normal in new[] { Vector3.right, Vector3.left, Vector3.up,
            Vector3.down, Vector3.forward, new Vector3(1f, 0f, 1f).normalized })
        {
            var tangent = Vector3.Cross(normal, Math.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.forward);
            foreach (float distance in new[] { 0f, 0.1f, 0.87f, 0.9f, 1f, 2f, 10f, 0.3f, 0f })
            foreach (float slide in new[] { -1f, 0f, 1f })
            {
                var world = new PlaneWorld { Normal = normal, Offset = 1f };
                var desired = normal * distance + tangent * slide;
                Require(HeadCollisionSolver.TryResolve(ref world, Vector3.zero, desired, radius, out var resolved), "clear seed");
                Require(Vector3.Dot(normal, resolved) + radius <= 1.00001f, "head volume remains on near side");
                if (distance + radius < 1f) Require((resolved - desired).sqrMagnitude < 1e-10f, "unobstructed movement unchanged");
                Require(world.Checks == 1 && world.Sweeps <= 1, "bounded query count");
                checks++;
            }
        }
        var embedded = new PlaneWorld { Normal = Vector3.right, Offset = 0.05f };
        Require(!HeadCollisionSolver.TryResolve(ref embedded, Vector3.zero, Vector3.right * 3f, radius, out _), "overlapping start rejected");
        Require(embedded.Sweeps == 0, "never cast from overlap");
        var movingWall = new PlaneWorld { Normal = Vector3.right, Offset = 2f };
        Require(HeadCollisionSolver.TryResolve(ref movingWall, Vector3.zero, Vector3.right, radius, out var before), "initial moving wall");
        movingWall.Offset = 0.6f;
        Require(HeadCollisionSolver.TryResolve(ref movingWall, Vector3.zero, Vector3.right, radius, out var after), "moving wall reaches stationary head");
        Require(before.x == 1f && after.x < 0.48f, "stationary head constrained by moving obstacle");
        Console.WriteLine($"PASS: {checks} directional/lean/return cases; initial overlap, moving obstacle and bounded query checks. Analytic query backend, not Unity runtime physics.");
    }

    private struct PlaneWorld : IHeadCollisionWorld
    {
        internal Vector3 Normal;
        internal float Offset;
        internal int Checks, Sweeps;
        public bool IsClear(Vector3 center, float radius)
        {
            Checks++;
            return Vector3.Dot(Normal, center) + radius < Offset;
        }
        public bool Sweep(Vector3 origin, float radius, Vector3 direction, float distance, out float hitDistance)
        {
            Sweeps++;
            float velocity = Vector3.Dot(Normal, direction);
            hitDistance = velocity > 0f ? (Offset - radius - Vector3.Dot(Normal, origin)) / velocity : float.PositiveInfinity;
            return hitDistance >= 0f && hitDistance <= distance;
        }
    }
}
