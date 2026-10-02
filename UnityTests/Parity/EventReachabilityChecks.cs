// US-9.3 event reachability: every authored event room can be entered through the
// native run coordinator, and every authored choice is either accepted (result text
// shown, history recorded, run continues) or refused when its requirement is unmet,
// with a constructed state that meets it. Uses the shipped content, not a fixture copy.
// Routes are found by seed search: rooms before the target are cleared with the
// session's own commands (opaque combat completion, leaving shops/shrines, exiting
// other events); the target room is entered with EnterNode, never by editing a save.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class EventReachabilityChecks
{
    internal const int DocumentedEvents = 22, DocumentedChoices = 62;

    internal static int Run(string root)
    {
        var contentRoot = Path.Combine(root, "GameContent/Unity/Original");
        var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(contentRoot, "content.json")));
        var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "mechanics.json")));
        var supplement = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "event-choices.json")));
        var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "progression.json"))));
        var callbacks = new OriginalRunContent(catalog, reconcile: new OriginalPlayerProjection(catalog, mechanics).Reconcile);
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("Event reachability: " + message); checks++; }
        void Same(JToken actual, JToken expected, string message) => Check(JToken.DeepEquals(actual, expected), message);

        // 1. Enumerate from content and cross-check the frozen choice identities.
        var events = catalog.Table("events").Cast<JObject>().ToList();
        var authored = (JObject)supplement["eventChoices"]!;
        var choiceCount = events.Sum(e => ((JArray)e["choices"]!).Count);
        Check(events.Count == DocumentedEvents, $"content has {events.Count} events, documented {DocumentedEvents}");
        Check(choiceCount == DocumentedChoices, $"content has {choiceCount} choices, documented {DocumentedChoices}");
        Check(authored.Count == events.Count && authored.Properties().Sum(p => ((JArray)p.Value).Count) == choiceCount, "event-choices.json covers exactly the content events and choices");
        foreach (var e in events)
        {
            var id = (string)e["id"]!; var rows = authored[id] as JArray;
            Check(rows != null && rows.Count == ((JArray)e["choices"]!).Count, "choice identities for " + id);
            for (var i = 0; i < rows!.Count; i++)
            {
                var content = e["choices"]![i]!; var row = rows[i];
                Check(!string.IsNullOrEmpty((string)row["id"]) && rows.Count(r => (string)r["id"] == (string)row["id"]) == 1, $"unique choice id {id}/{i}");
                Check(!string.IsNullOrEmpty((string)content["resultText"]), $"result text {id}/{row["id"]}");
                // The supplement may only add its own fields; every content field must match exactly.
                var copied = (JObject)row.DeepClone(); copied.Remove("id"); copied.Remove("requiresHistory");
                Same(copied, content, $"choice row {id}/{row["id"]} matches content apart from id/requiresHistory");
            }
        }

        // 2. Route search through the real coordinator.
        JObject BasePlayer()
        {
            var creator = LeanAllocation.Create(catalog, "reaver", progression);
            var kit = (string)catalog.Table("equipment.startingKits").First(k => (string)k["classId"] == "reaver" && (bool?)k["baseline"] == true)["id"]!;
            var player = new OriginalCharacterBuilder(catalog, progression, mechanics).Build(creator, kit);
            player["cinders"] = 1000; // every cinder gate is met; refusals are constructed below
            return player;
        }
        var basePlayer = BasePlayer();
        // Clears the current room with the session's own commands until the map is shown.
        bool Settle(OriginalRunSession run)
        {
            for (var guard = 0; guard < 20; guard++)
                switch (run.Phase)
                {
                    case OriginalRunPhase.Map: return true;
                    case OriginalRunPhase.Combat: run.CompleteCombat("victory", run.Player(), run.CreateRandom()); break;
                    case OriginalRunPhase.Rewards: run.ContinueRewards(false); break;
                    case OriginalRunPhase.Shop: run.LeaveShop(); break;
                    case OriginalRunPhase.Shrine: run.LeaveShrine(); break;
                    case OriginalRunPhase.Event: if (!run.ChooseEvent((string)run.EventChoices().Last()["id"]!)) return false; break;
                    case OriginalRunPhase.EventResult: run.LeaveEvent(); break;
                    default: return false;
                }
            return false;
        }
        List<string>? PathTo(OriginalRunSession run, Func<JObject, bool> target)
        {
            var nodes = (JObject)run.Map()["nodes"]!; var parent = new Dictionary<string, string?>(); var queue = new Queue<string>();
            foreach (var id in run.LegalNodeIds()) if (parent.TryAdd(id, null)) queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (target((JObject)nodes[id]!)) { var path = new List<string>(); for (string? at = id; at != null; at = parent[at]) path.Insert(0, at); return path; }
                foreach (var next in nodes[id]!["next"]!.Values<string>()) if (parent.TryAdd(next!, id)) queue.Enqueue(next!);
            }
            return null;
        }
        bool Walk(OriginalRunSession run, List<string> path)
        {
            for (var i = 0; i < path.Count; i++)
            {
                if (!run.EnterNode(path[i])) return false;
                if (i < path.Count - 1 && !Settle(run)) return false;
            }
            return true;
        }
        // Steps: earlier (event, choice) pairs build history; the last step only arrives.
        // Mirrors OriginalGameSession.Start (reconciled player, mechanics frozen into the
        // supplement) so every arrival save also restores as a full game session.
        var startPlayer = (JObject)basePlayer.DeepClone(); new OriginalPlayerProjection(catalog, mechanics).Reconcile(startPlayer);
        var frozen = (JObject)supplement.DeepClone(); frozen["mechanics"] = mechanics.DeepClone();
        var gates = (JObject)catalog.Data()["eventHistoryRequirements"]!;
        // Ungated steps must be in the current act's map. A history-gated event is only
        // dealt into maps built after its unlocking choice, so it is sought in the next act.
        OriginalRunSession? Reach(uint seed, (string Event, string? Choice)[] steps)
        {
            var run = OriginalRunSession.Start(catalog, frozen, startPlayer, seed, callbacks);
            foreach (var step in steps)
            {
                if (gates[step.Event] != null)
                {
                    if (run.ActNumber == 3) return null; // the final boss ends the run
                    var boss = PathTo(run, node => (string)node["type"] == "boss");
                    if (boss == null || !Walk(run, boss) || !Settle(run) || run.Phase != OriginalRunPhase.Map) return null;
                }
                var path = PathTo(run, node => (string)node["type"] == "event" && (string)node["resolved"]?["kind"] == "event" && (string)node["resolved"]!["eventId"] == step.Event);
                if (path == null || !Walk(run, path) || run.Phase != OriginalRunPhase.Event || (string)run.Room()["eventId"] != step.Event) return null;
                if (step.Choice == null) return run;
                if (!run.ChooseEvent(step.Choice)) return null;
                run.LeaveEvent();
                if (!Settle(run)) return null;
            }
            return null;
        }
        // The search starts at a recorded seed (found by the same search from 1) so the
        // section stays fast; if content changes move the route, it keeps searching.
        var routes = new List<string>();
        OriginalRunSession Find((string Event, string? Choice)[] steps, uint first = 1, Func<OriginalRunSession, bool>? wanted = null)
        {
            for (uint seed = first; seed < first + 5000; seed++)
            {
                var found = Reach(seed, steps);
                if (found != null && (wanted == null || wanted(found))) { routes.Add(steps.Last().Event + "@" + seed); return found; }
            }
            throw new Exception($"Event reachability: no seed in {first}..{first + 4999} reaches " + string.Join(" -> ", steps.Select(s => s.Event + "/" + (s.Choice ?? "arrive"))));
        }

        // 3. At each arrival, choose every authored choice from a fresh restored save.
        var accepted = new HashSet<string>(); var refused = new HashSet<string>(); var outcomes = new Dictionary<string, HashSet<string>>();
        bool Eligible(JToken choice, JObject run) => OriginalRunRules.HistoryMet(choice["requiresHistory"], (JArray)run["history"]!) && ((int?)choice["requires"]?["cinders"] ?? 0) <= (int)run["cinders"]!;
        void Accept(JObject arrival, JObject choice, string eventId, string label)
        {
            var choiceId = (string)choice["id"]!; var key = eventId + "/" + choiceId;
            var run = OriginalRunSession.Restore(arrival, callbacks); var before = run.Player();
            Check(run.EventChoices().Any(c => (string)c["id"] == choiceId), key + " offered " + label);
            Check(run.ChooseEvent(choiceId), key + " accepted " + label);
            Check(run.Phase == OriginalRunPhase.EventResult, key + " shows a result");
            var room = run.Room();
            Check((string)room["eventId"] == eventId && (string)room["choiceId"] == choiceId, key + " result receipt");
            Check(!string.IsNullOrEmpty((string)room["resultText"]) && (string)room["resultText"] == (string)choice["resultText"], key + " result text");
            var last = (JObject)((JArray)run.Player()["history"]!).Last();
            Check((string)last["kind"] == "eventChoice" && (string)last["eventId"] == eventId && (string)last["choiceId"] == choiceId && (string)last["mapNodeId"] == (string)before["mapNodeId"], key + " history row");
            var requiredCinders = (int?)choice["requires"]?["cinders"] ?? 0;
            Check((int)run.Player()["cinders"]! >= 0 && (int)before["cinders"]! >= requiredCinders, key + " cinders gate");
            // The UI command path produces the identical committed state.
            var game = OriginalGameSession.Restore(arrival); game.ChooseEvent(choiceId);
            Same(game.Snapshot(), run.Snapshot(), key + " game session matches run session");
            // Save -> restore mid-event (result screen) keeps the result and does not reroll.
            var saved = game.Snapshot(); game = OriginalGameSession.Restore(JObject.Parse(saved.ToString()));
            Same(game.Snapshot(), saved, key + " result-screen round trip");
            Check(game.Phase == OriginalRunPhase.EventResult && (string)game.Room["resultText"] == (string)choice["resultText"], key + " result shown after reload");
            game.LeaveEvent();
            var combat = ((JArray)choice["effects"]!).FirstOrDefault(e => (string)e["op"] == "startCombat");
            var entered = (string?)run.Player()["combatEntered"];
            Check(combat != null || entered == null, key + " has no authored fight");
            Check(combat == null || combat["if"] != null || entered == (string)combat["encounterId"], key + " unconditional fight is entered");
            if (!outcomes.ContainsKey(key)) outcomes[key] = new HashSet<string>();
            outcomes[key].Add(entered != null ? "fight" : "map");
            if (entered != null)
            {
                Check(entered == (string)combat!["encounterId"] && game.Phase == OriginalRunPhase.Combat && (string)game.Room["encounterId"] == entered && game.Enemies.Count > 0, key + " continues into its fight");
                var resumed = OriginalGameSession.Restore(game.Snapshot()); Same(resumed.Snapshot(), game.Snapshot(), key + " event fight round trip");
            }
            else Check(game.Phase == OriginalRunPhase.Map && game.LegalNodeIds.Length > 0, key + " returns to the map");
            accepted.Add(key);
        }
        void Refuse(JObject arrival, JObject choice, string eventId, string label)
        {
            var choiceId = (string)choice["id"]!; var key = eventId + "/" + choiceId;
            var run = OriginalRunSession.Restore(arrival, callbacks); var before = run.Snapshot();
            if (!OriginalRunRules.HistoryMet(choice["requiresHistory"], (JArray)arrival["run"]!["history"]!)) Check(run.EventChoices().All(c => (string)c["id"] != choiceId), key + " hidden " + label);
            Check(!run.ChooseEvent(choiceId), key + " refused " + label);
            Same(run.Snapshot(), before, key + " refusal leaves state and RNG unchanged");
            var game = OriginalGameSession.Restore(arrival); var refusedByGame = false;
            try { game.ChooseEvent(choiceId); } catch (ArgumentException) { refusedByGame = true; }
            Check(refusedByGame && game.Phase == OriginalRunPhase.Event, key + " game session refuses " + label);
            Same(game.Snapshot(), before, key + " game refusal rolls back");
            refused.Add(key);
        }
        JObject WithCinders(JObject arrival, int cinders) { var copy = (JObject)arrival.DeepClone(); copy["run"]!["cinders"] = cinders; return copy; }
        void Exercise(OriginalRunSession arrived, string label)
        {
            var arrival = arrived.Snapshot(); var eventId = (string)arrived.Room()["eventId"]!;
            Same(OriginalRunSession.Restore(JObject.Parse(arrival.ToString()), callbacks).Snapshot(), arrival, eventId + " event-room round trip " + label);
            Same(OriginalGameSession.Restore(JObject.Parse(arrival.ToString())).Snapshot(), arrival, eventId + " game event-room round trip " + label);
            foreach (JObject choice in (JArray)authored[eventId]!)
            {
                var required = (int?)choice["requires"]?["cinders"];
                if (Eligible(choice, (JObject)arrival["run"]!)) Accept(arrival, choice, eventId, label);
                else Refuse(arrival, choice, eventId, label);
                if (required != null && OriginalRunRules.HistoryMet(choice["requiresHistory"], (JArray)arrival["run"]!["history"]!))
                {
                    Refuse(WithCinders(arrival, required.Value - 1), choice, eventId, label + " short of cinders");
                    Accept(WithCinders(arrival, required.Value), choice, eventId, label + " with exactly enough cinders");
                }
            }
        }

        foreach (var e in events.Where(e => gates[(string)e["id"]!] == null)) Exercise(Find(new (string, string?)[] { ((string)e["id"]!, null) }), "first visit");
        // History-gated events and choices, each reached through the earlier choices that unlock them.
        Exercise(Find(new (string, string?)[] { ("abandonedCart", "lootStrongbox"), ("merchantsGhost", null) }), "after looting the cart");
        Exercise(Find(new (string, string?)[] { ("graveOfTheNameless", "digForCinders"), ("namelessKeeper", null) }), "after digging");
        Exercise(Find(new (string, string?)[] { ("graveOfTheNameless", "payRespects"), ("namelessKeeper", null) }), "after paying respects");
        Exercise(Find(new (string, string?)[] { ("graveOfTheNameless", "payRespects"), ("namelessKeeper", "acceptThanks"), ("namelessRest", null) }, 748), "after thanks");
        Exercise(Find(new (string, string?)[] { ("graveOfTheNameless", "digForCinders"), ("namelessKeeper", "returnCinders"), ("namelessRest", null) }, 748), "after returning cinders");
        Exercise(Find(new (string, string?)[] { ("graveOfTheNameless", "digForCinders"), ("namelessKeeper", "faceKeeper"), ("namelessRest", null) }, 748), "after facing the keeper");
        // Chance-gated fights: find arrivals whose committed roll gives the outcome not yet seen.
        foreach (var e in events)
            foreach (JObject choice in (JArray)authored[(string)e["id"]!]!)
            {
                var fight = ((JArray)choice["effects"]!).FirstOrDefault(x => (string)x["op"] == "startCombat");
                if (fight?["if"] == null) continue;
                var key = e["id"] + "/" + choice["id"];
                Check(gates[(string)e["id"]!] == null, key + " chance fight sits on an ungated event");
                foreach (var outcome in new[] { "fight", "map" })
                {
                    if (outcomes[key].Contains(outcome)) continue;
                    bool Gives(OriginalRunSession arrived) { var probe = OriginalRunSession.Restore(arrived.Snapshot(), callbacks); return probe.ChooseEvent((string)choice["id"]!) && (probe.Player()["combatEntered"]?.Type == JTokenType.String) == (outcome == "fight"); }
                    Exercise(Find(new (string, string?)[] { ((string)e["id"]!, null) }, 1, Gives), "rolling " + outcome);
                    Check(outcomes[key].Contains(outcome), key + " reaches outcome " + outcome);
                }
            }

        var all = authored.Properties().SelectMany(p => ((JArray)p.Value).Select(c => p.Name + "/" + c["id"])).ToList();
        var missing = all.Where(k => !accepted.Contains(k)).ToList();
        Check(missing.Count == 0, "choices never accepted: " + string.Join(", ", missing));
        var gated = authored.Properties().SelectMany(p => ((JArray)p.Value).Where(c => c["requires"] != null || c["requiresHistory"] != null).Select(c => p.Name + "/" + c["id"])).ToList();
        var unrefused = gated.Where(k => !refused.Contains(k)).ToList();
        Check(unrefused.Count == 0, "gated choices never refused: " + string.Join(", ", unrefused));
        Console.WriteLine($"Event reachability: {events.Count} events, {accepted.Count} of {choiceCount} choices accepted, {refused.Count} gated choices refused, {outcomes.Count(o => o.Value.Count == 2)} chance fights seen both ways; {checks} checks passed");
        Console.WriteLine("Event reachability routes (event@seed): " + string.Join(", ", routes));
        return checks;
    }
}
