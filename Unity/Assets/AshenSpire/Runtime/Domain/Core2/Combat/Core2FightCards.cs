// Core2FightCards.cs — playing a card in a Core 2 fight: its price, its target, its payment, its stance entry, its
// effects as one committed action, and where it goes afterwards.
//
// JS at the pin:
//   src/engine/combat.js            cardPlayCosts / resolvedCardPlayCosts (profileCardCosts), doPlayCard (validation,
//                                   cardPreparing, payment events, counters, prepareTacticalCard + setProfileStance,
//                                   cardPlayed), finishAbilityPlay (cardResolved; Exhaust, Power or discard)
//   src/engine/cardBehavior.js      selectCardVariant (effectVariants by predicate, e.g. resourcePaid)
//   src/model/cardTargets.js        cardTargetPlan / assertCardTarget (hostile cards aim at a living enemy; others at the
//                                   player alone), src/model/combatCardEffects.js immediateCardEffects (a Counter's
//                                   damage is its deferred return, not an immediate hostile effect)
//   src/engine/combatCardTactics.js prepareTacticalCard (a Counter arms and keeps only its support effects)
// Every refusal happens before anything changes, as JS's transaction guarantees (the play is atomic).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed partial class Core2Fight
    {
        private sealed class PendingPlay
        {
            public Core2CardInstance Instance;
            public Core2Carrier Carrier;
            public string Kind;
            public string TargetId;
        }
        private PendingPlay _pendingPlay;

        private static readonly HashSet<string> HostileTargets = new HashSet<string>(new[] { "enemy", "allEnemies", "randomEnemy", "otherEnemies" }, StringComparer.Ordinal);

        /// <summary>The card as this player plays it: the authored row with its gear-resolved face (combat profile).</summary>
        public Core2Carrier CardCarrier(Core2CardInstance instance)
        {
            var carrier = Core2Carrier.FromCard(Content.Card(instance.CardId));
            if (Setup.Cards.TryGetValue(instance.CardId, out var face) && face.Profile != null)
            {
                var profile = face.Profile.Clone();
                profile.Range ??= carrier.Profile?.Range;
                carrier.Profile = profile;
            }
            return carrier;
        }

        /// <summary>JS cardPlayCosts: the price of a card in hand right now (SP and MP).</summary>
        public Core2Costs CardCosts(string instanceId)
        {
            var instance = Hand.FirstOrDefault(c => c.InstanceId == instanceId) ?? throw new InvalidOperationException("Card '" + instanceId + "' is not in hand");
            return Core2Pricing.CardCosts(Ctx, Player, Core2Carrier.FromCard(Content.Card(instance.CardId)));
        }

        /// <summary>JS cardKind: attack, skill or power from the card's classification tag.</summary>
        private static string CardKind(Core2Card card)
        {
            var kinds = Core2Json.Strings(card.Raw["kindIds"]).Concat(card.Tags).ToList();
            if (kinds.Contains("classification.attack")) return "attack";
            if (kinds.Contains("classification.skill")) return "skill";
            if (kinds.Contains("classification.power")) return "power";
            if (kinds.Contains("classification.status")) return "status";
            if (kinds.Contains("classification.curse")) return "curse";
            return card.Type;
        }

        /// <summary>JS selectCardVariant: the first effect variant whose predicate holds for this payment, else the authored effects.</summary>
        private List<JObject> CardEffects(Core2Card card, Core2Costs costs)
        {
            if (card.Raw["effectVariants"] is JArray variants && variants.Count > 0)
            {
                var probe = new Core2FightAction { Source = Player, Owner = Player, EnergySpent = costs.Sp, ManaSpent = costs.Mp };
                foreach (var variant in variants.OfType<JObject>())
                    if (Predicate((JObject)variant["if"], Player, null, null, probe)) return ((JArray)variant["effects"]).OfType<JObject>().ToList();
            }
            return card.Effects.OfType<JObject>().ToList();
        }

        /// <summary>
        /// JS doPlayCard (configured, FixCore). Refuses — changing nothing — a card out of turn, not in hand, aimed at an
        /// illegal target, or unaffordable (its price, then its Advance); otherwise pays, enters the card's stance, runs
        /// its effects as one committed action and resolves it.
        /// </summary>
        public void PlayCard(string instanceId, string targetId = null)
        {
            if (Result != null) throw new InvalidOperationException("Combat is over");
            RequireNoPendingReaction();
            DoPlayCard(new Core2ReactionPlay { CardInstanceId = instanceId, TargetId = targetId });
        }

        /// <summary>
        /// JS doPlayCard: the one play path, on the player turn or as a reaction (JS ctx._reactionPlaying, any phase). A
        /// reaction play is a reaction carrier (no window opens on it); a Counter played as a reaction to an incoming
        /// contact also pays and reserves its return's Advance (JS paidCounterRange). A card whose own root waits on an
        /// enemy reaction window stays unresolved until the root resumes (JS deferRootPlay).
        /// </summary>
        private void DoPlayCard(Core2ReactionPlay play)
        {
            var instanceId = play.CardInstanceId; var targetId = play.TargetId;
            if (Phase != "player" && !_reactionPlaying) throw new InvalidOperationException("Cards can only be played on the player turn");
            var p = Player;
            var index = Hand.FindIndex(c => c.InstanceId == instanceId);
            if (index < 0) throw new InvalidOperationException("Card '" + instanceId + "' is not in hand");
            var instance = Hand[index];
            var card = Content.Card(instance.CardId);
            // JS doPlayCard: Control.controlGate before anything else is checked (a lock refuses as 'controlled').
            if (!ControlGate(p, card)) throw new Core2PlayRefusedException("Recover from the active control statuses before playing cards", null, 0, 0, "controlled");
            if (Core2Json.Strings(card.Raw["keywords"]).Contains("unplayable")) throw new Core2PlayRefusedException("'" + card.Name + "' is unplayable", null, 0, 0, "unplayable");
            var carrier = CardCarrier(instance);
            carrier.InstanceId = instance.InstanceId;
            if (_reactionPlaying) carrier.ReactionDepth = 1;
            var costs = Core2Pricing.CardCosts(Ctx, p, Core2Carrier.FromCard(card));
            var effects = CardEffects(card, costs);
            var counter = carrier.Profile?.Maneuver == "counter";
            var immediate = counter ? effects.Where(e => (string)e["op"] != "damage" && (string)e["op"] != "poiseDamage").ToList() : effects;
            var hostile = immediate.Any(e => HostileTargets.Contains((string)e["target"] ?? ""));
            if (targetId != null)
            {
                var legal = hostile ? Enemies.Where(e => e.Alive).Select(e => e.Id).ToList() : new List<string> { p.Id };
                if (!legal.Contains(targetId)) throw new InvalidOperationException("Invalid " + (hostile ? "enemy" : "friendly") + " target '" + targetId + "'");
            }
            Core2Combatant target = null;
            if (targetId != null)
            {
                target = Find(targetId);
                if (target == null || !target.Alive) throw new InvalidOperationException("Invalid target '" + targetId + "'");
            }
            else if (immediate.Any(e => (string)e["target"] == "enemy"))
            {
                target = Enemies.FirstOrDefault(e => e.Alive) ?? throw new InvalidOperationException("No living enemy to target");
            }
            Core2Pools.RequireAffordable(p, costs);
            // The Advance an attack needs is paid when its action is accepted; refuse now if the pool cannot cover it.
            var support = counter ? effects.Where(e => (string)e["op"] != "damage" && (string)e["op"] != "poiseDamage" && (string)e["op"] != "wardDamage").ToList() : effects;
            var probeActions = support.Select(e => new Core2FightAction { Effect = e, Source = p, Owner = p, Target = target, Card = carrier }).ToList();
            var advance = AdvanceFor(p, probeActions, carrier, out var reachable);
            if (!reachable) throw new Core2PlayRefusedException("The selected range route cannot reach every target", "range", 0, 0);
            if (p.Stamina - costs.Sp < advance) throw new Core2PlayRefusedException("Not enough Stamina to Advance", "stamina", advance, p.Stamina - costs.Sp);

            var kind = CardKind(card);
            Emit("cardPreparing", new JObject { ["cardInstanceId"] = instance.InstanceId, ["cardId"] = instance.CardId, ["cardType"] = kind, ["cardTags"] = new JArray(card.Tags), ["sourceId"] = p.Id, ["targetId"] = target?.Id });
            DrainQueue();
            var counterRange = PaidCounterRange(p, carrier, costs, play.RangeRoute ?? "advance");
            if (counterRange != null)
            {
                costs = counterRange.Value.Costs;
                carrier.RangeCounterReservation = counterRange.Value.Reservation;
                Core2Pools.RequireAffordable(p, costs);
            }
            p.Stamina -= costs.Sp;
            if (costs.Sp > 0 || carrier.XCost) Emit("energySpent", new JObject { ["amount"] = costs.Sp });
            p.Mana -= costs.Mp;
            if (costs.Mp > 0) Emit("manaSpent", new JObject { ["amount"] = costs.Mp });
            if (costs.Sp > 0)
            {
                p.StaminaSpentThisTurn += costs.Sp;
                Emit("staminaSpent", new JObject { ["amount"] = costs.Sp });
            }
            Hand.RemoveAt(index);
            p.CardsPlayedThisTurn += 1;
            p.CardsPlayed += 1;
            if (kind == "attack") p.AttacksPlayedThisCombat += 1;
            _pendingPlay = new PendingPlay { Instance = instance, Carrier = carrier, Kind = kind, TargetId = target?.Id };
            // prepareTacticalCard: the card enters its stance; a Counter arms and keeps only its support effects.
            carrier.AppliedStatuses = effects.Where(e => (string)e["op"] == "applyStatus").Select(e => (string)e["status"]).ToList();
            SetProfileStance(p, carrier, card.Tags);
            if (counter)
            {
                Core2Counters.Arm(Ctx, p, carrier);
                Emit("combatCounterArmed", new JObject { ["sourceKind"] = "player", ["cardId"] = instance.CardId, ["cardInstanceId"] = instance.InstanceId, ["sourceId"] = p.Id, ["mode"] = p.Counter.Mode });
            }
            if (++_commands > Rules.FreeCommandLimit) throw new InvalidOperationException("Turn work budget reached; End Turn remains available");
            SetProfileStance(p, carrier, card.Tags);
            var actions = support.Select(e => new Core2FightAction { Effect = e, Source = p, Owner = p, Target = target, Card = carrier, EnergySpent = costs.Sp, ManaSpent = costs.Mp }).ToList();
            EnqueueGroup(actions, p, target, carrier);
            Emit("cardPlayed", new JObject
            {
                ["cardInstanceId"] = instance.InstanceId, ["cardId"] = instance.CardId, ["cardType"] = kind, ["cardTags"] = new JArray(card.Tags), ["sourceId"] = p.Id,
                ["targetId"] = target?.Id, ["energySpent"] = costs.Sp + advance, ["manaSpent"] = costs.Mp, ["staminaSpent"] = costs.Sp + advance,
            });
            DrainQueue();
            // An enemy reaction window on this card's own root: it resolves when the root resumes.
            if (DeferRootPlay()) return;
            FinishPlay();
        }

        /// <summary>
        /// JS paidCounterRange: a Counter played as a reaction to an incoming contact quotes its return against that attacker
        /// in the contact's frozen engagement; the return's Advance joins the card's price now and the plan is reserved for
        /// the return (it pays nothing more). Refused when the route cannot reach.
        /// </summary>
        private (Core2Costs Costs, Core2CounterReservation Reservation)? PaidCounterRange(Core2Combatant source, Core2Carrier carrier, Core2Costs baseCosts, string route)
        {
            if (Range == null || _rangeIncoming == null || !_reactionPlaying || carrier.Profile?.Maneuver != "counter") return null;
            var incoming = _rangeIncoming.Value;
            var target = new[] { Player }.Concat(Enemies).FirstOrDefault(entity => Core2Range.ActorKey(entity) == incoming.SourceKey)
                ?? throw new Core2PlayRefusedException("The incoming attacker is unavailable", "range", 0, 0);
            var returnCarrier = carrier.Clone();
            returnCarrier.Profile.CounterReturnAttack = true;
            var key = Core2Range.ActorKey(target);
            // JS quoteCounterReturn: the return's damage and Poise contacts against the attacker.
            var contacts = new List<Core2RangeSource>();
            for (var index = 0; index < 2; index++)
            {
                var contact = RangeSourceFor(source, returnCarrier, key);
                contact.ContactId = index + ":0:0:" + key;
                contacts.Add(contact);
            }
            var plan = Core2Range.ActionPlan(Range, Core2Range.ActorKey(source), contacts, route, _rangeContext);
            if (!plan.Available) throw new Core2PlayRefusedException("The selected Counter return cannot reach its attacker", "range", 0, 0);
            var costs = Core2Range.AddRangeCosts(baseCosts, plan);
            plan.RootId = incoming.RootId + ":return:" + incoming.ContactId + ":" + (carrier.InstanceId ?? carrier.Id);
            return (costs, new Core2CounterReservation
            {
                SourceKey = plan.SourceKey, TargetKey = incoming.SourceKey, IncomingRootId = incoming.RootId, ContactId = incoming.ContactId, RangePlan = plan,
            });
        }

        /// <summary>The Advance SP an action's hostile contacts need from the player's range state (JS quoteRangeActions, advance route).</summary>
        private int AdvanceFor(Core2Combatant source, List<Core2FightAction> actions, Core2Carrier carrier, out bool available)
        {
            available = true;
            if (Range == null) return 0;
            var contacts = new List<Core2RangeSource>();
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                var op = (string)action.Effect["op"];
                if (op != "damage" && op != "applyStatus" && op != "buildup" && op != "poiseDamage" && op != "wardDamage" && op != "loseHp" && op != "stagger" && op != "arcaneBuildup") continue;
                var repeats = Math.Max(0, (int)Math.Floor(Eval(action, action.Effect["repeat"], 1)));
                var hits = op == "damage" ? Math.Max(0, (int)Math.Floor(Eval(action, action.Effect["hits"], 1))) : 1;
                for (var r = 0; r < repeats; r++) for (var h = 0; h < hits; h++)
                    foreach (var t in ResolveTargets(action, (string)action.Effect["target"]))
                        if (t.Alive && t.Side != source.Side)
                        {
                            var contact = RangeSourceFor(source, carrier, Core2Range.ActorKey(t));
                            contact.ContactId = index + ":" + r + ":" + h + ":" + Core2Range.ActorKey(t);
                            contacts.Add(contact);
                        }
            }
            var plan = Core2Range.ActionPlan(Range, Core2Range.ActorKey(source), contacts, "advance", _rangeContext);
            available = plan.Available;
            return plan.AdvanceSp;
        }

        /// <summary>JS finishAbilityPlay: cardResolved, then the card goes to Exhaust (keyword), out of play (a Power) or the discard pile.</summary>
        private void FinishPlay()
        {
            var play = _pendingPlay;
            if (play == null) return;
            _pendingPlay = null;
            Emit("cardResolved", new JObject { ["cardInstanceId"] = play.Instance.InstanceId, ["cardId"] = play.Instance.CardId, ["cardType"] = play.Kind, ["targetId"] = play.TargetId, ["sourceId"] = Player.Id });
            if (Result == null)
            {
                var card = Content.Card(play.Instance.CardId);
                if (Core2Json.Strings(card.Raw["keywords"]).Contains("exhaust"))
                {
                    Exhaust.Add(play.Instance);
                    Emit("cardExhausted", new JObject { ["cardInstanceId"] = play.Instance.InstanceId, ["cardId"] = play.Instance.CardId, ["reason"] = "played" });
                }
                else if (card.Type != "power") Discard.Add(play.Instance);
                DrainQueue();
            }
        }
    }
}
