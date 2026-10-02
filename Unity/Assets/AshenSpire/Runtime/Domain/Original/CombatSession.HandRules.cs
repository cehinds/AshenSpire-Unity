// CombatSession.HandRules.cs — draw counts, retention and turn-end discards under
// a fight's hand-rules snapshot (web src/engine/handRules.js). Every method here
// is the legacy behaviour exactly when the fight carries no snapshot.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        // Web turnDrawCount: the opening hand on turn 1, then fill-to-capacity or a fixed
        // turn draw plus replacements owed for last turn's optional discards; never past capacity.
        private int TurnDrawCount()
        {
            if (_handRules == null) return (int)_player["drawPerTurn"];
            var capacity = HandMaximum; var room = Math.Max(0, capacity - _piles["hand"].Count);
            var wanted = _turn == 1 ? HandRules.ScaledCards((JObject)_handRules["starting"], _attributes)
                : (string)_handRules["drawMode"] == "fill" ? room : HandRules.ScaledCards((JObject)_handRules["turn"], _attributes) + _pendingDiscardDraw;
            _pendingDiscardDraw = 0;
            return Math.Min(room, wanted);
        }

        // Web endTurnCardFate: retention turns an ordinary discard into a keep; Retain and Ethereal stay authoritative.
        private string EndTurnCardFate(JObject card)
        {
            var fate = CardMechanics.EndTurnFate(CardMechanics.FromDefinition(ResolvedCard(card)));
            return fate == "discard" && _handRules != null && (bool)_handRules["retain"] ? "keep" : fate;
        }

        /// <summary>
        /// The turn-end discard choice (web discardChoicePlan): eligible retained cards, the forced minimum
        /// (retained cards past capacity when overflow is "discard"), the optional maximum, and whether the
        /// player must be asked. A legacy fight always returns an empty, unprompted plan.
        /// </summary>
        public JObject DiscardChoicePlan()
        {
            if (_handRules == null) return new JObject { ["cardIds"] = new JArray(), ["minimum"] = 0, ["maximum"] = 0, ["prompt"] = false };
            var cards = _piles["hand"].Where(card => EndTurnCardFate(card) == "keep").ToList();
            var capacity = HandMaximum;
            var minimum = (string)_handRules["overflow"] == "discard" ? Math.Max(0, cards.Count - capacity) : 0;
            var optional = (bool)_handRules["retain"] && (bool)_handRules["promptDiscard"];
            var maximum = Math.Min(cards.Count, Math.Max(minimum, optional ? (int)_handRules["discardLimit"] : 0));
            return new JObject { ["cardIds"] = new JArray(cards.Select(c => c["instanceId"].DeepClone())), ["minimum"] = minimum, ["maximum"] = maximum, ["prompt"] = minimum > 0 || (optional && maximum > 0) };
        }

        private List<string> ValidateDiscardChoice(IEnumerable<string> discardIds)
        {
            var ids = (discardIds ?? Enumerable.Empty<string>()).ToList(); var plan = DiscardChoicePlan();
            var eligible = new HashSet<string>(((JArray)plan["cardIds"]).Values<string>(), StringComparer.Ordinal);
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(id => id == null || !eligible.Contains(id)) || ids.Count > (int)plan["maximum"] || ids.Count < (int)plan["minimum"])
                throw new ArgumentException("Select " + plan["minimum"] + "–" + plan["maximum"] + " eligible cards to discard.");
            return ids;
        }

        // Web applyDiscardChoice: chosen cards move before normal cleanup; replacements are owed only when configured.
        private void ApplyDiscardChoice(IEnumerable<string> ids)
        {
            var discarded = 0;
            foreach (var id in ids)
            {
                var card = _piles["hand"].FirstOrDefault(c => (string)c["instanceId"] == id);
                if (card == null || EndTurnCardFate(card) != "keep") continue; // turn-end hooks and Ethereal remain authoritative
                _piles["hand"].Remove(card); _piles["discard"].Add(card); CardEvent("cardDiscarded", card, "choice"); discarded++;
            }
            _pendingDiscardDraw = _handRules != null && (bool)_handRules["replaceDiscards"] ? discarded : 0;
        }
    }
}
