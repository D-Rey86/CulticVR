using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace CulticVR.WorldStereoTrial
{
    public sealed partial class WorldStereoTrialPlugin
    {
        private Harmony? _locomotionHarmony;
        private ConfigEntry<bool> _physicalCrouching = null!;
        private ConfigEntry<bool> _roomscaleWalking = null!;
        private ConfigEntry<float> _crouchThreshold = null!;
        private ConfigEntry<float> _roomscaleDeadzone = null!;
        private static readonly AccessTools.FieldRef<scrPlayerControl, float> NativeCrouchOffset =
            AccessTools.FieldRefAccess<scrPlayerControl, float>("crouchOffset");
        private static readonly AccessTools.FieldRef<scrPlayerControl, int> NativeLadderState =
            AccessTools.FieldRefAccess<scrPlayerControl, int>("ladderState");
        private static readonly AccessTools.FieldRef<scrPlayerControl, bool> NativeUnderwater =
            AccessTools.FieldRefAccess<scrPlayerControl, bool>("isUnderwater");
        private static readonly AccessTools.FieldRef<scrPlayerControl, bool> NativeInWater =
            AccessTools.FieldRefAccess<scrPlayerControl, bool>("isInWater");
        private static readonly AccessTools.FieldRef<scrPlayerControl, bool> NativeSliding =
            AccessTools.FieldRefAccess<scrPlayerControl, bool>("isSliding");
        private InputDevice _locomotionHead;
        private bool _locomotionReady;
        private bool _locomotionActive;
        private bool _physicalCrouchRequested;
        private bool _standingHeightKnown;
        private float _standingTrackingHeight;
        private Vector3 _lastLocomotionHead;
        private Vector3 _lastLocomotionBody;
        private const float BodySkin = 0.005f;

        private void InitializePhysicalLocomotion()
        {
            _physicalCrouching = Config.Bind("PhysicalLocomotion", "PhysicalCrouching", true,
                "Physical lowering engages the game's crouch collider. Stand naturally when first entering gameplay.");
            _roomscaleWalking = Config.Bind("PhysicalLocomotion", "RoomscaleWalking", true,
                "Move the player body with horizontal physical movement, constrained by the native capsule and world solids.");
            _crouchThreshold = Config.Bind("PhysicalLocomotion", "CrouchThreshold", 0.25f,
                new ConfigDescription("Head lowering in metres that engages physical crouch; release threshold is 0.10m higher.",
                    new AcceptableValueRange<float>(0.15f, 0.7f)));
            _roomscaleDeadzone = Config.Bind("PhysicalLocomotion", "BodyFollowDeadzone", 0.08f,
                new ConfigDescription("Horizontal lean allowed before the body follows (metres).",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            try
            {
                _locomotionHarmony = new Harmony("culticvr.worldstereotrial.physical-locomotion");
                _locomotionHarmony.Patch(AccessTools.Method(typeof(scrPlayerControl), "Update"),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(PhysicalLocomotionPrefix))),
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(PhysicalCrouchStores))));
                _locomotionReady = true;
            }
            catch (Exception ex)
            {
                _locomotionHarmony?.UnpatchSelf();
                _locomotionReady = false;
                Logger.LogError("Physical locomotion disabled; accepted camera/head collision retained: " + ex);
            }
        }

        private void ResetPhysicalLocomotion()
        {
            CancelArtificialLocomotion();
            _locomotionActive = false;
            _physicalCrouchRequested = false;
            // OpenXR tracking height survives scene/save loads. Do not calibrate
            // a crouched pose as standing when entering another map or a vent.
        }

        private static void PhysicalLocomotionPrefix(scrPlayerControl __instance)
        {
            _instance?.PrepareArtificialLocomotion(__instance);
            _instance?.PreparePhysicalLocomotion(__instance);
        }

        private void PreparePhysicalLocomotion(scrPlayerControl player)
        {
            if (!_locomotionReady || player != _player || !_reportedReady || _source == null) return;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID || game.gameState != 0 || !Application.isFocused ||
                Time.timeScale <= 0f || game.inputCooldown > 0f || player.isDead || player.freezeCD > 0f ||
                player.state == 4 || player.state == 69 || player.state == 2 || NativeLadderState(player) != 0 ||
                NativeUnderwater(player) || player.myCollider == null || !player.myCollider.enabled)
            {
                ResetPhysicalLocomotion();
                return;
            }
            if (!_locomotionHead.isValid) _locomotionHead = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!_locomotionHead.isValid ||
                (_locomotionHead.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) ||
                !_locomotionHead.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 head) ||
                !ScopeSightingMath.Finite(head))
            {
                ResetPhysicalLocomotion();
                return;
            }
            if (!_standingHeightKnown)
            {
                _standingHeightKnown = true;
                _standingTrackingHeight = _physicalPositionInitialized ? _physicalHeadPositionOrigin.y : head.y;
            }
            if (!_locomotionActive)
            {
                _lastLocomotionHead = head;
                // Do not accumulate body movement during pause/loading/tracking
                // loss. Keep vertical calibration independently across scenes.
                _physicalHeadPositionOrigin.x = head.x;
                _physicalHeadPositionOrigin.z = head.z;
            }
            else if ((head - _lastLocomotionHead).sqrMagnitude > TrackingPositionDiscontinuity * TrackingPositionDiscontinuity)
            {
                var jump = head - _lastLocomotionHead;
                _physicalHeadPositionOrigin += jump;
                _standingTrackingHeight += jump.y;
                _lastPhysicalHeadPosition = head; // existing late tracker must not apply the same recenter twice
            }
            if (_locomotionActive && (player.rigidBody.position - _lastLocomotionBody).sqrMagnitude > 1f)
            {
                _physicalHeadPositionOrigin.x = head.x;
                _physicalHeadPositionOrigin.z = head.z;
            }
            _lastLocomotionHead = head;
            _locomotionActive = true;
            _physicalCrouchRequested = _physicalCrouching.Value && PhysicalLocomotionMath.CrouchRequested(
                _physicalCrouchRequested, _standingTrackingHeight - head.y, _crouchThreshold.Value, 0.10f);
            if (_physicalCrouchRequested) player.isCrouched = true;

            if (_roomscaleWalking.Value && player.isGrounded && !NativeInWater(player) && !NativeSliding(player))
            {
                var yaw = Quaternion.Euler(0f, _source.transform.eulerAngles.y, 0f) * TrackingYawCompensation;
                var request = PhysicalLocomotionMath.BodyRequest(head - _physicalHeadPositionOrigin, yaw, _roomscaleDeadzone.Value);
                if (request.sqrMagnitude > 0.000001f)
                {
                    var accepted = MoveRoomscaleBody(player, request);
                    // Consume the actual body displacement, never the requested
                    // displacement. A blocked lean remains for head collision.
                    _physicalHeadPositionOrigin += Quaternion.Inverse(yaw) * accepted;
                }
            }
            _lastLocomotionBody = player.rigidBody.position;
        }

        private static void StorePhysicalCrouch(scrPlayerControl player, bool nativeValue)
        {
            var owner = _instance;
            player.isCrouched = nativeValue || (owner != null && owner._locomotionReady &&
                owner._locomotionActive && owner._player == player && owner._physicalCrouchRequested);
        }

        private static IEnumerable<CodeInstruction> PhysicalCrouchStores(IEnumerable<CodeInstruction> source)
        {
            var instructions = source.ToList(); // installation only
            var field = AccessTools.Field(typeof(scrPlayerControl), "isCrouched");
            var replacement = AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(StorePhysicalCrouch));
            var stores = instructions.Where(i => i.opcode == OpCodes.Stfld && Equals(i.operand, field)).ToArray();
            if (stores.Length != 4) throw new InvalidOperationException("Native crouch store contract changed: " + stores.Length);
            foreach (var store in stores)
            {
                store.opcode = OpCodes.Call;
                store.operand = replacement;
            }
            return instructions;
        }

        private void ApplyPhysicalCrouchHeight(Vector3 head)
        {
            if (!_locomotionReady || !_physicalCrouching.Value || !_standingHeightKnown || _player == null) return;
            var residual = TrackingPositionCompensation;
            residual.y = PhysicalLocomotionMath.CrouchResidual(head.y - _standingTrackingHeight, NativeCrouchOffset(_player));
            TrackingPositionCompensation = residual;
        }

        private static Vector3 MoveRoomscaleBody(scrPlayerControl player, Vector3 request)
        {
            // Solve the physical displacement separately from artificial
            // velocity. Combining the two would make accepted roomscale motion
            // unidentifiable when a stick and physical step cancel or hit a wall.
            // Native walking velocity, gravity, impulses and FixedUpdate remain
            // untouched. No unchecked Transform translation is used.
            var body = player.myCollider;
            var rigidBody = player.rigidBody;
            if (rigidBody == null || rigidBody.isKinematic || body.direction != 1) return Vector3.zero;
            var scale = body.transform.lossyScale;
            var radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var halfSegment = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * 0.5f - radius);
            var center = body.transform.TransformPoint(body.center);
            var axis = body.transform.up;
            var top = center + axis * halfSegment;
            var bottom = center - axis * halfSegment;
            var mask = player.canStandOn & ~(1 << body.gameObject.layer);
            var castRadius = Mathf.Max(0.01f, radius - BodySkin);
            // Respect the native capsule's contact shell, not just its geometric
            // radius. Live wall-slide trace showed the physics solver correcting
            // tiny penetrations AFTER an immediate Rigidbody.position read; those
            // corrections otherwise accumulate as a tracking-origin drift.
            var clearance = BodySkin + Mathf.Max(0f, body.contactOffset);
            if (Physics.CheckCapsule(bottom, top, castRadius, mask, QueryTriggerInteraction.Ignore)) return Vector3.zero;
            var moved = Vector3.zero;
            var remaining = Vector3.ClampMagnitude(request, 2f * Time.unscaledDeltaTime);
            for (int pass = 0; pass < 3 && remaining.sqrMagnitude > 0.000001f; pass++)
            {
                float distance = remaining.magnitude;
                var direction = remaining / distance;
                if (!Physics.CapsuleCast(bottom + moved, top + moved, castRadius, direction, out var hit,
                    distance + clearance, mask, QueryTriggerInteraction.Ignore))
                {
                    moved += remaining;
                    break;
                }
                float travel = Mathf.Clamp(hit.distance - clearance, 0f, distance);
                moved += direction * travel;
                var normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude < 0.00001f) break;
                normal.Normalize();
                remaining = Vector3.ProjectOnPlane(remaining - direction * travel, normal);
            }
            var before = rigidBody.position;
            if (moved.sqrMagnitude > 0.000001f) rigidBody.position = before + moved;
            return rigidBody.position - before;
        }
    }
}
