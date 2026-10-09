using UnityEngine;

namespace CulticVR.AimPreview
{
    internal static class IgnitionContactMath
    {
        // Matches the accepted TNT apparent-contact rule; depth safety remains
        // independent of image overlap so separated hands cannot light remotely.
        internal static bool InContact(Vector3 flame,Vector3 wick,Vector3 viewer,float radius,float maxDepth)
        {
            var f=flame-viewer; var w=wick-viewer;
            float fd=f.magnitude,wd=w.magnitude;
            if(fd<=.00001f || wd<=.00001f) return false;
            float apparent=Vector3.Distance(f/fd,w/wd)*Mathf.Min(fd,wd);
            return apparent<=radius && Mathf.Abs(fd-wd)<=maxDepth;
        }
    }
}
