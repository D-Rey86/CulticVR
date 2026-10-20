namespace UnityEngine.XR
{
    internal struct FakeHapticDevice : CulticVR.OpenXRControllers.IHapticDevice
    {
        internal InputDevice Device;
        public bool Valid=>Device.isValid;
        public bool Send(float amplitude,float seconds)=>Device.SendHapticImpulse(0,amplitude,seconds);
        public void Stop()=>Device.StopHaptics();
    }
    internal sealed class FakeHapticHands : CulticVR.OpenXRControllers.IHapticHandSource<FakeHapticDevice>
    {
        public event Action<bool>? Changed,Removed;
        private InputDevice _left,_right;
        internal FakeHapticHands(){InputDevices.deviceConnected+=Change;InputDevices.deviceConfigChanged+=Change;InputDevices.deviceDisconnected+=Remove;}
        public bool Resolve(bool left,out FakeHapticDevice endpoint){
            var device=InputDevices.GetDeviceAtXRNode(left?XRNode.LeftHand:XRNode.RightHand);
            if(left)_left=device;else _right=device;
            endpoint=new FakeHapticDevice {Device=device};return device.isValid&&device.TryGetHapticCapabilities(out var caps)&&caps.supportsImpulse&&caps.numChannels>0;
        }
        private void Change(InputDevice device){if((device.characteristics&InputDeviceCharacteristics.Left)!=0)Changed?.Invoke(true);if((device.characteristics&InputDeviceCharacteristics.Right)!=0)Changed?.Invoke(false);}
        private void Remove(InputDevice device){if(device==_left){_left=default;Removed?.Invoke(true);}if(device==_right){_right=default;Removed?.Invoke(false);}}
        public void Clear(){_left=_right=default;}
        public void Dispose(){InputDevices.deviceConnected-=Change;InputDevices.deviceConfigChanged-=Change;InputDevices.deviceDisconnected-=Remove;Clear();}
    }
    [Flags] public enum InputDeviceCharacteristics { None=0, Controller=1, Left=2, Right=4 }
    public enum XRNode { LeftHand, RightHand }
    public struct HapticCapabilities { public bool supportsImpulse; public uint numChannels; }
    public sealed class DeviceState
    {
        public bool Valid=true, Supported=true, Reject, ThrowSend, ThrowStop;
        public uint Channels=1;
        public int Sends, Stops, CapabilityQueries;
        public float Amplitude, Seconds;
        public InputDeviceCharacteristics Traits;
    }
    public struct InputDevice : IEquatable<InputDevice>
    {
        internal int Id;
        private DeviceState? State => Id==0 ? null : InputDevices.States[Id];
        public bool isValid => State?.Valid==true;
        public InputDeviceCharacteristics characteristics => State?.Traits??0;
        public bool TryGetHapticCapabilities(out HapticCapabilities caps)
        {
            var s=State; caps=default; if(s==null||!s.Valid)return false;
            s.CapabilityQueries++; caps=new HapticCapabilities {supportsImpulse=s.Supported,numChannels=s.Channels}; return true;
        }
        public bool SendHapticImpulse(uint channel,float amplitude,float seconds)
        {
            var s=State!; if(!s.Valid||channel!=0)throw new Exception("Invalid device/channel reached public API");
            s.Sends++; s.Amplitude=amplitude; s.Seconds=seconds;
            if(s.ThrowSend)throw new Exception("Optional provider failure"); return !s.Reject;
        }
        public void StopHaptics()
        {
            var s=State!; if(!s.Valid)throw new Exception("Stale device reached output");
            s.Stops++; if(s.ThrowStop)throw new Exception("Optional stop failure");
        }
        public bool Equals(InputDevice other)=>Id==other.Id;
        public override bool Equals(object? other)=>other is InputDevice d&&Equals(d);
        public override int GetHashCode()=>Id;
        public static bool operator ==(InputDevice a,InputDevice b)=>a.Equals(b);
        public static bool operator !=(InputDevice a,InputDevice b)=>!a.Equals(b);
    }
    public static class InputDevices
    {
        internal static readonly Dictionary<int,DeviceState> States=new();
        public static event Action<InputDevice>? deviceConnected, deviceConfigChanged, deviceDisconnected;
        private static InputDevice _left,_right;
        public static int Queries;
        public static InputDevice GetDeviceAtXRNode(XRNode node){Queries++;return node==XRNode.LeftHand?_left:_right;}
        public static InputDevice Connect(bool left,DeviceState state)
        {
            state.Traits=InputDeviceCharacteristics.Controller|(left?InputDeviceCharacteristics.Left:InputDeviceCharacteristics.Right);
            var device=new InputDevice {Id=States.Count+1}; States.Add(device.Id,state);
            if(left)_left=device;else _right=device; deviceConnected?.Invoke(device); return device;
        }
        public static void Disconnect(InputDevice device){States[device.Id].Valid=false;deviceDisconnected?.Invoke(device);}
        public static void Configure(InputDevice device)=>deviceConfigChanged?.Invoke(device);
    }
}
