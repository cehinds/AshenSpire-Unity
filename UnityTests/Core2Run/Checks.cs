// Focused checks of the Core 2 run layer the JS oracle families do not reach: class unlocks, the save format and its
// refusals, rest services once per visit, screen transitions, event gates and the class lock, flasks, the map's shape.
using AshenSpire.Domain.Core2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static class Core2RunChecks
{
    public static void Run(Core2Content content, Core2RunData data, Action<bool, string> check, Action<object, object, string> equal, Func<Action, bool> throws)
    {
        // ---- class unlocks (Reaver first, then one class per finished run) ---------------------------------------------
        var ledger = new Core2UnlockLedger();
        check(Core2RunProgression.ClassAvailable(content, ledger, "reaver"), "unlocks: Reaver is open on a fresh profile");
        check(!Core2RunProgression.ClassAvailable(content, ledger, "starseer"), "unlocks: Starseer is locked on a fresh profile");
        check(throws(() => Core2RunSession.Start(data, "starseer", 1, ledger)), "unlocks: a locked class cannot start a run");
        equal(string.Join(",", Core2RunProgression.RecordFinish(content, ledger, "run-a", false)), "starseer", "unlocks: the first finished run (even a defeat) unlocks Starseer");
        equal(Core2RunProgression.RecordFinish(content, ledger, "run-a", true).Count, 0, "unlocks: the same finish id is recorded once");
        equal(string.Join(",", Core2RunProgression.RecordFinish(content, ledger, "run-b", true)), "rogue", "unlocks: the second finished run unlocks Rogue");
        equal(string.Join(",", Core2RunProgression.RecordFinish(content, ledger, "run-c", true)), "herald", "unlocks: the third finished run unlocks Herald");
        equal(Core2RunProgression.RecordFinish(content, ledger, "run-d", true).Count, 0, "unlocks: nothing is left to unlock after Herald");
        check(new[] { "reaver", "starseer", "rogue", "herald" }.All(c => Core2RunProgression.ClassAvailable(content, ledger, c)), "unlocks: every class is open after three finished runs");
        check(throws(() => Core2RunProgression.RecordFinish(content, ledger, "", true)), "unlocks: a finish needs a durable id");
        var roundTrip = Core2UnlockLedger.FromJson(ledger.ToJson());
        equal(string.Join(",", roundTrip.CompletedRunIds), "run-a,run-b,run-c,run-d", "unlocks: the ledger round-trips through JSON");

        // ---- the map's shape rules ---------------------------------------------------------------------------------------
        var shapes = true;
        for (uint seed = 1; seed <= 40; seed++)
        {
            var map = Core2RunSession.Start(data, "reaver", seed).Run.MapGraph;
            var nodes = map.NodeList;
            var restFloors = nodes.Where(n => n.Type == "shrine").Select(n => n.Floor).Distinct().OrderBy(f => f).ToList();
            var bosses = nodes.Count(n => n.Type == "boss");
            shapes &= string.Join(",", restFloors) == "3,6,9" && bosses == map.Branches.Count && map.Branches.Count >= 1 && map.Branches.Count <= 5
                && map.Branches.All(b => b.RestIds.Count > 0) && nodes.Where(n => n.Type != "boss").All(n => n.Next.Count > 0)
                && map.StartIds.All(id => map.Node(id).Floor == 1);
        }
        check(shapes, "maps: 40 seeds give rests on floors 3/6/9, one boss per branch, 1-5 branches, no dead ends, starts on floor 1");

        // ---- screens: map -> fight -> reward -> map, with flasks in the fight ------------------------------------------
        var session = Core2RunSession.Start(data, "reaver", 7);
        equal(session.Screen, Core2Screen.Map, "screens: a new run opens on the map");
        check(throws(() => session.Travel("nowhere")), "screens: an unreachable node is refused");
        var first = session.TravelOptions()[0];
        equal(session.Travel(first), Core2Screen.Fight, "screens: a floor 1 node opens a fight");
        check(throws(() => session.DrinkFlask("hp")), "screens: map-only actions are refused during a fight");
        var hpCharges = session.Run.FlaskCharges.HpCurrent;
        session.UseFlask("hp");
        equal(session.Run.FlaskCharges.HpCurrent, hpCharges - 1, "flasks: an HP charge drunk in a fight spends one charge");
        session.Run.Flasks.Clear();
        session.Run.Flasks.Add("wondrousDraught");
        var misc = session.Fight.Rng.Counters().TryGetValue("misc", out var m) ? m : 0;
        session.UseFlask(slot: 0);
        equal(session.Run.Flasks.Count, 0, "flasks: the Wondrous Draught leaves its potion slot");
        check((session.Fight.Rng.Counters().TryGetValue("misc", out var m2) ? m2 : 0) > misc, "flasks: the Wondrous Draught picks its two flasks on the misc stream");
        check(throws(() => session.UseFlask(slot: 0)), "flasks: an empty potion slot cannot be drunk");
        equal(session.SettleFightAsWon(), Core2Screen.Reward, "screens: a won fight opens its reward");
        check(session.Run.Cinders > 0 && session.LastSettlement.Cinders > 0, "rewards: cinders are granted when the fight is won");
        check(session.CurrentOffer.Options.Count > 0, "rewards: the reward offers options");
        session.TakeReward(0);
        check(throws(() => session.TakeReward(0)), "rewards: one option is taken per offer");
        equal(session.ContinueReward(), Core2Screen.Map, "screens: leaving the reward returns to the map");
        check(session.TravelOptions().Count > 0 && session.TravelOptions().All(id => session.Run.MapGraph.Node(first).Next.Contains(id)), "screens: travel continues from the node just cleared");

        // ---- save format: round trip and refusals --------------------------------------------------------------------------
        var text = Core2RunSave.Serialize(content, session.Run);
        var loaded = Core2RunSave.Deserialize(content, text);
        check(JToken.DeepEquals(Core2RunSave.RunToJson(loaded), Core2RunSave.RunToJson(session.Run)), "save: a run round-trips through the Core 2 save format");
        var resumed = Core2RunSession.Resume(data, loaded);
        equal(resumed.Screen, Core2Screen.Map, "save: a resumed run reopens where it was saved");
        check(JToken.DeepEquals(JObject.FromObject(resumed.Rng.Counters()), JObject.FromObject(session.Rng.Counters())), "save: a resumed run continues the RNG streams");
        string Edited(Action<JObject> edit) { var row = JObject.Parse(text); edit(row); return row.ToString(Formatting.None); }
        string Refusal(string save) { try { Core2RunSave.Deserialize(content, save); return "loaded"; } catch (Core2SaveRefused error) { return error.Reason; } }
        equal(Refusal(Edited(r => r["format"] = "ashen.run")), "format", "save: another format is refused");
        equal(Refusal(Edited(r => r["formatVersion"] = Core2RunSave.FormatVersion + 1)), "newer", "save: a newer format version is refused");
        equal(Refusal(Edited(r => r["profileHash"] = "other")), "rules", "save: a save made under other rules is refused");
        equal(Refusal("{not json"), "unreadable", "save: unreadable text is refused");
        equal(Refusal(Edited(r => r.Remove("run"))), "unreadable", "save: a save without a run is refused");

        // A save taken mid-fight resumes into the same fight, restarted from its entry checkpoint.
        var fighting = Core2RunSession.Start(data, "reaver", 9);
        fighting.Travel(fighting.TravelOptions()[0]);
        var hpFlasksAtEntry = fighting.Run.FlaskCharges.HpCurrent;
        fighting.UseFlask("hp");
        var resumedFight = Core2RunSession.Resume(data, Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, fighting.Run)));
        equal(resumedFight.Screen, Core2Screen.Fight, "save: a save taken in a fight resumes into the fight");
        equal(resumedFight.FightEncounter?["id"]?.ToString(), fighting.FightEncounter?["id"]?.ToString(), "save: the resumed fight is the same encounter");
        equal(resumedFight.Run.FlaskCharges.HpCurrent, hpFlasksAtEntry, "save: a restarted fight gives back the flasks drunk in it");
        // An event that chose a fight but was saved before Continue reopens the event, not the fight.
        var eventSave = Core2RunSave.Deserialize(content, Core2RunSave.Serialize(content, fighting.Run));
        eventSave.OpenEvent = data.Content.Events.First().Id; eventSave.OpenEventChosen = true;
        eventSave.CombatEntered = new Core2CombatEntry { NodeId = eventSave.MapNodeId, EncounterId = fighting.Run.CombatEntered.EncounterId };
        equal(Core2RunSession.Resume(data, eventSave).Screen, Core2Screen.Event, "save: an event's chosen fight waits for Continue after a reload");

        // ---- rest: arrive healed once, each service once per visit ---------------------------------------------------------
        var rest = Core2RunSession.Start(data, "reaver", 12).Run;
        var shrine = rest.MapGraph.NodeList.First(n => n.Type == "shrine");
        rest.MapNodeId = shrine.Id;
        rest.Hp = 1;
        var visit = Core2RunRest.Begin(data, rest);
        equal(rest.Hp, rest.MaxHp, "rest: arriving heals fully");
        rest.Hp = 1;
        Core2RunRest.Begin(data, rest);
        equal(rest.Hp, 1, "rest: reopening the same visit does not heal again");
        check(throws(() => Core2RunRest.Begin(data, rest, "1:elsewhere")), "rest: a visit id must name the current rest node");
        var candidate = Core2RunRest.SmithPlan(data, rest).First();
        Core2RunRest.Commit(data, rest, "smith", itemRef: candidate.ItemRef);
        equal(rest.Hp, rest.MaxHp, "rest: a service heals fully");
        check(rest.ItemUpgradeLevels.TryGetValue(candidate.ItemRef, out var tier) && tier == candidate.NextLevel, "rest: Smith raises the item one tier");
        check(throws(() => Core2RunRest.Commit(data, rest, "smith", itemRef: candidate.ItemRef)), "rest: Smith is once per visit");
        check(!Core2RunRest.OpenServices(data, rest).Contains("smith"), "rest: Smith leaves the open services once used");
        check(throws(() => Core2RunRest.Commit(data, rest, "levelUp", attributeId: "str")), "rest: Level Up needs an unspent point");
        rest.Level.UnspentPoints = 1;
        var attribute = Core2Run.AttributeIds.First();
        var before = rest.Attributes[attribute];
        Core2RunRest.Commit(data, rest, "levelUp", attributeId: attribute);
        equal(rest.Attributes[attribute], before + 1, "rest: Level Up spends the point on the attribute");
        equal(rest.Level.UnspentPoints, 0, "rest: Level Up spends exactly one point");
        check(throws(() => Core2RunRest.Commit(data, rest, "nap")), "rest: an unknown service is refused");

        // ---- events: quest gates, quest completion and the Core 2 class lock -----------------------------------------------
        var history = new List<JObject>();
        check(!Core2ActMap.EligibleEventIds(content, history).Contains("namelessKeeper"), "events: a quest event is gated until its history is met");
        history.Add(new JObject { ["kind"] = "eventChoice", ["eventId"] = "graveOfTheNameless", ["choiceId"] = "payRespects" });
        check(Core2ActMap.EligibleEventIds(content, history).Contains("namelessKeeper"), "events: meeting the history opens the quest event");
        var eventRun = Core2RunSession.Start(data, "reaver", 3);
        eventRun.Run.History.Add((JObject)history[0].DeepClone());
        var open = Core2RunEvents.Available(content, eventRun.Run, "namelessKeeper").Select(c => c.Id).ToList();
        check(open.Contains("acceptThanks") && !open.Contains("returnCinders"), "events: choices are gated by their own history requirements");
        check(throws(() => Core2RunEvents.Commit(data, eventRun.Run, eventRun.Rng, "namelessKeeper", "returnCinders")), "events: a gated choice is refused");
        var mirror = Core2RunEvents.Choices(content, "turncoatMirror").First(c => c.Id == "stepThrough");
        check(!Core2RunEvents.Runnable(mirror), "events: Turncoat Mirror 'Step through' is not runnable (the class is fixed for a Core 2 run)");
        check(throws(() => Core2RunEvents.Commit(data, eventRun.Run, eventRun.Rng, "turncoatMirror", "stepThrough")), "events: committing swapClass throws, as the JS build does");
        var cinders = eventRun.Run.Cinders;
        eventRun.Run.Cinders = 0;
        var priced = content.Events.Select(e => e.Id).SelectMany(id => Core2RunEvents.Choices(content, id).Select(c => (id, c)))
            .FirstOrDefault(p => p.c.Requires?["cinders"] is JValue price && (int)price > 0);
        if (priced.c != null)
            check(!Core2RunEvents.Affordable(priced.c, eventRun.Run) && throws(() => Core2RunEvents.Commit(data, eventRun.Run, eventRun.Rng, priced.id, priced.c.Id)),
                "events: a choice the run cannot afford is refused (" + priced.id + "/" + priced.c.Id + ")");
        eventRun.Run.Cinders = cinders;
    }
}
