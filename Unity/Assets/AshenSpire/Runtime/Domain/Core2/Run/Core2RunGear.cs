// Core2RunGear.cs — what a Core 2 run's worn gear gives it: resolved gear stats, effective attributes, ratings, pool
// bonuses, the cards worn gear lends, the pools re-derived after a change, and the fight's mounted property rules.
//
// JS at the pin:
//   src/model/loadout.js          equippedIn / equippedPieces (slot order), runMods (run-scoped mods; resolved Core gear
//                                 replaces pool mods), moveEquipmentPool, reconcileRunLoadoutHp (max-HP home 3 of 3),
//                                 configuredGearGrantInstances / reconcileConfiguredGearGrants (gear[].grantedCards)
//   src/model/gameProfileGear.js  gameProfileEquipmentReceipt (resolveGearItem at rank + Smith tier), gameProfileEffectiveAttributes
//   src/model/gameProfileRun.js   gameProfileRatingReceipt (attribute rows + gear sources + weapon tiers as AR)
//   src/model/levelup.js          rederivePools
//   src/engine/properties.js      loadoutCarriers / relicCarrier / classCarrier (the mounts a fight starts with)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One worn piece: the slot it is in and its legacy row (armament or class armour).</summary>
    public sealed class Core2WornPiece
    {
        public string SlotId { get; set; }
        public JObject Row { get; set; }
        public bool Armor => (string)Row["kind"] == "armor";
        public string Id => (string)Row["id"];
        /// <summary>JS pieceItemRef: armament/&lt;id&gt; or armor/&lt;class&gt;/&lt;id&gt;.</summary>
        public string ItemRef => Armor ? "armor/" + (string)Row["classId"] + "/" + Id : "armament/" + Id;
    }

    public sealed class Core2GearReceipt
    {
        public Dictionary<string, int> Totals { get; } = Core2EnemyStats.GearKeys.ToDictionary(k => k, k => 0, StringComparer.Ordinal);
        public List<(string SourceId, string Slot, string ItemId, Dictionary<string, int> Values)> Sources { get; } = new List<(string, string, string, Dictionary<string, int>)>();
    }

    public sealed class Core2RatingReceipt
    {
        public Dictionary<string, int> Totals { get; set; }
        public int BaseEvasionBonus { get; set; }
        public int BasePrecisionBonus { get; set; }
        /// <summary>JS receipt.sources: the attribute row, each worn piece and the upgrade-tier row (the fight's ratingSources).</summary>
        public List<Core2RatingSource> Sources { get; set; } = new List<Core2RatingSource>();
    }

    public static class Core2RunGear
    {
        public static readonly IReadOnlyList<string> RatingIds = new[] { "ar", "dr", "pr", "ward", "poise" };
        private static readonly Regex Mod = new Regex(@"^([A-Za-z]\w*)\.([A-Za-z]\w*)=([+-]?)(\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant);

        public static IEnumerable<JObject> Slots(Core2Content content) => ((JArray)content.Table("adapter/equipment.json")["slots"]).OfType<JObject>();

        /// <summary>JS equippedIn: the item active in a slot (class armour for the armour slot, an armament otherwise).</summary>
        public static JObject EquippedIn(Core2RunData data, Core2RunLoadout loadout, string classId, string slotId)
        {
            var id = loadout.Equipped(slotId);
            if (id == null) return null;
            var slot = Slots(data.Content).FirstOrDefault(s => (string)s["id"] == slotId);
            if (slot != null && Core2Json.Strings(slot["kinds"]).Contains("armor")) return data.Armour(classId, id);
            return data.Armament(id);
        }

        /// <summary>JS equippedPieces: every worn piece, in slot order.</summary>
        public static List<Core2WornPiece> Worn(Core2RunData data, Core2RunLoadout loadout, string classId)
        {
            var output = new List<Core2WornPiece>();
            if (loadout == null) return output;
            foreach (var slot in Slots(data.Content))
            {
                var piece = EquippedIn(data, loadout, classId, (string)slot["id"]);
                if (piece != null) output.Add(new Core2WornPiece { SlotId = (string)slot["id"], Row = piece });
            }
            return output;
        }

        /// <summary>JS slotHand: the hand a slot is held in (right, left) or null.</summary>
        public static string SlotHand(JObject slot) => (string)slot?["hand"] is string h && (h == "right" || h == "left") ? h : null;

        /// <summary>The Core gear row a worn id resolves to (JS: source id or id, armour or not, the class allowed).</summary>
        public static JObject GearItem(Core2Content content, string id, bool armor, string classId) => content.Gear.Select(g => g.Raw).FirstOrDefault(item =>
            ((string)item["source"]?["id"] ?? (string)item["id"]) == id && ((string)item["kind"] == "armor") == armor
            && (!(item["classes"] is JArray classes) || classId == null || classes.Any(c => (string)c == classId)));

        /// <summary>JS gameProfileEquipmentReceipt: each worn Core gear piece resolved at its rank plus its Smith tier.</summary>
        public static Core2GearReceipt EquipmentReceipt(Core2RunData data, Core2Run run) => EquipmentReceipt(data, run.Loadout, run.Class, run.ItemUpgradeLevels);

        public static Core2GearReceipt EquipmentReceipt(Core2RunData data, Core2RunLoadout loadout, string classId, IReadOnlyDictionary<string, int> levels)
        {
            var receipt = new Core2GearReceipt();
            var settings = data.Module("gear");
            if ((string)settings?["mode"] != "configured" || settings["armorBudget"] == null || loadout == null) return receipt;
            foreach (var pair in loadout.Sets)
            {
                var id = loadout.Equipped(pair.Key);
                if (id == null) continue;
                var armor = pair.Key == "armor";
                var item = GearItem(data.Content, id, armor, classId);
                if (item == null) continue;
                var sourceId = armor ? "armor/" + classId + "/" + id : "armament/" + id;
                var tier = levels != null && levels.TryGetValue(sourceId, out var t) ? t : 0;
                var resolved = Core2EnemyStats.ResolveGearItem(item, settings, ((int?)item["rank"] ?? 1) + tier);
                foreach (var key in Core2EnemyStats.GearKeys) receipt.Totals[key] += resolved.Values.TryGetValue(key, out var v) ? v : 0;
                receipt.Sources.Add((sourceId, pair.Key, (string)item["id"], resolved.Values));
            }
            return receipt;
        }

        /// <summary>JS gameProfileEffectiveAttributes: the allocated attributes plus the worn gear's attribute values.</summary>
        public static Dictionary<string, int> EffectiveAttributes(Core2RunData data, Core2Run run)
        {
            var gear = EquipmentReceipt(data, run);
            return run.Attributes.ToDictionary(p => p.Key, p => p.Value + (gear.Totals.TryGetValue(p.Key, out var v) ? v : 0), StringComparer.Ordinal);
        }

        /// <summary>JS gameProfileRatingReceipt: AR/DR/PR/Ward/Poise from the attribute rows, the gear, and weapon tiers as AR.</summary>
        public static Core2RatingReceipt Ratings(Core2RunData data, Core2Run run)
        {
            var totals = RatingIds.ToDictionary(id => id, id => 0, StringComparer.Ordinal);
            var gear = EquipmentReceipt(data, run);
            var attributes = EffectiveAttributes(data, run);
            var stats = (string)data.Module("stats")?["mode"] == "configured";
            if (stats) foreach (var id in RatingIds) totals[id] = Core2DerivedStats.Derive(run.DerivedStatRules, id, attributes, run.Level.Level, run.Class);
            int Value(IReadOnlyDictionary<string, int> values, string id) => values.TryGetValue(id, out var v) ? v : 0;
            var sources = new List<Core2RatingSource> { new Core2RatingSource { Name = "Attributes", Kind = "attribute", Ar = totals["ar"], Pr = totals["pr"] } };
            foreach (var source in gear.Sources)
            {
                foreach (var id in RatingIds) totals[id] += Value(source.Values, id);
                var item = GearItem(data.Content, source.ItemId, source.Slot == "armor", run.Class);
                sources.Add(new Core2RatingSource { Name = ((string)item?["name"] ?? source.ItemId).Replace('_', ' '), Kind = "equipment", SourceId = source.SourceId, Slot = source.Slot,
                    Ar = Value(source.Values, "ar"), Pr = Value(source.Values, "pr") });
            }
            var policy = data.Module("equipment")?["upgradeBonuses"] as JObject;
            var armorBudget = data.Module("gear")?["armorBudget"] != null;
            if (stats && policy != null && run.Loadout != null)
            {
                int Tier(string itemRef) => run.ItemUpgradeLevels.TryGetValue(itemRef, out var t) && t > 0 ? t : 0;
                int weaponTier = 0, armorTier = 0;
                foreach (var pair in run.Loadout.Sets)
                {
                    var id = run.Loadout.Equipped(pair.Key);
                    if (id == null) continue;
                    if (pair.Key == "armor") { armorTier = Math.Max(armorTier, Tier("armor/" + run.Class + "/" + id)); continue; }
                    var piece = data.Armament(id);
                    if (piece == null) continue;
                    if ((string)piece["kind"] == "shield") { armorTier = Math.Max(armorTier, Tier("armament/" + id)); continue; }
                    var itemTier = Tier("armament/" + id);
                    weaponTier = Math.Max(weaponTier, itemTier);
                    if (armorBudget && itemTier > 0)
                    {
                        var ar = itemTier * (int)policy["weaponARPerTier"];
                        totals["ar"] += ar;
                        sources.Add(new Core2RatingSource { Name = (string)piece["name"] ?? id, Kind = "equipment", SourceId = "armament/" + id, Slot = pair.Key, Ar = ar });
                    }
                }
                if (!armorBudget)
                {
                    int ar = weaponTier * (int)policy["weaponARPerTier"], dr = armorTier * (int)policy["armorDRPerTier"];
                    totals["ar"] += ar; totals["dr"] += dr;
                    if (ar != 0 || dr != 0) sources.Add(new Core2RatingSource { Name = "Equipped upgrade tiers", Kind = "equipment", SourceId = "configured-upgrades", Ar = ar });
                }
            }
            return new Core2RatingReceipt { Totals = totals, BaseEvasionBonus = gear.Totals["evasion"], BasePrecisionBonus = gear.Totals["precision"], Sources = sources };
        }

        /// <summary>JS runMods: run-scoped mods of worn pieces (pool mods only for pieces Core gear does not resolve) and the gear's HP/MP/SP.</summary>
        public static (Dictionary<string, int> Pools, List<(string Status, int Stacks)> StartStatuses) RunMods(Core2RunData data, Core2Run run)
        {
            var gear = EquipmentReceipt(data, run);
            var resolved = new HashSet<string>(gear.Sources.Select(s => s.SourceId), StringComparer.Ordinal);
            var pools = new Dictionary<string, int>(StringComparer.Ordinal) { ["maxHp"] = 0, ["maxMana"] = 0, ["maxStamina"] = 0 };
            var stacks = new List<KeyValuePair<string, int>>();
            foreach (var piece in Worn(data, run.Loadout, run.Class))
            {
                var resolvedPiece = resolved.Contains(piece.Armor ? "armor/" + run.Class + "/" + piece.Id : "armament/" + piece.Id);
                foreach (var raw in Core2Json.Strings(piece.Row["mods"]))
                {
                    var m = Mod.Match(raw.Trim());
                    if (!m.Success) continue;
                    var spec = data.ModFields[m.Groups[2].Value] as JObject;
                    if (spec == null || (string)spec["scope"] != "run") continue;
                    var apply = (string)spec["apply"];
                    var add = m.Groups[3].Value.Length > 0;
                    var value = (int)(m.Groups[3].Value == "-" ? -double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture) : double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture));
                    if (pools.ContainsKey(apply))
                    {
                        if (resolvedPiece) continue;
                        pools[apply] = add ? pools[apply] + value : value;
                    }
                    else if (apply == "startStatus")
                    {
                        var status = (string)spec["status"];
                        var index = stacks.FindIndex(s => s.Key == status);
                        var prev = index >= 0 ? stacks[index].Value : 0;
                        var next = add ? prev + value : value;
                        if (index >= 0) stacks[index] = new KeyValuePair<string, int>(status, next); else stacks.Add(new KeyValuePair<string, int>(status, next));
                    }
                }
            }
            pools["maxHp"] += gear.Totals["hp"]; pools["maxMana"] += gear.Totals["mp"]; pools["maxStamina"] += gear.Totals["sp"];
            return (pools, stacks.Where(s => s.Value > 0).Select(s => (s.Key, s.Value)).ToList());
        }

        /// <summary>JS moveEquipmentPool: resize a pool, carrying its absolute deficit.</summary>
        private static int MovePool(Core2Run run, string maxField, int nextMax, int? carriedDeficit)
        {
            int Get(string f) => f switch { "maxHp" => run.MaxHp, "hp" => run.Hp, "maxMana" => run.MaxMana, "mana" => run.Mana, "maxStamina" => run.MaxStamina, _ => run.Stamina };
            void Set(string f, int v) { switch (f) { case "maxHp": run.MaxHp = v; break; case "hp": run.Hp = v; break; case "maxMana": run.MaxMana = v; break; case "mana": run.Mana = v; break; case "maxStamina": run.MaxStamina = v; break; default: run.Stamina = v; break; } }
            var current = maxField == "maxHp" ? "hp" : maxField == "maxMana" ? "mana" : "stamina";
            var oldMax = Get(maxField);
            var surplus = current == "stamina" ? Math.Max(0, Get(current) - oldMax) : 0;
            var observed = Math.Max(0, oldMax - Get(current));
            var prior = carriedDeficit.HasValue && carriedDeficit.Value >= 0 ? carriedDeficit.Value : observed;
            var represented = Math.Min(prior, oldMax);
            var deficit = Math.Max(0, prior + observed - represented);
            Set(maxField, nextMax);
            Set(current, Math.Max(0, nextMax - deficit) + surplus);
            return deficit;
        }

        /// <summary>JS reconcileRunLoadoutHp (Core: live gear bonuses adopted): every pool re-derived at the run's level, deficits carried.</summary>
        public static void ReconcilePools(Core2RunData data, Core2Run run)
        {
            var bonuses = RunMods(data, run).Pools;
            var attributes = EffectiveAttributes(data, run);
            var level = Math.Max(1, run.Level.Level);
            var deficits = new Dictionary<string, int>(StringComparer.Ordinal);
            int? Carried(string field) => run.EquipmentPoolDeficits != null && run.EquipmentPoolDeficits.TryGetValue(field, out var d) ? d : (int?)null;
            var hp = Core2DerivedStats.Derive(run.DerivedStatRules, "hp", attributes, level, run.Class);
            deficits["hp"] = MovePool(run, "maxHp", Math.Max(1, hp + bonuses["maxHp"] + run.MaxHpAdjustment), Carried("hp"));
            var mana = Core2DerivedStats.Derive(run.DerivedStatRules, "mana", attributes, level, run.Class);
            deficits["mana"] = MovePool(run, "maxMana", Math.Max(0, mana + bonuses["maxMana"]), Carried("mana"));
            var stamina = Core2DerivedStats.Derive(run.DerivedStatRules, "stamina", attributes, level, run.Class);
            deficits["stamina"] = MovePool(run, "maxStamina", Math.Max(0, stamina + bonuses["maxStamina"]), Carried("stamina"));
            run.EquipmentPoolBonuses = bonuses;
            run.EnergyMax = run.MaxStamina;
            run.EquipmentPoolDeficits = deficits;
        }

        /// <summary>JS rederivePools: draw re-derived from the snapshot at the run's level, then every pool reconciled.</summary>
        public static void RederivePools(Core2RunData data, Core2Run run)
        {
            run.DrawPerTurn = Core2DerivedStats.Derive(run.DerivedStatRules, "draw", EffectiveAttributes(data, run), Math.Max(1, run.Level.Level), run.Class);
            ReconcilePools(data, run);
        }

        /// <summary>JS configuredGearGrantInstances: the cards worn Core gear lends (gear[].grantedCards), with stable mount ids.</summary>
        public static List<Core2RunCard> GrantInstances(Core2RunData data, Core2Run run)
        {
            var output = new List<Core2RunCard>();
            var copies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var piece in Worn(data, run.Loadout, run.Class))
            {
                var row = data.Content.Gear.Select(g => g.Raw).FirstOrDefault(candidate => candidate["grantedCards"] is JArray granted && granted.Count > 0
                    && ((string)candidate["source"]?["id"] ?? (string)candidate["id"]) == piece.Id && ((string)candidate["kind"] == "armor") == piece.Armor
                    && (!(candidate["classes"] is JArray classes) || classes.Any(c => (string)c == run.Class)));
                if (row == null) continue;
                var owner = piece.Armor ? piece.ItemRef : piece.Id;
                foreach (var grant in ((JArray)row["grantedCards"]).OfType<JObject>())
                {
                    var key = owner + ":" + (string)grant["card"];
                    copies.TryGetValue(key, out var start);
                    var count = (int)grant["copies"];
                    copies[key] = start + count;
                    for (var i = start; i < start + count; i++)
                        output.Add(new Core2RunCard { InstanceId = "granted:" + owner + ":" + (string)grant["card"] + ":" + i, CardId = (string)grant["card"], EquipmentRole = "granted", GrantedBy = owner });
                }
            }
            return output;
        }

        /// <summary>JS reconcileConfiguredGearGrants: drop item-owned cards no worn piece lends, keep those still lent, append the missing.</summary>
        public static void ReconcileGrantedCards(Core2RunData data, Core2Run run)
        {
            var desired = GrantInstances(data, run);
            var wanted = desired.ToDictionary(d => d.InstanceId, StringComparer.Ordinal);
            var present = new HashSet<string>(StringComparer.Ordinal);
            run.Sideboard = run.Sideboard.Where(card =>
            {
                if (!card.ItemOwned) return true;
                if (!wanted.ContainsKey(card.InstanceId) || present.Contains(card.InstanceId)) return false;
                present.Add(card.InstanceId);
                return true;
            }).ToList();
            var kept = new List<Core2RunCard>();
            foreach (var card in run.Deck)
            {
                if (!card.ItemOwned) { kept.Add(card); continue; }
                if (!wanted.TryGetValue(card.InstanceId, out var want) || present.Contains(card.InstanceId)) continue;
                present.Add(card.InstanceId);
                kept.Add(want);
            }
            foreach (var d in desired) if (!present.Contains(d.InstanceId)) kept.Add(d);
            run.Deck = kept;
        }

        /// <summary>
        /// The property carriers a fight mounts for the player (JS createCombat: loadout pieces, then held relics, then the class
        /// card), each with its rules' triggers (JS carrierRules).
        /// </summary>
        public static List<Core2PropertyCarrier> PropertyCarriers(Core2RunData data, Core2Run run)
        {
            var output = new List<Core2PropertyCarrier>();
            void Add(string kind, string id, IReadOnlyList<string> tags)
            {
                if (tags == null || tags.Count == 0) return;
                // One mount per source key: the same armament in both hands mounts once (JS syncLoadoutProperties).
                if (output.Any(c => c.Kind == kind && c.Id == id)) return;
                var rules = data.CarrierRules(tags);
                if (rules.Count == 0) return;
                var carrier = new Core2PropertyCarrier { Kind = kind, Id = id };
                foreach (var rule in rules)
                    foreach (var trigger in ((rule["triggers"] as JArray) ?? new JArray()).OfType<JObject>()) carrier.Triggers.Add(trigger);
                output.Add(carrier);
            }
            foreach (var piece in Worn(data, run.Loadout, run.Class)) Add(piece.Armor ? "armour" : "armament", piece.Id, Core2Json.Strings(piece.Row["propertyTags"]));
            foreach (var relic in run.Relics) Add("relic", relic, Core2Json.Strings(data.Relic(relic)["propertyTags"]));
            Add("class", run.Class, data.Class(run.Class).PropertyTags);
            return output;
        }
    }
}
