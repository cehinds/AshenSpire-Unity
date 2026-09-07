// Abrupt companion PROCESS restart with two real sockets and native game state.
using AshenSpire.Transport;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
var directory=Path.Combine(LanTestPaths.OutputRoot,"Persistence");Directory.CreateDirectory(directory);var state=Path.Combine(directory,"host-state.json");var checks=0;
void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
async Task<JObject> Wire(Peer peer,string type,JObject payload){var id=Guid.NewGuid().ToString("N");await peer.Send(type,payload,id);return await peer.Next(m=>(string?)m["requestId"]==id);}
async Task<JObject> View(Peer peer){await Wire(peer,"resync",new JObject());return(JObject)(await peer.Next(m=>(string?)m["type"]=="state"))["payload"]!["game"]!;}
async Task<JObject> Intent(Peer peer,JObject intent,long? sequence=null){var view=await View(peer);var result=(JObject)(await Wire(peer,"intent",new JObject{["sequence"]=sequence??(long)view["local"]!["sequence"]!+1,["intent"]=intent}))["payload"]!;Check((bool?)result["ok"]==true,"native accepted command");return result;}
await using var server=await Server.Start(state);
bool locked=false;try{await using var duplicateServer=await Server.Start(state);}catch(InvalidOperationException e){locked=e.Message.Contains("host_state_in_use");}Check(locked,"second companion cannot own same save");
await using var host=await Peer.Open(server.Endpoint);await using var guest=await Peer.Open(server.Endpoint);
async Task<JObject> Hello(Peer peer,string name,bool hosting){await peer.Send("hello",new JObject{["joinToken"]=server.JoinToken,["hostToken"]=hosting?server.HostToken:null,["name"]=name,["setup"]=new JObject{["classId"]=name,["modeId"]="standard"}},"hello");return(JObject)(await peer.Next(m=>(string?)m["type"]=="welcome"))["payload"]!;}
var hw=await Hello(host,"reaver",true);var gw=await Hello(guest,"starseer",false);
await Wire(host,"seed",new JObject{["seed"]=17});await Wire(host,"ready",new JObject{["ready"]=true});await Wire(guest,"ready",new JObject{["ready"]=true});Check((bool?)(await Wire(host,"start",new JObject{["sequence"]=1}))["payload"]?["ok"]==true,"started persisted run");
var hv=await View(host);var node=(string)hv["reachableIds"]![0]!;await Intent(host,new JObject{["type"]="chooseNode",["nodeId"]=node});await Intent(guest,new JObject{["type"]="chooseNode",["nodeId"]=node});
hv=await View(host);var card=((JArray)hv["local"]!["hand"]!).OfType<JObject>().First();var play=new JObject{["type"]="playCard",["cardInstanceId"]=card["instance"]!["instanceId"]!.DeepClone(),["targetId"]=(string?)card["targets"]?["legalIds"]?.FirstOrDefault()??"e1"};var result=await Intent(host,play);hv=await View(host);var gv=await View(guest);
Check(File.Exists(state)&&File.Exists(state+".backup"),"primary and backup exist");Check(LanStateStore.Load(state)?["game"] is JObject,"complete native snapshot stored");
await server.DisposeAsync(); // Exact owned process killed, no graceful disconnect saves.
await host.DisposeAsync();await guest.DisposeAsync();
await using var restarted=await Server.Start(state);Check(restarted.JoinToken==server.JoinToken&&restarted.HostToken==server.HostToken,"host identity retained across process restart");
await using var rh=await Peer.Open(restarted.Endpoint);await using var rg=await Peer.Open(restarted.Endpoint);
async Task Rejoin(Peer peer,JObject welcome){await peer.Send("hello",new JObject{["resumeToken"]=welcome["resumeToken"]!.DeepClone()},"rejoin");var current=await peer.Next(m=>(string?)m["type"]=="welcome");Check((string?)current["payload"]?["seatId"]==(string?)welcome["seatId"],"same seat after process restart");}
await Rejoin(rh,hw);await Rejoin(rg,gw);var resumedH=await View(rh);var resumedG=await View(rg);
File.WriteAllText(Path.Combine(directory,"before-host.json"),hv.ToString());File.WriteAllText(Path.Combine(directory,"after-host.json"),resumedH.ToString());
Check(JToken.DeepEquals(hv["local"]!["hand"],resumedH["local"]!["hand"]),"host hand/cost/targets unchanged after process restart");Check(JToken.DeepEquals(gv["local"]!["hand"],resumedG["local"]!["hand"]),"guest hand unchanged after process restart");Check(JToken.DeepEquals(hv["scene"]!["enemies"],resumedH["scene"]!["enemies"]),"enemy state unchanged after restart");Check((long)resumedH["local"]!["sequence"]! == (long)result["sequence"]!,"accepted sequence persisted before ack");
var duplicate=await Intent(rh,play,(long)result["sequence"]!);Check((bool?)duplicate["duplicate"]==true,"pre-crash command retry remains idempotent");Check(JToken.DeepEquals(resumedH["local"]!["combat"],(await View(rh))["local"]!["combat"]),"retry does not double-pay resources");
await restarted.DisposeAsync();await rh.DisposeAsync();await rg.DisposeAsync();
// Corrupt this test-owned primary deliberately; startup must preserve it and fail.
var preserved=File.ReadAllText(state);File.WriteAllText(state,"broken-test-primary");bool refused=false;try{await using var broken=await Server.Start(state);}catch(InvalidOperationException e){refused=e.Message.Contains("host_state_corrupt");}Check(refused&&File.ReadAllText(state)=="broken-test-primary","corrupt primary fails closed without reset");
var evidence=LanStateStore.RecoverBackup(state);Check(File.ReadAllText(evidence)=="broken-test-primary","explicit backup recovery preserves corrupt evidence");Check(LanStateStore.Load(state)?["game"] is JObject,"verified backup becomes readable primary");
await using var recovered=await Server.Start(state);await using var peer=await Peer.Open(recovered.Endpoint);await Rejoin(peer,hw);Check((string?)(await View(peer))["local"]?["run"]?["classId"]=="reaver","backup retains rejoin identity and real run");
await peer.DisposeAsync();await recovered.DisposeAsync();
File.WriteAllText(Path.Combine(directory,"receipt.json"),new JObject{["checks"]=checks,["actualCompanionProcessRestarts"]=2,["abruptKill"]=true,["corruptionFailsClosed"]=true,["explicitBackupRecovery"]=true,["browserEvidence"]=false,["bundledContent"]=Environment.GetEnvironmentVariable("NATIVE_COMPANION_CONTENT")!=null}.ToString());Console.WriteLine($"PASS {checks} actual companion process-restart/persistence checks. Receipt: {directory}");

sealed class Server : IAsyncDisposable
{
 public Process Process=null!;public Uri Endpoint=null!;public string JoinToken="",HostToken="";private bool _disposed;private Task<string> _errors=null!;
 public static async Task<Server> Start(string state){var server=new Server();var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("NATIVE_COMPANION_EXE") ?? "dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};foreach(var value in new[]{LanTestPaths.CompanionDll,"--web-root",LanTestPaths.WebRoot,"--content-root",Environment.GetEnvironmentVariable("NATIVE_COMPANION_CONTENT") ?? LanTestPaths.ContentRoot,"--url","http://127.0.0.1:0","--state",state})if (Environment.GetEnvironmentVariable("NATIVE_COMPANION_EXE") == null || !value.EndsWith("AshenSpire.Companion.dll")) info.ArgumentList.Add(value);server.Process=Process.Start(info)!;server._errors=server.Process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));try{while(true){var line=await server.Process.StandardOutput.ReadLineAsync(timeout.Token);if(line==null)throw new InvalidOperationException(await server._errors);if(line.StartsWith("Native companion: "))server.Endpoint=new Uri(line[18..].Replace("http:","ws:")+"/lan");if(line.StartsWith("Join token: "))server.JoinToken=line[12..];if(line.StartsWith("Host token: "))server.HostToken=line[12..];if(line.StartsWith("Successful intents"))return server;}}catch{await server.DisposeAsync();throw;}}
 public async ValueTask DisposeAsync(){if(_disposed)return;_disposed=true;if(!Process.HasExited)Process.Kill(true);await Process.WaitForExitAsync();Process.Dispose();}
}




