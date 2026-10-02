// RunSummary checks — F11 end-of-run summary (domain side).
// Run from the repository root: dotnet run --project UnityTests/RunSummary
// 1. Labels match src/ui/screens/gameover.js wording and pluralisation.
// 2. A seeded real-command run driven in lockstep with and without a tracker
//    stays byte-identical after every command (snapshot, events, RNG counters).
// 3. Summary numbers equal the saved run and the OriginalProfile.Finish record.
using AshenSpire.Domain.Original;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "GameContent/Unity/Original/content.json")))
    root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var directory = Path.Combine(root, "GameContent/Unity/Original");
var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(directory, "content.json")));
var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(directory, "progression.json"))));
var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(directory, "mechanics.json")));
var supplement = JObject.Parse(File.ReadAllText(Path.Combine(directory, "event-choices.json")));
var passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Throws<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL (no " + typeof(T).Name + "): " + name); }
string Canon(JToken token) => token.ToString(Formatting.None);

JObject Player(string classId)
{
    var creator = new CreationModel(catalog, classId, "standard", progression);
    var kit = (string)catalog.Table("equipment.startingKits").First(x => (string)x["classId"] == classId && (bool?)x["baseline"] == true)["id"];
    return new OriginalCharacterBuilder(catalog, progression, mechanics).Build(creator, kit);
}

try
{
    // ---------------- 1. labels on a synthetic finished run ----------------
    var fresh = OriginalGameSession.Start(catalog, supplement, mechanics, Player("reaver"), 7);
    var baseRun = fresh.RunPlayer;
    Throws<InvalidOperationException>(() => RunSummary.FromRun(baseRun, catalog), "a run still on the map has no summary");
    var tracker0 = new RunSummaryTracker(fresh);
    Check(!tracker0.IsComplete && tracker0.Summary == null, "tracker stays empty until the run ends");
    Throws<InvalidOperationException>(() => tracker0.AttachEarned(new[] { "winAsReaver" }), "earned unlocks cannot attach before the end");
    tracker0.Dispose();

    var defeat = (JObject)baseRun.DeepClone();
    defeat["phase"] = "Defeat"; defeat["hp"] = 0; defeat["floor"] = 4; defeat["cinders"] = 57;
    defeat["stats"] = new JObject { ["fightsWon"] = 1, ["damageDealt"] = 214, ["damageTaken"] = 96 };
    defeat.Remove("customization");
    var d = RunSummary.FromRun(defeat, catalog);
    Check(d.DoorEyebrow == "The climb ends" && d.DoorTitle == "Defeat", "defeat door reads The climb ends / Defeat");
    Check(d.Title == "You perished" && d.AriaLabel == "You perished" && d.TitleTone == "loss", "defeat title reads You perished with loss tone");
    Check(d.CardEyebrow == "Forsaken" && d.CardName == "Forsaken", "missing customization names the hero Forsaken");
    var floors = (int)baseRun["mapGraph"]["floors"];
    Check(d.CardLine == $"Floor 4 / {floors} · 1 fight won", "one fight is singular: " + d.CardLine);
    Check(d.CardMeta == "Seed " + (string)baseRun["seedString"] && d.SeedString == RandomStreams.DisplaySeed(7), "seed meta uses the display seed");
    Check(string.Join("|", d.Stats.Select(s => s.Label)) == "Damage dealt|Damage taken|Cinders|Final HP", "four chips in HTML order and wording");
    Check(string.Join("|", d.Stats.Select(s => s.Value)) == $"214|96|57|0 / {baseRun["maxHp"]}", "chip values, Final HP is 0 on defeat");
    Check(d.DeckEyebrow == "Final deck" && d.DeckTitle == ((JArray)baseRun["deck"]).Count + " cards" && d.Deck.Count == ((JArray)baseRun["deck"]).Count, "final deck count and heading");
    Check(!d.HasEarned && !d.Lines().Contains("Earned"), "no Earned card without unlocks");
    Check(d.ReturnLabel == "Return to title" && d.HistoryLabel == "Run history", "button labels");

    var victory = (JObject)baseRun.DeepClone();
    victory["phase"] = "Victory"; victory["hp"] = 12; victory.Remove("seedString");
    victory["stats"] = new JObject { ["fightsWon"] = 11, ["damageDealt"] = 640, ["damageTaken"] = 212 };
    victory["customization"] = new JObject { ["name"] = "Ysolde", ["glyph"] = "⚔" };
    victory["deck"] = new JArray(((JArray)victory["deck"]).First.DeepClone());
    victory["deck"][0]["upgraded"] = true;
    ((JObject)victory["mapGraph"]).Remove("floors");
    var v = RunSummary.FromRun(victory, catalog, null, new[] { "winAsReaver", "no-such-unlock" });
    Check(v.DoorEyebrow == "The climb" && v.DoorTitle == "Victory" && v.Title == "Ember restored" && v.TitleTone == null, "victory reads The climb / Victory / Ember restored");
    Check(v.CardName == "Ysolde ⚔", "hero name and glyph");
    Check(v.CardLine == "Floor 0 · 11 fights won", "no map floors omits the ' / M' suffix");
    Check(v.SeedString == RandomStreams.DisplaySeed(7), "missing seedString falls back to the display seed");
    Check(v.Stats[3].Value == $"12 / {baseRun["maxHp"]}", "Final HP shows current HP on victory");
    var cardId = (string)victory["deck"][0]["cardId"]; var def = catalog.Record("cards", cardId);
    Check(v.DeckTitle == "1 card" && v.Deck[0].Glyph == "✦" && v.Deck[0].Name == ((string)def["upgrade"]?["name"] ?? (string)def["name"] + "+"), "upgraded card: ✦ glyph and '+' name, one card singular");
    Check(v.Earned.Count == 1 && v.Earned[0].Line == "Gilded Oathsworn outfit" && v.EarnedEyebrow == "Earned", "earned unlock line, unknown ids dropped");
    Check(v.ClassName == (string)catalog.Record("classes", "reaver")["name"], "class name recorded");
    Check(((ICollection<RunSummaryCard>)v.Deck).IsReadOnly && ((ICollection<RunSummaryStat>)v.Stats).IsReadOnly && ((ICollection<RunSummaryUnlock>)v.Earned).IsReadOnly, "summary collections are read-only");
    var before = string.Join("\n", v.Lines()); victory["stats"]["damageDealt"] = 1; victory["deck"] = new JArray();
    Check(string.Join("\n", v.Lines()) == before, "summary does not alias the source run");
    var noEarned = v.WithEarned(catalog, null);
    Check(!noEarned.HasEarned && v.HasEarned && noEarned.DamageDealt == v.DamageDealt, "WithEarned returns a new summary");

    // ------- 2 & 3. seeded real runs, tracked and untracked, in lockstep -------
    foreach (var (classId, seed, passive) in new[] { ("reaver", 1u, false), ("starseer", 2u, true) })
    {
        var plain = OriginalGameSession.Start(catalog, supplement, mechanics, Player(classId), seed);
        var watched = OriginalGameSession.Start(catalog, supplement, mechanics, Player(classId), seed);
        var tracker = new RunSummaryTracker(watched); var completions = 0; RunSummary fired = null;
        tracker.Completed += s => { completions++; fired = s; };
        Check(Canon(plain.Snapshot()) == Canon(watched.Snapshot()), $"{classId}/{seed}: identical start");
        int commands = 0; var diverged = false;
        while (!RunSummary.IsTerminal(plain.Phase) && commands < 4000)
        {
            var command = Policy.Next(plain, catalog, passive);
            command(plain); command(watched); commands++;
            if (Canon(plain.Snapshot()) != Canon(watched.Snapshot()) || Canon(plain.LastEvents) != Canon(watched.LastEvents) || plain.Phase != watched.Phase) { diverged = true; break; }
            if (!RunSummary.IsTerminal(plain.Phase) && tracker.IsComplete) throw new Exception("FAIL: tracker completed early");
        }
        var expected = passive ? OriginalRunPhase.Defeat : OriginalRunPhase.Victory;
        Check(!diverged, $"{classId}/{seed}: {commands} commands byte-identical with and without the tracker (state, events, RNG)");
        Check(plain.Phase == expected, $"{classId}/{seed}: run ends in {expected}");
        Check(completions == 1 && tracker.IsComplete && ReferenceEquals(fired, tracker.Summary) && tracker.ObservedCommands == commands, $"{classId}/{seed}: tracker latched once after observing every command");

        var s = tracker.Summary; var run = watched.RunPlayer;
        Check(s.Victory == !passive && s.DoorTitle == (passive ? "Defeat" : "Victory"), $"{classId}/{seed}: outcome");
        Check(s.FightsWon == (int)run["stats"]["fightsWon"] && s.DamageDealt == (int)run["stats"]["damageDealt"] && s.DamageTaken == (int)run["stats"]["damageTaken"], $"{classId}/{seed}: fights/damage equal the saved run stats");
        Check(s.DamageTaken > 0 && (passive || s.DamageDealt > 0 && s.FightsWon > 0), $"{classId}/{seed}: stats were actually accumulated ({s.FightsWon} fights, {s.DamageDealt} dealt, {s.DamageTaken} taken)");
        Check(s.Cinders == (int)run["cinders"] && s.Floor == (int)run["floor"] && s.Act == (int)run["actNumber"] && s.MaxHp == (int)run["maxHp"] && s.FinalHp == (passive ? 0 : (int)run["hp"]), $"{classId}/{seed}: cinders, floor, act and HP equal the saved run");
        Check(s.Deck.Select(c => c.InstanceId).SequenceEqual(((JArray)run["deck"]).Select(c => (string)c["instanceId"])) && s.Deck.All(c => c.Name == (string)watched.Resolve((JObject)((JArray)run["deck"]).First(x => (string)x["instanceId"] == c.InstanceId))["name"]), $"{classId}/{seed}: final deck in order with projected names");

        var profile = new OriginalProfile(catalog); var finish = profile.Finish("run-" + classId + seed, run, s.Victory);
        var record = (JObject)finish["result"];
        Check(s.ToRecord().Properties().All(p => JToken.DeepEquals(p.Value, record[p.Name])), $"{classId}/{seed}: summary matches the OriginalProfile history record");
        var earned = ((JArray)finish["newUnlocks"]).Values<string>().ToArray();
        var withEarned = tracker.AttachEarned(earned);
        Check(withEarned.Earned.Select(u => u.Id).SequenceEqual(earned) && (passive || earned.Contains("winAs" + char.ToUpperInvariant(classId[0]) + classId.Substring(1))), $"{classId}/{seed}: earned unlocks attach ({string.Join(",", earned)})");

        var restored = OriginalGameSession.Restore(JObject.Parse(watched.Snapshot().ToString(Formatting.None)));
        var fromSave = new RunSummaryTracker(restored).Summary;
        Check(fromSave != null && fromSave.Lines().SequenceEqual(s.Lines()), $"{classId}/{seed}: a restored finished save yields the same summary");
        Check(Canon(plain.Snapshot()) == Canon(watched.Snapshot()), $"{classId}/{seed}: building summaries left the run untouched");
        tracker.Dispose();
        Console.WriteLine("  " + string.Join(" | ", withEarned.Lines().Take(12)));
    }
}
catch (Exception error)
{
    Console.WriteLine(error.Message.StartsWith("FAIL") ? error.Message : "FAIL: " + error);
    Environment.Exit(1);
}
Console.WriteLine($"RunSummary: {passed} checks passed");

// Deterministic command policy (same greedy rules as UnityTests/Playthrough).
// passive=true only ends turns, rests and leaves: a reliable seeded defeat.
static class Policy
{
    public static Action<OriginalGameSession> Next(OriginalGameSession game, OriginalContentCatalog catalog, bool passive)
    {
        switch (game.Phase)
        {
            case OriginalRunPhase.Map:
            {
                var map = game.Map;
                var id = game.LegalNodeIds.OrderBy(n => passive ? 0 : RouteScore((string)map["nodes"][n]["type"])).ThenBy(n => n, StringComparer.Ordinal).First();
                if (passive) id = game.LegalNodeIds.OrderBy(n => (string)map["nodes"][n]["type"] == "monster" ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).First();
                return s => s.Enter(id);
            }
            case OriginalRunPhase.Combat:
            {
                if (passive) return s => s.EndTurn();
                var p = game.Player; var hand = game.Hand.OfType<JObject>().ToArray();
                var enemies = game.Enemies.OfType<JObject>().Where(e => (bool)e["alive"]).OrderBy(e => (int)e["hp"]).ToArray(); var target = enemies.First();
                if ((int)p["maxHp"] - (int)p["hp"] >= Math.Ceiling((int)p["maxHp"] * 0.25) && (int?)p["flaskCharges"]?["hpCurrent"] > 0) return s => s.DrinkCharge("hp");
                if ((int)p["mana"] == 0 && (int?)p["flaskCharges"]?["manaCurrent"] > 0 && hand.Any(c => (int)game.Cost(c)["mana"] > 0)) return s => s.DrinkCharge("mana");
                if (p["flasks"] is JArray flasks && flasks.Count > 0) { var f = catalog.Record("flasks", (string)flasks[0]["flaskId"]); var aimed = (bool?)f["targeted"] == true ? (string)target["id"] : null; return s => s.DrinkFlask(0, aimed); }
                var best = hand.Select(c => new { Instance = c, Definition = game.Resolve(c), Cost = game.Cost(c) })
                    .Where(c => !(c.Definition["keywords"] as JArray ?? new JArray()).Values<string>().Contains("unplayable") && (int)c.Cost["action"] <= (int)p["energy"] && (int)c.Cost["mana"] <= (int)p["mana"] && (int)c.Cost["stamina"] <= (int)p["stamina"])
                    .Select(c => new { c.Instance, Score = CardScore(c.Definition, p, target, enemies.Length) }).OrderByDescending(c => c.Score).FirstOrDefault();
                if (best != null && best.Score > 0) { var instance = (string)best.Instance["instanceId"]; var targetId = (string)target["id"]; return s => s.Play(instance, targetId); }
                var breathUses = (int?)game.Room["combatSnapshot"]?["catchBreathUses"] ?? 0;
                if ((int)p["energy"] > 0 && (int)p["stamina"] < (int)p["maxStamina"] && breathUses < 1) return s => s.CatchBreath();
                return s => s.EndTurn();
            }
            case OriginalRunPhase.Rewards:
            {
                var room = game.Room; var rewards = (JObject)room["rewards"];
                if (!passive)
                    foreach (var kind in new[] { "cinders", "relic", "armament", "flask", "card" })
                    {
                        if ((string)room["states"][kind] == "taken") continue; string rewardId = null;
                        var offered = kind == "cinders" ? (int?)rewards["cinders"] > 0 : kind == "card" ? (rewards["cardIds"] as JArray)?.Count > 0 : !string.IsNullOrEmpty((string)rewards[kind + "Id"]);
                        if (!offered) continue;
                        if (kind == "card") rewardId = ((JArray)rewards["cardIds"]).Values<string>().OrderByDescending(card => RewardScore(catalog.Record("cards", card))).First();
                        if (kind == "flask" && (game.RunPlayer["flasks"] as JArray)?.Count >= 3) continue;
                        var k = kind; return s => s.Reward(k, rewardId);
                    }
                return s => s.ContinueRewards();
            }
            case OriginalRunPhase.Shrine:
                if ((game.RunPlayer["relics"] as JArray ?? new JArray()).Values<string>().Any(id => (bool?)catalog.Record("relics", id)["passives"]?["shrineNoRest"] == true)) return s => s.LeaveShrine();
                return passive ? s => s.LeaveShrine() : s => s.Rest();
            case OriginalRunPhase.Event:
            {
                var choice = game.EventChoices.Where(c => ((int?)c["requires"]?["cinders"] ?? 0) <= (int)game.RunPlayer["cinders"]).OrderByDescending(EventScore).FirstOrDefault() ?? throw new Exception("No affordable event choice");
                var choiceId = (string)choice["id"]; return s => s.ChooseEvent(choiceId);
            }
            case OriginalRunPhase.EventResult: return s => s.LeaveEvent();
            case OriginalRunPhase.Shop:
            {
                var stock = (JArray)game.Room["relics"];
                var affordable = passive ? null : stock.Select((r, i) => new { Row = r, Index = i }).FirstOrDefault(x => (bool?)x.Row["sold"] != true && (int)x.Row["cost"] <= (int)game.RunPlayer["cinders"]);
                if (affordable != null) { var index = affordable.Index; return s => s.Buy("relic", index); }
                return s => s.LeaveShop();
            }
            default: throw new Exception("Unhandled run phase " + game.Phase);
        }
    }
    static int RouteScore(string kind) => kind switch { "shrine" => 0, "treasure" => 1, "event" => 2, "merchant" => 3, "monster" => 4, "elite" => 5, "boss" => 6, _ => 4 };
    static double RewardScore(JObject card) => (string)card["type"] == "attack" ? 10 : (card["effects"] as JArray ?? new JArray()).Any(e => (string)e["op"] == "heal") ? 9 : 5;
    static double EventScore(JToken choice) { double score = 0; foreach (var e in choice["effects"] as JArray ?? new JArray()) score += (string)e["op"] switch { "heal" => 10, "addRelic" => 8, "addCinders" => (double?)e["amount"] > 0 ? 5 : -5, "damage" => -10, "loseHp" => -10, "loseMaxHpPct" => -15, "startCombat" => -20, _ => 1 }; return score; }
    static double CardScore(JObject card, JObject player, JObject enemy, int enemies)
    {
        double score = 0;
        foreach (var effect in card["effects"] as JArray ?? new JArray())
        {
            double Amount(string field, double fallback = 0) { var value = effect[field]; if (value == null) return fallback; if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return (double)value; return fallback + 3; }
            var bonus = Amount("attributeBonus");
            switch ((string)effect["op"])
            {
                case "damage": var damage = Amount("amount", 6) * Amount("hits", 1) + bonus; score += damage * ((string)effect["target"] == "allEnemies" ? enemies : 1); if (damage >= (int)enemy["hp"] + (int)enemy["block"]) score += 25; break;
                case "block": score += Math.Max(0, Math.Min(Amount("amount", 5), 15 - (int)player["block"])) * 0.8; break;
                case "heal": score += Math.Min(Amount("amount", 5) + bonus, (int)player["maxHp"] - (int)player["hp"]) * 1.5; break;
                case "applyStatus": score += 6; break;
                case "draw": score += 5; break;
                case "gainEnergy": score += 10; break;
                case "dodgeRoll": score += (int)player["block"] < 8 ? 4 : 0; break;
                case "enterStance": score += 3; break;
                case "poiseDamage": score += Amount("amount", 2) * 0.6; break;
                case "loseHp": score -= Amount("amount", 2); break;
            }
        }
        return score;
    }
}
