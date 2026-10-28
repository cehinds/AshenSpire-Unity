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
            public Core2AbilityState Before;
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
            // JS projectCardBehavior: a modifyCards change overrides the face's maneuver and range.
            if (instance.Changes != null)
            {
                carrier.Profile ??= new Core2CombatProfile();
                if (instance.Changes["range"] != null) carrier.Profile.Range = (string)instance.Changes["range"];
                if (instance.Changes["maneuver"] != null) carrier.Profile.Maneuver = (string)instance.Changes["maneuver"];
            }
            return carrier;
        }

        /// <summary>JS cardPlayCosts: the price of a card in hand right now (SP and MP).</summary>
        public Core2Costs CardCosts(string instanceId)
        {
            var instance = Hand.FirstOrDefault(c => c.InstanceId == instanceId) ?? throw new InvalidOperationException("Card '" + instanceId + "' is not in hand");
            var priced = PricedCarrier(instance);
            return PlayCosts(priced, ProposalCardPlan(Player, priced, Content.Card(instance.CardId).Effects.OfType<JObject>().ToList()));
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
        public void PlayCard(string instanceId, string targetId = null, string choice = null)
        {
            if (Result != null) throw new InvalidOperationException("Combat is over");
            RequireNoPendingReaction();
            RequireNoCardChoice();
            DoPlayCard(new Core2ReactionPlay { CardInstanceId = instanceId, TargetId = targetId, Choice = choice });
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
            var priced = PricedCarrier(instance);
            // JS doPlayCard: the status rules' card plan (a restriction refuses as 'controlled'), then Control.controlGate.
            var statusPlan = ProposalCardPlan(p, priced, card.Effects.OfType<JObject>().ToList());
            if (!statusPlan.Allowed) throw new Core2PlayRefusedException(statusPlan.Refusal, null, 0, 0, "controlled");
            if (!ControlGate(p, card)) throw new Core2PlayRefusedException("Recover from the active control statuses before playing cards", null, 0, 0, "controlled");
            // JS assertCardChoice: a card that asks for a choice needs a legal one; any other refuses one.
            var options = CardChoiceOptions(instance.InstanceId);
            if (options == null && play.Choice != null) throw new InvalidOperationException("This card offers no choice");
            if (options != null && !options.Contains(play.Choice ?? "")) throw new InvalidOperationException("Choose a stance: " + string.Join(", ", options));
            if (Core2Json.Strings(card.Raw["keywords"]).Contains("unplayable") || priced.Unplayable) throw new Core2PlayRefusedException("'" + card.Name + "' is unplayable", null, 0, 0, "unplayable");
            var carrier = CardCarrier(instance);
            carrier.InstanceId = instance.InstanceId;
            if (_reactionPlaying) carrier.ReactionDepth = 1;
            var costs = PlayCosts(priced, statusPlan);
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
            // JS beforeAbilityPlay: the ledger as it stood before this card, carried by its events and actions.
            var before = AbilitySnapshot(p);
            var preparing = Emit("cardPreparing", new JObject { ["cardInstanceId"] = instance.InstanceId, ["cardId"] = instance.CardId, ["cardType"] = kind, ["cardTags"] = new JArray(card.Tags), ["sourceId"] = p.Id, ["targetId"] = target?.Id }, before: before);
            DrainQueue();
            // JS: the card charges the preparing triggers granted (and any held) that this card matches, and the effects they add.
            var abilityKind = carrier.AbilityKind ?? (card.Tags.Contains("source:spell") ? "spell" : "maneuver");
            var charges = MatchingAbilityCharges(p, kind, card.Tags, abilityKind, Enemies.Any(e => e.Alive));
            var charged = AbilityChargedEffects(effects, charges, Enemies.Any(e => e.Alive));
            if (!ReferenceEquals(charged, effects))
            {
                if (target != null && target.Side != Core2Side.Enemy) throw new InvalidOperationException("Buildup charge requires a living enemy target");
                target ??= Enemies.FirstOrDefault(e => e.Alive);
                effects = charged;
                support = counter ? effects.Where(e => (string)e["op"] != "damage" && (string)e["op"] != "poiseDamage" && (string)e["op"] != "wardDamage").ToList() : effects;
            }
            // JS: the price again after the preparing triggers (a status rule's tax may have changed with them).
            statusPlan = ProposalCardPlan(p, PricedCarrier(instance), card.Effects.OfType<JObject>().ToList());
            costs = PlayCosts(PricedCarrier(instance), statusPlan);
            var counterRange = PaidCounterRange(p, carrier, costs, play.RangeRoute ?? "advance");
            if (counterRange != null)
            {
                costs = counterRange.Value.Costs;
                carrier.RangeCounterReservation = counterRange.Value.Reservation;
            }
            Core2Pools.RequireAffordable(p, costs);
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
            RecordAbilityCard(p, kind, instance.CardId, instance.InstanceId, card.Tags, abilityKind, costs.Mp);
            ConsumeAbilityCharges(p, charges.Keys);
            if (charges.Keys.Count > 0) Emit("cardChargeConsumed", new JObject { ["keys"] = new JArray(charges.Keys), ["cardId"] = instance.CardId, ["sourceId"] = p.Id });
            _pendingPlay = new PendingPlay { Instance = instance, Carrier = carrier, Kind = kind, TargetId = target?.Id, Before = before };
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
            var applied = new HashSet<string>(StringComparer.Ordinal);
            var actions = new List<Core2FightAction>();
            foreach (var e in support)
            {
                var action = new Core2FightAction
                {
                    Effect = e, Source = p, Owner = p, Target = target, Card = carrier, EnergySpent = costs.Sp, ManaSpent = costs.Mp, StaminaSpent = costs.Sp,
                    AbilityBefore = before, OrdinalThisCombat = p.CardsPlayed, Choice = play.Choice,
                };
                AttachAbilityCharges(action, charges, applied);
                actions.Add(action);
            }
            EnqueueGroup(actions, p, target, carrier);
            // JS applyProposalPlan(statusPlan): an extra weapon or shield the card's status rules add, and their stacks spent.
            ApplyProposalPlan(statusPlan, p, target, carrier);
            Emit("cardPlayed", new JObject
            {
                ["cardInstanceId"] = instance.InstanceId, ["cardId"] = instance.CardId, ["cardType"] = kind, ["cardTags"] = new JArray(card.Tags), ["sourceId"] = p.Id,
                ["targetId"] = target?.Id, ["energySpent"] = costs.Sp + advance, ["manaSpent"] = costs.Mp, ["staminaSpent"] = costs.Sp + advance,
            }, before: before);
            DrainQueue();
            if (HasCardChoice) return;
            // An enemy reaction window on this card's own root: it resolves when the root resumes.
            if (DeferRootPlay()) return;
            FinishPlay();
        }

        /// <summary>
        /// The card as its price reads it (JS resolveCombatCard → projectCardBehavior): the authored row, a spent playLimit
        /// unplayable, its changes' maneuver and range, costGrowth.
        /// </summary>
        private Core2Carrier PricedCarrier(Core2CardInstance instance)
        {
            var carrier = Core2Carrier.FromCard(Content.Card(instance.CardId));
            ProjectCardBehavior(instance, carrier);
            return carrier;
        }

        /// <summary>JS resolvedCardPriceReceipt: the configured price plus the active status rules' card taxes.</summary>
        private Core2Costs PlayCosts(Core2Carrier priced, Core2ProposalPlan statusPlan)
        {
            var costs = Core2Pricing.CardCosts(Ctx, Player, priced);
            return statusPlan.StaminaTax != 0 || statusPlan.ManaTax != 0 ? new Core2Costs(costs.Sp + statusPlan.StaminaTax, costs.Mp + statusPlan.ManaTax) : costs;
        }

        /// <summary>JS cardChoicePlan: the maneuvers an enterStance choose: 'maneuver' card offers, else null (no choice).</summary>
        public IReadOnlyList<string> CardChoiceOptions(string instanceId)
        {
            var instance = Hand.FirstOrDefault(c => c.InstanceId == instanceId) ?? throw new InvalidOperationException("Card '" + instanceId + "' is not in hand");
            var effect = Content.Card(instance.CardId).Effects.OfType<JObject>().FirstOrDefault(e => (string)e["op"] == "enterStance" && e["choose"] != null);
            if (effect == null) return null;
            if ((string)effect["choose"] == "maneuver") return Maneuvers;
            throw new InvalidOperationException("enterStance choose '" + (string)effect["choose"] + "' offers registry stances; Core 2 has none");
        }

        /// <summary>JS concurrentStances.js MANEUVERS.</summary>
        public static readonly IReadOnlyList<string> Maneuvers = new[] { "attack", "smash", "defend", "sweep", "counter", "casting", "evade" };

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
            // JS quoteRangeActions (preview): the card's own contacts and its envelope's riders; an unbounded envelope refuses.
            var plan = QuoteRangeActions(source, actions, carrier, _rangeContext);
            available = plan.Available;
            return plan.AdvanceSp;
        }

        /// <summary>JS finishAbilityPlay: cardResolved, then the card goes to Exhaust (keyword), out of play (a Power) or the discard pile.</summary>
        private void FinishPlay()
        {
            var play = _pendingPlay;
            if (play == null) return;
            _pendingPlay = null;
            Emit("cardResolved", new JObject { ["cardInstanceId"] = play.Instance.InstanceId, ["cardId"] = play.Instance.CardId, ["cardType"] = play.Kind, ["targetId"] = play.TargetId, ["sourceId"] = Player.Id }, before: play.Before, lifecycle: play.Carrier);
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
