using System;

namespace CulticVR.OpenXRControllers
{
    internal interface IHapticDevice
    {
        bool Valid { get; }
        bool Send(float amplitude, float seconds);
        void Stop();
    }

    // One cached endpoint per physical hand. No native action identifiers are
    // accepted here. Session/device destruction discards state without output.
    internal sealed class HapticChannel<T> where T : struct, IHapticDevice
    {
        private T _device;
        private bool _supported, _playing, _failed;
        private double _until;
        internal bool Supported => _supported && !_failed && _device.Valid;
        internal bool Failed => _failed;
        internal bool PulseIfIdle(float amplitude,float seconds,double now)
        {
            if(_playing&&now<_until)return false;
            return Pulse(amplitude,seconds,now);
        }
        internal void Bind(T device, bool supported)
        {
            Stop();
            _device = device;
            _supported = supported;
            _failed = false;
        }
        internal bool Pulse(float amplitude, float seconds, double now)
        {
            if (!_supported || _failed || !Finite(amplitude) || !Finite(seconds) ||
                double.IsNaN(now) || double.IsInfinity(now) || amplitude <= 0f || seconds <= 0f) return false;
            try {
                if (!_device.Valid) { Forget(); return false; }
                seconds = Math.Min(.25f, seconds);
                if (!_device.Send(Math.Min(1f, amplitude), seconds)) {
                    // A rejected replacement must not leave an earlier pulse active.
                    Stop(); _failed = true; return false;
                }
                _playing = true; _until = now + seconds;
                return true;
            } catch (Exception) { Stop(); _failed = true; return false; }
        }
        internal void Tick(double now)
        {
            if (_playing && now >= _until) Stop();
        }
        internal void Stop()
        {
            if (!_playing) return;
            _playing = false;
            try { if (_device.Valid) _device.Stop(); }
            catch (Exception) { _failed = true; }
        }
        internal void Forget()
        {
            _playing = _supported = _failed = false;
            _until = 0; _device = default;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
