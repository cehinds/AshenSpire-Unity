// Program.cs — executes real native commands, then checks feedback against original receipts.
using AshenSpire.Domain;
using AshenSpire.Presentation;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var root = Directory.GetCurrentDirectory() + "/";
var oracle = JObject.Parse(File.ReadAllText(root + "UnityTests/Parity/combat-reference.json"));
var catalog = new OriginalContentCatalog(oracle["content"].ToString());
var mechanics = (JObject)oracle["mechanics"];
JObject Resolve(JObject card) => (JObject)oracle["definitions"][(string)card["cardId"] + ":" + ((bool?)card["upgraded"] == true ? "true" : "false")].DeepClone();
var checks = 0; var commands = 0; var coverage = new Dictionary<string, int>();
void Check(bool yes, string label) { if (!yes) throw new Exception(label); checks++; }
void Cover(string key) { coverage[key] = coverage.GetValueOrDefault(key) + 1; }
void Verify(JArray events, string label)
{
    var before = events.DeepClone(); var p = NativeFeedbackProjection.FromEvents(events);
    Check(JToken.DeepEquals(before, events), label + " input mutation");
    Check(Newtonsoft.Json.JsonConvert.SerializeObject(p) == Newtonsoft.Json.JsonConvert.SerializeObject(NativeFeedbackProjection.FromEvents(events)), label + " deterministic");
    if (p == null) return;
    Check(new[] { "attack", "guard", "hit", "heal" }.Contains(p.CueId), label + " existing cue");
    var hp = events.Where(e => (string)e["type"] == "hpLost");
    Check(p.Outcome.Hurt == hp.Where(e => (string)e["targetId"] == "player").Sum(e => (int)e["amount"]), label + " actual lost health");
    Check(p.Outcome.Damage == hp.Where(e => (string)e["targetId"] != "player").Sum(e => (int)e["amount"]), label + " actual foe health loss");
    bool Has(string type) => events.Any(e => (string)e["type"] == type);
    if (!p.EnemyTurn && !events.Any(e => (string)e["type"] == "damageDealt" && (string)e["sourceId"] == "player"))
    {
        var recovery = p.Outcome.Healing + events.Where(e => (string)e["type"] == "manaRestored" || (string)e["type"] == "staminaRecovered").Sum(e => (int)e["amount"]);
        if (recovery > 0) { Check(p.CueId == "heal", label + " recovery uses healing cue"); Cover("recoveryCue"); }
        else if (p.Outcome.Block > 0 && p.Outcome.Hurt == 0) { Check(p.CueId == "guard", label + " guard uses guard cue"); Cover("guardCue"); }
    }
    if (Has("enemyTurnStart")) { Check(p.EnemyTurn && p.CueId != "attack", label + " enemy turn"); Cover("enemyTurn"); }
    if (Has("equipmentChanged")) { Check(p.Outcome.Action.Contains("Equipment changed") && p.CueId != "attack", label + " equipment"); Cover("equipment"); }
    if (Has("statusApplied")) { Check(p.Outcome.Action.Contains("SELF ") || p.Outcome.Action.Contains("FOE "), label + " status"); Cover("status"); }
    if (Has("cardPlayed") && !p.EnemyTurn && events.Any(e => (string)e["type"] == "damageDealt" && (string)e["sourceId"] == "player"))
    { Check(p.CueId == "attack", label + " player attack"); Cover("attack"); if (p.Outcome.Hurt > 0) { Check(!p.EnemyTurn, label + " self loss is not enemy turn"); Cover("attackWithSelfLoss"); } }
    if (Has("blockGained")) { Check(p.Outcome.Block == events.Where(e => (string)e["type"] == "blockGained" && (string)e["targetId"] == "player").Sum(e => (int)e["amount"]), label + " block receipt"); Cover("guard"); }
    if (Has("healed")) { Check(p.Outcome.Healing == events.Where(e => (string)e["type"] == "healed" && (string)e["targetId"] == "player").Sum(e => (int)e["amount"]), label + " clamped healing"); Cover("heal"); }
    foreach (var (type, text) in new[] { ("manaRestored", "MANA"), ("staminaRecovered", "STAMINA") })
    {
        var amount = events.Where(e => (string)e["type"] == type && (type == "staminaRecovered" || (string)e["targetId"] == "player")).Sum(e => (int)e["amount"]);
        if (amount > 0) { Check(p.Outcome.Action.Contains(text + " +" + amount), label + " resource actual gain"); Cover(type); }
    }
    if (Has("flaskUsed")) Cover("flask");
}
foreach (var fixture in oracle["fixtures"])
{
    var session = new CombatSession(catalog, mechanics, new RandomStreams((uint)fixture["seed"]), (JObject)fixture["player"], fixture["deck"].OfType<JObject>(), fixture["enemyIds"].Values<string>(), Resolve, (double)fixture["hpMult"]);
    foreach (var step in fixture["steps"])
    {
        var intent = step["intent"]; JArray events;
        try { events = (string)intent["type"] switch { "playCard" => session.PlayCard((string)intent["cardInstanceId"], (string)intent["targetId"]), "endTurn" => session.EndTurn(), "useFlask" => intent["chargeKind"] != null ? session.DrinkCharge((string)intent["chargeKind"], (string)intent["targetId"]) : session.DrinkFlask((int)intent["slot"], (string)intent["targetId"]), _ => throw new Exception("Unknown command") }; }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { Check(step["error"].Type != JTokenType.Null, "unexpected rejection"); continue; }
        Check(JToken.DeepEquals(events, step["events"]), "original receipt equality");
        Verify(events, "command " + ++commands);
    }
}
var swapOracle = JObject.Parse(File.ReadAllText(root + "UnityTests/Parity/swap-reference.json"));
foreach (var fixture in swapOracle["fixtures"])
{
    var data = catalog.Data(); data["balance"]["equipment"]["swapCostKind"] = fixture["kind"].DeepClone();
    var swapCatalog = new OriginalContentCatalog(data.ToString()); var service = new OriginalCombatEquipment(swapCatalog, mechanics);
    var run = (JObject)fixture["run"].DeepClone(); var battle = (JObject)fixture["before"].DeepClone();
    foreach (var step in fixture["steps"])
    {
        JObject changed; try { changed = service.Apply(run, battle, "rightHand", (int)step["setIndex"]); } catch (ArgumentException) { continue; }
        run = (JObject)changed["run"]; battle = (JObject)changed["combat"];
        var weapons = new WeaponCardProjection(swapCatalog);
        var session = CombatSession.Restore(swapCatalog, mechanics, battle, c => (JObject)weapons.Resolve(c, (JObject)run["loadout"], "reaver", (JObject)run["attributes"])["card"]);
        var events = session.FinishEquipmentSwap((JArray)changed["events"], false);
        Check(JToken.DeepEquals(events, step["events"]), "original swap receipts"); Verify(events, "swap"); commands++;
    }
}
var basis = oracle["fixtures"][0];
var liveMechanics = JObject.Parse(File.ReadAllText(root + "GameContent/Unity/Original/mechanics.json"));
var breath = new CombatSession(catalog, liveMechanics, new RandomStreams((uint)basis["seed"]), (JObject)basis["player"], basis["deck"].OfType<JObject>(), basis["enemyIds"].Values<string>(), Resolve, (double)basis["hpMult"]);
var breathEvents = breath.CatchBreath(); Verify(breathEvents, "authored Catch Breath"); commands++;
Check(NativeFeedbackProjection.FromEvents(breathEvents).CueId == "heal", "Catch Breath recovery cue");
Check(NativeFeedbackProjection.FromEvents(new JArray()) == null, "empty silent");
Check(NativeFeedbackProjection.FromEvents(JArray.Parse("[{type:'cardDrawn',cardId:'strike'}]")) == null, "housekeeping silent");
foreach (var key in new[] { "attack", "attackWithSelfLoss", "enemyTurn", "guard", "heal", "manaRestored", "staminaRecovered", "status", "flask", "equipment", "recoveryCue", "guardCue" }) Check(coverage.GetValueOrDefault(key) > 0, "missing executed coverage: " + key);
Directory.CreateDirectory("TestResults/NativeFeedback"); File.WriteAllText("TestResults/NativeFeedback/checks.json", new JObject { ["checks"] = checks, ["executedCommands"] = commands, ["coverage"] = JObject.FromObject(coverage), ["status"] = "passed" }.ToString());
Console.WriteLine($"{checks} checks / {commands} actual native commands passed.\n" + JObject.FromObject(coverage));
