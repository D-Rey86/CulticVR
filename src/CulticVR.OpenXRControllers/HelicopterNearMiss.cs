using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using CulticVR.WorldStereoTrial;

namespace CulticVR.OpenXRControllers
{
    public sealed partial class OpenXRControllersPlugin
    {
        private Harmony? _helicopterNearMissHarmony;
        private sealed class HelicopterPass
        {
            internal scrPlayerControl Player=null!;
            internal Vector3 Travel;
            internal Collider Collider=null!;
            internal uint Generation;
        }
        private readonly ConditionalWeakTable<scrBullet,HelicopterPass> _helicopterPasses=new ConditionalWeakTable<scrBullet,HelicopterPass>();
        private uint _helicopterNearMissGeneration,_helicopterNearMissSequence;
        private double _nextHelicopterNearMiss;
        private int _helicopterNearMissPending;
        private void InitializeHelicopterNearMiss()
        {
            _helicopterNearMissHarmony=new Harmony("culticvr.haptics.helicopter-near-miss");
            try{
                _helicopterNearMissHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrBullet),"Update"),
                    transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(HelicopterBulletTranspiler)));
            }catch(Exception e){_helicopterNearMissHarmony.UnpatchSelf();Logger.LogWarning("Optional helicopter near-miss haptics unavailable: "+e.Message);}
        }
        private static IEnumerable<CodeInstruction> HelicopterBulletTranspiler(IEnumerable<CodeInstruction> source,MethodBase __originalMethod)=>
            HelicopterNearMissPatch.Rewrite(source,__originalMethod,
                AccessTools.DeclaredMethod(typeof(OpenXRControllersPlugin),nameof(HelicopterWhiz)),
                AccessTools.DeclaredMethod(typeof(OpenXRControllersPlugin),nameof(HelicopterBulletContact)),
                AccessTools.DeclaredMethod(typeof(OpenXRControllersPlugin),nameof(HelicopterBulletExpired)),
                AccessTools.DeclaredMethod(typeof(OpenXRControllersPlugin),nameof(HelicopterBulletMoved)));
        private static void HelicopterWhiz(scrBullet bullet,Collider nearby,Vector3 travel)
        {
            var owner=_hapticOwner;var g=scrGameControl.Instance;var p=g?.hudScript?.playerScript;
            if(owner==null||p==null||nearby==null||nearby.gameObject!=p.gameObject||!VrSettings.VestHaptics.Value||
                !Eligible(p,g!)||bullet.isHeld||bullet.hitInfo==null||bullet.hitInfo.hitBy==null||
                HapticProducerTags.Get(bullet.hitInfo)?.Kind!=IncomingHapticKind.HelicopterAutomatic||owner._helicopterPasses.TryGetValue(bullet,out _))return;
            // Native whiz already supplies the nearby collider. Store only actual
            // helicopter/local-player passes; no new overlap or trajectory query.
            owner._helicopterPasses.Add(bullet,new HelicopterPass{Player=p,Collider=nearby,Travel=travel,Generation=owner._helicopterNearMissGeneration});
            owner._helicopterNearMissPending++;
        }
        private static void HelicopterBulletMoved(scrBullet bullet)
        {
            var owner=_hapticOwner;
            if(owner==null||owner._helicopterNearMissPending==0||!owner._helicopterPasses.TryGetValue(bullet,out var pass)||
                pass.Generation!=owner._helicopterNearMissGeneration||pass.Collider==null||bullet.isHeld)return;
            // Native damage raycast has already completed before this movement.
            // Once beyond the farthest point of the live collider's world AABB
            // along travel, this straight-flight segment has safely passed it.
            // Bounds are geometry metadata, not another physics scene query.
            var bounds=pass.Collider.bounds;var delta=bullet.transform.position-bounds.center;var d=pass.Travel;var e=bounds.extents;
            double projected=(double)delta.x*d.x+(double)delta.y*d.y+(double)delta.z*d.z;
            double far=Math.Abs((double)d.x)*e.x+Math.Abs((double)d.y)*e.y+Math.Abs((double)d.z)*e.z;
            if(projected>far)HelicopterBulletFinished(bullet,null);
        }
        private static void HelicopterBulletContact(scrBullet bullet,RaycastHit hit)=>HelicopterBulletFinished(bullet,hit.collider);
        private static void HelicopterBulletExpired(scrBullet bullet)=>HelicopterBulletFinished(bullet,null);
        private static void HelicopterBulletFinished(scrBullet bullet,Collider? struck)
        {
            var owner=_hapticOwner;
            if(owner==null||!owner._helicopterPasses.TryGetValue(bullet,out var pass))return;
            owner._helicopterPasses.Remove(bullet);
            if(pass.Generation==owner._helicopterNearMissGeneration)owner._helicopterNearMissPending--;
            var p=pass.Player;var g=scrGameControl.Instance;
            if(pass.Generation!=owner._helicopterNearMissGeneration||p==null||!Eligible(p,g)||
                bullet.isHeld||bullet.hitInfo==null||bullet.hitInfo.hitBy==null||
                (struck!=null&&struck.gameObject==p.gameObject)||!VrSettings.VestHaptics.Value||p.playerCameraComponent==null)return;
            // A whiz can precede a direct hit. Confirm native terminal contact,
            // expiry or movement beyond the body; consume the pass exactly once.
            double now=Time.realtimeSinceStartupAsDouble;
            if(double.IsNaN(now)||double.IsInfinity(now)||now<owner._nextHelicopterNearMiss||!VrHandPose.TryHead(out _,out var head))return;
            var horizontal=Quaternion.Euler(0,p.playerCameraComponent.transform.eulerAngles.y,0);
            var facing=(horizontal*WorldStereoTrialPlugin.TrackingYawCompensation*head)*Vector3.forward;
            var source=-pass.Travel;
            var side=HapticPatterns.Side(facing.x,facing.z,source.x,source.z);
            if(side==ImpactSide.Unknown)return;
            owner.Vest(HapticPatterns.HelicopterNearMiss(side,owner._helicopterNearMissSequence++,VrSettings.VestHapticStrength.Value),false);
            owner._nextHelicopterNearMiss=now+.1; // Bound bursts without fabricating continuing fire.
        }
    }

}

