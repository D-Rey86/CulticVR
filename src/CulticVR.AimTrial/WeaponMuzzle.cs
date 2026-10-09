using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CulticVR.AimPreview;
using CulticVR.WorldStereoTrial;
using HarmonyLib;
using UnityEngine;

namespace CulticVR.AimTrial
{
    public sealed partial class AimTrialPlugin
    {
        private Harmony? _muzzleHarmony;
        private bool _muzzleReady;
        private Vector3 _rawMuzzleOrigin;
        private Quaternion _rawMuzzleRotation;
        private Transform? _muzzleTracerTransform;
        private scrBulletTracer? _muzzleTracer;
        private GameObject? _pendingMuzzleTracer;

        private void InitializeWeaponMuzzles()
        {
            if (!_ready) return;
            try
            {
                _muzzleHarmony = new Harmony("culticvr.aimtrial.muzzles");
                var patch = new HarmonyMethod(AccessTools.Method(typeof(AimTrialPlugin), nameof(RedirectWeaponSpawns)))
                {
                    after = new[] { "culticvr.aimtrial", "culticvr.aimtrial.bazooka",
                        "culticvr.aimtrial.disposable", "culticvr.aimtrial.thrownpreview" }
                };
                foreach (var name in new[] { "fireEvent", "altFireEvent", "Update", "spawnShells", "spawnShell",
                    "spawnMagCasing", "spawnMGCasing", "spawnMagLeveringCasing", "spawnMagOHCasing", "spawnSTENMag" })
                {
                    var method = AccessTools.Method(typeof(scrPlayerControl), name);
                    if (method == null) throw new MissingMethodException(name);
                    _muzzleHarmony.Patch(method, transpiler: patch);
                }
                _muzzleReady = true;
            }
            catch (Exception error)
            {
                RemoveWeaponMuzzles();
                Logger.LogWarning($"Weapon origin correction unavailable; accepted aiming retained: {error}");
            }
        }

        private void RemoveWeaponMuzzles()
        {
            _muzzleReady = false;
            _muzzleHarmony?.UnpatchSelf();
            _muzzleHarmony = null;
            _muzzleTracerTransform = null;
            _muzzleTracer = null;
            _pendingMuzzleTracer = null;
        }

        private bool TryWeaponSpawn(scrPlayerControl player, GameObject prefab, out Vector3 point)
        {
            point = default;
            if (!_muzzleReady || !VrSettings.MotionControls || player == null) return false;
            var bank = player.prefabBank;
            // Match actual prefab identity, not names/components/global Instantiate.
            // No thrown axe, dynamite, Molotov, melee or DLC bank opts in.
            var casing = prefab == bank[7] || prefab == bank[12] || prefab == bank[13] ||
                prefab == bank[14] || prefab == bank[33];
            if (!casing && prefab != bank[0] && prefab != bank[9] && prefab != bank[26] &&
                prefab != bank[27] && prefab != bank[31] && prefab != bank[36]) return false;
            // Reject other weapons BEFORE updating any shared aim state.
            if (player.tempWeapon == null)
            {
                var game = scrGameControl.Instance;
                if (game == null || player.weapon < 0 || player.playerID != game.localPlayerID) return false;
                try
                {
                    var id = game.gamePlayers[player.playerID].playerLoadout[player.weapon].weaponTableID;
                    if (id != 1 && id != 2 && id != 3 && id != 4 && id != 5 && id != 9 && id != 10) return false;
                }
                catch { return false; }
            }
            if (!(UseControllerAim(player) || UseBazookaAim(player) || UseDisposableAim(player))) return false;
            if (IsScopedFg42(player))
            {
                // Preserve the selected-eye optical ray; the physical barrel is
                // hidden behind this flat scope. Exit at its accepted crosshair.
                if (casing) return false;
                point = _origin;
                return true;
            }
            return AimPreviewPlugin.TryGetWeaponSpawnPoint(player, _rawMuzzleOrigin,
                _rawMuzzleRotation, casing, out point);
        }

        private static GameObject InstantiateWeaponSpawn(GameObject prefab, Vector3 nativePosition,
            Quaternion nativeRotation, scrPlayerControl player)
        {
            var self = Instance;
            var point = nativePosition;
            var corrected = self != null && self.TryWeaponSpawn(player, prefab, out point);
            if (corrected) nativePosition = point;
            var result = UnityEngine.Object.Instantiate(prefab, nativePosition, nativeRotation);
            if (corrected && prefab == player.prefabBank[0]) self!._pendingMuzzleTracer = result;
            return result;
        }

        private static void SetWeaponTracerDestination(scrBulletTracer tracer, Vector3 destination)
        {
            tracer.destination = destination;
            var self = Instance;
            if (self == null || !self._muzzleReady || self._pendingMuzzleTracer == null ||
                tracer.gameObject != self._pendingMuzzleTracer) return;
            self._pendingMuzzleTracer = null;
            // Reuse the native GetComponent result; never look it up twice.
            self._muzzleTracer = tracer;
            self._muzzleTracerTransform = tracer.transform;
        }

        private static void SetWeaponSpawnRotation(Transform target, Quaternion nativeRotation)
        {
            var self = Instance;
            if (self != null && self._muzzleReady && target == self._muzzleTracerTransform && self._muzzleTracer != null)
            {
                var direction = self._muzzleTracer.destination - target.position;
                if (direction.sqrMagnitude > 0.000001f) nativeRotation = Quaternion.LookRotation(direction);
                self._muzzleTracer = null;
                self._muzzleTracerTransform = null;
            }
            // Real projectiles retain CULTIC's spread, orientation and velocity.
            target.rotation = nativeRotation;
        }

        private static IEnumerable<CodeInstruction> RedirectWeaponSpawns(IEnumerable<CodeInstruction> source, MethodBase original)
        {
            var instantiate = AccessTools.Method(typeof(AimTrialPlugin), nameof(InstantiateWeaponSpawn));
            var destination = AccessTools.Field(typeof(scrBulletTracer), "destination");
            var destinationSetter = AccessTools.Method(typeof(AimTrialPlugin), nameof(SetWeaponTracerDestination));
            var rotationSetter = AccessTools.PropertySetter(typeof(Transform), "rotation");
            var rotationAdapter = AccessTools.Method(typeof(AimTrialPlugin), nameof(SetWeaponSpawnRotation));
            return WeaponSpawnPatch.Redirect(source, original.Name, instantiate, destination,
                destinationSetter, rotationSetter, rotationAdapter, typeof(GameObject));
        }
    }
}
