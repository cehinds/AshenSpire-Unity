// Core2EnemyInspection.cs — what the enemy inspector shows about one enemy, as data (the UI that renders it is phase 6).
//
// JS at the pin:
//   src/ui/screens/combat.js                 the enemy subject (resources HP / SP / Poise / Block, intent, move set,
//                                            history = performedMoves, traits = definition tags)
//   src/ui/models/EnemyDeckInspectionModel.js enemyDeckInspection (authored deck rows joined to runtime moves by their
//                                            explicit cardHints legacyMove; copies from defaultDeck, unlocks as one
//                                            more identity; current / last played with the paid SP receipt)
//   src/model/enemyMoveCards.js              enemyMoveCards (active, locked, unlock phases, maxConsecutive, tags)
//   src/ui/models/EnemyIntentReadModel.js    enemyIntentRead (a concealed intent names nothing about the move)
// SPEC "Enemy inspection": current and maximum SP, the authored deck or move set, the public current/last action with
// its costs; a move set is not a deck, no draw order is invented, and an unrevealed choice never leaks through here.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2InspectedResource
    {
        public string Label { get; set; }
        public int Value { get; set; }
        /// <summary>Null for an unbounded read (Block).</summary>
        public int? Max { get; set; }
    }

    /// <summary>One authored move as the move set lists it (JS enemyMoveCards row, without its UI prose).</summary>
    public sealed class Core2InspectedMove
    {
        public string MoveId { get; set; }
        public string Name { get; set; }
        public string Intent { get; set; }
        /// <summary>The live action SP (JS enemyMoveSp: combat modifiers and the multi-hit floor included).</summary>
        public int Sp { get; set; }
        public int? BaseDamage { get; set; }
        public int Hits { get; set; }
        public int? BaseBlock { get; set; }
        /// <summary>This is the current, revealed intent (never true while the intent is concealed).</summary>
        public bool Active { get; set; }
        public bool Charging { get; set; }
        public bool Locked { get; set; }
        public int? MaxConsecutive { get; set; }
        public int? DelayTurns { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        /// <summary>The phases that unlock this move (hpBelowPct with its percentage, or the named trigger).</summary>
        public List<string> UnlockedBy { get; set; } = new List<string>();
    }

    /// <summary>One authored deck card (JS enemyDeckInspection row): the card identity, its copies and the runtime move it plays as.</summary>
    public sealed class Core2InspectedCard
    {
        public string CardId { get; set; }
        public string Name { get; set; }
        public int Copies { get; set; }
        public string MoveId { get; set; }
        /// <summary>Action cost in combat (JS enemyMoveSp).</summary>
        public int ActionSp { get; set; }
        /// <summary>The printed card base (authored staminaCost / manaCost).</summary>
        public int CardSp { get; set; }
        public int CardMp { get; set; }
        public bool Active { get; set; }
        public bool Last { get; set; }
        public bool Locked { get; set; }
    }

    public sealed class Core2InspectedPlay
    {
        public Core2InspectedCard Card { get; set; }
        /// <summary>The SP the move paid, from its enemyMoveStarted receipt (totalSp, else spCost); null without a receipt.</summary>
        public int? PaidSp { get; set; }
    }

    public sealed class Core2EnemyInspection
    {
        public string EnemyInstanceId { get; set; }
        public string EnemyId { get; set; }
        public string Name { get; set; }
        /// <summary>HP, SP (current of max), Poise, Block — in the inspector's order.</summary>
        public List<Core2InspectedResource> Resources { get; set; } = new List<Core2InspectedResource>();
        public Core2IntentView Intent { get; set; }
        /// <summary>The remaining planned follow-ups, each "?" unless its own read passed.</summary>
        public List<Core2FollowUpSymbol> FollowUps { get; set; } = new List<Core2FollowUpSymbol>();
        /// <summary>The visible stance (null while the intent is concealed).</summary>
        public string Stance { get; set; }
        public List<KeyValuePair<string, int>> Statuses { get; set; } = new List<KeyValuePair<string, int>>();
        /// <summary>The runtime move set (JS "Move set"): every authored move in authored order.</summary>
        public List<Core2InspectedMove> Moves { get; set; } = new List<Core2InspectedMove>();
        /// <summary>The authored deck (null when the enemy has no authored row): identities and copies, never a draw order.</summary>
        public List<Core2InspectedCard> Deck { get; set; }
        public Core2InspectedPlay Current { get; set; }
        public Core2InspectedPlay Last { get; set; }
        public bool CurrentHidden { get; set; }
        /// <summary>Moves that resolved, oldest first (JS performedMoves; a roll cancelled by a stagger never happened).</summary>
        public List<string> History { get; set; } = new List<string>();
        /// <summary>The definition's tags (JS traits).</summary>
        public List<string> Traits { get; set; } = new List<string>();
    }

    public static class Core2EnemyInspector
    {
        /// <summary>Build the inspector's data for one enemy of a running fight. Reads only; draws no RNG.</summary>
        public static Core2EnemyInspection Inspect(Core2Fight fight, Core2Combatant enemy)
        {
            if (fight == null) throw new ArgumentNullException(nameof(fight));
            if (enemy == null || enemy.Side != Core2Side.Enemy) throw new ArgumentException("Inspect needs an enemy of this fight", nameof(enemy));
            var def = fight.Content.Enemy(enemy.EnemyId);
            var view = Core2Knowledge.Project(enemy, fight.Turn);
            var inspection = new Core2EnemyInspection
            {
                EnemyInstanceId = enemy.Id, EnemyId = enemy.EnemyId, Name = def.Name, Intent = view,
                Stance = view.Hidden ? null : enemy.Stance?.Maneuver, CurrentHidden = view.Hidden,
                History = enemy.PerformedMoves.ToList(), Traits = Core2Json.Strings(def.Raw["tags"]).ToList(),
                Statuses = enemy.Statuses.Where(p => p.Value.Stacks > 0).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, int>(p.Key, p.Value.Stacks)).ToList(),
                FollowUps = Core2Knowledge.RemainingFollowUps(fight, enemy, (enemy.PlannedMoves ?? new List<Core2PlannedMove>()).Select(m => m.MoveId).ToList()),
            };
            inspection.Resources.Add(new Core2InspectedResource { Label = "HP", Value = enemy.Hp, Max = enemy.MaxHp });
            inspection.Resources.Add(new Core2InspectedResource { Label = "SP", Value = enemy.Stamina, Max = enemy.MaxStamina });
            inspection.Resources.Add(new Core2InspectedResource { Label = "Poise", Value = enemy.Poise?.Value ?? 0, Max = enemy.Poise?.Max ?? 0 });
            // JS shows a Ward meter only where the entity carries one; breakMeterVersion 1 (Core 2) retires it.
            if (fight.Rules.WardEnabled && enemy.Ward != null) inspection.Resources.Add(new Core2InspectedResource { Label = "Ward", Value = enemy.Ward.Value, Max = enemy.Ward.Max });
            inspection.Resources.Add(new Core2InspectedResource { Label = "Block", Value = enemy.Block });

            var phases = (def.Raw["phases"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            foreach (var move in def.Moves)
            {
                var raw = move.Raw;
                var locked = ((bool?)raw["locked"] ?? false) && !enemy.UnlockedMoves.Contains(move.Id);
                var active = !view.Hidden && view.MoveId == move.Id;
                inspection.Moves.Add(new Core2InspectedMove
                {
                    MoveId = move.Id, Name = (string)raw["name"] ?? Words(move.Id), Intent = move.Intent,
                    Sp = Core2EnemyMoves.MoveSp(fight.Ctx, enemy, raw, move.Id),
                    BaseDamage = Core2EnemyMoves.EnemyMoveDamage(enemy, raw), Hits = move.Hits,
                    BaseBlock = raw["block"] != null && raw["block"].Type != JTokenType.Null ? (int)raw["block"] : (int?)null,
                    Active = active, Charging = active && view.Pending, Locked = locked,
                    MaxConsecutive = (int?)raw["maxConsecutive"], DelayTurns = (int?)raw["delay"]?["turns"], Tags = move.Tags.ToList(),
                    UnlockedBy = phases.Where(p => Core2Json.Strings(p["unlockMoves"]).Contains(move.Id))
                        .Select(p => (string)p["on"] == "hpBelowPct" ? "hpBelowPct:" + (string)p["pct"] : (string)p["on"]).ToList(),
                });
            }

            var authored = (fight.Content.Table("pack/enemies.json") as JArray)?.OfType<JObject>().FirstOrDefault(r => (string)r["id"] == enemy.EnemyId);
            if (authored == null) return inspection;
            var copies = new List<KeyValuePair<string, int>>();
            void Count(string cardId, bool unlock)
            {
                var at = copies.FindIndex(p => p.Key == cardId);
                if (at >= 0) { if (!unlock) copies[at] = new KeyValuePair<string, int>(cardId, copies[at].Value + 1); }
                else copies.Add(new KeyValuePair<string, int>(cardId, 1));
            }
            foreach (var cardId in Core2Json.Strings(authored["defaultDeck"])) Count(cardId, false);
            // An unlock is an available additional identity, not another copy of an already owned card.
            foreach (var cardId in Core2Json.Strings(authored["unlockCards"])) Count(cardId, true);
            var lastMoveId = enemy.PerformedMoves.Count > 0 ? enemy.PerformedMoves[enemy.PerformedMoves.Count - 1] : null;
            inspection.Deck = new List<Core2InspectedCard>();
            foreach (var pair in copies)
            {
                var card = fight.Content.EnemyCards.FirstOrDefault(c => c.Id == pair.Key);
                var moveId = (string)authored["cardHints"]?[pair.Key]?["legacyMove"];
                var move = moveId == null ? null : inspection.Moves.FirstOrDefault(m => m.MoveId == moveId);
                if (card == null || move == null) continue;
                inspection.Deck.Add(new Core2InspectedCard
                {
                    CardId = pair.Key, Name = card.Name, Copies = pair.Value, MoveId = moveId, ActionSp = move.Sp, CardSp = card.StaminaCost, CardMp = card.ManaCost,
                    Active = !view.Hidden && view.MoveId == moveId, Last = moveId == lastMoveId, Locked = move.Locked,
                });
            }
            var current = view.Hidden ? null : inspection.Deck.FirstOrDefault(c => c.Active);
            inspection.Current = current == null ? null : new Core2InspectedPlay { Card = current };
            var last = inspection.Deck.FirstOrDefault(c => c.Last);
            if (last != null)
            {
                var receipt = fight.Events.LastOrDefault(e => e.Type == "enemyMoveStarted" && (string)e.Data["sourceId"] == enemy.Id && (string)e.Data["moveId"] == lastMoveId);
                inspection.Last = new Core2InspectedPlay { Card = last, PaidSp = receipt == null ? (int?)null : (int?)receipt.Data["actionPreview"]?["totalSp"] ?? (int?)receipt.Data["actionPreview"]?["spCost"] };
            }
            return inspection;
        }

        /// <summary>JS words(): camelCase id to a spaced, capitalised label.</summary>
        private static string Words(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var text = System.Text.RegularExpressions.Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ').Replace('-', ' ');
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
