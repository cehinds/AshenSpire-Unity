// Real native cooperative run through two independent WebSocket clients.
using AshenSpire.Transport;
using AshenSpire.Companion;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Hosting;

var options=new LanOptions(LanTestPaths.WebRoot,"http://127.0.0.1:0",LanOptions.NewToken(),LanOptions.NewToken());
var factory=new OriginalLanGameFactory(LanTestPaths.ContentRoot);
await using var app=LanHost.Build(options,factory);await app.StartAsync();
var endpoint=new Uri(app.Urls.Single().Replace("http:","ws:")+"/lan");var checks=0;var commands=0;
void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
void Reject(JObject setup,string label){bool bad=false;try{factory.ValidatePlayerSetup(setup);}catch(ArgumentException){bad=true;}Check(bad,label);}
Reject(new JObject{["classId"]="reaver",["modeId"]="standard",["hp"]=999},"client HP refused");
Reject(new JObject{["classId"]="reaver",["modeId"]="standard",["profileMeta"]=new JObject()},"client unlocks refused");
Reject(new JObject{["classId"]="reaver",["modeId"]="pointbuy",["attributes"]=new JObject{["strength"]=15,["dexterity"]=15,["constitution"]=15,["wisdom"]=15,["intelligence"]=15}},"overbudget rejected");
Reject(new JObject{["classId"]="reaver",["modeId"]="pointbuy",["attributes"]=new JObject{["strength"]=10.5}},"partial/fractional attrs refused");
// Lean scale: all 1s plus 3 points, maximum 4, total 8. "lean" is Assign points; "leanStandard" (the default) is Standard.
JObject Lean(int str,int dex,int con,int wis,int intel)=>new JObject{["strength"]=str,["dexterity"]=dex,["constitution"]=con,["wisdom"]=wis,["intelligence"]=intel};
Reject(new JObject{["classId"]="reaver",["modeId"]="lean",["attributes"]=Lean(4,4,4,4,4)},"lean all 4s refused");
Reject(new JObject{["classId"]="reaver",["modeId"]="lean",["attributes"]=Lean(5,1,1,1,1)},"lean attribute above maximum refused");
var leanSetup=factory.ValidatePlayerSetup(new JObject{["classId"]="reaver",["modeId"]="lean",["attributes"]=Lean(3,1,2,1,1)});
Check((string?)leanSetup["modeId"]=="lean"&&JToken.DeepEquals(leanSetup["attributes"],Lean(3,1,2,1,1)),"lean {3,1,2,1,1} accepted");
string? unspent=null;try{factory.ValidatePlayerSetup(new JObject{["classId"]="reaver",["modeId"]="lean"});}catch(ArgumentException error){unspent=error.Message;}
Check(unspent!=null&&unspent.Contains("Spend all points"),"Assign points setup without attributes refused: "+unspent);
// Standard (the default): no attributes needed, the class preset is used; the player may still move points.
var defaultSetup=factory.ValidatePlayerSetup(new JObject{["classId"]="starseer"});
Check((string?)defaultSetup["modeId"]=="leanStandard"&&JToken.DeepEquals(defaultSetup["attributes"],Lean(1,1,1,2,3)),"default mode is Standard with the Starseer preset");
var standardSetup=factory.ValidatePlayerSetup(new JObject{["classId"]="reaver",["modeId"]="leanStandard"});
Check(JToken.DeepEquals(standardSetup["attributes"],Lean(3,1,2,1,1)),"Standard setup with no attributes accepted on the Reaver preset");
var movedSetup=factory.ValidatePlayerSetup(new JObject{["classId"]="reaver",["modeId"]="leanStandard",["attributes"]=Lean(2,2,2,1,1)});
Check(JToken.DeepEquals(movedSetup["attributes"],Lean(2,2,2,1,1)),"Standard setup with a moved point accepted");
Reject(new JObject{["classId"]="reaver",["modeId"]="leanStandard",["attributes"]=Lean(4,1,2,1,1)},"Standard overspend refused");
await using var host=await Peer.Open(endpoint);await using var guest=await Peer.Open(endpoint);
async Task<JObject> Hello(Peer peer,string cls,bool hosting){await peer.Send("hello",new JObject{["joinToken"]=options.JoinToken,["hostToken"]=hosting?options.HostToken:null,["name"]=cls,["setup"]=new JObject{["classId"]=cls,["modeId"]="standard"}},"hello");return(JObject)(await peer.Next(m=>(string?)m["type"]=="welcome"))["payload"]!;}
var hw=await Hello(host,"reaver",true);var gw=await Hello(guest,"starseer",false);var hid=(string)hw["seatId"]!;var gid=(string)gw["seatId"]!;
async Task<JObject> Wire(Peer peer,string type,JObject payload){var id=Guid.NewGuid().ToString("N");await peer.Send(type,payload,id);return await peer.Next(m=>(string?)m["requestId"]==id);}
async Task<JObject> View(Peer peer){await Wire(peer,"resync",new JObject());return(JObject)(await peer.Next(m=>(string?)m["type"]=="state"))["payload"]!["game"]!;}
async Task<JObject> Intent(Peer peer,JObject intent,bool expected=true,long? sequence=null){var view=await View(peer);var result=(JObject)(await Wire(peer,"intent",new JObject{["sequence"]=sequence??(long)view["local"]!["sequence"]!+1,["intent"]=intent}))["payload"]!;Check((bool?)result["ok"]==expected,"receipt "+result);if(expected)commands++;return result;}
await Wire(host,"seed",new JObject{["seed"]=17});await Wire(host,"ready",new JObject{["ready"]=true});await Wire(guest,"ready",new JObject{["ready"]=true});
Check((bool?)(await Wire(host,"start",new JObject{["sequence"]=1}))["payload"]?["ok"]==true,"native start");
var hv=await View(host);var gv=await View(guest);Check((string?)hv["local"]?["id"]==hid&&(string?)gv["local"]?["id"]==gid,"private local views");
var node=(string)hv["reachableIds"]![0]!;await Intent(host,new JObject{["type"]="chooseNode",["nodeId"]=node});Check((string?)(await View(host))["scene"]?["kind"]=="map","vote waits");await Intent(guest,new JObject{["type"]="chooseNode",["nodeId"]=node});
hv=await View(host);gv=await View(guest);Check((string?)hv["scene"]?["kind"]=="combat"&&JToken.DeepEquals(hv["scene"]!["enemies"],gv["scene"]!["enemies"]),"shared native encounter");Check(hv["local"]?["hand"]?.Count()>0&&gv["local"]?["hand"]?.Count()>0,"resolved native hands");
var before=(JObject)hv.DeepClone();await Intent(host,new JObject{["type"]="playCard",["cardInstanceId"]="missing",["targetId"]="e1"},false);hv=await View(host);Check(JToken.DeepEquals(before,hv),"invalid card atomic refusal");
var card=((JArray)hv["local"]!["hand"]!).OfType<JObject>().First(c=>(int)c["cost"]!["action"]!<=3);var target=(string?)card["targets"]?["legalIds"]?.FirstOrDefault()??"e1";
var play=new JObject{["type"]="playCard",["cardInstanceId"]=card["instance"]!["instanceId"]!.DeepClone(),["targetId"]=target};
var receipt=await Intent(host,play);var frozen=await View(host);var duplicate=await Intent(host,play,true,(long)receipt["sequence"]!);Check((bool?)duplicate["duplicate"]==true&&JToken.DeepEquals(frozen,await View(host)),"exact retry idempotent");await Intent(host,new JObject{["type"]="endTurn"},false,(long)receipt["sequence"]!);
await using var late=await Peer.Open(endpoint);var lw=await Hello(late,"rogue",false);var lv=await View(late);Check(lv["party"]?.Count()==3&&(string?)lv["local"]?["run"]?["classId"]=="rogue"&&lv["local"]?["hand"]?.Count()>0,"fresh late seat joins actual active combat");Check((int?)lv["local"]?["run"]?["fightsWon"]==0&&lv["local"]?["catchup"]?.Count()==0,"late seat has no retroactive rewards");
for(var step=0;step<160;step++){
 hv=await View(host);if((string?)hv["scene"]?["kind"]!="combat")break;
 foreach(var peer in new[]{host,guest,late}){
  var view=await View(peer);if((string?)view["scene"]?["kind"]!="combat")break;if((bool?)view["local"]?["combat"]?["ended"]==true)continue;
  var body=view["local"]!["combat"]!["entity"]!;
  var hand=((JArray)view["local"]!["hand"]!).OfType<JObject>().Where(c=>(int)c["cost"]!["action"]! <= (int)body["energy"]! && (int)c["cost"]!["mana"]! <= (int)body["mana"]! && (int)c["cost"]!["stamina"]! <= (int)body["stamina"]!).OrderByDescending(c=>(string?)c["card"]?["type"]=="attack").ToArray();
  if(hand.Length==0){await Intent(peer,new JObject{["type"]="endTurn"});continue;}
  var chosen=hand[0];var enemy=view["scene"]!["enemies"]!.FirstOrDefault(e=>(bool?)e["alive"]==true);var targetId=(string?)chosen["targets"]?["legalIds"]?.FirstOrDefault()??(string?)enemy?["id"];
  await Intent(peer,new JObject{["type"]="playCard",["cardInstanceId"]=chosen["instance"]!["instanceId"]!.DeepClone(),["targetId"]=targetId});
 }
}
hv=await View(host);Check((string?)hv["scene"]?["kind"]=="rewards","real encounter completed");
await guest.DisposeAsync();await host.Next(m=>(string?)m["type"]=="state"&&m["payload"]?["lobby"]?["seats"]?.Any(s=>(string?)s["id"]==gid&&(bool?)s["connected"]==false)==true);
await using var rejoined=await Peer.Open(endpoint);await rejoined.Send("hello",new JObject{["resumeToken"]=gw["resumeToken"]!.DeepClone()},"resume");var welcome=await rejoined.Next(m=>(string?)m["type"]=="welcome");Check((string?)welcome["payload"]?["seatId"]==gid,"token-only reload");var resumed=await View(rejoined);Check((string?)resumed["local"]?["run"]?["classId"]=="starseer"&&(long)resumed["local"]!["sequence"]!>1,"real member inventory/sequence retained");
File.WriteAllText(Path.Combine(LanTestPaths.OutputRoot,"native-receipt.json"),new JObject{["checks"]=checks,["acceptedCommands"]=commands,["scene"]=hv["scene"]!["kind"]!.DeepClone(),["transport"]="two real ClientWebSocket peers",["gameRules"]="OriginalCoopRun + OriginalCoopCombat",["browserEvidence"]=false}.ToString());
Console.WriteLine($"PASS {checks} real native co-op WebSocket checks; {commands} accepted/retry commands; encounter rewards reached.");
await host.DisposeAsync();await rejoined.DisposeAsync();await late.DisposeAsync();
await Task.Delay(150); var start = new System.Diagnostics.ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }; start.ArgumentList.Add(LanTestPaths.BridgeHarness); start.Environment["AS_LAN_JSLIB"] = LanTestPaths.JsLib; start.Environment["LAN_URL"] = endpoint.ToString(); start.Environment["HOST_RESUME"] = (string)hw["resumeToken"]!; start.Environment["GUEST_RESUME"] = (string)gw["resumeToken"]!; using var bridge = System.Diagnostics.Process.Start(start)!; var output = await bridge.StandardOutput.ReadToEndAsync(); var errors = await bridge.StandardError.ReadToEndAsync(); await bridge.WaitForExitAsync(); Check(bridge.ExitCode == 0,"Bridge harness failed: " + errors); Console.Write(output);
await app.StopAsync();


