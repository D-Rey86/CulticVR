using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace CulticVR.OpenXRControllers
{
    internal static class HapticEventSelectors
    {
        internal static IEnumerable<CodeInstruction> OffhandPickups(IEnumerable<CodeInstruction> source,MethodInfo callback)
        {
            var shield=typeof(scrDroppedShield).GetField("shieldHP")!;
            var heldShield=typeof(scrPlayerControl).GetField("shieldHP")!;
            var draw=typeof(scrPlayerControl).GetMethod("tryDrawOffHandItem")!;
            var result=new List<CodeInstruction>();CodeInstruction? previous=null;int shields=0,moths=0;
            foreach(var i in source){
                result.Add(i);bool accepted=false;
                if(i.opcode==OpCodes.Stfld&&Equals(i.operand,heldShield)&&previous?.opcode==OpCodes.Ldfld&&Equals(previous.operand,shield)){shields++;accepted=true;}
                if((i.opcode==OpCodes.Call||i.opcode==OpCodes.Callvirt)&&Equals(i.operand,draw)&&previous!=null){
                    int value=previous.opcode==OpCodes.Ldc_I4||previous.opcode==OpCodes.Ldc_I4_S?Convert.ToInt32(previous.operand):
                        previous.opcode.Value>=OpCodes.Ldc_I4_0.Value&&previous.opcode.Value<=OpCodes.Ldc_I4_8.Value?previous.opcode.Value-OpCodes.Ldc_I4_0.Value:-1;
                    if(value==(int)scrPlayerControl.OffHandItem.Moth){moths++;accepted=true;}
                }
                if(accepted){result.Add(new CodeInstruction(OpCodes.Ldarg_0));result.Add(new CodeInstruction(OpCodes.Call,callback));}
                previous=i;
            }
            if(shields!=1||moths!=1)throw new InvalidOperationException("Native offhand pickup success seams changed.");
            return result;
        }
        internal static IEnumerable<CodeInstruction> AfterCall(IEnumerable<CodeInstruction> source,MethodInfo target,MethodInfo callback,int argument,int expected)
        {
            var result=new List<CodeInstruction>();int count=0;
            foreach(var i in source){result.Add(i);if((i.opcode==OpCodes.Call||i.opcode==OpCodes.Callvirt)&&Equals(i.operand,target)){
                result.Add(new CodeInstruction(argument==0?OpCodes.Ldarg_0:OpCodes.Ldarg_1));result.Add(new CodeInstruction(OpCodes.Call,callback));count++;
            }}
            if(count!=expected)throw new InvalidOperationException("Native pickup success seam changed.");
            return result;
        }
        internal static IEnumerable<CodeInstruction> CaptureSpawns(IEnumerable<CodeInstruction> source,MethodInfo callback,int expected)
        {
            var result=new List<CodeInstruction>();int count=0;
            foreach(var i in source){result.Add(i);if(i.opcode==OpCodes.Call&&i.operand is MethodInfo m&&m.Name=="Instantiate"&&m.DeclaringType==typeof(UnityEngine.Object)&&m.ReturnType==typeof(GameObject)){
                result.Add(new CodeInstruction(OpCodes.Dup));result.Add(new CodeInstruction(OpCodes.Call,callback));count++;
            }}
            if(count!=expected)throw new InvalidOperationException("Native explosion spawn seam changed: "+count+" / "+expected);
            return result;
        }
    }
}
