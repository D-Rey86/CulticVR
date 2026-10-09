using Mono.Cecil;
using Mono.Cecil.Cil;

// Interpret the candidate's actual guard IL with explicitly supplied dependency
// outcomes. This is a predicate proof, NOT Unity objects or native hook execution.
record struct GateFixture(int Flags, int Temp, int State, int Zoom, int Connection)
{
    public bool Bit(int index) => (Flags & (1 << index)) != 0;
}
static class GateReplay
{
    enum Marker { Trial, Player, Temporary, Game, ZoomDelegate }
    record struct ZoomSlot(int Value);
    public static (bool Enabled, int PoseCalls) Evaluate(MethodDefinition method, GateFixture fixture)
    {
        var stack = new Stack<object?>();
        var locals = new Dictionary<int, object?>();
        var instruction = method.Body.Instructions[0];
        var poseCalls = 0;
        for (var step = 0; step < 200; step++)
        {
            var next = instruction.Next;
            switch (instruction.OpCode.Code)
            {
                case Code.Nop: break;
                case Code.Ldarg_0: stack.Push(Marker.Trial); break;
                case Code.Ldarg_1: stack.Push(fixture.Bit(2) ? Marker.Player : null); break;
                case Code.Ldnull: stack.Push(null); break;
                case Code.Ldc_I4_0: stack.Push(0); break;
                case Code.Ldc_I4_1: stack.Push(1); break;
                case Code.Ldc_I4_2: stack.Push(2); break;
                case Code.Ldc_I4_7: stack.Push(7); break;
                case Code.Ldfld:
                    var receiver = stack.Pop();
                    if (receiver == null) throw new InvalidOperationException("Null field receiver reached");
                    stack.Push(((FieldReference)instruction.Operand).Name switch
                    {
                        "_bazookaReady" => fixture.Bit(0),
                        "_disposableReady" => fixture.Bit(0),
                        "_ready" => fixture.Bit(1),
                        "tempWeapon" => fixture.Temp == -1 ? null : Marker.Temporary,
                        "tWepType" => fixture.Temp,
                        "weaponState" => fixture.State,
                        "connectionStatus" => fixture.Connection,
                        "playerID" => fixture.Bit(5) ? 7 : 8,
                        "localPlayerID" => 7,
                        _ => throw new InvalidOperationException($"Unexpected field: {instruction}")
                    });
                    break;
                case Code.Ldsfld:
                    if (((FieldReference)instruction.Operand).Name != "ZoomState") throw new InvalidOperationException("Unexpected static field");
                    stack.Push(Marker.ZoomDelegate); break;
                case Code.Ldind_I4:
                    if (stack.Pop() is not ZoomSlot slot) throw new InvalidOperationException("Unexpected indirect guard read");
                    stack.Push(slot.Value); break;
                case Code.Stloc_0: locals[0] = stack.Pop(); break;
                case Code.Ldloc_0: stack.Push(locals[0]); break;
                case Code.Brfalse: case Code.Brfalse_S:
                    if (!Truth(stack.Pop())) next = (Instruction)instruction.Operand;
                    break;
                case Code.Brtrue: case Code.Brtrue_S:
                    if (Truth(stack.Pop())) next = (Instruction)instruction.Operand;
                    break;
                case Code.Bne_Un: case Code.Bne_Un_S:
                    var right = stack.Pop();
                    if (!Equals(stack.Pop(), right)) next = (Instruction)instruction.Operand;
                    break;
                case Code.Beq: case Code.Beq_S:
                    var equalRight = stack.Pop();
                    if (Equals(stack.Pop(), equalRight)) next = (Instruction)instruction.Operand;
                    break;
                case Code.Br: case Code.Br_S: next = (Instruction)instruction.Operand; break;
                case Code.Call: case Code.Callvirt:
                    var call = (MethodReference)instruction.Operand;
                    switch (call.Name)
                    {
                        case "op_Equality":
                            var rhs = stack.Pop();
                            stack.Push(Equals(stack.Pop(), rhs));
                            break;
                        case "op_Inequality":
                            var unequalRhs = stack.Pop();
                            stack.Push(!Equals(stack.Pop(), unequalRhs));
                            break;
                        case "IsActiveGameplayPlayer":
                            if (!Equals(stack.Pop(), Marker.Player)) throw new InvalidOperationException("Gameplay gate receiver");
                            stack.Push(fixture.Bit(3)); break;
                        case "get_Instance": stack.Push(fixture.Bit(4) ? Marker.Game : null); break;
                        case "Invoke":
                            if (!Equals(stack.Pop(), Marker.Player) || !Equals(stack.Pop(), Marker.ZoomDelegate)) throw new InvalidOperationException("Zoom delegate receiver");
                            stack.Push(new ZoomSlot(fixture.Zoom)); break;
                        case "UpdatePose":
                            if (!Equals(stack.Pop(), Marker.Player) || !Equals(stack.Pop(), Marker.Trial)) throw new InvalidOperationException("Pose receiver");
                            poseCalls++; stack.Push(fixture.Bit(6)); break;
                        case "UseDisposableAim":
                            if (!Equals(stack.Pop(), Marker.Player) || !Equals(stack.Pop(), Marker.Trial)) throw new InvalidOperationException("Disposable gate receiver");
                            var nested = Evaluate(method.DeclaringType.Methods.Single(m => m.Name == "UseDisposableAim"), fixture);
                            poseCalls += nested.PoseCalls; stack.Push(nested.Enabled); break;
                        default: throw new InvalidOperationException($"Unexpected gate call: {instruction}");
                    }
                    break;
                case Code.Ret: return (Truth(stack.Pop()), poseCalls);
                default: throw new InvalidOperationException($"Unsupported guard instruction: {instruction}");
            }
            instruction = next;
        }
        throw new InvalidOperationException("Guard did not terminate");
    }
    static bool Truth(object? value) => value switch { null => false, bool b => b, int n => n != 0, _ => true };
}
