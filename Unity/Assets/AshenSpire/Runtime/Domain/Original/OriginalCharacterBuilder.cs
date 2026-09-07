// OriginalCharacterBuilder.cs — validates a completed creation before a native run.
// Preview/Build accept optional profile meta and original starting-option keys.
// Use OriginalStartingOptions for choice lists; pass the same options to both.
// Selected relic bonuses are included in preview and filled once at birth.
// Kit items are granted only at creation. Future equipment changes require ownership.
// Formula/weapon/card rules stay in their components; this composes their results.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCharacterBuilder
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly AttributeProgression _progression;
        private readonly JObject _mechanics;
        public OriginalCharacterBuilder(OriginalContentCatalog catalog, AttributeProgression progression, JObject mechanics)
        { _catalog = catalog; _progression = progression; _mechanics = (JObject)mechanics.DeepClone(); }
        public JObject Preview(CreationModel creation, string kitId = null, JObject meta = null, JObject options = null)
        {
            var attributes = creation.Attributes();
            var selected = new OriginalStartingOptions(_catalog).Resolve(creation.ClassId, kitId, attributes, meta, options);
            var locations = new WeaponLoadout(_catalog); var loadout = (JObject)selected["loadout"];
            var failures = (JArray)selected["requirements"]; var weights = new JObject();
            foreach (var slot in _catalog.Table("equipment.slots"))
            {
                var slotId = (string)slot["id"]; var item = locations.Equipped(loadout, creation.ClassId, slotId);
                if (item == null) continue;
                var weightKey = slotId == "rightHand" ? "mainHandWeight" : slotId == "leftHand" ? "offHandWeight" : slotId == "armor" ? "armorWeight" : "otherCountedWeight";
                weights[weightKey] = ((int?)weights[weightKey] ?? 0) + ((int?)item[(string)item["kind"] == "armor" ? "poiseThreshold" : "weight"] ?? 0);
            }
            var weight = new WeightSystem(_mechanics).Compute((int)attributes["constitution"], (int)attributes["strength"], weights);
            var deck = new WeaponCardComposer(_catalog).CreateStartingDeck(loadout, creation.ClassId);
            var projection = new WeaponCardProjection(_catalog); var overrides = _progression.BaselineProfiles(_catalog);
            var cards = new JArray(deck.OfType<JObject>().Select(instance => _progression.ResolveCard(projection.Resolve(instance, loadout, creation.ClassId, attributes, overrides), attributes, _catalog)));
            var modifiers = new EquipmentRunModifiers(_catalog).Resolve(loadout, creation.ClassId);
            var resourceProjection = new OriginalPlayerProjection(_catalog, _mechanics).Preview(new JObject {
                ["classId"] = creation.ClassId, ["attributes"] = attributes.DeepClone(), ["loadout"] = loadout.DeepClone(),
                ["relics"] = new JArray(selected["relicId"].DeepClone()), ["progression"] = _progression.Data() });
            var resources = (JObject)resourceProjection["resources"];
            return new JObject { ["canBegin"] = creation.CanBegin && failures.Count == 0, ["remaining"] = creation.Remaining,
                ["classId"] = creation.ClassId, ["kitId"] = selected["startingKitId"].DeepClone(), ["attributes"] = attributes, ["loadout"] = loadout, ["deck"] = deck,
                ["startingKitSnapshot"] = selected["startingKitSnapshot"].DeepClone(), ["relicId"] = selected["relicId"].DeepClone(),
                ["cards"] = cards, ["resources"] = resources, ["resourceReceipt"] = resourceProjection, ["equipmentModifiers"] = modifiers, ["weights"] = weights, ["weight"] = weight, ["requirements"] = failures };
        }
        public JObject Build(CreationModel creation, string kitId = null, JObject meta = null, JObject options = null)
        {
            var preview = Preview(creation, kitId, meta, options);
            if (!(bool)preview["canBegin"]) throw new ArgumentException("Spend all points and meet the selected kit's requirements before beginning.");
            var hero = _catalog.Record("classes", creation.ClassId); var resources = (JObject)preview["resources"];
            var allocation = hero["startingFlaskAllocation"];
            return new JObject { ["id"] = "player", ["kind"] = "player", ["alive"] = true,
                ["attributeMode"] = creation.ModeId,
                ["startingKitId"] = preview["kitId"].DeepClone(), ["startingKitSnapshot"] = preview["startingKitSnapshot"].DeepClone(), ["profileMeta"] = meta?.DeepClone() ?? new JObject(),
                ["classId"] = creation.ClassId, ["attributes"] = preview["attributes"].DeepClone(), ["loadout"] = preview["loadout"].DeepClone(), ["deck"] = preview["deck"].DeepClone(),
                ["hp"] = resources["hp"].DeepClone(), ["maxHp"] = resources["hp"].DeepClone(), ["maxMana"] = resources["mana"].DeepClone(), ["mana"] = resources["mana"].DeepClone(),
                ["maxStamina"] = resources["stamina"].DeepClone(), ["stamina"] = resources["stamina"].DeepClone(), ["energy"] = resources["energy"].DeepClone(), ["draw"] = resources["draw"].DeepClone(),
                ["statuses"] = new JObject(), ["block"] = 0, ["weightClass"] = preview["weight"]["weightClass"].DeepClone(), ["weight"] = preview["weight"].DeepClone(), ["weights"] = preview["weights"].DeepClone(),
                ["flaskCharges"] = new FlaskChargePool((int)_catalog.Data()["balance"]["flaskCapacity"], (int)allocation["hp"], (int)allocation["mana"]).Snapshot(),
                ["relicIds"] = new JArray(preview["relicId"].DeepClone()),
                ["relics"] = new JArray(preview["relicId"].DeepClone()),
                ["startStatuses"] = preview["equipmentModifiers"]["startStatuses"].DeepClone(), ["progression"] = _progression.Data() };
        }
    }
}

