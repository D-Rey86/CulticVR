using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using CulticVR.WorldStereoTrial;

namespace CulticVR.OpenXRControllers
{
    public sealed partial class OpenXRControllersPlugin
    {
        private static OpenXRControllersPlugin? _hapticOwner;
        private readonly ControllerHapticTransport _hapticTransport=new ControllerHapticTransport();
        private readonly VestHapticClient _vestHaptics=new VestHapticClient();
        private readonly HapticHeartbeat _heartbeat=new HapticHeartbeat();
        private readonly HapticWorldRumble _worldRumble=new HapticWorldRumble();
        private Harmony? _hapticHarmony,_producerHarmony,_temporaryHapticHarmony,_additionalProducerHarmony;
        private bool _vestEnabled,_allowed;
        private uint _automaticHit;
        private uint _burnHit,_fragmentHit;
        private bool _flamePhase;
        [ThreadStatic] private static int _regularFireDepth;
        private static bool SessionFocused=>_hapticOwner?._hapticTransport.Focused==true;
        internal static void HapticSessionBegin(ulong session)=>_hapticOwner?._hapticTransport.Begin(session);
        internal static void HapticSessionEnd()
        {
            var owner=_hapticOwner;if(owner==null)return;
            owner._hapticTransport.End();owner.Suspend();
        }
        internal static void HapticSessionState(int next)
        {
            var owner=_hapticOwner;if(owner==null)return;
            owner._hapticTransport.State(next);
            if(next!=5&&owner._allowed)owner.Suspend();
        }
        private void InitializeHaptics()
        {
            _hapticOwner=this;
            SceneManager.activeSceneChanged+=HapticSceneChanged;
            _hapticHarmony=new Harmony("culticvr.haptics.events");
            try{
                PatchEvent("fireEvent",nameof(ShotPrefix),nameof(ShotPostfix),nameof(ShotFinalizer));
                PatchEvent("altFireEvent",nameof(AltShotPrefix),nameof(ShotPostfix),nameof(ShotFinalizer));
                PatchEvent("takeDamage",nameof(DamagePrefix),nameof(DamagePostfix));
            }catch(Exception e){_hapticHarmony.UnpatchSelf();Logger.LogWarning("Optional haptic event hooks unavailable: "+e.Message);}
            _temporaryHapticHarmony=new Harmony("culticvr.haptics.temporary");
            try{
                _temporaryHapticHarmony.Patch(AccessTools.Method(typeof(scrPlayerControl),"Update"),
                    transpiler:new HarmonyMethod(typeof(OpenXRControllersPlugin),nameof(TemporaryFireTranspiler)));
            }catch(Exception e){_temporaryHapticHarmony.UnpatchSelf();Logger.LogWarning("Optional disposable recoil unavailable: "+e.Message);}
            _producerHarmony=new Harmony("culticvr.haptics.producers");
            try{HapticProducerTags.Install(_producerHarmony);}
            catch(Exception e){_producerHarmony.UnpatchSelf();HapticProducerTags.Disable();Logger.LogWarning("Optional directional attack classification unavailable: "+e.Message);}
            _additionalProducerHarmony=new Harmony("culticvr.haptics.additional-producers");
            try{AdditionalHapticProducers.Install(_additionalProducerHarmony);}
            catch(Exception e){_additionalProducerHarmony.UnpatchSelf();Logger.LogWarning("Optional additional attack classification unavailable: "+e.Message);}
            InitializeExtraHaptics();
            try{WorldRumbleHooks.Install(WorldShakeAccepted,WorldImpact,ExplosionHapticCapture.ScriptedDetonation);}
            catch(Exception e){WorldRumbleHooks.Shutdown();Logger.LogWarning("Optional world rumble unavailable: "+e.Message);}
            InitializeExpandedHaptics();
            try{ElevatorRideHooks.Install(WorldShakeAccepted);}
            catch(Exception e){ElevatorRideHooks.Shutdown();Logger.LogWarning("Optional elevator ride haptics unavailable: "+e.Message);}
        }
        private void PatchEvent(string target,string before,string after,string? finalizer=null)=>_hapticHarmony!.Patch(AccessTools.Method(typeof(scrPlayerControl),target),
            prefix:new HarmonyMethod(typeof(OpenXRControllersPlugin),before),postfix:new HarmonyMethod(typeof(OpenXRControllersPlugin),after),
            finalizer:finalizer==null?null:new HarmonyMethod(typeof(OpenXRControllersPlugin),finalizer));
        private static bool Local(scrPlayerControl p,scrGameControl g)=>p!=null&&g!=null&&g.connectionStatus==scrGameControl.ConnectionStatus.Offline&&
            p.playerID==g.localPlayerID&&p.playerHUD==g.hudScript;
        private static bool GameplayHapticsActive(scrGameControl g)=>g!=null&&g.gameState==0&&g.hudScript!=null&&
            (g.hudScript.cutsceneState==0||g.hudScript.cutsceneState==3);
        private static bool Eligible(scrPlayerControl p,scrGameControl g)=>Local(p,g)&&XRSettings.isDeviceActive&&Application.isFocused&&SessionFocused&&
            (GameplayHapticsActive(g)||_hapticOwner?.CinematicHapticsActive(g)==true)&&!p.isDead;
        private struct ShotState { internal bool Valid,Left,Flame;internal float Amplitude,Seconds;internal int PriorDepth;internal MeleeContactState PriorMelee; }
        private static void ShotPrefix(scrPlayerControl __instance,float ___fireCharge,out ShotState __state)
        { __state=ShotSnapshot(__instance,false);__state.PriorDepth=_regularFireDepth++;__state.PriorMelee=_meleeContact;BeginMeleeContact(__instance,___fireCharge); }
        private static void AltShotPrefix(scrPlayerControl __instance,out ShotState __state)
        { __state=ShotSnapshot(__instance,true);__state.PriorDepth=_regularFireDepth++;__state.PriorMelee=_meleeContact;_meleeContact=default; }
        private static void ShotFinalizer(ShotState __state){_regularFireDepth=__state.PriorDepth;_meleeContact=__state.PriorMelee;}
        private static ShotState ShotSnapshot(scrPlayerControl p,bool alternate)
        {
            var g=scrGameControl.Instance;if(_hapticOwner==null||!Eligible(p,g))return default;
            var loadout=g.gamePlayers[p.playerID].playerLoadout;
            if(p.weapon<0||p.weapon>=loadout.Count||loadout[p.weapon]==null)return default;
            int weapon=loadout[p.weapon].weaponTableID;
            if(weapon==10&&g.gamePlayers[p.playerID].ammo[8]<=0&&(alternate||!g.diffInfiniteAmmo))return default;
            float amplitude=HapticPatterns.Recoil(weapon,alternate,out float seconds);
            return new ShotState{Valid=amplitude>0,Left=VrSettings.LeftWeapon,Flame=weapon==10&&!alternate,Amplitude=amplitude,Seconds=seconds};
        }
        private static void ShotPostfix(scrPlayerControl __instance,ShotState __state)
        {
            var owner=_hapticOwner;if(!__state.Valid||owner==null||!Eligible(__instance,scrGameControl.Instance))return;
            float amplitude=__state.Amplitude;
            if(__state.Flame){owner._flamePhase=!owner._flamePhase;if(owner._flamePhase)amplitude*=.55f;}
            owner.Recoil(__state.Left,amplitude,__state.Seconds);
        }
        private static IEnumerable<CodeInstruction> TemporaryFireTranspiler(IEnumerable<CodeInstruction> instructions)=>
            TemporaryHapticPatch.Insert(instructions,
                AccessTools.Field(typeof(scrPlayerControl),"tempWeapon"),AccessTools.Field(typeof(TempWeapon),"tWepType"),
                AccessTools.Field(typeof(scrPlayerControl),"playerHUD"),AccessTools.Field(typeof(TempWeapon),"ammo"),
                AccessTools.Field(typeof(TempWeapon),"ammoIcon"),AccessTools.Method(typeof(scrPlayerHUD),"refreshAmmo"),
                AccessTools.Method(typeof(OpenXRControllersPlugin),nameof(TemporaryShotCompleted)));
        private static void TemporaryShotCompleted(scrPlayerControl __instance)
        {
            var owner=_hapticOwner;var temp=__instance.tempWeapon;
            if(owner==null||temp==null||_regularFireDepth!=0||!Eligible(__instance,scrGameControl.Instance))return;
            if(temp.tWepType==TempWeapon.TWepType.BBGun)owner.Recoil(VrSettings.LeftWeapon,.15f,.04f);
            else if(temp.tWepType==TempWeapon.TWepType.LP42)owner.Recoil(VrSettings.LeftWeapon,.5f,.09f);
            else if(temp.tWepType==TempWeapon.TWepType.MG)owner.HeavyRecoil(VrSettings.LeftWeapon,.85f,.06f);
            else if(temp.tWepType==TempWeapon.TWepType.Bazooka)owner.HeavyRecoil(VrSettings.LeftWeapon,1f,.2f);
        }
        private void HeavyRecoil(bool left,float amplitude,float seconds)
        {
            PulseControllers(left,false,amplitude,seconds);
            Vest(new VestMotorFrame(VestMotorFrame.All,0,amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),seconds),true);
        }
        private struct DamageState
        {
            internal bool Valid,Fall;internal int HP,Armor0,Armor1,ShieldHP;
            internal IncomingHapticKind Kind;internal ImpactSide Side;internal HapticAttackGroup? Group;
        }
        private static void DamagePrefix(scrPlayerControl __instance,scrHitInfo hitInfo,out DamageState __state)
        {
            __state=default;var g=scrGameControl.Instance;
            if(_hapticOwner==null||hitInfo==null||!Eligible(__instance,g))return;
            var data=g.gamePlayers[__instance.playerID];var group=HapticProducerTags.Get(hitInfo);
            var kind=hitInfo.damageType==scrHitInfo.DamageType.Explosive?IncomingHapticKind.Explosion:
                hitInfo.damageType==scrHitInfo.DamageType.Burn?IncomingHapticKind.Burn:group?.Kind??IncomingHapticKind.Unknown;
            var side=ImpactSide.Unknown;
            // Bullet.Update has already replaced producer source-position with
            // travel direction; all tagged castSwing producers use victim-source.
            // Snapshot now, before shared pellet metadata can be overwritten.
            if(kind!=IncomingHapticKind.Unknown&&kind!=IncomingHapticKind.Explosion&&kind!=IncomingHapticKind.Burn&&group?.Direction!=HapticDirection.None&&
                __instance.playerCameraComponent!=null&&VrHandPose.TryHead(out _,out var head)){
                var horizontal=Quaternion.Euler(0,__instance.playerCameraComponent.transform.eulerAngles.y,0);
                var facing=(horizontal*WorldStereoTrialPlugin.TrackingYawCompensation*head)*Vector3.forward;
                var incoming=group?.Direction==HapticDirection.TowardSource?hitInfo.hitFrom:-hitInfo.hitFrom;
                side=HapticPatterns.Side(facing.x,facing.z,incoming.x,incoming.z);
            }
            __state=new DamageState{Valid=true,HP=data.playerHP,Armor0=data.playerArmor[0],Armor1=data.playerArmor[1],Kind=kind,Side=side,Group=group,
                Fall=hitInfo.damageType==scrHitInfo.DamageType.Fall,ShieldHP=__instance.offHandItem==scrPlayerControl.OffHandItem.Shield&&__instance.shieldState==1?__instance.shieldHP:-1};
        }
        private static void DamagePostfix(scrPlayerControl __instance,DamageState __state)
        {
            var owner=_hapticOwner;var g=scrGameControl.Instance;
            if(!__state.Valid||owner==null||!Local(__instance,g))return;
            var data=g.gamePlayers[__instance.playerID];
            int loss=Math.Max(0,__state.HP-data.playerHP)+Math.Max(0,__state.Armor0-data.playerArmor[0])+Math.Max(0,__state.Armor1-data.playerArmor[1]);
            if(owner.ExpansionDamageAccepted(__instance,__state.ShieldHP,__state.Fall,loss))return;
            if(loss<=0)return;
            if(__state.Kind==IncomingHapticKind.Explosion)ExplosionHapticCapture.MarkPlayerDamage();
            if(__state.Kind==IncomingHapticKind.Shotgun&&__state.Group!=null&&!__state.Group.Accept(__state.Side,true))return;
            uint sequence=__state.Kind==IncomingHapticKind.Automatic||__state.Kind==IncomingHapticKind.HelicopterAutomatic?owner._automaticHit++:unchecked((uint)(__state.Group?.Identity??0));
            if(__state.Kind==IncomingHapticKind.Burn)sequence=owner._burnHit++;
            if(__state.Kind==IncomingHapticKind.Shrapnel)sequence=owner._fragmentHit++;
            var frame=HapticPatterns.Incoming(__state.Kind,__state.Side,sequence,1);
            owner.PrioritizeImpact(Math.Max(.15f,frame.Seconds));
            float amplitude=frame.Amplitude>0?frame.Amplitude:Math.Min(.85f,.25f+loss*.015f);
            if(__state.Kind==IncomingHapticKind.HelicopterAutomatic)amplitude=.42f; // Vest-only impact tuning; preserve working controller feedback.
            bool added=__state.Kind>=IncomingHapticKind.Arrow&&__state.Kind!=IncomingHapticKind.HelicopterAutomatic;
            owner.PulseControllers(false,true,amplitude,added&&frame.Seconds>0?frame.Seconds:.15f);
            owner.Vest(new VestMotorFrame(frame.Front,frame.Back,frame.Amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),frame.Seconds),false);
        }
        private void Recoil(bool left,float amplitude,float seconds)
        {
            PulseControllers(left,false,amplitude,seconds);
            const uint upperFront=(1u<<1)|(1u<<2)|(1u<<5)|(1u<<6);
            Vest(new VestMotorFrame(upperFront,0,amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),seconds),true);
        }
        private void PulseControllers(bool left,bool both,float amplitude,float seconds)
        {
            bool enabled=VrSettings.ControllerHaptics.Value&&VrSettings.MotionControls;
            _hapticTransport.SetAllowed(enabled);
            if(!enabled)return;
            float strength=amplitude*HapticPatterns.Strength(VrSettings.ControllerHapticStrength.Value);
            double now=Time.realtimeSinceStartupAsDouble;
            _hapticTransport.Pulse(left,strength,seconds,now);
            if(both)_hapticTransport.Pulse(!left,strength,seconds,now);
        }
        private void Vest(VestMotorFrame frame,bool recoil)
        { if(VrSettings.VestHaptics.Value)_vestHaptics.Enqueue(frame,recoil); }
        private void LateUpdate()
        {
            var g=scrGameControl.Instance;
            double now=Time.realtimeSinceStartupAsDouble;
            // Native stopCutscene/endCutsceneInstant return gameState to 0 but
            // leave HUD state 3 fading the scene overlay. It is not gameplay state.
            // Keep entry/exit fades (1/2/4) gated; actual cinematic events use the
            // native playback/skip check rather than the HUD overlay alone.
            bool gameplay=GameplayHapticsActive(g);
            // Real impacts and explosions may also occur during cinematic playback.
            bool allowed=XRSettings.isDeviceActive&&Application.isFocused&&SessionFocused&&g!=null&&(gameplay||CinematicHapticsActive(g));
            bool controllers=allowed&&VrSettings.ControllerHaptics.Value&&VrSettings.MotionControls&&VrSettings.ControllerHapticStrength.Value>0;
            _hapticTransport.SetAllowed(controllers);_hapticTransport.Tick(now);
            bool vest=allowed&&VrSettings.VestHaptics.Value&&VrSettings.VestHapticStrength.Value>0;
            if(vest!=_vestEnabled){_vestEnabled=vest;_vestHaptics.SetEnabled(vest);}
            DispatchLanding(g,controllers,now);
            // Reuse the native HUD's local-player reference; no object searches.
            bool heartbeat=false;int health=0;
            if((vest||controllers)&&g!=null&&g.hudScript!=null){
                var player=g.hudScript.playerScript;
                if(player!=null&&Eligible(player,g)&&g.gamePlayers!=null&&
                    player.playerID>=0&&player.playerID<g.gamePlayers.Length&&g.gamePlayers[player.playerID]!=null){
                    heartbeat=true;health=g.gamePlayers[player.playerID].playerHP;
                }
            }
            if(_heartbeat.Tick(gameplay&&vest&&heartbeat,health,now,VrSettings.VestHapticStrength.Value,out var beat))
                _vestHaptics.EnqueueHeartbeat(beat);
            if(_worldRumble.Tick((vest||controllers)&&heartbeat&&health>0&&now>=_rumblePriorityUntil,now,1,out var rumble)){
                float amplitude=rumble.Amplitude*.75f*HapticPatterns.Strength(VrSettings.ControllerHapticStrength.Value);
                _hapticTransport.PulseIfIdle(false,amplitude,rumble.Seconds,now);
                _hapticTransport.PulseIfIdle(true,amplitude,rumble.Seconds,now);
                Vest(new VestMotorFrame(rumble.Front,rumble.Back,rumble.Amplitude*HapticPatterns.Strength(VrSettings.VestHapticStrength.Value),rumble.Seconds),false);
            }
            HapticProducerTags.Enabled=allowed&&(controllers||vest);
            ExplosionHapticCapture.Enabled=allowed&&(vest||controllers);
            _allowed=allowed;
        }
        private void Suspend()
        {
            ResetExpandedHaptics();
            _helicopterNearMissGeneration++;_nextHelicopterNearMiss=0;_helicopterNearMissPending=0;
            _heartbeat.Reset();
            _worldRumble.Reset();
            _hapticCutsceneObject=null;_hapticCutscene=null;
            ExplosionHapticCapture.Enabled=false;
            _hapticTransport.SetAllowed(false);_vestHaptics.SetEnabled(false);_vestEnabled=_allowed=false;HapticProducerTags.Disable();
        }
        private void OnApplicationFocus(bool focused){if(!focused)Suspend();}
        private void OnApplicationPause(bool paused){if(paused)Suspend();}
        private void HapticSceneChanged(Scene before,Scene after){Suspend();_flamePhase=false;}
        private void ShutdownHaptics()
        {
            Suspend();_hapticTransport.Dispose();_vestHaptics.Dispose();_hapticHarmony?.UnpatchSelf();_producerHarmony?.UnpatchSelf();_temporaryHapticHarmony?.UnpatchSelf();
            _extraHapticHarmony?.UnpatchSelf();ExplosionHapticCapture.Shutdown();
            _cinematicHapticHarmony?.UnpatchSelf();
            _helicopterNearMissHarmony?.UnpatchSelf();
            _additionalProducerHarmony?.UnpatchSelf();
            WorldRumbleHooks.Shutdown();
            ElevatorRideHooks.Shutdown();
            _expansionHarmony?.UnpatchSelf();_expandedEventsReady=false;ExpandedWorldRumbleHooks.Shutdown();
            SceneManager.activeSceneChanged-=HapticSceneChanged;if(_hapticOwner==this)_hapticOwner=null;
        }
    }
}
