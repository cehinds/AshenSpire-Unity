// Core2FightZones.cs — card-zone effects in a Core 2 fight: moving cards between the draw, hand, discard and exhaust piles
// (first, random or chosen), transforming them, modifying a card in place (its maneuver, its range, Retain, hiding it for
// some turns), the chosen-card pause a choose selection opens, and the hidden cards coming back.
//
// JS at the pin:
//   src/engine/cardZones.js      hasCardChoice, zoneCandidates (the source pile less the player's stacked reaction cards,
//                                the first `look`), validateZoneEffect, applyMove (modifyCards: proposalChanges, retain
//                                until, hideTurns; moveCards: out of the source, transformTo, into the destination — a full
//                                hand sends it to the discard — at the top, bottom or a random slot on `shuffle`; the
//                                cardExhausted / cardDiscarded / cardMoved receipt), moveCards (choose opens a
//                                pendingCardChoice), chooseZoneCards, returnHiddenCards
//   src/engine/actions.js        runOpcode 'moveCards' / 'modifyCards' (a formula hideTurns resolved, 1–10)
//   src/engine/combat.js         dispatch 'chooseCards' (apply, drain, finish the play), drainQueue (a choice pauses it),
//                                startPlayerTurn (returnHiddenCards first)
//   src/engine/cardBehavior.js   projectCardBehavior (playLimit → unplayable; proposalChanges range / maneuver / retain;
//                                costGrowth)
//   src/engine/handRules.js      endTurnCardFate (a Retain change keeps the card while it lasts)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>A chosen-card selection the fight waits on (JS pendingCardChoice).</summary>
    public sealed class Core2CardChoice
    {
        public string Kind { get; set; } = "moveCards";
        public int Count { get; set; }
        public JObject Effect { get; set; }
        public List<string> CandidateIds { get; set; } = new List<string>();
        public List<Core2CardInstance> Candidates { get; set; } = new List<Core2CardInstance>();
        /// <summary>The card whose effect asks (JS pendingAbilityPlay.instance.cardId).</summary>
        public string CardId { get; set; }
    }

    public sealed partial class Core2CardInstance
    {
        /// <summary>JS card.proposalChanges: maneuver, range, retain (with retainUntilTurn) a modifyCards effect gave this card.</summary>
        public JObject Changes { get; set; }

        /// <summary>A separate instance (same id): a preview copy's modifyCards or transform must not touch the live card.</summary>
        public Core2CardInstance Clone() => new Core2CardInstance { InstanceId = InstanceId, CardId = CardId, Changes = (JObject)Changes?.DeepClone() };
        public static List<Core2CardInstance> CloneAll(IEnumerable<Core2CardInstance> cards) => cards.Select(c => c.Clone()).ToList();
    }

    public sealed partial class Core2Fight
    {
        private static readonly string[] Zones = { "draw", "hand", "discard", "exhaust" };

        /// <summary>The chosen-card selection waiting on ChooseCards, or null (JS pendingCardChoice).</summary>
        public Core2CardChoice PendingCardChoice { get; private set; }

        /// <summary>JS hasCardChoice: a choice pauses the queue and every other command.</summary>
        public bool HasCardChoice => PendingCardChoice != null;

        /// <summary>JS hiddenCards: cards a hideTurns change took out of play, and the turn each returns.</summary>
        private readonly List<(Core2CardInstance Card, int ReturnTurn)> _hiddenCards = new List<(Core2CardInstance, int)>();

        private void RequireNoCardChoice()
        {
            if (HasCardChoice) throw new InvalidOperationException("Choose the cards to discard before taking another action");
        }

        private List<Core2CardInstance> Pile(string zone) => zone switch
        {
            "draw" => Draw, "hand" => Hand, "discard" => Discard, "exhaust" => Exhaust,
            _ => throw new InvalidOperationException("Unknown card source zone"),
        };

        /// <summary>JS zoneCandidates: the source pile less the player's cards waiting on the reaction stack, its first `look`.</summary>
        private List<Core2CardInstance> ZoneCandidates(JObject effect)
        {
            var pile = Pile((string)effect["from"]);
            var stacked = new HashSet<string>((ReactionStack?.Entries ?? new List<Core2ReactionEntry>()).Where(e => e.Side == "player" && e.OwnerId == Seat)
                .Select(e => e.Play?.CardInstanceId).Where(id => id != null), StringComparer.Ordinal);
            var candidates = pile.Where(c => !stacked.Contains(c.InstanceId)).ToList();
            return effect["look"] != null ? candidates.Take((int)effect["look"]).ToList() : candidates;
        }

        /// <summary>JS validateZoneEffect: the closed card-zone fields and values.</summary>
        private static void ValidateZoneEffect(JObject effect)
        {
            var op = (string)effect["op"];
            var allowed = new[] { "op", "from", "to", "amount", "selection", "position", "look", "transformTo", "changes", "target", "if", "repeat", "oncePerTurn" };
            if (effect.Properties().Any(p => !allowed.Contains(p.Name))) throw new InvalidOperationException("Unknown card-zone field");
            if (!Zones.Contains((string)effect["from"]) || op != "modifyCards" && !Zones.Contains((string)effect["to"])) throw new InvalidOperationException("Unknown card zone");
            var selection = (string)effect["selection"] ?? "first";
            if (selection != "first" && selection != "random" && selection != "choose") throw new InvalidOperationException("Unknown card selection");
            if (op == "modifyCards")
            {
                if (!(effect["changes"] is JObject changes)) throw new InvalidOperationException("Card changes must be an object");
                foreach (var p in changes.Properties())
                    if (!new[] { "retain", "retainTurns", "hideTurns", "maneuver", "range" }.Contains(p.Name)) throw new InvalidOperationException("Unknown card change");
                if (changes["maneuver"] != null && !new[] { "attack", "smash", "defend", "sweep", "counter", "casting", "evade" }.Contains((string)changes["maneuver"])) throw new InvalidOperationException("Unknown card maneuver");
                if (changes["range"] != null && !new[] { "melee", "reach", "ranged" }.Contains((string)changes["range"])) throw new InvalidOperationException("Unknown card range");
            }
            var position = (string)effect["position"] ?? "bottom";
            if (position != "top" && position != "bottom" && position != "random") throw new InvalidOperationException("Unknown card destination position");
        }

        /// <summary>JS runOpcode 'moveCards' / 'modifyCards' → cardZones.js moveCards.</summary>
        private void MoveCards(Core2FightAction action, int amount)
        {
            var effect = action.Effect;
            // JS: a formula hideTurns resolves to a whole 1–10 turns before the move.
            if (effect["changes"]?["hideTurns"] is JObject hideFormula)
            {
                effect = (JObject)effect.DeepClone();
                effect["changes"]["hideTurns"] = Math.Min(10, Math.Max(1, (int)Eval(action, hideFormula, 1)));
            }
            ValidateZoneEffect(effect);
            if (effect["transformTo"] != null) Content.Card((string)effect["transformTo"]);
            var candidates = ZoneCandidates(effect);
            var count = Math.Min(candidates.Count, Math.Min(100, Math.Max(0, amount)));
            if (count == 0) return;
            if ((string)effect["selection"] == "choose")
            {
                if (_pendingPlay == null) throw new InvalidOperationException("Card selection requires a resolving card");
                var pending = (JObject)effect.DeepClone();
                pending["amount"] = count;
                PendingCardChoice = new Core2CardChoice
                {
                    Count = count, Effect = pending, CandidateIds = candidates.Select(c => c.InstanceId).ToList(),
                    Candidates = candidates.Select(c => new Core2CardInstance { InstanceId = c.InstanceId, CardId = c.CardId, Changes = (JObject)c.Changes?.DeepClone() }).ToList(),
                    CardId = _pendingPlay.Instance.CardId,
                };
                return;
            }
            var ordered = (string)effect["selection"] == "random" ? Rng.Shuffle("shuffle", candidates) : candidates;
            ApplyMove(effect, ordered.Take(count).Select(c => c.InstanceId).ToList());
        }

        /// <summary>JS applyMove: modify in place, or move (and transform) into the destination with its receipt.</summary>
        private void ApplyMove(JObject effect, List<string> ids)
        {
            var from = Pile((string)effect["from"]);
            if ((string)effect["op"] == "modifyCards")
            {
                var changes = (JObject)effect["changes"];
                foreach (var id in ids)
                {
                    var card = from.First(c => c.InstanceId == id);
                    var merged = (JObject)(card.Changes?.DeepClone() ?? new JObject());
                    foreach (var p in changes.Properties()) merged[p.Name] = p.Value.DeepClone();
                    card.Changes = merged;
                    if ((bool?)changes["retain"] == true) card.Changes["retainUntilTurn"] = Turn + ((int?)changes["retainTurns"] ?? 1) - 1;
                    if (changes["hideTurns"] != null && (int)changes["hideTurns"] != 0)
                    {
                        _hiddenCards.Add((card, Turn + (int)changes["hideTurns"]));
                        from.Remove(card);
                    }
                    Emit("cardModified", new JObject { ["cardInstanceId"] = id, ["cardId"] = card.CardId, ["changes"] = changes.DeepClone() });
                }
                return;
            }
            var to = (string)effect["to"];
            var cards = ids.Select(id => from.First(c => c.InstanceId == id)).ToList();
            foreach (var card in cards) from.Remove(card);
            foreach (var card in cards)
                if (effect["transformTo"] != null) card.CardId = (string)effect["transformTo"];
            var moved = new List<(Core2CardInstance Card, string To)>();
            foreach (var card in cards)
            {
                var actualTo = to == "hand" && Pile("hand").Count >= HandMax ? "discard" : to;
                var pile = Pile(actualTo);
                var position = (string)effect["position"];
                if (position == "random") pile.Insert((int)Math.Floor(Rng.Float("shuffle") * (pile.Count + 1)), card);
                else if (position == "top") pile.Insert(moved.Count(m => m.To == actualTo), card);
                else pile.Add(card);
                moved.Add((card, actualTo));
            }
            foreach (var (card, actualTo) in moved)
                Emit(actualTo == "exhaust" ? "cardExhausted" : actualTo == "discard" ? "cardDiscarded" : "cardMoved", new JObject
                {
                    ["cardInstanceId"] = card.InstanceId, ["cardId"] = card.CardId, ["from"] = effect["from"], ["to"] = actualTo,
                    ["reason"] = actualTo != to ? "handFull" : "chosen", ["explicit"] = true, ["sourceId"] = Player.Id,
                });
        }

        /// <summary>
        /// JS dispatch 'chooseCards' → chooseZoneCards: exactly the asked number of distinct candidates still eligible; then
        /// the paused card finishes (its remaining effects drain and it resolves).
        /// </summary>
        public void ChooseCards(IReadOnlyList<string> cardInstanceIds)
        {
            var pending = PendingCardChoice ?? throw new InvalidOperationException("No card-zone choice is pending");
            var valid = new HashSet<string>(ZoneCandidates(pending.Effect).Select(c => c.InstanceId), StringComparer.Ordinal);
            if (cardInstanceIds == null || cardInstanceIds.Count != pending.Count || cardInstanceIds.Distinct().Count() != cardInstanceIds.Count
                || cardInstanceIds.Any(id => !pending.CandidateIds.Contains(id) || !valid.Contains(id)))
                throw new InvalidOperationException("Choose exactly " + pending.Count + " distinct eligible cards");
            ApplyMove(pending.Effect, cardInstanceIds.ToList());
            PendingCardChoice = null;
            DrainQueue();
            if (HasCardChoice) return;
            if (DeferRootPlay()) return;
            FinishPlay();
        }

        /// <summary>JS returnHiddenCards: each hidden card whose turn has come goes back to the hand (the discard when full).</summary>
        private void ReturnHiddenCards()
        {
            var remaining = new List<(Core2CardInstance, int)>();
            foreach (var row in _hiddenCards)
            {
                if (row.ReturnTurn <= Turn)
                {
                    (Hand.Count < HandMax ? Hand : Discard).Add(row.Card);
                    Emit("cardMoved", new JObject { ["cardInstanceId"] = row.Card.InstanceId, ["cardId"] = row.Card.CardId, ["from"] = "hidden", ["to"] = "hand", ["reason"] = "returned" });
                }
                else remaining.Add(row);
            }
            _hiddenCards.Clear();
            _hiddenCards.AddRange(remaining);
        }

        /// <summary>JS endTurnCardFate's Retain: a card whose Retain change still lasts stays in hand.</summary>
        private bool RetainedCard(Core2CardInstance card) =>
            card.Changes != null && (bool?)card.Changes["retain"] == true && ((int?)card.Changes["retainUntilTurn"] ?? Turn) >= Turn;

        /// <summary>
        /// JS projectCardBehavior on the card as it is priced and played: a playLimit spent makes it unplayable; its changes
        /// override its maneuver and range; costGrowth raises its SP and MP by its plays this combat.
        /// </summary>
        private void ProjectCardBehavior(Core2CardInstance instance, Core2Carrier carrier)
        {
            var raw = Content.Card(instance.CardId).Raw;
            var riders = Player?.AbilityRiders;
            if (raw["playLimit"] != null && (int)raw["playLimit"] > 0 && Core2AbilityState.Count(riders?.InstanceUsesCombat ?? new Dictionary<string, int>(), instance.InstanceId) >= (int)raw["playLimit"])
                carrier.Unplayable = true;
            var changes = instance.Changes;
            if (changes != null)
            {
                carrier.Profile ??= new Core2CombatProfile();
                if (changes["range"] != null) carrier.Profile.Range = (string)changes["range"];
                if (changes["maneuver"] != null) carrier.Profile.Maneuver = (string)changes["maneuver"];
            }
            if (raw["costGrowth"] is JObject growth)
            {
                var plays = Core2AbilityState.Count(riders?.CardUsesCombat ?? new Dictionary<string, int>(), instance.CardId);
                var steps = Math.Min((int?)growth["max"] ?? 3, plays / Math.Max(1, (int?)growth["perUse"] ?? 1));
                if (!carrier.XCost) carrier.BaseSp = Math.Max(0, carrier.BaseSp + steps * ((int?)growth["stamina"] ?? 0));
                carrier.BaseMp = Math.Max(0, carrier.BaseMp + steps * ((int?)growth["mana"] ?? 0));
            }
        }
    }
}
