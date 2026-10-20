using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using CulticVR.WorldStereoTrial;

namespace CulticVR.OpenXRControllers
{
    public sealed partial class OpenXRControllersPlugin
    {
        private Harmony? _extraHapticHarmony;
        private Harmony? _cinematicHapticHarmony;
        private GameObject? _hapticCutsceneObject;
        private scrCutsceneTrigger? _hapticCutscene;
        private AccessTools.FieldRef<scrCutsceneTrigger,int>? _hapticCutsceneState;
        private struct MeleeContactState { internal scrPlayerControl? Player;internal bool Charged,Hit; }
        [ThreadStatic] private static MeleeContactState _meleeContact;
        private void InitializeExtraHaptics()
        {
            _extraHapticHarmony=new Harmony("culticvr.haptics.contacts");
            try{
                _extraHapticHarmony.Patch(AccessTools.Method(typeof(scrPlayerControl),"bulletHit"),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(MeleeContact)));
                _extraHapticHarmony.Patch(AccessTools.Method(typeof(scrPickup),"OnTriggerEnter"),transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(PickupTranspiler)));
                foreach(var type in new[]{typeof(scrTNT),typeof(scrMolotov),typeof(scrMolomite),typeof(scrTNTTripwire)})
                    _extraHapticHarmony.Patch(AccessTools.Method(type,"OnTriggerEnter"),transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(PickupTranspiler)));
                _extraHapticHarmony.Patch(AccessTools.Method(typeof(scrPlayerControl),"Update"),transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(TemporaryPickupTranspiler)));
                _extraHapticHarmony.Patch(AccessTools.Method(typeof(scrPlayerControl),"Update"),transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(OffhandPickupTranspiler)));
                _extraHapticHarmony.Patch(AccessTools.Method(typeof(scrGrabbable),"Grab"),prefix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(GrabBefore)),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(GrabAfter)));
            }catch(Exception e){_extraHapticHarmony.UnpatchSelf();Logger.LogWarning("Optional contact haptics unavailable: "+e.Message);}
            try{ExplosionHapticCapture.Install(NearbyExplosion,ScriptedExplosion);}
            catch(Exception e){ExplosionHapticCapture.Shutdown();Logger.LogWarning("Optional nearby explosion haptics unavailable: "+e.Message);}
            _cinematicHapticHarmony=new Harmony("culticvr.haptics.cinematic-impact");
            try{
                _hapticCutsceneState=AccessTools.FieldRefAccess<scrCutsceneTrigger,int>("state");
                _cinematicHapticHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrE2M6Cutscene),"breakWindow"),
                    prefix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(WindowImpactBefore)),
                    postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(WindowImpactAfter)));
                _cinematicHapticHarmony.Patch(AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(typeof(scrE2M6Cutscene),"shotCoroutine")),
                    transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(CinematicGunfireTranspiler)));
            }catch(Exception e){_hapticCutsceneState=null;_cinematicHapticHarmony.UnpatchSelf();Logger.LogWarning("Optional window cutscene haptics unavailable: "+e.Message);}
            InitializeHelicopterNearMiss();
        }
        private bool CinematicHapticsActive(scrGameControl g)
        {
            if(g==null||g.gameState!=3||g.hudScript==null||g.hudScript.cutsceneState!=3)return false;
            if(_hapticCutsceneObject!=g.cutsceneTrigger){
                _hapticCutsceneObject=g.cutsceneTrigger;
                _hapticCutscene=_hapticCutsceneObject!=null?_hapticCutsceneObject.GetComponent<scrCutsceneTrigger>():null;
            }
            var p=g.hudScript.playerScript;
            return _hapticCutscene!=null&&_hapticCutsceneState!=null&&_hapticCutsceneState(_hapticCutscene)==1&&_hapticCutscene.cameraState==1&&p!=null&&Local(p,g)&&!p.isDead;
        }
        private static bool WindowImpactAllowed()
        {
            var owner=_hapticOwner;
            return owner!=null&&UnityEngine.XR.XRSettings.isDeviceActive&&Application.isFocused&&SessionFocused&&owner.CinematicHapticsActive(scrGameControl.Instance);
        }
        private static void WindowImpactBefore(scrE2M6Cutscene __instance,out bool __state)
            =>__state=__instance.window!=null&&__instance.window.hp>0&&WindowImpactAllowed();
        private static void WindowImpactAfter(scrE2M6Cutscene __instance,bool __state)
        {
            // Timeline's exact breakWindow signal; only an actual intact-to-broken
            // transition during playback. Skip/load sets the trigger state to 2.
            if(!__state||__instance.window==null||__instance.window.hp>0||!WindowImpactAllowed())return;
            var owner=_hapticOwner!;
            owner.PrioritizeImpact(.25f);
            owner.PulseControllers(false,true,1f,.25f);
            owner.Vest(new VestMotorFrame(VestMotorFrame.All,VestMotorFrame.All,HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),.25f),false);
        }
        private static IEnumerable<CodeInstruction> CinematicGunfireTranspiler(IEnumerable<CodeInstruction> instructions)=>
            HapticEventSelectors.CaptureSpawns(instructions,AccessTools.Method(typeof(OpenXRControllersPlugin),nameof(CinematicGunfire)),1);
        private uint _cinematicGunfireSequence;
        private static void CinematicGunfire(GameObject projectile)
        {
            var owner=_hapticOwner;var p=scrGameControl.Instance?.hudScript?.playerScript;
            if(owner==null||p==null||projectile==null||!WindowImpactAllowed()||!TrySourceSide(p,projectile.transform.position,out var side))return;
            var frame=HapticPatterns.Incoming(IncomingHapticKind.Automatic,side,owner._cinematicGunfireSequence++,1);
            owner.PulseControllers(false,true,frame.Amplitude,frame.Seconds);
            owner.Vest(new VestMotorFrame(frame.Front,frame.Back,frame.Amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),frame.Seconds),false);
        }
        private static void BeginMeleeContact(scrPlayerControl p,float charge)
        {
            _meleeContact=default;var g=scrGameControl.Instance;
            if(_hapticOwner==null||!Eligible(p,g))return;
            var loadout=g.gamePlayers[p.playerID].playerLoadout;
            if(p.weapon<0||p.weapon>=loadout.Count||loadout[p.weapon]==null)return;
            int id=loadout[p.weapon].weaponTableID;
            if(id==0||id==7||id==13||id==16)_meleeContact=new MeleeContactState{Player=p,Charged=charge>20f};
        }
        private static void MeleeContact(scrPlayerControl __instance)
        {
            if(_meleeContact.Player!=__instance||_meleeContact.Hit||_hapticOwner==null||!Eligible(__instance,scrGameControl.Instance))return;
            _meleeContact.Hit=true;
            _hapticOwner.PulseControllers(false,false,_meleeContact.Charged?1f:.55f,_meleeContact.Charged?.16f:.08f);
        }
        private static IEnumerable<CodeInstruction> PickupTranspiler(IEnumerable<CodeInstruction> instructions)=>
            HapticEventSelectors.AfterCall(instructions,AccessTools.Method(typeof(scrGameControl),"playSystemSound"),AccessTools.Method(typeof(OpenXRControllersPlugin),nameof(PickupAccepted)),1,1);
        private static IEnumerable<CodeInstruction> TemporaryPickupTranspiler(IEnumerable<CodeInstruction> instructions)=>
            HapticEventSelectors.AfterCall(instructions,AccessTools.Method(typeof(scrPlayerControl),"equipTempWeapon"),AccessTools.Method(typeof(OpenXRControllersPlugin),nameof(TemporaryPickupAccepted)),0,1);
        private static IEnumerable<CodeInstruction> OffhandPickupTranspiler(IEnumerable<CodeInstruction> instructions)=>
            HapticEventSelectors.OffhandPickups(instructions,AccessTools.Method(typeof(OpenXRControllersPlugin),nameof(InteractionPickupAccepted)));
        private static void InteractionPickupAccepted(scrPlayerControl player)
        {if(Eligible(player,scrGameControl.Instance))PickupPulse();}
        private static void TemporaryPickupAccepted(scrPlayerControl p)
        {if(p.tempWeapon!=null&&Eligible(p,scrGameControl.Instance))PickupPulse();}
        private static void PickupAccepted(Collider other)
        {
            var g=scrGameControl.Instance;var p=g?.hudScript?.playerScript;
            if(p!=null&&other!=null&&other.gameObject==p.gameObject&&Eligible(p,g!))PickupPulse();
        }
        private static void GrabBefore(NetworkVariable<bool> ___isGrabbed,out bool __state)=>__state=___isGrabbed.Value;
        private static void GrabAfter(scrPlayerControl grabber,NetworkVariable<bool> ___isGrabbed,bool __state)
        {if(!__state&&___isGrabbed.Value&&Eligible(grabber,scrGameControl.Instance))PickupPulse();}
        // Keep pickups localized and vest-only. The former 55ms/18% frame
        // reached Player but was not felt; 140ms was perceptible in vest trials.
        private static void PickupPulse()=>_hapticOwner?.Vest(new VestMotorFrame((1u<<9)|(1u<<10),0,.4f*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),.14f),false);
        private static void WorldShakeAccepted(scrPlayerControl player,float amount)
        {
            var owner=_hapticOwner;
            bool vest=VrSettings.VestHaptics.Value&&VrSettings.VestHapticStrength.Value>0;
            bool controllers=VrSettings.ControllerHaptics.Value&&VrSettings.MotionControls&&VrSettings.ControllerHapticStrength.Value>0;
            if(owner==null||(!vest&&!controllers)||!Eligible(player,scrGameControl.Instance))return;
            if(Time.realtimeSinceStartupAsDouble<owner._rumblePriorityUntil)return;
            owner._worldRumble.Submit(amount,Time.realtimeSinceStartupAsDouble);
        }
        private static void WorldImpact()
        {
            var owner=_hapticOwner;
            var g=scrGameControl.Instance;var p=g?.hudScript?.playerScript;
            if(owner==null||p==null||!Eligible(p,g!))return;
            owner.PrioritizeImpact(.25f);
            owner.PulseControllers(false,true,1f,.25f);
            owner.Vest(new VestMotorFrame(VestMotorFrame.All,VestMotorFrame.All,HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),.25f),false);
        }
        private static void NearbyExplosion(Vector3 position)
            =>ExplosionFeedback(position);
        private static void ScriptedExplosion(Vector3 position)
            =>ExplosionFeedback(position);
        private static void ExplosionFeedback(Vector3 position)
        {
            var owner=_hapticOwner;var g=scrGameControl.Instance;var p=g?.hudScript?.playerScript;
            if(owner==null||p==null||!Eligible(p,g!)||!TrySourceSide(p,position,out var side))return;
            owner.PrioritizeImpact(.25f);
            bool back=side==ImpactSide.Back;
            owner.PulseControllers(false,true,1f,.25f);
            owner.Vest(new VestMotorFrame(back?0:VestMotorFrame.All,back?VestMotorFrame.All:0,HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),.25f),false);
        }
        private static bool TrySourceSide(scrPlayerControl p,Vector3 position,out ImpactSide side)
        {
            side=ImpactSide.Unknown;
            if(p.playerCameraComponent==null||!VrHandPose.TryHead(out _,out var head))return false;
            var camera=p.playerCameraComponent.transform;
            var basis=Quaternion.Euler(0,camera.eulerAngles.y,0)*WorldStereoTrialPlugin.TrackingYawCompensation;
            var origin=camera.position+basis*WorldStereoTrialPlugin.TrackingPositionCompensation;
            var delta=position-origin;
            if(float.IsNaN(delta.x)||float.IsInfinity(delta.x)||float.IsNaN(delta.y)||float.IsInfinity(delta.y)||float.IsNaN(delta.z)||float.IsInfinity(delta.z))return false;
            var forward=(basis*head)*Vector3.forward;
            side=HapticPatterns.Side(forward.x,forward.z,delta.x,delta.z);
            return true;
        }
    }
}
