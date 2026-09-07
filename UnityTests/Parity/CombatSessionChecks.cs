// CombatSessionChecks.cs — original command/state/event differential oracle and resume tests.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
public static class CombatSessionChecks
{
 private static bool Equivalent(JToken a,JToken b) { if(a is JValue av && b is JValue bv && av.Value==null && bv.Value==null)return true; if(a is JObject ao && b is JObject bo) return ao.Count==bo.Count && ao.Properties().All(p=>bo[p.Name]!=null && Equivalent(p.Value,bo[p.Name])); if(a is JArray aa && b is JArray ba) return aa.Count==ba.Count && aa.Zip(ba).All(p=>Equivalent(p.First,p.Second)); if((a.Type==JTokenType.Integer||a.Type==JTokenType.Float)&&(b.Type==JTokenType.Integer||b.Type==JTokenType.Float))return (double)a==(double)b; return JToken.DeepEquals(a,b); }
 public static int Run(string fixturePath)
 {
  var oracle=JObject.Parse(File.ReadAllText(fixturePath));var content=new OriginalContentCatalog(oracle["content"]!.ToString());var mechanics=(JObject)oracle["mechanics"]!;var definitions=(JObject)oracle["definitions"]!;int checks=0;int index=0;
  JObject Resolve(JObject instance)=>(JObject)definitions[(string)instance["cardId"]!+":"+(((bool?)instance["upgraded"]??false)?"true":"false")]!.DeepClone();
  void Equal(JToken actual,JToken expected,string label){if(!Equivalent(actual,expected))throw new Exception(label+"\nActual:"+actual+"\nExpected:"+expected);checks++;}
  JObject Normalize(CombatSession session){var s=session.Snapshot();var p=(JObject)s["player"]!;p.Remove("damageBySchoolAdd");p.Remove("itemUpgradeLevels");var piles=(JObject)s["piles"]!;piles.Remove("sealed");piles.Remove("removed");return new JObject{["turn"]=s["turn"],["phase"]=s["phase"],["result"]=s["result"],["player"]=p,["enemies"]=s["enemies"],["piles"]=piles,["rng"]=s["rng"]};}
  JArray Dispatch(CombatSession session,JToken intent){return (string)intent["type"]! switch{"playCard"=>session.PlayCard((string)intent["cardInstanceId"]!,(string)intent["targetId"]),"endTurn"=>session.EndTurn(),"useFlask"=>intent["chargeKind"]!=null?session.DrinkCharge((string)intent["chargeKind"]!,(string)intent["targetId"]):session.DrinkFlask((int)intent["slot"]!,(string)intent["targetId"]),_=>throw new Exception("Unknown fixture command")};}
  foreach(var f in oracle["fixtures"]!){
   var session=new CombatSession(content,mechanics,new RandomStreams((uint)f["seed"]!),(JObject)f["player"]!,f["deck"]!.Cast<JObject>(),f["enemyIds"]!.Values<string>(),Resolve,(double)f["hpMult"]!);
   var label="Fixture "+index++ +" "+f["deck"]![0]!["cardId"]+" "+f["enemyIds"]![0];Equal(Normalize(session),f["initial"]!,label+" setup state");Equal(session.Snapshot()["events"]!,f["setupEvents"]!,label+" setup events");
   foreach(var step in f["steps"]!){
    var resumed=CombatSession.Restore(content,mechanics,session.Snapshot(),Resolve);JArray events=null;bool refused=false;string refusal=null;
    try{events=Dispatch(session,step["intent"]!);}catch(ArgumentException e){refused=true;refusal=e.ToString();}catch(InvalidOperationException e){refused=true;refusal=e.ToString();}
    if((step["error"]!.Type!=JTokenType.Null)!=refused)throw new Exception(label+" rejection mismatch "+step["error"]+" actual "+refusal);checks++;
    if(!refused){Equal(events!,step["events"]!,label+" events "+step["intent"]);Equal(Dispatch(resumed,step["intent"]!),events!,label+" resumed events");Equal(resumed.Snapshot(),session.Snapshot(),label+" resumed state");}
    Equal(Normalize(session),step["state"]!,label+" command state");
   }
  }
  for(int bonus=1;bonus<=10;bonus++)for(int hits=1;hits<=4;hits++){if(Enumerable.Range(0,hits).Sum(h=>CombatSession.AttributeHitBonus(bonus,hits,h))!=bonus)throw new Exception("Attribute bonus duplicated/lost");checks++;}
  var basisFixture=oracle["fixtures"]![0]!;
  CombatSession Make(Func<JObject,JObject> resolver,JObject policy)=>new CombatSession(content,policy,new RandomStreams(1),(JObject)basisFixture["player"]!,new[]{new JObject{["instanceId"]="owner",["cardId"]="strike",["upgraded"]=false}},basisFixture["enemyIds"]!.Values<string>(),resolver,20);
  void RefusesUnchanged(CombatSession session,Action action,string label){var before=session.Snapshot();bool refused=false;try{action();}catch(ArgumentException){refused=true;}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception(label+" accepted");Equal(session.Snapshot(),before,label+" mutated state");}
  var policy=(JObject)mechanics.DeepClone();policy["stamina"]!["catchBreath"]=JObject.Parse("{actionCost:1,recovery:1,usesPerTurn:1}");
  var breath=Make(Resolve,policy);var beforeBreath=breath.Player;breath.CatchBreath();if((int)breath.Player["energy"]!=(int)beforeBreath["energy"]-1||(int)breath.Player["stamina"]!=(int)beforeBreath["stamina"]+1)throw new Exception("Catch Breath exchange failed");checks++;RefusesUnchanged(breath,()=>breath.CatchBreath(),"Catch Breath limit");breath.EndTurn();breath.CatchBreath();checks++;
  var fullPlayer=(JObject)basisFixture["player"]!.DeepClone();fullPlayer["stamina"]=fullPlayer["maxStamina"];var full=new CombatSession(content,policy,new RandomStreams(1),fullPlayer,basisFixture["deck"]!.Cast<JObject>(),basisFixture["enemyIds"]!.Values<string>(),Resolve,20);RefusesUnchanged(full,()=>full.CatchBreath(),"Catch Breath at full pool");
  for(int bonus=1;bonus<=10;bonus++)for(int hits=1;hits<=4;hits++){
   JObject Damage(JObject instance){var d=Resolve(instance);d["effects"]=new JArray(new JObject{["op"]="damage",["target"]="enemy",["amount"]=1,["hits"]=hits,["attributeBonus"]=bonus});return d;}
   var attack=Make(Damage,mechanics);var hp=(int)attack.Enemies[0]!["hp"]!;var events=attack.PlayCard("owner","e1");if(hp-(int)attack.Enemies[0]!["hp"]! != hits+bonus||events.Where(e=>(string)e["type"]! == "damageDealt").Sum(e=>(int)e["amount"]!)!=hits+bonus)throw new Exception("Attack bonus did not conserve per-point total");checks++;
  }
  JObject Healing(JObject instance){var d=Resolve(instance);d["effects"]=new JArray(new JObject{["op"]="heal",["target"]="self",["amount"]=1,["attributeBonus"]=3});return d;}
  var healing=Make(Healing,mechanics);var beforeHp=(int)healing.Player["hp"]!;healing.PlayCard("owner");if((int)healing.Player["hp"]! != beforeHp+4)throw new Exception("Healing point bonus failed");checks++;
  var invalid=Make(Resolve,mechanics);RefusesUnchanged(invalid,()=>invalid.PlayCard("missing","e1"),"Missing card");RefusesUnchanged(invalid,()=>invalid.PlayCard("owner","missing"),"Missing target");RefusesUnchanged(invalid,()=>invalid.DrinkCharge("mana","e1"),"Mana flask enemy target");
  JObject Tagged(JObject instance){var d=Resolve(instance);d.Remove("cardTags");d["tags"]=new JArray("ritual");d["effects"]=new JArray(new JObject{["op"]="applyStatus",["target"]="enemy",["status"]="insanityExposed",["stacks"]=1},new JObject{["op"]="damage",["target"]="enemy",["amount"]=10});return d;}
  var tagged=Make(Tagged,mechanics);var taggedHp=(int)tagged.Enemies[0]!["hp"]!;tagged.PlayCard("owner","e1");if(taggedHp-(int)tagged.Enemies[0]!["hp"]! != 13)throw new Exception("Ordinary card tags lost before vulnerability");checks++;
  JObject ProjectedCost(JObject instance){var d=Resolve(instance);d["cost"]=1.0;return d;}var projected=Make(ProjectedCost,mechanics);projected.PlayCard("owner","e1");checks++;
  return checks;
 }
}
