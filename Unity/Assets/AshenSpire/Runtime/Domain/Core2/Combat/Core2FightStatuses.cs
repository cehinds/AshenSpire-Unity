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
            // JS killReceipt: the statuses before the burst reset the meter, and the status's source.
            var killReceipt = (CloneStatuses(entity), source?.Id);
            inst.Stacks = 0;
            var pct = (int)Math.Floor(entity.MaxHp * (double)proc["burstPercent"] / 100);
            var burst = Math.Max((int)proc["burstMin"], Math.Min((int)proc["burstMax"], pct));
            Emit("procBurst", new JObject { ["targetId"] = entity.Id, ["status"] = id, ["amount"] = burst, ["threshold"] = inst.MeterMax });
            void Enq(JObject effect) => Enqueue(new Core2FightAction { Effect = effect, Source = entity, Owner = entity, Target = entity, KillReceipt = killReceipt });
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
            target.RemoveStatusEntry(id);
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
                // decay is a mode string (perTurnEnd, none, onConsume) or a { duration } object (Crimson Blight).
                if (id != "precision" && (def["decay"]?.Type == JTokenType.String && (string)def["decay"] == "perTurnEnd" || id == "evade"))
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
            if (inst.Stacks == 0) entity.RemoveStatusEntry(id);
            Emit("statusRemoved", new JObject { ["targetId"] = entity.Id, ["status"] = id, ["amount"] = removed, ["reason"] = reason });
            return removed;
        }

        /// <summary>
        /// JS recoverStatusesAtOwnerStart: once per owner cycle the control turn opens (a lock waited out last turn resolves
        /// now: resolveActive), then each chance-recoverable status the entity carries — an active stack or a pending gauge
        /// — rolls d100 on statusRecovery against its recovery chance (the gauge's recovery profile; Grounded gives
        /// Paralysis advantage, Off Balance gives a bodily roll disadvantage). Success clears one active stack, or a quarter
        /// (recovery.pendingRemoval) of a pending gauge. FixCore-overridden statuses (Weak, Vulnerable) decay instead and never roll.
        /// </summary>
        private void RecoverStatusesAtOwnerStart(Core2Combatant entity)
        {
            if (!entity.Alive) return;
            var cycle = entity.OwnerCycle;
            if (entity.LastRecoveryCycle == cycle) return;
            entity.LastRecoveryCycle = cycle;
            entity.ResolveActive = entity.ResolveNextTurn; entity.ResolveNextTurn = false;
            entity.CompletedStatusActions.Clear();
            var cfg = StatusControl;
            var interactions = (JObject)cfg["interactions"];
            foreach (var p in ((JObject)cfg["statuses"]).Properties())
            {
                var row = (JObject)p.Value;
                var active = (string)row["activeStatus"];
                if (FixCoreOverride(active)) continue;
                entity.StatusGauges.TryGetValue(p.Name, out var gauge);
                if ((bool?)row["chanceRecovery"] != true || Stacks(entity, active) == 0 && !(gauge?.Value > 0)) continue;
                var profile = gauge?.RecoveryProfile ?? (string)row["profile"];
                var advantage = p.Name == (string)interactions["paralysis"] && Stacks(entity, (string)interactions["grounded"]) > 0;
                var disadvantage = profile == "bodily" && Stacks(entity, (string)interactions["offBalance"]) > 0;
                var chance = RecoveryChance(entity, row, profile);
                var receipt = RollStatusChance(chance, advantage, disadvantage, "statusRecovery");
                double removed = 0;
                if ((bool)receipt["success"])
                {
                    if (Stacks(entity, active) > 0) removed = ClearActive(entity, active, 1, "recovered");
                    else { removed = gauge.Value * (double)cfg["recovery"]["pendingRemoval"]; gauge.Value -= removed; }
                }
                var ev = new JObject { ["targetId"] = entity.Id, ["status"] = p.Name };
                foreach (var field in receipt.Properties()) ev[field.Name] = field.Value;
                ev["removed"] = JsNumber(removed); ev["cycle"] = cycle;
                Emit("statusRecoveryRolled", ev);
            }
        }

        /// <summary>A JS number as JSON writes it: a whole value without a fraction.</summary>
        private static JToken JsNumber(double value) => value == Math.Floor(value) && Math.Abs(value) < 9e15 ? new JValue((long)value) : new JValue(value);

        /// <summary>
        /// JS rollStatusChance: d100 on <paramref name="stream"/> (a second die with advantage or disadvantage: the lower or
        /// higher counts); success when the roll is at most the chance. The receipt as JS returns it.
        /// </summary>
        private JObject RollStatusChance(double chance, bool advantage, bool disadvantage, string stream)
        {
            var rolls = new List<int> { Rng.Int(stream, 1, 100) };
            if (advantage != disadvantage) rolls.Add(Rng.Int(stream, 1, 100));
            var roll = advantage == disadvantage ? rolls[0] : advantage ? rolls.Min() : rolls.Max();
            var p = chance / 100;
            var effective = advantage == disadvantage ? chance : 100 * (advantage ? 1 - (1 - p) * (1 - p) : p * p);
            return new JObject { ["rolls"] = new JArray(rolls), ["roll"] = roll, ["chance"] = chance, ["effectiveChance"] = effective, ["success"] = roll <= chance };
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Status pressure and control (JS combatStatusControl.js)
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// JS applyStatusPressure: <paramref name="units"/> of buildup fill the status's gauge; each time it reaches the
        /// threshold (initialThreshold, or activeThreshold while an active stack is on) and the active status is under its
        /// cap, the threshold is spent and meets the camp's defense: a spell fill spends Ward while Ward is up, a physical
        /// fill strains the legacy Poise meter (a full meter is a status Poise break: the player is staggered and the meter
        /// grows). An admitted fill grants one active stack (and the row's extra grants); every fill reports
        /// statusGaugeFilled, the application statusPressure. The entity's combat traits (immunity, resistance) are not
        /// modelled: no Core 2 combatant carries any.
        /// </summary>
        private Core2PressureReceipt ApplyStatusPressure(Core2Combatant target, string id, double units, Core2Combatant source, string camp, string actionKey, string recoveryProfile)
        {
            var receipt = new Core2PressureReceipt();
            if (target == null || !target.Alive || !(units > 0)) return receipt;
            var row = StatusControl["statuses"]?[id] as JObject ?? throw new InvalidOperationException("Unknown status buildup '" + id + "'");
            camp ??= (string)row["camp"];
            if (camp != "physical" && camp != "spell") throw new InvalidOperationException("Unknown status source camp '" + camp + "'");
            var accepted = units;
            receipt.Resisted = units - accepted;
            if (!target.StatusGauges.TryGetValue(id, out var gauge)) target.StatusGauges[id] = gauge = new Core2StatusGauge();
            gauge.Camp = camp; gauge.RecoveryProfile = recoveryProfile ?? (string)row["profile"];
            gauge.Value += accepted;
            var active = (string)row["activeStatus"];
            int Threshold() => Stacks(target, active) > 0 ? (int)row["activeThreshold"] : (int)row["initialThreshold"];
            var iterations = 0;
            while (gauge.Value >= Threshold())
            {
                if (++iterations > 10000) throw new InvalidOperationException("Unbounded status buildup '" + id + "'");
                if (Stacks(target, active) >= (int)row["cap"]) break;
                var threshold = Threshold();
                gauge.Value -= threshold;
                var admitted = true;
                if (camp == "spell" && target.Ward?.Value > 0)
                    // JS spendPersistentWard: no Core 2 combatant has a Ward pool (persistentWard max 0), so this is never reached.
                    throw new NotSupportedException("A spell status fill against a standing Ward (spendPersistentWard) is not ported");
                if (camp == "physical" && target.PoiseMeter?.Max > 0)
                {
                    var meter = target.PoiseMeter;
                    meter.Value += 1; receipt.PoiseStrain += 1;
                    admitted = meter.Value >= meter.Max;
                    if (admitted)
                    {
                        meter.Value %= meter.Max;
                        Emit("statusPoiseBreak", new JObject { ["targetId"] = target.Id, ["status"] = id, ["actionKey"] = actionKey });
                        // JS: an enemy's fill is breakEnemy (stagger + balance.poise.onFill) — Core 2 enemies carry no legacy meter here.
                        if (target.Side == Core2Side.Enemy) throw new NotSupportedException("An enemy's status Poise break (breakEnemy) is not ported");
                        StaggerPlayerFromMeter(target);
                        GrowPoiseMeter(target);
                    }
                }
                if (admitted)
                {
                    var before = Stacks(target, active);
                    GrantStatus(target, active, 1, source);
                    receipt.ActiveAdded += Stacks(target, active) - before;
                    foreach (var extra in (row["grants"] as JObject ?? new JObject()).Properties()) GrantStatus(target, extra.Name, (int)extra.Value, source);
                }
                else receipt.Resisted += threshold;
                Emit("statusGaugeFilled", new JObject { ["targetId"] = target.Id, ["status"] = id, ["threshold"] = threshold, ["admitted"] = admitted, ["camp"] = camp, ["actionKey"] = actionKey });
            }
            receipt.AcceptedBuildup = Math.Min(accepted, gauge.Value);
            receipt.Resisted = Math.Min(units, receipt.Resisted);
            receipt.FullyResisted = receipt.ActiveAdded == 0 && receipt.AcceptedBuildup == 0;
            Emit("statusPressure", new JObject
            {
                ["targetId"] = target.Id, ["status"] = id, ["sourceId"] = source?.Id, ["camp"] = camp, ["actionKey"] = actionKey, ["value"] = JsNumber(gauge.Value),
                ["acceptedBuildup"] = JsNumber(receipt.AcceptedBuildup), ["activeAdded"] = receipt.ActiveAdded, ["resisted"] = JsNumber(receipt.Resisted), ["wardSpent"] = receipt.WardSpent,
                ["poiseStrain"] = receipt.PoiseStrain, ["fullyResisted"] = receipt.FullyResisted,
            });
            return receipt;
        }

        /// <summary>
        /// JS grant (combatStatusControl.js): a control row's active status goes through applyExpansionActiveStatus (capped,
        /// whatever FixCore overrides); any other status stacks by its row's stack mode and reports statusApplied.
        /// </summary>
        private void GrantStatus(Core2Combatant target, string id, int amount, Core2Combatant source)
        {
            if (ApplyExpansionActiveStatus(target, id, amount, source)) return;
            var def = StatusDef(id);
            var prior = Stacks(target, id);
            if (!target.Statuses.TryGetValue(id, out var inst)) target.Statuses[id] = inst = new Core2StatusInstance();
            var mode = (string)def["stackMode"];
            inst.Stacks = mode == "unique" ? 1 : mode == "refresh" ? Math.Max(prior, amount) : prior + amount;
            if (def["decay"] is JObject decay && decay["duration"] != null) inst.Duration = (int)decay["duration"];
            Emit("statusApplied", new JObject { ["targetId"] = target.Id, ["sourceId"] = source?.Id, ["status"] = id, ["stacks"] = amount, ["previousTotal"] = prior, ["total"] = inst.Stacks, ["wasAbsent"] = prior == 0 });
        }

        /// <summary>
        /// JS staggerPlayer (a full legacy Poise meter): the prepared Counter is gone, the player takes
        /// balance.stagger.player's statuses (Vulnerable 2, Weak 2) and loses its actionLoss SP next turn; playerStaggered.
        /// </summary>
        private void StaggerPlayerFromMeter(Core2Combatant player)
        {
            ClearCounter(player);
            var cfg = (JObject)Content.Mechanics["poiseMeter"]["playerStagger"];
            var actionLoss = cfg["actionLoss"]?.Type == JTokenType.Integer ? (int)cfg["actionLoss"] : 0;
            var applied = new JObject();
            foreach (var row in (cfg["statuses"] as JObject ?? new JObject()).Properties())
            {
                var stacks = (double)row.Value;
                if (!(stacks > 0)) continue;
                ApplyStatus(player, row.Name, (int)stacks, null);
                applied[row.Name] = row.Value;
            }
            player.PendingStaminaLoss += actionLoss;
            Emit("playerStaggered", new JObject { ["targetId"] = player.Id, ["actionLoss"] = actionLoss, ["statuses"] = applied });
        }

        /// <summary>JS growPoiseMeter: after a fill the meter's max grows by balance.poise.growthMult, rounded up.</summary>
        private void GrowPoiseMeter(Core2Combatant entity)
        {
            var growth = (double?)Content.Mechanics["poiseMeter"]?["growthMult"] ?? 1.25;
            if (growth == 1) return;
            entity.PoiseMeter.Max = (int)Math.Ceiling(entity.PoiseMeter.Max * growth);
        }

        /// <summary>JS controlRestrictions: a lock status (Dazed, Sleep, Paralysis) disables cards, Counter and Evade unless this turn resolves it.</summary>
        public Core2ControlRestrictions ControlRestrictions(Core2Combatant entity)
        {
            var locks = ((JObject)StatusControl["statuses"]).Properties().Select(p => (JObject)p.Value)
                .Where(row => (bool?)row["lock"] == true && Stacks(entity, (string)row["activeStatus"]) > 0).Select(row => (string)row["activeStatus"]).ToList();
            var locked = !entity.ResolveActive && locks.Count > 0;
            return new Core2ControlRestrictions { Locked = locked, Locks = locks, CounterDisabled = locked, EvadeDisabled = locked, ResolveActive = entity.ResolveActive };
        }

        /// <summary>JS controlGate: a locked entity may play only a card whose usableWhile covers every active lock.</summary>
        private bool ControlGate(Core2Combatant entity, Core2Card card)
        {
            var gate = ControlRestrictions(entity);
            if (!gate.Locked) return true;
            var covers = Core2Json.Strings(card.Raw["usableWhile"] ?? card.Raw["recovery"]?["usableWhile"]);
            return gate.Locks.All(covers.Contains);
        }

        /// <summary>JS endControlTurn: a locked owner waits out one stack of its first lock and resolves next turn (controlResolveScheduled).</summary>
        private void EndControlTurn(Core2Combatant entity)
        {
            var gate = ControlRestrictions(entity);
            if (gate.Locked)
            {
                var id = gate.Locks[0];
                ClearActive(entity, id, 1, "wait");
                entity.ResolveNextTurn = true;
                Emit("controlResolveScheduled", new JObject { ["targetId"] = entity.Id, ["status"] = id });
            }
            entity.ResolveActive = false;
        }

        /// <summary>JS consumeControlActionLoss: a Frozen stack denies one Action (frozen.actionLoss SP) unless this turn resolves control.</summary>
        private int ConsumeControlActionLoss(Core2Combatant entity)
        {
            if (entity.ResolveActive) return 0;
            var row = (JObject)StatusControl["frozen"];
            return ClearActive(entity, (string)row["status"], 1, "actionDenied") > 0 ? (int)row["actionLoss"] : 0;
        }

        /// <summary>JS sleepRestoration: Sleep heals and refills Ward at owner start. No Core 2 enemy or card sleeps the player; refused by name.</summary>
        private void SleepRestoration(Core2Combatant entity)
        {
            if (!entity.Alive || entity.ResolveActive) return;
            if (Stacks(entity, (string)StatusControl["sleep"]["status"]) > 0) throw new NotSupportedException("Sleep restoration (sleepRestoration) is not ported");
        }

        /// <summary>
        /// JS beforeStatusAction: a connected contact carrying Fire thaws the target (its Frost gauge empties; Frozen, Chilled
        /// and Frost clear as thawed).
        /// </summary>
        private void BeforeStatusAction(Core2Combatant target, Core2StatusInteractions interactions, bool connected)
        {
            if (!connected) return;
            var cfg = (JObject)StatusControl["interactions"];
            var profile = interactions.Profile;
            if (interactions.ContainsFire || profile?.School == "fire" || profile?.DamageType == "fire")
            {
                if (target.StatusGauges.TryGetValue((string)cfg["frostGauge"], out var frost)) frost.Value = 0;
                foreach (var id in new[] { (string)cfg["frozen"], (string)cfg["chilled"], (string)cfg["frostGauge"] }) ClearActive(target, id, int.MaxValue, "thawed");
            }
        }

        /// <summary>
        /// JS completeStatusAction: once per action key and owner cycle, a connected damaging action wakes a sleeper, Blunt
        /// shatters one Frozen and one Chilled stack, and a Sweep or a Sacred/Holy hit reveals (Decoy and Concealed clear).
        /// </summary>
        private void CompleteStatusAction(Core2Combatant target, Core2StatusInteractions interactions, bool connected, string actionKey, bool damaging)
        {
            if (!connected || !damaging) return;
            if (actionKey == null) throw new InvalidOperationException("Status action finalization requires actionKey");
            if (!target.CompletedStatusActions.Add(actionKey)) return;
            var cfg = StatusControl;
            var rules = (JObject)cfg["interactions"];
            var profile = interactions.Profile;
            if (!target.ResolveActive) ClearActive(target, (string)cfg["sleep"]["status"], 1, "woken");
            if (interactions.ContainsBlunt || profile?.DamageType == "blunt")
            {
                ClearActive(target, (string)rules["frozen"], 1, "shattered");
                ClearActive(target, (string)rules["chilled"], 1, "shattered");
            }
            if (interactions.ContainsReveal || profile?.Maneuver == "sweep" || profile?.DamageType == "sacred" || profile?.DamageType == "holy")
                foreach (var id in new[] { (string)rules["decoy"], (string)rules["concealment"] }) ClearActive(target, id, int.MaxValue, "revealed");
        }

        /// <summary>JS row.interactions: the contact profile a status hook reads, and whether a landed contact carried Fire, Blunt or a reveal.</summary>
        private static Core2StatusInteractions StatusInteractions(Core2Carrier carrier, IReadOnlyList<Core2ContactPlan> contacts) => new Core2StatusInteractions
        {
            Profile = contacts.FirstOrDefault()?.Profile ?? carrier?.Profile,
            ContainsFire = contacts.Any(c => c.Amount > 0 && (c.Profile?.School == "fire" || c.Profile?.DamageType == "fire")),
            ContainsBlunt = contacts.Any(c => c.Amount > 0 && c.Profile?.DamageType == "blunt"),
            ContainsReveal = contacts.Any(c => c.Amount > 0 && (c.Profile?.Maneuver == "sweep" || c.Profile?.DamageType == "sacred" || c.Profile?.DamageType == "holy")),
        };

        /// <summary>
        /// JS spellAuthoredStatusBonus: an authored buildup from a card or move in an action group gains
        /// stanceRules.spellStatusStacks while its source stands in a Casting or Spell stance (or a Spell Counter).
        /// </summary>
        private int SpellAuthoredStatusBonus(Core2FightAction action, double authored)
        {
            if (action.Card == null || action.Group == null || !(authored > 0)) return 0;
            var stance = action.Source?.Stance;
            var eligible = stance?.Maneuver == "casting" || stance?.Maneuver == "spell" || stance?.Maneuver == "counter" && stance.Camp == "spell";
            return eligible ? (int)Content.Mechanics["stanceRules"]["spellStatusStacks"] : 0;
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
