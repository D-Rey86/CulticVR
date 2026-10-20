using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CulticVR.WorldStereoTrial;
using EOp=System.Reflection.Emit.OpCodes;

const string root="G:/Games/Steam/steamapps/common/CULTIC";
string managed=root+"/CULTIC_Data/Managed";
AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{
    string path=Path.Combine(managed,new AssemblyName(e.Name).Name+".dll");
    return File.Exists(path)?Assembly.LoadFrom(path):null;
};
var native=Assembly.LoadFrom(managed+"/Assembly-CSharp.dll");
var player=native.GetType("scrPlayerControl")!;
var fields=new[]{"playerCamera","playerCameraParent","grabCamOrigin","playerFlashlightObject"}
    .ToDictionary(n=>n,n=>player.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!);
var core=Assembly.LoadFrom(managed+"/UnityEngine.CoreModule.dll");
var cameraProxy=typeof(Proxies).GetMethod(nameof(Proxies.Camera))!;
var positionProxy=typeof(Proxies).GetMethod(nameof(Proxies.Position))!;
var opcodes=typeof(EOp).GetFields(BindingFlags.Public|BindingFlags.Static)
    .Where(f=>f.FieldType==typeof(System.Reflection.Emit.OpCode)).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
using var assembly=AssemblyDefinition.ReadAssembly(managed+"/Assembly-CSharp.dll");
var nativePlayer=assembly.MainModule.Types.Single(t=>t.Name=="scrPlayerControl");
int unchanged=0;
List<CodeInstruction> Decode(string name){
    var body=nativePlayer.Methods.Single(m=>m.Name==name).Body;
    var generator=new DynamicMethod("HeadViewFixture",typeof(void),Type.EmptyTypes).GetILGenerator();
    var labels=body.Instructions.SelectMany(i=>i.Operand is Instruction[] many?many:i.Operand is Instruction one?new[]{one}:Array.Empty<Instruction>())
        .Distinct().ToDictionary(i=>i,_=>generator.DefineLabel());
    return body.Instructions.Select(i=>{
        object? operand=i.Operand;
        if(operand is FieldReference f && f.DeclaringType.Name=="scrPlayerControl" && fields.TryGetValue(f.Name,out var field)) operand=field;
        if(operand is MethodReference m && (m.Name is "get_transform" or "get_position" or "set_position" or "set_rotation") &&
            core.GetType(m.DeclaringType.FullName)?.GetMethod(m.Name) is MethodInfo getter)
            operand=getter;
        if(operand is Instruction[] many) operand=many.Select(t=>labels[t]).ToArray();
        else if(operand is Instruction one) operand=labels[one];
        var code=new CodeInstruction(opcodes[i.OpCode.Value],operand);
        if(labels.TryGetValue(i,out var label)) code.labels.Add(label);
        return code;
    }).ToList();
}
CodeInstruction[] Rewrite(List<CodeInstruction> code,string name)=>HeadViewReadPatch.Rewrite(code,name,
    fields["playerCamera"],fields["playerCameraParent"],fields["grabCamOrigin"],fields["playerFlashlightObject"],cameraProxy,positionProxy).ToArray();
string Snapshot(CodeInstruction c)=>$"{c.opcode.Value}:{c.operand}:{string.Join(',',c.labels.Select(l=>l.GetHashCode()))}:{string.Join(',',c.blocks.Select(b=>b.blockType))}";
void Require(bool ok,string message){if(!ok)throw new Exception(message);}
foreach(var name in new[]{"Update","grabObject","throwObject"}){
    var input=Decode(name);
    var before=input.Select(Snapshot).ToArray();
    var output=Rewrite(input,name);
    Require(output.Length==input.Count,"Instruction count changed");
    int view=0,position=0,nops=0;
    for(int i=0;i<output.Length;i++){
        Require(before[i]==Snapshot(input[i]),"Original instructions mutated");
        Require(input[i].labels.SequenceEqual(output[i].labels)&&input[i].blocks.SequenceEqual(output[i].blocks),"Metadata lost");
        if(output[i].opcode==EOp.Call && Equals(output[i].operand,cameraProxy)){
            Require(input[i].opcode==EOp.Ldfld&&Equals(input[i].operand,fields["playerCamera"]),"Non-camera read changed");view++;
        }else if(output[i].opcode==EOp.Call && Equals(output[i].operand,positionProxy)){
            Require(input[i].opcode==EOp.Ldfld&&Equals(input[i].operand,fields["playerCameraParent"]),"Wrong carry anchor");position++;
        }else if(output[i].opcode==EOp.Nop && input[i].opcode!=EOp.Nop){
            Require(position==1 && i>0 && (Equals(output[i-1].operand,positionProxy)||output[i-1].opcode==EOp.Nop),"Unrelated NOP");nops++;
        }else {Require(before[i]==Snapshot(output[i]),$"Unrelated instruction changed in {name} at {i}");unchanged++;}
    }
    Require(view==(name=="Update"?7:1)&&position==(name=="Update"?1:0)&&nops==(name=="Update"?2:0),"Unexpected patch scope");
    // Other Update transpilers may already have replaced every unrelated camera
    // read. The carry/flashlight selection must survive without ordinal coupling.
    var composed=input.Select(c=>new CodeInstruction(c)).ToList();
    for(int i=0;i<output.Length;i++)
        if(Equals(composed[i].operand,fields["playerCamera"])&&!Equals(output[i].operand,cameraProxy)){
            composed[i].opcode=EOp.Call;composed[i].operand=cameraProxy;
        }
    Rewrite(composed,name);
}
foreach(string failure in new[]{"missing-origin","duplicate-origin","missing-camera","missing-parent-getter","branch-into-parent-getter","missing-light"}){
    var input=Decode("Update");
    int Find(string field)=>input.FindIndex(c=>Equals(c.operand,fields[field]));
    int anchor=input.FindLastIndex(Find("grabCamOrigin"),c=>Equals(c.operand,fields["playerCameraParent"]));
    switch(failure){
        case "missing-origin":input[Find("grabCamOrigin")].operand=null;break;
        case "duplicate-origin":input.Add(new CodeInstruction(EOp.Ldfld,fields["grabCamOrigin"]));break;
        case "missing-camera":input[anchor+4].opcode=EOp.Nop;input[anchor+4].operand=null;break;
        case "missing-parent-getter":input[anchor+1].operand=null;break;
        case "branch-into-parent-getter":input[anchor+1].labels.Add(new DynamicMethod("label",typeof(void),Type.EmptyTypes).GetILGenerator().DefineLabel());break;
        case "missing-light":input[Find("playerFlashlightObject")].operand=null;break;
    }
    var before=input.Select(Snapshot).ToArray(); bool rejected=false;
    try{Rewrite(input,"Update");}catch(InvalidOperationException){rejected=true;}
    Require(rejected,$"Changed native layout accepted: {failure}");
    Require(before.SequenceEqual(input.Select(Snapshot)),"Rejected patch changed input");
}
Console.WriteLine($"PASS: native head-view selection changes 7 Update camera reads, 1 carry anchor and 2 event reads; {unchanged} unrelated instructions preserved, metadata retained, other camera patches coexist, 6 layout changes rejected atomically.");
CompiledGateTests.Run();
public static class Proxies { public static object Camera(object player)=>player; public static object Position(object player)=>player; }
