using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CulticVR.OpenXRControllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

int checks=0;
void Check(bool c,string text){checks++;if(!c)throw new Exception(text);}
var sb=new StringBuilder();
foreach(var kind in Enum.GetValues<IncomingHapticKind>())foreach(var side in Enum.GetValues<ImpactSide>()){
    var frame=HapticPatterns.Incoming(kind,side,3,1);
    if(frame.Front==0&&frame.Back==0)continue;
    using var packet=JsonDocument.Parse(VestPackets.Frame(new VestRequest(frame,0,0,7),sb));
    var submits=packet.RootElement.GetProperty("Submit");
    Check(submits.GetArrayLength()==(kind is IncomingHapticKind.Explosion or IncomingHapticKind.Burn or IncomingHapticKind.GroundShock or IncomingHapticKind.Irritation?2:1),"Panel batching");
    foreach(var submit in submits.EnumerateArray()){
        int position=submit.GetProperty("Frame").GetProperty("Position").GetInt32();
        Check(position is 201 or 202,"Legacy X40 panel position");
        var dots=submit.GetProperty("Frame").GetProperty("DotPoints");
        Check(dots.GetArrayLength()==(kind switch { IncomingHapticKind.Explosion=>20,IncomingHapticKind.Shotgun=>5,IncomingHapticKind.HeavyImpact=>8,IncomingHapticKind.Melee or IncomingHapticKind.Energy or IncomingHapticKind.GroundShock or IncomingHapticKind.Burn or IncomingHapticKind.Acid or IncomingHapticKind.Grip or IncomingHapticKind.Irritation=>4,_=>1 }),"Exact simultaneous point count");
        foreach(var dot in dots.EnumerateArray())Check(dot.GetProperty("Index").GetInt32() is >=0 and <20&&dot.GetProperty("Intensity").GetInt32() is >0 and <=100,"X40 motor/intensity bounds");
    }
    if(submits.GetArrayLength()==2)Check(submits[0].GetProperty("Key").GetString()!=submits[1].GetProperty("Key").GetString(),"Concurrent panels require distinct effect keys");
}
var explosion=HapticPatterns.Incoming(IncomingHapticKind.Explosion,ImpactSide.Unknown,0,1);
// Pickup at the user's 90% slider: preserve localization through real serialization.
using(var packet=JsonDocument.Parse(VestPackets.Frame(new VestRequest(new VestMotorFrame(1536,0,.4f*.9f,.14f),0,0,8),sb))){
    var submits=packet.RootElement.GetProperty("Submit");
    Check(submits.GetArrayLength()==1,"Pickup emits only one panel");
    var frame=submits[0].GetProperty("Frame");
    Check(frame.GetProperty("Position").GetInt32()==201&&frame.GetProperty("DurationMillis").GetInt32()==140,"Pickup front duration survives serializer");
    var dots=frame.GetProperty("DotPoints");
    Check(dots.GetArrayLength()==2&&dots.EnumerateArray().Select(d=>d.GetProperty("Index").GetInt32()).Order().SequenceEqual(new[]{9,10}),"Only two pickup motors reach Player");
    Check(dots.EnumerateArray().All(d=>d.GetProperty("Intensity").GetInt32()==36),"Pickup intensity respects user slider in Player packet");
}
// Strong wall impact at Dustin's 90% vest setting: both full panels must
// survive the real serializer as a 250ms frame, with independent keys.
using(var packet=JsonDocument.Parse(VestPackets.Frame(new VestRequest(new VestMotorFrame(VestMotorFrame.All,VestMotorFrame.All,.9f,.25f),0,0,9),sb))){
    var submits=packet.RootElement.GetProperty("Submit");
    Check(submits.GetArrayLength()==2,"Strong wall impact serializes both panels");
    var keys=new HashSet<string>();var positions=new HashSet<int>();
    foreach(var submit in submits.EnumerateArray()){
        keys.Add(submit.GetProperty("Key").GetString()!);var frame=submit.GetProperty("Frame");
        positions.Add(frame.GetProperty("Position").GetInt32());
        Check(frame.GetProperty("DurationMillis").GetInt32()==250,"Strong wall pulse lasts 250ms");
        var dots=frame.GetProperty("DotPoints");
        Check(dots.GetArrayLength()==20,"Every motor on each wall-impact panel included");
        Check(dots.EnumerateArray().All(d=>d.GetProperty("Intensity").GetInt32()==90),"Wall intensity reaches Player at the full user slider value");
    }
    Check(keys.Count==2&&positions.SetEquals(new[]{201,202}),"Distinct front/back wall effect keys and positions");
}
string first=Encoding.UTF8.GetString(VestPackets.Frame(new VestRequest(explosion,0,0,1),sb));
string second=Encoding.UTF8.GetString(VestPackets.Frame(new VestRequest(explosion,0,0,2),sb));
Check(first!=second,"Concurrent hit identities retain separate effect slots");
using(var stop=JsonDocument.Parse(VestPackets.Stop)){
    var keys=new HashSet<string>();foreach(var entry in stop.RootElement.GetProperty("Submit").EnumerateArray()){
        Check(entry.GetProperty("Type").GetString()=="turnOff","Owned-key stop only");
        string key=entry.GetProperty("Key").GetString()!;Check(key.StartsWith("culticvr_",StringComparison.Ordinal)&&keys.Add(key),"Distinct mod-owned stop key");
    }
    Check(keys.Count==132&&keys.Contains("culticvr_heartbeat_f")&&keys.Contains("culticvr_heartbeat_b"),"Every impact/recoil/heartbeat key has a bounded stop");
}
var queue=new VestRequestQueue();
for(int i=0;i<80;i++)queue.Enqueue(new VestRequest(explosion,100,0,i));
int count=0;while(queue.Take(100,out var request)){Check(request.Slot>=48,"Overflow keeps most recent requests");count++;}
Check(count==32,"Bounded capacity");queue.Enqueue(new VestRequest(explosion,1,0,0));
Check(!queue.Take(Stopwatch.Frequency,out _),"Expired event dropped");queue.Enqueue(new VestRequest(explosion,100,0,0));Check(!queue.Take(99,out _),"Invalid clock age dropped");
try{new VestHapticClient(new Uri("ws://example.com"));throw new Exception("Remote accepted");}catch(ArgumentException){}

// A fresh loopback mock port, not the real Player port. No physical device route.
var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var app=builder.Build();app.UseWebSockets();var packets=new ConcurrentQueue<string>();
app.Run(async context=>{
    using var socket=await context.WebSockets.AcceptWebSocketAsync();var buffer=new byte[20000];
    try{while(socket.State==WebSocketState.Open){
        using var message=new MemoryStream();WebSocketReceiveResult result;
        do{result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),CancellationToken.None);if(result.MessageType==WebSocketMessageType.Close)return;message.Write(buffer,0,result.Count);}while(!result.EndOfMessage);
        packets.Enqueue(Encoding.UTF8.GetString(message.ToArray()));
    }}catch(WebSocketException){}
});
await app.StartAsync();string address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var client=new VestHapticClient(new Uri(address.Replace("http:","ws:")));
async Task Until(Func<bool> predicate){var deadline=Stopwatch.StartNew();while(!predicate()){if(deadline.ElapsedMilliseconds>4000)throw new Exception("Mock timeout");await Task.Delay(10);}}
client.SetEnabled(true);await Until(()=>client.Connected&&!packets.IsEmpty);while(packets.TryDequeue(out _)){}
client.Enqueue(explosion,false);await Until(()=>packets.Any(p=>p.Contains("\"frame\"")));
using(var actual=JsonDocument.Parse(packets.First(p=>p.Contains("\"frame\""))))Check(actual.RootElement.GetProperty("Submit").GetArrayLength()==2,"Production client batches both explosion panels");
client.EnqueueHeartbeat(new VestMotorFrame(544,0,.45f,.08f));
await Until(()=>packets.Any(p=>p.Contains("\"culticvr_heartbeat_f\"")&&p.Contains("\"frame\"")));
using(var actual=JsonDocument.Parse(packets.First(p=>p.Contains("\"culticvr_heartbeat_f\"")&&p.Contains("\"frame\"")))){
    var submit=actual.RootElement.GetProperty("Submit");Check(submit.GetArrayLength()==1,"Heartbeat stays on front panel");
    var frame=submit[0].GetProperty("Frame");Check(frame.GetProperty("Position").GetInt32()==201&&frame.GetProperty("DurationMillis").GetInt32()==80,"Heartbeat finite front-chest frame");
    Check(frame.GetProperty("DotPoints").EnumerateArray().Select(x=>x.GetProperty("Index").GetInt32()).SequenceEqual(new[]{5,9}),"Heartbeat does not overwrite recoil or damage keys");
}
client.Stop();await Until(()=>packets.Any(p=>p.Contains("turnOff")));
client.SetEnabled(false);await Until(()=>!client.Connected);while(packets.TryDequeue(out _)){}
for(int i=0;i<1000;i++)client.Enqueue(explosion,false);
await Task.Delay(80);Check(packets.IsEmpty,"Disabled queue never sends feedback");
client.SetEnabled(true);await Until(()=>client.Connected);await Task.Delay(80);
Check(!packets.Any(p=>p.Contains("\"frame\"")),"Disabled events never replay on reconnect");
client.Dispose();await client.Completion.WaitAsync(TimeSpan.FromSeconds(6));await app.StopAsync();
var missing=new VestHapticClient(new Uri("ws://127.0.0.1:1"));var watch=Stopwatch.StartNew();
missing.SetEnabled(true);for(int i=0;i<10000;i++)missing.Enqueue(explosion,false);missing.Dispose();
Check(watch.ElapsedMilliseconds<250,"Unavailable Player does not block simulation thread");await missing.Completion.WaitAsync(TimeSpan.FromSeconds(6));
Console.WriteLine($"PASS {checks} vest packet/queue/loopback contracts. No real Player connection or physical feedback.");
