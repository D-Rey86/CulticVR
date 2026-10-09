using UnityEngine;

namespace CulticVR.AimPreview
{
    internal static class ClothContactMath
    {
        internal static bool Contains(Vector2 point,Vector2[] polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++) {
                var a=polygon[i];var b=polygon[j];
                if((a.y>point.y)!=(b.y>point.y) &&
                    point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x) inside=!inside;
            }
            return inside;
        }

        internal static bool InContact(Vector3 flame,Vector3 viewer,Vector3 origin,
            Vector3 x,Vector3 y,Vector2[] polygon,float radius,float maxDepth)
        {
            float xx=Vector3.Dot(x,x),xy=Vector3.Dot(x,y),yy=Vector3.Dot(y,y);
            float determinant=xx*yy-xy*xy;
            if(!(xx>0 && yy>0 && determinant>xx*yy*1e-6f)) return false;
            var ray=flame-viewer;float distance=ray.magnitude;
            if(!(distance>.00001f)) return false;
            ray/=distance;
            var normal=Vector3.Cross(x,y);float denominator=Vector3.Dot(normal,ray);
            if(Mathf.Abs(denominator)>normal.magnitude*1e-6f) {
                float t=Vector3.Dot(normal,origin-viewer)/denominator;
                if(t>0) {
                    var hit=viewer+ray*t;var local=hit-origin;
                    float lx=Vector3.Dot(local,x),ly=Vector3.Dot(local,y);
                    var point=new Vector2((lx*yy-ly*xy)/determinant,(ly*xx-lx*xy)/determinant);
                    if(Contains(point,polygon) && IgnitionContactMath.InContact(flame,hit,viewer,radius,maxDepth)) return true;
                }
            }
            // Retain the accepted tip's small angular/depth allowance at the
            // cloth boundary. No physics raycast, allocation or scene search.
            var previous=polygon[polygon.Length-1];
            var a=origin+x*previous.x+y*previous.y;
            for(int i=0;i<polygon.Length;i++) {
                var p=polygon[i];var b=origin+x*p.x+y*p.y;var edge=b-a;
                float length=Vector3.Dot(edge,edge);
                if(length>0) {
                    float physical=Mathf.Clamp01(Vector3.Dot(flame-a,edge)/length);
                    if(IgnitionContactMath.InContact(flame,a+edge*physical,viewer,radius,maxDepth)) return true;
                    // Stationary angular distance along a segment; endpoints
                    // also checked, since a stationary point can be a maximum.
                    var v=a-viewer;float re=Vector3.Dot(ray,edge),rv=Vector3.Dot(ray,v),ve=Vector3.Dot(v,edge);
                    float divisor=re*ve-rv*length;
                    if(divisor!=0) {
                        float angular=(rv*ve-re*Vector3.Dot(v,v))/divisor;
                        if(angular>0 && angular<1 && IgnitionContactMath.InContact(flame,a+edge*angular,viewer,radius,maxDepth)) return true;
                    }
                    if(IgnitionContactMath.InContact(flame,a,viewer,radius,maxDepth)) return true;
                }
                a=b;
            }
            return false;
        }
    }
}
