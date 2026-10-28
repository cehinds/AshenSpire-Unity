// Core2RunProgression.cs — character XP and levels, and the Core class unlock ledger.
//
// JS at the pin:
//   src/model/levelup.js       xpToNext (the one curve: exponential base × growth^(level−1), rounded to roundTo),
//                              combatXpReceipt (defeated enemies' combat power × combatPowerMultiplier × combatWin, plus
//                              kill base × killLevelMultiplier × level, cumulative floors), climbLevels, awardLevelXp
//                              (each level grants progression.attributePointsPerLevel, pools re-derived at the new level)
//   src/model/xpCurve.js       xpStepCost
//   src/model/combatPower.js   enemyCombatPower
//   src/model/gameProfileRun.js gameProfileClassAvailable / gameProfileCompletedRunMeta (Reaver first; each unique finished
//                              run, won or lost, unlocks the next class in classUnlocks.sequence)
//
// Class XP and used-weapon XP (JS settleConfiguredSkillProgression, skill tracks) are port plan phase 10 and not carried.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>A defeated (or surviving) enemy as the XP receipt reads it.</summary>
    public sealed class Core2XpEnemy
    {
        public string EnemyId { get; set; }
        public bool Defeated { get; set; }
        public int Level { get; set; } = 1;
        public int MaxHp { get; set; }
        public double DamageMult { get; set; } = 1;
    }

    public sealed class Core2XpReceipt
    {
        public int Total { get; set; }
        public List<JObject> Rows { get; } = new List<JObject>();
        public JObject ToJson() => new JObject { ["total"] = Total, ["rows"] = new JArray(Rows.Select(r => r.DeepClone())) };
    }

    public sealed class Core2LevelAward
    {
        public int Before { get; set; }
        public int After { get; set; }
        public int LevelUps { get; set; }
        public int Points { get; set; }
        public int Gained { get; set; }
        public int Discarded { get; set; }
    }

    /// <summary>The durable profile's Core class ledger (JS meta.gameProfileUnlocks[profileId].completedRunIds).</summary>
    public sealed class Core2UnlockLedger
    {
        public List<string> CompletedRunIds { get; set; } = new List<string>();
        public JObject ToJson() => new JObject { ["completedRunIds"] = new JArray(CompletedRunIds) };
        public static Core2UnlockLedger FromJson(JToken token) => new Core2UnlockLedger { CompletedRunIds = Core2Json.Strings(token?["completedRunIds"]).ToList() };
    }

    public static class Core2RunProgression
    {
        private static JObject Curve(Core2RunData data) => (JObject)data.Balance["level"]?["xp"] ?? new JObject();

        /// <summary>JS xpStepCost / levelup xpToNext: the XP the step from <paramref name="level"/> to the next costs.</summary>
        public static int XpToNext(Core2RunData data, int level)
        {
            var curve = Curve(data);
            var b = (double?)curve["base"] is double bv && bv > 0 ? bv : 100;
            var g = (double?)curve["growth"] is double gv && gv > 0 ? gv : 1.15;
            var step = level > 1 ? level - 1 : 0;
            var linear = (bool?)curve["linear"] == true;
            var raw = linear ? b + step * b * ((double?)curve["multScaler"] ?? 0) : b * Math.Pow(g, step);
            var unit = curve["roundTo"]?.Type == JTokenType.Integer && (int)curve["roundTo"] > 0 ? (int)curve["roundTo"] : 1;
            var epsilon = 1e-9;
            return (int)Math.Max(unit, Math.Floor(raw / unit + epsilon + 0.5) * unit);
        }

        /// <summary>JS enemyCombatPower: 1 + level/5 + (hp/24 + poise/10 + attack × damageMult/8)/3 + equipment power.</summary>
        public static double CombatPower(Core2Content content, Core2XpEnemy enemy)
        {
            var def = content.Enemies.FirstOrDefault(e => e.Id == enemy.EnemyId);
            if (def == null) return 3;
            var level = enemy.Level > 0 ? enemy.Level : 1;
            var hp = enemy.MaxHp > 0 ? enemy.MaxHp : (def.HpMin + def.HpMax) / 2.0;
            double poise = def.PoiseMax;
            var attack = Math.Max(0, def.Moves.Where(m => m.Intent == "attack").Select(m => (double)((m.Damage ?? 0) * (m.Hits > 0 ? m.Hits : 1))).DefaultIfEmpty(double.NegativeInfinity).Max());
            var stats = (hp / 24 + poise / 10 + attack * enemy.DamageMult / 8) / 3;
            return 1 + level / 5.0 + stats + ((double?)def.Raw["equipmentPower"] ?? 0);
        }

        /// <summary>JS combatXpReceipt: the XP one fight pays (combat power on a win, then each defeated enemy's level share).</summary>
        public static Core2XpReceipt CombatXp(Core2RunData data, bool victory, string pool, IReadOnlyList<Core2XpEnemy> enemies, double characterMultiplier = 1)
        {
            var t = (JObject)data.Balance["xp"] ?? new JObject();
            var kill = t["kill"] as JObject ?? new JObject();
            var perKill = (double?)kill[pool ?? "normal"] ?? (double?)kill["normal"] ?? 0;
            var levelMultiplier = (double?)t["killLevelMultiplier"] is double lm && lm >= 0 ? lm : 1;
            var powerMultiplier = (double?)t["combatPowerMultiplier"] is double pm && pm >= 0 ? pm : 0.2;
            var defeated = enemies.Where(e => e.Defeated).ToList();
            var powerSum = victory ? defeated.Aggregate(0.0, (sum, e) => sum + CombatPower(data.Content, e)) : 0;
            var raw = powerSum * powerMultiplier * ((double?)t["combatWin"] ?? 0);
            var subtotal = 0;
            var receipt = new Core2XpReceipt();
            if (victory)
            {
                subtotal = (int)Math.Floor(raw + 1e-9);
                receipt.Rows.Add(new JObject { ["kind"] = "power", ["amount"] = subtotal });
            }
            for (var i = 0; i < defeated.Count; i++)
            {
                var enemy = defeated[i];
                var level = enemy.Level > 0 ? enemy.Level : 1;
                raw += perKill * levelMultiplier * level;
                var next = (int)Math.Floor(raw + 1e-9);
                var def = data.Content.Enemies.FirstOrDefault(e => e.Id == enemy.EnemyId);
                receipt.Rows.Add(new JObject { ["kind"] = "enemy", ["enemyId"] = enemy.EnemyId, ["name"] = def?.Name ?? "Enemy " + (i + 1), ["level"] = level, ["amount"] = next - subtotal });
                subtotal = next;
            }
            var multiplier = characterMultiplier >= 0 ? characterMultiplier : 1;
            receipt.Total = (int)Math.Floor(subtotal * multiplier);
            if (receipt.Total != subtotal) receipt.Rows.Add(new JObject { ["kind"] = "bonus", ["amount"] = receipt.Total - subtotal });
            return receipt;
        }

        /// <summary>JS climbLevels: XP added at a level, stepping up while it pays; maxLevels and maxLevelsPerFight stop it.</summary>
        public static (int Level, int Xp, int LevelUps, int Discarded) Climb(Core2RunData data, int level, int xp, int gain)
        {
            var start = level >= 1 ? level : 1;
            var ceiling = data.Balance["levelUp"]?["maxLevels"]?.Type == JTokenType.Integer ? (int)data.Balance["levelUp"]["maxLevels"] : (int?)null;
            var perFight = data.Balance["level"]?["maxLevelsPerFight"]?.Type == JTokenType.Integer && (int)data.Balance["level"]["maxLevelsPerFight"] >= 1 ? (int)data.Balance["level"]["maxLevelsPerFight"] : (int?)null;
            var lv = start;
            var bank = xp + Math.Max(0, gain);
            var cost = XpToNext(data, lv);
            var discarded = 0;
            while (bank >= cost)
            {
                if (perFight.HasValue && lv - start >= perFight.Value) { discarded = bank - (cost - 1); bank = cost - 1; break; }
                if (ceiling.HasValue && lv >= ceiling.Value) break;
                bank -= cost;
                lv += 1;
                cost = XpToNext(data, lv);
            }
            return (lv, bank, lv - start, discarded);
        }

        /// <summary>JS awardLevelXp (configured, authored entitlement): climb, grant points per level, re-derive the pools.</summary>
        public static Core2LevelAward AwardLevelXp(Core2RunData data, Core2Run run, int amount)
        {
            var before = run.Level.Level;
            if (amount <= 0) return new Core2LevelAward { Before = before, After = before };
            var progression = data.Module("progression");
            var perLevel = (string)progression?["entitlementPolicy"] == "authored"
                ? (data.Module("stats") != null && (bool?)progression["characterLevels"] == true ? (int?)progression["attributePointsPerLevel"] ?? 0 : 0)
                : (int?)data.Balance["levelUp"]?["pointsPerLevel"] ?? 1;
            var climb = Climb(data, run.Level.Level, run.Level.Xp, amount);
            run.Level.Xp = climb.Xp;
            run.Level.Level = climb.Level;
            run.Level.UnspentPoints += climb.LevelUps * perLevel;
            if (climb.LevelUps > 0) Core2RunGear.RederivePools(data, run);
            return new Core2LevelAward { Before = before, After = climb.Level, LevelUps = climb.LevelUps, Points = climb.LevelUps * perLevel, Gained = amount, Discarded = climb.Discarded };
        }

        /// <summary>JS gameProfileClassAvailable: the initial classes, then one class of the sequence per finished run.</summary>
        public static bool ClassAvailable(Core2Content content, Core2UnlockLedger ledger, string classId)
        {
            var rules = content.ClassUnlocks;
            var completed = ledger?.CompletedRunIds.Count ?? 0;
            return Core2Json.Strings(rules["initial"]).Concat(Core2Json.Strings(rules["sequence"]).Take(completed)).Contains(classId);
        }

        /// <summary>JS gameProfileCompletedRunMeta: record a finished run once (by its id) and name the class it unlocks.</summary>
        public static List<string> RecordFinish(Core2Content content, Core2UnlockLedger ledger, string finishId, bool victory)
        {
            if (string.IsNullOrEmpty(finishId)) throw new ArgumentException("Configured completion requires a durable finishId");
            var rules = content.ClassUnlocks;
            if (ledger.CompletedRunIds.Contains(finishId)) return new List<string>();
            if (!Core2Json.Strings(rules["qualifyingOutcomes"]).Contains(victory ? "victory" : "defeat")) return new List<string>();
            var count = ledger.CompletedRunIds.Count;
            ledger.CompletedRunIds.Add(finishId);
            var sequence = Core2Json.Strings(rules["sequence"]);
            return count < sequence.Count ? new List<string> { sequence[count] } : new List<string>();
        }
    }
}
