// StatusSystem.cs — original stack, duration, meter, proc and resistance semantics.
// Content owns every named status and its effects. This component only interprets
// stackMode/decay/meter/proc fields; new Bleed-like statuses need data, not ID branches.
// Apply queues on-fill effects. The command owner drains them after event dispatch.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class StatusSystem
    {
        private readonly CombatContext _context;
        private readonly Dictionary<string, JObject> _definitions;
        private readonly Dictionary<string, JObject> _stances;
        public StatusSystem(CombatContext context)
        {
            _context = context;
            _definitions = context.Content.Table("statuses").OfType<JObject>().ToDictionary(x => (string)x["id"]);
            _stances = context.Content.Table("stances").OfType<JObject>().ToDictionary(x => (string)x["id"]);
        }
        public JObject Definition(string id) => _definitions.TryGetValue(id, out var value) ? (JObject)value.DeepClone() : throw new ArgumentException("Unknown status: " + id);
        public static int Stacks(JObject entity, string id) => (int?)(entity?["statuses"]?[id]?["meter"]?["value"] ?? entity?["statuses"]?[id]?["stacks"]) ?? 0;
        public void Apply(JObject target, string id, int amount = 1, JObject source = null)
        {
            var definition = Definition(id);
            if (target == null || (bool?)target["alive"] != true) return;
            if (amount <= 0 && (string)definition["stackMode"] != "unique") return;
            var statuses = (JObject)target["statuses"];
            if (definition["proc"] is JObject)
            {
                var blocked = 0;
                foreach (var other in statuses.Properties())
                {
                    if (Stacks(target, other.Name) <= 0) continue;
                    var resistance = Definition(other.Name)["resists"];
                    if ((string)resistance?["status"] == id) blocked += (int)Math.Ceiling(amount * (double)resistance["percent"] / 100);
                }
                if (blocked > 0)
                {
                    blocked = Math.Min(blocked, amount); amount -= blocked;
                    _context.Emit("procResisted", new JObject { ["targetId"] = target["id"], ["status"] = id, ["blocked"] = blocked, ["applied"] = amount });
                    if (amount <= 0) return;
                }
            }
            var instance = statuses[id] as JObject;
            if (instance == null)
            {
                instance = new JObject { ["stacks"] = 0 }; statuses[id] = instance;
                var maximum = definition["meter"]?["max"] ?? definition["proc"]?["threshold"];
                if (maximum != null) instance["meter"] = new JObject { ["value"] = 0, ["max"] = maximum.DeepClone() };
            }
            var holder = instance["meter"] as JObject ?? instance;
            var field = instance["meter"] != null ? "value" : "stacks";
            var old = (int)holder[field];
            switch ((string)definition["stackMode"])
            {
                case "add": holder[field] = checked(old + amount); break;
                case "refresh": holder[field] = Math.Max(old, amount); break;
                case "unique": holder[field] = instance["meter"] != null ? Math.Max(old, 1) : 1; break;
                default: throw new ArgumentException("Unknown stack mode: " + definition["stackMode"]);
            }
            if (definition["decay"] is JObject decay) instance["duration"] = decay["duration"].DeepClone();
            _context.Emit("statusApplied", new JObject { ["targetId"] = target["id"], ["sourceId"] = source?["id"], ["status"] = id, ["stacks"] = amount, ["total"] = Stacks(target, id) });
            if (definition["proc"] is JObject proc) Proc(target, id, proc, instance);
            else if (instance["meter"] is JObject meter)
            {
                var guard = 0;
                while ((int)meter["value"] >= (int)meter["max"])
                {
                    if (++guard > 100) throw new InvalidOperationException("Status meter fill loop did not terminate: " + id);
                    meter["value"] = (int)meter["value"] - (int)meter["max"];
                    _context.Emit("meterFilled", new JObject { ["targetId"] = target["id"], ["status"] = id, ["threshold"] = meter["max"] });
                    var growth = (double?)definition["meter"]["growthMult"] ?? 1;
                    if (growth != 1 && !AnyFlag("meterMaxGrowthDisabled")) meter["max"] = (int)Math.Ceiling((int)meter["max"] * growth);
                    foreach (var effect in definition["meter"]["onFill"] as JArray ?? new JArray()) Queue((JObject)effect, target);
                }
            }
        }
        private void Proc(JObject entity, string id, JObject proc, JObject instance)
        {
            var meter = (JObject)instance["meter"]; if ((int)meter["value"] < (int)meter["max"]) return;
            meter["value"] = 0;
            var burst = Math.Max((int)proc["burstMin"], Math.Min((int)proc["burstMax"], (int)Math.Floor((double)entity["maxHp"] * (double)proc["burstPercent"] / 100)));
            var poise = (int?)proc["poiseDamage"] ?? 0; var stagger = (bool?)proc["stagger"] ?? false;
            _context.Emit("procBurst", new JObject { ["targetId"] = entity["id"], ["status"] = id, ["amount"] = burst, ["threshold"] = meter["max"], ["poiseDamage"] = poise, ["stagger"] = stagger });
            Queue(new JObject { ["op"] = "loseHp", ["target"] = "self", ["amount"] = burst, ["cause"] = "proc:" + id }, entity);
            if ((string)entity["kind"] == "enemy")
            {
                if (poise > 0) Queue(new JObject { ["op"] = "poiseDamage", ["amount"] = poise }, entity);
                if (stagger) Queue(new JObject { ["op"] = "stagger" }, entity);
            }
            foreach (var effect in proc["effects"] as JArray ?? new JArray()) Queue((JObject)effect, entity);
            if (proc["resistance"] is JObject resistance && (string)entity["kind"] == "enemy")
            {
                var enemy = _context.Content.Record("enemies", (string)entity["enemyId"]);
                if (_context.Content.Tags("enemy", enemy).Intersect(resistance["tags"].Values<string>()).Any())
                    Queue(new JObject { ["op"] = "applyStatus", ["target"] = "self", ["status"] = resistance["status"], ["stacks"] = 1 }, entity);
            }
        }
        private void Queue(JObject effect, JObject entity) => _context.Enqueue(new CombatAction(effect, entity, entity, entity));
        public void Remove(JObject entity, string id, string reason = "removed")
        {
            if (!((JObject)entity["statuses"]).Remove(id)) return;
            _context.Emit("statusExpired", new JObject { ["targetId"] = entity["id"], ["status"] = id, ["reason"] = reason });
        }
        public void DecayAtTurnEnd(JObject entity)
        {
            foreach (var property in ((JObject)entity["statuses"]).Properties().ToArray())
            {
                var definition = Definition(property.Name); var instance = (JObject)property.Value;
                if (definition["decay"]?.Type == JTokenType.String && (string)definition["decay"] == "perTurnEnd")
                {
                    var holder = instance["meter"] as JObject ?? instance; var field = instance["meter"] == null ? "stacks" : "value";
                    holder[field] = (int)holder[field] - 1;
                    if (Stacks(entity, property.Name) <= 0) Remove(entity, property.Name, "decayed");
                }
                else if (definition["decay"] is JObject)
                {
                    instance["duration"] = (int)instance["duration"] - 1;
                    if ((int)instance["duration"] <= 0) Remove(entity, property.Name, "expired");
                }
            }
        }
        private IEnumerable<(JObject Modifiers, int Stacks)> Sources(JObject entity)
        {
            if (entity == null) yield break;
            foreach (var property in ((JObject)entity["statuses"]).Properties()) if (_definitions[property.Name]["modifiers"] is JObject modifiers) yield return (modifiers, Stacks(entity, property.Name));
            var stance = (string)entity["stanceId"];
            if ((string)entity["kind"] == "player" && !string.IsNullOrEmpty(stance) && _stances[stance]["modifiers"] is JObject stanceModifiers) yield return (stanceModifiers, 1);
        }
        public double Add(JObject entity, string key) => Sources(entity).Sum(x => ((double?)x.Modifiers[key] ?? 0) * x.Stacks);
        public double Multiply(JObject entity, string key) => Sources(entity).Aggregate(1d, (value, x) => value * ((double?)x.Modifiers[key] ?? 1));
        public bool Flag(JObject entity, string key) => Sources(entity).Any(x => x.Modifiers[key]?.Type == JTokenType.Boolean && (bool)x.Modifiers[key]);
        private bool AnyFlag(string key) => (_context.Player != null && (bool?)_context.Player["alive"] == true && Flag(_context.Player, key)) || _context.Enemies.Any(x => (bool?)x["alive"] == true && Flag(x, key));
    }
}
