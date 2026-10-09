using System;
using CulticVR.WorldStereoTrial;
using UnityEngine;

static class Program
{
    static void Need(bool value,string message) { if(!value) throw new Exception(message); }
    static bool Near(Vector3 a,Vector3 b)=>(a-b).magnitude<.000002f;
    static Matrix4x4 View(Vector3 p,float cant)
    {
        float c=(float)Math.Cos(cant),s=(float)Math.Sin(cant);
        var m=Matrix4x4.identity; m.m00=c; m.m02=s; m.m20=-s; m.m22=c;
        m.m03=-(c*p.x+s*p.z); m.m13=-p.y; m.m23=-(-s*p.x+c*p.z); return m;
    }
    static void Main()
    {
        int cases=0;
        foreach(float ipd in new[]{.052f,.063f,.076f})
        foreach(float cant in new[]{0f,.09f,-.15f})
        foreach(float scale in new[]{.5f,.75f,1f,1.25f,1.5f})
        {
            var center=new Vector3(.02f,.01f,-.04f); var baseline=new Vector3(ipd,.005f,-.003f);
            var l=View(center-baseline*.5f,cant); var r=View(center+baseline*.5f,-cant);
            Need(VrStereoMath.TryScaleBaseline(l,r,scale,out var sl,out var sr),"valid runtime eye pair");
            Need(ScopeSightingMath.TryGetEyeOrigin(sl,out var lo),"scaled left eye");
            Need(ScopeSightingMath.TryGetEyeOrigin(sr,out var ro),"scaled right eye");
            Need(Near((lo+ro)*.5f,center),"midpoint preserved");
            Need(Near(ro-lo,baseline*scale),"complete measured baseline scales");
            for(int a=0;a<3;a++) for(int b=0;b<3;b++) Need(sl[a,b]==l[a,b]&&sr[a,b]==r[a,b],"eye bases/cant preserved");
            if(scale==1) for(int n=0;n<16;n++) Need(sl[n]==l[n]&&sr[n]==r[n],"default is bit-identical");
            foreach(bool left in new[]{false,true}) {
                Need(ScopeStereoMath.TryLimitSeparation(sl,sr,.4f,left,out var ll,out var lr),"enhanced scope composes with baseline");
                for(int n=0;n<16;n++) Need((left?ll:lr)[n]==(left?sl:sr)[n],"chosen sighting eye stays exact");
            }
            cases++;
        }
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,0f,.49f,1.51f})
            Need(!VrStereoMath.TryScaleBaseline(Matrix4x4.identity,Matrix4x4.identity,invalid,out _,out _),"invalid scale rejected");
        Console.WriteLine($"PASS {cases} baseline/canted-eye cases, exact default, enhanced-scope composition and invalid input. Static math only.");
    }
}
