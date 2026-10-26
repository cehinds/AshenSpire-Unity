// Core2Damage.cs — one Core 2 hit: the damage chain, Block then Poise absorption, the Staggered stance.
//
// JS at the pin:
//   src/engine/gameProfileCombat.js profileDamageReceipt — the chain, in this order, at full precision and floored
//       once (owner, 2026-10-18 16:05): card damage + rating (AR, or PR for a player's spell/magic hit) × attack
//       rating weight; × matching-stance effectiveness (1.25); × Vulnerable (1.25, FixCore statuses); × Smash vs
//       defensive (1.25) or the profile's advantage multiplier; × defensive-stance mitigation; floor. A Smash that
//       beats Defend/Prepared adds floor(25% × target Block) and costs Poise floor(50% × the hit) instead of the
//       25% absorption. The hit splits into physical and magic parts (combatRatings.js splitContactParts); Block
//       takes physical, Barrier magic; with Ward off the magic left meets Poise too.
//   src/model/fixCoreMechanics.js protectionReceipt — Poise lets floor(x × 0.75) through while it holds any, and
//       never absorbs more than it holds (owner, 2026-10-18 20:09); Poise loses what it absorbed (D13/D4).
//   src/engine/actions.js applyProfileDamage / fixCorePoiseBreak / fixCoreStagger / staggerEnemy — a live hit
//       first rolls Accuracy, then applies the receipt; Poise taken from above zero to zero breaks it: the owner
//       takes the Staggered stance, loses staminaLoss SP at its next turn start and gains vulnerableStacks
//       Vulnerable (which only lengthens it: ×1.25 is one flat multiplier).
//   src/engine/combatDamagePreview.js applyReceiptToPreviewTarget — the same on a detached preview target.
//   Phase 5b: a Counter return (carrier counterReturnAttack, "flatReturn") is final pre-protection damage: no
//   matching-stance, Vulnerable, advantage or defensive-stance stage (SPEC §17; profileDamageReceipt). The magic share
//   is the whole damage-type table (Core2DamageTypes, JS magicShareOf), and a player's hit reads PR when it is a spell,
//   has any magic share, or is a magical card (JS isMagicalAttack / cardIsMagical). drainFixCorePoise (Poise damage
//   riders and Counter returns) and fixCorePoiseBreak live here; the Counter's advantage Block is added by
//   Core2Contacts before the hit lands (actions.js applyProfileDamage counterBlock).
// Not ported (named where they enter the chain): the status-rule "proposal" stage (legacy-adapter statuses),
// per-armament rating sources (gear, phase 8), blockCap (no Core 2 status sets one at the pin).
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2DamageReceipt
    {
        /// <summary>The chain before defenses (JS receipt.raw): rating, stance and Vulnerable applied, not floored.</summary>
        public double Raw { get; set; }
        /// <summary>The hit as it meets the target's defenses, floored (JS incoming).</summary>
        public int Incoming { get; set; }
        public int Physical { get; set; }
        public int Magical { get; set; }
        public int Blocked { get; set; }
        public int BarrierMitigated { get; set; }
        public int WardMitigated { get; set; }
        /// <summary>What Poise absorbed (JS poiseMitigated; the log's "Poise absorbed N").</summary>
        public int PoiseAbsorbed { get; set; }
        /// <summary>Poise a Smash vs a defensive stance takes as damage (JS poiseDrain / poiseDamage).</summary>
        public int PoiseDamage { get; set; }
        public int HpLoss { get; set; }
        /// <summary>JS receipt.amount: HP loss plus Block taken (what the damage event reports).</summary>
        public int Amount => HpLoss + Blocked;
        public int BlockRemaining { get; set; }
        /// <summary>The shown hit chance as a fraction (JS hitChance = preview % / 100).</summary>
        public double HitChance { get; set; }
        public bool Evaded { get; set; }
        public bool Advantage { get; set; }
        public bool SmashVsDefensive { get; set; }
        public int SmashBlockBonus { get; set; }
        public bool DefenseAdvantage { get; set; }
        public string AttackStance { get; set; }
        public string DefenseStance { get; set; }
        /// <summary>JS interrupted: an advantaged Smash under staggerOnAdvantage (enemy targets only stagger from it).</summary>
        public bool Interrupted { get; set; }
    }

    /// <summary>What one live hit did (JS damageDealt event fields plus the outcome of the Accuracy roll).</summary>
    public sealed class Core2HitOutcome
    {
        public Core2HitResult Accuracy { get; set; }
        public Core2DamageReceipt Receipt { get; set; }
        public bool PoiseBroken { get; set; }
        public bool BlockBroken { get; set; }
        /// <summary>An advantaged Smash staggered the enemy without breaking its Poise.</summary>
        public bool Interrupted { get; set; }
        public int Amount => Receipt?.Amount ?? 0;
    }

    public static class Core2Damage
    {
        /// <summary>JS combatRatings.js splitContactParts.</summary>
        public static void SplitContactParts(double amount, double baseDamage, double ratingBonus, string ratingPart, double share, string primary, out int magic, out int physical)
        {
            var total = Math.Max(0, (int)Math.Floor(amount));
            var card = Math.Max(0, (int)Math.Floor(baseDamage));
            var rating = Math.Max(0, (int)Math.Floor(ratingBonus));
            var cardMagic = share >= 1 ? card : share > 0 ? (int)Math.Floor(card * share + 1e-9) : 0;
            magic = cardMagic; physical = card - cardMagic;
            if (ratingPart == "magic") magic += rating; else if (ratingPart == "physical") physical += rating;
            var rest = total - card - (ratingPart != null ? rating : 0);
            if (primary == "magic") magic += rest; else physical += rest;
            if (magic < 0) { physical += magic; magic = 0; }
            if (physical < 0) { magic += physical; physical = 0; }
        }

        /// <summary>
        /// JS protectionReceipt for one pool: while the pool holds anything, floor(amount × multiplier) gets through; the
        /// pool absorbs the rest, capped at what it holds (Poise; JS caps only the physical pool).
        /// </summary>
        public static int Protect(Core2Pool pool, double multiplier, int amount, bool capAtPool, out int absorbed)
        {
            absorbed = 0;
            if (!(amount > 0) || pool == null || !(pool.Value > 0)) return Math.Max(0, amount);
            var taken = Math.Max(0, (int)Math.Floor(amount * multiplier));
            absorbed = Math.Max(0, amount - taken);
            if (capAtPool && absorbed > pool.Value) { taken += absorbed - pool.Value; absorbed = pool.Value; }
            return taken;
        }

        /// <summary>The hit's magic share (JS magicShareOf over {...combatProfile, magicShare: carrier.magicShare}).</summary>
        public static double MagicShare(Core2Rules rules, Core2Carrier carrier) => rules.DamageTypes.MagicShareOf(carrier?.Profile, carrier?.MagicShare);

        private static readonly HashSet<string> MagicalTagWords = new HashSet<string>(new[] { "magic", "magical", "arcane", "holy", "fire", "spell" }, StringComparer.Ordinal);
        /// <summary>JS isMagicalAttack / cardIsMagical: a non-physical damage school, a magical tag word, or a Mana cost.</summary>
        public static bool IsMagicalAttack(Core2Carrier carrier)
        {
            if (carrier == null) return false;
            if (!string.IsNullOrEmpty(carrier.DamageSchool)) return carrier.DamageSchool != "physical";
            if (carrier.Tags.Any(tag => tag != null && MagicalTagWords.Contains(tag.Substring(tag.LastIndexOf(':') + 1)))) return true;
            return carrier.BaseMp > 0;
        }

        /// <summary>JS profileDamageReceipt under FixCore. <paramref name="roll"/> is a legacy chance roll; FixCore passes null and rolls Accuracy first.</summary>
        public static Core2DamageReceipt Receipt(Core2Rules rules, Core2Combatant source, Core2Combatant target, int baseDamage, Core2Carrier carrier, Core2HitOptions options = null, double? roll = null)
        {
            if (!rules.FixCoreEnabled) throw new InvalidOperationException("Core 2 damage runs only under FixCore");
            carrier ??= new Core2Carrier();
            var attacking = Core2Stances.ConfiguredStance(carrier);
            // Stance capacity is 1 in Core 2 (no status grants a second stance), so the defending stance is the held one
            // (JS concurrentStances.js defensiveCombatStance with activeCombatStances of one row).
            var defending = Core2Stances.ConfiguredStance(target?.Stance);
            var advantage = Core2Stances.StanceAdvantage(rules, attacking, defending);
            var flatReturn = carrier.Profile?.CounterReturnAttack == true;
            var share = MagicShare(rules, carrier);
            // A player's spell (camp, any magic share, or a magical card) reads PR; enemies keep AR for their spells (their PR is 0 in data).
            var ratingId = source != null && source.Side != Core2Side.Enemy && (carrier.Profile?.Camp == "spell" || share > 0 || IsMagicalAttack(carrier)) ? "pr" : "ar";
            var rating = source == null ? 0 : Core2Statuses.RatingValue(rules, source, ratingId);
            double ratingBonus = carrier.SkipRatingBonus ? 0 : rating * rules.AttackRatingWeight;
            double raw = baseDamage;
            raw = raw + ratingBonus;
            // A Core 2 Counter return is final pre-protection damage (SPEC §17): no matching-stance or defensive-stance stage.
            var stanceMultiplier = flatReturn ? 1 : Core2Stances.StanceEffectiveness(rules, source, carrier);
            raw = raw * stanceMultiplier;
            if (!flatReturn && target != null && target.StatusStacks("vulnerable") > 0) raw = raw * rules.VulnerabilityMultiplier;
            var defenseMultiplier = Core2Stances.StanceMitigationMultiplier(rules, attacking, defending, out var defenseAdvantage);
            var smashRule = advantage && attacking == "smash";
            // Core 2 Counter returns carry their advantage in the return itself (SPEC §17), so the chain does not multiply it again.
            var advantageMultiplier = smashRule ? rules.SmashDamageMultiplier : advantage && !flatReturn ? rules.StanceAdvantageMultiplier : 1;
            var exact = raw * advantageMultiplier;
            exact = exact * (flatReturn ? 1 : defenseMultiplier);
            var amount = Math.Max(0, (int)Math.Floor(exact));
            var preview = target == null ? null : Core2Accuracy.Preview(rules, source, target, carrier, options);
            var hitChance = (preview?.HitChance ?? 100) / 100.0;
            var evaded = roll.HasValue && roll.Value >= hitChance;
            if (evaded) amount = 0;
            var smashBlockBonus = 0;
            if (smashRule && !evaded && target != null)
            {
                // Read from the Block held before this hit's own absorption.
                smashBlockBonus = (int)Math.Floor(rules.SmashBlockFraction * Math.Max(0, target.Block));
                amount += smashBlockBonus;
            }
            var incoming = amount;
            SplitContactParts(incoming, baseDamage, ratingBonus, ratingId == "pr" || share >= 1 ? "magic" : "physical", share, share >= 1 ? "magic" : "physical", out var magical, out var physical);
            var blocked = Math.Min(Math.Max(0, target?.Block ?? 0), physical);
            var barrierMitigated = Math.Min(Math.Max(0, target?.Barrier ?? 0), magical);
            // Ward off: the magic Barrier left meets Poise with the physical residual.
            var poiseMeetsMagic = !rules.WardEnabled;
            var poiseIn = physical - blocked + (poiseMeetsMagic ? magical - barrierMitigated : 0);
            int poiseAbsorbed = 0, wardAbsorbed = 0;
            var poiseThrough = smashRule ? poiseIn : Protect(target?.Poise, rules.PoiseProtectionMultiplier, poiseIn, true, out poiseAbsorbed);
            var wardIn = poiseMeetsMagic ? 0 : magical - barrierMitigated;
            var wardThrough = rules.WardEnabled ? Protect(target?.Ward, WardProtectionMultiplier, wardIn, false, out wardAbsorbed) : wardIn;
            var poiseDamage = smashRule && !evaded ? Math.Min(Math.Max(0, target?.Poise?.Value ?? 0), (int)Math.Floor(rules.SmashPoiseFraction * incoming)) : 0;
            var hpLoss = poiseThrough + wardThrough;
            return new Core2DamageReceipt
            {
                Raw = raw, Incoming = incoming, Physical = physical, Magical = magical, Blocked = blocked, BarrierMitigated = barrierMitigated,
                WardMitigated = wardAbsorbed, PoiseAbsorbed = poiseAbsorbed, PoiseDamage = poiseDamage, HpLoss = hpLoss,
                BlockRemaining = Math.Max(0, (target?.Block ?? 0) - blocked), HitChance = hitChance, Evaded = evaded, Advantage = advantage,
                SmashVsDefensive = smashRule, SmashBlockBonus = smashBlockBonus, DefenseAdvantage = defenseAdvantage,
                AttackStance = attacking, DefenseStance = defending,
                Interrupted = advantage && rules.StaggerOnAdvantage && ((IList<string>)rules.StaggerManeuvers).Contains(attacking),
            };
        }

        /// <summary>defense.wardProtection mechanic default (engine/mechanics/defense.wardProtection/mechanic.json); Ward is off in Core 2.</summary>
        public const double WardProtectionMultiplier = 0.75;

        /// <summary>
        /// JS fixCoreStagger: the Staggered stance (when the rules say so), staminaLoss SP owed to the next turn start,
        /// and vulnerableStacks Vulnerable.
        /// </summary>
        public static void Stagger(Core2CombatContext ctx, Core2Combatant entity)
        {
            var rules = ctx.Rules;
            // The hit that broke Poise still earns its Counter return: remember the spent Counter for the action's completion (SPEC §17).
            if (entity.Counter != null && entity.Counter.Charges == 0) entity.CounterStaggeredSerial = entity.Counter.Serial;
            entity.Counter = null;
            if (rules.StaggeredStance) entity.Stance = Core2Stance.Staggered(entity.OwnerCycle);
            if (rules.StaggeredStaminaLoss > 0) entity.PendingStaminaLoss += rules.StaggeredStaminaLoss;
            if (rules.StaggeredVulnerableStacks > 0) Core2Statuses.Apply(ctx, entity, "vulnerable", rules.StaggeredVulnerableStacks);
        }

        /// <summary>
        /// JS applyProfileDamage under FixCore: roll Accuracy (a miss deals nothing), then Block, Barrier, Poise and HP
        /// take the receipt; Poise emptied from above zero by a hit that leaves the target standing breaks it.
        /// </summary>
        public static Core2HitOutcome ApplyAttack(Core2CombatContext ctx, Core2Combatant source, Core2Combatant target, int baseDamage, Core2Carrier carrier, ICore2Dice dice, Core2HitOptions options = null)
        {
            if (target == null || !target.Alive) return new Core2HitOutcome();
            var rules = ctx.Rules;
            // A contact of a committed action was rolled once when the action began (JS carrier.expansionContact).
            var accuracy = options?.AccuracyResolved == true ? null : Core2Accuracy.Resolve(rules, source, target, carrier, dice, options);
            if (accuracy != null && !accuracy.Hit) return new Core2HitOutcome { Accuracy = accuracy };
            var receipt = Receipt(rules, source, target, baseDamage, carrier, options);
            var blockBefore = target.Block;
            var poiseBefore = target.Poise?.Value ?? 0;
            target.Block = receipt.BlockRemaining;
            target.Barrier = Math.Max(0, target.Barrier - receipt.BarrierMitigated);
            if (target.Ward != null) target.Ward.Value = Math.Max(0, target.Ward.Value - receipt.WardMitigated);
            if (target.Poise != null) target.Poise.Value = Math.Max(0, target.Poise.Value - receipt.PoiseAbsorbed - receipt.PoiseDamage);
            target.Hp -= receipt.HpLoss;
            if (target.Hp <= 0) { target.Hp = 0; target.Alive = false; }
            var outcome = new Core2HitOutcome { Accuracy = accuracy, Receipt = receipt, BlockBroken = blockBefore > 0 && target.Block == 0 };
            if (poiseBefore > 0 && target.Poise != null && target.Poise.Value == 0 && target.Alive && target.Hp > 0)
            {
                outcome.PoiseBroken = true;
                PoiseBreak(ctx, target);
            }
            // One break is one stagger: an advantaged Smash staggers an enemy whose Poise did not break, once per round.
            else if (receipt.Interrupted && target.Side == Core2Side.Enemy && target.Alive && ctx.ClaimStagger(target, rules.StaggerLimitPerTargetPerRound))
            {
                outcome.Interrupted = true;
                Stagger(ctx, target);
            }
            return outcome;
        }

        /// <summary>JS fixCorePoiseBreak: the owner is Staggered; an enemy's break also uses the round's stagger claim.</summary>
        public static void PoiseBreak(Core2CombatContext ctx, Core2Combatant target)
        {
            if (target.Side == Core2Side.Enemy) ctx.ClaimStagger(target, ctx.Rules.StaggerLimitPerTargetPerRound);
            Stagger(ctx, target);
        }

        /// <summary>
        /// JS drainFixCorePoise: Poise damage a card, rider or Counter return carries under FixCore — whole numbers, never
        /// more than the pool holds; emptying it (with the owner still standing) breaks Poise. Returns what it spent.
        /// </summary>
        public static int DrainPoise(Core2CombatContext ctx, Core2Combatant target, int amount)
        {
            var n = Math.Max(0, amount);
            if (target == null || !target.Alive || !(n > 0) || target.Poise == null || !(target.Poise.Value > 0)) return 0;
            var spent = Math.Min(target.Poise.Value, n);
            target.Poise.Value -= spent;
            if (target.Poise.Value == 0 && target.Hp > 0) PoiseBreak(ctx, target);
            return spent;
        }

        /// <summary>JS applyReceiptToPreviewTarget: carry one landed hit to the next on a detached preview target.</summary>
        public static void ApplyToPreviewTarget(Core2Rules rules, Core2Combatant defender, Core2DamageReceipt receipt)
        {
            defender.Block = receipt.BlockRemaining;
            defender.Barrier = Math.Max(0, defender.Barrier - receipt.BarrierMitigated);
            if (defender.Ward != null) defender.Ward.Value = Math.Max(0, defender.Ward.Value - receipt.WardMitigated);
            if (defender.Poise == null) return;
            var before = defender.Poise.Value;
            defender.Poise.Value = Math.Max(0, before - receipt.PoiseAbsorbed - receipt.PoiseDamage);
            if (before > 0 && defender.Poise.Value == 0)
            {
                // JS previewPoiseBreak: the stance and Vulnerable only (no SP is owed on a preview).
                if (rules.StaggeredStance) defender.Stance = Core2Stance.Staggered(defender.OwnerCycle);
                if (rules.StaggeredVulnerableStacks > 0)
                {
                    if (!defender.Statuses.TryGetValue("vulnerable", out var inst)) defender.Statuses["vulnerable"] = inst = new Core2StatusInstance();
                    inst.Stacks += rules.StaggeredVulnerableStacks;
                }
            }
        }
    }
}
