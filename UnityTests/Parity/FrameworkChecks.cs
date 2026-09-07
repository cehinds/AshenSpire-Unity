// FrameworkChecks.cs — compare native components against the pinned original engine.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
public static class FrameworkChecks
{
 public static int Run(string fixturePath)
 {
var oracle = JObject.Parse(File.ReadAllText(fixturePath));
var mechanics = (JObject)oracle["mechanics"]!;
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Equal(JToken actual, JToken expected, string message) { Check(JToken.DeepEquals(actual, expected), message + "\nActual: " + actual + "\nExpected: " + expected); }
void Refuses(Action action, string message) { try { action(); } catch (ArgumentException) { checks++; return; } catch (OverflowException) { checks++; return; } throw new Exception(message); }
foreach(var card in oracle["cards"]!)
{
 var definition = (JObject)card["definition"]!; var view = CardMechanics.FromDefinition(definition);
 Equal(view["properties"]!,card["properties"]!,"Property mapping " + definition["id"]);
 Check(CardMechanics.AfterPlay(view)==(string)card["destination"]!,"Destination " + definition["id"]);
 Check(CardMechanics.EndTurnFate(view)==(string)card["fate"]!,"Cleanup " + definition["id"]);
 foreach(var profile in card["profiles"]!) Equal(CardMechanics.CostProfile(definition,(int)profile["reduction"]!,profile["weightClass"] as JObject),profile["result"]!,"Cost profile " + definition["id"]);
}
foreach(var fixture in oracle["lifecycle"]!)
{
 var view=(JObject)fixture["view"]!; Check(CardMechanics.EndTurnFate(view)==(string)fixture["fate"]!,"Lifecycle combinations");
 foreach(var d in fixture["destinations"]!) Check(CardMechanics.AfterPlay(view,(bool)d["legal"]!,(bool)d["cancelled"]!,(bool)d["sealConditionMet"]!)==(string)d["result"]!,"Lifecycle order");
}
foreach(var fixture in oracle["costs"]!) Equal(CardMechanics.CompileCosts((JObject)fixture["view"]!,(JArray)fixture["modifiers"]!),fixture["result"]!,"Cost modifiers");
var weights = new WeightSystem(mechanics);
foreach(var fixture in oracle["weightCases"]!) { var input=fixture["input"]!; Equal(weights.Compute((int)input["constitution"]!,(int)input["strength"]!,(JObject)input["weights"]!),fixture["result"]!,"Carry thresholds"); }
foreach(var fixture in oracle["dodges"]!) { var input=fixture["input"]!; Equal(weights.Dodge((int)input["roll"]!,(int)input["dexterity"]!,(JObject)input["weightClass"]!),fixture["result"]!,"Dodge strict threshold"); }
foreach(var fixture in oracle["resourceCases"]!)
{
 var start=fixture["start"]!; var wallet=new ResourceWallet(3,(int)start["maxMana"]!,(int)start["maxStamina"]!,mechanics,(int)start["currentMana"]!,(int)start["currentStamina"]!);
 foreach(var step in fixture["steps"]!) {
  var value=(int?)step["args"]?.First ?? 0;
  switch((string)step["op"]!) { case "RecoverMana":wallet.RecoverMana(value);break;case "BeginTurn":wallet.BeginTurn(3);break;case "SpendStamina":Check(wallet.SpendStamina(value),"Stamina payment");break;case "RefundStamina":wallet.RefundStamina(value);break;case "EndTurn":wallet.EndTurn();break;case "Rest":wallet.Rest();break;default:throw new Exception("Unknown trace operation"); }
  var expected=step["result"]!;var snapshot=wallet.Snapshot();
  Check((int)snapshot["mana"]! ==(int)expected["currentMana"]! && (int)snapshot["stamina"]! ==(int)expected["currentStamina"]! && (int)snapshot["staminaSpentThisTurn"]! ==(int)expected["staminaSpentThisTurn"]!,"Original resource transition");
  Equal(new ResourceWallet(snapshot,mechanics).Snapshot(),snapshot,"Wallet restore");
 }
}
JArray Cost(string resource,int amount)=>new JArray(new JObject{["resource"]=resource,["amount"]=amount});
var atomic=new ResourceWallet(3,4,5,mechanics);var before=atomic.Snapshot();
Check(!atomic.TryPay(new JArray(new JObject{["resource"]="action",["amount"]=2},new JObject{["resource"]="mana",["amount"]=9})),"Unaffordable multi-resource cost rejected"); Equal(atomic.Snapshot(),before,"Failed cost is atomic");
Check(!atomic.TryPay(Cost("mana",1),false),"Target cancellation");Equal(atomic.Snapshot(),before,"Cancellation spends nothing");
Check(!atomic.TryPay(new JArray(new JObject{["resource"]="stamina",["amount"]=3},new JObject{["resource"]="stamina",["amount"]=3})),"Duplicate costs aggregated");Equal(atomic.Snapshot(),before,"Duplicate overdraft is atomic");
Refuses(()=>atomic.TryPay(Cost("mana",-1)),"Negative cost rejected");Refuses(()=>atomic.TryPay(Cost("unknown",1)),"Unknown resource rejected");Equal(atomic.Snapshot(),before,"Malformed costs are atomic");
Check(atomic.TryPayProfile(JObject.Parse("{action:2,mana:1,stamina:3,variable:false}")),"Mixed resource payment");Equal(atomic.Snapshot(),JObject.Parse("{action:1,mana:3,stamina:2,maxMana:4,maxStamina:5,staminaSpentThisTurn:3}"),"All costs committed once");
atomic.RefundStamina(3);atomic.EndTurn();Check((int)atomic.Snapshot()["stamina"]! == 5,"Refund capped");
var idle=new ResourceWallet(3,5,5,mechanics,0,2);idle.SpendStamina(1);idle.RefundStamina(1);idle.EndTurn();Check((int)idle.Snapshot()["stamina"]! == 2,"Refund does not erase spent turn");idle.EndTurn();Check((int)idle.Snapshot()["stamina"]! == 3,"Idle turn recovers authored amount");
var tuned=(JObject)mechanics.DeepClone();tuned["mana"]!["naturalRecoveryPerTurn"]=2;tuned["stamina"]!["idleRecoveryPerTurn"]=2;var policy=new ResourceWallet(3,3,4,tuned,2,1);policy.BeginTurn(3);policy.EndTurn();Check((int)policy.Snapshot()["mana"]! == 3 && (int)policy.Snapshot()["stamina"]! == 3,"Authored recovery seam caps resources");
var zone=new CardZoneLedger(new[]{"strike#1","strike#2","guard#1"});zone.Move("strike#1","HAND");zone.Move("strike#2","EXHAUST_PILE");Equal(new CardZoneLedger(zone.Snapshot()).Snapshot(),zone.Snapshot(),"Zone persistence");
Refuses(()=>new CardZoneLedger(new[]{"same","same"}),"Duplicate instance refused");var zoneBefore=zone.Snapshot();Refuses(()=>zone.Move("unknown","HAND"),"Unknown instance refused");Refuses(()=>zone.Move("strike#1","VOID"),"Unknown zone refused");Equal(zone.Snapshot(),zoneBefore,"Rejected zone commands leave state intact");zone.Move("strike#1","HAND");Equal(zone.InZone("HAND"),new JArray("strike#1"),"Recall retains one copy");
var snapshotCopy=zone.Snapshot();snapshotCopy["instances"]![0]!["zone"]="SEALED";Check(zone.ZoneOf("strike#1")=="HAND","Zone snapshot isolated");
Refuses(()=>weights.Dodge(0,10,(JObject)mechanics["weight"]!["classes"]![0]!),"Invalid die refused");Refuses(()=>weights.Compute(5,5,JObject.Parse("{mainHandWeight:-1}")),"Negative weight refused");
var recall=JObject.Parse("{id:'recall',properties:[{propertyId:'lifecycle.recall.afterUse'}],overrides:{}}");Refuses(()=>CardMechanics.CompileCosts(recall),"Unlimited recall refused");recall["overrides"]!["oncePerTurn"]=true;Check((string)CardMechanics.CompileCosts(recall)["mode"]! == "ALL_REQUIRED","Repeat limiter admitted");
var x=new ResourceWallet(3,1,1,mechanics);var xp=JObject.Parse("{action:0,mana:0,stamina:0,variable:true}");Refuses(()=>x.TryPayProfile(xp),"X requires resolved amount");Check(x.TryPayProfile(xp,true,3) && (int)x.Snapshot()["action"]! == 0,"X commits resolved amount");
return checks;

 }
}
