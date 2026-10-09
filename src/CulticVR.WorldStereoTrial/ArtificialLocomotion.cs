using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace CulticVR.WorldStereoTrial
{
    public sealed partial class WorldStereoTrialPlugin
    {
        private bool _snapArmed,_teleportArmed,_teleportHeld,_teleportValid;
        private int _artificialRevision=-1;
        private float _nextTeleportQuery;
        private Vector3 _teleportDestination;
        private LineRenderer? _teleportLine;
        private readonly Vector3[] _teleportPoints=new Vector3[25];
        private int _teleportPointCount;
        private scrPlayerControl? _teleportOwner;

        private void PrepareArtificialLocomotion(scrPlayerControl player)
        {
            if(VrSettings.MotionControls && !VrSettings.SnapTurn.Value && !VrSettings.Teleport.Value)
            { CancelArtificialLocomotion(); return; }
            var game=scrGameControl.Instance;
            bool valid=_locomotionReady && player==_player && _reportedReady && _source!=null && game!=null &&
                game.connectionStatus==scrGameControl.ConnectionStatus.Offline && player.playerID==game.localPlayerID &&
                game.gameState==0 && Application.isFocused && Time.timeScale>0 && !player.isDead && player.freezeCD<=0 && game.inputCooldown<=0 &&
                (player.state==0||player.state==7) && NativeLadderState(player)==0 && !NativeInWater(player) &&
                player.myCollider!=null && player.myCollider.enabled;
            if(!valid||!VrHandPose.TryHead(out var head,out var headRotation)) { CancelArtificialLocomotion(); return; }
            if(_artificialRevision!=VrSettings.Revision) {
                _artificialRevision=VrSettings.Revision; CancelArtificialLocomotion();
            }
            var pad=Gamepad.current;
            if(pad==null) { CancelArtificialLocomotion(); return; }
            var turn=pad.rightStick.ReadValue();
            if(!VrSettings.MotionControls && game!.lookMode==1) game.lookInput=new Vector2(game.lookInput.x,0);
            // Native controlMode is chosen by MOVEMENT only and becomes keyboard
            // mode when both movement inputs are zero. Turning has its own owner.
            if(VrSettings.SnapTurn.Value && game!.lookMode==1) {
                game.lookInput=new Vector2(0,game.lookInput.y);
                if(Mathf.Abs(turn.x)<.25f) _snapArmed=true;
                else if(_snapArmed && Mathf.Abs(turn.x)>.7f && Mathf.Abs(turn.x)>Mathf.Abs(turn.y)) {
                    _snapArmed=false; float degrees=Mathf.Sign(turn.x)*VrSettings.SnapDegrees.Value;
                    player.camRot=new Vector2(Mathf.Repeat(player.camRot.x+degrees,360),player.camRot.y);
                    _source!.transform.rotation=Quaternion.AngleAxis(degrees,Vector3.up)*_source.transform.rotation;
                }
            } else _snapArmed=false;
            if(!VrSettings.Teleport.Value||!player.isGrounded) { CancelTeleport(); return; }
            var move=pad.leftStick.ReadValue();
            if(game!.controlMode!=1 && !_teleportHeld && game.moveInput.sqrMagnitude>.0001f)
            { CancelTeleport(); return; }
            game.moveInput=Vector2.zero;
            player.moveSpeed=Vector2.zero;
            var velocity=player.rigidBody.linearVelocity;
            player.rigidBody.linearVelocity=new Vector3(0,velocity.y,0);
            if(move.magnitude<.25f && !_teleportHeld) _teleportArmed=true;
            bool forward=move.y>.65f&&move.y>Mathf.Abs(move.x)*1.1f;
            if(forward&&(_teleportArmed||_teleportHeld)) {
                _teleportArmed=false; _teleportHeld=true;
                if(Time.unscaledTime>=_nextTeleportQuery) {
                    _nextTeleportQuery=Time.unscaledTime+1f/30f;
                    UpdateTeleportArc(player,head,headRotation);
                }
            } else if(_teleportHeld) {
                bool release=move.magnitude<.25f;
                if(release && _teleportValid && DestinationClear(player,_teleportDestination,true)) {
                    player.rigidBody.position=_teleportDestination;
                    player.transform.position=_teleportDestination;
                    _lastLocomotionBody=_teleportDestination;
                    _physicalHeadPositionOrigin.x=head.x; _physicalHeadPositionOrigin.z=head.z;
                    ResetHeadCollision();
                }
                CancelTeleport();
            }
        }
        private void UpdateTeleportArc(scrPlayerControl player,Vector3 head,Quaternion headRotation)
        {
            var yaw=Quaternion.Euler(0,_source!.transform.eulerAngles.y,0)*TrackingYawCompensation;
            var origin=_source.transform.position+yaw*TrackingPositionCompensation;
            var orientation=yaw*headRotation;
            if(VrSettings.MotionControls) {
                var hand=VrSettings.SticksSwapped?XRNode.RightHand:XRNode.LeftHand;
                if(!VrHandPose.TryPointer(hand,out var position,out var rotation)) { _teleportValid=false; HideTeleportArc(); return; }
                origin+=yaw*(position-head); orientation=yaw*rotation;
            }
            int mask=player.canStandOn&~(1<<player.myCollider.gameObject.layer);
            var initialVelocity=orientation*Vector3.forward*6f+Vector3.up*2f;
            _teleportValid=false; _teleportPointCount=1; _teleportPoints[0]=origin;
            for(int i=1;i<_teleportPoints.Length;i++) {
                float t=i*.06f;
                var point=origin+initialVelocity*t+Physics.gravity*(.5f*t*t);
                var last=_teleportPoints[i-1]; var delta=point-last;
                if(Physics.Raycast(last,delta,out var hit,delta.magnitude,mask,QueryTriggerInteraction.Ignore)) {
                    _teleportPoints[_teleportPointCount++]=hit.point;
                    if(hit.normal.y>=Mathf.Cos(45f*Mathf.Deg2Rad) && Vector3.Distance(origin,hit.point)<=6f) {
                        Capsule(player,player.rigidBody.position,out var bottom,out _,out var radius);
                        float footOffset=bottom.y-radius-player.rigidBody.position.y;
                        _teleportDestination=hit.point+Vector3.up*(-footOffset+BodySkin+player.myCollider.contactOffset);
                        float rise=_teleportDestination.y-player.rigidBody.position.y;
                        _teleportValid=rise<=.45f && rise>=-1f && DestinationClear(player,_teleportDestination);
                    }
                    break;
                }
                _teleportPoints[_teleportPointCount++]=point;
            }
            EnsureTeleportArc(player);
            if(_teleportLine==null) return;
            _teleportLine.positionCount=_teleportPointCount;
            for(int i=0;i<_teleportPointCount;i++) _teleportLine.SetPosition(i,_teleportPoints[i]);
            _teleportLine.startColor=_teleportLine.endColor=_teleportValid?new Color(.3f,1f,.3f):new Color(1f,.3f,.15f);
            _teleportLine.enabled=true;
        }
        private static void Capsule(scrPlayerControl player,Vector3 position,out Vector3 bottom,out Vector3 top,out float radius)
        {
            var c=player.myCollider; var scale=c.transform.lossyScale;
            radius=c.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));
            var center=c.transform.TransformPoint(c.center)+position-player.rigidBody.position;
            float half=Mathf.Max(0,c.height*Mathf.Abs(scale.y)*.5f-radius);
            bottom=center-c.transform.up*half; top=center+c.transform.up*half;
        }
        private static bool DestinationClear(scrPlayerControl player,Vector3 destination,bool verifyFloor=false)
        {
            if(player.myCollider.direction!=1||player.rigidBody.isKinematic) return false;
            int mask=player.canStandOn&~(1<<player.myCollider.gameObject.layer);
            Capsule(player,destination,out var b,out var t,out var radius);
            if(verifyFloor) {
                var foot=b-Vector3.up*radius;
                if(!Physics.Raycast(foot+Vector3.up*.05f,Vector3.down,out var ground,.1f,mask,QueryTriggerInteraction.Ignore)||
                    ground.normal.y<Mathf.Cos(45f*Mathf.Deg2Rad)) return false;
            }
            radius=Mathf.Max(.01f,radius-BodySkin);
            if(Physics.CheckCapsule(b,t,radius,mask,QueryTriggerInteraction.Ignore)) return false;
            Capsule(player,player.rigidBody.position,out b,out t,out _);
            var delta=destination-player.rigidBody.position; float distance=delta.magnitude;
            // No minimum travel distance. Zero-distance destinations are safe
            // no-ops after overlap/floor validation; do not normalize zero.
            return distance<=Mathf.Epsilon || !Physics.CapsuleCast(b,t,radius,delta/distance,out _,distance,mask,QueryTriggerInteraction.Ignore);
        }
        private void EnsureTeleportArc(scrPlayerControl player)
        {
            if(_teleportOwner==player&&_teleportLine!=null) return;
            if(_teleportLine!=null) Destroy(_teleportLine.gameObject);
            _teleportOwner=player;
            // Weapon preview may be active at the same time and owns its own
            // line's points/lifecycle. Share its material, not its mutable line.
            if(player.throwArc==null||player.throwArc.sharedMaterial==null) return;
            var go=new GameObject("CulticVR Teleport Arc"); go.transform.SetParent(player.transform,false);
            go.layer=player.throwArc.gameObject.layer;
            _teleportLine=go.AddComponent<LineRenderer>(); _teleportLine.sharedMaterial=player.throwArc.sharedMaterial;
            _teleportLine.useWorldSpace=true; _teleportLine.startWidth=_teleportLine.endWidth=.015f;
            _teleportLine.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; _teleportLine.receiveShadows=false;
        }
        private void HideTeleportArc() { if(_teleportLine!=null) _teleportLine.enabled=false; }
        private void CancelTeleport() { _teleportArmed=false; _teleportHeld=false; _teleportValid=false; HideTeleportArc(); }
        private void CancelArtificialLocomotion() { _snapArmed=false; CancelTeleport(); }
        private void RemoveArtificialLocomotion()
        { CancelArtificialLocomotion(); if(_teleportLine!=null) Destroy(_teleportLine.gameObject); _teleportLine=null; _teleportOwner=null; }
    }
}
