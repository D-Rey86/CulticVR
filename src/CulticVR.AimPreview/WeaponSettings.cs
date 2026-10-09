using BepInEx.Configuration;
using CulticVR.WorldStereoTrial;
using UnityEngine;

namespace CulticVR.AimPreview
{
    public sealed partial class AimPreviewPlugin
    {
        private ConfigEntry<float> CalibrationEntry(int axis)
        {
            switch(axis) { case 0:return _rightPositionX; case 1:return _rightPositionY; case 2:return _rightPositionZ;
                case 3:return _rightRotationX; case 4:return _rightRotationY; default:return _rightRotationZ; }
        }
        public static float GetCalibration(int axis)
        {
            if(Instance==null) return DefaultCalibration(axis);
            float v=Instance.CalibrationEntry(axis).Value;
            return WeaponSpriteMirror.CalibrationAxis(axis<3?v:Mathf.DeltaAngle(0,v),axis,VrSettings.LeftWeapon);
        }
        public static float DefaultCalibration(int axis)
        {
            float value=axis<3?DefaultRightPositionOffset[axis]:Mathf.DeltaAngle(0,DefaultRightRotationOffset[axis-3]);
            return WeaponSpriteMirror.CalibrationAxis(value,axis,VrSettings.LeftWeapon);
        }
        public static void SetCalibration(int axis,float value) { if(Instance!=null) VrSettings.Set(Instance.CalibrationEntry(axis),WeaponSpriteMirror.CalibrationAxis(value,axis,VrSettings.LeftWeapon)); }
        public static void SaveCalibration() { if(Instance!=null) Instance.Config.Save(); }
        public static void ResetCalibration()
        {
            if(Instance==null) return;
            foreach(var entry in Instance.CalibrationEntries()) VrSettings.Set(entry,(float)entry.DefaultValue);
        }
    }
}
