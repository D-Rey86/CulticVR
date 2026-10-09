using Mono.Cecil;
using Mono.Cecil.Cil;

// Candidate adapter control-flow replay with explicit guard outcomes. No Unity
// execution: compare normal/throw fallback routing to the exact accepted DLL.
static class AdapterReplay
{
    enum Value { Trial, Player, Game, ProxyCamera, NativeCamera, ProxyObject, NativeObject, Field, Type }
    public static void Run(TypeDefinition accepted, TypeDefinition candidate)
    {
        var cases = 0;
        foreach (var pair in new[] {
            ("AimCamera", "AimCameraForDisposableBullet"),
            ("AimPlayerCamera", "AimPlayerCameraForDisposableBullet"),
            ("AimAssistFlag", "AimAssistFlagForDisposableBullet") })
        for (var flags = 0; flags < 64; flags++)
        {
            var old = Evaluate(accepted.Methods.Single(m => m.Name == pair.Item1), flags);
            var updated = Evaluate(candidate.Methods.Single(m => m.Name == pair.Item2), flags);
            var mgEnabled = (flags & 1) != 0 && (flags & 16) != 0 && (pair.Item1 != "AimCamera" || (flags & 2) != 0);
            object expected = mgEnabled ? pair.Item1 switch { "AimCamera" => Value.ProxyCamera, "AimPlayerCamera" => Value.ProxyObject, _ => false } : old.Result;
            if (!Equals(updated.Result, expected) || updated.PlayerLookups != old.PlayerLookups)
                throw new InvalidOperationException($"Adapter fallback/lookup mismatch: {pair}/{flags}");
            cases++;
        }
        Console.WriteLine($"PASS: {cases} compiled bullet-adapter fixtures preserve accepted regular/scope/physical-throw/native/assist routing; only enabled MG gains proxy/assist suppression; no duplicate player lookup.");
    }

    static (object Result, int PlayerLookups) Evaluate(MethodDefinition method, int flags)
    {
        bool Bit(int index) => (flags & (1 << index)) != 0;
        var stack = new Stack<object?>(); var locals = new Dictionary<int, object?>();
        var instruction = method.Body.Instructions[0]; var lookups = 0;
        for (var step = 0; step < 200; step++)
        {
            var next = instruction.Next;
            switch (instruction.OpCode.Code)
            {
                case Code.Nop: break;
                case Code.Ldarg_0: stack.Push(method.Parameters[0].ParameterType.Name == "scrGameControl" ? Value.Game : Value.Player); break;
                case Code.Ldnull: stack.Push(null); break;
                case Code.Ldc_I4_0: stack.Push(false); break;
                case Code.Ldsfld:
                    if (((FieldReference)instruction.Operand).Name != "Instance") throw new InvalidOperationException("Unexpected adapter static field");
                    stack.Push(Bit(0) ? Value.Trial : null); break;
                case Code.Ldfld:
                    if (stack.Pop() == null) throw new InvalidOperationException("Null adapter receiver");
                    stack.Push(((FieldReference)instruction.Operand).Name switch {
                        "_aimCamera" => Value.ProxyCamera, "_aimObject" => Value.ProxyObject,
                        "mainCam" => Value.NativeCamera, "playerCamera" => Value.NativeObject,
                        _ => throw new InvalidOperationException("Unexpected adapter field") }); break;
                case Code.Stloc_0: locals[0] = stack.Pop(); break;
                case Code.Stloc_1: locals[1] = stack.Pop(); break;
                case Code.Ldloc_0: stack.Push(locals[0]); break;
                case Code.Ldloc_1: stack.Push(locals[1]); break;
                case Code.Dup: stack.Push(stack.Peek()); break;
                case Code.Pop: stack.Pop(); break;
                case Code.Brtrue: case Code.Brtrue_S: if (Truth(stack.Pop())) next = (Instruction)instruction.Operand; break;
                case Code.Brfalse: case Code.Brfalse_S: if (!Truth(stack.Pop())) next = (Instruction)instruction.Operand; break;
                case Code.Br: case Code.Br_S: next = (Instruction)instruction.Operand; break;
                case Code.Ldtoken: stack.Push(Value.Type); break;
                case Code.Ldstr: stack.Push(instruction.Operand); break;
                case Code.Unbox_Any:
                    if (((TypeReference)instruction.Operand).FullName != "System.Boolean" || stack.Peek() is not bool) throw new InvalidOperationException("Unexpected adapter unbox");
                    break;
                case Code.Call: case Code.Callvirt:
                    var call = (MethodReference)instruction.Operand;
                    switch (call.Name)
                    {
                        case "LocalPlayer":
                            if (!Equals(stack.Pop(), Value.Trial)) throw new InvalidOperationException("Player lookup receiver");
                            lookups++; stack.Push(Bit(1) ? Value.Player : null); break;
                        case "op_Equality": case "op_Inequality":
                            var rhs = stack.Pop(); var equality = Equals(stack.Pop(), rhs);
                            stack.Push(call.Name == "op_Equality" ? equality : !equality); break;
                        case "UsePendingPhysicalThrowAim": case "UseControllerAim": case "UseDisposableHitscanAim":
                            if (!Equals(stack.Pop(), Value.Player) || !Equals(stack.Pop(), Value.Trial)) throw new InvalidOperationException("Guard receiver");
                            stack.Push(Bit(call.Name == "UsePendingPhysicalThrowAim" ? 2 : call.Name == "UseControllerAim" ? 3 : 4)); break;
                        case "AimAssistFlag":
                            if (!Equals(stack.Pop(), Value.Player)) throw new InvalidOperationException("Assist receiver");
                            var delegated = Evaluate(method.DeclaringType.Methods.Single(m => m.Name == "AimAssistFlag"), flags);
                            stack.Push(delegated.Result); lookups += delegated.PlayerLookups; break;
                        case "GetTypeFromHandle": stack.Pop(); stack.Push(Value.Type); break;
                        case "Field": stack.Pop(); stack.Pop(); stack.Push(Value.Field); break;
                        case "GetValue":
                            if (!Equals(stack.Pop(), Value.Player) || !Equals(stack.Pop(), Value.Field)) throw new InvalidOperationException("Assist field receiver");
                            stack.Push(Bit(5)); break;
                        default: throw new InvalidOperationException($"Unexpected adapter call: {instruction}");
                    }
                    break;
                case Code.Ret: return (stack.Pop()!, lookups);
                default: throw new InvalidOperationException($"Unsupported adapter instruction: {instruction}");
            }
            instruction = next;
        }
        throw new InvalidOperationException("Adapter did not terminate");
    }
    static bool Truth(object? value) => value switch { null => false, bool b => b, _ => true };
}
