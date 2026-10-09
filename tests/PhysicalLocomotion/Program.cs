using System;
using UnityEngine;
using CulticVR.WorldStereoTrial;

static class Program
{
    static void Require(bool ok, string name) { if (!ok) throw new Exception(name); }
    static void Main()
    {
        Require(!PhysicalLocomotionMath.CrouchRequested(false, 0.24f, 0.25f, 0.10f), "standing noise does not crouch");
        Require(PhysicalLocomotionMath.CrouchRequested(false, 0.26f, 0.25f, 0.10f), "crouch entry");
        Require(PhysicalLocomotionMath.CrouchRequested(true, 0.20f, 0.25f, 0.10f), "hysteresis holds");
        Require(!PhysicalLocomotionMath.CrouchRequested(true, 0.14f, 0.25f, 0.10f), "stand release");
        int cases = 0;
        foreach (float native in new[] { 0f, 0.2f, 0.931f, 1.1f })
        foreach (float physical in new[] { -0.2f, 0f, 0.1f, 0.5f, 0.931f, 1.2f })
        {
            float residual = PhysicalLocomotionMath.CrouchResidual(-physical, native);
            float rendered = -native + residual;
            Require(Math.Abs(rendered - (physical >= 0f ? -Math.Max(native, physical) : -native - physical)) < 0.00001f,
                "native and physical lowering not doubled");
            cases++;
        }
        for (int degrees = 0; degrees < 360; degrees += 15)
        foreach (float acceptedFraction in new[] { 0f, 0.1f, 0.5f, 1f })
        {
            float halfAngle = degrees * (float)Math.PI / 360f;
            var yaw = new Quaternion(0f, (float)Math.Sin(halfAngle), 0f, (float)Math.Cos(halfAngle));
            var head = new Vector3(0.4f, -0.3f, 0.2f);
            var request = PhysicalLocomotionMath.BodyRequest(head, yaw, 0.08f);
            Require(Math.Abs(request.y) < 0.000001f, "roomscale never injects vertical body motion");
            var accepted = request * acceptedFraction;
            var origin = new Quaternion(-yaw.x, -yaw.y, -yaw.z, yaw.w) * accepted;
            var after = accepted + yaw * (head - origin);
            Require((after - yaw * head).sqrMagnitude < 1e-10f, "body plus residual conserves head position under yaw/collision");
            cases++;
        }
        Require(PhysicalLocomotionMath.BodyRequest(new Vector3(0.02f, -1f, 0.01f), Quaternion.identity, 0.08f) == Vector3.zero,
            "small lean and pure crouch do not move body");
        Console.WriteLine($"PASS {cases} lowering/conservation cases plus crouch hysteresis and body deadzone. Not runtime physics evidence.");
    }
}
