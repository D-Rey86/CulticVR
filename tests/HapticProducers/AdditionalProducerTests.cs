using System.Reflection;
using System.Reflection.Emit;
using CulticVR.OpenXRControllers;
using HarmonyLib;
internal static class AdditionalProducerTests
{
    internal static void Run()
    {
        int checks=0;void Check(bool v,string message){checks++;if(!v)throw new Exception(message);}
        using var native=Mono.Cecil.AssemblyDefinition.ReadAssembly("G:/Games/Steam/steamapps/common/CULTIC/CULTIC_Data/Managed/Assembly-CSharp.dll");
        var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
        var constructor=typeof(scrHitInfo).GetConstructor(Type.EmptyTypes)!;
        var publish=typeof(HapticProducerTags).GetMethod("Publish",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(var producer in AdditionalHapticProducers.Catalog){
            var method=producer.Type.GetMethod(producer.Method,BindingFlags.DeclaredOnly|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!;
            Check(method!=null,"Declared producer exists");
            var body=(Mono.Cecil.MethodDefinition)native.MainModule.LookupToken(method!.MetadataToken);
            var original=body.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&i.Operand is Mono.Cecil.MethodReference mr&&mr.DeclaringType.Name==nameof(scrHitInfo)?constructor:i.Operand)).ToList();
            var rewritten=AdditionalHapticProducers.Rewrite(original,producer.Sites).ToList();
            int cursor=0,ordinal=0;
            foreach(var instruction in original){
                Check(ReferenceEquals(instruction,rewritten[cursor++]),"Original IL/labels/blocks identity and order");
                if(instruction.opcode!=OpCodes.Newobj||!Equals(instruction.operand,constructor))continue;
                var site=producer.Sites[ordinal++];if(site.Kind==IncomingHapticKind.Unknown)continue;
                Check(rewritten[cursor++].opcode==OpCodes.Dup,"Native constructed value retained on stack");
                Check((int)rewritten[cursor++].operand==(int)site.Kind,"Exact constructor kind");
                Check((int)rewritten[cursor++].operand==(int)site.Direction,"Exact constructor direction convention");
                Check(Equals(rewritten[cursor++].operand,publish),"No gameplay mutation callback");
            }
            Check(cursor==rewritten.Count&&ordinal==producer.Sites.Length,"All native constructors accounted for");
            try{AdditionalHapticProducers.Rewrite(original.Concat(new[]{new CodeInstruction(OpCodes.Newobj,constructor)}),producer.Sites).ToList();throw new Exception("Unexpected constructor accepted");}catch(InvalidOperationException){checks++;}
            try{AdditionalHapticProducers.Rewrite(Array.Empty<CodeInstruction>(),producer.Sites).ToList();throw new Exception("Missing constructor accepted");}catch(InvalidOperationException){checks++;}
        }
        var heli=AdditionalHapticProducers.Catalog.Single(p=>p.Type==typeof(scrHeliSpider)&&p.Method=="Update");
        Check(heli.Sites.Select(s=>s.Kind).SequenceEqual(new[]{IncomingHapticKind.HeavyImpact,IncomingHapticKind.HeavyImpact,IncomingHapticKind.HeavyImpact,IncomingHapticKind.Automatic,IncomingHapticKind.HeavyImpact}),"Mixed boss method preserves distinct native attacks");
        HapticProducerTags.Enabled=true;
        var helicopter=AdditionalHapticProducers.Catalog.Single(p=>p.Type==typeof(scrHelicopter)&&p.Method=="fireBullet");
        Check(helicopter.Sites.Single().Kind==IncomingHapticKind.HelicopterAutomatic,"Only exact helicopter producer uses duration correction");
        var info=new scrHitInfo();HapticProducerTags.Publish(info,IncomingHapticKind.Shrapnel,HapticDirection.TowardSource);
        var group=HapticProducerTags.Get(info)!;Check(group.Kind==IncomingHapticKind.Shrapnel&&group.Direction==HapticDirection.TowardSource,"Nail convention survives metadata publication");
        var clone=info.DeepCopy(info);typeof(HapticProducerTags).GetMethod("Copy",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{info,clone});
        Check(ReferenceEquals(HapticProducerTags.Get(clone),group),"Copy preserves new direction metadata");
        var unknown=new scrHitInfo();HapticProducerTags.Publish(unknown,IncomingHapticKind.Unknown,HapticDirection.Travel);Check(HapticProducerTags.Get(unknown)==null,"Unused zero-damage constructor not tagged");
        HapticProducerTags.Disable();var disabled=new scrHitInfo();HapticProducerTags.Publish(disabled,IncomingHapticKind.Arrow,HapticDirection.Travel);Check(HapticProducerTags.Get(disabled)==null,"Disabled producer has no metadata");
        for(int i=0;i<1000;i++)HapticProducerTags.Publish(disabled,IncomingHapticKind.Arrow,HapticDirection.Travel);
        long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)HapticProducerTags.Publish(disabled,IncomingHapticKind.Arrow,HapticDirection.Travel);bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        Check(bytes==0,"Disabled path allocates nothing");
        // No prefix/postfix added to Update: only actual native damage constructors call Publish.
        Console.WriteLine($"PASS {checks} additional native producer checks across {AdditionalHapticProducers.Catalog.Length} methods; no gameplay/actuators executed.");
    }
}
