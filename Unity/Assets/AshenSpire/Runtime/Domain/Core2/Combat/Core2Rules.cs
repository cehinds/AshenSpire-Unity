// Core2Rules.cs — the FixCore rule values a Core 2 fight reads, typed from the export (SPEC.md §0.3, §0.9 phase 5a).
//
// Sources at the SPEC §0.2 pin:
//   content/source/fixCoreMechanics.json (exported as mechanics.json fixCoreMechanics) — every FixCore number;
//   src/content/stanceRules.js (mechanics.json stanceRules) — stance ratings and entry grants;
//   bundles/core/bundle.json modules (profile.json modules) — stances, defenses, effects, resources, cards;
//   engine/mechanics/defense.poiseProtection/mechanic.json — the Poise multiplier default (0.75), used because
//   fixCoreMechanics.json protectionParameters is empty at the pin;
//   legacy-adapter statuses (adapter/statuses.json) with the FixCore overrides (src/model/fixCoreMechanics.js
//   fixCoreStatusDefinition: weak, vulnerable and staggered stack by adding);
//   src/content/combatRules.js (mechanics.json damageTypes) — the damage-type, school and magic-split table magicShareOf
//   reads (phase 5b); combatExpansionMatchups.counter (mechanics.json counterRules) — the Counter coverage defaults;
//   fixCoreMechanics.json counter / range — the Counter return fractions and the range policy (phase 5b).
// No value is invented here: a missing key falls back only where the JS reader itself has a default, and the
// fallback cites it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>How a status's stacks fall off (JS src/engine/statuses.js decayAtTurnStart / isDurationDecay).</summary>
    public enum Core2StatusDecay { None, PerTurnEnd, Duration, OnConsume }

    public sealed class Core2StatusDefinition
    {
        public string Id { get; }
        /// <summary>add, refresh or unique (JS applyStatusRaw stackMode switch).</summary>
        public string StackMode { get; }
        public Core2StatusDecay Decay { get; }
        /// <summary>Turns a duration status lasts after each application (JS def.decay.duration).</summary>
        public int Duration { get; }

        public Core2StatusDefinition(string id, string stackMode, Core2StatusDecay decay, int duration)
        {
            Id = id; StackMode = stackMode; Decay = decay; Duration = duration;
            if (stackMode != "add" && stackMode != "refresh" && stackMode != "unique")
                throw new InvalidDataException("status " + id + ": unknown stackMode '" + stackMode + "' (JS statuses.js applyStatusRaw)");
        }
    }

    public sealed class Core2Rules
    {
        // ---- fixCoreMechanics.json ------------------------------------------------------------------
        public bool FixCoreEnabled { get; }
        public int FixCoreVersion { get; }
        /// <summary>fixCoreMechanics.ward.enabled; missing means on (JS fixCoreWardEnabled). Off in Core 2.</summary>
        public bool WardEnabled { get; }
        public double StanceEffectivenessMultiplier { get; }
        public int StanceDiscount { get; }
        public int StanceMinimumCost { get; }
        /// <summary>reaction.handCostDiscount (JS FIXCORE_REACTION_DEFAULTS 1): the pre-Core 2 action-policy reaction discount.</summary>
        public int ReactionHandCostDiscount { get; }
        public double BaseAC { get; }
        public double EvadeStackBase { get; }
        public double EvadeStackDex { get; }
        public double PrecisionBase { get; }
        public double EvasionBase { get; }
        public double PrecisionWeightDex { get; }
        public double PrecisionWeightStr { get; }
        public double PrecisionWeightWis { get; }
        public double PrecisionWeightInt { get; }
        public double PrecisionWeightLevel { get; }
        public IReadOnlyDictionary<string, double> ManeuverPrecision { get; }
        public int StatusDecayAmount { get; }
        public double WeakARMultiplier { get; }
        public double VulnerabilityMultiplier { get; }
        public bool StaggeredStance { get; }
        public int StaggeredStaminaLoss { get; }
        public int StaggeredVulnerableStacks { get; }
        public double SmashDamageMultiplier { get; }
        public double SmashBlockFraction { get; }
        public double SmashPoiseFraction { get; }
        /// <summary>defense.poiseProtection multiplier: an absorbing Poise pool lets floor(x × this) of a physical hit through.</summary>
        public double PoiseProtectionMultiplier { get; }
        // fixCoreMechanics.counter (JS FIXCORE_COUNTER_DEFAULTS fill a missing key; fixCoreCounterRules).
        public double CounterReturnFraction { get; }
        public double CounterPoiseFraction { get; }
        public double CounterAdvantageIncomingFraction { get; }
        public double CounterAdvantagePoiseFraction { get; }
        public double CounterAdvantageBlockFraction { get; }
        /// <summary>fixCoreMechanics.range (JS snapshotRangePolicy over RANGE_POLICY_DEFAULTS).</summary>
        public Core2RangePolicy RangePolicy { get; }

        // ---- stanceRules (src/content/stanceRules.js) ---------------------------------------------
        public int OffensiveRating { get; }
        public int DefensiveRating { get; }
        public int OffensivePrecision { get; }
        public int EvadeStacks { get; }
        public int EntryProtection { get; }
        /// <summary>stanceRules.spellBlockShare / schoolBlockShares: a spell-camp entry protection's Block part (JS stanceEntryBonuses).</summary>
        public double SpellBlockShare { get; }
        public IReadOnlyDictionary<string, double> SchoolBlockShares { get; }
        /// <summary>stanceRules.defenseRestore. Read only by the non-configured (expansion) Defend path; a Core 2 fight never applies it (Core2Stances.EnterStance).</summary>
        public int DefenseRestore { get; }
        /// <summary>stanceRules.fallbackPoise: the Poise an un-authored Counter reply or enemy Smash carries.</summary>
        public int FallbackPoise { get; }
        /// <summary>stanceRules.sweepReactionPoise / sweepReactionWard: a reactive Sweep's riders against a melee physical attacker.</summary>
        public int SweepReactionPoise { get; }
        public int SweepReactionWard { get; }

        // ---- profile modules ----------------------------------------------------------------------
        public IReadOnlyList<string> StanceInclude { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> StanceMatchups { get; }
        public double StanceAdvantageMultiplier { get; }
        public double StanceDefenseMitigation { get; }
        public bool StaggerOnAdvantage { get; }
        public IReadOnlyList<string> StaggerManeuvers { get; }
        /// <summary>stances.staggerLimitPerTargetPerRound (JS claimStagger, default 1).</summary>
        public int StaggerLimitPerTargetPerRound { get; }
        public double AttackRatingWeight { get; }
        /// <summary>effects.defenseRatingWeight: DR's weight in a Block gain (JS computeBlockGain, configured branch).</summary>
        public double DefenseRatingWeight { get; }
        /// <summary>defenses.include has "block": a configured profile without it grants no Block (JS computeBlockGain).</summary>
        public bool BlockDefense { get; }
        public IReadOnlyList<string> ResourceInclude { get; }
        /// <summary>resources.manaPerTurn; absent in Core 2, so MP is not restored at turn start (JS combat.js startPlayerTurn).</summary>
        public int ManaPerTurn { get; }
        public int HandBase { get; }
        public string HandAttribute { get; }

        // ---- damage typing and Counter coverage (mechanics.json damageTypes / counterRules) -----------
        public Core2DamageTypes DamageTypes { get; }
        /// <summary>combatExpansionMatchups.counter.defaultCoverage by Counter mode (melee, ranged, spell).</summary>
        public IReadOnlyDictionary<string, Core2CounterCoverage> CounterDefaultCoverage { get; }

        private readonly Dictionary<string, Core2StatusDefinition> _statuses;
        public IReadOnlyDictionary<string, Core2StatusDefinition> Statuses => _statuses;

        // ---- phase 5c: enemy plans and Perception (mechanics.json actionCosts, fixCoreMechanics) ---------------
        /// <summary>JS ACTION_COST_TABLE (content/actionCosts.js): the SP an enemy move without its own `sp` costs, by stance.</summary>
        public IReadOnlyDictionary<string, int> ActionCostTable { get; }
        /// <summary>fixCoreMechanics.enemyHealBlockCost: a move that both heals and blocks costs this much SP.</summary>
        public int EnemyHealBlockCost { get; }
        /// <summary>fixCoreMechanics.perception.baseDC: the read DC is baseDC + target INT + target WIS + bonuses.</summary>
        public int PerceptionBaseDC { get; }
        public int PerceptionBonus { get; }
        /// <summary>cards.roundShuffle: "entireDeck" returns every card and reshuffles the whole deck each round (JS refreshProfileRound).</summary>
        public string RoundShuffle { get; }
        /// <summary>cards.freeCommandLimit: card plays a turn may accept before only End Turn remains.</summary>
        public int FreeCommandLimit { get; }
        /// <summary>
        /// reactions.chainLimit (else reaction rules stack.maxDepth, 4): the enemy entries one reaction stack may hold (JS
        /// openEnemyWindow cap). The player's own per-contact cap is 1 under the perHit contact policy.
        /// </summary>
        public int ReactionStackMaxDepth { get; }

        public Core2Rules(JObject fixCore, JObject stanceRules, JObject modules, IEnumerable<Core2Row> statuses, JObject damageTypes = null, JObject counterRules = null,
            JObject actionCosts = null)
        {
            if (fixCore == null || stanceRules == null || modules == null) throw new ArgumentNullException(nameof(fixCore));
            if ((int?)fixCore["version"] != 1 || fixCore["enabled"]?.Type != JTokenType.Boolean)
                throw new InvalidDataException("fixCoreMechanics requires version 1 and enabled (JS validateFixCoreRules)");
            FixCoreEnabled = (bool)fixCore["enabled"];
            FixCoreVersion = (int?)modules["effects"]?["fixCoreVersion"] ?? 0;
            WardEnabled = (bool?)fixCore["ward"]?["enabled"] ?? true;
            var stance = Need(fixCore, "stance");
            StanceEffectivenessMultiplier = Num(stance, "effectivenessMultiplier");
            StanceDiscount = (int)Num(stance, "discount");
            StanceMinimumCost = (int)Num(stance, "minimumCost");
            ReactionHandCostDiscount = (int?)fixCore["reaction"]?["handCostDiscount"] ?? 1;
            var accuracy = Need(fixCore, "accuracy");
            if ((int?)accuracy["evasionVersion"] != 2) throw new InvalidDataException("fixCoreMechanics.accuracy.evasionVersion must be 2 (JS normalizeFixCoreRules)");
            BaseAC = Num(accuracy, "baseAC"); EvadeStackBase = Num(accuracy, "evadeStackBase"); EvadeStackDex = Num(accuracy, "evadeStackDex");
            PrecisionBase = Num(accuracy, "precisionBase"); EvasionBase = Num(accuracy, "evasionBase");
            var weights = Need(accuracy, "precisionWeights");
            PrecisionWeightDex = Num(weights, "dex"); PrecisionWeightStr = Num(weights, "str"); PrecisionWeightWis = Num(weights, "wis");
            PrecisionWeightInt = Num(weights, "int"); PrecisionWeightLevel = Num(weights, "level");
            ManeuverPrecision = Need(accuracy, "maneuverPrecision").Properties().ToDictionary(p => p.Name, p => (double)p.Value, StringComparer.Ordinal);
            var statusRules = Need(fixCore, "statuses");
            StatusDecayAmount = (int)Num(statusRules, "decayAmount");
            WeakARMultiplier = Num(statusRules, "weakARMultiplier");
            VulnerabilityMultiplier = Num(statusRules, "vulnerabilityMultiplier");
            // JS FIXCORE_STAGGERED_DEFAULTS / FIXCORE_SMASH_VS_DEFENSIVE_DEFAULTS apply only when a block is missing.
            var staggered = fixCore["staggered"] as JObject ?? new JObject();
            StaggeredStance = (bool?)staggered["stance"] ?? true;
            StaggeredStaminaLoss = (int?)staggered["staminaLoss"] ?? 1;
            StaggeredVulnerableStacks = (int?)staggered["vulnerableStacks"] ?? 1;
            var smash = fixCore["smashVsDefensive"] as JObject ?? new JObject();
            SmashDamageMultiplier = (double?)smash["damageMultiplier"] ?? 1.25;
            SmashBlockFraction = (double?)smash["blockFraction"] ?? 0.25;
            SmashPoiseFraction = (double?)smash["poiseFraction"] ?? 0.5;
            PoiseProtectionMultiplier = (double?)fixCore["protectionParameters"]?["defense.poiseProtection"]?["multiplier"] ?? 0.75;
            var counter = fixCore["counter"] as JObject ?? new JObject();
            CounterReturnFraction = (double?)counter["returnFraction"] ?? 0.5;
            CounterPoiseFraction = (double?)counter["poiseFraction"] ?? 0.2;
            CounterAdvantageIncomingFraction = (double?)counter["advantageIncomingFraction"] ?? 0.25;
            CounterAdvantagePoiseFraction = (double?)counter["advantagePoiseFraction"] ?? 0.5;
            CounterAdvantageBlockFraction = (double?)counter["advantageBlockFraction"] ?? 0.25;
            RangePolicy = Core2RangePolicy.Snapshot(fixCore["range"] as JObject);

            OffensiveRating = (int)Num(stanceRules, "offensiveRating");
            DefensiveRating = (int)Num(stanceRules, "defensiveRating");
            OffensivePrecision = (int)Num(stanceRules, "offensivePrecision");
            EvadeStacks = (int)Num(stanceRules, "evadeStacks");
            EntryProtection = (int)Num(stanceRules, "entryProtection");
            SpellBlockShare = Num(stanceRules, "spellBlockShare");
            SchoolBlockShares = ((stanceRules["schoolBlockShares"] as JObject) ?? new JObject()).Properties()
                .ToDictionary(p => p.Name, p => (double)p.Value, StringComparer.Ordinal);
            DefenseRestore = (int)Num(stanceRules, "defenseRestore");
            FallbackPoise = (int)Num(stanceRules, "fallbackPoise");
            SweepReactionPoise = (int)Num(stanceRules, "sweepReactionPoise");
            SweepReactionWard = (int)Num(stanceRules, "sweepReactionWard");

            var stances = Need(modules, "stances");
            StanceInclude = Core2Json.Strings(stances["include"]);
            StanceMatchups = ((stances["matchups"] as JObject) ?? new JObject()).Properties()
                .ToDictionary(p => p.Name, p => (IReadOnlyList<string>)Core2Json.Strings(p.Value), StringComparer.Ordinal);
            StanceAdvantageMultiplier = (double?)stances["advantageMultiplier"] ?? 1;
            StanceDefenseMitigation = (double?)stances["defenseMitigation"] ?? 0;
            StaggerOnAdvantage = (bool?)stances["staggerOnAdvantage"] ?? false;
            StaggerManeuvers = stances["staggerManeuvers"] is JArray ? Core2Json.Strings(stances["staggerManeuvers"]) : new[] { "smash" };
            StaggerLimitPerTargetPerRound = (int?)stances["staggerLimitPerTargetPerRound"] ?? 1;
            AttackRatingWeight = (double?)modules["effects"]?["attackRatingWeight"] ?? 0;
            DefenseRatingWeight = (double?)modules["effects"]?["defenseRatingWeight"] ?? 0;
            BlockDefense = Core2Json.Strings(modules["defenses"]?["include"]).Contains("block");
            ResourceInclude = Core2Json.Strings(modules["resources"]?["include"]);
            ManaPerTurn = (int?)modules["resources"]?["manaPerTurn"] ?? 0;
            HandBase = (int?)modules["cards"]?["handBase"] ?? 4;
            HandAttribute = (string)modules["cards"]?["handAttribute"] ?? "intelligence";
            RoundShuffle = (string)modules["cards"]?["roundShuffle"];
            FreeCommandLimit = (int?)modules["cards"]?["freeCommandLimit"] ?? 32;
            ReactionStackMaxDepth = (int?)modules["reactions"]?["chainLimit"] ?? 4;
            ActionCostTable = ((actionCosts?["table"] as JObject) ?? new JObject()).Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal);
            EnemyHealBlockCost = (int?)fixCore["enemyHealBlockCost"] ?? 3;
            PerceptionBaseDC = (int?)fixCore["perception"]?["baseDC"] ?? 5;
            PerceptionBonus = ((fixCore["perception"]?["bonuses"] as JArray) ?? new JArray()).Sum(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float ? (int)Math.Floor((double)v) : 0);

            DamageTypes = new Core2DamageTypes(damageTypes ?? throw new InvalidDataException("Core 2 rules: mechanics.json damageTypes is required (tools/export-js-core2.mjs)"));
            var coverage = counterRules?["defaultCoverage"] as JObject ?? throw new InvalidDataException("Core 2 rules: mechanics.json counterRules.defaultCoverage is required (tools/export-js-core2.mjs)");
            CounterDefaultCoverage = coverage.Properties().ToDictionary(p => p.Name, p => Core2CounterCoverage.FromJson(p.Value), StringComparer.Ordinal);

            var overrides = statusRules["overrides"] as JObject ?? new JObject();
            _statuses = new Dictionary<string, Core2StatusDefinition>(StringComparer.Ordinal);
            foreach (var row in statuses ?? Enumerable.Empty<Core2Row>())
            {
                var stackMode = (string)overrides[row.Id]?["stackMode"] ?? (string)row.Raw["stackMode"] ?? "add";
                var decay = row.Raw["decay"];
                var kind = decay is JObject ? Core2StatusDecay.Duration
                    : (string)decay == "perTurnEnd" ? Core2StatusDecay.PerTurnEnd
                    : (string)decay == "onConsume" ? Core2StatusDecay.OnConsume : Core2StatusDecay.None;
                _statuses[row.Id] = new Core2StatusDefinition(row.Id, stackMode, kind, kind == Core2StatusDecay.Duration ? (int?)decay["duration"] ?? 0 : 0);
            }
        }

        /// <summary>The rules the exported Core 2 profile runs (mechanics.json, profile.json modules, adapter statuses).</summary>
        public static Core2Rules From(Core2Content content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return new Core2Rules(content.Mechanics["fixCoreMechanics"] as JObject, content.Mechanics["stanceRules"] as JObject,
                content.Profile["modules"] as JObject, content.Statuses, content.Mechanics["damageTypes"] as JObject, content.Mechanics["counterRules"] as JObject,
                content.Mechanics["actionCosts"] as JObject);
        }

        public Core2StatusDefinition Status(string id) => _statuses.TryGetValue(id, out var row) ? row
            : throw new KeyNotFoundException("Unknown Core 2 status " + id);
        public bool HasStatus(string id) => id != null && _statuses.ContainsKey(id);
        public bool Includes(string pool) => ResourceInclude.Contains(pool);

        private static JObject Need(JObject row, string key) => row[key] as JObject ?? throw new InvalidDataException("Core 2 rules: " + key + " is required");
        private static double Num(JObject row, string key)
        {
            var value = row[key];
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) throw new InvalidDataException("Core 2 rules: " + key + " must be a number");
            return (double)value;
        }
    }
}
