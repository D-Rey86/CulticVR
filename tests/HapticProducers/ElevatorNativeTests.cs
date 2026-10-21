using System.Reflection;
using System.Reflection.Emit;
using CulticVR.OpenXRControllers;
using HarmonyLib;

internal static class ElevatorNativeTests
{
    internal static void Run()
    {
        int checks=0;void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
        using var native=Mono.Cecil.AssemblyDefinition.ReadAssembly("G:/Games/Steam/steamapps/common/CULTIC/CULTIC_Data/Managed/Assembly-CSharp.dll");
        var method=typeof(scrMovingPlatform).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly)!;
        var body=(Mono.Cecil.MethodDefinition)native.MainModule.LookupToken(method.MetadataToken);
        var module=method.Module;
        var ops=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
        var original=body.Body.Instructions.Select(i=>new CodeInstruction(ops[i.OpCode.Value],i.Operand switch{
            Mono.Cecil.MethodReference m=>module.ResolveMethod(m.MetadataToken.ToInt32()),
            Mono.Cecil.FieldReference f=>module.ResolveField(f.MetadataToken.ToInt32()),_=>i.Operand})).ToList();
        var labels=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator();foreach(var i in original)i.labels.Add(labels.DefineLabel());
        var rewritten=ElevatorRideHooks.Rewrite(original).ToList();
        Check(rewritten.Count==original.Count+2,"Only two source arguments added");
        int changed=0,j=0;
        for(int i=0;i<original.Count;i++,j++){
            if(ReferenceEquals(original[i],rewritten[j])){checks++;continue;}
            Check(rewritten[j].opcode==OpCodes.Ldarg_0,"Load current platform without disturbing native position arguments");
            Check(rewritten[j].labels.SequenceEqual(original[i].labels)&&rewritten[j].blocks.SequenceEqual(original[i].blocks),"Branch/EH metadata moves onto injected argument");
            var wrapper=(MethodInfo)rewritten[++j].operand;
            Check(wrapper.DeclaringType==typeof(ElevatorRideHooks)&&wrapper.Name=="MoveRider","Only reviewed passenger setters wrapped");
            Check(wrapper.ReturnType==typeof(void)&&wrapper.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(UnityEngine.Transform),typeof(UnityEngine.Vector3),typeof(scrMovingPlatform)}),"Wrapper consumes original setter stack plus source");
            Check(body.Body.Instructions[i].Offset is 0x70e or 0x810,"Exact moving/stationary passenger displacement IL sites");
            changed++;
        }
        Check(changed==2,"Both native passenger paths accounted for; own/platform/temp transforms untouched");
        foreach(var broken in new[]{original.Where(i=>!(i.opcode==OpCodes.Ldfld&&i.operand is FieldInfo f&&f.Name=="lastPos")).ToList(),original.Concat(original).ToList(),new List<CodeInstruction>()}){
            try{ElevatorRideHooks.Rewrite(broken).ToList();throw new Exception("Changed native contract accepted");}catch(InvalidOperationException){checks++;}
        }
        Console.WriteLine($"PASS {checks} exact native elevator observer checks; no native gameplay executed.");
    }
}
