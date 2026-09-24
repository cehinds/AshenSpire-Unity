// HandRules — the web game's solo hand rules (SPEC §4.1) as ported to the native engine.
// Mirrors web tests/hand-rules.test.mjs case by case, then checks the native wiring:
// content validation, the per-fight snapshot a new run takes, and the legacy path that
// saved runs and fights without a snapshot (and co-op) keep.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var root = Directory.GetCurrentDirectory();
var directory = Path.Combine(root, "GameContent/Unity/Original");
var contentText = File.ReadAllText(Path.Combine(directory, "content.json"));
var catalog = new OriginalContentCatalog(contentText);
var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(directory, "mechanics.json")));
var shipped = catalog.SoloHandRules ?? throw new Exception("content.json has no handRules");
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); checks++; }
void Equal(JToken actual, JToken expected, string label) { if (!JToken.DeepEquals(actual, expected)) throw new Exception("FAIL: " + label + "\nActual: " + actual + "\nExpected: " + expected); checks++; }
void Throws(Action action, string label) { try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; } throw new Exception("FAIL: accepted " + label); }

// The shipped defaults are the owner's 2026-09-24 values (web src/content/handRules.js).
Equal(shipped, JObject.Parse(@"{retain:true,promptDiscard:false,discardLimit:10,replaceDiscards:false,overflow:'discard',reshuffle:true,drawMode:'fixed',
 starting:{base:4,statEnabled:true,stat:'intelligence',baseline:1,pointsPerCard:2,minimum:3,maximum:15},
 classStarting:{reaver:{base:3,statEnabled:true,stat:'strength',baseline:1,pointsPerCard:2,minimum:3,maximum:6},
  rogue:{base:4,statEnabled:true,stat:'dexterity',baseline:1,pointsPerCard:2,minimum:3,maximum:6},
  herald:{base:4,statEnabled:true,stat:'wisdom',baseline:1,pointsPerCard:2,minimum:3,maximum:6},
  starseer:{base:5,statEnabled:true,stat:'intelligence',baseline:1,pointsPerCard:2,minimum:3,maximum:6}},
 turn:{base:2,statEnabled:true,stat:'intelligence',baseline:4,pointsPerCard:5,minimum:2,maximum:10},
 capacity:{base:7,statEnabled:true,stat:'intelligence',baseline:1,pointsPerCard:5,minimum:1,maximum:30}}"), "shipped hand rules");
Check(File.ReadAllText(Path.Combine(root, "Unity/Assets/AshenSpire/Resources/Original/content.json")) == contentText, "Resources content.json mirrors GameContent byte for byte");

// The rules the web mechanic tests pin (fill to a flat ten, keep overflow, open on 3 + INT/10).
var legacyRules = new JObject { ["drawMode"] = "fill", ["overflow"] = "keep",
 ["starting.base"] = 3, ["starting.statEnabled"] = true, ["starting.baseline"] = 10, ["starting.pointsPerCard"] = 10, ["starting.minimum"] = 0, ["starting.maximum"] = 10,
 ["turn.base"] = 2, ["turn.statEnabled"] = false, ["turn.baseline"] = 10, ["turn.pointsPerCard"] = 10, ["turn.minimum"] = 0, ["turn.maximum"] = 10,
 ["capacity.base"] = 10, ["capacity.statEnabled"] = false, ["capacity.baseline"] = 10, ["capacity.pointsPerCard"] = 10, ["capacity.minimum"] = 1, ["capacity.maximum"] = 30 };
// The mechanic cases below exercise the shared "starting" rule, so they drop the per-class
// opening hands (section 12 covers those).
JObject RulesOf(JObject overrides = null, JObject basis = null)
{
 var rules = (JObject)shipped.DeepClone(); rules.Remove("classStarting");
 foreach (var source in new[] { basis ?? legacyRules, overrides ?? new JObject() })
  foreach (var p in source.Properties()) { var parts = p.Name.Split('.'); if (parts.Length == 1) rules[parts[0]] = p.Value.DeepClone(); else rules[parts[0]]![parts[1]] = p.Value.DeepClone(); }
 return rules;
}
JObject ResolveCard(JObject instance) => catalog.Record("cards", (string)instance["cardId"]);
JObject Player(JObject attributes) => new JObject { ["classId"] = "reaver", ["maxHp"] = 10000, ["hp"] = 10000, ["maxMana"] = 0, ["energyMax"] = 3, ["drawPerTurn"] = 5, ["attributes"] = attributes, ["relicIds"] = new JArray() };
IEnumerable<JObject> Deck() => Enumerable.Range(0, 25).Select(i => new JObject { ["instanceId"] = "c" + i, ["cardId"] = "strike", ["upgraded"] = false });
CombatSession Fight(JObject overrides = null, int intelligence = 10, JObject basis = null, string stat = "intelligence") =>
 new CombatSession(catalog, mechanics, new RandomStreams(2309), Player(new JObject { [stat] = intelligence }), Deck(), new[] { "wanderingSoldier" }, ResolveCard, 1, null, RulesOf(overrides, basis));
CombatSession Edit(CombatSession session, Action<JObject> change) { var s = session.Snapshot(); change(s); return CombatSession.Restore(catalog, mechanics, s, ResolveCard); }
JArray Pile(CombatSession session, string pile) => (JArray)session.Snapshot()["piles"]![pile]!;
List<string> Ids(JArray cards) => cards.Select(c => (string)c["instanceId"]!).ToList();

// 1. the default opening follows the shipped starting rule; unplayed cards survive and fill mode fills capacity
{
 var defaults = Fight(null, 10, new JObject());
 Equal(defaults.Hand.Count, HandRules.ScaledCards((JObject)shipped["starting"]!, new JObject { ["intelligence"] = 10 }), "shipped opening = starting rule");
 Equal(defaults.Hand.Count, 8, "4 + floor((10 − 1) ÷ 2), inside 3–15");
 var c = Fight(); var ids = Ids(c.Hand);
 Equal(c.Hand.Count, 3, "legacy-rules opening of three");
 c.EndTurn(); Equal(c.Hand.Count, 10, "fill mode fills capacity");
 Check(ids.All(id => Ids(c.Hand).Contains(id)), "unplayed cards are retained");
 var draw = Pile(c, "draw"); c.EndTurn(); Equal(Pile(c, "draw"), draw, "a full hand never touches the draw pile");
}
// 2. stat selection, baseline, whole intervals, bounds and scaling off
{
 var rules = RulesOf(new JObject { ["starting.stat"] = "strength", ["starting.pointsPerCard"] = 3 });
 var starting = (JObject)rules["starting"]!;
 Equal(HandRules.ScaledCards(starting, new JObject { ["strength"] = 18 }), 5, "3 + floor((18 − 10) ÷ 3)");
 Equal(HandRules.ScaledCards(starting, new JObject { ["strength"] = 1 }), 3, "below the baseline never removes cards");
 starting["statEnabled"] = false;
 Equal(HandRules.ScaledCards(starting, new JObject { ["strength"] = 99 }), 3, "scaling off");
 Equal(Fight(new JObject { ["capacity.base"] = 2 }).Hand.Count, 2, "opening bounded by capacity");
 Equal(Fight(null, 30).Hand.Count, 5, "INT 30 opens on 3 + 2");
 Equal(HandRules.ScaledCards(starting, new JObject()), 3, "a missing attribute reads as 0");
}
// 3. fixed draws are the shipped default, two below the turn baseline, and can be changed or scaled
{
 Equal((string)shipped["drawMode"]!, "fixed", "fixed is the shipped draw mode");
 var d0 = Fight(null, 4, new JObject());
 Equal(d0.Hand.Count, 5, "INT 4: opening 4 + floor(3 ÷ 2)");
 d0.EndTurn(); Equal(d0.Hand.Count, 7, "then a fixed two, capacity 7");
 var c = Fight(new JObject { ["drawMode"] = "fixed" }); c.EndTurn(); Equal(c.Hand.Count, 5, "fixed two onto three retained");
 var d = Fight(new JObject { ["drawMode"] = "fixed", ["turn.base"] = 1, ["turn.statEnabled"] = true, ["turn.pointsPerCard"] = 5 }, 20);
 d.EndTurn(); Equal(d.Hand.Count, 7, "scaled fixed draw 1 + floor((20 − 10) ÷ 5) onto four");
 // Lean all-1s sheet under the shipped rules: open on 4, capacity 7, then 2 a turn up to 7.
 var lean = Fight(null, 1, new JObject());
 Equal(lean.Hand.Count, 4, "lean INT 1 opens on four"); Equal(lean.HandCapacity, 7, "lean INT 1 capacity seven");
 lean.EndTurn(); Equal(lean.Hand.Count, 6, "retained four + fixed two"); lean.EndTurn(); Equal(lean.Hand.Count, 7, "fixed draw stops at capacity");
}
// 4. retention off discards ordinary cards; optional choice validates atomically
{
 var c = Fight(new JObject { ["retain"] = false, ["drawMode"] = "fixed" });
 c.EndTurn(); Equal(c.Hand.Count, 2, "retention off: only the fixed draw"); Equal(Pile(c, "discard").Count, 3, "three discarded at turn end");
 var d = Fight(new JObject { ["promptDiscard"] = true, ["discardLimit"] = 1, ["drawMode"] = "fixed", ["replaceDiscards"] = true });
 var ids = Ids(d.Hand); var before = d.Snapshot();
 Equal(d.DiscardChoicePlan(), JObject.FromObject(new { cardIds = ids, minimum = 0, maximum = 1, prompt = true }), "optional plan");
 foreach (var choice in new[] { new[] { ids[0], ids[1] }, new[] { ids[0], ids[0] }, new[] { "missing" } })
 { Throws(() => d.EndTurn(choice), "invalid discard choice " + string.Join(",", choice)); Equal(d.Snapshot(), before, "a refused choice changes nothing"); }
 d.EndTurn(new[] { ids[0] });
 Equal(d.Hand.Count, 5, "two retained + fixed two + one replacement");
 Check(Ids(Pile(d, "discard")).Contains(ids[0]), "the chosen card was discarded");
 Check(d.Snapshot()["events"]!.Any(e => (string)e["type"] == "cardDiscarded" && (string)e["reason"] == "choice" && (string)e["cardInstanceId"] == ids[0]), "choice discard event");
}
// 5. overflow requires the selected excess; full hands never churn the draw pile
{
 var c = Edit(Fight(new JObject { ["overflow"] = "discard" }), s => s["handRules"]!["capacity"]!["base"] = 1);
 var plan = c.DiscardChoicePlan(); Equal(plan["minimum"], 2, "two over capacity"); Check((bool)plan["prompt"]!, "forced overflow prompts");
 Throws(() => c.EndTurn(), "ending with a forced discard unchosen");
 var chosen = ((JArray)plan["cardIds"]!).Take(2).Select(x => (string)x!).ToArray();
 var draw = Pile(c, "draw");
 c.EndTurn(chosen); Equal(c.Hand.Count, 1, "hand trimmed to capacity"); Equal(Pile(c, "draw"), draw, "a full hand drew nothing");
}
// 6. empty draw pile respects the reshuffle toggle
{
 void Empty(JObject s) { var piles = (JObject)s["piles"]!; foreach (var card in ((JArray)piles["draw"]!).ToList()) ((JArray)piles["discard"]!).Add(card); piles["draw"] = new JArray(); }
 var off = Edit(Fight(new JObject { ["reshuffle"] = false }), Empty);
 off.EndTurn(); Equal(off.Hand.Count, 3, "no reshuffle: nothing to draw");
 var on = Edit(Fight(), Empty);
 on.EndTurn(); Equal(on.Hand.Count, 10, "reshuffle refills the draw pile");
}
// 7. combat snapshot keeps rules and resumes deterministically
{
 var c = Fight(new JObject { ["drawMode"] = "fixed", ["promptDiscard"] = true });
 var restored = CombatSession.Restore(catalog, mechanics, c.Snapshot(), ResolveCard);
 Equal(restored.HandRulesSnapshot, c.HandRulesSnapshot, "rules survive the save");
 c.EndTurn(); restored.EndTurn();
 Equal(restored.Snapshot()["piles"], c.Snapshot()["piles"], "resumed piles"); Equal(restored.Turn, c.Turn, "resumed turn");
 var d = Fight(new JObject { ["promptDiscard"] = true, ["drawMode"] = "fixed", ["replaceDiscards"] = true });
 d.EndTurn(new[] { Ids(d.Hand)[0] });
 // The replacement owed is spent at turn start, so a mid-turn save owes nothing; a hand-set debt survives.
 var owed = Edit(d, s => s["pendingDiscardDraw"] = 2); Equal(owed.Snapshot()["pendingDiscardDraw"], 2, "pending replacements survive saves");
}
// 8. ethereal exhausts despite auto-retention and cannot be chosen to avoid it
{
 var c = Edit(Fight(new JObject { ["promptDiscard"] = true }), s => ((JArray)s["piles"]!["hand"]!).Add(new JObject { ["instanceId"] = "ethereal", ["cardId"] = "lastStand", ["upgraded"] = false }));
 Check(!((JArray)c.DiscardChoicePlan()["cardIds"]!).Any(x => (string)x! == "ethereal"), "ethereal is not eligible");
 Throws(() => c.EndTurn(new[] { "ethereal" }), "choosing an ethereal card");
 c.EndTurn(); Check(Ids(Pile(c, "exhaust")).Contains("ethereal"), "ethereal exhausted");
}
// 9. saved rules reject malformed formulas, and old snapshots retain legacy behaviour
{
 var saved = Fight().Snapshot();
 saved["handRules"]!["starting"]!["pointsPerCard"] = 0;
 Throws(() => CombatSession.Restore(catalog, mechanics, saved, ResolveCard), "restoring pointsPerCard 0");
 saved.Remove("handRules"); saved.Remove("pendingDiscardDraw");
 var legacy = CombatSession.Restore(catalog, mechanics, saved, ResolveCard);
 Check(legacy.HandRulesSnapshot == null, "legacy fight has no rules");
 legacy.EndTurn(); Equal(legacy.Hand.Count, 5, "legacy draws derived Draw"); Equal(Pile(legacy, "discard").Count, 3, "legacy discards the hand");
 Equal(legacy.DiscardChoicePlan(), JObject.Parse("{cardIds:[],minimum:0,maximum:0,prompt:false}"), "legacy plan is empty");
 Throws(() => legacy.EndTurn(new[] { "c0" }), "a discard choice in a legacy fight");
 // A fight created with no rules (co-op, LAN, headless) is the legacy engine exactly.
 var plain = new CombatSession(catalog, mechanics, new RandomStreams(2309), Player(new JObject { ["intelligence"] = 10 }), Deck(), new[] { "wanderingSoldier" }, ResolveCard);
 Equal(plain.Hand.Count, 5, "no rules: open on derived Draw"); Equal(plain.HandCapacity, (int)catalog.Data()["balance"]!["handMax"]!, "no rules: balance.handMax");
 Check(plain.Snapshot()["handRules"] == null && plain.Snapshot()["pendingDiscardDraw"] == null, "legacy snapshot shape unchanged");
 foreach (var (path, value) in new (string, JToken)[] { ("drawMode", "draw"), ("retain", 1), ("discardLimit", 100), ("capacity.base", 0), ("turn.stat", "luck"), ("turn.minimum", 11) })
 {
  var bad = RulesOf(new JObject { [path] = value });
  Check(HandRules.Problems(bad).Count > 0, "problem reported for " + path);
  Throws(() => new CombatSession(catalog, mechanics, new RandomStreams(1), Player(new JObject()), Deck(), new[] { "wanderingSoldier" }, ResolveCard, 1, null, bad), "combat with bad " + path);
 }
 Check(HandRules.Problems(shipped).Count == 0, "shipped rules are valid");
 var badContent = JObject.Parse(contentText); badContent["handRules"]!["capacity"]!["maximum"] = 0;
 Throws(() => new OriginalContentCatalog(badContent.ToString()), "content with invalid handRules");
}
// 10. the hand receipt is the arithmetic ScaledCards does
{
 var rule = JObject.Parse("{base:3,statEnabled:true,stat:'intelligence',baseline:4,pointsPerCard:3,minimum:1,maximum:5}");
 foreach (var intelligence in new[] { 0, 4, 7, 10, 40 })
 {
  var receipt = HandRules.ScaledCardsReceipt(rule, new JObject { ["intelligence"] = intelligence });
  Equal(receipt["value"], HandRules.ScaledCards(rule, new JObject { ["intelligence"] = intelligence }), "receipt value INT " + intelligence);
  Equal(receipt["bonus"], Math.Max(0, intelligence - 4) / 3, "receipt bonus INT " + intelligence);
 }
}
// 11. native wiring: a new solo run's fights snapshot the frozen content's rules; runs and fights without them stay legacy.
{
 var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(directory, "progression.json"))));
 var supplement = JObject.Parse(File.ReadAllText(Path.Combine(directory, "event-choices.json")));
 var creator = new CreationModel(catalog, "starseer", "leanStandard", progression);
 Check(creator.CanBegin, "the Standard Starseer preset begins without spending");
 var kit = (string)catalog.Table("equipment.startingKits").First(x => (string)x["classId"] == "starseer" && (bool?)x["baseline"] == true)["id"]!;
 var player = new OriginalCharacterBuilder(catalog, progression, mechanics).Build(creator, kit);
 var game = OriginalGameSession.Start(catalog, supplement, mechanics, player, 7);
 var atMap = game.Snapshot();
 OriginalGameSession IntoFight(JObject save)
 {
  foreach (var id in OriginalGameSession.Restore(save).LegalNodeIds) { var g = OriginalGameSession.Restore(save); g.Enter(id); if (g.Phase == OriginalRunPhase.Combat) return g; }
  throw new Exception("no opening fight");
 }
 var fight = IntoFight(atMap);
 var combat = (JObject)fight.Snapshot()["run"]!["room"]!["combatSnapshot"]!;
 Equal(combat["handRules"], HandRules.ForClass(shipped, "starseer"), "a new fight snapshots the run's frozen hand rules, resolved for its class");
 Check(combat["handRules"]!["classStarting"] == null, "the snapshot carries only the class's opening rule");
 Equal(combat["pendingDiscardDraw"], 0, "no replacements owed at the start");
 var intelligence = (int)player["attributes"]!["intelligence"]!;
 Equal(fight.Hand.Count, 6, "the Standard Starseer (INT " + intelligence + ") opens on six");
 Equal(fight.DiscardPlan, JObject.FromObject(new { cardIds = Ids(fight.Hand), minimum = 0, maximum = 0, prompt = false }), "default rules never prompt under capacity");
 var retained = Ids(fight.Hand); fight.EndTurn();
 if (fight.Phase == OriginalRunPhase.Combat) Check(retained.All(id => Ids(fight.Hand).Contains(id)), "unplayed cards are kept through the session");
 // A run whose frozen content predates hand rules fights the legacy way.
 var legacySave = (JObject)atMap.DeepClone(); ((JObject)legacySave["content"]!).Remove("handRules");
 var legacyFight = IntoFight(legacySave);
 var legacyCombat = (JObject)legacyFight.Snapshot()["run"]!["room"]!["combatSnapshot"]!;
 Check(legacyCombat["handRules"] == null, "legacy run: no hand-rules snapshot");
 Equal(legacyFight.Hand.Count, Math.Min((int)player["draw"]!, (int)catalog.Data()["balance"]!["handMax"]!), "legacy run: opening hand is the derived Draw");
 // A saved fight without a snapshot resumes legacy even when its run's content has rules.
 var savedFight = (JObject)fight.Snapshot().DeepClone(); var room = (JObject)savedFight["run"]!["room"]!["combatSnapshot"]!; room.Remove("handRules"); room.Remove("pendingDiscardDraw");
 var resumed = OriginalGameSession.Restore(savedFight);
 Check(resumed.Snapshot()["run"]!["room"]!["combatSnapshot"]!["handRules"] == null, "saved legacy fight keeps no rules after restore");
 resumed.EndTurn();
 if (resumed.Phase == OriginalRunPhase.Combat) Equal(resumed.Hand.Count, Math.Min((int)player["draw"]!, (int)catalog.Data()["balance"]!["handMax"]!), "saved legacy fight: hand discarded, derived Draw drawn");
}
// 12. per-class opening hands (owner, 2026-09-24: "start with 4-6 cards depending on the base (3-5)";
// "Class base 3–5, +1 from stats"). Base: Reaver 3, Rogue 4, Herald 4, Starseer 5; +1 once the class's
// primary stat (STR, DEX, WIS, INT) reaches 3; never more than six.
{
 var primary = new Dictionary<string, string> { ["reaver"] = "strength", ["rogue"] = "dexterity", ["herald"] = "wisdom", ["starseer"] = "intelligence" };
 var standard = new Dictionary<string, int> { ["reaver"] = 4, ["rogue"] = 5, ["herald"] = 5, ["starseer"] = 6 };
 var allOnes = new Dictionary<string, int> { ["reaver"] = 3, ["rogue"] = 4, ["herald"] = 4, ["starseer"] = 5 };
 var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(directory, "progression.json"))));
 JObject Hero(string cls, JObject attributes) => new JObject { ["classId"] = cls, ["maxHp"] = 10000, ["hp"] = 10000, ["maxMana"] = 0, ["energyMax"] = 3, ["drawPerTurn"] = 2, ["attributes"] = attributes, ["relicIds"] = new JArray() };
 CombatSession Open(string cls, JObject attributes) => new CombatSession(catalog, mechanics, new RandomStreams(2309), Hero(cls, attributes), Deck(), new[] { "wanderingSoldier" }, ResolveCard, 1, null, shipped);
 foreach (var cls in primary.Keys)
 {
  var preset = new CreationModel(catalog, cls, "leanStandard", progression).Attributes();
  var ones = new CreationModel(catalog, cls, "lean", progression).Attributes();
  var rule = (JObject)HandRules.ForClass(shipped, cls)["starting"]!;
  Equal(rule, shipped["classStarting"]![cls], cls + " resolves its own opening rule");
  Equal(HandRules.ScaledCards(rule, preset), standard[cls], cls + " Standard preset opening");
  Equal(HandRules.ScaledCards(rule, ones), allOnes[cls], cls + " all-1s opening is the class base");
  var fight = Open(cls, preset);
  Equal(fight.Hand.Count, standard[cls], cls + " Standard fight opens on " + standard[cls]);
  Equal(fight.HandRulesSnapshot!["starting"], rule, cls + " fight snapshots its class rule");
  Check(fight.HandRulesSnapshot!["classStarting"] == null, cls + " snapshot drops classStarting");
  Equal(Open(cls, ones).Hand.Count, allOnes[cls], cls + " all-1s fight opens on " + allOnes[cls]);
  var two = (JObject)ones.DeepClone(); two[primary[cls]] = 2;
  Equal(HandRules.ScaledCards(rule, two), allOnes[cls], cls + " primary 2 adds nothing");
  var four = (JObject)ones.DeepClone(); four[primary[cls]] = 4;
  Equal(HandRules.ScaledCards(rule, four), allOnes[cls] + 1, cls + " primary 4 still +1");
  var huge = (JObject)ones.DeepClone(); huge[primary[cls]] = 99;
  Equal(HandRules.ScaledCards(rule, huge), 6, cls + " capped at six");
  // Only the primary stat counts: INT 3 on a Reaver opens on the Reaver base.
  var other = (JObject)ones.DeepClone(); other[cls == "starseer" ? "strength" : "intelligence"] = 3;
  Equal(HandRules.ScaledCards(rule, other), allOnes[cls], cls + " off-primary stat adds nothing");
 }
 // A class the override does not name keeps the shared rule; rules without classStarting are unchanged.
 Equal(HandRules.ForClass(shipped, "wanderer")["starting"], shipped["starting"], "unnamed class keeps the shared starting rule");
 var noClass = (JObject)shipped.DeepClone(); noClass.Remove("classStarting");
 Equal(HandRules.ForClass(noClass, "reaver"), noClass, "rules without classStarting pass through unchanged");
 // Validation: a malformed override is refused like any other hand rule; an unknown class is refused by the catalog.
 foreach (var (key, value) in new (string, JToken)[] { ("pointsPerCard", 0), ("stat", "luck"), ("minimum", 7), ("base", -1) })
 {
  var bad = (JObject)shipped.DeepClone(); bad["classStarting"]!["reaver"]![key] = value;
  Check(HandRules.Problems(bad).Count > 0, "classStarting problem reported for " + key);
  Throws(() => new CombatSession(catalog, mechanics, new RandomStreams(1), Hero("rogue", new JObject()), Deck(), new[] { "wanderingSoldier" }, ResolveCard, 1, null, bad), "combat with bad classStarting." + key);
 }
 var notObject = (JObject)shipped.DeepClone(); notObject["classStarting"] = 3; Check(HandRules.Problems(notObject).Count > 0, "classStarting must be an object");
 var unknown = JObject.Parse(contentText); unknown["handRules"]!["classStarting"]!["wanderer"] = shipped["classStarting"]!["reaver"]!.DeepClone();
 Throws(() => new OriginalContentCatalog(unknown.ToString()), "classStarting for an unknown class");
 // A saved fight that predates classStarting keeps the shared rule it snapshotted.
 var old = (JObject)shipped.DeepClone(); old.Remove("classStarting");
 var legacy = CombatSession.Restore(catalog, mechanics, Edit(Open("reaver", new JObject { ["intelligence"] = 1, ["strength"] = 3 }), s => s["handRules"] = old.DeepClone()).Snapshot(), ResolveCard);
 Equal(legacy.HandRulesSnapshot!["starting"], shipped["starting"], "restored pre-override fight keeps the shared starting rule");
}
Console.WriteLine($"Hand rules checks passed: {checks}");
