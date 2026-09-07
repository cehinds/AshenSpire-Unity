// EquipmentInventory.cs — run-owned loadout, ownership, and deck composition.
// Construct once when a run is born; keep Snapshot with its save and restore through
// Restore. All public reads are copies and all equipment mutations commit together.
// Combat callers must use their resource/swap service before selecting active sets;
// this aggregate deliberately offers out-of-combat selection only, so no unpaid
// swap can bypass stamina/action costs. Mid-combat equip must supply all four piles.
// Item upgrades and smith mounts are not supported by this versioned snapshot.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class EquipmentInventory
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly WeaponLoadout _locations;
        private readonly WeaponCardComposer _composer;
        private JObject _loadout;
        private JArray _deck;
        private JObject _attributes;
        private HashSet<string> _owned;
        private readonly string _classId;
        private readonly int _attackSlotCount;
        public string ClassId => _classId;
        public int AttackSlotCount => _attackSlotCount;
        public EquipmentInventory(OriginalContentCatalog catalog, string classId, JObject attributes, IEnumerable<string> ownedItemRefs = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _locations = new WeaponLoadout(catalog); _composer = new WeaponCardComposer(catalog);
            _classId = classId; _attributes = ValidateAttributes(attributes);
            _loadout = _locations.Create(classId);
            _owned = new HashSet<string>(ownedItemRefs ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var piece in _locations.Pieces(_loadout, classId).OfType<JObject>()) _owned.Add(WeaponLoadout.ItemRef(piece));
            ValidateOwnership();
            _deck = _composer.CreateStartingDeck(_loadout, classId);
            _attackSlotCount = _deck.Count(x => (string)x["equipmentRole"] == "attack");
        }
        private EquipmentInventory(OriginalContentCatalog catalog, JObject snapshot)
        {
            _catalog = catalog; _locations = new WeaponLoadout(catalog); _composer = new WeaponCardComposer(catalog);
            if ((int?)snapshot["snapshotVersion"] != 1) throw new ArgumentException("Unsupported equipment inventory snapshot version.");
            var legal = new HashSet<string>(new[] { "snapshotVersion", "classId", "attributes", "ownedItemRefs", "loadout", "deck", "attackSlotCount" });
            if (snapshot.Properties().Any(x => !legal.Contains(x.Name))) throw new ArgumentException("Unsupported equipment inventory save field.");
            _classId = (string)snapshot["classId"]; catalog.Record("classes", _classId);
            _attributes = ValidateAttributes(snapshot["attributes"] as JObject);
            _loadout = (JObject)(snapshot["loadout"] as JObject ?? throw new ArgumentException("Missing loadout.")).DeepClone();
            _deck = (JArray)(snapshot["deck"] as JArray ?? throw new ArgumentException("Missing equipment deck.")).DeepClone();
            var owned = snapshot["ownedItemRefs"] as JArray ?? throw new ArgumentException("Missing equipment ownership.");
            _owned = new HashSet<string>(owned.Select(x => (string)x), StringComparer.Ordinal);
            if (_owned.Count != owned.Count) throw new ArgumentException("Duplicate equipment ownership.");
            _attackSlotCount = (int?)snapshot["attackSlotCount"] ?? throw new ArgumentException("Missing born attack quota.");
            if (_attackSlotCount < 0 || _deck.Count(x => (string)x["equipmentRole"] == "attack") != _attackSlotCount) throw new ArgumentException("Saved attack quota differs from deck.");
            ValidateOwnership();
            var ids = new HashSet<string>();
            foreach (var card in _deck)
            {
                if (card is not JObject || string.IsNullOrEmpty((string)card["instanceId"]) || !ids.Add((string)card["instanceId"])) throw new ArgumentException("Invalid or duplicate card instance.");
                _catalog.Record("cards", (string)card["cardId"]);
                if (((int?)card["smithingLevel"] ?? 0) != 0) throw new ArgumentException("Smithing save needs the upgrade resolver.");
            }
            // Validate the saved projection rather than silently repairing tampered cards.
            var expected = _composer.Recompose(_deck, _loadout, _classId);
            if (!JToken.DeepEquals(expected, _deck)) throw new ArgumentException("Saved equipment cards do not match their owners.");
        }
        public static EquipmentInventory Restore(OriginalContentCatalog catalog, JObject snapshot) => new EquipmentInventory(catalog, (JObject)snapshot.DeepClone());
        public JObject Snapshot() => new JObject { ["snapshotVersion"] = 1, ["classId"] = _classId, ["attributes"] = _attributes.DeepClone(), ["ownedItemRefs"] = new JArray(_owned.OrderBy(x => x, StringComparer.Ordinal)), ["loadout"] = _loadout.DeepClone(), ["deck"] = _deck.DeepClone(), ["attackSlotCount"] = _attackSlotCount };
        public JObject Loadout() => (JObject)_loadout.DeepClone();
        public JArray Deck() => (JArray)_deck.DeepClone();
        private JObject ValidateAttributes(JObject attributes)
        {
            if (attributes == null) throw new ArgumentException("Missing equipment attributes.");
            foreach (var attribute in _catalog.Table("attributes"))
            {
                var value = attributes[(string)attribute["id"]];
                if (value?.Type != JTokenType.Integer || (int)value < 0) throw new ArgumentException("Equipment attributes must be nonnegative integers.");
            }
            return (JObject)attributes.DeepClone();
        }
        private void ValidateOwnership()
        {
            foreach (var itemRef in _owned)
            {
                var parts = itemRef?.Split('/') ?? Array.Empty<string>();
                if (parts.Length == 2 && parts[0] == "armament") _catalog.Record("equipment.armaments", parts[1]);
                else if (parts.Length == 3 && parts[0] == "armor" && parts[1] == _classId) _catalog.Record("equipment.armour", parts[2], parts[1]);
                else throw new ArgumentException("Unknown owned item reference: " + itemRef);
            }
            var occupied = new HashSet<string>();
            foreach (var slot in _catalog.Table("equipment.slots").OfType<JObject>())
            {
                var values = _loadout["sets"]?[(string)slot["id"]] as JArray ?? throw new ArgumentException("Missing equipment slot.");
                if (values.Count != Math.Max(1, (int)slot["sets"])) throw new ArgumentException("Equipment set count differs from content.");
                for (var i = 0; i < values.Count; i++)
                {
                    var id = (string)values[i]; if (string.IsNullOrEmpty(id)) continue;
                    var armour = slot["kinds"].Any(x => (string)x == "armor");
                    var piece = _catalog.Record(armour ? "equipment.armour" : "equipment.armaments", id, armour ? _classId : null);
                    if (!WeaponLoadout.Fits(slot, piece) || !_owned.Contains(WeaponLoadout.ItemRef(piece))) throw new ArgumentException("Saved equipment is not owned or does not fit.");
                    if (!armour && !occupied.Add(id)) throw new ArgumentException("Armament is present in two locations.");
                }
                _locations.Equipped(_loadout, _classId, (string)slot["id"]);
            }
            var storage = _loadout["storage"] as JArray ?? throw new ArgumentException("Missing equipment storage.");
            if (storage.Count > ((int?)_catalog.Data()["balance"]["equipment"]["storageSlots"] ?? 8)) throw new ArgumentException("Equipment storage exceeds capacity.");
            foreach (var id in storage)
                if (!_owned.Contains("armament/" + (string)id) || !occupied.Add((string)id)) throw new ArgumentException("Stored armament is duplicated or not owned.");
            _composer.BuildAttackPlan(_loadout, _classId, _attackSlotCount);
        }
        public JObject Equip(string slotId, int setIndex, string itemId, bool inCombat, JObject combatPiles = null)
        {
            if (inCombat && combatPiles == null) return new JObject { ["ok"] = false, ["reason"] = "Mid-combat equipment requires all four piles." };
            var receipt = _locations.Equip(_loadout, _classId, slotId, setIndex, itemId, _owned, _attributes, inCombat);
            if (!(bool)receipt["ok"]) return receipt;
            var loadout = (JObject)receipt["loadout"]; var deck = _composer.Recompose(_deck, loadout, _classId);
            var piles = inCombat ? _composer.ReconcileCombat(combatPiles, loadout, _classId, _attackSlotCount) : null;
            _loadout = loadout; _deck = deck;
            if (piles != null) receipt["piles"] = piles;
            return (JObject)receipt.DeepClone();
        }
        public JObject SelectSetOutOfCombat(string slotId, int index, int openedSets)
        {
            var slot = _catalog.Record("equipment.slots", slotId);
            var values = _loadout["sets"][slotId] as JArray;
            if (openedSets < 1 || openedSets > values.Count || index < 0 || index >= openedSets) return new JObject { ["ok"] = false, ["reason"] = "Equipment set is locked." };
            var next = (JObject)_loadout.DeepClone(); next["active"][slotId] = index;
            try
            {
                var piece = _locations.Equipped(next, _classId, slotId);
                if (piece != null && !(bool)_locations.RequirementReceipt(piece, _attributes)["ok"]) return new JObject { ["ok"] = false, ["reason"] = "Attribute requirements are not met." };
                var deck = _composer.Recompose(_deck, next, _classId); _loadout = next; _deck = deck;
                return new JObject { ["ok"] = true, ["loadout"] = next.DeepClone() };
            }
            catch (ArgumentException error) { return new JObject { ["ok"] = false, ["reason"] = error.Message }; }
        }
        public JObject AcquireArmament(string itemId)
        {
            var item = _catalog.Record("equipment.armaments", itemId); var itemRef = WeaponLoadout.ItemRef(item);
            if (_owned.Contains(itemRef)) return new JObject { ["ok"] = false, ["reason"] = "Item is already owned." };
            var capacity = (int?)_catalog.Data()["balance"]["equipment"]["storageSlots"] ?? 8;
            if (((JArray)_loadout["storage"]).Count >= capacity) return new JObject { ["ok"] = false, ["reason"] = "Inventory is full." };
            _owned.Add(itemRef); ((JArray)_loadout["storage"]).Add(itemId);
            return new JObject { ["ok"] = true, ["itemRef"] = itemRef };
        }
    }
}
