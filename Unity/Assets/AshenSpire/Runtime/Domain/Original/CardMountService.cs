// CardMountService.cs — the smith extracts and installs cards without editing assets.
// Author balance.equipment.cardMounts, equipmentGrants, weaponCardPackage and tags.
// itemMounts records each item's edits; deterministic mount IDs survive replacement.
// Extract/install return a new run plus a durable receipt, so failed validation never
// spends stones or removes cards. These are out-of-combat services; the host chooses
// where the authored smith service is offered before calling them.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class CardMountService
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _data;
        public CardMountService(OriginalContentCatalog catalog) { _catalog = catalog; _data = catalog.Data(); }
        private JToken Rules => _data["balance"]["equipment"]["cardMounts"];
        public static string Owner(JToken instance)
        { var owner = (string)instance["grantedBy"]; return string.IsNullOrEmpty(owner) || owner.StartsWith("unarmed:", StringComparison.Ordinal) ? null : owner.Contains("/") ? owner : "armament/" + owner; }
        public string Fallback(string itemRef, string kind)
        {
            var item = Rules?["fallbackByItem"]?[itemRef] as JObject;
            var fallback = item?.Property(kind) != null ? item[kind] : Rules?["kinds"]?[kind]?["fallback"];
            if (fallback == null || fallback.Type == JTokenType.Null) return null;
            var id = (string)fallback["cardId"];
            if (id == null)
            {
                var profile = (string)_data["balance"]["equipment"]["unarmedProfiles"]?[(string)fallback["unarmedProfile"]];
                if (profile == null) return null; id = (string)_catalog.Record("equipment.basicCardProfiles", profile)["baseCardId"];
            }
            _catalog.Record("cards", id); return id;
        }
        public JArray ApplyOverrides(JArray desired, JObject itemMounts)
        {
            var result = new JArray();
            foreach (var original in desired.OfType<JObject>())
            {
                var card = (JObject)original.DeepClone(); var owner = Owner(card);
                var entry = owner == null ? null : itemMounts?[owner]?[(string)card["instanceId"]] as JObject;
                if (entry == null) { result.Add(card); continue; }
                var id = (string)entry["card"];
                if (!string.IsNullOrEmpty(id)) { _catalog.Record("cards", id); card["cardId"] = id; card["upgraded"] = (bool?)entry["upgraded"] == true; result.Add(card); }
                else { id = Fallback(owner, (string)card["equipmentRole"]); if (id != null) { card["cardId"] = id; card["upgraded"] = false; result.Add(card); } }
            }
            return result;
        }
        public JArray ExtraInstances(string itemRef, JObject itemMounts, string grantSource)
        {
            var result = new JArray();
            foreach (var entry in (itemMounts?[itemRef] as JObject ?? new JObject()).Properties())
            {
                if (!entry.Name.StartsWith("mount:", StringComparison.Ordinal) || string.IsNullOrEmpty((string)entry.Value["card"])) continue;
                if (!entry.Name.StartsWith("mount:" + itemRef + ":", StringComparison.Ordinal)) throw new ArgumentException("Mount key belongs to another item.");
                _catalog.Record("cards", (string)entry.Value["card"]);
                result.Add(new JObject { ["instanceId"] = entry.Name, ["cardId"] = entry.Value["card"].DeepClone(), ["upgraded"] = (bool?)entry.Value["upgraded"] == true, ["equipmentRole"] = (string)Rules["extraMounts"]["kind"], ["grantedBy"] = itemRef, ["grantSource"] = grantSource });
            }
            return result;
        }
        public JArray AuthoredMounts(JObject piece)
        {
            var output = new JArray(); var owner = WeaponLoadout.ItemRef(piece); var armour = (string)piece["kind"] == "armor";
            var source = (string)_data["balance"]["equipment"]["startingDeck"]["sources"][armour ? "armor" : "weapon"];
            void Mint(string key, string card, string role, string by) => output.Add(new JObject { ["instanceId"] = key, ["cardId"] = card, ["upgraded"] = false, ["equipmentRole"] = role, ["grantedBy"] = by, ["grantSource"] = source });
            if (_catalog.Tags(armour ? "armour" : "armament", piece).Contains("bound"))
            {
                var family = armour ? "armour" : "armament"; var scope = armour ? (string)piece["classId"] : "";
                var row = _catalog.Table("equipment.equipmentGrants").FirstOrDefault(x => (string)x["sourceId"] == (string)piece["id"] && (string.IsNullOrEmpty((string)x["family"]) || (string)x["family"] == family) && ((string)x["scope"] ?? "") == scope);
                var copies = new Dictionary<string, int>();
                foreach (var card in row?["cards"] ?? new JArray()) { var id = (string)card; copies.TryGetValue(id, out var n); copies[id] = n + 1; Mint("bound:" + owner + ":" + id + ":" + n, id, "granted", owner); }
            }
            var package = armour ? null : new WeaponCardComposer(_catalog).Package(piece);
            if (package != null)
            {
                foreach (var grant in package["grantedCards"]) for (var n = 0; n < (int)grant["count"]; n++) Mint("granted:" + (string)piece["id"] + ":" + (string)grant["cardId"] + ":" + n, (string)grant["cardId"], "granted", (string)piece["id"]);
                foreach (var art in package["weaponArtDefaults"]) Mint("weaponArt:" + (string)piece["id"] + ":" + (string)art, (string)art, "weaponArt", (string)piece["id"]);
            }
            return output;
        }
        public JArray MountRows(string itemRef, JObject itemMounts)
        {
            var piece = new ItemUpgradeService(_catalog).Definition(itemRef);
            if (itemRef.StartsWith("relic/", StringComparison.Ordinal)) throw new ArgumentException("Relics do not have card mounts.");
            var authored = AuthoredMounts(piece); var live = ApplyOverrides(authored, itemMounts); var result = new JArray();
            var entries = itemMounts?[itemRef] as JObject ?? new JObject();
            void Row(string key, string kind, string state, string authoredId, string cardId, bool upgraded, int extractions, bool extra)
            {
                var definition = cardId == null ? null : _catalog.Record("cards", cardId);
                var extractable = definition != null && (state == "authored" || state == "installed") && _catalog.Tags("card", definition).Contains((string)Rules["extractableTag"]);
                result.Add(new JObject { ["mountKey"] = key, ["kind"] = kind, ["state"] = state, ["authoredCardId"] = authoredId, ["cardId"] = cardId, ["cardName"] = definition?["name"]?.DeepClone(), ["upgraded"] = upgraded, ["extractable"] = extractable, ["fallbackCardId"] = extra ? null : Fallback(itemRef, kind), ["accepts"] = Rules["kinds"]?[kind]?["accepts"]?.DeepClone() ?? new JArray(), ["extractions"] = extractions, ["extra"] = extra });
            }
            foreach (var card in authored)
            {
                var key = (string)card["instanceId"]; var entry = entries[key] as JObject; var current = live.FirstOrDefault(x => (string)x["instanceId"] == key);
                var state = entry == null ? "authored" : !string.IsNullOrEmpty((string)entry["card"]) ? "installed" : current == null ? "empty" : "fallback";
                Row(key, (string)card["equipmentRole"], state, (string)card["cardId"], (string)current?["cardId"], (bool?)current?["upgraded"] == true, (int?)entry?["extractions"] ?? 0, false);
            }
            foreach (var entry in entries.Properties().Where(x => x.Name.StartsWith("mount:", StringComparison.Ordinal) && !string.IsNullOrEmpty((string)x.Value["card"]))) Row(entry.Name, (string)Rules["extraMounts"]["kind"], "installed", null, (string)entry.Value["card"], (bool?)entry.Value["upgraded"] == true, (int?)entry.Value["extractions"] ?? 0, true);
            if ((bool?)Rules["extraMounts"]["enabled"] == true)
            {
                var cap = (int)Rules["extraMounts"]["perItem"];
                var used = entries.Properties().Count(x => x.Name.StartsWith("mount:", StringComparison.Ordinal) && !string.IsNullOrEmpty((string)x.Value["card"]));
                if (used < cap) for (var n = 0; n < cap; n++) { var key = "mount:" + itemRef + ":" + n; if (string.IsNullOrEmpty((string)entries[key]?["card"])) { Row(key, (string)Rules["extraMounts"]["kind"], "open", null, null, false, 0, true); break; } }
            }
            return result;
        }
        public int Cost(string service) => (int)_data["balance"]["smithing"]["services"][service]["cost"];
        private void Owned(JObject run, string itemRef)
        { if (!new ItemUpgradeService(_catalog).OwnedRefs(run).Contains(itemRef) || itemRef.StartsWith("relic/", StringComparison.Ordinal)) throw new ArgumentException("Item is not carried or worn."); }
        public JObject Extract(JObject run, string itemRef, string mountKey, bool free = false)
        {
            Owned(run, itemRef);
            var mount = MountRows(itemRef, run["itemMounts"] as JObject).FirstOrDefault(x => (string)x["mountKey"] == mountKey && (bool)x["extractable"]) ?? throw new ArgumentException("Mount is not extractable.");
            var cost = Cost("extract"); var stones = ItemUpgradeService.Stones(run); if (!free && stones < cost) throw new ArgumentException("Insufficient Smithing Stones.");
            var next = (JObject)run.DeepClone(); var transaction = checked(((int?)run["mountTransactions"] ?? 0) + 1);
            var key = "extracted:" + transaction + ":" + (string)mount["cardId"];
            if ((next["deck"] as JArray ?? new JArray()).Any(x => (string)x["instanceId"] == key)) throw new ArgumentException("Duplicate extracted card instance.");
            WriteMount(next, itemRef, mountKey, (bool)mount["extra"] ? null : new JObject { ["card"] = null, ["extractions"] = (int)mount["extractions"] + 1 });
            ((JArray)next["deck"]).Add(new JObject { ["instanceId"] = key, ["cardId"] = mount["cardId"].DeepClone(), ["upgraded"] = (bool)mount["upgraded"] });
            return Finish(next, "extract", itemRef, mount, key, transaction, stones, cost, free);
        }
        public JObject Install(JObject run, string itemRef, string mountKey, string instanceId, bool free = false)
        {
            Owned(run, itemRef);
            var mount = MountRows(itemRef, run["itemMounts"] as JObject).FirstOrDefault(x => (string)x["mountKey"] == mountKey && new[] { "fallback", "empty", "open" }.Contains((string)x["state"])) ?? throw new ArgumentException("Mount is not open.");
            var card = (run["deck"] as JArray ?? new JArray()).OfType<JObject>().FirstOrDefault(x => (string)x["instanceId"] == instanceId && string.IsNullOrEmpty((string)x["equipmentRole"])) ?? throw new ArgumentException("Card is not run-owned.");
            var definition = _catalog.Record("cards", (string)card["cardId"]);
            if (!_catalog.Tags("card", definition).Intersect(mount["accepts"].Select(x => (string)x)).Any()) throw new ArgumentException("Card tags do not fit this mount.");
            var cost = Cost("install"); var stones = ItemUpgradeService.Stones(run); if (!free && stones < cost) throw new ArgumentException("Insufficient Smithing Stones.");
            var next = (JObject)run.DeepClone(); var transaction = checked(((int?)run["mountTransactions"] ?? 0) + 1);
            ((JArray)next["deck"]).First(x => (string)x["instanceId"] == instanceId).Remove();
            WriteMount(next, itemRef, mountKey, new JObject { ["card"] = card["cardId"].DeepClone(), ["upgraded"] = (bool?)card["upgraded"] == true, ["extractions"] = mount["extractions"].DeepClone() });
            var installed = (JObject)mount.DeepClone(); installed["cardId"] = card["cardId"].DeepClone();
            return Finish(next, "install", itemRef, installed, instanceId, transaction, stones, cost, free);
        }
        private static void WriteMount(JObject run, string itemRef, string key, JObject entry)
        {
            if (!(run["itemMounts"] is JObject)) run["itemMounts"] = new JObject();
            var map = (JObject)run["itemMounts"]; if (!(map[itemRef] is JObject)) map[itemRef] = new JObject();
            if (entry == null) ((JObject)map[itemRef]).Remove(key); else map[itemRef][key] = entry;
            if (!((JObject)map[itemRef]).Properties().Any()) map.Remove(itemRef);
        }
        private JObject Finish(JObject run, string service, string itemRef, JToken mount, string instanceId, int transaction, int before, int cost, bool free)
        {
            run["smithingStones"] = before - (free ? 0 : cost); run["mountTransactions"] = transaction;
            run["deck"] = new WeaponCardComposer(_catalog).Recompose((JArray)run["deck"], (JObject)run["loadout"], ((string)run["class"] ?? (string)run["classId"]), run["itemMounts"] as JObject);
            new ItemUpgradeService(_catalog).RestampCards(run);
            var receipt = new JObject { ["schemaVersion"] = 1, ["service"] = service, ["itemRef"] = itemRef, ["mountKey"] = mount["mountKey"].DeepClone(), ["kind"] = mount["kind"].DeepClone(), ["cardId"] = mount["cardId"].DeepClone(), ["instanceId"] = instanceId, ["authoredCost"] = cost, ["spent"] = free ? 0 : cost, ["stoneBalanceBefore"] = before, ["stoneBalanceAfter"] = run["smithingStones"].DeepClone(), ["free"] = free, ["transaction"] = transaction };
            run["lastMountReceipt"] = receipt.DeepClone(); return new JObject { ["run"] = run, ["receipt"] = receipt };
        }
    }
}
