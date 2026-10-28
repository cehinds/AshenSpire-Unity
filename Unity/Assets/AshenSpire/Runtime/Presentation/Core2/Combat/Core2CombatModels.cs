// Core2CombatModels.cs — what the Core 2 combat screen draws, as plain data: one Core2CombatScreenModel per paint, built
// by Core2CombatViewModel from the fight (or from the display state while a beat plays) and the screen's own choices
// (selection, drawer, modal). The UI Toolkit view only turns these into elements; the console suite asserts them.
using System.Collections.Generic;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2CombatScreenModel
    {
        /// <summary>"Player Turn" or "Enemy Turn" (JS .turn-ribbon).</summary>
        public string TurnRibbon { get; set; }
        public bool PlayerTurn { get; set; }
        public Core2CombatTopBand Top { get; set; } = new Core2CombatTopBand();
        public Core2CombatantModel Player { get; set; }
        public List<Core2CombatantModel> Enemies { get; set; } = new List<Core2CombatantModel>();
        public List<Core2HandCardModel> Hand { get; set; } = new List<Core2HandCardModel>();
        public Core2CombatFooterModel Footer { get; set; } = new Core2CombatFooterModel();
        /// <summary>The scene painting (an Art/js1815/combat/scenes resource) for the run's region.</summary>
        public string Backdrop { get; set; }
        /// <summary>The armed card while it waits for an enemy (JS selected): its instance id, else null.</summary>
        public string Targeting { get; set; }
        /// <summary>No input but answering the offer (or the beats playing) is accepted.</summary>
        public bool InputLocked { get; set; }
        public Core2ReactionModalModel Reaction { get; set; }
        public string Result { get; set; }
    }

    /// <summary>JS run HUD band in combat: HP fraction and rule, relics, Cinders, the Armoury and menu doors.</summary>
    public sealed class Core2CombatTopBand
    {
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public string HpText { get; set; }
        public int Cinders { get; set; }
        public List<Core2RelicChip> Relics { get; set; } = new List<Core2RelicChip>();
    }

    public sealed class Core2RelicChip
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        /// <summary>Art/js1815/combat/relics/{id} when that painting exists, else null (the view draws the relic glyph).</summary>
        public string Art { get; set; }
    }

    /// <summary>One fighter: its HP bar, its defence badges (JS CombatHealthModel order), status chips and (enemy) intent.</summary>
    public sealed class Core2CombatantModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsPlayer { get; set; }
        public bool Alive { get; set; } = true;
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        /// <summary>"19/19" (JS .m-value).</summary>
        public string HpText { get; set; }
        /// <summary>HP fill fraction 0–1.</summary>
        public double HpFraction { get; set; }
        /// <summary>barrier, block, ward or none: the HP fill's guard tone (JS healthGuardTone).</summary>
        public string Guard { get; set; } = "none";
        /// <summary>Poise still standing trims the HP bar in gold (JS data-poised).</summary>
        public bool Poised { get; set; }
        public string PoiseLabel { get; set; }
        public List<Core2BadgeModel> Badges { get; set; } = new List<Core2BadgeModel>();
        public List<Core2StatusChip> Statuses { get; set; } = new List<Core2StatusChip>();
        public Core2IntentBadge Intent { get; set; }
        /// <summary>The figure: Art/js1815/combat/enemies/{id}-idle or Art/js1815/combat/player/{class}-ready.</summary>
        public string Figure { get; set; }
        /// <summary>The armed card's hit chance against this enemy ("95%"), else null (JS hit badge).</summary>
        public string HitBadge { get; set; }
        /// <summary>advantage (green), disadvantage (red) or neutral (white) — the hit badge tone.</summary>
        public string HitTone { get; set; }
        /// <summary>A legal target for the armed card.</summary>
        public bool Targetable { get; set; }
        /// <summary>The held stance (attack, defend, counter…) or null.</summary>
        public string Stance { get; set; }
        /// <summary>The enemy's range lane (melee, reach, ranged) from the player (JS engagement band).</summary>
        public string Lane { get; set; }
    }

    public sealed class Core2BadgeModel
    {
        /// <summary>shield (Block), evade, precision, poise or ward (JS combat-health-{kind}).</summary>
        public string Kind { get; set; }
        public string Label { get; set; }
        public int Value { get; set; }
    }

    public sealed class Core2StatusChip
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Stacks { get; set; }
        /// <summary>Art/js1815/combat/icons/status-{icon}, or the unknown status mark.</summary>
        public string Icon { get; set; }
        public string Tooltip { get; set; }
    }

    public sealed class Core2HandCardModel
    {
        public string InstanceId { get; set; }
        public string CardId { get; set; }
        public string Name { get; set; }
        /// <summary>The slot key: "1"–"9", then "Q" (JS cardHotkeyAction).</summary>
        public string Key { get; set; }
        public int Sp { get; set; }
        public int Mp { get; set; }
        public bool ShowMp { get; set; }
        public bool Playable { get; set; }
        /// <summary>Why the engine would refuse it now (cost, range, unplayable, controlled), null when playable.</summary>
        public string Refusal { get; set; }
        public bool Selected { get; set; }
        /// <summary>The card's maneuver (attack, defend, smash, counter, sweep, evade) or null; picks the stance glyph.</summary>
        public string Maneuver { get; set; }
        /// <summary>The type band word (ATTACK, DEFENSE, SMASH, COUNTER, SWEEP, SKILL…).</summary>
        public string TypeLabel { get; set; }
        /// <summary>The type band sigil resource (Art/js1815/cards/mark-*).</summary>
        public string Sigil { get; set; }
        public List<string> FaceLines { get; set; } = new List<string>();
        public string Art { get; set; }
        public string Range { get; set; }
        public bool TargetsEnemy { get; set; }
        /// <summary>A reaction card (trait:reaction), answerable in the reaction window.</summary>
        public bool Reaction { get; set; }
    }

    public sealed class Core2CombatFooterModel
    {
        public int Sp { get; set; }
        public int MaxSp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int Draw { get; set; }
        public int Discard { get; set; }
        public int Exhaust { get; set; }
        public string DrawLabel { get; set; }
        public string DiscardLabel { get; set; }
        public string ExhaustLabel { get; set; }
        public string EndTurnLabel { get; set; }
        public string EndTurnKey { get; set; }
        /// <summary>HOLD while a card is still playable (the hold commits), else empty (a tap ends the turn).</summary>
        public string EndTurnHint { get; set; }
        public bool EndTurnNeedsHold { get; set; }
        public bool EndTurnEnabled { get; set; }
        public bool ReactionsEnabled { get; set; }
        public string ReactionState { get; set; }
        public bool Auto { get; set; }
        public string AutoState { get; set; }
        public bool ReactionSwitchEnabled { get; set; }
        public List<Core2FlaskModel> Flasks { get; set; } = new List<Core2FlaskModel>();
    }

    public sealed class Core2FlaskModel
    {
        /// <summary>hp or mana for a charge flask (UseFlask chargeKind), else null with Slot set.</summary>
        public string ChargeKind { get; set; }
        public int Slot { get; set; } = -1;
        public string Name { get; set; }
        public string Text { get; set; }
        public int Charges { get; set; }
        public bool Usable { get; set; }
        public bool NeedsTarget { get; set; }
        public string Art { get; set; }
    }

    /// <summary>JS combatIntentPresentation + intentDamageRead for one enemy, as the overhead draws it.</summary>
    public sealed class Core2IntentBadge
    {
        /// <summary>known, partial or unknown.</summary>
        public string Visibility { get; set; }
        /// <summary>Attacking, Defending… or "?" / "Preparing ?".</summary>
        public string Title { get; set; }
        public string Stance { get; set; }
        public List<Core2IntentPart> Parts { get; set; } = new List<Core2IntentPart>();
        /// <summary>physical, magic or null.</summary>
        public string DamageClass { get; set; }
        /// <summary>red or green when a projected HP range is shown (JS data-damage-tone).</summary>
        public string Tone { get; set; }
        /// <summary>The remaining follow-up moves this turn: an icon each, "unknown" where unread.</summary>
        public List<string> FollowUps { get; set; } = new List<string>();
    }

    public sealed class Core2IntentPart
    {
        /// <summary>attack, smash, defend, counter, evade, cast, buff, prepare (Art/js1815/combat/icons/intent-*).</summary>
        public string Icon { get; set; }
        public string Value { get; set; }
        /// <summary>"Total 0–12 HP" (JS .intent-damage-scope).</summary>
        public string Summary { get; set; }
    }

    /// <summary>The reaction window (JS openReactionChoiceModal).</summary>
    public sealed class Core2ReactionModalModel
    {
        public string OfferId { get; set; }
        public string Eyebrow { get; set; }
        public string Title { get; set; }
        /// <summary>"0 · Hit 1/1 · next 0 HP" — the incoming contact's read, or null when it is unread.</summary>
        public string Incoming { get; set; }
        public string Help { get; set; }
        /// <summary>Who threatens: the attacking enemy's name, and its move when read.</summary>
        public string Threat { get; set; }
        public string SourceId { get; set; }
        public string StackLabel { get; set; }
        /// <summary>The committed stack, top first (a stacked enemy reaction reads "Gilded Knight: Gilded Guard").</summary>
        public List<string> Stack { get; set; } = new List<string>();
        public List<Core2ReactionOptionModel> Options { get; set; } = new List<Core2ReactionOptionModel>();
        public string Selected { get; set; }
        /// <summary>Back, or Resolve while the stack holds entries (it still answers null).</summary>
        public string DeclineLabel { get; set; }
        public string PlayLabel { get; set; }
        public bool PlayEnabled { get; set; }
    }

    public sealed class Core2ReactionOptionModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        /// <summary>The cost pills ("1 SP", "1 MP").</summary>
        public List<string> Costs { get; set; } = new List<string>();
        public string CardId { get; set; }
        public string InstanceId { get; set; }
    }

    /// <summary>One section of the enemy inspector (JS combatantDetailBody).</summary>
    public sealed class Core2InspectorSection
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public List<(string Label, string Value)> Rows { get; set; } = new List<(string, string)>();
        /// <summary>known, none or unknown (JS disclose).</summary>
        public string Knowledge { get; set; } = "known";
    }

    public sealed class Core2InspectorModel
    {
        public string EnemyId { get; set; }
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public string Figure { get; set; }
        public List<(string Label, string Value)> Meters { get; set; } = new List<(string, string)>();
        public List<Core2InspectorSection> Sections { get; set; } = new List<Core2InspectorSection>();
    }
}
