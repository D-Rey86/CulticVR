using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CulticVR.AimTrial
{
    internal static class WeaponSpawnPatch
    {
        internal static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> source, string original,
            MethodInfo instantiate, FieldInfo destination, MethodInfo destinationSetter,
            MethodInfo rotationSetter, MethodInfo rotationAdapter, Type gameObject)
        {
            var input = source.ToList();
            var expected = original == "fireEvent" ? 23 : original == "altFireEvent" ? 9 : original == "Update" ? 16 : 1;
            var expectedTracers = original == "fireEvent" ? 9 : original == "altFireEvent" ? 3 : original == "Update" ? 2 : 0;
            var measuredSpawns = 0;
            var measuredTracers = 0;
            var pendingRotation = false;
            // Validate the COMPLETE native contract before mutating any opcode,
            // operand or metadata; failure cannot leave a half-rewritten body.
            foreach (var code in input)
            {
                if (IsSpawn(code, gameObject)) measuredSpawns++;
                if (code.opcode == OpCodes.Stfld && Equals(code.operand, destination))
                {
                    if (pendingRotation) throw new InvalidOperationException("Unpaired tracer destination");
                    pendingRotation = true;
                    measuredTracers++;
                }
                else if (pendingRotation && code.Calls(rotationSetter)) pendingRotation = false;
            }
            if (measuredSpawns != expected || measuredTracers != expectedTracers || pendingRotation)
                throw new InvalidOperationException($"Changed spawn/tracer contract: {original}");
            var output = new List<CodeInstruction>();
            var count = 0;
            var tracerCount = 0;
            var awaitingTracerRotation = false;
            foreach (var instruction in input)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
                    method.DeclaringType?.FullName == "UnityEngine.Object" && method.Name == "Instantiate" &&
                    method.IsGenericMethod && method.GetGenericArguments()[0] == gameObject)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 3 && parameters[0].ParameterType == gameObject &&
                        parameters[1].ParameterType.FullName == "UnityEngine.Vector3" &&
                        parameters[2].ParameterType.FullName == "UnityEngine.Quaternion")
                    {
                        var receiver = new CodeInstruction(OpCodes.Ldarg_0);
                        // Shipping Harmony exposes Mono mscorlib's Label type.
                        var labelsField = typeof(CodeInstruction).GetField("labels")!;
                        var labels = (System.Collections.IList)labelsField.GetValue(instruction)!;
                        var receiverLabels = (System.Collections.IList)labelsField.GetValue(receiver)!;
                        foreach (var label in labels) receiverLabels.Add(label);
                        labels.Clear();
                        receiver.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                        output.Add(receiver);
                        instruction.operand = instantiate;
                        count++;
                    }
                }
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, destination))
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = destinationSetter;
                    if (awaitingTracerRotation) throw new InvalidOperationException("Unpaired tracer destination");
                    awaitingTracerRotation = true;
                    tracerCount++;
                }
                else if (awaitingTracerRotation && instruction.Calls(rotationSetter))
                {
                    instruction.opcode = OpCodes.Call; instruction.operand = rotationAdapter;
                    awaitingTracerRotation = false;
                }
                output.Add(instruction);
            }
            if (count != expected || tracerCount != expectedTracers || awaitingTracerRotation)
                throw new InvalidOperationException($"Missing measured spawn/tracer calls: {original}");
            return output;
        }

        private static bool IsSpawn(CodeInstruction code, Type gameObject)
        {
            if (code.opcode != OpCodes.Call || !(code.operand is MethodInfo method) ||
                method.DeclaringType?.FullName != "UnityEngine.Object" || method.Name != "Instantiate" ||
                !method.IsGenericMethod || method.GetGenericArguments()[0] != gameObject) return false;
            var args = method.GetParameters();
            return args.Length == 3 && args[0].ParameterType == gameObject &&
                args[1].ParameterType.FullName == "UnityEngine.Vector3" &&
                args[2].ParameterType.FullName == "UnityEngine.Quaternion";
        }
    }
}
