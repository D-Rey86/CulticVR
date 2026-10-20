using System.Reflection;
using CulticVR.OpenXRControllers;
using UnityEngine;
internal static class WorldCaptureTests
{
    internal static void Run(Action<bool,string> check)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var type=typeof(ExplosionHapticCapture);
        object? Call(string name,params object?[] args)=>type.GetMethod(name,flags)!.Invoke(null,args);
        int notifications=0;
        type.GetField("_notify",flags)!.SetValue(null,(Action<Vector3>)(_=>notifications++));
        ExplosionHapticCapture.Enabled=true;
        void Update(scrExplosion e,bool before,bool after,bool hit=false){object?[] args={e,before,null};Call("ExplosionBefore",args);if(hit)ExplosionHapticCapture.MarkPlayerDamage();Call("ExplosionAfter",e,after,args[2]);Call("ExplosionEnd",args[2]);}
        var lone=new scrExplosion();Update(lone,false,false);check(notifications==0,"Delayed explosion silent until detonation");
        Update(lone,false,true);Update(lone,true,true);check(notifications==1,"One notification on exact explosion transition");
        Update(new scrExplosion(),false,true,true);check(notifications==1,"Direct explosion damage suppresses proximity");
        foreach(bool hitBeforeSpawn in new[]{false,true}){
            var source=new object();var e=new scrExplosion();object?[] args={source,null};Call("Before",args);
            if(hitBeforeSpawn)ExplosionHapticCapture.MarkPlayerDamage();
            Call("Capture",new GameObject{Component=e});
            if(!hitBeforeSpawn)ExplosionHapticCapture.MarkPlayerDamage();
            Call("End",args[1]);Update(e,false,true);
            check(notifications==1,"Parent damage before/after visual spawn suppresses duplicate");
        }
        var outer=new object();object?[] parent={outer,null};Call("Before",parent);
        object?[] child={new object(),null};Call("Before",child);ExplosionHapticCapture.MarkPlayerDamage();Call("End",child[1]);
        var independent=new scrExplosion();Call("Capture",new GameObject{Component=independent});Call("End",parent[1]);Update(independent,false,true);
        check(notifications==2,"Different chained source does not suppress independent blast");
        object?[] a={outer,null};Call("Before",a);object?[] b={outer,null};Call("Before",b);ExplosionHapticCapture.MarkPlayerDamage();Call("End",b[1]);
        var nested=new scrExplosion();Call("Capture",new GameObject{Component=nested});Call("End",a[1]);Update(nested,false,true);check(notifications==2,"Same-source nested damage propagates");
        int scripted=0;Vector3 origin=default;
        type.GetField("_scriptedNotify",flags)!.SetValue(null,(Action<Vector3>)(v=>{scripted++;origin=v;}));
        var first=new scrExplosion{transform=new Transform{position=new(1,2,1000)}};
        var second=new scrExplosion();
        ExplosionHapticCapture.ScriptedDetonation(new GameObject{Children=new object[]{first,second}});
        check(scripted==1&&origin.z==1000,"One scripted blast from actual child explosion origin, independent of distance");
        Update(first,false,true);Update(second,false,true);
        check(notifications==2,"All child visual explosions suppressed after dedicated scripted feedback");
        Update(new scrExplosion(),false,true);check(notifications==3,"Unrelated explosion still reports proximity");
        ExplosionHapticCapture.ScriptedDetonation(new GameObject{transform=new Transform{position=new(2,3,4)}});
        check(scripted==2&&origin.z==4,"Particle-only scripted blast uses exact effect origin");
        ExplosionHapticCapture.Enabled=false;Update(new scrExplosion(),false,true);ExplosionHapticCapture.ScriptedDetonation(new GameObject());
        check(notifications==3&&scripted==2,"Disabled capture silent");
        type.GetField("_scriptedNotify",flags)!.SetValue(null,null);
        type.GetField("_notify",flags)!.SetValue(null,null);
    }
}
