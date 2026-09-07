using AshenSpire.Domain.Original;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
internal static class NativeServicesChecks
{
 internal static void Run(string root)
 {
 Directory.CreateDirectory(Path.Combine(root,"TestResults/Parity"));
var oracle=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/NativeServicesChecks.json")));
var catalog=new OriginalContentCatalog(oracle["content"].ToString());
var repo=root;
var mechanics=JObject.Parse(File.ReadAllText(repo+"/GameContent/Unity/Original/mechanics.json"));
var projection=new OriginalPlayerProjection(catalog,mechanics);
var content=new OriginalRunContent(catalog,reconcile:projection.Reconcile);
// Exercise the actual already-ported growth callback without changing its visibility.
var growthMethod=typeof(OriginalRunContent).GetMethod("SyncFlaskGrowth",BindingFlags.Instance|BindingFlags.NonPublic)!;
void Growth(JObject run)=>growthMethod.Invoke(content,new object[]{run});
var service=new OriginalRunServices(catalog,projection.Reconcile,Growth);int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
void Equal(JToken left,JToken right,string label)
{
 JToken Normalize(JToken value)=>value is JValue jv && jv.Value==null ? JValue.CreateNull() : value is JObject obj?new JObject(obj.Properties().Select(p=>new JProperty(p.Name,Normalize(p.Value)))):value is JArray array?new JArray(array.Select(Normalize)):value.Type==JTokenType.Integer||value.Type==JTokenType.Float?new JValue((double)value):value.Type==JTokenType.Null?JValue.CreateNull():value.DeepClone();
 if(!JToken.DeepEquals(Normalize(left),Normalize(right))){File.WriteAllText(Path.Combine(root,"TestResults/Parity/NativeServicesChecks-failure.json"),new JObject{["label"]=label,["actual"]=left.DeepClone(),["expected"]=right.DeepClone()}.ToString());throw new Exception(label+"; see failure.json");}checks++;
}
foreach(var fixture in oracle["plans"]!){Equal(service.LevelPlan((JObject)fixture["run"]!,1),fixture["plan"]!,"Original level plan");Equal(service.LevelBudget((JObject)fixture["run"]!),fixture["budget"]!,"Original cumulative level budget");}
foreach(var fixture in oracle["levels"]!)
{
 var run=(JObject)fixture["before"]!.DeepClone();projection.Reconcile(run);run["phase"]="Shrine";
 var rng=run["streamCounters"]!.DeepClone();Check(service.Apply(run,"levelUp",new JObject{["allocation"]=fixture["allocation"]!.DeepClone()}),"Valid shrine allocation");
 foreach(var field in ((JObject)fixture["result"]!).Properties())Equal(run[field.Name]!,field.Value,"Original level transaction "+run["class"]+" "+field.Name);
 Equal(run["streamCounters"]!,rng,"Level-up consumes no RNG");Check((string)run["phase"]! =="Shrine","Level allocation leaves shrine open");
}
foreach(var row in oracle["sales"]!)
{
 var run=new JObject{["relics"]=new JArray(),["flasks"]=new JArray()};var kind=(string)row["kind"]!;
 if(kind=="relic")((JArray)run["relics"]!).Add((string)row["id"]!);else ((JArray)run["flasks"]!).Add(new JObject{["flaskId"]=row["id"]!.DeepClone()});
 var available=service.Sellables(run);Equal(new JValue(available.Count==0?0:(int)available[0]!["price"]!),row["price"]!,"Original resale price "+row["id"]);
}
JObject Player(){var run=(JObject)oracle["levels"]![0]!["before"]!.DeepClone();projection.Reconcile(run);run["phase"]="Shrine";run["ownedItemRefs"]=new JArray(new ItemUpgradeService(catalog).OwnedRefs(run));return run;}
void Atomic(JObject run,Func<bool> command,string label){var before=run.ToString(Formatting.None);bool refused;try{refused=!command();}catch(ArgumentException){refused=true;}catch(InvalidOperationException){refused=true;}Check(refused,label+" refused");Check(run.ToString(Formatting.None)==before,label+" atomic");}
var player=Player();var allocation=new JObject{["allocation"]=new JObject{["constitution"]=2,["wisdom"]=1}};
foreach(var phase in new[]{"Map","Shop","Combat","Rewards","Victory","Defeat"}){player["phase"]=phase;Atomic(player,()=>service.Apply(player,"levelUp",allocation),"Level phase "+phase);}player["phase"]="Shrine";
Atomic(player,()=>service.Apply(player,"levelUp",new JObject{["allocation"]=new JObject{["unknown"]=1}}),"Unknown attribute");
Atomic(player,()=>service.Apply(player,"levelUp",new JObject{["allocation"]=new JObject{["strength"]=-1}}),"Negative allocation");
Atomic(player,()=>service.Apply(player,"levelUp",new JObject{["allocation"]=new JObject{["strength"]=1.5}}),"Fractional allocation");
Atomic(player,()=>service.Apply(player,"levelUp",new JObject{["allocation"]=new JObject()}),"Empty allocation");
player["cinders"]=43;Atomic(player,()=>service.Apply(player,"levelUp",new JObject{["allocation"]=new JObject{["strength"]=2}}),"Insufficient cumulative purse");
player["cinders"]=44;Check(service.Apply(player,"levelUp",new JObject{["allocation"]=new JObject{["strength"]=2}}),"Exact two-step budget");Check((int)player["cinders"]! ==0&&(int)player["levelUps"]! ==2&&(int)player["levelPoints"]! ==2,"Purchases and granted points stored independently");
var frozen=Player();var rules=frozen["playerProjectionRules"]!.DeepClone();var edited=catalog.Data();edited["derivedStatRules"]!["rules"]!["hp"]!["gainPerTier"]=99;
var laterCatalog=new OriginalContentCatalog(edited.ToString());var laterProjection=new OriginalPlayerProjection(laterCatalog,mechanics);var laterServices=new OriginalRunServices(laterCatalog,laterProjection.Reconcile,Growth);var originalSame=(JObject)frozen.DeepClone();
Check(service.Apply(originalSame,"levelUp",allocation)&&laterServices.Apply(frozen,"levelUp",allocation),"Saved rule level transactions");Equal(frozen["playerProjectionRules"]!,rules,"Later content cannot reprice stat snapshot");Equal(frozen["maxHp"]!,originalSame["maxHp"]!,"Frozen resource projection survives current content tuning");
var throwing=new OriginalRunServices(catalog,r=>{r["cinders"]=0;throw new InvalidOperationException("Fixture projection failed");},Growth);player=Player();Atomic(player,()=>throwing.Apply(player,"levelUp",allocation),"Failing level projection");
var shop=Player();shop["phase"]="Shop";var goods=oracle["sales"]!.Where(r=>(string)r["kind"]=="relic"&&(int)r["price"]!>0).Select(r=>(string)r["id"]!).ToArray();
var relic=goods[0];content.CollectReward(shop,"relic",new JObject{["relicId"]=relic},new RandomStreams(1));
((JArray)shop["ownedItemRefs"]!).Add("relic/"+relic);shop["itemUpgradeLevels"]!["relic/"+relic]=1;
var sale=service.Sellables(shop).First(r=>(string)r["kind"]=="relic"&&(string)r["id"]==relic);var paid=(int)shop["cinders"]!;var mounts=shop["itemMounts"]?.DeepClone()??new JObject();var tiers=shop["itemUpgradeLevels"]!.DeepClone();var deck=shop["deck"]!.DeepClone();var loadout=shop["loadout"]!.DeepClone();
Check(service.Apply(shop,"sell",(JObject)sale),"Owned relic resale");Check((int)shop["cinders"]! ==paid+(int)sale["price"]!,"Exact resale cinders");Check(!shop["relics"]!.Values<string>().Contains(relic)&&!shop["ownedItemRefs"]!.Values<string>().Contains("relic/"+relic),"Resale removes actual and cached ownership");
Equal(shop["itemUpgradeLevels"]!,tiers,"Original tier ledger retained after resale");Equal(shop["itemMounts"]??new JObject(),mounts,"Relic sale preserves mounts");Equal(shop["loadout"]!,loadout,"Relic sale preserves weapon locations");Equal(shop["deck"]!,deck,"Relic sale preserves card identities");Atomic(shop,()=>service.Apply(shop,"sell",(JObject)sale),"Repeated relic resale");
var starterIndex=((JArray)shop["relics"]!).Select((id,i)=>(id,i)).First(x=>(string)catalog.Record("relics",(string)x.id)["rarity"]=="starter").i;
Atomic(shop,()=>service.Apply(shop,"sell",new JObject{["kind"]="relic",["index"]=starterIndex,["id"]=shop["relics"]![starterIndex]!.DeepClone()}),"Starter identity cannot sell");
Atomic(shop,()=>service.Apply(shop,"sell",new JObject{["kind"]="armament",["index"]=0,["id"]="straightSword"}),"Original merchant never buys weapons");
var flaskId=(string)catalog.Table("flasks")[0]["id"];content.CollectReward(shop,"flask",new JObject{["flaskId"]=flaskId},new RandomStreams(1));var flaskSale=(JObject)service.Sellables(shop).First(x=>(string)x["kind"]=="flask");var charges=shop["flaskCharges"]!.DeepClone();Check(service.Apply(shop,"sell",flaskSale),"Utility flask sale");Equal(shop["flaskCharges"]!,charges,"Utility flask sale never spends reusable charges");
shop["profileMeta"]=new JObject{["settings"]=new JObject{["shopSell"]=false}};Check(service.Sellables(shop).Count==0,"Disabled merchant buy-back has no rows");Atomic(shop,()=>service.Apply(shop,"sell",flaskSale),"Disabled resale command");
var growing=Player();growing["phase"]="Shop";var initialCharges=growing["flaskCharges"]!.DeepClone();content.CollectReward(growing,"relic",new JObject{["relicId"]="goldenSprout"},new RandomStreams(1));Check((int)growing["flaskCharges"]!["capacity"]! == (int)initialCharges["capacity"]!+1,"Acquire original growth relic");var growthSale=(JObject)service.Sellables(growing).First(x=>(string)x["id"]=="goldenSprout");Check(service.Apply(growing,"sell",growthSale),"Sell original growth source");Equal(growing["flaskCharges"]!,initialCharges,"Selling growth source unbinds capacity and current charges");
var badSale=Player();badSale["phase"]="Shop";content.CollectReward(badSale,"relic",new JObject{["relicId"]=relic},new RandomStreams(1));var badRow=(JObject)service.Sellables(badSale).First(x=>(string)x["id"]==relic);Atomic(badSale,()=>throwing.Apply(badSale,"sell",badRow),"Failing resale projection");
var cappedData=catalog.Data();cappedData["balance"]!["levelUp"]!["maxLevels"]=1;var capped=new OriginalRunServices(new OriginalContentCatalog(cappedData.ToString()),projection.Reconcile,Growth);var capPlayer=Player();Atomic(capPlayer,()=>capped.Apply(capPlayer,"levelUp",new JObject{["allocation"]=new JObject{["strength"]=2}}),"Authored level cap");Check(capped.Apply(capPlayer,"levelUp",new JObject{["allocation"]=new JObject{["strength"]=1}}),"Final allowed level");Check((bool)capped.LevelPlan(capPlayer)["capped"]! && !(bool)capped.LevelPlan(capPlayer)["offerable"]!,"Cap reflected in plan");
var noRules=Player();noRules.Remove("playerProjectionRules");Atomic(noRules,()=>service.Apply(noRules,"levelUp",allocation),"Missing frozen rules");
// Additional authored modifier exercises the same frozen projection seam; this
// fixture is intentionally not claimed as shipped original content.
var modifierData=catalog.Data();((JObject)modifierData["relics"]!.First(x=>(string)x["id"]==relic))["passives"]=new JObject{["modifiers"]=new JArray(new JObject{["tag"]="resource.flat",["resource"]="hp",["amount"]=10})};
var modCatalog=new OriginalContentCatalog(modifierData.ToString());var modProjection=new OriginalPlayerProjection(modCatalog,mechanics);var modContent=new OriginalRunContent(modCatalog,reconcile:modProjection.Reconcile);void ModGrowth(JObject r)=>growthMethod.Invoke(modContent,new object[]{r});
var modServices=new OriginalRunServices(modCatalog,modProjection.Reconcile,ModGrowth);var modPlayer=Player();modPlayer["phase"]="Shop";modPlayer["hp"]=(int)modPlayer["maxHp"]!-7;modContent.CollectReward(modPlayer,"relic",new JObject{["relicId"]=relic},new RandomStreams(1));var withBonus=(int)modPlayer["maxHp"]!;Check(modServices.Apply(modPlayer,"sell",(JObject)modServices.Sellables(modPlayer).First(x=>(string)x["id"]==relic)),"Sell authored resource modifier");Check((int)modPlayer["maxHp"]! == withBonus-10&&(int)modPlayer["maxHp"]!-(int)modPlayer["hp"]! ==7,"Resale updates maximum and preserves wounds");
var supplement=JObject.Parse(File.ReadAllText(repo+"/GameContent/Unity/Original/event-choices.json"));
var adapter=new ServiceAdapter(content,service);var session=OriginalRunSession.Start(catalog,supplement,Player(),1,adapter);
var graph=session.Map();var queue=new Queue<string[]>();foreach(var id in graph["startIds"]!.Values<string>())queue.Enqueue(new[]{id!});string[] route=null!;
while(queue.Count>0){var path=queue.Dequeue();var node=graph["nodes"]![path.Last()]!;if((string)node["type"]=="shrine"){route=path;break;}foreach(var id in node["next"]!.Values<string>())queue.Enqueue(path.Concat(new[]{id!}).ToArray());}
foreach(var id in route){Check(session.EnterNode(id),"Level service route legal");if(id==route.Last())break;while(session.Phase!=OriginalRunPhase.Map){switch(session.Phase){case OriginalRunPhase.Combat:session.CompleteCombat("victory",session.Player(),session.CreateRandom());break;case OriginalRunPhase.Rewards:session.ContinueRewards(false);break;case OriginalRunPhase.Shop:session.LeaveShop();break;case OriginalRunPhase.Shrine:session.LeaveShrine();break;case OriginalRunPhase.Event:session.ChooseEvent((string)session.EventChoices().Last()["id"]!);break;case OriginalRunPhase.EventResult:session.LeaveEvent();break;default:throw new Exception("Unexpected service route phase");}}}
Check(session.UseService("levelUp",allocation),"Existing run service abstraction commits shrine levels");Check(session.Phase==OriginalRunPhase.Shrine,"Integrated level allocation keeps shrine open");var saved=session.Snapshot();Equal(OriginalRunSession.Restore(saved,adapter).Snapshot(),saved,"Level service save/resume no reroll");
var rngBefore=session.Player()["streamCounters"]!.DeepClone();var funds=session.Player()["cinders"]!;Check(!session.UseService("levelUp",new JObject{["allocation"]=new JObject{["strength"]=1000}}),"Run service refuses unaffordable batch");Equal(session.Snapshot(),saved,"Rejected service restores full run");Equal(session.Player()["streamCounters"]!,rngBefore,"Run service consumes no RNG");
File.WriteAllText(Path.Combine(root,"TestResults/Parity/NativeServicesChecks-checks.json"),new JObject{["sourceCommit"]=oracle["sourceCommit"]!.DeepClone(),["status"]="passed",["checks"]=checks,["oracleCases"]=174}.ToString());
Console.WriteLine($"{checks} checks passed");

 }

sealed class ServiceAdapter:IOriginalRunContent
{
 private readonly OriginalRunContent _content;private readonly OriginalRunServices _services;
 public ServiceAdapter(OriginalRunContent content,OriginalRunServices services){_content=content;_services=services;}
 public void ApplyEffects(JObject run,JArray effects,RandomStreams rng)=>_content.ApplyEffects(run,effects,rng);
 public bool CollectReward(JObject run,string kind,JObject reward,RandomStreams rng)=>_content.CollectReward(run,kind,reward,rng);
 public bool ApplyService(JObject run,string service,JObject request,RandomStreams rng)=>service=="levelUp"||service=="sell"?_services.Apply(run,service,request):_content.ApplyService(run,service,request,rng);
}


}
