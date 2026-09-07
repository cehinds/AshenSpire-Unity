// Program.cs — original shape/config/map/RNG/sample parity plus native frozen-run checks.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var root=Directory.GetCurrentDirectory();
var output=args.FirstOrDefault()??Path.Combine(root,"TestResults/MapShape/checks.json");
var expected=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/MapShape/map-shape-reference.json")));
var original=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/combat-reference.json")));
var data=(JObject)original["content"];
var catalog=new OriginalContentCatalog(data.ToString());var rules=new OriginalRunRules(data);
var limits=(JObject)expected["limits"];int checks=0,graphs=0;
void Check(bool yes,string label){if(!yes)throw new Exception(label);checks++;}
bool Same(JToken a,JToken b){if(a==null||b==null)return a==b;if(a.Type is JTokenType.Float or JTokenType.Integer&&b.Type is JTokenType.Float or JTokenType.Integer)return(double)a==(double)b;if(a is JObject ao&&b is JObject bo)return ao.Count==bo.Count&&ao.Properties().All(p=>Same(p.Value,bo[p.Name]));if(a is JArray aa&&b is JArray ba)return aa.Count==ba.Count&&aa.Zip(ba).All(pair=>Same(pair.First,pair.Second));return JToken.DeepEquals(a,b);}
foreach(var row in expected["fixtures"])
{
 var config=(JObject)expected["configs"][row["act"].ToString()]; var frozen=config.DeepClone();JObject result=null;bool valid=true;
 try{result=OriginalMapShape.Apply(config,row["shape"],limits);}catch(ArgumentException){valid=false;}
 Check(valid==(bool)row["valid"],"shape acceptance "+row["act"]+" "+row["shape"]);
 Check(JToken.DeepEquals(config,frozen),"immutable authored config");
 if(!valid)continue;
 Check(Same(result["config"],row["config"]),"effective config");Check((bool)result["changed"]==(bool)row["changed"],"changed receipt");
 foreach(var sample in row["maps"]){var rng=new RandomStreams((uint)sample["seed"]);var map=rules.BuildAct(rng,(int)row["act"],new JArray(),OriginalMapShape.Normalize(row["shape"]),limits);Check(Same(map,sample["map"]),"graph parity "+row["shape"]);Check(Same(JObject.FromObject(rng.Snapshot()),sample["rng"]),"draw order");graphs++;}
}
foreach(var row in expected["samples"]){var config=(JObject)expected["configs"][row["act"].ToString()];var resolved=(JObject)OriginalMapShape.Apply(config,row["shape"],limits)["config"];Check(Same(OriginalMapShape.Sample(resolved,24),row["sample"]),"canonical 24-seed sample");}
foreach(var row in ((JObject)expected["minimumFloors"]).Properties())Check(OriginalMapShape.MinimumFloors((JObject)expected["configs"][row.Name])==(int)row.Value,"minimum floors");
var contentRoot=Path.Combine(root,"GameContent/Unity/Original");
var mechanics=JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"mechanics.json")));
var supplement=JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"event-choices.json")));supplement["mapShapeLimits"]=limits.DeepClone();
var progression=new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(contentRoot,"progression.json"))));
JObject Player(string deck="standard"){var creator=new CreationModel(catalog,"reaver","standard",progression);var p=new OriginalCharacterBuilder(catalog,progression,mechanics).Build(creator);new OriginalPlayerProjection(catalog,mechanics).Reconcile(p);p["custom"]=new JObject{["deckMode"]=deck,["mapShape"]=new JObject{["floors"]=7,["columns"]=2}};return p;}
var callbacks=new OriginalRunContent(catalog,reconcile:new OriginalPlayerProjection(catalog,mechanics).Reconcile);
OriginalRunSession Start(string deck="standard")=>OriginalRunSession.Start(catalog,supplement,Player(deck),13,callbacks);
var session=Start();Check((int)session.Map()["floors"]==7&&(int)session.Map()["columns"]==2,"shaped map boot");Check((bool)session.Player()["isCustom"]&&!(bool)session.Player()["countsForWinRate"],"shape marks custom");
var saved=session.Snapshot();Check(Same(OriginalRunSession.Restore(saved,callbacks).Snapshot(),saved),"exact shaped restore");
supplement["mapShapeLimits"]["minColumns"]=7;Check(Same(OriginalRunSession.Restore(saved,callbacks).Snapshot(),saved),"restore ignores current external limits");supplement["mapShapeLimits"]=limits.DeepClone();
foreach(var bad in new Action<JObject>[] {s=>s["run"]["custom"]["mapShape"]["floors"]=1,s=>s["run"]["mapGraph"]["columns"]=7,s=>s["run"]["mapShapeLimits"]["maxWeight"]=999,s=>s["run"]["custom"]["mapShape"]["typeWeights"]=new JObject{["bad"]=1}}){var corrupt=(JObject)saved.DeepClone();bad(corrupt);var refused=false;try{OriginalRunSession.Restore(corrupt,callbacks);}catch(ArgumentException){refused=true;}Check(refused,"corrupt shape refused");Check(Same(session.Snapshot(),saved),"corruption cannot mutate live run");}
var draft=Start("draft");for(var i=0;i<3;i++){var snap=draft.Snapshot();var copy=OriginalRunSession.Restore(snap,callbacks);var id=(string)draft.DraftChoices()[0];Check(draft.PickDraft(id)&&copy.PickDraft(id),"real draft selection");Check(Same(draft.Snapshot(),copy.Snapshot()),"draft shape exact continuation");}Check((int)draft.Map()["floors"]==7,"draft final boot shaped");
// Run-room transaction harness supplies combat outcomes deliberately; graph and
// room progression are real, but this loop is not playable combat evidence.
int steps=0,acts=0;
while(session.Phase!=OriginalRunPhase.Victory&&steps++<160)
{
 var before=session.Snapshot();Check(Same(OriginalRunSession.Restore(before,callbacks).Snapshot(),before),"room exact restore");
 if(session.ActNumber>acts){acts=session.ActNumber;Check((int)session.Map()["floors"]==7&&(int)session.Map()["columns"]==2,"every act stays shaped");}
 switch(session.Phase){case OriginalRunPhase.Map:session.EnterNode(session.LegalNodeIds()[0]);break;case OriginalRunPhase.Combat:session.CompleteCombat("victory",session.Player(),session.CreateRandom());break;case OriginalRunPhase.Rewards:session.ContinueRewards(true);break;case OriginalRunPhase.Shop:session.LeaveShop();break;case OriginalRunPhase.Shrine:session.LeaveShrine();break;case OriginalRunPhase.Event:var choice=(JObject)session.EventChoices().Last();session.ChooseEvent((string)choice["id"]);break;case OriginalRunPhase.EventResult:session.LeaveEvent();break;default:throw new Exception("Unexpected phase");}
}
Check(session.Phase==OriginalRunPhase.Victory&&acts==3,"three shaped acts traverse");
var report=new JObject{["checks"]=checks,["graphs"]=graphs,["fixtures"]=expected["fixtures"].Count(),["sampleSets"]=expected["samples"].Count(),["roomSteps"]=steps,["status"]="PASS"};Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,report.ToString()+"\n");Console.WriteLine(report);

Console.WriteLine($"PASS - {checks}/{checks} checks passed");
