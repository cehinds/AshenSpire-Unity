// Core2Pricing.cs — what a card costs in SP and MP, and the SP/MP turn pools that pay it (SPEC.md §0.3 "Card
// action costs": JS SPEC §4.3a as changed for Core 2 by §17 and docs/STANCE-COMBAT-RULES.md).
//
// JS at the pin:
//   src/model/stanceRules.js        stanceStaminaCost, stanceManaCost, hasAdept, adeptMinimum
//   src/engine/gameProfileCombat.js profileCardCosts (the configured profile's price: X cost, SP/MP lanes)
//   src/engine/combat.js            doPlayCard payment and refusals; startPlayerTurn pool refill
//   src/model/core2ActionPolicy.js  core2ActionPolicyEnabled
//
// Rules, in short: a card played in the stance it would enter is "same stance". Under the Core 2 action policy a
// Martial Adept (training.basic: Reaver, Rogue) pays 1 SP less for a non-Spell; a Magic Adept (training.graduate:
// Starseer, Herald) pays 1 MP less for a Spell. A Spell after a mirrored Spell (or the reverse) is a Spell chain and
// pays 1 MP less for anyone. The floor is 1 on the player's own turn and 0 for a trait:reaction card priced on the
// enemy turn. X-cost cards spend all SP and take no discount. Every price is a whole number.
using System;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>A card's price (JS { energy, stamina, mana }; energy and stamina are the one SP lane under Core 2).</summary>
    public struct Core2Costs
    {
        public int Sp;
        public int Mp;
        public Core2Costs(int sp, int mp) { Sp = sp; Mp = mp; }
        public override string ToString() => Sp + " SP, " + Mp + " MP";
    }

    /// <summary>A play the pools cannot pay (JS F.playRefusal(..., 'cost', { resource, need, have })).</summary>
    public sealed class Core2PlayRefusedException : InvalidOperationException
    {
        public string Resource { get; }
        public int Need { get; }
        public int Have { get; }
        public Core2PlayRefusedException(string message, string resource, int need, int have) : base(message) { Resource = resource; Need = need; Have = have; }
    }

    public static class Core2Pricing
    {
        /// <summary>JS hasAdept: a player (not an enemy, not classUnequipped) whose character row carries the training tag.</summary>
        public static bool HasAdept(Core2CombatContext ctx, Core2Combatant entity, string tag) =>
            entity != null && entity.Side == Core2Side.Player && !entity.ClassUnequipped && ctx.CharacterTags(entity.ClassId).Contains(tag);

        /// <summary>JS adeptMinimum: 0 for a trait:reaction carrier priced on the enemy turn, else the own-turn floor.</summary>
        public static int AdeptMinimum(Core2CombatContext ctx, Core2Carrier carrier, int ownTurnMinimum) =>
            ctx.Phase == "enemy" && carrier != null && carrier.IsReaction ? 0 : ownTurnMinimum;

        /// <summary>JS stanceStaminaCost(base, entity, carrier, ctx, mpBase).</summary>
        public static int StanceStaminaCost(Core2CombatContext ctx, int baseSp, Core2Combatant entity, Core2Carrier carrier, int mpBase = 0)
        {
            var rules = ctx.Rules;
            if (!Core2Stances.SameStance(entity, carrier)) return baseSp;
            if (!rules.FixCoreEnabled) throw new InvalidOperationException("Core 2 prices only under FixCore (fixCoreMechanics.enabled)");
            // Authored enemy plans budget moves through their existing one-lane rule.
            if (entity?.Side == Core2Side.Enemy)
            {
                if (!(baseSp > 0) || mpBase > 0) return baseSp;
                return Math.Max(rules.StanceMinimumCost, baseSp - rules.StanceDiscount);
            }
            if (ctx.Stamp.ActionPolicyEnabled)
            {
                if (!(baseSp > 0) || carrier.XCost || Core2Stances.IsSpellCard(carrier) || !HasAdept(ctx, entity, "training.basic")) return baseSp;
                return Math.Max(AdeptMinimum(ctx, carrier, rules.StanceMinimumCost), baseSp - rules.StanceDiscount);
            }
            // A fight stamped before the action policy keeps its accepted reaction-only discount.
            if (!(baseSp > 0) || carrier.XCost || ctx.Phase != "enemy" || !carrier.IsReaction) return baseSp;
            return Math.Max(0, baseSp - rules.ReactionHandCostDiscount);
        }

        /// <summary>JS stanceManaCost(base, entity, carrier, ctx).</summary>
        public static int StanceManaCost(Core2CombatContext ctx, int baseMp, Core2Combatant entity, Core2Carrier carrier)
        {
            var rules = ctx.Rules;
            if (!rules.FixCoreEnabled || !(baseMp > 0)) return baseMp;
            if (ctx.Stamp.ActionPolicyEnabled && entity?.Side != Core2Side.Enemy)
            {
                var adept = Core2Stances.IsSpellCard(carrier) && HasAdept(ctx, entity, "training.graduate") && Core2Stances.SameStance(entity, carrier);
                if (!adept && !Core2Stances.SpellChain(entity, carrier)) return baseMp;
                return Math.Max(adept ? AdeptMinimum(ctx, carrier, rules.StanceMinimumCost) : rules.StanceMinimumCost, baseMp - rules.StanceDiscount);
            }
            if (!(Core2Stances.SameStance(entity, carrier) || Core2Stances.SpellChain(entity, carrier))) return baseMp;
            return Math.Max(rules.StanceMinimumCost, baseMp - rules.StanceDiscount);
        }

        /// <summary>
        /// JS profileCardCosts: the price the player pays for a card right now. X cost spends all SP; a price in a
        /// pool the profile does not include is a content error (JS "requires an authored disabled-resource variant").
        /// </summary>
        public static Core2Costs CardCosts(Core2CombatContext ctx, Core2Combatant player, Core2Carrier card)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (card == null) throw new ArgumentNullException(nameof(card));
            var rules = ctx.Rules;
            var spLane = rules.Includes("sp"); var mpLane = rules.Includes("mp");
            var baseSp = card.XCost && spLane ? Math.Max(0, player.Stamina) : card.BaseSp;
            var baseMp = card.BaseMp;
            var sp = rules.FixCoreEnabled ? StanceStaminaCost(ctx, baseSp, player, card, baseMp) : baseSp;
            var mp = StanceManaCost(ctx, baseMp, player, card);
            if ((!spLane && sp > 0) || (!mpLane && mp > 0)) throw new InvalidOperationException("Card " + (card.Id ?? "unknown") + " requires an authored disabled-resource variant");
            return new Core2Costs(spLane ? Math.Max(0, sp) : 0, mpLane ? Math.Max(0, mp) : 0);
        }
    }

    /// <summary>The SP and MP turn pools (JS combat.js startPlayerTurn / doPlayCard; enemy turn start for SP).</summary>
    public static class Core2Pools
    {
        /// <summary>
        /// Owner's turn start: SP refills to its maximum less what a Staggered break withheld (owed to this turn only);
        /// MP is not restored unless the profile sets resources.manaPerTurn (Core 2 does not).
        /// </summary>
        public static void StartTurn(Core2CombatContext ctx, Core2Combatant entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            entity.Stamina = ctx.Rules.Includes("sp") ? Math.Max(0, entity.MaxStamina - entity.PendingStaminaLoss) : 0;
            entity.PendingStaminaLoss = 0;
            if (ctx.Rules.ManaPerTurn > 0) entity.Mana = Math.Min(entity.MaxMana, entity.Mana + ctx.Rules.ManaPerTurn);
        }

        /// <summary>Refuses a price the pools cannot pay, naming the pool (JS doPlayCard refusals).</summary>
        public static void RequireAffordable(Core2Combatant entity, Core2Costs costs)
        {
            if (entity.Stamina < costs.Sp) throw new Core2PlayRefusedException("Not enough Actions (Stamina) to play this card", "stamina", costs.Sp, entity.Stamina);
            if (entity.Mana < costs.Mp) throw new Core2PlayRefusedException("Not enough mana to play this card", "mana", costs.Mp, entity.Mana);
        }

        public static bool CanAfford(Core2Combatant entity, Core2Costs costs) => entity.Stamina >= costs.Sp && entity.Mana >= costs.Mp;

        /// <summary>Pays a card's price (X cost was priced at all the SP held, so it spends every point).</summary>
        public static void Pay(Core2Combatant entity, Core2Costs costs)
        {
            RequireAffordable(entity, costs);
            entity.Stamina -= costs.Sp;
            entity.Mana -= costs.Mp;
        }

        /// <summary>Prices and pays a card in one step: the price shown is the price paid (JS cardPlayCosts → doPlayCard).</summary>
        public static Core2Costs PayFor(Core2CombatContext ctx, Core2Combatant player, Core2Carrier card)
        {
            var costs = Core2Pricing.CardCosts(ctx, player, card);
            Pay(player, costs);
            return costs;
        }
    }
}
