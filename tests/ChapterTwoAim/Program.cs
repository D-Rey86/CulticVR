using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Emit = System.Reflection.Emit.OpCodes;

var root = args.Length > 0 ? args[0] : "G:/Games/Steam/steamapps/common/CULTIC";
var managed = Path.Combine(root, "CULTIC_Data/Managed");
var nativePath = Path.Combine(managed, "Assembly-CSharp.dll");
Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath))) ==
    "0AC77697A3F08589E20AF683DA2DE78E664DBE0726397280905608423E557CDC", "Shipping target changed");
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var name = new AssemblyName(e.Name).Name + ".dll";
    foreach (var dir in new[] { managed, Path.Combine(root,"BepInEx/core"), Path.Combine(root,"BepInEx/plugins") })
        if (File.Exists(Path.Combine(dir,name))) return Assembly.LoadFrom(Path.Combine(dir,name));
    return null;
};
var nativeReflection = Assembly.LoadFrom(nativePath);
var modPath = Path.GetFullPath("src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll");
var modReflection = Assembly.LoadFrom(modPath);
var plugin = modReflection.GetType("CulticVR.AimTrial.AimTrialPlugin")!;
var redirect = plugin.GetMethod("RedirectFiringReads", BindingFlags.NonPublic | BindingFlags.Static)!;
var knockback = plugin.GetMethod("FindPrimaryKnockbackRead", BindingFlags.NonPublic | BindingFlags.Static)!;
using var game = AssemblyDefinition.ReadAssembly(nativePath);
var player = game.MainModule.Types.Single(t => t.Name == "scrPlayerControl");
var map = typeof(Emit).GetFields().Where(f=>f.FieldType == typeof(System.Reflection.Emit.OpCode))
    .Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);

List<CodeInstruction> Decode(MethodDefinition method)
{
    var generator = new DynamicMethod("Fixture", typeof(void), Type.EmptyTypes).GetILGenerator();
    var targets = method.Body.Instructions.ToDictionary(i=>i, _=>generator.DefineLabel());
    return method.Body.Instructions.Select(i=>
    {
        object? value = i.Operand;
        if (value is FieldReference f && (f.DeclaringType.Name == "scrPlayerControl" || f.DeclaringType.Name == "scrGameControl"))
            value = nativeReflection.GetType(f.DeclaringType.FullName)!.GetField(f.Name, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)!;
        if (value is MethodReference m && (m.DeclaringType.FullName is "UnityEngine.GameObject" or "UnityEngine.Transform" or "UnityEngine.Vector3") &&
            (m.Name is "get_transform" or "get_forward" or "op_Multiply" or "op_Addition"))
        {
            var type = Assembly.LoadFrom(Path.Combine(managed,"UnityEngine.CoreModule.dll")).GetType(m.DeclaringType.FullName)!;
            value = type.GetMethods().Single(x=>x.Name == m.Name && x.GetParameters().Select(p=>p.ParameterType.FullName)
                .SequenceEqual(m.Parameters.Select(p=>p.ParameterType.FullName)));
        }
        if (value is Instruction one) value=targets[one];
        if (value is Instruction[] many) value=many.Select(t=>targets[t]).ToArray();
        var code=new CodeInstruction(map[i.OpCode.Value],value);
        code.labels.Add(targets[i]);
        return code;
    }).ToList();
}
var fire=player.Methods.Single(m=>m.Name=="fireEvent");
var original=Decode(fire);
var pr=nativeReflection.GetType("scrPlayerControl")!;
var unity=Assembly.LoadFrom(Path.Combine(managed,"UnityEngine.CoreModule.dll"));
var vector=unity.GetType("UnityEngine.Vector3")!;
var contract=new object[]{pr.GetField("knockSpeed")!,pr.GetField("playerCamera")!,
    unity.GetType("UnityEngine.GameObject")!.GetProperty("transform")!.GetMethod!,
    unity.GetType("UnityEngine.Transform")!.GetProperty("forward")!.GetMethod!,
    vector.GetMethod("op_Multiply",new[]{vector,typeof(float)})!,vector.GetMethod("op_Addition",new[]{vector,vector})!};
CodeInstruction Select(List<CodeInstruction> codes) => (CodeInstruction)knockback.Invoke(null,new object[]{codes}.Concat(contract).ToArray())!;
var selected=Select(original); var offset=original.IndexOf(selected);
Require(fire.Body.Instructions[offset].Offset==0x350a,"Preserved read is not shipping SASG knockback");
var failures=0;
foreach(var relative in Enumerable.Range(-4,11))
{
    var codes=Decode(fire); codes[offset+relative].opcode=Emit.Nop;
    var before=codes.Select(c=>(c.opcode,c.operand,labels:c.labels.ToArray())).ToArray();
    var rejected=false;
    try{Select(codes);}catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException){rejected=true;}
    Require(rejected,"Malformed knockback accepted");
    Require(codes.Select((c,i)=>c.opcode==before[i].opcode && Equals(c.operand,before[i].operand) && c.labels.SequenceEqual(before[i].labels)).All(x=>x),"Selector mutated input");
    failures++;
}
using var compiled=AssemblyDefinition.ReadAssembly(modPath);
var candidate=compiled.MainModule.Types.Single(t=>t.FullName==plugin.FullName);
var patch=candidate.Methods.Single(m=>m.Name=="RedirectFiringReads");
Require(patch.Body.Instructions.Count(i=>i.Operand is MethodReference m && m.Name=="FindPrimaryKnockbackRead")==1,"Missing production movement selector");
Console.WriteLine($"PASS: production selector preserves exact SASG knockback read IL_350a; {failures} damaged contract fixtures fail without mutation. Shipping fireEvent has {original.Count} instructions.");
Console.WriteLine("STATIC ONLY: selector execution and compiled wiring; shipping Harmony patch startup/Unity behavior not executed.");
static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
