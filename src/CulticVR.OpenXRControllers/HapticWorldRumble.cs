using System;
namespace CulticVR.OpenXRControllers
{
    // Coalesce native frame-rate shake submissions into a bounded 10 Hz rumble.
    internal sealed class HapticWorldRumble
    {
        private float _pending;
        private double _expires,_next,_last=double.NaN;
        internal void Submit(float amount,double now)
        {
            if(float.IsNaN(amount)||float.IsInfinity(amount)||amount<=0||!Clock(now))return;
            if(now>_expires)_pending=0;
            _pending=Math.Max(_pending,Math.Min(4,amount));_expires=now+.15;
        }
        internal bool Tick(bool allowed,double now,float strength,out VestMotorFrame frame)
        {
            frame=default;
            if(!allowed){Reset();return false;}
            if(!Clock(now)||_pending<=0)return false;
            if(now>_expires){_pending=0;return false;}
            if(now<_next)return false;
            float amplitude=.65f*(float)Math.Sqrt(_pending/4f)*HapticPatterns.Strength(strength);
            _pending=0;_next=now+.1;
            const uint lowerTwelve=VestMotorFrame.All&~255u;
            frame=new VestMotorFrame(lowerTwelve,lowerTwelve,amplitude,.1f);
            return amplitude>0;
        }
        private bool Clock(double now)
        {
            if(double.IsNaN(now)||double.IsInfinity(now)){Reset();return false;}
            if(!double.IsNaN(_last)&&now<_last)Reset();
            _last=now;return true;
        }
        internal void Reset(){_pending=0;_expires=_next=0;_last=double.NaN;}
    }
}
