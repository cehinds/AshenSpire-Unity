// NativeGamePlaytest — real public game commands under a deterministic greedy policy.
// This diagnoses integration and resource use; a bot is not evidence of fun or mobile usability.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
var root=Directory.GetCurrentDirectory();
var output=args.Length>1?Path.GetFullPath(args[1]):Path.Combine(root,"TestResults/NativePolicy");Directory.CreateDirectory(output);
var directory=Path.Combine(root,"GameContent/Unity/Original");
var catalog=new OriginalContentCatalog(File.ReadAllText(Path.Combine(directory,"content.json")));
var progression=new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(directory,"progression.json"))));
var mechanics=JObject.Parse(File.ReadAllText(Path.Combine(directory,"mechanics.json")));
var supplement=JObject.Parse(File.ReadAllText(Path.Combine(directory,"event-choices.json")));
var seeds=args.Length>0?int.Parse(args[0]):3;
var report=new JArray();var timer=Stopwatch.StartNew();
var classes=new[]{"reaver","starseer","rogue","herald"};
var sourceFiles=Directory.GetFiles(Path.Combine(root,"Unity/Assets/AshenSpire/Runtime/Domain/Original"),"*.cs").OrderBy(x=>x).ToArray();
var digest=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",sourceFiles.Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))))))).ToLowerInvariant();
foreach(var classId in classes)for(uint seed=1;seed<=seeds;seed++)
{
 var result=new JObject{["classId"]=classId,["seed"]=seed};var counts=new JObject();var trace=new JArray();var phaseCounts=new JObject();var seenServices=new HashSet<string>();var rejectionPhases=new HashSet<OriginalRunPhase>();int commands=0,resumeChecks=0;OriginalGameSession game=null;string lastCommand="create";
 try
 {
  var creator=new CreationModel(catalog,classId,"standard",progression);var kit=(string)catalog.Table("equipment.startingKits").First(x=>(string)x["classId"]==classId&&(bool?)x["baseline"]==true)["id"];
  var player=new OriginalCharacterBuilder(catalog,progression,mechanics).Build(creator,kit);result["initialAttributes"]=player["attributes"].DeepClone();result["initialResources"]=new JObject{["hp"]=player["hp"],["mana"]=player["mana"],["stamina"]=player["stamina"],["actions"]=player["energy"],["draw"]=player["draw"]};
  game=OriginalGameSession.Start(catalog,supplement,mechanics,player,seed);
  void Act(string name,Action<OriginalGameSession> command)
  {
   lastCommand=name;var before=game.Snapshot();var testResume=commands%25==0||name.StartsWith("enter")||name=="continueRewards";OriginalGameSession resumed=null;
   if(testResume)resumed=OriginalGameSession.Restore(JObject.Parse(before.ToString(Newtonsoft.Json.Formatting.None)));
   var action=new JObject{["command"]=name,["beforePhase"]=game.Phase.ToString(),["beforeHp"]=game.Player["hp"], ["targetId"]=(string)game.Enemies.OfType<JObject>().Where(e=>(bool?)e["alive"]==true).OrderBy(e=>(int)e["hp"]).FirstOrDefault()?["id"]};
   var oldHand=game.Hand;var oldRun=game.RunPlayer;var oldRoom=game.Room;
   if(name.StartsWith("play:")) action["instanceId"]=(string)oldHand.First(c=>(string)c["cardId"]==name.Substring(5))["instanceId"];
   command(game);commands++;
   action["phase"]=game.Phase.ToString();action["hp"]=game.Player["hp"];action["mana"]=game.Player["mana"];action["stamina"]=game.Player["stamina"];action["turn"]=game.Turn;action["act"]=game.ActNumber;
   if(name=="reward:card") action["cardId"]=(string)((JArray)game.RunPlayer["deck"]).First(c=>!((JArray)oldRun["deck"]).Any(o=>(string)o["instanceId"]==(string)c["instanceId"]))["cardId"];
   if(name=="buy:relic") action["index"]=Enumerable.Range(0,((JArray)game.Room["relics"]).Count).First(i=>(bool?)oldRoom["relics"][i]["sold"]!=true&&(bool?)game.Room["relics"][i]["sold"]==true);
   if(name=="service:upgrade") action["itemRef"]=((JObject)game.RunPlayer["itemUpgradeLevels"]).Properties().First(p=>!JToken.DeepEquals(p.Value,oldRun["itemUpgradeLevels"]?[p.Name])).Name;
   trace.Add(action);counts[name.Split(':')[0]]=((int?)counts[name.Split(':')[0]]??0)+1;
   foreach(var ev in game.LastEvents){var type=(string)ev["type"];if(type is "manaSpent" or "staminaSpent" or "staminaRecovered" or "flaskUsed")counts[type]=((int?)counts[type]??0)+((int?)ev["amount"]??1);}
   if(testResume){command(resumed);var a=JObject.Parse(game.Snapshot().ToString(Newtonsoft.Json.Formatting.None));var b=JObject.Parse(resumed.Snapshot().ToString(Newtonsoft.Json.Formatting.None));if(!JToken.DeepEquals(a,b)||!JToken.DeepEquals(game.LastEvents,resumed.LastEvents))throw new Exception("Save/resume command diverged: "+name);resumeChecks++;}

  }
  while(game.Phase!=OriginalRunPhase.Victory&&game.Phase!=OriginalRunPhase.Defeat&&commands<3000)
  {
   var phase=game.Phase;phaseCounts[phase.ToString()]=((int?)phaseCounts[phase.ToString()]??0)+1;
   if(rejectionPhases.Add(phase))
   {
    var beforeRejection=game.Snapshot();var beforeEvents=game.LastEvents;bool rejected=false;
    try{if(phase==OriginalRunPhase.Combat)game.Play("__missing_instance__",(string)game.Enemies[0]["id"]);else game.Enter("__missing_node__");}
    catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}
    if(!rejected||!JToken.DeepEquals(beforeRejection,game.Snapshot())||!JToken.DeepEquals(beforeEvents,game.LastEvents))throw new Exception("Invalid command failed atomic rejection in "+phase);
    counts["atomicRejections"]=((int?)counts["atomicRejections"]??0)+1;
   }
   switch(phase)
   {
    case OriginalRunPhase.Map:
     var map=game.Map;var id=game.LegalNodeIds.OrderBy(n=>RouteScore((string)map["nodes"][n]["type"],game.Player)).ThenBy(n=>n,StringComparer.Ordinal).First();Act("enter:"+id,s=>s.Enter(id));break;
    case OriginalRunPhase.Combat:
     var p=game.Player;var hand=game.Hand.OfType<JObject>().ToArray();var enemies=game.Enemies.OfType<JObject>().Where(e=>(bool)e["alive"]).OrderBy(e=>(int)e["hp"]).ToArray();var target=enemies.FirstOrDefault();
     if(target==null)throw new Exception("Combat has no living enemy but has not ended.");
     if((int)p["maxHp"]-(int)p["hp"]>=Math.Ceiling((int)p["maxHp"]*0.25)&&(int?)p["flaskCharges"]?["hpCurrent"]>0){Act("charge:hp",s=>s.DrinkCharge("hp"));break;}
     if((int)p["mana"]==0&&(int?)p["flaskCharges"]?["manaCurrent"]>0&&hand.Any(c=>(int)game.Cost(c)["mana"]>0)){Act("charge:mana",s=>s.DrinkCharge("mana"));break;}
     if(p["flasks"] is JArray flasks&&flasks.Count>0){var f=catalog.Record("flasks",(string)flasks[0]["flaskId"]);var aimed=(bool?)f["targeted"]==true?(string)target["id"]:null;Act("flask:"+f["id"],s=>s.DrinkFlask(0,aimed));break;}
     var candidates=hand.Select(c=>new{Instance=c,Definition=game.Resolve(c),Cost=game.Cost(c)}).Where(c=>!(c.Definition["keywords"] as JArray??new JArray()).Values<string>().Contains("unplayable")&&(int)c.Cost["action"]<=(int)p["energy"]&&(int)c.Cost["mana"]<=(int)p["mana"]&&(int)c.Cost["stamina"]<=(int)p["stamina"]).Select(c=>new{c.Instance,Score=CardScore(c.Definition,p,target,enemies.Length)}).OrderByDescending(c=>c.Score).ToArray();
     if(candidates.Length>0&&candidates[0].Score>0){var instance=(string)candidates[0].Instance["instanceId"];var targetId=(string)target["id"];Act("play:"+(string)candidates[0].Instance["cardId"],s=>s.Play(instance,targetId));break;}
     var breathUses=(int?)game.Room["combatSnapshot"]?["catchBreathUses"]??0;
     if((int)p["energy"]>0&&(int)p["stamina"]<(int)p["maxStamina"]&&breathUses<1){Act("catchBreath",s=>s.CatchBreath());break;}
     Act("endTurn",s=>s.EndTurn());break;
    case OriginalRunPhase.Rewards:
     var room=game.Room;var rewards=(JObject)room["rewards"];bool collected=false;
     foreach(var kind in new[]{"cinders","relic","armament","flask","card"})
     {
      if((string)room["states"][kind]=="taken")continue;string rewardId=null;bool offered=kind=="cinders"?(int?)rewards["cinders"]>0:kind=="card"?(rewards["cardIds"] as JArray)?.Count>0:!string.IsNullOrEmpty((string)rewards[kind+"Id"]);
      if(!offered)continue;if(kind=="card")rewardId=((JArray)rewards["cardIds"]).Values<string>().OrderByDescending(card=>RewardScore(catalog.Record("cards",card))).First();
      if(kind=="flask"&&(game.RunPlayer["flasks"] as JArray)?.Count>=3)continue;
      Act("reward:"+kind,s=>s.Reward(kind,rewardId));collected=true;break;
     }
     if(!collected)Act("continueRewards",s=>s.ContinueRewards());break;
    case OriginalRunPhase.Shrine:
     var shrineKey=game.ActNumber+":"+game.RunPlayer["mapNodeId"];if(!seenServices.Contains(shrineKey)){seenServices.Add(shrineKey);var upgrade=new ItemUpgradeService(catalog);var run=game.RunPlayer;var item=upgrade.OwnedRefs(run).Where(item=>((int?)run["itemUpgradeLevels"]?[item]??0)<upgrade.MaximumTier(item)).Select(item=>new{Item=item,Plan=upgrade.Plan(run,item)}).FirstOrDefault(x=>(bool?)x.Plan["affordable"]==true);if(item!=null){Act("service:upgrade",s=>s.Service("upgrade",new JObject{["itemRef"]=item.Item}));break;}}
     if((game.RunPlayer["relics"] as JArray??new JArray()).Values<string>().Any(id=>(bool?)catalog.Record("relics",id)["passives"]?["shrineNoRest"]==true))Act("leaveShrine",s=>s.LeaveShrine());else Act("rest",s=>s.Rest());break;
    case OriginalRunPhase.Event:
     var choice=game.EventChoices.Where(c=>((int?)c["requires"]?["cinders"]??0)<=(int)game.RunPlayer["cinders"]).OrderByDescending(EventScore).FirstOrDefault()??throw new Exception("No affordable event choice");var choiceId=(string)choice["id"];Act("event:"+choiceId,s=>s.ChooseEvent(choiceId));break;
    case OriginalRunPhase.EventResult:Act("leaveEvent",s=>s.LeaveEvent());break;
    case OriginalRunPhase.Shop:
     var shop=game.Room;var stock=(JArray)shop["relics"];var affordable=stock.Select((r,i)=>new{Row=r,Index=i}).FirstOrDefault(x=>(bool?)x.Row["sold"]!=true&&(int)x.Row["cost"]<=(int)game.RunPlayer["cinders"]);if(affordable!=null){Act("buy:relic",s=>s.Buy("relic",affordable.Index));break;}Act("leaveShop",s=>s.LeaveShop());break;
    default:throw new Exception("Unhandled run phase "+phase);
   }
  }
  result["result"]=game.Phase.ToString();result["act"]=game.ActNumber;result["floor"]=game.RunPlayer["floor"];result["hp"]=game.Player["hp"];result["fightsWon"]=game.RunPlayer["fightsWon"]??0;result["commands"]=commands;result["resumeChecks"]=resumeChecks;
 }
 catch(Exception error){result["result"]="Exception";result["error"]=error.ToString();result["lastCommand"]=lastCommand;if(game!=null){File.WriteAllText(Path.Combine(output,$"failure-{classId}-{seed}.json"),game.Snapshot().ToString());result["act"]=game.ActNumber;result["phase"]=game.Phase.ToString();result["fightsWon"]=game.RunPlayer["fightsWon"]??0;}Console.WriteLine($"ERROR {classId}/{seed} {lastCommand}: {error.Message}");}
 result["counts"]=counts;result["phaseCounts"]=phaseCounts;result["trace"]=trace;report.Add(result);Console.WriteLine($"{classId}/{seed}: {result["result"]}, act {result["act"]}, fights {result["fightsWon"]}, commands {commands}, resumes {resumeChecks}");File.WriteAllText(Path.Combine(output,"results.json"),new JObject{["runtimeSourceDigest"]=digest,["elapsedSeconds"]=timer.Elapsed.TotalSeconds,["runs"]=report}.ToString());
}
Console.WriteLine("Completed "+report.Count+" real-command runs in "+timer.Elapsed.TotalSeconds+" seconds.");
if(report.Any(r=>(string)r["result"]!="Victory"))Environment.ExitCode=1;
static int RouteScore(string kind,JObject player)=>kind switch{"shrine"=>0,"treasure"=>1,"event"=>2,"merchant"=>3,"monster"=>4,"elite"=>5,"boss"=>6,_=>4};
static double RewardScore(JObject card)=>(string)card["type"]=="attack"?10:(card["effects"] as JArray??new JArray()).Any(e=>(string)e["op"]=="heal")?9:5;
static double EventScore(JToken choice){double score=0;foreach(var e in choice["effects"] as JArray??new JArray())score+=(string)e["op"] switch{"heal"=>10,"addRelic"=>8,"addCinders"=>(double?)e["amount"]>0?5:-5,"damage"=>-10,"loseHp"=>-10,"loseMaxHpPct"=>-15,"startCombat"=>-20,_=>1};return score;}
static double CardScore(JObject card,JObject player,JObject enemy,int enemies)
{
 double score=0;foreach(var effect in card["effects"] as JArray??new JArray()){
  double Amount(string field,double fallback=0){var value=effect[field];if(value==null)return fallback;if(value.Type==JTokenType.Integer||value.Type==JTokenType.Float)return(double)value;return fallback+3;}
  var bonus=Amount("attributeBonus");switch((string)effect["op"]){case "damage":var damage=Amount("amount",6)*Amount("hits",1)+bonus;score+=damage*((string)effect["target"]=="allEnemies"?enemies:1);if(damage>=(int)enemy["hp"]+(int)enemy["block"])score+=25;break;case "block":score+=Math.Max(0,Math.Min(Amount("amount",5),15-(int)player["block"]))*0.8;break;case "heal":score+=Math.Min(Amount("amount",5)+bonus,(int)player["maxHp"]-(int)player["hp"])*1.5;break;case "applyStatus":score+=6;break;case "draw":score+=5;break;case "gainEnergy":score+=10;break;case "dodgeRoll":score+=(int)player["block"]<8?4:0;break;case "enterStance":score+=3;break;case "poiseDamage":score+=Amount("amount",2)*0.6;break;case "loseHp":score-=Amount("amount",2);break;}}
 return score;
}
