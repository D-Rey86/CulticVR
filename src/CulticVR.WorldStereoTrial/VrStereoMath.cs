using UnityEngine;

namespace CulticVR.WorldStereoTrial
{
    public static class VrStereoMath
    {
        // The runtime owns eye cant/orientation and the measured baseline.
        // Scale origins symmetrically about that baseline's midpoint; never
        // substitute an assumed IPD or world-space X axis.
        public static bool TryScaleBaseline(Matrix4x4 left,Matrix4x4 right,float scale,
            out Matrix4x4 resultLeft,out Matrix4x4 resultRight)
        {
            resultLeft=left; resultRight=right;
            if(float.IsNaN(scale)||float.IsInfinity(scale)||scale<.5f||scale>1.5f||
                !ScopeSightingMath.TryGetEyeOrigin(left,out var l)||!ScopeSightingMath.TryGetEyeOrigin(right,out var r)) return false;
            if(scale==1f) return true;
            var center=(l+r)*.5f;
            resultLeft=At(left,center+(l-center)*scale); resultRight=At(right,center+(r-center)*scale);
            return ScopeStereoMath.Valid(resultLeft)&&ScopeStereoMath.Valid(resultRight);
        }
        private static Matrix4x4 At(Matrix4x4 view,Vector3 p)
        {
            view.m03=-(view.m00*p.x+view.m01*p.y+view.m02*p.z);
            view.m13=-(view.m10*p.x+view.m11*p.y+view.m12*p.z);
            view.m23=-(view.m20*p.x+view.m21*p.y+view.m22*p.z);
            return view;
        }
    }
}
