// WeightSystem.cs — authored carry/load thresholds and deterministic dodge checks.
// Pass content/framework/mechanics.json and the actual equipped weights. Attribute
// scaling follows the original here; owner-approved progression belongs in separate data.
// The caller owns random streams and pays the returned cost before applying dodge effects.
using System;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class WeightSystem
    {
        private readonly JObject _weight, _dodge;
        public WeightSystem(JObject mechanics)
        {
            _weight = (JObject)mechanics?["weight"]?.DeepClone() ?? throw new ArgumentException("Missing weight mechanics.");
            _dodge = (JObject)mechanics?["dodgeRoll"]?.DeepClone() ?? throw new ArgumentException("Missing dodge mechanics.");
            foreach (var key in new[] { "capacityBase", "capacityPerConstitution", "capacityPerStrength" }) CardMechanics.Nonnegative(_weight[key], key);
            var classes = _weight["classes"] as JArray;
            if (classes == null || classes.Count == 0) throw new ArgumentException("Weight classes cannot be empty.");
            var previous = -1; var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var row in classes)
            {
                var id = (string)row["id"]; if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("Missing or duplicate weight class.");
                var limit = CardMechanics.Nonnegative(row["maxLoadPercent"], "weight threshold"); if (limit <= previous) throw new ArgumentException("Weight thresholds must increase."); previous = limit;
                foreach (var key in new[] { "dodgeStaminaCost", "dodgeActionCost", "extraEligibleDexterityCards", "extraPhysicalAttacks" }) CardMechanics.Nonnegative(row[key], key);
                foreach (var key in new[] { "evasionModifier", "temporaryGuardModifier" }) CardMechanics.Integer(row[key], key);
            }
            if (CardMechanics.Nonnegative(_dodge["die"], "dodge die") == 0) throw new ArgumentException("Dodge die must have a side.");
            CardMechanics.Integer(_dodge["baseDifficulty"], "dodge difficulty"); CardMechanics.Integer(_dodge["temporaryGuardBase"], "dodge guard");
        }
        public int DodgeDie => (int)_dodge["die"];
        public JObject Compute(int constitution, int strength, JObject weights, int bonuses = 0)
        {
            if (constitution < 0 || strength < 0) throw new ArgumentException("Attributes cannot be negative.");
            var capacity = checked((int)_weight["capacityBase"] + (int)_weight["capacityPerConstitution"] * constitution + (int)_weight["capacityPerStrength"] * strength + bonuses);
            if (capacity < 0) throw new ArgumentException("Carry capacity cannot be negative.");
            var load = 0;
            foreach (var key in new[] { "mainHandWeight", "offHandWeight", "armorWeight", "otherCountedWeight" }) load = checked(load + (weights?[key] == null ? 0 : CardMechanics.Nonnegative(weights[key], key)));
            var percent = checked((int)Math.Floor(100d * load / Math.Max(1, capacity)));
            var classes = (JArray)_weight["classes"]; JToken selected = classes[classes.Count - 1];
            foreach (var row in classes) if (percent <= (int)row["maxLoadPercent"]) { selected = row; break; }
            return new JObject { ["capacity"] = capacity, ["load"] = load, ["percent"] = percent, ["weightClass"] = selected.DeepClone() };
        }
        public static int AttributeModifier(int score) => checked((int)Math.Floor(((double)score - 10) / 2));
        public JObject Dodge(int roll, int dexterity, JObject weightClass, int otherEvasionModifiers = 0, int? baseDifficulty = null, int sourceCombatantModifier = 0, int incomingAttackModifier = 0)
        {
            if (roll < 1 || roll > DodgeDie) throw new ArgumentException("Dodge roll is outside the die.");
            if (dexterity < 0) throw new ArgumentException("Dexterity cannot be negative.");
            var modifier = AttributeModifier(dexterity);
            var check = checked(roll + modifier + CardMechanics.Integer(weightClass?["evasionModifier"], "evasion modifier") + otherEvasionModifiers);
            var difficulty = checked((baseDifficulty ?? (int)_dodge["baseDifficulty"]) + sourceCombatantModifier + incomingAttackModifier);
            var success = check > difficulty;
            var guard = checked((int)_dodge["temporaryGuardBase"] + modifier + CardMechanics.Integer(weightClass?["temporaryGuardModifier"], "guard modifier"));
            return new JObject { ["check"] = check, ["difficulty"] = difficulty, ["success"] = success, ["temporaryGuard"] = success ? guard : 0, ["cost"] = new JObject { ["stamina"] = CardMechanics.Nonnegative(weightClass?["dodgeStaminaCost"], "dodge stamina"), ["actions"] = CardMechanics.Nonnegative(weightClass?["dodgeActionCost"], "dodge actions") } };
        }
    }
}
