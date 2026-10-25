// Program.cs — pinned JS differential binding/text checks and real native bonus receipts.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AshenSpire.Domain;
using AshenSpire.Domain.Original;
using AshenSpire.Presentation;
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
var weaponReceipts = WeaponTextChecks.Run(catalog, combat, JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/progression.json"))), Check, out var weaponCommands);
commands += weaponCommands;

// US-13.4: every tag on every authored card yields its blurb through StatusExplainer.CardTags,
// the same id path the card face draws (explicit cardTags, else the catalog junction).
var shippedJson = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
var shipped = new OriginalContentCatalog(shippedJson.ToString());
var tagRows = shippedJson["tags"]!.OfType<JObject>().ToDictionary(x => (string)x["id"]!);
var taggedCards = 0; var tagLines = 0;
foreach (var card in shippedJson["cards"]!.OfType<JObject>())
{
    var frozenCard = card.DeepClone();
    var ids = StatusExplainer.TagIds(shipped, card).ToArray();
    var rows = StatusExplainer.CardTags(shipped, card);
    Check(JToken.DeepEquals(card, frozenCard), card["id"] + " tag explanation is read-only");
    foreach (var id in ids) Check(tagRows.ContainsKey(id), card["id"] + " tag '" + id + "' has an authored tag row");
    Check(rows.Select(r => r.Id).SequenceEqual(ids), card["id"] + " explains every tag in face order");
    foreach (var row in rows)
    {
        var blurb = (string)tagRows[row.Id]["blurb"]!;
        Check(!string.IsNullOrWhiteSpace(blurb) && row.Blurb == blurb && row.Line == (string)tagRows[row.Id]["label"]! + " — " + blurb, card["id"] + " tag '" + row.Id + "' yields its blurb");
        tagLines++;
    }
    if (ids.Length > 0) taggedCards++;
}
Check(taggedCards > 0 && tagLines >= taggedCards, "tag blurbs reachable for " + taggedCards + " authored cards (" + tagLines + " lines)");
var untagged = (JObject)shippedJson["cards"]!.OfType<JObject>().First(x => StatusExplainer.TagIds(shipped, x).Any()).DeepClone();
untagged["cardTags"] = new JArray();
Check(StatusExplainer.CardTags(shipped, untagged).Count == 0, "explicit empty cardTags stay empty");
untagged["cardTags"] = new JArray("noSuchTag");
Check(StatusExplainer.CardTags(shipped, untagged).Count == 0, "an unknown tag id is skipped like the card face");
Console.WriteLine("Card tag blurbs: " + taggedCards + " cards, " + tagLines + " lines");

// The illustrated face's tag rail (OriginalCardRailTags) comes from the resolved card: its marks
// and counts are the rules text's own numbers, and only ids with 1815 symbol art can show.
var railReference = JObject.Parse(File.ReadAllText(Path.Combine(root, "Unity/Assets/AshenSpire/Resources/Original/illustrated-reference-1815.json")));
var railSymbols = (JObject)railReference["tagSymbols"]!;
Func<string, bool> hasSymbol = id => railSymbols[id] != null;
string Rail(JObject card) => string.Join(",", OriginalCardRailTags.For(card, hasSymbol).Select(m => m.Id + (m.Count == null ? "" : "#" + m.Count)));
var shippedDefend = (JObject)shippedJson["cards"]!.First(c => (string)c["id"]! == "defend");
var shippedStrike = (JObject)shippedJson["cards"]!.First(c => (string)c["id"]! == "strike");
var defendBlock = (int)shippedDefend["effects"]![0]!["amount"]!;
Check(Rail(shippedDefend) == "category:skill,effect:block#" + defendBlock, "rail: defend shows Skill and its resolved Block " + defendBlock + " (" + Rail(shippedDefend) + ")");
Check(OriginalCardText.Describe(shippedDefend, shipped, includeRatingBreakdown: false).Contains(defendBlock + " Block"), "rail: defend's Block count is the number its rules text prints");
Check(Rail(shippedStrike) == "camp:physical", "rail: strike shows only Physical, no reference-only Precision (" + Rail(shippedStrike) + ")");
var boostedDefend = (JObject)shippedDefend.DeepClone(); boostedDefend["effects"]![0]!["attributeBonus"] = 3;
Check(Rail(boostedDefend) == "category:skill,effect:block#" + (defendBlock + 3) && OriginalCardText.Describe(boostedDefend, shipped, includeRatingBreakdown: false).Contains((defendBlock + 3) + " Block"),
    "rail: a folded attribute bonus raises the Block count exactly as the rules text does");
var railCards = 0;
foreach (var card in shippedJson["cards"]!.OfType<JObject>())
{
    var frozenCard = card.DeepClone();
    var marks = OriginalCardRailTags.For(card, hasSymbol);
    Check(JToken.DeepEquals(card, frozenCard) && marks.Count <= 5 && marks.All(m => hasSymbol(m.Id)) && marks.Select(m => m.Id).Distinct().Count() == marks.Count, card["id"] + " rail: read-only, at most five known symbols");
    var text = OriginalCardText.Describe(card, shipped, includeRatingBreakdown: false);
    foreach (var mark in marks.Where(m => m.Count != null))
        Check(System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<!\d)" + mark.Count + @"(?!\d)"), card["id"] + " rail: " + mark.Id + " count " + mark.Count + " appears in its rules text");
    if (marks.Count > 0) railCards++;
}
Console.WriteLine("Card tag rails: " + railCards + " cards");
// The footer word is the resolved Unity card's type label and its sigil the reference mark for that
// type (generic family mark otherwise), never the frozen JS stance identity (Shield Bash is an ATTACK).
var cardTypes = (JObject)shippedJson["balance"]!["ui"]!["cardTypes"]!;
var railSigils = (JObject)railReference["sigils"]!;
Func<string, bool> hasSigil = id => railSigils[id]?["resource"] != null;
var footers = 0;
foreach (var card in shippedJson["cards"]!.OfType<JObject>())
{
    var unityType = (string)card["type"]!;
    var expected = (string?)cardTypes[unityType]?["label"] ?? OriginalCardText.Humanize(unityType);
    Check(OriginalCardRailTags.TypeLabel(card, cardTypes) == expected, card["id"] + " footer: word is its Unity type label " + expected);
    var sigil = OriginalCardRailTags.FooterSigil(unityType, hasSigil);
    Check(sigil != null && hasSigil(sigil) && (sigil == unityType || !hasSigil(unityType)), card["id"] + " footer: sigil " + sigil + " is the reference mark for " + unityType);
    footers++;
}
var shieldBash = (JObject)shippedJson["cards"]!.First(c => (string)c["id"]! == "shieldBash");
Check(OriginalCardRailTags.TypeLabel(shieldBash, cardTypes).ToUpperInvariant() == "ATTACK" && OriginalCardRailTags.FooterSigil("attack", hasSigil) == "attack",
    "footer: Shield Bash reads ATTACK with the attack sigil, not the reference's COUNTER");
Check(OriginalCardRailTags.FooterSigil("curse", hasSigil) == "status" && OriginalCardRailTags.FooterSigil("noSuchType", hasSigil) == "skill", "footer: types without a mark use the generic family sigil");
Console.WriteLine("Card footers: " + footers + " cards");
// Rules are never silently clipped (OriginalCardRulesFit): every shipped card's resolved face
// rules must fit its 1815 parchment at some step, measured conservatively from the bundled
// rules font (FontMetrics.CardFaceFont: Liberation Serif, the face's -unity-font and the bundled
// stand-in for the JS card's Georgia). Step 4 would rely on card inspection's details, which
// print the complete rules; no shipped card may need it.
var rulesFont = new FontMetrics(Path.Combine(root, FontMetrics.CardFaceFont));
var rulesLayer = (JObject)railReference["documents"]!["template"]!["layers"]!.First(l => (string?)l["bind"] == "rules")!;
float rw = (float)rulesLayer["w"]!, rh = (float)rulesLayer["h"]!, rmax = (float)rulesLayer["maxFontSize"]!, rmin = (float)rulesLayer["minFontSize"]!;
var fitSteps = new SortedDictionary<int, List<string>>();
var fitReceipt = new JArray();
foreach (var card in shippedJson["cards"]!.OfType<JObject>())
{
    var faceRules = OriginalCardText.Describe(card, shipped, includeRatingBreakdown: false);
    var plan = OriginalCardRulesFit.Choose(faceRules, rw, rh, rmax, rmin, OriginalCardRulesFit.FloorFontSize, rulesFont.Height);
    var step = (int)plan.Step;
    if (!fitSteps.TryGetValue(step, out var ids)) fitSteps[step] = ids = new List<string>();
    ids.Add((string)card["id"]!);
    fitReceipt.Add(new JObject { ["id"] = card["id"], ["step"] = step, ["stepName"] = plan.Step.ToString(), ["size"] = Math.Round(plan.Size, 2), ["text"] = plan.Text });
    if (plan.Clipped)
        Check(OriginalCardText.Describe(card, shipped).Contains(faceRules.TrimEnd('.')), card["id"] + " rules: too long for the face, so inspection details print them in full");
    else
        Check(rulesFont.Height(plan.Text, plan.Size, rw) <= rh + .5f && plan.Size >= OriginalCardRulesFit.FloorFontSize, card["id"] + " rules: fit at step " + step + " size " + plan.Size.ToString("0.#"));
    // Nothing is dropped by any step: the shown text keeps every clause's words.
    foreach (var clause in OriginalCardRulesFit.FaceLines(faceRules).Split('\n'))
        Check(plan.Text.Contains(clause), card["id"] + " rules: clause '" + clause + "' is kept");
}
Check(fitSteps.Values.Sum(v => v.Count) == shippedJson["cards"]!.Count(), "rules: every shipped card resolves to a fit step");
Check(!fitSteps.ContainsKey((int)OriginalCardRulesFit.Step.Overflow), "rules: no shipped card needs step 4 (" + string.Join(", ", fitSteps.GetValueOrDefault((int)OriginalCardRulesFit.Step.Overflow) ?? new List<string>()) + ")");
var defendPlan = OriginalCardRulesFit.Choose("Gain 5 Block.", rw, rh, rmax, rmin, OriginalCardRulesFit.FloorFontSize, rulesFont.Height);
Check(defendPlan.Step == OriginalCardRulesFit.Step.FaceLines && defendPlan.Size == rmax, "rules: a short card keeps the JS face lines at full size");
foreach (var (step, ids) in fitSteps)
    Console.WriteLine("Rules fit step " + step + " (" + (OriginalCardRulesFit.Step)step + "): " + ids.Count + " cards" + (step >= 2 ? " — " + string.Join(", ", ids) : ""));
var result = new JObject { ["checks"]=checks,["authoredDefinitions"]=364,["fixtures"]=oracle["fixtures"].Count(),["realCombatCommands"]=commands,["bonusReceipts"]=bonusChecks,["weaponReceipts"]=weaponReceipts,["sourceCommit"]=oracle["sourceCommit"],["fixtureSha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtures,"card-text-reference.json")))).ToLowerInvariant(),["status"]="PASS" };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output,result.ToString(Formatting.Indented)+Environment.NewLine);
// Per-card rules fit receipt (step and template font size), measured with FontMetrics.CardFaceFont.
File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "rules-fit.json"),
    new JObject { ["font"] = FontMetrics.CardFaceFont, ["floor"] = OriginalCardRulesFit.FloorFontSize, ["box"] = new JArray(rw, rh), ["cards"] = fitReceipt }.ToString(Formatting.Indented) + Environment.NewLine);
Console.WriteLine(result.ToString(Formatting.Indented));

Console.WriteLine($"PASS - {checks}/{checks} checks passed");
