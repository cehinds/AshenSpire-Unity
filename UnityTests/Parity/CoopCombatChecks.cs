using AshenSpire.Domain.Original;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
internal static class CoopCombatChecks
{
 public static int Run(string root)
 {
var reference=JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/coop-reference.json")));var catalog=new OriginalContentCatalog(reference["content"]!.ToString());var mechanics=JObject.Parse(File.ReadAllText(Path.Combine(root,"GameContent/Unity/Original/mechanics.json")));mechanics["stamina"]?["catchBreath"]?.Parent?.Remove();
JObject Resolve(string member,JObject card)=>(JObject)reference["definitions"]![(string)card["cardId"]!+":"+((bool?)card["upgraded"]==true?"true":"false")]!.DeepClone();
int checks=0;JToken Normal(JToken token){if(token is JValue v&&v.Value==null)return JValue.CreateNull();if(token is JObject o)return new JObject(o.Properties().Select(p=>new JProperty(p.Name,Normal(p.Value))));if(token is JArray a)return new JArray(a.Select(Normal));if(token.Type==JTokenType.Integer||token.Type==JTokenType.Float)return new JValue((double)token);return token.DeepClone();}
string Difference(JToken a,JToken b,string path="$"){if(JToken.DeepEquals(Normal(a),Normal(b)))return null!;if(a is JObject ao&&b is JObject bo){foreach(var p in ao.Properties()){if(bo[p.Name]==null)return path+"."+p.Name+" extra";var result=Difference(p.Value,bo[p.Name]!,path+"."+p.Name);if(result!=null)return result;}foreach(var p in bo.Properties())if(ao[p.Name]==null)return path+"."+p.Name+" missing";}if(a is JArray aa&&b is JArray ba){if(aa.Count!=ba.Count)return path+" count "+aa.Count+" != "+ba.Count;for(var i=0;i<aa.Count;i++){var result=Difference(aa[i],ba[i],path+"["+i+"]");if(result!=null)return result;}}return path+": "+a+" != "+b;}
void Equal(JToken actual,JToken expected,string label){var difference=Difference(actual,expected);if(difference!=null){File.WriteAllText("failure.json",new JObject{["label"]=label,["difference"]=difference,["actual"]=actual.DeepClone(),["expected"]=expected.DeepClone()}.ToString());throw new Exception(label+" "+difference);}checks++;}
JObject Project(OriginalCoopCombat game){var s=game.Snapshot();var players=game.Players;foreach(var player in players){((JObject)player["piles"]!).Remove("sealed");((JObject)player["piles"]!).Remove("removed");}return new JObject{["turn"]=game.Turn,["phase"]=game.Phase,["result"]=game.Result,["baseHpMultiplier"]=s["baseHpMultiplier"]!.DeepClone(),["extraHpMultiplier"]=s["extraHpMultiplier"]!.DeepClone(),["players"]=players,["enemies"]=game.Enemies,["events"]=s["events"]!.DeepClone(),["rng"]=s["rng"]!.DeepClone(),["outcome"]=game.Outcome()};}
foreach(var fixture in reference["fixtures"]!)
{
 var label=(string)fixture["label"]!;var game=new OriginalCoopCombat(catalog,mechanics,new RandomStreams((uint)fixture["seed"]!),fixture["players"]!.OfType<JObject>(),fixture["enemyIds"]!.Values<string>()!,Resolve);
 Equal(Project(game),fixture["states"]![0]!,label+" initial");int index=0;
 foreach(var request in fixture["steps"]!)
 {
  var saved=game.Snapshot();game=OriginalCoopCombat.Restore(catalog,mechanics,saved,Resolve);Equal(game.Snapshot(),saved,label+" exact restore");bool refused=false;
  try{var member=(string)request["member"]!;switch((string)request["op"]){case "play":var instance=game.Players.First(p=>(string)p["id"]==member)["piles"]!["hand"]!.FirstOrDefault(c=>(string)c["cardId"]==(string)request["card"]);game.Play(member,(string)instance?["instanceId"]??"missing",(string)request["target"]);break;case "end":game.EndTurn(member);break;case "leave":game.Leave(member);break;case "join":game.Join((JObject)request["player"]!);break;case "flask":game.UseFlask(member,(int)request["slot"]!,(string)request["target"],(string)request["kind"]);break;}}
  catch(ArgumentException){refused=true;}catch(InvalidOperationException){refused=true;}
  Equal(new JValue(refused),new JValue(request["error"]!.Type!=JTokenType.Null),label+" refusal "+index);Equal(Project(game),fixture["states"]![++index]!,label+" command "+index);
 }
}

return checks;
 }
}
