using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using Uuvr;

namespace CulticVR.WorldStereoTrial
{
    public sealed partial class WorldStereoTrialPlugin
    {
        private Harmony? _poseRotationHarmony;
        private static readonly MethodInfo? UnityRotationMethod =
            AccessTools.Method(typeof(InputTracking), nameof(InputTracking.GetLocalRotation), new[] { typeof(XRNode) });

        private void InitializePoseRotationFastPath()
        {
            // Keep UUVR's lifecycle, null guard, camera auto-tracking policy and
            // fresh Update/LateUpdate/before-render reads. Only remove the
            // reflective call plus boxed Quaternion on the recognized path.
            try
            {
                if (UnityRotationMethod == null) return;
                var update = AccessTools.Method(typeof(UuvrPoseDriver), "UpdateTransform");
                if (update == null) return;
                _poseRotationHarmony = new Harmony("culticvr.worldstereotrial.pose-rotation");
                _poseRotationHarmony.Patch(update, transpiler: new HarmonyMethod(
                    AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(InlinePoseRotation))));
            }
            catch (Exception error)
            {
                _poseRotationHarmony?.UnpatchSelf();
                Logger.LogWarning($"UUVR pose optimization unavailable; retaining its original tracking path: {error.Message}");
            }
        }

        private static IEnumerable<CodeInstruction> InlinePoseRotation(IEnumerable<CodeInstruction> source)
        {
            var instructions = new List<CodeInstruction>(source);
            var invoke = AccessTools.Method(typeof(MethodBase), nameof(MethodBase.Invoke),
                new[] { typeof(object), typeof(object[]) });
            var match = -1;
            var count = 0;
            for (var i = 0; i + 1 < instructions.Count; i++)
            {
                if (!instructions[i].Calls(invoke) || instructions[i + 1].opcode != OpCodes.Unbox_Any ||
                    !Equals(instructions[i + 1].operand, typeof(Quaternion))) continue;
                match = i;
                count++;
            }
            if (count != 1)
            {
                _instance?.Logger.LogWarning("UUVR pose method differs from the supported call pattern; retaining its original tracking path.");
                return instructions;
            }
            instructions[match].opcode = OpCodes.Call;
            instructions[match].operand = AccessTools.Method(typeof(WorldStereoTrialPlugin), nameof(ReadPoseRotation));
            // Preserve any labels/exception markers on the old unbox location.
            instructions[match + 1].opcode = OpCodes.Nop;
            instructions[match + 1].operand = null;
            return instructions;
        }

        // Deliberately call the exact legacy API chosen by UUVR. Switching to a
        // different XRNode/device feature would be a tracking behavior change.
#pragma warning disable CS0618
        private static Quaternion ReadPoseRotation(MethodBase method, object? target, object[] arguments)
        {
            if (method == UnityRotationMethod && target == null && arguments != null && arguments.Length == 1 &&
                arguments[0] is int node && node == (int)XRNode.CenterEye)
                return InputTracking.GetLocalRotation(XRNode.CenterEye);
            // Other Unity versions, methods or arguments retain UUVR's exact
            // reflection behavior (including its exception semantics).
            return (Quaternion)method.Invoke(target, arguments);
        }
#pragma warning restore CS0618
    }
}
