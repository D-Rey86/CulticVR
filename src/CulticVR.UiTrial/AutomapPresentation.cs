using CulticVR.OpenXRControllers;
using CulticVR.WorldStereoTrial;
using UnityEngine;

namespace CulticVR.UiTrial
{
    public sealed partial class UiTrialPlugin
    {
        private scrAutomap? _anchoredMap;
        private Vector3 _mapPosition,_mapScale,_mapOriginalScale;
        private Quaternion _mapRotation;
        private bool ApplyAutomapPresentation(Transform pose)
        {
            var map=VrSettings.PalmMap.Value && VrSettings.MotionControls?AutomapInput.VisibleMap:null;
            if(map==null || _nativeMenuPresentation || _loadingPresentation || _quad==null) {
                RestoreAutomapPresentation();return false;
            }
            if(_anchoredMap!=map) {
                RestoreAutomapPresentation();_anchoredMap=map;
                _mapOriginalScale=_quad.transform.localScale;
                // Use the upstream UI's standard width (1.8 m) and existing 16:9 aspect,
                // at 1.4 times the configured UI offset. Freeze only this panel's pose.
                _mapScale=new Vector3(1.8f,1.8f*UiTextureHeight/UiTextureWidth,_mapOriginalScale.z);
                _mapPosition=pose.TransformPoint(Uuvr.ModConfiguration.Instance.VrUiPosition.Value*1.4f);
                _mapRotation=pose.rotation;
            }
            _quad.transform.localScale=_mapScale;
            _quad.transform.SetPositionAndRotation(_mapPosition,_mapRotation);
            return true;
        }
        private void RestoreAutomapPresentation()
        {
            if(_anchoredMap==null && _mapOriginalScale==Vector3.zero)return;
            if(_quad!=null)_quad.transform.localScale=_mapOriginalScale;
            _anchoredMap=null;_mapOriginalScale=Vector3.zero;
        }
    }
}
