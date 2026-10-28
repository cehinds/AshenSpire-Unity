// Core2RunFlasks.cs — a Core 2 run's flasks: the HP and MP charge flasks (a ledger of charges, refilled at rests) and
// utility flasks carried in potion slots.
//
// JS at the pin:
//   src/model/gracerefill.js  flaskKindOf / firstFlaskOfKind / chargeFlaskDefinition (the first heal flask is the HP
//                             charge, the first restoreMana flask the MP charge), flaskAcquisitionPlan / acquireFlask
//   src/engine/actions.js     useRunChargeFlask (a charge drunk on the map: its effects on the run, flaskPowerMult)
//   src/engine/combat.js      doUseFlask (in a fight: Core2Fight.UseFlask)
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2RunFlasks
    {
        /// <summary>JS chargeFlaskDefinition: the flask a charge kind (hp, mana) drinks.</summary>
        public static JObject ChargeFlask(Core2RunData data, string kind)
        {
            if (kind != "hp" && kind != "mana") throw new ArgumentException("A charge flask is hp or mana");
            var id = data.Content.Flasks.Select(f => f.Id).FirstOrDefault(fid => Core2RunRewards.ChargeKind(data, fid) == kind)
                ?? throw new InvalidOperationException("Missing authored '" + kind + "' charge flask definition");
            return data.Flask(id);
        }

        /// <summary>JS passiveMult(relics, 'flaskPowerMult').</summary>
        public static double PowerMult(Core2RunData data, Core2Run run) => data.RelicPassiveMult(run.Relics, "flaskPowerMult");

        private static int Current(Core2Run run, string kind) => kind == "hp" ? run.FlaskCharges.HpCurrent : run.FlaskCharges.ManaCurrent;
        private static void Spend(Core2Run run, string kind) { if (kind == "hp") run.FlaskCharges.HpCurrent -= 1; else run.FlaskCharges.ManaCurrent -= 1; }

        /// <summary>JS useRunChargeFlask: drink a charge on the map (its heal or MP on the run, scaled and rounded up).</summary>
        public static void DrinkOnMap(Core2RunData data, Core2Run run, Core2Rng rng, string kind)
        {
            var def = ChargeFlask(data, kind);
            if (run.FlaskCharges == null || Current(run, kind) <= 0) throw new InvalidOperationException("No " + kind + " flask charges");
            var mult = PowerMult(data, run);
            var effects = new JArray(((JArray)def["effects"] ?? new JArray()).OfType<JObject>().Select(e => Scale(e, mult, run)));
            Core2RunEvents.Execute(data, run, rng, effects);
            Spend(run, kind);
        }

        /// <summary>Drink in a fight (JS doUseFlask): a charge kind, or the utility flask in a potion slot.</summary>
        public static void UseInFight(Core2RunData data, Core2Run run, Core2Fight fight, string chargeKind = null, int slot = -1, string targetId = null)
        {
            JObject def;
            if (chargeKind != null)
            {
                def = ChargeFlask(data, chargeKind);
                if (run.FlaskCharges == null || Current(run, chargeKind) <= 0) throw new InvalidOperationException("No " + chargeKind + " flask charges");
            }
            else
            {
                if (slot < 0 || slot >= run.Flasks.Count) throw new InvalidOperationException("No flask in slot " + slot);
                def = data.Flask(run.Flasks[slot]);
            }
            fight.UseFlask(def, PowerMult(data, run), targetId);
            if (chargeKind != null) Spend(run, chargeKind); else run.Flasks.RemoveAt(slot);
        }

        /// <summary>evalNum's amountMult on a run-level effect: numeric amounts and percent-of-max-HP heals scaled, rounded up.</summary>
        private static JObject Scale(JObject effect, double mult, Core2Run run)
        {
            var scaled = (JObject)effect.DeepClone();
            if (mult == 1) return scaled;
            var value = scaled["amount"];
            if (value == null) return scaled;
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) scaled["amount"] = (int)Math.Ceiling(Math.Floor((double)value) * mult);
            else if (value is JObject formula && (string)formula["f"] == "percentMaxHp") scaled["amount"] = (int)Math.Ceiling(run.MaxHp * (double)formula["pct"] / 100 * mult);
            return scaled;
        }
    }
}
