// Core2DamageTypes.cs — the damage-type table: how much of a hit is magic (Barrier, then Ward; with Ward off, Poise)
// rather than physical (Block, then Poise). Phase 5a read only the carrier's recorded share or its camp; this is the
// whole JS precedence.
//
// JS at the pin:
//   src/content/combatRules.js     damageTypes (magicShare, physical flag), schools (magicShare, physicalType),
//                                  magicSplit (meleeSpell 0.7, technique 0.3) — exported as mechanics.json damageTypes
//   src/model/combatRatings.js     magicShareOf, componentPartsOf, isTechniqueProfile, schoolOf, canonicalDamageType,
//                                  damageTypeMagicShare, isMagicDamageType, isPhysicalDamageType, physicalTypeOf,
//                                  meleeSpellMagicShare, techniqueMagicShare, isForceHit
// A Core 2 fight has no Advanced ratings rows (ctx.ratingsRules is empty at the pin), so the content defaults decide.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One weighted part of a mixed hit (JS damageComponents row).</summary>
    public sealed class Core2DamageComponent
    {
        public string Type { get; }
        public double Weight { get; }
        public Core2DamageComponent(string type, double weight) { Type = type; Weight = weight; }
    }

    public sealed class Core2DamageTypes
    {
        private sealed class TypeRow { public bool Physical; public double? MagicShare; }
        private sealed class SchoolRow { public double? MagicShare; public string PhysicalType; }

        private readonly Dictionary<string, TypeRow> _types;
        private readonly Dictionary<string, SchoolRow> _schools;
        public double MeleeSpellShare { get; }
        public double TechniqueShare { get; }

        /// <summary>JS DAMAGE_TYPE_ALIASES.</summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pierce"] = "piercing", ["slash"] = "slashing", ["cold"] = "frost", ["force"] = "arcane", ["holy"] = "sacred", ["necrotic"] = "decay",
        };
        private static readonly HashSet<string> MeleeReaches = new HashSet<string>(new[] { "contact", "melee" }, StringComparer.Ordinal);

        public Core2DamageTypes(JObject table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            var types = table["damageTypes"] as JObject ?? throw new InvalidDataException("damageTypes.damageTypes is required");
            _types = types.Properties().ToDictionary(p => p.Name, p => new TypeRow
            {
                Physical = (bool?)p.Value["physical"] ?? false,
                MagicShare = Finite(p.Value["magicShare"]),
            }, StringComparer.Ordinal);
            _schools = ((table["schools"] as JObject) ?? new JObject()).Properties().ToDictionary(p => p.Name, p => new SchoolRow
            {
                MagicShare = Finite(p.Value["magicShare"]), PhysicalType = (string)p.Value["physicalType"],
            }, StringComparer.Ordinal);
            var split = table["magicSplit"] as JObject ?? throw new InvalidDataException("damageTypes.magicSplit is required");
            MeleeSpellShare = Finite(split["meleeSpell"]) ?? throw new InvalidDataException("magicSplit.meleeSpell must be a number");
            TechniqueShare = Finite(split["technique"]) ?? throw new InvalidDataException("magicSplit.technique must be a number");
        }

        private static double? Finite(JToken token) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) ? (double)token : (double?)null;

        /// <summary>JS canonicalDamageType.</summary>
        public static string Canonical(string type) => type != null && Aliases.TryGetValue(type, out var canonical) ? canonical : string.IsNullOrEmpty(type) ? null : type;

        /// <summary>JS schoolOf: the profile's school, else a school:&lt;id&gt; tag; "physical" is no magic school.</summary>
        public static string SchoolOf(Core2CombatProfile profile, IEnumerable<string> tags = null)
        {
            var tag = (tags ?? Enumerable.Empty<string>()).FirstOrDefault(t => t != null && t.StartsWith("school:", StringComparison.Ordinal));
            var school = profile?.School ?? tag?.Substring(7);
            return string.IsNullOrEmpty(school) || school == "physical" ? null : school;
        }

        /// <summary>JS routingShare: the type's content magicShare, else 1 when it is marked non-physical, else 0; null when unknown.</summary>
        private double? RoutingShare(string type)
        {
            if (type == null || !_types.TryGetValue(type, out var row)) return null;
            return row.MagicShare ?? (row.Physical ? 0 : 1);
        }

        public bool IsMagicDamageType(string type) => RoutingShare(type) is double share && share > 0;
        public bool IsPhysicalDamageType(string type) => RoutingShare(type) == 0;

        /// <summary>JS damageTypeMagicShare (no Advanced override in Core 2): the type's content magicShare, or null.</summary>
        public double? DamageTypeMagicShare(string type) => type != null && _types.TryGetValue(type, out var row) ? row.MagicShare : null;

        /// <summary>JS schoolMagicShare: a school the rules do not list is all magic.</summary>
        public double SchoolMagicShare(string school) => school != null && _schools.TryGetValue(school, out var row) && row.MagicShare.HasValue ? row.MagicShare.Value : 1;

        /// <summary>JS componentPartsOf: the magic weight over the whole weight of a hit with two or more components, else null.</summary>
        public double? ComponentMagicShare(IReadOnlyList<Core2DamageComponent> components)
        {
            if (components == null || components.Count < 2) return null;
            double magic = 0, physical = 0;
            foreach (var component in components)
            {
                var type = Canonical(component?.Type);
                if (type == null) continue;
                var weight = Math.Max(0, double.IsNaN(component.Weight) ? 0 : component.Weight);
                if (IsMagicDamageType(type)) magic += weight; else physical += weight;
            }
            var total = magic + physical;
            return total > 0 ? magic / total : (double?)null;
        }

        /// <summary>JS isTechniqueProfile: a physical-camp maneuver carrying a magic school, magic type/component or a spell rider.</summary>
        public bool IsTechnique(Core2CombatProfile profile, IEnumerable<string> tags = null)
        {
            if (profile?.Camp != "physical" || profile.Maneuver == null) return false;
            var magicComponent = profile.DamageComponents.Any(c => IsMagicDamageType(Canonical(c?.Type)));
            return SchoolOf(profile, tags) != null || IsMagicDamageType(Canonical(profile.DamageType)) || magicComponent || profile.RiderSpell;
        }

        /// <summary>JS physicalTypeOf: the authored physical type, else a physical damage type, else the school's physicalType.</summary>
        public string PhysicalTypeOf(Core2CombatProfile profile, IEnumerable<string> tags = null)
        {
            var type = Canonical(profile?.DamageType);
            var school = SchoolOf(profile, tags);
            var schoolRow = school != null && _schools.TryGetValue(school, out var row) ? row : null;
            return profile?.PhysicalDamageType ?? (IsPhysicalDamageType(type) ? type : null) ?? schoolRow?.PhysicalType;
        }

        /// <summary>
        /// JS magicShareOf, in its precedence: the carrier's own share; a mixed hit's component weights; a school whose share
        /// is below 1; a melee spell with a physical type (Melee spell share); a technique (Technique share); a magic school;
        /// the damage type's share; else the camp (a spell is all magic).
        /// </summary>
        public double MagicShareOf(Core2CombatProfile profile, double? ownShare = null, IEnumerable<string> tags = null)
        {
            var own = ownShare ?? profile?.MagicShare;
            if (own.HasValue && !double.IsNaN(own.Value) && !double.IsInfinity(own.Value)) return Math.Min(1, Math.Max(0, own.Value));
            if (ComponentMagicShare(profile?.DamageComponents) is double mixed) return mixed;
            var spell = profile?.Camp == "spell";
            var school = SchoolOf(profile, tags);
            var type = Canonical(profile?.DamageType);
            double? schoolShare = school != null && _schools.TryGetValue(school, out var row) ? row.MagicShare : null;
            if (schoolShare.HasValue && schoolShare.Value < 1) return Math.Max(0, schoolShare.Value);
            if (spell && school == null && profile?.Reach != null && MeleeReaches.Contains(profile.Reach) && PhysicalTypeOf(profile, tags) != null) return MeleeSpellShare;
            if (IsTechnique(profile, tags)) return TechniqueShare;
            if (school != null) return SchoolMagicShare(school);
            if (DamageTypeMagicShare(type) is double typed) return typed;
            return spell ? 1 : 0;
        }

        /// <summary>JS isForceHit: the force school, or any Arcane (force) type or component.</summary>
        public static bool IsForceHit(Core2CombatProfile profile, IEnumerable<string> tags = null) =>
            profile != null && (SchoolOf(profile, tags) == "force" || Canonical(profile.DamageType) == "arcane"
                || profile.DamageComponents.Any(c => Canonical(c?.Type) == "arcane"));
    }
}
