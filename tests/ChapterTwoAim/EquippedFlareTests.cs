using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class EquippedFlareTests
{
    internal static void Run(AssemblyDefinition mod,AssemblyDefinition game)
    {
        int checks=0;void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
        var owner=mod.MainModule.Types.Single(t=>t.FullName=="CulticVR.AimTrial.AimTrialPlugin");
        MethodDefinition Method(string name)=>owner.Methods.Single(m=>m.Name==name);
        foreach(int id in Enumerable.Range(-2,35))foreach(bool controller in new[]{false,true})foreach(bool thrown in new[]{false,true})foreach(bool pose in new[]{false,true}){
            Check(Evaluate(Method("UseControllerAim"),id,controller,thrown,pose)==(id>=0&&id<=17&&pose),"Primary weapon coverage and pose failure");
            Check(Evaluate(Method("UseAlternateAim"),id,controller,thrown,pose)==(new[]{1,2,3,10,11,17}.Contains(id)&&controller||new[]{0,16}.Contains(id)&&thrown),"Alternate controller/throw fallback coverage");
            Check(Evaluate(Method("TryWeaponSpawn"),id,controller,thrown,pose)==new[]{1,2,3,4,5,9,10,12,14,15,17}.Contains(id),"Muzzle gate opts in only accepted weapons");
        }
        foreach(string name in new[]{"fireEvent","altFireEvent"}){
            var native=game.MainModule.Types.Single(t=>t.Name=="scrPlayerControl").Methods.Single(m=>m.Name==name);
            Instruction branch,end;
            if(name=="fireEvent"){
                var dispatch=native.Body.Instructions.Single(i=>i.OpCode.Code==Code.Switch&&i.Operand is Instruction[] entries&&entries.Length==18);
                branch=((Instruction[])dispatch.Operand)[17];end=(Instruction)dispatch.Next.Operand;
            }else{
                var comparison=native.Body.Instructions.Single(i=>i.OpCode.Code==Code.Beq&&i.Previous.OpCode.Code==Code.Ldc_I4_S&&Convert.ToInt32(i.Previous.Operand)==17);
                branch=(Instruction)comparison.Operand;end=(Instruction)comparison.Next.Operand;
            }
            var body=native.Body.Instructions.Where(i=>i.Offset>=branch.Offset&&i.Offset<end.Offset).ToArray();
            Check(body.Count(i=>i.Operand is FieldReference f&&f.Name=="mainCam")==5,"Five equipped flare camera reads in "+name);
            Check(body.Count(i=>i.Operand is MethodReference m&&m.Name=="Instantiate")==1,"One flare spawn in "+name);
            Check(body.Count(i=>i.OpCode.Code==Code.Ldc_I4_S&&Convert.ToInt32(i.Operand)==36)==1,"Exact flare prefab36 in "+name);
            Check(body.Any(i=>i.Operand is MethodReference m&&m.Name=="set_linearVelocity"),"Native flare velocity retained");
            Check(body.Any(i=>i.Operand is FieldReference f&&f.Name=="isStarFlare")== (name=="altFireEvent"),"Alternate star flare remains separate");
        }
        // The reticle and spawn implementations are existing shared consumers;
        // the compiled audit separately verifies all of their bodies unchanged.
        Console.WriteLine($"PASS {checks} compiled equipped-flare gates and exact native primary/star-flare spawn contracts; no Unity or visual proof.");
    }
    private static bool Evaluate(MethodDefinition method,int id,bool controller,bool thrown,bool pose){
        var anchor=method.Body.Instructions.Single(i=>i.Operand is FieldReference f&&f.Name=="weaponTableID");
        var stack=new Stack<int>();stack.Push(id);var locals=new Dictionary<int,int>();var ip=anchor.Next;
        for(int step=0;step<200;step++){
            var next=ip.Next;var code=ip.OpCode.Code;
            switch(code){
                case Code.Stloc:case Code.Stloc_S:locals[((VariableDefinition)ip.Operand).Index]=stack.Pop();break;
                case Code.Ldloc:case Code.Ldloc_S:stack.Push(locals[((VariableDefinition)ip.Operand).Index]);break;
                case Code.Stloc_0:case Code.Stloc_1:case Code.Stloc_2:case Code.Stloc_3:locals[(int)code-(int)Code.Stloc_0]=stack.Pop();break;
                case Code.Ldloc_0:case Code.Ldloc_1:case Code.Ldloc_2:case Code.Ldloc_3:stack.Push(locals[(int)code-(int)Code.Ldloc_0]);break;
                case Code.Ldc_I4_0:case Code.Ldc_I4_1:case Code.Ldc_I4_2:case Code.Ldc_I4_3:case Code.Ldc_I4_4:case Code.Ldc_I4_5:case Code.Ldc_I4_6:case Code.Ldc_I4_7:case Code.Ldc_I4_8:stack.Push((int)code-(int)Code.Ldc_I4_0);break;
                case Code.Ldc_I4:case Code.Ldc_I4_S:stack.Push(Convert.ToInt32(ip.Operand));break;
                case Code.Beq:case Code.Beq_S:if(stack.Pop()==stack.Pop())next=(Instruction)ip.Operand;break;
                case Code.Bne_Un:case Code.Bne_Un_S:if(stack.Pop()!=stack.Pop())next=(Instruction)ip.Operand;break;
                case Code.Brtrue:case Code.Brtrue_S:if(stack.Pop()!=0)next=(Instruction)ip.Operand;break;
                case Code.Brfalse:case Code.Brfalse_S:if(stack.Pop()==0)next=(Instruction)ip.Operand;break;
                case Code.Br:case Code.Br_S:case Code.Leave:case Code.Leave_S:next=(Instruction)ip.Operand;break;
                case Code.Ceq:stack.Push(stack.Pop()==stack.Pop()?1:0);break;
                case Code.Or:stack.Push(stack.Pop()|stack.Pop());break;
                case Code.And:stack.Push(stack.Pop()&stack.Pop());break;
                case Code.Ldarg_0:case Code.Ldarg_1:case Code.Nop:break;
                case Code.Call:case Code.Callvirt:
                    string call=((MethodReference)ip.Operand).Name;
                    if(method.Name=="TryWeaponSpawn"&&call=="UseControllerAim")return true;
                    if(call=="UpdatePose")return pose;
                    stack.Push(call switch{"UseControllerAim"=>controller?1:0,"UseThrownWeaponAim"=>thrown?1:0,_=>throw new Exception("Unreviewed gate call "+call)});break;
                case Code.Ret:return stack.Pop()!=0;
                default:throw new Exception("Unreviewed equipped-flare gate IL "+ip);
            }ip=next;
        }throw new Exception("Gate failed to terminate");
    }
}
