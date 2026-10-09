using System.Reflection;
using System.Reflection.Emit;
using CulticVR.AimTrial;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using EmitOpCodes = System.Reflection.Emit.OpCodes;

static class DisposableTests
{
    public static void Run(Func<List<CodeInstruction>> nativeInstructions, FieldInfo temp, FieldInfo type, FieldInfo prefab,
        FieldInfo camera, FieldInfo playerCamera, FieldInfo parent, MethodInfo bullet,
        AssemblyDefinition native, AssemblyDefinition candidate)
    {
        MethodInfo Proxy(string name) => typeof(TestProxy).GetMethod(name)!;
        var cameraProxy = Proxy(nameof(TestProxy.DisposableCamera));
        var playerProxy = Proxy(nameof(TestProxy.DisposablePlayer));
        var parentProxy = Proxy(nameof(TestProxy.DisposableParent));
        var newMethods = new[] { cameraProxy, playerProxy, parentProxy };
        List<CodeInstruction> Apply(IEnumerable<CodeInstruction> source) => DisposableFiringPatch.Redirect(source,
            temp, type, prefab, camera, playerCamera, parent, bullet, cameraProxy, playerProxy, parentProxy).ToList();
        var instructions = nativeInstructions();
        var before = instructions.Select(Snapshot.From).ToArray();
        var dispatch = instructions.FindIndex(i => i.opcode == EmitOpCodes.Switch && i.operand is Label[] labels && labels.Length == 4 &&
            Equals(instructions[instructions.IndexOf(i) - 3].operand, type));
        var entries = (Label[])instructions[dispatch].operand;
        var mgStart = instructions.FindIndex(i => i.labels.Contains(entries[1]));
        var flareStart = instructions.FindIndex(i => i.labels.Contains(entries[2]));
        var bbStart = instructions.FindIndex(i => i.labels.Contains(entries[3]));
        Apply(instructions);
        var changed = new List<int>();
        for (var index = 0; index < instructions.Count; index++)
        {
            var actual = Snapshot.From(instructions[index]);
            if (instructions[index].opcode == EmitOpCodes.Call && newMethods.Contains(instructions[index].operand))
            {
                changed.Add(index);
                Require(index >= mgStart && index < bbStart && before[index].Opcode == EmitOpCodes.Ldfld, "Wrong disposable branch/load");
                var expected = Equals(before[index].Operand, camera) ? cameraProxy : Equals(before[index].Operand, playerCamera) ? playerProxy : parentProxy;
                Require(Equals(actual.Operand, expected), "Wrong camera adapter");
                Require(actual.Labels.SequenceEqual(before[index].Labels) && actual.Blocks.SequenceEqual(before[index].Blocks), "Lost instruction metadata");
            }
            else Require(before[index].Same(actual), $"Unrelated Update instruction changed: {index}");
        }
        Require(changed.Count(index => index < flareStart) == 10 && changed.Count(index => index >= flareStart) == 5, "MG/flare counts");

        var failures = 0;
        void Reject(Action<List<CodeInstruction>> corrupt)
        {
            var fixture = nativeInstructions(); corrupt(fixture);
            var snapshots = fixture.Select(Snapshot.From).ToArray();
            var rejected = false;
            try { Apply(fixture); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && snapshots.Where((s, i) => !s.Same(Snapshot.From(fixture[i]))).Count() == 0, "Rejection mutated or accepted input");
            failures++;
        }
        Reject(f => f.RemoveAt(changed[0]));
        Reject(f => f.Insert(changed[^1], new CodeInstruction(EmitOpCodes.Ldfld, camera)));
        Reject(f => f[flareStart].labels.Clear());
        Reject(f => f[bbStart].labels.Clear());
        Reject(f => f[dispatch].operand = new int[4]);
        Reject(f => f[dispatch - 2].operand = new object());
        Reject(f => f[f.FindIndex(mgStart, flareStart - mgStart, i => Equals(i.operand, bullet))].opcode = EmitOpCodes.Nop);
        Reject(f => f[f.FindIndex(flareStart, bbStart - flareStart, i => Equals(i.operand, prefab)) + 1].operand = (sbyte)37);
        Reject(f => f[f.FindIndex(mgStart, flareStart - mgStart, i => Equals(i.operand, prefab)) + 1].opcode = EmitOpCodes.Ldc_I4_1);

        List<CodeInstruction> Clone(IEnumerable<CodeInstruction> source) => source.Select(i =>
        {
            var copy = new CodeInstruction(i.opcode, i.operand); copy.labels.AddRange(i.labels); copy.blocks.AddRange(i.blocks); return copy;
        }).ToList();
        var common = nativeInstructions();
        var bazookaFirst = BazookaFiringPatch.Redirect(Clone(common), temp, type, prefab, camera, Proxy(nameof(TestProxy.Camera))).ToList();
        Apply(bazookaFirst);
        var disposableFirst = Apply(Clone(common));
        BazookaFiringPatch.Redirect(disposableFirst, temp, type, prefab, camera, Proxy(nameof(TestProxy.Camera))).ToArray();
        Require(bazookaFirst.Select((i, index) => Snapshot.From(i).Same(Snapshot.From(disposableFirst[index]))).All(v => v), "Bazooka patch ordering interference");

        var nativeBullet = native.MainModule.Types.Single(t => t.Name == "scrPlayerControl").Methods.Single(m => m.Name == "castBullet");
        var opcodeMap = typeof(EmitOpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
        var oldCamera = Proxy(nameof(TestProxy.Camera)); var oldPlayer = Proxy(nameof(TestProxy.Player)); var oldAssist = Proxy(nameof(TestProxy.Assist));
        var newAssist = Proxy(nameof(TestProxy.DisposableAssist));
        List<CodeInstruction> BulletFixture(bool acceptedAdapters = true) => nativeBullet.Body.Instructions.Select(i =>
        {
            var result = new CodeInstruction(opcodeMap[i.OpCode.Value], i.Operand);
            if (acceptedAdapters && i.OpCode.Code == Code.Ldfld && i.Operand is FieldReference field)
            {
                var replacement = field.Name switch { "mainCam" => oldCamera, "playerCamera" => oldPlayer, "hasAdjustedVector" => oldAssist, _ => null };
                if (replacement != null) { result.opcode = EmitOpCodes.Call; result.operand = replacement; }
            }
            result.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            return result;
        }).ToList();
        void PatchBullet(List<CodeInstruction> fixture) => DisposableFiringPatch.RedirectBulletAdapters(fixture,
            oldCamera, oldPlayer, oldAssist, cameraProxy, playerProxy, newAssist).ToArray();
        var bulletFixture = BulletFixture(); var bulletBefore = bulletFixture.Select(Snapshot.From).ToArray();
        PatchBullet(bulletFixture);
        var bulletChanged = 0;
        for (var index = 0; index < bulletFixture.Count; index++)
        {
            var actual = Snapshot.From(bulletFixture[index]); var previous = bulletBefore[index];
            if (Equals(previous.Operand, oldCamera) || Equals(previous.Operand, oldPlayer) || Equals(previous.Operand, oldAssist))
            {
                var expected = Equals(previous.Operand, oldCamera) ? cameraProxy : Equals(previous.Operand, oldPlayer) ? playerProxy : newAssist;
                Require(actual.Opcode == previous.Opcode && Equals(actual.Operand, expected) && actual.Blocks.SequenceEqual(previous.Blocks) && actual.Labels.SequenceEqual(previous.Labels), "Bullet adapter replacement changed metadata");
                bulletChanged++;
            }
            else Require(previous.Same(actual), "Native bullet/physics logic changed");
        }
        Require(bulletChanged == 21, "Expected16 camera/4 player/1 assist adapters");
        void RejectBullet(List<CodeInstruction> fixture)
        {
            var snapshots = fixture.Select(Snapshot.From).ToArray(); var rejected = false;
            try { PatchBullet(fixture); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && snapshots.Where((s,i) => !s.Same(Snapshot.From(fixture[i]))).Count() == 0, "Bullet ordering/shape rejection failed");
            failures++;
        }
        RejectBullet(BulletFixture(false));
        var missingAssist = BulletFixture(); missingAssist.Single(i => Equals(i.operand, oldAssist)).opcode = EmitOpCodes.Nop; RejectBullet(missingAssist);
        var extraCamera = BulletFixture(); extraCamera.Add(new CodeInstruction(EmitOpCodes.Call, oldCamera)); RejectBullet(extraCamera);

        var plugin = candidate.MainModule.Types.Single(t => t.FullName == "CulticVR.AimTrial.AimTrialPlugin");
        var aimGate = plugin.Methods.Single(m => m.Name == "UseDisposableAim");
        var bulletGate = plugin.Methods.Single(m => m.Name == "UseDisposableHitscanAim");
        var cases = 0;
        for (var flags = 0; flags < 128; flags++)
        foreach (var temporary in new[] { -1,0,1,2,3,99 })
        foreach (var state in new[] { -1,0,5,6,7,8 })
        foreach (var zoom in new[] { -1,0,1 })
        foreach (var connection in new[] { 0,1,2 })
        {
            var fixture = new GateFixture(flags, temporary, state, zoom, connection);
            var (enabled, poseCalls) = GateReplay.Evaluate(aimGate, fixture);
            var allowed = Enumerable.Range(0, 6).All(fixture.Bit) && (temporary == 1 || temporary == 2) && state == 7 && zoom == 0 && connection == 0;
            Require(enabled == (allowed && fixture.Bit(6)) && poseCalls == (allowed ? 1 : 0), "Disposable gate mismatch");
            var hitscan = GateReplay.Evaluate(bulletGate, fixture);
            Require(hitscan.Enabled == (allowed && temporary == 1 && fixture.Bit(6)) && hitscan.PoseCalls == (allowed && temporary == 1 ? 1 : 0), "MG-only bullet gate mismatch");
            cases += 2;
        }
        Console.WriteLine($"PASS: MG10/LP42 five Update loads only; {bulletChanged} accepted castBullet adapters only, native shot/physics untouched. Both bazooka/disposable patch orders identical.");
        Console.WriteLine($"PASS: {failures} malformed/ordering cases reject without partial mutation; {cases} compiled disposable/MG-only guard fixtures pass (Bazooka/BBGun excluded).");
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
