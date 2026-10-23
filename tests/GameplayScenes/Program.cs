using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;

// Execute the actual packaged method IL with only Unity Scene properties
// substituted. No Unity initialization, native rendering, or copied gate logic.
const string candidate="src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll";
var gate=Compile(candidate);
var baselineFile="tools/cultmas/backup.txt";
Func<SceneFixture,bool>? baseline=File.Exists(baselineFile)?Compile(Path.Combine(File.ReadAllText(baselineFile).Trim(),"CulticVR.dll")):null;
int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var survival=new[]{"sceneSurvivalTest","sceneSurvivalMines","sceneSurvivalAsylum","sceneSurvivalCoopTest","sceneSurvivalCrypts","sceneSurvivalMall","sceneSurvivalOldTown","sceneSurvivalTrainYard","sceneSurvivalGrandewel"};
var accepted=new List<string>{"sceneEBM1","sceneTest"};
accepted.AddRange(survival);
for(int map=1;map<=10;map++)accepted.Add($"sceneE1M{map}");
for(int map=0;map<=12;map++)accepted.Add($"sceneE2M{map}");
foreach(var name in accepted){
 foreach(bool valid in new[]{false,true})foreach(bool loaded in new[]{false,true}){
  var scene=new SceneFixture(name,valid,loaded);
  Check(gate(scene)==(valid&&loaded),"Campaign/bonus still requires valid loaded Scene: "+name);
  if(baseline!=null)Check(baseline(scene)==(valid&&loaded&&name!="sceneEBM1"&&name!="sceneTest"&&!survival.Contains(name)),"Baseline proves bonus exclusion and unchanged campaign cases");
 }
}
var rejected=new[]{"sceneMainMenu","sceneLoading","sceneIntro","scenePreload","sceneEBM","sceneEBM0","sceneEBM2","sceneEBM10","sceneEBM1extra","sceneebm1","SCENEEBM1","sceneEBM1 "," sceneEBM1","sceneEBM01","sceneE1M","sceneE1","sceneEM1","sceneE1M1intro","sceneE1M-1","sceneE-1M1","sceneE１M1","sceneE1M１","", "sceneEBM1\0","scenetest","SCENETEST","sceneTest2","sceneTestExtra","sceneTest "," sceneTest","sceneTest\0"};
foreach(string name in rejected){Check(!gate(new(name,true,true)),"Non-gameplay/malformed rejected: "+name);if(baseline!=null)Check(!baseline(new(name,true,true)),"Existing rejection preserved");}
foreach(string name in survival){
 foreach(string variant in new[]{name+"Extra",name+" "," "+name,name.ToLowerInvariant(),name.ToUpperInvariant(),name+"\0"})
  Check(!gate(new(variant,true,true)),"Survival names require exact ordinal whole-name match");
}
foreach(string name in new[]{"sceneSurvival","sceneSurvivalUnknown","sceneSurvivalTest2","sceneSurvivalTestMenu"})
 Check(!gate(new(name,true,true)),"Survival prefix alone cannot enable unknown scenes");
// Numbered grammar remains deliberately unchanged, including multi-digit IDs.
for(int e=0;e<=20;e++)for(int m=0;m<=30;m++){
 string name=$"sceneE{e}M{m}";Check(gate(new(name,true,true)),"Numbered scene grammar retained");
 foreach(string variant in new[]{name+"x","x"+name,name+" ",name.ToUpperInvariant()}){
  Check(!gate(new(variant,true,true)),"Whole-name ordinal matching");
  if(baseline!=null)Check(gate(new(variant,true,true))==baseline(new(variant,true,true)),"Unrelated classification identical");
 }
}
var demoBackup="tools/demo2021/backup.txt";
if(File.Exists(demoBackup)){
 var beforeDemo=Compile(Path.Combine(File.ReadAllText(demoBackup).Trim(),"CulticVR.dll"));
 foreach(var name in accepted.Concat(rejected))foreach(bool valid in new[]{false,true})foreach(bool loaded in new[]{false,true}){
  var fixture=new SceneFixture(name,valid,loaded);
  Check(beforeDemo(fixture)==(accepted.Contains(name)&&name!="sceneTest"&&!survival.Contains(name)&&valid&&loaded),"Immediate baseline reproduces Demo exclusion with Cultmas working");
 }
}
var survivalBackup="tools/survival-stereo/backup.txt";
if(File.Exists(survivalBackup)){
 var beforeSurvival=Compile(Path.Combine(File.ReadAllText(survivalBackup).Trim(),"CulticVR.dll"));
 foreach(var name in accepted.Concat(rejected))foreach(bool valid in new[]{false,true})foreach(bool loaded in new[]{false,true}){
  var fixture=new SceneFixture(name,valid,loaded);
  Check(beforeSurvival(fixture)==(accepted.Contains(name)&&!survival.Contains(name)&&valid&&loaded),"Immediate baseline reproduces all survival exclusions with campaign/bonus working");
 }
}
Console.WriteLine($"PASS {checks} production compiled gameplay-scene cases; all nine survival scenes, Cultmas and 2021 Demo accepted; campaign/menu/loading guards preserved. No Unity/headset visual proof.");

static Func<SceneFixture,bool> Compile(string path){
 using var assembly=AssemblyDefinition.ReadAssembly(path);
 var source=assembly.MainModule.Types.Single(t=>t.FullName=="CulticVR.WorldStereoTrial.WorldStereoTrialPlugin").Methods.Single(m=>m.Name=="IsGameplayScene");
 var method=new DynamicMethod("CompiledSceneGate",typeof(bool),new[]{typeof(SceneFixture)},typeof(SceneFixture).Module,true);
 var il=method.GetILGenerator();
 foreach(var local in source.Body.Variables)il.DeclareLocal(Clr(local.VariableType));
 var labels=source.Body.Instructions.ToDictionary(i=>i,i=>il.DefineLabel());
 var ops=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
 foreach(var instruction in source.Body.Instructions){
  il.MarkLabel(labels[instruction]);var op=ops[instruction.OpCode.Value];var arg=instruction.Operand;
  switch(arg){
   case null:il.Emit(op);break;
   case Mono.Cecil.Cil.Instruction target:il.Emit(op,labels[target]);break;
   case Mono.Cecil.Cil.VariableDefinition local:il.Emit(op,(short)local.Index);break;
   case ParameterDefinition parameter:il.Emit(op,(short)parameter.Index);break;
   case string value:il.Emit(op,value);break;
   case int value:il.Emit(op,value);break;
   case sbyte value:il.Emit(op,value);break;
   case MethodReference call:
    Type owner=call.DeclaringType.FullName=="UnityEngine.SceneManagement.Scene"?typeof(SceneFixture):Clr(call.DeclaringType);
    var resolved=owner.GetMethod(call.Name,BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public,null,call.Parameters.Select(p=>Clr(p.ParameterType)).ToArray(),null)??throw new Exception("Unsupported call "+call);
    il.Emit(op,resolved);break;
   default:throw new Exception("Unsupported IL operand "+arg);
  }
 }
 return method.CreateDelegate<Func<SceneFixture,bool>>();
}
static Type Clr(TypeReference type)=>Type.GetType(type.FullName,throwOnError:true)!;
public readonly struct SceneFixture{
 private readonly bool _valid;public bool isLoaded{get;}public string name{get;}
 public SceneFixture(string name,bool valid,bool loaded){this.name=name;_valid=valid;isLoaded=loaded;}
 public bool IsValid()=>_valid;
}
