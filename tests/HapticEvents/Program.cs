using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.XR;

int checks=0;void Check(bool c,string s){checks++;if(!c)throw new Exception(s);}
var g=new scrGameControl();scrGameControl.Instance=g;var data=g.gamePlayers[0];data.ammo[8]=100;
foreach(int id in new[]{1,3,14,15,10,11,12})data.playerLoadout.Add(new scrWeapon{weaponTableID=id});
var p=new scrPlayerControl{playerHUD=g.hudScript};var owner=new OpenXRControllersPlugin();owner.Use();owner.Tick();
float Shot(int slot){owner.Clear();p.weapon=slot;owner.Fire(p);Check(owner.Output.Pulses.Count==1,"Exactly one pulse per accepted primary");return owner.Output.Pulses[0].Amplitude;}
float small=Shot(0),shotgun=Shot(1),rifle=Shot(2);Check(shotgun>rifle&&rifle>small,"Shotgun > rifle > small through event consumer");
owner.Clear();p.weapon=1;p.tempWeapon=new TempWeapon{tWepType=TempWeapon.TWepType.MG};
owner.Fire(p,during:()=>{for(int i=0;i<8;i++)owner.Temp(p);});
Check(owner.Output.Pulses.Count==1,"Regular shotgun pellets cannot become temporary MG shots");
owner.Clear();owner.Temp(p);Check(owner.Output.Pulses.Count==1&&owner.Output.Pulses[0].Amplitude==.85f&&owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.VestOutput.Frames[0].Back==0,"Actual temporary MG round: strong full front-chest recoil");
p.tempWeapon.tWepType=TempWeapon.TWepType.Bazooka;owner.Clear();owner.Temp(p);
Check(owner.Output.Pulses.Single().Amplitude==1f&&owner.Output.Pulses[0].Seconds==.2f&&owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.VestOutput.Frames[0].Back==0,"Rocket has strong full front-chest recoil");
p.tempWeapon.tWepType=TempWeapon.TWepType.LP42;owner.Clear();owner.Temp(p);
Check(owner.Output.Pulses.Single().Amplitude==.5f&&owner.VestOutput.Frames.Single().Front!=VestMotorFrame.All,"Disposable flare has distinct lighter recoil");
// Exact output equivalence against the existing disposable LP42, including
// independent controls, strength scaling, both modes and either weapon hand.
int flareSlot=data.playerLoadout.Count;data.playerLoadout.Add(new scrWeapon{weaponTableID=17});
foreach(bool left in new[]{false,true})foreach(bool alt in new[]{false,true})
foreach(float handStrength in new[]{0f,.4f,1f})foreach(float vestStrength in new[]{0f,.9f,1f})foreach(int enabled in new[]{0,1,2,3}){
    VrSettings.LeftWeapon=left;VrSettings.ControllerHapticStrength.Value=handStrength;VrSettings.VestHapticStrength.Value=vestStrength;
    VrSettings.ControllerHaptics.Value=(enabled&1)!=0;VrSettings.VestHaptics.Value=(enabled&2)!=0;
    p.tempWeapon=new TempWeapon{tWepType=TempWeapon.TWepType.LP42};owner.Clear();owner.Temp(p);
    var referenceHands=owner.Output.Pulses.ToArray();var referenceVest=owner.VestOutput.Frames.ToArray();
    p.tempWeapon=null;p.weapon=flareSlot;owner.Clear();owner.Fire(p,alt);
    Check(owner.Output.Pulses.SequenceEqual(referenceHands),"Equipped normal/star flare matches disposable hand/strength/duration/toggle exactly");
    Check(owner.VestOutput.Frames.SequenceEqual(referenceVest),"Equipped normal/star flare matches disposable chest pattern/strength/duration/toggle exactly");
}
VrSettings.LeftWeapon=false;VrSettings.ControllerHapticStrength.Value=VrSettings.VestHapticStrength.Value=1;
VrSettings.ControllerHaptics.Value=VrSettings.VestHaptics.Value=true;
foreach(bool alt in new[]{false,true}){
    owner.Clear();owner.Fire(p,alt);
    Check(owner.Output.Pulses.Count==1&&owner.Output.Pulses[0].Amplitude==.5f&&owner.Output.Pulses[0].Seconds==.09f&&owner.VestOutput.Frames.Count==1,"One matching90ms recoil per accepted equipped flare shot");
    owner.Clear();g.gameState=1;owner.Fire(p,alt);g.gameState=0;owner.Tick();
    Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Paused equipped flare cannot replay feedback");
    p.tempWeapon=new TempWeapon{tWepType=TempWeapon.TWepType.LP42};owner.Clear();owner.Fire(p,alt,()=>owner.Temp(p));
    Check(owner.Output.Pulses.Count==1&&owner.VestOutput.Frames.Count==1,"Nested temporary callback cannot duplicate equipped flare recoil");p.tempWeapon=null;
}
p.weapon=1;p.tempWeapon=new TempWeapon{tWepType=TempWeapon.TWepType.LP42};
VrSettings.LeftWeapon=true;owner.Clear();owner.Temp(p);Check(owner.Output.Pulses.Single().Left,"Disposable recoil follows left weapon mode");VrSettings.LeftWeapon=false;
p.tempWeapon.tWepType=TempWeapon.TWepType.MG;owner.Clear();for(int i=0;i<5;i++)owner.Temp(p);
Check(owner.Output.Pulses.Count==5&&owner.VestOutput.Frames.Count==5,"Five completed MG rounds produce five pulses");
owner.Clear();g.gameState=1;owner.Temp(p);Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Paused disposable callback stays silent");g.gameState=0;
p.tempWeapon.tWepType=TempWeapon.TWepType.BBGun;owner.Clear();owner.Temp(p);Check(owner.Output.Pulses.Single().Amplitude==.15f,"BB gun round");p.tempWeapon=null;
owner.Clear();p.weapon=1;owner.Fire(p,during:()=>p.weapon=0);Check(owner.Output.Pulses.Single().Amplitude==shotgun,"Snapshot survives weapon replacement in native callback");
VrSettings.LeftWeapon=true;Shot(0);Check(owner.Output.Pulses[0].Left,"Left-hand recoil follows selected physical weapon hand");VrSettings.LeftWeapon=false;
owner.Clear();p.weapon=4;data.ammo[8]=0;owner.Fire(p,true);Check(owner.Output.Pulses.Count==0,"Empty incinerator alternate callback stays silent");
data.ammo[8]=100;owner.Fire(p);owner.Fire(p);Check(owner.Output.Pulses.Count==2&&owner.Output.Pulses[0].Amplitude!=owner.Output.Pulses[1].Amplitude,"Flame flutter follows actual emission events");
float flutter=owner.Output.Pulses.Max(x=>x.Amplitude);owner.Clear();owner.Fire(p,true);Check(owner.Output.Pulses.Single().Amplitude>flutter,"Charged alternate has stronger distinct pulse");
void Hit(scrHitInfo info,int hp=5,int armor=0){owner.Damage(p,info,()=>{data.playerHP-=hp;data.playerArmor[0]-=armor;});}
scrHitInfo Info(IncomingHapticKind kind,ulong identity=1,Vector3? direction=null){var i=new scrHitInfo{hitFrom=direction??new Vector3(0,0,-1)};HapticProducerTags.Tag(i,new HapticAttackGroup(identity,kind));return i;}
owner.Clear();var group=Info(IncomingHapticKind.Shotgun);for(int i=0;i<8;i++)Hit(group);
Check(owner.VestOutput.Frames.Count==1&&owner.Output.Pulses.Count==2,"Eight accepted pellets make one simultaneous blast, not eight full bursts");
Check(owner.VestOutput.Frames[0].Front!=0&&owner.VestOutput.Frames[0].Back==0,"Incoming front uses negative final travel direction");
owner.Clear();group=Info(IncomingHapticKind.Shotgun,2);Hit(group,0);Check(owner.Output.Pulses.Count==0,"Shield/rejected hit has no torso feedback");Hit(group);Check(owner.VestOutput.Frames.Count==1,"Rejected pellet does not consume blast group");
owner.Clear();Hit(Info(IncomingHapticKind.Shotgun,3));Hit(Info(IncomingHapticKind.Shotgun,4));Check(owner.VestOutput.Frames.Count==2,"Concurrent attackers retain distinct native attack groups");
owner.Clear();for(ulong i=10;i<13;i++)Hit(Info(IncomingHapticKind.Automatic,i));
Check(owner.VestOutput.Frames.Count==3&&owner.VestOutput.Frames[0].Front!=owner.VestOutput.Frames[1].Front&&owner.VestOutput.Frames[1].Front!=owner.VestOutput.Frames[2].Front,"Automatic accepted-hit cadence changes spots");
owner.Clear();var single=Info(IncomingHapticKind.SingleShot);Hit(single);float light=owner.VestOutput.Frames.Single().Amplitude;
owner.Clear();Hit(Info(IncomingHapticKind.Rifle));Check(owner.VestOutput.Frames.Single().Amplitude>light,"Incoming rifle stronger than small single");
owner.Clear();Hit(Info(IncomingHapticKind.Melee,direction:new Vector3(0,0,1)));Check(owner.VestOutput.Frames.Single().Back!=0&&owner.VestOutput.Frames[0].Front==0,"Rear melee thump");
owner.Clear();var explosion=new scrHitInfo{damageType=scrHitInfo.DamageType.Explosive};Hit(explosion);
Check(owner.VestOutput.Frames.Single().Front==VestMotorFrame.All&&owner.VestOutput.Frames[0].Back==VestMotorFrame.All,"Native accepted explosion drives all 40 motors together");
owner.Clear();var mutable=Info(IncomingHapticKind.SingleShot);owner.Damage(p,mutable,()=>{mutable.hitFrom=new Vector3(0,0,1);data.playerHP--;});
Check(owner.VestOutput.Frames.Single().Front!=0,"Impact snapshot survives shared metadata mutation");
owner.Clear();Hit(new scrHitInfo());Check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==2,"Unknown producer retains generic controller damage without fabricated vest class");
owner.Clear();owner.Damage(p,Info(IncomingHapticKind.SingleShot),()=>{data.playerHP=0;p.isDead=true;});Check(owner.VestOutput.Frames.Count==1,"Accepted lethal hit still has feedback");p.isDead=false;data.playerHP=100;
foreach(Action change in new Action[]{()=>Application.isFocused=false,()=>XRSettings.isDeviceActive=false,()=>g.gameState=1,()=>g.connectionStatus=scrGameControl.ConnectionStatus.Online,()=>p.playerHUD.cutsceneState=1,()=>p.isDead=true}){
    owner.Clear();change();owner.Fire(p);Hit(Info(IncomingHapticKind.Rifle));Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Invalid context rejects event output");
    Application.isFocused=true;XRSettings.isDeviceActive=true;g.gameState=0;g.connectionStatus=scrGameControl.ConnectionStatus.Offline;p.playerHUD.cutsceneState=0;p.isDead=false;
}
owner.Clear();VrSettings.MotionControls=false;owner.Fire(p);Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==1,"Vest remains independent in gamepad/HMD mode");VrSettings.MotionControls=true;
owner.Clear();VrSettings.ControllerHapticStrength.Value=0;VrSettings.VestHapticStrength.Value=0;owner.Fire(p);Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Zero strengths silent independently");VrSettings.ControllerHapticStrength.Value=VrSettings.VestHapticStrength.Value=1;
owner.Fire(p);owner.Tick();int stops=owner.Output.Stops,vestStops=owner.VestOutput.Stops;g.gameState=1;owner.Tick();Check(owner.Output.Stops>stops&&owner.VestOutput.Stops>vestStops,"Pause stops active controller/vest output");g.gameState=0;owner.Tick();
owner.Clear();OpenXRControllersPlugin.HapticSessionState(4);owner.Fire(p);Check(owner.Output.Pulses.Count==0,"XR focus loss rejects shots");OpenXRControllersPlugin.HapticSessionState(5);owner.Tick();
for(int i=0;i<1000;i++)owner.Tick();long bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)owner.Tick();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;Console.WriteLine("Steady event manager bytes: "+bytes);Check(bytes==0,"Steady event manager LateUpdate allocation-free");
// Heartbeat uses the actual native HUD ownership seam and shares no controller output.
g.hudScript.playerScript=p;data.playerHP=26;Time.realtimeSinceStartupAsDouble=10;owner.Clear();owner.Tick();
Check(owner.VestOutput.Frames.Count==0,"No heartbeat above 25 HP");
data.playerHP=25;owner.Tick();Check(owner.VestOutput.Frames.Count==1&&owner.VestOutput.Frames[0].Front==544&&owner.Output.Pulses.Count==0,"25 HP starts front-chest heartbeat, vest only");
Time.realtimeSinceStartupAsDouble=10.05;owner.Tick();Check(owner.VestOutput.Frames.Count==1,"Heartbeat has a quiet gap");
Time.realtimeSinceStartupAsDouble=10.125;owner.Tick();Check(owner.VestOutput.Frames.Count==2&&owner.VestOutput.Frames[1].Amplitude<owner.VestOutput.Frames[0].Amplitude,"Second thump is distinct and softer");
Time.realtimeSinceStartupAsDouble=10.5;owner.Tick();Check(owner.VestOutput.Frames.Count==3,"120 BPM repeats every half second");
data.playerHP=26;owner.Clear();Time.realtimeSinceStartupAsDouble=10.625;owner.Tick();Check(owner.VestOutput.Frames.Count==0,"Healing cancels pending second thump");
foreach(Action disable in new Action[]{()=>p.isDead=true,()=>data.playerHP=0,()=>data.playerHP=-1,()=>g.gameState=1,()=>Application.isFocused=false,()=>XRSettings.isDeviceActive=false,()=>g.hudScript.cutsceneState=1,()=>g.connectionStatus=scrGameControl.ConnectionStatus.Online,()=>g.hudScript.playerScript=null,()=>VrSettings.VestHaptics.Value=false,()=>VrSettings.VestHapticStrength.Value=0}){
    data.playerHP=25;owner.Clear();disable();Time.realtimeSinceStartupAsDouble+=1;owner.Tick();
    Check(owner.VestOutput.Frames.Count==0,"Heartbeat suppressed on invalid/dead/disabled context");
    p.isDead=false;g.gameState=0;Application.isFocused=true;XRSettings.isDeviceActive=true;g.hudScript.cutsceneState=0;g.connectionStatus=scrGameControl.ConnectionStatus.Offline;g.hudScript.playerScript=p;VrSettings.VestHaptics.Value=true;VrSettings.VestHapticStrength.Value=1;
}
owner.Clear();data.playerHP=1;owner.Tick();Check(owner.VestOutput.Frames.Count==1,"1 HP still beats");
Time.realtimeSinceStartupAsDouble+=5;owner.Tick();Check(owner.VestOutput.Frames.Count==2,"Long hitch emits one fresh thump, no catch-up burst");
owner.Clear();Time.realtimeSinceStartupAsDouble-=10;owner.Tick();Check(owner.VestOutput.Frames.Count==1,"Clock rewind safely restarts");
owner.Clear();Time.realtimeSinceStartupAsDouble=double.NaN;owner.Tick();Check(owner.VestOutput.Frames.Count==0,"Invalid clock suppresses heartbeat");
Time.realtimeSinceStartupAsDouble=100;owner.Tick();owner.Clear();
for(int i=0;i<1000;i++)owner.Tick();bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)owner.Tick();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
Check(bytes==0,"Active low-health timer adds no steady per-frame allocations");
var timer=new HapticHeartbeat();timer.Tick(true,25,0,.4f,out var beat);
Check(Math.Abs(beat.Amplitude-.18f)<.00001f&&beat.Seconds==.08f,"Heartbeat scales with independent vest strength");
Check(!timer.Tick(true,25,.4,1,out _),"Late second thump is dropped");
Check(timer.Tick(true,25,.5,1,out _),"Next full beat follows a dropped second thump");
timer.Tick(false,25,.55,1,out _);Check(timer.Tick(true,25,.56,1,out beat)&&beat.Seconds==.08f,"Resume starts a fresh full beat");
for(int i=0;i<1000;i++)timer.Tick(true,25,200+i*.125,1,out _);
bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)timer.Tick(true,25,400+i*.125,1,out _);bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
Check(bytes==0,"Repeated heartbeat emissions allocate no managed memory in scheduler");
// New contact/world event contracts (production callbacks with explicit output fixtures).
owner.Use();data.playerHP=100;g.hudScript.playerScript=p;owner.Tick();
foreach(int id in new[]{0,7,13,16})foreach(bool left in new[]{false,true}){
    VrSettings.LeftWeapon=left;
    data.playerLoadout.Add(new scrWeapon{weaponTableID=id});p.weapon=data.playerLoadout.Count-1;
    owner.Clear();p.fireCharge=0;owner.Fire(p);Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Melee miss silent in either hand mode");
    owner.Fire(p,during:()=>{owner.Contact(p);owner.Contact(p);});
    Check(owner.Output.Pulses.Count==1&&owner.Output.Pulses[0].Left==left&&owner.Output.Pulses[0].Amplitude==.55f&&owner.Output.Pulses[0].Seconds==.08f&&owner.VestOutput.Frames.Count==0,"One normal contact in selected weapon hand, no vest");
    owner.Clear();p.fireCharge=21;
    owner.Fire(p,during:()=>{p.fireCharge=0;VrSettings.LeftWeapon=!left;owner.Contact(p);owner.Contact(p);});
    Check(owner.Output.Pulses.Count==1&&owner.Output.Pulses[0].Amplitude==1&&owner.Output.Pulses[0].Seconds==.16f&&owner.Output.Pulses[0].Left==left&&owner.VestOutput.Frames.Count==0,"Charged/hand snapshot survives native reset and hand setting change");VrSettings.LeftWeapon=left;
    owner.Clear();owner.Fire(p,true,()=>owner.Contact(p));Check(owner.Output.Pulses.Count==0,"Alternate throw not melee contact");
    try{owner.Fire(p,during:()=>throw new InvalidOperationException());}catch(InvalidOperationException){}
    owner.Contact(p);Check(owner.Output.Pulses.Count==0,"Exception restores contact scope in either hand mode");
    foreach(float strength in new[]{0f,.4f,1f}){
        owner.Clear();p.fireCharge=0;VrSettings.ControllerHapticStrength.Value=strength;owner.Fire(p,during:()=>owner.Contact(p));
        Check(strength==0?owner.Output.Pulses.Count==0:owner.Output.Pulses.Count==1&&owner.Output.Pulses[0].Left==left&&Math.Abs(owner.Output.Pulses[0].Amplitude-.55f*strength)<.00001f,"Melee selected hand honors independent controller strength");
    }
    VrSettings.ControllerHapticStrength.Value=1;
    owner.Clear();VrSettings.ControllerHaptics.Value=false;owner.Fire(p,during:()=>owner.Contact(p));VrSettings.ControllerHaptics.Value=true;
    Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Disabled melee haptics silent");
    owner.Clear();g.gameState=1;owner.Fire(p,during:()=>owner.Contact(p));g.gameState=0;owner.Tick();
    Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"Paused contacts cannot replay on resume");
}
VrSettings.LeftWeapon=false;
owner.Clear();p.weapon=0;owner.Fire(p,during:()=>owner.Contact(p));Check(owner.Output.Pulses.Count==1,"Gun contact does not add melee output");
owner.Clear();owner.Pickup(new Collider{gameObject=p.gameObject});Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Single().Amplitude==.4f,"Small vest-only accepted pickup");
var pickup=owner.VestOutput.Frames.Single();
Check(pickup.Front==1536&&pickup.Back==0&&pickup.Seconds==.14f,"Pickup stays on two front motors for perceptible duration");
owner.Clear();VrSettings.VestHapticStrength.Value=.9f;owner.Pickup(new Collider{gameObject=p.gameObject});
Check(Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.36f)<.00001f&&owner.Output.Pulses.Count==0,"Pickup respects actual 90% vest setting without controller output");
owner.Clear();VrSettings.VestHapticStrength.Value=0;owner.Pickup(new Collider{gameObject=p.gameObject});
Check(owner.VestOutput.Frames.Count==0,"Zero vest slider leaves pickup silent");VrSettings.VestHapticStrength.Value=1;
owner.Clear();owner.Pickup(new Collider());Check(owner.VestOutput.Frames.Count==0,"Other actor pickup silent");
owner.Clear();owner.InteractionPickup(p);Check(owner.VestOutput.Frames.Single().Amplitude==.4f&&owner.Output.Pulses.Count==0,"Accepted shield/moth uses existing small vest-only pickup pulse");
owner.Clear();g.gameState=1;owner.InteractionPickup(p);g.gameState=0;Check(owner.VestOutput.Frames.Count==0,"Offhand pickup pulse retains gameplay eligibility");
owner.Grab(p,false,false);owner.Grab(p,true,true);Check(owner.VestOutput.Frames.Count==0,"Rejected or already-held grab silent");
owner.Grab(p,false,true);Check(owner.VestOutput.Frames.Count==1,"New successful carryable pickup");
owner.Clear();g.gameState=1;owner.Pickup(new Collider{gameObject=p.gameObject});owner.Blast(new(0,0,3));Check(owner.VestOutput.Frames.Count==0,"Paused extras silent");g.gameState=0;
VrHandPose.Head=Quaternion.identity;WorldStereoTrialPlugin.TrackingYawCompensation=Quaternion.identity;p.playerCameraComponent.transform.eulerAngles=default;
owner.Clear();owner.Blast(new(0,0,5));var near=owner.VestOutput.Frames.Single();Check(near.Front==VestMotorFrame.All&&near.Back==0&&owner.Output.Pulses.Count==2&&near.Amplitude==1&&near.Seconds==.25f,"Explosion drives full front and both controllers");
owner.Clear();owner.Blast(new(0,0,-15));var far=owner.VestOutput.Frames.Single();Check(far.Back==VestMotorFrame.All&&far.Front==0&&far.Amplitude==near.Amplitude,"Rear blast has identical strength regardless of distance");
foreach(float distance in new[]{25f,1000f,1000000f}){
    owner.Clear();owner.Blast(new(0,0,distance));
    Check(owner.VestOutput.Frames.Single().Amplitude==near.Amplitude&&owner.Output.Pulses.Count==2,"All explosion distances retain full strength");
}
owner.Clear();owner.Blast(new(float.NaN,0,0));owner.Blast(new(0,float.PositiveInfinity,0));Check(owner.VestOutput.Frames.Count==0&&owner.Output.Pulses.Count==0,"Invalid blast coordinates silent");
owner.Clear();VrHandPose.Head=Quaternion.Euler(0,180,0);owner.Blast(new(0,0,5));Check(owner.VestOutput.Frames.Single().Back==VestMotorFrame.All,"Blast direction follows actual view yaw");VrHandPose.Head=Quaternion.identity;
owner.Clear();WorldStereoTrialPlugin.TrackingPositionCompensation=new(0,0,10);owner.Blast(new(0,0,5));Check(owner.VestOutput.Frames.Single().Back==VestMotorFrame.All,"Blast uses rendered tracking origin");WorldStereoTrialPlugin.TrackingPositionCompensation=default;
WorldCaptureTests.Run(Check);

// Additional incoming attacks preserve native acceptance, source conventions and old patterns.
owner.Use();data.playerHP=100;owner.Tick();
foreach(var kind in new[]{IncomingHapticKind.Arrow,IncomingHapticKind.Shrapnel,IncomingHapticKind.HeavyImpact,IncomingHapticKind.Energy,IncomingHapticKind.Acid,IncomingHapticKind.Grip}){
    owner.Clear();var attack=Info(kind);Hit(attack,0);Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"New attack rejected by native damage remains silent");
    Hit(attack,0,1);Check(owner.VestOutput.Frames.Count==1&&owner.VestOutput.Frames[0].Front!=0&&owner.VestOutput.Frames[0].Back==0,"Accepted armor-only new attack uses front panel");
    owner.Clear();Hit(Info(kind,direction:new(0,0,1)),1);Check(owner.VestOutput.Frames.Single().Back!=0&&owner.VestOutput.Frames[0].Front==0,"New attack rear direction preserved");
}
owner.Clear();var nail=new scrHitInfo{hitFrom=new(0,0,1)};HapticProducerTags.Tag(nail,new HapticAttackGroup(100,IncomingHapticKind.Shrapnel,HapticDirection.TowardSource));Hit(nail,1);
Check(owner.VestOutput.Frames.Single().Front!=0&&owner.VestOutput.Frames[0].Back==0,"Native reverse nail direction corrected in metadata only");
owner.Clear();Hit(Info(IncomingHapticKind.Shrapnel),1);Hit(Info(IncomingHapticKind.Shrapnel),1);Check(owner.VestOutput.Frames.Count==2&&owner.VestOutput.Frames[0].Front!=owner.VestOutput.Frames[1].Front,"Accepted fragments scatter across successive spots");
owner.Clear();var burn=new scrHitInfo{damageType=scrHitInfo.DamageType.Burn};Hit(burn,0);Check(owner.VestOutput.Frames.Count==0,"Burn buildup without actual loss stays silent");
Hit(burn,1);Hit(burn,1);var burns=owner.VestOutput.Frames;Check(burns.Count==2&&burns[0].Front!=0&&burns[0].Back!=0&&burns[0].Front!=burns[1].Front,"Directionless actual burn ticks alternate sparse panel patches");
Check(owner.Output.Pulses.All(x=>x.Seconds==.045f&&x.Amplitude==.24f),"Burn controller pulses remain short and subdued");
owner.Clear();for(int i=0;i<10;i++)owner.Tick();Check(owner.VestOutput.Frames.Count==0,"No continuing burn effect without another native loss");
owner.Clear();var ground=Info(IncomingHapticKind.GroundShock);HapticProducerTags.Tag(ground,new HapticAttackGroup(102,IncomingHapticKind.GroundShock,HapticDirection.None));Hit(ground,1);
Check(owner.VestOutput.Frames.Single().Front!=0&&owner.VestOutput.Frames[0].Front==owner.VestOutput.Frames[0].Back&&owner.VestOutput.Frames[0].Front!=VestMotorFrame.All,"Ground/vertical shocks broad but distinct from explosions");
owner.Clear();g.gameState=1;Hit(burn,1);Hit(ground,1);Check(owner.Output.Pulses.Count==0&&owner.VestOutput.Frames.Count==0,"New burn/shock feedback respects pause");g.gameState=0;
owner.Clear();VrSettings.VestHapticStrength.Value=.9f;Hit(burn,1);Check(Math.Abs(owner.VestOutput.Frames.Single().Amplitude-.216f)<.00001f,"New pattern honors user's independent vest strength");VrSettings.VestHapticStrength.Value=1;

owner.Clear();var gas=Info(IncomingHapticKind.Irritation,direction:new(100,0,100));HapticProducerTags.Tag(gas,new HapticAttackGroup(103,IncomingHapticKind.Irritation,HapticDirection.None));Hit(gas,1);
Check(owner.VestOutput.Frames.Single().Front==owner.VestOutput.Frames[0].Back&&owner.VestOutput.Frames[0].Amplitude==.28f,"Gas/embedded injury discomfort does not mistake world position for direction");
WorldRumbleTests.Run(Check,owner,p,g);
CinematicImpactTests.Run(Check,owner,p,g);
GameplayGateTests.Run(Check,owner,p,g);
HelicopterNearMissTests.Run(Check,owner,p,g);
ExpansionTests.Run(Check,owner,p,g);
ElevatorRideTests.Run(Check,owner,p,g);
FinalRumbleTests.Run(Check,owner,p,g);
owner.Close();Console.WriteLine($"PASS {checks} production event-consumer contracts using explicit Unity/output fixtures; native hooks/runtime gameplay still require live proof.");
