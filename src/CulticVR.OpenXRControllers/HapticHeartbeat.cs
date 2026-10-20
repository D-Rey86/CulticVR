using System;

namespace CulticVR.OpenXRControllers
{
    // Two finite chest pulses per 0.5-second cycle (120 BPM). No catch-up burst
    // after a slow frame, pause, scene change, clock reset or reconnect.
    internal sealed class HapticHeartbeat
    {
        private bool _running, _second;
        private double _start, _last;
        internal void Reset() { _running=false;_second=false; }
        internal bool Tick(bool allowed,int health,double now,float strength,out VestMotorFrame frame)
        {
            frame=default;
            strength=HapticPatterns.Strength(strength);
            if(!allowed||health<=0||health>25||strength<=0||double.IsNaN(now)||double.IsInfinity(now)){
                Reset();return false;
            }
            if(!_running||now<_last||now-_start>=.5){
                _running=true;_second=false;_start=_last=now;
                frame=Pulse(.45f*strength,.08f);return true;
            }
            _last=now;
            if(!_second&&now-_start>=.12){
                _second=true;
                // An overdue second thump is dropped rather than replayed.
                if(now-_start>.25)return false;
                frame=Pulse(.30f*strength,.06f);return true;
            }
            return false;
        }
        private static VestMotorFrame Pulse(float amplitude,float seconds)=>
            new VestMotorFrame((1u<<5)|(1u<<9),0,amplitude,seconds);
    }
}
