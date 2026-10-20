using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CulticVR.WorldStereoTrial
{
    // Select only the native carry/flashlight expressions. Never redirect the
    // whole Update camera: its other reads own movement, weapons and abilities.
    internal static class HeadViewReadPatch
    {
        private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels")!;
        private static bool HasLabels(CodeInstruction code) =>
            ((System.Collections.ICollection)Labels.GetValue(code)!).Count != 0;
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,
            string methodName, FieldInfo camera, FieldInfo parent, FieldInfo grabOrigin,
            FieldInfo flashlight, MethodInfo view, MethodInfo carryPosition)
        {
            var code = source.Select(i => new CodeInstruction(i)).ToList();
            var reads = new List<int>();
            int anchor = -1;
            if (methodName == "Update")
            {
                var origins = Indices(code, grabOrigin);
                if (origins.Count != 1) throw new InvalidOperationException("Carry rotation anchor changed");
                int end = SetterAfter(code, origins[0], "set_rotation");
                for (int i = origins[0] - 1; i >= 0; i--)
                    if (Reads(code[i], parent)) { anchor = i; break; }
                if (anchor < 0 || origins[0] - anchor > 100 ||
                    !Calls(code[anchor + 1], "get_transform") || !Calls(code[anchor + 2], "get_position") ||
                    HasLabels(code[anchor + 1]) || HasLabels(code[anchor + 2]) ||
                    code[anchor + 1].blocks.Count != 0 || code[anchor + 2].blocks.Count != 0)
                    throw new InvalidOperationException("Carry position anchor changed");
                reads.AddRange(Enumerable.Range(anchor, end - anchor).Where(i => Reads(code[i], camera)));
                if (reads.Count != 5) throw new InvalidOperationException("Carry camera expression changed");

                var lights = Indices(code, flashlight);
                if (lights.Count != 2 || lights[0] <= end)
                    throw new InvalidOperationException("Flashlight position anchor changed");
                int lightEnd = SetterAfter(code, lights[1], "set_position");
                var lightReads = Enumerable.Range(lights[0], lightEnd - lights[0])
                    .Where(i => Reads(code[i], camera)).ToList();
                if (lightReads.Count != 2) throw new InvalidOperationException("Flashlight camera expression changed");
                reads.AddRange(lightReads);
            }
            else if (methodName == "grabObject" || methodName == "throwObject")
            {
                reads = Indices(code, camera);
                if (reads.Count != 1) throw new InvalidOperationException("Carry event camera expression changed");
            }
            else throw new InvalidOperationException("Unexpected head-view patch target");

            // All anchors/counts have passed before modifying the cloned body.
            foreach (int index in reads) { code[index].opcode = OpCodes.Call; code[index].operand = view; }
            if (anchor >= 0)
            {
                code[anchor].opcode = OpCodes.Call; code[anchor].operand = carryPosition;
                for (int i = anchor + 1; i <= anchor + 2; i++) { code[i].opcode = OpCodes.Nop; code[i].operand = null; }
            }
            return code;
        }
        private static bool Reads(CodeInstruction i, FieldInfo field) => i.opcode == OpCodes.Ldfld && Equals(i.operand, field);
        private static List<int> Indices(List<CodeInstruction> code, FieldInfo field) =>
            Enumerable.Range(0, code.Count).Where(i => Reads(code[i], field)).ToList();
        private static bool Calls(CodeInstruction i, string name) =>
            (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && i.operand is MethodInfo m && m.Name == name;
        private static int SetterAfter(List<CodeInstruction> code, int start, string name)
        {
            for (int i = start + 1; i < Math.Min(code.Count, start + 100); i++)
                if (Calls(code[i], name)) return i;
            throw new InvalidOperationException("Head-view expression setter missing");
        }
    }
}
