using System;
using System.Collections.Generic;
using System.Reflection;
using CulticVR.WorldStereoTrial;
using HarmonyLib;
using UnityEngine;

namespace CulticVR.AimTrial
{
    public sealed partial class AimTrialPlugin
    {
        private Harmony? _disposableHarmony;
        private bool _disposableReady;

        private void InitializeDisposableAim()
        {
            if (!_ready) return;
            try
            {
                var update = AccessTools.Method(typeof(scrPlayerControl), "Update");
                var bullet = AccessTools.Method(typeof(scrPlayerControl), "castBullet");
                if (update == null || bullet == null) throw new MissingMethodException("CULTIC disposable firing methods unavailable");
                _disposableHarmony = new Harmony("culticvr.aimtrial.disposable");
                _disposableHarmony.Patch(update,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectDisposableFiringReads))));
                // Extend the already validated bullet adapters AFTER their owner;
                // do not replace/repatch native fireEvent or any regular weapon gate.
                var bulletPatch = new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectDisposableBulletReads)))
                {
                    after = new[] { "culticvr.aimtrial" }
                };
                _disposableHarmony.Patch(bullet, transpiler: bulletPatch);
                _disposableReady = true;
            }
            catch (Exception error)
            {
                RemoveDisposableAim();
                Logger.LogWarning($"Disposable MG/flare aim unavailable; native disposable aim and accepted weapon patches retained: {error}");
            }
        }

        private void RemoveDisposableAim()
        {
            _disposableReady = false;
            try { _disposableHarmony?.UnpatchSelf(); }
            catch (Exception error) { Logger.LogWarning($"Disposable aim cleanup failed: {error}"); }
            _disposableHarmony = null;
        }

        private bool UseDisposableAim(scrPlayerControl player)
        {
            if (!_disposableReady || !_ready || player == null || player.tempWeapon == null ||
                (player.tempWeapon.tWepType != TempWeapon.TWepType.MG && player.tempWeapon.tWepType != TempWeapon.TWepType.LP42) ||
                player.weaponState != 7 || ZoomState(player) != 0 ||
                !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player)) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID) return false;
            // Native draw state6 resets zoom before state7; use the shared raw
            // right pointer. Do not enter FG42's eye-through-crosshair calculation.
            return UpdatePose(player);
        }

        private bool UseDisposableHitscanAim(scrPlayerControl player) =>
            player != null && player.tempWeapon != null && player.tempWeapon.tWepType == TempWeapon.TWepType.MG &&
            UseDisposableAim(player);

        private static Camera AimCameraForDisposable(scrGameControl game)
        {
            var trial = Instance;
            var player = trial?.LocalPlayer();
            return trial != null && player != null && trial.UseDisposableAim(player) ? trial._aimCamera! : game.mainCam;
        }

        private static GameObject AimPlayerCameraForDisposable(scrPlayerControl player) =>
            Instance != null && Instance.UseDisposableAim(player) ? Instance._aimObject! : player.playerCamera;

        private static GameObject AimPlayerCameraParentForDisposable(scrPlayerControl player) =>
            Instance != null && Instance.UseDisposableAim(player) ? Instance._aimObject! : player.playerCameraParent;

        private static Camera AimCameraForDisposableBullet(scrGameControl game)
        {
            // Same fallback precedence as the accepted AimCamera, with ONE cached
            // player lookup. Ordinary supported weapons short-circuit before MG.
            var trial = Instance;
            var player = trial?.LocalPlayer();
            if (trial != null && player != null && trial.UsePendingPhysicalThrowAim(player))
                return trial._aimCamera!;
            return trial != null && player != null &&
                (trial.UseControllerAim(player) || trial.UseDisposableHitscanAim(player)) ? trial._aimCamera! : game.mainCam;
        }

        private static GameObject AimPlayerCameraForDisposableBullet(scrPlayerControl player) =>
            Instance != null && (Instance.UseControllerAim(player) || Instance.UseDisposableHitscanAim(player)) ?
                Instance._aimObject! : player.playerCamera;

        private static bool AimAssistFlagForDisposableBullet(scrPlayerControl player)
        {
            if (Instance != null && Instance.UseDisposableHitscanAim(player)) return false;
            return AimAssistFlag(player);
        }

        private static IEnumerable<CodeInstruction> RedirectDisposableFiringReads(IEnumerable<CodeInstruction> source, MethodBase original)
        {
            if (original.Name != "Update") throw new InvalidOperationException($"Unexpected disposable method {original.Name}");
            return DisposableFiringPatch.Redirect(source,
                AccessTools.Field(typeof(scrPlayerControl), "tempWeapon"), AccessTools.Field(typeof(TempWeapon), "tWepType"),
                AccessTools.Field(typeof(scrPlayerControl), "prefabBank"), AccessTools.Field(typeof(scrGameControl), "mainCam"),
                AccessTools.Field(typeof(scrPlayerControl), "playerCamera"), AccessTools.Field(typeof(scrPlayerControl), "playerCameraParent"),
                AccessTools.Method(typeof(scrPlayerControl), "castBullet"),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimCameraForDisposable)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimPlayerCameraForDisposable)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimPlayerCameraParentForDisposable)));
        }

        private static IEnumerable<CodeInstruction> RedirectDisposableBulletReads(IEnumerable<CodeInstruction> source, MethodBase original)
        {
            if (original.Name != "castBullet") throw new InvalidOperationException($"Unexpected disposable bullet method {original.Name}");
            return DisposableFiringPatch.RedirectBulletAdapters(source,
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimCamera)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimPlayerCamera)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimAssistFlag)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimCameraForDisposableBullet)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimPlayerCameraForDisposableBullet)),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimAssistFlagForDisposableBullet)));
        }
    }
}
