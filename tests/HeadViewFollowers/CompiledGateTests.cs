using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CompiledGateTests
{
    internal static void Run()
    {
        using var mod=AssemblyDefinition.ReadAssembly("src/CulticVR/bin/Release/netstandard2.1/CulticVR.dll");
        var gate=mod.MainModule.Types.Single(t=>t.FullName=="CulticVR.WorldStereoTrial.WorldStereoTrialPlugin")
            .Methods.Single(m=>m.Name=="CanFollowHead");
        int cases=0;
        foreach(string failure in new[]{"none","plugin-disabled","patch-missing","proxy-missing","not-ready","other-player","source-missing",
            "other-camera","stereo-missing","stereo-disabled","not-stereo","game-missing","network-client","network-host","remote-player","dead","hud-missing","cutscene"})
        {
            var camera=Node();
            var stereo=Node(("isActiveAndEnabled",true),("stereoEnabled",true));
            var hud=Node(("cutsceneState",0));
            var player=Node(("playerCameraComponent",camera),("playerID",0),("isDead",false),("playerHUD",hud));
            var self=Node(("isActiveAndEnabled",true),("_headViewHarmony",new object()),("_headViewObject",new object()),
                ("_reportedReady",true),("_player",player),("_source",camera),("_stereo",stereo));
            Dictionary<string,object?>? game=Node(("connectionStatus",0),("localPlayerID",0));
            switch(failure)
            {
                case "plugin-disabled":self["isActiveAndEnabled"]=false;break;
                case "patch-missing":self["_headViewHarmony"]=null;break;
                case "proxy-missing":self["_headViewObject"]=null;break;
                case "not-ready":self["_reportedReady"]=false;break;
                case "other-player":self["_player"]=Node();break;
                case "source-missing":self["_source"]=null;break;
                case "other-camera":player["playerCameraComponent"]=Node();break;
                case "stereo-missing":self["_stereo"]=null;break;
                case "stereo-disabled":stereo["isActiveAndEnabled"]=false;break;
                case "not-stereo":stereo["stereoEnabled"]=false;break;
                case "game-missing":game=null;break;
                case "network-client":game!["connectionStatus"]=1;break;
                case "network-host":game!["connectionStatus"]=2;break;
                case "remote-player":player["playerID"]=1;break;
                case "dead":player["isDead"]=true;break;
                case "hud-missing":player["playerHUD"]=null;break;
                case "cutscene":hud["cutsceneState"]=3;break;
            }
            if(Execute(gate,self,player,game)!=(failure=="none"))throw new Exception("Compiled head-view gate: "+failure);
            cases++;
        }
        Console.WriteLine($"PASS: {cases} production compiled head-view guard cases; flat/inactive, scene/player/camera replacement, remote/network, death and cinematic fallbacks.");
    }
    private static Dictionary<string,object?> Node(params (string key,object? value)[] fields)=>fields.ToDictionary(f=>f.key,f=>f.value);
    private static bool Truth(object? x)=>x switch{null=>false,bool b=>b,int i=>i!=0,_=>true};
    private static bool Equal(object? x,object? y)=>x is int || x is bool?Truth(x)==Truth(y)&&Convert.ToInt32(x)==Convert.ToInt32(y):ReferenceEquals(x,y);
    private static bool Execute(MethodDefinition gate,object self,object player,object? game)
    {
        var stack=new Stack<object?>();object? local=null;
        var ip=gate.Body.Instructions[0];
        for(int step=0;step<200;step++)
        {
            var next=ip.Next;
            switch(ip.OpCode.Code)
            {
                case Code.Nop:break;
                case Code.Ldarg_0:stack.Push(self);break;
                case Code.Ldarg_1:stack.Push(player);break;
                case Code.Ldnull:stack.Push(null);break;
                case Code.Ldloc_0:stack.Push(local);break;
                case Code.Stloc_0:local=stack.Pop();break;
                case Code.Ldfld:stack.Push(((Dictionary<string,object?>)stack.Pop()!)[((FieldReference)ip.Operand).Name]);break;
                case Code.Ldc_I4_0:stack.Push(0);break;
                case Code.Ldc_I4_1:stack.Push(1);break;
                case Code.Brfalse:case Code.Brfalse_S:if(!Truth(stack.Pop()))next=(Instruction)ip.Operand;break;
                case Code.Brtrue:case Code.Brtrue_S:if(Truth(stack.Pop()))next=(Instruction)ip.Operand;break;
                case Code.Bne_Un:case Code.Bne_Un_S:{var y=stack.Pop();if(!Equal(stack.Pop(),y))next=(Instruction)ip.Operand;break;}
                case Code.Ceq:{var y=stack.Pop();stack.Push(Equal(stack.Pop(),y));break;}
                case Code.Call:case Code.Callvirt:
                {
                    var m=(MethodReference)ip.Operand;
                    if(m.Name=="get_Instance")stack.Push(game);
                    else if(m.Name.StartsWith("get_"))stack.Push(((Dictionary<string,object?>)stack.Pop()!)[m.Name[4..]]);
                    else if(m.Name is "op_Equality" or "op_Inequality"){
                        var y=stack.Pop();bool same=Equal(stack.Pop(),y);stack.Push(m.Name=="op_Equality"?same:!same);
                    }else throw new Exception("Unexpected compiled gate call "+m.FullName);
                    break;
                }
                case Code.Ret:return Truth(stack.Pop());
                default:throw new Exception("Unexpected compiled gate opcode "+ip.OpCode);
            }
            ip=next;
        }
        throw new Exception("Compiled gate did not return");
    }
}
