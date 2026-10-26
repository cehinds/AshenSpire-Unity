// Core2Stances.cs — stance identity, combat profiles and the carriers (cards and enemy moves) the rules price and
// resolve. Phase 5a needs stance identity for the same-stance Adept discount, the Spell chain, active ratings,
// matching-stance effectiveness and stance mitigation; the stance *entry* rules (grants, Defense restore,
// matchups beyond the damage receipt) are phase 5b.
//
// JS at the pin:
//   src/model/stanceRules.js        stanceOf, sameStance, spellChain, stanceRatingBonus, stanceEffectiveness
//   src/model/profileStance.js      MIRRORED_TYPES, mirroredManeuver, configuredStance
//   src/model/combatCardProfile.js  combatProfileFor (tags → camp/maneuver/school/range/traits), isSpellCard
//   src/model/cardClassification.js cardPreparationCategory, martialStanceType
//   src/engine/stanceMitigation.js  stanceMitigation
//   src/model/fixCoreMechanics.js   fixCoreStaggeredStance
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

        public Core2Stance(string maneuver, string camp = "physical", string range = "melee", bool mirrored = false, bool counterStance = false, string school = null)
        {
            Maneuver = maneuver; Camp = camp ?? "physical"; Range = range ?? "melee"; Mirrored = mirrored; CounterStance = counterStance; School = school;
        }

        /// <summary>JS fixCoreStaggeredStance: the stance a Poise break puts its owner in.</summary>
        public static Core2Stance Staggered() => new Core2Stance("staggered");

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

        public Core2CombatProfile Clone()
        {
            var copy = (Core2CombatProfile)MemberwiseClone();
            copy.Traits = new List<string>(Traits);
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
            };
        }
    }

    /// <summary>
    /// What is played or performed: a card (JS resolved card def) or an enemy move carrier (JS enemyMoveCarrier).
    /// The pricing fields are the card's authored costs; the profile is its combat identity.
    /// </summary>
    public sealed class Core2Carrier
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
        /// <summary>Hit's magic share when the damage typing is supplied (JS magicShareOf); null reads the camp (spell = 1).</summary>
        public double? MagicShare { get; set; }
        /// <summary>JS skipRatingBonus: the hit adds no attack rating.</summary>
        public bool SkipRatingBonus { get; set; }
        /// <summary>JS carrier.fixCoreSameStance: the matching-stance decision taken when the carrier entered its stance.</summary>
        public bool? FixCoreSameStance { get; set; }

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
            };
            carrier.Profile = Core2Stances.CombatProfileFor(carrier.Id, carrier.Tags, raw);
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
            };
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
    }
}
