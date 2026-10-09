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
        private Harmony? _bazookaHarmony;
        private bool _bazookaReady;

        private void InitializeBazookaAim()
        {
            if (!_ready) return;
            try
            {
                var update = AccessTools.Method(typeof(scrPlayerControl), "Update");
                if (update == null) throw new MissingMethodException("CULTIC player Update unavailable");
                // Independent patch ownership: a changed temporary-weapon branch
                // must not disable the accepted interaction/throw-preview patch.
                _bazookaHarmony = new Harmony("culticvr.aimtrial.bazooka");
                _bazookaHarmony.Patch(update,
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectBazookaFiringReads))));
                _bazookaReady = true;
            }
            catch (Exception error)
            {
                RemoveBazookaAim();
                Logger.LogWarning($"Bazooka aim patch unavailable; native bazooka aim and other weapon patches retained: {error}");
            }
        }

        private void RemoveBazookaAim()
        {
            _bazookaReady = false;
            try { _bazookaHarmony?.UnpatchSelf(); }
            catch (Exception error) { Logger.LogWarning($"Bazooka aim cleanup failed: {error}"); }
            _bazookaHarmony = null;
        }

        private bool UseBazookaAim(scrPlayerControl player)
        {
            // Temporary weapons are deliberately still rejected by the regular
            // weapon/alternate gates. Only this measured Bazooka branch opts in.
            if (!_bazookaReady || !_ready || player == null || player.tempWeapon == null ||
                player.tempWeapon.tWepType != TempWeapon.TWepType.Bazooka || player.weaponState != 7 ||
                ZoomState(player) != 0 || !WorldStereoTrialPlugin.IsActiveGameplayPlayer(player)) return false;
            var game = scrGameControl.Instance;
            if (game == null || game.connectionStatus != scrGameControl.ConnectionStatus.Offline ||
                player.playerID != game.localPlayerID) return false;
            // Native draw state6 resets zoomState to0 before entering state7.
            // Thus this uses the cached raw pointer, never the FG42 eye ray.
            return UpdatePose(player);
        }

        private static Camera AimCameraForBazooka(scrGameControl game)
        {
            var trial = Instance;
            var player = trial?.LocalPlayer();
            return trial != null && player != null && trial.UseBazookaAim(player) ? trial._aimCamera! : game.mainCam;
        }

        private static IEnumerable<CodeInstruction> RedirectBazookaFiringReads(IEnumerable<CodeInstruction> source, MethodBase original)
        {
            if (original.Name != "Update") throw new InvalidOperationException($"Unexpected bazooka method {original.Name}");
            return BazookaFiringPatch.Redirect(source,
                AccessTools.Field(typeof(scrPlayerControl), "tempWeapon"),
                AccessTools.Field(typeof(TempWeapon), "tWepType"),
                AccessTools.Field(typeof(scrPlayerControl), "prefabBank"),
                AccessTools.Field(typeof(scrGameControl), "mainCam"),
                AccessTools.Method(typeof(AimTrialPlugin), nameof(AimCameraForBazooka)));
        }
    }
}
