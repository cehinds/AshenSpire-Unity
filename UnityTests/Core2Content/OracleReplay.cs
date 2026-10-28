// Replays the phase 5 and phase 7 JS oracle families (UnityTests/Core2Content/Oracles) through the engine-free Core 2 combat
// domain (Runtime/Domain/Core2/Combat) and compares every case with what the JS functions returned at the pin.
// The inputs are rebuilt exactly as tools/oracle-js-core2.mjs built them; the carrier and stance tables below are
// copied from that tool and must change with it.
using System.Globalization;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

static class OracleReplay
{
    /// <summary>One family's result: cases compared, cases that matched, and the first mismatches.</summary>
    public sealed class Result
    {
        public int Cases;
        public int Passed;
        public readonly List<string> Failures = new();
        /// <summary>Cases left out on purpose, each with its reason (counted apart from Cases).</summary>
        public readonly List<string> Excluded = new();
        public void Compare(bool ok, Func<string> describe)
        {
            Cases++;
            if (ok) Passed++;
            else if (Failures.Count < 25) Failures.Add(describe());
        }
    }

    // ---- tools/oracle-js-core2.mjs pricing carriers and stances ------------------------------------------------
    static readonly Dictionary<string, string> PricingCarriers = new()
    {
        ["physicalAttack"] = "{type:'attack',combatProfile:{camp:'physical',maneuver:'attack',range:'melee'},cardTags:[]}",
        ["physicalReaction"] = "{type:'attack',combatProfile:{camp:'physical',maneuver:'counter',range:'melee'},cardTags:['trait:reaction']}",
        ["physicalXCost"] = "{type:'attack',cost:'X',combatProfile:{camp:'physical',maneuver:'attack',range:'melee'},cardTags:[]}",
        ["spellCasting"] = "{type:'spell',combatProfile:{camp:'spell',maneuver:'casting',range:'ranged'},cardTags:[]}",
        ["spellReaction"] = "{type:'spell',combatProfile:{camp:'spell',maneuver:'counter',range:'melee'},cardTags:['trait:reaction']}",
        ["spellMirroredAttack"] = "{type:'spell',combatProfile:{camp:'spell',maneuver:'attack',mirrors:'attack',range:'melee'},cardTags:[]}",
    };
    static readonly Dictionary<string, string> PricingStances = new()
    {
        ["none"] = null,
        ["physicalAttack"] = "{camp:'physical',maneuver:'attack',range:'melee'}",
        ["physicalCounter"] = "{camp:'physical',maneuver:'counter',range:'melee'}",
        ["physicalDefend"] = "{camp:'physical',maneuver:'defend',range:'melee'}",
        ["spellCasting"] = "{camp:'spell',maneuver:'casting',range:'ranged'}",
        ["spellMirroredAttack"] = "{camp:'spell',maneuver:'attack',mirrored:true,range:'melee'}",
    };

    sealed class FixedDice : ICore2Dice
    {
        private readonly int _die;
        public FixedDice(int die) { _die = die; }
        public int Roll(string stream, int min, int max) => _die;
    }

    static Dictionary<string, int> Ints(JToken token) => token is JObject row
        ? row.Properties().ToDictionary(p => p.Name, p => (int)Math.Floor((double)p.Value), StringComparer.Ordinal)
        : new Dictionary<string, int>(StringComparer.Ordinal);

    static Core2Combatant Player(string classId, Core2Stance stance = null, bool unequipped = false) => new()
    {
        Id = "player", Side = Core2Side.Player, ClassId = classId, ClassUnequipped = unequipped, Stance = stance, Hp = 70, MaxHp = 70,
    };

    static string Show(JToken token) => token?.ToString(Newtonsoft.Json.Formatting.None) ?? "(missing)";

    /// <summary>A C# null string written into JSON is a null-valued string token; JS writes null. Normalize before comparing.</summary>
    static JToken Nulls(JToken token)
    {
        if (token is JValue v && v.Value == null && v.Type != JTokenType.Null) return JValue.CreateNull();
        // JS JSON writes a whole number without a fraction: 1.0 is 1.
        if (token is JValue f && f.Type == JTokenType.Float && (double)f == Math.Floor((double)f) && Math.Abs((double)f) < 9e15) return new JValue((long)(double)f);
        if (token is JObject o) foreach (var p in o.Properties().ToList()) p.Value = Nulls(p.Value);
        if (token is JArray a) for (var i = 0; i < a.Count; i++) a[i] = Nulls(a[i]);
        return token;
    }

    // ---- pricing ---------------------------------------------------------------------------------------------
    public static Result Pricing(Core2Content content, JArray cases)
    {
        var result = new Result();
        var ctx = Core2CombatContext.NewFight(content);
        // (class, card) pairs JS priced in their own stance: a card JS found no stance for has no in-stance row.
        var inStance = new HashSet<string>(cases.Where(c => (string)c["input"]["kind"] == "hand" && c["input"]["stance"]?.Type == JTokenType.Object)
            .Select(c => (string)c["input"]["classId"] + "|" + (string)c["input"]["cardId"]));
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            if ((string)input["kind"] == "rule")
            {
                var stanceJson = PricingStances[(string)input["stance"]];
                var player = Player((string)input["classId"], stanceJson == null ? null : Core2Stance.FromJson(JObject.Parse(stanceJson)), (bool)input["classUnequipped"]);
                var carrier = Core2Carrier.FromJson(JObject.Parse(PricingCarriers[(string)input["carrier"]]));
                ctx.Phase = (string)input["phase"];
                var baseCost = (int)input["base"];
                var sp = Core2Pricing.StanceStaminaCost(ctx, baseCost, player, carrier, 0);
                var spLane = Core2Pricing.StanceStaminaCost(ctx, baseCost, player, carrier, 2);
                var mp = Core2Pricing.StanceManaCost(ctx, baseCost, player, carrier);
                result.Compare(sp == (int)expected["sp"] && spLane == (int)expected["spWithMpLane"] && mp == (int)expected["mp"],
                    () => "pricing rule " + Show(input) + ": got sp " + sp + ", spWithMpLane " + spLane + ", mp " + mp + "; JS " + Show(expected));
            }
            else
            {
                ctx.Phase = "player";
                var card = Core2Carrier.FromCard(content.Card((string)input["cardId"]));
                var stance = Core2Stance.FromJson(input["stance"]);
                var player = Player((string)input["classId"], stance);
                player.Stamina = (int)input["stamina"]; player.Mana = (int)input["mana"];
                var own = Core2Stances.StanceOf(card, player);
                var identity = stance != null ? own != null && own.SameIdentity(stance)
                    : inStance.Contains((string)input["classId"] + "|" + (string)input["cardId"]) == (own != null);
                string got;
                bool ok;
                try
                {
                    var costs = Core2Pricing.CardCosts(ctx, player, card);
                    var want = expected["costs"];
                    ok = want != null && costs.Sp == (int)want["energy"] && costs.Sp == (int)want["stamina"] && costs.Mp == (int)want["mana"];
                    got = costs.ToString();
                }
                catch (Exception error) { ok = expected["error"] != null; got = "error " + error.Message; }
                result.Compare(ok && identity, () => "pricing hand " + Show(input) + ": got " + got + ", stance " + (own?.ToString() ?? "none") + "; JS " + Show(expected));
            }
        }
        return result;
    }

    // ---- hit chance ------------------------------------------------------------------------------------------
    static Core2Combatant HitTarget(int dexterity, int evade)
    {
        var enemy = new Core2Combatant
        {
            Id = "enemy", Side = Core2Side.Enemy, Hp = 50, MaxHp = 50, Stance = new Core2Stance("attack"),
            Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["dexterity"] = dexterity },
        };
        if (evade > 0) enemy.Statuses["evade"] = new Core2StatusInstance { Stacks = evade };
        return enemy;
    }

    public static Result HitChance(Core2Content content, JArray cases)
    {
        var result = new Result();
        var rules = Core2Rules.From(content);
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            if ((string)input["kind"] == "preview")
            {
                var attacker = (JObject)input["attacker"];
                var mode = (string)input["mode"];
                var source = Player("reaver");
                source.Attributes = Ints(attacker["attributes"]);
                source.Level = (int)attacker["level"];
                if ((int)attacker["precision"] > 0) source.Statuses["precision"] = new Core2StatusInstance { Stacks = (int)attacker["precision"] };
                source.AccuracyAdvantage = mode == "advantage"; source.AccuracyDisadvantage = mode == "disadvantage";
                var target = HitTarget((int)input["target"]["dexterity"], (int)input["target"]["evade"]);
                var carrier = Core2Carrier.FromJson(new JObject { ["combatProfile"] = input["carrier"] });
                var p = Core2Accuracy.Preview(rules, source, target, carrier);
                result.Compare(p.Accuracy == (int)expected["accuracy"] && p.Evasion == (int)expected["evasion"] && p.Need == (int)expected["need"]
                    && p.Mode == (string)expected["mode"] && p.InRange == (bool)expected["inRange"] && p.HitChance == (int)expected["hitChance"],
                    () => "hit preview " + Show(input) + ": got accuracy " + p.Accuracy + ", evasion " + p.Evasion + ", need " + p.Need + ", " + p.Mode + ", " + p.HitChance + "%; JS " + Show(expected));
            }
            else
            {
                var source = Player("reaver");
                source.Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["strength"] = 1, ["dexterity"] = 1, ["constitution"] = 1, ["wisdom"] = 1, ["intelligence"] = 1 };
                source.Level = 1;
                source.BasePrecisionBonus = (int)input["basePrecisionBonus"];
                var target = HitTarget((int)input["target"]["dexterity"], 0);
                var carrier = Core2Carrier.FromJson(JObject.Parse("{combatProfile:{camp:'physical',maneuver:'attack',range:'melee'}}"));
                var r = Core2Accuracy.Resolve(rules, source, target, carrier, new FixedDice((int)input["die"]));
                result.Compare(r.Preview.Accuracy == (int)expected["accuracy"] && r.Preview.Evasion == (int)expected["evasion"] && r.Hit == (bool)expected["hit"],
                    () => "hit roll " + Show(input) + ": got accuracy " + r.Preview.Accuracy + ", evasion " + r.Preview.Evasion + ", hit " + r.Hit + "; JS " + Show(expected));
            }
        }
        return result;
    }

    // ---- Block → Poise absorb ---------------------------------------------------------------------------------
    /// <summary>tools/oracle-js-core2.mjs strike(): one natural-20 hit from the Reaver into a 500 HP enemy, as applyAttackDamage reports it.</summary>
    static JObject Strike(Core2Content content, int amount, int block, int poise, int vulnerable = 0, string targetStance = null, string maneuver = "attack")
    {
        var ctx = Core2CombatContext.NewFight(content);
        var source = Player("reaver");
        source.Attributes = content.Class("reaver").Attributes.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var target = new Core2Combatant
        {
            Id = "enemy", Side = Core2Side.Enemy, Hp = 500, MaxHp = 500, Block = block,
            Poise = new Core2Pool(poise, Math.Max(poise, 20)), Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["dexterity"] = 0 },
            Ratings = new Dictionary<string, int>(StringComparer.Ordinal) { ["ar"] = 0, ["dr"] = 0, ["pr"] = 0, ["ward"] = 0, ["poise"] = 20 },
            Stance = targetStance == null ? null : new Core2Stance(targetStance, "physical", "melee"),
        };
        if (vulnerable > 0) target.Statuses["vulnerable"] = new Core2StatusInstance { Stacks = vulnerable };
        var carrier = Core2Carrier.FromJson(new JObject { ["skipRatingBonus"] = true, ["combatProfile"] = new JObject { ["camp"] = "physical", ["maneuver"] = maneuver, ["range"] = "melee" } });
        var hit = Core2Damage.ApplyAttack(ctx, source, target, amount, carrier, new FixedDice(20));
        var receipt = hit.Receipt;
        return new JObject
        {
            ["amount"] = receipt?.Amount ?? 0, ["blocked"] = receipt?.Blocked ?? 0, ["poiseAbsorbed"] = receipt?.PoiseAbsorbed ?? 0,
            ["poiseDamage"] = receipt?.PoiseDamage ?? 0, ["hpLoss"] = 500 - target.Hp, ["blockAfter"] = target.Block, ["poiseAfter"] = target.Poise.Value,
            ["targetStanceAfter"] = target.Stance?.Maneuver, ["vulnerableAfter"] = target.StatusStacks("vulnerable"),
        };
    }

    public static Result PoiseAbsorb(Core2Content content, JArray cases)
    {
        var result = new Result();
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            var got = Strike(content, (int)input["amount"], (int)input["block"], (int)input["poise"], (int)input["vulnerable"]);
            result.Compare(Show(got) == Show(expected), () => "poise " + Show(input) + ": got " + Show(got) + "; JS " + Show(expected));
        }
        return result;
    }

    // ---- matchups (phase 5b) -----------------------------------------------------------------------------------
    // "hit": an Attack/Smash/Sweep into each target stance (Smash vs Defend/Prepared, the advantaged-Smash interrupt,
    // defensive mitigation, Sweep's advantage over Counter). "stance": each player card's stance identity (stanceOf),
    // entry grants (stanceEntryBonuses), active ratings in that stance and the matching-stance ×1.25.
    public static Result Matchups(Core2Content content, JArray cases)
    {
        var result = new Result();
        var rules = Core2Rules.From(content);
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            if ((string)input["kind"] == "hit")
            {
                var got = Strike(content, (int)input["amount"], (int)input["block"], (int)input["poise"], 0, (string)input["targetStance"], (string)input["maneuver"]);
                result.Compare(Show(got) == Show(expected), () => "matchup " + Show(input) + ": got " + Show(got) + "; JS " + Show(expected));
                continue;
            }
            var carrier = Core2Carrier.FromCard(content.Card((string)input["cardId"]));
            var player = Player("reaver");
            var stance = Core2Stances.StanceOf(carrier, player);
            var want = Core2Stance.FromJson(expected["stance"]);
            var identity = stance == null ? want == null : stance.SameIdentity(want);
            var entry = Core2Stances.EntryBonuses(rules, carrier);
            var gotEntry = new JObject { ["precision"] = entry.Precision, ["evade"] = entry.Evade, ["block"] = entry.Block, ["barrier"] = entry.Barrier };
            var source = Player("reaver", stance);
            JToken gotRatings = stance == null ? JValue.CreateNull()
                : new JObject { ["ar"] = Core2Stances.StanceRatingBonus(rules, source, "ar"), ["pr"] = Core2Stances.StanceRatingBonus(rules, source, "pr"),
                    ["dr"] = Core2Stances.StanceRatingBonus(rules, source, "dr"), ["ward"] = Core2Stances.StanceRatingBonus(rules, source, "ward") };
            var effectiveness = stance == null ? 1 : Core2Stances.StanceEffectiveness(rules, source, carrier);
            result.Compare(identity && Show(gotEntry) == Show(expected["entry"]) && Show(gotRatings) == Show(expected["ratings"])
                && effectiveness == (double)expected["sameStanceEffectiveness"],
                () => "stance " + input["cardId"] + ": got " + (stance?.ToString() ?? "none") + ", entry " + Show(gotEntry) + ", ratings " + Show(gotRatings) + ", x" + effectiveness + "; JS " + Show(expected));
        }
        return result;
    }

    // ---- counter returns (phase 5b) ----------------------------------------------------------------------------
    public static Result CounterReturn(Core2Content content, JArray cases)
    {
        var result = new Result();
        var rules = Core2Rules.From(content);
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            if ((string)input["kind"] == "beats")
            {
                var beats = Core2Counters.Beats(rules, (string)input["maneuver"]);
                result.Compare(beats == (bool)expected["beats"], () => "counter beats " + input["maneuver"] + ": got " + beats + "; JS " + Show(expected));
                continue;
            }
            var r = Core2Counters.Return(rules, (int)input["incoming"], (bool)input["advantaged"]);
            var got = new JObject { ["hp"] = r.Hp, ["poise"] = r.Poise, ["block"] = r.Block };
            result.Compare(Show(got) == Show(expected), () => "counter return " + Show(input) + ": got " + Show(got) + "; JS " + Show(expected));
        }
        return result;
    }

    // ---- intent damage ranges ----------------------------------------------------------------------------------
    static Core2Combatant FromState(JObject row, string id)
    {
        var entity = new Core2Combatant
        {
            Id = id, Side = (string)row["kind"] == "enemy" ? Core2Side.Enemy : Core2Side.Player,
            Hp = (int?)row["hp"] ?? 1, MaxHp = (int?)row["hp"] ?? 1, Block = (int?)row["block"] ?? 0, Barrier = (int?)row["barrier"] ?? 0,
            Level = (int?)row["level"] ?? 1, HasLevelProfile = (bool?)row["levelProfile"] ?? true,
            Attributes = Ints(row["attributes"]), Ratings = Ints(row["ratings"]), Stance = Core2Stance.FromJson(row["stance"]),
            BasePrecisionBonus = (int?)row["basePrecisionBonus"] ?? 0, BaseEvasionBonus = (int?)row["baseEvasionBonus"] ?? 0,
            AccuracyAdvantage = (bool?)row["accuracyAdvantage"] ?? false, AccuracyDisadvantage = (bool?)row["accuracyDisadvantage"] ?? false,
        };
        if (row["poise"] is JObject poise) entity.Poise = new Core2Pool((int)poise["value"], (int)poise["max"]);
        if (row["ward"] is JObject ward) entity.Ward = new Core2Pool((int)ward["value"], (int)ward["max"]);
        foreach (var status in Ints(row["statuses"])) entity.Statuses[status.Key] = new Core2StatusInstance { Stacks = status.Value };
        return entity;
    }

    public static Result DamageRanges(Core2Content content, JArray cases)
    {
        var result = new Result();
        var rules = Core2Rules.From(content);
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            if (!(input["state"] is JObject state))
            {
                // No damage on the opening intent: JS shows no range.
                result.Compare(expected["projected"]?.Type == JTokenType.Null && expected["damage"]?.Type == JTokenType.Null,
                    () => "damage range " + Show(input) + ": no state but JS projected " + Show(expected["projected"]));
                continue;
            }
            var contact = state["contact"] as JObject;
            var proposal = (JObject)state["proposal"];
            var options = new Core2HitOptions
            {
                Contact = contact == null ? null : new Core2RangeContact((bool)contact["inRange"], (int)contact["precisionDelta"]),
                AccuracyDelta = (double)proposal["accuracyDelta"], Unavoidable = (bool)proposal["unavoidable"],
            };
            var plan = new Core2HitProposal
            {
                Allowed = (bool)proposal["allowed"], EvadeChance = (double)proposal["evadeChance"],
                PerceptionChance = proposal["perceptionChance"]?.Type == JTokenType.Null ? (double?)null : (double)proposal["perceptionChance"],
            };
            var range = Core2IntentProjection.Project(rules, FromState((JObject)state["attacker"], "enemy"), FromState((JObject)state["defender"], "player"),
                Core2Carrier.FromJson((JObject)state["carrier"]), (int)state["base"], (int)state["hits"], (int)state["damageEffects"], options, plan);
            var want = expected["projected"] as JObject;
            var got = new JObject { ["min"] = range.Min, ["max"] = range.Max, ["label"] = range.Label, ["uncertain"] = range.Uncertain, ["reason"] = range.Reason };
            result.Compare(want != null && Show(got) == Show(want), () => "damage range " + (string)input["enemyId"] + " block " + input["playerBlock"] + " stance " + Show(input["playerStance"]) + ": got " + Show(got) + "; JS " + Show(want));
        }
        return result;
    }

    // ---- enemy SP planning (phase 5c) ----------------------------------------------------------------------------
    /// <summary>
    /// Each enemy alone against the seed's starting player (data.starts): its SP pool, the plan it opened with, and the
    /// quote of every authored move, built by the engine-free fight exactly as JS createRunCombat built it.
    /// </summary>
    public static Result EnemyPlanning(Core2Content content, JObject data)
    {
        var result = new Result();
        foreach (var row in (JArray)data["cases"])
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            var seed = (int)input["seed"];
            var enemyId = (string)input["enemyId"];
            JObject got;
            try
            {
                var setup = Core2FightSetup.FromJson((JObject)data["starts"][seed.ToString(CultureInfo.InvariantCulture)]);
                var fight = Core2Fight.Create(content, setup, new List<string> { enemyId }, (uint)seed);
                var live = fight.Enemies[0];
                var quotes = new JObject();
                foreach (var move in content.Enemy(enemyId).Moves)
                {
                    var q = Core2EnemyMoves.Quote(fight.Ctx, fight.Range, live, fight.Player, move.Raw, move.Id);
                    quotes[move.Id] = new JObject { ["sp"] = q.Sp, ["advanceSp"] = q.AdvanceSp, ["totalSp"] = q.TotalSp, ["route"] = q.Route, ["available"] = q.Available };
                }
                got = new JObject
                {
                    ["maxStamina"] = live.MaxStamina, ["stamina"] = live.Stamina, ["intentMoveId"] = live.Intent?.MoveId,
                    ["plannedMoves"] = new JArray((live.PlannedMoves ?? new List<Core2PlannedMove>()).Select(m => m.MoveId)),
                    ["plan"] = new JArray(Core2EnemyPlanning.PlanActions(fight, live).Select(a => a.MoveId)), ["quotes"] = quotes,
                };
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(JToken.DeepEquals(got, expected), () => "enemy planning " + enemyId + " seed " + seed + ": " + FirstDifference(got, expected));
        }
        return result;
    }

    // ---- scripted fights (phase 5 complete) ----------------------------------------------------------------------
    /// <summary>
    /// Whole fights under the oracle's policy: reactions off; each turn play the first affordable card in hand order at
    /// the first living enemy (else untargeted), then End Turn. Compares every turn's snapshot, the event counts, every
    /// damage event and the final RNG counters.
    /// </summary>
    public static Result ScriptedFights(Core2Content content, JObject data)
    {
        var result = new Result();
        foreach (var row in (JArray)data["cases"])
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            JObject got;
            try { got = ScriptedFight(content, input).Expected; }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message }; }
            result.Compare(JToken.DeepEquals(got, expected), () => "scripted fight " + input["classId"] + " seed " + input["seed"] + ": " + FirstDifference(got, expected));
        }
        return result;
    }

    static (JObject Expected, Core2Fight Fight) ScriptedFight(Core2Content content, JObject input, int maxTurns = 30)
    {
        var fight = Core2Fight.Create(content, Core2FightSetup.FromJson((JObject)input["start"]), ((JArray)input["enemyIds"]).Select(t => (string)t).ToList(), (uint)(int)input["seed"]);
        fight.ReactionsEnabled = false;
        var turns = new JArray();
        for (var turn = 1; turn <= maxTurns && fight.Result == null; turn++)
        {
            var plays = new JArray();
            for (var guard = 0; guard < 20 && fight.Result == null; guard++)
            {
                var target = fight.Enemies.FirstOrDefault(e => e.Alive);
                ProbePreviews(fight, target);
                string played = null;
                foreach (var card in fight.Hand.ToList())
                {
                    Core2Costs costs;
                    try { costs = fight.CardCosts(card.InstanceId); } catch (InvalidOperationException) { continue; }
                    if (costs.Sp > fight.Player.Stamina || costs.Mp > fight.Player.Mana) continue;
                    foreach (var targetId in new[] { target?.Id, null })
                    {
                        try { fight.PlayCard(card.InstanceId, targetId); played = card.CardId; break; }
                        catch (InvalidOperationException) { }
                    }
                    if (played != null) break;
                }
                if (played == null) break;
                plays.Add(played);
            }
            var snapshot = new JObject
            {
                ["turn"] = turn, ["plays"] = plays,
                ["player"] = new JObject { ["hp"] = fight.Player.Hp, ["block"] = fight.Player.Block, ["stamina"] = fight.Player.Stamina, ["mana"] = fight.Player.Mana, ["stance"] = fight.Player.Stance?.Maneuver },
                ["enemies"] = new JArray(fight.Enemies.Select(e => new JObject
                {
                    ["enemyId"] = e.EnemyId, ["hp"] = e.Hp, ["block"] = e.Block, ["poise"] = e.Poise?.Value, ["stamina"] = e.Stamina,
                    ["intent"] = e.Intent?.MoveId, ["alive"] = e.Alive,
                })),
            };
            if (fight.Result == null) fight.EndTurn();
            snapshot["after"] = new JObject { ["playerHp"] = fight.Player.Hp, ["enemyHp"] = new JArray(fight.Enemies.Select(e => e.Hp)) };
            turns.Add(snapshot);
        }
        var counts = new JObject();
        foreach (var group in fight.Events.GroupBy(e => e.Type).OrderBy(g => g.Key, StringComparer.Ordinal)) counts[group.Key] = group.Count();
        var rng = new JObject();
        foreach (var pair in fight.Rng.Counters().OrderBy(p => p.Key, StringComparer.Ordinal)) rng[pair.Key] = pair.Value;
        return (new JObject
        {
            ["result"] = fight.Result, ["turns"] = turns, ["eventCounts"] = counts,
            ["damageDealt"] = new JArray(fight.Events.Where(e => e.Type == "damageDealt").Select(e => new JObject
            {
                ["source"] = e.Data["sourceId"], ["target"] = e.Data["targetId"], ["amount"] = e.Data["amount"], ["blocked"] = (int?)e.Data["blocked"] ?? 0,
            })),
            ["rngCounters"] = rng,
        }, fight);
    }

    // ---- reaction fights (phase 7) -------------------------------------------------------------------------------

    static readonly Dictionary<string, string[]> ReactionKeys = new()
    {
        ["combatReactionOffered"] = new[] { "offerId", "ownerId", "sourceId" }, ["combatReactionChosen"] = new[] { "offerId", "ownerId", "cardInstanceId" },
        ["combatReactionStacked"] = new[] { "offerId", "ownerId", "cardInstanceId", "depth" }, ["combatReactionFizzled"] = new[] { "ownerId", "cardInstanceId", "reason", "side", "moveId" },
        ["combatReactionUnavailable"] = new[] { "ownerId", "sourceId", "reason", "cardId", "cardIds", "attackKind", "resource", "need", "have" },
        ["combatEnemyReactionStacked"] = new[] { "sourceId", "enemyId", "moveId", "targetPlayerId", "groupKey", "depth" }, ["enemyMoveStarted"] = new[] { "sourceId", "moveId", "kind", "reaction", "spCost", "totalSp" },
        ["combatCounterArmed"] = new[] { "sourceKind", "sourceId", "cardId", "moveId", "mode" }, ["combatCounterConsumed"] = new[] { "sourceId", "targetId", "serial", "cardId" },
        ["combatCounterTriggered"] = new[] { "sourceId", "targetId", "amount", "poiseDamage", "cardId" }, ["combatContactPrepared"] = new[] { "sourceId", "contactKey", "contactIndex", "contactCount" },
        ["combatRangeReturnUnavailable"] = new[] { "sourceId", "targetId", "reason" }, ["cardPlayed"] = new[] { "cardInstanceId", "cardId", "targetId", "energySpent", "manaSpent", "staminaSpent" },
        ["enemyMoveUnavailable"] = new[] { "sourceId", "moveId", "required", "available" },
    };

    public static Result ReactionFights(Core2Content content, JObject data)
    {
        var result = new Result();
        foreach (var row in (JArray)data["cases"])
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            JObject got;
            try { got = (JObject)Nulls(ReactionFight(content, input).Expected); }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault()?.Trim() }; }
            result.Compare(JToken.DeepEquals(got, expected), () => "reaction fight " + input["classId"] + " seed " + input["seed"] + " " + input["policy"] + ": "
                + (got["eventTypes"] != null && !JToken.DeepEquals(got["eventTypes"], expected["eventTypes"]) ? FirstDifference(got["eventTypes"], expected["eventTypes"], "$.eventTypes") : FirstDifference(got, expected)));
        }
        return result;
    }

    /// <summary>
    /// fuzz-fights: every Core 2 enemy fought with reactions off and on, driven exactly as reaction-fights (ReactionFight),
    /// each compared as the digest tools/oracle-js-core2.mjs fuzzDigest writes. The player start is shared by class:seed.
    /// Excluded cases (none at the pin) would be listed in <see cref="FuzzExclusions"/> with their reason.
    /// </summary>
    public static Result FuzzFights(Core2Content content, JObject data)
    {
        var result = new Result();
        foreach (var row in (JArray)data["cases"])
        {
            var input = (JObject)row["input"].DeepClone();
            var expected = (JObject)row["expected"];
            var label = "fuzz fight " + input["enemyIds"]?[0] + " " + input["classId"] + " seed " + input["seed"] + " " + input["policy"];
            if (FuzzExclusions.TryGetValue(label, out var reason)) { result.Excluded.Add(label + ": " + reason); continue; }
            JObject got;
            try
            {
                input["start"] = data["starts"][(string)input["start"]].DeepClone();
                got = FuzzDigest(ReactionFight(content, input).Expected);
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault()?.Trim() }; }
            result.Compare(JToken.DeepEquals(got, expected), () => label + ": " + FirstDifference(got, expected));
        }
        return result;
    }

    /// <summary>fuzz-fights cases left out on purpose ("fuzz fight &lt;enemy&gt; &lt;class&gt; seed &lt;n&gt; &lt;policy&gt;" → why). None at the pin.</summary>
    public static readonly Dictionary<string, string> FuzzExclusions = new();

    /// <summary>tools/oracle-js-core2.mjs fuzzDigest over a ReactionFight result.</summary>
    static JObject FuzzDigest(JObject e)
    {
        static string S(JToken t) => t == null || t.Type == JTokenType.Null ? "-" : t.Type == JTokenType.Boolean ? ((bool)t ? "true" : "false")
            : t.Type == JTokenType.Float ? ((double)t).ToString("R", CultureInfo.InvariantCulture) : t.Type == JTokenType.Integer ? ((long)t).ToString(CultureInfo.InvariantCulture) : (string)t ?? "-";
        static string Join(IEnumerable<string> parts, string empty = "-") { var text = string.Join(",", parts); return text.Length == 0 ? empty : text; }
        var turns = (JArray)e["turns"];
        var sha = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((string)e["eventTypes"]));
        return new JObject
        {
            ["result"] = e["result"]?.DeepClone(), ["turns"] = turns.Count,
            ["log"] = new JArray(turns.Select(t =>
            {
                var p = t["player"]; var after = t["after"];
                return S(t["turn"]) + " " + Join(t["plays"].Select(S)) + " | " + S(p["hp"]) + "/" + S(p["block"]) + "/" + S(p["stamina"]) + "/" + S(p["mana"]) + "/" + S(p["stance"]) + " | "
                    + string.Join(";", t["enemies"].Select(x => S(x["hp"]) + "/" + S(x["block"]) + "/" + S(x["poise"]) + "/" + S(x["stamina"]) + "/" + S(x["intent"]) + "/" + ((bool)x["alive"] ? "1" : "0"))) + " | "
                    + S(after["playerHp"]) + "/" + string.Join(",", after["enemyHp"].Select(S)) + " | " + Join((after["hand"] ?? new JArray()).Select(S));
            })),
            ["offers"] = new JArray(e["offers"].Select(o => S(o["turn"]) + " " + S(o["sourceId"]) + " "
                + Join(o["options"].Select(x => S(x["cardId"]) + ":" + S(x["staminaCost"]) + "/" + S(x["manaCost"]))) + " > " + S(o["answer"]))),
            ["damage"] = string.Join(" ", e["damageDealt"].Select(d => S(d["source"]) + ">" + S(d["target"]) + " " + S(d["amount"]) + "/" + S(d["blocked"]))),
            ["eventCounts"] = e["eventCounts"].DeepClone(), ["eventOrder"] = Convert.ToHexString(sha).ToLowerInvariant().Substring(0, 24), ["rngCounters"] = e["rngCounters"].DeepClone(),
        };
    }

    /// <summary>
    /// The combat log of the fights the reaction-fights and scripted-fights families drive: each entry (event, round, text,
    /// named actors) and the drawn view in full detail with the legend, against JS combatLogEntries and renderCombatLogEntries.
    /// </summary>
    public static Result CombatLog(Core2Content content, JObject data)
    {
        var result = new Result();
        var sources = new Dictionary<string, JArray>();
        JObject FightInput(string family, int index)
        {
            if (!sources.TryGetValue(family, out var cases)) sources[family] = cases = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(OraclesFolder, family + ".json")))["cases"];
            return (JObject)cases[index]["input"];
        }
        foreach (var row in (JArray)data["cases"])
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            JObject got;
            try
            {
                var family = (string)input["fight"]; var index = (int)input["index"];
                var fight = family == "reaction-fights" ? ReactionFight(content, FightInput(family, index)).Fight : ScriptedFight(content, FightInput(family, index)).Fight;
                var entries = Core2CombatLog.Entries(content, fight.Events, fight.Enemies);
                got = new JObject
                {
                    ["entries"] = new JArray(entries.Select(e => new JObject
                    {
                        ["event"] = e.EventIndex, ["round"] = e.Round, ["text"] = e.Text,
                        ["sides"] = new JArray(e.Segments.Where(s => s.Side != null).Select(s => s.Side + ":" + s.ActorId)),
                    })),
                    ["view"] = new JArray(Core2CombatLog.View(content, entries, detailed: true, legend: true).Select(v => v.Kind + " " + v.Text)),
                };
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault()?.Trim() }; }
            result.Compare(JToken.DeepEquals(got, expected), () => "combat log " + input["fight"] + "#" + input["index"] + ": " + FirstDifference(got, expected));
        }
        return result;
    }

    /// <summary>The oracle folder (Program.cs runs from the repository root).</summary>
    public static string OraclesFolder = "UnityTests/Core2Content/Oracles";

    static Core2PerceivedIntent PerceivedIntent(JToken intent) => intent is JObject row
        ? new Core2PerceivedIntent { Hidden = (bool?)row["hidden"] ?? false, Revealed = row["revealed"]?.Type != JTokenType.Boolean || (bool)row["revealed"],
            Maneuver = (string)row["profile"]?["maneuver"], Stance = (string)row["stance"] }
        : null;

    /// <summary>Auto's chooser, the pacing settings and pace, and the combat log's heights and palette (combat-automation family).</summary>
    public static Result CombatAutomation(Core2Content content, JArray cases)
    {
        var result = new Result();
        var pacing = new Core2CombatPacing(content);
        var sizes = Core2CombatLog.Sizes(content);
        foreach (var row in cases)
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            JObject got;
            switch ((string)input["kind"])
            {
                case "choose":
                {
                    var options = (JArray)input["options"];
                    var picks = ((JObject)input["picks"]).Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal);
                    var index = Core2AutoCombat.Choose(content.Profile["modules"]["stances"]["matchups"].ToObject<Dictionary<string, List<string>>>()
                        .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value), PerceivedIntent(input["intent"]), (string)input["lastStance"], picks,
                        options.Select(o => (string)o["kind"]).ToList());
                    got = new JObject { ["pick"] = index < 0 ? null : (string)options[index]["id"] };
                    break;
                }
                case "perceived": got = new JObject { ["maneuver"] = Core2AutoCombat.PerceivedManeuver(PerceivedIntent(input["intent"])) }; break;
                case "resolve":
                {
                    var a = pacing.Resolve((JObject)input["settings"]);
                    got = new JObject { ["enabled"] = a.Enabled, ["speed"] = a.Speed, ["preference"] = a.Preference, ["autoSpeed"] = a.AutoSpeed, ["animationMs"] = a.AnimationMs, ["delayMs"] = a.DelayMs };
                    break;
                }
                case "pace":
                {
                    var p = pacing.Pace((string)input["animSpeed"], (JObject)input["settings"], (bool)input["reduced"]);
                    got = new JObject { ["gapMs"] = p.GapMs, ["stepMs"] = p.StepMs, ["bannerMs"] = p.BannerMs, ["scale"] = p.Scale, ["lungeMs"] = p.SpriteSpeed["lungeMs"], ["beatMs"] = p.SpriteSpeed["beatMs"] };
                    break;
                }
                case "change": got = pacing.Changes((string)input["key"], input["value"]); break;
                case "logHeights":
                {
                    var space = (JObject)input["space"];
                    var gap = (double?)space["gap"] ?? 8;
                    var heights = sizes.ToDictionary(s => s, s => Core2CombatLog.Height(s, (double)space["cardHeight"], (double)space["viewportHeight"], (double)space["dockTop"], (double)space["menuBottom"], gap), StringComparer.Ordinal);
                    var probes = new[] { 0, heights["Small"], (heights["Small"] + heights["Medium"]) / 2 + 1, heights["Medium"], heights["Large"] + 50 };
                    got = new JObject
                    {
                        ["heights"] = new JObject(sizes.Select(s => new JProperty(s, heights[s]))), ["snaps"] = new JArray(probes.Select(h => Core2CombatLog.SnapSize(h, heights, sizes))),
                        ["dragScale"] = Core2CombatLog.DragScale(heights, (double)space["viewportHeight"], (double)space["dockTop"]),
                    };
                    break;
                }
                case "palette":
                {
                    var settings = (JObject)input["settings"];
                    var palette = Core2CombatLog.Palette(content, (string)settings["combatLogPlayerColor"] ?? (string)settings["player"], (string)settings["combatLogEnemyColor"] ?? (string)settings["enemy"]);
                    got = new JObject { ["player"] = palette.Player, ["enemy"] = palette.Enemy };
                    break;
                }
                default: got = new JObject { ["error"] = "unknown kind " + input["kind"] }; break;
            }
            got = (JObject)Nulls(got);
            result.Compare(JToken.DeepEquals(got, expected), () => "automation " + Show(input) + ": " + FirstDifference(got, expected));
        }
        return result;
    }

    /// <summary>
    /// A whole fight with reactions on, driven as tools/oracle-js-core2.mjs reactive() drives the JS one: hold trait:reaction
    /// cards on the player turn, play the first affordable other card, End Turn; answer each offer by the policy.
    /// </summary>
    /// <summary>
    /// content-coverage: short fights carrying the relics or playing the cards the content-coverage port added, driven as
    /// ReactionFight drives a fight plus the coverage policy (a maneuver choice plays input.choice; a chosen-card selection
    /// takes the first candidates), compared as the fuzz digest plus the choices made and the piles, cinders and statuses at
    /// the end.
    /// </summary>
    public static Result ContentCoverage(Core2Content content, JObject data)
    {
        var result = new Result();
        foreach (var row in (JArray)data["cases"])
        {
            var input = (JObject)row["input"];
            var expected = (JObject)row["expected"];
            var label = "content coverage " + input["classId"] + " seed " + input["seed"] + " vs " + string.Join("+", input["enemyIds"].Select(t => (string)t))
                + " relics [" + string.Join(",", (input["relics"] ?? new JArray()).Select(t => (string)t)) + "]";
            JObject got;
            try
            {
                var choices = new JArray();
                var run = ReactionFight(content, input, 12, choices);
                got = FuzzDigest(run.Expected);
                got["choices"] = choices;
                var f = run.Fight;
                // A parity trace for one case (CORE2_TRACE=<seed>): every event's type and data, one per line.
                if (Environment.GetEnvironmentVariable("CORE2_TRACE") == (string)input["seed"])
                    File.WriteAllLines(Environment.GetEnvironmentVariable("CORE2_TRACE_FILE") ?? "core2-trace.txt", f.Events.Select(e => e.Type + " " + e.Data.ToString(Newtonsoft.Json.Formatting.None)));
                string Statuses(Core2Combatant e) { var s = string.Join(",", e.Statuses.Where(p => p.Value.Stacks > 0).Select(p => p.Key + ":" + p.Value.Stacks).OrderBy(x => x, StringComparer.Ordinal)); return s.Length == 0 ? "-" : s; }
                var end = new JArray("piles " + f.Draw.Count + "/" + f.Hand.Count + "/" + f.Discard.Count + "/" + f.Exhaust.Count, "cinders " + f.RewardCinders, "player " + Statuses(f.Player));
                foreach (var e in f.Enemies) end.Add(e.Id + " " + Statuses(e));
                got["end"] = end;
                got = (JObject)Nulls(got);
            }
            catch (Exception error) { got = new JObject { ["error"] = error.GetType().Name + ": " + error.Message + " " + error.StackTrace?.Split('\n').FirstOrDefault()?.Trim() }; }
            result.Compare(JToken.DeepEquals(got, expected), () => label + ": " + FirstDifference(got, expected));
        }
        return result;
    }

    /// <summary>Preview purity: every CheckPlay / hit preview / intent projection the combat screen makes; a leak is one that changed the live fight.</summary>
    public static int PreviewProbes, PreviewLeaks;
    public static readonly List<string> PreviewLeakNotes = new List<string>();
    private static string FightSignature(Core2Fight f)
    {
        string Cards(IEnumerable<Core2CardInstance> cards) => string.Join(",", cards.Select(c => c.CardId + "#" + c.InstanceId + (c.Changes == null ? "" : c.Changes.ToString(Newtonsoft.Json.Formatting.None))));
        string Entity(Core2Combatant e) => e.Id + " hp" + e.Hp + " sp" + e.Stamina + " mp" + e.Mana + " b" + e.Block + " "
            + string.Join(",", e.Statuses.Where(p => p.Value.Stacks != 0).Select(p => p.Key + ":" + p.Value.Stacks).OrderBy(x => x, StringComparer.Ordinal))
            + " riders" + Newtonsoft.Json.JsonConvert.SerializeObject(e.AbilityRiders)
            + " reveal" + string.Join(",", e.IntentRevealedUntil.Select(p => p.Key + ":" + p.Value))
            + " burdens" + (e.BurdenPiles == null ? "-" : Cards(e.BurdenPiles.Draw) + "|" + Cards(e.BurdenPiles.Hand) + "|" + Cards(e.BurdenPiles.Discard) + "|" + Cards(e.BurdenPiles.Exhaust));
        return string.Join("\n", new[] { Cards(f.Draw), Cards(f.Hand), Cards(f.Discard), Cards(f.Exhaust), Entity(f.Player) }
            .Concat(f.Enemies.Select(Entity)).Concat(f.Rng.Counters().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)));
    }
    private static void ProbePreviews(Core2Fight fight, Core2Combatant target)
    {
        if (fight.PendingReaction != null || fight.PendingCardChoice != null) return;
        var before = FightSignature(fight);
        foreach (var card in fight.Hand.ToList())
        {
            foreach (var targetId in new[] { target?.Id, null }) fight.CheckPlay(card.InstanceId, targetId);
            if (target != null) fight.PreviewCardHit(card.InstanceId, target.Id);
        }
        foreach (var enemy in fight.Enemies.Where(e => e.Alive)) { fight.ProjectIntent(enemy); fight.IntentHitDamages(enemy); }
        PreviewProbes++;
        var after = FightSignature(fight);
        if (after == before) return;
        PreviewLeaks++;
        if (PreviewLeakNotes.Count < 3)
        {
            var b = before.Split('\n'); var a = after.Split('\n');
            var i = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(k => a[k] != b[k]);
            PreviewLeakNotes.Add("turn " + fight.Turn + ": " + b[i] + "  ->  " + a[i]);
        }
    }

    public static (JObject Expected, Core2Fight Fight) ReactionFight(Core2Content content, JObject input, int maxTurns = 30, JArray choices = null)
    {
        var fight = Core2Fight.Create(content, Core2FightSetup.FromJson((JObject)input["start"]), ((JArray)input["enemyIds"]).Select(t => (string)t).ToList(), (uint)(int)input["seed"]);
        var policy = (string)input["policy"];
        if (policy == "off") fight.SetReactions(false);
        var offers = new JArray();
        var turns = new JArray();
        var turn = 0;
        void Settle()
        {
            for (var i = 0; (fight.PendingReaction != null || fight.PendingCardChoice != null) && i < 50; i++)
            {
                if (fight.PendingCardChoice != null)
                {
                    // content-coverage: a chosen-card selection takes the first candidates in pile order.
                    var choice = fight.PendingCardChoice;
                    var ids = choice.CandidateIds.Take(choice.Count).ToList();
                    choices?.Add(turn + " " + choice.CardId + " " + choice.Effect["op"] + " " + choice.Effect["from"] + ">" + ((string)choice.Effect["to"] ?? "-") + " " + string.Join(",", ids));
                    fight.ChooseCards(ids);
                    continue;
                }
                var pending = fight.PendingReaction;
                var options = new JArray(pending.Options.Select(o =>
                {
                    var play = new JObject { ["cardInstanceId"] = o.Play.CardInstanceId };
                    if (o.Play.TargetId != null) play["targetId"] = o.Play.TargetId;
                    if (o.Play.RangeRoute != null) play["rangeRoute"] = o.Play.RangeRoute;
                    var option = new JObject { ["id"] = o.Id, ["cardId"] = o.CardId, ["staminaCost"] = o.StaminaCost, ["manaCost"] = o.ManaCost, ["play"] = play };
                    if (o.SourceId != null) option["sourceId"] = o.SourceId;
                    return option;
                }));
                string optionId = null;
                JObject auto = null;
                if (policy == "first") optionId = pending.Options.FirstOrDefault()?.Id;
                else if (policy == "auto")
                {
                    var inputs = Core2AutoCombat.Inputs(fight);
                    optionId = Core2AutoCombat.Answer(fight);
                    auto = new JObject
                    {
                        ["intent"] = inputs.Intent == null ? JValue.CreateNull() : new JObject { ["hidden"] = inputs.Intent.Hidden, ["revealed"] = inputs.Intent.Revealed, ["maneuver"] = inputs.Intent.Maneuver, ["stance"] = inputs.Intent.Stance },
                        ["perceived"] = inputs.Perceived, ["lastStance"] = inputs.LastStance, ["kinds"] = new JArray(inputs.Kinds),
                    };
                }
                var offer = new JObject { ["turn"] = turn, ["offerId"] = pending.Id, ["sourceId"] = pending.SourceId, ["options"] = options, ["answer"] = optionId };
                if (auto != null) offer["auto"] = auto;
                offers.Add(offer);
                fight.ChooseReaction(pending.Id, optionId);
            }
        }
        bool IsReaction(string cardId) => content.Card(cardId).Tags.Contains("trait:reaction");
        for (turn = 1; turn <= maxTurns && fight.Result == null; turn++)
        {
            Settle();
            var plays = new JArray();
            for (var guard = 0; guard < 20 && fight.Result == null; guard++)
            {
                var target = fight.Enemies.FirstOrDefault(e => e.Alive);
                ProbePreviews(fight, target);
                string played = null;
                foreach (var card in fight.Hand.ToList())
                {
                    if (IsReaction(card.CardId)) continue;
                    Core2Costs costs;
                    try { costs = fight.CardCosts(card.InstanceId); } catch (InvalidOperationException) { continue; }
                    if (costs.Sp > fight.Player.Stamina || costs.Mp > fight.Player.Mana) continue;
                    var choice = input["choice"] != null && fight.CardChoiceOptions(card.InstanceId) != null ? (string)input["choice"] : null;
                    foreach (var targetId in new[] { target?.Id, null })
                    {
                        try { fight.PlayCard(card.InstanceId, targetId, choice); played = card.CardId; break; }
                        catch (InvalidOperationException) { }
                    }
                    if (played != null) break;
                }
                if (played == null) break;
                plays.Add(played);
                Settle();
            }
            var snapshot = new JObject
            {
                ["turn"] = turn, ["plays"] = plays,
                ["player"] = new JObject { ["hp"] = fight.Player.Hp, ["block"] = fight.Player.Block, ["stamina"] = fight.Player.Stamina, ["mana"] = fight.Player.Mana, ["stance"] = fight.Player.Stance?.Maneuver },
                ["enemies"] = new JArray(fight.Enemies.Select(e => new JObject
                {
                    ["enemyId"] = e.EnemyId, ["hp"] = e.Hp, ["block"] = e.Block, ["poise"] = e.Poise?.Value, ["stamina"] = e.Stamina, ["intent"] = e.Intent?.MoveId, ["alive"] = e.Alive,
                })),
            };
            if (fight.Result == null) { fight.EndTurn(); Settle(); }
            snapshot["after"] = new JObject { ["playerHp"] = fight.Player.Hp, ["enemyHp"] = new JArray(fight.Enemies.Select(e => e.Hp)), ["hand"] = new JArray(fight.Hand.Select(c => c.CardId)) };
            turns.Add(snapshot);
        }
        var counts = new JObject();
        foreach (var group in fight.Events.GroupBy(e => e.Type).OrderBy(g => g.Key, StringComparer.Ordinal)) counts[group.Key] = group.Count();
        var rng = new JObject();
        foreach (var pair in fight.Rng.Counters().OrderBy(p => p.Key, StringComparer.Ordinal)) rng[pair.Key] = pair.Value;
        var reactionEvents = new JArray();
        foreach (var e in fight.Events)
        {
            if (!ReactionKeys.TryGetValue(e.Type, out var keys)) continue;
            var picked = new JObject { ["type"] = e.Type };
            foreach (var key in keys) if (e.Data.ContainsKey(key)) picked[key] = e.Data[key];
            reactionEvents.Add(picked);
        }
        var expected = new JObject
        {
            ["result"] = fight.Result, ["turns"] = turns, ["offers"] = offers, ["eventTypes"] = string.Join(" ", fight.Events.Select(e => e.Type)), ["eventCounts"] = counts,
            ["reactionEvents"] = reactionEvents,
            ["damageDealt"] = new JArray(fight.Events.Where(e => e.Type == "damageDealt").Select(e => new JObject
            {
                ["source"] = e.Data["sourceId"], ["target"] = e.Data["targetId"], ["amount"] = e.Data["amount"], ["blocked"] = (int?)e.Data["blocked"] ?? 0,
            })),
            ["rngCounters"] = rng,
        };
        return (expected, fight);
    }

    /// <summary>The first path where two JSON values differ, with both sides (a readable fight mismatch).</summary>
    static string FirstDifference(JToken got, JToken want, string path = "$")
    {
        if (got is JObject a && want is JObject b)
        {
            foreach (var name in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)))
                if (!JToken.DeepEquals(a[name], b[name])) return FirstDifference(a[name], b[name], path + "." + name);
        }
        if (got is JArray x && want is JArray y)
        {
            for (var i = 0; i < Math.Min(x.Count, y.Count); i++)
                if (!JToken.DeepEquals(x[i], y[i])) return FirstDifference(x[i], y[i], path + "[" + i + "]");
            if (x.Count != y.Count) return path + ": got " + x.Count + " items; JS " + y.Count;
        }
        if (got?.Type == JTokenType.String && want?.Type == JTokenType.String && ((string)got).Length > 200)
        {
            // A long space-separated sequence (an event order): the first differing token, with context.
            var g = ((string)got).Split(' '); var w = ((string)want).Split(' ');
            var at = 0; while (at < g.Length && at < w.Length && g[at] == w[at]) at++;
            string Around(string[] s) => string.Join(" ", s.Skip(Math.Max(0, at - 4)).Take(10));
            return path + " token " + at + ": got ..." + Around(g) + "...; JS ..." + Around(w) + "...";
        }
        return path + ": got " + Show(got) + "; JS " + Show(want);
    }
}
