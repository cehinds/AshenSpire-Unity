// Core2AutoCombat.cs — Auto combat (owner ruling D12, 2026-10-18): Auto answers the player's REACTION prompts only. It
// never plays a card and never ends a turn. The chooser is pure and deterministic and reads only what the player can
// see: the incoming action as the player perceives it (a hidden or unread intent is unknown), the maneuver of the player's
// last confirmed card, and the player's own manual reaction picks this run. Pacing is in Core2CombatPacing.cs (Auto
// runs at its own speed, ×1.5 by default).
//
// JS at the pin:
//   src/model/autoCombatPolicy.js     MANEUVER/normalize, perceivedManeuver, matchupRank, chooseReaction, reactionAnswer,
//                                     recordManualReaction / manualReactionPicks (memory only, never saved)
//   src/ui/screens/combat.js          autoReactionChoice (its inputs: the offer source's perceived intent, the stance
//                                     ledger's last maneuver, the seat's picks; reactionKind for each option)
//   src/ui/models/ReactionIncomingModel.js stackedEnemyReactionIntent (a stacked enemy reaction is public)
//   src/ui/models/CombatIntentProjectionModel.js projectCombatIntent → src/engine/combat.js previewIntent (the read gate)
//   src/model/alternativeStance.js    kindForCard / stanceForCard, createStanceLedger (the last confirmed card per actor)
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>An intent as the player perceives it (JS the projected intent's hidden / revealed / profile.maneuver / stance).</summary>
    public sealed class Core2PerceivedIntent
    {
        public bool Hidden { get; set; }
        public bool Revealed { get; set; } = true;
        public string Maneuver { get; set; }
        public string Stance { get; set; }
    }

    /// <summary>What the Auto chooser reads for one offer (JS autoReactionChoice's state.seats[seat]).</summary>
    public sealed class Core2AutoInputs
    {
        /// <summary>Null when the source has no readable intent (dead, or no intent module).</summary>
        public Core2PerceivedIntent Intent { get; set; }
        public string LastStance { get; set; }
        public IReadOnlyDictionary<string, int> Picks { get; set; } = new Dictionary<string, int>();
        /// <summary>Each option's reaction kind (the card's maneuver), in option order.</summary>
        public List<string> Kinds { get; set; } = new List<string>();
        public string Perceived => Core2AutoCombat.PerceivedManeuver(Intent);
    }

    public static class Core2AutoCombat
    {
        private static readonly Dictionary<string, string> ManeuverWords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["casting"] = "prepared", ["preparing"] = "prepared", ["cast"] = "prepared", ["ranged"] = "attack", ["attacking"] = "attack",
            ["smashing"] = "smash", ["sweeping"] = "sweep", ["defending"] = "defend", ["countering"] = "counter", ["evading"] = "evade", ["block"] = "defend",
        };

        /// <summary>JS normalize: a stance word or maneuver to the matchup table's maneuver; null for none.</summary>
        public static string Normalize(string value) => string.IsNullOrEmpty(value) ? null : ManeuverWords.TryGetValue(value, out var word) ? word : value;

        /// <summary>JS perceivedManeuver: the incoming maneuver the player can see, or null when it is hidden or unread.</summary>
        public static string PerceivedManeuver(Core2PerceivedIntent intent)
        {
            if (intent == null || intent.Hidden || !intent.Revealed) return null;
            return Normalize(!string.IsNullOrEmpty(intent.Maneuver) ? intent.Maneuver : intent.Stance);
        }

        /// <summary>JS matchupRank: 2 = the reaction beats the perceived action, 1 = neutral or unknown, 0 = the action beats it.</summary>
        private static int MatchupRank(string kind, string incoming, IReadOnlyDictionary<string, IReadOnlyList<string>> table)
        {
            if (kind == null || incoming == null) return 1;
            if (table != null && table.TryGetValue(kind, out var beats) && beats.Contains(incoming)) return 2;
            if (table != null && table.TryGetValue(incoming, out var beaten) && beaten.Contains(kind)) return 0;
            return 1;
        }

        /// <summary>
        /// JS chooseReaction: the option whose kind counters the perceived action, then (only while the action is unknown) the
        /// one matching the last stance, then the most often manually picked kind, then option order. Returns the option's
        /// index, or -1 to decline (no option carries a signal). Auto answers a decline with Back, never "do nothing".
        /// </summary>
        public static int Choose(IReadOnlyDictionary<string, IReadOnlyList<string>> matchups, Core2PerceivedIntent intent, string lastStance,
            IReadOnlyDictionary<string, int> picks, IReadOnlyList<string> optionKinds)
        {
            var incoming = PerceivedManeuver(intent);
            var last = Normalize(lastStance);
            int[] best = null; var bestIndex = -1; var bestSignal = false;
            for (var index = 0; index < (optionKinds?.Count ?? 0); index++)
            {
                var kind = Normalize(optionKinds[index]);
                var rank = MatchupRank(kind, incoming, matchups);
                var stance = incoming == null && last != null && kind == last ? 1 : 0;
                var picked = kind != null && picks != null && picks.TryGetValue(kind, out var count) ? Math.Max(0, count) : 0;
                var signal = rank > 0 && (rank == 2 || stance == 1 || picked > 0);
                var key = new[] { rank, stance, picked };
                if (best == null || Beats(key, best)) { best = key; bestIndex = index; bestSignal = signal; }
            }
            return bestSignal ? bestIndex : -1;
        }

        private static bool Beats(int[] a, int[] b)
        {
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        /// <summary>
        /// JS kindForCard: the player's authored action profile for a confirmed card — casting cards hold no maneuver,
        /// defensive ones Defend, offensive ones their Attack/Smash/Ranged (else Attack), Counter and Sweep their own.
        /// </summary>
        public static string KindForCard(Core2Carrier card)
        {
            if (card == null) return null;
            var profile = card.Profile ?? new Core2CombatProfile();
            var stance = StanceForCard(card, profile);
            if (stance == "casting") return null;
            if (stance == "defensive") return "defend";
            if (stance == "offensive") return profile.Maneuver == "attack" || profile.Maneuver == "smash" || profile.Maneuver == "ranged" ? profile.Maneuver : "attack";
            return stance ?? profile.Maneuver;
        }

        private static readonly HashSet<string> Offensive = new HashSet<string>(new[] { "attack", "smash", "sweep", "ranged", "attacking", "smashing", "sweeping" }, StringComparer.Ordinal);

        /// <summary>JS stanceForCard (presentation family of a confirmed card).</summary>
        private static string StanceForCard(Core2Carrier card, Core2CombatProfile profile)
        {
            if (Core2Stances.PreparationCategory(card) != null) return "casting";
            if (profile.Maneuver == "counter") return "counter";
            if (profile.Maneuver == "defend" || profile.Maneuver == "defending") return "defensive";
            if (profile.Camp == "spell") return "casting";
            if (profile.Maneuver == "sweep") return "sweep";
            if (profile.Maneuver == "smash") return "smash";
            if (profile.Maneuver != null && Offensive.Contains(profile.Maneuver)) return "offensive";
            if (card.Tags.Contains("source:spell") || card.AbilityKind == "spell" || card.Type == "spell") return "casting";
            if (card.Tags.Contains("guard") || card.Tags.Contains("block")) return "defensive";
            if (card.Type == "attack") return "offensive";
            return "casting";
        }

        /// <summary>JS combat screen reactionKind: the option card's combat-profile maneuver, else its at-rest glyph maneuver.</summary>
        public static string ReactionKind(Core2Carrier card) => card?.Profile?.Maneuver ?? KindForCard(card);

        /// <summary>
        /// The chooser's inputs for the pending offer, as the combat screen builds them: the offer's source as the player
        /// perceives it (a stacked enemy reaction is public; otherwise the enemy's intent through the Perception read gate),
        /// the maneuver of the player's last confirmed card, and the given manual picks.
        /// </summary>
        public static Core2AutoInputs Inputs(Core2Fight fight, IReadOnlyDictionary<string, int> picks = null)
        {
            var pending = fight.PendingReaction ?? throw new InvalidOperationException("No reaction is pending");
            var inputs = new Core2AutoInputs { Picks = picks ?? new Dictionary<string, int>() };
            inputs.Intent = StackedEnemyReactionIntent(fight, pending);
            var enemy = fight.Enemies.FirstOrDefault(row => row.Id == pending.SourceId);
            if (inputs.Intent == null && enemy != null && enemy.Alive)
            {
                var view = Core2Knowledge.Project(enemy);
                inputs.Intent = view.Hidden ? new Core2PerceivedIntent { Hidden = true, Revealed = false, Stance = view.Stance }
                    : new Core2PerceivedIntent { Hidden = false, Revealed = true, Maneuver = enemy.Intent?.Profile?.Maneuver, Stance = view.Stance };
            }
            inputs.LastStance = fight.LastConfirmedManeuver();
            foreach (var option in pending.Options)
            {
                var inst = fight.Hand.FirstOrDefault(card => card.InstanceId == option.Play?.CardInstanceId);
                inputs.Kinds.Add(inst == null ? null : ReactionKind(fight.CardCarrier(inst)));
            }
            return inputs;
        }

        /// <summary>JS stackedEnemyReactionIntent: the offer answers a stacked enemy reaction, whose maneuver is public.</summary>
        private static Core2PerceivedIntent StackedEnemyReactionIntent(Core2Fight fight, Core2ReactionOffer pending)
        {
            var stack = fight.ReactionStack;
            if (stack == null || stack.GroupKey != pending.GroupKey) return null;
            var entry = stack.Entries.LastOrDefault(row => row.Side == "enemy" && row.OwnerId == pending.SourceId && (row.TargetPlayerId == null || row.TargetPlayerId == Core2Fight.Seat));
            if (entry == null) return null;
            var enemy = fight.Enemies.FirstOrDefault(row => row.Id == entry.OwnerId);
            var move = enemy == null ? null : fight.Content.Enemy(entry.EnemyId ?? enemy.EnemyId).Move(entry.MoveId)?.Raw;
            if (move == null) return new Core2PerceivedIntent { Hidden = true, Revealed = false };
            var maneuver = Core2EnemyMoves.Carrier(fight.Rules, move, entry.MoveId)?.Profile?.Maneuver;
            return maneuver != null ? new Core2PerceivedIntent { Hidden = false, Revealed = true, Maneuver = maneuver } : new Core2PerceivedIntent { Hidden = true, Revealed = false };
        }

        /// <summary>
        /// Auto's answer to the pending offer (JS reactionAnswer(pending, chooseReaction(...))): the chosen option's id, or
        /// null — a deliberate Back, so the prompt always resolves.
        /// </summary>
        public static string Answer(Core2Fight fight, IReadOnlyDictionary<string, int> picks = null)
        {
            var inputs = Inputs(fight, picks);
            var index = Choose(fight.Rules.StanceMatchups, inputs.Intent, inputs.LastStance, inputs.Picks, inputs.Kinds);
            return index < 0 ? null : fight.PendingReaction.Options[index].Id;
        }
    }

    /// <summary>
    /// JS recordManualReaction / manualReactionPicks: the player's manual reaction picks, per run and in memory only (they
    /// never enter a save, so they cannot change gameplay; a reloaded run starts its tally again).
    /// </summary>
    public sealed class Core2ManualReactionPicks
    {
        private readonly Dictionary<string, int> _picks = new Dictionary<string, int>(StringComparer.Ordinal);
        public void Record(string kind)
        {
            var key = Core2AutoCombat.Normalize(kind);
            if (key == null) return;
            _picks[key] = (_picks.TryGetValue(key, out var count) ? count : 0) + 1;
        }
        public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>(_picks, StringComparer.Ordinal);
    }
}
