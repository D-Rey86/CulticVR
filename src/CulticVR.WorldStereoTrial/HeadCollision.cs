using BepInEx.Configuration;
using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    public sealed partial class WorldStereoTrialPlugin
    {
        private ConfigEntry<bool> _headCollisionEnabled = null!;
        private ConfigEntry<float> _headCollisionRadius = null!;
        private bool _hasSafeHeadPosition;
        private Vector3 _safeHeadPosition;
        private Vector3 _lastCollisionBodyCenter;
        private CapsuleCollider? _headCollisionBody;

        private void InitializeHeadCollision()
        {
            _headCollisionEnabled = Config.Bind("HeadCollision", "Enabled", true,
                "Stop room-scale head movement at solid world geometry. Does not move the player body.");
            _headCollisionRadius = Config.Bind("HeadCollision", "Radius", 0.12f,
                new ConfigDescription("Head collision radius in game metres (comfort clearance, not a tracking scale).",
                    new AcceptableValueRange<float>(0.06f, 0.2f)));
        }

        private void ResetHeadCollision()
        {
            _hasSafeHeadPosition = false;
            _headCollisionBody = null;
        }

        private Vector3 ConstrainHeadPosition(Vector3 desired)
        {
            var player = _player;
            var body = player != null ? player.myCollider : null;
            if (!_headCollisionEnabled.Value || body == null || !body.enabled ||
                player!.isDead || player.state == 69)
            {
                ResetHeadCollision();
                return desired;
            }

            // Native crouch changes capsule height/center live. Use its actual
            // geometry, including scale/direction, not a hardcoded eye height.
            var transform = body.transform;
            var scale = transform.lossyScale;
            int axial = body.direction;
            int radialA = (axial + 1) % 3;
            int radialB = (axial + 2) % 3;
            var bodyRadius = body.radius * Mathf.Max(Mathf.Abs(scale[radialA]), Mathf.Abs(scale[radialB]));
            var segment = Mathf.Max(0f, body.height * Mathf.Abs(scale[axial]) * 0.5f - bodyRadius);
            var localAxis = Vector3.zero;
            localAxis[axial] = 1f;
            var axis = transform.TransformDirection(localAxis);
            var center = transform.TransformPoint(body.center);
            // Teleports/respawns must not pin the camera to a safe point from
            // the old location if the new capsule temporarily overlaps a solid.
            if (_headCollisionBody != body || (center - _lastCollisionBodyCenter).sqrMagnitude > 1f)
                _hasSafeHeadPosition = false;
            _headCollisionBody = body;
            _lastCollisionBodyCenter = center;

            var radius = _headCollisionRadius.Value;
            if (float.IsNaN(radius) || float.IsInfinity(radius)) radius = 0.12f;
            radius = Mathf.Clamp(radius, 0.06f, 0.2f);
            var anchor = center + axis * Mathf.Clamp(Vector3.Dot(desired - center, axis), -segment, segment);
            // CULTIC uses this mask for ceiling clearance and locomotion. Do
            // not use weapon/interaction masks: they include non-solid targets.
            var world = new HeadCollisionWorld { Mask = player.canStandOn & ~(1 << body.gameObject.layer) };
            if (HeadCollisionSolver.TryResolve(ref world, anchor, desired, radius, out var resolved) ||
                (anchor != center && HeadCollisionSolver.TryResolve(ref world, center, desired, radius, out resolved)))
            {
                _safeHeadPosition = resolved;
                _hasSafeHeadPosition = true;
                return resolved;
            }

            // An externally moved object can penetrate the native capsule.
            // Never cast from inside it and mistakenly accept the far side.
            if (_hasSafeHeadPosition && world.IsClear(_safeHeadPosition, radius))
                return _safeHeadPosition;
            _hasSafeHeadPosition = false;
            // No clear seed exists (invalid/embedded native spawn). Leave the
            // game's own recovery authoritative instead of teleporting the body.
            return _source != null ? _source.transform.position : desired;
        }

        private struct HeadCollisionWorld : IHeadCollisionWorld
        {
            internal int Mask;
            public bool IsClear(Vector3 center, float radius) =>
                !Physics.CheckSphere(center, radius, Mask, QueryTriggerInteraction.Ignore);

            public bool Sweep(Vector3 origin, float radius, Vector3 direction, float distance, out float hitDistance)
            {
                var blocked = Physics.SphereCast(origin, radius, direction, out var hit, distance,
                    Mask, QueryTriggerInteraction.Ignore);
                hitDistance = hit.distance;
                return blocked;
            }
        }
    }
}
