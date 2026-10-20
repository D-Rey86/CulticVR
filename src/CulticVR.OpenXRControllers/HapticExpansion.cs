using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using CulticVR.WorldStereoTrial;
namespace CulticVR.OpenXRControllers
{
    public sealed partial class OpenXRControllersPlugin
    {
        private Harmony? _expansionHarmony;
        private bool _expandedEventsReady;
        private double _rumblePriorityUntil,_nextHealingPulse;
        private struct LandingEvent {internal scrPlayerControl? Player;internal float Drop;internal int Damage;internal bool Rolled;}
        private LandingEvent _landing;
        private void InitializeExpandedHaptics()
        {
            _expansionHarmony=new Harmony("culticvr.haptics.expanded-events");
            try{
                _expansionHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrCutsceneTrigger),"addShake"),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(CinematicShakeAccepted)));
                _expansionHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrPlayerControl),"castLanding"),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(LandingAccepted)));
                _expansionHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrPlayerControl),"tryFallDamage"),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(FallResolved)));
                _expansionHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrPlayerControl),"Update"),transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(HealingTranspiler)));
                _expansionHarmony.Patch(AccessTools.DeclaredMethod(typeof(scrTriggerPlatformBreak),"OnCollisionEnter"),
                    prefix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(PlatformBefore)),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(PlatformAfter)));
                _expandedEventsReady=true;
            }catch(Exception e){_expansionHarmony.UnpatchSelf();Logger.LogWarning("Optional landing/shield/healing/cinematic haptics unavailable: "+e.Message);}
            try{ExpandedWorldRumbleHooks.Install(WindowImpactAllowed);}
            catch(Exception e){ExpandedWorldRumbleHooks.Shutdown();Logger.LogWarning("Optional cinematic/boss rumble unavailable: "+e.Message);}
        }
        private void PrioritizeImpact(float seconds)
        {
            _worldRumble.Reset();
            double now=Time.realtimeSinceStartupAsDouble;
            if(!double.IsNaN(now)&&!double.IsInfinity(now)&&!float.IsNaN(seconds)&&!float.IsInfinity(seconds)&&seconds>0)
                _rumblePriorityUntil=Math.Max(_rumblePriorityUntil,now+seconds);
        }
        private static void CinematicShakeAccepted(scrCutsceneTrigger __instance,float shakeAmt)
        {
            var owner=_hapticOwner;var g=scrGameControl.Instance;
            if(owner==null||!WindowImpactAllowed()||owner._hapticCutscene!=__instance)return;
            var p=g.hudScript.playerScript;
            if(p!=null)WorldShakeAccepted(p,shakeAmt); // Native callback uses ignoreDistance=true.
        }
        private static void LandingAccepted(scrPlayerControl __instance,float ___fallDist)
        {
            var owner=_hapticOwner;var g=scrGameControl.Instance;
            if(owner==null||!GameplayHapticsActive(g)||!Eligible(__instance,g))return;
            float drop=___fallDist-__instance.transform.position.y;
            if(float.IsNaN(drop)||float.IsInfinity(drop)||drop<2)return;
            if(owner._landing.Player!=__instance)owner._landing=default;
            owner._landing.Player=__instance;owner._landing.Drop=Math.Max(owner._landing.Drop,drop);
        }
        private static void FallResolved(scrPlayerControl __instance,bool ___isSliding)
        {var owner=_hapticOwner;if(owner!=null&&owner._landing.Player==__instance)owner._landing.Rolled=___isSliding;}
        private bool ExpansionDamageAccepted(scrPlayerControl player,int previousShield,bool fall,int loss)
        {
            if(!_expandedEventsReady)return false;
            if(previousShield>0&&player.shieldHP<previousShield){
                bool broken=player.shieldHP<=0;
                PulseControllers(!VrSettings.LeftWeapon,false,broken?.9f:.55f,broken?.14f:.09f);
            }
            if(!fall||loss<=0)return false;
            if(_landing.Player!=player)_landing=default;
            _landing.Player=player;_landing.Damage+=loss;
            if(player.isDead){
                // Native takeDamage has already switched gameState to 3. Preserve
                // the baseline's immediate accepted-lethal-impact behavior.
                var frame=LandingFrame(_landing);_landing=default;
                PrioritizeImpact(frame.Seconds);PulseControllers(false,true,frame.Amplitude,frame.Seconds);
                Vest(new VestMotorFrame(frame.Front,frame.Back,frame.Amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),frame.Seconds),false);
            }
            return true; // Resolve one landing/injury after native roll and fall handling.
        }
        private static VestMotorFrame LandingFrame(LandingEvent landing)
        {
            float severity=Math.Min(1,Math.Max(0,(landing.Drop-2)/6));
            if(landing.Damage>0)severity=Math.Max(severity,Math.Min(1,landing.Damage/40f));
            float amplitude=(.35f+.5f*severity)*(landing.Rolled?.7f:1);
            const uint lowerEight=0xffu<<12;
            return new VestMotorFrame(lowerEight,lowerEight,amplitude,.12f+.06f*severity);
        }
        private void DispatchLanding(scrGameControl? g,bool controllers,double now)
        {
            var landing=_landing;_landing=default;var p=landing.Player;
            if(p==null||g==null)return;
            if(!Eligible(p,g))return;
            if(landing.Damage==0&&now<_rumblePriorityUntil)return; // One body jolt for a simultaneous platform/other impact.
            var frame=LandingFrame(landing);
            PrioritizeImpact(frame.Seconds);
            if(controllers){
                float hand=frame.Amplitude*HapticPatterns.Strength(VrSettings.ControllerHapticStrength.Value);
                _hapticTransport.PulseIfIdle(false,hand,frame.Seconds,now);_hapticTransport.PulseIfIdle(true,hand,frame.Seconds,now);
            }
            Vest(new VestMotorFrame(frame.Front,frame.Back,frame.Amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),frame.Seconds),false);
        }
        private static IEnumerable<CodeInstruction> HealingTranspiler(IEnumerable<CodeInstruction> source)=>ExpansionEventSelectors.Healing(source,AccessTools.DeclaredMethod(typeof(OpenXRControllersPlugin),nameof(HealingAccepted)));
        private static void HealingAccepted(scrPlayerControl player,int amount)
        {
            var owner=_hapticOwner;
            if(owner==null||amount<=0||!VrSettings.VestHaptics.Value||!Eligible(player,scrGameControl.Instance))return;
            float strength=HapticPatterns.Strength(VrSettings.VestHapticStrength.Value);
            if(strength<=0)return;
            double now=Time.realtimeSinceStartupAsDouble;
            if(double.IsNaN(now)||double.IsInfinity(now)||now<owner._nextHealingPulse)return;
            owner._nextHealingPulse=now+.75;
            owner.Vest(new VestMotorFrame((1u<<9)|(1u<<10),0,.3f*strength,.14f),false);
        }
        private static void PlatformBefore(scrTriggerPlatformBreak __instance,Collision collision,out bool __state)
        {
            var g=scrGameControl.Instance;var p=g?.hudScript?.playerScript;
            __state=__instance.state==0&&p!=null&&collision.collider!=null&&collision.collider.gameObject==p.gameObject&&Eligible(p,g!);
        }
        private static void PlatformAfter(scrTriggerPlatformBreak __instance,bool __state)
        {
            var owner=_hapticOwner;var g=scrGameControl.Instance;var p=g?.hudScript?.playerScript;
            if(!__state||__instance.state!=1||owner==null||p==null||!Eligible(p,g!))return;
            owner.PrioritizeImpact(.14f);owner.PulseControllers(false,true,.65f,.14f);
            const uint lowerEight=0xffu<<12;
            owner.Vest(new VestMotorFrame(lowerEight,lowerEight,.6f*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),.14f),false);
        }
        private void ResetExpandedHaptics(){_landing=default;_rumblePriorityUntil=_nextHealingPulse=0;}
    }
}
