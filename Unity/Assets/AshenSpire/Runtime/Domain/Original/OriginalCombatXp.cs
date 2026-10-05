// Published combat-power and cumulative-floor XP receipts. Pure calculation;
// the run transaction owns paying this receipt once at battle completion.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCombatXp
    {
        private readonly Dictionary<string, JObject> _enemies;
        private readonly JObject _awards;
        public OriginalCombatXp(JObject content)
        {
            _enemies = (content["enemies"] as JArray ?? new JArray()).OfType<JObject>()
                .ToDictionary(e => (string)e["id"], e => (JObject)e.DeepClone(), StringComparer.Ordinal);
            _awards = (JObject)(content["balance"]?["xp"] as JObject ?? new JObject()).DeepClone();
        }
        private static bool Finite(JToken token) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) && !double.IsNaN((double)token) && !double.IsInfinity((double)token);
        private static double Number(JToken token, double fallback = 0) => Finite(token) ? (double)token : fallback;
        private static double Level(JObject enemy)
        {
            var value = Number(enemy?["level"]);
            return value > 0 && value <= 9007199254740991d && value == Math.Floor(value) ? value : 1;
        }
        private JObject Definition(JObject enemy) => enemy?["enemyId"]?.Type == JTokenType.String && _enemies.TryGetValue((string)enemy["enemyId"], out var definition) ? definition : null;
        public double Power(JObject enemy)
        {
            var definition = Definition(enemy);
            if (definition == null) return Finite(enemy?["combatPower"]) && (double)enemy["combatPower"] >= 0 ? (double)enemy["combatPower"] : 3;
            var hp = Number(enemy["maxHp"], (Number(definition["hp"]?[0]) + Number(definition["hp"]?[1])) / 2);
            var poise = Number(enemy["poiseMeter"]?["max"], Number(definition["poiseMax"]));
            var attack = 0d;
            foreach (var move in (definition["moves"] as JObject ?? new JObject()).Properties().Select(p => p.Value).Where(m => (string)m["intent"] == "attack"))
            {
                var hits = Number(move["hits"], 1); if (hits == 0) hits = 1;
                attack = Math.Max(attack, Number(move["damage"]) * hits);
            }
            var stats = (hp / 24 + poise / 10 + attack * Number(enemy["damageMult"], 1) / 8) / 3;
            return 1 + Level(enemy) / 5 + stats + Number(definition["equipmentPower"]);
        }
        public JObject Receipt(JObject options)
        {
            var victory = (bool?)options["victory"] == true; var pool = (string)options["pool"] ?? "normal";
            var kill = _awards["kill"] as JObject ?? new JObject();
            var perKill = Number(kill[pool], Number(kill["normal"]));
            var levelMult = Number(_awards["killLevelMultiplier"], 1); if (levelMult < 0) levelMult = 1;
            var powerMult = Number(_awards["combatPowerMultiplier"], .2); if (powerMult < 0) powerMult = .2;
            List<JObject> defeated;
            if (options["enemies"] is JArray enemies)
                defeated = enemies.OfType<JObject>().Where(e => (bool?)e["alive"] == false || Finite(e["hp"]) && (double)e["hp"] <= 0).ToList();
            else
            {
                var kills = Number(options["kills"]);
                if (kills > 10000) throw new ArgumentException("Combat XP enemy count exceeds action bound.");
                var count = kills > 0 && kills == Math.Floor(kills) ? (int)kills : 0;
                defeated = Enumerable.Range(0, count).Select(_ => new JObject { ["level"] = 1, ["combatPower"] = 3 }).ToList();
            }
            var raw = (victory ? defeated.Sum(Power) : 0) * powerMult * Number(_awards["combatWin"]);
            var subtotal = 0d; var rows = new JArray();
            if (victory) { subtotal = Math.Floor(raw + 1e-9); rows.Add(new JObject { ["kind"] = "power", ["amount"] = subtotal }); }
            for (var i = 0; i < defeated.Count; i++)
            {
                var enemy = defeated[i]; var level = Level(enemy); raw += perKill * levelMult * level;
                var next = Math.Floor(raw + 1e-9);
                rows.Add(new JObject { ["kind"] = "enemy", ["enemyId"] = enemy["enemyId"]?.DeepClone() ?? JValue.CreateNull(),
                    ["name"] = Definition(enemy)?["name"]?.DeepClone() ?? new JValue("Enemy " + (i + 1)), ["level"] = level, ["amount"] = next - subtotal });
                subtotal = next;
            }
            var multiplier = Number(options["characterMultiplier"], 1); if (multiplier < 0) multiplier = 1;
            var total = Math.Floor(subtotal * multiplier);
            if (total != subtotal) rows.Add(new JObject { ["kind"] = "bonus", ["amount"] = total - subtotal });
            return new JObject { ["total"] = total, ["rows"] = rows };
        }
    }
}
