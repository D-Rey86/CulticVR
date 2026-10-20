using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class ChapterTwoMuzzles
{
    public static void Verify(string managed)
    {
        var path = Path.GetFullPath("src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll");
        var mod = Assembly.LoadFrom(path);
        // This independent data class has no plugin/Harmony/Unity native initialization.
        var type = mod.GetType("CulticVR.AimPreview.WeaponLandmarks", true)!;
        var points = (IDictionary)type.GetField("Points", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        float[] Read(string name)
        {
            if (!points.Contains(name)) throw new Exception("Missing muzzle sprite: " + name);
            var value = points[name]!;
            var t = value.GetType();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var muzzle = t.GetField("Muzzle", flags)!.GetValue(value)!;
            var casing = t.GetField("Casing", flags)!.GetValue(value)!;
            float Axis(object v, string axis) => (float)v.GetType().GetField(axis)!.GetValue(v)!;
            return new[] { Axis(muzzle,"x"), Axis(muzzle,"y"), Axis(casing,"x"), Axis(casing,"y") };
        }
        var baseline = JsonSerializer.Deserialize<Dictionary<string, float[]>>(
            File.ReadAllText("tests/WeaponMuzzle/chapter-one-landmarks.json"))!;
        foreach (var (name, expected) in baseline)
            if (!Read(name).SequenceEqual(expected)) throw new Exception("Chapter One landmark changed: " + name);

        // Shipping clip pointer curves, decoded at animation event times (RGB
        // light bindings precede Image on these clips). BB fires in Update.
        var firing = new[] { "sprWeaponSASG_1", "sprWeaponSASG_44",
            "sprWeaponSASGOfficer_1", "sprWeaponSASGOfficer_44",
            "sprWeaponLeeEnfield_1", "sprWeaponLeeEnfieldOfficer_1",
            "sprThompson_1", "sprThompson_4", "sprWeaponBBGun_0", "sprWeaponBBGun_1" };
        foreach (var name in firing) Read(name);
        var added = points.Keys.Cast<string>().Except(baseline.Keys).ToArray();
        if (added.Length != 48) throw new Exception("Unexpected new muzzle coverage");
        foreach (var name in added)
        {
            var p = Read(name);
            if (p[0] <= 0 || p[0] >= 480 || p[1] <= 0 || p[1] >= 220 || p[2] != -1 || p[3] != -1)
                throw new Exception("Invalid or unintended casing annotation: " + name);
        }
        foreach (var name in new[] { "sprWeaponRevolver_2", "sprWeaponRevolverOfficer_65",
            "sprWeaponBBGun_8", "sprWeaponLeeEnfield_40", "unknown" })
            if (points.Contains(name)) throw new Exception("Unrequested/hidden/scoped sprite opted in: " + name);

        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var method = assembly.MainModule.Types.Single(t => t.FullName == "CulticVR.AimTrial.AimTrialPlugin")
            .Methods.Single(m => m.Name == "TryWeaponSpawn");
        var anchor = method.Body.Instructions.Single(i => i.Operand is FieldReference f && f.Name == "weaponTableID");
        bool Admitted(int id)
        {
            var stack = new Stack<int>(); stack.Push(id);
            var locals = new Dictionary<int,int>();
            var instruction = anchor.Next;
            for (var step = 0; step < 150; step++)
            {
                var next = instruction.Next;
                switch (instruction.OpCode.Code)
                {
                    case Code.Stloc: case Code.Stloc_S:
                        locals[((VariableDefinition)instruction.Operand).Index] = stack.Pop(); break;
                    case Code.Ldloc: case Code.Ldloc_S:
                        stack.Push(locals[((VariableDefinition)instruction.Operand).Index]); break;
                    case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
                        locals[(int)instruction.OpCode.Code-(int)Code.Stloc_0] = stack.Pop(); break;
                    case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
                        stack.Push(locals[(int)instruction.OpCode.Code-(int)Code.Ldloc_0]); break;
                    case Code.Ldc_I4_0: case Code.Ldc_I4_1: case Code.Ldc_I4_2: case Code.Ldc_I4_3:
                    case Code.Ldc_I4_4: case Code.Ldc_I4_5: case Code.Ldc_I4_6: case Code.Ldc_I4_7: case Code.Ldc_I4_8:
                        stack.Push((int)instruction.OpCode.Code-(int)Code.Ldc_I4_0); break;
                    case Code.Ldc_I4: case Code.Ldc_I4_S: stack.Push(Convert.ToInt32(instruction.Operand)); break;
                    case Code.Beq: case Code.Beq_S:
                        if (stack.Pop() == stack.Pop()) next=(Instruction)instruction.Operand; break;
                    case Code.Bne_Un: case Code.Bne_Un_S:
                        if (stack.Pop() != stack.Pop()) next=(Instruction)instruction.Operand; break;
                    case Code.Br: case Code.Br_S: case Code.Leave: case Code.Leave_S:
                        next=(Instruction)instruction.Operand; break;
                    case Code.Ldarg_0: case Code.Ldarg_1: case Code.Nop: break;
                    case Code.Call:
                        if (((MethodReference)instruction.Operand).Name == "UseControllerAim") return true;
                        throw new Exception("Unexpected muzzle gate dependency");
                    case Code.Ret: return stack.Pop() != 0;
                    default: throw new Exception("Unreviewed muzzle gate instruction: " + instruction);
                }
                instruction = next;
            }
            throw new Exception("Muzzle gate did not terminate");
        }
        var allowed = new[] {1,2,3,4,5,9,10,12,14,15};
        for (var id = -2; id <= 32; id++)
            if (Admitted(id) != allowed.Contains(id)) throw new Exception("Muzzle weapon gate: " + id);
        Console.WriteLine($"PASS {baseline.Count} unchanged Chapter One landmarks; 48 new muzzle-only sprites; firing coverage; 35 weapon gate cases; revolver unchanged.");
    }
}
