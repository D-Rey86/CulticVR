using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.XR;
using System.Reflection;
internal static class ExpansionTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        owner.SceneChanged();g.gameState=0;g.hudScript.cutsceneState=0;g.hudScript.playerScript=p;g.gamePlayers[0].playerHP=100;owner.Tick();owner.Clear();
        Time.realtimeSinceStartupAsDouble+=100;p.transform.position=new(0,2,0);
        owner.Land(p,3);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Small ground step below hard-landing threshold stays silent");
        owner.Land(p,10);owner.Land(p,8);owner.Tick();
        var landing=owner.VestOutput.Frames.Single();
        check(landing.Front==(0xffu<<12)&&landing.Back==landing.Front&&landing.Amplitude==.85f,"Hard landing is one lower-body thump; strongest native drop retained");
        check(owner.Output.Pulses.Count==2&&owner.Output.Pulses.All(x=>x.Amplitude==.85f),"Hard landing reaches both controllers");
        owner.Clear();owner.Tick();check(owner.VestOutput.Frames.Count==0,"No synthetic continuing landing pulses");
        Time.realtimeSinceStartupAsDouble+=1;owner.Land(p,10);owner.FallDone(p,true);owner.Tick();
        check(owner.VestOutput.Frames.Single().Amplitude<landing.Amplitude,"Native landing roll softens impact");owner.Clear();
        owner.Land(p,10);owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Fall},()=>g.gamePlayers[0].playerHP-=20);
        check(owner.Output.Pulses.Count==0,"Accepted fall injury waits for unified landing event");owner.Tick();
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==1,"Fall damage and physical landing do not double controllers or vest");owner.Clear();
        owner.Land(p,10);owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Fall},()=>{g.gamePlayers[0].playerHP=0;p.isDead=true;g.gameState=3;});
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Native lethal fall game3 transition retains immediate final impact");owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2&&!owner.VestOutput.Enabled,"Death-camera state does not replay deferred fall or keep effects enabled");p.isDead=false;g.gameState=0;g.gamePlayers[0].playerHP=100;owner.Tick();owner.Clear();
        owner.ExpansionReady(false);owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Fall},()=>g.gamePlayers[0].playerHP--);
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==0,"Optional expansion installation failure retains baseline fall-damage controllers");owner.ExpansionReady(true);owner.Clear();
        p.offHandItem=scrPlayerControl.OffHandItem.Shield;p.shieldState=1;p.shieldHP=50;
        owner.Damage(p,new scrHitInfo(),()=>p.shieldHP-=10);
        check(owner.Output.Pulses.Single().Left&&owner.Output.Pulses[0].Amplitude==.55f&&owner.VestOutput.Frames.Count==0,"Accepted shield block reaches shield hand, not injury vest");owner.Clear();
        owner.Damage(p,new scrHitInfo(),()=>{});check(owner.Output.Pulses.Count==0,"Unchanged shield durability does not invent a blocked hit");
        VrSettings.LeftWeapon=true;owner.Damage(p,new scrHitInfo(),()=>p.shieldHP=0);
        check(!owner.Output.Pulses.Single().Left&&owner.Output.Pulses[0].Amplitude==.9f,"Shield break is stronger and follows physical offhand in left-weapon mode");
        VrSettings.LeftWeapon=false;p.offHandItem=scrPlayerControl.OffHandItem.None;p.shieldState=0;owner.Clear();
        Time.realtimeSinceStartupAsDouble+=2;owner.Heal(p,0);check(owner.VestOutput.Frames.Count==0,"Clamped/full-health zero recovery stays silent");owner.Heal(p,1);
        check(owner.VestOutput.Frames.Single().Front==1536&&owner.VestOutput.Frames[0].Seconds==.14f&&owner.Output.Pulses.Count==0,"Actual recovery uses soft vest-only front pulse");
        owner.Heal(p,1);check(owner.VestOutput.Frames.Count==1,"Healing ticks are rate limited");owner.Clear();Time.realtimeSinceStartupAsDouble+=.76;owner.Heal(p,1);
        check(owner.VestOutput.Frames.Count==1,"Next real recovery can pulse after 750ms");owner.Clear();
        Time.realtimeSinceStartupAsDouble+=2;var platform=new scrTriggerPlatformBreak();var local=new Collider{gameObject=p.gameObject};
        owner.Platform(platform,local,()=>{});check(owner.VestOutput.Frames.Count==0,"Rejected platform collision stays silent");
        owner.Platform(platform,local,()=>platform.state=1);
        check(owner.VestOutput.Frames.Single().Front==(0xffu<<12)&&owner.Output.Pulses.Count==2,"Actual local one-shot platform collapse reaches lower vest and both hands");owner.Clear();
        owner.Platform(platform,local,()=>{});check(owner.VestOutput.Frames.Count==0,"Already broken platform does not replay");
        platform.state=0;owner.Platform(platform,new Collider(),()=>platform.state=1);check(owner.VestOutput.Frames.Count==0,"Other actor's collision cannot create player's body jolt");
        Time.realtimeSinceStartupAsDouble+=2;owner.Shake(p,4);owner.Impact();owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Strong impact supersedes same-frame underlying rumble");owner.Clear();owner.Shake(p,4);owner.Tick();
        check(owner.VestOutput.Frames.Count==0,"No weak rumble stacks during stronger impact duration");
        Time.realtimeSinceStartupAsDouble+=.3;owner.Shake(p,4);owner.Tick();check(owner.VestOutput.Frames.Count==1,"A new real rumble resumes after impact priority expires");owner.Clear();
        var trigger=new scrCutsceneTrigger{state=1,cameraState=1};g.cutsceneTrigger=new GameObject{Component=trigger};g.gameState=3;g.hudScript.cutsceneState=3;owner.Tick();
        Time.realtimeSinceStartupAsDouble+=1;owner.CinematicShake(trigger,2);owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Common native cinematic shake emits bounded vest/controller rumble");owner.Clear();
        owner.CinematicShake(new scrCutsceneTrigger{state=1,cameraState=1},4);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Unrelated cutscene object's signal is not assigned to active camera");
        trigger.state=2;owner.CinematicShake(trigger,4);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Skipped timeline signal stays silent");
        trigger.state=1;Time.realtimeSinceStartupAsDouble+=2;
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
        var gate=typeof(ExpandedWorldRumbleHooks).GetField("_cinematicAllowed",hidden)!;
        var cinematicAdd=typeof(ExpandedWorldRumbleHooks).GetMethod("CinematicGameAdd",hidden)!;
        var notify=typeof(WorldRumbleHooks).GetField("_notify",hidden)!;
        notify.SetValue(null,(Action<scrPlayerControl,float>)owner.Shake);gate.SetValue(null,(Func<bool>)(()=>false));
        int nativeCount=g.NativeShakeCalls;
        cinematicAdd.Invoke(null,new object[]{g,1f,new Vector3(1,2,3),true});owner.Tick();
        check(g.NativeShakeCalls==nativeCount+1&&g.LastPosition.x==1&&g.LastIgnore&&owner.VestOutput.Frames.Count==0,"Skipped continuous cinematic wrapper preserves exact native shake but suppresses haptics");
        gate.SetValue(null,(Func<bool>)(()=>true));cinematicAdd.Invoke(null,new object[]{g,1f,new Vector3(1,2,3),true});owner.Tick();
        check(g.NativeShakeCalls==nativeCount+2&&owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Active continuous cinematic wrapper forwards actual native delivered rumble once");owner.Clear();
        gate.SetValue(null,null);notify.SetValue(null,null);
        g.gameState=0;g.hudScript.cutsceneState=0;owner.CinematicShake(trigger,4);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Loaded post-cutscene game0 state cannot replay cinematic signal");
        g.cutsceneTrigger=null;owner.Clear();
        foreach(Action block in new Action[]{()=>g.gameState=1,()=>Application.isFocused=false,()=>XRSettings.isDeviceActive=false,()=>p.isDead=true,()=>g.connectionStatus=scrGameControl.ConnectionStatus.Online}){
            owner.Land(p,10);block();owner.Heal(p,1);owner.Tick();
            check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"New recovery and pending landings respect pause/focus/death/local guards");
            g.gameState=0;Application.isFocused=true;XRSettings.isDeviceActive=true;p.isDead=false;g.connectionStatus=scrGameControl.ConnectionStatus.Offline;owner.Tick();owner.Clear();
        }
        owner.Land(p,10);owner.SceneChanged();owner.Tick();check(owner.VestOutput.Frames.Count==0,"Scene/session suspension clears pending landing");
        VrSettings.VestHaptics.Value=false;owner.Land(p,10);owner.Tick();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==2,"Landing controllers independent of vest setting");owner.Clear();VrSettings.VestHaptics.Value=true;
        Time.realtimeSinceStartupAsDouble+=1;VrSettings.ControllerHaptics.Value=false;owner.Tick();owner.Land(p,10);owner.Tick();check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Landing vest independent of controller setting");owner.Clear();VrSettings.ControllerHaptics.Value=true;owner.Tick();
        Time.realtimeSinceStartupAsDouble+=2;VrSettings.VestHapticStrength.Value=.9f;owner.Heal(p,1);check(Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.27f)<.00001,"Recovery honors vest slider");VrSettings.VestHapticStrength.Value=1;owner.Clear();
        Time.realtimeSinceStartupAsDouble+=2;VrSettings.VestHapticStrength.Value=0;owner.Heal(p,1);check(owner.VestOutput.Frames.Count==0,"Zero recovery gain remains silent");VrSettings.VestHapticStrength.Value=1;owner.Heal(p,1);check(owner.VestOutput.Frames.Count==1,"Muted recovery does not consume enabled recovery cooldown");owner.Clear();
        Time.realtimeSinceStartupAsDouble+=2;owner.Land(p,10);owner.Impact();owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&owner.VestOutput.Frames[0].Front==VestMotorFrame.All,"Simultaneous strong impact takes priority over ordinary landing");owner.Clear();
        for(int i=0;i<1000;i++){Time.realtimeSinceStartupAsDouble+=.3;owner.Land(p,10);owner.Tick();owner.Clear();}long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){Time.realtimeSinceStartupAsDouble+=.3;owner.Land(p,10);owner.Tick();owner.Clear();}
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Landing snapshot/dispatch adds no managed allocations");
        owner.SceneChanged();owner.Tick();owner.Clear();p.transform.position=default;
    }
}
