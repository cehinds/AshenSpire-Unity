// Program.cs — pinned JS differential binding/text checks and real native bonus receipts.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AshenSpire.Domain;
using AshenSpire.Domain.Original;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var root = Directory.GetCurrentDirectory();
var fixtures = Path.Combine(root, "UnityTests/CardText");
var output = args.FirstOrDefault() ?? Path.Combine(root, "TestResults/CardText/checks.json");
var oracle = JObject.Parse(File.ReadAllText(Path.Combine(fixtures,"card-text-reference.json")));
var combat = JObject.Parse(File.ReadAllText(Path.Combine(root,"UnityTests/Parity/combat-reference.json")));
var catalog = new OriginalContentCatalog(combat["content"].ToString());
var checks = 0; var commands = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
foreach (var row in oracle["fixtures"])
{
    var def = (JObject)row["definition"];
    var frozen = def.DeepClone();
    Check(JToken.DeepEquals(OriginalCardText.ComputeTokenBindings(def["effects"] as JArray),row["bindings"]),row["id"] + " binding receipt");
    Check(JToken.DeepEquals(OriginalCardText.StaticTokens(def),row["tokens"]),row["id"] + " static numbers");
    Check(OriginalCardText.Describe(def,catalog) == (string)row["text"],row["id"] + " original text");
    Check(JToken.DeepEquals(def,frozen),row["id"] + " immutable input");
}
var repeated = (JObject)oracle["fixtures"].First(x => (string)x["id"] == "grammar/repeats")["definition"];
Check(OriginalCardText.Describe(repeated,catalog,new JObject()).Contains("2 5 7 3 5 2 4 1 6 0.25 {unknown} {Block} {block:bad}"),"formula context and unknown grammar visibility");
var culture = CultureInfo.CurrentCulture;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
Check(OriginalCardText.Describe(repeated,catalog).Contains("0.25"),"culture-independent authored numbers");
CultureInfo.CurrentCulture = culture;

// Attribute improvements are separate from original parity. Execute real original
// strike and multi-hit definitions with one injected progression receipt, then
// compare the engine's actual damage increment and the amount presented to users.
var bonusChecks = new JArray();
foreach (var hits in new[] {1,2,3,5})
{
    var fixture = combat["fixtures"][0];
    var basis = (JObject)combat["definitions"]["strike:false"].DeepClone();
    var damage = basis["effects"].OfType<JObject>().First(x => (string)x["op"] == "damage");
    damage["hits"] = hits;
    basis["textTemplate"] = "Deal {damage} damage {hits} times.";
    var improved = (JObject)basis.DeepClone();
    improved["effects"][0]["attributeBonus"] = 8;
    improved["attributeProgression"] = new JArray(new JObject { ["operation"]="damage", ["attribute"]="strength", ["bonus"]=8 });
    JArray Execute(JObject definition)
    {
        var player = (JObject)fixture["player"].DeepClone();
        var engine = new CombatSession(catalog,(JObject)combat["mechanics"],new RandomStreams((uint)fixture["seed"]),player,fixture["deck"].OfType<JObject>(),fixture["enemyIds"].Values<string>(),_ => (JObject)definition.DeepClone(),(double)fixture["hpMult"]);
        commands++;
        return engine.PlayCard("probe","e1");
    }
    var before = Execute(basis); var after = Execute(improved);
    int Damage(JArray events) => events.Where(x => (string)x["type"]=="damageDealt" && (string)x["sourceId"]=="player").Sum(x => (int)x["amount"]);
    Check(Damage(after)-Damage(before)==8,"bonus distributed once for " + hits + " hits");
    var text = OriginalCardText.Describe(improved,catalog);
    var shown = int.Parse(Regex.Match(text,@"Deal (\d+)").Groups[1].Value,CultureInfo.InvariantCulture);
    Check(shown == (int)damage["amount"]+(hits==1?8:0),"single-hit only displayed fold");
    Check(text.Contains(hits==1?"Includes +8":". +8"),"included versus additional total caption");
    bonusChecks.Add(new JObject { ["hits"]=hits,["text"]=text,["baseActualDamage"]=Damage(before),["improvedActualDamage"]=Damage(after) });
}
var repeatDef = (JObject)combat["definitions"]["strike:false"].DeepClone();
repeatDef["effects"][0]["repeat"] = 2;
repeatDef["effects"][0]["attributeBonus"] = 8;
repeatDef["attributeProgression"] = new JArray(new JObject { ["operation"]="damage",["attribute"]="strength",["bonus"]=8 });
Check(!OriginalCardText.Describe(repeatDef,catalog).Contains("Includes"),"repeat bonus remains separate");
var formulaDef = (JObject)repeatDef.DeepClone();
formulaDef["effects"][0]["amount"] = JObject.Parse("{f:'add',args:[2,4]}");
Check(OriginalCardText.Describe(formulaDef,catalog).Contains("{damage}"),"static formula remains visibly unresolved");
Check(OriginalCardText.Describe(formulaDef,catalog,new JObject()).StartsWith("Deal 6 damage"),"formula context delegates to evaluator without bonus folding");
var result = new JObject { ["checks"]=checks,["authoredDefinitions"]=364,["fixtures"]=oracle["fixtures"].Count(),["realCombatCommands"]=commands,["bonusReceipts"]=bonusChecks,["sourceCommit"]=oracle["sourceCommit"],["fixtureSha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtures,"card-text-reference.json")))).ToLowerInvariant(),["status"]="PASS" };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output,result.ToString(Formatting.Indented)+Environment.NewLine);
Console.WriteLine(result.ToString(Formatting.Indented));

Console.WriteLine($"PASS - {checks}/{checks} checks passed");
