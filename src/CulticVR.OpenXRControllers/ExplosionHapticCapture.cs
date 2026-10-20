using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace CulticVR.OpenXRControllers
{
    // Associate visual explosions with their exact native damage invocation.
    // Update wrappers use value states; allocation occurs only when an explosion spawns.
    internal static class ExplosionHapticCapture
    {
        private sealed class Batch { internal bool PlayerHit,Scripted; }
        private struct Scope { internal object? Source;internal Batch? Batch;internal bool PlayerHit; }
        private struct UpdateState { internal Scope Previous;internal bool Exploded; }
        private static readonly ConditionalWeakTable<scrExplosion,Batch> Batches=new ConditionalWeakTable<scrExplosion,Batch>();
        private static readonly Dictionary<MethodBase,int> Counts=new Dictionary<MethodBase,int>();
        [ThreadStatic] private static Scope _current;
        private static Harmony? _harmony;
        private static Action<Vector3>? _notify;
        private static Action<Vector3>? _scriptedNotify;
        internal static bool Enabled;
        internal static void Install(Action<Vector3> notify,Action<Vector3> scriptedNotify)
        {
            _notify=notify;_scriptedNotify=scriptedNotify;_harmony=new Harmony("culticvr.haptics.explosions");
            Add(typeof(scrGrenadeShell),"detonate",1);Add(typeof(scrImpactProjectile),"detonate",1);
            Add(typeof(scrTNT),"detonate",1);Add(typeof(scrTNT),"takeDamage",1);
            Add(typeof(scrMolomite),"detonate",3);Add(typeof(scrMolotov),"detonate",4);
            Add(typeof(scrNailJar),"detonate",5);Add(typeof(scrNailJar),"takeDamage",1);
            Add(typeof(scrTank),"Update",4);Add(typeof(scrArchonBloodBall),"OnTriggerStay",1);
            Add(typeof(scrAbomination),"topArmSlam",1);Add(typeof(scrFlare),"Update",2);
            _harmony.Patch(AccessTools.Method(typeof(scrExplosion),"Update"),
                prefix:Hook(nameof(ExplosionBefore)),postfix:Hook(nameof(ExplosionAfter)),finalizer:Hook(nameof(ExplosionEnd)));
        }
        private static HarmonyMethod Hook(string name)=>new HarmonyMethod(typeof(ExplosionHapticCapture),name);
        private static void Add(Type type,string name,int count)
        {
            var m=AccessTools.DeclaredMethod(type,name);Counts[m]=count;
            _harmony!.Patch(m,prefix:Hook(nameof(Before)),transpiler:Hook(nameof(Spawns)),finalizer:Hook(nameof(End)));
        }
        private static Scope Begin(object source)
        {
            var previous=_current;_current=default;
            if(Enabled){_current.Source=source;if(source is scrExplosion explosion&&Batches.TryGetValue(explosion,out var batch))_current.Batch=batch;}
            return previous;
        }
        private static void Before(object __instance,out Scope __state)=>__state=Begin(__instance);
        private static void End(Scope __state)
        {
            if(ReferenceEquals(_current.Source,__state.Source)&&_current.PlayerHit){__state.PlayerHit=true;if(__state.Batch!=null)__state.Batch.PlayerHit=true;}
            _current=__state;
        }
        private static IEnumerable<CodeInstruction> Spawns(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)=>
            HapticEventSelectors.CaptureSpawns(instructions,typeof(ExplosionHapticCapture).GetMethod(nameof(Capture),BindingFlags.Static|BindingFlags.NonPublic)!,Counts[__originalMethod]);
        private static void Capture(GameObject created)
        {
            if(!Enabled||_current.Source==null||created==null)return;
            var explosion=created.GetComponent<scrExplosion>();if(explosion==null)return;
            if(_current.Batch==null)_current.Batch=new Batch{PlayerHit=_current.PlayerHit};
            Batches.Remove(explosion);Batches.Add(explosion,_current.Batch);
        }
        internal static void MarkPlayerDamage()
        {
            if(_current.Source==null)return;
            _current.PlayerHit=true;if(_current.Batch!=null)_current.Batch.PlayerHit=true;
        }
        internal static void ScriptedDetonation(GameObject effect)
        {
            if(!Enabled||effect==null)return;
            // One event-time traversal of this specific blast's effects, including
            // inactive children. Never a frame scan or global object search.
            var explosions=effect.GetComponentsInChildren<scrExplosion>(true);
            var position=explosions.Length>0?explosions[0].transform.position:effect.transform.position;
            if(explosions.Length>0){
                var batch=new Batch{Scripted=true};
                foreach(var explosion in explosions){Batches.Remove(explosion);Batches.Add(explosion,batch);}
            }
            _scriptedNotify?.Invoke(position);
        }
        private static void ExplosionBefore(scrExplosion __instance,bool ___hasExploded,out UpdateState __state)=>
            __state=new UpdateState{Previous=Begin(__instance),Exploded=___hasExploded};
        private static void ExplosionAfter(scrExplosion __instance,bool ___hasExploded,UpdateState __state)
        {
            if(Enabled&&!__state.Exploded&&___hasExploded&&!_current.PlayerHit&&_current.Batch?.PlayerHit!=true&&_current.Batch?.Scripted!=true)_notify?.Invoke(__instance.transform.position);
        }
        private static void ExplosionEnd(UpdateState __state)=>End(__state.Previous);
        internal static void Shutdown(){Enabled=false;_harmony?.UnpatchSelf();_harmony=null;_notify=null;_scriptedNotify=null;_current=default;Counts.Clear();}
    }
}
