// OriginalCombatEquipment.cs — atomic prepared-set swaps against frozen run content.
// Apply returns isolated run/combat drafts; the game owner restores them together,
// emits returned events through CombatSession, and optionally ends the turn.
// Author cost rules, incoming category tags, allowance and turn-ending in balance.
// Original combat opens only occupied carried sets, independent of profile unlocks.
// Every live pile rebinds stable weapon cards; sealed/removed cards stay outside it.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCombatEquipment
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _mechanics, _cfg;
        public OriginalCombatEquipment(OriginalContentCatalog catalog, JObject mechanics)
        { _catalog = catalog; _mechanics = (JObject)mechanics.DeepClone(); _cfg = (JObject)catalog.Data()["balance"]["equipment"]; }
        private static string ClassId(JObject run) => (string)run["classId"] ?? (string)run["class"];
        private static int Number(JToken token, string label, int fallback = 0)
        { if (token == null) return fallback; var n = (double)token; if (double.IsNaN(n) || double.IsInfinity(n) || n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new ArgumentException("Invalid equipment integer: " + label); return (int)n; }
        public int OpenedSets(JObject run, string slotId)
        {
            var slot = _catalog.Record("equipment.slots", slotId); var ids = run["loadout"]?["sets"]?[slotId] as JArray ?? throw new ArgumentException("Missing carried equipment sets.");
            var opened = 1; for (var i = 0; i < ids.Count; i++) if (!string.IsNullOrEmpty((string)ids[i])) opened = i + 1;
            return Math.Min(Math.Max(1, Number(slot["sets"], "set capacity", 1)), opened);
        }
        private JObject Rule(JObject run, JObject battle)
        {
            if (battle["player"]?["equipmentSwapRule"] is JObject frozen) return (JObject)frozen.DeepClone();
            var rules = _cfg["swapCostRules"] as JArray ?? new JArray(); var wanted = (string)run["profileMeta"]?["settings"]?["swapCostRule"];
            return (JObject)(rules.FirstOrDefault(r => (string)r["id"] == wanted) ?? rules.FirstOrDefault(r => (string)r["id"] == (string)_cfg["swapCostRule"]))?.DeepClone();
        }
        public JObject Price(JObject run, JObject battle, string slotId, int setIndex)
        {
            var rule = Rule(run, battle); var basis = new[] { "default", "category" }.Contains((string)rule?["base"]) ? (string)rule["base"] : "default";
            var baseCost = Number(_cfg["swapCost"], "default cost"); string category = null;
            if (basis == "category")
            {
                var ids = run["loadout"]?["sets"]?[slotId] as JArray; var id = ids != null && setIndex >= 0 && setIndex < ids.Count ? (string)ids[setIndex] : null;
                var piece = id == null ? null : _catalog.Table("equipment.armaments").FirstOrDefault(p => (string)p["id"] == id);
                var tags = piece == null ? Array.Empty<string>() : _catalog.Tags("armament", (JObject)piece);
                var hit = (_cfg["swapCostByCategory"] as JArray ?? new JArray()).FirstOrDefault(r => tags.Contains((string)r["tag"]));
                if (hit != null) { category = (string)hit["tag"]; baseCost = Number(hit["cost"], "category cost", baseCost); }
            }
            var delta = Number(new EquipmentRunModifiers(_catalog).Resolve((JObject)run["loadout"], ClassId(run))["swapCostDelta"], "worn swap modifier");
            var player = (JObject)battle["player"]; var upgrades = new ItemUpgradeService(_catalog);
            foreach (var relicId in (player["relicIds"] as JArray ?? new JArray()).Values<string>())
            { var relic = upgrades.ResolveItem("relic/" + relicId, Number(player["itemUpgradeLevels"]?["relic/" + relicId], "relic tier")); delta = checked(delta + Number(relic["passives"]?["swapCostDelta"], "relic swap modifier")); }
            var gear = (bool?)rule?["gear"] == true; var raw = checked(baseCost + (gear ? delta : 0));
            return new JObject { ["cost"] = Math.Max(0, raw), ["ruleId"] = rule?["id"]?.DeepClone(), ["base"] = basis, ["baseCost"] = baseCost, ["categoryTag"] = category, ["gearOn"] = gear, ["gearDelta"] = gear ? delta : 0, ["gearIgnored"] = gear ? 0 : delta, ["floored"] = raw < 0 };
        }
        public int SwapsLeft(JObject battle)
        { return (string)_cfg["swapCostKind"] != "allowance" ? 0 : Number(battle["player"]?["equipmentSwapTurn"], "swap turn", -1) == Number(battle["turn"], "turn") ? Number(battle["player"]["equipmentSwapsLeft"], "swaps left") : Math.Max(0, Number(_cfg["swapAllowancePerTurn"], "swap allowance")); }
        public JObject Apply(JObject run, JObject battle, string slotId, int setIndex)
        {
            if ((string)battle["phase"] != "player" || !string.IsNullOrEmpty((string)battle["result"])) throw new InvalidOperationException("Equipment swaps require an active player turn.");
            if ((bool?)_cfg["enabled"] != true) throw new InvalidOperationException("Equipment is disabled.");
            var slot = _catalog.Record("equipment.slots", slotId); if ((string)slot["swap"] != "combat") throw new ArgumentException((string)slot["label"] + " stays fastened until the fight ends.");
            var before = (JObject)run["loadout"]; var ids = before["sets"]?[slotId] as JArray;
            if (ids == null || setIndex < 0 || setIndex >= ids.Count || setIndex >= OpenedSets(run, slotId)) throw new ArgumentException("That prepared set is unavailable in combat.");
            var price = Price(run, battle, slotId, setIndex); var allowance = (string)_cfg["swapCostKind"] == "allowance";
            if (allowance ? SwapsLeft(battle) < 1 : Number(battle["player"]?["energy"], "actions") < (int)price["cost"]) throw new ArgumentException(allowance ? "No equipment changes left this turn." : "Swapping costs " + price["cost"] + " actions.");
            var nextRun = (JObject)run.DeepClone(); var nextBattle = (JObject)battle.DeepClone(); var after = (JObject)nextRun["loadout"]; after["active"][slotId] = setIndex;
            var composer = new WeaponCardComposer(_catalog); _ = composer.BuildAttackPlan(after, ClassId(run)); // Reject incompatible two-hand layouts before spending.
            var beforeMods = new EquipmentRunModifiers(_catalog).Resolve(before, ClassId(run)); var afterMods = new EquipmentRunModifiers(_catalog).Resolve(after, ClassId(run));
            var player = (JObject)nextBattle["player"];
            var deficits = (JObject)(player["equipmentPoolDeficits"] ?? run["equipmentPoolDeficits"] ?? new JObject()).DeepClone();
            if (Number(before["active"]?[slotId], "active set") != setIndex)
            {
                foreach (var resource in new[] { "hp", "mana", "stamina" })
                {
                    var max = "max" + char.ToUpperInvariant(resource[0]) + resource.Substring(1); var oldMax = Number(player[max], max); var newMax = Math.Max(resource == "hp" ? 1 : 0, checked(oldMax + Number(afterMods[max], max) - Number(beforeMods[max], max)));
                    var moved = EquipmentRunModifiers.MovePool(oldMax, Number(player[resource], resource), newMax, deficits[resource] == null ? (int?)null : Number(deficits[resource], "pool deficit")); player[max] = newMax; player[resource] = moved["current"].DeepClone(); deficits[resource] = moved["deficit"].DeepClone();
                }
            }
            player["equipmentPoolDeficits"] = deficits; nextRun["equipmentPoolDeficits"] = deficits.DeepClone();
            player["equipmentSwapTurn"] = battle["turn"].DeepClone(); player["equipmentSwapsLeft"] = allowance ? SwapsLeft(battle) - 1 : 0;
            if (!allowance) player["energy"] = checked(Number(player["energy"], "actions") - (int)price["cost"]);
            var rule = Rule(run, battle); if (rule != null) player["equipmentSwapRule"] = rule;
            var mounts = nextRun["itemMounts"] as JObject; var quota = Number(nextRun["equipmentAttackSlotCount"], "birth attack quota", ((JArray)nextRun["deck"]).Count(c => (string)c["equipmentRole"] == "attack"));
            nextRun["deck"] = composer.Recompose((JArray)nextRun["deck"], after, ClassId(run), mounts);
            nextBattle["piles"] = composer.ReconcileCombat((JObject)battle["piles"], after, ClassId(run), quota, mounts);
            var upgradesService = new ItemUpgradeService(_catalog); upgradesService.RestampCards(nextRun);
            foreach (var pile in new[] { "hand", "draw", "discard", "exhaust" })
            { var carrier = (JObject)nextRun.DeepClone(); carrier["deck"] = nextBattle["piles"][pile].DeepClone(); upgradesService.RestampCards(carrier); nextBattle["piles"][pile] = carrier["deck"].DeepClone(); }
            var projection = new OriginalPlayerProjection(_catalog, _mechanics).Preview(nextRun); nextBattle["weights"] = projection["weights"].DeepClone();
            foreach (var key in new[] { "weights", "weight", "poiseThreshold" }) nextRun[key] = projection[key].DeepClone();
            player["poiseThreshold"] = projection["poiseThreshold"].DeepClone();
            if (player["poiseMeter"] is JObject meter) meter["max"] = projection["poiseThreshold"].DeepClone();
            foreach (var key in new[] { "hp", "maxHp", "mana", "maxMana", "stamina", "maxStamina" }) nextRun[key] = player[key].DeepClone();
            nextRun["equipmentPoolBonuses"] = new JObject { ["maxHp"] = afterMods["maxHp"].DeepClone(), ["maxMana"] = afterMods["maxMana"].DeepClone(), ["maxStamina"] = afterMods["maxStamina"].DeepClone() };
            var events = new JArray(Changed(before, after), new JObject { ["type"] = "armamentSwapped", ["slotId"] = slotId, ["setIndex"] = setIndex, ["cost"] = price["cost"].DeepClone(), ["rule"] = price["ruleId"]?.DeepClone() });
            return new JObject { ["run"] = nextRun, ["combat"] = nextBattle, ["receipt"] = price, ["events"] = events, ["endsTurn"] = (bool?)_cfg["swapEndsTurn"] == true };
        }
        private static string Signature(JObject loadout) => string.Join("|", ((JObject)loadout["sets"]).Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + ":" + ((string)p.Value[Number(loadout["active"]?[p.Name], "active set")] ?? "-")));
        private static JObject Changed(JObject before, JObject after)
        {
            var positions = new JArray();
            foreach (var slot in ((JObject)before["sets"]).Properties().Select(p => p.Name).Union(((JObject)after["sets"]).Properties().Select(p => p.Name)).OrderBy(s => s, StringComparer.Ordinal))
            {
                var from = before["sets"][slot] as JArray ?? new JArray(); var to = after["sets"][slot] as JArray ?? new JArray();
                for (var i = 0; i < Math.Max(from.Count, to.Count); i++)
                { var a = i < from.Count ? (string)from[i] : null; var b = i < to.Count ? (string)to[i] : null; var activeA = Number(before["active"]?[slot], "active set") == i; var activeB = Number(after["active"]?[slot], "active set") == i; if (a == b && activeA == activeB) continue; positions.Add(new JObject { ["slotId"] = slot, ["setIndex"] = i, ["beforeItemId"] = a, ["afterItemId"] = b, ["beforeActive"] = activeA, ["afterActive"] = activeB }); }
            }
            return new JObject { ["type"] = "equipmentChanged", ["reason"] = "swapSet", ["beforeLoadoutSignature"] = Signature(before), ["afterLoadoutSignature"] = Signature(after), ["changedPositions"] = positions };
        }
    }
}


