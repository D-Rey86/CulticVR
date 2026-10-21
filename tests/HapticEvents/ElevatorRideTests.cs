using System.Reflection;
using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;

internal static class ElevatorRideTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        ElevatorRideHooks.Shutdown();
        typeof(ElevatorRideHooks).GetField("_notify",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,(Action<scrPlayerControl,float>)owner.Shake);
        g.gameState=0;g.hudScript.cutsceneState=0;p.isDead=false;g.gamePlayers[p.playerID].playerHP=100;
        VrSettings.VestHaptics.Value=VrSettings.ControllerHaptics.Value=true;VrSettings.MotionControls=true;
        VrSettings.VestHapticStrength.Value=.9f;VrSettings.ControllerHapticStrength.Value=1;
        var lift=new scrFreightElevatorPlatform{state=2};var parent=new Transform{Component=lift};
        var platform=new scrMovingPlatform{isStationary=true,parentObject=parent};
        void Clear(){Time.realtimeSinceStartupAsDouble+=1;owner.Tick();owner.Clear();}
        void Move(){ElevatorRideHooks.MoveRider(p.transform,p.transform.position+new Vector3(0,-.1f,0),platform);owner.Tick();}
        Clear();var before=p.transform.position;Move();
        check(p.transform.position.y==before.y-.1f,"Native passenger displacement preserved");
        var frame=owner.VestOutput.Frames.Single();
        check(frame.Front==frame.Back&&System.Numerics.BitOperations.PopCount(frame.Front)==12&&Math.Abs(frame.Amplitude-.2925f)<.00001f&&frame.Seconds==.1f,"Moving cargo lift gives scaled lower front/back rumble");
        check(owner.Output.Pulses.Count==2,"Cargo lift drives both controllers");
        check(parent.GetComponentCalls==1,"Lift component resolved once");
        Clear();ElevatorRideHooks.MoveRider(p.transform,p.transform.position,platform);owner.Tick();
        check(owner.VestOutput.Frames.Count==0,"Stationary passenger gets no movement feedback");
        Clear();var other=new Transform();ElevatorRideHooks.MoveRider(other,new Vector3(1,2,3),platform);owner.Tick();
        check(other.position.y==2&&owner.VestOutput.Frames.Count==0,"Other passengers still move without local feedback");
        foreach(int state in new[]{0,1,3}){Clear();lift.state=state;Move();check(owner.VestOutput.Frames.Count==0,"Idle, alarm and stopping states do not fabricate a ride");}lift.state=2;
        Clear();platform.isStationary=false;Move();check(owner.VestOutput.Frames.Count==0,"Unrelated path-driven platform output unchanged");platform.isStationary=true;
        var unrelated=new Transform();platform.parentObject=unrelated;Clear();Move();Move();
        check(unrelated.GetComponentCalls==4&&owner.VestOutput.Frames.Count==0,"Unsupported parent cached without repeated searches or output");
        var replacement=new Transform{Component=lift};platform.parentObject=replacement;Clear();Move();
        check(replacement.GetComponentCalls==1&&owner.VestOutput.Frames.Count==1,"Changed parent invalidates cached lift binding");
        Clear();VrSettings.ControllerHaptics.Value=false;owner.Tick();Move();
        check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Elevator vest independent of controller toggle");VrSettings.ControllerHaptics.Value=true;
        Clear();VrSettings.VestHaptics.Value=false;owner.Tick();Move();
        check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==2,"Elevator controllers independent of vest toggle");VrSettings.VestHaptics.Value=true;
        Clear();VrSettings.VestHapticStrength.Value=0;owner.Tick();Move();check(owner.VestOutput.Frames.Count==0,"Zero vest slider mutes elevator");VrSettings.VestHapticStrength.Value=.9f;
        Clear();g.gameState=1;owner.Tick();Move();g.gameState=0;owner.Tick();
        check(owner.VestOutput.Frames.Count==0,"Paused ride cannot queue a stale pulse");
        Clear();Application.isFocused=false;Move();Application.isFocused=true;owner.Tick();
        check(owner.VestOutput.Frames.Count==0,"Unfocused native movement cannot replay feedback");
        Clear();p.isDead=true;Move();p.isDead=false;owner.Tick();check(owner.VestOutput.Frames.Count==0,"Dead passenger has no rumble");
        Clear();ElevatorRideHooks.MoveRider(p.transform,p.transform.position+new Vector3(0,-.1f,0),platform);owner.SceneChanged();owner.Tick();
        check(owner.VestOutput.Frames.Count==0,"Scene transition discards queued ride feedback");
        Clear();owner.Impact();owner.Clear();Move();check(owner.VestOutput.Frames.Count==0,"Elevator rumble yields to strong foreground impacts");
        Clear();int pulses=0;
        for(int i=0;i<500;i++){Time.realtimeSinceStartupAsDouble+=.02;Move();pulses+=owner.VestOutput.Frames.Count;owner.Clear();}
        check(pulses>=80&&pulses<=101,"50Hz passenger updates coalesce to at most ten pulses per second");
        check(replacement.GetComponentCalls==1,"Repeated movement never repeats component lookup");
        for(int i=0;i<1000;i++){Time.realtimeSinceStartupAsDouble+=.02;Move();owner.Clear();}
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++){Time.realtimeSinceStartupAsDouble+=.02;Move();owner.Clear();}
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Steady elevator dispatch allocates zero managed bytes");
        Clear();Time.realtimeSinceStartupAsDouble+=.5;owner.Tick();check(owner.VestOutput.Frames.Count==0,"No timer-driven ride persists after motion stops");
        // Exercise each new native component through the real consumer/scheduler/output path.
        var freight=new scrFreightElevator();var office=new scrOfficeElevator();var dumbwaiter=new scrDumbwaiter();
        foreach(var spec in new (object lift,Action<int> state,float scale,int lookups)[]{
            (freight,v=>freight.state=v,1f,2),(office,v=>office.state=v,.7f,3),(dumbwaiter,v=>dumbwaiter.state=v,.7f,4)}){
            var carrier=new Transform{Component=spec.lift};platform.parentObject=carrier;spec.state(2);
            Clear();Move();var travel=owner.VestOutput.Frames.Single();
            check(Math.Abs(travel.Amplitude-.2925f*spec.scale)<.00001f&&travel.Front==frame.Front&&travel.Back==frame.Back&&travel.Seconds==.1f,"Each lift gives expected vest travel strength/layout/duration");
            check(owner.Output.Pulses.Count==2,"Each lift drives both controllers");
            foreach(var pulse in owner.Output.Pulses)check(Math.Abs(pulse.Amplitude-.24375f*spec.scale)<.00001f,"Controller travel strength scales with lift size");
            foreach(int state in new[]{0,1,3}){Clear();spec.state(state);Move();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Each lift silent outside traveling state");}spec.state(2);
            Clear();ElevatorRideHooks.MoveRider(p.transform,p.transform.position,platform);owner.Tick();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Each stationary lift silent");
            Clear();VrSettings.ControllerHaptics.Value=false;owner.Tick();Move();check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Each lift supports vest alone");VrSettings.ControllerHaptics.Value=true;
            Clear();VrSettings.VestHaptics.Value=false;owner.Tick();Move();check(owner.Output.Pulses.Count==2&&owner.VestOutput.Frames.Count==0,"Each lift supports controllers alone");VrSettings.VestHaptics.Value=true;
            Clear();g.gameState=1;owner.Tick();Move();g.gameState=0;owner.Tick();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Each lift pause guard prevents stale feedback");
            for(int i=0;i<1000;i++){Time.realtimeSinceStartupAsDouble+=.02;Move();owner.Clear();}
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++){Time.realtimeSinceStartupAsDouble+=.02;Move();owner.Clear();}
            check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Each lift allocates zero in steady movement");
            check(carrier.GetComponentCalls==spec.lookups,"Each lift resolves component only on binding");
        }
        platform.parentObject=replacement;
        ElevatorRideHooks.Shutdown();Clear();before=p.transform.position;Move();
        check(p.transform.position.y==before.y-.1f&&owner.VestOutput.Frames.Count==0,"Uninstalled optional observer preserves native movement");
        VrSettings.VestHapticStrength.Value=1;owner.Clear();
    }
}
