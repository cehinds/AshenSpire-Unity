using System;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    // JObject cannot install JavaScript accessors. All current-rules pool
    // writes must use this boundary so serialized compatibility fields agree.
    public sealed class OriginalTurnStamina
    {
        public static bool Enabled(JObject mechanics)
        {
            var flag = mechanics?["stamina"]?["turnBudget"];
            if (flag == null) return false;
            if (flag.Type != JTokenType.Boolean) throw new ArgumentException("turnBudget must be a boolean.");
            return (bool)flag;
        }
        public static void Validate(JObject player)
        {
            foreach (var key in new[] { "energy", "energyMax", "stamina", "maxStamina", "mana", "maxMana" })
                CardMechanics.Nonnegative(player?[key], key);
            if ((int)player["energy"] != (int)player["stamina"] || (int)player["energyMax"] != (int)player["maxStamina"])
                throw new ArgumentException("Saved turn-budget aliases disagree.");
            if ((int)player["mana"] > (int)player["maxMana"]) throw new ArgumentException("Mana exceeds its capacity.");
            // gainEnergy may temporarily exceed the turn's base capacity.
            CardMechanics.Nonnegative(player["counters"]?["staminaSpentThisTurn"] ?? 0, "Stamina spend counter");
        }
        private readonly JObject _player;
        public OriginalTurnStamina(JObject player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _player["energy"] = _player["stamina"]?.DeepClone();
            _player["energyMax"] = _player["maxStamina"]?.DeepClone();
        }
        public void Write(string key, JToken value)
        {
            if (key == "energy" || key == "stamina") { _player["energy"] = value?.DeepClone(); _player["stamina"] = value?.DeepClone(); }
            else if (key == "energyMax" || key == "maxStamina") { _player["energyMax"] = value?.DeepClone(); _player["maxStamina"] = value?.DeepClone(); }
            else throw new ArgumentException("Unknown turn-budget field: " + key);
        }
        public JObject Snapshot() => (JObject)_player.DeepClone();
    }
}
