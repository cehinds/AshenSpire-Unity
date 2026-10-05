using System;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private bool UsesTurnStamina => OriginalTurnStamina.Enabled(_mechanics);
        private void WriteEnergy(int value)
        {
            if (UsesTurnStamina) new OriginalTurnStamina(_player).Write("energy", value);
            else _player["energy"] = value;
        }
        private void ValidateResources()
        {
            if (UsesTurnStamina) OriginalTurnStamina.Validate(_player);
            else Wallet();
        }
        private int PayCard(JObject profile, int cost)
        {
            if (!UsesTurnStamina)
            {
                var wallet = Wallet();
                if (!wallet.TryPayProfile(profile, true, (bool)profile["variable"] ? (int?)cost : null))
                    throw new ArgumentException("Insufficient actions, mana or stamina.");
                CopyWallet(wallet);
                return (int)profile["stamina"];
            }
            var draft = new JObject { ["stamina"] = _player["stamina"], ["mana"] = _player["mana"],
                ["staminaSpentThisTurn"] = Counter("staminaSpentThisTurn") };
            var paid = OriginalCardPayment.TryPay(draft, profile);
            if (paid == null) throw new ArgumentException("Insufficient Stamina or Mana.");
            WriteEnergy((int)draft["stamina"]); _player["mana"] = draft["mana"];
            _player["counters"]["staminaSpentThisTurn"] = draft["staminaSpentThisTurn"];
            return (int)paid["staminaSpent"];
        }
        private void BeginResources(bool coop)
        {
            if (UsesTurnStamina)
            {
                var loss = CardMechanics.Nonnegative(_player["pendingActionLoss"] ?? 0, "pending action loss");
                WriteEnergy(Math.Max(0, (int)_player["maxStamina"] - loss));
                _player["pendingActionLoss"] = 0;
                _player["counters"]["staminaSpentThisTurn"] = 0;
            }
            else if (coop) _player["energy"] = _player["energyMax"].DeepClone();
            else { var wallet = Wallet(); wallet.BeginTurn((int)_player["energyMax"]); CopyWallet(wallet); }
        }
        private void EndResources(bool coop)
        {
            if (UsesTurnStamina) { _player["counters"]["staminaSpentThisTurn"] = 0; return; }
            var wallet = Wallet(); var before = (int)_player["stamina"]; wallet.EndTurn(); CopyWallet(wallet);
            if ((int)_player["stamina"] == before) return;
            var payload = new JObject { ["amount"] = (int)_player["stamina"] - before, ["reason"] = "idle" };
            if (coop) payload["playerId"] = _coopSeatKey;
            Emit("staminaRecovered", payload);
        }
        internal void CoopBeginResources() => BeginResources(true);
    }
}
