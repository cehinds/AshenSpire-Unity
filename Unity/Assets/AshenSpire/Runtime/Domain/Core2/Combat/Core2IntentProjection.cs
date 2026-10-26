// Core2IntentProjection.cs — the damage range an enemy intent shows ("0–12", or "12" when certain): direct HP loss
// for the revealed damage packet at the player's current defenses.
//
// JS at the pin: src/ui/models/CombatIntentProjectionModel.js projectCombatIntent / damageBounds (configured branch).
// Every hit either lands (probability = its hit chance) or misses; a landed hit carries its effect on Block, Barrier,
// Ward, Poise, stance and Vulnerable into the next hit (JS applyReceiptToPreviewTarget), so the reachable target
// states are enumerated and merged, never "all hits × chance". The range is the least and most HP any reachable
// outcome loses; a baseline with the player's stance removed decides the green "stance reduced" tone.
// Visibility (concealed intents, Perception) is phase 5c: callers project only intents the player may see.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2DamageBounds
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public double HitChance { get; set; }
        public double Expected { get; set; }
    }

    public sealed class Core2DamageRange
    {
        /// <summary>Null when the range cannot be promised (Reason says why).</summary>
        public int? Min { get; set; }
        public int? Max { get; set; }
        public double HitChance { get; set; }
        public double Expected { get; set; }
        /// <summary>"0–12", "12", or "?" (JS label; an en dash between the bounds).</summary>
        public string Label { get; set; }
        public bool Uncertain { get; set; }
        /// <summary>green when the shown range is lower because of the player's stance, else red.</summary>
        public string Tone { get; set; }
        public bool StanceReduced { get; set; }
        public bool DisplayedReduction { get; set; }
        /// <summary>multipleDamageEffects or unresolvedDamage when no range is promised; null otherwise.</summary>
        public string Reason { get; set; }
        public Core2DamageBounds Baseline { get; set; }
    }

    /// <summary>The status-rule plan JS proposalTargetPlan gives the hit (chance the attack is allowed, perceived and not evaded).</summary>
    public sealed class Core2HitProposal
    {
        public bool Allowed { get; set; } = true;
        /// <summary>Percent, or null when no Perception check applies.</summary>
        public double? PerceptionChance { get; set; }
        /// <summary>Percent the status rules evade the hit.</summary>
        public double EvadeChance { get; set; }
        public double Chance => (Allowed ? 1 : 0) * (PerceptionChance.HasValue ? PerceptionChance.Value / 100 : 1) * (1 - EvadeChance / 100);
    }

    public static class Core2IntentProjection
    {
        private static Core2DamageRange Unknown(string reason) => new Core2DamageRange { Label = "?", Tone = "red", Uncertain = true, Reason = reason };

        /// <summary>JS projectCombatIntent's projectedDamage for an attacking intent of <paramref name="hits"/> × <paramref name="baseDamage"/>.</summary>
        public static Core2DamageRange Project(Core2Rules rules, Core2Combatant attacker, Core2Combatant player, Core2Carrier carrier, int baseDamage, int hits,
            int damageEffects = 1, Core2HitOptions options = null, Core2HitProposal proposal = null)
        {
            if (damageEffects > 1) return Unknown("multipleDamageEffects");
            if (hits < 0 || hits > 100) return Unknown("unresolvedDamage");
            var actual = Bounds(rules, attacker, player, carrier, baseDamage, hits, false, options, proposal);
            var baseline = Bounds(rules, attacker, player, carrier, baseDamage, hits, true, options, proposal);
            var stanceReduced = actual.Expected < baseline.Expected - 1e-9;
            // Green describes the displayed HP range, not an undisplayed average.
            var displayedReduction = stanceReduced && actual.Min <= baseline.Min && actual.Max < baseline.Max;
            return new Core2DamageRange
            {
                Min = actual.Min, Max = actual.Max, HitChance = actual.HitChance, Expected = actual.Expected,
                Label = actual.Min == actual.Max ? actual.Max.ToString(System.Globalization.CultureInfo.InvariantCulture) : actual.Min + "–" + actual.Max,
                Tone = displayedReduction ? "green" : "red", StanceReduced = stanceReduced, DisplayedReduction = displayedReduction,
                Uncertain = actual.Min != actual.Max, Baseline = baseline,
            };
        }

        private sealed class State
        {
            public Core2Combatant Target;
            public int Loss;
            public double Probability;
        }

        /// <summary>JS damageBounds (configured): enumerate and merge the target states each hit can reach.</summary>
        public static Core2DamageBounds Bounds(Core2Rules rules, Core2Combatant attacker, Core2Combatant player, Core2Carrier carrier, int baseDamage, int hits,
            bool neutral, Core2HitOptions options = null, Core2HitProposal proposal = null)
        {
            var source = attacker.Clone();
            var target = player.Clone();
            if (neutral) { target.Stance = null; target.ConcurrentStances.Clear(); }
            var proposalChance = (proposal ?? new Core2HitProposal()).Chance;
            var states = new List<State> { new State { Target = target, Loss = 0, Probability = 1 } };
            for (var hit = 0; hit < hits; hit++)
            {
                var next = new List<State>();
                var index = new Dictionary<string, State>(StringComparer.Ordinal);
                void Add(Core2Combatant row, int loss, double probability)
                {
                    if (!(probability > 0)) return;
                    var key = Key(row) + ":" + loss;
                    if (index.TryGetValue(key, out var previous)) previous.Probability += probability;
                    else { var state = new State { Target = row, Loss = loss, Probability = probability }; index[key] = state; next.Add(state); }
                }
                foreach (var state in states)
                {
                    var receipt = Core2Damage.Receipt(rules, source, state.Target, baseDamage, carrier, options);
                    var landed = state.Target.Clone();
                    Core2Damage.ApplyToPreviewTarget(rules, landed, receipt);
                    Add(landed, state.Loss + receipt.HpLoss, state.Probability * receipt.HitChance);
                    Add(state.Target, state.Loss, state.Probability * (1 - receipt.HitChance));
                }
                states = next;
            }
            var min = states.Min(s => s.Loss);
            var max = states.Max(s => s.Loss);
            var expected = states.Sum(s => s.Loss * s.Probability) * proposalChance;
            return new Core2DamageBounds
            {
                Min = proposalChance < 1 ? 0 : min, Max = proposalChance == 0 ? 0 : max,
                HitChance = states.Where(s => s.Loss > 0).Sum(s => s.Probability) * proposalChance, Expected = expected,
            };
        }

        /// <summary>JS targetKey: Block, Barrier, Ward, Poise, stance and statuses decide whether two outcomes are the same state.</summary>
        private static string Key(Core2Combatant row)
        {
            var key = new StringBuilder();
            key.Append(row.Block).Append('|').Append(row.Barrier).Append('|')
                .Append(row.Ward == null ? "-" : row.Ward.Value + "/" + row.Ward.Max).Append('|')
                .Append(row.Poise == null ? "-" : row.Poise.Value + "/" + row.Poise.Max).Append('|')
                .Append(row.Stance == null ? "-" : row.Stance.ToString()).Append('|');
            foreach (var status in row.Statuses.OrderBy(p => p.Key, StringComparer.Ordinal))
                key.Append(status.Key).Append('=').Append(status.Value.Stacks).Append('/').Append(status.Value.Duration).Append(',');
            return key.ToString();
        }
    }
}
