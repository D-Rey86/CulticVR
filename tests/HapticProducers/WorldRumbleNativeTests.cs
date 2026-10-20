using System.Reflection;
using System.Reflection.Emit;
using CulticVR.OpenXRControllers;
using HarmonyLib;
internal static class WorldRumbleNativeTests
{
    internal static void Run()
    {
        int checks=0;void Check(bool c,string s){checks++;if(!c)throw new Exception(s);}
        using var native=Mono.Cecil.AssemblyDefinition.ReadAssembly("G:/Games/Steam/steamapps/common/CULTIC/CULTIC_Data/Managed/Assembly-CSharp.dll");
        var module=typeof(scrPlayerControl).Module;
        var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
        var flags=BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public;
        var add=typeof(scrGameControl).GetMethod("addShake",flags)!;var set=typeof(scrGameControl).GetMethod("setShake",flags)!;var player=typeof(scrPlayerControl).GetMethod("addShake",flags)!;
        int totalSites=0;
        foreach(var producer in WorldRumbleHooks.Catalog){
            var method=producer.Type.GetMethod(producer.Method,BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
            Check(method!=null,"Exact native world-event method");
            var body=(Mono.Cecil.MethodDefinition)native.MainModule.LookupToken(method!.MetadataToken);
            var original=body.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand is Mono.Cecil.MethodReference mr?module.ResolveMethod(mr.MetadataToken.ToInt32()):i.Operand)).ToList();
            var labels=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator();
            // Labels and EH boundaries must survive call replacement as well as untouched instructions.
            foreach(var i in original){i.labels.Add(labels.DefineLabel());}
            var rewritten=WorldRumbleHooks.Rewrite(original,producer).ToList();Check(rewritten.Count==original.Count,"No stack or control-flow instructions added");
            int changed=0;
            for(int i=0;i<original.Count;i++){
                var before=original[i];var after=rewritten[i];
                if(ReferenceEquals(before,after)){checks++;continue;}
                changed++;Check(after.opcode==OpCodes.Call&&after.operand is MethodInfo wrapper&&wrapper.DeclaringType==typeof(WorldRumbleHooks),"Only shake call replaced");
                var oldMethod=(MethodInfo)before.operand;var replacement=(MethodInfo)after.operand;
                Check(oldMethod.ReturnType==replacement.ReturnType&&replacement.GetParameters()[0].ParameterType==oldMethod.DeclaringType,"Equivalent instance-to-static stack signature");
                Check(oldMethod.GetParameters().Select(p=>p.ParameterType).SequenceEqual(replacement.GetParameters().Skip(1).Select(p=>p.ParameterType)),"All native call arguments preserved");
                Check(before.labels.SequenceEqual(after.labels)&&before.blocks.SequenceEqual(after.blocks),"Branch/EH metadata retained");
                Check(Equals(oldMethod,add)?replacement.Name=="GameAdd":Equals(oldMethod,set)?replacement.Name=="GameSet":Equals(oldMethod,player)&&replacement.Name=="PlayerAdd","Exact native operation preserved");
            }
            Check(changed==producer.Add+producer.Set+producer.Player,"Reviewed call count");totalSites+=changed;
            try{WorldRumbleHooks.Rewrite(Array.Empty<CodeInstruction>(),producer).ToList();throw new Exception("Missing event accepted");}catch(InvalidOperationException){checks++;}
            try{WorldRumbleHooks.Rewrite(original.Concat(new[]{new CodeInstruction(OpCodes.Callvirt,add)}),producer).ToList();throw new Exception("Extra shake accepted");}catch(InvalidOperationException){checks++;}
        }
        foreach(var type in native.MainModule.Types.Where(t=>System.Text.RegularExpressions.Regex.IsMatch(t.Name,@"^scrE[12B]M"))){
            // Cutscenes deliberately retain the existing no-feedback policy.
            if(type.Name is "scrE2M0AmalgamCutscene" or "scrE2M9Cutscene")continue;
            foreach(var method in type.Methods.Where(m=>m.HasBody)){
                bool shakes=method.Body.Instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference mr&&
                    (mr.DeclaringType.Name=="scrGameControl"||mr.DeclaringType.Name=="scrPlayerControl")&&(mr.Name=="addShake"||mr.Name=="setShake"));
                if(shakes&&type.Name is not ("scrE1M5Plunger" or "scrE2M3Plunger"))Check(WorldRumbleHooks.Catalog.Any(p=>p.Type.Name==type.Name&&p.Method==method.Name),"Environmental shake omitted: "+method.FullName);
            }
        }
        foreach(var type in new[]{typeof(scrE2M5BarnWalls),typeof(scrE2M1HarvesterTrigger),typeof(scrE2M3Plunger)}){
            var method=type.GetMethod("Update",BindingFlags.DeclaredOnly|BindingFlags.Instance|BindingFlags.NonPublic)!;
            var body=(Mono.Cecil.MethodDefinition)native.MainModule.LookupToken(method.MetadataToken);
            var original=body.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand switch{
                Mono.Cecil.MethodReference mr=>module.ResolveMethod(mr.MetadataToken.ToInt32()),
                Mono.Cecil.FieldReference fr=>module.ResolveField(fr.MetadataToken.ToInt32()),_=>i.Operand})).ToList();
            var labels=new DynamicMethod("wallLabels",typeof(void),Type.EmptyTypes).GetILGenerator();
            foreach(var i in original)i.labels.Add(labels.DefineLabel());
            var rewritten=(type==typeof(scrE2M3Plunger)?WorldRumbleHooks.RewritePlunger(original,type):WorldRumbleHooks.RewriteWall(original,type)).ToList();
            Check(rewritten.Count==original.Count+2,"Only event callback appended");
            int cursor=0,callbacks=0;
            foreach(var i in original){
                Check(ReferenceEquals(rewritten[cursor++],i),"Every original wall instruction and metadata retained");
                if(cursor<rewritten.Count&&rewritten[cursor].opcode==OpCodes.Ldarg_0&&cursor+1<rewritten.Count&&rewritten[cursor+1].operand is MethodInfo callback&&callback.DeclaringType==typeof(WorldRumbleHooks)){
                    Check(i.opcode==OpCodes.Stfld&&Equals(i.operand,type.GetField("state")),"Callback only after native state transition");
                    Check(callback.GetParameters().Single().ParameterType==type&&callback.ReturnType==typeof(void),"Wall callback stack signature");
                    callbacks++;cursor+=2;
                }
            }
            Check(callbacks==1,"One wall notification per completion, no hot Update callback");
            foreach(var invalid in new[]{Array.Empty<CodeInstruction>(),original.Concat(original).ToArray()}){
                try{(type==typeof(scrE2M3Plunger)?WorldRumbleHooks.RewritePlunger(invalid,type):WorldRumbleHooks.RewriteWall(invalid,type)).ToList();throw new Exception("Changed wall seam accepted");}catch(InvalidOperationException){checks++;}
            }
        }
        Check(!WorldRumbleHooks.Catalog.Any(p=>p.Type==typeof(scrPlayerControl)||p.Type==typeof(scrExplosion)||p.Type==typeof(scrGrenadeShell)||p.Type==typeof(scrTank)),"No recoil/explosion/global producer instrumentation");
        Console.WriteLine($"PASS {checks} native world-rumble selector checks across {WorldRumbleHooks.Catalog.Length} methods / {totalSites} shake calls; no game execution.");
    }
}

