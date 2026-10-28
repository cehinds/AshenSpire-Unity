// Core2FightProposal.cs — the authored base-card statuses' rules ("proposal" status rules, adapter/statuses.json
// proposalStatusRules): what each enabled behaviour does at a turn start or end, to a card's play and price, to an
// attack's targeting, its damage and its aftermath, to a heal, and to the Block kept at a turn start; and the stance
// capacity a status grants (base.many-forms).
//
// JS at the pin:
//   src/model/proposalStatusRules.js  proposalRuleEnabled, active (statuses with stacks whose rule is enabled, in order),
//                                     proposalTurnRules (persistentProtection, turnDamage, turnChance; the rule's decay),
//                                     proposalThresholdRules (lethalThreshold), proposalCardRules (restriction, cardTax,
//                                     stanceCapacity, extraWeapon, extraShield), perception, proposalTargetRules (accuracy,
//                                     greaseFire, veil, evade), proposalHitRules (outgoingMultiplier, weaponBonus, greaseFire,
//                                     incomingCap, damageToHealing, guard), proposalAfterHitRules (persistentProtection,
//                                     reflect, autoReaction), proposalHealingRules (healingToDamage), proposalRetainProtection
//   src/engine/proposalStatusRuntime.js proposalContext (capabilities: the enabled modules; no elements and the defenses
//                                     module's protections outside a legacy foundation), applyProposalPlan (status changes
//                                     and effects queued as reactive actions), proposalDamageType, proposalTargetPlan (the
//                                     perception and evade rolls on `misc`, 0..99), proposalHitPlan, proposalAfterHit,
//                                     proposalTurn (the turn-chance roll on `misc`), proposalThreshold, proposalRetained,
//                                     proposalHealingPlan
//   src/engine/concurrentStances.js   policy / stanceCapacity, rememberCombatStance, trimCombatStances, defensiveCombatStance
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One proposal plan (JS { effects, statusChanges, ... }): reactive effects and stack changes to queue.</summary>
    public sealed class Core2ProposalPlan
    {
        public bool Allowed = true, Avoided, Unavoidable;
        public string Refusal;
        public int StaminaTax, ManaTax, AdditionalStanceCapacity;
        public double AccuracyDelta;
        public double? PerceptionChance;
        public double EvadeChance;
        public double Amount;
        public int Healing, Damage;
        public string DamageType;
        public List<JObject> Effects = new List<JObject>();
        public List<(string ActorId, string Status, int Delta)> StatusChanges = new List<(string, string, int)>();
    }

    public sealed partial class Core2Carrier
    {
        /// <summary>JS card.proposalReactive: a status rule's own hit (no outgoing multiplier, no second reaction).</summary>
        public bool ProposalReactive { get; set; }
        /// <summary>JS card.proposalProtectionFloor: a status rule's turn protection (Block gained as authored).</summary>
        public bool ProposalProtectionFloor { get; set; }
        /// <summary>JS card.damageType on a status rule's job (the hit's damage type, read by proposalDamageType).</summary>
        public string ProposalDamageType { get; set; }
        /// <summary>JS carrier.expansionContact carried over: a hit made from a planned contact's carrier rolls no second Accuracy.</summary>
        public bool ContactResolved { get; set; }
    }

    public sealed partial class Core2Fight
    {
        private JObject _proposalRules;
        private JObject ProposalRules => _proposalRules ??= ((JObject)Content.Table("adapter/statuses.json"))["proposalStatusRules"] as JObject ?? new JObject();

        /// <summary>JS proposalContext + proposalRuleEnabled: every required module enabled, no element, the defenses' protections.</summary>
        private bool ProposalRuleEnabled(JObject rule)
        {
            if (rule == null) return false;
            var modules = (JObject)Content.Profile["modules"];
            foreach (var requirement in (rule["requires"] as JObject ?? new JObject()).Properties())
            {
                var values = Core2Json.Strings(requirement.Value).ToList();
                switch (requirement.Name)
                {
                    case "modules": if (!values.All(m => modules[m] != null && modules[m].Type != JTokenType.Null && !(modules[m].Type == JTokenType.Boolean && !(bool)modules[m]))) return false; break;
                    case "elements": if (values.Count > 0) return false; break;
                    case "protections":
                    {
                        var include = Core2Json.Strings(modules["defenses"]?["include"]).ToList();
                        if (!values.All(include.Contains)) return false;
                        break;
                    }
                }
            }
            return true;
        }

        /// <summary>The behaviour kinds this port runs; an enabled rule naming any other refuses by name (JS has no other).</summary>
        private static bool Behaviors(string kind)
        {
            switch (kind)
            {
                case "persistentProtection": case "turnDamage": case "turnChance": case "lethalThreshold": case "restriction": case "cardTax":
                case "stanceCapacity": case "extraWeapon": case "extraShield": case "perception": case "accuracy": case "greaseFire": case "veil":
                case "evade": case "outgoingMultiplier": case "weaponBonus": case "incomingCap": case "damageToHealing": case "guard": case "reflect":
                case "autoReaction": case "healingToDamage": case "retainProtection":
                    return true;
            }
            return false;
        }

        /// <summary>JS active(actor): each enabled rule's behaviours for the statuses the actor holds with stacks.</summary>
        private List<(string Status, int Stacks, JObject Rule, JObject Behavior)> ActiveBehaviors(Core2Combatant actor)
        {
            var rows = new List<(string, int, JObject, JObject)>();
            if (actor == null) return rows;
            foreach (var pair in actor.Statuses.ToList())
            {
                var stacks = Math.Max(0, pair.Value.Stacks);
                var rule = ProposalRules[pair.Key] as JObject;
                if (stacks == 0 || !ProposalRuleEnabled(rule)) continue;
                foreach (var behavior in (rule["behaviors"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if (!Behaviors((string)behavior["kind"])) throw new InvalidOperationException("Unknown status rule behaviour '" + (string)behavior["kind"] + "'");
                    rows.Add((pair.Key, stacks, rule, behavior));
                }
            }
            return rows;
        }

        private static bool Matches(JToken list, string value) { var items = Core2Json.Strings(list).ToList(); return items.Count == 0 || items.Contains(value); }
        private static int Rounded(double value) => Math.Max(0, (int)Math.Floor(double.IsNaN(value) ? 0 : value));
        private static JObject Job(Core2Combatant actor, Core2Combatant target, JObject data) { data["sourceId"] = actor?.Id ?? "player"; data["targetId"] = target?.Id ?? "player"; return data; }
        private static IReadOnlyList<string> CardTags(Core2Carrier card) => card?.Tags ?? new List<string>();
        private static bool HasAnyTag(Core2Carrier card, JToken tags) { var list = Core2Json.Strings(tags).ToList(); return list.Count == 0 || list.Any(t => CardTags(card).Contains(t)); }
        private static string RangeOfCard(Core2Carrier card) => card?.Profile?.Range ?? CardTags(card).FirstOrDefault(t => t.StartsWith("range:", StringComparison.Ordinal))?.Substring(6);

        /// <summary>JS proposalDamageType: an explicit non-generic type, else the profile's type, its magic type, its physical type or school.</summary>
        private string ProposalDamageType(Core2Carrier card)
        {
            if (card == null) return null;
            var explicitType = Core2DamageTypes.Canonical(card.ProposalDamageType ?? card.Profile?.DamageType);
            if (explicitType != null && explicitType != "magic" && explicitType != "physical") return explicitType;
            var profile = card.Profile ?? new Core2CombatProfile();
            return Core2DamageTypes.Canonical(profile.DamageType ?? Rules.DamageTypes.MagicDamageTypeOf(profile, card.Tags) ?? profile.PhysicalDamageType ?? card.DamageSchool);
        }

        // ---- turn, threshold, retain ------------------------------------------------------------------------------------

        /// <summary>JS proposalTurn: the turn rules' effects (the turn-chance roll on `misc`) and decay, queued as the actor's own.</summary>
        private void ProposalTurn(Core2Combatant actor, string phase)
        {
            var plan = new Core2ProposalPlan();
            foreach (var (status, stacks, _, b) in ActiveBehaviors(actor))
            {
                var kind = (string)b["kind"];
                if (phase == "start" && kind == "persistentProtection")
                {
                    var pool = (string)b["pool"] == "barrier" ? actor.Barrier : actor.Block;
                    var amount = Math.Max(0, stacks - Rounded(pool));
                    if (amount > 0) plan.Effects.Add(Job(actor, actor, new JObject { ["op"] = (string)b["pool"] == "barrier" ? "gainBarrier" : "block", ["amount"] = amount }));
                }
                if (kind == "turnDamage" && (string)b["phase"] == phase)
                    plan.Effects.Add(Job(actor, actor, new JObject { ["op"] = "damage", ["amount"] = Rounded(stacks * (double)b["amountPerStack"]), ["damageType"] = b["damageType"], ["camp"] = b["camp"], ["direct"] = false, ["proposalReactive"] = true }));
                if (kind == "turnChance" && (string)b["phase"] == phase && Math.Floor(Rng.Float("misc") * 100) < (double)b["chance"])
                    plan.Effects.Add(Job(actor, actor, new JObject { ["op"] = "applyStatus", ["status"] = b["status"], ["stacks"] = b["stacks"] }));
            }
            foreach (var pair in actor.Statuses.ToList())
            {
                var rule = ProposalRules[pair.Key] as JObject;
                if (pair.Value.Stacks > 0 && ProposalRuleEnabled(rule) && (string)rule["decay"]?["phase"] == phase)
                    plan.StatusChanges.Add((actor.Id, pair.Key, -Math.Min(pair.Value.Stacks, (int)rule["decay"]["stacks"])));
            }
            ApplyProposalPlan(plan, actor, actor, new Core2Carrier { SkipRatingBonus = true, ProposalProtectionFloor = true });
        }

        /// <summary>JS proposalThreshold: a lethal threshold met by the stacks takes the actor's HP.</summary>
        private void ProposalThreshold(Core2Combatant actor)
        {
            if (actor == null) return;
            var plan = new Core2ProposalPlan();
            foreach (var (_, stacks, _, b) in ActiveBehaviors(actor))
                if ((string)b["kind"] == "lethalThreshold" && actor.Hp > 0 && stacks >= actor.Hp)
                    plan.Effects.Add(Job(actor, actor, new JObject { ["op"] = "loseHp", ["amount"] = actor.Hp, ["damageType"] = b["damageType"], ["proposalReactive"] = true }));
            ApplyProposalPlan(plan, actor, actor, null);
        }

        /// <summary>JS proposalRetainProtection: the Block (or Barrier) the retain rules keep at a turn start.</summary>
        private int ProposalRetained(Core2Combatant actor, string pool = "block")
        {
            var retain = 0;
            foreach (var (_, stacks, _, b) in ActiveBehaviors(actor))
                if ((string)b["kind"] == "retainProtection" && (string)b["pool"] == pool) retain += stacks * (int)b["amountPerStack"];
            return Math.Min(Rounded(pool == "barrier" ? actor.Barrier : actor.Block), retain);
        }

        // ---- cards ------------------------------------------------------------------------------------------------------

        /// <summary>JS proposalCardRules: refusals and taxes while active; extra weapon and shield effects on a matching card.</summary>
        private Core2ProposalPlan ProposalCardPlan(Core2Combatant actor, Core2Carrier card, List<JObject> effects, bool reactive = false)
        {
            var plan = new Core2ProposalPlan();
            var kind = card == null ? null : CarrierKind(card);
            foreach (var (status, stacks, _, b) in ActiveBehaviors(actor))
            {
                var behavior = (string)b["kind"];
                if (behavior == "restriction" && Matches(b["cardTypes"], kind)) { plan.Allowed = false; plan.Refusal = "Restricted by " + status; }
                if (behavior == "cardTax" && Matches(b["ranges"], RangeOfCard(card))) { plan.StaminaTax += (int?)b["stamina"] ?? 0; plan.ManaTax += (int?)b["mana"] ?? 0; }
                if (behavior == "stanceCapacity") plan.AdditionalStanceCapacity += stacks * (int)b["additionalPerStack"];
                if (reactive || card == null || !HasAnyTag(card, b["requiredTags"])) continue;
                if (behavior == "extraWeapon" && kind == "attack")
                {
                    var hit = effects?.FirstOrDefault(e => (string)e["op"] == "damage");
                    var job = (JObject)(hit?.DeepClone() ?? new JObject());
                    job["op"] = "damage"; job["amount"] = hit?["amount"]?.DeepClone() ?? b["fallbackDamage"]; job["target"] = (string)hit?["target"] ?? "enemy";
                    job["sourceId"] = actor.Id; job["proposalReactive"] = true;
                    plan.Effects.Add(job);
                    plan.StatusChanges.Add((actor.Id, status, -Math.Min(stacks, (int)b["consumeStacks"])));
                }
                if (behavior == "extraShield")
                {
                    var protection = effects?.FirstOrDefault(e => (string)e["op"] == "block" || (string)e["op"] == "gainBarrier");
                    plan.Effects.Add(Job(actor, actor, new JObject { ["op"] = (string)protection?["op"] ?? "block", ["amount"] = protection?["amount"]?.DeepClone() ?? b["fallbackBlock"], ["proposalReactive"] = true }));
                    plan.StatusChanges.Add((actor.Id, status, -Math.Min(stacks, (int)b["consumeStacks"])));
                }
            }
            return plan;
        }

        // ---- targeting, hits ---------------------------------------------------------------------------------------------

        private int Perception(Core2Combatant actor)
        {
            var value = actor.Attribute("wisdom") + actor.Attribute("intelligence");
            foreach (var (_, stacks, _, b) in ActiveBehaviors(actor)) if ((string)b["kind"] == "perception") value += stacks * (int)b["amountPerStack"];
            return value;
        }

        /// <summary>
        /// JS proposalTargetPlan: the attacker's accuracy rules, the target's veil (a perception check) and evade (a direct hit
        /// of a matching camp); <paramref name="resolve"/> rolls the checks on `misc` (0..99) and spends the evade stack.
        /// </summary>
        private Core2ProposalPlan ProposalTargetPlan(Core2Combatant actor, Core2Combatant target, Core2Carrier card, bool resolve)
        {
            Core2ProposalPlan Rules(int? perceptionRoll, int? evadeRoll)
            {
                var plan = new Core2ProposalPlan();
                var range = RangeOfCard(card);
                var camp = card?.Profile?.Camp ?? (CardTags(card).Contains("camp:spell") ? "spell" : "physical");
                var damageType = ProposalDamageType(card);
                var direct = card?.ProposalReactive != true;
                foreach (var (_, _, _, b) in ActiveBehaviors(actor))
                    if ((string)b["kind"] == "accuracy" && Matches(b["ranges"], range)) plan.AccuracyDelta += (double)b["delta"];
                foreach (var (status, stacks, _, b) in ActiveBehaviors(target))
                {
                    var kind = (string)b["kind"];
                    if (kind == "greaseFire" && Matches(b["damageTypes"], damageType)) plan.Unavoidable = true;
                    if (kind == "veil" && !((bool?)b["areaBypass"] == true && (card?.Profile?.Targeting == "area" || CardTags(card).Contains("targeting:area"))))
                    {
                        var chance = Math.Max((double)b["minimum"], Math.Min((double)b["maximum"],
                            (double)b["baseChance"] + (Perception(actor) - Perception(target)) * (double)b["perceptionScale"] - stacks * (double)b["penaltyPerStack"]));
                        plan.PerceptionChance = chance;
                        if (perceptionRoll != null && !(perceptionRoll < chance)) plan.Allowed = false;
                    }
                }
                if (!plan.Allowed || plan.Unavoidable || !direct) return plan;
                foreach (var (status, stacks, _, b) in ActiveBehaviors(target))
                {
                    if ((string)b["kind"] != "evade" || !Matches(b["camps"], camp)) continue;
                    plan.EvadeChance = Math.Max(plan.EvadeChance, (double)b["chance"]);
                    if (evadeRoll != null)
                    {
                        plan.Avoided |= evadeRoll < (double)b["chance"];
                        plan.StatusChanges.Add((target.Id, status, -Math.Min(stacks, (int)b["consumeStacks"])));
                    }
                }
                return plan;
            }
            var preview = Rules(null, null);
            if (!resolve) return preview;
            int? perception = preview.PerceptionChance != null ? (int)Math.Floor(Rng.Float("misc") * 100) : (int?)null;
            int? evade = preview.EvadeChance > 0 ? (int)Math.Floor(Rng.Float("misc") * 100) : (int?)null;
            return Rules(perception, evade);
        }

        /// <summary>JS proposalHitPlan (configured receipts: the primary damage is not rounded here).</summary>
        private Core2ProposalPlan ProposalHitPlan(Core2Combatant actor, Core2Combatant target, double amount, Core2Carrier card, bool roundDamage = false)
        {
            Func<double, double> quantity = roundDamage ? v => Rounded(v) : v => Math.Max(0, double.IsNaN(v) ? 0 : v);
            var damageType = ProposalDamageType(card);
            var camp = card?.Profile?.Camp ?? "physical";
            var reactive = card?.ProposalReactive == true;
            var direct = !reactive;
            var original = quantity(amount);
            var plan = new Core2ProposalPlan { Amount = original };
            double outgoing = 1;
            foreach (var (_, _, _, b) in ActiveBehaviors(actor))
            {
                var kind = (string)b["kind"];
                if (!reactive && kind == "outgoingMultiplier" && ((bool?)b["directOnly"] != true || direct)) outgoing *= (double)b["multiplier"];
                if (kind == "weaponBonus" && direct && !reactive && HasAnyTag(card, b["requiredTags"]))
                    plan.Effects.Add(Job(actor, target, new JObject { ["op"] = "damage", ["amount"] = b["amount"], ["damageType"] = b["damageType"], ["camp"] = b["camp"], ["proposalReactive"] = true }));
            }
            plan.Amount = quantity(plan.Amount * outgoing);
            var incomingCap = double.PositiveInfinity;
            double? healingMultiplier = null;
            var allies = target == null ? new List<Core2Combatant>() : (target.Side == Core2Side.Player ? new List<Core2Combatant> { Player } : Enemies).Where(a => a.Alive).ToList();
            foreach (var (status, stacks, _, b) in ActiveBehaviors(target))
            {
                var kind = (string)b["kind"];
                if (kind == "greaseFire" && Matches(b["damageTypes"], damageType))
                {
                    plan.Unavoidable = true; plan.Amount = quantity(plan.Amount * (double)b["multiplier"]);
                    plan.Effects.Add(Job(actor, target, new JObject { ["op"] = "applyStatus", ["status"] = b["burnStatus"], ["stacks"] = b["burnStacks"] }));
                    if (!reactive) foreach (var ally in allies.Where(a => a.Id != target.Id && a.Hp > 0))
                        plan.Effects.Add(Job(actor, ally, new JObject { ["op"] = "damage", ["amount"] = Rounded(original * (double)b["splashMultiplier"]), ["damageType"] = damageType, ["camp"] = camp, ["proposalReactive"] = true }));
                    plan.StatusChanges.Add((target.Id, status, -Math.Min(stacks, (int)b["consumeStacks"])));
                }
                if (kind == "incomingCap" && ((bool?)b["directOnly"] != true || direct)) incomingCap = Math.Min(incomingCap, (double)b["maximum"]);
                if (kind == "damageToHealing" && Matches(b["damageTypes"], damageType)) healingMultiplier = (double)b["multiplier"];
            }
            if (healingMultiplier != null) { plan.Healing = Rounded(plan.Amount * healingMultiplier.Value); plan.Amount = 0; }
            else plan.Amount = Math.Min(plan.Amount, incomingCap);
            if (direct && !reactive && plan.Amount > 0 && target != null)
            {
                foreach (var guard in allies.Where(a => a.Id != target.Id && a.Hp > 0).OrderBy(a => a.Id, StringComparer.Ordinal))
                {
                    var guarded = false;
                    foreach (var (status, stacks, _, b) in ActiveBehaviors(guard))
                    {
                        if ((string)b["kind"] != "guard") continue;
                        var intercepted = Math.Min(plan.Amount, stacks * (double)b["amountPerStack"]);
                        plan.Amount -= intercepted;
                        plan.Effects.Add(Job(actor, guard, new JObject { ["op"] = "damage", ["amount"] = intercepted, ["damageType"] = damageType, ["camp"] = camp, ["proposalReactive"] = true }));
                        plan.StatusChanges.Add((guard.Id, status, -Math.Min(stacks, (int)b["consumeStacks"])));
                        guarded = true;
                        break;
                    }
                    if (guarded) break;
                }
            }
            return plan;
        }

        /// <summary>JS proposalAfterHit: persistent protection spent, a reflection and the automatic reactions of the hit target.</summary>
        private void ProposalAfterHit(Core2Combatant actor, Core2Combatant target, double incomingDamage, int hpDamage, Core2Carrier card)
        {
            var plan = new Core2ProposalPlan();
            var damageType = ProposalDamageType(card);
            var camp = card?.Profile?.Camp ?? "physical";
            var range = RangeOfCard(card);
            var reactive = card?.ProposalReactive == true;
            var direct = !reactive;
            var opponents = actor == null ? new List<Core2Combatant>() : (actor.Side == Core2Side.Player ? new List<Core2Combatant> { Player } : Enemies).Where(a => a.Alive).ToList();
            foreach (var (status, stacks, _, b) in ActiveBehaviors(target))
            {
                var kind = (string)b["kind"];
                if (kind == "persistentProtection" && direct && camp == (string)b["camp"] && incomingDamage >= (double)b["minimumIncomingDamage"])
                    plan.StatusChanges.Add((target.Id, status, -Math.Min(stacks, 1)));
                if (!direct || reactive || actor?.Id == target.Id) continue;
                if (kind == "reflect" && hpDamage > 0)
                    plan.Effects.Add(Job(target, actor, new JObject { ["op"] = "damage", ["amount"] = Rounded(hpDamage * (double)b["multiplier"]), ["damageType"] = damageType, ["camp"] = camp, ["proposalReactive"] = true }));
                if (kind == "autoReaction" && incomingDamage > 0 && Matches(b["ranges"], range))
                {
                    var targets = (string)b["mode"] == "sweep" ? opponents.Where(o => o.Hp > 0).ToList() : new List<Core2Combatant> { actor };
                    foreach (var opponent in targets)
                        plan.Effects.Add(Job(target, opponent, new JObject { ["op"] = "damage", ["amount"] = b["amount"], ["damageType"] = b["damageType"], ["camp"] = b["camp"], ["proposalReactive"] = true }));
                    plan.StatusChanges.Add((target.Id, status, -Math.Min(stacks, (int)b["consumeStacks"])));
                }
            }
            ApplyProposalPlan(plan, actor, target, card);
        }

        /// <summary>JS proposalHealingPlan: healing turned to damage by a matching rule.</summary>
        private Core2ProposalPlan ProposalHealing(Core2Combatant target, int amount, string damageType, Core2Carrier card)
        {
            var plan = new Core2ProposalPlan { Healing = Rounded(amount), DamageType = damageType ?? ProposalDamageType(card) };
            foreach (var (_, _, _, b) in ActiveBehaviors(target))
                if ((string)b["kind"] == "healingToDamage" && Matches(b["damageTypes"], plan.DamageType))
                { plan.Damage = Rounded(plan.Healing * (double)b["multiplier"]); plan.Healing = 0; plan.DamageType = (string)b["damageType"]; }
            return plan;
        }

        /// <summary>
        /// JS applyProposalPlan: each stack change as a removeStatus / applyStatus of the actor it names, then each effect from
        /// its source at its target, on a copy of the card marked reactive (typed by the effect, camp kept), queued.
        /// </summary>
        private void ApplyProposalPlan(Core2ProposalPlan plan, Core2Combatant source, Core2Combatant target, Core2Carrier card)
        {
            Core2Combatant FindActor(string id) => (id == null ? null : Find(id)) ?? target ?? source;
            foreach (var (actorId, status, delta) in plan.StatusChanges)
            {
                var entity = FindActor(actorId);
                var effect = new JObject { ["op"] = delta < 0 ? "removeStatus" : "applyStatus", ["status"] = status };
                if (delta < 0) effect["amount"] = -delta; else effect["stacks"] = delta;
                Enqueue(new Core2FightAction { Effect = effect, Source = entity, Owner = entity, Target = entity });
            }
            foreach (var job in plan.Effects)
            {
                var effect = (JObject)job.DeepClone();
                var sourceId = (string)effect["sourceId"]; var targetId = (string)effect["targetId"];
                var reactive = (bool?)effect["proposalReactive"] == true; var damageType = (string)effect["damageType"]; var camp = (string)effect["camp"];
                foreach (var key in new[] { "sourceId", "targetId", "proposalReactive", "damageType", "camp", "direct" }) effect.Remove(key);
                var actor = (sourceId == null ? null : Find(sourceId)) ?? source;
                var recipient = (targetId == null ? null : Find(targetId)) ?? target;
                var carrier = card?.Clone() ?? new Core2Carrier();
                carrier.ProposalReactive = reactive; carrier.ProposalDamageType = damageType;
                carrier.DamageSchool = damageType ?? card?.DamageSchool;
                carrier.Profile ??= new Core2CombatProfile();
                carrier.Profile.Camp = camp ?? card?.Profile?.Camp ?? "physical";
                carrier.SkipRatingBonus = reactive || card?.SkipRatingBonus == true;
                Enqueue(new Core2FightAction { Effect = effect, Source = actor, Owner = actor, Target = recipient, Card = carrier });
            }
        }

        // ---- stance capacity (concurrentStances.js) ---------------------------------------------------------------------

        /// <summary>JS stanceCapacity: 1 plus the stanceCapacity rules' extra stances, at most the rule's maxStances (2).</summary>
        private int StanceCapacity(Core2Combatant entity)
        {
            JObject policy = null;
            foreach (var pair in entity.Statuses)
            {
                if (pair.Value.Stacks <= 0 || !(ProposalRules[pair.Key] is JObject rule)) continue;
                policy = (rule["behaviors"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(b => (string)b["kind"] == "stanceCapacity");
                if (policy != null) break;
            }
            var extra = ProposalCardPlan(entity, null, null).AdditionalStanceCapacity;
            return Math.Min((int?)policy?["maxStances"] ?? 2, Math.Max(1, 1 + extra));
        }

        /// <summary>JS rememberCombatStance: with room for more than one, the previous and new stances are kept (Staggered never).</summary>
        private void RememberCombatStance(Core2Combatant entity, Core2Stance previous)
        {
            var capacity = StanceCapacity(entity);
            if (capacity <= 1) { entity.ConcurrentStances.Clear(); return; }
            var unique = new List<(string Key, Core2Stance Stance)>();
            foreach (var stance in entity.ConcurrentStances.Concat(new[] { previous, entity.Stance }))
            {
                if (stance == null || stance.Maneuver == "staggered") continue;
                var key = stance.Camp + ":" + stance.Maneuver;
                var at = unique.FindIndex(u => u.Key == key);
                if (at >= 0) unique[at] = (key, stance); else unique.Add((key, stance));
            }
            entity.ConcurrentStances = unique.Skip(Math.Max(0, unique.Count - capacity)).Select(u => u.Stance).ToList();
        }

        /// <summary>JS trimCombatStances: a capacity of one forgets the remembered stances; more keeps the active ones.</summary>
        private void TrimCombatStances(Core2Combatant entity)
        {
            if (StanceCapacity(entity) <= 1) entity.ConcurrentStances.Clear();
            else entity.ConcurrentStances = ActiveCombatStances(entity);
        }

        /// <summary>
        /// JS defensiveCombatStance: with two or more active stances, the one that takes the least from this carrier (its
        /// advantage penalty × mitigation), ties broken by the rule's defense priority.
        /// </summary>
        private Core2Stance DefensiveCombatStance(Core2Combatant entity, Core2Carrier carrier)
        {
            var rows = ActiveCombatStances(entity);
            if (rows.Count < 2) return entity?.Stance;
            var raw = carrier?.Profile?.Maneuver;
            var incoming = raw == "casting" ? "prepared" : raw;
            JObject policy = null;
            foreach (var pair in entity.Statuses)
                if (pair.Value.Stacks > 0 && ProposalRules[pair.Key] is JObject rule
                    && (rule["behaviors"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(b => (string)b["kind"] == "stanceCapacity") is JObject found) { policy = found; break; }
            var priority = Core2Json.Strings(policy?["defensePriority"]).ToList();
            if (priority.Count == 0) priority = new List<string> { "counter", "defend", "evade", "casting", "attack", "smash", "sweep" };
            double Factor(Core2Stance stance) => (Core2Stances.StanceAdvantage(Rules, incoming, stance.Maneuver) ? Rules.StanceAdvantageMultiplier : 1)
                * Core2Stances.StanceMitigationMultiplier(Rules, incoming, stance.Maneuver, out _);
            int Rank(Core2Stance stance) => priority.IndexOf(stance.Maneuver == "prepared" ? "casting" : stance.Maneuver);
            return rows.Select((stance, index) => (stance, index)).OrderBy(r => Factor(r.stance)).ThenBy(r => Rank(r.stance)).ThenBy(r => r.index).First().stance;
        }
    }
}
