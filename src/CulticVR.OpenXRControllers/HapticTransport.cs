using System;

namespace CulticVR.OpenXRControllers
{
    internal interface IHapticHandSource<T> : IDisposable where T : struct, IHapticDevice
    {
        event Action<bool> Changed;
        event Action<bool> Removed;
        bool Resolve(bool left, out T device);
        void Clear();
    }
    internal class HapticTransport<T> : IDisposable where T : struct, IHapticDevice
    {
        private readonly HapticChannel<T> _left = new HapticChannel<T>();
        private readonly HapticChannel<T> _right = new HapticChannel<T>();
        private readonly IHapticHandSource<T> _source;
        private ulong _session;
        private bool _focused, _allowed, _disposed;
        internal bool LeftSupported => _left.Supported;
        internal bool RightSupported => _right.Supported;
        internal ulong Session => _session;
        internal bool Focused => _focused;
        internal HapticTransport(IHapticHandSource<T> source)
        { _source=source;source.Changed+=Changed;source.Removed+=Removed; }
        internal void Begin(ulong session) { ForgetHands();_session=session;_focused=false; }
        internal void End() { _session=0;_focused=false;ForgetHands(); }
        internal void State(int next)
        {
            if(_disposed||_session==0)return;
            bool previous=_focused;_focused=next==5;
            if(_focused){if(!previous){Refresh(true);Refresh(false);}}
            else if(next==3||next==4)Stop();
            else ForgetHands();
        }
        internal void SetAllowed(bool allowed) { if(_allowed&&!allowed)Stop();_allowed=allowed; }
        internal bool Pulse(bool left,float amplitude,float seconds,double now)
        {
            if(_disposed||!_allowed||!_focused||_session==0)return false;
            return(left?_left:_right).Pulse(amplitude,seconds,now);
        }
        internal void Tick(double now)
        {
            if(_disposed||_session==0||!_focused)return;
            _left.Tick(now);_right.Tick(now);
        }
        internal bool PulseIfIdle(bool left,float amplitude,float seconds,double now)
        {
            if(_disposed||!_allowed||!_focused||_session==0)return false;
            return(left?_left:_right).PulseIfIdle(amplitude,seconds,now);
        }
        internal void Stop(){_left.Stop();_right.Stop();}
        private void Refresh(bool left)
        {
            var channel=left?_left:_right;
            try { bool supported=_source.Resolve(left,out var device);channel.Bind(device,supported); }
            catch(Exception) { channel.Forget(); }
        }
        private void Changed(bool left){if(!_disposed&&_focused&&_session!=0)Refresh(left);}
        private void Removed(bool left)=>(left?_left:_right).Forget();
        private void ForgetHands(){_left.Forget();_right.Forget();_source.Clear();}
        public void Dispose()
        {
            if(_disposed)return;
            if(_session!=0&&_focused)Stop();
            End();_disposed=true;_source.Changed-=Changed;_source.Removed-=Removed;_source.Dispose();
        }
    }
}
