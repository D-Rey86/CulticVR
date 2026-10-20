using System;
using CulticVR.WorldStereoTrial;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace CulticVR.OpenXRControllers
{
    public static class AutomapInput
    {
        private static scrPlayerHUD? _hud;
        private static InputActionAsset? _asset;
        private static InputAction? _mapAction;
        private static bool _bindingValid,_bindingDirty=true;
        private static bool _gameplayScene;
        private static Scene _scene;
        private static AutomapGesture _gesture;
        private static bool _wasVisible,_releaseControls;
        private static int _gestureHand=-1;
        internal static void Initialize() => InputSystem.onActionChange+=ActionChanged;
        internal static void Shutdown() { InputSystem.onActionChange-=ActionChanged;Reset(); }
        private static void ActionChanged(object obj,InputActionChange change) {
            if(change==InputActionChange.BoundControlsChanged)_bindingDirty=true;
        }
        public static scrAutomap? VisibleMap {
            get {
                var game=scrGameControl.Instance;
                var map=game!=null && game.sceneData!=null?game.sceneData.autoMap:null;
                return game!=null && game.gameState==1 && map!=null && map.state==scrAutomap.AutomapState.Tumble?map:null;
            }
        }
        internal static void Reset() {
            _gesture.Reset();_hud=null;_asset=null;_mapAction=null;_bindingValid=false;_bindingDirty=true;
            _wasVisible=false;_releaseControls=false;_gestureHand=-1;
            _gameplayScene=false;_scene=default;
        }
        // Return a short native Back/Select tap. HUD owns map open/close, pause, audio and controls.
        internal static bool Tick(scrGameControl? game,scrPlayerHUD? hud,bool controlsNeutral,bool wristCapturing,
            out bool suppressControls)
        {
            bool visible=VisibleMap!=null;
            if(_wasVisible && !visible)_releaseControls=true;
            _wasVisible=visible;
            if(_releaseControls && controlsNeutral)_releaseControls=false;
            suppressControls=_releaseControls && VrSettings.MotionControls;
            var map=hud!=null?hud.autoMap:null;
            bool ownerChanged=_hud!=hud || (game!=null && _asset!=game.playerControls);
            if(ownerChanged || _bindingDirty) {
                _hud=hud;_asset=game!=null?game.playerControls:null;
                _scene=hud!=null?hud.gameObject.scene:default;
                _gameplayScene=WorldStereoTrialPlugin.IsGameplayScene(_scene);
                _mapAction=_asset!=null?_asset.FindAction("Map",false):null;
                _bindingValid=false;
                if(_mapAction!=null)foreach(var binding in _mapAction.bindings)
                    if(string.Equals(binding.effectivePath,"<Gamepad>/select",StringComparison.OrdinalIgnoreCase)) {_bindingValid=true;break;}
                _bindingDirty=false;
                if(ownerChanged)_gesture.Reset();else _gesture.CancelPending();
            }
            var node=VrHandPose.WeaponNode;
            if(_gestureHand!=(int)node) {_gestureHand=(int)node;_gesture.Reset();}
            var player=hud!=null?hud.playerScript:null;
            bool eligible=VrSettings.PalmMap!=null && VrSettings.PalmMap.Value && VrSettings.MotionControls &&
                XRSettings.isDeviceActive && Application.isFocused && !wristCapturing &&
                game!=null && game.connectionStatus==scrGameControl.ConnectionStatus.Offline && game.inputCooldown<=0 &&
                hud!=null && hud.gameObject.activeInHierarchy && hud.cutsceneState==0 && hud.wepWheelState==0 && hud.invWheelState==0 &&
                map!=null && game.sceneData!=null && game.sceneData.autoMap==map &&
                _gameplayScene && player!=null && player.gameObject.activeInHierarchy && player.gameObject.scene==_scene &&
                SceneManager.GetActiveScene()==_scene && !player.isDead && player.freezeCD<=0 &&
                player.playerID==game.localPlayerID && (player.state==0 || player.state==7) &&
                ((game.gameState==0 && map.state==scrAutomap.AutomapState.Hidden) ||
                 (visible && game.systemMenu!=null && game.systemMenu.menuState==scrMenuControllerV2.MenuState.menuStateClosed)) &&
                _bindingValid && _mapAction!=null && _mapAction.enabled;
            Vector3 head=default,hand=default;Quaternion headRotation=default,handRotation=default;
            bool poseValid=VrHandPose.TryHead(out head,out headRotation) && VrHandPose.TryGrip(node,out hand,out handRotation);
            eligible=eligible && poseValid;
            return _gesture.Step(eligible,node==XRNode.LeftHand,Time.unscaledTime,head,headRotation,hand,handRotation);
        }
    }
}
