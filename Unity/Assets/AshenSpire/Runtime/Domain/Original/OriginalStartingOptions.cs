// OriginalStartingOptions.cs — profile-aware original character equipment choices.
// Edit characterCreation.classes and equipment.startingKits/armour to add choices.
// Resolve accepts original option keys startingHands/startingArmourId/startingRelicId.
// The uncustomized baseline kit retains its birth waiver. Explicit hand changes
// pass requirements, closing the original customized-baseline waiver loophole.
// Save startingKitId/Snapshot and profileMeta; ValidateSaved verifies their identity.
// Existing native saves may adopt a baseline only via the explicit legacy flag.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalStartingOptions
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _data;
        public OriginalStartingOptions(OriginalContentCatalog catalog) { _catalog = catalog; _data = catalog.Data(); }
        private JObject Config(string classId) => _data["characterCreation"]?["classes"]?[classId] as JObject ?? throw new ArgumentException("No character creation configuration for " + classId);
        private static string OptionalId(JToken token) => string.IsNullOrEmpty((string)token) ? null : (string)token;
        private static JArray Array(JToken token) => token as JArray ?? new JArray();
        private bool KitDiscovered(JToken kit, JObject meta) => (bool?)kit["baseline"] == true || new[] { "rightHand", "leftHand" }.Select(k => OptionalId(kit[k])).Where(id => id != null).All(id => Array(meta?["discoveredArmaments"]).Values<string>().Contains(id));
        public JArray AvailableKits(string classId, JObject meta = null)
        {
            var eligible = Array(_catalog.Record("classes", classId)["eligibleStartingKitIds"]).Values<string>().ToHashSet(); var result = new JArray();
            foreach (JObject row in _catalog.Table("equipment.startingKits").Where(r => (string)r["classId"] == classId && eligible.Contains((string)r["id"])))
            {
                var available = KitDiscovered(row, meta); if (!available && (string)_data["balance"]["equipment"]["startingKitDiscovery"]["undiscoveredPresentation"] == "hidden") continue;
                if (!available) { result.Add(new JObject { ["id"] = row["id"].DeepClone(), ["classId"] = classId, ["baseline"] = false, ["available"] = false, ["silhouette"] = true }); continue; }
                row["tags"] = new JArray(_catalog.Tags("startingKit", row)); row["available"] = true; row["pieceIds"] = new JArray(new[] { "rightHand", "leftHand" }.Select(k => OptionalId(row[k])).Where(id => id != null)); result.Add(row);
            }
            return result;
        }
        private bool ArmourEligible(JToken row, string classId, JObject meta) => (string)row["unlock"] == "" || Array(Config(classId)["armourIds"]).Values<string>().Contains((string)row["id"]) || Array(meta?["unlocked"]).Values<string>().Contains((string)row["unlock"]);
        public JArray AvailableArmour(string classId, JObject meta = null) => new JArray(_catalog.Table("equipment.armour").Where(row => (string)row["classId"] == classId && ArmourEligible(row, classId, meta)).Select(row => new JObject { ["id"] = row["id"].DeepClone(), ["label"] = row["name"].DeepClone(), ["blurb"] = row["blurb"]?.DeepClone(), ["free"] = (string)row["unlock"] == "" }));
        public JArray AvailableHands(string classId, string slotId = null)
        {
            var locations = new WeaponLoadout(_catalog); var result = new JArray();
            foreach (var id in Array(Config(classId)["handIds"]).Values<string>())
            { var piece = _catalog.Record("equipment.armaments", id); if (slotId == null || WeaponLoadout.Fits(_catalog.Record("equipment.slots", slotId), piece)) result.Add(piece); }
            return result;
        }
        public JArray AvailableRelics(string classId) => new JArray(Array(Config(classId)["relicIds"]).Values<string>().Select(id => _catalog.Record("relics", id)));
        public static JObject SelectHand(JObject current, string hand, string itemId)
        {
            if (!new[] { "rightHand", "leftHand" }.Contains(hand)) throw new ArgumentException("Unknown starting hand.");
            var next = new JObject { ["leftHand"] = OptionalId(current?["leftHand"]), ["rightHand"] = OptionalId(current?["rightHand"]) }; var other = hand == "rightHand" ? "leftHand" : "rightHand";
            if (!string.IsNullOrEmpty(itemId) && (string)next[other] == itemId) next[other] = JValue.CreateNull(); next[hand] = string.IsNullOrEmpty(itemId) ? JValue.CreateNull() : new JValue(itemId); return next;
        }
        private JObject ResolveKit(string classId, string kitId, JObject meta)
        {
            var rows = _catalog.Table("equipment.startingKits"); kitId = string.IsNullOrEmpty(kitId) ? (string)rows.FirstOrDefault(row => (string)row["classId"] == classId && (bool?)row["baseline"] == true)?["id"] : kitId;
            var hero = _catalog.Record("classes", classId); if (!Array(hero["eligibleStartingKitIds"]).Values<string>().Contains(kitId)) throw new ArgumentException("Starting kit is unavailable to this class.");
            var kit = _catalog.Record("equipment.startingKits", kitId); if ((string)kit["classId"] != classId || !KitDiscovered(kit, meta)) throw new ArgumentException("Starting kit has not been discovered."); return kit;
        }
        private static JObject KitSnapshot(JObject kit) => new JObject { ["id"] = kit["id"].DeepClone(), ["classId"] = kit["classId"].DeepClone(), ["rightHand"] = OptionalId(kit["rightHand"]), ["leftHand"] = OptionalId(kit["leftHand"]) };
        public JObject Resolve(string classId, string kitId, JObject attributes, JObject meta = null, JObject options = null)
        {
            var kit = ResolveKit(classId, kitId, meta); var locations = new WeaponLoadout(_catalog); var loadout = locations.Create(classId); var requested = options?["startingHands"] as JObject;
            if (options?["startingHands"] != null && options["startingHands"].Type != JTokenType.Null && requested == null) throw new ArgumentException("Starting hands must be an object.");
            var hands = requested ?? kit; var selected = new JObject();
            foreach (var hand in new[] { "rightHand", "leftHand" })
            {
                var id = OptionalId(hands[hand]); selected[hand] = id;
                if (requested != null && id != null && !AvailableHands(classId, hand).Any(row => (string)row["id"] == id)) throw new ArgumentException(hand + ": this starting armament is unavailable or does not fit.");
                loadout["sets"][hand][0] = id == null ? JValue.CreateNull() : new JValue(id);
            }
            if (OptionalId(selected["leftHand"]) != null && (string)selected["leftHand"] == (string)selected["rightHand"]) throw new ArgumentException("A starting armament cannot occupy both hands.");
            var armourId = OptionalId(options?["startingArmourId"]); var armour = armourId == null ? _catalog.Table("equipment.armour").OfType<JObject>().FirstOrDefault(row => (string)row["classId"] == classId && (string)row["unlock"] == "") : _catalog.Record("equipment.armour", armourId, classId);
            if (armour == null || !ArmourEligible(armour, classId, meta)) throw new ArgumentException("Starting armour has not been earned or configured.");
            loadout["sets"]["armor"][0] = armour["id"].DeepClone(); loadout["creationArmourGrant"] = new JObject { ["classId"] = classId, ["id"] = armour["id"].DeepClone() };
            var relicId = OptionalId(options?["startingRelicId"]) ?? (string)_catalog.Record("classes", classId)["startingRelic"];
            if (!AvailableRelics(classId).Any(row => (string)row["id"] == relicId)) throw new ArgumentException("Starting relic is unavailable to this class.");
            var requirements = new JArray();
            if (requested != null || (bool?)kit["baseline"] != true)
                foreach (var hand in new[] { "rightHand", "leftHand" })
                { var piece = locations.Equipped(loadout, classId, hand); if (piece == null) continue; foreach (var failure in locations.RequirementReceipt(piece, attributes)["failures"]) requirements.Add(new JObject { ["itemId"] = piece["id"].DeepClone(), ["attribute"] = failure["attributeId"].DeepClone(), ["required"] = failure["required"].DeepClone(), ["actual"] = failure["actual"].DeepClone() }); }
            _ = new WeaponCardComposer(_catalog).BuildAttackPlan(loadout, classId);
            foreach (var hand in new[] { "rightHand", "leftHand" }) kit[hand] = selected[hand].DeepClone(); var snapshot = KitSnapshot(kit); if (requested != null) snapshot["customized"] = true;
            return new JObject { ["startingKitId"] = kit["id"].DeepClone(), ["startingKitSnapshot"] = snapshot, ["loadout"] = loadout, ["relicId"] = relicId, ["requirements"] = requirements, ["canBegin"] = requirements.Count == 0 };
        }
        public void ValidateSaved(JObject run, JObject meta = null, bool legacy = false)
        {
            var classId = (string)run["classId"] ?? (string)run["class"];
            if (legacy && run["startingKitId"] == null && run["startingKitSnapshot"] == null)
            { var kit = ResolveKit(classId, null, null); run["startingKitId"] = kit["id"].DeepClone(); run["startingKitSnapshot"] = KitSnapshot(kit); }
            var id = OptionalId(run["startingKitId"]); var snapshot = run["startingKitSnapshot"] as JObject;
            if (id == null || snapshot == null || (string)snapshot["id"] != id || (string)snapshot["classId"] != classId) throw new ArgumentException("Starting kit identity is missing or inconsistent.");
            var grant = run["loadout"]?["creationArmourGrant"];
            if (grant != null && grant.Type != JTokenType.Null) { if ((string)grant["classId"] != classId) throw new ArgumentException("Starting armour grant belongs to another class."); _ = _catalog.Record("equipment.armour", (string)grant["id"], classId); }
            if ((bool?)snapshot["customized"] == true)
            {
                var left = OptionalId(snapshot["leftHand"]); var right = OptionalId(snapshot["rightHand"]); if (left != null && left == right) throw new ArgumentException("Saved starting hands duplicate an armament.");
                var locations = new WeaponLoadout(_catalog);
                foreach (var hand in new[] { "leftHand", "rightHand" }) { var item = OptionalId(snapshot[hand]); if (item != null && !WeaponLoadout.Fits(_catalog.Record("equipment.slots", hand), _catalog.Record("equipment.armaments", item))) throw new ArgumentException("Saved starting armament does not fit its hand."); }
                return;
            }
            var expected = KitSnapshot(ResolveKit(classId, id, meta ?? run["profileMeta"] as JObject));
            if (!JToken.DeepEquals(JObject.Parse(expected.ToString()), JObject.Parse(snapshot.ToString()))) throw new ArgumentException("Saved starting kit differs from its authored identity.");
        }
    }
}



