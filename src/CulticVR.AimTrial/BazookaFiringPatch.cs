using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CulticVR.AimTrial
{
    internal static class BazookaFiringPatch
    {
        // Setup-time only. Preserve every instruction, branch label and exception
        // block; replace exactly the four camera loads in the native Bazooka case.
        internal static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> source,
            FieldInfo temporaryWeapon, FieldInfo temporaryType, FieldInfo prefabBank,
            FieldInfo mainCamera, MethodInfo replacement)
        {
            if (temporaryWeapon == null || temporaryType == null || prefabBank == null || mainCamera == null || replacement == null)
                throw new MissingMemberException("Bazooka firing contract unavailable");
            var instructions = source.ToList();
            var dispatch = -1;
            for (var index = 1; index < instructions.Count - 3; index++)
            {
                if (instructions[index].opcode != OpCodes.Ldfld || !Equals(instructions[index].operand, temporaryType)) continue;
                if (dispatch != -1 || instructions[index - 1].opcode != OpCodes.Ldfld ||
                    !Equals(instructions[index - 1].operand, temporaryWeapon) ||
                    instructions[index + 1].opcode != OpCodes.Stloc_S || instructions[index + 2].opcode != OpCodes.Ldloc_S ||
                    !Equals(instructions[index + 1].operand, instructions[index + 2].operand) ||
                    instructions[index + 3].opcode != OpCodes.Switch)
                    throw new InvalidOperationException("Temporary weapon dispatch structure changed");
                dispatch = index + 3;
            }
            // The installed framework-targeted Harmony exposes Label through
            // mscorlib; use its boxed values without a netstandard Label cast.
            if (dispatch == -1 || !(instructions[dispatch].operand is Array entries) || entries.Length != 4 ||
                entries.GetType().GetElementType()?.FullName != "System.Reflection.Emit.Label")
                throw new InvalidOperationException("Temporary weapon dispatch unavailable");
            // Shipping TWepType enum: Bazooka=0, MG=1. Derive actual instruction
            // boundaries from those switch labels, never from a guessed window.
            var labelField = typeof(CodeInstruction).GetField("labels", BindingFlags.Public | BindingFlags.Instance);
            if (labelField == null) throw new MissingFieldException("Harmony instruction labels unavailable");
            var start = -1;
            var end = -1;
            for (var index = dispatch + 1; index < instructions.Count; index++)
            {
                var labels = labelField.GetValue(instructions[index]) as IList ??
                    throw new InvalidOperationException("Harmony instruction label list unavailable");
                if (labels.Contains(entries.GetValue(0)))
                {
                    if (start != -1) throw new InvalidOperationException("Ambiguous Bazooka case label");
                    start = index;
                }
                if (labels.Contains(entries.GetValue(1)))
                {
                    if (end != -1) throw new InvalidOperationException("Ambiguous MG case label");
                    end = index;
                }
            }
            if (start <= dispatch || end <= start)
                throw new InvalidOperationException("Bazooka case boundaries changed");
            var cameras = new List<CodeInstruction>(4);
            var prefabs = 0;
            for (var index = start; index < end; index++)
            {
                var instruction = instructions[index];
                if (instruction.opcode != OpCodes.Ldfld) continue;
                if (Equals(instruction.operand, mainCamera)) cameras.Add(instruction);
                if (!Equals(instruction.operand, prefabBank)) continue;
                if (index + 2 >= end || instructions[index + 1].opcode != OpCodes.Ldc_I4_S ||
                    Convert.ToInt32(instructions[index + 1].operand) != 31 || instructions[index + 2].opcode != OpCodes.Ldelem_Ref)
                    throw new InvalidOperationException("Bazooka projectile prefab changed");
                prefabs++;
            }
            if (cameras.Count != 4 || prefabs != 1)
                throw new InvalidOperationException($"Bazooka firing structure changed: cameras={cameras.Count}, prefabs={prefabs}");
            // Validate the entire branch before modifying anything, so rejection
            // cannot leave a partial camera rewrite in another transpiler's list.
            foreach (var instruction in cameras)
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            return instructions;
        }
    }
}
