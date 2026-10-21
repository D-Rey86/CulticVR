using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace CulticVR.OpenXRControllers
{
    internal static class WorldRumbleHooks
    {
        internal readonly struct Producer
        {
            internal readonly Type Type;internal readonly string Method;internal readonly int Add,Set,Player;
            internal Producer(Type type,string method,int add=0,int set=0,int player=0){Type=type;Method=method;Add=add;Set=set;Player=player;}
        }
        internal static readonly Producer[] Catalog={
            new Producer(typeof(scrE1M1EarthquakeTrigger),"Update",player:1),
            new Producer(typeof(scrE1M2VortexTrigger),"Update",player:1),
            new Producer(typeof(scrTimedShake),"Update",set:1),
            new Producer(typeof(scrE2M3CaveCollapseTrigger),"Update",set:2),
            new Producer(typeof(scrE2M3SluiceGate),"Update",set:1),
            new Producer(typeof(scrE2M11FloodTrigger),"Update",set:1),
            new Producer(typeof(scrE2M0ElevatorButton),"Update",add:2),
            new Producer(typeof(scrE2M7BridgeController),"Update",add:1),
            new Producer(typeof(scrE2M9TrainController),"Update",add:1),
            new Producer(typeof(scrE2M9TrainController),"updateTrainState",add:1),
            new Producer(typeof(scrEBM1MausoleumEyePuzzle),"Update",add:2,set:1),
            new Producer(typeof(scrFreightElevator),"FixedUpdate",add:2),
            new Producer(typeof(scrFreightElevatorPlatform),"FixedUpdate",add:2),
            new Producer(typeof(scrOfficeElevator),"FixedUpdate",add:2),
            new Producer(typeof(scrDumbwaiter),"FixedUpdate",add:2),
            new Producer(typeof(scrE2M2RollingDoorPanel),"Update",add:1),
            // All remaining non-cinematic environmental shake producers in the
            // shipping assembly. Selection is by native event, never scene name.
            new Producer(typeof(scrDoor),"Update",add:2),
            new Producer(typeof(scrE1M5SkeletonTrigger),"Update",add:1),
            new Producer(typeof(scrE1M9ValveStand),"Update",add:2),
            new Producer(typeof(scrE2M0RooftopExplosion),"Update",add:1),
            new Producer(typeof(scrE2M10Statute),"Update",add:1),
            new Producer(typeof(scrE2M11PortTrigger),"Update",add:2),
            new Producer(typeof(scrE2M12EnergyBarrier),"Update",set:1),
            new Producer(typeof(scrE2M1ManagerKeyTrigger),"Update",add:2),
            new Producer(typeof(scrE2M2NightmareSwitch),"Update",set:1),
            new Producer(typeof(scrE2M2NightmareSwitch),"enemyKilled",add:1),
            new Producer(typeof(scrE2M4BruteGarageTrigger),"Update",set:3),
            new Producer(typeof(scrE2M4FreezerFight),"Update",add:2)
        };
        private static readonly Dictionary<MethodBase,Producer> Producers=new Dictionary<MethodBase,Producer>();
        [ThreadStatic] private static bool _scoped;
        [ThreadStatic] private static bool _restrained;
        private static Action<scrPlayerControl,float>? _notify;
        private static Action? _impact;
        private static Action<GameObject>? _blast;
        private static Harmony? _harmony;
        internal static void Install(Action<scrPlayerControl,float> notify,Action impact,Action<GameObject> blast)
        {
            _notify=notify;_impact=impact;_blast=blast;_harmony=new Harmony("culticvr.haptics.world-rumble");Producers.Clear();
            foreach(var p in Catalog){
                var method=p.Type.GetMethod(p.Method,BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    ??throw new MissingMethodException(p.Type.FullName,p.Method);
                Producers[method]=p;_harmony.Patch(method,transpiler:new HarmonyMethod(typeof(WorldRumbleHooks),nameof(Transpile)));
            }
            _harmony.Patch(AccessTools.DeclaredMethod(typeof(scrPlayerControl),"addShake"),postfix:new HarmonyMethod(typeof(WorldRumbleHooks),nameof(Accepted)));
            _harmony.Patch(AccessTools.DeclaredMethod(typeof(scrPlayerControl),"setShake"),postfix:new HarmonyMethod(typeof(WorldRumbleHooks),nameof(Accepted)));
            foreach(var type in new[]{typeof(scrE2M5BarnWalls),typeof(scrE2M1HarvesterTrigger)})
                _harmony.Patch(AccessTools.DeclaredMethod(type,"Update"),transpiler:new HarmonyMethod(typeof(WorldRumbleHooks),nameof(WallTranspile)));
            foreach(var type in new[]{typeof(scrE1M5Plunger),typeof(scrE2M3Plunger)})
                _harmony.Patch(AccessTools.DeclaredMethod(type,"Update"),transpiler:new HarmonyMethod(typeof(WorldRumbleHooks),nameof(PlungerTranspile)));
        }
        // These two destruction events have no native shake. Publish only at
        // their one-shot state transition, not in an Update prefix or on save load.
        private static IEnumerable<CodeInstruction> WallTranspile(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)=>RewriteWall(instructions,__originalMethod.DeclaringType!);
        internal static IEnumerable<CodeInstruction> RewriteWall(IEnumerable<CodeInstruction> instructions,Type type)
        {
            var callback=typeof(WorldRumbleHooks).GetMethod(type==typeof(scrE2M5BarnWalls)?nameof(BarnBroken):nameof(HarvesterBroken),BindingFlags.Static|BindingFlags.NonPublic)!;
            return AfterStateTwo(instructions,type,callback);
        }
        private static IEnumerable<CodeInstruction> PlungerTranspile(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)=>RewritePlunger(instructions,__originalMethod.DeclaringType!);
        internal static IEnumerable<CodeInstruction> RewritePlunger(IEnumerable<CodeInstruction> instructions,Type type)
        {
            var callback=typeof(WorldRumbleHooks).GetMethod(type==typeof(scrE1M5Plunger)?nameof(PlungerSpawned):nameof(PlungerDetonated),BindingFlags.Static|BindingFlags.NonPublic)!;
            return type==typeof(scrE1M5Plunger)?HapticEventSelectors.CaptureSpawns(instructions,callback,2):AfterStateTwo(instructions,type,callback);
        }
        private static IEnumerable<CodeInstruction> AfterStateTwo(IEnumerable<CodeInstruction> instructions,Type type,MethodInfo callback)
        {
            var field=type.GetField("state")!;
            var result=new List<CodeInstruction>();CodeInstruction? previous=null;int count=0;
            foreach(var i in instructions){
                result.Add(i);
                if(i.opcode==OpCodes.Stfld&&Equals(i.operand,field)&&previous?.opcode==OpCodes.Ldc_I4_2){
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));result.Add(new CodeInstruction(OpCodes.Call,callback));count++;
                }
                previous=i;
            }
            if(count!=1)throw new InvalidOperationException("Native wall-break transition changed: "+type.Name);
            return result;
        }
        // One strong event per breach, irrespective of distance or debris count.
        internal static void BarnBroken(scrE2M5BarnWalls source)=>_impact?.Invoke();
        internal static void HarvesterBroken(scrE2M1HarvesterTrigger source)=>_impact?.Invoke();
        internal static void PlungerDetonated(scrE2M3Plunger source)=>_blast?.Invoke(source.explosionObject);
        internal static void PlungerSpawned(GameObject effect)=>_blast?.Invoke(effect);
        // Reuse the game's actual per-player attenuation. The final player
        // callback supplies its amount before screen-shake preference scaling.
        internal static void GameAdd(scrGameControl game,float amount,Vector3 position,bool ignoreDistance)
        {bool previous=_scoped;_scoped=true;try{game.addShake(amount,position,ignoreDistance);}finally{_scoped=previous;}}
        // Limit only haptics after native distance attenuation; screen shake is untouched.
        internal static void RestrainedGameAdd(scrGameControl game,float amount,Vector3 position,bool ignoreDistance)
        {bool previous=_restrained;_restrained=true;try{GameAdd(game,amount,position,ignoreDistance);}finally{_restrained=previous;}}
        internal static void GameSet(scrGameControl game,float amount,Vector3 position,bool ignoreDistance)
        {bool previous=_scoped;_scoped=true;try{game.setShake(amount,position,ignoreDistance);}finally{_scoped=previous;}}
        internal static void PlayerAdd(scrPlayerControl player,float amount)
        {bool previous=_scoped;_scoped=true;try{player.addShake(amount);}finally{_scoped=previous;}}
        internal static void Accepted(scrPlayerControl __instance,float amount)
        {if(_scoped)_notify?.Invoke(__instance,_restrained?Math.Min(amount,.49f):amount);}
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)=>Rewrite(instructions,Producers[__originalMethod]);
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,Producer producer)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly;
            var add=typeof(scrGameControl).GetMethod("addShake",flags)!;var set=typeof(scrGameControl).GetMethod("setShake",flags)!;
            var player=typeof(scrPlayerControl).GetMethod("addShake",flags)!;
            var result=new List<CodeInstruction>();int adds=0,sets=0,players=0;
            foreach(var i in instructions){
                string? wrapper=null;
                if(i.opcode==OpCodes.Call||i.opcode==OpCodes.Callvirt){
                    if(Equals(i.operand,add)){wrapper=nameof(GameAdd);adds++;}
                    else if(Equals(i.operand,set)){wrapper=nameof(GameSet);sets++;}
                    else if(Equals(i.operand,player)){wrapper=nameof(PlayerAdd);players++;}
                }
                if(wrapper==null)result.Add(i);
                else{
                    var replaced=new CodeInstruction(i){opcode=OpCodes.Call,operand=typeof(WorldRumbleHooks).GetMethod(wrapper,BindingFlags.Static|BindingFlags.NonPublic)!};
                    result.Add(replaced);
                }
            }
            if(adds!=producer.Add||sets!=producer.Set||players!=producer.Player)throw new InvalidOperationException("Native world shake call sites changed: "+producer.Type.Name+"."+producer.Method);
            return result;
        }
        internal static void Shutdown(){_harmony?.UnpatchSelf();_harmony=null;_notify=null;_impact=null;_blast=null;_scoped=false;_restrained=false;Producers.Clear();}
    }
}
