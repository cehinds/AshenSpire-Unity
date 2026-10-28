// The Core 2 run layer (SPEC.md §0.9 phase 8), engine-free (Runtime/Domain/Core2/Run).
// Run from the repository root: dotnet run --project UnityTests/Core2Run [-- <repo root>]
//
// 1. Every phase 8 JS oracle family (UnityTests/Core2Content/Oracles; tools/js-core2-run.mjs) is replayed case by case:
//    run creation, act maps, card faces, reward offers, shop stock, rest outcomes, level-up and whole scripted runs.
// 2. Focused checks the oracles do not reach: class unlocks, the save format and its refusals, rest services once per
//    visit, screen transitions, event gates and the Core 2 class lock, flasks (Wondrous Draught), the map's shape rules.
using System.Text;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
var checks = 0;
var failures = 0;
var replayed = new List<string>();
var pending = new List<string>();
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
string Full(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
string Read(string relative) => File.ReadAllText(Full(relative), new UTF8Encoding(false));

const string Export = "GameContent/Unity/Core2";
const string Oracles = "UnityTests/Core2Content/Oracles";

try
{
    var content = Core2Content.LoadFolder(Full(Export));
    var data = new Core2RunData(content);
    Check(true, "Core2RunData reads adapter/run.json (classes, balance, property rules, boss labels, faces, Smith ladder)");

    // ---- 1. JS oracle families ----------------------------------------------------------------------------------
    var families = new (string Family, Func<Core2RunData, JArray, RunReplay.Result> Replay)[]
    {
        ("run-creation", RunReplay.Creation), ("run-maps", RunReplay.Maps), ("card-faces", RunReplay.Faces), ("reward-offers", RunReplay.Rewards),
        ("shop-stock", RunReplay.Shops), ("rest-outcomes", RunReplay.Rests), ("level-up", RunReplay.Levels), ("scripted-runs", RunReplay.ScriptedRuns),
        ("scripted-runs-auto", RunReplay.ScriptedRunsAuto),
    };
    foreach (var (family, replay) in families)
    {
        var fixture = JObject.Parse(Read(Oracles + "/" + family + ".json"));
        Equal((string)fixture["js"]?["commit"], content.Manifest.JsCommit, family + ": generated at the export's JS commit");
        Equal((string)fixture["profileHash"], content.Manifest.ProfileHash, family + ": generated from the export's profile hash");
        var cases = (JArray)fixture["cases"];
        var outcome = replay(data, cases);
        foreach (var failure in outcome.Failures) Console.Error.WriteLine("FAIL: " + failure);
        foreach (var reason in outcome.Pending) pending.Add(reason);
        Check(outcome.Cases == cases.Count && outcome.Passed + outcome.Pending.Count == outcome.Cases,
            family + ": " + outcome.Passed + "/" + cases.Count + " JS oracle cases match the Core 2 run layer" + (outcome.Pending.Count > 0 ? " (" + outcome.Pending.Count + " pending on combat gaps, listed below)" : ""));
        replayed.Add(family + " " + outcome.Passed + "/" + cases.Count + (outcome.Pending.Count > 0 ? " (" + outcome.Pending.Count + " pending)" : ""));
    }

    Core2RunChecks.Run(content, data, Check, Equal, Throws);
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: " + error);
}

foreach (var line in replayed) Console.WriteLine("ORACLE: " + line + " cases pass");
foreach (var line in pending) Console.WriteLine("PENDING: " + line);
if (failures > 0) { Console.Error.WriteLine("Core2Run: " + failures + " failed, " + checks + " passed"); return 1; }
Console.WriteLine("Core2Run: " + replayed.Count + " oracle families replayed.");
Console.WriteLine("Core2Run: " + checks + " checks passed");
return 0;
