using CulticVR.OpenXRControllers;
using UnityEngine.XR;
using ControllerHapticTransport = CulticVR.OpenXRControllers.HapticTransport<UnityEngine.XR.FakeHapticDevice>;

int checks=0;
void Check(bool result,string text){checks++;if(!result)throw new Exception(text);}
var left=new DeviceState();var right=new DeviceState();
var ld=InputDevices.Connect(true,left);var rd=InputDevices.Connect(false,right);
using(var t=new ControllerHapticTransport(new FakeHapticHands())){
    Check(!t.Pulse(true,.5f,.1f,0),"No output before a session");
    t.Begin(11);t.State(2);t.State(3);t.State(4);t.State(5);
    Check(left.Stops+right.Stops==0,"Startup must never issue a stop");
    Check(t.LeftSupported&&t.RightSupported,"Capability gated hand caches");
    Check(!t.Pulse(true,.5f,.1f,0),"Caller authorization defaults off");
    t.SetAllowed(true);
    Check(t.Pulse(true,.23f,.06f,1)&&left.Sends==1&&right.Sends==0,"Left hand selection");
    Check(t.Pulse(false,.67f,.12f,1)&&right.Sends==1&&left.Amplitude==.23f,"Right hand selection");
    t.Tick(1.05);Check(left.Stops+right.Stops==0,"No premature cutoff");
    t.Tick(1.08);Check(left.Stops==1&&right.Stops==0,"Independent left deadline");
    t.Tick(1.13);Check(right.Stops==1,"Independent right deadline");
    t.Tick(2);t.Stop();Check(left.Stops+right.Stops==2,"No duplicate stops");
    foreach(var value in new[]{float.NaN,float.NegativeInfinity,float.PositiveInfinity,-1f,0f}){
        Check(!t.Pulse(true,value,.1f,2),"Reject nonfinite/nonpositive amplitude");
        Check(!t.Pulse(true,.5f,value,2),"Reject nonfinite/nonpositive duration");
    }
    Check(!t.Pulse(true,.5f,.1f,double.NaN),"Reject invalid clock");
    Check(t.Pulse(true,3f,2f,3)&&left.Amplitude==1f&&left.Seconds==.25f,"Bound amplitude/duration");
    t.State(4);Check(left.Stops==2&&!t.Pulse(true,.5f,.1f,3),"Focus loss stops then rejects");
    t.State(3);Check(left.Stops==2,"Lower session states never repeat a stop");
    t.State(5);t.Pulse(true,.5f,.2f,4);t.Pulse(false,.5f,.2f,4);
    t.SetAllowed(false);Check(left.Stops==3&&right.Stops==2,"Pause/disable stops both active hands");
    Check(!t.Pulse(false,.5f,.1f,4),"Disabled pulse rejected");t.SetAllowed(true);
    int queries=InputDevices.Queries,caps=left.CapabilityQueries+right.CapabilityQueries;
    for(int i=0;i<1000;i++){t.Pulse(true,.3f,.01f,5+i);t.Tick(5+i+.02);}
    Check(InputDevices.Queries==queries&&left.CapabilityQueries+right.CapabilityQueries==caps,"No device/capability searches on hot path");
    t.Pulse(true,.5f,.2f,2000);int stops=left.Stops;
    InputDevices.Disconnect(ld);t.Tick(2001);Check(left.Stops==stops&&!t.Pulse(true,.5f,.1f,2001),"Disconnect discards stale endpoint without output");
    left=new DeviceState();ld=InputDevices.Connect(true,left);Check(t.Pulse(true,.5f,.1f,2001),"Reconnect resolves new endpoint");
    t.End();Check(left.Stops==0&&t.Session==0&&!t.Pulse(true,.5f,.1f,2001),"Session destruction invalidates without output");
    t.Begin(12);t.State(3);Check(!t.Pulse(true,.5f,.1f,2002),"New session waits for focus");t.State(5);
    Check(t.Pulse(false,.5f,.1f,2002),"Recreated session reacquires hands");t.Tick(2003);
    left.Reject=true;Check(!t.Pulse(true,.5f,.1f,2003),"Provider false propagates");int sends=left.Sends;
    Check(!t.Pulse(true,.5f,.1f,2004)&&left.Sends==sends,"Failed provider does not hammer native output");
    left.Reject=false;InputDevices.Configure(ld);Check(t.Pulse(true,.5f,.2f,2004),"Device reconfiguration resets capability state");
    left.ThrowSend=true;stops=left.Stops;Check(!t.Pulse(true,.5f,.1f,2004)&&left.Stops==stops+1,"Provider exception cancels previous active pulse");
    left.ThrowSend=false;InputDevices.Configure(ld);left.ThrowStop=true;t.Pulse(true,.5f,.1f,2005);t.Stop();
    sends=left.Sends;Check(!t.Pulse(true,.5f,.1f,2006)&&left.Sends==sends,"Stop failure silences channel");
    left.ThrowStop=false;InputDevices.Configure(ld);left.Supported=false;InputDevices.Configure(ld);
    Check(!t.LeftSupported&&!t.Pulse(true,.5f,.1f,2006),"Unsupported controller remains silent");
    left.Supported=true;left.Channels=0;InputDevices.Configure(ld);Check(!t.LeftSupported,"Zero channels rejected");
    left.Channels=1;InputDevices.Configure(ld);t.Pulse(true,.5f,.1f,2007);t.Tick(2008);
    // Exercise the compiled production hot path, excluding test assertion formatting.
    for(int i=0;i<100;i++){t.Pulse(true,.2f,.01f,i);t.Tick(i+.02);}
    long allocated=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<10000;i++){t.Pulse(true,.2f,.01f,i);t.Tick(i+.02);}
    allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
    Check(allocated==0,"No managed allocations on pulse/tick path");
    t.Dispose();sends=left.Sends;InputDevices.Configure(ld);Check(!t.Pulse(true,.5f,.1f,3000)&&left.Sends==sends,"Disposed transport remains silent");
}
// Background world rumble must yield per hand to foreground combat output.
using(var background=new ControllerHapticTransport(new FakeHapticHands())){
    Check(!background.PulseIfIdle(true,.2f,.1f,0),"Background respects absent session");
    background.Begin(20);background.State(5);background.SetAllowed(true);
    Check(background.Pulse(true,.95f,.14f,10),"Foreground shot starts");int sendsBefore=left.Sends;
    Check(!background.PulseIfIdle(true,.2f,.1f,10.01)&&left.Sends==sendsBefore&&left.Amplitude==.95f,"Ambient cannot replace active shot or shorten its duration");
    Check(background.PulseIfIdle(false,.2f,.1f,10.01)&&right.Amplitude==.2f,"Unoccupied opposite hand can rumble");
    Check(background.PulseIfIdle(true,.2f,.1f,10.15),"Ambient resumes after shot deadline");
    Check(background.Pulse(true,1,.16f,10.16)&&left.Amplitude==1,"Foreground immediately replaces background");
    background.SetAllowed(false);Check(!background.PulseIfIdle(false,.2f,.1f,10.17),"Background respects disable");background.SetAllowed(true);
    background.State(4);Check(!background.PulseIfIdle(false,.2f,.1f,10.18),"Background respects focus loss");background.State(5);
    for(int i=0;i<1000;i++){background.PulseIfIdle(true,.2f,.01f,20+i);background.Tick(20+i+.02);}
    long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){background.PulseIfIdle(true,.2f,.01f,2000+i);background.Tick(2000+i+.02);}bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
    Check(bytes==0,"Background idle arbitration has no managed allocation");
}
Console.WriteLine($"PASS {checks} transport contracts; 10,000 pulse/deadline cycles allocated zero managed bytes. Fixture API tests, not runtime output proof.");

PatternTests.Run();

