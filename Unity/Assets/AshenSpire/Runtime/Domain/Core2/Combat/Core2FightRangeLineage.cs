// Core2FightRangeLineage.cs — the causal range envelope of a Core 2 action: before an action pays, every hook its own
// effects could set off (a relic's, a status's) is walked, and the hostile contacts those hooks would make join the
// action's range plan (their Advance is paid with it). The walk's sites and edges are kept on the accepted plan; when a
// hook then fires from one of those sites it rides the plan (its targets the sampled ones, its contacts the reserved
// riders) instead of being quoted on its own. A walk that cannot end refuses the action.
//
// JS at the pin:
//   src/engine/combatRangeLineage.js possible (a predicate that could pass on a planned event), rawEventsFor / eventsFor
//                                    (the trigger events an effect can raise), gateSpent, sourceStatuses, sampledTargets,
//                                    extendRangeEnvelope (sites, edges, rider contacts, the cardPlayed / cardResolved
//                                    lifecycle sites; maxEvents 2048, maxDepth 16), eventRangeOrigin, inheritedRangeAction,
//                                    rangeTargetsFor, inheritedContactId, acceptIndependentTrigger
//   src/engine/combatEngagement.js   quoteRangeActions (a preview samples on a detached RNG; acceptance samples once and
//                                    keeps the targets), acceptRangeActions (the accepted plan carries the lineage)
//   src/engine/triggers.js           rangeHookSources (an owner's property mounts in sorted source order, then its statuses)
//   src/content/combatRules.js       triggers.maxEvents / maxDepth
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One site of a range envelope (JS graph.sites[]): an entity an effect reaches, the events it can raise, its delivery.</summary>
    public sealed class Core2RangeSite
    {
        public string Id, TargetKey, ParentId;
        public List<(string Type, string Status)> Events = new List<(string, string)>();
        public Core2RangeSource Delivery;
    }

    /// <summary>One edge (JS graph.edges[]): a hook's effect firing on a site's event, and the sites it reaches.</summary>
    public sealed class Core2RangeEdge
    {
        public string Id, ParentId, Event, Status, TriggerKey;
        public int EffectIndex;
        public List<(string TargetKey, string SiteId, string ContactId, int Batch)> Targets = new List<(string, string, string, int)>();
    }

    /// <summary>JS rangeLineagePlan: the accepted envelope of one root action.</summary>
    public sealed class Core2RangeLineage
    {
        public string RootId, SourceKey;
        public List<Core2RangeSite> Sites = new List<Core2RangeSite>();
        public List<Core2RangeEdge> Edges = new List<Core2RangeEdge>();
    }

    public sealed partial class Core2RangePlan
    {
        /// <summary>The envelope the quote walked (JS plan.lineage), or null when no hostile hook can follow.</summary>
        public Core2RangeLineage Lineage { get; set; }
    }

    public sealed partial class Core2Carrier
    {
        /// <summary>JS carrier.rangePlan for an action outside a group (an accepted trigger, an inherited hook).</summary>
        public Core2RangePlan RangePlan { get; set; }
        /// <summary>JS carrier.rangeLineagePlan: the root's accepted envelope.</summary>
        public Core2RangeLineage RangeLineage { get; set; }
        /// <summary>JS carrier.rangeLineage.edgeId: the envelope edge an inherited hook rides.</summary>
        public string RangeEdgeId { get; set; }
    }

    public sealed partial class Core2FightEvent
    {
        /// <summary>JS event.rangeOrigin: the envelope site of the action that raised it (root, site, source key).</summary>
        public (string RootId, string SiteId, string SourceKey)? RangeOrigin { get; set; }
    }

    public sealed partial class Core2Fight
    {
        private static readonly HashSet<string> LineageHostile = new HashSet<string>(StringComparer.Ordinal)
            { "damage", "applyStatus", "buildup", "poiseDamage", "wardDamage", "loseHp", "stagger", "arcaneBuildup" };
        private static readonly HashSet<string> FriendlyTargets = new HashSet<string>(StringComparer.Ordinal) { "self", "owner", "player", "ally", "allAllies" };
        private const int LineageMaxEvents = 2048, LineageMaxDepth = 16;

        /// <summary>JS schemas.js TRIGGER_EVENTS less the events rawEventsFor names that no hook can listen to.</summary>
        private static readonly HashSet<string> NotTriggerEvents = new HashSet<string>(StringComparer.Ordinal) { "attackHit", "attackMissed", "staminaRestored" };

        /// <summary>JS possible: whether a hook's predicate could pass on a planned event.</summary>
        private static bool Possible(JToken pred, (string Type, string Status) ev, string sourceKey, string targetKey, IReadOnlyList<string> cardTags)
        {
            if (!(pred is JObject p)) return true;
            switch ((string)p["p"])
            {
                case "all": return (p["preds"] as JArray ?? new JArray()).All(row => Possible(row, ev, sourceKey, targetKey, cardTags));
                case "any": return (p["preds"] as JArray ?? new JArray()).Any(row => Possible(row, ev, sourceKey, targetKey, cardTags));
                case "eventStatusIs": return ev.Status == (string)p["status"];
                case "eventTargetIsOwner": return targetKey == sourceKey;
                case "eventSourceIsOwner": return true;
                case "eventIsAttack": return ev.Type == "damageDealt";
                case "cardTagIs": return cardTags.Contains((string)p["tag"]);
            }
            return true;
        }

        /// <summary>JS rawEventsFor filtered to TRIGGER_EVENTS.</summary>
        private static List<(string Type, string Status)> EventsFor(JObject effect, Core2Combatant target, Core2Fight fight)
        {
            var op = (string)effect["op"];
            var arcane = target?.Side == Core2Side.Enemy && fight.EnemyDef(target).Raw["arcaneExposure"] is JObject;
            var rows = op switch
            {
                "damage" => new List<(string, string)> { ("damageDealt", null), ("hpLost", null), ("attackHit", null), ("attackMissed", null), ("enemyStaggered", null), ("enemyDied", null), ("blockBroken", null) }
                    .Concat(arcane ? new[] { ("arcaneBreak", (string)null) } : new (string, string)[0]).Concat(new[] { ("statusApplied", "vulnerable") }).ToList(),
                "applyStatus" or "buildup" => new List<(string, string)> { ("statusApplied", (string)effect["status"]) },
                "arcaneBuildup" => arcane ? new List<(string, string)> { ("arcaneBreak", null), ("statusApplied", (string)fight.EnemyDef(target).Raw["arcaneExposure"]?["onBreak"]?["status"]) } : new List<(string, string)>(),
                "poiseDamage" or "stagger" => new List<(string, string)> { ("poiseBroken", null), ("enemyStaggered", null), ("statusApplied", "vulnerable") },
                "wardDamage" => new List<(string, string)> { ("wardBroken", null) },
                "heal" => new List<(string, string)> { ("healed", null) },
                "restoreMana" => new List<(string, string)> { ("manaRestored", null) },
                "restoreStamina" => new List<(string, string)> { ("staminaRestored", null) },
                "enterStance" => new List<(string, string)> { ("stanceEntered", null), ("stanceExited", null) },
                "loseHp" => new List<(string, string)> { ("hpLost", null) },
                _ => new List<(string, string)>(),
            };
            return rows.Where(r => !NotTriggerEvents.Contains(r.Item1)).ToList();
        }

        /// <summary>JS rangeHookSources: the owner's hooks with their gate keys (property mounts sorted, then statuses).</summary>
        private List<(string Key, JObject Trigger)> RangeHookSources(Core2Combatant owner, IEnumerable<string> additionalStatuses)
        {
            var rows = new List<(string, JObject)>();
            if (owner == null) return rows;
            if (owner.Side == Core2Side.Player)
                foreach (var mount in _mounts.OrderBy(m => m.SourceKey, StringComparer.Ordinal))
                    for (var i = 0; i < mount.Triggers.Count; i++) rows.Add(("property:player:" + mount.SourceKey + ":" + i, mount.Triggers[i]));
            foreach (var id in owner.Statuses.Keys.Concat(additionalStatuses).Distinct(StringComparer.Ordinal).ToList())
            {
                var hooks = StatusHooks(id);
                for (var i = 0; i < hooks.Count; i++) rows.Add(("status:" + owner.Id + ":" + id + ":" + i, hooks[i]));
            }
            return rows;
        }

        /// <summary>JS sourceStatuses: the statuses the source holds, its actions give itself, and its hooks could give itself.</summary>
        private List<string> SourceStatuses(Core2Combatant source, IEnumerable<Core2FightAction> actions)
        {
            var ids = new List<string>(source.Statuses.Keys);
            foreach (var action in actions)
                if ((string)action.Effect["op"] == "applyStatus" && FriendlySelf((string)action.Effect["target"]) && !ids.Contains((string)action.Effect["status"])) ids.Add((string)action.Effect["status"]);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (_, trigger) in RangeHookSources(source, ids))
                    foreach (var effect in (trigger["do"] as JArray ?? new JArray()).OfType<JObject>())
                        if ((string)effect["op"] == "applyStatus" && FriendlySelf((string)effect["target"]) && !ids.Contains((string)effect["status"])) { ids.Add((string)effect["status"]); changed = true; }
            }
            return ids;
            static bool FriendlySelf(string target) => target == "self" || target == "owner";
        }

        /// <summary>JS gateSpent: a once trigger that fired, or a turn limit reached this turn.</summary>
        private bool GateSpent(string key, JObject trigger)
        {
            var has = _triggerState.TryGetValue(key, out var st);
            return (bool?)trigger["once"] == true && has && st.Fires > 0
                || trigger["limitPerTurn"] != null && trigger["limitPerTurn"].Type != JTokenType.Null && has && st.Turn == Turn && st.TurnFires >= (int)trigger["limitPerTurn"];
        }

        /// <summary>
        /// JS extendRangeEnvelope: walk the hooks each action's effects can set off, adding a rider contact for every hostile
        /// effect at an opponent; null when no owned hook is hostile or no edge is found. Non-preview, each action keeps its
        /// root sites per batch (rangeSiteBatches).
        /// </summary>
        private Core2RangeLineage ExtendRangeEnvelope(Core2Combatant source, List<Core2FightAction> actions, List<Core2RangeSource> contacts, bool preview, Core2Carrier carrier, Core2Rng rng)
        {
            if (Range == null || !Range.Policy.Enabled) return null;
            var sourceKey = Core2Range.ActorKey(source);
            var hooks = RangeHookSources(source, SourceStatuses(source, actions));
            if (!hooks.Any(h => (h.Trigger["do"] as JArray ?? new JArray()).OfType<JObject>().Any(e => LineageHostile.Contains((string)e["op"] ?? "") && !FriendlyTargets.Contains((string)e["target"] ?? ""))))
                return null;
            var graph = new Core2RangeLineage { SourceKey = sourceKey };
            Core2RangeSource Flags(Core2RangeSource row) => new Core2RangeSource { Coverage = row.Coverage, Weapon = row.Weapon, ThrowCapable = row.ThrowCapable, Bypass = row.Bypass, SourceWeaponId = row.SourceWeaponId };
            void Visit(Core2RangeSite site, IReadOnlyList<string> cardTags, List<string> path)
            {
                if (graph.Sites.Count > LineageMaxEvents || graph.Edges.Count > LineageMaxEvents || path.Count > LineageMaxDepth)
                    throw new Core2PlayRefusedException("This action has an unbounded range-trigger envelope", "range", 0, 0);
                var target = FindByRangeKey(site.TargetKey);
                foreach (var ev in site.Events)
                    foreach (var (key, trigger) in hooks)
                    {
                        if ((string)trigger["on"] != ev.Type || GateSpent(key, trigger) || !Possible(trigger["if"], ev, sourceKey, site.TargetKey, cardTags)) continue;
                        var prior = path.Count(k => k == key);
                        int? limit = (bool?)trigger["once"] == true ? 1 : trigger["limitPerTurn"] != null && trigger["limitPerTurn"].Type != JTokenType.Null ? (int)trigger["limitPerTurn"] : (int?)null;
                        if (limit != null && prior >= limit) continue;
                        var effects = (trigger["do"] as JArray ?? new JArray()).OfType<JObject>().ToList();
                        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
                        {
                            var effect = effects[effectIndex];
                            var edge = new Core2RangeEdge { Id = "e" + graph.Edges.Count, ParentId = site.Id, Event = ev.Type, Status = ev.Status, TriggerKey = key, EffectIndex = effectIndex };
                            graph.Edges.Add(edge);
                            var probe = new Core2FightAction { Effect = effect, Source = source, Owner = source, Target = target };
                            var repeats = Math.Max(0, (int)Eval(probe, effect["repeat"], 1, target));
                            var hits = (string)effect["op"] == "damage" ? Math.Max(0, (int)Eval(probe, effect["hits"], 1, target)) : 1;
                            for (var r = 0; r < repeats; r++) for (var h = 0; h < hits; h++)
                                foreach (var child in ResolveTargets(probe, (string)effect["target"], rng))
                                {
                                    var targetKey = Core2Range.ActorKey(child);
                                    var childId = "s" + graph.Sites.Count;
                                    var delivery = effect["attack"] != null || effect["tags"] != null ? Flags(Core2RangeSource.For(new Core2Carrier { Profile = new Core2CombatProfile { Range = EffectRange(effect) } }, targetKey)) : site.Delivery;
                                    var contactId = LineageHostile.Contains((string)effect["op"] ?? "") && source.Side != child.Side ? "rider:" + childId : null;
                                    if (contactId != null)
                                        contacts.Add(new Core2RangeSource { ContactId = contactId, TargetKey = targetKey, Coverage = delivery.Coverage, Weapon = delivery.Weapon, ThrowCapable = delivery.ThrowCapable, Bypass = delivery.Bypass, SourceWeaponId = delivery.SourceWeaponId });
                                    var childSite = new Core2RangeSite { Id = childId, TargetKey = targetKey, ParentId = site.Id, Events = EventsFor(effect, child, this), Delivery = delivery };
                                    graph.Sites.Add(childSite);
                                    edge.Targets.Add((targetKey, childId, contactId, r * hits + h));
                                    Visit(childSite, new List<string>(), path.Concat(new[] { key }).ToList());
                                }
                        }
                    }
            }
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                var effect = action.Effect;
                if (EventsFor(effect, action.Target, this).Count == 0) continue;
                var repeats = Math.Max(0, (int)Eval(action, effect["repeat"], 1, action.Target));
                var hits = (string)effect["op"] == "damage" ? Math.Max(0, (int)Eval(action, effect["hits"], 1, action.Target)) : 1;
                var batches = new List<List<(Core2Combatant, string)>>();
                for (var r = 0; r < repeats; r++) for (var h = 0; h < hits; h++)
                {
                    var batch = r * hits + h;
                    var targets = action.RangeTargetBatches != null && batch < action.RangeTargetBatches.Count ? action.RangeTargetBatches[batch]
                        : ResolveTargets(new Core2FightAction { Effect = effect, Source = source, Owner = source, Target = action.Target, Card = action.Card }, (string)effect["target"], rng);
                    var refs = new List<(Core2Combatant, string)>();
                    foreach (var target in targets)
                    {
                        var id = "s" + graph.Sites.Count;
                        var site = new Core2RangeSite { Id = id, TargetKey = Core2Range.ActorKey(target), Events = EventsFor(effect, target, this), Delivery = Flags(RangeSourceFor(source, action.Card, Core2Range.ActorKey(target))) };
                        graph.Sites.Add(site);
                        refs.Add((target, id));
                        Visit(site, action.Card?.Tags ?? new List<string>(), new List<string>());
                    }
                    batches.Add(refs);
                }
                if (!preview) action.RangeSiteBatches = batches;
            }
            if (carrier?.InstanceId != null && carrier.Profile?.CounterReturnAttack != true && source.Side == Core2Side.Player)
                foreach (var type in new[] { "cardPlayed", "cardResolved" })
                {
                    var target = actions.FirstOrDefault(a => a.Target != null)?.Target ?? source;
                    var site = new Core2RangeSite { Id = "lifecycle:" + type, TargetKey = Core2Range.ActorKey(target), Events = { (type, null) }, Delivery = Flags(RangeSourceFor(source, carrier, Core2Range.ActorKey(target))) };
                    graph.Sites.Add(site);
                    Visit(site, carrier.Tags, new List<string>());
                }
            if (graph.Edges.Count == 0)
            {
                if (!preview) foreach (var action in actions) action.RangeSiteBatches = null;
                return null;
            }
            return graph;
        }

        private static string EffectRange(JObject effect) => Core2Json.Strings(effect["tags"]).FirstOrDefault(t => t.StartsWith("range:", StringComparison.Ordinal))?.Substring(6) ?? (string)effect["attack"]?["range"] ?? "melee";

        private Core2Combatant FindByRangeKey(string key) => new[] { Player }.Concat(Enemies).FirstOrDefault(e => Core2Range.ActorKey(e) == key);

        /// <summary>JS eventRangeOrigin: the envelope site of the acting carrier that raised this event, or null.</summary>
        private (string RootId, string SiteId, string SourceKey)? EventRangeOrigin(string type, JObject payload, Core2FightAction action, Core2Carrier lifecycle)
        {
            var carrier = action?.Card ?? lifecycle;
            var graph = carrier?.RangeLineage;
            if (graph == null) return null;
            if (type == "cardPlayed" || type == "cardResolved")
            {
                var lifeSite = graph.Sites.FirstOrDefault(s => s.Id == "lifecycle:" + type);
                return lifeSite == null ? null : (graph.RootId, lifeSite.Id, graph.SourceKey);
            }
            if (action == null) return null;
            var targetId = (string)payload["targetId"];
            var targetKey = targetId == null ? graph.SourceKey : targetId == "player" ? "player:player" : "enemy:" + targetId;
            var hits = (string)action.Effect?["op"] == "damage" ? Math.Max(0, (int)Eval(action, action.Effect["hits"], 1, action.Target)) : 1;
            var batch = action.ExecRepeat * hits + action.RangeHitIndex;
            var refs = action.RangeSiteBatches != null && batch < action.RangeSiteBatches.Count ? action.RangeSiteBatches[batch] : new List<(Core2Combatant, string)>();
            var status = (string)payload["status"];
            var site = refs.Select(r => graph.Sites.FirstOrDefault(s => s.Id == r.Item2)).FirstOrDefault(s => s != null && s.TargetKey == targetKey
                && s.Events.Any(e => e.Type == type && (e.Status == null || e.Status == status)));
            return site == null ? null : (graph.RootId, site.Id, graph.SourceKey);
        }

        /// <summary>
        /// JS inheritedRangeAction: a hook fired from an envelope site rides the root's plan — its edge's sampled targets,
        /// its reserved rider contacts — instead of being quoted on its own. A missing envelope or edge refuses.
        /// </summary>
        private (Core2Carrier Carrier, List<List<(Core2Combatant, string)>> Batches)? InheritedRangeAction(string key, Core2Combatant owner, Core2FightEvent ev, int effectIndex, Core2Carrier parentCarrier)
        {
            if (ev.RangeOrigin == null) return null;
            var origin = ev.RangeOrigin.Value;
            if (Core2Range.ActorKey(owner) != origin.SourceKey) return null;
            var parent = parentCarrier ?? _currentAction?.Card ?? _pendingPlay?.Carrier;
            var graph = parent?.RangeLineage; var plan = parent?.RangePlan;
            if (graph == null || plan == null || graph.RootId != origin.RootId) throw new Core2PlayRefusedException("The committed trigger envelope is missing", "range-lineage", 0, 0);
            var status = ev.Type == "statusApplied" ? (string)ev.Data["status"] : null;
            var edge = graph.Edges.FirstOrDefault(e => e.ParentId == origin.SiteId && e.Event == ev.Type && e.Status == status && e.TriggerKey == key && e.EffectIndex == effectIndex)
                ?? throw new Core2PlayRefusedException("This trigger is outside the committed range envelope", "range-lineage", 0, 0);
            var batches = new List<List<(Core2Combatant, string)>>();
            foreach (var row in edge.Targets)
            {
                var entity = FindByRangeKey(row.TargetKey);
                if (entity == null) continue;
                while (batches.Count <= row.Batch) batches.Add(new List<(Core2Combatant, string)>());
                batches[row.Batch].Add((entity, row.SiteId));
            }
            var carrier = new Core2Carrier { RangePlan = plan, RangeLineage = graph, RangeEdgeId = edge.Id };
            var first = graph.Sites.FirstOrDefault(s => s.Id == edge.Targets.FirstOrDefault().SiteId);
            if (first?.Delivery != null) carrier.Profile = new Core2CombatProfile { Range = first.Delivery.Coverage };
            return (carrier, batches);
        }

        /// <summary>JS inheritedContactId: the rider contact an inherited hook's hit uses against this target.</summary>
        private string InheritedContactId(Core2FightAction action, Core2Combatant target)
        {
            var carrier = action.Card;
            var edge = carrier?.RangeLineage?.Edges.FirstOrDefault(e => e.Id == carrier.RangeEdgeId);
            if (edge == null) return null;
            var hits = (string)action.Effect["op"] == "damage" ? Math.Max(0, (int)Eval(action, action.Effect["hits"], 1, action.Target)) : 1;
            var batch = action.ExecRepeat * hits + action.RangeHitIndex;
            var key = Core2Range.ActorKey(target);
            return edge.Targets.FirstOrDefault(row => row.Batch == batch && row.TargetKey == key).ContactId;
        }

        /// <summary>JS rangeTargetsFor: an inherited hook's sampled targets, or an accepted action's committed targets, for this hit.</summary>
        private List<Core2Combatant> RangeTargetsFor(Core2FightAction action)
        {
            if (action.Card?.RangeEdgeId == null && action.RangeTargetBatches == null) return null;
            var hits = (string)action.Effect?["op"] == "damage" ? Math.Max(0, (int)Eval(action, action.Effect["hits"], 1, action.Target)) : 1;
            var batch = action.ExecRepeat * hits + action.RangeHitIndex;
            if (action.Card?.RangeEdgeId != null)
                return (action.RangeSiteBatches != null && batch < action.RangeSiteBatches.Count ? action.RangeSiteBatches[batch] : new List<(Core2Combatant, string)>()).Select(r => r.Item1).Where(e => e.Alive).ToList();
            return (batch < action.RangeTargetBatches.Count ? action.RangeTargetBatches[batch] : new List<Core2Combatant>()).Where(e => e.Alive).ToList();
        }
    }
}
