// Core2Range.cs — optional engagement, Advance and Throw (JS docs/design/RangeAdvance.md, a normative amendment to
// §17): every opposing actor pair holds one band (0 Melee, 1 Reach, 2 Ranged); an attack's committed source covers a
// band; a gap is closed by Advance (+SP per band, paid once per action) or, for a Throw-capable weapon, by one band of
// Throw (-Precision); a Reach weapon at actual Melee engagement gains Precision. Phase 5a took the resulting contact
// (inRange, precisionDelta) as an input; this is the planner that produces it.
//
// JS at the pin:
//   src/model/combatEngagement.js   RANGE_POLICY_DEFAULTS, rangePolicyProblems, snapshotRangePolicy, rangeBand,
//                                   rangePairKey, opposingRangeKeys, createRangeState, rangeStateProblems,
//                                   engagementBand, movedEngagementState, rangeActionPlan
//   src/engine/combatEngagement.js  initializeCombatRange (starting bands), rangeSourceContact (coverage of the
//                                   committed source), rangeHitContact (inRange / precisionDelta), addRangeCosts,
//                                   executeEngagementMovement (Advance, Closing Dodge, Backstep: moveEngagement ±1)
//   src/model/rangeCardAvailability.js pureEngagementMovement (a movement-only card refuses play with range off)
//   content/source/fixCoreMechanics.json range: version 1, enabled, advanceSpPerBand 1, reachPrecisionBonus 1,
//                                   throwPrecisionPenalty 1
// Resolving which armament a card swings (loadout, sourceArmamentId, hands) is gear work (phase 8): callers name the
// committed source (Core2RangeSource); a source not named is the carrier's own coverage, as for an enemy move.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>The versioned range policy a fight snapshots (JS snapshotRangePolicy).</summary>
    public sealed class Core2RangePolicy
    {
        public int Version { get; }
        public bool Enabled { get; }
        public int AdvanceSpPerBand { get; }
        public int ReachPrecisionBonus { get; }
        public int ThrowPrecisionPenalty { get; }

        public Core2RangePolicy(bool enabled = true, int advanceSpPerBand = 1, int reachPrecisionBonus = 1, int throwPrecisionPenalty = 1, int version = 1)
        {
            if (version != 1 || advanceSpPerBand < 0 || reachPrecisionBonus < 0 || throwPrecisionPenalty < 0) throw new InvalidDataException("Invalid range policy");
            Version = version; Enabled = enabled; AdvanceSpPerBand = advanceSpPerBand; ReachPrecisionBonus = reachPrecisionBonus; ThrowPrecisionPenalty = throwPrecisionPenalty;
        }

        /// <summary>JS snapshotRangePolicy(authored): RANGE_POLICY_DEFAULTS under the authored keys; unknown keys or bad values are refused.</summary>
        public static Core2RangePolicy Snapshot(JObject authored)
        {
            if (authored == null) return new Core2RangePolicy();
            var known = new[] { "version", "enabled", "advanceSpPerBand", "reachPrecisionBonus", "throwPrecisionPenalty" };
            if (authored.Properties().Any(p => !known.Contains(p.Name))) throw new InvalidDataException("Invalid range policy");
            int Count(string key, int fallback)
            {
                var token = authored[key];
                if (token == null) return fallback;
                if (token.Type != JTokenType.Integer || (long)token < 0) throw new InvalidDataException("Invalid range policy");
                return (int)token;
            }
            var enabled = authored["enabled"];
            if (enabled != null && enabled.Type != JTokenType.Boolean) throw new InvalidDataException("Invalid range policy");
            return new Core2RangePolicy(enabled == null || (bool)enabled, Count("advanceSpPerBand", 1), Count("reachPrecisionBonus", 1), Count("throwPrecisionPenalty", 1), Count("version", 1));
        }
    }

    /// <summary>One actor and the band it starts at (JS initializeCombatRange row).</summary>
    public sealed class Core2RangeActor
    {
        public string Key { get; }
        public string StartingRange { get; }
        public Core2RangeActor(string key, string startingRange = "melee") { Key = key; StartingRange = startingRange ?? "melee"; }
    }

    /// <summary>JS rangeState: the policy and one band per opposing pair. Immutable: movement returns a new state.</summary>
    public sealed class Core2RangeState
    {
        public Core2RangePolicy Policy { get; }
        private readonly Dictionary<string, int> _engagements;
        public IReadOnlyDictionary<string, int> Engagements => _engagements;

        private Core2RangeState(Core2RangePolicy policy, Dictionary<string, int> engagements) { Policy = policy; _engagements = engagements; }

        /// <summary>JS createRangeState: every opposing pair starts at the greater of the two starting bands.</summary>
        public static Core2RangeState Create(Core2RangePolicy policy, IEnumerable<Core2RangeActor> actors)
        {
            var list = (actors ?? Enumerable.Empty<Core2RangeActor>()).ToList();
            if (list.Select(a => a.Key).Distinct().Count() != list.Count || list.Any(a => a.Key == null || !(a.Key.StartsWith("player:", StringComparison.Ordinal) || a.Key.StartsWith("enemy:", StringComparison.Ordinal)) || a.Key.IndexOf(':') == a.Key.Length - 1))
                throw new InvalidDataException("Invalid range actor identities");
            var engagements = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var a in list) foreach (var b in list)
                if (Core2Range.Opposing(a.Key, b.Key))
                {
                    int aa = Core2Range.Band(a.StartingRange), bb = Core2Range.Band(b.StartingRange);
                    if (aa < 0 || bb < 0) throw new InvalidDataException("Invalid starting engagement band");
                    engagements[Core2Range.PairKey(a.Key, b.Key)] = Math.Max(aa, bb);
                }
            return new Core2RangeState(policy ?? new Core2RangePolicy(), engagements);
        }

        /// <summary>JS engagementBand.</summary>
        public int Band(string sourceKey, string targetKey) =>
            _engagements.TryGetValue(Core2Range.PairKey(sourceKey, targetKey), out var band) ? band : throw new InvalidOperationException("Missing engagement pair");

        /// <summary>JS movedEngagementState: one band closer (-1) or farther (+1), clamped 0..2; a disabled policy moves nothing.</summary>
        public Core2RangeState Moved(string sourceKey, string targetKey, int bands)
        {
            if (!Policy.Enabled) return this;
            if (bands != 1 && bands != -1) throw new ArgumentException("Engagement movement requires one band", nameof(bands));
            var next = new Dictionary<string, int>(_engagements, StringComparer.Ordinal) { [Core2Range.PairKey(sourceKey, targetKey)] = Math.Max(0, Math.Min(2, Band(sourceKey, targetKey) + bands)) };
            return new Core2RangeState(Policy, next);
        }
    }

    /// <summary>The committed source of one contact (JS rangeSourceContact): what it covers and whether it may Throw.</summary>
    public sealed class Core2RangeSource
    {
        public string ContactId { get; set; } = "0";
        public string TargetKey { get; set; }
        /// <summary>melee, reach or ranged.</summary>
        public string Coverage { get; set; } = "melee";
        public bool Weapon { get; set; }
        public bool ThrowCapable { get; set; }
        /// <summary>An explicit range-bypass trait (rangeOverride, adequateReach) waives approach.</summary>
        public bool Bypass { get; set; }
        public string SourceWeaponId { get; set; }

        /// <summary>
        /// JS rangeSourceContact. A weapon source covers its range (Throw from a "throw" property tag); an unarmed hit covers
        /// Melee; any other source (a spell, an enemy move, no resolved source) covers the carrier's own range.
        /// </summary>
        public static Core2RangeSource For(Core2Carrier carrier, string targetKey, string sourceType = null, string weaponId = null, string weaponRange = null,
            bool throwCapable = false, string contactId = "0")
        {
            var profile = carrier?.Profile ?? new Core2CombatProfile();
            var weapon = sourceType == "weapon";
            return new Core2RangeSource
            {
                ContactId = contactId, TargetKey = targetKey, Weapon = weapon, SourceWeaponId = weapon ? weaponId : null, ThrowCapable = weapon && throwCapable,
                Coverage = weapon ? weaponRange ?? "melee" : sourceType == "unarmed" ? "melee" : profile.Range ?? "melee",
                Bypass = profile.RangeOverride || profile.Traits.Contains("adequateReach"),
            };
        }
    }

    public sealed class Core2RangeTarget
    {
        public string ContactId { get; set; }
        public string TargetKey { get; set; }
        /// <summary>The persistent band (or the incoming root's, for a nested reaction).</summary>
        public int ActualBand { get; set; }
        /// <summary>The distance this action meets (the root context's effective band when nested).</summary>
        public int Band { get; set; }
        public int Coverage { get; set; }
        public bool Weapon { get; set; }
        public bool ThrowCapable { get; set; }
        public bool Bypass { get; set; }
        public string SourceWeaponId { get; set; }
        public int MissingBands { get; set; }
        public int EffectiveBand { get; set; }
        public int PrecisionDelta { get; set; }
    }

    /// <summary>JS rangeActionPlan result: the route, whether it reaches every target, the Advance SP, and each contact's receipt.</summary>
    public sealed partial class Core2RangePlan
    {
        public string SourceKey { get; set; }
        /// <summary>advance or throw.</summary>
        public string Route { get; set; }
        public bool Available { get; set; }
        public int AdvanceSp { get; set; }
        public List<Core2RangeTarget> Targets { get; set; } = new List<Core2RangeTarget>();
        /// <summary>JS accepted plan rootId: the action group's root key (a reserved Counter return binds to it).</summary>
        public string RootId { get; set; }
    }

    public static class Core2Range
    {
        /// <summary>JS rangeBand: a band number, or melee/reach/ranged as 0/1/2; -1 when neither.</summary>
        public static int Band(string range) => Core2Stances.RangeIndex(Core2Stances.NormalizeRange(range));

        /// <summary>JS rangePairKey: the unordered pair, sorted (JSON.stringify of the two keys).</summary>
        public static string PairKey(string a, string b)
        {
            var pair = new[] { a, b }.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            return "[\"" + pair[0] + "\",\"" + pair[1] + "\"]";
        }

        public static bool Opposing(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && (a.StartsWith("player:", StringComparison.Ordinal) && b.StartsWith("enemy:", StringComparison.Ordinal)
                || a.StartsWith("enemy:", StringComparison.Ordinal) && b.StartsWith("player:", StringComparison.Ordinal));

        /// <summary>JS actorRangeKey for a solo fight: enemy:&lt;id&gt; or player:player.</summary>
        public static string ActorKey(Core2Combatant entity) => entity == null ? null
            : entity.RangeKey ?? (entity.Side == Core2Side.Enemy ? "enemy:" + entity.Id : "player:player");

        /// <summary>JS initializeCombatRange: the player starts at its primary weapon's range (else Melee), each enemy at its authored startingRange.</summary>
        public static Core2RangeState Initialize(Core2Rules rules, Core2Combatant player, string playerPrimaryWeaponRange, IEnumerable<(Core2Combatant Enemy, string StartingRange)> enemies) =>
            Core2RangeState.Create(rules.RangePolicy, new[] { new Core2RangeActor(ActorKey(player), playerPrimaryWeaponRange ?? "melee") }
                .Concat(enemies.Select(e => new Core2RangeActor(ActorKey(e.Enemy), e.StartingRange ?? "melee"))));

        /// <summary>
        /// JS rangeActionPlan. Each contact's missing bands are max(0, distance − coverage) (none when the policy is off or the
        /// contact bypasses); Advance costs the greatest missing count × advanceSpPerBand, once per action, and closes each
        /// target pair by its greatest gap; Throw extends a Throw-capable contact by one band (with the Throw penalty) and is
        /// unavailable for any gap it cannot cover. A Reach weapon at actual Melee engagement gains the Reach bonus.
        /// <paramref name="context"/> is an incoming root's plan, whose distance a nested reaction (a Counter return) meets.
        /// </summary>
        public static Core2RangePlan ActionPlan(Core2RangeState state, string sourceKey, IReadOnlyList<Core2RangeSource> contacts, string route = "advance", Core2RangePlan context = null)
        {
            if (state == null) return null;
            if (route != "advance" && route != "throw") throw new ArgumentException("Unknown range route", nameof(route));
            if (contacts == null) throw new ArgumentNullException(nameof(contacts));
            var policy = state.Policy;
            var available = route != "throw" || policy.Enabled;
            var maxMissing = 0;
            var targets = new List<Core2RangeTarget>();
            for (var index = 0; index < contacts.Count; index++)
            {
                var contact = contacts[index];
                var coverage = Band(contact.Coverage ?? "melee");
                if (!Opposing(sourceKey, contact.TargetKey) || coverage < 0) throw new ArgumentException("Invalid range contact");
                var inherited = context?.Targets.FirstOrDefault(row => PairKey(context.SourceKey, row.TargetKey) == PairKey(sourceKey, contact.TargetKey));
                var actual = inherited?.ActualBand ?? state.Band(sourceKey, contact.TargetKey);
                var distance = inherited?.EffectiveBand ?? actual;
                var missing = policy.Enabled && !contact.Bypass ? Math.Max(0, distance - coverage) : 0;
                var extended = route == "throw" && missing > 0;
                if (extended && (!contact.ThrowCapable || missing > 1)) available = false;
                maxMissing = Math.Max(maxMissing, missing);
                targets.Add(new Core2RangeTarget
                {
                    ContactId = contact.ContactId ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture), TargetKey = contact.TargetKey,
                    ActualBand = actual, Band = distance, Coverage = coverage, Weapon = contact.Weapon, ThrowCapable = contact.ThrowCapable, Bypass = contact.Bypass,
                    SourceWeaponId = contact.SourceWeaponId, MissingBands = missing,
                    PrecisionDelta = policy.Enabled ? (contact.Weapon && coverage == 1 && actual == 0 ? policy.ReachPrecisionBonus : 0) - (extended ? policy.ThrowPrecisionPenalty : 0) : 0,
                });
            }
            // Approach changes one temporary pair distance, shared by every source in this root.
            var approach = new Dictionary<string, int>(StringComparer.Ordinal);
            if (policy.Enabled && route == "advance")
                foreach (var row in targets) approach[row.TargetKey] = Math.Max(approach.TryGetValue(row.TargetKey, out var seen) ? seen : 0, row.MissingBands);
            foreach (var row in targets)
                row.EffectiveBand = policy.Enabled ? Math.Max(0, row.Band - (approach.TryGetValue(row.TargetKey, out var closed) ? closed : 0)) : row.Band;
            return new Core2RangePlan
            {
                SourceKey = sourceKey, Route = route, Available = available, Targets = targets,
                AdvanceSp = policy.Enabled && route == "advance" ? maxMissing * policy.AdvanceSpPerBand : 0,
            };
        }

        /// <summary>
        /// JS rangeHitContact: with an accepted plan, the planned contact's row (an unplanned target is out of range) is in
        /// range when the policy is off, it bypasses, or its effective band is within its coverage (+1 on a Throw route);
        /// with no plan, the persistent band against the source's coverage.
        /// </summary>
        public static Core2RangeContact HitContact(Core2RangeState state, string sourceKey, Core2RangeSource source, Core2RangePlan plan = null, string rangeContactId = null, Core2RangePlan context = null)
        {
            if (state == null) return null;
            if (plan != null)
            {
                var coverage = Band(source.Coverage ?? "melee");
                var row = rangeContactId != null
                    ? plan.Targets.FirstOrDefault(r => r.ContactId == rangeContactId && r.TargetKey == source.TargetKey)
                    : plan.Targets.FirstOrDefault(r => r.TargetKey == source.TargetKey && r.SourceWeaponId == source.SourceWeaponId && r.Coverage == coverage && r.Bypass == source.Bypass);
                if (row == null) return new Core2RangeContact(false, 0);
                var reach = Math.Min(2, row.Coverage + (plan.Route == "throw" && row.ThrowCapable ? 1 : 0));
                return new Core2RangeContact(!state.Policy.Enabled || row.Bypass || row.EffectiveBand <= reach, row.PrecisionDelta);
            }
            var single = ActionPlan(state, sourceKey, new[] { source }, "advance", context).Targets[0];
            return new Core2RangeContact(!state.Policy.Enabled || single.Bypass || single.Band <= single.Coverage, single.PrecisionDelta);
        }

        /// <summary>JS addRangeCosts: the Advance surcharge joins the SP lane only, after every base-card reduction.</summary>
        public static Core2Costs AddRangeCosts(Core2Costs costs, Core2RangePlan plan) => new Core2Costs(costs.Sp + (plan?.AdvanceSp ?? 0), costs.Mp);

        /// <summary>JS pureEngagementMovement: a card whose only effects are moveEngagement (Advance) refuses play with range off.</summary>
        public static bool PureEngagementMovement(Core2Card card) => card != null && card.Effects is JArray effects && effects.Count > 0
            && effects.OfType<JObject>().All(e => (string)e["op"] == "moveEngagement");

        /// <summary>JS assertRangeCardEnabled: a movement-only card cannot be played while the fight's policy is off.</summary>
        public static bool RangeCardPlayable(Core2RangeState state, Core2Card card) => state == null || state.Policy.Enabled || !PureEngagementMovement(card);

        /// <summary>The moveEngagement bands a card's effects carry (Advance and Closing Dodge −1, Backstep +1), in order.</summary>
        public static IReadOnlyList<int> MovementBands(Core2Card card) => card?.Effects is JArray effects
            ? effects.OfType<JObject>().Where(e => (string)e["op"] == "moveEngagement").Select(e => (int)e["bands"]).ToList()
            : (IReadOnlyList<int>)Array.Empty<int>();
    }
}
