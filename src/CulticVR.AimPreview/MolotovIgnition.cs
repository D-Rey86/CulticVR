using System;
using CulticVR.WorldStereoTrial;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CulticVR.AimPreview
{
    public sealed partial class AimPreviewPlugin
    {
        private AccessTools.FieldRef<scrPlayerControl,bool>? _molotovUnderwater;
        private AccessTools.FieldRef<scrPlayerControl,bool>? _isMolomite;
        private scrPlayerControl? _molotovContactOwner;
        private int _molotovContactWeapon=-1;
        private bool _molotovContactStarted,_molotovContactSeparated;
        private RectTransform? _molotovWickRect;
        private Image? _molotovWickImage;
        private Sprite? _molotovWickSprite;
        private bool _molotovWickKnown;

        private void InitializeMolotovIgnition()
        {
            try {
                _molotovUnderwater=AccessTools.FieldRefAccess<scrPlayerControl,bool>("isUnderwater");
                _isMolomite=AccessTools.FieldRefAccess<scrPlayerControl,bool>("isMolomite");
            } catch(Exception e) { Logger.LogError("Physical Molotov ignition unavailable: "+e.Message); }
        }
        private void ResetMolotovIgnition()
        {
            if(ReferenceEquals(_molotovContactOwner,null) && ReferenceEquals(_molotovWickRect,null)) return;
            _molotovContactOwner=null; _molotovContactWeapon=-1;
            _molotovContactStarted=false; _molotovContactSeparated=false;
            _molotovWickRect=null; _molotovWickImage=null; _molotovWickSprite=null; _molotovWickKnown=false;
        }
        private void TryIgniteMolotov(scrPlayerControl player,Vector3 viewerPosition,Camera sourceCamera)
        {
            var game=scrGameControl.Instance;
            if (!VrSettings.MotionControls || game==null || game.gameState!=0 ||
                game.connectionStatus!=scrGameControl.ConnectionStatus.Offline || player.playerID!=game.localPlayerID ||
                player.weapon<0 || player.tempWeapon!=null || player.offHandItem!=scrPlayerControl.OffHandItem.Lighter ||
                _handObject==null || _handRenderer==null || !_handRenderer.enabled ||
                _leftLighterImage==null || !_leftLighterImage.isActiveAndEnabled) {
                ResetMolotovIgnition(); return;
            }
            var players=game.gamePlayers;
            if(players==null || player.playerID<0 || player.playerID>=players.Length || players[player.playerID]==null)
            { ResetMolotovIgnition(); return; }
            var loadout=players[player.playerID].playerLoadout;
            if(loadout==null || player.weapon>=loadout.Count || loadout[player.weapon].weaponTableID!=8) { ResetMolotovIgnition(); return; }
            if(_molotovContactOwner!=player || _molotovContactWeapon!=player.weapon) {
                ResetMolotovIgnition(); _molotovContactOwner=player; _molotovContactWeapon=player.weapon;
            }
            if(_tntIsLitRef==null || _tntIsLitRef(player) ||
                !TryGetMolotovCloth(player,sourceCamera,out var origin,out var x,out var y)) return;
            var flame=_leftLighterImage.rectTransform.TransformPoint(SpritePointToImageRect(_leftLighterImage,LighterFlamePoint));
            float canvasScale=Mathf.Abs(player.armCanvas.lossyScale.y)*ViewmodelWorldScale;
            if(canvasScale<=.00001f) return;
            bool contact=ClothContactMath.InContact(flame,viewerPosition,origin,x,y,MolotovCloth.Outline,
                IgnitionContactPixels*canvasScale,MaximumIgnitionDepthSeparation);
            if(_molotovContactStarted) {
                if(!contact) _molotovContactSeparated=true;
                if(_tntIsLitRef==null || _tntIsLitRef(player) || !_molotovContactSeparated) return;
                _molotovContactStarted=false; _molotovContactSeparated=false;
            }
            if(!contact || !CanPhysicallyIgniteMolotov(player,game,loadout[player.weapon].weaponAmmoID)) return;
            _molotovContactStarted=true; _molotovContactSeparated=false;
            // Shipping sprMolotovLight event and controller transition. Native
            // handler owns lit state/audio; native throw/extinguish remain owner.
            player.molotovLight();
            player.armAnim.Play("Molotov Idle Lit",0,0f);
        }
        private bool CanPhysicallyIgniteMolotov(scrPlayerControl player,scrGameControl game,int ammo)
        {
            if(_throwStateRef==null || _tntIsLitRef==null || _tntIsBoundRef==null || _lighterStateRef==null ||
                _molotovUnderwater==null || _isMolomite==null) return false;
            var remaining=game.gamePlayers[player.playerID].ammo;
            return Application.isFocused && !player.isDead && player.freezeCD<=0 && game.inputCooldown<=0 &&
                player.fireCD<=0 && player.weaponState==0 && _throwStateRef(player)==0 && !_tntIsLitRef(player) &&
                _tntIsBoundRef(player) && _lighterStateRef(player)==1 && !_molotovUnderwater(player) && !_isMolomite(player) &&
                player.offHandState==scrPlayerControl.OffHandState.Idle && player.armAnim!=null && player.playerHUD!=null &&
                player.playerHUD.wepWheelState==0 && player.playerHUD.invWheelState==0 && player.playerHUD.wepWheelCD<=0 &&
                remaining!=null && ammo>=0 && ammo<remaining.Length && remaining[ammo]>0;
        }
        private bool TryGetMolotovCloth(scrPlayerControl player,Camera camera,out Vector3 origin,out Vector3 x,out Vector3 y)
        {
            origin=x=y=Vector3.zero;
            if(!camera.orthographic || player.playerWeapon==null || _handObject==null) return false;
            if(_molotovWickRect!=player.playerWeapon || _molotovWickImage==null) {
                _molotovWickRect=player.playerWeapon;
                _molotovWickImage=_molotovWickRect.GetComponent<Image>(); _molotovWickSprite=null; _molotovWickKnown=false;
            }
            var image=_molotovWickImage;
            if(image==null || !image.isActiveAndEnabled || image.type!=Image.Type.Simple || image.preserveAspect || image.useSpriteMesh) return false;
            var sprite=image.overrideSprite!=null?image.overrideSprite:image.sprite;
            if(sprite==null) return false;
            if(sprite!=_molotovWickSprite) {
                _molotovWickSprite=sprite;
                _molotovWickKnown=sprite.name=="sprWeaponMolotov_0" && sprite.rect.width==480 && sprite.rect.height==220;
            }
            if(!_molotovWickKnown) return false; // No invented region for Molomite or unknown animation art.
            var rect=image.GetPixelAdjustedRect();
            // Orthographic native rect -> UV -> actual quad is affine. Three
            // projections give the entire cloth mapping, including mirror and
            // calibration, instead of projecting every boundary vertex.
            var native=_molotovWickRect!.TransformPoint(new Vector3(
                rect.x+rect.width*MolotovCloth.CropX/480f,rect.y+rect.height*MolotovCloth.CropTop/220f,0));
            var uv=camera.WorldToViewportPoint(native);
            var ux=camera.WorldToViewportPoint(native+_molotovWickRect.TransformVector(new Vector3(rect.width/480f,0,0)));
            var uy=camera.WorldToViewportPoint(native+_molotovWickRect.TransformVector(new Vector3(0,-rect.height/220f,0)));
            if(!ScopeSightingMath.Finite(uv) || !ScopeSightingMath.Finite(ux) || !ScopeSightingMath.Finite(uy) || uv.z<=0 || ux.z<=0 || uy.z<=0) return false;
            var quad=_handObject.transform;
            origin=quad.TransformPoint(new Vector3(uv.x-.5f,uv.y-.5f,0));
            x=quad.TransformVector(new Vector3(ux.x-uv.x,ux.y-uv.y,0));
            y=quad.TransformVector(new Vector3(uy.x-uv.x,uy.y-uv.y,0));
            return ScopeSightingMath.Finite(origin) && ScopeSightingMath.Finite(x) && ScopeSightingMath.Finite(y);
        }
    }
}
