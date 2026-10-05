#nullable enable
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

internal static class TurnBudgetChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        void Equal(JToken a, JToken b, string label) => Check(JToken.DeepEquals(a, b), label);
        void Refuses(Action action, string label) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception(label); }
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(root, "UnityTests/Parity/coop-reference.json")));
        var content = (JObject)fixture["content"]!.DeepClone();
        var enemy = content["enemies"]!.OfType<JObject>().First(e => (string?)e["id"] == "wanderingSoldier");
        enemy["firstMove"] = "wait";
        enemy["moves"] = new JObject { ["wait"] = new JObject { ["weight"] = 1, ["intent"] = "defend", ["block"] = 1 } };
        var catalog = new OriginalContentCatalog(content.ToString());
        var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/mechanics.json")));
        mechanics["stamina"]!["turnBudget"] = true;
        var deck = new JArray(Enumerable.Range(0, 7).Select(i => new JObject { ["instanceId"] = "budget" + i, ["cardId"] = "defend" }));
        JObject Resolve(JObject instance)
        {
            var def = catalog.Record("cards", "defend"); var id = (string?)instance["instanceId"];
            def.Remove("properties"); def["kindIds"] = new JArray("classification.skill"); def["keywords"] = new JArray(); def["staminaCost"] = 99;
            def["cost"] = id == "budget2" ? new JValue("X") : new JValue(id == "budget0" ? 2 : 0);
            def["manaCost"] = id == "budget0" ? 1 : id == "budget6" ? 99 : 0;
            def["effects"] = new JArray(new JObject { ["op"] = id == "budget1" ? "gainEnergy" : id == "budget3" || id == "budget4" ? "restoreStamina" : "block", ["target"] = "self", ["amount"] = id == "budget1" ? 5 : 2 });
            return def;
        }
        JObject Player()
        {
            var player = (JObject)fixture["fixtures"]![0]!["players"]![0]!.DeepClone();
            player["maxStamina"] = 5; player["stamina"] = 1; player["energyMax"] = 3;
            player["maxMana"] = 3; player["mana"] = 3; player["drawPerTurn"] = 7; player["relicIds"] = new JArray();
            return player;
        }
        void Pools(JObject p, int expected, string label)
        {
            Check((int)p["stamina"]! == expected && (int)p["energy"]! == expected, label);
            Check((int)p["maxStamina"]! == 5 && (int)p["energyMax"]! == 5, "Base capacity aliases");
        }
        var game = new CombatSession(catalog, mechanics, new RandomStreams(17), Player(), deck.OfType<JObject>(), new[] { "wanderingSoldier" }, Resolve);
        Pools(game.Player, 5, "Opening turn refills Stamina, ignoring legacy action capacity");
        var before = game.Snapshot();
        Refuses(() => game.PlayCard("budget6"), "Insufficient Mana must refuse"); Equal(before, game.Snapshot(), "Unaffordable card leaves piles, streams, counters and events untouched");
        Refuses(() => game.PlayCard("budget0", "missing"), "Invalid target must refuse"); Equal(before, game.Snapshot(), "Invalid target spends nothing");
        var events = game.PlayCard("budget0"); Pools(game.Player, 3, "Fixed cost charges shared pool once");
        Check((int)game.Player["mana"]! == 2 && (int)game.Player["counters"]!["staminaSpentThisTurn"]! == 2, "Mana and spend accounting commit together");
        Check(events.Any(e => (string?)e["type"] == "staminaSpent" && (int?)e["amount"] == 2), "Fixed spend receipt");
        game.PlayCard("budget1"); Pools(game.Player, 8, "Gain-energy effect permits temporary over-capacity Stamina");
        game.PlayCard("budget3"); Pools(game.Player, 8, "Recovery never removes an over-capacity bonus");
        var saved = game.Snapshot(); var restored = CombatSession.Restore(catalog, mechanics, saved, Resolve);
        Equal(saved, restored.Snapshot(), "Over-capacity budget round-trips exactly");
        Equal(game.PlayCard("budget2"), restored.PlayCard("budget2"), "X payment event replay");
        Pools(game.Player, 0, "X consumes all shared Stamina");
        Check((int)game.Player["counters"]!["staminaSpentThisTurn"]! == 10, "X actual spend contributes to counter");
        Equal(game.Snapshot(), restored.Snapshot(), "X payment state replay");
        game.PlayCard("budget4"); Pools(game.Player, 2, "Recovery effect updates both aliases");
        Check((int)game.Player["counters"]!["staminaSpentThisTurn"]! == 10, "Recovery does not erase spend");
        var next = game.EndTurn(); Pools(game.Player, 5, "Next turn fully refills budget");
        Check((int)game.Player["counters"]!["staminaSpentThisTurn"]! == 0, "Turn resets spend counter");
        Check(!next.Any(e => (string?)e["reason"] == "idle"), "Current Stamina has no legacy idle recovery");
        var corrupt = game.Snapshot(); corrupt["player"]!["energy"] = 4;
        Refuses(() => CombatSession.Restore(catalog, mechanics, corrupt, Resolve), "Mismatched saved aliases must refuse");
        var pending = game.Snapshot(); pending["player"]!["pendingActionLoss"] = 2;
        var penalized = CombatSession.Restore(catalog, mechanics, pending, Resolve); penalized.EndTurn();
        Pools(penalized.Player, 3, "Pending turn loss applies once"); penalized.EndTurn(); Pools(penalized.Player, 5, "Pending turn loss clears");
        var badMechanics = (JObject)mechanics.DeepClone(); badMechanics["stamina"]!["turnBudget"] = "yes";
        Refuses(() => OriginalTurnStamina.Enabled(badMechanics), "Invalid saved rules flag must refuse");
        var players = fixture["fixtures"]![0]!["players"]!.OfType<JObject>().Select(p => { var seat = Player(); seat["id"] = p["id"]; seat["deck"] = deck.DeepClone(); return seat; }).ToArray();
        JObject ResolveSeat(string id, JObject card) => Resolve(card);
        var party = new OriginalCoopCombat(catalog, mechanics, new RandomStreams(17), players, new[] { "wanderingSoldier" }, ResolveSeat);
        foreach (var seat in party.Players) Pools((JObject)seat["entity"]!, 5, "Every seat begins with its own budget");
        party.Play("p1", "budget0");
        Pools((JObject)party.Players[0]!["entity"]!, 3, "Acting seat pays once"); Pools((JObject)party.Players[1]!["entity"]!, 5, "Other seat remains untouched");
        party.EndTurn("p1"); Pools((JObject)party.Players[0]!["entity"]!, 0, "Ended seat loses unspent budget");
        var partySave = party.Snapshot(); var resumed = OriginalCoopCombat.Restore(catalog, mechanics, partySave, ResolveSeat);
        Equal(partySave, resumed.Snapshot(), "Partly ended party round-trips");
        Equal(party.EndTurn("p2"), resumed.EndTurn("p2"), "Party next-turn events replay exactly");
        Equal(party.Snapshot(), resumed.Snapshot(), "Party next-turn state replays exactly");
        foreach (var seat in party.Players) Pools((JObject)seat["entity"]!, 5, "Every seat refills next round");
        return checks;
    }
}
