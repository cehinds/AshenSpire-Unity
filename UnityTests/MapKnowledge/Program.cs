// Compare integrated runtime projection with pinned JavaScript source receipts.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var oracle=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"source-reference.json")));
var checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
Check((string)oracle["originalCommit"]=="b17a7f4543e1710f49fae8b58880121690a314de","Reference commit differs");
foreach(var fixture in oracle["fixtures"]){
 var map=oracle["maps"][(int)fixture["mapIndex"]] as JObject;
 var before=map?.ToString();var path=fixture["path"].Values<string>().ToArray();
 var actual=OriginalMapKnowledge.Project(map,path,(string)fixture["currentId"],(bool)fixture["fog"],(bool)fixture["revealUnknown"],(bool)fixture["shrineGlow"]);
 var json=JObject.FromObject(actual);
 foreach(var field in new[]{"Nodes","VisibleIds","Edges"})Check(JToken.DeepEquals(json[field],fixture["expected"][field]),$"Original {field} differs in map {fixture["mapIndex"]}: {fixture}");
 Check(map?.ToString()==before,"Projection mutated source graph");
 Check(JToken.DeepEquals(new JArray(path),fixture["path"]),"Projection mutated source path");
 Check(!json.ToString().Contains("resolved"),"Resolved data leaked");
 var visible=actual.VisibleIds.ToHashSet();
 Check(actual.Edges.All(edge=>visible.Contains(edge.From)&&visible.Contains(edge.To)),"Edge leaks hidden ground");
}
foreach(var fixture in oracle["nearest"]){
 var map=oracle["maps"][(int)fixture["mapIndex"]] as JObject;var before=map?.ToString();
 var result=OriginalMapKnowledge.NearestShrine(map,fixture["from"].Values<string>());
 JToken actual=result==null?JValue.CreateNull():new JObject{["id"]=result.Id,["path"]=new JArray(result.Path),["distance"]=result.Distance};
 Check(JToken.DeepEquals(actual,fixture["expected"]),"Original shrine search differs: "+fixture);
 Check(map?.ToString()==before,"Shrine search mutated source graph");
}
// Every prefix retains ground revealed earlier, including unchosen branches.
foreach(var map in oracle["maps"].OfType<JObject>()){
 var path=new List<string>();var current=(string)map["startIds"]?.FirstOrDefault();var previous=new HashSet<string>();
 while(current!=null&&map["nodes"]?[current] is JObject node&&!path.Contains(current)){
  path.Add(current);var projection=OriginalMapKnowledge.Project(map,path,current);var visible=projection.VisibleIds.ToHashSet();
  Check(previous.IsSubsetOf(visible),"Fog closed over previously visible ground");
  previous=visible;current=(string)node["next"]?.LastOrDefault();
 }
}
Console.WriteLine($"PASS {checks} checks; {oracle["fixtures"].Count()} original projections, {oracle["nearest"].Count()} shrine searches.");
