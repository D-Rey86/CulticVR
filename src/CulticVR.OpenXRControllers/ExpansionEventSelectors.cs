using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
namespace CulticVR.OpenXRControllers
{
    internal static class ExpansionEventSelectors
    {
        internal static IEnumerable<CodeInstruction> Healing(IEnumerable<CodeInstruction> source,MethodInfo accepted)
        {
            var original=new List<CodeInstruction>(source);var result=new List<CodeInstruction>();int count=0;
            for(int i=0;i<original.Count;i++){
                var instruction=original[i];result.Add(instruction);
                if(instruction.opcode!=OpCodes.Stfld||!(instruction.operand is FieldInfo hp)||hp.DeclaringType?.Name!="scrPlayer"||hp.Name!="playerHP"||hp.FieldType!=typeof(int))continue;
                if(i<3||original[i-1].opcode!=OpCodes.Add||original[i-3].opcode!=OpCodes.Ldfld||!Equals(original[i-3].operand,hp))continue;
                var value=original[i-2];
                if(value.opcode!=OpCodes.Ldloc&&value.opcode!=OpCodes.Ldloc_S&&value.opcode!=OpCodes.Ldloc_0&&value.opcode!=OpCodes.Ldloc_1&&value.opcode!=OpCodes.Ldloc_2&&value.opcode!=OpCodes.Ldloc_3)continue;
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));result.Add(new CodeInstruction(value.opcode,value.operand));result.Add(new CodeInstruction(OpCodes.Call,accepted));count++;
            }
            if(count!=2)throw new InvalidOperationException("Native accepted field-kit heal seams changed.");
            return result;
        }
    }
}
