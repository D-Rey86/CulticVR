using System.Reflection;
using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
internal static class WorldRumbleTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        var scheduler=new HapticWorldRumble();
        scheduler.Submit(.2f,0);scheduler.Submit(4,0);
        check(scheduler.Tick(true,0,1,out var peak)&&peak.Amplitude==.65f,"Strong same-frame jolt retained above mild tremor");
        check(peak.Front==peak.Back&&System.Numerics.BitOperations.PopCount(peak.Front)==12&&peak.Seconds==.1f,"Bounded lower chest/back rumble distinct from explosion");
        scheduler.Submit(.2f,.01);check(!scheduler.Tick(true,.01,1,out _),"Frame-rate emissions coalesced");
        check(scheduler.Tick(true,.1,1,out var mild)&&mild.Amplitude<peak.Amplitude,"Tremor strength follows native amount");
        scheduler.Submit(1,.11);check(!scheduler.Tick(true,.11,1,out _),"Rate cap independent of rendering frame rate");
        check(!scheduler.Tick(true,.5,1,out _),"Stale pending tremor dropped after hitch");
        check(!scheduler.Tick(true,.6,1,out _),"No synthetic pulses after native event stops");
        scheduler.Submit(2,.7);check(!scheduler.Tick(false,.7,1,out _),"Pause cancels pending event");
        check(!scheduler.Tick(true,.8,1,out _),"Resume does not replay old tremor");
        scheduler.Submit(4,1);check(scheduler.Tick(true,1,.9f,out var scaled)&&Math.Abs(scaled.Amplitude-.585f)<.00001f,"Existing vest strength honored");
        scheduler.Submit(1,.9);check(scheduler.Tick(true,.9,1,out _),"Clock rewind restarts safely");
        scheduler.Submit(4,2);check(!scheduler.Tick(true,double.NaN,1,out _),"Invalid clock cancels pending effect");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-1,0})scheduler.Submit(invalid,3);
        check(!scheduler.Tick(true,3,1,out _),"Invalid/zero native shake silent");
        scheduler.Submit(100,4);check(scheduler.Tick(true,4,1,out var bounded)&&bounded.Amplitude==.65f,"Extreme native shake bounded");
        scheduler.Submit(4,5);check(!scheduler.Tick(true,5,0,out _),"Zero strength silent");
        int pulses=0;for(int i=0;i<900;i++){double now=10+i/90d;scheduler.Submit(.5f,now);if(scheduler.Tick(true,now,1,out _))pulses++;}
        check(pulses is >=85 and <=100,"90 Hz continuous shake becomes at most ten commands per second");
        for(int i=0;i<1000;i++){scheduler.Submit(.5f,100+i*.01);scheduler.Tick(true,100+i*.01,1,out _);}
        long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){scheduler.Submit(.5f,200+i*.01);scheduler.Tick(true,200+i*.01,1,out _);}bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(bytes==0,"Native shake scheduler adds zero managed allocations");

        g.gamePlayers[0].playerHP=100;g.hudScript.playerScript=p;Time.realtimeSinceStartupAsDouble=1000;owner.Use();owner.Tick();owner.Clear();
        var notify=typeof(WorldRumbleHooks).GetField("_notify",BindingFlags.Static|BindingFlags.NonPublic)!;
        notify.SetValue(null,(Action<scrPlayerControl,float>)owner.Shake);
        p.addShake(4);p.setShake(4);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Unscoped recoil/explosion/player shake never creates world rumble");
        int native=g.NativeShakeCalls;g.DeliveredShake=.2f;WorldRumbleHooks.GameAdd(g,4,new(1,2,3),false);owner.Tick();
        check(g.NativeShakeCalls==native+1&&g.LastAmount==4&&g.LastPosition.x==1&&!g.LastIgnore,"Native addShake called once with identical arguments");
        check(owner.VestOutput.Frames.Count==1&&Math.Abs(owner.VestOutput.Frames[0].Amplitude-.65f*MathF.Sqrt(.2f/4))<.00001f&&owner.Output.Pulses.Count==2&&owner.Output.Pulses[0].Left!=owner.Output.Pulses[1].Left&&Math.Abs(owner.Output.Pulses[0].Amplitude-owner.VestOutput.Frames[0].Amplitude*.75f)<.00001f,"Native delivered attenuation drives both hands and lower vest");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;g.DeliveredShake=2;WorldRumbleHooks.GameSet(g,3,default,true);owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&g.LastAmount==3&&g.LastIgnore&&p.cameraShake==2,"Native setShake behavior preserved");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;WorldRumbleHooks.PlayerAdd(p,.2f);owner.Tick();check(owner.VestOutput.Frames.Count==1,"Direct local-player earthquake path covered");
        owner.Clear();g.ThrowOnShake=true;try{WorldRumbleHooks.GameAdd(g,1,default,true);}catch(InvalidOperationException){}g.ThrowOnShake=false;p.addShake(4);owner.Tick();
        check(owner.VestOutput.Frames.Count==0,"Exception restores scope and cannot leak world feedback into recoil");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;g.gameState=1;WorldRumbleHooks.PlayerAdd(p,2);owner.Tick();g.gameState=0;owner.Tick();check(owner.VestOutput.Frames.Count==0,"Paused shake suppressed, no stale resume");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;var remote=new scrPlayerControl{playerID=1,playerHUD=g.hudScript};WorldRumbleHooks.PlayerAdd(remote,2);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Other player's quake notification silent");
        owner.Clear();VrSettings.VestHaptics.Value=false;WorldRumbleHooks.PlayerAdd(p,2);owner.Tick();VrSettings.VestHaptics.Value=true;owner.Tick();check(owner.VestOutput.Frames.Count==0,"Vest-disabled event not replayed when re-enabled");
                owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;VrSettings.ControllerHapticStrength.Value=.4f;VrSettings.VestHapticStrength.Value=.9f;WorldRumbleHooks.PlayerAdd(p,4);owner.Tick();
        check(owner.Output.Pulses.Count==2&&Math.Abs(owner.Output.Pulses[0].Amplitude-.195f)<.00001f&&Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.585f)<.00001f,"Independent controller and vest strengths");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;VrSettings.VestHaptics.Value=false;WorldRumbleHooks.PlayerAdd(p,4);owner.Tick();
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==0,"Both controllers still rumble with vest disabled");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;VrSettings.VestHaptics.Value=true;VrSettings.ControllerHaptics.Value=false;WorldRumbleHooks.PlayerAdd(p,4);owner.Tick();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Vest rumble independent of controller enable");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;VrSettings.VestHaptics.Value=false;WorldRumbleHooks.PlayerAdd(p,4);owner.Tick();VrSettings.ControllerHaptics.Value=true;VrSettings.VestHaptics.Value=true;owner.Tick();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Both disabled drops pending rumble, no replay on enable");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;VrSettings.MotionControls=false;WorldRumbleHooks.PlayerAdd(p,4);owner.Tick();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Gamepad/headset mode keeps vest without motion-controller output");
        VrSettings.MotionControls=true;VrSettings.ControllerHapticStrength.Value=1;VrSettings.VestHapticStrength.Value=1;
        var impact=typeof(WorldRumbleHooks).GetField("_impact",BindingFlags.Static|BindingFlags.NonPublic)!;
        impact.SetValue(null,(Action)owner.Impact);
        p.playerCameraComponent.transform.position=default;p.playerCameraComponent.transform.eulerAngles=default;
        WorldStereoTrialPlugin.TrackingPositionCompensation=default;WorldStereoTrialPlugin.TrackingYawCompensation=Quaternion.identity;
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;
        WorldRumbleHooks.BarnBroken(new scrE2M5BarnWalls{objectsToActivateOnce=new[]{new GameObject{transform=new Transform{position=new(0,0,5)}},new GameObject{transform=new Transform{position=new(0,0,10)}}}});
        owner.Tick();
        check(owner.Output.Pulses.Count==2&&owner.Output.Pulses.All(v=>v.Amplitude==1&&v.Seconds==.25f)&&owner.VestOutput.Frames.Count==1,"Multiple wall debris produces one strong pulse in both hands and vest");
        var wall=owner.VestOutput.Frames.Single();
        check(wall.Front==VestMotorFrame.All&&wall.Back==VestMotorFrame.All&&wall.Amplitude==1&&wall.Seconds==.25f,"Wall impact covers all 40 motors at full authored strength for 250ms");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;
        WorldRumbleHooks.HarvesterBroken(new scrE2M1HarvesterTrigger{transform=new Transform{position=new(0,0,1000)}});
        check(owner.VestOutput.Frames.Single().Amplitude==wall.Amplitude&&owner.Output.Pulses.Count==2,"Distant shared wall event has identical strength");
        owner.Clear();VrSettings.ControllerHapticStrength.Value=.4f;VrSettings.VestHapticStrength.Value=.9f;
        owner.Impact();
        check(owner.Output.Pulses.All(v=>Math.Abs(v.Amplitude-.4f)<.00001f)&&Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.9f)<.00001f,"Wall pulse honors independent user strength sliders");
        owner.Clear();VrSettings.VestHaptics.Value=false;owner.Impact();
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==0,"Wall controllers work with vest disabled");
        owner.Clear();VrSettings.VestHaptics.Value=true;VrSettings.ControllerHaptics.Value=false;owner.Impact();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Wall vest works with controllers disabled");
        owner.Clear();VrSettings.ControllerHaptics.Value=true;VrSettings.ControllerHapticStrength.Value=1;VrSettings.VestHapticStrength.Value=1;
        g.gameState=1;owner.Impact();g.gameState=0;owner.Tick();
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Paused wall event cannot replay");
        for(int i=0;i<1000;i++){owner.Impact();owner.Clear();}
        bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){owner.Impact();owner.Clear();}
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Wall event dispatch allocates no managed memory");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=.2;
        var scriptedNotify=typeof(ExplosionHapticCapture).GetField("_scriptedNotify",BindingFlags.Static|BindingFlags.NonPublic)!;
        scriptedNotify.SetValue(null,(Action<Vector3>)owner.ScriptedBlast);
        var blast=typeof(WorldRumbleHooks).GetField("_blast",BindingFlags.Static|BindingFlags.NonPublic)!;
        blast.SetValue(null,(Action<GameObject>)ExplosionHapticCapture.ScriptedDetonation);
        var effect=new GameObject{Component=new scrExplosion{transform=new Transform{position=new(0,0,1000)}}};
        WorldRumbleHooks.PlungerDetonated(new scrE2M3Plunger{explosionObject=effect});
        check(owner.Output.Pulses.Count==2&&owner.Output.Pulses.All(v=>v.Amplitude==1)&&owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.VestOutput.Frames[0].Amplitude==1,"Detonator at 1000 units produces full-strength front vest and both hands");
        owner.Clear();WorldRumbleHooks.PlungerSpawned(new GameObject{transform=new Transform{position=new(0,0,-1000)}});
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Single().Back==VestMotorFrame.All,"Chapter One detonator spawns use same strong directional path");
        owner.Clear();owner.Blast(new(0,0,1000));
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Single().Amplitude==1,"Ordinary distant explosions have full strength without a range limit");
        owner.Clear();
        VrSettings.VestHaptics.Value=false;owner.Tick();owner.ScriptedBlast(new(0,0,1000));
        check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==0,"Scripted blast controllers independent of vest enable");
        owner.Clear();VrSettings.VestHaptics.Value=true;VrSettings.ControllerHaptics.Value=false;owner.Tick();owner.ScriptedBlast(new(0,0,1000));
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Scripted blast vest independent of controllers");
        owner.Clear();VrSettings.ControllerHaptics.Value=true;VrSettings.ControllerHapticStrength.Value=.4f;VrSettings.VestHapticStrength.Value=.9f;owner.Tick();owner.ScriptedBlast(new(0,0,1000));
        check(owner.Output.Pulses.All(v=>Math.Abs(v.Amplitude-.4f)<.00001f)&&Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.9f)<.00001f,"Scripted blast honors both strength sliders");
        owner.Clear();g.gameState=1;owner.ScriptedBlast(default);g.gameState=0;
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Scripted blast remains silent in pause");
        VrSettings.ControllerHapticStrength.Value=1;VrSettings.VestHapticStrength.Value=1;
        scriptedNotify.SetValue(null,null);blast.SetValue(null,null);
        impact.SetValue(null,null);
        g.DeliveredShake=null;notify.SetValue(null,null);
    }
}
