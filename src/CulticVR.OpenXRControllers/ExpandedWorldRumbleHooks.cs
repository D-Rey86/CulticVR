using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace CulticVR.OpenXRControllers
{
    // A separate optional owner preserves the previously verified environment hooks.
    internal static class ExpandedWorldRumbleHooks
    {
        internal static readonly WorldRumbleHooks.Producer[] Catalog={
            new WorldRumbleHooks.Producer(typeof(scrE2M0AmalgamCutscene),"Update",add:1),
            new WorldRumbleHooks.Producer(typeof(scrE2M9Cutscene),"Update",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAbomination),"leftArmSlam",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAbomination),"leftArmSlamFinal",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAbomination),"grabRubble",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAmalgamation),"onFrameAdvance",add:3),
            new WorldRumbleHooks.Producer(typeof(scrAmalgamationMesh),"castStomp",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAmalgamationMesh),"stopKick",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAmalgamationMesh),"stopLeap",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAmalgamationMesh),"dropOntoClaw",add:1),
            new WorldRumbleHooks.Producer(typeof(scrAmalgamationMesh),"startRoar",add:1),
            new WorldRumbleHooks.Producer(typeof(scrArchon),"grabFinish",set:1),
            new WorldRumbleHooks.Producer(typeof(scrArchon),"leapSmash",add:1),
            new WorldRumbleHooks.Producer(typeof(scrArchon),"barrierSlam",set:1),
            new WorldRumbleHooks.Producer(typeof(scrArchonGroundFault),"Update",add:1),
            new WorldRumbleHooks.Producer(typeof(scrArchonLightningStrike),"Update",add:1),
            new WorldRumbleHooks.Producer(typeof(scrHeliSpider),"Update",add:1),
            new WorldRumbleHooks.Producer(typeof(scrHeliSpider),"endSpinAttack",add:1),
            new WorldRumbleHooks.Producer(typeof(scrInfectedBrute),"onFrameAdvance",add:2)
        };
        private static Harmony? _harmony;
        private static Func<bool>? _cinematicAllowed;
        private static readonly Dictionary<MethodBase,WorldRumbleHooks.Producer> Methods=new Dictionary<MethodBase,WorldRumbleHooks.Producer>();
        internal static void Install(Func<bool> cinematicAllowed)
        {
            _cinematicAllowed=cinematicAllowed;_harmony=new Harmony("culticvr.haptics.expanded-world");Methods.Clear();
            foreach(var site in Catalog){
                var method=site.Type.GetMethod(site.Method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                    ??throw new MissingMethodException(site.Type.FullName,site.Method);
                Methods.Add(method,site);
                _harmony.Patch(method,transpiler:new HarmonyMethod(typeof(ExpandedWorldRumbleHooks),nameof(Transpile)));
            }
        }
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> source,MethodBase __originalMethod)=>Rewrite(source,Methods[__originalMethod]);
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,WorldRumbleHooks.Producer site)
        {
            var result=WorldRumbleHooks.Rewrite(source,site);
            bool cinematic=site.Type==typeof(scrE2M0AmalgamCutscene)||site.Type==typeof(scrE2M9Cutscene);
            var from=typeof(WorldRumbleHooks).GetMethod("GameAdd",BindingFlags.Static|BindingFlags.NonPublic)!;
            var to=typeof(ExpandedWorldRumbleHooks).GetMethod(nameof(CinematicGameAdd),BindingFlags.Static|BindingFlags.NonPublic)!;
            foreach(var i in result){
                if(cinematic&&Equals(i.operand,from))yield return new CodeInstruction(i){operand=to};
                else yield return i;
            }
        }
        private static void CinematicGameAdd(scrGameControl game,float amount,Vector3 position,bool ignoreDistance)
        {
            // Skip/load may evaluate timeline signals while native gameState is 0.
            // Preserve native shake; authorize haptics only during real playback.
            if(_cinematicAllowed?.Invoke()==true)WorldRumbleHooks.GameAdd(game,amount,position,ignoreDistance);
            else game.addShake(amount,position,ignoreDistance);
        }
        internal static void Shutdown(){_harmony?.UnpatchSelf();_harmony=null;_cinematicAllowed=null;Methods.Clear();}
    }
}
