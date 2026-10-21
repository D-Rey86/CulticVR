using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace CulticVR.OpenXRControllers
{
    // Freight lifts shake only at their endpoints. Observe the native passenger
    // displacement instead of polling every elevator or adding contact queries.
    internal static class ElevatorRideHooks
    {
        private sealed class LiftBinding
        {
            internal Transform? Parent;
            internal scrFreightElevatorPlatform? Lift;
        }
        private static ConditionalWeakTable<scrMovingPlatform,LiftBinding> _bindings = new ConditionalWeakTable<scrMovingPlatform,LiftBinding>();
        private static Harmony? _harmony;
        private static Action<scrPlayerControl,float>? _notify;

        internal static void Install(Action<scrPlayerControl,float> notify)
        {
            _notify=notify;
            _harmony=new Harmony("culticvr.haptics.elevator-ride");
            _harmony.Patch(typeof(scrMovingPlatform).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly),
                transpiler:new HarmonyMethod(typeof(ElevatorRideHooks),nameof(Rewrite)));
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code=new List<CodeInstruction>(instructions);
            var setter=typeof(Transform).GetProperty("position")!.SetMethod;
            var getter=typeof(Transform).GetProperty("position")!.GetMethod;
            var transform=typeof(scrPlayerControl).GetProperty("transform")!.DeclaringType!.GetProperty("transform")!.GetMethod;
            var parent=typeof(scrMovingPlatform).GetField("parentObject",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var last=typeof(scrMovingPlatform).GetField("lastPos",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var add=typeof(Vector3).GetMethod("op_Addition",new[]{typeof(Vector3),typeof(Vector3)});
            var subtract=typeof(Vector3).GetMethod("op_Subtraction",new[]{typeof(Vector3),typeof(Vector3)});
            var wrapper=typeof(ElevatorRideHooks).GetMethod(nameof(MoveRider),BindingFlags.Static|BindingFlags.NonPublic);
            var result=new List<CodeInstruction>(code.Count+2);int count=0;
            for(int i=0;i<code.Count;i++){
                // rider.position += parentObject.position - lastPos; exactly
                // two sites (path-driven and externally driven platforms).
                if(i>=10&&Calls(code[i],setter)&&Calls(code[i-1],add)&&Calls(code[i-2],subtract)&&
                    code[i-3].opcode==OpCodes.Ldfld&&Equals(code[i-3].operand,last)&&code[i-4].opcode==OpCodes.Ldarg_0&&
                    Calls(code[i-5],getter)&&code[i-6].opcode==OpCodes.Ldfld&&Equals(code[i-6].operand,parent)&&
                    code[i-7].opcode==OpCodes.Ldarg_0&&Calls(code[i-8],getter)&&code[i-9].opcode==OpCodes.Dup&&Calls(code[i-10],transform)){
                    var load=new CodeInstruction(code[i]) { opcode=OpCodes.Ldarg_0, operand=null };
                    result.Add(load);result.Add(new CodeInstruction(OpCodes.Call,wrapper));count++;
                }else result.Add(code[i]);
            }
            if(count!=2)throw new InvalidOperationException("Native elevator passenger displacement sites changed");
            return result;
        }

        private static bool Calls(CodeInstruction code,MethodInfo? method)=>(code.opcode==OpCodes.Call||code.opcode==OpCodes.Callvirt)&&Equals(code.operand,method);

        internal static void MoveRider(Transform rider,Vector3 position,scrMovingPlatform platform)
        {
            var player=HapticProducerTags.Enabled?scrGameControl.Instance?.hudScript?.playerScript:null;
            bool local=player!=null&&rider==player.transform;
            var before=local?rider.position:default;
            rider.position=position; // Preserve the native operation exactly once, even when feedback is disabled.
            if(!local||_notify==null||!platform.isStationary||platform.parentObject==null)return;
            var delta=position-before;
            float distanceSquared=delta.x*delta.x+delta.y*delta.y+delta.z*delta.z;
            if(distanceSquared<=0||float.IsNaN(distanceSquared)||float.IsInfinity(distanceSquared))return;
            if(!_bindings.TryGetValue(platform,out var binding)){
                binding=new LiftBinding();_bindings.Add(platform,binding);
            }
            if(binding.Parent!=platform.parentObject){
                binding.Parent=platform.parentObject;
                binding.Lift=binding.Parent.GetComponent<scrFreightElevatorPlatform>();
            }
            if(binding.Lift==null||binding.Lift.state!=2)return;
            // Reuse the existing bounded, lower-front/back world-rumble output.
            // This value describes machinery vibration, not damage or an explosion.
            _notify(player!,1f);
        }

        internal static void Shutdown()
        {
            _harmony?.UnpatchSelf();_harmony=null;_notify=null;
            _bindings=new ConditionalWeakTable<scrMovingPlatform,LiftBinding>();
        }
    }
}
