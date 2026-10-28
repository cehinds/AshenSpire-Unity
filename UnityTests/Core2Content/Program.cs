// Core 2 export and JS oracle checks (SPEC.md §0.9 phase 4).
// Run from the repository root: dotnet run --project UnityTests/Core2Content [-- <repo root>]
//
// 1. The export (GameContent/Unity/Core2) names the SPEC §0.2 pin, lists exactly its files, and
//    loads through the engine-free loader with every sha256, count, id and cross-reference intact.
// 2. The Resources/Core2 mirror is byte-identical and every asset has a Unity .meta.
// 3. The JS oracle fixtures (Oracles/*.json, tools/oracle-js-core2.mjs) come from the same commit
//    and profile hash, are well formed, and name only ids the export contains. The starter decks
//    and the phase 5a families (pricing, hit chance, Poise absorption, intent damage ranges), 5b
//    families (matchups, Counter returns), 5c families (enemy SP planning, whole scripted fights) and
//    phase 7 families (fights with reactions on, their combat log, Auto and pacing) and fuzz-fights (every enemy, reactions off and on) are replayed case by case through Runtime/Domain/Core2/Combat (OracleReplay.cs). A family with
//    no replay yet would be reported PENDING with its reason. The phase 8 run families are checked
//    for form here and replayed by UnityTests/Core2Run.
using System.Text;
using System.Text.RegularExpressions;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
var checks = 0;
var failures = 0;
var pending = new List<string>();
var replayed = new List<string>();
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
string Full(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
string Read(string relative) => File.ReadAllText(Full(relative), new UTF8Encoding(false));
IEnumerable<string> FilesUnder(string relative) => Directory.Exists(Full(relative))
    ? Directory.GetFiles(Full(relative), "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(Full(relative), p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal)
    : Enumerable.Empty<string>();

const string Export = "GameContent/Unity/Core2";
const string Mirror = "Unity/Assets/AshenSpire/Resources/Core2";
const string Oracles = "UnityTests/Core2Content/Oracles";

try
{
    // ---- 1. The pin ------------------------------------------------------------------------
    var spec = Read("SPEC.md");
    var pinCommit = Regex.Match(spec, @"\|\s*Commit \(the pin\)\s*\|\s*`([0-9a-f]{40})`").Groups[1].Value;
    var pinBuild = Regex.Match(spec, @"\|\s*Build\s*\|\s*`(\d+\.\d+\.\d+\.\d+)`, digest `([0-9a-f]+)`");
    var pinProfile = Regex.Match(spec, @"\|\s*Profile\s*\|\s*`([^`]+)`").Groups[1].Value;
    Check(pinCommit.Length == 40 && pinBuild.Success && pinProfile.Length > 0, "SPEC.md §0.2 pin table is readable");
    var manifest = new Core2Manifest(Read(Export + "/manifest.json"));
    Equal(manifest.JsCommit, pinCommit, "export manifest commit is the SPEC §0.2 pin (regenerate with tools/export-js-core2.mjs after a pin bump)");
    Equal(manifest.JsBuild, pinBuild.Groups[1].Value, "export manifest build is the SPEC §0.2 build");
    Equal(manifest.JsDigest, pinBuild.Groups[2].Value, "export manifest digest is the SPEC §0.2 digest");
    Equal(manifest.Core, pinProfile, "export profile is the SPEC §0.2 profile");
    Check(manifest.ProfileHash.StartsWith("sha256:") && manifest.ProfileHash.Length == 71, "manifest records the resolved profile's sha256 rules hash");
    Equal(manifest.Core2ActionRulesVersion, 1, "manifest records core2ActionRulesVersion 1 (JS §4.6a)");

    // ---- 1b. Exactly the listed files, each matching its hash ------------------------------
    var listed = manifest.Files.Select(f => f.Path).Append("manifest.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
    var present = FilesUnder(Export).ToList();
    Check(listed.SequenceEqual(present), "GameContent/Unity/Core2 holds exactly the manifest's files" + (listed.SequenceEqual(present) ? "" : ": extra [" + string.Join(", ", present.Except(listed)) + "], missing [" + string.Join(", ", listed.Except(present)) + "]"));
    foreach (var file in manifest.Files)
    {
        var bytes = File.ReadAllBytes(Full(Export + "/" + file.Path));
        Equal((long)bytes.Length, file.Bytes, file.Path + " byte length matches the manifest");
        Equal(Core2Content.Sha256Hex(new UTF8Encoding(false).GetString(bytes)), file.Sha256, file.Path + " sha256 matches the manifest");
    }

    // ---- 1c. Loader: counts, ids, schema --------------------------------------------------
    var content = Core2Content.LoadFolder(Full(Export));
    Check(true, "Core2Content.LoadFolder validates every table and cross-reference");
    Equal(content.Classes.Count, manifest.Count("classes"), "classes count");
    Equal(content.Characters.Count, manifest.Count("characters"), "characters count");
    Equal(content.Cards.Count, manifest.Count("profileCards"), "profile cards count");
    Equal(content.Cards.Count(c => c.Id.StartsWith("core.")), manifest.Count("coreCards"), "core card count");
    Equal(content.Cards.Count(c => c.Id.StartsWith("base.")), manifest.Count("baseCards"), "base card count");
    Equal(content.EnemyCards.Count, manifest.Count("enemyCards"), "enemy cards count");
    Equal(content.Enemies.Count, manifest.Count("enemies"), "enemies count");
    Equal(content.EnemyRelics.Count, manifest.Count("enemyRelics"), "enemy relics count");
    Equal(content.Gear.Count, manifest.Count("gear"), "gear count");
    Equal(content.Gear.Count(g => g.Slot == "rightHand"), manifest.Count("gearRightHand"), "right-hand gear count");
    Equal(content.Gear.Count(g => g.Slot == "leftHand"), manifest.Count("gearLeftHand"), "left-hand gear count");
    Equal(content.Gear.Count(g => g.Slot != "rightHand" && g.Slot != "leftHand"), manifest.Count("gearArmour"), "armour gear count");
    Equal(content.Relics.Count, manifest.Count("relics"), "adapter relics count");
    Equal(content.Events.Count, manifest.Count("events"), "adapter events count");
    Equal(content.Flasks.Count, manifest.Count("flasks"), "adapter flasks (potions) count");
    Equal(content.Statuses.Count, manifest.Count("statuses"), "statuses count");
    Equal(content.Encounters.Count, manifest.Count("encounters"), "encounters count");
    Equal(content.Seats.Count, manifest.Count("seats"), "seats count");
    Equal(content.AdapterCards.Count, manifest.Count("adapterCards"), "adapter cards count");
    Equal(content.Armaments.Count, manifest.Count("armaments"), "armaments count");
    Equal(content.Armour.Count, manifest.Count("armour"), "armour rows count");
    foreach (var file in manifest.Files)
    {
        var token = content.Table(file.Path);
        foreach (var table in file.Tables)
        {
            var inner = token is JObject obj && obj[table.Key] != null && (file.Tables.Count > 1 || obj[table.Key] is JArray) ? obj[table.Key] : token;
            var actual = inner is JArray array ? array.Count : inner is JObject o ? o.Count : -1;
            if (file.Path == "adapter/enemies.json" && table.Key == "moves") actual = content.Enemies.Sum(e => e.Moves.Count);
            if (file.Path == "profile.json" || file.Path == "mechanics.json") continue; // summary counts of nested objects, checked below
            Equal(actual, table.Value, file.Path + " table " + table.Key + " row count");
        }
    }
    Equal(content.ModuleIds.Count, manifest.File("profile.json").Tables["modules"], "profile modules count");
    Check(content.Profile["contentKeys"] is JArray keys && keys.Select(k => (string)k).Where(k => content.Profile["contentScalars"]?[k] == null).All(k => manifest.Files.Any(f => f.Path == "pack/" + k + ".json")), "every profile content table has a pack/<key>.json file");
    foreach (var module in new[] { "cards", "characters", "enemies", "equipment", "gear", "resources", "rest", "rewards", "routes", "shops", "stances", "reactions", "effects", "progression", "statuses" })
        Check(content.HasModule(module), "profile enables module " + module);
    Equal((int?)content.Profile["modules"]?["effects"]?["fixCoreVersion"], 1, "effects module is FixCore version 1");
    Check(content.Mechanics["fixCoreMechanics"] is JObject fx && fx["poise"] != null && fx["accuracy"] != null && fx["counter"] != null && fx["smashVsDefensive"] != null, "mechanics.json carries the FixCore value blocks phase 5 reads");
    Check(new[] { "reaver", "starseer", "rogue", "herald" }.All(id => content.Classes.Any(c => c.Id == id)) && content.Classes.Count == 4, "the four Core 2 classes");
    Check(content.Classes.All(c => c.Attributes.Values.Sum() == (int)content.Stats["attributePointTotal"]), "every class spends exactly the stats.json attribute point total");
    Check(new[] { "core.strike", "core.defend", "core.counter", "core.smash", "core.sweep", "core.evade", "core.reaverSpecial", "core.starseerSpecial", "core.rogueSpecial", "core.heraldSpecial", "core.cantrip", "core.manaSurge", "core.advance", "core.closingDodge", "core.backstep" }
        .All(content.HasCard), "the 15 Core card definitions are present");
    Equal(string.Join(",", content.ClassUnlocks["initial"].Select(t => (string)t)), "reaver", "only Reaver is selectable at first");
    Check(content.Cards.Where(c => c.XCost).All(c => c.Cost == 0), "X-cost cards load with XCost and no fixed cost");
    Check(content.Enemies.All(e => e.Moves.Count > 0 && e.HpMax >= e.HpMin && e.HpMin > 0), "every enemy has moves and a positive HP range");
    Check(content.Routes.Acts == 3 && content.Routes.MaxBranches.HasValue, "routes: three acts and a branched map (maxBranches)");
    Check(manifest.ScriptedEffects.Contains("wondrousDraught"), "the one scripted JS effect (wondrousDraught) is listed for a hand port");

    // ---- 1d. The loader refuses an altered or incomplete export ----------------------------
    string Reader(string path) => Read(Export + "/" + path);
    Check(Throws(() => Core2Content.Load(path => path == "pack/cards.json" ? Reader(path).Replace("\"Strike\"", "\"Strikes\"") : Reader(path))), "an edited table fails its sha256 check");
    Check(Throws(() => Core2Content.Load(path => path == "adapter/relics.json" ? null : Reader(path))), "a missing table fails to load");
    Check(Throws(() => new Core2Manifest(Reader("manifest.json").Replace("ashen.core@2.0.0", "ashen.core@1.0.0"))), "a manifest for another core is refused");
    Check(!Throws(() => Core2Content.Load(path => Reader(path).Replace("\n", "\r\n"))), "CRLF line endings (a Windows TextAsset) still verify");

    // ---- 2. Resources mirror ---------------------------------------------------------------
    var mirrored = FilesUnder(Mirror).Where(p => !p.EndsWith(".meta")).ToList();
    Check(mirrored.SequenceEqual(listed), "Resources/Core2 mirrors exactly the export's files");
    foreach (var path in listed)
    {
        var same = File.Exists(Full(Mirror + "/" + path)) && File.ReadAllBytes(Full(Mirror + "/" + path)).SequenceEqual(File.ReadAllBytes(Full(Export + "/" + path)));
        Check(same, "Resources/Core2/" + path + " is byte-identical to GameContent");
        Check(File.Exists(Full(Mirror + "/" + path + ".meta")) && Regex.IsMatch(Read(Mirror + "/" + path + ".meta"), @"guid: [0-9a-f]{32}\n") && Read(Mirror + "/" + path + ".meta").Contains("TextScriptImporter"), "Resources/Core2/" + path + " has a TextAsset .meta");
    }
    foreach (var folder in new[] { "", "/pack", "/adapter" })
        Check(File.Exists(Full(Mirror + folder + ".meta")) && Read(Mirror + folder + ".meta").Contains("folderAsset: yes"), "Resources/Core2" + folder + " folder has a .meta");
    var guids = FilesUnder(Mirror).Where(p => p.EndsWith(".meta")).Select(p => Regex.Match(Read(Mirror + "/" + p), @"guid: ([0-9a-f]{32})").Groups[1].Value).ToList();
    Equal(guids.Distinct().Count(), guids.Count, "mirror .meta guids are distinct");
    var build = Read("Unity/Assets/AshenSpire/Editor/BuildTools.cs");
    Check(build.Contains("ImportCore2()") && build.Contains("Core2Content.LoadFolder"), "BuildTools.ImportContent validates and mirrors the Core 2 export");
    var runtime = Read("Unity/Assets/AshenSpire/Runtime/Application/Core2Resources.cs");
    Check(runtime.Contains("Resources.Load<TextAsset>") && runtime.Contains("Core2Content.Folder"), "Core2Resources reads the mirror through Resources.Load");
    var asmdef = JObject.Parse(Read("Unity/Assets/AshenSpire/Runtime/Domain/Core2/AshenSpire.Core2.asmdef"));
    Check((bool?)asmdef["noEngineReferences"] == true, "the Core 2 loader assembly is engine-free");
    Check(!Directory.GetFiles(Full("Unity/Assets/AshenSpire/Runtime/Domain/Core2"), "*.cs", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("using UnityEngine")), "no Core 2 loader, combat or run file uses UnityEngine");
    Check(Directory.GetFiles(Full("Unity/Assets/AshenSpire/Runtime/Domain/Core2"), "*.cs", SearchOption.AllDirectories).All(f => File.Exists(f + ".meta")) && File.Exists(Full("Unity/Assets/AshenSpire/Runtime/Domain/Core2/Combat.meta")) && File.Exists(Full("Unity/Assets/AshenSpire/Runtime/Domain/Core2/Run.meta")), "every Core 2 source file and folder has a Unity .meta");

    // ---- 3. JS oracle fixtures -------------------------------------------------------------
    var expectedFamilies = new Dictionary<string, string>
    {
        ["starter-decks"] = null,
        ["pricing"] = null,
        ["hit-chance"] = null,
        ["poise-absorb"] = null,
        ["damage-ranges"] = null,
        ["matchups"] = null,
        ["counter-return"] = null,
        ["enemy-planning"] = null,
        ["scripted-fights"] = null,
        ["reaction-fights"] = null,
        ["fuzz-fights"] = null,
        ["combat-log"] = null,
        ["combat-automation"] = null,
    };
    // Phase 8, 9 and 10 run-layer families: checked for form here, replayed case by case by UnityTests/Core2Run.
    var runFamilies = new[] { "run-creation", "run-maps", "card-faces", "reward-offers", "shop-stock", "rest-outcomes", "level-up", "scripted-runs", "scripted-runs-auto",
        "adept-entitlements", "deck-edits", "armoury", "skill-progression" };
    foreach (var runFamily in runFamilies) expectedFamilies[runFamily] = null;
    var fixtures = FilesUnder(Oracles).Where(p => p.EndsWith(".json")).ToList();
    Check(fixtures.Select(p => p.Substring(0, p.Length - 5)).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(expectedFamilies.Keys.OrderBy(p => p, StringComparer.Ordinal)), "Oracles/ holds exactly the " + expectedFamilies.Count + " oracle families (" + runFamilies.Length + " replayed by UnityTests/Core2Run)");
    Func<Core2Content, JObject, OracleReplay.Result> Cases(Func<Core2Content, JArray, OracleReplay.Result> replay) => (c, data) => replay(c, (JArray)data["cases"]);
    var replays = new Dictionary<string, Func<Core2Content, JObject, OracleReplay.Result>>
    {
        ["pricing"] = Cases(OracleReplay.Pricing),
        ["hit-chance"] = Cases(OracleReplay.HitChance),
        ["poise-absorb"] = Cases(OracleReplay.PoiseAbsorb),
        ["damage-ranges"] = Cases(OracleReplay.DamageRanges),
        ["matchups"] = Cases(OracleReplay.Matchups),
        ["counter-return"] = Cases(OracleReplay.CounterReturn),
        ["enemy-planning"] = OracleReplay.EnemyPlanning,
        ["scripted-fights"] = OracleReplay.ScriptedFights,
        ["reaction-fights"] = OracleReplay.ReactionFights,
        ["fuzz-fights"] = OracleReplay.FuzzFights,
        ["combat-log"] = OracleReplay.CombatLog,
        ["combat-automation"] = Cases(OracleReplay.CombatAutomation),
    };
    OracleReplay.OraclesFolder = Full(Oracles);
    var enemyIds = new HashSet<string>(content.Enemies.Select(e => e.Id));
    var classIds = new HashSet<string>(content.Classes.Select(c => c.Id));
    foreach (var name in fixtures)
    {
        var family = name.Substring(0, name.Length - 5);
        var data = JObject.Parse(Read(Oracles + "/" + name));
        var cases = data["cases"] as JArray ?? new JArray();
        Equal((string)data["family"], family, family + ": family id matches the file name");
        Equal((string)data["js"]?["commit"], manifest.JsCommit, family + ": generated at the export's JS commit");
        Equal((string)data["profileHash"], manifest.ProfileHash, family + ": generated from the export's profile hash");
        Equal((int?)data["caseCount"], cases.Count, family + ": caseCount matches the cases");
        Check(cases.Count > 0 && cases.All(c => c["input"] is JObject && c["expected"] is JObject), family + ": every case has an input and an expected object");
        var unknown = new List<string>();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            foreach (var id in input.Properties().Where(p => p.Name == "enemyId").Select(p => (string)p.Value).Concat((input["enemyIds"] as JArray ?? new JArray()).Select(t => (string)t)))
                if (!enemyIds.Contains(id)) unknown.Add("enemy " + id);
            if (input["classId"] != null && !classIds.Contains((string)input["classId"])) unknown.Add("class " + input["classId"]);
            if (input["cardId"] != null && !content.HasCard((string)input["cardId"])) unknown.Add("card " + input["cardId"]);
            if (family == "enemy-planning")
                foreach (var move in ((JObject)row["expected"]["quotes"]).Properties())
                    if (content.Enemy((string)input["enemyId"]).Move(move.Name) == null) unknown.Add("move " + input["enemyId"] + "." + move.Name);
        }
        Check(unknown.Count == 0, family + ": names only ids the export contains" + (unknown.Count == 0 ? "" : " (" + string.Join(", ", unknown.Distinct().Take(10)) + ")"));
        if (family == "starter-decks")
        {
            foreach (var row in cases)
            {
                var classId = (string)row["input"]["classId"];
                var expected = string.Join(",", row["expected"]["deck"].Select(t => (string)t));
                Equal(string.Join(",", content.StarterDeck(classId)), expected, "starter deck for " + classId + " matches JS gameProfileStarterDeck");
            }
        }
        else if (replays.TryGetValue(family, out var replay))
        {
            var outcome = replay(content, data);
            foreach (var failure in outcome.Failures) Console.Error.WriteLine("FAIL: " + failure);
            foreach (var excluded in outcome.Excluded) Console.WriteLine("EXCLUDED: " + family + ": " + excluded);
            Check(outcome.Cases + outcome.Excluded.Count == cases.Count && outcome.Passed == outcome.Cases, family + ": " + outcome.Passed + "/" + outcome.Cases
                + " JS oracle cases match the Core 2 combat domain" + (outcome.Excluded.Count > 0 ? " (" + outcome.Excluded.Count + " excluded with a reason)" : ""));
            replayed.Add(family + " " + outcome.Passed + "/" + cases.Count);
        }
        else if (expectedFamilies.TryGetValue(family, out var reason) && reason != null)
            pending.Add(family + " (" + cases.Count + " cases): " + reason);
    }
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: " + error);
}

foreach (var line in replayed) Console.WriteLine("ORACLE: " + line + " cases pass");
foreach (var line in pending) Console.WriteLine("PENDING: " + line);
if (failures > 0) { Console.Error.WriteLine("Core2Content: " + failures + " failed, " + checks + " passed"); return 1; }
Console.WriteLine("Core2Content: " + replayed.Count + " oracle families replayed" + (pending.Count > 0 ? ", " + pending.Count + " pending (listed above)." : "."));
Console.WriteLine("Core2Content: " + checks + " checks passed");
return 0;
