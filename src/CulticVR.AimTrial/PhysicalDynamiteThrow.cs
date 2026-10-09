using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace CulticVR.AimTrial
{
    public sealed partial class AimTrialPlugin
    {
        private const int PhysicalThrowSampleCapacity = 16;
        private const int PhysicalThrowArcPointCount = 25;
        private const float PhysicalThrowSampleWindow = 0.10f;
        private const float PhysicalThrowMinimumSampleSpan = 0.035f;
        private const float PhysicalThrowMinimumSpeed = 0.75f;
        private const float PhysicalThrowMaximumTrackedSpeed = 12f;
        private const float PhysicalThrowMaximumLaunchSpeed = 42.2f;
        private const float PhysicalThrowArcStep = 0.075f;

        private ConfigEntry<bool> _physicalDynamiteThrowEnabled = null!;
        private ConfigEntry<float> _physicalDynamiteThrowVelocityMultiplier = null!;
        private AccessTools.FieldRef<scrPlayerControl, bool>? _tntIsLitRef;
        private AccessTools.FieldRef<scrPlayerControl, float>? _fireChargeRef;
        private readonly Vector3[] _physicalThrowPositions = new Vector3[PhysicalThrowSampleCapacity];
        private readonly float[] _physicalThrowTimes = new float[PhysicalThrowSampleCapacity];
        private readonly Vector3[] _physicalThrowArcPoints = new Vector3[PhysicalThrowArcPointCount];
        private int _physicalThrowSampleStart;
        private int _physicalThrowSampleCount;
        private bool _physicalThrowTriggerPressed;
        private bool _physicalThrowPreviewActive;
        private bool _physicalThrowPending;
        private scrPlayerControl? _physicalThrowPlayer;
        private Vector3 _physicalThrowReleaseOrigin;
        private Quaternion _physicalThrowReleaseRotation;
        private Vector3 _physicalThrowReleaseVelocity;
        private Vector3 _trackingRelativePosition;

        private bool PhysicalThrowPreviewActive => _physicalThrowPreviewActive;

        private void InitializePhysicalDynamiteThrow()
        {
            _physicalDynamiteThrowEnabled = Config.Bind("Physical Interaction", "PhysicalDynamiteThrowEnabled", false,
                "Use right-controller motion for lit equipped dynamite when Fire is held and released. This is intended for a future in-game VR menu toggle.");
            _physicalDynamiteThrowVelocityMultiplier = Config.Bind("Physical Interaction", "PhysicalDynamiteThrowVelocityMultiplier", 3.5f,
                new ConfigDescription("Gameplay velocity gain applied to physical hand motion. CULTIC's normal charged throw reaches 42.2 units per second, while real controller motion is much slower.",
                    new AcceptableValueRange<float>(1f, 6f)));
            try
            {
                _tntIsLitRef = AccessTools.FieldRefAccess<scrPlayerControl, bool>("tntIsLit");
                _fireChargeRef = AccessTools.FieldRefAccess<scrPlayerControl, float>("fireCharge");
            }
            catch (Exception error)
            {
                _tntIsLitRef = null;
                _fireChargeRef = null;
                Logger.LogWarning($"Physical dynamite throwing is unavailable; CULTIC throw fields changed: {error}");
            }
        }

        private void UpdatePhysicalDynamiteThrow()
        {
            if (!CulticVR.WorldStereoTrial.VrSettings.MotionControls) { ResetPhysicalDynamiteThrow(clearPending:true); return; }
            var player = LocalPlayer();
            if (_physicalThrowPending)
            {
                _physicalThrowPreviewActive = false;
                ClearPhysicalThrowArc(player);
                if (!_physicalDynamiteThrowEnabled.Value || !IsEquippedDynamite(player) || !IsTntLit(player))
                    ResetPhysicalDynamiteThrow(clearPending: true);
                return;
            }

            if (!_physicalDynamiteThrowEnabled.Value || !IsEquippedDynamite(player) || !IsTntLit(player) ||
                !UpdatePose(player!))
            {
                ResetPhysicalDynamiteThrow(clearPending: false);
                return;
            }

            var right = InputDevices.GetDeviceAtXRNode(CulticVR.WorldStereoTrial.VrHandPose.WeaponNode);
            if (!right.isValid ||
                !right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked ||
                !right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerPressed))
            {
                ResetPhysicalDynamiteThrow(clearPending: false);
                return;
            }

            if (triggerPressed)
            {
                if (!_physicalThrowTriggerPressed) ClearPhysicalThrowSamples();
                AddPhysicalThrowSample(_trackingRelativePosition, Time.unscaledTime);
                if (TryEstimatePhysicalThrowVelocity(out var trackedVelocity))
                {
                    _physicalThrowPreviewActive = trackedVelocity.magnitude >= PhysicalThrowMinimumSpeed;
                    if (_physicalThrowPreviewActive)
                        UpdatePhysicalThrowArc(player!, _origin, ScalePhysicalThrowVelocity(player!, trackedVelocity));
                    else ClearPhysicalThrowArc(player);
                }
                else
                {
                    _physicalThrowPreviewActive = false;
                    ClearPhysicalThrowArc(player);
                }
                _physicalThrowTriggerPressed = true;
                return;
            }

            if (_physicalThrowTriggerPressed)
            {
                AddPhysicalThrowSample(_trackingRelativePosition, Time.unscaledTime);
                if (TryEstimatePhysicalThrowVelocity(out var trackedVelocity) &&
                    trackedVelocity.magnitude >= PhysicalThrowMinimumSpeed)
                    ArmPhysicalDynamiteThrow(player!, trackedVelocity);
            }
            _physicalThrowTriggerPressed = false;
            _physicalThrowPreviewActive = false;
            ClearPhysicalThrowArc(player);
            ClearPhysicalThrowSamples();
        }

        private bool IsEquippedDynamite(scrPlayerControl? player)
        {
            if (player == null ||
                !CulticVR.WorldStereoTrial.WorldStereoTrialPlugin.IsActiveGameplayPlayer(player) || player.weapon < 0 ||
                player.tempWeapon != null) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.gameState != 0 || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID) return false;
            try
            {
                return game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID == 6;
            }
            catch { return false; }
        }

        private bool IsTntLit(scrPlayerControl? player)
        {
            if (player == null || _tntIsLitRef == null) return false;
            try { return _tntIsLitRef(player); }
            catch { return false; }
        }

        private void AddPhysicalThrowSample(Vector3 position, float time)
        {
            var index = (_physicalThrowSampleStart + _physicalThrowSampleCount) % PhysicalThrowSampleCapacity;
            if (_physicalThrowSampleCount == PhysicalThrowSampleCapacity)
            {
                _physicalThrowSampleStart = (_physicalThrowSampleStart + 1) % PhysicalThrowSampleCapacity;
                index = (_physicalThrowSampleStart + _physicalThrowSampleCount - 1) % PhysicalThrowSampleCapacity;
            }
            else
            {
                _physicalThrowSampleCount++;
            }
            _physicalThrowPositions[index] = position;
            _physicalThrowTimes[index] = time;

            while (_physicalThrowSampleCount > 2)
            {
                var oldest = _physicalThrowSampleStart;
                if (time - _physicalThrowTimes[oldest] <= PhysicalThrowSampleWindow) break;
                _physicalThrowSampleStart = (_physicalThrowSampleStart + 1) % PhysicalThrowSampleCapacity;
                _physicalThrowSampleCount--;
            }
        }

        private bool TryEstimatePhysicalThrowVelocity(out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (_physicalThrowSampleCount < 3) return false;
            var newestIndex = (_physicalThrowSampleStart + _physicalThrowSampleCount - 1) % PhysicalThrowSampleCapacity;
            var newestTime = _physicalThrowTimes[newestIndex];
            if (newestTime - _physicalThrowTimes[_physicalThrowSampleStart] < PhysicalThrowMinimumSampleSpan) return false;

            var sumTime = 0f;
            var sumTimeSquared = 0f;
            var sumPosition = Vector3.zero;
            var sumTimePosition = Vector3.zero;
            for (var sample = 0; sample < _physicalThrowSampleCount; sample++)
            {
                var index = (_physicalThrowSampleStart + sample) % PhysicalThrowSampleCapacity;
                var relativeTime = _physicalThrowTimes[index] - newestTime;
                var position = _physicalThrowPositions[index];
                sumTime += relativeTime;
                sumTimeSquared += relativeTime * relativeTime;
                sumPosition += position;
                sumTimePosition += position * relativeTime;
            }
            var denominator = _physicalThrowSampleCount * sumTimeSquared - sumTime * sumTime;
            if (denominator <= 0.0000001f) return false;
            velocity = (_physicalThrowSampleCount * sumTimePosition - sumPosition * sumTime) / denominator;
            return IsFinite(velocity);
        }

        private Vector3 ScalePhysicalThrowVelocity(scrPlayerControl player, Vector3 trackedVelocity)
        {
            trackedVelocity = Vector3.ClampMagnitude(trackedVelocity, PhysicalThrowMaximumTrackedSpeed);
            var yaw = Quaternion.Euler(0f, player.playerCameraComponent.transform.eulerAngles.y, 0f) *
                CulticVR.WorldStereoTrial.WorldStereoTrialPlugin.TrackingYawCompensation;
            return Vector3.ClampMagnitude(yaw * trackedVelocity * _physicalDynamiteThrowVelocityMultiplier.Value,
                PhysicalThrowMaximumLaunchSpeed);
        }

        private void ArmPhysicalDynamiteThrow(scrPlayerControl player, Vector3 trackedVelocity)
        {
            if (trackedVelocity.magnitude < PhysicalThrowMinimumSpeed || _fireChargeRef == null) return;
            var velocity = ScalePhysicalThrowVelocity(player, trackedVelocity);
            var speed = velocity.magnitude;
            var direction = velocity / speed;
            var controllerUp = _rotation * Vector3.up;
            var throwUp = Vector3.ProjectOnPlane(controllerUp, direction);
            if (throwUp.sqrMagnitude < 0.001f) throwUp = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (throwUp.sqrMagnitude < 0.001f) throwUp = Vector3.ProjectOnPlane(Vector3.right, direction);

            _physicalThrowReleaseOrigin = _origin;
            _physicalThrowReleaseRotation = Quaternion.LookRotation(direction, throwUp.normalized);
            _physicalThrowReleaseVelocity = direction * speed;
            _physicalThrowPlayer = player;
            _physicalThrowPending = true;
            ref var fireCharge = ref _fireChargeRef(player);
            fireCharge = Mathf.Clamp((speed - 0.2f) / 0.7f, 0f, 60f);
            _aimObject!.transform.SetPositionAndRotation(_physicalThrowReleaseOrigin, _physicalThrowReleaseRotation);
        }

        private bool UsePendingPhysicalThrowAim(scrPlayerControl player)
        {
            if (!_physicalThrowPending || _physicalThrowPlayer != player || !IsEquippedDynamite(player)) return false;
            _aimObject!.transform.SetPositionAndRotation(_physicalThrowReleaseOrigin, _physicalThrowReleaseRotation);
            return true;
        }

        private static void PhysicalThrowFireEventPostfix(scrPlayerControl __instance)
        {
            var trial = Instance;
            if (trial == null || !trial._physicalThrowPending || trial._physicalThrowPlayer != __instance) return;
            trial.ResetPhysicalDynamiteThrow(clearPending: true);
        }

        private void UpdatePhysicalThrowArc(scrPlayerControl player, Vector3 origin, Vector3 velocity)
        {
            var arc = player.throwArc;
            if (arc == null) return;
            var direction = velocity.normalized;
            var controllerRight = _rotation * Vector3.right;
            var start = origin + direction * 0.1f + controllerRight * 0.05f;
            for (var index = 0; index < PhysicalThrowArcPointCount; index++)
            {
                var time = index * PhysicalThrowArcStep;
                _physicalThrowArcPoints[index] = start + velocity * time + Physics.gravity * (0.5f * time * time);
            }
            arc.positionCount = PhysicalThrowArcPointCount;
            arc.SetPositions(_physicalThrowArcPoints);
        }

        private void ClearPhysicalThrowArc(scrPlayerControl? player)
        {
            if (player != null && player.throwArc != null && player.throwArc.positionCount != 0)
                player.throwArc.positionCount = 0;
        }

        private void ClearPhysicalThrowSamples()
        {
            _physicalThrowSampleStart = 0;
            _physicalThrowSampleCount = 0;
        }

        private void ResetPhysicalDynamiteThrow(bool clearPending)
        {
            ClearPhysicalThrowArc(_physicalThrowPlayer ?? _localPlayer);
            ClearPhysicalThrowSamples();
            _physicalThrowTriggerPressed = false;
            _physicalThrowPreviewActive = false;
            if (!clearPending) return;
            _physicalThrowPending = false;
            _physicalThrowPlayer = null;
            _physicalThrowReleaseOrigin = Vector3.zero;
            _physicalThrowReleaseRotation = Quaternion.identity;
            _physicalThrowReleaseVelocity = Vector3.zero;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
