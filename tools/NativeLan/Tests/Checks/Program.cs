// Real HTTP/WebSocket transport boundary tests. RecordingGame is a test spy only;
// it deliberately implements no game rules and is never referenced by Companion.
using AshenSpire.Transport;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
var options = new LanOptions(LanTestPaths.WebRoot,"http://127.0.0.1:0",LanOptions.NewToken(),LanOptions.NewToken());
var factory = new RecordingFactory(); await using var app = LanHost.Build(options,factory); await app.StartAsync();
var httpUrl = app.Urls.Single(); var wsUrl = new Uri(httpUrl.Replace("http:","ws:")+"/lan"); int checks = 0;
void Assert(bool value,string message) { checks++; if (!value) throw new Exception(message); }
using var http = new HttpClient { BaseAddress = new Uri(httpUrl), Timeout = TimeSpan.FromSeconds(10) };
Assert((await http.GetAsync("/")).IsSuccessStatusCode,"static test Web fixture served");
Assert((await http.GetAsync("/Build/Web.wasm")).Content.Headers.ContentType?.MediaType == "application/wasm","wasm MIME");
Assert((await http.GetAsync("/../AGENTS.md")).StatusCode == System.Net.HttpStatusCode.NotFound,"outside file not served");
var info = await http.GetStringAsync("/api/lan/info"); Assert(!info.Contains(options.JoinToken)&&!info.Contains(options.HostToken),"HTTP does not disclose credentials");
await using var guest = await Peer.Open(wsUrl); await using var host = await Peer.Open(wsUrl);
async Task<JObject> Hello(Peer peer,string name,bool isHost=false,string? resume=null){await peer.Send("hello",new JObject{["joinToken"]=options.JoinToken,["name"]=name,["setup"]=new JObject{["classId"]="reaver"},["hostToken"]=isHost?options.HostToken:null,["resumeToken"]=resume},"hello");return await peer.Next(m=>(string?)m["type"]=="welcome");}
// Guests may arrive before the host; possession of a join token never grants hosting.
var gw = await Hello(guest,"Guest"); var hw = await Hello(host,"Host",true); var gid=(string)gw["payload"]!["seatId"]!;var hid=(string)hw["payload"]!["seatId"]!;var resume=(string)gw["payload"]!["resumeToken"]!;
Assert(gid!=hid&&(bool)hw["payload"]!["host"]!&&!(bool)gw["payload"]!["host"]!,"unique seats and explicit host token");
var roster=await host.Next(m=>(string?)m["type"]=="state"&&m["payload"]?["lobby"]?["seats"]?.Count()==2);Assert(!roster.ToString().Contains(resume)&&!roster.ToString().Contains(options.HostToken),"roster redacts tokens");
await guest.Send("start",new JObject{["sequence"]=1},"notHost");Assert((string?)(await guest.Next(m=>(string?)m["requestId"]=="notHost"))["payload"]?["code"]=="host_required","guest cannot start");
await host.Send("start",new JObject{["sequence"]=1},"notReady");Assert((string?)(await host.Next(m=>(string?)m["requestId"]=="notReady"))["payload"]?["code"]=="all_players_must_be_ready","readiness gate");
// A host can recover an abandoned lobby without touching started run members.
await using var abandoned=await Peer.Open(wsUrl);var aw=await Hello(abandoned,"Abandoned");var aid=(string)aw["payload"]!["seatId"]!;
await host.Send("removeSeat",new JObject{["seatId"]=aid},"removeConnected");Assert((string?)(await host.Next(m=>(string?)m["requestId"]=="removeConnected"))["payload"]?["code"]=="seat_cannot_be_removed","connected guest cannot be removed");
await abandoned.DisposeAsync();await host.Next(m=>(string?)m["type"]=="state"&&m["payload"]?["lobby"]?["seats"]?.Any(s=>(string?)s["id"]==aid&&(bool?)s["connected"]==false)==true);
await guest.Send("removeSeat",new JObject{["seatId"]=aid},"removeNotHost");Assert((string?)(await guest.Next(m=>(string?)m["requestId"]=="removeNotHost"))["payload"]?["code"]=="host_required","guest cannot remove abandoned seat");
await host.Send("removeSeat",new JObject{["seatId"]=aid},"removeAbandoned");Assert((bool?)(await host.Next(m=>(string?)m["requestId"]=="removeAbandoned"))["payload"]?["ok"]==true,"host removes disconnected guest before start");
var removedRoster=await host.Next(m=>(string?)m["type"]=="state"&&m["payload"]?["lobby"]?["seats"]?.Count()==2);Assert(!removedRoster["payload"]!["lobby"]!["seats"]!.Any(s=>(string?)s["id"]==aid),"abandoned seat leaves roster");
await host.Send("seed",new JObject{["seed"]=1234},"seed");await host.Next(m=>(string?)m["requestId"]=="seed");
await host.Send("endless",new JObject{["enabled"]=true},"endless");await host.Next(m=>(string?)m["requestId"]=="endless");
await host.Send("ready",new JObject{["ready"]=true},"readyH");await host.Next(m=>(string?)m["requestId"]=="readyH");await guest.Send("ready",new JObject{["ready"]=true},"readyG");await guest.Next(m=>(string?)m["requestId"]=="readyG");
await host.Send("start",new JObject{["sequence"]=1},"start");Assert((bool)(await host.Next(m=>(string?)m["requestId"]=="start"))["payload"]!["ok"]!,"host starts");
Assert(factory.Seed==1234&&factory.Endless&&factory.Members![0].Id==hid,"factory gets host-first native roster,seed,endless");
await guest.Send("intent",new JObject{["sequence"]=1,["intent"]=new JObject{["type"]="endTurn",["memberId"]=hid}},"spoof");Assert((string?)(await guest.Next(m=>(string?)m["requestId"]=="spoof"))["payload"]?["code"]=="actor_identity_is_server_owned","spoof rejected");Assert(factory.Game!.Commands.Count==1,"spoof never reaches native boundary");
await guest.Send("intent",new JObject{["sequence"]=1,["intent"]=new JObject{["type"]="endTurn"}},"owned");await guest.Next(m=>(string?)m["requestId"]=="owned");Assert(factory.Game.Commands.Last().Seat==gid,"authenticated seat passed independently");
var state=await host.Next(m=>(string?)m["type"]=="state"&&(int?)m["payload"]?["game"]?["commands"]==2);Assert((string?)state["payload"]?["events"]?[0]?["seat"]==gid,"authoritative event reaches other peer");
await guest.DisposeAsync();await host.Next(m=>(string?)m["type"]=="state"&&m["payload"]?["lobby"]?["seats"]?.Any(s=>(string?)s["id"]==gid&&(bool?)s["connected"]==false)==true);Assert(!factory.Game.Connected[gid],"disconnect reaches native model");
await host.Send("removeSeat",new JObject{["seatId"]=gid},"removeDuringRun");Assert((string?)(await host.Next(m=>(string?)m["requestId"]=="removeDuringRun"))["payload"]?["code"]=="run_already_started","started run seat cannot be removed");
await using var rejoined=await Peer.Open(wsUrl);var rw=await Hello(rejoined,"Ignored",resume:resume);Assert((string?)rw["payload"]?["seatId"]==gid&&(bool?)rw["payload"]?["rejoined"]==true,"valid token rejoins exact seat");Assert(factory.Game.Connected[gid],"rejoin reconnects native seat");
await rejoined.Send("intent",new JObject{["sequence"]=2,["intent"]=new JObject{["type"]="endTurn"}},"resumed");await rejoined.Next(m=>(string?)m["requestId"]=="resumed");Assert(factory.Game.Commands.Last().Seat==gid&&factory.Game.Commands.Last().Sequence==2,"rejoin sequence routed unchanged");
async Task MustClose(JObject message,string label){await using var peer=await Peer.Open(wsUrl);await peer.Raw(message.ToString());bool closed=false;try{while(true)await peer.Next(_=>true);}catch(Exception e)when(e is WebSocketException or ChannelClosedException or OperationCanceledException){closed=peer.Closed;}Assert(closed,label);}
await MustClose(new JObject{["v"]=1,["type"]="hello",["payload"]=new JObject{["joinToken"]="wrong"}},"wrong join token rejected");
await MustClose(new JObject{["v"]=1,["type"]="hello",["payload"]=new JObject{["joinToken"]=options.JoinToken,["resumeToken"]=LanOptions.NewToken()}},"wrong rejoin token rejected");
await MustClose(new JObject{["v"]=1,["type"]="hello",["payload"]=new JObject{["resumeToken"]=resume}},"connected seat cannot be taken over");
await MustClose(new JObject{["v"]=2,["type"]="hello",["payload"]=new JObject()},"protocol mismatch rejected");
await MustClose(new JObject{["v"]=1,["type"]="hello",["payload"]=new JObject{["padding"]=new string('x',options.MaximumInboundBytes)}},"oversized complete message rejected");
await using(var duplicate=await Peer.Open(wsUrl)){await duplicate.Raw("{\"v\":1,\"v\":1,\"type\":\"hello\",\"payload\":{}}");try{await duplicate.Next(_=>true);}catch{}Assert(duplicate.Closed,"duplicate JSON keys rejected");}
await host.DisposeAsync();await rejoined.DisposeAsync();
for(var i=0;i<40;i++){var status=JObject.Parse(await http.GetStringAsync("/api/lan/info"));if((int)status["connections"]! == 0)break;await Task.Delay(25);}
Assert((int)JObject.Parse(await http.GetStringAsync("/api/lan/info"))["connections"]! == 0,"all connection resources released");
await app.StopAsync();Console.WriteLine($"PASS {checks} real WebSocket transport checks (test spy boundary; no gameplay claims).");

sealed class RecordingFactory : ILanGameFactory
{
 public uint Seed;public bool Endless;public IReadOnlyList<LanMember>? Members;public RecordingGame? Game;
 public JObject ValidatePlayerSetup(JObject setup){if((string?)setup["classId"]!="reaver")throw new ArgumentException("Unknown test class");return(JObject)setup.DeepClone();}
 public ILanGame Restore(JObject snapshot,bool disconnectMembers=true)=>throw new NotSupportedException("Spy is not a persistence implementation.");
 public ILanGame Create(uint seed,bool endless,IReadOnlyList<LanMember> members){Seed=seed;Endless=endless;Members=members;return Game=new RecordingGame(members);}
}
sealed class RecordingGame(IReadOnlyList<LanMember> members) : ILanGame
{
 public readonly List<(string Seat,long Sequence)> Commands=[];public readonly Dictionary<string,bool> Connected=members.ToDictionary(m=>m.Id,_=>true);
 public JObject View(string? memberId=null)=>new(){["commands"]=Commands.Count};public JObject Snapshot()=>View();
 public JObject Execute(string seat,long sequence,JObject intent){Commands.Add((seat,sequence));return new JObject{["ok"]=true,["events"]=new JArray(new JObject{["seat"]=seat})};}
 public JObject ValidatePlayerSetup(JObject setup)=>(JObject)setup.DeepClone();public void AddMember(LanMember member)=>Connected[member.Id]=true;
 public void SetConnected(string memberId,bool connected)=>Connected[memberId]=connected;
}





