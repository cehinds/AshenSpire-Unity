// CoopPolicyChecks.cs — actual unmodified-content cooperative playthrough.
// Greedy commands demonstrate lifecycle viability and save/rejoin continuity;
// they do not claim that a bot establishes balance, fun or mobile usability.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class CoopPolicyChecks
{
 public static void Run(OriginalContentCatalog catalog,JObject supplement,JObject mechanics,AttributeProgression progression,uint seed=1)
 {
  var game=new OriginalCoopRun(catalog,supplement,mechanics,seed);foreach(var id in new[]{"host","guest"}){var c=new CreationModel(catalog,"rogue","standard",progression);game.AddMember(id,id,new OriginalCharacterBuilder(catalog,progression,mechanics).Build(c));}
  var trace=new JArray();int commands=0,resumes=0;var timer=System.Diagnostics.Stopwatch.StartNew();var services=new HashSet<string>();
  void Act(string id,JObject intent)
  {
   var seq=(long)game.View(id)["local"]["sequence"]+1;var before=commands%40==0?game.Snapshot():null;
   var receipt=game.Execute(id,seq,intent);if((bool?)receipt["ok"]!=true)throw new Exception("Policy refusal: "+intent+" "+receipt);
   commands++;trace.Add(new JObject{["member"]=id,["command"]=intent.DeepClone(),["scene"]=game.View()["scene"]["kind"].DeepClone(),["act"]=game.View()["actNumber"].DeepClone()});
   if(before!=null){var restored=OriginalCoopRun.Restore(JObject.Parse(before.ToString()),false);var retry=restored.Execute(id,seq,intent);if(!JToken.DeepEquals(JObject.Parse(game.Snapshot().ToString()),JObject.Parse(restored.Snapshot().ToString()))||!JToken.DeepEquals(JObject.Parse(receipt.ToString()),JObject.Parse(retry.ToString())))throw new Exception("Policy resume diverged");resumes++;}
  }
  try
  {
   Act("host",new JObject{["type"]="start"});
   while((string)game.View()["scene"]["kind"]!="complete"&&commands<4000)
   {
    var view=game.View();var kind=(string)view["scene"]["kind"];var scene=(JObject)view["scene"];
    if(kind=="map")
    {
     var node=((JArray)view["reachableIds"]).Values<string>().OrderBy(n=>Route((string)view["map"]["nodes"][n]["type"])).ThenBy(n=>n,StringComparer.Ordinal).First();foreach(var id in new[]{"host","guest"})Act(id,new JObject{["type"]="chooseNode",["nodeId"]=node});
    }
    else if(kind=="combat")
    {
     var seat=scene["players"].First(p=>(bool)p["connected"]&&(bool)p["entity"]["alive"]&&!(bool)p["ended"]);var id=(string)seat["id"];var p=(JObject)seat["entity"];var local=game.View(id)["local"];var hand=(JArray)local["hand"];var enemies=scene["enemies"].Where(e=>(bool)e["alive"]).OrderBy(e=>(int)e["hp"]).ToArray();var enemy=(JObject)enemies.First();
     if((int)p["maxHp"]-(int)p["hp"]>=Math.Ceiling((int)p["maxHp"]*.25)&&(int?)p["flaskCharges"]?["hpCurrent"]>0){Act(id,new JObject{["type"]="useFlask",["slot"]=-1,["chargeKind"]="hp",["targetId"]=id});continue;}
     if((int)p["mana"]==0&&(int?)p["flaskCharges"]?["manaCurrent"]>0&&hand.Any(c=>(int)c["cost"]["mana"]>0)){Act(id,new JObject{["type"]="useFlask",["slot"]=-1,["chargeKind"]="mana",["targetId"]=id});continue;}
     if(p["flasks"] is JArray flasks&&flasks.Count>0){var flask=catalog.Record("flasks",(string)flasks[0]["flaskId"]);Act(id,new JObject{["type"]="useFlask",["slot"]=0,["targetId"]=(bool?)flask["targeted"]==true?(string)enemy["id"]:id});continue;}
     var candidates=hand.OfType<JObject>().Where(c=>!(c["card"]["keywords"] as JArray??new JArray()).Values<string>().Contains("unplayable")&&(int)c["cost"]["action"]<=(int)p["energy"]&&(int)c["cost"]["mana"]<=(int)p["mana"]&&(int)c["cost"]["stamina"]<=(int)p["stamina"]&&(!(bool)c["targets"]["active"]||c["targets"]["legalIds"].Any())).OrderByDescending(c=>Score((JObject)c["card"],p,enemy,enemies.Length)).ToArray();
     if(candidates.Length>0&&Score((JObject)candidates[0]["card"],p,enemy,enemies.Length)>0){var card=candidates[0];var target=(bool)card["targets"]["active"]?(string)card["targets"]["legalIds"].First():(string)enemy["id"];Act(id,new JObject{["type"]="playCard",["cardInstanceId"]=card["instance"]["instanceId"].DeepClone(),["targetId"]=target});}
     else Act(id,new JObject{["type"]="endTurn"});
    }
    else if(kind=="rewards")
    {
     foreach(var id in new[]{"host","guest"}){var local=game.View(id);if((bool?)local["scene"]["done"]?[id]==true)continue;var offer=local["scene"]["offers"][id];var card=((JArray)offer["cards"]).Values<string>().OrderByDescending(c=>Reward(catalog.Record("cards",c))).First();Act(id,new JObject{["type"]="chooseReward",["cardId"]=card,["takeRelic"]=true,["flask"]=((JArray)local["local"]["run"]["flasks"]).Count<3});}
    }
    else if(kind=="shrine")
    {
     foreach(var id in new[]{"host","guest"})
     {
      if((string)game.View()["scene"]["kind"]!="shrine")break;var local=game.View(id);if((bool?)local["scene"]["done"]?[id]==true)continue;var run=(JObject)local["local"]["run"];var smith=local["local"]["room"]["smith"];
      if((bool?)smith?["offered"]==true&&((JArray)smith["services"]).Values<string>().Contains("upgrade")){var upgrade=new ItemUpgradeService(catalog);var candidate=upgrade.OwnedRefs(run).Where(item=>((int?)run["itemUpgradeLevels"]?[item]??0)<upgrade.MaximumTier(item)).FirstOrDefault(item=>(bool?)upgrade.Plan(run,item)["affordable"]==true);if(candidate!=null){Act(id,new JObject{["type"]="service",["service"]="upgrade",["request"]=new JObject{["itemRef"]=candidate}});continue;}}
      var rest=!((JArray)run["relics"]).Values<string>().Any(r=>(bool?)catalog.Record("relics",r)["passives"]?["shrineNoRest"]==true);Act(id,new JObject{["type"]="shrineChoice",["choice"]=rest?"rest":"leave"});
     }
    }
    else if(kind=="shop")
    {
     foreach(var id in new[]{"host","guest"})
     {
      var local=game.View(id);if((bool?)local["scene"]["done"]?[id]==true)continue;var stock=(JArray)local["local"]["room"]["relics"];var item=stock.Select((r,i)=>new{r,i}).FirstOrDefault(x=>(bool?)x.r["sold"]!=true&&(int)x.r["cost"]<=(int)local["local"]["run"]["cinders"]);if(item!=null)Act(id,new JObject{["type"]="buy",["kind"]="relic",["index"]=item.i});else Act(id,new JObject{["type"]="leaveShop"});
     }
    }
    else if(kind=="event")
    {
     foreach(var id in new[]{"host","guest"}){var local=game.View(id);if(local["scene"]["next"]!=null){Act(id,new JObject{["type"]="eventContinue"});continue;}if((bool?)local["scene"]["done"]?[id]==true)continue;var choice=local["scene"]["choices"][id].Where(c=>((int?)c["requires"]?["cinders"]??0)<=(int)local["local"]["run"]["cinders"]).OrderByDescending(Event).First();Act(id,new JObject{["type"]="eventChoice",["choiceId"]=choice["id"].DeepClone()});}
    }
    else throw new Exception("Unsupported policy room "+kind);
    if(commands%100==0)Console.WriteLine($"Policy {commands} commands, act {view["actNumber"]}, {kind}");
   }
   var result=game.View();File.WriteAllText("policy-result.json",new JObject{["result"]=result,["commands"]=commands,["resumeChecks"]=resumes,["seconds"]=timer.Elapsed.TotalSeconds,["trace"]=trace}.ToString());
   if((string)result["scene"]["result"]!="victory")throw new Exception("Actual-content policy ended "+result["scene"]);
   Console.WriteLine($"Actual-content two-player victory: {commands} commands, {resumes} exact resume checks.");
  }
  catch{File.WriteAllText("policy-failure.json",game.Snapshot().ToString());File.WriteAllText("policy-trace.json",trace.ToString());throw;}
 }
 private static int Route(string kind)=>kind switch{"shrine"=>0,"treasure"=>1,"event"=>2,"merchant"=>3,"monster"=>4,"elite"=>5,"boss"=>6,_=>4};
 private static double Reward(JObject card)=>(string)card["type"]=="attack"?10:(card["effects"] as JArray??new JArray()).Any(e=>(string)e["op"]=="heal")?9:5;
 private static double Event(JToken choice){double score=0;foreach(var e in choice["effects"] as JArray??new JArray())score+=(string)e["op"] switch{"heal"=>10,"addRelic"=>8,"addCinders"=>(double?)e["amount"]>0?5:-5,"damage"=>-10,"loseHp"=>-10,"loseMaxHpPct"=>-15,"startCombat"=>-20,_=>1};return score;}
 private static double Score(JObject card,JObject player,JObject enemy,int enemies)
 {
  double score=0;foreach(var effect in card["effects"] as JArray??new JArray()){
   double Amount(string field,double fallback=0){var value=effect[field];if(value==null)return fallback;if(value.Type==JTokenType.Integer||value.Type==JTokenType.Float)return(double)value;return fallback+3;}
   var bonus=Amount("attributeBonus");switch((string)effect["op"]){case "damage":var damage=Amount("amount",6)*Amount("hits",1)+bonus;score+=damage*((string)effect["target"]=="allEnemies"?enemies:1);if(damage>=(int)enemy["hp"]+(int)enemy["block"])score+=25;break;case "block":score+=Math.Max(0,Math.Min(Amount("amount",5),15-(int)player["block"]))*0.8;break;case "heal":score+=Math.Min(Amount("amount",5)+bonus,(int)player["maxHp"]-(int)player["hp"])*1.5;break;case "applyStatus":score+=6;break;case "draw":score+=5;break;case "gainEnergy":score+=10;break;case "dodgeRoll":score+=(int)player["block"]<8?4:0;break;case "enterStance":score+=3;break;case "poiseDamage":score+=Amount("amount",2)*0.6;break;case "loseHp":score-=Amount("amount",2);break;}}
  return score;
 }
}
