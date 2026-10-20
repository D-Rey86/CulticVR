using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CulticVR.OpenXRControllers
{
    internal readonly struct VestRequest
    {
        internal readonly VestMotorFrame Frame;
        internal readonly long Timestamp, Generation;
        internal readonly int Slot;
        internal VestRequest(VestMotorFrame frame,long timestamp,long generation,int slot)
        { Frame=frame;Timestamp=timestamp;Generation=generation;Slot=slot; }
    }
    internal sealed class VestRequestQueue
    {
        private readonly VestRequest[] _items=new VestRequest[32];
        private int _read,_count;
        internal void Clear(){_read=_count=0;}
        internal void Enqueue(VestRequest request)
        {
            if(_count==_items.Length){_read=(_read+1)%_items.Length;_count--;}
            _items[(_read+_count)%_items.Length]=request;_count++;
        }
        internal bool Take(long now,out VestRequest request)
        {
            while(_count>0){request=_items[_read];_read=(_read+1)%_items.Length;_count--;
                long age=now-request.Timestamp;if(age>=0&&age<=Stopwatch.Frequency/4)return true;}
            request=default;return false;
        }
    }
    internal sealed class VestHapticClient : IDisposable
    {
        private readonly object _gate=new object();
        private readonly SemaphoreSlim _wake=new SemaphoreSlim(0,1);
        private readonly VestRequestQueue _queue=new VestRequestQueue();
        private readonly Uri _endpoint;
        private Task? _worker;
        private bool _enabled,_disposed,_stopPending,_connected;
        private long _generation;
        private int _slot;
        internal bool Connected { get { lock(_gate)return _connected; } }
        internal Task Completion=>_worker??Task.CompletedTask;
        internal VestHapticClient():this(new Uri("ws://127.0.0.1:15881/v2/feedbacks?app_id=culticvr&app_name=CULTIC%20VR")){}
        internal VestHapticClient(Uri endpoint)
        {
            if(!endpoint.IsLoopback||endpoint.Scheme!="ws")throw new ArgumentException("Local Player WebSocket required");
            _endpoint=endpoint;
        }
        private static long Now=>Stopwatch.GetTimestamp();
        internal void SetEnabled(bool enabled)
        {
            lock(_gate){
                if(_disposed||_enabled==enabled)return;
                _enabled=enabled;_queue.Clear();_generation++;_stopPending=true;
                if(enabled&&_worker==null)_worker=Task.Run(Run);
            }
            Wake();
        }
        internal void Enqueue(VestMotorFrame frame,bool recoil)
            =>Enqueue(frame,recoil,false);
        internal void EnqueueHeartbeat(VestMotorFrame frame)=>Enqueue(frame,false,true);
        private void Enqueue(VestMotorFrame frame,bool recoil,bool heartbeat)
        {
            if(frame.Front==0&&frame.Back==0||HapticPatterns.Strength(frame.Amplitude)<=0||
                float.IsNaN(frame.Seconds)||float.IsInfinity(frame.Seconds)||frame.Seconds<=0)return;
            lock(_gate){
                if(!_enabled||_disposed||!_connected)return;
                int slot=heartbeat?-2:recoil?-1:(_slot++&63);
                _queue.Enqueue(new VestRequest(frame,Now,_generation,slot));
            }
            Wake();
        }
        internal void Stop()
        { lock(_gate){_queue.Clear();_generation++;_stopPending=true;}Wake(); }
        public void Dispose()
        { lock(_gate){_disposed=true;_enabled=false;_queue.Clear();_generation++;_stopPending=true;}Wake(); }
        private void Wake(){lock(_gate){if(_wake.CurrentCount==0)_wake.Release();}}
        private async Task Run()
        {
            while(true){
                bool enabled,disposed;lock(_gate){enabled=_enabled;disposed=_disposed;}
                if(disposed)return;
                if(!enabled){await _wake.WaitAsync().ConfigureAwait(false);continue;}
                try{await Session().ConfigureAwait(false);}catch(Exception){/* Optional disconnected output is silent. */}
                lock(_gate){_connected=false;_queue.Clear();enabled=_enabled;disposed=_disposed;}
                if(disposed)return;
                if(enabled)await Task.Delay(5000).ConfigureAwait(false);
            }
        }
        private async Task Session()
        {
            using var socket=new ClientWebSocket();socket.Options.Proxy=null;
            using(var timeout=new CancellationTokenSource(1000))await socket.ConnectAsync(_endpoint,timeout.Token).ConfigureAwait(false);
            using var receiveCancellation=new CancellationTokenSource();
            var receive=Drain(socket,receiveCancellation.Token);
            var text=new StringBuilder(3200);
            lock(_gate){_connected=_enabled&&!_disposed;_queue.Clear();}
            try{
                while(socket.State==WebSocketState.Open&&!receive.IsCompleted){
                    bool stop,enabled,disposed,has;VestRequest request;
                    lock(_gate){stop=_stopPending;_stopPending=false;enabled=_enabled;disposed=_disposed;has=_queue.Take(Now,out request);}
                    if(stop||!enabled||disposed)await Send(socket,VestPackets.Stop).ConfigureAwait(false);
                    if(!enabled||disposed)return;
                    bool current;lock(_gate){current=_enabled&&!_disposed&&request.Generation==_generation;}
                    if(has&&current&&Now-request.Timestamp<=Stopwatch.Frequency/4)
                        await Send(socket,VestPackets.Frame(request,text)).ConfigureAwait(false);
                    else await _wake.WaitAsync(100).ConfigureAwait(false);
                }
            }finally{
                lock(_gate){_connected=false;_queue.Clear();}
                receiveCancellation.Cancel();socket.Abort();
                try{await receive.ConfigureAwait(false);}catch(Exception){}
            }
        }
        private static async Task Send(ClientWebSocket socket,byte[] bytes)
        {
            using var timeout=new CancellationTokenSource(1000);
            await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,timeout.Token).ConfigureAwait(false);
        }
        private static async Task Drain(ClientWebSocket socket,CancellationToken cancellation)
        {
            var buffer=new byte[4096];
            while(socket.State==WebSocketState.Open){var result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),cancellation).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)return;}
        }
    }
    internal static class VestPackets
    {
        internal static readonly byte[] Stop=BuildStop();
        private static string Key(int slot,bool back)=>slot==-2?"culticvr_heartbeat_"+(back?"b":"f"):slot<0?"culticvr_recoil_"+(back?"b":"f"):
            "culticvr_impact_"+slot.ToString(CultureInfo.InvariantCulture)+(back?"_b":"_f");
        private static byte[] BuildStop()
        {
            var text=new StringBuilder(10000);text.Append("{\"Register\":[],\"Submit\":[");bool first=true;
            for(int slot=-2;slot<64;slot++)for(int side=0;side<2;side++){
                if(!first)text.Append(',');first=false;
                text.Append("{\"Type\":\"turnOff\",\"Key\":\"").Append(Key(slot,side==1)).Append("\"}");
            }
            return Encoding.UTF8.GetBytes(text.Append("]}").ToString());
        }
        internal static byte[] Frame(VestRequest request,StringBuilder text)
        {
            text.Clear();text.Append("{\"Register\":[],\"Submit\":[");bool has=false;
            if(request.Frame.Front!=0){Append(text,request,false);has=true;}
            if(request.Frame.Back!=0){if(has)text.Append(',');Append(text,request,true);}
            return Encoding.UTF8.GetBytes(text.Append("]}").ToString());
        }
        private static void Append(StringBuilder text,VestRequest request,bool back)
        {
            uint mask=back?request.Frame.Back:request.Frame.Front;
            int intensity=(int)(100*HapticPatterns.Strength(request.Frame.Amplitude));
            int duration=Math.Max(1,Math.Min(250,(int)(request.Frame.Seconds*1000)));
            text.Append("{\"Type\":\"frame\",\"Key\":\"").Append(Key(request.Slot,back))
                .Append("\",\"Frame\":{\"DurationMillis\":").Append(duration.ToString(CultureInfo.InvariantCulture))
                .Append(",\"Position\":").Append(back?"202":"201").Append(",\"Texture\":0,\"DotPoints\":[");
            bool first=true;
            for(int i=0;i<20;i++)if((mask&(1u<<i))!=0){
                if(!first)text.Append(',');first=false;
                text.Append("{\"Index\":").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",\"Intensity\":")
                    .Append(intensity.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            text.Append("],\"PathPoints\":[]}}");
        }
    }
}
