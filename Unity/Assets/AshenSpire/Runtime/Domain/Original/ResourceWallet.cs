// ResourceWallet.cs — atomic action/mana/stamina costs and persistent spend accounting.
// Supply mana/stamina capacities from derived stats; this class does not calculate them.
// Recovery values come from the mechanics policy. Original defaults are zero natural
// mana recovery and one stamina after an idle turn; designers can tune that policy.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class ResourceWallet
    {
        private int _actions, _mana, _stamina, _spent;
        private readonly int _maxMana, _maxStamina, _manaRecovery, _idleRecovery;
        private readonly bool _refundErasesSpend;
        public ResourceWallet(int actions, int maxMana, int maxStamina, JObject mechanics, int? mana = null, int? stamina = null)
        {
            RequireNonnegative(actions, nameof(actions)); RequireNonnegative(maxMana, nameof(maxMana)); RequireNonnegative(maxStamina, nameof(maxStamina));
            _actions = actions; _maxMana = maxMana; _maxStamina = maxStamina; _mana = mana ?? maxMana; _stamina = stamina ?? maxStamina;
            if (_mana < 0 || _mana > _maxMana || _stamina < 0 || _stamina > _maxStamina) throw new ArgumentException("Resources exceed their capacities.");
            _manaRecovery = CardMechanics.Nonnegative(mechanics?["mana"]?["naturalRecoveryPerTurn"], "natural mana recovery");
            _idleRecovery = CardMechanics.Nonnegative(mechanics?["stamina"]?["idleRecoveryPerTurn"], "idle stamina recovery");
            var refund = mechanics?["stamina"]?["refundErasesSpend"];
            if (refund?.Type != JTokenType.Boolean) throw new ArgumentException("Missing refund spend policy.");
            _refundErasesSpend = (bool)refund;
        }
        public ResourceWallet(JObject snapshot, JObject mechanics) : this(Read(snapshot, "action"), Read(snapshot, "maxMana"), Read(snapshot, "maxStamina"), mechanics, Read(snapshot, "mana"), Read(snapshot, "stamina"))
        { _spent = Read(snapshot, "staminaSpentThisTurn"); }
        public JObject Snapshot() => new JObject { ["action"] = _actions, ["mana"] = _mana, ["stamina"] = _stamina, ["maxMana"] = _maxMana, ["maxStamina"] = _maxStamina, ["staminaSpentThisTurn"] = _spent };
        public bool CanPay(JArray entries)
        {
            var totals = TotalCosts(entries);
            return totals["action"] <= _actions && totals["mana"] <= _mana && totals["stamina"] <= _stamina && (long)_spent + totals["stamina"] <= int.MaxValue;
        }
        // Cancellation/target validation belongs before this command. No callback is
        // invoked during commit, so re-entrant callbacks cannot partially spend a wallet.
        public bool TryPay(JArray entries, bool targetConfirmed = true)
        {
            var totals = TotalCosts(entries);
            if (!targetConfirmed || totals["action"] > _actions || totals["mana"] > _mana || totals["stamina"] > _stamina || (long)_spent + totals["stamina"] > int.MaxValue) return false;
            _actions -= totals["action"]; _mana -= totals["mana"]; _stamina -= totals["stamina"]; _spent += totals["stamina"]; return true;
        }
        public bool TryPayProfile(JObject profile, bool targetConfirmed = true, int? variableActionAmount = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var variable = (bool?)profile["variable"] ?? false;
            var action = CardMechanics.Nonnegative(profile["action"], "action cost");
            if (variable) { if (!variableActionAmount.HasValue) throw new ArgumentException("X cost requires its committed action amount."); RequireNonnegative(variableActionAmount.Value, nameof(variableActionAmount)); action = variableActionAmount.Value; }
            else if (variableActionAmount.HasValue) throw new ArgumentException("Fixed cost cannot receive an X amount.");
            return TryPay(new JArray(new JObject { ["resource"] = "action", ["amount"] = action }, new JObject { ["resource"] = "mana", ["amount"] = CardMechanics.Nonnegative(profile["mana"], "mana cost") }, new JObject { ["resource"] = "stamina", ["amount"] = CardMechanics.Nonnegative(profile["stamina"], "stamina cost") }), targetConfirmed);
        }
        public bool SpendStamina(int amount) { RequireNonnegative(amount, nameof(amount)); return TryPay(new JArray(new JObject { ["resource"] = "stamina", ["amount"] = amount })); }
        public void RefundStamina(int amount, bool? erasesSpend = null)
        { RequireNonnegative(amount, nameof(amount)); _stamina = SaturatingRecover(_stamina, _maxStamina, amount); if (erasesSpend ?? _refundErasesSpend) _spent = Math.Max(0, _spent - amount); }
        public void RecoverMana(int amount) { RequireNonnegative(amount, nameof(amount)); _mana = SaturatingRecover(_mana, _maxMana, amount); }
        public void RecoverStamina(int amount) { RequireNonnegative(amount, nameof(amount)); _stamina = SaturatingRecover(_stamina, _maxStamina, amount); }
        public void GainActions(int amount) { RequireNonnegative(amount, nameof(amount)); _actions = checked(_actions + amount); }
        public void BeginTurn(int actions) { RequireNonnegative(actions, nameof(actions)); _actions = actions; _mana = SaturatingRecover(_mana, _maxMana, _manaRecovery); }
        public void EndTurn() { if (_spent == 0) _stamina = SaturatingRecover(_stamina, _maxStamina, _idleRecovery); _spent = 0; }
        public void Rest() { _mana = _maxMana; }
        // Duplicate entries must be aggregated before checking affordability. This
        // deliberately closes the original alternative-cost double-charge loophole.
        public static Dictionary<string, int> TotalCosts(JArray entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var totals = new Dictionary<string, int>(StringComparer.Ordinal) { ["action"] = 0, ["mana"] = 0, ["stamina"] = 0 };
            foreach (var entry in entries)
            {
                var resource = (string)entry?["resource"];
                if (resource == null || !totals.ContainsKey(resource)) throw new ArgumentException("Unknown cost resource: " + resource);
                var amount = CardMechanics.Nonnegative(entry["amount"], "cost amount");
                totals[resource] = checked(totals[resource] + amount);
            }
            return totals;
        }
        private static int SaturatingRecover(int current, int maximum, int amount) => (int)Math.Min(maximum, (long)current + amount);
        private static int Read(JObject value, string name) => CardMechanics.Nonnegative(value?[name], name);
        private static void RequireNonnegative(int value, string name) { if (value < 0) throw new ArgumentOutOfRangeException(name); }
    }
}
