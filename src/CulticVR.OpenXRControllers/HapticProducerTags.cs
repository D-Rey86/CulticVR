using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;

namespace CulticVR.OpenXRControllers
{
    internal static class HapticProducerTags
    {
        private static readonly ConditionalWeakTable<scrHitInfo,HapticAttackGroup> Tags=new ConditionalWeakTable<scrHitInfo,HapticAttackGroup>();
        private static readonly Dictionary<MethodBase,IncomingHapticKind> Kinds=new Dictionary<MethodBase,IncomingHapticKind>();
        private static readonly Dictionary<MethodBase,int> Counts=new Dictionary<MethodBase,int>();
        [ThreadStatic] private static HapticAttackGroup? _current;
        private static long _sequence;
        internal static bool Enabled;
        internal static void Install(Harmony harmony)
        {
            Add(harmony,typeof(scrShotgunCultist),"castShot",IncomingHapticKind.Shotgun);
            Add(harmony,typeof(scrRiotCop),"castShot",IncomingHapticKind.Shotgun);
            Add(harmony,typeof(scrAdeptCultist),"castShotgunBlast",IncomingHapticKind.Shotgun);
            Add(harmony,typeof(scrSTENCultist),"castShot",IncomingHapticKind.Automatic);
            Add(harmony,typeof(scrHandgunCultist),"castShot",IncomingHapticKind.SingleShot);
            Add(harmony,typeof(scrRevolverCultist),"castShot",IncomingHapticKind.SingleShot);
            Add(harmony,typeof(scrAdeptCultist),"castShot",IncomingHapticKind.Automatic);
            Add(harmony,typeof(scrSharpshooter),"castShot",IncomingHapticKind.SingleShot);
            Add(harmony,typeof(scrSharpshooter),"castRifleShot",IncomingHapticKind.Rifle);
            Add(harmony,typeof(scrThrownAxe),"OnCollisionEnter",IncomingHapticKind.Melee);
            Add(harmony,typeof(scrThrownAxe),"tryLineCheck",IncomingHapticKind.Melee);
            foreach(var type in new[]{typeof(scrAfflicted),typeof(scrAmalgamation),typeof(scrAxeCultist),typeof(scrCivilian),
                typeof(scrGLCultist),typeof(scrHarvested),typeof(scrHarvester),typeof(scrInfectedArmor),typeof(scrInfectedBrute),
                typeof(scrInfectedCorpse),typeof(scrInfectedDevourer),typeof(scrMartyr),typeof(scrRiotCop),typeof(scrRotten),typeof(scrShotgunCultist)})
                Add(harmony,type,"castSwing",IncomingHapticKind.Melee,type==typeof(scrHarvester)?2:1);
            harmony.Patch(AccessTools.Method(typeof(scrHitInfo),"DeepCopy"),postfix:new HarmonyMethod(typeof(HapticProducerTags),nameof(Copy)));
        }
        private static void Add(Harmony harmony,Type type,string name,IncomingHapticKind kind,int count=1)
        {
            var method=type.GetMethod(name,BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)??throw new MissingMethodException(type.FullName,name);
            Kinds[method]=kind;Counts[method]=count;
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(HapticProducerTags),nameof(Enter)),
                transpiler:new HarmonyMethod(typeof(HapticProducerTags),nameof(Constructed)),
                finalizer:new HarmonyMethod(typeof(HapticProducerTags),nameof(Exit)));
        }
        private static void Enter(MethodBase __originalMethod,out HapticAttackGroup? __state)
        {
            __state=_current;
            _current=Enabled&&Kinds.TryGetValue(__originalMethod,out var kind)?
                new HapticAttackGroup(unchecked((ulong)Interlocked.Increment(ref _sequence)),kind):null;
        }
        private static void Exit(HapticAttackGroup? __state)=>_current=__state;
        private static IEnumerable<CodeInstruction> Constructed(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var result=new List<CodeInstruction>();int count=0;
            var tag=typeof(HapticProducerTags).GetMethod(nameof(Tag),BindingFlags.Static|BindingFlags.NonPublic)!;
            foreach(var instruction in instructions){
                result.Add(instruction);
                if(instruction.opcode==OpCodes.Newobj&&instruction.operand is ConstructorInfo ctor&&ctor.DeclaringType==typeof(scrHitInfo)){
                    result.Add(new CodeInstruction(OpCodes.Dup));result.Add(new CodeInstruction(OpCodes.Call,tag));count++;
                }
            }
            if(!Counts.TryGetValue(__originalMethod,out var expected)||count!=expected)
                throw new InvalidOperationException("Native haptic producer constructor shape changed: "+__originalMethod);
            return result;
        }
        private static void Tag(scrHitInfo info)
        {
            var group=_current;if(group==null)return;
            Tags.Add(info,group); // Constructor seam: a new native object, never a guessed lifetime.
        }
        private static void Copy(scrHitInfo hitInfo,scrHitInfo __result)
        {
            if(hitInfo==null||__result==null||!Tags.TryGetValue(hitInfo,out var group))return;
            Tags.Remove(__result);Tags.Add(__result,group);
        }
        internal static HapticAttackGroup? Get(scrHitInfo info)=>info!=null&&Tags.TryGetValue(info,out var group)?group:null;
        internal static void Publish(scrHitInfo info,IncomingHapticKind kind,HapticDirection direction)
        {
            if(!Enabled||kind==IncomingHapticKind.Unknown)return;
            Tags.Add(info,new HapticAttackGroup(unchecked((ulong)Interlocked.Increment(ref _sequence)),kind,direction));
        }
        internal static void Disable(){Enabled=false;_current=null;}
    }
}
