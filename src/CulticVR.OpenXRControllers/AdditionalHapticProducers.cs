using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CulticVR.OpenXRControllers
{
    // Constructor-specific metadata: no Update prefix, polling or allocation
    // unless the native method actually constructs damage information.
    internal static class AdditionalHapticProducers
    {
        internal readonly struct Site
        {
            internal readonly IncomingHapticKind Kind;
            internal readonly HapticDirection Direction;
            internal Site(IncomingHapticKind kind,HapticDirection direction=HapticDirection.Travel){Kind=kind;Direction=direction;}
        }
        internal readonly struct Producer
        {
            internal readonly Type Type;internal readonly string Method;internal readonly Site[] Sites;
            internal Producer(Type type,string method,params Site[] sites){Type=type;Method=method;Sites=sites;}
        }
        private static readonly Site Automatic=new Site(IncomingHapticKind.Automatic),Arrow=new Site(IncomingHapticKind.Arrow),
            Fragment=new Site(IncomingHapticKind.Shrapnel),Heavy=new Site(IncomingHapticKind.HeavyImpact),
            Energy=new Site(IncomingHapticKind.Energy),Shock=new Site(IncomingHapticKind.GroundShock,HapticDirection.None),
            Acid=new Site(IncomingHapticKind.Acid),Grip=new Site(IncomingHapticKind.Grip),Ignore=new Site(IncomingHapticKind.Unknown);
        internal static readonly Producer[] Catalog={
            new Producer(typeof(scrInfectedBowman),"castShot",Arrow),
            new Producer(typeof(scrHelicopter),"fireBullet",new Site(IncomingHapticKind.HelicopterAutomatic)),
            new Producer(typeof(scrTankGunner),"castShot",Automatic),
            new Producer(typeof(scrMountedWeapon),"Attack",Automatic),
            new Producer(typeof(scrAmalgamation),"castShot",Automatic),
            new Producer(typeof(scrAmalgamationMesh),"castShot",Automatic),
            new Producer(typeof(scrGrenadeShell),"spawnShrapnel",Fragment),
            new Producer(typeof(scrNail),"Update",new Site(IncomingHapticKind.Shrapnel,HapticDirection.TowardSource)),
            new Producer(typeof(scrAmalgamationMesh),"Update",Heavy,Heavy),
            new Producer(typeof(scrAmalgamationMesh),"castStomp",Heavy),
            // The fourth Archon constructor is an unused zero-damage grab object.
            new Producer(typeof(scrArchon),"Update",Heavy,Heavy,Heavy,Ignore,Heavy,Heavy),
            new Producer(typeof(scrArchon),"grabFinish",Heavy),
            new Producer(typeof(scrArchon),"leapSmash",Heavy),
            new Producer(typeof(scrArchon),"castSinglePunch",Heavy),
            new Producer(typeof(scrArchon),"beamCheckHit",Energy),
            // faultDetonate stores direction from the boss, not from its pos argument.
            // Do not misrepresent that as the ground eruption's source direction.
            new Producer(typeof(scrArchon),"faultDetonate",Shock),
            new Producer(typeof(scrArchon),"barrierSlam",Heavy),
            new Producer(typeof(scrArchonEnergyWave),"OnTriggerEnter",Energy),
            new Producer(typeof(scrArchonGroundFault),"tryDamageCast",Shock),
            new Producer(typeof(scrArchonLightningStrike),"Update",Shock),
            // Four physical strikes and one machine-gun constructor in this Update.
            new Producer(typeof(scrHeliSpider),"Update",Heavy,Heavy,Heavy,Automatic,Heavy),
            new Producer(typeof(scrHeliSpider),"castBite",Heavy),
            new Producer(typeof(scrInfectedSpit),"Update",Acid),
            new Producer(typeof(scrInfectedSpit),"OnTriggerEnter",Acid),
            new Producer(typeof(scrVomitParticle),"Update",Acid),
            new Producer(typeof(scrVomitParticle),"OnTriggerEnter",Acid),
            new Producer(typeof(scrRotten),"Update",Grip),
            new Producer(typeof(scrRotten),"castGrab",Grip),
            new Producer(typeof(scrCreepingHand),"Update",Grip),
            new Producer(typeof(scrGhost),"Update",Grip),
            new Producer(typeof(scrHarvester),"Update",Grip,Grip),
            // Cloud and embedded-fork ticks store a world position in hitFrom,
            // not a travel vector; use non-directional discomfort for those ticks.
            new Producer(typeof(scrGasCloud),"Update",new Site(IncomingHapticKind.Irritation,HapticDirection.None)),
            new Producer(typeof(scrThrownPitchfork),"Update",Arrow,new Site(IncomingHapticKind.Irritation,HapticDirection.None))
        };
        private static readonly Dictionary<MethodBase,Site[]> Sites=new Dictionary<MethodBase,Site[]>();
        internal static void Install(Harmony harmony)
        {
            Sites.Clear();
            foreach(var producer in Catalog){
                var method=producer.Type.GetMethod(producer.Method,BindingFlags.DeclaredOnly|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)
                    ??throw new MissingMethodException(producer.Type.FullName,producer.Method);
                Sites[method]=producer.Sites;
                harmony.Patch(method,transpiler:new HarmonyMethod(typeof(AdditionalHapticProducers),nameof(Transpile)));
            }
        }
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)=>Rewrite(instructions,Sites[__originalMethod]);
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,Site[] sites)
        {
            var result=new List<CodeInstruction>();int ordinal=0;
            var publish=typeof(HapticProducerTags).GetMethod(nameof(HapticProducerTags.Publish),BindingFlags.Static|BindingFlags.NonPublic)!;
            foreach(var i in instructions){
                result.Add(i);
                if(i.opcode!=OpCodes.Newobj||!(i.operand is ConstructorInfo ctor)||ctor.DeclaringType!=typeof(scrHitInfo))continue;
                if(ordinal>=sites.Length)throw new InvalidOperationException("Additional haptic producer has unexpected damage constructors.");
                var site=sites[ordinal++];
                if(site.Kind==IncomingHapticKind.Unknown)continue;
                result.Add(new CodeInstruction(OpCodes.Dup));
                result.Add(new CodeInstruction(OpCodes.Ldc_I4,(int)site.Kind));
                result.Add(new CodeInstruction(OpCodes.Ldc_I4,(int)site.Direction));
                result.Add(new CodeInstruction(OpCodes.Call,publish));
            }
            if(ordinal!=sites.Length)throw new InvalidOperationException("Additional haptic producer is missing damage constructors.");
            return result;
        }
    }
}
