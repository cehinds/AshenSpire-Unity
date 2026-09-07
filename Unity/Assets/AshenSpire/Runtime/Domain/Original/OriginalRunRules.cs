// OriginalRunRules.cs — deterministic original act boot, encounter and economy rolls.
// Content is copied at construction. Change authored map/balance/catalog rows,
// never Unity Random. Rolls retain original stream names and draw order.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalRunRules
    {
        private readonly JObject _data;
        public OriginalRunRules(JObject content) { _data = (JObject)content.DeepClone(); }
        private IEnumerable<JObject> Rows(string table) => ((JArray)_data.SelectToken(table)).Cast<JObject>();
        private JObject Record(string table, string id) => Rows(table).First(x => (string)x["id"] == id);
        private static string[] Ids(JToken values) => values is JArray array ? array.Values<string>().ToArray() : Array.Empty<string>();
        private T Pick<T>(RandomStreams rng, string stream, IList<T> values) => values[rng.Int(stream, 0, values.Count - 1)];
        private double Passive(JObject run, string key) => Ids(run["relics"]).Aggregate(1d, (m,id) => m * ((double?)Record("relics",id)["passives"]?[key] ?? 1));
        private bool Flag(JObject run, string key) => Ids(run["relics"]).Any(id => (bool?)Record("relics",id)["passives"]?[key] == true);
        private static bool RewardRelic(JObject row) => ((string)row["pool"] ?? "reward") == "reward";
        public static bool HistoryMet(JToken requirement, JArray history)
        {
            if (requirement == null || requirement.Type == JTokenType.Null) return true;
            if (!(requirement is JObject groups) || groups.Properties().Any(p => !new[] {"all","any","none"}.Contains(p.Name))) return false;
            bool Has(JToken wanted) => wanted["eventId"]?.Type == JTokenType.String && wanted["choiceId"]?.Type == JTokenType.String && history.Any(row => (string)row["kind"] == "eventChoice" && (string)row["eventId"] == (string)wanted["eventId"] && (string)row["choiceId"] == (string)wanted["choiceId"]);
            foreach (var group in groups.Properties())
            {
                if (!(group.Value is JArray refs)) return false;
                if (group.Name == "all" && !refs.All(Has)) return false;
                if (group.Name == "any" && !refs.Any(Has)) return false;
                if (group.Name == "none" && refs.Any(Has)) return false;
            }
            return true;
        }
        public JObject BuildAct(RandomStreams rng, int act, JArray history, JObject mapShape = null, JObject mapShapeLimits = null)
        {
            var config = (JObject)_data["mapConfigs"][act.ToString()] ?? throw new ArgumentException("Unknown act");
            if (mapShape != null) config = (JObject)OriginalMapShape.Apply(config, mapShape, mapShapeLimits)["config"];
            var map = ActMapGenerator.Generate(config, rng); var assigned = new List<string>();
            foreach (var node in ((JObject)map["nodes"]).Properties().Select(p => (JObject)p.Value))
            {
                if ((string)node["type"] != "event") continue;
                var resolved = ResolveUnknown(rng, act, assigned, history); node["resolved"] = resolved;
                if ((string)resolved["kind"] == "event") assigned.Add((string)resolved["eventId"]);
            }
            return map;
        }
        public JObject ResolveUnknown(RandomStreams rng, int act, IEnumerable<string> seen, JArray history)
        {
            var weights = (JObject)_data["mapConfigs"][act.ToString()]["unknownWeights"];
            double roll = rng.Float("events") * weights.Properties().Sum(p => (double)p.Value); var kind = "event";
            foreach (var pair in weights.Properties()) { roll -= (double)pair.Value; if (roll < 0) { kind = pair.Name; break; } }
            if (kind != "event") return new JObject { ["kind"] = kind };
            var gates = (JObject)_data["eventHistoryRequirements"];
            var completed = history.Where(row => (string)row["kind"] == "eventChoice").Select(row => (string)row["eventId"]).ToHashSet();
            var earned = Rows("events").Select(row => (string)row["id"]).Where(id => gates[id] == null || (!completed.Contains(id) && HistoryMet(gates[id],history))).ToList();
            var pool = earned.Where(id => !seen.Contains(id)).ToList(); if (pool.Count == 0) pool = earned;
            return pool.Count == 0 ? new JObject { ["kind"] = "fight" } : new JObject { ["kind"] = "event", ["eventId"] = Pick(rng,"events",pool) };
        }
        public string Encounter(RandomStreams rng, int act, string pool, IEnumerable<string> exclude)
        {
            var full = Rows("encounters").Where(row => (string)row["pool"] == pool && ((int?)row["act"] ?? 1) == act).ToList();
            var candidates = full.Where(row => !exclude.Contains((string)row["id"])).ToList(); if (candidates.Count == 0) candidates = full;
            if (candidates.Count == 0) throw new ArgumentException("No authored encounter in act/pool");
            var roll = rng.Float("enemyAI") * candidates.Sum(row => (double)row["weight"]);
            foreach (var row in candidates) { roll -= (double)row["weight"]; if (roll < 0) return (string)row["id"]; }
            return (string)candidates.Last()["id"];
        }
        private List<string> UtilityFlasks()
        {
            string Kind(JObject row) => (string)row["kind"] ?? (((JArray)row["effects"]).Any(e => (string)e["op"] == "restoreMana") ? "mana" : ((JArray)row["effects"]).Any(e => (string)e["op"] == "heal") ? "hp" : "utility");
            var chargeIds = new[] { "hp", "mana" }.Select(kind => (string)Rows("flasks").First(row => Kind(row) == kind)["id"]).Concat(new[] { "crimsonFlask", "azureFlask" }).ToHashSet();
            return Rows("flasks").Select(row => (string)row["id"]).Where(id => !chargeIds.Contains(id)).ToList();
        }
        public JObject Shop(RandomStreams rng, JObject run)
        {
            var bal = _data["balance"]["shop"];
            var cards = Ids(Record("classes", (string)run["class"])["cardPool"]).ToList();
            cards.AddRange(Rows("cards").Where(c => (string)c["class"] == "colorless" && new[] { "common", "uncommon", "rare" }.Contains((string)c["rarity"])).Select(c => (string)c["id"]));
            var picked = new List<string>(); while (picked.Count < (int)bal["cardStock"] && cards.Count > 0) { var id = Pick(rng,"shop",cards); cards.Remove(id); picked.Add(id); }
            JObject Item(string table, string id, string costKey) { var range = bal[costKey][(string)Record(table,id)["rarity"]]; return new JObject { ["id"] = id, ["cost"] = rng.Int("shop",(int)range[0],(int)range[1]) }; }
            var cardRows = new JArray(picked.Select(id => Item("cards",id,"cardCost")));
            var relicPool = Rows("relics").Where(r => RewardRelic(r) && new[] { "common", "uncommon", "rare" }.Contains((string)r["rarity"]) && !Ids(run["relics"]).Contains((string)r["id"])).Select(r => (string)r["id"]).ToList();
            var relics = new JArray(); for (var i = 0; i < (int)bal["relicStock"] && relicPool.Count > 0; i++) { var id = Pick(rng,"shop",relicPool); relicPool.Remove(id); relics.Add(Item("relics",id,"relicCost")); }
            var flasks = new JArray(); var flaskIds = UtilityFlasks();
            for (var i = 0; i < (int)bal["flaskStock"] && flaskIds.Count > 0; i++) flasks.Add(new JObject { ["id"] = Pick(rng,"shop",flaskIds), ["cost"] = rng.Int("shop",(int)bal["flaskCost"][0],(int)bal["flaskCost"][1]) });
            var stock = new JObject { ["cards"] = cardRows, ["relics"] = relics, ["flasks"] = flasks, ["removeCost"] = (int)bal["removeBase"] + (int)bal["removeStep"] * ((int?)run["removesPurchased"] ?? 0) };
            var multiplier = new OriginalCustomRunRules(_data).ShopPriceMultiplier(run);
            if (multiplier != 1) { foreach (var row in cardRows.Concat(relics).Concat(flasks)) row["cost"] = checked((int)Math.Ceiling((int)row["cost"] * multiplier)); stock["removeCost"] = checked((int)Math.Ceiling((int)stock["removeCost"] * multiplier)); }
            return stock;
        }
        public int Cinders(RandomStreams rng, string pool, JObject run)
        {
            var range = _data["balance"]["rewards"]["cinders"][pool];
            return (int)Math.Floor(rng.Int("misc",(int)range[0],(int)range[1]) * Passive(run,"runeGainMult"));
        }
        public JArray Cards(RandomStreams rng, string pool, JObject run)
        {
            var bal = _data["balance"]["rewards"]; var count = (int)bal["cardChoices"] + (pool == "elite" && Flag(run,"eliteExtraCardReward") ? 1 : 0);
            var options = Ids(Record("classes",(string)run["class"])["cardPool"]).Select(id => Record("cards",id)).ToList();
            var weights = OriginalCustomRunRules.Enabled(run,"chaosRewards") ? new JObject { ["common"] = 1, ["uncommon"] = 1, ["rare"] = 1 } : (JObject)(bal["rarityWeights"][pool] ?? bal["rarityWeights"]["normal"]);
            var rarities = weights.Properties().Where(p => options.Any(c => (string)c["rarity"] == p.Name)).ToList();
            var total = rarities.Sum(r => (double)r.Value); var picks = new List<string>();
            for (var guard = 0; picks.Count < count && guard < 100; guard++)
            {
                var roll = rng.Float("cardRewards") * total; var rarity = rarities.Last().Name;
                foreach (var r in rarities) { roll -= (double)r.Value; if (roll < 0) { rarity = r.Name; break; } }
                var candidates = options.Where(c => (string)c["rarity"] == rarity && !picks.Contains((string)c["id"])).Select(c => (string)c["id"]).ToList();
                if (candidates.Count > 0) picks.Add(Pick(rng,"cardRewards",candidates));
            }
            return new JArray(picks);
        }
        public string Flask(RandomStreams rng, JObject run)
        {
            var bal = _data["balance"]["rewards"]; var chance = (int?)run["flaskChancePct"] ?? (int)bal["flaskDropBasePct"];
            var hit = rng.Float("flaskRewards") * 100 < chance;
            run["flaskChancePct"] = hit ? Math.Max(0,chance - (int)bal["flaskDropStepPct"]) : Math.Min(100,chance + (int)bal["flaskDropStepPct"]);
            var pool = UtilityFlasks(); return hit && pool.Count > 0 ? Pick(rng,"flaskRewards",pool) : null;
        }
        public string Relic(RandomStreams rng, JObject run, params string[] rarities)
        {
            if (rarities.Length == 0) rarities = new[] { "common", "uncommon", "rare" };
            var pool = Rows("relics").Where(r => RewardRelic(r) && rarities.Contains((string)r["rarity"]) && !Ids(run["relics"]).Contains((string)r["id"])).Select(r => (string)r["id"]).ToList();
            return pool.Count > 0 ? Pick(rng,"relicRewards",pool) : null;
        }
        public string Armament(RandomStreams rng, string source, IEnumerable<string> found, IEnumerable<string> carried)
        {
            var cfg = _data["balance"]["equipment"]["drops"]; var chance = (int?)cfg["chance"]?[source] ?? 0;
            if ((bool?)cfg["enabled"] != true || chance == 0 || rng.Int("armaments",1,100) > chance) return null;
            var seen = found.Concat(carried).ToHashSet(); var pool = Rows("equipment.armaments").Where(a => (string)a["unlock"] == "").ToList();
            if ((bool?)cfg["preferUnfound"] == true) { pool = pool.Where(a => !seen.Contains((string)a["id"])).ToList(); if (pool.Count == 0) return null; }
            var weights = (JObject)cfg["rarityWeights"][source]; var rarities = weights.Properties().Where(r => pool.Any(a => (string)a["rarity"] == r.Name)).ToList();
            if (rarities.Count == 0) return null;
            var roll = rng.Float("armaments") * rarities.Sum(r => (double)r.Value); var rarity = rarities.Last().Name;
            foreach (var r in rarities) { roll -= (double)r.Value; if (roll < 0) { rarity = r.Name; break; } }
            var candidates = pool.Where(a => (string)a["rarity"] == rarity && (double)a["dropWeight"] > 0).ToList(); if (candidates.Count == 0) return null;
            roll = rng.Float("armaments") * candidates.Sum(a => (double)a["dropWeight"]);
            foreach (var a in candidates) { roll -= (double)a["dropWeight"]; if (roll < 0) return (string)a["id"]; }
            return (string)candidates.Last()["id"];
        }
        public int RestHeal(JObject run) => (int)Math.Floor(Math.Min((int)run["maxHp"]-(int)run["hp"], (int)Math.Floor((int)run["maxHp"] * (double)_data["balance"]["shrine"]["healPct"] * Passive(run,"shrineHealMult") / 100)) * new OriginalCustomRunRules(_data).HealMultiplier(run));
        public bool CanRest(JObject run) => !Flag(run,"shrineNoRest");
        public int OpenedSets(JObject run,string slotId)
        {
            var slot = Record("equipment.slots",slotId); var cap = Math.Max(1,(int)slot["sets"]);
            var earned = Ids(run["profileMeta"]?["unlocked"] ?? run["unlocked"]).ToHashSet();
            var opened = 1 + Rows("unlocks").Count(row => (string)row["kind"] == "slot" && (string)row["ref"] == slotId && earned.Contains((string)row["id"]));
            var ids = run["loadout"]?["sets"]?[slotId] as JArray ?? throw new ArgumentException("Missing equipment slot");
            for (var i = 0; i < ids.Count; i++) if (!string.IsNullOrEmpty((string)ids[i])) opened = Math.Max(opened,i + 1);
            return Math.Min(cap,opened);
        }
        public JObject SmithServices(RandomStreams rng, string kind)
        {
            var row = _data["balance"]["smithing"]["services"]["offeredAt"][kind]; var chance = (int?)row?["chance"] ?? 0;
            var rolled = chance > 0 && chance < 100; var offered = chance >= 100 || (rolled && rng.Float("smith") * 100 < chance);
            return new JObject { ["nodeKind"] = kind, ["offered"] = offered, ["rolled"] = rolled, ["chance"] = chance, ["services"] = offered ? row["services"].DeepClone() : new JArray() };
        }
    }
}
