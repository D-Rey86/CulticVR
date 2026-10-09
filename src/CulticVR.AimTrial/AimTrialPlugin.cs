using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using CulticVR.AimPreview;
using CulticVR.WorldStereoTrial;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace CulticVR.AimTrial
{
    [BepInPlugin("culticvr.aimtrial", "CULTIC VR Controller Aim", "0.4.17")]
    [BepInDependency("culticvr.aimpreview")]
    [BepInDependency("culticvr.worldstereotrial")]
    public sealed partial class AimTrialPlugin : BaseUnityPlugin
    {
        public enum ScopeEye { Right, Left }
        private ConfigEntry<ScopeEye> _scopeSightingEye = null!;
        private Func<Camera.StereoscopicEye>? _renderScopeEyeProvider;
        public ScopeEye ScopeSightingEye
        {
            get => _scopeSightingEye.Value;
            set => _scopeSightingEye.Value = value;
        }
        internal static AimTrialPlugin? Instance;
        private Harmony? _harmony;
        private Harmony? _quickThrowHarmony;
        private Harmony? _thrownPreviewHarmony;
        private GameObject? _aimObject;
        private Camera? _aimCamera;
        private scrPlayerControl? _localPlayer;
        private UnityEngine.InputSystem.InputDevice? _pointerDevice;
        private Vector3Control? _pointerPositionControl;
        private QuaternionControl? _pointerRotationControl;
        private bool _ready;
        private bool _quickThrowReady;
        private bool _thrownPreviewReady;
        private bool _interactionReady;
        private bool _pitchforkThrowInProgress;
        private AccessTools.FieldRef<scrPlayerControl, RaycastHit>? _interactLastHitRef;
        private int _interactionAimFrame = -1;
        private scrPlayerControl? _interactionAimPlayer;
        private bool _interactionAimValid;
        private static readonly FieldInfo? AltChargeStateField = AccessTools.Field(typeof(scrPlayerControl), "altChargeState");
        private static readonly AccessTools.FieldRef<scrPlayerControl, int> ZoomState =
            AccessTools.FieldRefAccess<scrPlayerControl, int>("zoomState");
        private int _poseFrame = -1;
        private scrPlayerControl? _posePlayer;
        private int _poseZoomState;
        private int _poseWeapon;
        private ScopeEye _poseEye;
        private bool _poseValid;
        private Vector3 _origin;
        private Quaternion _rotation;
        private string? _lastFailure;

        private void Awake()
        {
            Instance = this;
            _scopeSightingEye = Config.Bind("Scope", "SightingEye", ScopeEye.Right,
                "Eye used to sight through the FG42 scope: Right or Left. Independent of the weapon hand. Unscoped aim is unchanged.");
            _renderScopeEyeProvider = () => ScopeSightingEye == ScopeEye.Left ?
                Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;
            WorldStereoTrialPlugin.ScopeSightingEyeProvider = _renderScopeEyeProvider;
            InitializePhysicalDynamiteThrow();
            _aimObject = new GameObject("CulticVR Firearm Aim Proxy");
            DontDestroyOnLoad(_aimObject);
            _aimCamera = _aimObject.AddComponent<Camera>();
            _aimCamera.enabled = false;
            _harmony = new Harmony("culticvr.aimtrial");
            try
            {
                var fire = AccessTools.Method(typeof(scrPlayerControl), "fireEvent");
                var altFire = AccessTools.Method(typeof(scrPlayerControl), "altFireEvent");
                var bullet = AccessTools.Method(typeof(scrPlayerControl), "castBullet");
                if (fire == null || altFire == null || bullet == null) throw new MissingMethodException("CULTIC firing method unavailable");
                var transpiler = new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectFiringReads)));
                _harmony.Patch(fire,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(ScopedFireEventPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(PhysicalThrowFireEventPostfix))),
                    transpiler: transpiler);
                _harmony.Patch(altFire, transpiler: transpiler);
                _harmony.Patch(bullet, transpiler: transpiler);
                _ready = true;
                try
                {
                    var quickThrow = AccessTools.Method(typeof(scrPlayerControl), "offHandQuickToss");
                    if (quickThrow == null) throw new MissingMethodException("CULTIC Quick TNT method unavailable");
                    _quickThrowHarmony = new Harmony("culticvr.aimtrial.quickthrow");
                    _quickThrowHarmony.Patch(quickThrow,
                        transpiler: new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectQuickThrowReads))));
                    _quickThrowReady = true;
                }
                catch (Exception quickThrowError)
                {
                    _quickThrowHarmony?.UnpatchSelf();
                    _quickThrowHarmony = null;
                    Logger.LogWarning($"Quick TNT aim patch unavailable; other attack aim retained: {quickThrowError}");
                }
                try
                {
                    var update = AccessTools.Method(typeof(scrPlayerControl), "Update");
                    if (update == null) throw new MissingMethodException("CULTIC player Update method unavailable");
                    if (AltChargeStateField == null) throw new MissingFieldException("CULTIC throw charge state unavailable");
                    _interactLastHitRef = AccessTools.FieldRefAccess<scrPlayerControl, RaycastHit>("interactLastHit");
                    _thrownPreviewHarmony = new Harmony("culticvr.aimtrial.thrownpreview");
                    _thrownPreviewHarmony.Patch(update,
                        transpiler: new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectThrownPreviewReads))));
                    _thrownPreviewReady = true;
                    _interactionReady = true;
                }
                catch (Exception axePreviewError)
                {
                    try { _thrownPreviewHarmony?.UnpatchSelf(); }
                    catch (Exception unpatchError)
                    {
                        Logger.LogWarning($"Thrown-weapon preview cleanup failed after patch error: {unpatchError}");
                    }
                    _thrownPreviewHarmony = null;
                    _interactLastHitRef = null;
                    Logger.LogWarning($"Thrown-weapon preview patch unavailable; hatchet/pitchfork game-camera launches retained: {axePreviewError}");
                }
            }
            catch (Exception error)
            {
                _harmony.UnpatchSelf();
                Logger.LogError($"Firearm aim patch failed; CULTIC's game-camera shot path retained: {error}");
            }
            InitializeBazookaAim();
            InitializeDisposableAim();
            InitializeWeaponMuzzles();
        }

        private void OnDestroy()
        {
            RemoveWeaponMuzzles();
            RemoveDisposableAim();
            RemoveBazookaAim();
            if (WorldStereoTrialPlugin.ScopeSightingEyeProvider == _renderScopeEyeProvider)
                WorldStereoTrialPlugin.ScopeSightingEyeProvider = null;
            ResetPhysicalDynamiteThrow(clearPending: true);
            RestoreReticle();
            RestoreInteractionReticle();
            _ready = false;
            _quickThrowReady = false;
            _thrownPreviewReady = false;
            _interactionReady = false;
            _pitchforkThrowInProgress = false;
            _thrownPreviewHarmony?.UnpatchSelf();
            _quickThrowHarmony?.UnpatchSelf();
            _harmony?.UnpatchSelf();
            if (_aimObject != null) Destroy(_aimObject);
            _localPlayer = null;
            _pointerDevice = null;
            _pointerPositionControl = null;
            _pointerRotationControl = null;
            _interactLastHitRef = null;
            Instance = null;
        }

        private bool UseControllerAim(scrPlayerControl player)
        {
            if (!_ready || !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player)) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID || player.weapon < 0 || player.tempWeapon != null) return false;
            try
            {
                var weaponId = game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID;
                // Equipped dynamite (6) launches from mainCam in fireEvent. The
                // same camera substitution drives its spawn and velocity, while
                // the live reticle shows the initial throw direction. The charge
                // still determines speed and gravity determines the landing point.
                // Unarmed IDs 7 and 13 use the same charged-melee fireEvent
                // path as the hatchet. Their idle viewmodel is intentionally
                // blank; the fists appear only during the punch animation.
                // Shotgun ID 3 is hitscan in both fire modes. Its alternate
                // fire also uses playerCamera for knockback; that movement read
                // is deliberately kept on the native camera below.
                // STEN ID 4 is primary-fire-only hitscan. Its bullet, tracer,
                // and casing reads share the same controller proxy, while its
                // scalar camera shake and tilt remain owned by CULTIC.
                // Grenade launcher ID 5 spawns prefabBank[9] from mainCam and
                // launches it at 35 m/s along the spread-adjusted camera forward.
                // Both detonation modes share that path; native spawn offsets,
                // physics, damage and scalar recoil stay unchanged. The straight
                // reticle indicates initial aim, NOT the grenade's landing point.
                // FG42 ID 9 is also primary-fire hitscan through castBullet.
                // Its LT scope state is handled independently in Update; the
                // stereo world camera continues to mirror CULTIC's scoped FOV.
                // Incinerator ID 10 launches flame particles in fireEvent and
                // charged particles/impact projectiles in altFireEvent. Both use
                // mainCam for spawn position and launch direction; enable the
                // same proxy in BOTH gates, preserving native spread and physics.
                if (weaponId != 0 && weaponId != 1 && weaponId != 2 && weaponId != 3 && weaponId != 4 && weaponId != 5 && weaponId != 6 &&
                    weaponId != 7 && weaponId != 8 && weaponId != 9 && weaponId != 10 && weaponId != 13) return false;
            }
            catch { return false; }
            return UpdatePose(player);
        }

        private bool UseQuickThrowAim(scrPlayerControl player)
        {
            if (!_quickThrowReady || !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player) ||
                player.offHandItem != scrPlayerControl.OffHandItem.TNT) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID) return false;
            return UpdatePose(player);
        }

        private bool UpdatePose(scrPlayerControl player)
        {
            var zoomState = ZoomState(player);
            var eye = ScopeSightingEye;
            if (_poseFrame == Time.frameCount && _posePlayer == player && _poseZoomState == zoomState &&
                _poseWeapon == player.weapon && _poseEye == eye) return _poseValid;
            _poseFrame = Time.frameCount;
            _posePlayer = player;
            _poseZoomState = zoomState;
            _poseWeapon = player.weapon;
            _poseEye = eye;
            _poseValid = false;
            var gameCamera = player.playerCameraComponent;
            if (gameCamera == null) return Fail("gameplay camera unavailable");
            if (!VrSettings.MotionControls)
            {
                if (!VrHandPose.TryHead(out _,out var trackedRotation)) return Fail("tracked HMD aim unavailable");
                var trackedYaw=Quaternion.Euler(0f,gameCamera.transform.eulerAngles.y,0f)*WorldStereoTrialPlugin.TrackingYawCompensation;
                _origin=gameCamera.transform.position+trackedYaw*WorldStereoTrialPlugin.TrackingPositionCompensation;
                _rotation=trackedYaw*trackedRotation;
                _rawMuzzleOrigin=_origin; _rawMuzzleRotation=_rotation; _trackingRelativePosition=Vector3.zero;
                _aimObject!.transform.SetPositionAndRotation(_origin,_rotation);
                _lastFailure=null; _poseValid=true; return true;
            }
            if (!VrHandPose.TryPointer(VrHandPose.WeaponNode,out var pointerPosition,out var pointerRotation))
                return Fail("weapon controller pointer unavailable");
            if (Quaternion.Dot(pointerRotation, pointerRotation) <= 0.5f) return Fail("right pointer rotation invalid");
            if (!VrHandPose.TryHead(out var headPosition,out var headRotation))
                return Fail("tracked head position unavailable");
            var yaw = Quaternion.Euler(0f, gameCamera.transform.eulerAngles.y, 0f) *
                WorldStereoTrialPlugin.TrackingYawCompensation;
            _trackingRelativePosition = pointerPosition - headPosition;
            _origin = gameCamera.transform.position + yaw *
                (WorldStereoTrialPlugin.TrackingPositionCompensation + _trackingRelativePosition);
            _rotation = yaw * pointerRotation;
            _rawMuzzleOrigin = _origin;
            _rawMuzzleRotation = _rotation;
            // A flat scope's normal is NOT the eye's sight line through its
            // crosshair. Change only ID9/LT, keeping the raw pointer elsewhere.
            if (IsScopedFg42(player))
            {
                var stereoEye = eye == ScopeEye.Left ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;
                if (WorldStereoTrialPlugin.TryGetSightingEyeOffset(player, stereoEye, out var eyeOffset) &&
                    AimPreviewPlugin.TryGetRightHandScopeCrosshair(player, _origin, _rotation, out var crosshair, out var artworkRotation) &&
                    ScopeSightingMath.TryGetDirection(gameCamera.transform.position +
                        yaw * (WorldStereoTrialPlugin.TrackingPositionCompensation + headRotation * eyeOffset),
                        crosshair, out var direction))
                {
                    _origin = crosshair;
                    _rotation = Quaternion.LookRotation(direction, artworkRotation * Vector3.up);
                }
                else
                {
                    // Unsupported/missing runtime or artwork data: retain the
                    // pre-candidate scope proxy rather than inventing an IPD.
                    AimPreviewPlugin.TryGetRightHandOpticalPose(player, ref _origin, ref _rotation);
                }
            }
            _aimObject!.transform.SetPositionAndRotation(_origin, _rotation);
            _lastFailure = null;
            _poseValid = true;
            return true;
        }

        private static bool IsScopedFg42(scrPlayerControl player)
        {
            if (ZoomState(player) != 1) return false;
            var game = scrGameControl.Instance;
            if (game == null || player.weapon < 0) return false;
            try
            {
                return game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID == 9;
            }
            catch { return false; }
        }

        private static void ScopedFireEventPrefix(scrPlayerControl __instance)
        {
            var trial = Instance;
            // Interaction can populate the proxy in Update before Animator sets
            // the firing sprite. Refresh once at the event, then share the pose
            // across all original bullet/tracer/casing reads in that shot.
            if (trial != null && IsScopedFg42(__instance)) trial._poseFrame = -1;
        }

        private bool EnsurePointerControls()
        {
            if (_pointerDevice != null && _pointerDevice.added &&
                _pointerPositionControl != null && _pointerRotationControl != null)
                return true;

            _pointerDevice = null;
            _pointerPositionControl = null;
            _pointerRotationControl = null;
            foreach (var device in InputSystem.devices)
            {
                if (device.description.interfaceName?.IndexOf("XR", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var rightHand = false;
                foreach (var usage in device.usages)
                {
                    if (usage.ToString() != "RightHand") continue;
                    rightHand = true;
                    break;
                }
                if (!rightHand) continue;
                foreach (var control in device.allControls)
                {
                    if (_pointerPositionControl == null && control is Vector3Control position &&
                        position.path.EndsWith("/pointer/position", StringComparison.OrdinalIgnoreCase))
                        _pointerPositionControl = position;
                    else if (_pointerRotationControl == null && control is QuaternionControl rotation &&
                        rotation.path.EndsWith("/pointer/rotation", StringComparison.OrdinalIgnoreCase))
                        _pointerRotationControl = rotation;
                }
                if (_pointerPositionControl == null || _pointerRotationControl == null)
                {
                    _pointerPositionControl = null;
                    _pointerRotationControl = null;
                    continue;
                }
                _pointerDevice = device;
                return true;
            }
            return false;
        }

        private bool Fail(string reason)
        {
            if (_lastFailure != reason) Logger.LogWarning($"Controller aim uses CULTIC's game-camera path: {reason}");
            _lastFailure = reason;
            return false;
        }

        private static Camera AimCamera(scrGameControl game)
        {
            var trial = Instance;
            var player = trial?.LocalPlayer();
            if (trial != null && player != null && trial.UsePendingPhysicalThrowAim(player))
                return trial._aimCamera!;
            return trial != null && player != null && trial.UseControllerAim(player) ? trial._aimCamera! : game.mainCam;
        }

        private static Camera AimCameraForQuickThrow(scrGameControl game)
        {
            var trial = Instance;
            var player = trial?.LocalPlayer();
            return trial != null && player != null && trial.UseQuickThrowAim(player) ? trial._aimCamera! : game.mainCam;
        }

        private static IEnumerable<CodeInstruction> RedirectQuickThrowReads(IEnumerable<CodeInstruction> source)
        {
            var instructions = source.ToList();
            var mainCamera = AccessTools.Field(typeof(scrGameControl), "mainCam");
            var replacement = AccessTools.Method(typeof(AimTrialPlugin), nameof(AimCameraForQuickThrow));
            var count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, mainCamera)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                count++;
            }
            if (count != 4) throw new InvalidOperationException($"offHandQuickToss camera read count changed: {count}");
            return instructions;
        }

        private bool UseThrownWeaponAim(scrPlayerControl player)
        {
            if (!_thrownPreviewReady || !_ready ||
                !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player)) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID || player.weapon < 0 || player.tempWeapon != null) return false;
            try
            {
                var id = game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID;
                if (id != 0 && id != 16) return false;
            }
            catch { return false; }
            return UpdatePose(player);
        }

        private bool UseInteractionAim(scrPlayerControl player)
        {
            if (_interactionAimFrame == Time.frameCount && _interactionAimPlayer == player)
                return _interactionAimValid;
            _interactionAimFrame = Time.frameCount;
            _interactionAimPlayer = player;
            _interactionAimValid = false;
            if (!_interactionReady || !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player)) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID) return false;
            _interactionAimValid = UpdatePose(player);
            return _interactionAimValid;
        }

        private static GameObject AimPlayerCameraForInteraction(scrPlayerControl player) =>
            Instance != null && Instance.UseInteractionAim(player) ? Instance._aimObject! : player.playerCamera;

        private bool UsePitchforkThrowReticle(scrPlayerControl player)
        {
            if (!UseThrownWeaponAim(player) || AltChargeStateField == null)
            {
                _pitchforkThrowInProgress = false;
                return false;
            }
            var game = scrGameControl.Instance;
            try
            {
                if (game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID != 16)
                {
                    _pitchforkThrowInProgress = false;
                    return false;
                }
                var charging = (int)AltChargeStateField.GetValue(player) == 1;
                if (charging) _pitchforkThrowInProgress = true;
                else if (player.weaponState == 0) _pitchforkThrowInProgress = false;
                return _pitchforkThrowInProgress;
            }
            catch
            {
                _pitchforkThrowInProgress = false;
                return false;
            }
        }

        private static Camera AimCameraForThrownPreview(scrGameControl game)
        {
            var trial = Instance;
            var player = trial?.LocalPlayer();
            return trial != null && player != null && trial.UseThrownWeaponAim(player) ? trial._aimCamera! : game.mainCam;
        }

        private static GameObject AimPlayerCameraForThrownPreview(scrPlayerControl player) =>
            Instance != null && Instance.UseThrownWeaponAim(player) ? Instance._aimObject! : player.playerCamera;

        private static IEnumerable<CodeInstruction> RedirectThrownPreviewReads(IEnumerable<CodeInstruction> source, MethodBase original)
        {
            if (original.Name != "Update") throw new InvalidOperationException($"Unexpected preview method {original.Name}");
            var instructions = source.ToList();
            var upgrades = AccessTools.Field(typeof(scrPlayer), "playerUpgrades");
            var arc = AccessTools.Field(typeof(scrPlayerControl), "throwArc");
            var mainCamera = AccessTools.Field(typeof(scrGameControl), "mainCam");
            var playerCamera = AccessTools.Field(typeof(scrPlayerControl), "playerCamera");
            var canInteractWith = AccessTools.Field(typeof(scrPlayerControl), "canInteractWith");
            if (canInteractWith == null) throw new MissingFieldException("CULTIC interaction layer mask unavailable");

            var interactionAnchors = new List<int>();
            for (var index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].opcode == OpCodes.Ldfld && Equals(instructions[index].operand, canInteractWith))
                    interactionAnchors.Add(index);
            }
            if (interactionAnchors.Count != 1)
                throw new InvalidOperationException($"Interaction ray anchor count changed: {interactionAnchors.Count}");

            var interactionPlayerCount = 0;
            var interactionStart = Math.Max(0, interactionAnchors[0] - 10);
            var interactionEnd = Math.Min(instructions.Count - 1, interactionAnchors[0] + 210);
            for (var index = interactionStart; index <= interactionEnd; index++)
            {
                var instruction = instructions[index];
                if (instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, playerCamera)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(AimTrialPlugin), nameof(AimPlayerCameraForInteraction));
                interactionPlayerCount++;
            }
            if (interactionPlayerCount != 9)
                throw new InvalidOperationException($"Interaction camera structure changed: player={interactionPlayerCount}");

            var anchors = new Dictionary<int, int>();
            for (var index = 0; index < instructions.Count - 3; index++)
            {
                if (instructions[index].opcode == OpCodes.Ldfld && Equals(instructions[index].operand, upgrades) &&
                    instructions[index + 2].opcode == OpCodes.Ldc_I4_2 &&
                    (instructions[index + 1].opcode == OpCodes.Ldc_I4_8 ||
                     (instructions[index + 1].opcode == OpCodes.Ldc_I4_S && Convert.ToInt32(instructions[index + 1].operand) == 19)))
                {
                    var upgradeId = instructions[index + 1].opcode == OpCodes.Ldc_I4_8 ? 8 : 19;
                    if (anchors.ContainsKey(upgradeId)) throw new InvalidOperationException($"Throw preview upgrade {upgradeId} anchor is ambiguous");
                    anchors.Add(upgradeId, index);
                }
            }
            if (anchors.Count != 2 || !anchors.ContainsKey(8) || !anchors.ContainsKey(19))
                throw new InvalidOperationException("Hatchet/pitchfork preview upgrade anchors unavailable");
            foreach (var anchor in anchors)
            {
                var start = anchor.Value;
                var arcReads = 0;
                var end = -1;
                for (var index = start; index < Math.Min(instructions.Count, start + 800); index++)
                {
                    if (instructions[index].opcode != OpCodes.Ldfld || !Equals(instructions[index].operand, arc)) continue;
                    arcReads++;
                    if (arcReads == 5) { end = index; break; }
                }
                if (end < 0) throw new InvalidOperationException($"Throw preview {anchor.Key} arc structure changed: {arcReads} reads");
                var cameraCount = 0;
                var playerCount = 0;
                for (var index = start; index <= end; index++)
                {
                    var instruction = instructions[index];
                    if (instruction.opcode != OpCodes.Ldfld) continue;
                    MethodInfo? replacement = null;
                    if (Equals(instruction.operand, mainCamera))
                    {
                        cameraCount++;
                        replacement = AccessTools.Method(typeof(AimTrialPlugin), nameof(AimCameraForThrownPreview));
                    }
                    else if (Equals(instruction.operand, playerCamera))
                    {
                        playerCount++;
                        replacement = AccessTools.Method(typeof(AimTrialPlugin), nameof(AimPlayerCameraForThrownPreview));
                    }
                    if (replacement == null) continue;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                if (cameraCount != 1 || playerCount != 12)
                    throw new InvalidOperationException($"Throw preview {anchor.Key} camera structure changed: main={cameraCount}, player={playerCount}");
            }
            return instructions;
        }

        private bool UseAlternateAim(scrPlayerControl player)
        {
            var game = scrGameControl.Instance;
            if (game == null || player == null || player.weapon < 0) return false;
            try
            {
                var weaponId = game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID;
                return ((weaponId == 1 || weaponId == 2 || weaponId == 3 || weaponId == 10) && UseControllerAim(player)) ||
                    ((weaponId == 0 || weaponId == 16) && UseThrownWeaponAim(player));
            }
            catch { return false; }
        }

        private bool UseAlternatePlayerCameraAim(scrPlayerControl player)
        {
            if (!UseAlternateAim(player)) return false;
            var game = scrGameControl.Instance;
            if (game == null || player.weapon < 0) return false;
            try
            {
                // Shotgun alternate fire uses playerCamera.forward to apply
                // knockback. Preserve that gameplay direction while mainCam,
                // playerCameraParent, and castBullet use controller aim for the
                // shot and tracer visuals.
                return game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID != 3;
            }
            catch { return false; }
        }

        private static Camera AimCameraForAlt(scrGameControl game)
        {
            var trial = Instance;
            var player = trial?.LocalPlayer();
            return trial != null && player != null && trial.UseAlternateAim(player) ? trial._aimCamera! : game.mainCam;
        }

        private static GameObject AimPlayerCamera(scrPlayerControl player) =>
            Instance != null && Instance.UseControllerAim(player) ? Instance._aimObject! : player.playerCamera;

        private static GameObject AimPlayerCameraParent(scrPlayerControl player) =>
            Instance != null && Instance.UseControllerAim(player) ? Instance._aimObject! : player.playerCameraParent;

        private static GameObject AimPlayerCameraForAlt(scrPlayerControl player) =>
            Instance != null && Instance.UseAlternatePlayerCameraAim(player) ? Instance._aimObject! : player.playerCamera;

        private static GameObject AimPlayerCameraParentForAlt(scrPlayerControl player) =>
            Instance != null && Instance.UseAlternateAim(player) ? Instance._aimObject! : player.playerCameraParent;

        private static bool AimAssistFlag(scrPlayerControl player)
        {
            if (Instance != null && Instance.UseControllerAim(player)) return false;
            return (bool)AccessTools.Field(typeof(scrPlayerControl), "hasAdjustedVector").GetValue(player);
        }

        private scrPlayerControl? LocalPlayer()
        {
            var game = scrGameControl.Instance;
            if (game == null)
            {
                _localPlayer = null;
                return null;
            }
            var activeScene = SceneManager.GetActiveScene();
            // Every controller-aim consumer already requires an active campaign
            // player. Menus/loading cannot supply one; avoid a global search on
            // each reticle/physical-throw update while those scenes are active.
            if (!WorldStereoTrialPlugin.IsGameplayScene(activeScene))
            {
                _localPlayer = null;
                return null;
            }
            if (_localPlayer != null && _localPlayer.gameObject.scene == activeScene &&
                _localPlayer.gameObject.activeInHierarchy && _localPlayer.playerID == game.localPlayerID)
                return _localPlayer;
            _localPlayer = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<scrPlayerControl>())
            {
                if (candidate == null || candidate.gameObject.scene != activeScene ||
                    !candidate.gameObject.activeInHierarchy || candidate.playerID != game.localPlayerID) continue;
                _localPlayer = candidate;
                break;
            }
            return _localPlayer;
        }

        private static IEnumerable<CodeInstruction> RedirectFiringReads(IEnumerable<CodeInstruction> source, MethodBase original)
        {
            var instructions = source.ToList();
            var expected = original.Name == "fireEvent" ? (camera: 114, player: 30, parent: 28, assist: 0) :
                original.Name == "altFireEvent" ? (camera: 58, player: 2, parent: 3, assist: 0) :
                original.Name == "castBullet" ? (camera: 16, player: 4, parent: 0, assist: 1) :
                throw new InvalidOperationException($"Unexpected aim method {original.Name}");
            var cameraCount = 0;
            var playerCount = 0;
            var parentCount = 0;
            var assistCount = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode != OpCodes.Ldfld || instruction.operand is not FieldInfo field) continue;
                MethodInfo? replacement = null;
                if (field.DeclaringType == typeof(scrGameControl) && field.Name == "mainCam")
                {
                    cameraCount++;
                    replacement = AccessTools.Method(typeof(AimTrialPlugin),
                        original.Name == "altFireEvent" ? nameof(AimCameraForAlt) : nameof(AimCamera));
                }
                else if (field.DeclaringType == typeof(scrPlayerControl) && field.Name == "playerCamera")
                {
                    playerCount++;
                    replacement = AccessTools.Method(typeof(AimTrialPlugin),
                        original.Name == "altFireEvent" ? nameof(AimPlayerCameraForAlt) : nameof(AimPlayerCamera));
                }
                else if (field.DeclaringType == typeof(scrPlayerControl) && field.Name == "playerCameraParent")
                {
                    parentCount++;
                    replacement = AccessTools.Method(typeof(AimTrialPlugin),
                        original.Name == "altFireEvent" ? nameof(AimPlayerCameraParentForAlt) : nameof(AimPlayerCameraParent));
                }
                else if (field.DeclaringType == typeof(scrPlayerControl) && field.Name == "hasAdjustedVector")
                {
                    assistCount++;
                    replacement = AccessTools.Method(typeof(AimTrialPlugin), nameof(AimAssistFlag));
                }
                if (replacement == null) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            if (cameraCount != expected.camera || playerCount != expected.player ||
                parentCount != expected.parent || assistCount != expected.assist)
                throw new InvalidOperationException($"{original.Name} camera read count changed: " +
                    $"main={cameraCount}, player={playerCount}, parent={parentCount}, assist={assistCount}");
            return instructions;
        }
    }
}
