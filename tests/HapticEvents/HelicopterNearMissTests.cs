using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.XR;
internal static class HelicopterNearMissTests
{
    internal static void Run(Action<bool,string> check,OpenXRControllersPlugin owner,scrPlayerControl p,scrGameControl g)
    {
        g.gameState=0;g.hudScript.cutsceneState=0;g.hudScript.playerScript=p;g.gamePlayers[0].playerHP=100;owner.Tick();owner.Clear();
        Time.realtimeSinceStartupAsDouble+=10;
        var local=new Collider{gameObject=p.gameObject};var wall=new Collider();
        scrBullet Bullet(IncomingHapticKind kind=IncomingHapticKind.HelicopterAutomatic){
            var bullet=new scrBullet();bullet.hitInfo.hitBy=new GameObject();bullet.hitInfo.hitFrom=new(0,0,-1);HapticProducerTags.Tag(bullet.hitInfo,new HapticAttackGroup(1000,kind));return bullet;
        }
        void Observe(scrBullet b)=>owner.Whiz(b,local,new(0,0,-1));
        var b=Bullet();Observe(b);
        check(owner.VestOutput.Frames.Count==0,"Native whiz alone does not misclassify a future direct hit as a miss");
        owner.BulletEnd(b,wall);
        var frame=owner.VestOutput.Frames.Single();
        check(frame.Front!=0&&frame.Back==0&&frame.Amplitude==.36f&&frame.Seconds==.14f,"Finished helicopter whiz sends lighter directional front vest pulse");
        check(owner.Output.Pulses.Count==0,"Near miss is distinct from actual injury controller feedback");
        owner.Clear();Time.realtimeSinceStartupAsDouble+=1;
        var moving=Bullet();Observe(moving);moving.transform.position=new(0,0,1);owner.BulletMoved(moving);
        check(owner.VestOutput.Frames.Count==0,"Approaching bullet cannot trigger before native damage checks pass the body");
        moving.transform.position=new(0,0,-.4f);owner.BulletMoved(moving);
        check(owner.VestOutput.Frames.Count==0,"Bullet still overlapping longitudinal body bounds waits");
        moving.transform.position=new(0,0,-.6f);owner.BulletMoved(moving);
        check(owner.VestOutput.Frames.Count==1,"Actual flight beyond farthest live collider bound triggers promptly without expiry wait");
        owner.Clear();owner.BulletEnd(moving,wall);
        check(owner.VestOutput.Frames.Count==0,"Passed bullet terminal callback cannot duplicate near-miss output");
        Time.realtimeSinceStartupAsDouble+=1;moving=Bullet();Observe(moving);local.bounds=new(){center=new(0,0,-2),extents=new(.5f,1,.5f)};
        moving.transform.position=new(0,0,-.6f);owner.BulletMoved(moving);
        check(owner.VestOutput.Frames.Count==0,"Near miss uses live moving/crouching collider bounds, not stale captured geometry");
        moving.transform.position=new(0,0,-3);owner.BulletMoved(moving);
        check(owner.VestOutput.Frames.Count==1,"Moved body's far extent correctly determines completed pass");
        owner.Clear();local.bounds=new(){center=new(0,0,0),extents=new(.5f,1,.5f)};
        owner.Clear();Time.realtimeSinceStartupAsDouble+=1;owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Each bullet miss consumed once");
        b=Bullet();Observe(b);owner.Damage(p,b.hitInfo,()=>g.gamePlayers[0].playerHP-=2);owner.BulletEnd(b,local);
        check(owner.VestOutput.Frames.Count==1&&owner.VestOutput.Frames[0].Amplitude==.9f,"Direct injury has strong hit only, no duplicate weak miss");
        owner.Clear();b=Bullet();Observe(b);owner.BulletEnd(b,local);
        check(owner.VestOutput.Frames.Count==0,"Rejected local contact does not become near miss");
        b=Bullet();owner.Whiz(b,wall,new(0,0,-1));owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Whiz near another collider does not count as local-player pass");
        b=Bullet(IncomingHapticKind.Automatic);Observe(b);owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Other automatic guns untouched");
        b=Bullet();owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Far bullet without native local whiz silent");
        b=Bullet();Observe(b);b.isHeld=true;owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Held shield bullet cannot become a miss");
        b=Bullet();Observe(b);b.hitInfo.hitBy=null;owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Caught/redirected bullet with removed owner silent");
        Time.realtimeSinceStartupAsDouble+=1;VrHandPose.Head=Quaternion.Euler(0,180,0);b=Bullet();Observe(b);owner.BulletEnd(b);
        check(owner.VestOutput.Frames.Single().Back!=0,"Expiry also reports miss; direction follows rendered view");
        owner.Clear();VrHandPose.Head=Quaternion.identity;
        for(int i=0;i<20;i++){b=Bullet();Observe(b);owner.BulletEnd(b,wall);}
        check(owner.VestOutput.Frames.Count==0,"Same-time bullet storm bounded to ten near-miss pulses per second");
        Time.realtimeSinceStartupAsDouble+=.11;b=Bullet();Observe(b);owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==1,"Next real miss permitted after burst interval");owner.Clear();
        Time.realtimeSinceStartupAsDouble+=1;b=Bullet();Observe(b);owner.SceneChanged();owner.Tick();owner.BulletEnd(b,wall);
        check(owner.VestOutput.Frames.Count==0,"Scene/focus suspension invalidates pending passes");
        foreach(Action disable in new Action[]{()=>g.gameState=1,()=>Application.isFocused=false,()=>XRSettings.isDeviceActive=false,()=>p.isDead=true,()=>g.connectionStatus=scrGameControl.ConnectionStatus.Online,()=>VrSettings.VestHaptics.Value=false}){
            b=Bullet();Observe(b);disable();owner.BulletEnd(b,wall);
            check(owner.VestOutput.Frames.Count==0,"Completed miss respects pause/focus/death/local/vest guards");
            g.gameState=0;Application.isFocused=true;XRSettings.isDeviceActive=true;p.isDead=false;g.connectionStatus=scrGameControl.ConnectionStatus.Offline;VrSettings.VestHaptics.Value=true;owner.Tick();
        }
        Time.realtimeSinceStartupAsDouble+=1;VrSettings.VestHapticStrength.Value=.9f;b=Bullet();Observe(b);owner.BulletEnd(b,wall);
        check(Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.324f)<.00001f,"Near miss honors independent vest slider");owner.Clear();VrSettings.VestHapticStrength.Value=1;
        for(int i=0;i<1000;i++)owner.BulletEnd(b,wall);long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)owner.BulletEnd(b,wall);
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Non-candidate bullet completion adds no allocations");
        for(int i=0;i<1000;i++)owner.BulletMoved(b);bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)owner.BulletMoved(b);
        check(GC.GetAllocatedBytesForCurrentThread()==bytes,"Idle native movement observer adds no allocations");
    }
}
