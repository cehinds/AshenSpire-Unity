// Integrated original-source differential fixtures.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class NativeRunChecks { internal static int Run(string fixturePath) {
var reference=JObject.Parse(File.ReadAllText(fixturePath));
var rules=new OriginalRunRules((JObject)reference["content"]);
int checks=0;
void Equal(string label,JToken actual,JToken expected){var left=actual.ToString(Newtonsoft.Json.Formatting.None);var right=expected.ToString(Newtonsoft.Json.Formatting.None);if(left!=right&&!JToken.DeepEquals(JToken.Parse(left),JToken.Parse(right))){File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "mismatch.json"),new JObject{["label"]=label,["actual"]=actual.DeepClone(),["expected"]=expected.DeepClone()}.ToString());throw new Exception(label+" mismatch; see scratch mismatch.json");}checks++;}
foreach(JObject fixture in reference["fixtures"]){
 var rng=new RandomStreams((uint)fixture["seed"]);JToken result=null;var run=fixture["run"] is JObject r?(JObject)r.DeepClone():new JObject();var pool=(string)fixture["pool"];
 switch((string)fixture["kind"]){
  case "map":result=rules.BuildAct(rng,(int)fixture["act"],(JArray)fixture["history"]);break;
  case "shop":result=rules.Shop(rng,run);break;
  case "encounter":var list=new List<string>();for(int i=0;i<5;i++)list.Add(rules.Encounter(rng,(int)fixture["act"],pool,list.TakeLast(2)));result=new JArray(list);break;
  case "rewardRolls":result=new JObject{["cinders"]=rules.Cinders(rng,pool,run),["cardIds"]=rules.Cards(rng,pool,run),["flaskId"]=rules.Flask(rng,run),["relicId"]=rules.Relic(rng,run),["armamentId"]=rules.Armament(rng,pool,Array.Empty<string>(),Array.Empty<string>())};Equal("flask pity",run["flaskChancePct"],fixture["finalFlaskChancePct"]);break;
 }
 Equal($"{fixture["kind"]}/{fixture["seed"]}",result,fixture["result"]);Equal("stream counters",JObject.FromObject(rng.Snapshot()),fixture["counters"]);
}
var catalog=new OriginalContentCatalog(reference["content"].ToString());var callbacks=new OriginalRunContent(catalog);
JObject Project(JObject run)=>new JObject{["hp"]=run["hp"].DeepClone(),["maxHp"]=run["maxHp"].DeepClone(),["cinders"]=run["cinders"].DeepClone(),["deck"]=new JArray(run["deck"].Select(c=>{var card=new JObject{["cardId"]=c["cardId"].DeepClone(),["upgraded"]=c["upgraded"].DeepClone()};foreach(var key in new[]{"sourceArmamentId","smithingLevel"})if(c[key]!=null)card[key]=c[key].DeepClone();return card;})),["relics"]=run["relics"].DeepClone(),["flasks"]=run["flasks"].DeepClone(),["flaskCharges"]=run["flaskCharges"].DeepClone(),["itemUpgradeLevels"]=run["itemUpgradeLevels"]?.DeepClone()??new JObject(),["combatEntered"]=run["combatEntered"]?.DeepClone()??JValue.CreateNull()};
foreach(JObject fixture in reference["eventFixtures"]){var run=(JObject)fixture["input"].DeepClone();var rng=new RandomStreams((uint)fixture["seed"]);callbacks.ApplyEffects(run,(JArray)fixture["effects"],rng);Equal("event "+fixture["eventId"]+" "+fixture["label"],Project(run),fixture["result"]);Equal("event counters",JObject.FromObject(rng.Snapshot()),fixture["counters"]);}
var supplemental=JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(fixturePath)!, "event-choices.json")));
foreach(uint seed in new uint[]{1,25})foreach(JObject playerFixture in reference["players"]){
 var player=(JObject)playerFixture.DeepClone();player["cinders"]=1000;
 var session=OriginalRunSession.Start(catalog,supplemental,player,seed,callbacks);int guard=0;
 while(session.Phase!=OriginalRunPhase.Victory){
  if(++guard>180)throw new Exception("Run traversal did not terminate");
  var saved=session.Snapshot();var resumed=OriginalRunSession.Restore(saved,callbacks);Equal("snapshot roundtrip",resumed.Snapshot(),saved);session=resumed;
  var isolated=session.Player();isolated["hp"]=0;Equal("public copies isolated",session.Snapshot(),saved);
  switch(session.Phase){
   case OriginalRunPhase.Map:
    if(session.EnterNode("not-a-node"))throw new Exception("Illegal path accepted");Equal("invalid path leaves state/RNG unchanged",session.Snapshot(),saved);
    var ids=session.LegalNodeIds();if(ids.Length==0)throw new Exception("Map route stuck");session.EnterNode(ids[seed==1?0:ids.Length-1]);break;
   case OriginalRunPhase.Combat:
    var rng=session.CreateRandom();rng.Float("enemyAI");session.SaveCombat(new JObject{["turn"]=3,["testOpaqueCombat"]=true},rng);
    var paused=session.Snapshot();session=OriginalRunSession.Restore(paused,callbacks);Equal("mid-combat snapshot no reroll",session.Snapshot(),paused);
    var combatPlayer=session.Player();combatPlayer["hp"]=Math.Max(1,(int)combatPlayer["hp"]-1);session.CompleteCombat("victory",combatPlayer,session.CreateRandom());break;
   case OriginalRunPhase.Rewards:
    if(session.CollectReward("cinders")){var claimed=session.Snapshot();if(session.CollectReward("cinders"))throw new Exception("Duplicate cinders granted");Equal("reward claim idempotent",session.Snapshot(),claimed);}
    session.ContinueRewards(true);break;
   case OriginalRunPhase.Shop:
    if(session.BuyShopItem("card",0)){var bought=session.Snapshot();if(session.BuyShopItem("card",0))throw new Exception("Sold shop item purchased twice");Equal("shop sold receipt persists",session.Snapshot(),bought);}
    session.LeaveShop();break;
   case OriginalRunPhase.Shrine:
    var charges=session.Player()["flaskCharges"];Equal("arrival refills HP flask",charges["hpCurrent"],charges["hp"]);Equal("arrival refills mana flask",charges["manaCurrent"],charges["mana"]);if(rules.CanRest(session.Player()))session.Rest();else session.LeaveShrine();break;
   case OriginalRunPhase.Event:
    var choice=session.EventChoices().Last();if(!session.ChooseEvent((string)choice["id"]))throw new Exception("Authored exit unavailable");break;
   case OriginalRunPhase.EventResult:session.LeaveEvent();break;
   default:throw new Exception("Unexpected test phase "+session.Phase);
  }
 }
 if(session.ActNumber!=3)throw new Exception("Victory before third act");checks++;
 var corrupt=session.Snapshot();corrupt["run"]["path"]=new JArray("not-a-node");bool rejected=false;try{OriginalRunSession.Restore(corrupt,callbacks);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Illegal saved path accepted");checks++;
}
Console.WriteLine($"{checks} checks passed");
return checks;

} }
