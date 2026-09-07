// FlaskChargePool.cs — original shared Crimson/Azure charge ledger.
// Reallocation is an atomic shrine command and refills the selected split.
// Base capacity belongs to this run; do not reconstruct it from current balance.
using System;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class FlaskChargePool
    {
        private readonly JObject _state;
        public FlaskChargePool(int capacity, int health, int mana)
        {
            ValidateAllocation(capacity, health, mana);
            _state = new JObject { ["capacity"] = capacity, ["base"] = capacity, ["hp"] = health, ["mana"] = mana,
                ["hpCurrent"] = health, ["manaCurrent"] = mana, ["grown"] = new JObject { ["hp"] = 0, ["mana"] = 0 }, ["granted"] = 0 };
        }
        public FlaskChargePool(JObject snapshot)
        {
            _state = (JObject)snapshot.DeepClone();
            ValidateAllocation((int)_state["capacity"], (int)_state["hp"], (int)_state["mana"]);
            if ((int)_state["capacity"] != (int)_state["base"] + (int)_state["grown"]["hp"] + (int)_state["grown"]["mana"] + (int)_state["granted"])
                throw new ArgumentException("Flask capacity does not match its ledger.");
            foreach (var kind in new[] { "hp", "mana" }) if ((int)_state[kind + "Current"] < 0 || (int)_state[kind + "Current"] > (int)_state[kind]) throw new ArgumentException("Invalid remaining flask charges.");
        }
        public JObject Snapshot() => (JObject)_state.DeepClone();
        public void Reallocate(int health, int mana)
        {
            ValidateAllocation((int)_state["capacity"], health, mana);
            _state["hp"] = health; _state["mana"] = mana; Refill();
        }
        public void Refill() { _state["hpCurrent"] = _state["hp"].DeepClone(); _state["manaCurrent"] = _state["mana"].DeepClone(); }
        public bool Spend(string kind)
        {
            if (kind != "hp" && kind != "mana") throw new ArgumentException("Unknown charge kind: " + kind);
            var count = (int)_state[kind + "Current"];
            if (count == 0) return false;
            _state[kind + "Current"] = count - 1; return true;
        }
        private static void ValidateAllocation(int capacity, int health, int mana)
        {
            if (capacity <= 0 || health < 0 || mana < 0 || (long)health + mana != capacity) throw new ArgumentException("Flask allocation must equal capacity.");
        }
    }
}
