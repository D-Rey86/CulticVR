using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CulticVR.AimTrial
{
    internal static class DisposableFiringPatch
    {
        internal static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> source,
            FieldInfo temporaryWeapon, FieldInfo temporaryType, FieldInfo prefabBank, FieldInfo mainCamera,
            FieldInfo playerCamera, FieldInfo playerParent, MethodInfo bullet,
            MethodInfo cameraReplacement, MethodInfo playerReplacement, MethodInfo parentReplacement)
        {
            if (temporaryWeapon == null || temporaryType == null || prefabBank == null || mainCamera == null ||
                playerCamera == null || playerParent == null || bullet == null || cameraReplacement == null ||
                playerReplacement == null || parentReplacement == null)
                throw new MissingMemberException("Disposable firing contract unavailable");
            var instructions = source.ToList();
            var dispatch = -1;
            for (var index = 1; index < instructions.Count - 3; index++)
            {
                if (instructions[index].opcode != OpCodes.Ldfld || !Equals(instructions[index].operand, temporaryType)) continue;
                if (dispatch != -1 || instructions[index - 1].opcode != OpCodes.Ldfld ||
                    !Equals(instructions[index - 1].operand, temporaryWeapon) ||
                    instructions[index + 1].opcode != OpCodes.Stloc_S || instructions[index + 2].opcode != OpCodes.Ldloc_S ||
                    !Equals(instructions[index + 1].operand, instructions[index + 2].operand) || instructions[index + 3].opcode != OpCodes.Switch)
                    throw new InvalidOperationException("Disposable dispatch structure changed");
                dispatch = index + 3;
            }
            if (dispatch == -1 || !(instructions[dispatch].operand is Array entries) || entries.Length != 4 ||
                entries.GetType().GetElementType()?.FullName != "System.Reflection.Emit.Label")
                throw new InvalidOperationException("Disposable dispatch unavailable");
            // Shipping enum MG=1, LP42=2, BBGun=3; label-derived case bounds.
            // Boxed labels bridge installed framework Harmony/netstandard safely.
            var labelField = typeof(CodeInstruction).GetField("labels", BindingFlags.Public | BindingFlags.Instance);
            if (labelField == null) throw new MissingFieldException("Harmony labels unavailable");
            var bounds = new[] { -1, -1, -1 };
            for (var index = dispatch + 1; index < instructions.Count; index++)
            {
                var labels = labelField.GetValue(instructions[index]) as IList ??
                    throw new InvalidOperationException("Harmony label list unavailable");
                for (var slot = 0; slot < bounds.Length; slot++)
                {
                    if (!labels.Contains(entries.GetValue(slot + 1))) continue;
                    if (bounds[slot] != -1) throw new InvalidOperationException("Ambiguous disposable case label");
                    bounds[slot] = index;
                }
            }
            if (bounds[0] <= dispatch || bounds[1] <= bounds[0] || bounds[2] <= bounds[1])
                throw new InvalidOperationException("Disposable case boundaries changed");
            var rewrites = new List<KeyValuePair<CodeInstruction, MethodInfo>>(15);
            for (var slot = 0; slot < 2; slot++)
            {
                var cameras = 0; var players = 0; var parents = 0; var bullets = 0;
                var prefabs = new List<int>(3);
                for (var index = bounds[slot]; index < bounds[slot + 1]; index++)
                {
                    var instruction = instructions[index];
                    if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, bullet)) bullets++;
                    if (instruction.opcode != OpCodes.Ldfld) continue;
                    MethodInfo? replacement = null;
                    if (Equals(instruction.operand, mainCamera)) { cameras++; replacement = cameraReplacement; }
                    else if (Equals(instruction.operand, playerCamera)) { players++; replacement = playerReplacement; }
                    else if (Equals(instruction.operand, playerParent)) { parents++; replacement = parentReplacement; }
                    else if (Equals(instruction.operand, prefabBank))
                    {
                        if (index + 2 >= bounds[slot + 1] || instructions[index + 2].opcode != OpCodes.Ldelem_Ref)
                            throw new InvalidOperationException("Disposable prefab structure changed");
                        if (instructions[index + 1].opcode == OpCodes.Ldc_I4_0) prefabs.Add(0);
                        else if (instructions[index + 1].opcode == OpCodes.Ldc_I4_S) prefabs.Add(Convert.ToInt32(instructions[index + 1].operand));
                        else throw new InvalidOperationException("Disposable prefab index changed");
                    }
                    if (replacement != null) rewrites.Add(new KeyValuePair<CodeInstruction, MethodInfo>(instruction, replacement));
                }
                var expectedPrefabs = slot == 0 ? new[] { 0, 32, 33 } : new[] { 36 };
                if (cameras != (slot == 0 ? 6 : 5) || players != (slot == 0 ? 2 : 0) || parents != (slot == 0 ? 2 : 0) ||
                    bullets != (slot == 0 ? 1 : 0) || !prefabs.SequenceEqual(expectedPrefabs))
                    throw new InvalidOperationException($"Disposable case {slot + 1} firing structure changed");
            }
            // Validate BOTH branches before mutating either; independent owner
            // can cleanly fail back without partially patching accepted paths.
            foreach (var rewrite in rewrites)
            {
                rewrite.Key.opcode = OpCodes.Call;
                rewrite.Key.operand = rewrite.Value;
            }
            return instructions;
        }

        internal static IEnumerable<CodeInstruction> RedirectBulletAdapters(IEnumerable<CodeInstruction> source,
            MethodInfo oldCamera, MethodInfo oldPlayer, MethodInfo oldAssist,
            MethodInfo newCamera, MethodInfo newPlayer, MethodInfo newAssist)
        {
            if (oldCamera == null || oldPlayer == null || oldAssist == null || newCamera == null || newPlayer == null || newAssist == null)
                throw new MissingMemberException("Disposable bullet adapter contract unavailable");
            var instructions = source.ToList();
            var replacements = new List<KeyValuePair<CodeInstruction, MethodInfo>>(21);
            var cameras = 0; var players = 0; var assists = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode != OpCodes.Call) continue;
                MethodInfo? replacement = null;
                if (Equals(instruction.operand, oldCamera)) { cameras++; replacement = newCamera; }
                else if (Equals(instruction.operand, oldPlayer)) { players++; replacement = newPlayer; }
                else if (Equals(instruction.operand, oldAssist)) { assists++; replacement = newAssist; }
                if (replacement != null) replacements.Add(new KeyValuePair<CodeInstruction, MethodInfo>(instruction, replacement));
            }
            if (cameras != 16 || players != 4 || assists != 1)
                throw new InvalidOperationException("Accepted bullet adapters unavailable or ordering changed");
            foreach (var replacement in replacements) replacement.Key.operand = replacement.Value;
            return instructions;
        }
    }
}
