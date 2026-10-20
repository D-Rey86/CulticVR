using System.Reflection;
using System.Reflection.Emit;
using CulticVR.OpenXRControllers;
using CulticVR.AimTrial;
using HarmonyLib;

internal static class TemporaryHapticTests
{
    internal static void Run(Func<List<CodeInstruction>> native,FieldInfo temp,FieldInfo type,FieldInfo hud,
        FieldInfo ammo,FieldInfo icon,MethodInfo refresh,FieldInfo prefab,FieldInfo camera,FieldInfo playerCamera,
        FieldInfo parent,MethodInfo bullet)
    {
        int checks=0;
        void Check(bool c,string message){checks++;if(!c)throw new Exception(message);}
        MethodInfo Proxy(string name)=>typeof(TestProxy).GetMethod(name)!;
        var completed=Proxy(nameof(TestProxy.TemporaryShot));
        List<CodeInstruction> Patch(IEnumerable<CodeInstruction> source)=>TemporaryHapticPatch.Insert(source,temp,type,hud,ammo,icon,refresh,completed).ToList();
        var original=native();var before=original.Select(Snapshot.From).ToArray();var patched=Patch(original);
        int call=patched.FindIndex(i=>Equals(i.operand,completed));
        Check(call>0&&patched.Count==original.Count+2&&patched[call-1].opcode==OpCodes.Ldarg_0&&Equals(patched[call-2].operand,refresh),"One completed-shot callback after native ammo refresh");
        var retained=patched.Where((_,i)=>i!=call&&i!=call-1).ToArray();
        for(int i=0;i<original.Count;i++)Check(ReferenceEquals(original[i],retained[i])&&before[i].Same(Snapshot.From(retained[i])),"Original Update instruction/metadata changed");
        int failures=0;
        void Reject(Action<List<CodeInstruction>> change){
            var broken=native();change(broken);var saved=broken.Select(Snapshot.From).ToArray();bool failed=false;
            try{Patch(broken);}catch(InvalidOperationException){failed=true;}
            Check(failed&&saved.Select((s,i)=>s.Same(Snapshot.From(broken[i]))).All(x=>x),"Invalid seam must reject without mutation");failures++;
        }
        int end=call-11;
        Reject(x=>x[end+2].opcode=OpCodes.Ldc_I4_0);
        Reject(x=>x[end+5].operand=icon);
        Reject(x=>x[end+8].operand=ammo);
        Reject(x=>x[end+9].operand=completed);
        Reject(x=>x[end].labels.Clear());
        Reject(x=>x.Insert(1,new CodeInstruction(OpCodes.Ldfld,type)));
        Snapshot[]? reference=null;
        // Each decode owns a fresh switch operand array. Compare its labels by
        // value across decodes, while preserving identity within each patch.
        bool Equivalent(Snapshot a,Snapshot b)=>a.Opcode==b.Opcode&&
            (a.Operand is Label[] la&&b.Operand is Label[] lb?la.SequenceEqual(lb):Equals(a.Operand,b.Operand))&&
            a.Labels.SequenceEqual(b.Labels)&&a.Blocks.Select(x=>(x.blockType,x.catchType)).SequenceEqual(b.Blocks.Select(x=>(x.blockType,x.catchType)));
        foreach(var order in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}}){
            var code=native();foreach(int step in order)code=step switch{
                0=>Patch(code),
                1=>BazookaFiringPatch.Redirect(code,temp,type,prefab,camera,Proxy(nameof(TestProxy.Camera))).ToList(),
                _=>DisposableFiringPatch.Redirect(code,temp,type,prefab,camera,playerCamera,parent,bullet,
                    Proxy(nameof(TestProxy.DisposableCamera)),Proxy(nameof(TestProxy.DisposablePlayer)),Proxy(nameof(TestProxy.DisposableParent))).ToList()
            };
            var snapshot=code.Select(Snapshot.From).ToArray();
            if(reference==null)reference=snapshot;
            else Check(snapshot.Length==reference.Length&&snapshot.Select((s,i)=>Equivalent(s,reference[i])).All(x=>x),"Haptic/aim patch order changes native behavior");
        }
        Console.WriteLine($"PASS {checks} disposable haptic shipping-IL checks: one completed-shot insertion, every original instruction preserved, {failures} damaged contracts rejected, all six haptic/rocket/disposable aim patch orders identical. Static only.");
    }
}
