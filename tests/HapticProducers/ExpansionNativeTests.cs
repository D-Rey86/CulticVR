using System.Reflection;
using System.Reflection.Emit;
using CulticVR.OpenXRControllers;
using HarmonyLib;
using Mono.Cecil;
internal static class ExpansionNativeTests
{
    public static void Healed(scrPlayerControl player,int amount){}
    internal static void Run()
    {
        int checks=0;void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        using var assembly=AssemblyDefinition.ReadAssembly("G:/Games/Steam/steamapps/common/CULTIC/CULTIC_Data/Managed/Assembly-CSharp.dll");
        var module=typeof(scrPlayerControl).Module;
        var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
        List<CodeInstruction> Read(MethodDefinition method,MethodInfo native)=>method.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand switch{
            MethodReference mr=>module.ResolveMethod(mr.MetadataToken.ToInt32()),FieldReference fr=>module.ResolveField(fr.MetadataToken.ToInt32()),
            Mono.Cecil.Cil.VariableDefinition v=>native.GetMethodBody()!.LocalVariables[v.Index],_=>i.Operand})).ToList();
        foreach(var site in ExpandedWorldRumbleHooks.Catalog){
            var native=site.Type.GetMethod(site.Method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!;
            var definition=assembly.MainModule.Types.Single(t=>t.Name==site.Type.Name).Methods.Single(m=>m.Name==site.Method);
            var original=Read(definition,native);var observed=ExpandedWorldRumbleHooks.Rewrite(original,site).ToList();
            Check(original.Count==observed.Count,"Expanded world source retains native instruction count");
            int replacements=0;
            for(int i=0;i<original.Count;i++){
                if(ReferenceEquals(original[i],observed[i])){checks++;continue;}
                Check(original[i].operand is MethodInfo m&&(m.Name=="addShake"||m.Name=="setShake"),"Only actual native shake call replaced");
                bool cinematic=site.Type==typeof(scrE2M0AmalgamCutscene)||site.Type==typeof(scrE2M9Cutscene);
                var before=(MethodInfo)original[i].operand;
                string wrapper=cinematic?"CinematicGameAdd":ExpandedWorldRumbleHooks.Restrained(site)?"RestrainedGameAdd":before.DeclaringType==typeof(scrPlayerControl)?"PlayerAdd":before.Name=="setShake"?"GameSet":"GameAdd";
                Check(observed[i].opcode==OpCodes.Call&&observed[i].operand is MethodInfo replacement&&replacement.Name==wrapper,"Playback-only versus regular native wrapper selection");replacements++;
            }
            Check(replacements==site.Add+site.Set+site.Player,"Exact native shake coverage");
            try{ExpandedWorldRumbleHooks.Rewrite(Array.Empty<CodeInstruction>(),site).ToList();throw new Exception("Invalid expanded source accepted");}catch(InvalidOperationException){checks++;}
        }
        var player=assembly.MainModule.Types.Single(t=>t.Name=="scrPlayerControl");
        Check(player.Fields.Single(f=>f.Name=="fallDist").FieldType.FullName=="System.Single","Native landing distance injection field type");
        Check(player.Fields.Single(f=>f.Name=="isSliding").FieldType.FullName=="System.Boolean","Native roll outcome injection field type");
        Check(player.Methods.Single(m=>m.Name=="takeDamage").Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.Name=="gameState"&&i.Previous.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_3),"Native lethal damage switches to death-camera game3 before haptic postfix");
        var update=player.Methods.Single(m=>m.Name=="Update");var nativeUpdate=typeof(scrPlayerControl).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var instructions=Read(update,nativeUpdate);var callback=typeof(ExpansionNativeTests).GetMethod(nameof(Healed))!;
        var healing=ExpansionEventSelectors.Healing(instructions,callback).ToList();
        Check(healing.Count==instructions.Count+6&&healing.Count(i=>Equals(i.operand,callback))==2,"Two healing modes get one accepted-HP observer each");
        int cursor=0;
        foreach(var instruction in instructions){
            Check(ReferenceEquals(healing[cursor++],instruction),"All native Update instructions preserved for healing");
            if(cursor+2<healing.Count&&Equals(healing[cursor+2].operand,callback)){
                Check(healing[cursor].opcode==OpCodes.Ldarg_0&&healing[cursor+1].operand is LocalVariableInfo local&&local.LocalType==typeof(int)&&local.LocalIndex is 170 or 171,"Exact accepted HP amount local, not a raw input or regenerated HP");cursor+=3;
            }
        }
        foreach(var invalid in new[]{Array.Empty<CodeInstruction>(),instructions.Concat(instructions).ToArray()}){
            try{ExpansionEventSelectors.Healing(invalid,callback).ToList();throw new Exception("Invalid healing seams accepted");}catch(InvalidOperationException){checks++;}
        }
        var extraPickup=typeof(ExpansionNativeTests).GetMethod(nameof(Player))!;
        var pickupsFirst=ExpansionEventSelectors.Healing(HapticEventSelectors.OffhandPickups(instructions,extraPickup),callback).ToList();
        var healingFirst=HapticEventSelectors.OffhandPickups(ExpansionEventSelectors.Healing(instructions,callback),extraPickup).ToList();
        Check(pickupsFirst.Select(i=>i.ToString()).SequenceEqual(healingFirst.Select(i=>i.ToString())),"Healing and existing offhand pickup transpilers commute");
        var platform=assembly.MainModule.Types.Single(t=>t.Name=="scrTriggerPlatformBreak");
        var collision=platform.Methods.Single(m=>m.Name=="OnCollisionEnter");
        Check(collision.Parameters.Single().ParameterType.FullName=="UnityEngine.Collision","Native accepted platform collision callback ABI");
        Check(collision.Body.Instructions.Count(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.Name=="state"&&i.Previous.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_1)==1,"One accepted platform broken-state transition");
        Check(collision.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="Instantiate")==1,"One native platform effect spawn");
        Console.WriteLine($"PASS {checks} expanded cinematic/boss/heal/landing/platform native contracts across {ExpandedWorldRumbleHooks.Catalog.Length} shake sources. Static only.");
    }
    public static void Player(scrPlayerControl value){}
}
