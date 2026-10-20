using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace CulticVR.OpenXRControllers
{
    internal static class HelicopterNearMissPatch
    {
        private static CodeInstruction LoadLocal(int index)
        {
            switch(index){case 0:return new CodeInstruction(OpCodes.Ldloc_0);case 1:return new CodeInstruction(OpCodes.Ldloc_1);
                case 2:return new CodeInstruction(OpCodes.Ldloc_2);case 3:return new CodeInstruction(OpCodes.Ldloc_3);}
            return index<256?new CodeInstruction(OpCodes.Ldloc_S,(byte)index):new CodeInstruction(OpCodes.Ldloc,(short)index);
        }
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,MethodBase native,MethodInfo whiz,MethodInfo contact,MethodInfo expiry,MethodInfo moved)
        {
            var original=new List<CodeInstruction>(source);
            var whizField=typeof(scrBullet).GetField("whizPrefab");
            var direction=typeof(scrBullet).GetField("direction",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
            var destroy=typeof(UnityEngine.Object).GetMethod("Destroy",new[]{typeof(UnityEngine.Object)});
            var locals=native.GetMethodBody()!.LocalVariables;
            // Harmony's desktop Label generic ABI is not exposed by netstandard;
            // inspect this cold patch-time contract through non-generic ICollection.
            var labels=typeof(CodeInstruction).GetField("labels")!;
            int whizIndex=-1,spawn=-1,whizzes=0,destructions=0,hit=-1,advance=-1,positionWrites=0;
            foreach(var local in locals)if(local.LocalType==typeof(RaycastHit)&&hit<0)hit=local.LocalIndex;
            for(int i=0;i<original.Count;i++){
                var instruction=original[i];
                if(instruction.opcode==OpCodes.Callvirt&&instruction.operand is MethodInfo setter&&setter.DeclaringType==typeof(Transform)&&setter.Name=="set_position"){
                    if(positionWrites++==0)advance=i;
                }
                if(instruction.opcode==OpCodes.Ldfld&&Equals(instruction.operand,whizField)){whizzes++;whizIndex=i;}
                if(instruction.opcode==OpCodes.Call&&Equals(instruction.operand,destroy)){
                    if(i<2||original[i-2].opcode!=OpCodes.Ldarg_0||!(original[i-1].operand is MethodInfo getter)||getter.Name!="get_gameObject"||((System.Collections.ICollection)labels.GetValue(instruction)!).Count!=0||instruction.blocks.Count!=0)
                        throw new InvalidOperationException("Native bullet destruction seam changed.");
                    destructions++;
                }
            }
            if(whizzes!=1||destructions!=3||hit<0||whizIndex+2>=original.Count||positionWrites!=2||advance<whizIndex)throw new InvalidOperationException("Native bullet whiz/termination/movement contract changed.");
            var array=original[whizIndex+1];
            if(array.opcode!=OpCodes.Ldloc_S&&array.opcode!=OpCodes.Ldloc)throw new InvalidOperationException("Native whiz collider array load changed.");
            int arrayIndex=array.operand is LocalBuilder builder?builder.LocalIndex:array.operand is LocalVariableInfo info?info.LocalIndex:Convert.ToInt32(array.operand);
            if(locals[arrayIndex].LocalType!=typeof(Collider[]))throw new InvalidOperationException("Native whiz collider array type changed.");
            for(int i=whizIndex+1;i<original.Count;i++)if(original[i].opcode==OpCodes.Call&&original[i].operand is MethodInfo method&&method.DeclaringType==typeof(UnityEngine.Object)&&method.Name=="Instantiate"&&method.ReturnType==typeof(GameObject)){spawn=i;break;}
            if(spawn<0||spawn-whizIndex!=9||original[spawn+1].opcode!=OpCodes.Pop)throw new InvalidOperationException("Native bullet whiz spawn changed.");
            var result=new List<CodeInstruction>();int terminal=0;
            for(int i=0;i<original.Count;i++){
                var instruction=original[i];
                if(instruction.opcode==OpCodes.Call&&Equals(instruction.operand,destroy)){
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    if(terminal++<2){result.Add(LoadLocal(hit));result.Add(new CodeInstruction(OpCodes.Call,contact));}
                    else result.Add(new CodeInstruction(OpCodes.Call,expiry));
                }
                result.Add(instruction);
                if(i==advance){result.Add(new CodeInstruction(OpCodes.Ldarg_0));result.Add(new CodeInstruction(OpCodes.Call,moved));}
                if(i==spawn){
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));result.Add(new CodeInstruction(array.opcode,array.operand));
                    result.Add(new CodeInstruction(OpCodes.Ldc_I4_0));result.Add(new CodeInstruction(OpCodes.Ldelem_Ref));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));result.Add(new CodeInstruction(OpCodes.Ldfld,direction));result.Add(new CodeInstruction(OpCodes.Call,whiz));
                }
            }
            return result;
        }
    }
}

