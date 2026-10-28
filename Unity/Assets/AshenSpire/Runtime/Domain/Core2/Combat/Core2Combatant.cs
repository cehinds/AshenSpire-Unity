// Core2Combatant.cs — the live state a Core 2 fight keeps per combatant, and the context the rules read.
//
// Field names follow the JS combat entity (src/engine/combat.js createCombat / runCombat.js): SP is JS
// `stamina`/`energy` (one unified turn pool under Core 2, src/engine/gameProfileCombat.js profileCardCosts),
// MP is `mana`, Poise and Ward are `persistentPoise` / `persistentWard`, the stance is `combatStance`.
// Engine-free: the presentation layer (phase 6) reads these, it never writes them.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    public enum Core2Side { Player, Enemy }

    /// <summary>A pool with a value and a maximum (JS persistentPoise / persistentWard).</summary>
    public sealed class Core2Pool
    {
        public int Value { get; set; }
        public int Max { get; set; }
        public Core2Pool(int value, int max) { Value = value; Max = max; }
        public Core2Pool Clone() => new Core2Pool(Value, Max);
    }

    /// <summary>One source of a combatant's ratings (JS ratingSources row): its name, kind, id and slot, and the AR and PR it gives.</summary>
    public sealed class Core2RatingSource
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string SourceId { get; set; }
        public string Slot { get; set; }
        public double Ar { get; set; }
        public double Pr { get; set; }
    }

    /// <summary>One status on a combatant (JS entity.statuses[id]).</summary>
    public sealed class Core2StatusInstance
    {
        public int Stacks { get; set; }
        /// <summary>Turns left for a duration status (JS inst.duration).</summary>
        public int Duration { get; set; }
        /// <summary>Precision stacks a trait:persistent grant keeps past its owner's turn end (JS persistentStacks).</summary>
        public int PersistentStacks { get; set; }
        /// <summary>A build-up status's threshold (JS inst.meter.max, a proc status such as Bleed); Stacks holds the meter value. 0 for none.</summary>
        public int MeterMax { get; set; }
        public Core2StatusInstance Clone() => new Core2StatusInstance { Stacks = Stacks, Duration = Duration, PersistentStacks = PersistentStacks, MeterMax = MeterMax };
    }

    public sealed partial class Core2Combatant
    {
        public string Id { get; set; }
        public Core2Side Side { get; set; }
        /// <summary>Player class (characters.json row id): decides the Adept discount (JS stanceRules.js hasAdept).</summary>
        public string ClassId { get; set; }
        /// <summary>JS classUnequipped: a player without the class's gear has no Adept discount.</summary>
        public bool ClassUnequipped { get; set; }
        public string EnemyId { get; set; }
        /// <summary>False for an enemy whose definition has no level profile: its Precision mix counts level 0 (JS stanceHit.js mixLevel).</summary>
        public bool HasLevelProfile { get; set; } = true;

        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public bool Alive { get; set; } = true;
        public int Block { get; set; }
        public int Barrier { get; set; }
        public Core2Pool Poise { get; set; }
        public Core2Pool Ward { get; set; }

        /// <summary>SP left this turn (JS player.energy / stamina; enemy stamina).</summary>
        public int Stamina { get; set; }
        /// <summary>SP the pool refills to at its owner's turn start (JS energyMax / enemy maxStamina).</summary>
        public int MaxStamina { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }
        /// <summary>SP the next turn start withholds (a Staggered break; JS pendingActionLoss / pendingStaminaLoss).</summary>
        public int PendingStaminaLoss { get; set; }

        public Dictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Level { get; set; } = 1;
        /// <summary>Base ratings (ar, dr, pr, ward, poise) before stance and status modifiers (JS entity.ratings).</summary>
        public Dictionary<string, int> Ratings { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, Core2StatusInstance> Statuses { get; set; } = new Dictionary<string, Core2StatusInstance>(StringComparer.Ordinal);

        /// <summary>
        /// JS delete entity.statuses[id]: the status goes and the rest keep their order. A JS object adds a re-applied key at
        /// the end; a .NET Dictionary would reuse the freed slot, so the turn-start decay would walk statuses in another
        /// order. Always remove a status through here.
        /// </summary>
        public void RemoveStatusEntry(string id)
        {
            if (!Statuses.ContainsKey(id)) return;
            var kept = new Dictionary<string, Core2StatusInstance>(StringComparer.Ordinal);
            foreach (var pair in Statuses) if (pair.Key != id) kept.Add(pair.Key, pair.Value);
            Statuses = kept;
        }

        public Core2Stance Stance { get; set; }
        /// <summary>Stances held alongside the main one (JS concurrentStances); a match with any counts as the same stance.</summary>
        public List<Core2Stance> ConcurrentStances { get; set; } = new List<Core2Stance>();

        public int BasePrecisionBonus { get; set; }
        public int BaseEvasionBonus { get; set; }
        public bool AccuracyAdvantage { get; set; }
        public bool AccuracyDisadvantage { get; set; }
        public bool EvadeAdvantage { get; set; }
        public bool EvadeDisadvantage { get; set; }
        /// <summary>The round whose turn-start decay already ran (JS fixCoreDecayRound): decay is once per round.</summary>
        public int? DecayRound { get; set; }

        // ---- phase 5b: tactical turn, Counter and range state (JS combatMatchups.js, counterRule.js) ----------------
        /// <summary>The prepared Counter (JS combatCounter), or null.</summary>
        public Core2CounterState Counter { get; set; }
        /// <summary>JS combatOwnerCycle: this combatant's turn count; a Counter and the Staggered stance lapse by it.</summary>
        public int OwnerCycle { get; set; }
        /// <summary>JS combatPreparationSerial: the last Counter serial issued.</summary>
        public int PreparationSerial { get; set; }
        /// <summary>JS combatCounterReturnedCycle: the owner cycle an automatic Counter return was spent in (one per cycle).</summary>
        public int? CounterReturnedCycle { get; set; }
        /// <summary>JS combatCounterStaggered: the serial of a spent Counter whose covered hit broke Poise (it still answers).</summary>
        public int? CounterStaggeredSerial { get; set; }
        /// <summary>JS counters.cardsPlayedThisCombat: a spell Counter stays armed only while no later card is accepted.</summary>
        public int CardsPlayed { get; set; }
        /// <summary>JS rangeActorKey: "player:&lt;seat&gt;" or "enemy:&lt;id&gt;"; null derives it from Side and Id.</summary>
        public string RangeKey { get; set; }
        /// <summary>JS combatReactionTurn: the combat turn this enemy last reacted in (one enemy reaction per turn).</summary>
        public int? CombatReactionTurn { get; set; }
        /// <summary>JS entity.ratingSources: where a player's AR/PR come from (attributes, each worn piece), for the damage receipt's lines.</summary>
        public List<Core2RatingSource> RatingSources { get; set; } = new List<Core2RatingSource>();

        public int StatusStacks(string id) => Statuses.TryGetValue(id, out var row) ? Math.Max(0, row.Stacks) : 0;
        public int Attribute(string id) => Attributes.TryGetValue(id, out var value) ? value : 0;
        public int Rating(string id) => Ratings.TryGetValue(id, out var value) ? value : 0;

        /// <summary>Copies the phase 5c fight state (plan, history, reads) into a clone (Core2CombatantFight.cs).</summary>
        partial void CloneFightState(Core2Combatant copy);

        public Core2Combatant Clone()
        {
            var copy = (Core2Combatant)MemberwiseClone();
            copy.Poise = Poise?.Clone();
            copy.Ward = Ward?.Clone();
            copy.Attributes = new Dictionary<string, int>(Attributes, StringComparer.Ordinal);
            copy.Ratings = new Dictionary<string, int>(Ratings, StringComparer.Ordinal);
            copy.Statuses = Statuses.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.Ordinal);
            copy.ConcurrentStances = new List<Core2Stance>(ConcurrentStances);
            copy.Counter = Counter?.Clone();
            // Ability riders, burdens and intent reveals change as cards play: a preview copy (Candidate) gets its own.
            copy.AbilityRiders = AbilityRiders?.Clone();
            copy.BurdenPiles = BurdenPiles == null ? null : new Core2BurdenPiles
            {
                Draw = Core2CardInstance.CloneAll(BurdenPiles.Draw), Hand = Core2CardInstance.CloneAll(BurdenPiles.Hand),
                Discard = Core2CardInstance.CloneAll(BurdenPiles.Discard), Exhaust = Core2CardInstance.CloneAll(BurdenPiles.Exhaust),
                Budget = BurdenPiles.Budget, Tax = BurdenPiles.Tax,
            };
            copy.IntentRevealedUntil = new Dictionary<string, int>(IntentRevealedUntil, StringComparer.Ordinal);
            CloneFightState(copy);
            return copy;
        }
    }

    /// <summary>
    /// What a Core 2 rule reads besides the combatants: the fight stamp (rules and action rules version), the
    /// character tags for Adept, whose turn it is and the round number (JS ctx.phase, ctx.turn).
    /// </summary>
    public sealed class Core2CombatContext
    {
        public Core2FightStamp Stamp { get; }
        public Core2Rules Rules => Stamp.Rules;
        /// <summary>"player" or "enemy" (JS combat.phase): a reaction priced on the enemy turn may drop to 0 SP.</summary>
        public string Phase { get; set; } = "player";
        public int Turn { get; set; } = 1;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _characterTags;

        public Core2CombatContext(Core2FightStamp stamp, IReadOnlyDictionary<string, IReadOnlyList<string>> characterTags)
        {
            Stamp = stamp ?? throw new ArgumentNullException(nameof(stamp));
            _characterTags = characterTags ?? new Dictionary<string, IReadOnlyList<string>>();
        }

        /// <summary>A detached copy (JS candidateState): the same stamp, phase, turn and the round's stagger claims.</summary>
        public Core2CombatContext Clone()
        {
            var copy = new Core2CombatContext(Stamp, _characterTags) { Phase = Phase, Turn = Turn };
            foreach (var pair in _staggers) copy._staggers[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>A context over the export: the rules, a new-fight stamp and every character's tags.</summary>
        public static Core2CombatContext NewFight(Core2Content content)
        {
            var rules = Core2Rules.From(content);
            var stamp = Core2FightStamp.NewFight(Core2ProfileSnapshot.Take(content), rules);
            return new Core2CombatContext(stamp, content.Characters.ToDictionary(c => c.Id, c => c.Tags, StringComparer.Ordinal));
        }

        /// <summary>JS characterTags(row) for the player's class (engine/kernel/gameConfig.js).</summary>
        public IReadOnlyList<string> CharacterTags(string classId) => classId != null && _characterTags.TryGetValue(classId, out var tags) ? tags : Array.Empty<string>();

        private readonly Dictionary<string, int> _staggers = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>
        /// JS actions.js claimStagger: one stagger per target per round (stances.staggerLimitPerTargetPerRound, 1). A Poise
        /// break always staggers but still uses the round's claim; an advantage interrupt staggers only while it is unused.
        /// </summary>
        public bool ClaimStagger(Core2Combatant target, int limit = 1)
        {
            var key = Turn + ":" + target.Id;
            _staggers.TryGetValue(key, out var used);
            if (used >= limit) return false;
            _staggers[key] = used + 1;
            return true;
        }
    }

    public static class Core2Combat
    {
        /// <summary>JS CORE2_ACTION_RULES_VERSION (src/model/core2ActionPolicy.js).</summary>
        public const int ActionRulesVersion = 1;
    }
}
