using System;
using CulticVR.UiTrial;
using UnityEngine;

UiTrialPlugin.RunChecks();

namespace CulticVR.UiTrial
{
    public sealed partial class UiTrialPlugin
    {
        private bool _nativeMenuPresentation;
        public static void RunChecks()
        {
            static void Check(bool value,string name) { if(!value) throw new Exception(name); }
            var plugin=new UiTrialPlugin();
            var originalColor=new Color(.2f,.3f,.4f,.5f);
            var camera=new Camera(CameraClearFlags.Depth,originalColor);
            plugin.ApplyMenuBackground(camera);
            Check(camera.Writes==0,"gameplay untouched");
            plugin._nativeMenuPresentation=true;
            plugin.ApplyMenuBackground(camera);
            Check(camera.clearFlags==CameraClearFlags.SolidColor && camera.backgroundColor==Color.black,"menu clear black");
            for(int i=0;i<1000;i++) plugin.ApplyMenuBackground(camera);
            Check(camera.Writes==2,"no per-frame settings writes or saved baseline overwritten");
            var replacement=new Camera(CameraClearFlags.Nothing,new Color(.6f,.7f,.8f,.9f));
            plugin.ApplyMenuBackground(replacement);
            Check(camera.clearFlags==CameraClearFlags.Depth && camera.backgroundColor==originalColor,"old overlay restored before replacement");
            Check(replacement.clearFlags==CameraClearFlags.SolidColor && replacement.backgroundColor==Color.black,"replacement overlay gets menu clear");
            plugin._nativeMenuPresentation=false;
            plugin.ApplyMenuBackground(replacement);
            Check(replacement.clearFlags==CameraClearFlags.Nothing && replacement.backgroundColor==new Color(.6f,.7f,.8f,.9f),"exact gameplay original pair restored");
            int writes=replacement.Writes;
            plugin.RestoreMenuBackground();plugin.ApplyMenuBackground(replacement);
            Check(replacement.Writes==writes,"teardown idempotent");
            plugin._nativeMenuPresentation=true;
            plugin.ApplyMenuBackground(camera);plugin.RestoreMenuBackground();
            Check(camera.clearFlags==CameraClearFlags.Depth && camera.backgroundColor==originalColor,"scene unload restoration");
            plugin.ApplyMenuBackground(camera);camera.Destroyed=true;
            plugin.ApplyMenuBackground(replacement);plugin.RestoreMenuBackground();
            Check(plugin._menuClearCamera is null,"destroyed Unity-style reference and teardown safe");
            Console.WriteLine("PASS actual menu helper: gameplay unchanged, black clear,1000-frame idempotence, camera replacement, exact restoration, destroyed-camera guard. Stubbed Camera lifecycle, not native graphics proof.");
        }
    }
}

// Managed camera stand-in tests the actual production helper without starting
// Unity/VR. Destroyed references mimic Unity's null equality and reject access.
namespace UnityEngine
{
    public enum CameraClearFlags { SolidColor=2,Depth=3,Nothing=4 }
    public readonly record struct Color(float r,float g,float b,float a)
    { public static Color black=>new Color(0,0,0,1); }
    public sealed class Camera
    {
        private CameraClearFlags _flags;private Color _color;
        public bool Destroyed;public int Writes;
        public Camera(CameraClearFlags flags,Color color){_flags=flags;_color=color;}
        private void Validate(){if(Destroyed)throw new Exception("access to destroyed camera");}
        public CameraClearFlags clearFlags { get{Validate();return _flags;} set{Validate();_flags=value;Writes++;} }
        public Color backgroundColor { get{Validate();return _color;} set{Validate();_color=value;Writes++;} }
        public static bool operator ==(Camera? a,Camera? b)
        {
            bool an=a is null || a.Destroyed,bn=b is null || b.Destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a,b);
        }
        public static bool operator !=(Camera? a,Camera? b)=>!(a==b);
        public override bool Equals(object? other)=>ReferenceEquals(this,other);
        public override int GetHashCode()=>base.GetHashCode();
    }
}
