// Core2EnemyMoves.cs — an enemy move as a Core 2 fight reads it: the expanded move, its carrier, its SP price (the
// multi-hit floor included) and its full quote with the Advance it needs (SPEC §4.6a, owner 2026-10-12/13).
//
// JS at the pin:
//   src/engine/combatCardTactics.js  expandedEnemyMove (range/targeting defaults, enemy pressure statuses become
//                                    buildup, the Smash fallback Poise, delayed whileCharging), enemyMoveCarrier
//                                    (tacticalCarrier with type 'attack')
//   src/model/stanceRules.js         enemySmashFallbackPoise
//   src/model/actionCosts.js         enemyMoveSpOf (own `sp`, else the stance table price, else 1; heal+block moves cost
//                                    enemyHealBlockCost), stanceCost, actionStance
//   src/engine/combatRounds/stance.js moveKind, stanceFor
//   src/engine/combatRoundsAdapter.js moveSp (Core 2: a move with more than one HP-damage contact on a target costs at
//                                    least 2 SP), enemyMoveHasMultipleHits, enemyMoveQuote (SP + Advance SP)
//   src/engine/enemyActionPreview.js previewEnemyAction (the action payload a quote ranges over)
//   src/engine/combatEngagement.js   quoteRangeActions / rangeSourceContact (an enemy move covers its own range)
// Not ported: range-lineage rider contacts (combatRangeLineage.js extendRangeEnvelope; a relic rider that reaches
// another band) — Core 2 enemies carry no range hooks at the pin, so their quotes have none.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>JS enemyMoveQuote: the move's SP, the Advance SP its contacts need, the total, and the range route.</summary>
    public sealed class Core2MoveQuote
    {
        public int Sp { get; set; }
        public int AdvanceSp { get; set; }
        public int TotalSp => Sp + AdvanceSp;
        public string Route { get; set; }
        public bool? Available { get; set; }
        public Core2RangePlan Plan { get; set; }
    }

    public static class Core2EnemyMoves
    {
        /// <summary>JS ENEMY_PRESSURE: statuses an enemy applies to the player as status pressure (buildup), with their recovery profile.</summary>
        private static readonly IReadOnlyDictionary<string, (string Status, string Recovery)> EnemyPressure = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["frail"] = ("vulnerable", "bodily"), ["weak"] = ("weak", "bodily"), ["bleed"] = ("bleed", "bodily"),
            ["crimsonBlight"] = ("crimsonBlight", "curse"), ["frost"] = ("frost", "elemental"), ["insanity"] = ("dazed", "mental"),
        };

        public static JObject RawMove(Core2Content content, string enemyId, string moveId) =>
            content.Enemy(enemyId).Move(moveId)?.Raw ?? throw new KeyNotFoundException("Enemy " + enemyId + " has no move " + moveId);

        /// <summary>JS combatProfileFor({...move, enemyId, moveId}).</summary>
        public static Core2CombatProfile ProfileOf(JObject move, string moveId) =>
            Core2Stances.CombatProfileFor(moveId, Core2Json.Strings(move["cardTags"] ?? move["tags"]), move);

        /// <summary>JS expandedEnemyMove (combatExpansionVersion 2, FixCore on).</summary>
        public static JObject Expanded(Core2Rules rules, JObject move, string moveId)
        {
            var profile = ProfileOf(move, moveId);
            var result = (JObject)move.DeepClone();
            result["range"] = profile.Range ?? (profile.Maneuver == "ranged" || profile.Camp == "spell" ? "reach" : "melee");
            result["targeting"] = profile.Targeting ?? (profile.Maneuver == "sweep" ? "area" : "single");
            if (profile.Maneuver == "smash") result["breakPoiseBonus"] = move["breakPoiseBonus"] ?? 3;
            if (profile.Maneuver == null) result["stanceTrigger"] = false;
            var effects = new JArray();
            foreach (var effect in (move["effects"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var status = (string)effect["status"];
                var target = (string)effect["target"];
                if ((string)effect["op"] == "applyStatus" && status != null && EnemyPressure.TryGetValue(status, out var pressure) && target != "self" && target != "owner")
                {
                    var mapped = (JObject)effect.DeepClone();
                    mapped.Remove("stacks"); mapped.Remove("status");
                    mapped["status"] = pressure.Status; mapped["recoveryProfile"] = pressure.Recovery;
                    mapped["op"] = "buildup"; mapped["camp"] = profile.Camp ?? "physical";
                    var stacks = effect["stacks"];
                    mapped["amount"] = stacks?.Type == JTokenType.Integer ? (JToken)((long)stacks * 4)
                        : stacks?.Type == JTokenType.Float ? (JToken)((double)stacks * 4)
                        : new JObject { ["f"] = "mul", ["args"] = new JArray(stacks ?? 1, 4) };
                    effects.Add(mapped);
                }
                else effects.Add(effect.DeepClone());
            }
            result["effects"] = effects;
            var fallback = SmashFallbackPoise(rules, result, profile);
            if (fallback.HasValue) effects.Add(new JObject { ["op"] = "poiseDamage", ["target"] = "player", ["amount"] = fallback.Value });
            if (profile.Maneuver == "ranged") result["projectile"] = true;
            if (move["delay"]?["whileCharging"] is JObject charging)
            {
                var delay = (JObject)move["delay"].DeepClone();
                var inner = (JObject)charging.DeepClone();
                inner["tags"] = move["tags"]?.DeepClone(); if (move["cardTags"] != null) inner["cardTags"] = move["cardTags"].DeepClone();
                delay["whileCharging"] = Expanded(rules, inner, moveId);
                result["delay"] = delay;
            }
            if (profile.Maneuver == "counter" && result["counterCoverage"] == null)
            {
                var ranged = profile.CounterMode == "ranged"; var spell = profile.Camp == "spell";
                var coverage = new JObject
                {
                    ["camps"] = new JArray(spell ? "spell" : "physical"),
                    ["ranges"] = spell ? new JArray("melee", "reach", "ranged") : ranged ? new JArray("reach", "ranged") : new JArray("melee"),
                    ["targeting"] = new JArray("single"), ["effects"] = spell ? new JArray("damage", "status") : new JArray("damage"),
                };
                if (!ranged && !spell) coverage["maneuvers"] = new JArray("attack", "smash");
                result["counterCoverage"] = coverage;
            }
            return result;
        }

        /// <summary>JS enemySmashFallbackPoise: a Smash that deals damage and authors no Poise damage carries the shared fallback.</summary>
        public static int? SmashFallbackPoise(Core2Rules rules, JObject move, Core2CombatProfile profile)
        {
            var effects = (move["effects"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (profile?.Maneuver != "smash" || !(move["damage"] != null && move["damage"].Type != JTokenType.Null || effects.Any(e => (string)e["op"] == "damage"))
                || effects.Any(e => (string)e["op"] == "poiseDamage")) return null;
            return rules.FallbackPoise;
        }

        /// <summary>JS enemyMoveCarrier: the expanded move as a carrier (tacticalCarrier with type 'attack'; no kindIds copied).</summary>
        public static Core2Carrier Carrier(Core2Rules rules, JObject move, string moveId)
        {
            var expanded = Expanded(rules, move, moveId);
            var effects = (expanded["effects"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            var carrier = new Core2Carrier
            {
                Id = moveId, Type = "attack", Tags = Core2Json.Strings(expanded["cardTags"] ?? expanded["tags"]).ToList(),
                DamageSchool = (string)expanded["damageSchool"],
                HasDamageEffect = effects.Any(e => (string)e["op"] == "damage"),
                AppliedStatuses = effects.Where(e => (string)e["op"] == "applyStatus" && e["status"] != null).Select(e => (string)e["status"]).ToList(),
            };
            carrier.Profile = Core2Stances.CombatProfileFor(moveId, carrier.Tags, expanded);
            if (expanded["magicShare"]?.Type == JTokenType.Integer || expanded["magicShare"]?.Type == JTokenType.Float) carrier.MagicShare = (double)expanded["magicShare"];
            return carrier;
        }

        /// <summary>The raw move as a stance carrier (JS stanceOf({...def.moves[id], enemyId, moveId}): no type).</summary>
        public static Core2Carrier RawCarrier(JObject move, string moveId)
        {
            var carrier = new Core2Carrier
            {
                Id = moveId, Type = (string)move["type"], Tags = Core2Json.Strings(move["cardTags"] ?? move["tags"]).ToList(),
                KindIds = Core2Json.Strings(move["kindIds"]).ToList(), DamageSchool = (string)move["damageSchool"],
                HasDamageEffect = (move["effects"] as JArray ?? new JArray()).OfType<JObject>().Any(e => (string)e["op"] == "damage"),
            };
            carrier.Profile = ProfileOf(move, moveId);
            return carrier;
        }

        /// <summary>JS stanceOf over the raw move: the stance a plan projects the enemy into after the move.</summary>
        public static Core2Stance PlannedStance(JObject move, string moveId) => Core2Stances.StanceOf(RawCarrier(move, moveId));

        /// <summary>JS moveKind: the move's timeline kind from its authored intent.</summary>
        public static string MoveKind(JObject move)
        {
            if (move["damage"] != null && move["damage"].Type != JTokenType.Null || (string)move["intent"] == "attack") return "attack";
            if ((string)move["intent"] == "block" || move["block"] != null && move["block"].Type != JTokenType.Null) return "guard";
            if ((string)move["intent"] == "counter") return "counter";
            return "other";
        }

        /// <summary>JS actionStance({kind, tags}) with stanceFor as the fallback.</summary>
        public static string ActionStance(string kind, IReadOnlyList<string> tags)
        {
            if (tags.Contains("camp:spell") || tags.Contains("maneuver:casting")) return "cast";
            if (tags.Contains("maneuver:smash")) return "smash";
            if (tags.Contains("maneuver:sweep")) return "sweep";
            if (tags.Contains("maneuver:counter")) return "counter";
            if (tags.Contains("maneuver:defend")) return "block";
            if (tags.Contains("maneuver:attack") || tags.Contains("maneuver:ranged")) return "attack";
            if (kind == "attack" && (tags.Contains("smash") || tags.Contains("maneuver:smash"))) return "smash";
            if (kind == "attack" && (tags.Contains("sweep") || tags.Contains("maneuver:sweep"))) return "sweep";
            switch (kind) { case "attack": return "attack"; case "guard": return "block"; case "evade": return "evade"; case "counter": return "counter"; }
            return null;
        }

        private static bool Positive(JToken token) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) && (double)token > 0;

        /// <summary>
        /// JS enemyMoveSpOf for the paying enemy: a heal-and-block move costs enemyHealBlockCost; else its own `sp`, else
        /// its stance's table price (1 when the stance has none), less the same-stance discount, plus one per Silenced
        /// stack on a spell-camp move.
        /// </summary>
        public static int BaseMoveSp(Core2CombatContext ctx, Core2Combatant enemy, JObject move, string moveId)
        {
            var rules = ctx.Rules;
            var effects = (move["effects"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (rules.FixCoreEnabled && (Positive(move["heal"]) || effects.Any(e => (string)e["op"] == "heal"))
                && (Positive(move["block"]) || effects.Any(e => (string)e["op"] == "block"))) return rules.EnemyHealBlockCost;
            var sp = move["sp"];
            var own = sp != null && (sp.Type == JTokenType.Integer || sp.Type == JTokenType.Float);
            var tags = Core2Json.Strings(move["tags"]);
            var stance = ActionStance(MoveKind(move), tags);
            var baseSp = own ? Math.Max(0, (int)Math.Floor((double)sp)) : stance != null && rules.ActionCostTable.TryGetValue(stance, out var price) ? price : 1;
            var carrier = RawCarrier(move, moveId);
            var silenced = enemy != null && Core2Stances.IsSpellCard(carrier) ? enemy.StatusStacks("silenced") : 0;
            return Core2Pricing.StanceStaminaCost(ctx, baseSp, enemy, carrier, 0) + silenced;
        }

        /// <summary>
        /// JS enemyMoveHasMultipleHits: more than one HP-damage contact on one target (an area hit landing once on each
        /// defender is one hit). A Counter's damage is its return, not a contact.
        /// </summary>
        public static bool HasMultipleHits(Core2Rules rules, JObject move, string moveId)
        {
            var expanded = Expanded(rules, move, moveId);
            if (Carrier(rules, move, moveId).Profile.Maneuver == "counter") return false;
            var contacts = 0;
            if (expanded["damage"] != null && expanded["damage"].Type != JTokenType.Null) contacts += Math.Max(0, (int?)expanded["hits"] ?? 1);
            foreach (var effect in (expanded["effects"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if ((string)effect["op"] != "damage") continue;
                var target = (string)effect["target"];
                // An enemy's damage at the player (or its default target) lands on the one player of a solo fight.
                if (target != null && target != "player" && target != "enemy") throw new NotSupportedException("Enemy move " + moveId + ": damage target '" + target + "' is not ported");
                contacts += Math.Max(0, Core2Formula.Int(effect["hits"], 1)) * Math.Max(0, Core2Formula.Int(effect["repeat"], 1));
            }
            return contacts > 1;
        }

        /// <summary>JS moveSp: the base price, with the Core 2 multi-hit floor of 2 SP under the action policy.</summary>
        public static int MoveSp(Core2CombatContext ctx, Core2Combatant enemy, JObject move, string moveId)
        {
            var price = BaseMoveSp(ctx, enemy, move, moveId);
            return ctx.Stamp.ActionPolicyEnabled && HasMultipleHits(ctx.Rules, move, moveId) ? Math.Max(2, price) : price;
        }

        /// <summary>
        /// JS previewEnemyAction(...).actionEffects: the move's damage packet at the player (none for a Counter), its Block and
        /// Barrier (a primed Counter telegraph already laid them down), then its expanded effects (a Counter keeps only support).
        /// </summary>
        public static List<JObject> ActionEffects(Core2Rules rules, Core2Combatant enemy, JObject move, string moveId)
        {
            var expanded = Expanded(rules, move, moveId);
            var counter = Carrier(rules, move, moveId).Profile.Maneuver == "counter";
            var primed = counter && enemy?.Intent?.MoveId == moveId && enemy.Intent.CounterDefensePrimed;
            var output = new List<JObject>();
            if (expanded["damage"] != null && expanded["damage"].Type != JTokenType.Null && !counter)
                output.Add(new JObject { ["op"] = "damage", ["target"] = "player", ["amount"] = EnemyMoveDamage(enemy, expanded), ["hits"] = expanded["hits"] ?? 1 });
            if (expanded["block"] != null && expanded["block"].Type != JTokenType.Null && !primed) output.Add(new JObject { ["op"] = "block", ["target"] = "self", ["amount"] = expanded["block"] });
            if (expanded["barrier"] != null && expanded["barrier"].Type != JTokenType.Null && !primed) output.Add(new JObject { ["op"] = "gainBarrier", ["target"] = "self", ["amount"] = expanded["barrier"] });
            foreach (var effect in (expanded["effects"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var op = (string)effect["op"];
                if (!counter || op != "damage" && op != "poiseDamage" && op != "wardDamage") output.Add((JObject)effect.DeepClone());
            }
            return output;
        }

        /// <summary>
        /// JS enemyMoveDamage (state.js): the authored per-hit damage, scaled by the enemy's damageMult (a boss's tier damage
        /// scale) when it is not 1 — Math.round(damage × mult), at least 1.
        /// </summary>
        public static int? EnemyMoveDamage(Core2Combatant enemy, JObject move)
        {
            if (move["damage"] == null || move["damage"].Type == JTokenType.Null) return null;
            var damage = (double)move["damage"];
            var mult = enemy?.DamageMult ?? 1;
            if (mult == 1 || !(damage > 0)) return (int)damage;
            return Math.Max(1, (int)Math.Floor(damage * mult + 0.5));
        }

        private static readonly HashSet<string> HostileOps = new HashSet<string>(new[] { "damage", "applyStatus", "buildup", "poiseDamage", "wardDamage", "loseHp", "stagger", "arcaneBuildup" }, StringComparer.Ordinal);

        /// <summary>
        /// JS quoteRangeActions(combat, enemy, actions, {route: 'advance', preview: true}) for an enemy move: one contact per
        /// hostile effect hit on the player, each covering the move's own range (an enemy has no resolved weapon source).
        /// Null when the fight has no range state.
        /// </summary>
        public static Core2RangePlan QuoteRange(Core2Rules rules, Core2RangeState range, Core2Combatant enemy, Core2Combatant player, JObject move, string moveId, string route = "advance")
        {
            if (range == null) return null;
            var carrier = Carrier(rules, move, moveId);
            var contacts = new List<Core2RangeSource>();
            var effects = ActionEffects(rules, enemy, move, moveId);
            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];
                if (!HostileOps.Contains((string)effect["op"])) continue;
                // JS resolveTargets from the enemy: player, enemy and the default target are the player; self, owner and the
                // enemy side are its own kind, which is no contact.
                var target = (string)effect["target"];
                if (target == "self" || target == "owner" || target == "allEnemies" || target == "otherEnemies") continue;
                if (target != null && target != "player" && target != "enemy") throw new NotSupportedException("Enemy move " + moveId + ": effect target '" + target + "' is not ported");
                if (player == null || !player.Alive) continue;
                var repeats = Math.Max(0, Core2Formula.Int(effect["repeat"], 1));
                var hits = (string)effect["op"] == "damage" ? Math.Max(0, Core2Formula.Int(effect["hits"], 1)) : 1;
                if (effect["tags"] is JArray)
                    throw new NotSupportedException("Enemy move " + moveId + ": an effect with its own tags is not ported");
                for (var r = 0; r < repeats; r++) for (var h = 0; h < hits; h++)
                {
                    var targetKey = Core2Range.ActorKey(player);
                    var source = Core2RangeSource.For(carrier, targetKey, contactId: index + ":" + r + ":" + h + ":" + targetKey);
                    contacts.Add(source);
                }
            }
            return Core2Range.ActionPlan(range, Core2Range.ActorKey(enemy), contacts, route);
        }

        /// <summary>JS enemyMoveQuote: SP plus the Advance SP its contacts need from the pricing range state (Core 2 action policy).</summary>
        public static Core2MoveQuote Quote(Core2CombatContext ctx, Core2RangeState range, Core2Combatant enemy, Core2Combatant player, JObject move, string moveId)
        {
            var sp = MoveSp(ctx, enemy, move, moveId);
            if (!ctx.Stamp.ActionPolicyEnabled) return new Core2MoveQuote { Sp = sp };
            var plan = QuoteRange(ctx.Rules, range, enemy, player, move, moveId);
            return new Core2MoveQuote { Sp = sp, AdvanceSp = plan?.AdvanceSp ?? 0, Route = plan?.Route, Available = plan?.Available, Plan = plan };
        }
    }

    /// <summary>
    /// The formula values Core 2 content uses (JS src/model/formulas.js evaluate): numbers, add, mul and the
    /// evadeStackGain metric. Anything else is refused by name rather than guessed.
    /// </summary>
    public static class Core2Formula
    {
        public static double Evaluate(JToken value, Func<string, string, double> metric = null)
        {
            if (value == null || value.Type == JTokenType.Null) return 0;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) return (double)value;
            if (value is JObject row && row["f"] != null)
            {
                var f = (string)row["f"];
                var args = (row["args"] as JArray ?? new JArray()).Select(a => Evaluate(a, metric)).ToList();
                switch (f)
                {
                    case "add": return args.Sum();
                    case "mul": return args.Aggregate(1.0, (a, b) => a * b);
                    case "metric":
                        if (metric == null) throw new NotSupportedException("Formula metric " + (string)row["metric"] + " needs a fight");
                        return metric((string)row["metric"], (string)row["of"]);
                }
                throw new NotSupportedException("Formula f '" + f + "' is not ported");
            }
            throw new NotSupportedException("Unsupported formula value " + value.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>JS evalNum for a whole count (hits, repeat): floored.</summary>
        public static int Int(JToken value, int fallback, Func<string, string, double> metric = null) =>
            value == null || value.Type == JTokenType.Null ? fallback : (int)Math.Floor(Evaluate(value, metric));
    }
}
