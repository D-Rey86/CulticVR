using System.Reflection;
using System.Reflection.Emit;
using CulticVR.OpenXRControllers;
using HarmonyLib;
internal static class NativeWorldTests
{
    public static void Callback(UnityEngine.GameObject value){}
    public static void PlayerCallback(scrPlayerControl value){}
    public static void WhizCallback(scrBullet bullet,UnityEngine.Collider nearby,UnityEngine.Vector3 travel){}
    public static void ContactCallback(scrBullet bullet,UnityEngine.RaycastHit hit){}
    public static void ExpiryCallback(scrBullet bullet){}
    public static void MovedCallback(scrBullet bullet){}
    internal static void Run()
    {
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        using var native=Mono.Cecil.AssemblyDefinition.ReadAssembly("G:/Games/Steam/steamapps/common/CULTIC/CULTIC_Data/Managed/Assembly-CSharp.dll");
        var module=typeof(scrPlayerControl).Module;
        var cutscene=native.MainModule.Types.Single(t=>t.Name=="scrCutsceneTrigger");
        foreach(string name in new[]{"stopCutscene","endCutsceneInstant"}){
            var instructions=cutscene.Methods.Single(m=>m.Name==name).Body.Instructions;
            Check(instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is Mono.Cecil.FieldReference f&&f.Name=="gameState"&&i.Previous.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_0),name+" returns native game state to gameplay");
            Check(!instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is Mono.Cecil.FieldReference f&&f.Name=="cutsceneState"),name+" does not reset HUD overlay state");
            Check(instructions.Any(i=>i.Operand is Mono.Cecil.MethodReference m&&m.Name=="showHUD"),name+" restores native HUD");
        }
        Check(!native.MainModule.Types.Single(t=>t.Name=="scrPlayerHUD").Methods.Single(m=>m.Name=="showHUD").Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is Mono.Cecil.FieldReference f&&f.Name=="cutsceneState"),"showHUD also leaves overlay state unchanged");
        var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
        var bulletUpdate=typeof(scrBullet).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var bulletNative=native.MainModule.Types.Single(t=>t.Name=="scrBullet").Methods.Single(m=>m.Name=="Update");
        var bulletLocals=bulletUpdate.GetMethodBody()!.LocalVariables;
        var bulletInstructions=bulletNative.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand switch{
            Mono.Cecil.MethodReference mr=>module.ResolveMethod(mr.MetadataToken.ToInt32()),
            Mono.Cecil.FieldReference fr=>module.ResolveField(fr.MetadataToken.ToInt32()),
            Mono.Cecil.Cil.VariableDefinition v=>bulletLocals[v.Index],_=>i.Operand})).ToList();
        var whiz=typeof(NativeWorldTests).GetMethod(nameof(WhizCallback))!;var contact=typeof(NativeWorldTests).GetMethod(nameof(ContactCallback))!;var expiry=typeof(NativeWorldTests).GetMethod(nameof(ExpiryCallback))!;
        var moved=typeof(NativeWorldTests).GetMethod(nameof(MovedCallback))!;
        var bulletObserved=HelicopterNearMissPatch.Rewrite(bulletInstructions,bulletUpdate,whiz,contact,expiry,moved).ToList();
        Check(bulletObserved.Count==bulletInstructions.Count+17,"One native whiz, one movement and three terminal observers only");
        Check(bulletObserved.Count(i=>Equals(i.operand,whiz))==1&&bulletObserved.Count(i=>Equals(i.operand,contact))==2&&bulletObserved.Count(i=>Equals(i.operand,expiry))==1,"Exact whiz/world-contact/damage-contact/expiry seams");
        int bulletCursor=0;foreach(var i in bulletInstructions){while(!ReferenceEquals(bulletObserved[bulletCursor],i))bulletCursor++;Check(ReferenceEquals(bulletObserved[bulletCursor++],i),"Every original bullet instruction/operand preserved in order");}
        Check(bulletObserved.Count(i=>i.operand is MethodInfo m&&m.DeclaringType==typeof(UnityEngine.Physics))==bulletInstructions.Count(i=>i.operand is MethodInfo m&&m.DeclaringType==typeof(UnityEngine.Physics)),"No additional bullet physics calls");
        foreach(var invalid in new[]{bulletInstructions.Where(i=>!Equals(i.operand,typeof(scrBullet).GetField("whizPrefab"))).ToList(),bulletInstructions.Concat(bulletInstructions).ToList()}){
            try{HelicopterNearMissPatch.Rewrite(invalid,bulletUpdate,whiz,contact,expiry,moved).ToList();throw new Exception("Malformed near-miss seam accepted");}catch(InvalidOperationException){checks++;}
        }
        var callback=typeof(NativeWorldTests).GetMethod(nameof(Callback))!;
        var targets=new[]{("scrE1M5Plunger","Update",2),("scrGrenadeShell","detonate",1),("scrImpactProjectile","detonate",1),("scrTNT","detonate",1),("scrTNT","takeDamage",1),("scrMolomite","detonate",3),("scrMolotov","detonate",4),("scrNailJar","detonate",5),("scrNailJar","takeDamage",1),("scrTank","Update",4),("scrArchonBloodBall","OnTriggerStay",1),("scrAbomination","topArmSlam",1),("scrFlare","Update",2),("scrPickup","OnTriggerEnter",1),("scrTNT","OnTriggerEnter",1),("scrMolotov","OnTriggerEnter",1),("scrMolomite","OnTriggerEnter",1),("scrTNTTripwire","OnTriggerEnter",1),("scrPlayerControl","Update",1)};
        foreach(var (type,name,count) in targets){
            var method=native.MainModule.Types.Single(t=>t.Name==type).Methods.Single(m=>m.Name==name);
            var original=method.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand is Mono.Cecil.MethodReference mr?module.ResolveMethod(mr.MetadataToken.ToInt32()):i.Operand)).ToList();
            bool temporary=type=="scrPlayerControl";bool pickup=name=="OnTriggerEnter"||temporary;
            var sound=temporary?typeof(scrPlayerControl).GetMethod("equipTempWeapon")!:typeof(scrGameControl).GetMethod("playSystemSound")!;
            List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input)=>(pickup?HapticEventSelectors.AfterCall(input,sound,callback,temporary?0:1,count):HapticEventSelectors.CaptureSpawns(input,callback,count)).ToList();
            if(type=="scrE1M5Plunger"){var actual=WorldRumbleHooks.RewritePlunger(original,typeof(scrE1M5Plunger)).ToList();Check(actual.Count==original.Count+4,"Both actual plunger spawn callbacks");Check(actual.Count(i=>i.operand is MethodInfo m&&m.Name=="PlungerSpawned")==2,"Actual plunger callbacks selected");}
            var result=Rewrite(original);Check(result.Count==original.Count+2*count,type+" injected count");
            int cursor=0;
            foreach(var instruction in original){Check(ReferenceEquals(instruction,result[cursor++]),type+" original unchanged");
                if(cursor<result.Count&&!original.Contains(result[cursor])){Check(result[cursor++].opcode==(pickup?(temporary?OpCodes.Ldarg_0:OpCodes.Ldarg_1):OpCodes.Dup),"Stack preserved");Check(Equals(result[cursor++].operand,callback),"Callback target");}}
            try{Rewrite(Array.Empty<CodeInstruction>());throw new Exception("Missing native seam accepted");}catch(InvalidOperationException){checks++;}
        }
        var player=native.MainModule.Types.Single(t=>t.Name=="scrPlayerControl");
        Check(player.Fields.Single(f=>f.Name=="fireCharge").FieldType.FullName=="System.Single","Native charge injection type");
        Check(native.MainModule.Types.Single(t=>t.Name=="scrExplosion").Fields.Single(f=>f.Name=="hasExploded").FieldType.FullName=="System.Boolean","Native explosion injection type");
        var cinematic=native.MainModule.Types.Single(t=>t.Name=="scrE2M6Cutscene");
        var window=cinematic.Methods.Single(m=>m.Name=="breakWindow");
        Check(window.IsPublic&&!window.IsStatic&&window.Parameters.Count==0&&window.ReturnType.FullName=="System.Void","Exact window timeline callback signature");
        Check(cinematic.Fields.Single(f=>f.Name=="window").FieldType.Name=="scrDestructible","Native window reference type");
        Check(window.Body.Instructions.Count(i=>i.Operand is Mono.Cecil.MethodReference m&&m.DeclaringType.Name=="scrDestructible"&&m.Name=="takeDamage")==1,"Window signal performs one native destructive hit");
        Check(window.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4&&Equals(i.Operand,999999)),"Exact native window-break damage");
        var destructible=native.MainModule.Types.Single(t=>t.Name=="scrDestructible");
        Check(destructible.Fields.Single(f=>f.Name=="hp").IsPublic&&destructible.Fields.Single(f=>f.Name=="hp").FieldType.FullName=="System.Int32","Public native HP transition can suppress duplicate/rejected breaks");
        var trigger=native.MainModule.Types.Single(t=>t.Name=="scrCutsceneTrigger");
        foreach(string name in new[]{"skipCutscene","loadPostCutscene"}){
            var instructions=trigger.Methods.Single(m=>m.Name==name).Body.Instructions;
            int state=instructions.ToList().FindIndex(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is Mono.Cecil.FieldReference f&&f.Name=="state");
            int play=instructions.ToList().FindIndex(i=>i.Operand is Mono.Cecil.MethodReference m&&m.Name=="Play"&&m.DeclaringType.Name=="PlayableDirector");
            Check(state>0&&instructions[state-1].OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_2&&play>state,"Skip/load marks trigger expired before advancing the timeline");
        }
        var coroutine=cinematic.NestedTypes.Single(t=>t.Name.StartsWith("<shotCoroutine>"));
        var moveNext=coroutine.Methods.Single(m=>m.Name=="MoveNext");
        var shots=moveNext.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand is Mono.Cecil.MethodReference mr?module.ResolveMethod(mr.MetadataToken.ToInt32()):i.Operand)).ToList();
        var captured=HapticEventSelectors.CaptureSpawns(shots,callback,1).ToList();
        Check(captured.Count==shots.Count+2,"One cinematic spawn observer per native round");
        int shotCursor=0;foreach(var i in shots){Check(ReferenceEquals(i,captured[shotCursor++]),"Original cinematic coroutine instructions retained");if(shotCursor<captured.Count&&captured[shotCursor].opcode==OpCodes.Dup&&Equals(captured[shotCursor+1].operand,callback))shotCursor+=2;}
        var update=player.Methods.Single(m=>m.Name=="Update");
        var pickupInstructions=update.Body.Instructions.Select(i=>new CodeInstruction(opcodes[i.OpCode.Value],i.Operand switch{
            Mono.Cecil.MethodReference mr=>module.ResolveMethod(mr.MetadataToken.ToInt32()),
            Mono.Cecil.FieldReference fr=>module.ResolveField(fr.MetadataToken.ToInt32()),_=>i.Operand})).ToList();
        var pickupCallback=typeof(NativeWorldTests).GetMethod(nameof(PlayerCallback))!;
        var offhands=HapticEventSelectors.OffhandPickups(pickupInstructions,pickupCallback).ToList();
        Check(offhands.Count==pickupInstructions.Count+4,"Exactly shield and moth success callbacks");
        int pickupCursor=0;foreach(var i in pickupInstructions){Check(ReferenceEquals(i,offhands[pickupCursor++]),"All native player Update instructions retained by offhand selector");if(pickupCursor<offhands.Count&&offhands[pickupCursor].opcode==OpCodes.Ldarg_0&&Equals(offhands[pickupCursor+1].operand,pickupCallback))pickupCursor+=2;}
        foreach(var input in new[]{Array.Empty<CodeInstruction>(),pickupInstructions.Concat(pickupInstructions).ToArray()}){
            try{HapticEventSelectors.OffhandPickups(input,pickupCallback).ToList();throw new Exception("Invalid offhand seams accepted");}catch(InvalidOperationException){checks++;}
        }
        using(var mod=Mono.Cecil.AssemblyDefinition.ReadAssembly("src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll")){
            var preview=mod.MainModule.Types.Single(t=>t.FullName=="CulticVR.AimPreview.AimPreviewPlugin");
            var wrist=preview.Methods.Single(m=>m.Name=="UpdateWristQuickMenu").Body.Instructions.ToList();
            int create=wrist.FindIndex(i=>i.Operand is Mono.Cecil.MethodReference mr&&mr.Name=="CreateWristQuickMenu");
            int gaze=wrist.FindIndex(i=>i.Operand is Mono.Cecil.MethodReference mr&&mr.Name=="LookingAt");
            Check(create>=0&&gaze>create,"Reloaded HUD host initializes before palm gaze can return");
            var createBody=preview.Methods.Single(m=>m.Name=="CreateWristQuickMenu").Body.Instructions.ToList();
            Check(createBody.Any(i=>i.Operand is Mono.Cecil.MethodReference mr&&mr.Name=="SetActive"&&i.Previous.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_I4_0),"New wrist host starts hidden");
            var frame=preview.Methods.Single(m=>m.Name=="LateUpdate").Body.Instructions.ToList();
            int menu=frame.FindIndex(i=>i.Operand is Mono.Cecil.MethodReference mr&&mr.Name=="UpdateWristQuickMenu");
            int status=frame.FindIndex(i=>i.Operand is Mono.Cecil.MethodReference mr&&mr.Name=="UpdateWristStatusHud");
            Check(menu>=0&&status>menu&&!frame.Skip(menu+1).Take(status-menu-1).Any(i=>i.OpCode.FlowControl==Mono.Cecil.Cil.FlowControl.Return),"Status strip attaches after hidden host initialization in same frame");
        }
        Console.WriteLine($"PASS {checks} native world/contact selector checks, original IL preserved.");
    }
}

