// Core2FightPreview.cs — read-only questions the combat screen asks a running Core 2 fight (port plan phase 6). Nothing
// here changes the fight, draws on its RNG streams or emits an event: a question that has to try a play runs it on a
// detached copy (Candidate), and every other answer reads cloned combatants.
//
// JS at the pin:
//   src/ui/models/CombatIntentProjectionModel.js projectCombatIntent (the intent's HP range at the player's current
//                                                defenses: the enemy move as a carrier, its intent damage and hits)
//   src/engine/combat.js                         previewCardHit (a hand card's hit chance against one enemy)
//   src/engine/stanceHit.js                      previewCardAttackHit (the card's stance and its entry Precision are
//                                                projected onto the attacker), previewTargetFor (an unread enemy is
//                                                read as a neutral Melee target)
//   src/ui/screens/combat.js                     the hand's playable state (a card the engine would refuse is dimmed)
//   src/ui/components/card.js                    the hand face's live numbers (the previewed damage and Block)
// Limits: proposal-status plans (JS proposalTargetPlan: Perception and status evasion of the intent) are not part of
// the Core 2 profile's content, so the projection reads them as allowed and unevaded; the throw route is not previewed.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>Why the fight would refuse a play (null from CheckPlay when it would accept it).</summary>
    public sealed class Core2PlayCheck
    {
        /// <summary>cost, range, unplayable, controlled, target or other.</summary>
        public string Reason { get; set; }
        /// <summary>stamina, mana or hp for a cost refusal.</summary>
        public string Resource { get; set; }
        public string Message { get; set; }
    }

    /// <summary>A hand card's live face numbers (JS card.js preview tokens): null where the card has no such effect.</summary>
    public sealed class Core2CardFaceValues
    {
        public int? Damage { get; set; }
        public int? Hits { get; set; }
        public int? Block { get; set; }
        public int? BaseDamage { get; set; }
        public int? BaseBlock { get; set; }
    }

    public sealed partial class Core2Fight
    {
        /// <summary>
        /// JS projectCombatIntent: the HP an enemy's revealed damaging intent can take from the player at the player's
        /// current defenses ("0–12"). Null when the intent is concealed, Staggered or deals no damage.
        /// </summary>
        public Core2DamageRange ProjectIntent(Core2Combatant enemy) => ProjectIntent(enemy, null);

        /// <summary>The same projection over <paramref name="hits"/> of the intent's contacts (1: the next contact a reaction answers).</summary>
        public Core2DamageRange ProjectIntent(Core2Combatant enemy, int? hits)
        {
            if (enemy == null || enemy.Side != Core2Side.Enemy || !enemy.Alive || Player == null) return null;
            var view = Core2Knowledge.Project(enemy);
            var intent = enemy.Intent;
            if (view.Hidden || intent == null || intent.Kind == "staggered" || intent.Damage == null || intent.MoveId == null) return null;
            var raw = Content.Enemy(enemy.EnemyId).Move(intent.MoveId)?.Raw;
            if (raw == null) return null;
            var expanded = Core2EnemyMoves.Expanded(Rules, raw, intent.MoveId);
            var damageEffects = (expanded["effects"] as JArray ?? new JArray()).OfType<JObject>().Count(e => (string)e["op"] == "damage");
            var carrier = Core2EnemyMoves.Carrier(Rules, raw, intent.MoveId);
            var count = Math.Max(0, hits ?? intent.Hits ?? 1);
            return Core2IntentProjection.Project(Rules, enemy, Player, carrier, intent.Damage.Value, count, Math.Max(1, damageEffects),
                HitOptions(enemy, Player, carrier, null, null));
        }

        /// <summary>
        /// JS previewDamageHits' hitDamages for a revealed damaging intent: the HP each hit takes when it lands at the
        /// player's defenses (Block, Poise and stance), every earlier hit landed on a detached copy of the player.
        /// Null when ProjectIntent would be.
        /// </summary>
        public List<int> IntentHitDamages(Core2Combatant enemy, int? hits = null)
        {
            if (enemy == null || enemy.Side != Core2Side.Enemy || !enemy.Alive || Player == null) return null;
            var intent = enemy.Intent;
            if (Core2Knowledge.Project(enemy).Hidden || intent == null || intent.Kind == "staggered" || intent.Damage == null || intent.MoveId == null) return null;
            var raw = Content.Enemy(enemy.EnemyId).Move(intent.MoveId)?.Raw;
            if (raw == null) return null;
            var carrier = Core2EnemyMoves.Carrier(Rules, raw, intent.MoveId);
            var options = HitOptions(enemy, Player, carrier, null, null);
            var source = enemy.Clone();
            var target = Player.Clone();
            var damages = new List<int>();
            for (var i = 0; i < Math.Max(0, hits ?? intent.Hits ?? 1); i++)
            {
                var receipt = Core2Damage.Receipt(Rules, source, target, intent.Damage.Value, carrier, options);
                damages.Add(receipt.HpLoss);
                Core2Damage.ApplyToPreviewTarget(Rules, target, receipt);
            }
            return damages;
        }

        /// <summary>
        /// Would PlayCard(<paramref name="instanceId"/>, <paramref name="targetId"/>) be accepted now? Null when it would; else
        /// the refusal. The play is tried on a detached copy of the fight (JS candidateState), so this fight is untouched.
        /// </summary>
        public Core2PlayCheck CheckPlay(string instanceId, string targetId = null)
        {
            if (Result != null) return new Core2PlayCheck { Reason = "other", Message = "Combat is over" };
            if (PendingReaction != null) return new Core2PlayCheck { Reason = "other", Message = "Answer the pending reaction first" };
            if (Phase != "player") return new Core2PlayCheck { Reason = "other", Message = "Cards can only be played on the player turn" };
            var candidate = Candidate();
            candidate.ReactionsEnabled = false;
            try
            {
                candidate.DoPlayCard(new Core2ReactionPlay { CardInstanceId = instanceId, TargetId = targetId });
                return null;
            }
            catch (Core2PlayRefusedException refused) { return new Core2PlayCheck { Reason = refused.Reason ?? "other", Resource = refused.Resource, Message = refused.Message }; }
            catch (InvalidOperationException error) { return new Core2PlayCheck { Reason = error.Message.StartsWith("Invalid", StringComparison.Ordinal) ? "target" : "other", Message = error.Message }; }
        }

        /// <summary>JS cardTargetPlan(...).mode === 'enemy': the card in hand aims its immediate effects at an enemy.</summary>
        public bool CardTargetsEnemy(string instanceId)
        {
            var instance = Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            if (instance == null) return false;
            return CardAimsAtEnemy(Content.Card(instance.CardId), CardCarrier(instance));
        }

        /// <summary>
        /// JS previewCardHit → previewCardAttackHit: the hit chance (percent) of a hand card against one enemy, with the
        /// card's stance and its entry Precision projected onto the player and an unread enemy read as a neutral Melee
        /// target. Null when the card does not aim at enemies or the enemy is gone. HitChance is the percent the badge shows;
        /// Mode (advantage, disadvantage, normal) picks its tone.
        /// </summary>
        public Core2HitPreview PreviewCardHit(string instanceId, string targetId)
        {
            var instance = Hand.FirstOrDefault(c => c.InstanceId == instanceId);
            var target = Enemies.FirstOrDefault(e => e.Id == targetId);
            if (instance == null || target == null || !target.Alive || Player == null) return null;
            var carrier = CardCarrier(instance);
            carrier.InstanceId = instance.InstanceId;
            if (!CardAimsAtEnemy(Content.Card(instance.CardId), carrier) && carrier.Profile?.Maneuver != "counter") return null;
            var source = Player.Clone();
            if (!Core2Stances.SameStance(Player, carrier))
            {
                var grants = Core2Stances.EntryBonuses(Rules, carrier);
                var precision = grants != null && grants.Precision > 0 ? Core2Accuracy.PrecisionEntryGrant(Rules, source, grants.Precision) : 0;
                if (precision > 0) Core2Statuses.Apply(Ctx, source, "precision", precision);
            }
            var seen = target;
            if (Core2Knowledge.Project(target).Hidden)
            {
                seen = target.Clone();
                seen.Stance = new Core2Stance("neutral", "physical", "melee");
                seen.MatchingStanceCardMultiplier = 0;
            }
            return Core2Accuracy.Preview(Rules, source, seen, carrier, HitOptions(Player, target, carrier, null, null));
        }

        /// <summary>
        /// The live numbers a hand card's face prints (JS card.js preview tokens): its first damage effect through the
        /// player's damage chain before any target's defenses, and its Block through the player's Block gain, both read on
        /// a cloned player that has entered the card's stance.
        /// </summary>
        public Core2CardFaceValues CardFaceValues(Core2CardInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            var card = Content.Card(instance.CardId);
            var carrier = CardCarrier(instance);
            var values = new Core2CardFaceValues();
            // The card is read as it would be played: its stance entered first (JS doPlayCard → setProfileStance before
            // its effects), so a Defend reads its matching-stance Block and a Strike its matching-stance damage.
            var player = Player.Clone();
            Core2Stances.EnterStance(Ctx, player, carrier);
            foreach (var effect in card.Effects.OfType<JObject>())
            {
                var op = (string)effect["op"];
                var amount = effect["amount"];
                if (amount == null || (amount.Type != JTokenType.Integer && amount.Type != JTokenType.Float)) continue;
                if (op == "damage" && values.Damage == null)
                {
                    var baseDamage = (int)Math.Floor((double)amount);
                    values.BaseDamage = baseDamage;
                    values.Damage = Math.Max(0, (int)Math.Floor(Core2Damage.Receipt(Rules, player, null, baseDamage, carrier).Raw));
                    var hits = effect["hits"];
                    values.Hits = hits != null && (hits.Type == JTokenType.Integer || hits.Type == JTokenType.Float) ? Math.Max(1, (int)Math.Floor((double)hits)) : 1;
                }
                else if (op == "block" && values.Block == null)
                {
                    var baseBlock = (int)Math.Floor((double)amount);
                    values.BaseBlock = baseBlock;
                    values.Block = Core2Stances.GainBlock(Ctx, player.Clone(), baseBlock, carrier).Block;
                }
            }
            return values;
        }
    }
}
