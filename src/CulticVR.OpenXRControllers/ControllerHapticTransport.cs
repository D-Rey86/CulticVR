using System;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace CulticVR.OpenXRControllers
{
    internal readonly struct NativeHapticDevice : IHapticDevice
    {
        private readonly InputDevice _device;
        private readonly uint _deviceId;
        private readonly ulong _action;
        internal NativeHapticDevice(InputDevice device,uint deviceId,ulong action)
        { _device=device;_deviceId=deviceId;_action=action; }
        public bool Valid => _device!=null&&_device.added&&_device.enabled&&_deviceId!=0&&_action!=0;
        public bool Send(float amplitude,float seconds)
        {
            // SDK export is void. True means dispatched, not motor acknowledgement.
            NativeHapticApi.Send(_deviceId,_action,amplitude,0,seconds);return true;
        }
        public void Stop()=>NativeHapticApi.Stop(_deviceId,_action);
    }
    internal static class NativeHapticApi
    {
        // Unity OpenXR 1.4.2 SDK signatures. Registration tokens never reach these.
        [DllImport("UnityOpenXR",EntryPoint="OpenXRInputProvider_GetActionIdByControl",CharSet=CharSet.Ansi)]
        internal static extern ulong Resolve(uint deviceId,string controlName);
        [DllImport("UnityOpenXR",EntryPoint="OpenXRInputProvider_SendHapticImpulse",CallingConvention=CallingConvention.Cdecl)]
        internal static extern void Send(uint deviceId,ulong action,float amplitude,float frequency,float seconds);
        [DllImport("UnityOpenXR",EntryPoint="OpenXRInputProvider_StopHaptics",CallingConvention=CallingConvention.Cdecl)]
        internal static extern void Stop(uint deviceId,ulong action);
    }
    internal sealed class NativeHapticHands : IHapticHandSource<NativeHapticDevice>
    {
        public event Action<bool>? Changed;
        public event Action<bool>? Removed;
        private InputDevice? _left,_right;
        internal NativeHapticHands()=>InputSystem.onDeviceChange+=DeviceChanged;
        public bool Resolve(bool left,out NativeHapticDevice endpoint)
        {
            endpoint=default;
            var device=left?XRController.leftHand:XRController.rightHand;
            if(left)_left=device;else _right=device;
            if(device==null||!device.added||!device.enabled)return false;
            var inputInterface=device.description.interfaceName;
            if(string.IsNullOrEmpty(inputInterface)||inputInterface.IndexOf("XR",StringComparison.OrdinalIgnoreCase)<0)return false;
            var legacy=InputDevices.GetDeviceAtXRNode(left?XRNode.LeftHand:XRNode.RightHand);
            if(!legacy.isValid||!legacy.TryGetHapticCapabilities(out var caps)||!caps.supportsImpulse||caps.numChannels==0)return false;
            var command=InternalDeviceIdCommand.Create();
            if(device.ExecuteCommand(ref command)<=0||command.DeviceId==0)return false;
            // Exact registered output name; generated input layouts omit this child.
            ulong action=NativeHapticApi.Resolve(command.DeviceId,"haptic");
            if(action==0)return false;
            endpoint=new NativeHapticDevice(device,command.DeviceId,action);return true;
        }
        public void Clear(){_left=_right=null;}
        public void Dispose(){InputSystem.onDeviceChange-=DeviceChanged;Clear();}
        private void DeviceChanged(InputDevice device,InputDeviceChange change)
        {
            if(!(device is XRController))return;
            if(change==InputDeviceChange.Removed||change==InputDeviceChange.Disconnected||change==InputDeviceChange.Disabled){
                if(device==_left){_left=null;Removed?.Invoke(true);}
                if(device==_right){_right=null;Removed?.Invoke(false);}
                return;
            }
            if(change!=InputDeviceChange.Added&&change!=InputDeviceChange.Reconnected&&change!=InputDeviceChange.Enabled&&
                change!=InputDeviceChange.ConfigurationChanged&&change!=InputDeviceChange.UsageChanged)return;
            foreach(var usage in device.usages){
                if(usage==UnityEngine.InputSystem.CommonUsages.LeftHand)Changed?.Invoke(true);
                else if(usage==UnityEngine.InputSystem.CommonUsages.RightHand)Changed?.Invoke(false);
            }
        }
        [StructLayout(LayoutKind.Explicit,Size=12)]
        private struct InternalDeviceIdCommand : IInputDeviceCommandInfo
        {
            [FieldOffset(0)] public InputDeviceCommand Base;
            [FieldOffset(8)] public uint DeviceId;
            public FourCC typeStatic=>new FourCC('X','R','D','I');
            internal static InternalDeviceIdCommand Create()=>new InternalDeviceIdCommand{
                Base=new InputDeviceCommand(new FourCC('X','R','D','I'),12)};
        }
    }
    internal sealed class ControllerHapticTransport : HapticTransport<NativeHapticDevice>
    { internal ControllerHapticTransport():base(new NativeHapticHands()){} }
}
