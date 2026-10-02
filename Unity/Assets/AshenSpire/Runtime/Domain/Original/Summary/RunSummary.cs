// RunSummary.cs — immutable end-of-run view-model (F11 / US-11.2), pure C#.
// PARITY: mirrors the HTML end screen src/ui/screens/gameover.js (mountGameOver)
// field for field and label for label. The HTML screen shows exactly: the page
// door ("The climb" / "The climb ends", "Victory" / "Defeat"), the decide title
// ("Ember restored" / "You perished"), a detail card (eyebrow "Forsaken", hero
// name + glyph, "Floor N / M · K fights won", "Seed S"), four stat chips
// (Damage dealt, Damage taken, Cinders, Final HP), the "Earned" unlock list,
// "Final deck" with its "N cards" count, and the deck strip (✦ upgraded, ◆ not).
// The HTML computes NO score, playtime, highest hit or cause of death, so none
// is invented here. ClassId/ClassName/Act/BossesBeaten are carried as record
// data because the HTML run-history record (src/main.js runResult) stores them.
// SOURCE OF TRUTH: the committed run state (run["stats"] accumulates the same
// damageDealt/hpLost events the HTML trackStats in src/ui/screens/combat.js
// sums), so the summary always equals the saved run. Building one never
// mutates the run, touches an RNG stream or issues a command.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    /// <summary>One StatStrip chip: key label and display-ready value.</summary>
    public sealed class RunSummaryStat
    {
        public string Id { get; }
        public string Label { get; }
        public string Value { get; }
        public RunSummaryStat(string id, string label, string value) { Id = id; Label = label; Value = value; }
    }

    /// <summary>One card on the final-deck strip.</summary>
    public sealed class RunSummaryCard
    {
        public string InstanceId { get; }
        public string CardId { get; }
        public string Name { get; }
        public bool Upgraded { get; }
        /// <summary>"✦" when upgraded, otherwise "◆" (gameover.js kitItem glyph).</summary>
        public string Glyph => Upgraded ? "✦" : "◆";
        public RunSummaryCard(string instanceId, string cardId, string name, bool upgraded) { InstanceId = instanceId; CardId = cardId; Name = name; Upgraded = upgraded; }
    }

    /// <summary>One newly earned unlock ("Earned" detail card).</summary>
    public sealed class RunSummaryUnlock
    {
        public string Id { get; }
        public string Name { get; }
        public string Kind { get; }
        /// <summary>The dc-line text: name, a space, then the kind status text.</summary>
        public string Line => Name + " " + Kind;
        public RunSummaryUnlock(string id, string name, string kind) { Id = id; Name = name; Kind = kind; }
    }

    public sealed class RunSummary
    {
        public const string DefaultHeroName = "Forsaken";

        // ---- raw record data ------------------------------------------------
        public bool Victory { get; }
        public uint Seed { get; }
        public string SeedString { get; }
        public string ClassId { get; }
        public string ClassName { get; }
        public string HeroName { get; }
        public string HeroGlyph { get; }
        public int Act { get; }
        public int Floor { get; }
        /// <summary>Floors in the current act map; null when the run has no map graph (HTML omits " / M").</summary>
        public int? FloorsInAct { get; }
        public int FightsWon { get; }
        public int DamageDealt { get; }
        public int DamageTaken { get; }
        public int Cinders { get; }
        /// <summary>HP shown on the screen: current HP on victory, 0 on defeat.</summary>
        public int FinalHp { get; }
        public int MaxHp { get; }
        public IReadOnlyList<string> BossesBeaten { get; }
        public IReadOnlyList<RunSummaryCard> Deck { get; }
        public IReadOnlyList<RunSummaryUnlock> Earned { get; }

        // ---- display-ready labels (gameover.js wording) --------------------
        public string DoorEyebrow => Victory ? "The climb" : "The climb ends";
        public string DoorTitle => Victory ? "Victory" : "Defeat";
        /// <summary>Title·L text; CSS may render it in capitals ("YOU PERISHED", SPEC §7.4).</summary>
        public string Title => Victory ? "Ember restored" : "You perished";
        public string AriaLabel => Title;
        /// <summary>"loss" on defeat (data-tone on the title), null on victory.</summary>
        public string TitleTone => Victory ? null : "loss";
        public string CardEyebrow => "Forsaken";
        public string CardName => (HeroName + " " + HeroGlyph).Trim();
        public string CardLine => "Floor " + Floor + (FloorsInAct.HasValue ? " / " + FloorsInAct.Value : "") + " · " + FightsWon + " fight" + (FightsWon == 1 ? "" : "s") + " won";
        public string CardMeta => "Seed " + SeedString;
        public IReadOnlyList<RunSummaryStat> Stats { get; }
        public string EarnedEyebrow => "Earned";
        public bool HasEarned => Earned.Count > 0;
        public string DeckEyebrow => "Final deck";
        public string DeckTitle => Deck.Count + " card" + (Deck.Count == 1 ? "" : "s");
        public string ReturnLabel => "Return to title";
        public string HistoryLabel => "Run history";

        private RunSummary(bool victory, uint seed, string seedString, string classId, string className, string heroName, string heroGlyph,
            int act, int floor, int? floors, int fightsWon, int dealt, int taken, int cinders, int finalHp, int maxHp,
            IEnumerable<string> bosses, IEnumerable<RunSummaryCard> deck, IEnumerable<RunSummaryUnlock> earned)
        {
            Victory = victory; Seed = seed; SeedString = seedString; ClassId = classId; ClassName = className; HeroName = heroName; HeroGlyph = heroGlyph;
            Act = act; Floor = floor; FloorsInAct = floors; FightsWon = fightsWon; DamageDealt = dealt; DamageTaken = taken; Cinders = cinders; FinalHp = finalHp; MaxHp = maxHp;
            BossesBeaten = Array.AsReadOnly(bosses.ToArray()); Deck = Array.AsReadOnly(deck.ToArray()); Earned = Array.AsReadOnly(earned.ToArray());
            Stats = Array.AsReadOnly(new[]
            {
                new RunSummaryStat("damageDealt", "Damage dealt", DamageDealt.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new RunSummaryStat("damageTaken", "Damage taken", DamageTaken.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new RunSummaryStat("cinders", "Cinders", Cinders.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new RunSummaryStat("finalHp", "Final HP", FinalHp + " / " + MaxHp),
            });
        }

        public static bool IsTerminal(OriginalRunPhase phase) => phase == OriginalRunPhase.Victory || phase == OriginalRunPhase.Defeat;

        /// <summary>
        /// Build from a finished run's state (OriginalGameSession.RunPlayer or a save's
        /// snapshot["run"]). resolveCard, when given, names cards exactly as the game
        /// projects them (pass OriginalGameSession.Resolve); otherwise the HTML
        /// upgrade rule is applied (upgrade.name, else base name + "+").
        /// earnedUnlockIds are OriginalProfile.Finish(...)["newUnlocks"].
        /// </summary>
        public static RunSummary FromRun(JObject run, OriginalContentCatalog catalog, Func<JObject, JObject> resolveCard = null, IEnumerable<string> earnedUnlockIds = null)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            run = (JObject)run.DeepClone(); // never alias the caller's state
            if (!Enum.TryParse<OriginalRunPhase>((string)run["phase"], out var phase) || !IsTerminal(phase))
                throw new InvalidOperationException("A run summary requires a finished run (Victory or Defeat).");
            var victory = phase == OriginalRunPhase.Victory;
            var seed = (uint?)run["seed"] ?? 0u;
            var seedString = (string)run["seedString"];
            if (string.IsNullOrEmpty(seedString)) seedString = RandomStreams.DisplaySeed(seed);
            var classId = (string)run["classId"] ?? (string)run["class"];
            var className = (string)catalog.Record("classes", classId)["name"];
            var custom = run["customization"] as JObject;
            var heroName = (string)custom?["name"]; if (string.IsNullOrEmpty(heroName)) heroName = DefaultHeroName;
            var heroGlyph = (string)custom?["glyph"] ?? "";
            var stats = run["stats"] as JObject;
            int? floors = run["mapGraph"]?["floors"]?.Type == JTokenType.Integer ? (int?)run["mapGraph"]["floors"] : null;
            var hp = Int(run["hp"]); var maxHp = Int(run["maxHp"]);
            var deck = (run["deck"] as JArray ?? new JArray()).OfType<JObject>().Select(inst =>
            {
                var upgraded = (bool?)inst["upgraded"] == true;
                return new RunSummaryCard((string)inst["instanceId"], (string)inst["cardId"], ResolveCardName(inst, catalog, resolveCard), upgraded);
            });
            var bosses = (run["bossesBeaten"] as JArray ?? new JArray()).Values<string>();
            return new RunSummary(victory, seed, seedString, classId, className, heroName, heroGlyph,
                Int(run["actNumber"] ?? run["act"], 1), Int(run["floor"]), floors,
                Int(stats?["fightsWon"] ?? run["fightsWon"]), Int(stats?["damageDealt"]), Int(stats?["damageTaken"]),
                Int(run["cinders"]), victory ? hp : 0, maxHp, bosses, deck, Unlocks(catalog, earnedUnlockIds));
        }

        /// <summary>Build from a live session whose run has just ended.</summary>
        public static RunSummary FromSession(OriginalGameSession session, IEnumerable<string> earnedUnlockIds = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return FromRun(session.RunPlayer, session.Catalog, session.Resolve, earnedUnlockIds);
        }

        /// <summary>Copy with the "Earned" list replaced (after OriginalProfile.Finish).</summary>
        public RunSummary WithEarned(OriginalContentCatalog catalog, IEnumerable<string> earnedUnlockIds) =>
            new RunSummary(Victory, Seed, SeedString, ClassId, ClassName, HeroName, HeroGlyph, Act, Floor, FloorsInAct, FightsWon, DamageDealt, DamageTaken,
                Cinders, FinalHp, MaxHp, BossesBeaten, Deck, Unlocks(catalog, earnedUnlockIds));

        /// <summary>Fields shared with the run-history record (OriginalProfile.Finish / HTML runResult).</summary>
        public JObject ToRecord() => new JObject
        {
            ["victory"] = Victory, ["seed"] = SeedString, ["class"] = ClassId, ["className"] = ClassName, ["act"] = Act, ["floor"] = Floor,
            ["fightsWon"] = FightsWon, ["damageDealt"] = DamageDealt, ["damageTaken"] = DamageTaken, ["bosses"] = new JArray(BossesBeaten),
        };

        /// <summary>Plain-text rendering of the screen, top to bottom (logs, tests, fallback UI).</summary>
        public IReadOnlyList<string> Lines()
        {
            var lines = new List<string> { DoorEyebrow, DoorTitle, Title, CardEyebrow, CardName, CardLine, CardMeta };
            lines.AddRange(Stats.Select(s => s.Label + ": " + s.Value));
            if (HasEarned) { lines.Add(EarnedEyebrow); lines.AddRange(Earned.Select(u => u.Line)); }
            lines.Add(DeckEyebrow); lines.Add(DeckTitle);
            lines.AddRange(Deck.Select(c => c.Glyph + " " + c.Name));
            return lines.AsReadOnly();
        }

        private static IEnumerable<RunSummaryUnlock> Unlocks(OriginalContentCatalog catalog, IEnumerable<string> ids)
        {
            if (ids == null) return Enumerable.Empty<RunSummaryUnlock>();
            var rows = catalog.Table("unlocks").OfType<JObject>().ToArray();
            // HTML: fresh.map(id => unlocks.find(u => u.id === id)).filter(Boolean)
            return ids.Select(id => rows.FirstOrDefault(r => (string)r["id"] == id)).Where(r => r != null)
                .Select(r => new RunSummaryUnlock((string)r["id"], (string)r["name"], (string)r["kind"])).ToArray();
        }

        private static string ResolveCardName(JObject instance, OriginalContentCatalog catalog, Func<JObject, JObject> resolve)
        {
            if (resolve != null) { var resolved = resolve((JObject)instance.DeepClone()); if (!string.IsNullOrEmpty((string)resolved?["name"])) return (string)resolved["name"]; }
            var card = catalog.Record("cards", (string)instance["cardId"]);
            if ((bool?)instance["upgraded"] != true) return (string)card["name"];
            return (string)card["upgrade"]?["name"] ?? (string)card["name"] + "+";
        }

        private static int Int(JToken token, int fallback = 0) => token != null && token.Type == JTokenType.Integer ? (int)token : fallback;
    }
}
