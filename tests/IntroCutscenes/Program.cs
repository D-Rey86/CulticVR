using Mono.Cecil;
using Mono.Cecil.Cil;

// Replay the production compiled gates with managed property/field fixtures.
// Never initialize a Unity plugin or call native rendering/physics from .NET.
using var mod = AssemblyDefinition.ReadAssembly("src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll");
var world = mod.MainModule.Types.Single(t => t.FullName == "CulticVR.WorldStereoTrial.WorldStereoTrialPlugin");
var ui = mod.MainModule.Types.Single(t => t.FullName == "CulticVR.UiTrial.UiTrialPlugin");
var collision = world.Methods.Single(m => m.Name == "ConstrainHeadPosition");
var continuation = collision.Body.Instructions.First(i => i.OpCode.Code == Code.Ret).Next;
var selector = ui.NestedTypes.SelectMany(t => t.Methods).Single(m => m.HasBody &&
    m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "IsEpisodeIntroCanvas"));
var bodyReached = new object();
var desired = new object(); // Exact input identity must be returned, unmodified.
int cases = 0;
foreach (var state in new[] { 0, 1, 2, 3, 4, 0, 3, 0 })
{
    var self = Node(("_player", Node(("playerHUD", Node(("cutsceneState", state))))),
        ("_hasSafeHeadPosition", true), ("_headCollisionBody", new object()));
    var result = Run(collision, new[] { self, desired }, continuation);
    Require(ReferenceEquals(result, state == 0 ? bodyReached : desired), "camera ownership by HUD state");
    Require(Truth(self["_hasSafeHeadPosition"]) == (state == 0), "saved gameplay contact reset");
    Require((self["_headCollisionBody"] != null) == (state == 0), "cached collider reset");
    cases++;
}
foreach (var player in new object?[] { null, Node(("playerHUD", null)) })
{
    Require(ReferenceEquals(Run(collision, new object?[] { Node(("_player", player)), desired }, continuation),
        bodyReached), "missing player/HUD retains existing fallback");
    cases++;
}

foreach (var name in new[] { "sceneE2M0", "sceneE2M1", "sceneE1M1", "sceneE2M2", "sceneMainMenu", "sceneLoading" })
foreach (var active in new[] { false, true })
foreach (var parentName in new[] { "introCutscene", "amalgamCutscene", "other" })
foreach (var mode in new[] { 0, 1, 2 })
foreach (var root in new[] { false, true })
foreach (var sameScene in new[] { false, true })
{
    var scene = Node(("name", name), ("valid", true));
    var source = Node(("gameObject", Node(("scene", scene))), ("transform", Node(("parent", new object()))));
    var canvas = Node(("gameObject", Node(("scene", sameScene ? scene : Node(("name", name), ("valid", true))),
        ("activeInHierarchy", active))), ("name", "Canvas"), ("isRootCanvas", root),
        ("renderMode", mode), ("worldCamera", null), ("transform", Node(("parent", Node(("name", parentName))))));
    var result = Run(selector, new object?[] { Node(("source", source)), canvas });
    Require(Truth(result) == (sameScene && root && mode == 0 && parentName == "introCutscene" &&
        (name == "sceneE2M0" || name == "sceneE2M1")), "intro canvas scope/inactive activation");
    // Existing HUD eligibility must survive unchanged, in every scene.
    canvas["worldCamera"] = source;
    Require(Truth(Run(selector, new object?[] { Node(("source", source)), canvas })) ==
        (root && (active || (sameScene && mode == 0 && parentName == "introCutscene" &&
        (name == "sceneE2M0" || name == "sceneE2M1")))), "existing HUD eligibility");
    foreach (var excluded in new[] { "renderCanvas", "scalingCanvas" })
    {
        canvas["name"] = excluded;
        Require(!Truth(Run(selector, new object?[] { Node(("source", source)), canvas })), "world/viewmodel canvas excluded");
    }
    cases += 4;
}
Require(!Truth(Run(selector, new object?[] { Node(("source", null)), null })), "null canvas rejected");
Console.WriteLine($"PASS {cases + 1} compiled cinematic collision/canvas cases, including inactive Chapter Two cards, gameplay handoff and missing references. No Unity rendering or physics acceptance claimed.");

object? Run(MethodDefinition method, object?[] args, Instruction? stop = null)
{
    var stack = new Stack<object?>();
    var locals = new Dictionary<int, object?>();
    var ip = method.Body.Instructions[0];
    for (var step = 0; step < 300; step++)
    {
        if (ip == stop) return bodyReached;
        var next = ip.Next;
        switch (ip.OpCode.Code)
        {
            case Code.Nop: break;
            case Code.Ldarg_0: stack.Push(args[0]); break;
            case Code.Ldarg_1: stack.Push(args[1]); break;
            case Code.Ldarga: case Code.Ldarga_S: stack.Push(args[((ParameterDefinition)ip.Operand).Index]); break;
            case Code.Ldloc_0: stack.Push(locals[0]); break;
            case Code.Ldloca: case Code.Ldloca_S: stack.Push(locals[((VariableDefinition)ip.Operand).Index]); break;
            case Code.Stloc_0: locals[0] = stack.Pop(); break;
            case Code.Ldnull: stack.Push(null); break;
            case Code.Ldc_I4_0: stack.Push(0); break;
            case Code.Ldc_I4_1: stack.Push(1); break;
            case Code.Ldstr: stack.Push(ip.Operand); break;
            case Code.Ldfld: stack.Push(((Dictionary<string, object?>)stack.Pop()!)[((FieldReference)ip.Operand).Name]); break;
            case Code.Stfld:
                var value = stack.Pop();
                ((Dictionary<string, object?>)stack.Pop()!)[((FieldReference)ip.Operand).Name] = value; break;
            case Code.Brfalse: case Code.Brfalse_S: if (!Truth(stack.Pop())) next = (Instruction)ip.Operand; break;
            case Code.Brtrue: case Code.Brtrue_S: if (Truth(stack.Pop())) next = (Instruction)ip.Operand; break;
            case Code.Br: case Code.Br_S: next = (Instruction)ip.Operand; break;
            case Code.Call: case Code.Callvirt:
                var call = (MethodReference)ip.Operand;
                if (call.Name is "op_Equality" or "op_Inequality")
                {
                    var right = stack.Pop(); var left = stack.Pop();
                    stack.Push(Equals(left, right) == (call.Name == "op_Equality"));
                }
                else if (call.Name.StartsWith("get_", StringComparison.Ordinal))
                    stack.Push(((Dictionary<string, object?>)stack.Pop()!)[call.Name[4..]]);
                else if (call.Name == "IsValid") stack.Push(((Dictionary<string, object?>)stack.Pop()!)["valid"]);
                else if (call.Name == "ResetHeadCollision")
                    Run(world.Methods.Single(m => m.Name == call.Name), new[] { stack.Pop() });
                else if (call.Name == "IsEpisodeIntroCanvas")
                {
                    var scene = stack.Pop(); var canvas = stack.Pop();
                    stack.Push(Run(ui.Methods.Single(m => m.Name == call.Name), new[] { canvas, scene }));
                }
                else throw new Exception("Unexpected/native call in replay: " + call.FullName);
                break;
            case Code.Ret: return method.ReturnType.FullName == "System.Void" ? null : stack.Pop();
            default: throw new Exception("Unreviewed instruction: " + ip);
        }
        ip = next;
    }
    throw new Exception("Replay did not terminate");
}
static Dictionary<string, object?> Node(params (string key, object? value)[] fields) => fields.ToDictionary(f => f.key, f => f.value);
static bool Truth(object? value) => value switch { null => false, bool b => b, int n => n != 0, _ => true };
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
