// Core2Stances.cs — stance identity, combat profiles and the carriers (cards and enemy moves) the rules price and
// resolve. Phase 5a needs stance identity for the same-stance Adept discount, the Spell chain, active ratings,
// matching-stance effectiveness and stance mitigation. Phase 5b adds stance entry (EnterStance: JS setProfileStance),
// the entry grants a card face shows (EntryBonuses: stanceEntryBonuses) and Block-card Poise refill (GainBlock: JS
// gainBlock / computeBlockGain / restorePersistentPoise).
//
// JS at the pin:
//   src/model/stanceRules.js        stanceOf, sameStance, spellChain, stanceRatingBonus, stanceEffectiveness
//   src/model/profileStance.js      MIRRORED_TYPES, mirroredManeuver, configuredStance
//   src/model/combatCardProfile.js  combatProfileFor (tags → camp/maneuver/school/range/traits), isSpellCard
//   src/model/cardClassification.js cardPreparationCategory, martialStanceType
//   src/engine/stanceMitigation.js  stanceMitigation
//   src/model/fixCoreMechanics.js   fixCoreStaggeredStance
//   src/engine/gameProfileCombat.js setProfileStance; src/engine/actions.js gainBlock, computeBlockGain, gainBlockAmount
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>A held stance (JS combatStance): maneuver, camp and range, plus the mirrored/counter flags.</summary>
    public sealed class Core2Stance
    {
        public string Maneuver { get; }
        public string Camp { get; }
        public string Range { get; }
        public bool Mirrored { get; }
        public bool CounterStance { get; }
        public string School { get; }
        /// <summary>JS expiresOnOwnerCycle: the owner cycle at which the stance lapses (the Staggered stance, until its owner's next turn).</summary>
        public int? ExpiresOnOwnerCycle { get; set; }

        public Core2Stance(string maneuver, string camp = "physical", string range = "melee", bool mirrored = false, bool counterStance = false, string school = null)
        {
            Maneuver = maneuver; Camp = camp ?? "physical"; Range = range ?? "melee"; Mirrored = mirrored; CounterStance = counterStance; School = school;
        }

        /// <summary>JS fixCoreStaggeredStance: the stance a Poise break puts its owner in, until its owner's next turn begins.</summary>
        public static Core2Stance Staggered(int ownerCycle = 0) => new Core2Stance("staggered") { ExpiresOnOwnerCycle = ownerCycle + 1 };

        public static Core2Stance FromJson(JToken token)
        {
            if (!(token is JObject row)) return null;
            return new Core2Stance((string)row["maneuver"], (string)row["camp"], (string)row["range"], (bool?)row["mirrored"] ?? false,
                (bool?)row["counterStance"] ?? false, (string)row["school"]);
        }

        public bool SameIdentity(Core2Stance other) => other != null && other.Maneuver == Maneuver && other.Camp == Camp && other.Range == Range
            && other.Mirrored == Mirrored && other.CounterStance == CounterStance && other.School == School;
        public override string ToString() => Camp + "/" + Maneuver + "/" + Range + (Mirrored ? "/mirrored" : "") + (CounterStance ? "/counterStance" : "");
    }

    /// <summary>A carrier's combat identity (JS combatProfileFor result / carrier.combatProfile).</summary>
    public sealed class Core2CombatProfile
    {
        public string Camp { get; set; }
        public string Maneuver { get; set; }
        public string School { get; set; }
        public string Range { get; set; }
        public string Mirrors { get; set; }
        /// <summary>JS profile.mirrored === true (a mirrored Spell stance object).</summary>
        public bool Mirrored { get; set; }
        /// <summary>JS stanceTrigger.stance: the stance the carrier enters in place of its maneuver.</summary>
        public string StanceTrigger { get; set; }
        /// <summary>JS stanceTrigger === false: the carrier enters no stance.</summary>
        public bool StanceTriggerDisabled { get; set; }
        public bool Evade { get; set; }
        public string EvadeWhileStatus { get; set; }
        /// <summary>JS evadeEligible: null when unset.</summary>
        public bool? EvadeEligible { get; set; }
        public bool CounterStance { get; set; }
        public bool Projectile { get; set; }
        public bool RangeOverride { get; set; }
        public List<string> Traits { get; set; } = new List<string>();
        /// <summary>The single damage type (damage:&lt;type&gt; tag or authored), null for none or a mixed hit (JS damageType).</summary>
        public string DamageType { get; set; }
        /// <summary>JS physicalDamageType: the physical part's type when the profile authors it.</summary>
        public string PhysicalDamageType { get; set; }
        /// <summary>JS profile.reach (an authored delivery reach; magicShareOf's melee-spell test reads it).</summary>
        public string Reach { get; set; }
        /// <summary>JS riderSpell: a physical maneuver carrying a spell rider is a technique.</summary>
        public bool RiderSpell { get; set; }
        /// <summary>JS damageComponents: the weighted parts of a mixed hit.</summary>
        public List<Core2DamageComponent> DamageComponents { get; set; } = new List<Core2DamageComponent>();
        /// <summary>JS profile.magicShare: an authored share that overrides the damage-type table.</summary>
        public double? MagicShare { get; set; }
        /// <summary>JS counterMode (counter:melee|ranged|spell tag): which Counter a counter maneuver prepares.</summary>
        public string CounterMode { get; set; }
        /// <summary>JS targeting: single or area (a Sweep is area).</summary>
        public string Targeting { get; set; }
        /// <summary>JS counterCoverage: what an authored Counter answers; null takes the mode's default.</summary>
        public Core2CounterCoverage CounterCoverage { get; set; }
        /// <summary>JS counterReturnAttack: this hit is a Counter's return (a flat, final pre-protection amount).</summary>
        public bool CounterReturnAttack { get; set; }

        public Core2CombatProfile Clone()
        {
            var copy = (Core2CombatProfile)MemberwiseClone();
            copy.Traits = new List<string>(Traits);
            copy.DamageComponents = new List<Core2DamageComponent>(DamageComponents);
            return copy;
        }

        public static Core2CombatProfile FromJson(JToken token)
        {
            var row = token as JObject ?? new JObject();
            var trigger = row["stanceTrigger"];
            var evade = row["evade"];
            return new Core2CombatProfile
            {
                Camp = (string)row["camp"], Maneuver = (string)row["maneuver"], School = (string)row["school"], Range = (string)row["range"],
                Mirrors = (string)row["mirrors"], Mirrored = (bool?)row["mirrored"] ?? false,
                StanceTrigger = trigger is JObject ? (string)trigger["stance"] : null,
                StanceTriggerDisabled = trigger?.Type == JTokenType.Boolean && !(bool)trigger,
                Evade = evade != null && evade.Type != JTokenType.Null && !(evade.Type == JTokenType.Boolean && !(bool)evade),
                EvadeWhileStatus = evade is JObject ? (string)evade["whileStatus"] : null,
                EvadeEligible = row["evadeEligible"]?.Type == JTokenType.Boolean ? (bool)row["evadeEligible"] : (bool?)null,
                CounterStance = (bool?)row["counterStance"] ?? false,
                Projectile = row["projectile"] != null && row["projectile"].Type != JTokenType.Null && !(row["projectile"].Type == JTokenType.Boolean && !(bool)row["projectile"]),
                RangeOverride = (bool?)row["rangeOverride"] == true || (bool?)row["adequateReach"] == true,
                Traits = Core2Json.Strings(row["traits"]).ToList(),
                DamageType = (string)row["damageType"], PhysicalDamageType = (string)row["physicalDamageType"], Reach = (string)row["reach"],
                RiderSpell = row["riderSpell"] != null && row["riderSpell"].Type != JTokenType.Null && !(row["riderSpell"].Type == JTokenType.Boolean && !(bool)row["riderSpell"]),
                DamageComponents = Components(row["damageComponents"]),
                MagicShare = row["magicShare"]?.Type == JTokenType.Integer || row["magicShare"]?.Type == JTokenType.Float ? (double)row["magicShare"] : (double?)null,
                CounterMode = (string)row["counterMode"], Targeting = (string)row["targeting"],
                CounterCoverage = row["counterCoverage"] is JObject coverage ? Core2CounterCoverage.FromJson(coverage) : null,
                CounterReturnAttack = (bool?)row["counterReturnAttack"] ?? false,
            };
        }

        internal static List<Core2DamageComponent> Components(JToken token) => token is JArray rows
            ? rows.OfType<JObject>().Select(c => new Core2DamageComponent((string)c["type"], c["weight"]?.Type == JTokenType.Integer || c["weight"]?.Type == JTokenType.Float ? (double)c["weight"] : 0)).ToList()
            : new List<Core2DamageComponent>();
    }

    /// <summary>
    /// What is played or performed: a card (JS resolved card def) or an enemy move carrier (JS enemyMoveCarrier).
    /// The pricing fields are the card's authored costs; the profile is its combat identity.
    /// </summary>
    public sealed partial class Core2Carrier
    {
        public string Id { get; set; }
        /// <summary>attack, skill, power, spell… (JS def.type).</summary>
        public string Type { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        /// <summary>JS def.kindIds (classification.* ids an enemy move carries).</summary>
        public List<string> KindIds { get; set; } = new List<string>();
        public string AbilityKind { get; set; }
        public Core2CombatProfile Profile { get; set; } = new Core2CombatProfile();
        /// <summary>JS def.evade != null (martialStanceType).</summary>
        public bool HasEvadeField { get; set; }
        public bool HasDamageEffect { get; set; }
        /// <summary>JS cost === "X": SP is everything the player holds.</summary>
        public bool XCost { get; set; }
        /// <summary>Authored SP before any discount (JS resourceCosts.sp ?? staminaCost ?? numeric cost).</summary>
        public int BaseSp { get; set; }
        /// <summary>Authored MP before any discount (JS resourceCosts.mp ?? manaCost).</summary>
        public int BaseMp { get; set; }
        /// <summary>The carrier's own magic share (JS carrier.magicShare), which overrides the damage-type table; null reads the table.</summary>
        public double? MagicShare { get; set; }
        /// <summary>JS def.damageSchool ("physical", "magic", …): cardIsMagical reads it first.</summary>
        public string DamageSchool { get; set; }
        /// <summary>JS skipRatingBonus: the hit adds no attack rating.</summary>
        public bool SkipRatingBonus { get; set; }
        /// <summary>JS carrier.fixCoreSameStance: the matching-stance decision taken when the carrier entered its stance.</summary>
        public bool? FixCoreSameStance { get; set; }
        /// <summary>JS carrier.appliedStatuses: statuses the card's own effects apply (an Evade card grants no second entry stack).</summary>
        public List<string> AppliedStatuses { get; set; } = new List<string>();
        /// <summary>JS carrier.counterRequiredRange: the range a Counter return must reach (the covered contacts' farthest).</summary>
        public string CounterRequiredRange { get; set; }
        /// <summary>JS carrier.combatReaction: a reaction-made hit (a return, a rider); it never arms or answers a Counter.</summary>
        public bool CombatReaction { get; set; }
        /// <summary>JS carrier.instanceId: the hand card this carrier was played from (a player-card root); null for a move.</summary>
        public string InstanceId { get; set; }
        /// <summary>JS carrier.reactionDepth: played as a reaction (1), or an enemy reaction's stack depth; 0 for an ordinary action. No window opens on it.</summary>
        public int ReactionDepth { get; set; }
        /// <summary>JS carrier.rangeCounterReservation: a Counter played as a reaction reserved (and paid) its return's range plan.</summary>
        public Core2CounterReservation RangeCounterReservation { get; set; }
        /// <summary>JS projectCardBehavior's unplayable: a spent playLimit.</summary>
        public bool Unplayable { get; set; }
        /// <summary>JS def.targetLimit: how many of an allEnemies effect's targets the card reaches (a formula, e.g. SP spent).</summary>
        public JToken TargetLimit { get; set; }

        /// <summary>A copy with its own profile and lists.</summary>
        public Core2Carrier Clone()
        {
            var copy = (Core2Carrier)MemberwiseClone();
            copy.Tags = new List<string>(Tags); copy.KindIds = new List<string>(KindIds); copy.AppliedStatuses = new List<string>(AppliedStatuses);
            copy.Profile = Profile?.Clone();
            return copy;
        }

        public bool HasTag(string tag) => Tags.Contains(tag);
        public bool IsReaction => Tags.Contains("trait:reaction");

        /// <summary>A Core 2 card as JS resolveCombatCard prices and plays it at fight start (no upgrades, no upcast).</summary>
        public static Core2Carrier FromCard(Core2Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var raw = card.Raw;
            var costs = raw["resourceCosts"] as JObject ?? raw["costs"] as JObject;
            var carrier = new Core2Carrier
            {
                Id = card.Id, Type = card.Type, Tags = card.Tags.ToList(), KindIds = Core2Json.Strings(raw["kindIds"]).ToList(),
                AbilityKind = Core2Json.OptionalString(raw, "abilityKind"),
                HasEvadeField = raw["evade"] != null && raw["evade"].Type != JTokenType.Null,
                HasDamageEffect = card.Effects.OfType<JObject>().Any(e => (string)e["op"] == "damage"),
                XCost = card.XCost,
                BaseSp = costs?["sp"] != null ? (int)costs["sp"] : raw["staminaCost"] != null ? card.StaminaCost : card.XCost ? 0 : card.Cost,
                BaseMp = costs?["mp"] != null ? (int)costs["mp"] : card.ManaCost,
                DamageSchool = Core2Json.OptionalString(raw, "damageSchool"), TargetLimit = raw["targetLimit"],
                AppliedStatuses = card.Effects.OfType<JObject>().Where(e => (string)e["op"] == "applyStatus" && e["status"] != null).Select(e => (string)e["status"]).ToList(),
            };
            carrier.Profile = Core2Stances.CombatProfileFor(carrier.Id, carrier.Tags, raw);
            if (raw["magicShare"]?.Type == JTokenType.Integer || raw["magicShare"]?.Type == JTokenType.Float) carrier.MagicShare = (double)raw["magicShare"];
            return carrier;
        }

        /// <summary>A carrier written as plain JSON (oracle fixtures, saved plans): type, tags/cardTags, combatProfile, cost.</summary>
        public static Core2Carrier FromJson(JObject row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var tags = row["cardTags"] ?? row["tags"];
            var carrier = new Core2Carrier
            {
                Id = (string)row["id"] ?? (string)row["cardId"] ?? (string)row["moveId"], Type = (string)row["type"],
                Tags = Core2Json.Strings(tags).ToList(), KindIds = Core2Json.Strings(row["kindIds"]).ToList(),
                AbilityKind = (string)row["abilityKind"],
                XCost = row["cost"]?.Type == JTokenType.String && (string)row["cost"] == "X",
                MagicShare = row["magicShare"] != null && row["magicShare"].Type != JTokenType.Null ? (double)row["magicShare"] : (double?)null,
                SkipRatingBonus = (bool?)row["skipRatingBonus"] ?? false,
                AppliedStatuses = Core2Json.Strings(row["appliedStatuses"]).ToList(),
                DamageSchool = (string)row["damageSchool"], BaseMp = (int?)row["manaCost"] ?? 0,
                CounterRequiredRange = (string)row["counterRequiredRange"],
                CombatReaction = (bool?)row["combatReaction"] ?? false,
            };
            carrier.Profile = row["combatProfile"] is JObject profile ? Core2CombatProfile.FromJson(profile) : Core2Stances.CombatProfileFor(carrier.Id, carrier.Tags, row);
            if (carrier.Profile.Mirrors == null && row["mirrors"]?.Type == JTokenType.String) carrier.Profile.Mirrors = (string)row["mirrors"];
            return carrier;
        }
    }

    public static class Core2Stances
    {
        /// <summary>JS profileStance.js MIRRORED_TYPES.</summary>
        public static readonly IReadOnlyCollection<string> MirroredTypes = new HashSet<string>(new[] { "attack", "smash", "sweep", "defend", "counter" }, StringComparer.Ordinal);
        /// <summary>JS combatRanges.js RANGES, nearest first.</summary>
        public static readonly IReadOnlyList<string> Ranges = new[] { "melee", "reach", "ranged" };
        private static readonly HashSet<string> CampIds = new HashSet<string>(new[] { "physical", "spell" }, StringComparer.Ordinal);
        private static readonly HashSet<string> ManeuverIds = new HashSet<string>(new[] { "attack", "defend", "counter", "sweep", "ranged", "smash", "casting" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SpellCamps = new HashSet<string>(new[] { "spell", "spells", "magic", "magical" }, StringComparer.Ordinal);
        private static readonly HashSet<string> CounterModes = new HashSet<string>(new[] { "melee", "ranged", "spell" }, StringComparer.Ordinal);
        private static readonly HashSet<string> TargetingIds = new HashSet<string>(new[] { "single", "area" }, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LegacyRange = new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "melee", ["near"] = "reach", ["far"] = "ranged" };

        public static string NormalizeRange(string value) => value != null && LegacyRange.TryGetValue(value, out var current) ? current : value;
        public static int RangeIndex(string range) { for (var i = 0; i < Ranges.Count; i++) if (Ranges[i] == range) return i; return -1; }

        /// <summary>JS combatProfileFor: camp, maneuver, school, range and traits from tags, plus the authored payload keys.</summary>
        public static Core2CombatProfile CombatProfileFor(string id, IReadOnlyList<string> tags, JObject raw = null)
        {
            tags ??= Array.Empty<string>();
            string Identity(string prefix, IEnumerable<string> list, ISet<string> allowed)
            {
                var values = list.Where(t => t != null && t.StartsWith(prefix, StringComparison.Ordinal)).Select(t => t.Substring(prefix.Length)).Distinct().ToList();
                if (values.Count > 1) throw new InvalidDataException(id + " carries conflicting " + prefix + " tags");
                if (values.Count == 1 && allowed != null && !allowed.Contains(values[0])) throw new InvalidDataException(id + " carries unknown " + prefix + " tag '" + values[0] + "'");
                return values.Count == 1 ? values[0] : null;
            }
            var profile = new Core2CombatProfile
            {
                Camp = Identity("camp:", tags, CampIds),
                Maneuver = Identity("maneuver:", tags, ManeuverIds),
                School = Identity("school:", tags, null),
                Range = Identity("range:", tags.Select(t => t.StartsWith("reach:", StringComparison.Ordinal) ? "range:" + NormalizeRange(t.Substring(6)) : t), new HashSet<string>(Ranges)),
                Traits = tags.Where(t => t.StartsWith("trait:", StringComparison.Ordinal)).Select(t => t.Substring(6)).Distinct().ToList(),
                CounterMode = Identity("counter:", tags, CounterModes),
                Targeting = Identity("targeting:", tags, TargetingIds),
            };
            // JS: the attack's components, else its damageType, else the damage:<type> tags; one type names the hit.
            var components = raw?["attack"]?["components"] as JArray;
            var types = (components != null ? components.Select(c => (string)c["type"])
                : raw?["attack"]?["damageType"]?.Type == JTokenType.String ? new[] { (string)raw["attack"]["damageType"] }
                : tags.Where(t => t.StartsWith("damage:", StringComparison.Ordinal)).Select(t => t.Substring(7))).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            profile.DamageType = types.Count == 1 ? types[0] : null;
            if (components != null && types.Count > 1)
            {
                profile.DamageComponents = Core2CombatProfile.Components(components);
                profile.DamageType = profile.DamageComponents.OrderByDescending(c => c.Weight).First().Type;
            }
            if (raw != null)
            {
                var reach = raw["reach"]?.Type == JTokenType.String ? NormalizeRange((string)raw["reach"]) : null;
                if (reach != null && raw["range"] == null && RangeIndex(reach) >= 0) profile.Range = reach;
                var authored = Core2CombatProfile.FromJson(raw);
                if (raw["range"]?.Type == JTokenType.String) profile.Range = (string)raw["range"];
                if (raw["mirrors"] != null) profile.Mirrors = authored.Mirrors;
                if (raw["stanceTrigger"] != null) { profile.StanceTrigger = authored.StanceTrigger; profile.StanceTriggerDisabled = authored.StanceTriggerDisabled; }
                if (raw["evade"] != null) { profile.Evade = authored.Evade; profile.EvadeWhileStatus = authored.EvadeWhileStatus; }
                if (raw["projectile"] != null) profile.Projectile = authored.Projectile;
                if (raw["traits"] is JArray) profile.Traits = authored.Traits;
                if (raw["targeting"] != null) profile.Targeting = authored.Targeting;
                if (raw["counterCoverage"] != null) profile.CounterCoverage = authored.CounterCoverage;
            }
            return profile;
        }

        /// <summary>THE ONE SPELL TEST (JS isSpellCard): the spell camp, nothing else.</summary>
        public static bool IsSpellCard(Core2Carrier card) => card != null && (card.Profile?.Camp == "spell" || card.Tags.Contains("camp:spell"));

        /// <summary>JS mirroredManeuver: the basic maneuver a mirrored Spell takes.</summary>
        public static string MirroredManeuver(Core2Carrier carrier)
        {
            var mirrors = carrier?.Profile?.Mirrors;
            return mirrors != null && MirroredTypes.Contains(mirrors) && (carrier.Profile.Camp ?? "spell") == "spell" ? mirrors : null;
        }

        /// <summary>JS effectiveManeuver: the mirrored basic's maneuver, else the profile's.</summary>
        public static string EffectiveManeuver(Core2Carrier carrier) => MirroredManeuver(carrier) ?? carrier?.Profile?.Maneuver;

        /// <summary>JS martialStanceType.</summary>
        private static string MartialStanceType(Core2Carrier def, Core2CombatProfile profile)
        {
            if (def.HasEvadeField) return "evade";
            switch (profile.Maneuver)
            {
                case "smash": return "smash";
                case "attack": return "attack";
                case "sweep": return "sweep";
                case "counter": return "counter";
                case "defend": return "defense";
            }
            if (def.Type == "attack" || (profile.Maneuver == "casting" || profile.Maneuver == "ranged") && def.HasDamageEffect) return "attack";
            return null;
        }

        /// <summary>JS cardPreparationCategory: "power", "spell", "skill" or null.</summary>
        public static string PreparationCategory(Core2Carrier def)
        {
            if (def == null || def.Type == "consumable" || def.Type == "status" || def.Type == "curse") return null;
            var profile = def.Profile ?? new Core2CombatProfile();
            var kinds = def.KindIds.Concat(def.Tags).ToList();
            if (def.Type == "power" || kinds.Contains("classification.power")) return "power";
            var explicitSpell = kinds.Contains("classification.spell") || def.Tags.Contains("source:spell") || def.AbilityKind == "spell" || def.Type == "spell";
            var cardKind = def.Type == "attack" || def.Type == "skill" || def.Type == "power" || def.Type == "spell"
                || kinds.Any(k => k == "classification.attack" || k == "classification.skill" || k == "classification.power");
            if (explicitSpell || profile.Camp == "spell" && cardKind) return "spell";
            var skill = def.Type == "skill" || kinds.Contains("classification.skill");
            if (skill && (profile.Maneuver == "casting" || MartialStanceType(def, profile) == null)) return "skill";
            return null;
        }

        /// <summary>JS stanceOf(carrier, entity): the stance a carrier plays in, or null when it enters none.</summary>
        public static Core2Stance StanceOf(Core2Carrier carrier, Core2Combatant entity = null)
        {
            if (carrier == null) return null;
            var profile = carrier.Profile ?? new Core2CombatProfile();
            if (profile.StanceTriggerDisabled) return null;
            var preparation = PreparationCategory(carrier);
            return Stance(profile, preparation, entity);
        }

        /// <summary>JS stanceOf over a held stance object (it has a camp, so it is its own profile and has no preparation).</summary>
        public static Core2Stance StanceOf(Core2Stance held)
        {
            if (held == null) return null;
            var profile = new Core2CombatProfile { Camp = held.Camp, Maneuver = held.Maneuver, Range = held.Range, Mirrored = held.Mirrored, School = held.School, CounterStance = held.CounterStance };
            return Stance(profile, null, null);
        }

        private static Core2Stance Stance(Core2CombatProfile profile, string preparation, Core2Combatant entity)
        {
            if (preparation == null && profile.Maneuver == null) return null;
            var camp = profile.Camp != null && SpellCamps.Contains(profile.Camp) ? "spell" : "physical";
            // A Spell that mirrors a basic card (owner 2026-10-18) takes that basic's stance type; it stays a Spell.
            var mirrored = camp == "spell" && (profile.Mirrors != null && MirroredTypes.Contains(profile.Mirrors) || profile.Mirrored && profile.Maneuver != null && MirroredTypes.Contains(profile.Maneuver));
            var maneuver = mirrored ? (profile.Mirrors ?? profile.Maneuver) : preparation != null ? "casting" : profile.StanceTrigger ?? profile.Maneuver;
            if (maneuver == "defense" || maneuver == "block") maneuver = "defend";
            if (maneuver == "spell" || maneuver == "cast" || maneuver == "prepared") maneuver = "casting";
            var evade = profile.Evade && profile.EvadeEligible != false
                && (profile.EvadeWhileStatus == null || profile.EvadeEligible == true || (entity?.StatusStacks(profile.EvadeWhileStatus) ?? 0) > 0);
            if (preparation == null && ((evade && (maneuver == "defend" || maneuver == "counter")) || maneuver == "evade")) maneuver = "evade";
            if (camp == "spell" && !mirrored && maneuver != "defend" && maneuver != "counter" && maneuver != "evade"
                && (profile.Range ?? (profile.Projectile || maneuver == "ranged" ? "ranged" : "melee")) != "melee") maneuver = "casting";
            if (maneuver == "ranged") maneuver = "attack";
            // Evade remains the stance identity; retain authored Counter offense across snapshots.
            var counterStance = maneuver == "evade" && (profile.Maneuver == "counter" || profile.CounterStance);
            return new Core2Stance(maneuver, camp, profile.Range ?? "melee", mirrored, counterStance, profile.School);
        }

        private static IEnumerable<Core2Stance> Held(Core2Combatant entity) =>
            (entity?.ConcurrentStances ?? new List<Core2Stance>()).Append(StanceOf(entity?.Stance)).Where(s => s != null);

        /// <summary>JS sameStance: the carrier's stance matches (maneuver and camp) the held stance or a concurrent one.</summary>
        public static bool SameStance(Core2Combatant entity, Core2Carrier carrier)
        {
            var next = StanceOf(carrier, entity);
            return next != null && Held(entity).Any(row => row.Maneuver == next.Maneuver && row.Camp == next.Camp);
        }

        /// <summary>JS spellChain: a Spell after a mirrored Spell (or a mirrored Spell after a Spell) takes the Mana discount.</summary>
        public static bool SpellChain(Core2Combatant entity, Core2Carrier carrier)
        {
            var next = StanceOf(carrier, entity);
            return next != null && next.Camp == "spell" && Held(entity).Any(row => row.Camp == "spell" && (row.Mirrored || next.Mirrored));
        }

        /// <summary>JS stanceRatingBonus: +offensiveRating AR (PR for casting/mirrored/spell Counter) and +defensiveRating DR (Ward for spell camp).</summary>
        public static int StanceRatingBonus(Core2Rules rules, Core2Combatant entity, string id)
        {
            var stance = StanceOf(entity?.Stance);
            if (stance == null) return 0;
            var m = stance.Maneuver;
            var offensive = stance.CounterStance || m == "attack" || m == "smash" || m == "sweep" || m == "counter" || m == "casting";
            var defensive = m == "defend" || m == "counter" || m == "evade";
            var offensiveId = m == "casting" || stance.Mirrored || ((m == "counter" || stance.CounterStance) && stance.Camp == "spell") ? "pr" : "ar";
            return (offensive && id == offensiveId ? rules.OffensiveRating : 0) + (defensive && id == (stance.Camp == "spell" ? "ward" : "dr") ? rules.DefensiveRating : 0);
        }

        /// <summary>JS stanceEffectiveness: ×effectivenessMultiplier when the carrier matches its source's stance.</summary>
        public static double StanceEffectiveness(Core2Rules rules, Core2Combatant source, Core2Carrier carrier) =>
            rules.FixCoreEnabled && (carrier?.FixCoreSameStance ?? SameStance(source, carrier)) ? rules.StanceEffectivenessMultiplier : 1;

        /// <summary>JS configuredStance: the stance name the profile's stances module reads (casting → prepared, ranged → attack).</summary>
        public static string ConfiguredStance(Core2Carrier carrier)
        {
            var mirrored = MirroredManeuver(carrier);
            if (mirrored != null) return mirrored;
            if (PreparationCategory(carrier) != null) return "prepared";
            var raw = carrier?.Profile?.StanceTrigger ?? carrier?.Profile?.Maneuver
                ?? carrier?.Tags.FirstOrDefault(t => t.StartsWith("maneuver:", StringComparison.Ordinal))?.Substring(9);
            return Canonical(raw) ?? "neutral";
        }

        /// <summary>
        /// JS configuredStance over a held stance object: it has no `mirrors` key and no preparation category, so the
        /// name is its maneuver, canonicalised (a missing stance reads "neutral").
        /// </summary>
        public static string ConfiguredStance(Core2Stance held) => Canonical(held?.Maneuver) ?? "neutral";

        private static string Canonical(string raw) => raw == "casting" || raw == "cast" || raw == "spell" ? "prepared" : raw == "ranged" ? "attack" : raw;

        /// <summary>JS stanceMitigation: a defender whose stance beats the incoming one takes defenseMitigation less.</summary>
        public static double StanceMitigationMultiplier(Core2Rules rules, string incoming, string defending, out bool defenseAdvantage)
        {
            string C(string m) => m == "casting" || m == "cast" ? "prepared" : m == "ranged" ? "attack" : m;
            var attack = C(incoming); var defense = C(defending);
            defenseAdvantage = rules.StanceInclude.Contains(attack) && rules.StanceInclude.Contains(defense)
                && rules.StanceMatchups.TryGetValue(defense ?? "", out var beats) && beats.Contains(attack);
            return 1 - (defenseAdvantage ? rules.StanceDefenseMitigation : 0);
        }

        /// <summary>JS gameProfileCombat.js advantage: the attacking stance's matchups list the defending one.</summary>
        public static bool StanceAdvantage(Core2Rules rules, string attacking, string defending) =>
            rules.StanceInclude.Contains(attacking) && rules.StanceMatchups.TryGetValue(attacking ?? "", out var beats) && beats.Contains(defending);

        // ---- phase 5b: entering a stance ------------------------------------------------------------------------

        /// <summary>
        /// JS stanceEntryBonuses under FixCore: an offensive stance (Attack, Smash, Sweep, Counter, Casting, or an Evade that
        /// keeps Counter offense) grants offensivePrecision; a defensive one (Defend, Counter, Evade) grants entryProtection,
        /// split Block:Barrier for the spell camp by its school's share (spellBlockShare otherwise), Block floored and the
        /// exact rest Barrier; Evade (or an auxiliary Evade the card authors) grants evadeStacks. This is what a card face
        /// shows; a Core 2 fight grants only the Precision and Evade parts (EnterStance).
        /// </summary>
        public static Core2EntryGrants EntryBonuses(Core2Rules rules, Core2Carrier carrier)
        {
            var stance = StanceOf(carrier);
            if (stance == null) return new Core2EntryGrants();
            var m = stance.Maneuver;
            var offensive = stance.CounterStance || m == "attack" || m == "smash" || m == "sweep" || m == "counter" || m == "casting";
            var defensive = m == "defend" || m == "counter" || m == "evade";
            var amount = defensive ? rules.EntryProtection : 0;
            var share = stance.School != null && rules.SchoolBlockShares.TryGetValue(stance.School, out var school) ? school : rules.SpellBlockShare;
            var block = stance.Camp == "spell" ? (int)Math.Floor(amount * share) : amount;
            var profile = carrier.Profile ?? new Core2CombatProfile();
            var auxiliaryEvade = profile.Evade && profile.EvadeEligible != false && (profile.EvadeWhileStatus == null || profile.EvadeEligible == true);
            return new Core2EntryGrants
            {
                Precision = offensive ? rules.OffensivePrecision : 0, Evade = m == "evade" || auxiliaryEvade ? rules.EvadeStacks : 0,
                Block = block, Barrier = amount - block,
            };
        }

        /// <summary>
        /// JS setProfileStance (configured profile, FixCore) — what accepting a card does to its player's or enemy's stance:
        /// the matching-stance decision is taken first (it prices the card's effects ×1.25); the entity then stands in the
        /// card's configured stance (Neutral when the stances module does not include it), carrying the card's camp and range
        /// (a mirrored Spell keeps its basic's stance in the spell camp). Only a real stance change grants: Precision of
        /// offensivePrecision + the stat mix, and the Evade stack unless the card applies Evade itself. A Core 2 fight grants
        /// no entry Block or Barrier and no 5-point Defense restore (those are the non-configured expansion path); a Block
        /// card's Poise refill is GainBlock. Stance capacity is 1 in Core 2, so no concurrent stance is remembered.
        /// </summary>
        public static Core2StanceEntry EnterStance(Core2CombatContext ctx, Core2Combatant entity, Core2Carrier carrier)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            var rules = ctx.Rules;
            if (carrier != null && carrier.FixCoreSameStance == null) carrier.FixCoreSameStance = SameStance(entity, carrier);
            var previous = entity.Stance;
            var changed = !SameStance(entity, carrier);
            var grants = changed ? EntryBonuses(rules, carrier) : null;
            var name = ConfiguredStance(carrier);
            var mirrored = MirroredManeuver(carrier) != null;
            entity.Stance = rules.StanceInclude.Contains(name)
                ? new Core2Stance(name, mirrored ? "spell" : carrier?.Profile?.Camp ?? "physical", carrier?.Profile?.Range ?? "melee", mirrored)
                : new Core2Stance("neutral", "physical", "melee");
            var precision = grants != null && grants.Precision > 0 ? Core2Accuracy.PrecisionEntryGrant(rules, entity, grants.Precision) : 0;
            if (precision > 0) Core2Statuses.Apply(ctx, entity, "precision", precision, persistent: carrier.Tags.Contains("trait:persistent"));
            var evade = grants != null && grants.Evade > 0 && !carrier.AppliedStatuses.Contains("evade") ? grants.Evade : 0;
            if (evade > 0) Core2Statuses.Apply(ctx, entity, "evade", evade);
            entity.ConcurrentStances.Clear();
            return new Core2StanceEntry { Previous = previous, Stance = entity.Stance, Changed = changed, SameStance = carrier?.FixCoreSameStance == true, Precision = precision, Evade = evade };
        }

        /// <summary>
        /// JS gainBlock for a card or move under FixCore: Block = floor((base + DR × defenseRatingWeight) × matching-stance
        /// effectiveness) (computeBlockGain, configured branch; none when the profile has no Block defense); the Block a card
        /// lays down also refills Poise by the same amount, up to its maximum, with no overflow (gainBlockAmount →
        /// restorePersistentPoise). Ward is off in Core 2, so it is not refilled. Returns the Block gained and Poise restored.
        /// </summary>
        public static (int Block, int PoiseRestored) GainBlock(Core2CombatContext ctx, Core2Combatant entity, int baseBlock, Core2Carrier carrier)
        {
            if (entity == null || !entity.Alive) return (0, 0);
            var rules = ctx.Rules;
            var amount = rules.BlockDefense
                ? Math.Max(0, (int)Math.Floor((baseBlock + Core2Statuses.RatingValue(rules, entity, "dr") * rules.DefenseRatingWeight) * StanceEffectiveness(rules, entity, carrier)))
                : 0;
            entity.Block += amount;
            var restored = 0;
            if (amount > 0 && carrier != null && entity.Poise != null)
            {
                restored = Math.Min(Math.Max(0, entity.Poise.Max - entity.Poise.Value), amount);
                entity.Poise.Value += restored;
            }
            return (amount, restored);
        }
    }

    /// <summary>JS stanceEntryBonuses result.</summary>
    public sealed class Core2EntryGrants
    {
        public int Precision { get; set; }
        public int Evade { get; set; }
        public int Block { get; set; }
        public int Barrier { get; set; }
        public override string ToString() => "precision " + Precision + ", evade " + Evade + ", block " + Block + ", barrier " + Barrier;
    }

    /// <summary>What EnterStance did.</summary>
    public sealed class Core2StanceEntry
    {
        public Core2Stance Previous { get; set; }
        public Core2Stance Stance { get; set; }
        /// <summary>The stance identity (maneuver and camp) changed, so the entry grants applied.</summary>
        public bool Changed { get; set; }
        /// <summary>The card matched the stance held before it (JS carrier.fixCoreSameStance).</summary>
        public bool SameStance { get; set; }
        public int Precision { get; set; }
        public int Evade { get; set; }
    }
}
