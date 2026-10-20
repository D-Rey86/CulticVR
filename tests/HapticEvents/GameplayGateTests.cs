using CulticVR.OpenXRControllers;
using UnityEngine;
using UnityEngine.XR;

internal static class GameplayGateTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        // Runtime trace: stopCutscene hands gameplay back with game=0/HUD=3.
        // Replay every suppressed route, then pause/fade/focus/death guards.
        var data=g.gamePlayers[0];g.hudScript.playerScript=p;g.gameState=0;g.hudScript.cutsceneState=3;
        data.playerHP=100;p.weapon=0;owner.Tick();owner.Clear();
        check(owner.VestOutput.Enabled&&owner.Output.Allowed,"Post-intro gameplay keeps both transports ready with HUD overlay state 3");
        check(HapticProducerTags.Enabled&&ExplosionHapticCapture.Enabled,"Post-intro gameplay publishes incoming metadata and captures explosions");
        for(int i=0;i<3;i++){
            var bullet=new scrHitInfo{hitFrom=new(0,0,-1)};
            HapticProducerTags.Tag(bullet,new HapticAttackGroup((ulong)(800+i),IncomingHapticKind.HelicopterAutomatic));
            owner.Damage(p,bullet,()=>data.playerHP-=2);
        }
        check(owner.VestOutput.Frames.Count==3&&owner.Output.Pulses.Count==6,"Actual post-intro helicopter damage reaches vest and both hands");
        check(owner.VestOutput.Frames.All(f=>f.Front!=0&&f.Back==0)&&owner.VestOutput.Frames.Select(f=>f.Front).Distinct().Count()==3,"Helicopter hits retain directional successive spots");
        check(owner.VestOutput.Frames.All(f=>f.Amplitude==.9f&&f.Seconds==.14f),"Helicopter strengthens vest hits and retains physically verified 140ms duration");
        check(owner.Output.Pulses.All(f=>f.Amplitude==.42f&&f.Seconds==.15f),"Helicopter controller output unchanged by vest duration correction");
        owner.Clear();
        var rear=new scrHitInfo{hitFrom=new(0,0,1)};HapticProducerTags.Tag(rear,new HapticAttackGroup(900,IncomingHapticKind.HelicopterAutomatic));
        owner.Damage(p,rear,()=>data.playerHP--);
        check(owner.VestOutput.Frames.Single().Back!=0&&owner.VestOutput.Frames[0].Front==0&&owner.VestOutput.Frames[0].Seconds==.14f,"Rear helicopter hit uses same measured duration on back panel");
        owner.Clear();owner.Damage(p,rear,()=>{});
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Rejected helicopter hit stays silent");
        owner.Clear();owner.Blast(new(0,0,1000));
        check(owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.Output.Pulses.Count==2,"Post-intro non-damaging rocket blast retains unlimited-range full panel feedback");
        owner.Clear();owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Explosive},()=>data.playerHP-=8);
        check(owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.VestOutput.Frames[0].Back==VestMotorFrame.All,"Post-intro damaging rocket retains all-40-motor effect");
        owner.Clear();owner.Pickup(new Collider{gameObject=p.gameObject});
        check(owner.VestOutput.Frames.Single().Amplitude==.4f&&owner.Output.Pulses.Count==0,"Standard post-intro pickups reach vest only at existing strength");
        owner.Clear();owner.Fire(p);
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==1,"Post-intro recoil remains enabled");
        foreach(int state in new[]{1,2,4}){
            owner.Clear();g.hudScript.cutsceneState=state;owner.Tick();owner.Pickup(new Collider{gameObject=p.gameObject});owner.Blast(new(0,0,2));owner.Fire(p);
            check(!owner.VestOutput.Enabled&&!HapticProducerTags.Enabled&&!ExplosionHapticCapture.Enabled&&owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"HUD entry/exit fade remains silent: "+state);
        }
        g.hudScript.cutsceneState=3;
        foreach(Action block in new Action[]{()=>g.gameState=1,()=>g.gameState=3,()=>Application.isFocused=false,()=>XRSettings.isDeviceActive=false,()=>OpenXRControllersPlugin.HapticSessionState(4),()=>p.isDead=true,()=>g.connectionStatus=scrGameControl.ConnectionStatus.Online}){
            block();owner.Clear();owner.Tick();owner.Fire(p);owner.Blast(new(0,0,2));owner.Pickup(new Collider{gameObject=p.gameObject});
            owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Explosive},()=>data.playerHP--);
            check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Post-intro eligibility preserves pause/cinematic/focus/death/local guards");
            g.gameState=0;Application.isFocused=true;XRSettings.isDeviceActive=true;OpenXRControllersPlugin.HapticSessionState(5);p.isDead=false;g.connectionStatus=scrGameControl.ConnectionStatus.Offline;owner.Tick();
        }
        for(int i=0;i<1000;i++)owner.Tick();long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)owner.Tick();
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Post-intro gameplay gate adds no per-frame allocation");
        g.hudScript.cutsceneState=0;data.playerHP=100;owner.Tick();owner.Clear();
    }
}
