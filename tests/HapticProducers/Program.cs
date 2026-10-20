using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using CulticVR.OpenXRControllers;
using HarmonyLib;

AssemblyLoadContext.Default.Resolving+=(_,name)=>{
    foreach(var folder in new[]{"CULTIC_Data/Managed","BepInEx/core","BepInEx/plugins"}){
        var path=Path.Combine("G:/Games/Steam/steamapps/common/CULTIC",folder,name.Name+".dll");
        if(File.Exists(path))return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
ProducerReplay.Run(); NativeWorldTests.Run(); AdditionalProducerTests.Run(); WorldRumbleNativeTests.Run();ExpansionNativeTests.Run();

internal static class ProducerReplay
{
    internal static void Run()
    {
        int checks=0;void Check(bool c,string s){checks++;if(!c)throw new Exception(s);}
        var targets=new List<(Type Type,string Name,IncomingHapticKind Kind,int Count)>{
            (typeof(scrShotgunCultist),"castShot",IncomingHapticKind.Shotgun,1),(typeof(scrRiotCop),"castShot",IncomingHapticKind.Shotgun,1),
            (typeof(scrAdeptCultist),"castShotgunBlast",IncomingHapticKind.Shotgun,1),(typeof(scrSTENCultist),"castShot",IncomingHapticKind.Automatic,1),
            (typeof(scrAdeptCultist),"castShot",IncomingHapticKind.Automatic,1),(typeof(scrHandgunCultist),"castShot",IncomingHapticKind.SingleShot,1),
            (typeof(scrRevolverCultist),"castShot",IncomingHapticKind.SingleShot,1),(typeof(scrSharpshooter),"castShot",IncomingHapticKind.SingleShot,1),
            (typeof(scrSharpshooter),"castRifleShot",IncomingHapticKind.Rifle,1),(typeof(scrThrownAxe),"OnCollisionEnter",IncomingHapticKind.Melee,1),(typeof(scrThrownAxe),"tryLineCheck",IncomingHapticKind.Melee,1)};
        foreach(var t in new[]{typeof(scrAfflicted),typeof(scrAmalgamation),typeof(scrAxeCultist),typeof(scrCivilian),typeof(scrGLCultist),
            typeof(scrHarvested),typeof(scrHarvester),typeof(scrInfectedArmor),typeof(scrInfectedBrute),typeof(scrInfectedCorpse),
            typeof(scrInfectedDevourer),typeof(scrMartyr),typeof(scrRiotCop),typeof(scrRotten),typeof(scrShotgunCultist)})
            targets.Add((t,"castSwing",IncomingHapticKind.Melee,t==typeof(scrHarvester)?2:1));
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        Type tagger=typeof(HapticProducerTags);
        var kinds=(Dictionary<MethodBase,IncomingHapticKind>)tagger.GetField("Kinds",flags)!.GetValue(null)!;
        var counts=(Dictionary<MethodBase,int>)tagger.GetField("Counts",flags)!.GetValue(null)!;
        MethodInfo transpiler=tagger.GetMethod("Constructed",flags)!;
        using var native=Mono.Cecil.AssemblyDefinition.ReadAssembly("G:/Games/Steam/steamapps/common/CULTIC/CULTIC_Data/Managed/Assembly-CSharp.dll");
        var opcodes=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
        MethodInfo Declared(Type t,string name)=>t.GetMethod(name,BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)??throw new MissingMethodException(t.FullName,name);
        foreach(var target in targets){
            var method=Declared(target.Type,target.Name);Check(method.DeclaringType==target.Type,"Exact native producer owner");
            kinds[method]=target.Kind;counts[method]=target.Count;
            var body=(Mono.Cecil.MethodDefinition)native.MainModule.LookupToken(method!.MetadataToken);
            // Native IL decoded without initializing Mono-only Harmony reflection helpers on .NET 8.
            var original=body.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],
                i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&i.Operand is Mono.Cecil.MethodReference mr&&mr.DeclaringType.Name==nameof(scrHitInfo)?
                typeof(scrHitInfo).GetConstructor(Type.EmptyTypes):i.Operand)).ToList();
            var rewritten=((IEnumerable<CodeInstruction>)transpiler.Invoke(null,new object[]{original,method})!).ToList();
            Check(rewritten.Count==original.Count+2*target.Count,"Constructor instrumentation site count");
            int cursor=0,injected=0;
            foreach(var instruction in original){
                Check(ReferenceEquals(instruction,rewritten[cursor++]),"Original instructions/labels/blocks retain identity and order");
                if(instruction.opcode==OpCodes.Newobj&&instruction.operand is ConstructorInfo ctor&&ctor.DeclaringType==typeof(scrHitInfo)){
                    Check(rewritten[cursor++].opcode==OpCodes.Dup,"Preserve created object stack value");
                    var call=rewritten[cursor++];Check(call.opcode==OpCodes.Call&&((MethodInfo)call.operand).Name=="Tag","Only side metadata added after construction");injected++;
                }
            }
            Check(injected==target.Count,"Every reviewed constructor tagged");
            try{transpiler.Invoke(null,new object[]{Array.Empty<CodeInstruction>(),method});throw new Exception("Changed native shape accepted");}
            catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){checks++;}
        }
        MethodInfo enter=tagger.GetMethod("Enter",flags)!,exit=tagger.GetMethod("Exit",flags)!,tag=tagger.GetMethod("Tag",flags)!,copy=tagger.GetMethod("Copy",flags)!;
        HapticProducerTags.Enabled=true;
        object?[] outer={Declared(typeof(scrShotgunCultist),"castShot"),null};enter.Invoke(null,outer);
        var info1=new scrHitInfo();tag.Invoke(null,new object[]{info1});var info2=new scrHitInfo();tag.Invoke(null,new object[]{info2});
        Check(ReferenceEquals(HapticProducerTags.Get(info1),HapticProducerTags.Get(info2)),"All native info objects in one attack/difficulty loop share group");
        object?[] inner={Declared(typeof(scrSharpshooter),"castRifleShot"),null};enter.Invoke(null,inner);
        var info3=new scrHitInfo();tag.Invoke(null,new object[]{info3});Check(HapticProducerTags.Get(info3)!.Kind==IncomingHapticKind.Rifle,"Distinct nested producer classification");
        exit.Invoke(null,new[]{inner[1]});var info4=new scrHitInfo();tag.Invoke(null,new object[]{info4});
        Check(ReferenceEquals(HapticProducerTags.Get(info1),HapticProducerTags.Get(info4)),"Finalizer restores surrounding attack identity");
        exit.Invoke(null,new[]{outer[1]});var outside=new scrHitInfo();tag.Invoke(null,new object[]{outside});Check(HapticProducerTags.Get(outside)==null,"No identity leaks to unrelated native construction");
        var cloned=info1.DeepCopy(info1);copy.Invoke(null,new object[]{info1,cloned});Check(ReferenceEquals(HapticProducerTags.Get(info1),HapticProducerTags.Get(cloned)),"Native DeepCopy retains original attack grouping");
        HapticProducerTags.Disable();
        Console.WriteLine($"PASS {checks} shipping-IL/tag metadata checks over {targets.Count} exact producer methods. No enemy/game methods or actuators executed.");
    }
}
