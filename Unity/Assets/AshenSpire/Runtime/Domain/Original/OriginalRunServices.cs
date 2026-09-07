// OriginalRunServices.cs — shrine allocation and merchant buy-back transactions.
// Author prices in balance.levelUp/shop. Shrine UI submits pending counts by
// attribute; each point purchases one level on the original escalating ladder.
// Supply the run's frozen player projection and existing flask-growth callback.
// Plans are read-only. Commands commit a copy; failed commands retain all state.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed class OriginalRunServices
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _data;
        private readonly Action<JObject> _reconcile, _syncFlaskGrowth;
        public OriginalRunServices(OriginalContentCatalog catalog, Action<JObject> reconcile = null, Action<JObject> syncFlaskGrowth = null)
        { _catalog = catalog; _data = catalog.Data(); _reconcile = reconcile; _syncFlaskGrowth = syncFlaskGrowth; }
        private static int Count(JToken value, string name, int fallback = 0)
        {
            if (value == null || value.Type == JTokenType.Null) return fallback;
            if (value.Type != JTokenType.Integer || (long)value < 0 || (long)value > int.MaxValue) throw new ArgumentException(name + " must be a nonnegative integer.");
            return (int)value;
        }
        private JArray Attributes() => new JArray(_catalog.Table("attributes").OrderBy(a => (int)a["order"]).Select(a => a.DeepClone()));
        public int LevelCost(int levelsTaken)
        {
            if (levelsTaken < 0) throw new ArgumentException("Negative purchased levels.");
            var table = _data["balance"]["levelUp"];
            return checked(Count(table?["firstCost"],"level first cost") + Count(table?["costStep"],"level cost step") * levelsTaken);
        }
        public JObject LevelPlan(JObject run, int? pointsPerLevel = null)
        {
            var table = _data["balance"]["levelUp"]; var levels = Count(run["levelUps"],"level purchases"); var cost = LevelCost(levels);
            var cinders = Count(run["cinders"],"cinders"); var capped = table?["maxLevels"]?.Type == JTokenType.Integer && levels >= Count(table["maxLevels"],"level cap");
            var points = pointsPerLevel > 0 ? pointsPerLevel.Value : Math.Max(1,Count(table?["pointsPerLevel"],"points per level",1));
            return new JObject { ["levelsTaken"] = levels, ["cost"] = cost, ["cinders"] = cinders, ["affordable"] = cinders >= cost,
                ["capped"] = capped, ["short"] = Math.Max(0,cost-cinders), ["blockedBy"] = capped ? "cap" : cinders < cost ? "cinders" : null,
                ["offerable"] = !capped && cinders >= cost, ["pointsPerLevel"] = points, ["attributes"] = Attributes() };
        }
        public JObject LevelBudget(JObject run)
        {
            var taken = Count(run["levelUps"],"level purchases"); var left = Count(run["cinders"],"cinders"); var costs = new JArray();
            var cap = _data["balance"]["levelUp"]?["maxLevels"]; int total = 0;
            for (var k = 0; ; k++)
            {
                if (cap?.Type == JTokenType.Integer && taken + k >= Count(cap,"level cap")) break;
                var cost = LevelCost(checked(taken+k)); if (left < cost) break;
                if (cost == 0 && cap?.Type != JTokenType.Integer) throw new ArgumentException("Unbounded free level budget is invalid.");
                left -= cost; total = checked(total+cost); costs.Add(cost);
            }
            return new JObject { ["levels"] = costs.Count, ["costs"] = costs, ["total"] = total };
        }
        public JArray Sellables(JObject run)
        {
            var result = new JArray(); if ((bool?)run["profileMeta"]?["settings"]?["shopSell"] == false) return result;
            foreach (var kind in new[] { "relic","flask" })
            {
                var goods = run[kind + "s"] as JArray ?? new JArray();
                for (var index = 0; index < goods.Count; index++)
                {
                    var id = kind == "relic" ? (string)goods[index] : (string)goods[index]["flaskId"];
                    var definition = _catalog.Record(kind + "s",id); var price = SellPrice(kind,definition);
                    if (price > 0) result.Add(new JObject { ["kind"] = kind, ["index"] = index, ["id"] = id, ["name"] = definition["name"].DeepClone(), ["price"] = price });
                }
            }
            return result;
        }
        private int SellPrice(string kind,JObject definition)
        {
            var shop = _data["balance"]["shop"]; var fraction = (double?)shop["sellFraction"] ?? 0;
            if (!(fraction > 0)) return 0;
            var range = kind == "relic" ? shop["relicCost"]?[(string)definition["rarity"]] : shop["flaskCost"];
            return range == null ? 0 : checked((int)Math.Floor((double)range[0] * fraction));
        }
        public bool Apply(JObject run,string service,JObject request)
        {
            if (service != "levelUp" && service != "sell") return false;
            if ((string)run["phase"] != (service == "levelUp" ? "Shrine" : "Shop")) return false;
            var next = (JObject)run.DeepClone();
            if (service == "levelUp")
            {
                var allocation = request["allocation"] as JObject ?? throw new ArgumentException("Level allocation is required.");
                var attributes = Attributes(); var ids = attributes.Values<JObject>().Select(a => (string)a["id"]).ToArray();
                foreach (var entry in allocation.Properties()) if (!ids.Contains(entry.Name)) throw new ArgumentException("Unknown level attribute " + entry.Name);
                var counts = allocation.Properties().ToDictionary(p => p.Name,p => Count(p.Value,"allocated levels"));
                var amount = counts.Values.Aggregate(0,(sum,n) => checked(sum+n)); if (amount == 0) return false;
                if (amount > (int)LevelBudget(next)["levels"]) return false;
                if (_reconcile == null || !(next["playerProjectionRules"]?["baseRules"] is JObject)) throw new InvalidOperationException("Level-up requires the run's frozen player projection.");
                var paid = new JArray(); var startingCinders = Count(next["cinders"],"cinders");
                foreach (var id in ids) for (var n = 0; n < (counts.TryGetValue(id,out var count) ? count : 0); n++)
                {
                    var plan = LevelPlan(next,1); if (!(bool)plan["offerable"]) throw new InvalidOperationException("Level budget changed during allocation.");
                    next["cinders"] = (int)next["cinders"] - (int)plan["cost"];
                    next["attributes"][id] = checked(Count(next["attributes"][id],"attribute")+1);
                    next["levelUps"] = checked((int)plan["levelsTaken"]+1); next["levelPoints"] = checked(Count(next["levelPoints"],"granted level points")+1);
                    _reconcile(next); paid.Add(new JObject { ["attributeId"] = id, ["cost"] = plan["cost"].DeepClone(), ["level"] = next["levelUps"].DeepClone() });
                }
                next["lastServiceReceipt"] = new JObject { ["service"] = service, ["purchases"] = paid, ["spent"] = startingCinders-(int)next["cinders"], ["points"] = amount };
            }
            else
            {
                var kind = (string)request["kind"]; var id = (string)request["id"]; var index = Count(request["index"],"sale index",-1);
                var sale = Sellables(next).FirstOrDefault(row => (string)row["kind"] == kind && (string)row["id"] == id && (int)row["index"] == index);
                if (sale == null) return false;
                if (kind == "relic" && (_reconcile == null || _syncFlaskGrowth == null)) throw new InvalidOperationException("Relic resale requires resource and flask reconciliation.");
                ((JArray)next[kind+"s"]).RemoveAt(index);
                if (kind == "relic")
                {
                    next["relicIds"] = next["relics"].DeepClone();
                    if (next["ownedItemRefs"] is JArray owned) foreach (var item in owned.Where(x => (string)x == "relic/"+id).ToArray()) item.Remove();
                    _syncFlaskGrowth(next); _reconcile(next);
                }
                next["cinders"] = checked(Count(next["cinders"],"cinders") + (int)sale["price"]);
                next["lastServiceReceipt"] = new JObject { ["service"] = service, ["kind"] = kind, ["id"] = id, ["index"] = index, ["earned"] = sale["price"].DeepClone() };
            }
            run.RemoveAll(); foreach (var property in next.Properties()) run.Add(property.Name,property.Value.DeepClone()); return true;
        }
    }
}
