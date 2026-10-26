// Core2FightStatuses.cs — statuses as a live Core 2 fight applies, decays and recovers them, with their events.
//
// JS at the pin:
//   src/engine/statuses.js           applyStatus / applyStatusRaw (stack modes, duration refresh, build-up proc meters,
//                                    resistances), removeStatus / removeStatusRaw (statusRemoved, statusExpired),
//                                    decayAtTurnStart / decayAtTurnEnd (FixCore: once per round; temporary Precision)
//   src/model/fixCoreMechanics.js    fixCoreStatusDefinition (statuses.overrides: weak, vulnerable, staggered)
//   src/engine/combatStatusControl.js applyExpansionActiveStatus (capped active statuses such as Venom),
//                                    recoverStatusesAtOwnerStart / rollStatusChance / recoveryChance (statusRecovery
//                                    stream), clearActive
//   src/content/combatStatusRules.js the rules those read (exported as mechanics.json statusControl)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed partial class Core2Fight
    {
        private JObject StatusControl => (JObject)Content.Mechanics["statusControl"];
        private JObject FixCoreStatusRules => (JObject)Content.Mechanics["fixCoreMechanics"]["statuses"];

        /// <summary>JS statusDefinition: the adapter status row with FixCore's overrides merged.</summary>
        private JObject StatusDef(string id)
        {
            var row = Content.Statuses.FirstOrDefault(s => s.Id == id)?.Raw ?? throw new KeyNotFoundException("Unknown status " + id);
            if (!(FixCoreStatusRules["overrides"]?[id] is JObject overrides)) return row;
            var merged = (JObject)row.DeepClone();
            foreach (var p in overrides.Properties()) merged[p.Name] = p.Value.DeepClone();
            return merged;
        }

        private bool FixCoreOverride(string id) => FixCoreStatusRules["overrides"]?[id] is JObject;

        /// <summary>The control row whose active status is <paramref name="id"/> (JS ruleForActive).</summary>
        private JObject ControlRowForActive(string id) => ((JObject)StatusControl["statuses"]).Properties()
            .Select(p => (JObject)p.Value).FirstOrDefault(row => (string)row["activeStatus"] == id);

        private static int Stacks(Core2Combatant entity, string id) => entity?.StatusStacks(id) ?? 0;

        private JObject StatusEvent(Core2Combatant target, Core2Combatant source, string id, int stacks, int previous, int total) => new JObject
        {
            ["sourceKind"] = source == null ? null : source.Side == Core2Side.Enemy ? "enemy" : "player",
            ["targetKind"] = target.Side == Core2Side.Enemy ? "enemy" : "player", ["wasAbsent"] = previous <= 0,
            ["targetId"] = target.Id, ["sourceId"] = source?.Id, ["status"] = id, ["stacks"] = stacks, ["previousTotal"] = previous, ["total"] = total,
        };

        /// <summary>JS applyStatus: the capped active path for a control status, else the status row's stack mode; Precision granted by a trait:persistent card persists.</summary>
        public void ApplyStatus(Core2Combatant target, string id, int stacks, Core2Combatant source, IReadOnlyList<string> tags = null)
        {
            var before = Stacks(target, id);
            ApplyStatusRaw(target, id, stacks, source);
            if (id == "precision" && target != null && target.Statuses.TryGetValue("precision", out var inst) && tags != null && tags.Contains("trait:persistent"))
                inst.PersistentStacks = Math.Min(inst.Stacks, inst.PersistentStacks + Math.Max(0, inst.Stacks - before));
        }

        private void ApplyStatusRaw(Core2Combatant target, string id, int stacks, Core2Combatant source)
        {
            if (!FixCoreOverride(id) && ApplyExpansionActiveStatus(target, id, stacks, source)) return;
            var def = StatusDef(id);
            if (target == null || !target.Alive) return;
            var previous = Stacks(target, id);
            var amount = (int)Math.Floor((double)stacks);
            var mode = (string)def["stackMode"] ?? "add";
            if (amount <= 0 && mode != "unique") return;
            var proc = def["proc"] as JObject;
            if (proc != null)
            {
                // Threshold-proc resistance: a carried resist status blocks ceil(amount × percent / 100).
                var blocked = 0;
                foreach (var other in target.Statuses.Where(s => s.Value.Stacks > 0).ToList())
                {
                    var resists = Content.Statuses.FirstOrDefault(s => s.Id == other.Key)?.Raw["resists"];
                    if ((string)resists?["status"] == id) blocked += (int)Math.Ceiling(amount * (double)resists["percent"] / 100);
                }
                if (blocked > 0)
                {
                    blocked = Math.Min(blocked, amount);
                    amount -= blocked;
                    Emit("procResisted", new JObject { ["targetId"] = target.Id, ["status"] = id, ["blocked"] = blocked, ["applied"] = amount });
                    if (amount <= 0) return;
                }
            }
            if (!target.Statuses.TryGetValue(id, out var inst))
            {
                target.Statuses[id] = inst = new Core2StatusInstance();
                if (def["meter"] is JObject meter) inst.MeterMax = (int)meter["max"];
                else if (proc != null) inst.MeterMax = (int)proc["threshold"];
            }
            switch (mode)
            {
                case "add": inst.Stacks += amount; break;
                case "refresh": inst.Stacks = Math.Max(inst.Stacks, amount); break;
                case "unique": inst.Stacks = inst.MeterMax > 0 ? Math.Max(inst.Stacks, 1) : 1; break;
                default: throw new InvalidOperationException("Unknown stackMode '" + mode + "' on status '" + id + "'");
            }
            if (def["decay"] is JObject decay && decay["duration"] != null) inst.Duration = (int)decay["duration"];
            Emit("statusApplied", StatusEvent(target, source, id, amount, previous, Stacks(target, id)));
            if (proc != null) CheckProcFill(target, id, proc, inst, source);
            else if (inst.MeterMax > 0) throw new NotSupportedException("Status meter " + id + " is not ported");
        }

        /// <summary>JS checkProcFill: at the threshold the build-up bursts (HP loss, Poise damage, resistance) and resets to zero.</summary>
        private void CheckProcFill(Core2Combatant entity, string id, JObject proc, Core2StatusInstance inst, Core2Combatant source)
        {
            if (inst.Stacks < inst.MeterMax) return;
            inst.Stacks = 0;
            var pct = (int)Math.Floor(entity.MaxHp * (double)proc["burstPercent"] / 100);
            var burst = Math.Max((int)proc["burstMin"], Math.Min((int)proc["burstMax"], pct));
            Emit("procBurst", new JObject { ["targetId"] = entity.Id, ["status"] = id, ["amount"] = burst, ["threshold"] = inst.MeterMax });
            void Enq(JObject effect) => Enqueue(new Core2FightAction { Effect = effect, Source = entity, Owner = entity, Target = entity });
            Enq(new JObject { ["op"] = "loseHp", ["target"] = "self", ["amount"] = burst, ["cause"] = "proc:" + id });
            if (((int?)proc["poiseDamage"] ?? 0) > 0 && entity.Side == Core2Side.Enemy) Enq(new JObject { ["op"] = "poiseDamage", ["amount"] = proc["poiseDamage"] });
            if ((bool?)proc["stagger"] == true && entity.Side == Core2Side.Enemy) Enq(new JObject { ["op"] = "stagger" });
            foreach (var effect in (proc["effects"] as JArray ?? new JArray()).OfType<JObject>()) Enq(effect);
            if (proc["resistance"] is JObject resistance && entity.Side == Core2Side.Enemy)
            {
                var tags = Core2Json.Strings(EnemyDef(entity).Raw["tags"]);
                if (tags.Any(t => Core2Json.Strings(resistance["tags"]).Contains(t))) Enq(new JObject { ["op"] = "applyStatus", ["target"] = "self", ["status"] = resistance["status"], ["stacks"] = 1 });
            }
        }

        /// <summary>JS applyExpansionActiveStatus: a control row's active status adds up to its cap (true when handled).</summary>
        private bool ApplyExpansionActiveStatus(Core2Combatant target, string id, int amount, Core2Combatant source)
        {
            var row = ControlRowForActive(id);
            if (row == null) return false;
            if (target == null || !target.Alive || !(amount > 0)) return true;
            var before = Stacks(target, id);
            var add = Math.Min(Math.Max(0, amount), Math.Max(0, (int)row["cap"] - before));
            if (add == 0) return true;
            if (!target.Statuses.TryGetValue(id, out var inst)) target.Statuses[id] = inst = new Core2StatusInstance();
            inst.Stacks = before + add;
            inst.MeterMax = 0;
            var def = Content.Statuses.FirstOrDefault(s => s.Id == id)?.Raw;
            if (def?["decay"] is JObject decay && decay["duration"] != null) inst.Duration = (int)decay["duration"];
            Emit("statusApplied", StatusEvent(target, source, id, add, before, inst.Stacks));
            return true;
        }

        /// <summary>
        /// JS removeStatus(ctx, target, id, { amount?, reason, silent? }): a partial removal reports statusRemoved for a
        /// consumed or owner-turn removal; reaching zero deletes the status (statusRemoved for consumed/ownerTurnStart, then
        /// statusExpired unless silent).
        /// </summary>
        public void RemoveStatus(Core2Combatant target, string id, int? amount, string reason, bool silent = false)
        {
            if (!target.Statuses.TryGetValue(id, out var inst)) return;
            var before = Stacks(target, id);
            if (amount.HasValue)
            {
                inst.Stacks = Math.Max(0, inst.Stacks - amount.Value);
                inst.PersistentStacks = Math.Min(inst.PersistentStacks, inst.Stacks);
                if (inst.Stacks > 0)
                {
                    if (reason == "consumed" || reason == "ownerTurnStart" || reason == "ownerTurnEnd")
                        Emit("statusRemoved", new JObject { ["targetId"] = target.Id, ["status"] = id, ["amount"] = before - Stacks(target, id), ["reason"] = reason });
                    return;
                }
            }
            target.Statuses.Remove(id);
            if (reason == "consumed" || reason == "ownerTurnStart")
                Emit("statusRemoved", new JObject { ["targetId"] = target.Id, ["status"] = id, ["amount"] = before, ["reason"] = "consumed" });
            if (!silent) Emit("statusExpired", new JObject { ["targetId"] = target.Id, ["status"] = id, ["reason"] = reason ?? "removed" });
        }

        /// <summary>JS decayAtTurnStart (FixCore): once per round, every perTurnEnd status and Evade lose decayAmount; durations tick.</summary>
        private void DecayAtTurnStart(Core2Combatant entity)
        {
            if (entity.DecayRound == Turn) return;
            entity.DecayRound = Turn;
            foreach (var id in entity.Statuses.Keys.ToList())
            {
                if (!entity.Statuses.TryGetValue(id, out var inst)) continue;
                var def = StatusDef(id);
                if (id != "precision" && ((string)def["decay"] == "perTurnEnd" || id == "evade"))
                    RemoveStatus(entity, id, Rules.StatusDecayAmount, "ownerTurnStart");
                else if (def["decay"] is JObject decay && decay["duration"] != null)
                {
                    inst.Duration -= 1;
                    if (inst.Duration <= 0) RemoveStatus(entity, id, null, "expired");
                }
            }
        }

        /// <summary>JS decayAtTurnEnd (FixCore): temporary Precision (beyond its persistent stacks) expires.</summary>
        private void DecayAtTurnEnd(Core2Combatant entity)
        {
            if (!entity.Statuses.TryGetValue("precision", out var precision)) return;
            var temporary = Math.Max(0, precision.Stacks - precision.PersistentStacks);
            if (temporary > 0) RemoveStatus(entity, "precision", temporary, "ownerTurnEnd");
        }

        /// <summary>JS clearActive: remove stacks of an active control status, reporting statusRemoved with the reason.</summary>
        private int ClearActive(Core2Combatant entity, string id, int count, string reason)
        {
            if (!entity.Statuses.TryGetValue(id, out var inst)) return 0;
            var removed = Math.Min(Stacks(entity, id), count);
            if (removed == 0) return 0;
            inst.Stacks -= removed;
            if (inst.Stacks == 0) entity.Statuses.Remove(id);
            Emit("statusRemoved", new JObject { ["targetId"] = entity.Id, ["status"] = id, ["amount"] = removed, ["reason"] = reason });
            return removed;
        }

        /// <summary>
        /// JS recoverStatusesAtOwnerStart: once per owner cycle, each chance-recoverable status the entity carries (active or
        /// pending gauge) rolls d100 on statusRecovery against its recovery chance; success clears one active stack.
        /// FixCore-overridden statuses (Weak, Vulnerable) decay instead and never roll.
        /// </summary>
        private void RecoverStatusesAtOwnerStart(Core2Combatant entity)
        {
            if (!entity.Alive) return;
            var cycle = entity.OwnerCycle;
            if (entity.LastRecoveryCycle == cycle) return;
            entity.LastRecoveryCycle = cycle;
            var cfg = StatusControl;
            foreach (var p in ((JObject)cfg["statuses"]).Properties())
            {
                var row = (JObject)p.Value;
                var active = (string)row["activeStatus"];
                if (FixCoreOverride(active)) continue;
                if ((bool?)row["chanceRecovery"] != true || Stacks(entity, active) == 0) continue;
                var profile = (string)row["profile"];
                var chance = RecoveryChance(entity, row, profile);
                var roll = Rng.Int("statusRecovery", 1, 100);
                var removed = roll <= chance ? ClearActive(entity, active, 1, "recovered") : 0;
                Emit("statusRecoveryRolled", new JObject { ["targetId"] = entity.Id, ["status"] = p.Name, ["roll"] = roll, ["chance"] = chance, ["success"] = roll <= chance, ["removed"] = removed, ["cycle"] = cycle });
            }
        }

        /// <summary>JS recoveryChance: base + floor(weighted attributes) − stackPenalty × (stacks − 1), clamped.</summary>
        private int RecoveryChance(Core2Combatant entity, JObject row, string profile)
        {
            var cfg = StatusControl;
            var weights = cfg["profiles"]?[profile] as JObject ?? new JObject();
            var weighted = weights.Properties().Sum(w => entity.Attribute(w.Name) * (double)w.Value);
            var count = Stacks(entity, (string)row["activeStatus"]);
            var recovery = (JObject)cfg["recovery"];
            return (int)Math.Min((double)recovery["maximum"], Math.Max((double)recovery["minimum"],
                (double)recovery["base"] + Math.Floor(weighted) - (double)recovery["stackPenalty"] * Math.Max(0, count - 1)));
        }
    }
}
