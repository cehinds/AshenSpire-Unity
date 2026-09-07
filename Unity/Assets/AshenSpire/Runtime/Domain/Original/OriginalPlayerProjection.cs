// OriginalPlayerProjection.cs — one non-combat projection after equipment, relic,
// attribute or smith changes. Wire Reconcile into OriginalRunContent at Start/Restore.
// Author original derivedStatRules, relic modifiers and equipment tables; a run's
// frozen progression layer determines the owner's Unity improvements. Base rules
// are saved separately from folded relic bonuses, so repeated reconciliation never
// stacks the same bonus twice. Maxima retain absolute deficits, including deficits
// hidden by a smaller vessel. Newborn full pools therefore stay full automatically.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalPlayerProjection
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _data, _mechanics;
        public OriginalPlayerProjection(OriginalContentCatalog catalog, JObject mechanics)
        { _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); _data = catalog.Data(); _mechanics = (JObject)mechanics.DeepClone(); }
        private static int Integer(JToken value, string label, bool nonnegative = true)
        {
            if (value == null || value.Type != JTokenType.Integer && value.Type != JTokenType.Float) throw new ArgumentException("Missing numeric player value: " + label);
            var number = (double)value;
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Truncate(number) != number || number < int.MinValue || number > int.MaxValue || nonnegative && number < 0) throw new ArgumentException("Invalid player integer: " + label);
            return (int)number;
        }
        private static string ClassId(JObject run) => (string)run["classId"] ?? (string)run["class"];
        private static JArray Relics(JObject run) => run["relics"] as JArray ?? run["relicIds"] as JArray ?? new JArray();
        private JObject BaseRules(JObject run, JObject hero)
        {
            JObject rules;
            if (run["playerProjectionRules"] is JObject saved)
            {
                if ((int?)saved["snapshotVersion"] != 1 || !(saved["baseRules"] is JObject)) throw new ArgumentException("Unsupported player projection rules snapshot.");
                rules = (JObject)saved["baseRules"].DeepClone();
            }
            else
            {
                var layer = run["progression"]?["derivedStats"] as JObject;
                rules = DerivedStatCalculator.Resolve((JObject)_data["derivedStatRules"], layer);
                foreach (var row in rules.Properties().Select(x => (JObject)x.Value)) foreach (var key in new[] { "base", "gainPerTier" })
                    if (row[key] is JObject reference)
                    {
                        if ((string)reference["strategy"] != "classField") throw new ArgumentException("Unsupported derived class strategy.");
                        row[key] = hero[(string)reference["field"]]?.DeepClone() ?? throw new ArgumentException("Missing derived class field.");
                    }
            }
            // Every rule is evaluated before it can be installed into a saved run.
            foreach (var row in rules.Properties()) _ = DerivedStatCalculator.Receipt(rules, row.Name, (JObject)run["attributes"], hero);
            return rules;
        }
        public JObject RelicModifiers(JObject run, JObject baseRules)
        {
            var resources = new JObject(); foreach (var id in new[] { "hp", "mana", "stamina" }) resources[id] = new JObject { ["flat"] = 0, ["attributeTiers"] = new JArray(), ["total"] = 0 };
            var damage = new JObject { ["physical"] = 0, ["magic"] = 0, ["arcane"] = 0, ["holy"] = 0, ["fire"] = 0 }; var sources = new JArray();
            var attributes = (JObject)run["attributes"]; var upgrades = new ItemUpgradeService(_catalog);
            foreach (var id in Relics(run).Values<string>())
            {
                var relic = upgrades.ResolveItem("relic/" + id, Integer(run["itemUpgradeLevels"]?["relic/" + id] ?? new JValue(0), "relic tier")); var index = 0;
                foreach (var modifier in relic["passives"]?["modifiers"] as JArray ?? new JArray())
                {
                    var tag = (string)modifier["tag"]; var source = new JObject { ["relicId"] = id, ["index"] = index++, ["tag"] = tag };
                    if (tag == "resource.flat")
                    {
                        var resource = (string)modifier["resource"]; var row = resources[resource] as JObject ?? throw new ArgumentException("Unknown relic resource."); var amount = Integer(modifier["amount"], "relic flat", false);
                        row["flat"] = checked((int)row["flat"] + amount); row["total"] = checked((int)row["total"] + amount); source["resource"] = resource; source["value"] = amount;
                    }
                    else if (tag == "resource.attributeTier")
                    {
                        var resource = (string)modifier["resource"]; var row = resources[resource] as JObject ?? throw new ArgumentException("Unknown relic resource.");
                        var stat = (string)modifier["sourceStat"]; var per = Integer(modifier["pointsPerTier"] ?? baseRules[resource]?["pointsPerTier"], "relic points per tier"); if (per == 0) throw new ArgumentException("Relic tier cannot be zero.");
                        var tier = (int)Math.Floor(Integer(attributes[stat], stat) / (double)per); var amount = Integer(modifier["amountPerTier"], "relic tier amount", false); var value = checked(tier * amount);
                        var term = new JObject { ["sourceStat"] = stat, ["pointsPerTier"] = per, ["amountPerTier"] = amount, ["tier"] = tier, ["value"] = value };
                        ((JArray)row["attributeTiers"]).Add(term); row["total"] = checked((int)row["total"] + value); source["resource"] = resource; foreach (var property in term.Properties()) source[property.Name] = property.Value.DeepClone();
                    }
                    else if (tag == "damage.school.flat")
                    {
                        var school = (string)modifier["school"]; if (damage[school] == null) throw new ArgumentException("Unknown relic damage school."); var amount = Integer(modifier["amount"], "school damage", false);
                        damage[school] = checked((int)damage[school] + amount); source["school"] = school; source["value"] = amount;
                    }
                    else throw new ArgumentException("Unknown relic modifier: " + tag);
                    sources.Add(source);
                }
            }
            return new JObject { ["resources"] = resources, ["damageBySchoolAdd"] = damage, ["sources"] = sources };
        }
        public JObject Preview(JObject run)
        {
            var hero = _catalog.Record("classes", ClassId(run)); var baseRules = BaseRules(run, hero); var folded = (JObject)baseRules.DeepClone(); var relics = RelicModifiers(run, baseRules);
            foreach (var resource in ((JObject)relics["resources"]).Properties())
            {
                var rule = (JObject)folded[resource.Name]; rule["base"] = (double)rule["base"] + (int)resource.Value["flat"];
                foreach (var term in resource.Value["attributeTiers"])
                {
                    if ((string)term["sourceStat"] != (string)rule["sourceStat"] || (double)term["pointsPerTier"] != (double)rule["pointsPerTier"] || (string)rule["rounding"] != "floor") throw new ArgumentException("Relic tier is incompatible with the resolved derived rule.");
                    rule["gainPerTier"] = (double)rule["gainPerTier"] + (int)term["amountPerTier"];
                }
            }
            var equipment = new EquipmentRunModifiers(_catalog).Resolve((JObject)run["loadout"], ClassId(run)); var derived = new JObject(); var receipts = new JArray();
            foreach (var row in folded.Properties())
            {
                var receipt = DerivedStatCalculator.Receipt(folded, row.Name, (JObject)run["attributes"], hero); var value = Integer(receipt["value"], row.Name);
                var bonus = new[] { "hp", "mana", "stamina" }.Contains(row.Name) ? Integer(equipment["max" + char.ToUpperInvariant(row.Name[0]) + row.Name.Substring(1)], "equipment bonus", false) : 0;
                var adjustment = row.Name == "hp" ? Integer(run["maxHpAdjustment"] ?? new JValue(0), "max HP adjustment", false) : 0;
                derived[row.Name] = Math.Max(row.Name == "hp" ? 1 : 0, checked(value + bonus + adjustment)); receipt["equipmentBonus"] = bonus; receipt["adjustment"] = adjustment; receipt["final"] = derived[row.Name].DeepClone(); receipts.Add(receipt);
            }
            var weights = new JObject { ["mainHandWeight"] = 0, ["offHandWeight"] = 0, ["armorWeight"] = 0, ["otherCountedWeight"] = 0 }; var poise = 0;
            var locations = new WeaponLoadout(_catalog); var upgrades = new ItemUpgradeService(_catalog); var sources = new JArray();
            foreach (var slot in _catalog.Table("equipment.slots"))
            {
                var piece = locations.Equipped((JObject)run["loadout"], ClassId(run), (string)slot["id"]); if (piece == null) continue;
                var itemRef = WeaponLoadout.ItemRef(piece); var level = Integer(run["itemUpgradeLevels"]?[itemRef] ?? new JValue(0), "item tier");
                if ((string)piece["kind"] == "armor") piece = upgrades.ResolveItem(itemRef, level);
                var value = Integer(piece[(string)piece["kind"] == "armor" ? "poiseThreshold" : "weight"] ?? new JValue(0), "item weight");
                var key = (string)piece["kind"] == "armor" ? "armorWeight" : (string)slot["hand"] == "right" ? "mainHandWeight" : (string)slot["hand"] == "left" ? "offHandWeight" : "otherCountedWeight";
                weights[key] = checked((int)weights[key] + value); var threshold = Integer(piece["poiseThreshold"] ?? new JValue(0), "equipment poise"); poise = checked(poise + threshold);
                sources.Add(new JObject { ["itemRef"] = itemRef, ["weight"] = value, ["poise"] = threshold });
            }
            foreach (var id in Relics(run).Values<string>())
            { var item = upgrades.ResolveItem("relic/" + id, Integer(run["itemUpgradeLevels"]?["relic/" + id] ?? new JValue(0), "relic tier")); poise = checked(poise + Integer(item["passives"]?["poiseThresholdAdd"] ?? new JValue(0), "relic poise")); }
            var attributes = (JObject)run["attributes"]; var weight = new WeightSystem(_mechanics).Compute(Integer(attributes["constitution"], "constitution"), Integer(attributes["strength"], "strength"), weights);
            return new JObject { ["baseRules"] = baseRules, ["foldedRules"] = folded, ["resources"] = derived, ["derivedReceipts"] = receipts, ["relicModifiers"] = relics, ["equipmentModifiers"] = equipment, ["weights"] = weights, ["weight"] = weight, ["poiseThreshold"] = poise, ["equipmentSources"] = sources };
        }
        public void Reconcile(JObject run)
        {
            // Calculate on a draft first: a bad item row cannot partially resize pools.
            var next = (JObject)run.DeepClone(); var preview = Preview(next); var deficits = new JObject();
            foreach (var resource in new[] { "hp", "mana", "stamina" })
            {
                var maxKey = "max" + char.ToUpperInvariant(resource[0]) + resource.Substring(1); var maximum = Integer(preview["resources"][resource], resource);
                var oldMaximum = next[maxKey] == null ? maximum : Integer(next[maxKey], maxKey); var current = next[resource] == null ? oldMaximum : Integer(next[resource], resource);
                var prior = next["equipmentPoolDeficits"]?[resource];
                var moved = EquipmentRunModifiers.MovePool(oldMaximum, current, maximum, prior == null ? (int?)null : Integer(prior, "resource deficit"));
                next[maxKey] = maximum; next[resource] = moved["current"].DeepClone(); deficits[resource] = moved["deficit"].DeepClone();
            }
            next["equipmentPoolDeficits"] = deficits;
            next["equipmentPoolBonuses"] = new JObject { ["maxHp"] = preview["equipmentModifiers"]["maxHp"].DeepClone(), ["maxMana"] = preview["equipmentModifiers"]["maxMana"].DeepClone(), ["maxStamina"] = preview["equipmentModifiers"]["maxStamina"].DeepClone() };
            next["maxHpAdjustment"] = next["maxHpAdjustment"]?.DeepClone() ?? new JValue(0);
            next["playerProjectionRules"] = new JObject { ["snapshotVersion"] = 1, ["baseRules"] = preview["baseRules"].DeepClone() };
            next["derivedStatRules"] = preview["foldedRules"].DeepClone(); next["relicModifierReceipt"] = preview["relicModifiers"].DeepClone(); next["damageBySchoolAdd"] = preview["relicModifiers"]["damageBySchoolAdd"].DeepClone();
            next["energy"] = preview["resources"]["energy"].DeepClone(); next["draw"] = preview["resources"]["draw"].DeepClone(); next["energyMax"] = next["energy"].DeepClone(); next["drawPerTurn"] = next["draw"].DeepClone();
            next["startStatuses"] = preview["equipmentModifiers"]["startStatuses"].DeepClone(); next["weights"] = preview["weights"].DeepClone(); next["weight"] = preview["weight"].DeepClone(); next["weightClass"] = preview["weight"]["weightClass"].DeepClone(); next["poiseThreshold"] = preview["poiseThreshold"].DeepClone();
            next["relics"] = Relics(next).DeepClone(); next["relicIds"] = next["relics"].DeepClone();
            if (next["deck"] is JArray deck)
            {
                next["deck"] = new WeaponCardComposer(_catalog).Recompose(deck, (JObject)next["loadout"], ClassId(next), next["itemMounts"] as JObject);
                new ItemUpgradeService(_catalog).RestampCards(next);
            }
            run.RemoveAll(); foreach (var property in next.Properties()) run[property.Name] = property.Value.DeepClone();
        }
    }
}

