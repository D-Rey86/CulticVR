using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    public sealed partial class WorldStereoTrialPlugin
    {
        private Harmony? _headViewHarmony;
        private GameObject? _headViewObject;
        private int _headViewFrame = -1;
        private bool _headViewValid;
        private Vector3 _headViewOffset;
        private Transform? _headViewLight;
        private Quaternion _headViewLightLocalRotation;

        private void InitializeHeadViewFollowers()
        {
            _headViewObject = new GameObject("CulticVR.HeadViewPose");
            _headViewObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_headViewObject);
            _headViewHarmony = new Harmony("culticvr.worldstereotrial.headview");
            try
            {
                var patch = new HarmonyMethod(typeof(WorldStereoTrialPlugin), nameof(RedirectHeadViewReads));
                foreach (var name in new[] { "Update", "grabObject", "throwObject" })
                    _headViewHarmony.Patch(AccessTools.Method(typeof(scrPlayerControl), name), transpiler: patch);
            }
            catch (Exception error)
            {
                _headViewHarmony.UnpatchSelf();
                _headViewHarmony = null;
                Logger.LogError("Head-view followers could not match native carry/flashlight code: " + error);
            }
        }

        private static IEnumerable<CodeInstruction> RedirectHeadViewReads(IEnumerable<CodeInstruction> code, MethodBase original) =>
            HeadViewReadPatch.Rewrite(code, original.Name,
                AccessTools.Field(typeof(scrPlayerControl), "playerCamera"),
                AccessTools.Field(typeof(scrPlayerControl), "playerCameraParent"),
                AccessTools.Field(typeof(scrPlayerControl), "grabCamOrigin"),
                AccessTools.Field(typeof(scrPlayerControl), "playerFlashlightObject"),
                AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(HeadViewCamera)),
                AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(HeadViewCarryPosition)));

        private bool CanFollowHead(scrPlayerControl player)
        {
            var game = scrGameControl.Instance;
            return isActiveAndEnabled && _headViewHarmony != null && _headViewObject != null && _reportedReady &&
                _player == player && _source != null && _source == player.playerCameraComponent &&
                _stereo != null && _stereo.isActiveAndEnabled && _stereo.stereoEnabled &&
                game != null && game.connectionStatus == scrGameControl.ConnectionStatus.Offline &&
                player.playerID == game.localPlayerID && !player.isDead &&
                player.playerHUD != null && player.playerHUD.cutsceneState == 0;
        }

        private bool TryHeadView(scrPlayerControl player)
        {
            if (!CanFollowHead(player)) return false;
            if (_headViewFrame == Time.frameCount) return _headViewValid;
            _headViewFrame = Time.frameCount;
            _headViewValid = false;
            if (!VrHandPose.TryHead(out _, out var rotation)) return false;
            var facing = Quaternion.Euler(0f, _source!.transform.eulerAngles.y, 0f) * TrackingYawCompensation;
            _headViewOffset = facing * TrackingPositionCompensation;
            _headViewObject!.transform.SetPositionAndRotation(_source.transform.position + _headViewOffset, facing * rotation);
            _headViewValid = true;
            return true;
        }

        private static GameObject HeadViewCamera(scrPlayerControl player) =>
            _instance != null && _instance.TryHeadView(player) ? _instance._headViewObject! : player.playerCamera;

        private static Vector3 HeadViewCarryPosition(scrPlayerControl player) =>
            player.playerCameraParent.transform.position +
            (_instance != null && _instance.TryHeadView(player) ? _instance._headViewOffset : Vector3.zero);

        private void ApplyHeadViewFlashlight(Quaternion facing)
        {
            var player = _player;
            if (player == null || !player.hasFlashlight || !TryHeadView(player) || player.playerFlashlightObject == null)
            {
                RestoreHeadViewLight();
                return;
            }
            var light = player.playerFlashlightObject.transform;
            if (_headViewLight != light)
            {
                RestoreHeadViewLight();
                // The native prefab's light is an identity-rotation camera child.
                // Preserve the authored local orientation rather than assuming it.
                if (light.parent != player.playerCamera.transform) return;
                _headViewLight = light;
                _headViewLightLocalRotation = light.localRotation;
            }
            light.rotation = facing * _headViewLightLocalRotation;
        }

        private void RestoreHeadViewLight()
        {
            if (_headViewLight != null) _headViewLight.localRotation = _headViewLightLocalRotation;
            _headViewLight = null;
        }
        private void ResetHeadViewFollowers()
        {
            RestoreHeadViewLight();
            _headViewFrame = -1;
            _headViewValid = false;
        }
        private void OnDisable() => ResetHeadViewFollowers();
        private void RemoveHeadViewFollowers()
        {
            ResetHeadViewFollowers();
            _headViewHarmony?.UnpatchSelf();
            _headViewHarmony = null;
            if (_headViewObject != null) Destroy(_headViewObject);
            _headViewObject = null;
        }
    }
}
