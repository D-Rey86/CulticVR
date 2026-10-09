using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using CulticVR.AimTrial;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Emit = System.Reflection.Emit.OpCodes;

var root = @"G:/Games/Steam/steamapps/common/CULTIC";
var managed = Path.Combine(root, "CULTIC_Data/Managed");
var path = Path.Combine(managed, "Assembly-CSharp.dll");
Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) ==
    "0AC77697A3F08589E20AF683DA2DE78E664DBE0726397280905608423E557CDC", "Native hash");
AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
{
    var file = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
};
var native = Assembly.LoadFrom(path);
var unity = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.CoreModule.dll"));
var go = unity.GetType("UnityEngine.GameObject")!;
var objectType = unity.GetType("UnityEngine.Object")!;
var instantiateNative = objectType.GetMethods().Single(m => m.Name == "Instantiate" &&
    m.IsGenericMethod && m.GetParameters().Length == 3 &&
    m.GetParameters()[1].ParameterType.FullName == "UnityEngine.Vector3" &&
    m.GetParameters()[2].ParameterType.FullName == "UnityEngine.Quaternion").MakeGenericMethod(go);
var destination = native.GetType("scrBulletTracer")!.GetField("destination")!;
var rotation = unity.GetType("UnityEngine.Transform")!.GetProperty("rotation")!.SetMethod!;
var spawnProxy = typeof(Proxy).GetMethod(nameof(Proxy.Spawn))!;
var destProxy = typeof(Proxy).GetMethod(nameof(Proxy.Destination))!;
var rotProxy = typeof(Proxy).GetMethod(nameof(Proxy.Rotation))!;
var opcodes = typeof(Emit).GetFields().Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
    .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
using var module = AssemblyDefinition.ReadAssembly(path);
var player = module.MainModule.Types.Single(t => t.Name == "scrPlayerControl");
var total = 0;
foreach (var name in new[] { "fireEvent", "altFireEvent", "Update", "spawnShells", "spawnShell",
    "spawnMagCasing", "spawnMGCasing", "spawnMagLeveringCasing", "spawnMagOHCasing", "spawnSTENMag" })
{
    var method = player.Methods.Single(m => m.Name == name);
    var generator = new DynamicMethod("Fixture", typeof(void), Type.EmptyTypes).GetILGenerator();
    var input = method.Body.Instructions.Select(i =>
    {
        object? operand = i.Operand;
        if (operand is GenericInstanceMethod g && g.Name == "Instantiate" &&
            g.GenericArguments[0].FullName == go.FullName && g.Parameters.Count == 3 &&
            g.Parameters[1].ParameterType.FullName == "UnityEngine.Vector3" &&
            g.Parameters[2].ParameterType.FullName == "UnityEngine.Quaternion") operand = instantiateNative;
        if (operand is FieldReference f && f.DeclaringType.Name == "scrBulletTracer" && f.Name == "destination") operand = destination;
        if (operand is MethodReference r && r.DeclaringType.FullName == "UnityEngine.Transform" && r.Name == "set_rotation") operand = rotation;
        var code = new CodeInstruction(opcodes[i.OpCode.Value], operand);
        if (Equals(operand, instantiateNative))
        {
            code.labels.Add(generator.DefineLabel());
            code.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        }
        return code;
    }).ToArray();
    var before = input.Select(i => (i.opcode, i.operand, labels: i.labels.ToArray(), blocks: i.blocks.ToArray())).ToArray();
    var bad = input.Select(i => new CodeInstruction(i.opcode, i.operand)).ToList();
    bad.RemoveAt(bad.FindIndex(i => Equals(i.operand, instantiateNative)));
    var untouched = bad.Select(i => (i.opcode, i.operand)).ToArray();
    var rejected = false;
    try { WeaponSpawnPatch.Redirect(bad, name, spawnProxy, destination, destProxy, rotation, rotProxy, go).ToArray(); }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected && bad.Select(i => (i.opcode, i.operand)).SequenceEqual(untouched), name + " malformed contract mutated");
    var output = WeaponSpawnPatch.Redirect(input, name, spawnProxy, destination, destProxy, rotation, rotProxy, go).ToArray();
    var cursor = 0; var spawns = 0; var destinations = 0; var rotations = 0;
    foreach (var item in before)
    {
        if (Equals(item.operand, instantiateNative))
        {
            var receiver = output[cursor++];
            Require(receiver.opcode == Emit.Ldarg_0 && receiver.labels.SequenceEqual(item.labels) &&
                receiver.blocks.SequenceEqual(item.blocks), name + " metadata/receiver");
            var call = output[cursor++];
            Require(call.opcode == Emit.Call && Equals(call.operand, spawnProxy) && call.labels.Count == 0 &&
                call.blocks.Count == 0, name + " spawn adapter");
            spawns++;
        }
        else
        {
            var call = output[cursor++];
            if (Equals(call.operand, destProxy)) { Require(item.opcode == Emit.Stfld && Equals(item.operand, destination), name + " destination"); destinations++; }
            else if (Equals(call.operand, rotProxy)) { Require(Equals(item.operand, rotation), name + " tracer rotation"); rotations++; }
            else Require(call.opcode == item.opcode && Equals(call.operand, item.operand), name + " unrelated IL changed");
            Require(call.labels.SequenceEqual(item.labels) && call.blocks.SequenceEqual(item.blocks), name + " metadata changed");
        }
    }
    Require(cursor == output.Length && spawns > 0 && destinations == rotations, name + " exact replacement count");
    Console.WriteLine($"PASS {name}: {spawns} spawns; {destinations} tracer pairs; all other IL preserved");
    total += spawns;
}
Console.WriteLine($"PASS {total} shipping spawn call sites; static only, not Unity/headset validation.");
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static class Proxy
{
    public static void Spawn() { }
    public static void Destination() { }
    public static void Rotation() { }
}
