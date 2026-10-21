using System.Reflection;
using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
internal static class FinalRumbleTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        typeof(WorldRumbleHooks).GetField("_notify",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,(Action<scrPlayerControl,float>)owner.Shake);
        g.gameState=0;p.isDead=false;Application.isFocused=true;g.gamePlayers[p.playerID].playerHP=100;
        VrSettings.VestHaptics.Value=VrSettings.ControllerHaptics.Value=true;
        VrSettings.VestHapticStrength.Value=.9f;VrSettings.ControllerHapticStrength.Value=1;
        void Clear(){Time.realtimeSinceStartupAsDouble+=1;owner.Tick();owner.Clear();}
        void Fire(){WorldRumbleHooks.RestrainedGameAdd(g,2,new Vector3(1,2,3),false);owner.Tick();}
        foreach(float delivered in new[]{.05f,.25f,.49f,1.5f,2f}){
            Clear();g.DeliveredShake=delivered;int native=g.NativeShakeCalls;Fire();
            float amplitude=.325f*MathF.Sqrt(Math.Min(delivered,.49f));
            check(g.NativeShakeCalls==native+1&&g.LastAmount==2&&g.LastPosition.x==1&&!g.LastIgnore,"Firing wrapper preserves native shake and arguments once");
            check(owner.VestOutput.Frames.Count==1&&Math.Abs(owner.VestOutput.Frames[0].Amplitude-amplitude*.9f)<.00001f,"Firing vest cap preserves smaller distance-attenuated amounts");
            check(owner.Output.Pulses.Count==2&&owner.Output.Pulses.All(x=>Math.Abs(x.Amplitude-amplitude*.75f)<.00001f),"Firing cap reaches both controllers");
        }
        Clear();g.DeliveredShake=2;WorldRumbleHooks.RestrainedGameAdd(g,2,default,true);WorldRumbleHooks.RestrainedGameAdd(g,2,default,false);owner.Tick();
        check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Paired cannon shakes coalesce to one output");
        Clear();WorldRumbleHooks.GameAdd(g,2,default,true);owner.Tick();check(owner.VestOutput.Frames.Single().Amplitude>.4f,"Ordinary impacts retain uncapped amplitude");
        Clear();g.ThrowOnShake=true;try{Fire();}catch(InvalidOperationException){}g.ThrowOnShake=false;p.addShake(2);owner.Tick();check(owner.VestOutput.Frames.Count==0,"Exception restores firing scope");
        Clear();WorldRumbleHooks.GameAdd(g,2,default,true);owner.Tick();check(owner.VestOutput.Frames.Single().Amplitude>.4f,"Exception cannot leave ordinary impacts restrained");
        Clear();VrSettings.ControllerHaptics.Value=false;owner.Tick();Fire();check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==0,"Firing vest independent of controllers");VrSettings.ControllerHaptics.Value=true;
        Clear();VrSettings.VestHaptics.Value=false;owner.Tick();Fire();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==2,"Firing controllers independent of vest");VrSettings.VestHaptics.Value=true;
        Clear();g.gameState=1;owner.Tick();Fire();g.gameState=0;owner.Tick();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Paused firing does not replay later");
        Clear();WorldRumbleHooks.RestrainedGameAdd(g,2,default,true);owner.Impact();owner.Clear();owner.Tick();check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Strong impact discards queued firing rumble");
        foreach(float amount in new[]{.5f,1f,3f}){
            Clear();WorldRumbleHooks.PlayerAdd(p,amount);owner.Tick();check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Trap landing and dropper native delivered amounts drive both outputs");
        }
        Clear();for(int i=0;i<1000;i++){Time.realtimeSinceStartupAsDouble+=.02;Fire();owner.Clear();}
        long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){Time.realtimeSinceStartupAsDouble+=.02;Fire();owner.Clear();}
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Restrained firing adds no steady allocations");
        g.DeliveredShake=null;VrSettings.VestHapticStrength.Value=1;Clear();
    }
}
