// Core2RunCreation.cs — a new Core 2 run from a class and a seed (port plan phase 8).
//
// JS at the pin:
//   src/model/state.js        createRunState (configured class selection: the class's fixed six-point attributes with
//                             its primary at 2, the fixed loadout, the starting relics, the flask charge split, the
//                             derived-stat snapshot; initializeRunDerivedStats fills every pool; the configured deck is
//                             gameProfileStarterDeck with gear grants reconciled on), syncZones (collected cards)
//   src/model/gameProfileRun.js gameProfileStarterDeck / initializeGameProfileRun (instance ids gp1…), gameProfileClassAvailable
//   src/main.js               newRun (the seat order drawn once on the `seats` stream), startClimb (the act 1 map)
//
// The creation resolvers' output (relics, loadout, flask charges, the rule snapshot) is exported per class as data
// (adapter/run.json classes.birth); the pools, ratings and deck are derived here.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2RunCreation
    {
        /// <summary>
        /// JS createRunState for a Core 2 class (refused while the class is locked), then main.js newRun's seat draw on a fresh
        /// RNG of the seed. Returns the run (act 1, floor 0, no map yet) and the RNG positioned after the seat draw.
        /// </summary>
        public static (Core2Run Run, Core2Rng Rng) Create(Core2RunData data, string classId, uint seed, Core2UnlockLedger ledger = null, string runId = null, bool ignoreUnlocks = false)
        {
            var content = data.Content;
            if (!ignoreUnlocks && !Core2RunProgression.ClassAvailable(content, ledger, classId)) throw new InvalidOperationException("Class '" + classId + "' is locked in this profile");
            var cls = content.Class(classId);
            var birth = data.Class(classId);
            var run = new Core2Run
            {
                RunId = runId ?? Guid.NewGuid().ToString(), Seed = seed, Class = classId, AttributeMode = birth.AttributeMode,
                Attributes = Core2Run.AttributeIds.ToDictionary(id => id, id => cls.Attributes.TryGetValue(id, out var v) ? v : 0, StringComparer.Ordinal),
                DerivedStatRules = (Newtonsoft.Json.Linq.JObject)birth.DerivedStatRules.DeepClone(),
                Cinders = birth.Cinders, Relics = birth.Relics.ToList(), Loadout = Core2RunLoadout.FromJson(birth.Loadout),
                FlaskCharges = Core2FlaskCharges.FromJson(birth.FlaskCharges),
            };
            // The derived-stat door (JS initializeRunDerivedStats, a run being born): every pool full at its derived maximum.
            var bonuses = Core2RunGear.RunMods(data, run).Pools;
            var attributes = Core2RunGear.EffectiveAttributes(data, run);
            run.EquipmentPoolBonuses = bonuses;
            run.MaxHp = Math.Max(1, Core2DerivedStats.Derive(run.DerivedStatRules, "hp", attributes, 1, classId) + bonuses["maxHp"] + run.MaxHpAdjustment);
            run.MaxMana = Math.Max(0, Core2DerivedStats.Derive(run.DerivedStatRules, "mana", attributes, 1, classId) + bonuses["maxMana"]);
            run.MaxStamina = Math.Max(0, Core2DerivedStats.Derive(run.DerivedStatRules, "stamina", attributes, 1, classId) + bonuses["maxStamina"]);
            run.EnergyMax = run.MaxStamina;
            run.DrawPerTurn = Core2DerivedStats.Derive(run.DerivedStatRules, "draw", attributes, 1, classId);
            run.Hp = run.MaxHp; run.Mana = run.MaxMana; run.Stamina = run.MaxStamina;
            // The configured deck: the class's authored opening list, then what the worn gear lends.
            run.Deck = content.StarterDeck(classId).Select((cardId, i) => new Core2RunCard { InstanceId = "gp" + (i + 1), CardId = cardId }).ToList();
            Core2RunGear.ReconcileGrantedCards(data, run);
            Core2RunGear.ReconcilePools(data, run);
            Core2RunRewards.RecordCollectedCards(data, run);
            var rng = new Core2Rng(seed);
            run.SeatOrder = Core2ActMap.DrawSeatOrder(content, rng);
            return (run, rng);
        }
    }
}
