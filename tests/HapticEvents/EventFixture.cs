using CulticVR.OpenXRControllers;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEngine {
    public static class Application { public static bool isFocused=true; }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public class Object { public static T Instantiate<T>(T value) => value; }
    public class GameObject : Object { public Transform transform=new(); public object? Component; public object[] Children=Array.Empty<object>();public T[] GetComponentsInChildren<T>(bool inactive) where T:class=>Children.OfType<T>().Concat(Component is T t?new[]{t}:Array.Empty<T>()).ToArray();public int GetComponentCalls;public T? GetComponent<T>() where T:class {GetComponentCalls++;return Component as T;} }
    public class Collider { public GameObject gameObject=new();public Bounds bounds=new(){center=new(0,0,0),extents=new(.5f,1,.5f)}; }
    public struct Bounds {public Vector3 center,extents;}
    public struct RaycastHit { public Collider collider; }
    public class Collision {public Collider collider=new();}
    public struct Vector3 {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float magnitude=>MathF.Sqrt(x*x+y*y+z*z); public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 forward=>new(0,0,1);
        public static Vector3 operator -(Vector3 v)=>new(-v.x,-v.y,-v.z);
    }
    public struct Quaternion {
        internal System.Numerics.Quaternion Value;
        public static Quaternion identity=>new(){Value=System.Numerics.Quaternion.Identity};
        public static Quaternion Euler(float x,float y,float z)=>new(){Value=System.Numerics.Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180)};
        public static Quaternion operator *(Quaternion a,Quaternion b)=>new(){Value=a.Value*b.Value};
        public static Vector3 operator *(Quaternion q,Vector3 v){var r=System.Numerics.Vector3.Transform(new(v.x,v.y,v.z),q.Value);return new(r.X,r.Y,r.Z);}
    }
    public sealed class Transform { public Vector3 eulerAngles,position;public object? Component;public int GetComponentCalls;public T? GetComponent<T>() where T:class {GetComponentCalls++;return Component as T;} }
    public sealed class Camera { public Transform transform=new(); }
}
namespace UnityEngine.XR { public static class XRSettings { public static bool isDeviceActive=true; } }
namespace UnityEngine.SceneManagement {
    public struct Scene{}
    public static class SceneManager { public static event Action<Scene,Scene>? activeSceneChanged;public static void Change()=>activeSceneChanged?.Invoke(default,default); }
}
public sealed class scrWeapon { public int weaponTableID; }
public sealed class scrPlayerHUD { public int cutsceneState;public scrPlayerControl? playerScript; }
public sealed class TempWeapon { public enum TWepType {Bazooka,MG,LP42,BBGun} public TWepType tWepType; }
public sealed class scrPlayerControl { public enum OffHandItem {None,Lighter,TNT,Telekinesis,Corruption,Grab,Shield,Ladder,Moth,Tackle,Bandage,Creep} public int shieldHP;public void tryDrawOffHandItem(OffHandItem item){}
    public GameObject gameObject=new();public Transform transform=>gameObject.transform;public float fireCharge;public int playerID,weapon,shieldState;public OffHandItem offHandItem;public bool isDead;public scrPlayerHUD playerHUD=null!;public Camera playerCameraComponent=new();public TempWeapon? tempWeapon;
    public float cameraShake;public void addShake(float amount=.25f){cameraShake+=amount;WorldRumbleHooks.Accepted(this,amount);}public void setShake(float amount=.25f){cameraShake=amount;WorldRumbleHooks.Accepted(this,amount);}
    public void fireEvent(){}public void altFireEvent(){}public void takeDamage(){}public void castBullet(){}
}
public sealed class PlayerData { public List<scrWeapon> playerLoadout=new();public int playerHP=100;public int[] playerArmor={25,25},ammo=new int[10]; }
public sealed class scrGameControl {
    public int NativeShakeCalls;public float? DeliveredShake;public bool ThrowOnShake;public float LastAmount;public Vector3 LastPosition;public bool LastIgnore;
    public void addShake(float amount,Vector3 position,bool ignoreDistance){NativeShakeCalls++;LastAmount=amount;LastPosition=position;LastIgnore=ignoreDistance;if(ThrowOnShake)throw new InvalidOperationException();hudScript.playerScript?.addShake(DeliveredShake??amount);}
    public void setShake(float amount,Vector3 position,bool ignoreDistance){NativeShakeCalls++;LastAmount=amount;LastPosition=position;LastIgnore=ignoreDistance;if(ThrowOnShake)throw new InvalidOperationException();hudScript.playerScript?.setShake(DeliveredShake??amount);}
    public static scrGameControl Instance=null!;public enum ConnectionStatus {Offline,Online}
    public GameObject? cutsceneTrigger;public ConnectionStatus connectionStatus;public int localPlayerID,gameState;public bool diffInfiniteAmmo;public scrPlayerHUD hudScript=new();public PlayerData[] gamePlayers={new()};
}
public sealed class scrHitInfo { public enum DamageType {Basic,Bullet,Slash,Explosive,Burn,Fall}public DamageType damageType;public Vector3 hitFrom;public GameObject? hitBy; }
public sealed class scrBullet { public GameObject whizPrefab=new();public Vector3 direction;public Transform transform=new();public bool isHeld;public scrHitInfo hitInfo=new(); }
namespace CulticVR.WorldStereoTrial {
    public sealed class Entry<T>{public T Value;public Entry(T v){Value=v;}}
    public static class VrSettings {
        public static bool LeftWeapon,MotionControls=true;
        public static Entry<bool> ControllerHaptics=new(true),VestHaptics=new(true);
        public static Entry<float> ControllerHapticStrength=new(1),VestHapticStrength=new(1);
    }
    public static class WorldStereoTrialPlugin { public static Vector3 TrackingPositionCompensation;public static Quaternion TrackingYawCompensation=Quaternion.identity; }
    public static class VrHandPose {
        public static Quaternion Head=Quaternion.identity;public static bool Valid=true;
        public static bool TryHead(out Vector3 p,out Quaternion q){p=default;q=Head;return Valid;}
    }
}
namespace CulticVR.OpenXRControllers {
    internal sealed class ControllerHapticTransport : IDisposable {
        internal bool Focused;internal bool Allowed,Playing;internal int Stops;
        internal readonly List<(bool Left,float Amplitude,float Seconds)> Pulses=new();
        internal void Begin(ulong _) {Focused=false;Playing=false;}internal void End(){Focused=false;Playing=false;}
        internal void State(int next){Focused=next==5;if(!Focused)Stop();}
        internal void SetAllowed(bool value){if(!value)Stop();Allowed=value;}
        internal bool Pulse(bool left,float amplitude,float seconds,double _){if(!Focused||!Allowed||amplitude<=0)return false;Playing=true;Pulses.Add((left,amplitude,seconds));return true;}
        internal bool PulseIfIdle(bool left,float amplitude,float seconds,double now)=>Pulse(left,amplitude,seconds,now);
        internal void Tick(double _){}private void Stop(){if(Playing)Stops++;Playing=false;}public void Dispose()=>Stop();
    }
    internal sealed class VestHapticClient : IDisposable {
        internal bool Enabled;internal int Stops;internal List<VestMotorFrame> Frames=new();
        internal void SetEnabled(bool e){if(Enabled&&!e)Stops++;Enabled=e;}
        internal void Enqueue(VestMotorFrame f,bool _){if(Enabled&&f.Amplitude>0&&(f.Front!=0||f.Back!=0))Frames.Add(f);}
        internal void EnqueueHeartbeat(VestMotorFrame f)=>Enqueue(f,false);
        public void Dispose()=>SetEnabled(false);
    }
    internal static class HapticProducerTags {
        internal static bool Enabled;private static readonly ConditionalWeakTable<scrHitInfo,HapticAttackGroup> Tags=new();
        internal static void Install(HarmonyLib.Harmony _){}internal static void Disable(){Enabled=false;}
        internal static HapticAttackGroup? Get(scrHitInfo info)=>Tags.TryGetValue(info,out var g)?g:null;
        internal static void Tag(scrHitInfo info,HapticAttackGroup group){Tags.Remove(info);Tags.Add(info,group);}
    }
    public sealed partial class OpenXRControllersPlugin {
        public sealed class FixtureLogger{public void LogWarning(string _) {}}
        public FixtureLogger Logger=new();
        internal ControllerHapticTransport Output=>_hapticTransport;internal VestHapticClient VestOutput=>_vestHaptics;
        internal void Use(){_expandedEventsReady=true;_hapticCutsceneState=(scrCutsceneTrigger trigger)=>ref trigger.state;_hapticOwner=this;HapticSessionBegin(1);HapticSessionState(5);}
        internal void Fire(scrPlayerControl p,bool alt=false,Action? during=null){
            ShotState state;if(alt)AltShotPrefix(p,out state);else ShotPrefix(p,p.fireCharge,out state);
            try{during?.Invoke();ShotPostfix(p,state);}finally{ShotFinalizer(state);}
        }
        internal void Damage(scrPlayerControl p,scrHitInfo info,Action during){DamagePrefix(p,info,out var state);during();DamagePostfix(p,state);}
        internal void Shake(scrPlayerControl p,float amount)=>WorldShakeAccepted(p,amount); internal void Impact()=>WorldImpact(); internal void ScriptedBlast(Vector3 position)=>ScriptedExplosion(position);
        internal void CinematicShot(GameObject projectile)=>CinematicGunfire(projectile);internal void InteractionPickup(scrPlayerControl player)=>InteractionPickupAccepted(player);internal void Window(scrE2M6Cutscene source,Action damage){WindowImpactBefore(source,out var state);damage();WindowImpactAfter(source,state);} internal void Contact(scrPlayerControl p)=>MeleeContact(p);internal void Pickup(Collider c)=>PickupAccepted(c);internal void Blast(Vector3 position)=>NearbyExplosion(position);
        internal void Grab(scrPlayerControl p,bool before,bool after)=>GrabAfter(p,new Unity.Netcode.NetworkVariable<bool>{Value=after},before);
        internal void Temp(scrPlayerControl p)=>TemporaryShotCompleted(p);internal void Tick()=>LateUpdate();
        internal void SceneChanged()=>HapticSceneChanged(default,default);internal void Close()=>ShutdownHaptics();
        internal void Whiz(scrBullet bullet,Collider near,Vector3 travel)=>HelicopterWhiz(bullet,near,travel);
        internal void BulletEnd(scrBullet bullet,Collider? struck=null)=>HelicopterBulletFinished(bullet,struck);
        internal void BulletMoved(scrBullet bullet)=>HelicopterBulletMoved(bullet);
        internal void Land(scrPlayerControl p,float fallOrigin)=>LandingAccepted(p,fallOrigin);
        internal void FallDone(scrPlayerControl p,bool rolled)=>FallResolved(p,rolled);
        internal void Heal(scrPlayerControl p,int hp)=>HealingAccepted(p,hp);
        internal void CinematicShake(scrCutsceneTrigger trigger,float amount)=>CinematicShakeAccepted(trigger,amount);
        internal void Platform(scrTriggerPlatformBreak source,Collider collider,Action native){PlatformBefore(source,new Collision{collider=collider},out var state);native();PlatformAfter(source,state);}
        internal void ExpansionReady(bool value)=>_expandedEventsReady=value;
        internal void Clear(){Output.Pulses.Clear();VestOutput.Frames.Clear();}
    }
}

namespace Unity.Netcode { public class NetworkVariable<T> { public T Value=default!; } }
public class scrPickup {}
public class scrGrabbable {}
public class scrExplosion {public UnityEngine.Transform transform=new();}
public class scrGrenadeShell {}
public class scrImpactProjectile {}
public class scrTNT {}
public class scrMolomite {}
public class scrMolotov {}
public class scrNailJar {}
public class scrTank {}
public class scrArchonBloodBall {}
public class scrAbomination {}
public class scrFlare {}
public class scrTNTTripwire {}
namespace CulticVR.OpenXRControllers { internal static class AdditionalHapticProducers { internal static void Install(HarmonyLib.Harmony harmony){} } }
public class scrE1M1EarthquakeTrigger {}
public class scrE1M2VortexTrigger {}
public class scrTimedShake {}
public class scrE2M3CaveCollapseTrigger {}
public class scrE2M3SluiceGate {}
public class scrE2M11FloodTrigger {}
public class scrE2M0ElevatorButton {}
public class scrE2M7BridgeController {}
public class scrE2M9TrainController {}
public class scrEBM1MausoleumEyePuzzle {}
public class scrFreightElevator {public int state;}
public class scrFreightElevatorPlatform {public int state;}
public class scrMovingPlatform {public bool isStationary;public Transform? parentObject;}
public class scrOfficeElevator {public int state;}
public class scrDumbwaiter {public int state;}
public class scrE2M2RollingDoorPanel {}
public class scrDoor {}

public class scrE1M5Plunger {}

public class scrE1M5SkeletonTrigger {}

public class scrE1M9ValveStand {}

public class scrE2M0RooftopExplosion {}

public class scrE2M10Statute {}

public class scrE2M11PortTrigger {}

public class scrE2M12EnergyBarrier {}

public class scrE2M1ManagerKeyTrigger {}

public class scrE2M2NightmareSwitch {}

public class scrE2M3Plunger { public int state;public UnityEngine.GameObject explosionObject=new(); }

public class scrE2M4BruteGarageTrigger {}

public class scrE2M4FreezerFight {}
public class scrE2M5BarnWalls { public int state;public UnityEngine.GameObject[] objectsToActivateOnce=Array.Empty<UnityEngine.GameObject>(); }
public class scrE2M1HarvesterTrigger { public int state;public UnityEngine.Transform transform=new(); }




public class scrE2M6Cutscene { public scrDestructible window=new(); }
public class scrDestructible { public int hp; }
public class scrCutsceneTrigger { public int state,cameraState; }




public class scrDroppedShield { public int shieldHP; }
public class scrE2M0AmalgamCutscene {}
public class scrE2M9Cutscene {}
public class scrAmalgamation {}
public class scrAmalgamationMesh {}
public class scrArchon {}
public class scrArchonGroundFault {}
public class scrArchonLightningStrike {}
public class scrHeliSpider {}
public class scrInfectedBrute {}
public class scrTriggerPlatformBreak {public int state;}



public class scrDamageDropper {}

public class scrArchonTrap {}

public class scrIncinerator {}
