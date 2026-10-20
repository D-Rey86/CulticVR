using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.XR;
internal static class CinematicImpactTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        var trigger=new scrCutsceneTrigger{state=1,cameraState=1};
        var root=new GameObject{Component=trigger};g.cutsceneTrigger=root;
        var source=new scrE2M6Cutscene();
        g.gameState=3;g.hudScript.cutsceneState=3;g.hudScript.playerScript=p;g.gamePlayers[0].playerHP=25;
        owner.Use();owner.Tick();owner.Clear();
        check(owner.Output.Allowed&&owner.VestOutput.Enabled,"Transports remain ready during active cinematic playback");
        check(HapticProducerTags.Enabled&&ExplosionHapticCapture.Enabled,"Real attack/explosion producers remain available during cinematic playback");
        for(int i=0;i<1000;i++)owner.Tick();
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Cinematic playback alone produces no heartbeat or rumble");
        check(root.GetComponentCalls==1,"Stable native cutscene component resolved once");
        p.playerCameraComponent.transform.position=default;p.playerCameraComponent.transform.eulerAngles=default;
        VrHandPose.Head=Quaternion.identity;WorldStereoTrialPlugin.TrackingYawCompensation=Quaternion.identity;WorldStereoTrialPlugin.TrackingPositionCompensation=default;
        owner.Blast(new(0,0,1000));
        check(owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.Output.Pulses.Count==2,"Real cinematic explosion reaches vest and both controllers without distance cutoff");
        owner.Clear();owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Explosive},()=>{});
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Cinematic damage rejected by game does not invent haptics");
        owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Explosive},()=>g.gamePlayers[0].playerHP--);
        check(owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.VestOutput.Frames[0].Back==VestMotorFrame.All,"Actual cinematic injury retains damage feedback");
        owner.Clear();owner.Impact();
        check(owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.Output.Pulses.Count==2,"Real cinematic world impact remains strong in vest and controllers");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=1;owner.Shake(p,4);owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Native cinematic world shake triggers both transports");
        owner.Clear();trigger.state=2;owner.Blast(new(0,0,2));owner.Impact();owner.Damage(p,new scrHitInfo{damageType=scrHitInfo.DamageType.Explosive},()=>g.gamePlayers[0].playerHP--);owner.Tick();
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0&&!ExplosionHapticCapture.Enabled&&!HapticProducerTags.Enabled,"Skip/load advancement suppresses all event routes and metadata");
        trigger.state=1;owner.Tick();owner.Clear();
        var projectile=new GameObject{transform=new Transform{position=new(0,0,5)}};
        for(int i=0;i<4;i++)owner.CinematicShot(projectile);
        check(owner.Output.Pulses.Count==8&&owner.VestOutput.Frames.Count==4,"Each actual cinematic round produces both-controller and vest feedback");
        check(owner.VestOutput.Frames.All(v=>v.Front!=0&&v.Back==0)&&owner.VestOutput.Frames.Select(v=>v.Front).Distinct().Count()==4,"Cinematic automatic fire advances successive front vest spots");
        owner.Clear();VrHandPose.Head=Quaternion.Euler(0,180,0);owner.CinematicShot(projectile);
        check(owner.VestOutput.Frames.Single().Back!=0,"Cinematic fire follows VR view direction");
        owner.Clear();VrHandPose.Head=Quaternion.identity;trigger.state=2;owner.CinematicShot(projectile);trigger.state=1;
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Skipping cinematic cannot emit leftover gunfire");
        g.gameState=1;owner.CinematicShot(projectile);g.gameState=3;
        check(owner.VestOutput.Frames.Count==0,"Paused cinematic gunfire stays silent");
        for(int i=0;i<1000;i++){owner.CinematicShot(projectile);owner.Clear();}
        long gunBytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){owner.CinematicShot(projectile);owner.Clear();}
        check(GC.GetAllocatedBytesForCurrentThread()==gunBytes,"Cinematic gunfire dispatch allocates no managed memory");
        Action damage=()=>source.window.hp=0;void Window(){source.window.hp=999999;owner.Window(source,damage);}
        Window();
        check(owner.Output.Pulses.Count==2&&owner.Output.Pulses.All(v=>v.Amplitude==1&&v.Seconds==.25f),"Window impact reaches both controllers at full authored strength");
        var frame=owner.VestOutput.Frames.Single();
        check(frame.Front==VestMotorFrame.All&&frame.Back==VestMotorFrame.All&&frame.Amplitude==1&&frame.Seconds==.25f,"Window impact reaches all 40 vest motors for 250ms");
        owner.Clear();owner.Window(source,()=>source.window.hp=0);
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Repeated signal on broken window does not replay");
        source.window.hp=999999;owner.Window(source,()=>{});
        check(owner.VestOutput.Frames.Count==0,"Rejected native break does not produce feedback");
        trigger.state=2;Window();owner.Tick();
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0&&!owner.VestOutput.Enabled,"Skip/save-load state suppresses window event and closes output");
        trigger.state=1;trigger.cameraState=0;Window();
        check(owner.Output.Pulses.Count==0,"Inactive cutscene camera suppresses impact");
        trigger.cameraState=1;g.hudScript.cutsceneState=4;Window();
        check(owner.VestOutput.Frames.Count==0,"Fade-out window signal suppressed");
        g.hudScript.cutsceneState=3;g.gameState=1;Window();owner.Tick();
        check(owner.Output.Pulses.Count==0&&!owner.VestOutput.Enabled,"Paused cutscene remains silent");
        g.gameState=3;owner.Tick();Application.isFocused=false;Window();owner.Tick();
        check(owner.Output.Pulses.Count==0&&!owner.VestOutput.Enabled,"Application focus loss suppresses cutscene feedback");
        Application.isFocused=true;owner.Tick();OpenXRControllersPlugin.HapticSessionState(4);Window();
        check(owner.Output.Pulses.Count==0&&!owner.VestOutput.Enabled,"XR focus loss suspends cinematic output");
        OpenXRControllersPlugin.HapticSessionState(5);owner.Tick();owner.Clear();
        g.connectionStatus=scrGameControl.ConnectionStatus.Online;Window();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Network session does not produce local cinematic feedback");
        g.connectionStatus=scrGameControl.ConnectionStatus.Offline;p.isDead=true;Window();p.isDead=false;
        check(owner.Output.Pulses.Count==0,"Dead player cannot trigger cinematic impact");
        VrSettings.VestHaptics.Value=false;owner.Tick();Window();
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==0,"Window controllers independent of vest enable");
        owner.Clear();VrSettings.VestHaptics.Value=true;VrSettings.ControllerHaptics.Value=false;owner.Tick();Window();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Window vest independent of controller enable");
        owner.Clear();VrSettings.ControllerHaptics.Value=true;VrSettings.VestHapticStrength.Value=.9f;VrSettings.ControllerHapticStrength.Value=.4f;owner.Tick();Window();
        check(owner.Output.Pulses.All(v=>Math.Abs(v.Amplitude-.4f)<.00001f)&&Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.9f)<.00001f,"Window respects independent strength sliders");
        owner.Clear();VrSettings.VestHapticStrength.Value=1;VrSettings.ControllerHapticStrength.Value=1;
        for(int i=0;i<1000;i++)owner.Tick();long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)owner.Tick();
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Cinematic readiness adds no steady per-frame allocations");
        for(int i=0;i<1000;i++){Window();owner.Clear();}bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){Window();owner.Clear();}
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Window dispatch adds no managed allocations");
        var replacement=new GameObject{Component=trigger};g.cutsceneTrigger=replacement;owner.Tick();
        check(replacement.GetComponentCalls==1,"Replacement cutscene invalidates component cache");
        owner.SceneChanged();owner.Tick();
        check(replacement.GetComponentCalls==2,"Scene transition invalidates cutscene cache");
        g.gameState=0;g.hudScript.cutsceneState=0;g.cutsceneTrigger=null;g.gamePlayers[0].playerHP=100;owner.Tick();owner.Clear();
        Window();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Gameplay cannot invoke cinematic-only window haptics");
    }
}


