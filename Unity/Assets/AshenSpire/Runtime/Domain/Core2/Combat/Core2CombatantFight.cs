// Core2CombatantFight.cs — the per-combatant state a whole Core 2 fight keeps beyond the rules of one hit (phase 5c):
// an enemy's intent, saved plan and history, its Perception read; a player's counters and relic trigger ledger.
//
// JS at the pin (field names): src/model/state.js createEnemyCombatEntity (movesHistory = rolls, performedMoves = moves that
// resolved, intent, pendingMove, skipNextTurn, unlockedMoves), src/engine/combat.js buildIntent / rollIntents
// (plannedMoves, planDue), src/engine/enemyKnowledge.js rollEnemyKnowledge (knowledgeAction), src/engine/combat.js
// createCombat (player counters).
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>What an enemy telegraphs (JS buildIntent): the move, its damage packet and its combat profile.</summary>
    public sealed class Core2Intent
    {
        /// <summary>attack, block, buff, debuff… (the move's authored intent), or staggered / unknown.</summary>
        public string Kind { get; set; }
        public string MoveId { get; set; }
        public int? Damage { get; set; }
        public int? Hits { get; set; }
        public int? Block { get; set; }
        public int? Barrier { get; set; }
        public bool Delayed { get; set; }
        /// <summary>A charging move committed and waiting to resolve.</summary>
        public bool Pending { get; set; }
        public Core2CombatProfile Profile { get; set; }
        /// <summary>JS counterDefensePrimed: a Counter telegraph already laid down its Block and Barrier.</summary>
        public bool CounterDefensePrimed { get; set; }

        public Core2Intent Clone() { var copy = (Core2Intent)MemberwiseClone(); copy.Profile = Profile?.Clone(); return copy; }
        public static Core2Intent Staggered() => new Core2Intent { Kind = "staggered" };
    }

    /// <summary>One planned follow-up and the SP the plan priced it at (JS plannedMoves[] { moveId, sp }).</summary>
    public sealed class Core2PlannedMove
    {
        public string MoveId { get; set; }
        public int Sp { get; set; }
        public Core2PlannedMove Clone() => new Core2PlannedMove { MoveId = MoveId, Sp = Sp };
    }

    /// <summary>A committed charging move (JS pendingMove): it resolves on resolveOnTurn with the SP it already paid.</summary>
    public sealed class Core2PendingMove
    {
        public string MoveId { get; set; }
        public int ResolveOnTurn { get; set; }
        public int? PaidSp { get; set; }
        public int? PaidAdvanceSp { get; set; }
        public int? PaidTotalSp { get; set; }
        public Core2PendingMove Clone() => (Core2PendingMove)MemberwiseClone();
    }

    /// <summary>One observer's Perception read of one enemy action (JS rollKnowledgeRead, FixCore branch).</summary>
    public sealed class Core2PerceptionRead
    {
        /// <summary>exact (the move is shown) or unknown ("?").</summary>
        public string Visibility { get; set; }
        public string Label { get; set; }
        public int Roll { get; set; }
        public int Wisdom { get; set; }
        public int Dc { get; set; }
        public int Total { get; set; }
        public bool Passed { get; set; }
        public string Prediction { get; set; }
        public bool Resolved { get; set; }
        public bool? Correct { get; set; }
        public Core2PerceptionRead Clone() => (Core2PerceptionRead)MemberwiseClone();
    }

    /// <summary>The selected enemy action every observer reads once (JS knowledgeAction).</summary>
    public sealed class Core2KnowledgeAction
    {
        public int Serial { get; set; }
        /// <summary>The prediction category (Attack, Smash, Defend, Counter, Spell, Casting, Preparing…).</summary>
        public string Category { get; set; }
        public bool Executed { get; set; }
        public bool Cancelled { get; set; }
        public Dictionary<string, Core2PerceptionRead> Reads { get; set; } = new Dictionary<string, Core2PerceptionRead>(StringComparer.Ordinal);
        public Core2KnowledgeAction Clone() => new Core2KnowledgeAction
        {
            Serial = Serial, Category = Category, Executed = Executed, Cancelled = Cancelled,
            Reads = Reads.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal),
        };
    }

    public sealed partial class Core2Combatant
    {
        // ---- enemy (JS createEnemyCombatEntity and the Core 2 plan) -------------------------------------------------
        /// <summary>JS movesHistory: every pick the plan made (maxConsecutive reads it), including picks a stagger cancelled.</summary>
        public List<string> MovesHistory { get; set; } = new List<string>();
        /// <summary>JS performedMoves: the moves that resolved, oldest first (the inspector's previous actions).</summary>
        public List<string> PerformedMoves { get; set; } = new List<string>();
        public Core2Intent Intent { get; set; }
        /// <summary>JS plannedMoves: the saved plan's follow-ups after the head, in play order.</summary>
        public List<Core2PlannedMove> PlannedMoves { get; set; }
        public Core2PendingMove PendingMove { get; set; }
        public bool SkipNextTurn { get; set; }
        public List<string> UnlockedMoves { get; set; } = new List<string>();
        /// <summary>JS planDue: the plan's follow-ups play this enemy phase.</summary>
        public bool PlanDue { get; set; }
        public Core2KnowledgeAction KnowledgeAction { get; set; }
        /// <summary>JS actorIntentRevealed: the acting enemy's current move is public while it acts.</summary>
        public bool ActorIntentRevealed { get; set; }
        /// <summary>JS matchingStanceCardMultiplier: 0.25 while the last accepted carrier matched the stance, else 0.</summary>
        public double MatchingStanceCardMultiplier { get; set; }
        public int MaxHpRolled { get; set; }
        /// <summary>JS enemy.damageMult (createCombat enemyDamageMult: a boss's tier damage scale): authored move damage × it.</summary>
        public double DamageMult { get; set; } = 1;
        /// <summary>JS statusControl.lastRecoveryCycle: the owner cycle whose status recovery already rolled.</summary>
        public int LastRecoveryCycle { get; set; } = -1;

        // ---- status control (JS entity.statusControl, combatStatusControl.js) ----------------------------------------
        /// <summary>JS statusControl.gauges: each pressure status's pending buildup, the camp that last filled it and its recovery profile.</summary>
        public Dictionary<string, Core2StatusGauge> StatusGauges { get; set; } = new Dictionary<string, Core2StatusGauge>(StringComparer.Ordinal);
        /// <summary>JS statusControl.resolveNextTurn: a lock was waited out at this owner's turn end; the next owner turn acts freely.</summary>
        public bool ResolveNextTurn { get; set; }
        /// <summary>JS statusControl.resolveActive: this owner turn ignores control locks (and Frozen's action loss).</summary>
        public bool ResolveActive { get; set; }
        /// <summary>JS statusControl.completedActions: the action keys completeStatusAction already finished this owner cycle.</summary>
        public HashSet<string> CompletedStatusActions { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>
        /// JS entity.poiseMeter: the legacy Poise vessel a physical status-pressure fill strains (applyStatusPressure). A
        /// player's is stamped by the run (createPlayerCombatEntity stampPlayerPoiseMax); null when the entity has none.
        /// </summary>
        public Core2Pool PoiseMeter { get; set; }

        // ---- player (JS player.counters, abilityRiders) ---------------------------------------------------------------
        public int CardsPlayedThisTurn { get; set; }
        public int AttacksPlayedThisCombat { get; set; }
        public int StaminaSpentThisTurn { get; set; }
        public List<string> RelicIds { get; set; } = new List<string>();
        /// <summary>The draw a turn starts with (JS drawPerTurn / handMax under Core 2: 4 + INT).</summary>
        public int HandSize { get; set; }

        partial void CloneFightState(Core2Combatant copy)
        {
            copy.MovesHistory = new List<string>(MovesHistory);
            copy.PerformedMoves = new List<string>(PerformedMoves);
            copy.Intent = Intent?.Clone();
            copy.PlannedMoves = PlannedMoves?.Select(m => m.Clone()).ToList();
            copy.PendingMove = PendingMove?.Clone();
            copy.UnlockedMoves = new List<string>(UnlockedMoves);
            copy.KnowledgeAction = KnowledgeAction?.Clone();
            copy.RelicIds = new List<string>(RelicIds);
            copy.StatusGauges = StatusGauges.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal);
            copy.CompletedStatusActions = new HashSet<string>(CompletedStatusActions, StringComparer.Ordinal);
            copy.PoiseMeter = PoiseMeter?.Clone();
        }
    }

    /// <summary>One pressure gauge (JS statusControl.gauges[id]): pending buildup below the next threshold.</summary>
    public sealed class Core2StatusGauge
    {
        public double Value { get; set; }
        /// <summary>physical or spell: the camp of the application that last filled it (it picks the defense a fill meets).</summary>
        public string Camp { get; set; }
        /// <summary>bodily, elemental, mental or curse: which attributes its chance recovery reads.</summary>
        public string RecoveryProfile { get; set; }
        public Core2StatusGauge Clone() => (Core2StatusGauge)MemberwiseClone();
    }

    /// <summary>JS applyStatusPressure's receipt.</summary>
    public sealed class Core2PressureReceipt
    {
        public double AcceptedBuildup { get; set; }
        public int ActiveAdded { get; set; }
        public double Resisted { get; set; }
        public int WardSpent { get; set; }
        public int PoiseStrain { get; set; }
        public bool FullyResisted { get; set; } = true;
    }

    /// <summary>JS controlRestrictions: the active lock statuses and what they disable this owner turn.</summary>
    public sealed class Core2ControlRestrictions
    {
        public bool Locked { get; set; }
        public List<string> Locks { get; set; } = new List<string>();
        public bool CounterDisabled { get; set; }
        public bool EvadeDisabled { get; set; }
        public bool ResolveActive { get; set; }
    }
}
