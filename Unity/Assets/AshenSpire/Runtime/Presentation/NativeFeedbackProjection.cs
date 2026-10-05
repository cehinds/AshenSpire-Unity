// NativeFeedbackProjection.cs — translates completed solo combat receipts into presentation.
// EDIT: cue precedence and receipt labels here; tune poses/audio in campaign.json/Feedback.
// INPUT: one accepted command's events, never the cumulative combat event log.
// Pure projection: no Unity dependency, RNG, save mutation, timers or authored effect estimates.
// The existing CombatFeedback timeline owns reduced/fast motion; GameAudio owns mute/pause.
using System;
using AshenSpire.Domain;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation
{
    public sealed class NativeFeedbackProjection
    {
        public string CueId { get; private set; }
        public bool EnemyTurn { get; private set; }
        public FeedbackOutcome Outcome { get; private set; }
        public bool AnimatePlayer { get; private set; } = true;
        public string EnemyTargetId { get; private set; }

        public static NativeFeedbackProjection FromCoopEvents(IEnumerable<JToken> events, string memberId)
        {
            if (events == null || string.IsNullOrEmpty(memberId)) return null;
            var rows = events.OfType<JObject>().Where(row =>
                (string)row["targetId"] == "player" ? ((string)row["playerId"] ?? (string)row["actorMemberId"]) == memberId
                : row["targetId"] != null || row["actorMemberId"] == null || (string)row["actorMemberId"] == memberId
                    || (string)row["type"] == "enemyTurnStart" || (string)row["type"] == "enemyMoveStarted")
                .Select(row => (JObject)row.DeepClone()).ToArray();
            var local = rows.Any(row => (string)row["actorMemberId"] == memberId || (string)row["playerId"] == memberId);
            // Server receipts use the solo ID "player" plus member annotations.
            // Never animate this seat as the attacker for another member's command.
            foreach (var row in rows) if ((string)row["sourceId"] == "player" && (string)row["actorMemberId"] != memberId) row["sourceId"] = "ally";
            var result = FromEvents(rows);
            if (result != null) result.AnimatePlayer = local;
            return result;
        }

        /// <summary>Returns null for housekeeping-only or empty receipts. Solo engine IDs apply.</summary>
        public static NativeFeedbackProjection FromEvents(IEnumerable<JToken> events)
        {
            if (events == null) return null;
            var rows = events.OfType<JObject>().ToArray();
            bool Has(string type) => rows.Any(row => (string)row["type"] == type);
            bool Player(JObject row) => (string)row["targetId"] == "player";
            int Amount(JObject row) => Math.Max(0, (int?)row["amount"] ?? 0);
            int Total(string type, Func<JObject, bool> predicate) => rows.Where(row => (string)row["type"] == type && predicate(row)).Sum(Amount);
            var outcome = new FeedbackOutcome
            {
                Damage = Total("hpLost", row => !Player(row) && row["targetId"] != null),
                Hurt = Total("hpLost", Player),
                Healing = Total("healed", Player),
                Block = Total("blockGained", Player)
            };
            var enemyTurn = Has("enemyTurnStart") || Has("enemyMoveStarted");
            var playerAttack = rows.Any(row => (string)row["type"] == "damageDealt" && (string)row["sourceId"] == "player" && !Player(row));
            var enemyAttack = rows.Any(row => (string)row["type"] == "damageDealt" && (string)row["sourceId"] != "player" && Player(row));
            var selfCost = Total("hpLost", row => Player(row) && (string)row["cause"] != "attack");
            var mana = Total("manaRestored", Player);
            var stamina = Total("staminaRecovered", row => true) + Total("energyGained", row => (bool?)row["turnStamina"] == true);
            var energy = Total("energyGained", row => (bool?)row["turnStamina"] != true);
            var equipment = Has("equipmentChanged");
            var flask = Has("flaskUsed") || Has("flaskThrown");
            var status = Has("statusApplied") || Has("statusExpired") || Has("stanceEntered") || Has("enemyStaggered");
            var relevant = enemyTurn || playerAttack || enemyAttack || equipment || flask || status || Has("cardPlayed") || outcome.Damage + outcome.Hurt + outcome.Healing + outcome.Block + mana + stamina + energy > 0;
            if (!relevant) return null;

            // One command can attack AND spend HP. The player still attacks; health cost is text.
            // Enemy turns can contain poison ticks and idle recovery; those never animate a player attack.
            var cue = enemyTurn || enemyAttack ? (outcome.Hurt > 0 ? "hit" : "guard")
                : playerAttack ? "attack"
                : outcome.Healing + mana + stamina > 0 ? "heal"
                : outcome.Hurt > 0 ? "hit" : "guard";
            var labels = new List<string>();
            if (equipment) labels.Add("Equipment changed");
            if (enemyTurn) labels.Add("Enemy turn");
            else if (playerAttack) labels.Add("Attack");
            else if (flask) labels.Add("Flask used");
            else if (stamina > 0) labels.Add("Recover");
            if (selfCost > 0) labels.Add("Effect health loss " + selfCost);
            if (mana > 0) labels.Add("MANA +" + mana);
            if (stamina > 0) labels.Add("STAMINA +" + stamina);
            if (energy > 0) labels.Add("ACTIONS +" + energy);
            foreach (var row in rows.Where(row => (string)row["type"] == "statusApplied"))
            {
                var stacks = Math.Max(0, (int?)row["stacks"] ?? 0);
                if (stacks > 0) labels.Add((Player(row) ? "SELF " : "FOE ") + Label((string)row["status"]) + " +" + stacks);
            }
            foreach (var row in rows.Where(row => (string)row["type"] == "statusExpired"))
                labels.Add((Player(row) ? "SELF " : "FOE ") + Label((string)row["status"]) + " removed");
            if (Has("enemyStaggered")) labels.Add("Foe staggered");
            foreach (var row in rows.Where(row => (string)row["type"] == "stanceEntered")) labels.Add("Stance: " + Label((string)row["stance"]));
            if (labels.Count == 0 && Has("cardPlayed")) labels.Add("Card played");
            outcome.Action = string.Join(" · ", labels.Distinct());
            return new NativeFeedbackProjection { CueId = cue, EnemyTurn = enemyTurn || enemyAttack, Outcome = outcome,
                EnemyTargetId = rows.FirstOrDefault(row => (string)row["type"] == "hpLost" && !Player(row))?["targetId"]?.ToString() };
        }

        private static string Label(string id) => Regex.Replace(id ?? "status", "([a-z])([A-Z])", "$1 $2").Replace('_', ' ');
    }
}
