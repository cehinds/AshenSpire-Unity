// AttributeProgression.cs — owner-authored point benefits layered over original rules.
// DATA: GameContent/Unity/Original/progression.json. Persist Data() with each run.
// ORDER: weapon projection with BaselineProfiles(), then ResolveCard(), then costs/combat.
// A damage bonus is a TOTAL across one effect's hits; the combat executor distributes
// the remainder over its first hits. Never add it independently to every hit.
// This is an intentional Unity balance improvement, separate from original parity.
// schemaVersion 1 (saved runs): per-point bonuses above a baseline, unchanged.
// schemaVersion 2 (web lean ruleset): ratings AR/DR/PR = floor(sum(floor(weight x
// attribute)) x multiplier) (web src/model/ratingFormula.js) plus, when
// equipmentRatingAddend, the source armament's attack/defense rating; profile cards
// keep their base value and gain nothing per tier.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class AttributeProgression
    {
        private const double Epsilon = 1e-9;
        private static readonly string[] AttributeIds = { "strength", "dexterity", "intelligence", "wisdom", "constitution" };
        private readonly JObject _rules;
        private readonly bool _rated;
        public AttributeProgression(JObject rules)
        {
            _rules = (JObject)(rules ?? throw new ArgumentNullException(nameof(rules))).DeepClone();
            _rated = _rules["schemaVersion"]?.Type == JTokenType.Integer && (long)_rules["schemaVersion"] == 2;
            if (!_rated) Integer(_rules["baseline"], "baseline");
            var rows = _rules["attributes"] as JObject ?? throw new ArgumentException("Missing attribute benefits.");
            foreach (var id in AttributeIds)
            {
                var row = rows[id] as JObject ?? throw new ArgumentException("Missing benefit: " + id);
                if (string.IsNullOrWhiteSpace((string)row["perPoint"])) throw new ArgumentException("Missing point explanation: " + id);
            }
            if (_rated) { ValidateRatings(); return; }
            foreach (var property in ((JObject)_rules["damageSchools"]).Properties())
                if (rows[(string)property.Value] == null) throw new ArgumentException("Unknown damage attribute: " + property.Value);
            Integer(_rules["damagePerPoint"], "damagePerPoint");
            Integer(_rules["healingPerPoint"], "healingPerPoint");
        }
        private static int Integer(JToken value, string name)
        {
            if (value == null || value.Type != JTokenType.Integer || (long)value < 0 || (long)value > int.MaxValue) throw new ArgumentException("Expected nonnegative integer: " + name);
            return (int)value;
        }
        private void ValidateRatings()
        {
            var formula = _rules["ratingFormula"] as JObject ?? throw new ArgumentException("Missing rating formula.");
            if (Number(formula["multiplier"], "rating multiplier") < 0) throw new ArgumentException("Rating multiplier cannot be negative.");
            var ratings = formula["ratings"] as JObject ?? throw new ArgumentException("Missing ratings.");
            foreach (var rating in ratings.Properties())
            {
                var row = rating.Value as JObject ?? throw new ArgumentException("Invalid rating: " + rating.Name);
                if (string.IsNullOrWhiteSpace((string)row["label"])) throw new ArgumentException("Missing rating label: " + rating.Name);
                Number(row["base"], rating.Name + ".base");
                foreach (var property in row.Properties().Where(x => x.Name != "label" && x.Name != "base"))
                {
                    if (!AttributeIds.Contains(property.Name)) throw new ArgumentException("Unknown rating field: " + rating.Name + "." + property.Name);
                    if (Number(property.Value, rating.Name + "." + property.Name) < 0) throw new ArgumentException("Rating weight cannot be negative: " + rating.Name + "." + property.Name);
                }
            }
            string Known(JToken value, string name) { var id = value?.Type == JTokenType.String ? (string)value : null; if (id == null || ratings[id] == null) throw new ArgumentException("Unknown rating for " + name + ": " + value); return id; }
            foreach (var property in (_rules["damageRatings"] as JObject ?? throw new ArgumentException("Missing damage ratings.")).Properties()) Known(property.Value, "damage school " + property.Name);
            foreach (var property in (_rules["profileRatings"] as JObject ?? new JObject()).Properties()) Known(property.Value, "profile " + property.Name);
            Known(_rules["healingRating"], "healing");
            if (_rules["equipmentRatingAddend"] != null && _rules["equipmentRatingAddend"].Type != JTokenType.Boolean) throw new ArgumentException("equipmentRatingAddend must be a boolean.");
        }
        private static double Number(JToken value, string name)
        {
            if (value == null || value.Type != JTokenType.Integer && value.Type != JTokenType.Float || double.IsNaN((double)value) || double.IsInfinity((double)value)) throw new ArgumentException("Expected finite number: " + name);
            return (double)value;
        }
        public JObject Data() => (JObject)_rules.DeepClone();
        // Schema 2 has no progression layer: derived stats are the content's weighted rows.
        public JObject DerivedLayer() => _rated ? null : (JObject)_rules["derivedStats"].DeepClone();
        public JObject BaselineProfiles(OriginalContentCatalog catalog)
        {
            var overrides = new JObject();
            if (_rated)
            {
                // Profile cards read their authored base; the rating supplies all scaling.
                foreach (var row in catalog.Table("equipment.basicCardProfiles").OfType<JObject>())
                    overrides[(string)row["id"]] = new JObject { ["baseValue"] = (double)row["baseValue"], ["gainPerTier"] = 0 };
                return overrides;
            }
            foreach (var row in catalog.Table("equipment.basicCardProfiles").OfType<JObject>().Where(x => (string)x["role"] == "attack"))
            {
                var ratio = (double)_rules["baseline"] / (double)row["pointsPerTier"];
                var tier = (string)row["rounding"] == "ceil" ? Math.Ceiling(ratio) : (string)row["rounding"] == "round" ? Math.Floor(ratio + .5) : Math.Floor(ratio);
                overrides[(string)row["id"]] = new JObject { ["baseValue"] = (double)row["baseValue"] + tier * (double)row["gainPerTier"], ["gainPerTier"] = 0 };
            }
            return overrides;
        }
        public JObject ResolveCard(JObject projected, JObject attributes, OriginalContentCatalog catalog)
        {
            var result = (JObject)projected.DeepClone();
            var card = result["card"] as JObject ?? throw new ArgumentException("Expected a projected card.");
            if (card["attributeProgression"] != null) throw new ArgumentException("Attribute progression already applied.");
            var effects = card["effects"] as JArray ?? throw new ArgumentException("Card effects must be an array.");
            if (_rated) { card["attributeProgression"] = RatedReceipts(result, card, effects, attributes, catalog); return result; }
            var damage = effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == "damage");
            var healing = effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == "heal");
            var receipts = new JArray();
            if (damage != null)
            {
                var profileId = (string)card["equipmentProfileId"];
                var school = (string)damage["damageSchool"] ?? (string)card["damageSchool"] ?? "physical";
                var stat = profileId != null ? (string)catalog.Record("equipment.basicCardProfiles", profileId)["scalingStat"] : (string)_rules["damageSchools"][school];
                if (profileId == null && school == "physical" && _rules["damageTags"] is JObject tags)
                    foreach (var tag in tags.Properties())
                        if ((card["tags"] as JArray ?? new JArray()).Any(x => (string)x == tag.Name)) { stat = (string)tag.Value; break; }
                if (stat == null) throw new ArgumentException("Missing offensive scaling for school: " + school);
                var points = Integer(attributes[stat], stat);
                var bonus = checked(Math.Max(0, points - (int)_rules["baseline"]) * (int)_rules["damagePerPoint"]);
                damage["attributeBonus"] = bonus;
                receipts.Add(new JObject { ["operation"] = "damage", ["attribute"] = stat, ["points"] = points, ["bonus"] = bonus, ["scope"] = "totalAcrossHits" });
            }
            if (healing != null)
            {
                var stat = (string)_rules["healingAttribute"];
                var points = Integer(attributes[stat], stat);
                var bonus = checked(Math.Max(0, points - (int)_rules["baseline"]) * (int)_rules["healingPerPoint"]);
                // Keep formula evaluation intact; the executor adds this once after it.
                healing["attributeBonus"] = bonus;
                receipts.Add(new JObject { ["operation"] = "heal", ["attribute"] = stat, ["points"] = points, ["bonus"] = bonus, ["scope"] = "once" });
            }
            card["attributeProgression"] = receipts;
            return result;
        }
        public JObject RatingTerm(string rating, JObject attributes)
        {
            var formula = (JObject)_rules["ratingFormula"]; var row = formula["ratings"]?[rating] as JObject ?? throw new ArgumentException("Unknown rating: " + rating);
            var weighted = 0d;
            foreach (var id in AttributeIds) if (row[id] != null) weighted += Math.Floor(Integer(attributes[id], id) * (double)row[id] + Epsilon);
            var term = (double)row["base"] + Math.Floor(weighted * (double)formula["multiplier"] + Epsilon);
            return new JObject { ["rating"] = rating, ["label"] = row["label"].DeepClone(), ["weighted"] = weighted, ["value"] = term };
        }
        private JArray RatedReceipts(JObject projected, JObject card, JArray effects, JObject attributes, OriginalContentCatalog catalog)
        {
            var receipts = new JArray();
            var profileId = (string)card["equipmentProfileId"];
            var profileRating = profileId == null ? null : (string)_rules["profileRatings"]?[profileId];
            var pieceId = (string)(projected["profileReceipt"] as JObject)?["pieceId"] ?? (string)projected["weaponId"];
            JObject piece = null;
            if (profileId != null && pieceId != null && (bool?)_rules["equipmentRatingAddend"] == true)
                piece = catalog.Table("equipment.armaments").OfType<JObject>().FirstOrDefault(x => (string)x["id"] == pieceId);
            JObject Rated(JObject effect, string operation, string rating, bool equipment, string scope)
            {
                var term = RatingTerm(rating, attributes);
                var attributeTerm = checked((int)(double)term["value"]);
                var equipmentTerm = equipment && piece != null ? (int?)piece[rating == "dr" ? "defenseRating" : "attackRating"] ?? 0 : 0;
                var bonus = Math.Max(0, checked(attributeTerm + equipmentTerm));
                effect["attributeBonus"] = bonus;
                return new JObject { ["operation"] = operation, ["attribute"] = rating, ["rating"] = rating, ["label"] = term["label"].DeepClone(),
                    ["attributeTerm"] = attributeTerm, ["equipmentTerm"] = equipmentTerm, ["bonus"] = bonus, ["scope"] = scope };
            }
            var damage = effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == "damage");
            if (damage != null)
            {
                var school = (string)damage["damageSchool"] ?? (string)card["damageSchool"] ?? "physical";
                var rating = profileRating ?? (string)_rules["damageRatings"]?[school] ?? throw new ArgumentException("Missing offensive rating for school: " + school);
                receipts.Add(Rated(damage, "damage", rating, true, "totalAcrossHits"));
            }
            var block = (string)card["equipmentRole"] == "guard" ? effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == "block") : null;
            if (block != null) receipts.Add(Rated(block, "block", profileRating ?? throw new ArgumentException("Missing guard rating for profile: " + profileId), true, "once"));
            // Healing scales with power only on a magical card (web engine/combatRatings.js cardRatingBonus).
            var healing = effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == "heal");
            var cardSchool = (string)card["damageSchool"];
            if (healing != null && (cardSchool == "magic" || cardSchool == "arcane")) receipts.Add(Rated(healing, "heal", (string)_rules["healingRating"], false, "once"));
            return receipts;
        }
        public JArray Benefits(JObject attributes, JObject derivedRules = null)
        {
            if (_rated) return RatedBenefits(attributes, derivedRules);
            var result = new JArray();
            foreach (var property in ((JObject)_rules["attributes"]).Properties())
            {
                var points = Integer(attributes[property.Name], property.Name); var row = (JObject)property.Value;
                var receipt = new JObject { ["id"] = property.Name, ["points"] = points, ["perPoint"] = row["perPoint"].DeepClone(), ["investment"] = Math.Max(0, points - (int)_rules["baseline"]) };
                if (row["milestone"] is JObject milestone)
                {
                    var interval = Integer(milestone["interval"], "milestone interval");
                    if (interval == 0) throw new ArgumentException("Milestone interval must be positive.");
                    receipt["nextAt"] = checked((points / interval + 1) * interval);
                    receipt["milestone"] = milestone["description"].DeepClone();
                }
                result.Add(receipt);
            }
            return result;
        }
        private static readonly JObject StatLabels = new JObject { ["hp"] = "HP", ["mana"] = "Mana", ["stamina"] = "Stamina", ["energy"] = "Actions", ["draw"] = "Draw" };
        // nextAt: the next score at which some floor(weight x score) term steps up,
        // across the ratings and the weighted derived-stat rows; milestone names what steps.
        private JArray RatedBenefits(JObject attributes, JObject derivedRules)
        {
            var sources = new System.Collections.Generic.List<(string label, JObject row)>();
            foreach (var rating in ((JObject)_rules["ratingFormula"]["ratings"]).Properties()) sources.Add(((string)rating.Value["label"], (JObject)rating.Value));
            // Resolved rows (CreationModel.Rules) or an authored derivedStatRules table.
            var statRows = derivedRules?["rules"] as JObject ?? derivedRules;
            foreach (var stat in statRows?.Properties() ?? Enumerable.Empty<JProperty>())
                if (stat.Value is JObject row && row["sourceStat"] == null) sources.Add(((string)StatLabels[stat.Name] ?? stat.Name, row));
            var result = new JArray();
            foreach (var property in ((JObject)_rules["attributes"]).Properties())
            {
                var points = Integer(attributes[property.Name], property.Name); var row = (JObject)property.Value;
                var receipt = new JObject { ["id"] = property.Name, ["points"] = points, ["perPoint"] = row["perPoint"].DeepClone() };
                var weights = sources.Select(x => (x.label, weight: x.row[property.Name]?.Type == JTokenType.Integer || x.row[property.Name]?.Type == JTokenType.Float ? (double)x.row[property.Name] : 0)).Where(x => x.weight > 0).ToArray();
                if (weights.Length > 0)
                {
                    long Term(double weight, long score) => (long)Math.Floor(score * weight + Epsilon);
                    var next = (long)points + 1;
                    while (next < points + 10000 && !weights.Any(x => Term(x.weight, next) > Term(x.weight, points))) next++;
                    if (!weights.Any(x => Term(x.weight, next) > Term(x.weight, points))) { result.Add(receipt); continue; }
                    receipt["nextAt"] = next;
                    receipt["milestone"] = string.Join(", ", weights.Where(x => Term(x.weight, next) > Term(x.weight, points)).Select(x => "+" + (Term(x.weight, next) - Term(x.weight, points)) + " " + x.label)) + ".";
                }
                result.Add(receipt);
            }
            return result;
        }
        public static int BonusForHit(int bonus, int hit, int hits)
        {
            if (bonus < 0 || hits < 1 || hit < 0 || hit >= hits) throw new ArgumentException("Invalid hit distribution.");
            return bonus / hits + (hit < bonus % hits ? 1 : 0);
        }
    }
}
