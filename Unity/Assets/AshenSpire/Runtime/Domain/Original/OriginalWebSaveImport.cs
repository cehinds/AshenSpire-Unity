// OriginalWebSaveImport.cs — explicit, read-only conversion of original-game map saves.
// Never writes storage. The caller previews the result and commits to an empty slot.
// Refuse unsupported active rooms instead of replaying a fight or granting its rewards.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public static class OriginalWebSaveImport
    {
        public const int MaximumBytes = 1024 * 1024;
        private static JToken Canonical(JToken value) => value is JObject obj
            ? new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value))))
            : value is JArray array ? new JArray(array.Select(Canonical)) : value.DeepClone();
        internal static string Hash(JObject source)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(source).ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
        }
        internal static JObject ParseOriginal(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > MaximumBytes)
                throw new ArgumentException("Choose original save JSON no larger than 1 MB.");
            using (var reader = new JsonTextReader(new StringReader(value)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
            {
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ArgumentException("Unexpected data after the save.");
                return result;
            }
        }
        public static void ValidateReceipt(JObject run)
        {
            if (run["webImport"] == null) return;
            if (!(run["webImport"] is JObject receipt) || (int?)receipt["version"] != 1 || !(receipt["original"] is JObject original))
                throw new ArgumentException("Unsupported original-save import receipt.");
            var hash = Hash(original);
            if ((string)receipt["sha256"] != hash || (string)run["runId"] != "web-import-" + hash || (string)run["classId"] != (string)original["class"])
                throw new ArgumentException("The original-save import receipt does not match this run.");
        }
        public static JObject Convert(string text, OriginalContentCatalog catalog, JObject supplement, JObject mechanics, JObject progression)
        {
            if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaximumBytes)
                throw new ArgumentException("Choose an original save JSON file no larger than 1 MB.");
            JObject Parse(string value)
            {
                using (var reader = new JsonTextReader(new StringReader(value)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
                {
                    var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new ArgumentException("Unexpected data after the save.");
                    return result;
                }
            }
            var source = Parse(text);
            if (source["archive"] is JObject archive)
            {
                if ((string)archive["kind"] != "run" || archive["save"]?.Type != JTokenType.String)
                    throw new ArgumentException("Choose a run archive, not a profile archive.");
                source = Parse((string)archive["save"]);
            }
            if (source["profile"] != null) throw new ArgumentException("This is a profile export. Run import needs an original run save.");
            if (source["schemaVersion"]?.Type != JTokenType.Integer || (int)source["schemaVersion"] != 5)
                throw new ArgumentException("This initial importer supports original run schema 5. Earlier and newer original-game save formats are not supported yet. Your original save is unchanged.");
            foreach (var name in new[] { "combatEntered", "pendingReward", "shopStock", "draft", "skillDraft", "skills", "classAbilities", "handRuleSnapshot", "handRulesSnapshot", "handRules" })
                if (source[name] != null && source[name].Type != JTokenType.Null)
                    throw new ArgumentException("This save contains " + name + ". Finish the active room in the original game and save on the map before importing.");
            if (source["webImport"] != null || source["playerProjectionRules"] != null || source["phase"] != null)
                throw new ArgumentException("Choose an original-game save, not an AshenedSpire snapshot.");
            var knownFields = ("schemaVersion contentVersion seed streamCounters class startingKitId startingKitSnapshot attributeMode attributeModeSnapshot attributes levelUps levelPoints floor actNumber mapNodeId hp maxHp maxHpAdjustment equipmentPoolBonuses equipmentPoolDeficits cinders smithingStones itemUpgradeLevels smithingRewardClaims deck loadout equipmentAttackSlotCount relics damageBySchoolAdd flasks flaskCharges seedString mapGraph combatEntered history modifiers equipmentProfileRuleSnapshot derivedStatRuleSnapshot maxMana maxStamina energyMax drawPerTurn mana stamina path custom customization keepsakeId profileMeta lastEncounters bossesBeaten stats itemMounts lastMountReceipt mountTransactions lastSmithingReceipt mapView").Split(' ');
            var inactiveFields = new[] { "pendingReward", "shopStock", "draft", "skillDraft", "skills", "classAbilities", "handRuleSnapshot", "handRulesSnapshot", "handRules" };
            var unknown = source.Properties().FirstOrDefault(p => !knownFields.Contains(p.Name) && p.Name != "seenEvents" && !(inactiveFields.Contains(p.Name) && p.Value.Type == JTokenType.Null));
            if (unknown != null) throw new ArgumentException("This save contains unsupported original-game state: " + unknown.Name + ". Your original save is unchanged.");
            if (!(source["modifiers"] is JArray modifiers) || modifiers.Count != 0)
                throw new ArgumentException("This save has original run modifiers that cannot yet be imported faithfully.");
            if (source["hp"]?.Type != JTokenType.Integer || (int)source["hp"] <= 0)
                throw new ArgumentException("Import requires a living character at a map checkpoint. Finished or damaged runs are not imported as new climbs.");
            foreach (var key in new[] { "attributes", "loadout", "streamCounters", "mapGraph", "flaskCharges", "derivedStatRuleSnapshot", "equipmentProfileRuleSnapshot" })
                if (!(source[key] is JObject)) throw new ArgumentException("Original save is missing " + key + ".");
            foreach (var key in new[] { "deck", "path", "history", "relics", "flasks" })
                if (!(source[key] is JArray)) throw new ArgumentException("Original save is missing " + key + ".");
            var data = catalog.Data();
            var classId = (string)source["class"];
            _ = catalog.Record("classes", classId);
            foreach (var card in (JArray)source["deck"])
            {
                if (!(card is JObject) || string.IsNullOrWhiteSpace((string)card["instanceId"])) throw new ArgumentException("Every imported card needs an instance ID.");
                _ = catalog.Record("cards", (string)card["cardId"]);
            }
            if (source["deck"].Select(c => (string)c["instanceId"]).Distinct().Count() != source["deck"].Count()) throw new ArgumentException("Duplicate card instance IDs.");
            foreach (var id in source["relics"].Values<string>()) _ = catalog.Record("relics", id);
            foreach (var flask in source["flasks"]) _ = catalog.Record("flasks", (string)flask["flaskId"]);
            if (source["seenEvents"] != null)
            {
                if (!(source["seenEvents"] is JArray seenEvents)) throw new ArgumentException("Original event history must be an array.");
                foreach (var id in seenEvents) _ = catalog.Record("events", (string)id);
            }
            var loadout = (JObject)source["loadout"];
            if (loadout.Properties().Any(p => !new[] { "sets", "active", "storage", "creationArmourGrant" }.Contains(p.Name)) || !(loadout["sets"] is JObject sets) || !(loadout["active"] is JObject active) || !(loadout["storage"] is JArray storage))
                throw new ArgumentException("Unsupported original equipment layout.");
            var slotIds = catalog.Table("equipment.slots").Select(s => (string)s["id"]).ToArray();
            if (sets.Properties().Any(p => !slotIds.Contains(p.Name)) || active.Properties().Any(p => !slotIds.Contains(p.Name))) throw new ArgumentException("Unknown equipment slot.");
            foreach (var slot in catalog.Table("equipment.slots"))
            {
                var id = (string)slot["id"];
                if (!(sets[id] is JArray items) || items.Count != Math.Max(1, (int)slot["sets"]) || active[id]?.Type != JTokenType.Integer || (int)active[id] < 0 || (int)active[id] >= items.Count) throw new ArgumentException("Malformed equipment slot: " + id);
                foreach (var item in items.Where(x => x.Type != JTokenType.Null))
                {
                    if (item.Type != JTokenType.String) throw new ArgumentException("Malformed equipment reference.");
                    var armour = slot["kinds"].Any(x => (string)x == "armor");
                    _ = catalog.Record(armour ? "equipment.armour" : "equipment.armaments", (string)item, armour ? classId : null);
                }
            }
            foreach (var item in storage) _ = catalog.Record("equipment.armaments", (string)item);
            var equipment = new EquipmentRunModifiers(catalog);
            _ = equipment.Resolve((JObject)source["loadout"], classId);
            var profiles = source["equipmentProfileRuleSnapshot"];
            if ((int?)profiles["snapshotVersion"] != 1 || !(profiles["profiles"] is JObject frozenProfiles)) throw new ArgumentException("Unsupported equipment rule snapshot.");
            if (!(profiles["rarityBonuses"] is JObject rarityBonuses)) throw new ArgumentException("Missing saved equipment rarity rules.");
            data["balance"]["equipment"]["rarityBonuses"] = rarityBonuses.DeepClone();
            foreach (var saved in frozenProfiles.Properties())
            {
                var row = ((JArray)data["equipment"]["basicCardProfiles"]).OfType<JObject>().SingleOrDefault(r => (string)r["id"] == saved.Name)
                    ?? throw new ArgumentException("Unknown saved equipment profile: " + saved.Name);
                if (!(saved.Value is JObject value)) throw new ArgumentException("Malformed equipment profile: " + saved.Name);
                if ((string)value["compatibility"] != (string)row["compatibility"]) throw new ArgumentException("Unsupported saved equipment compatibility: " + saved.Name);
                foreach (var key in new[] { "baseValue", "scalingStat", "pointsPerTier", "rounding", "gainPerTier", "cap", "damageSchool", "exposureBuildupPerHit" })
                    if (value[key] != null) row[key] = value[key].DeepClone();
            }
            var stats = source["derivedStatRuleSnapshot"];
            if ((int?)stats["snapshotVersion"] != 2 || !(stats["rules"]?["rules"] is JObject folded)) throw new ArgumentException("Unsupported derived-stat rule snapshot.");
            var baseRules = (JObject)folded.DeepClone();
            // Original snapshots fold relic flats into the base. The native projection
            // applies those relics independently, so unfold them once to avoid healing.
            foreach (var modifier in stats["relicModifiers"]?["sources"] as JArray ?? new JArray())
            {
                if ((string)modifier["tag"] == "damage.school.flat") continue;
                var resource = (string)modifier["resource"];
                if (string.IsNullOrEmpty(resource) || !(baseRules[resource] is JObject rule))
                    throw new ArgumentException("Malformed saved relic resource rule.");
                if ((string)modifier["tag"] == "resource.flat")
                {
                    if (rule["base"]?.Type != JTokenType.Integer || modifier["value"]?.Type != JTokenType.Integer) throw new ArgumentException("Malformed saved relic flat rule.");
                    rule["base"] = checked((int)rule["base"] - (int)modifier["value"]);
                }
                else if ((string)modifier["tag"] == "resource.attributeTier")
                {
                    if ((string)modifier["sourceStat"] != (string)rule["sourceStat"] || (double?)modifier["pointsPerTier"] != (double?)rule["pointsPerTier"] || (string)rule["rounding"] != "floor") throw new ArgumentException("Incompatible saved relic tier rule.");
                    rule["gainPerTier"] = (double)rule["gainPerTier"] - (double)modifier["amountPerTier"];
                }
                else throw new ArgumentException("This save uses a relic rule that cannot yet be imported faithfully.");
            }
            // Missing hand snapshots belong to the original legacy draw model, not
            // the Unity lean opening-hand defaults introduced after those saves.
            data.Remove("handRules");
            var frozenCatalog = new OriginalContentCatalog(data.ToString());
            var run = (JObject)source.DeepClone();
            run["classId"] = classId; run["id"] = "player"; run["kind"] = "player";
            run["alive"] = true; run["statuses"] = new JObject(); run["block"] = 0;
            run["phase"] = "Map"; run["room"] = new JObject();
            run["progression"] = progression.DeepClone();
            run["playerProjectionRules"] = new JObject { ["snapshotVersion"] = 1, ["baseRules"] = baseRules };
            run["profileMeta"] = source["profileMeta"]?.DeepClone() ?? new JObject();
            run["lastEncounters"] = source["lastEncounters"]?.DeepClone() ?? new JArray();
            run["bossesBeaten"] = source["bossesBeaten"]?.DeepClone() ?? new JArray();
            run["stats"] = source["stats"]?.DeepClone() ?? new JObject { ["fightsWon"] = 0, ["damageDealt"] = 0, ["damageTaken"] = 0 };
            run["fightsWon"] = run["stats"]["fightsWon"]?.DeepClone() ?? new JValue(0);
            run["custom"] = OriginalCustomRunRules.Normalize(source["custom"] as JObject);
            // Wrapper timestamps and JSON spacing must not make the same checkpoint
            // importable twice. Hash the parsed run, independently of its export wrapper.
            var hash = Hash(source);
            run["runId"] = "web-import-" + hash;
            run["webImport"] = new JObject { ["version"] = 1, ["sha256"] = hash, ["original"] = source.DeepClone() };
            new OriginalPlayerProjection(frozenCatalog, mechanics).Reconcile(run);
            foreach (var resource in new[] { "hp", "maxHp", "mana", "maxMana", "stamina", "maxStamina", "energyMax", "drawPerTurn" })
                if (!JToken.DeepEquals(run[resource], source[resource])) throw new ArgumentException("The saved " + resource + " does not match supported original rules; no save was changed.");
            if (!JToken.DeepEquals(run["damageBySchoolAdd"], source["damageBySchoolAdd"])) throw new ArgumentException("Saved relic damage modifiers differ from supported content.");
            var frozenSupplement = (JObject)supplement.DeepClone(); frozenSupplement["mechanics"] = mechanics.DeepClone();
            var snapshot = new JObject { ["schemaVersion"] = 1, ["content"] = data, ["supplement"] = frozenSupplement, ["run"] = run };
            var restored = OriginalGameSession.Restore(snapshot);
            if (restored.LegalNodeIds.Length == 0) throw new ArgumentException("This checkpoint has no continuing route. Completed or damaged maps cannot be imported as active climbs.");
            foreach (var card in restored.RunPlayer["deck"].OfType<JObject>()) _ = restored.Resolve(card);
            var projection = new WeaponCardProjection(frozenCatalog);
            foreach (var card in source["deck"].OfType<JObject>())
            {
                var projected = projection.Resolve(card, loadout, classId, (JObject)source["attributes"]);
                if (!JToken.DeepEquals(card["mods"] ?? new JArray(), projected["modifiers"])) throw new ArgumentException("Saved card modifiers are not supported faithfully: " + (string)card["cardId"]);
            }
            return restored.Snapshot();
        }
    }
}
