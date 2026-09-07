// WeaponLoadout.cs — native equipment locations and atomic equip transitions.
// Edit equipment.slots, armaments, armour and requirements in Original/content.json.
// The slot owns hand identity; an item's hand only constrains eligibility. Callers
// supply ownership and combat context explicitly. This does not charge active-set
// swap costs or implement smith upgrades; those services must call their own gates.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class WeaponLoadout
    {
        private readonly OriginalContentCatalog _catalog;
        public WeaponLoadout(OriginalContentCatalog catalog) { _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }
        public JObject Create(string classId)
        {
            _catalog.Record("classes", classId);
            var sets = new JObject(); var active = new JObject();
            foreach (var slot in _catalog.Table("equipment.slots"))
            {
                var id = (string)slot["id"];
                sets[id] = new JArray(Enumerable.Range(0, Math.Max(1, (int)slot["sets"])).Select(_ => JValue.CreateNull()));
                active[id] = 0;
            }
            var armour = _catalog.Table("equipment.armour").FirstOrDefault(x => (string)x["classId"] == classId && (string)x["unlock"] == "");
            var kit = _catalog.Table("equipment.startingKits").FirstOrDefault(x => (string)x["classId"] == classId && (bool?)x["baseline"] == true);
            if (armour != null && sets["armor"] != null) sets["armor"][0] = armour["id"].DeepClone();
            foreach (var hand in new[] { "rightHand", "leftHand" }) if (kit?[hand]?.Type == JTokenType.String && !string.IsNullOrEmpty((string)kit[hand])) sets[hand][0] = kit[hand].DeepClone();
            return new JObject { ["sets"] = sets, ["active"] = active, ["storage"] = new JArray(), ["creationArmourGrant"] = armour == null ? null : new JObject { ["classId"] = classId, ["id"] = armour["id"].DeepClone() } };
        }
        public JObject Equipped(JObject loadout, string classId, string slotId)
        {
            var slot = _catalog.Record("equipment.slots", slotId);
            var values = loadout["sets"]?[slotId] as JArray ?? throw new ArgumentException("Missing loadout slot: " + slotId);
            var active = (int?)loadout["active"]?[slotId] ?? 0;
            if (active < 0 || active >= values.Count) throw new ArgumentException("Invalid active set: " + slotId);
            var id = (string)values[active];
            if (string.IsNullOrEmpty(id)) return null;
            var isArmour = slot["kinds"].Any(x => (string)x == "armor");
            var piece = _catalog.Record(isArmour ? "equipment.armour" : "equipment.armaments", id, isArmour ? classId : null);
            piece["tags"] = new JArray(_catalog.Tags(isArmour ? "armour" : "armament", piece));
            return piece;
        }
        public JArray Pieces(JObject loadout, string classId) => new JArray(_catalog.Table("equipment.slots").Select(x => Equipped(loadout, classId, (string)x["id"])).Where(x => x != null));
        public static string ItemRef(JObject piece) => (string)piece["kind"] == "armor" ? "armor/" + (string)piece["classId"] + "/" + (string)piece["id"] : "armament/" + (string)piece["id"];
        public JObject RequirementReceipt(JObject piece, JObject attributes, JObject itemUpgradeLevels = null)
        {
            var requirements = new JArray(); var failures = new JArray();
            foreach (var property in (piece["requirements"]?["attributes"] as JObject ?? new JObject()).Properties())
            {
                _catalog.Record("attributes", property.Name);
                if (property.Value.Type != JTokenType.Integer || (int)property.Value < 0) throw new ArgumentException("Invalid requirement: " + property.Name);
                var actual = attributes?[property.Name];
                var finite = actual != null && (actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float) && !double.IsNaN((double)actual) && !double.IsInfinity((double)actual);
                var level = (int?)itemUpgradeLevels?[ItemRef(piece)] ?? 0;
                var delta = level == 0 ? 0 : new ItemUpgradeService(_catalog).RequirementDelta(ItemRef(piece), property.Name, level);
                var required = Math.Max(0, (double)property.Value + delta);
                var row = new JObject { ["attributeId"] = property.Name, ["baseRequired"] = property.Value.DeepClone(), ["reduction"] = -delta, ["required"] = required, ["actual"] = finite ? actual.DeepClone() : JValue.CreateNull() };
                requirements.Add(row); if (!finite || (double)actual < required) failures.Add(row.DeepClone());
            }
            return new JObject { ["itemId"] = piece["id"].DeepClone(), ["requirements"] = requirements, ["failures"] = failures, ["ok"] = failures.Count == 0 };
        }
        public static bool Fits(JObject slot, JObject piece)
        {
            var eligibility = (string)piece["hand"];
            return slot["kinds"].Any(x => (string)x == (string)piece["kind"]) && (eligibility != "right" && eligibility != "left" || eligibility == (string)slot["hand"]);
        }
        // A successful receipt contains a NEW loadout. Failure never mutates input.
        public JObject Equip(JObject loadout, string classId, string slotId, int setIndex, string itemId, ISet<string> ownedItemRefs, JObject attributes, bool inCombat, JObject itemUpgradeLevels = null)
        {
            JObject Refuse(string reason) => new JObject { ["ok"] = false, ["reason"] = reason };
            var slot = _catalog.Record("equipment.slots", slotId);
            var values = loadout["sets"]?[slotId] as JArray;
            if (values == null || setIndex < 0 || setIndex >= values.Count) return Refuse("Unknown equipment set.");
            var balance = _catalog.Data()["balance"]["equipment"];
            if (inCombat && (bool?)balance["allowChangesInCombat"] != true) return Refuse("Equipment changes are disabled in combat.");
            JObject piece = null;
            if (!string.IsNullOrEmpty(itemId))
            {
                try { piece = _catalog.Record(slot["kinds"].Any(x => (string)x == "armor") ? "equipment.armour" : "equipment.armaments", itemId, slot["kinds"].Any(x => (string)x == "armor") ? classId : null); }
                catch (ArgumentException) { return Refuse("Unknown equipment."); }
                if (!Fits(slot, piece)) return Refuse("This item does not fit this slot.");
                if (ownedItemRefs == null || !ownedItemRefs.Contains(ItemRef(piece))) return Refuse("This item is not owned.");
                var requirement = RequirementReceipt(piece, attributes, itemUpgradeLevels);
                if (!(bool)requirement["ok"]) return new JObject { ["ok"] = false, ["reason"] = "Attribute requirements are not met.", ["requirements"] = requirement };
            }
            var next = (JObject)loadout.DeepClone();
            var nextValues = (JArray)next["sets"][slotId];
            var previous = (string)nextValues[setIndex];
            var isHand = new[] { "right", "left" }.Contains((string)slot["hand"]);
            if (isHand)
            {
                var storage = (JArray)next["storage"];
                foreach (var entry in storage.Where(x => (string)x == itemId).ToArray()) entry.Remove();
                if (!string.IsNullOrEmpty(itemId)) foreach (var handSlot in _catalog.Table("equipment.slots").Where(x => new[] { "right", "left" }.Contains((string)x["hand"])))
                    foreach (var entry in ((JArray)next["sets"][(string)handSlot["id"]]).Where(x => (string)x == itemId).ToArray()) entry.Replace(JValue.CreateNull());
                if (!string.IsNullOrEmpty(previous) && previous != itemId && !storage.Any(x => (string)x == previous)) storage.Add(previous);
                if (storage.Count > ((int?)balance["storageSlots"] ?? 8)) return Refuse("Inventory is full.");
            }
            nextValues[setIndex] = string.IsNullOrEmpty(itemId) ? JValue.CreateNull() : new JValue(itemId);
            try { new WeaponCardComposer(_catalog).BuildAttackPlan(next, classId); }
            catch (ArgumentException ex) { return Refuse(ex.Message); }
            return new JObject { ["ok"] = true, ["loadout"] = next, ["slotId"] = slotId, ["setIndex"] = setIndex, ["previousId"] = previous, ["itemId"] = itemId };
        }
    }
}
