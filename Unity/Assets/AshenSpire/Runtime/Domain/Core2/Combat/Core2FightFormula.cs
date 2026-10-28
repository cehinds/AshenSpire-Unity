// Core2FightFormula.cs — the structured formulas a Core 2 fight evaluates for an action (amounts, stacks, hits, repeats,
// target limits): the whole closed formula set, with an action's entities and paid amounts, and the combat metrics.
//
// JS at the pin:
//   src/model/formulas.js  evaluate / evaluateRaw / evalNode (add, mul, percentMaxHp, missingHp, missingMana, stacks with
//                          per, energySpent, blockOf, hpOf, cardsPlayedThisTurn, metric, traitBonus; min / max clamps on any
//                          node; the result floored once), resolveOne / resolveMany (entity refs), stacksOf (a meter
//                          status reports its meter value)
//   src/engine/actions.js  formulaCtxFor (self, owner, target / enemy = the primary target else the action's, player,
//                          allEnemies = the living; weaponDamage, cardId, metricsBefore, manaSpent, staminaSpent,
//                          evadeStackGain, energySpent, cardsPlayedThisTurn), evalNum (a number floored; a formula
//                          evaluated) and evalRaw (unfloored)
//   src/engine/cardBehavior.js weaponDamageBasis (configured: core.strike's damage + the actor's AR)
//   src/engine/combatCardTraitMath.js counterstrikeAmount (traitBonus)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed partial class Core2Fight
    {
        /// <summary>JS formulaCtxFor: what a formula may read for one action and its primary target.</summary>
        private sealed class FormulaScope
        {
            public Core2Combatant Self, Owner, Target, Player;
            public List<Core2Combatant> AllEnemies;
            public Core2Carrier Card;
            public Core2AbilityState MetricsBefore;
            public int ManaSpent, StaminaSpent, EnergySpent, CardsPlayedThisTurn;
            public Core2Combatant Source;
        }

        private FormulaScope ScopeFor(Core2FightAction action, Core2Combatant primaryTarget)
        {
            return new FormulaScope
            {
                Self = action?.Source, Owner = action?.Owner ?? action?.Source, Target = primaryTarget ?? action?.Target, Player = Player,
                AllEnemies = Enemies.Where(e => e.Alive).ToList(), Card = action?.Card, MetricsBefore = action?.AbilityBefore,
                ManaSpent = action?.ManaSpent ?? 0, StaminaSpent = action?.StaminaSpent ?? 0, EnergySpent = action?.EnergySpent ?? 0,
                CardsPlayedThisTurn = Player?.CardsPlayedThisTurn ?? 0, Source = action?.Source,
            };
        }

        /// <summary>JS evalNum: a number floored, a formula evaluated (floored once); a missing value is the fallback.</summary>
        private double Eval(Core2FightAction action, JToken value, double fallback, Core2Combatant target = null)
        {
            if (value == null || value.Type == JTokenType.Null) return fallback;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return Math.Floor((double)value);
            if (value is JObject row && row["f"]?.Type == JTokenType.String) return Math.Floor(Node(row, ScopeFor(action, target)));
            throw new InvalidOperationException("Expected number or formula, got " + value.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>JS evalRaw: the unfloored amount (the heal reads it so a percentage is floored once).</summary>
        private double EvalRaw(Core2FightAction action, JToken value, double fallback, Core2Combatant target = null)
        {
            if (value == null || value.Type == JTokenType.Null) return fallback;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return (double)value;
            if (value is JObject row && row["f"]?.Type == JTokenType.String) return Node(row, ScopeFor(action, target));
            throw new InvalidOperationException("Expected number or formula, got " + value.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static Core2Combatant FormulaEntity(FormulaScope scope, string of, string op)
        {
            if (of == null) throw new InvalidOperationException("Formula '" + op + "' requires an 'of' entity ref");
            var entity = of switch
            {
                "self" => scope.Self, "owner" => scope.Owner, "target" => scope.Target, "enemy" => scope.Target, "player" => scope.Player,
                "allEnemies" => throw new InvalidOperationException("Formula '" + op + "' cannot target 'allEnemies' (a group); use a single entity ref"),
                _ => null,
            };
            return entity ?? throw new InvalidOperationException("Formula '" + op + "': entity ref '" + of + "' did not resolve in this context");
        }

        private static IEnumerable<Core2Combatant> FormulaEntities(FormulaScope scope, string of, string op) =>
            of == "allEnemies" ? scope.AllEnemies : new[] { FormulaEntity(scope, of, op) };

        private static double Args(JObject node, Func<IEnumerable<double>, double> fold, Func<JToken, double> eval) =>
            fold((node["args"] as JArray ?? throw new InvalidOperationException("Formula '" + (string)node["f"] + "' requires an args array")).Select(eval));

        private static double NumberField(JObject node, string field) =>
            node[field]?.Type == JTokenType.Integer || node[field]?.Type == JTokenType.Float ? (double)node[field]
                : throw new InvalidOperationException("Formula '" + (string)node["f"] + "' requires numeric field '" + field + "'");

        /// <summary>JS evalNode: one formula node, clamped by its own min / max; the caller floors the final result once.</summary>
        private double Node(JToken token, FormulaScope scope)
        {
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) return (double)token;
            if (!(token is JObject node) || node["f"]?.Type != JTokenType.String) throw new InvalidOperationException("Not a number or formula: " + token.ToString(Newtonsoft.Json.Formatting.None));
            double v;
            var f = (string)node["f"];
            switch (f)
            {
                case "add": v = Args(node, xs => xs.Aggregate(0.0, (a, b) => a + b), a => Node(a, scope)); break;
                case "mul": v = Args(node, xs => xs.Aggregate(1.0, (a, b) => a * b), a => Node(a, scope)); break;
                case "percentMaxHp": v = FormulaEntity(scope, (string)node["of"], f).MaxHp * NumberField(node, "pct") / 100; break;
                case "missingHp": { var e = FormulaEntity(scope, (string)node["of"], f); v = Math.Max(0, e.MaxHp - e.Hp); break; }
                case "missingMana": { var e = FormulaEntity(scope, (string)node["of"], f); v = Math.Max(0, e.MaxMana - e.Mana); break; }
                case "stacks":
                {
                    var status = (string)node["status"] ?? throw new InvalidOperationException("Formula 'stacks' requires a 'status' id");
                    var total = FormulaEntities(scope, (string)node["of"], f).Sum(e => (double)e.StatusStacks(status));
                    var per = node["per"];
                    if (per != null && per.Type != JTokenType.Null && !((double)per > 0)) throw new InvalidOperationException("stacks per must be a positive number, got " + per);
                    v = per != null && per.Type != JTokenType.Null ? Math.Floor(total / (double)per) : total;
                    break;
                }
                case "metric":
                {
                    var of = (string)node["of"];
                    var entity = FormulaEntity(scope, of ?? "self", f);
                    var state = (of == null || of == "self" || of == "owner") && scope.MetricsBefore != null ? scope.MetricsBefore : entity.AbilityRiders ?? new Core2AbilityState();
                    v = Metric((string)node["metric"], node, entity, state, scope) * (node["per"] != null && node["per"].Type != JTokenType.Null ? (double)node["per"] : 1);
                    break;
                }
                case "energySpent": v = scope.EnergySpent * (node["per"] != null && node["per"].Type != JTokenType.Null ? (double)node["per"] : 1); break;
                case "blockOf": v = FormulaEntity(scope, (string)node["of"], f).Block; break;
                case "hpOf": v = FormulaEntity(scope, (string)node["of"], f).Hp; break;
                case "cardsPlayedThisTurn": v = scope.CardsPlayedThisTurn * (node["per"] != null && node["per"].Type != JTokenType.Null ? (double)node["per"] : 1); break;
                case "traitBonus":
                {
                    // JS counterstrikeAmount: the base is floored, then a + max(floor(a × pct), 1) for a > 0.
                    var a = Math.Floor(Node(node["base"], scope));
                    var pct = NumberField(node, "pct");
                    v = a > 0 ? a + Math.Max(Math.Floor(a * pct), 1) : a;
                    break;
                }
                default: throw new InvalidOperationException("Unknown formula op '" + f + "'");
            }
            if (node["min"] != null && node["min"].Type != JTokenType.Null && v < (double)node["min"]) v = (double)node["min"];
            if (node["max"] != null && node["max"].Type != JTokenType.Null && v > (double)node["max"]) v = (double)node["max"];
            return v;
        }

        /// <summary>JS evalNode 'metric': the named combat metric of the entity (or its pre-play snapshot for self/owner).</summary>
        private double Metric(string metric, JObject node, Core2Combatant entity, Core2AbilityState state, FormulaScope scope)
        {
            var listed = Core2Json.Strings(node["statuses"]).ToHashSet(StringComparer.Ordinal);
            var active = entity.Statuses.Where(p => p.Value.Stacks > 0 && listed.Contains(p.Key)).Select(p => p.Value).ToList();
            switch (metric)
            {
                case "weaponDamage": return WeaponDamageBasis(scope.Source);
                case "negativeStatusTypes": return active.Count;
                case "negativeStatusStacks": return active.Sum(row => row.Stacks);
                case "damageTakenThisTurn": return state.HpLostSinceTurnStart;
                case "sameCardPlays": return Core2AbilityState.Count(state.CardUsesCombat, scope.Card?.Id);
                case "cardsPlayed": return state.CardsPlayed;
                case "distinctTypes": return state.TypePlays.Count;
                case "manaSpent": return scope.ManaSpent;
                case "staminaSpent": return scope.StaminaSpent;
                case "evadeStackGain": return scope.Source == null ? 0 : Core2Accuracy.EvadeStackGain(Rules, scope.Source);
            }
            throw new InvalidOperationException("Unknown combat metric");
        }

        /// <summary>JS weaponDamageBasis (configured): core.strike's authored damage plus the actor's AR.</summary>
        private double WeaponDamageBasis(Core2Combatant actor)
        {
            var strike = Content.HasCard("core.strike") ? Content.Card("core.strike").Effects.OfType<JObject>().FirstOrDefault(e => (string)e["op"] == "damage") : null;
            var amount = strike?["amount"]?.Type == JTokenType.Integer ? (int)strike["amount"] : 6;
            return Math.Max(0, amount + (actor?.Rating("ar") ?? 0));
        }
    }
}
