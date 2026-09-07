using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var repository=Environment.GetEnvironmentVariable("ASHENSPIRE_GAME_ROOT");
if(string.IsNullOrEmpty(repository)){var search=new DirectoryInfo(Directory.GetCurrentDirectory());while(search!=null&&!Directory.Exists(Path.Combine(search.FullName,"GameContent/Unity/Original")))search=search.Parent;repository=search?.FullName;}
if(string.IsNullOrEmpty(repository))throw new Exception("Run from the repository or set ASHENSPIRE_GAME_ROOT to its path.");
var root=Path.Combine(repository,"GameContent/Unity/Original")+Path.DirectorySeparatorChar;
var output=Path.Combine(repository,"TestResults/CoopRun");Directory.CreateDirectory(output);Directory.SetCurrentDirectory(output);
var data=JObject.Parse(File.ReadAllText(root+"content.json"));
var catalog=new OriginalContentCatalog(data.ToString());
var supplement=JObject.Parse(File.ReadAllText(root+"event-choices.json"));
var mechanics=JObject.Parse(File.ReadAllText(root+"mechanics.json"));
var progression=new AttributeProgression(JObject.Parse(File.ReadAllText(root+"progression.json")));
JObject Player(string cls){var creation=new CreationModel(catalog,cls,"pointbuy",progression);while(creation.Remaining>0)foreach(var stat in creation.Attributes().Properties().Select(p=>p.Name))if(creation.Remaining>0)creation.Adjust(stat,1);return new OriginalCharacterBuilder(catalog,progression,mechanics).Build(creation);}
if(args.Contains("--policy")){CoopPolicyChecks.Run(catalog,supplement,mechanics,progression);return;}
Console.WriteLine($"Co-op flask tiers: {CoopFlaskChecks.Run(catalog,mechanics)} checks passed");
var player=Player("reaver");
var game=new OriginalCoopRun(catalog,supplement,mechanics,17);
game.AddMember("host","Host",player);game.AddMember("guest","Guest",player);
var checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
JObject Do(string member,JObject intent,bool expected=true){var view=game.View();var seq=(long)view["party"]!.First(m=>(string)m["id"]==member)["sequence"]!+1;var result=game.Execute(member,seq,intent);Check((bool)result["ok"]! ==expected,intent+" "+result);return result;}
Do("guest",new JObject{["type"]="start"},false);Do("host",new JObject{["type"]="start"});
var first=(string)game.View()["reachableIds"]![0]!;
Check(game.Snapshot()["state"]!["members"]![0]!["run"]!["playerProjectionRules"]?["baseRules"] is JObject,"Birth freezes level-up projection rules");
Do("host",new JObject{["type"]="chooseNode",["nodeId"]=first});Check((string)game.View()["scene"]!["kind"]=="map","Wait for both votes");
Do("guest",new JObject{["type"]="chooseNode",["nodeId"]=first});
Check((int)game.View("host")["local"]!["combat"]!["entity"]!["poiseMeter"]!["max"]! == (int)game.View("host")["local"]!["run"]!["poiseThreshold"]!,"Armour poise reaches the actual party fighter");
File.WriteAllText("view-host.json",game.View("host").ToString());
var before=game.Snapshot();var restored=OriginalCoopRun.Restore(before,false);Check(JToken.DeepEquals(before,restored.Snapshot()),"Exact roundtrip");
game=OriginalCoopRun.Restore(before);Check(game.View()["party"]!.All(m=>(bool)m["connected"]! ==false),"Disk restore disconnects");game.SetConnected("host",true);game.SetConnected("guest",true);
Check(JToken.DeepEquals(before,game.Snapshot()),"Sequential host-restart rejoins preserve complete saved turn without redraw or rescaling");
var branch=game.Snapshot();game.AddMember("late","Late",player);Check(game.View()["party"]!.Count()==3,"Fresh member joins active run");Check(game.View()["scene"]!["players"]!.Count()==3,"Fresh member joins actual combat");Check((int)game.View("late")["local"]!["run"]!["fightsWon"]! ==0,"Fresh member receives no prior wins");Check(game.View("late")["local"]!["catchup"]!.Count()==0,"Fresh member receives no retroactive claims");
game=OriginalCoopRun.Restore(branch,false);
Do("host",new JObject{["type"]="playCard",["cardInstanceId"]="invalid",["targetId"]="e1"},false);
Check((bool?)game.Execute("host",0,null)["ok"]==false,"Malformed input refuses atomically");
Console.WriteLine($"{checks} smoke checks passed");
foreach(var fixture in JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"route-reference.json")))["fixtures"]!)
{
 game=new OriginalCoopRun(catalog,supplement,mechanics,(uint)fixture["seed"]!);game.AddMember("host","Host",player);game.AddMember("guest","Guest",player);Do("host",new JObject{["type"]="start"});
 var state=game.Snapshot()["state"]!;
 Check(JToken.DeepEquals(state["mapGraph"],fixture["map"]),"Original seeded shared map "+fixture["seed"]);
 Check(JToken.DeepEquals(state["sharedRng"],fixture["rng"]),"Original map RNG "+fixture["seed"]);
 var reachable=(JArray)state["reachableIds"]!;Do("host",new JObject{["type"]="chooseNode",["nodeId"]=reachable[0]!.DeepClone()});
 Check((string)game.View()["scene"]!["kind"]=="map","Original wait for votes");
 Do("guest",new JObject{["type"]="chooseNode",["nodeId"]=reachable.Last!.DeepClone()});Check((string)game.View()["cursorId"]==(string)fixture["cursorId"],"Original earliest joined tie break");
}
game=new OriginalCoopRun(catalog,supplement,mechanics,17);game.AddMember("host","Host",player);game.AddMember("guest","Guest",player);Do("host",new JObject{["type"]="start"});
var eventFixture=game.Snapshot();var eventState=eventFixture["state"]!;var eventNode=(string)eventState["reachableIds"]![0]!;
eventState["mapGraph"]!["nodes"]![eventNode]!["type"]="event";eventState["mapGraph"]!["nodes"]![eventNode]!["resolved"]=new JObject{["kind"]="event",["eventId"]="goldenMoth"};
foreach(var member in eventState["members"]!)member["run"]!["mapGraph"]=eventState["mapGraph"]!.DeepClone();
game=OriginalCoopRun.Restore(eventFixture,false);game.SetConnected("guest",false);
Do("host",new JObject{["type"]="chooseNode",["nodeId"]=eventNode});Do("host",new JObject{["type"]="eventChoice",["choiceId"]="gatherCinders"});
var queued=game.Snapshot()["state"]!["members"]![1]!;Check(((JArray)queued["catchup"]!).Count==1,"Absent event queues once");
Check(((JObject)queued["run"]!["streamCounters"]!).Properties().All(p=>(uint)p.Value==256),"Every event RNG stream reserved");
Do("host",new JObject{["type"]="eventContinue"});game.SetConnected("guest",true);
var entry=(JObject)game.View("guest")["local"]!["catchup"]![0]!;var guestBefore=game.View("guest")["local"]!["run"]!;
Do("guest",new JObject{["type"]="chooseNode",["nodeId"]=game.View()["reachableIds"]![0]!.DeepClone()},false);
Do("guest",new JObject{["type"]="resolveCatchup",["entryId"]=entry["id"]!.DeepClone(),["pick"]=new JObject{["choiceId"]="gatherCinders"}});
Check((int)game.View("guest")["local"]!["run"]!["cinders"]! == (int)guestBefore["cinders"]!+40,"Catchup grants own reward once");
var pending=game.Snapshot();var retrySequence=(long)game.View("guest")["local"]!["sequence"]!;
var duplicate=game.Execute("guest",retrySequence,new JObject{["type"]="resolveCatchup",["entryId"]=entry["id"]!.DeepClone(),["pick"]=new JObject{["choiceId"]="gatherCinders"}});
Check((bool?)duplicate["duplicate"]==true,"Exact retry is idempotent");Check(JToken.DeepEquals(pending,game.Snapshot()),"Retry leaves save byte state unchanged");
Do("guest",new JObject{["type"]="resolveCatchup",["entryId"]=entry["id"]!.DeepClone(),["pick"]=new JObject{["choiceId"]="gatherCinders"}},false);
Do("guest",new JObject{["type"]="resolveCatchup",["entryId"]=entry["id"]!.DeepClone(),["pick"]=new JObject{["continue"]=true}});
Check(game.View("guest")["local"]!["catchup"]!.Count()==0,"Catchup acknowledged and cleared");
Check(((JObject)game.Snapshot()["state"]!["members"]![1]!["run"]!["streamCounters"]!).Properties().All(p=>(uint)p.Value==256),"Catchup replay preserves reserved live RNG");
var corrupted=game.Snapshot();corrupted["state"]!["members"]![1]!["run"]!["hp"]=-1;var recovered=OriginalCoopRun.Restore(corrupted,false);Check(recovered.View()["party"]!.Count()==1,"Healthy member recovered");Check(recovered.Snapshot()["state"]!["refusedMembers"]!.Count()==1,"Corrupt member bytes preserved");
Console.WriteLine($"{checks} route oracle and queue checks passed");
foreach(var group in JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"route-reference.json")))["rewards"]!.GroupBy(r=>r["seed"]+":"+r["pool"]))
{
 var firstReward=group.First();game=new OriginalCoopRun(catalog,supplement,mechanics,(uint)firstReward["seed"]!);game.AddMember("host","Host",player);game.AddMember("guest","Guest",player);Do("host",new JObject{["type"]="start"});
 // Directly exercise the private room factory; real combat lifecycle is covered
 // by the three-act fixture below. This isolates original random draw ordering.
 typeof(OriginalCoopRun).GetMethod("EnterRewards",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(game,new object[]{(string)firstReward["pool"]!});
 var state=game.Snapshot()["state"]!;
 foreach(var reward in group)
 {
  var member=state["members"]![(int)reward["index"]!]!;var offer=state["scene"]!["offers"]![(string)member["id"]!]!;
  foreach(var key in new[]{"cards","cinders","flaskId","relicId"})Check(offer[key]?.ToString(Newtonsoft.Json.Formatting.None)==reward[key]?.ToString(Newtonsoft.Json.Formatting.None),"Original cooperative reward "+key+" "+group.Key);
  Check(JToken.DeepEquals(member["run"]!["streamCounters"],reward["rng"]),"Original cooperative reward stream counters "+group.Key);
 }
}
Console.WriteLine($"{checks} original reward checks passed");
if(args.Contains("--focused"))return;
// Controlled combat fixture lowers authored enemy HP only. Commands still use
// the real card/target/cost engine; there is no production auto-win operation.
var fast=(JObject)data.DeepClone();foreach(var enemy in fast["enemies"]!)enemy["hp"]=new JArray(1,1);
catalog=new OriginalContentCatalog(fast.ToString());
game=new OriginalCoopRun(catalog,supplement,mechanics,17);game.AddMember("host","Host",Player("reaver"));game.AddMember("guest","Guest",Player("reaver"));Do("host",new JObject{["type"]="start"});
int commands=0;var visited=new HashSet<string>();
while((string)game.View()["scene"]!["kind"]! !="complete" && commands++<800)
{
 var view=game.View();var scene=(JObject)game.Snapshot()["state"]!["scene"]!; if((string)scene["kind"]=="combat") scene=(JObject)view["scene"]!;var kind=(string)scene["kind"]!;visited.Add(kind);
 if(kind=="map") {var reachable=view["reachableIds"]!.Values<string>().ToArray();var next=reachable.OrderBy(n=> (string)view["map"]!["nodes"]![n]! ["type"]=="merchant"?0:(string)view["map"]!["nodes"]![n]!["type"]=="shrine"?1:2).First();foreach(var id in new[]{"host","guest"})Do(id,new JObject{["type"]="chooseNode",["nodeId"]=next});}
 else if(kind=="combat")
 {
  var active=scene["players"]!.First(p=>(bool)p["connected"]! && (bool)p["entity"]!["alive"]! && (bool)p["ended"]! ==false);var id=(string)active["id"]!;
  var local=game.View(id)["local"]!;var hand=(JArray)local["hand"]!;bool played=false;
  foreach(var row in hand)
  {
   var target=(bool)row["targets"]!["active"]! ? (string)row["targets"]!["legalIds"]!.FirstOrDefault() : (string)scene["enemies"]!.FirstOrDefault(e=>(bool)e["alive"]!)?["id"];
   var seq=(long)local["sequence"]!+1;var result=game.Execute(id,seq,new JObject{["type"]="playCard",["cardInstanceId"]=row["instance"]!["instanceId"]!.DeepClone(),["targetId"]=target});
   if((bool)result["ok"]!){checks++;played=true;break;}
  }
  if(!played)Do(id,new JObject{["type"]="endTurn"});
 }
 else if(kind=="rewards")foreach(var id in new[]{"host","guest"}){if((bool?)scene["done"]?[id]==true)continue;Do(id,new JObject{["type"]="chooseReward",["cardId"]=null,["takeRelic"]=false});}
 else if(kind=="shop")foreach(var id in new[]{"host","guest"})Do(id,new JObject{["type"]="leaveShop"});
 else if(kind=="shrine")foreach(var id in new[]{"host","guest"})Do(id,new JObject{["type"]="shrineChoice",["choice"]="leave"});
 else if(kind=="event")
 {
  foreach(var id in new[]{"host","guest"})
  {
   if(scene["next"]!=null){Do(id,new JObject{["type"]="eventContinue"});continue;}
   var choices=scene["choices"]![id]!.OfType<JObject>();var choice=choices.First(c=>((int?)c["requires"]?["cinders"]??0)==0 && !c["effects"]!.Any(e=>(string)e["op"]=="loseHp"||(string)e["op"]=="loseMaxHpPct")) ;
   Do(id,new JObject{["type"]="eventChoice",["choiceId"]=choice["id"]!.DeepClone()});
  }
 }
 else throw new Exception("Unhandled "+kind);
}
Check((string)game.View()["scene"]!["result"]=="victory","Three-act real-combat fixture completion");
Check((int)game.View()["actNumber"]! ==3,"Complete at authored last act");
File.WriteAllText("complete-fixture.json",new JObject{["checks"]=checks,["commands"]=commands,["rooms"]=new JArray(visited),["result"]=game.View()}.ToString());Console.WriteLine($"{checks} checks, {commands} command batches; three-act victory");






