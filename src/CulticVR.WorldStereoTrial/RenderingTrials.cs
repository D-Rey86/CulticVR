using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace CulticVR.WorldStereoTrial
{
    // Automatic shader optimizations, separate from the game's quality preferences.
    // The native helper patches only allowlisted bytecode on creation and caches both lighting variants.
    internal static class RenderingTrials
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StartNative(IntPtr resource);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SelectNative(int lighting,int palette);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative();
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        private static IntPtr _library;
        private static SelectNative? _select;
        private static ReadNative? _ready;
        private static bool _initialized;
        internal static string StartupStatus { get; private set; } = "Shaders: not initialized";
        internal static uint Ready => _ready == null ? 0 : _ready();
        internal static void Initialize()
        {
            if(_initialized)return;
            _initialized=true;
            if(IntPtr.Size!=8 || SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11) {
                StartupStatus="Shaders unavailable: requires Windows x64 / Direct3D 11";return;
            }
            try {
                // Reuse Unity's built-in texture. This runs once, before gameplay shaders are created.
                // GetNativeTexturePtr can synchronize the render thread; never call it in a frame loop.
                var pointer=Texture2D.whiteTexture.GetNativeTexturePtr();
                if(pointer==IntPtr.Zero)throw new InvalidOperationException("Built-in texture has no native resource");
                var assembly=typeof(WorldStereoTrialPlugin).Assembly;
                using var stream=assembly.GetManifestResourceStream("CulticVR.RenderingNative.dll") ?? throw new InvalidOperationException("Missing embedded rendering helper");
                using var memory=new MemoryStream();stream.CopyTo(memory);var bytes=memory.ToArray();
                using var sha=SHA256.Create();var hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
                var directory=Path.Combine(Paths.CachePath,"CulticVR.Rendering",hash);
                Directory.CreateDirectory(directory);
                var path=Path.Combine(directory,"CulticVR.RenderingNative.dll");
                if(!File.Exists(path) || !SameHash(sha,File.ReadAllBytes(path),hash)) {
                    var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                    File.WriteAllBytes(temporary,bytes);
                    if(File.Exists(path))File.Delete(path);
                    File.Move(temporary,path);
                }
                _library=LoadLibraryW(path);
                if(_library==IntPtr.Zero)throw new InvalidOperationException("Native helper load error "+Marshal.GetLastWin32Error());
                var start=Export<StartNative>("RenderingStart");
                var select=Export<SelectNative>("RenderingSelect");
                var ready=Export<ReadNative>("RenderingReady");
                int result=start(pointer);
                if(result!=0)throw new InvalidOperationException("Native initialization error "+result);
                _select=select;_ready=ready;
                StartupStatus="Shader trials available";
            } catch(Exception e) {
                StartupStatus="Shader trials unavailable (see log)";
                Debug.LogWarning("CULTIC VR optional rendering trials unavailable: "+e.Message);
            }
        }
        private static bool SameHash(HashAlgorithm sha,byte[] bytes,string expected) =>
            BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","")==expected;
        private static T Export<T>(string name) where T:Delegate {
            var address=GetProcAddress(_library,name);
            if(address==IntPtr.Zero)throw new InvalidOperationException("Missing native export "+name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        internal static void Apply(bool stereoActive) {
            // Best measured modes: discard zero-light writes and conservative palette search.
            // Exact-shader compatibility/failure fallback remains owned by the native helper.
            _select?.Invoke(stereoActive?2:0,stereoActive?1:0);
        }
        // Native callbacks outlive the managed plugin. Keep the DLL loaded; OFF makes them pass through.
        internal static void Disable() => _select?.Invoke(0,0);
    }

    public sealed partial class WorldStereoTrialPlugin
    {
        private RenderingPath _originalRenderingPath;
        private bool _trialsActive;
        private int _renderingRevision=-1;
        public static uint RenderingShaderReady => RenderingTrials.Ready;
        public static string RenderingStartupStatus => RenderingTrials.StartupStatus;
        private void ApplyRenderingTrials()
        {
            if(_stereo==null || (_trialsActive && _renderingRevision==VrSettings.Revision))return;
            _stereo.renderingPath=VrSettings.DeferredTrial.Value?RenderingPath.DeferredShading:_originalRenderingPath;
            RenderingTrials.Apply(true);
            _trialsActive=true;_renderingRevision=VrSettings.Revision;
        }
        private void RestoreRenderingTrials()
        {
            if(!_trialsActive)return;
            if(_stereo!=null)_stereo.renderingPath=_originalRenderingPath;
            RenderingTrials.Disable();_trialsActive=false;_renderingRevision=-1;
        }
    }
}
