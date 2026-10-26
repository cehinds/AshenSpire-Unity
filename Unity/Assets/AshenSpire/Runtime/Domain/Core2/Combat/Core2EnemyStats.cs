// Core2EnemyStats.cs — an enemy's Core 2 stat block: attributes, level, SP/MP pools and ratings, from its character row,
// its default gear and its enemy relics (owner 2026-10-17: one algorithm for players and enemies).
//
// JS at the pin:
//   engine/kernel/gameConfig.js     characterSettings, characterTags, characterRule, enemyLevel, resolveEnemyAttributes
//                                   (tier budget split by attributePriority, caps, seeded one-point variance),
//                                   enemyItemBonuses (gear points × pointValues, enemy relic self.<stat>=±N modifiers),
//                                   enemyPools (statRules per attribute and level, floored per term; pool penalties floor
//                                   at penaltyFloor), resolveGearItem / gearWeights / gearSplit (the gear module's merged
//                                   pool, largest remainder)
//   src/model/gameProfileEnemy.js   gameProfileEnemyRatings (what a Core 2 fight stamps on the enemy entity)
//   src/engine/combat.js            createCombat (the HP roll on enemyHP, the base ratings from the compiled enemy row)
//   src/model/combatRatings.js      persistentPoiseMaximum (Poise pool = Poise rating × poisePool 1)
// The SP pool is the enemy's turn budget (SPEC §4.6a): maxStamina, refilled at its turn start less a Staggered loss.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>What gameProfileEnemyRatings stamps on a Core 2 enemy.</summary>
    public sealed class Core2EnemyStatBlock
    {
        public Dictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Level { get; set; } = 1;
        public int MaxStamina { get; set; }
        public int MaxMana { get; set; }
        public int BaseEvasionBonus { get; set; }
        public int BasePrecisionBonus { get; set; }
        public Dictionary<string, int> Ratings { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The pools enemyPools derived (hp, sp, mp, ar, dr, hand), for inspection.</summary>
        public Dictionary<string, int> Pools { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>A gear piece as the gear module resolves it (JS resolveGearItem).</summary>
    public sealed class Core2ResolvedGear
    {
        public string Id { get; set; }
        public int Rank { get; set; }
        public int? Budget { get; set; }
        public Dictionary<string, int> Points { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Values { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public static class Core2EnemyStats
    {
        public static readonly IReadOnlyList<string> GearRatings = new[] { "ar", "dr", "poise", "hp", "mp", "sp", "ward", "evasion", "precision" };
        public static readonly IReadOnlyList<string> GearAttributes = new[] { "strength", "dexterity", "constitution", "wisdom", "intelligence" };
        public static readonly IReadOnlyList<string> GearKeys = GearRatings.Concat(GearAttributes).ToList();
        private static readonly IReadOnlyList<string> PoolMods = new[] { "sp", "mp", "hp" };
        private static readonly Regex ItemModifier = new Regex(@"^self\.(strength|dexterity|constitution|wisdom|intelligence|sp|mp|hp)=([+-]\d+)$", RegexOptions.CultureInvariant);

        /// <summary>JS characterSettings(modules): the enemies and characters modules merged, with the variance module.</summary>
        public static JObject CharacterSettings(JObject modules)
        {
            if (modules["enemies"] == null && modules["characters"] == null) return null;
            var settings = new JObject();
            foreach (var key in new[] { "enemies", "characters" })
                if (modules[key] is JObject module) foreach (var p in module.Properties()) settings[p.Name] = p.Value.DeepClone();
            settings["variance"] = modules["attribute-variance"] is JObject variance ? variance.DeepClone() : JValue.CreateNull();
            return settings;
        }

        private static double Number(JToken token) => token == null || token.Type == JTokenType.Null ? 0 : (double)token;
        private static bool Has(JToken token) => token != null && token.Type != JTokenType.Null && !(token.Type == JTokenType.Boolean && !(bool)token)
            && !((token.Type == JTokenType.Integer || token.Type == JTokenType.Float) && (double)token == 0);

        /// <summary>JS characterTags(row).</summary>
        public static IReadOnlyList<string> CharacterTags(JObject row) => row["tags"] is JArray tags
            ? Core2Json.Strings(tags)
            : new[] { "character." + (row["player"] != null ? "player" : "enemy") }.Concat(row["tier"] != null ? new[] { "tier." + (string)row["tier"] } : Array.Empty<string>()).ToList();

        /// <summary>JS characterRule: pools, hand and budgets summed/overridden over the row's tag rules.</summary>
        public sealed class Rule
        {
            public Dictionary<string, double> Pools = new Dictionary<string, double>(StringComparer.Ordinal) { ["hp"] = 0, ["sp"] = 0, ["mp"] = 0 };
            public double Hand;
            public int[] AttributeBudget = { 0, 0 };
            public bool Variance;
        }

        public static Rule CharacterRule(JObject row, JObject settings)
        {
            var rule = new Rule();
            foreach (var tag in CharacterTags(row))
            {
                if (!(settings["tagRules"]?[tag] is JObject r)) continue;
                foreach (var k in new[] { "hp", "sp", "mp" }) rule.Pools[k] += Number(r["pools"]?[k]);
                rule.Hand += Number(r["hand"]);
                if (r["attributeBudget"] is JArray budget) rule.AttributeBudget = budget.Select(v => (int)v).ToArray();
                if (Has(r["variance"])) rule.Variance = true;
            }
            return rule;
        }

        /// <summary>JS enemyLevel: an authored level, else levelProfile.min (or .max when levelFrom says so), at least 1.</summary>
        public static int EnemyLevel(JObject row, JObject settings)
        {
            if (row["level"]?.Type == JTokenType.Integer) return Math.Max(1, (int)row["level"]);
            var profile = row["levelProfile"] as JObject ?? new JObject();
            var level = (string)settings["levelFrom"] == "levelProfile.max" ? (int?)profile["max"] ?? 1 : (int?)profile["min"] ?? 1;
            return Math.Max(1, level);
        }

        /// <summary>JS gearSplit: <paramref name="total"/> points over positive weights, floors first, then largest remainder (ties in key order).</summary>
        public static List<KeyValuePair<string, int>> GearSplit(double total, IReadOnlyList<KeyValuePair<string, double>> weights)
        {
            var keys = weights.Where(w => w.Value > 0).Select(w => w.Key).ToList();
            var output = keys.Select(k => new KeyValuePair<string, int>(k, 0)).ToList();
            var sum = keys.Sum(k => weights.First(w => w.Key == k).Value);
            if (total <= 0 || sum == 0) return output;
            var raw = keys.Select(k => (Key: k, Value: weights.First(w => w.Key == k).Value / sum * total)).ToList();
            var floors = raw.ToDictionary(r => r.Key, r => (int)Math.Floor(r.Value), StringComparer.Ordinal);
            var left = total - floors.Values.Sum();
            var order = raw.Select((r, i) => (r.Key, Fraction: r.Value - Math.Floor(r.Value), Index: keys.IndexOf(r.Key)))
                .OrderByDescending(r => r.Fraction).ThenBy(r => r.Index).ToList();
            foreach (var r in order) if (left-- > 0) floors[r.Key]++;
            return keys.Select(k => new KeyValuePair<string, int>(k, floors[k])).ToList();
        }

        private static List<KeyValuePair<string, double>> Weights(JToken token) => token is JObject row
            ? row.Properties().Select(p => new KeyValuePair<string, double>(p.Name, (double)p.Value)).ToList()
            : new List<KeyValuePair<string, double>>();

        /// <summary>JS gearWeights: the item's own weights, else its tags' weights × priority / 100, else kind, slot, global.</summary>
        private static List<KeyValuePair<string, double>> GearWeights(JObject item, JObject settings, string pool)
        {
            if (item["weights"]?[pool] is JObject own && own.Count > 0) return Weights(own);
            var summed = new List<KeyValuePair<string, double>>();
            foreach (var tag in Core2Json.Strings(item["tags"]))
            {
                var rule = settings["tags"]?[tag] as JObject;
                if (!(rule?["weights"]?[pool] is JObject w)) continue;
                foreach (var p in w.Properties())
                {
                    var add = (double)p.Value * Number(rule["priority"]) / 100;
                    var index = summed.FindIndex(s => s.Key == p.Name);
                    if (index >= 0) summed[index] = new KeyValuePair<string, double>(p.Name, summed[index].Value + add);
                    else summed.Add(new KeyValuePair<string, double>(p.Name, add));
                }
            }
            if (summed.Count > 0) return summed;
            var weights = settings["weights"];
            return Weights(weights?["kinds"]?[(string)item["kind"] ?? ""]?[pool] ?? weights?["slots"]?[(string)item["slot"] ?? ""]?[pool] ?? weights?["global"]?[pool]);
        }

        /// <summary>JS armorBudgetFor.</summary>
        private static int ArmorBudgetFor(JObject item, JObject settings, int rank)
        {
            var b = (JObject)settings["armorBudget"];
            var rarity = (string)item["rarity"] ?? "";
            return (int)b["baseline"] + (rank - 1) * ((int?)b["perRank"] ?? 0) + (b["rarityBonus"] is JObject bonus && bonus[rarity] != null ? (int)bonus[rarity] : 0);
        }

        /// <summary>JS resolveGearItem: authored points kept, the rest of the (merged) pool filled by weights.</summary>
        public static Core2ResolvedGear ResolveGearItem(JObject item, JObject settings, int? rankOverride = null)
        {
            var rank = rankOverride ?? (int?)item["rank"] ?? 1;
            if (rank < 1) throw new ArgumentException("Gear rank must be a positive integer");
            var kind = (string)item["kind"];
            var rarity = (string)item["rarity"] ?? "";
            int? armorBudget = kind == "armor" && settings["armorBudget"] is JObject ? ArmorBudgetFor(item, settings, rank) : (int?)null;
            var fillTo = (string)settings["fillTo"];
            (int Min, int Max) Size(string pool) => settings["poolSizes"]?[pool]?[rarity] is JObject s ? ((int)s["min"], (int)s["max"]) : (0, 0);
            int Target(string pool) => fillTo == "none" ? 0 : fillTo == "min" ? Size(pool).Min : Size(pool).Max;
            var explicitPoints = GearKeys.ToDictionary(k => k, k => (int?)item["points"]?[k] ?? 0, StringComparer.Ordinal);
            var tagCost = Core2Json.Strings(item["tags"]).Sum(t => (int?)settings["tags"]?[t]?["cost"] ?? 0);
            int Sum(IEnumerable<string> keys) => keys.Sum(k => explicitPoints[k]);
            var perRank = (int?)settings["armorBudget"]?["perRank"] ?? 0;
            var tierPoints = (rank - 1) * perRank;
            var merged0 = settings["poolSizes"]?["merged"]?[rarity] as JObject;
            if ((kind == "armor" || kind == "shield") && (string)settings["armorBudget"]?["perRankStat"] == "dr" && tierPoints > 0)
            {
                var whole0 = armorBudget ?? tierPoints + (merged0 != null ? (fillTo == "none" ? 0 : fillTo == "min" ? (int)merged0["min"] : (int)merged0["max"]) : Target("ratings") + Target("bonus"));
                explicitPoints["dr"] += Math.Max(0, Math.Min(tierPoints, (fillTo == "none" ? 0 : whole0) - Sum(GearKeys) - tagCost));
            }
            var maxAttributes = (int?)settings["maxAttributes"] ?? 0;
            List<KeyValuePair<string, double>> LimitAttributes(List<KeyValuePair<string, double>> weights)
            {
                var chosen = GearAttributes.Where(k => explicitPoints[k] > 0).ToList();
                var weightOf = weights.ToDictionary(w => w.Key, w => w.Value, StringComparer.Ordinal);
                var candidates = GearAttributes.Where(k => !chosen.Contains(k) && weightOf.TryGetValue(k, out var w) && w > 0)
                    .Select((k, i) => (k, i)).OrderByDescending(r => weightOf[r.k]).ThenBy(r => r.i).Select(r => r.k);
                foreach (var k in candidates) if (chosen.Count < maxAttributes) chosen.Add(k);
                return weights.Where(w => GearRatings.Contains(w.Key) || chosen.Contains(w.Key)).ToList();
            }
            var ratingsW = GearWeights(item, settings, "ratings");
            var bonusW = LimitAttributes(GearWeights(item, settings, "bonus"));
            List<KeyValuePair<string, int>> fill;
            if ((string)settings["pools"] == "merged" || armorBudget != null)
            {
                List<KeyValuePair<string, double>> Norm(List<KeyValuePair<string, double>> w, double share)
                {
                    var s = w.Sum(p => p.Value); if (s == 0) s = 1;
                    return w.Select(p => new KeyValuePair<string, double>(p.Key, p.Value / s * share)).ToList();
                }
                var shieldTierPoints = kind == "shield" && settings["armorBudget"] is JObject ? (rank - 1) * perRank : 0;
                var whole = armorBudget ?? shieldTierPoints + (merged0 != null ? (fillTo == "none" ? 0 : fillTo == "min" ? (int)merged0["min"] : (int)merged0["max"]) : Target("ratings") + Target("bonus"));
                var remaining = (fillTo == "none" ? 0 : whole) - Sum(GearKeys) - tagCost;
                var share = settings["mergedShare"]?[rarity] as JObject ?? (JObject)settings["mergedShare"]["default"];
                var combined = Norm(ratingsW, (double)share["ratings"]);
                foreach (var p in Norm(bonusW, (double)share["bonus"]))
                {
                    var index = combined.FindIndex(c => c.Key == p.Key);
                    if (index >= 0) combined[index] = p; else combined.Add(p);
                }
                fill = GearSplit(remaining, combined);
            }
            else
            {
                fill = GearSplit(Target("ratings") - Sum(GearRatings), ratingsW).Concat(GearSplit(Target("bonus") - Sum(GearAttributes) - tagCost, bonusW)).ToList();
            }
            var fillOf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in fill) fillOf[p.Key] = p.Value;
            var points = GearKeys.ToDictionary(k => k, k => explicitPoints[k] + (fillOf.TryGetValue(k, out var f) ? f : 0), StringComparer.Ordinal);
            var values = GearKeys.ToDictionary(k => k, k => points[k] * ((int?)settings["pointValues"]?[k] ?? 0), StringComparer.Ordinal);
            return new Core2ResolvedGear { Id = (string)item["id"], Rank = rank, Budget = armorBudget, Points = points, Values = values };
        }

        /// <summary>JS resolveEnemyAttributes(row, settings, seed): authored points kept, the tier budget filled by priority, then the seeded variance move.</summary>
        public static Dictionary<string, int> ResolveEnemyAttributes(JObject row, JObject settings, string seed = null)
        {
            var fill = (JObject)settings["attributeFill"];
            var tier = CharacterRule(row, settings);
            var budget = row["attributePoints"]?.Type == JTokenType.Integer ? (int)row["attributePoints"] : tier.AttributeBudget[1];
            var set = GearAttributes.ToDictionary(id => id, id => (int?)row["attributes"]?[id] ?? 0, StringComparer.Ordinal);
            var output = new Dictionary<string, int>(set, StringComparer.Ordinal);
            var caps = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var source in new[] { fill["caps"] as JObject, row["attributeCaps"] as JObject })
                if (source != null) foreach (var p in source.Properties()) caps[p.Name] = (int)p.Value;
            var order = row["attributePriority"] is JArray priority ? Core2Json.Strings(priority) : Core2Json.Strings(fill["defaultPriority"]);
            var ranks = (row["priorityWeights"] as JArray ?? (JArray)fill["priorityWeights"]).Select(v => (double)v).ToList();
            var weights = order.Select((id, i) => new KeyValuePair<string, double>(id, i < ranks.Count ? ranks[i] : 0)).ToList();
            var left = budget - set.Values.Sum();
            for (var guard = 0; left > 0 && guard < 10; guard++)
            {
                var open = weights.Where(w => !caps.ContainsKey(w.Key) || output[w.Key] < caps[w.Key]).ToList();
                if (open.Count == 0) break;
                foreach (var share in GearSplit(left, open))
                {
                    var room = caps.ContainsKey(share.Key) ? Math.Min(share.Value, caps[share.Key] - output[share.Key]) : share.Value;
                    output[share.Key] += room; left -= room;
                }
                weights = open;
            }
            var variance = settings["variance"] as JObject;
            var points = (int?)row["variance"]?["points"] ?? (int?)variance?["points"] ?? 0;
            var among = (int?)row["variance"]?["among"] ?? (int?)variance?["among"] ?? 2;
            if (seed != null && variance != null && points > 0 && tier.Variance)
            {
                var top = order.Take(among).ToList();
                var h = 7u;
                foreach (var c in seed + (string)row["id"]) h = Core2Rng.ToUint32((double)h * 31 + c);
                for (var i = 0; i < points; i++)
                {
                    var from = top[(int)(h % (uint)top.Count)];
                    var to = top[(int)((h >> 3) % (uint)top.Count)];
                    h = Core2Rng.ToUint32((double)h * 1103515245 + 12345);
                    if (from != to && output[from] > set[from] && (!caps.ContainsKey(to) || output[to] < caps[to])) { output[from]--; output[to]++; }
                }
            }
            return output;
        }

        /// <summary>JS enemyItemBonuses: gear values (the armorBudget path) or attribute points, plus relic modifiers.</summary>
        public static Dictionary<string, int> EnemyItemBonuses(JObject row, IEnumerable<JObject> gear, IEnumerable<JObject> enemyRelics, JObject gearSettings)
        {
            var bonus = GearAttributes.Concat(PoolMods).Concat(GearKeys).Distinct().ToDictionary(id => id, id => 0, StringComparer.Ordinal);
            var items = gear.ToDictionary(g => (string)g["id"], StringComparer.Ordinal);
            foreach (var entry in (row["defaultGear"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (!items.TryGetValue((string)entry["id"] ?? "", out var item) || gearSettings == null) continue;
                var resolved = ResolveGearItem(item, gearSettings);
                var armor = gearSettings["armorBudget"] is JObject;
                foreach (var a in armor ? GearKeys : GearAttributes) bonus[a] += armor ? resolved.Values[a] : resolved.Points[a];
            }
            var relics = enemyRelics.ToDictionary(r => (string)r["id"], StringComparer.Ordinal);
            foreach (var entry in (row["defaultRelics"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if (!relics.TryGetValue((string)entry["id"] ?? "", out var relic)) continue;
                foreach (var modifier in Core2Json.Strings(relic["modifiers"]))
                {
                    var match = ItemModifier.Match(modifier);
                    if (match.Success) bonus[match.Groups[1].Value] += int.Parse(match.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                }
            }
            return bonus;
        }

        /// <summary>JS enemyPools: each statRules row on the (gear-boosted) attributes and level, floored per term.</summary>
        public static Dictionary<string, int> EnemyPools(JObject row, JObject settings, int level, Dictionary<string, int> allocated, Dictionary<string, int> bonus)
        {
            var tier = CharacterRule(row, settings);
            Func<double, double> round = (string)settings["rounding"] == "up" ? Math.Ceiling : (string)settings["rounding"] == "nearest" ? (Func<double, double>)(v => Math.Floor(v + 0.5)) : Math.Floor;
            var baseValues = new Dictionary<string, double>(StringComparer.Ordinal) { ["hp"] = tier.Pools["hp"], ["sp"] = tier.Pools["sp"], ["mp"] = tier.Pools["mp"], ["hand"] = tier.Hand, ["ar"] = 0, ["dr"] = 0 };
            var attributes = GearAttributes.ToDictionary(id => id, id => Math.Max(0, (allocated.TryGetValue(id, out var own) ? own : 0) + (bonus != null && bonus.TryGetValue(id, out var b) ? b : 0)), StringComparer.Ordinal);
            var pools = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var stat in ((JObject)settings["statRules"]).Properties())
            {
                var rule = (JObject)stat.Value;
                var value = baseValues.TryGetValue(stat.Name, out var start) ? start : 0;
                foreach (var id in GearAttributes) if (Has(rule[id])) value += round(attributes[id] * (double)rule[id]);
                if (Has(rule["perLevel"])) value += round((level - 1) * (double)rule["perLevel"]);
                var mod = PoolMods.Contains(stat.Name) && bonus != null && bonus.TryGetValue(stat.Name, out var m) ? m : 0;
                var floor = (double?)settings["penaltyFloor"]?[stat.Name] ?? 0;
                pools[stat.Name] = (int)(mod < 0 ? Math.Max(value + mod, Math.Min(value, floor)) : value + mod);
            }
            return pools;
        }

        /// <summary>
        /// JS gameProfileEnemyRatings for one enemy of a Core 2 fight. <paramref name="baseRatings"/> are the ratings the
        /// fight gave it first (JS createCombat: ar/dr from the compiled row, ward and poise from poiseMax); the variance
        /// seed is "&lt;fight seed&gt;/&lt;enemy instance id&gt;".
        /// </summary>
        public static Core2EnemyStatBlock Resolve(Core2Content content, string enemyId, string instanceId, uint fightSeed, IReadOnlyDictionary<string, int> baseRatings)
        {
            var modules = (JObject)content.Profile["modules"];
            var settings = CharacterSettings(modules);
            var definition = ((JArray)content.Table("pack/enemies.json")).OfType<JObject>().FirstOrDefault(r => (string)r["id"] == enemyId)
                ?? throw new KeyNotFoundException("Core 2 enemy " + enemyId + " has no character row");
            var level = EnemyLevel(definition, settings);
            var allocated = ResolveEnemyAttributes(definition, settings, fightSeed.ToString(CultureInfo.InvariantCulture) + "/" + instanceId);
            var bonus = EnemyItemBonuses(definition, content.Gear.Select(g => g.Raw), content.EnemyRelics.Select(r => r.Raw), modules["gear"] as JObject);
            var pools = EnemyPools(definition, settings, level, allocated, bonus);
            int Base(string id) => baseRatings != null && baseRatings.TryGetValue(id, out var v) ? v : 0;
            return new Core2EnemyStatBlock
            {
                Attributes = allocated.ToDictionary(p => p.Key, p => p.Value + bonus[p.Key], StringComparer.Ordinal),
                Level = level, MaxStamina = pools["sp"], MaxMana = pools["mp"], Pools = pools,
                BaseEvasionBonus = bonus["evasion"], BasePrecisionBonus = bonus["precision"],
                Ratings = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["ar"] = (pools.TryGetValue("ar", out var ar) ? ar : 0) + bonus["ar"], ["dr"] = (pools.TryGetValue("dr", out var dr) ? dr : 0) + bonus["dr"], ["pr"] = 0,
                    ["poise"] = Base("poise") + bonus["poise"], ["ward"] = Base("ward") + bonus["ward"],
                },
            };
        }

        /// <summary>JS createCombat's first ratings for an enemy (from the compiled registry row): ar/dr, ward and poise from poiseMax.</summary>
        public static Dictionary<string, int> BaseRatings(Core2Enemy def)
        {
            var raw = def.Raw;
            int Or(string key, int fallback) { var v = (int?)raw[key] ?? 0; return v != 0 ? v : fallback; }
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["ar"] = (int?)raw["ar"] ?? 0, ["dr"] = (int?)raw["dr"] ?? 0, ["pr"] = 0,
                ["ward"] = Or("ward", def.PoiseMax), ["poise"] = Or("poise", def.PoiseMax),
            };
        }
    }
}
