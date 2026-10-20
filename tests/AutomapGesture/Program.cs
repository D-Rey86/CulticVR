using System;
using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
static class Program {
    static void Need(bool b,string s){if(!b)throw new Exception(s);}
    static readonly Vector3 Head=Vector3.zero,Hand=new Vector3(0,0,.4f);
    static Quaternion Face(bool left)=>new Quaternion(0,left?.70710677f:-.70710677f,0,.70710677f);
    static bool Step(ref AutomapGesture g,float t,bool facing,bool eligible=true,bool left=false,Vector3? hand=null,Quaternion? headRotation=null) =>
        g.Step(eligible,left,t,Head,headRotation??Quaternion.identity,hand??Hand,facing?Face(left):Quaternion.identity);
    static void Main(){
        foreach(bool left in new[]{false,true}) {
            var g=new AutomapGesture();g.Reset();
            Need(!Step(ref g,0,true,left:left) && !Step(ref g,.05f,true,left:left),"Reject passing glance");
            Need(Step(ref g,.13f,true,left:left),"Look at facing palm activates without lowering/raising/stability ritual");
            Need(!Step(ref g,1,true,left:left),"Continuing to look cannot repeat");
            g.CancelPending();Need(!Step(ref g,1.2f,true,left:left),"Binding refresh preserves activation latch");
            Step(ref g,2,true,eligible:false,left:left);
            Need(!Step(ref g,3,true,left:left),"Temporary ineligibility cannot repeat while still looking");
            Step(ref g,4,false,eligible:false,left:left);
            Need(!Step(ref g,4.1f,true,left:left) && Step(ref g,4.23f,true,left:left),"Look-away rearms even during temporary ineligibility");
            Step(ref g,5,false,left:left);Step(ref g,5.1f,true,left:left);
            Need(Step(ref g,5.23f,true,left:left,hand:new Vector3(0,0,.7f)),"Movement no longer restarts gesture");
        }
        var invalid=new AutomapGesture();
        Need(!Step(ref invalid,0,true,hand:new Vector3(float.NaN,0,.4f)),"Nonfinite hand rejected");
        Need(!Step(ref invalid,1,true,headRotation:new Quaternion()),"Invalid head rotation rejected");
        Need(!Step(ref invalid,float.NaN,true),"Invalid time rejected");
        Need(!PalmAttention.LookingAtPalm(false,Head,Quaternion.identity,Hand,Quaternion.identity,false),"Back/edge of hand rejected");
        Need(!PalmAttention.LookingAt(Head,Quaternion.identity,new Vector3(.3f,0,.4f),false),"Out-of-view wrist rejected");
        Need(!PalmAttention.LookingAt(Head,Quaternion.identity,new Vector3(0,0,-.4f),false),"Behind-head wrist rejected");
        Need(!PalmAttention.LookingAt(Head,Quaternion.identity,Head,false),"Degenerate target rejected");
        var edge=new Vector3(.16f,0,.4f);
        Need(!PalmAttention.LookingAt(Head,Quaternion.identity,edge,false) && PalmAttention.LookingAt(Head,Quaternion.identity,edge,true),"Separate entry/retention bounds");
        int cases=0;
        foreach(bool left in new[]{false,true})
        foreach(float yaw in new[]{-1.5f,-.5f,0f,.5f,1.5f})
        foreach(float depth in new[]{.2f,.4f,.7f}) {
            var q=new Quaternion(0,(float)Math.Sin(yaw/2),0,(float)Math.Cos(yaw/2));
            var hand=new Vector3(left?-.15f:.15f,-.1f,depth);var rotation=Quaternion.identity;
            var oldCenter=hand-rotation*Vector3.forward*.115f+rotation*(left?Vector3.right:Vector3.left)*.035f;
            var center=PalmAttention.WristCenter(left,hand,rotation);
            Need((oldCenter-center).sqrMagnitude<1e-12f,"Existing wrist placement retained");
            var translation=new Vector3(123,17,-42);
            var worldCenter=PalmAttention.WristCenter(left,translation+q*hand,q*rotation);
            Need((worldCenter-(translation+q*center)).sqrMagnitude<1e-9f,"Input/rendered wrist center agree after body transform");
            Need(PalmAttention.LookingAt(Head,Quaternion.identity,center,false)==PalmAttention.LookingAt(translation,q,worldCenter,false),"Gaze invariant across tracking/world spaces");
            cases++;
        }
        float pitch=(float)Math.Atan2(.6,.4);
        var lookDown=new Quaternion((float)Math.Sin(pitch/2),0,0,(float)Math.Cos(pitch/2));
        var low=new Vector3(0,-.6f,.4f);var lowGesture=new AutomapGesture();
        Need(!lowGesture.Step(true,false,0,Head,lookDown,low,lookDown*Face(false)) && lowGesture.Step(true,false,.13f,Head,lookDown,low,lookDown*Face(false)),"No world-up/height gate");
        Console.WriteLine($"PASS gaze toggle/rearm, context/binding changes, movement, palm-facing and invalid guards; {cases} exact wrist-placement/shared-gaze cases. Static policy only.");
    }
}
