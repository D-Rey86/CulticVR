using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CulticVR.OpenXRControllers
{
    internal static class TemporaryHapticPatch
    {
        // Cold patch setup only. Observe the common completed-shot HUD update,
        // inside the native fire gate, after all four disposable weapon cases.
        internal static IEnumerable<CodeInstruction> Insert(IEnumerable<CodeInstruction> source,
            FieldInfo temporary,FieldInfo type,FieldInfo hud,FieldInfo ammo,FieldInfo icon,
            MethodInfo refresh,MethodInfo completed)
        {
            var code=new List<CodeInstruction>(source);
            int dispatch=-1;
            for(int i=1;i<code.Count-3;i++)if(code[i].opcode==OpCodes.Ldfld&&Equals(code[i].operand,type)){
                if(dispatch!=-1||code[i-1].opcode!=OpCodes.Ldfld||!Equals(code[i-1].operand,temporary)||
                    code[i+1].opcode!=OpCodes.Stloc_S||code[i+2].opcode!=OpCodes.Ldloc_S||
                    !Equals(code[i+1].operand,code[i+2].operand)||code[i+3].opcode!=OpCodes.Switch)
                    throw new InvalidOperationException("Temporary haptic dispatch changed");
                dispatch=i+3;
            }
            if(dispatch<0||!(code[dispatch].operand is Array entries)||entries.Length!=4||
                (code[dispatch+1].opcode!=OpCodes.Br&&code[dispatch+1].opcode!=OpCodes.Br_S))
                throw new InvalidOperationException("Temporary haptic cases unavailable");
            var labelField=typeof(CodeInstruction).GetField("labels")??throw new MissingFieldException("Harmony labels unavailable");
            object exit=code[dispatch+1].operand;
            int end=-1;var starts=new int[]{-1,-1,-1,-1};
            for(int i=dispatch+1;i<code.Count;i++){
                var labels=labelField.GetValue(code[i]) as IList??throw new InvalidOperationException("Harmony labels unavailable");
                for(int j=0;j<4;j++)if(labels.Contains(entries.GetValue(j))){
                    if(starts[j]!=-1)throw new InvalidOperationException("Ambiguous disposable case");starts[j]=i;
                }
                if(labels.Contains(exit)){if(end!=-1)throw new InvalidOperationException("Ambiguous disposable exit");end=i;}
            }
            if(starts[0]<=dispatch||starts[1]<=starts[0]||starts[2]<=starts[1]||starts[3]<=starts[2]||end<=starts[3])
                throw new InvalidOperationException("Temporary haptic case order changed");
            // Exact native refreshAmmo(-1, tempWeapon.ammo, tempWeapon.ammoIcon).
            if(end+9>=code.Count||code[end].opcode!=OpCodes.Ldarg_0||
                !Field(code[end+1],hud)||code[end+2].opcode!=OpCodes.Ldc_I4_M1||
                code[end+3].opcode!=OpCodes.Ldarg_0||!Field(code[end+4],temporary)||!Field(code[end+5],ammo)||
                code[end+6].opcode!=OpCodes.Ldarg_0||!Field(code[end+7],temporary)||!Field(code[end+8],icon)||
                code[end+9].opcode!=OpCodes.Callvirt||!Equals(code[end+9].operand,refresh))
                throw new InvalidOperationException("Completed disposable shot seam changed");
            // No original operands, labels or blocks are altered. A branch that
            // skips firing still targets its original instruction after this seam.
            code.InsertRange(end+10,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,completed)});
            return code;
        }
        private static bool Field(CodeInstruction instruction,FieldInfo field)=>
            instruction.opcode==OpCodes.Ldfld&&Equals(instruction.operand,field);
    }
}
