// Core2Knowledge.cs — Perception and intent concealment (SPEC §17 FixCore D8; 2026-10-22 amendment): each enemy action
// the player can see is read once, as it is presented; a failed read shows "?" and nothing about the move, and a planned
// follow-up shows its icon only when its own read passed.
//
// JS at the pin:
//   src/model/enemyIntentKnowledge.js rollKnowledgeRead (FixCore: d20 on enemyIntentVisibility + observer WIS against
//                                     DC = baseDC + target INT + target WIS + bonuses; one enemyIntentClue draw is spent to
//                                     keep that stream in step), predictionCategory, concealKnowledgeIntent
//   src/engine/enemyKnowledge.js      rollEnemyKnowledge (one serial per presented action), knowledgeIntentProjection,
//                                     resolveKnowledgeAction, cancelKnowledgeAction
//   src/engine/combat.js              intentReadGate / previewIntent (an exact read, an acting enemy or a Staggered intent
//                                     is public), perceptionRead (the follow-up trace's per-move read)
//   src/ui/models/IntentFollowUpModel.js remainingFollowUps (the "?" rule for remaining planned moves)
// Perception XP, predictions and the run's knowledge ledger are run-layer work (phase 8); a read here never pays XP.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>What the observer may see of an enemy's current intent (JS previewIntent's public fields).</summary>
    public sealed class Core2IntentView
    {
        public bool Hidden { get; set; }
        /// <summary>"?" when the read failed; the category (Attack, Defend…) or "Staggered" when exact.</summary>
        public string Label { get; set; }
        /// <summary>exact or unknown (JS knowledgeRead).</summary>
        public string KnowledgeRead { get; set; }
        public int ActionSerial { get; set; }
        /// <summary>Null when hidden: a failed read leaves nothing about the move in the view.</summary>
        public string MoveId { get; set; }
        public string Kind { get; set; }
        public int? Damage { get; set; }
        public int? Hits { get; set; }
        public int? Block { get; set; }
        public string Stance { get; set; }
        public bool Pending { get; set; }
    }

    /// <summary>One remaining follow-up as the intent row shows it: an icon kind when its own read passed, else unknown.</summary>
    public sealed class Core2FollowUpSymbol
    {
        /// <summary>action or unknown.</summary>
        public string Kind { get; set; }
        /// <summary>The move's intent stance when read (attacking, defending, casting…); null for unknown.</summary>
        public string Stance { get; set; }
    }

    /// <summary>One follow-up's record in the enemy phase (JS combat.followTrace[enemyId] entry): its read and what became of it.</summary>
    public sealed class Core2FollowTrace
    {
        public bool Read { get; set; }
        /// <summary>played, charging or refused.</summary>
        public string Outcome { get; set; }
    }

    public static class Core2Knowledge
    {
        public const string Observer = "player";

        /// <summary>JS predictionCategory.</summary>
        public static string PredictionCategory(Core2Intent intent, bool charging = false)
        {
            if (intent?.Kind == "staggered") return "Staggered";
            var profile = intent?.Profile ?? new Core2CombatProfile();
            var maneuver = profile.Maneuver;
            if (maneuver == "defend" || intent?.Kind == "block" || intent?.Kind == "defend") return "Defend";
            if (maneuver == "counter" || intent?.Kind == "counter") return "Counter";
            if (charging || intent?.Kind == "casting") return "Casting";
            if (profile.Camp == "spell" || profile.Camp == "spells" || profile.Camp == "magic") return "Spell";
            switch (maneuver) { case "attack": return "Attack"; case "smash": return "Smash"; case "sweep": return "Sweep"; case "ranged": return "Ranged"; }
            if (intent?.Kind == "attack" || intent?.Kind == "attackDebuff") return "Attack";
            return "Preparing";
        }

        /// <summary>
        /// JS rollKnowledgeRead (FixCore): roll = 1 + floor(float × 20) on enemyIntentVisibility, then one enemyIntentClue
        /// draw; the read passes when roll + observer WIS ≥ baseDC + target INT + target WIS + bonuses.
        /// </summary>
        public static Core2PerceptionRead RollRead(Core2Rules rules, Core2Rng rng, Core2Combatant observer, Core2Combatant target)
        {
            var roll = 1 + (int)Math.Floor(rng.Float("enemyIntentVisibility") * 20);
            rng.Float("enemyIntentClue");
            var wisdom = observer?.Attribute("wisdom") ?? 0;
            var dc = rules.PerceptionBaseDC + (target?.Attribute("intelligence") ?? 0) + (target?.Attribute("wisdom") ?? 0) + rules.PerceptionBonus;
            var total = roll + wisdom;
            var passed = total >= dc;
            return new Core2PerceptionRead { Visibility = passed ? "exact" : "unknown", Label = passed ? null : "?", Roll = roll, Wisdom = wisdom, Dc = dc, Total = total, Passed = passed };
        }

        /// <summary>JS rollEnemyKnowledge: a new serial for the presented action and one read per living observer.</summary>
        public static Core2KnowledgeAction Roll(Core2Fight fight, Core2Combatant enemy, bool charging)
        {
            var action = new Core2KnowledgeAction { Serial = ++fight.KnowledgeSerial, Category = PredictionCategory(enemy.Intent, charging) };
            if (fight.Player != null && fight.Player.Alive) action.Reads[Observer] = RollRead(fight.Rules, fight.Rng, fight.Player, enemy);
            enemy.KnowledgeAction = action;
            return action;
        }

        /// <summary>JS cancelKnowledgeAction: a staggered or skipped action never plays; its reads resolve with no result.</summary>
        public static bool Cancel(Core2Combatant enemy)
        {
            var action = enemy?.KnowledgeAction;
            if (action == null || action.Executed || action.Cancelled) return false;
            action.Cancelled = true;
            foreach (var read in action.Reads.Values) { read.Resolved = true; read.Correct = null; }
            return true;
        }

        /// <summary>JS resolveKnowledgeAction: the action played (a prediction would be scored here; phase 8 pays its XP).</summary>
        public static void Resolve(Core2Combatant enemy)
        {
            var action = enemy?.KnowledgeAction;
            if (action == null || action.Executed || action.Cancelled || !enemy.Alive) return;
            if (enemy.Intent?.Kind == "staggered") { Cancel(enemy); return; }
            action.Executed = true;
            foreach (var read in action.Reads.Values) { read.Resolved = true; read.Correct = read.Prediction == null ? (bool?)null : read.Prediction == action.Category; }
        }

        /// <summary>JS perceptionRead: whether the observer's own read of the enemy's current move succeeded.</summary>
        public static bool PerceptionRead(Core2Combatant enemy) =>
            enemy?.KnowledgeAction != null && enemy.KnowledgeAction.Reads.TryGetValue(Observer, out var read) && read.Visibility == "exact";

        /// <summary>JS combatIntentStance: the stance word an intent shows (attacking, defending, casting…).</summary>
        public static string IntentStance(Core2Intent intent)
        {
            if (intent?.Kind == "staggered") return "staggered";
            var profile = intent?.Profile ?? new Core2CombatProfile();
            if (profile.Maneuver == "counter") return "countering";
            if (profile.Maneuver == "defend") return "defending";
            if (profile.Maneuver == "sweep") return "sweeping";
            if (profile.Camp == "spell" || profile.Camp == "spells" || profile.Camp == "magic") return "casting";
            if (profile.Maneuver == "ranged") return "ranged";
            if (profile.Maneuver == "smash") return "smashing";
            if (intent?.Kind == "block" || intent?.Kind == "defend") return "defending";
            if (profile.Maneuver == "attack" || intent?.Kind == "attack" || intent?.Kind == "attackDebuff") return "attacking";
            return "preparing";
        }

        /// <summary>
        /// JS previewIntent behind knowledgeIntentProjection: a Read Intent reveal (when the fight's turn is given), an exact
        /// read, an acting enemy or a Staggered intent shows the
        /// move; a failed read shows "?" with the action serial and nothing else (JS concealKnowledgeIntent).
        /// </summary>
        public static Core2IntentView Project(Core2Combatant enemy, int? turn = null)
        {
            var intent = enemy?.Intent ?? new Core2Intent { Kind = "unknown" };
            var action = enemy?.KnowledgeAction;
            // JS proposalIntentRevealUntil (a Read Intent card, revealIntent): the seat reads the move exactly until the turn it names.
            var revealed = enemy != null && turn != null && enemy.IntentRevealedUntil.TryGetValue(Core2Fight.Seat, out var until) && until > turn.Value;
            var exact = enemy != null && (revealed || enemy.ActorIntentRevealed || intent.Kind == "staggered" || PerceptionRead(enemy));
            if (!exact)
                return new Core2IntentView { Hidden = true, Label = "?", KnowledgeRead = "unknown", ActionSerial = action?.Serial ?? 0, Kind = "unknown", Stance = "unknown" };
            return new Core2IntentView
            {
                Hidden = false, KnowledgeRead = action != null ? "exact" : null, ActionSerial = action?.Serial ?? 0,
                Label = intent.Kind == "staggered" ? "Staggered" : action?.Category, MoveId = intent.MoveId, Kind = intent.Kind,
                Damage = intent.Damage, Hits = intent.Hits, Block = intent.Block, Stance = IntentStance(intent), Pending = enemy.PendingMove != null,
            };
        }

        /// <summary>
        /// JS remainingFollowUps: the enemy's remaining planned follow-ups after <paramref name="cursor"/>, each "action" with
        /// its stance only where its own recorded read passed (the trace the enemy phase wrote), else "unknown".
        /// Before the phase, no follow-up has a read yet, so every one is "?".
        /// </summary>
        public static List<Core2FollowUpSymbol> RemainingFollowUps(Core2Fight fight, Core2Combatant enemy, IReadOnlyList<string> plannedMoveIds, int cursor = 0, int offset = 0)
        {
            var output = new List<Core2FollowUpSymbol>();
            fight.FollowTrace.TryGetValue(enemy.Id, out var trace);
            for (var i = cursor; i < plannedMoveIds.Count; i++)
            {
                var read = trace != null && offset + i < trace.Count && trace[offset + i].Read;
                if (!read) { output.Add(new Core2FollowUpSymbol { Kind = "unknown" }); continue; }
                var raw = fight.Content.Enemy(enemy.EnemyId).Move(plannedMoveIds[i])?.Raw;
                var intent = raw == null ? null : Core2EnemyPlanning.BuildIntent(fight.Rules, enemy, raw, plannedMoveIds[i]);
                output.Add(new Core2FollowUpSymbol { Kind = "action", Stance = IntentStance(intent) });
            }
            return output;
        }
    }
}
