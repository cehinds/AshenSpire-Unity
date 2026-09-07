// AttributeProgression.cs — owner-authored point benefits layered over original rules.
// DATA: GameContent/Unity/Original/progression.json. Persist Data() with each run.
// ORDER: weapon projection with BaselineProfiles(), then ResolveCard(), then costs/combat.
// A damage bonus is a TOTAL across one effect's hits; the combat executor distributes
// the remainder over its first hits. Never add it independently to every hit.
// This is an intentional Unity balance improvement, separate from original parity.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class AttributeProgression
    {
        private readonly JObject _rules;
        public AttributeProgression(JObject rules)
        {
            _rules = (JObject)(rules ?? throw new ArgumentNullException(nameof(rules))).DeepClone();
            Integer(_rules["baseline"], "baseline");
            var rows = _rules["attributes"] as JObject ?? throw new ArgumentException("Missing attribute benefits.");
            foreach (var id in new[] { "strength", "dexterity", "intelligence", "wisdom", "constitution" })
            {
                var row = rows[id] as JObject ?? throw new ArgumentException("Missing benefit: " + id);
                if (string.IsNullOrWhiteSpace((string)row["perPoint"])) throw new ArgumentException("Missing point explanation: " + id);
            }
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
        public JObject Data() => (JObject)_rules.DeepClone();
        public JObject DerivedLayer() => (JObject)_rules["derivedStats"].DeepClone();
        public JObject BaselineProfiles(OriginalContentCatalog catalog)
        {
            var overrides = new JObject();
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
        public JArray Benefits(JObject attributes)
        {
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
        public static int BonusForHit(int bonus, int hit, int hits)
        {
            if (bonus < 0 || hits < 1 || hit < 0 || hit >= hits) throw new ArgumentException("Invalid hit distribution.");
            return bonus / hits + (hit < bonus % hits ? 1 : 0);
        }
    }
}
