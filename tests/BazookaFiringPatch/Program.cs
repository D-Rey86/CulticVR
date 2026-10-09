using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using CulticVR.AimTrial;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using EmitOpCodes = System.Reflection.Emit.OpCodes;

var gameDir = args.Length == 0 ? @"G:\Games\Steam\steamapps\common\CULTIC" : args[0];
var managed = Path.Combine(gameDir, "CULTIC_Data", "Managed");
var nativePath = Path.Combine(managed, "Assembly-CSharp.dll");
Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath))) ==
    "0AC77697A3F08589E20AF683DA2DE78E664DBE0726397280905608423E557CDC", "Shipping target hash changed");
AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
{
    var path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
};
var game = Assembly.LoadFrom(nativePath);
var player = game.GetType("scrPlayerControl")!;
var temporary = game.GetType("TempWeapon")!;
var enumType = temporary.GetNestedType("TWepType")!;
Require(Convert.ToInt32(Enum.Parse(enumType, "Bazooka")) == 0 && Convert.ToInt32(Enum.Parse(enumType, "MG")) == 1,
    "Native enum order changed");
var tempField = player.GetField("tempWeapon")!;
var typeField = temporary.GetField("tWepType")!;
var prefabField = player.GetField("prefabBank")!;
var cameraField = game.GetType("scrGameControl")!.GetField("mainCam")!;
var playerCameraField = player.GetField("playerCamera")!;
var playerParentField = player.GetField("playerCameraParent")!;
var bulletMethod = player.GetMethod("castBullet")!;
var proxy = typeof(TestProxy).GetMethod(nameof(TestProxy.Camera))!;
using var module = AssemblyDefinition.ReadAssembly(nativePath);
var update = module.MainModule.Types.Single(t => t.Name == "scrPlayerControl").Methods.Single(m => m.Name == "Update");
var opcodeMap = typeof(EmitOpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
    .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
    .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

// Decode shipping opcodes/dispatch labels with Cecil because this Unity assembly
// fails the legacy offline Harmony reader. Irrelevant operands remain opaque:
// this exercises production selection, NOT executable Unity IL or patch startup.
List<CodeInstruction> NativeInstructions()
{
    var generator = new DynamicMethod("BazookaFixture", typeof(void), Type.EmptyTypes).GetILGenerator();
    var targets = update.Body.Instructions.Where(i => i.Operand is Instruction[])
        .SelectMany(i => (Instruction[])i.Operand).Distinct().ToDictionary(i => i, _ => generator.DefineLabel());
    return update.Body.Instructions.Select(i =>
    {
        object? operand = i.Operand;
        if (operand is FieldReference f)
        {
            if (f.DeclaringType.Name == "scrPlayerControl" && f.Name == "tempWeapon") operand = tempField;
            if (f.DeclaringType.Name == "TempWeapon" && f.Name == "tWepType") operand = typeField;
            if (f.DeclaringType.Name == "scrPlayerControl" && f.Name == "prefabBank") operand = prefabField;
            if (f.DeclaringType.Name == "scrGameControl" && f.Name == "mainCam") operand = cameraField;
            if (f.DeclaringType.Name == "scrPlayerControl" && f.Name == "playerCamera") operand = playerCameraField;
            if (f.DeclaringType.Name == "scrPlayerControl" && f.Name == "playerCameraParent") operand = playerParentField;
        }
        if (operand is MethodReference m && m.DeclaringType.Name == "scrPlayerControl" && m.Name == "castBullet") operand = bulletMethod;
        if (operand is Instruction[] entries) operand = entries.Select(t => targets[t]).ToArray();
        var result = new CodeInstruction(opcodeMap[i.OpCode.Value], operand);
        if (targets.TryGetValue(i, out var label)) result.labels.Add(label);
        // Extra metadata on camera reads tests that the production replacement
        // retains existing labels/exception boundaries, even inside another patch.
        if (Equals(operand, cameraField))
        {
            result.labels.Add(generator.DefineLabel());
            result.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        }
        return result;
    }).ToList();
}
CodeInstruction[] Run(List<CodeInstruction> instructions) =>
    BazookaFiringPatch.Redirect(instructions, tempField, typeField, prefabField, cameraField, proxy).ToArray();
var input = NativeInstructions();
var before = input.Select(Snapshot.From).ToArray();
var result = Run(input);
Require(result.Length == before.Length, "Instruction count changed");
var replacements = new List<int>();
for (var index = 0; index < result.Length; index++)
{
    var actual = Snapshot.From(result[index]);
    if (result[index].opcode == EmitOpCodes.Call && Equals(result[index].operand, proxy))
    {
        replacements.Add(index);
        Require(before[index].Opcode == EmitOpCodes.Ldfld && Equals(before[index].Operand, cameraField), "Wrong field replaced");
        Require(actual.Labels.SequenceEqual(before[index].Labels) && actual.Blocks.SequenceEqual(before[index].Blocks), "Rewrite lost metadata");
    }
    else Require(before[index].Same(actual), $"Unrelated native Update instruction changed: {index}");
}
Require(replacements.Count == 4, "Expected exactly four rocket camera substitutions");
var dispatch = input.FindIndex(i => i.opcode == EmitOpCodes.Switch && i.operand is Label[] labels && labels.Length == 4 &&
    input[input.IndexOf(i) - 3].operand is FieldInfo f && Equals(f, typeField));
Require(dispatch >= 0, "Expected native temporary-weapon dispatch");
var labels = (Label[])input[dispatch].operand;
var start = input.FindIndex(i => i.labels.Contains(labels[0]));
var end = input.FindIndex(i => i.labels.Contains(labels[1]));
Require(replacements.All(index => index >= start && index < end), "Another temporary weapon was redirected");
var composed = NativeInstructions();
var otherProxy = typeof(TestProxy).GetMethod(nameof(TestProxy.OtherCamera))!;
for (var index = 0; index < composed.Count; index++)
{
    if (index >= start && index < end || !Equals(composed[index].operand, cameraField)) continue;
    composed[index].opcode = EmitOpCodes.Call;
    composed[index].operand = otherProxy;
}
var composedBefore = composed.Select(Snapshot.From).ToArray();
Run(composed);
Require(composed.Select((i, index) => replacements.Contains(index) || composedBefore[index].Same(Snapshot.From(i))).All(v => v),
    "Interference with another patch outside the bazooka branch");

var failures = 0;
void Reject(Action<List<CodeInstruction>> corrupt)
{
    var fixture = NativeInstructions();
    corrupt(fixture);
    var snapshots = fixture.Select(Snapshot.From).ToArray();
    var rejected = false;
    try { Run(fixture); }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected, "Changed structure was accepted");
    Require(snapshots.Length == fixture.Count && snapshots.Where((s, i) => !s.Same(Snapshot.From(fixture[i]))).Count() == 0,
        "Rejected rewrite partially mutated its input");
    failures++;
}
Reject(f => f.RemoveAt(replacements[0]));
Reject(f => f.Insert(replacements[0], new CodeInstruction(EmitOpCodes.Ldfld, cameraField)));
Reject(f => f[start].labels.Remove(labels[0]));
Reject(f => f[dispatch].operand = new int[4]);
Reject(f => f[dispatch - 2].operand = new object());
Reject(f => f.Insert(dispatch, new CodeInstruction(EmitOpCodes.Ldfld, typeField)));
Reject(f =>
{
    var prefab = f.FindIndex(start, end - start, i => Equals(i.operand, prefabField));
    f[prefab + 1].operand = (sbyte)32;
});
Console.WriteLine($"PASS: production selector changes ONLY four Bazooka camera loads in {result.Length} shipping Update instructions; all other operands/labels/blocks preserved.");
Console.WriteLine($"PASS: {failures} malformed native-contract fixtures rejected with zero partial mutations; MG/LP42/BBGun/movement/scope/interaction/throw-preview instructions untouched.");
Console.WriteLine("PASS: identical four-load selection after other camera substitutions outside the bazooka branch; existing replacements preserved.");
using var candidate = AssemblyDefinition.ReadAssembly(Path.GetFullPath("src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll"));
var gate = candidate.MainModule.Types.Single(t => t.FullName == "CulticVR.AimTrial.AimTrialPlugin").Methods.Single(m => m.Name == "UseBazookaAim");
var cases = 0;
for (var flags = 0; flags < 128; flags++)
foreach (var temp in new[] { -1,0,1,2,3,99 })
foreach (var state in new[] { -1,0,5,6,7,8 })
foreach (var zoom in new[] { -1,0,1 })
foreach (var connection in new[] { 0,1,2 })
{
    var fixture = new GateFixture(flags, temp, state, zoom, connection);
    var (enabled, poseCalls) = GateReplay.Evaluate(gate, fixture);
    var permitsPose = fixture.Bit(0) && fixture.Bit(1) && fixture.Bit(2) && fixture.Bit(3) && fixture.Bit(4) && fixture.Bit(5) &&
        temp == 0 && state == 7 && zoom == 0 && connection == 0;
    Require(enabled == (permitsPose && fixture.Bit(6)) && poseCalls == (permitsPose ? 1 : 0), $"Compiled gate mismatch: {fixture}");
    cases++;
}
Console.WriteLine($"PASS: {cases} compiled bazooka-gate cases: patch readiness, player/temp identity, draw/drop/ready state, zoom, active gameplay, offline/local ownership and tracked-pose success/failure.");
DisposableTests.Run(NativeInstructions, tempField, typeField, prefabField, cameraField, playerCameraField, playerParentField,
    bulletMethod, module, candidate);
using var accepted = AssemblyDefinition.ReadAssembly(Path.GetFullPath("deployment/backups/disposable-baseline-20261005/CulticVR.dll"));
AdapterReplay.Run(accepted.MainModule.Types.Single(t => t.FullName == "CulticVR.AimTrial.AimTrialPlugin"),
    candidate.MainModule.Types.Single(t => t.FullName == "CulticVR.AimTrial.AimTrialPlugin"));
Console.WriteLine("STATIC ONLY: does not execute Unity, Harmony patch startup, headset rendering/impact accuracy or performance.");

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
public static class TestProxy
{
    public static object Camera(object game) => game;
    public static object OtherCamera(object game) => game;
    public static object Player(object player) => player;
    public static bool Assist(object player) => false;
    public static object DisposableCamera(object game) => game;
    public static object DisposablePlayer(object player) => player;
    public static object DisposableParent(object player) => player;
    public static bool DisposableAssist(object player) => false;
}
record Snapshot(System.Reflection.Emit.OpCode Opcode, object? Operand, Label[] Labels, ExceptionBlock[] Blocks)
{
    public static Snapshot From(CodeInstruction i) => new(i.opcode, i.operand, i.labels.ToArray(), i.blocks.ToArray());
    public bool Same(Snapshot other) => Opcode == other.Opcode && Equals(Operand, other.Operand) &&
        Labels.SequenceEqual(other.Labels) && Blocks.SequenceEqual(other.Blocks);
}
