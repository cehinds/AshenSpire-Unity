// CombatSession.Flasks.cs — charge and utility flask commands use authored effect rows.
// Definition identity comes from flask kind; shared charges retain their save ledger.
// Validate targets/effects before consuming anything, then emit hooks and drain the queue.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        public JArray DrinkCharge(string kind,string targetId = null)
        {
            RequirePlayerTurn(); if (kind != "hp" && kind != "mana") throw new ArgumentException("Unknown flask charge kind: " + kind);
            var definition = _content.Table("flasks").OfType<JObject>().FirstOrDefault(f => FlaskKind(f) == kind) ?? throw new ArgumentException("No authored flask of kind: " + kind);
            var charges = _player["flaskCharges"] as JObject ?? throw new ArgumentException("No flask charge pool.");
            _ = new FlaskChargePool(charges); var remaining = (int)charges[kind+"Current"]; if (remaining <= 0) throw new ArgumentException("No remaining flask charges.");
            var target = FlaskTarget(definition,targetId); ValidateEffects(definition["effects"]); var start = _events.Count; charges[kind+"Current"] = remaining-1;
            UseFlask(definition,target,null); return Since(start);
        }
        public JArray DrinkFlask(int slot,string targetId = null)
        {
            RequirePlayerTurn(); var flasks = (JArray)_player["flasks"]; if (slot < 0 || slot >= flasks.Count) throw new ArgumentException("No flask in slot: " + slot);
            var definition = _content.Record("flasks",(string)flasks[slot]["flaskId"]); var target = FlaskTarget(definition,targetId); ValidateEffects(definition["effects"]);
            var start = _events.Count; flasks.RemoveAt(slot); UseFlask(definition,target,slot); return Since(start);
        }
        private JObject FlaskTarget(JObject definition,string targetId)
        {
            if (targetId != null) { if ((bool?)definition["targeted"] != true && targetId != "player") throw new ArgumentException("This flask cannot target an enemy."); var target = Find(targetId); if (!Alive(target)) throw new ArgumentException("Invalid flask target: " + targetId); return target; }
            return (bool?)definition["targeted"] == true ? _enemies.FirstOrDefault(Alive) : null;
        }
        private void UseFlask(JObject definition,JObject target,int? slot)
        {
            var payload = new JObject { ["flaskId"] = definition["id"], ["targetId"] = target?["id"] }; if (slot.HasValue) payload["slot"] = slot.Value;
            Emit("flaskUsed",payload); var multiplier = ((JArray)_player["relicIds"]).Aggregate(1d,(value,id) => value * ((double?)RelicDefinition((string)id)["passives"]?["flaskPowerMult"] ?? 1));
            QueueEffects(definition["effects"],_player,_player,target,multiplier == 1 ? null : new JObject { ["amountMult"] = multiplier }); Drain();
        }
        private static string FlaskKind(JObject definition)
        { if (definition["kind"] != null) return (string)definition["kind"]; var effects = definition["effects"] as JArray ?? new JArray(); return effects.Any(e => (string)e["op"] == "restoreMana") ? "mana" : effects.Any(e => (string)e["op"] == "heal") ? "hp" : "utility"; }
        public JObject RandomSnapshot() => new JObject { ["seed"] = _random.Seed, ["counters"] = JObject.FromObject(_random.Snapshot()) };
    }
}
