using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace CulticVR.WorldStereoTrial
{
    // Uses the game's shipped colour-grading shader/resources. The default is
    // entirely disabled. Non-neutral brightness grades the stereo world once;
    // the existing viewmodel layer uses a neutral gamma before its texture is
    // drawn in that world, avoiding grading the weapon twice.
    public sealed class VrBrightness : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<PostProcessLayer,PostProcessResources> ResourcesRef=
            AccessTools.FieldRefAccess<PostProcessLayer,PostProcessResources>("m_Resources");
        private Camera _camera=null!;
        private scrPlayerControl _player=null!;
        private PostProcessLayer _layer=null!;
        private PostProcessLayer _viewmodel=null!;
        private PostProcessProfile _profile=null!,_neutralProfile=null!;
        private PostProcessVolume _volume=null!,_neutralVolume=null!;
        private ColorGrading _grading=null!;
        private readonly List<LayerState> _nativeLayers=new List<LayerState>();
        private int _brightMask,_neutralMask;
        private bool _built,_failed,_applied,_neutralViewmodel;
        private float _lastGamma=float.NaN;
        private struct LayerState { internal PostProcessLayer Layer; internal int Mask; }
        internal void Initialize(Camera camera,scrPlayerControl player) { _camera=camera; _player=player; }
        private void LateUpdate()
        {
            var game=scrGameControl.Instance;
            if(game==null||_camera==null||!game.prefsList.TryGetValue("gamma",out float gamma)) return;
            bool wanted=Mathf.Abs(gamma-.5f)>.00001f;
            if(!wanted) { Disable(); return; }
            if(!_built&&!_failed) {
                try { Build(game); }
                catch(Exception e) { _failed=true; Disable(); Debug.LogError("CULTIC VR brightness unavailable: "+e); return; }
            }
            if(!_built) return;
            if(!_applied) {
                foreach(var state in _nativeLayers) if(state.Layer!=null) state.Layer.volumeLayer=state.Mask&~(_brightMask|_neutralMask);
                _volume.enabled=true; _layer.enabled=true; _applied=true;
            }
            if(_neutralViewmodel!=VrSettings.MotionControls) {
                _neutralViewmodel=VrSettings.MotionControls;
                foreach(var state in _nativeLayers) if(state.Layer==_viewmodel) {
                    _viewmodel.volumeLayer=(state.Mask&~(_brightMask|_neutralMask))|(_neutralViewmodel?_neutralMask:0);
                    break;
                }
                _neutralVolume.enabled=_neutralViewmodel;
            }
            if(_lastGamma!=gamma) {
                _lastGamma=gamma;
                _grading.gamma.Override(new Vector4(1,1,1,Mathf.Clamp(gamma-.5f,-1,1)));
            }
        }
        private void Build(scrGameControl game)
        {
            if(game.standardPP==null||!game.standardPP.profile.TryGetSettings<ColorGrading>(out var sourceGrade))
                throw new InvalidOperationException("Native colour grading missing");
            var source=game.mainCam.GetComponent<PostProcessLayer>();
            if(source==null||ResourcesRef(source)==null) throw new InvalidOperationException("Native post-process resources missing");
            _viewmodel=_player.viewmodelCameraComponent.GetComponent<PostProcessLayer>();
            if(_viewmodel==null) throw new InvalidOperationException("Native viewmodel grading layer missing");
            // Reserve two otherwise-unused POST-PROCESS volume layers. No
            // renderer/physics layer is changed. All existing volume masks are
            // recorded, then exclude our private globals so effects cannot leak
            // into the game's UI, other cameras, or shared native profiles.
            int occupied=0;
            foreach(var volume in UnityEngine.Resources.FindObjectsOfTypeAll<PostProcessVolume>())
                if(volume!=null&&volume.gameObject.scene.IsValid()) occupied|=1<<volume.gameObject.layer;
            int bright=-1,neutral=-1;
            for(int i=31;i>=0;i--) if((occupied&(1<<i))==0) { if(bright<0) bright=i; else { neutral=i; break; } }
            if(neutral<0) throw new InvalidOperationException("No private grading volume layers available");
            _brightMask=1<<bright; _neutralMask=1<<neutral;
            foreach(var layer in UnityEngine.Resources.FindObjectsOfTypeAll<PostProcessLayer>())
                if(layer!=null&&layer.gameObject.scene.IsValid()) _nativeLayers.Add(new LayerState{Layer=layer,Mask=layer.volumeLayer.value});
            _profile=ScriptableObject.CreateInstance<PostProcessProfile>();
            _grading=_profile.AddSettings<ColorGrading>(); _grading.enabled.Override(true);
            _grading.gradingMode.Override(sourceGrade.gradingMode.value);
            _volume=Volume("CulticVR World Brightness",bright,_profile);
            _neutralProfile=ScriptableObject.CreateInstance<PostProcessProfile>();
            var neutralGrade=_neutralProfile.AddSettings<ColorGrading>();
            neutralGrade.gamma.Override(new Vector4(1,1,1,0));
            _neutralVolume=Volume("CulticVR Viewmodel Neutral Gamma",neutral,_neutralProfile);
            _layer=_camera.gameObject.AddComponent<PostProcessLayer>(); _layer.enabled=false;
            _layer.Init(ResourcesRef(source)); _layer.volumeLayer=_brightMask; _layer.volumeTrigger=_camera.transform;
            _layer.antialiasingMode=PostProcessLayer.Antialiasing.None; _layer.stopNaNPropagation=false;
            _layer.fog.enabled=false;
            _built=true;
        }
        private PostProcessVolume Volume(string name,int layer,PostProcessProfile profile)
        {
            var go=new GameObject(name); go.transform.SetParent(transform,false); go.layer=layer;
            var volume=go.AddComponent<PostProcessVolume>(); volume.enabled=false;
            volume.isGlobal=true; volume.priority=10000; volume.sharedProfile=profile; volume.weight=1;
            return volume;
        }
        private void Disable()
        {
            if(_layer!=null) _layer.enabled=false;
            if(_volume!=null) _volume.enabled=false;
            if(_neutralVolume!=null) _neutralVolume.enabled=false;
            if(_applied) foreach(var state in _nativeLayers) if(state.Layer!=null) state.Layer.volumeLayer=state.Mask;
            _applied=false; _neutralViewmodel=false;
        }
        private void OnDestroy()
        {
            Disable();
            if(_profile!=null) foreach(var settings in _profile.settings) Destroy(settings);
            if(_neutralProfile!=null) foreach(var settings in _neutralProfile.settings) Destroy(settings);
            if(_profile!=null) Destroy(_profile); if(_neutralProfile!=null) Destroy(_neutralProfile);
        }
    }
}
