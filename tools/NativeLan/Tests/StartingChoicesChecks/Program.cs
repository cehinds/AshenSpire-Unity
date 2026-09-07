using AshenSpire.Companion;
using AshenSpire.Transport;
using Newtonsoft.Json.Linq;
var root=LanTestPaths.ContentRoot;var factory=new OriginalLanGameFactory(root);var checks=0;
void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
JObject Setup(string keepsake="none",string glyph="⚔",string tint="gold",string style="animated")=>new(){["classId"]="reaver",["modeId"]="standard",["keepsakeId"]=keepsake,["customization"]=new JObject{["name"]="Mara the Forsaken",["glyph"]=glyph,["tint"]=tint,["spriteStyle"]=style}};
ILanGame Create(JObject setup)=>factory.Create(17,false,new[]{new LanMember("p1","Mara",setup),new LanMember("p2","Guest",new JObject{["classId"]="starseer",["modeId"]="standard"})});
var baseline=Create(Setup());var baseRun=baseline.View("p1")["local"]!["run"]!;
foreach(var keepsake in new[]{"none","oldCinder","travelersFlask","whetstoneMemory"}){
 var setup=Setup(keepsake,"🌙","frost","classic");var normalized=factory.ValidatePlayerSetup(setup);Check(JToken.DeepEquals(normalized["customization"],setup["customization"]),"cosmetics preserved");var game=Create(setup);var run=game.View("p1")["local"]!["run"]!;Check((string?)run["keepsakeId"]==keepsake&&JToken.DeepEquals(run["customization"],setup["customization"]),"choice IDs survive native birth");
 var saved=game.Snapshot();var restored=factory.Restore(saved);restored.SetConnected("p1",true);restored.SetConnected("p2",true);var resumed=restored.View("p1")["local"]!["run"]!;Check(JToken.DeepEquals(run,resumed),"keepsake never reapplied on restore/rejoin");
 if(keepsake=="oldCinder")Check((int)run["cinders"]!-(int)baseRun["cinders"]! == 50,"authored Old Cinder +50");
 if(keepsake=="travelersFlask")Check((int)run["flaskCharges"]!["capacity"]!-(int)baseRun["flaskCharges"]!["capacity"]! == 1,"authored Traveler capacity +1");
 if(keepsake=="whetstoneMemory")Check(!JToken.DeepEquals(run["deck"],baseRun["deck"])||!JToken.DeepEquals(run["itemUpgradeLevels"],baseRun["itemUpgradeLevels"]),"authored Whetstone improves starting armament/card");
}
foreach(var glyph in new[]{"⚔","🛡","🔥","🌙","☀","🐺"})foreach(var tint in new[]{"gold","ember","frost","rot","grace"})foreach(var style in new[]{"animated","rendered","classic","glyph"}){var row=factory.ValidatePlayerSetup(Setup(glyph:glyph,tint:tint,style:style));Check((string?)row["customization"]?["glyph"]==glyph&&(string?)row["customization"]?["tint"]==tint&&(string?)row["customization"]?["spriteStyle"]==style,"exact original choice tuple");}
void Reject(JObject setup,string label){bool rejected=false;try{factory.ValidatePlayerSetup(setup);}catch(ArgumentException){rejected=true;}Check(rejected,label);}
Reject(Setup("missing"),"unknown keepsake");Reject(Setup(glyph:"<script>"),"unknown glyph");Reject(Setup(tint:"red"),"unknown tint");Reject(Setup(style:"shader"),"unknown style");
foreach(var pair in new[]{("name",(JToken)new JValue(new string('x',129))),("name",new JValue("bad\nname")),("tint",new JValue(1)),("effects",new JArray()),("profileMeta",new JObject()),("figureId",new JValue("p2"))}){var setup=Setup();setup["customization"]![pair.Item1]=pair.Item2;Reject(setup,"invalid cosmetic "+pair.Item1);}
foreach(var field in new[]{"effects","hp","deck","profileMeta"}){var setup=Setup();setup[field]=new JObject();Reject(setup,"host authority "+field);}
var runBefore=baseline.View("p1")["local"]!["run"]!.DeepClone();baseline.AddMember(new LanMember("p3","Late",Setup("oldCinder","🔥","ember")));Check((string?)baseline.View("p3")["local"]?["run"]?["keepsakeId"]=="oldCinder","late member preserves keepsake");Check(JToken.DeepEquals(runBefore,baseline.View("p1")["local"]!["run"]!),"late keepsake never changes existing player");
var fixture=Path.Combine(LanTestPaths.OutputRoot,"AppearanceFixture");Directory.CreateDirectory(fixture);foreach(var name in new[]{"content.json","mechanics.json","progression.json","event-choices.json"})File.Copy(Path.Combine(root,name),Path.Combine(fixture,name));
var appearance=JObject.Parse(File.ReadAllText(Path.Combine(LanTestPaths.ContentRoot,"appearance-options.json")));((JArray)appearance["tints"]!).Add(new JObject{["id"]="testSilver",["name"]="Test Silver",["color"]="#aaa"});File.WriteAllText(Path.Combine(fixture,"appearance-options.json"),appearance.ToString());
var authored=new OriginalLanGameFactory(fixture);var customSetup=Setup(tint:"testSilver");Check((string?)authored.ValidatePlayerSetup(customSetup)["customization"]?["tint"]=="testSilver","authored sidecar IDs control availability");
var authoredGame=authored.Create(17,false,new[]{new LanMember("a","A",customSetup),new LanMember("b","B",Setup())});var authoredRestore=factory.Restore(authoredGame.Snapshot());authoredRestore.AddMember(new LanMember("c","C",customSetup));Check((string?)authoredRestore.View("c")["local"]?["run"]?["customization"]?["tint"]=="testSilver","restored host uses frozen appearance IDs for late join");
File.WriteAllText(Path.Combine(LanTestPaths.OutputRoot,"starting-choices.json"),new JObject{["checks"]=checks,["originalChoiceTuples"]=120,["keepsakes"]=4,["restoreRejoin"]=4,["runtimeAuthority"]="native authored OriginalRunSession effects, no client effects"}.ToString());Console.WriteLine($"PASS {checks} native companion starting-choice checks.");

