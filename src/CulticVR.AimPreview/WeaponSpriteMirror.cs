using UnityEngine;

namespace CulticVR.AimPreview
{
    internal static class WeaponSpriteMirror
    {
        // M=diag(-1,1,1) reflects the complete calibrated artwork around its
        // controller-local anchor: M(p+Rv) = Mp + (MRM)(Mv). Mirroring just v
        // leaves the original sideways grip correction on the wrong side.
        internal static float HorizontalScale(float scale, bool leftHanded) => leftHanded ? -scale : scale;
        internal static Vector3 Position(Vector3 value,bool leftHanded) => leftHanded ? new Vector3(-value.x,value.y,value.z) : value;
        internal static Quaternion Rotation(Quaternion value,bool leftHanded) => leftHanded ? new Quaternion(value.x,-value.y,-value.z,value.w) : value;
        internal static float CalibrationAxis(float value,int axis,bool leftHanded) => leftHanded && (axis==0||axis==4||axis==5) ? -value : value;
    }
}
