// Core2CombatText.cs — the words the Core 2 combat screen prints, as the JS build prints them (content/source/uiStrings.csv
// at the pin, keyed by the same ids), and the small formatters that place numbers in them. Plain C#: the combat view
// models and the console suite read it; nothing here touches UnityEngine.
//
// JS at the pin:
//   content/source/uiStrings.csv     combat.*, common.*, hud.cinders, potions.run.title (the copy below)
//   src/ui/screens/combat.js         the turn ribbon ("Player Turn" / "Enemy Turn"), cardHotkeyAction
//   src/ui/components/combatActionRow.js  the footer labels (DRAW, Discard N, Exhaust N, Potions, End Turn · E · HOLD)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AshenSpire.Presentation.Core2
{
    public static class Core2CombatText
    {
        private static readonly Dictionary<string, string> Copy = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.endTurn"] = "End Turn",
            ["combat.discard"] = "Discard",
            ["combat.exhaust"] = "Exhaust",
            ["js.orb.sp"] = "SP",
            ["js.orb.mp"] = "MP",
            ["potions.run.title"] = "Potions",
            ["hud.cinders"] = "Cinders",
            ["combat.log.title"] = "Combat log",
            ["combat.log.full"] = "Full log",
            ["combat.grid.label"] = "Grid",
            ["combat.reactions.label"] = "Reaction",
            ["combat.reactions.enabled"] = "enabled",
            ["combat.reactions.disabled"] = "disabled",
            ["combat.reactions.state"] = "Reaction {state}",
            ["combat.auto.label"] = "Auto",
            ["combat.auto.state"] = "Auto combat {state}",
            ["combat.reaction.title"] = "Respond to the incoming action",
            ["combat.reaction.eyebrow"] = "Defensive reaction",
            ["combat.reaction.help"] = "Choose a card to play before this action resolves. Back skips this reaction.",
            ["combat.reaction.helpStack"] = "Choose another card to add on top, or Resolve to play the stack, last card first.",
            ["combat.reaction.play"] = "Play",
            ["combat.reaction.sp"] = "{amount} SP",
            ["combat.reaction.mp"] = "{amount} MP",
            ["combat.reaction.stack"] = "On the stack",
            ["combat.reaction.resolve"] = "Resolve",
            ["common.back"] = "Back",
            ["common.close"] = "Close",
            ["combat.callout.hit"] = "Hit",
            ["combat.callout.evadeFailed"] = "Hit — Evade Failed",
            ["combat.callout.missed"] = "Missed",
            ["combat.callout.outOfRange"] = "Out of range",
            ["combat.callout.dodged"] = "Dodged",
            ["combat.callout.staggered"] = "STAGGERED",
            ["combat.callout.countered"] = "COUNTERED",
            ["combat.callout.poiseBreak"] = "POSTURE BREAK",
            ["combat.callout.blockBreak"] = "BREAK",
            ["combat.callout.interrupted"] = "INTERRUPTED",
            ["combat.callout.resisted"] = "RESISTED",
            ["combat.callout.blocked"] = "BLOCKED",
            ["combat.receipt.poiseAbsorbed"] = "Poise absorbed {n}",
            ["combat.receipt.poiseDamage"] = "Poise damage {n}",
            ["combat.intent.partial"] = "{stance} ?",
            ["combat.intent.damageHp"] = "HP",
            ["combat.intent.damageBeforeBlock"] = "before Block",
            ["combat.intent.damageTotal"] = "Total {amount} {basis}",
            ["combat.intent.damageContact"] = "Hit {index}/{count} · next {amount} {basis}",
            ["combat.protection.block"] = "Block",
            ["combat.protection.ward"] = "Arcane Ward",
            ["combat.protection.poise"] = "Poise",
            ["combat.protection.poiseValue"] = "Poise {value}/{max}",
            ["card.tags.evade"] = "Evade",
            ["card.tags.precision"] = "Precision",
            ["combat.targetHud.unknown"] = "Exact move, damage, and effects unread.",
            ["combat.targetHud.damageScope"] = "Direct HP damage at current defenses and committed stance. Excludes delayed effects and retaliation.",
            ["combat.enemyTarget.choose"] = "Choose an enemy target",
            ["combat.log.role.enemy"] = "Enemy",
            ["inspector.section.summary"] = "Summary",
            ["inspector.section.state"] = "Current state",
            ["inspector.section.history"] = "Previous actions",
            ["inspector.section.traits"] = "Known traits",
            ["inspector.summary.hp"] = "HP",
            ["inspector.summary.intent"] = "Intent",
            ["inspector.summary.defense"] = "Defense",
            ["inspector.summary.block"] = "{amount} Block",
            ["inspector.empty.none"] = "None.",
            ["inspector.empty.unknown"] = "Unknown.",
            ["gameProfile.combat.unreadIntent"] = "Unread",
            // Literal copy in the JS components (no uiStrings id); the key names the JS file that prints it.
            ["js.combatantOverhead.inspect"] = "Inspect {name}",
            ["js.combat.moveSet"] = "Move set",
            ["js.inspector.enemyDeck"] = "Enemy deck & played cards",
            ["js.inspector.enemyDeckNote"] = "Action SP includes combat modifiers. Card base shows authored SP/MP; effects use combat move rules.",
            ["js.inspector.currentAction"] = "Current action: {name}",
            ["js.inspector.lastPlayed"] = "Last played: {name}",
            ["js.inspector.none"] = "None",
            ["js.inspector.noneYet"] = "None yet",
            ["js.inspector.authoredDeck"] = "Authored deck ({count})",
            ["js.inspector.hiddenIntent"] = "{stance} · Move hidden",
            ["js.inspector.hiddenDetail"] = "Exact move, damage, and effects unread. Wisdom and Intelligence improve intent reads.",
            ["js.combat.turnPlayer"] = "Player Turn",
            ["js.combat.turnEnemy"] = "Enemy Turn",
            ["js.actionRow.endTurnKey"] = "E",
            ["js.actionRow.hold"] = "HOLD",
            ["js.actionRow.draw"] = "Draw",
            ["js.holdConfirm.armed"] = "Tap again to confirm",
        };

        /// <summary>The JS copy for <paramref name="id"/> (throws on an id this screen never prints, so a typo fails loudly).</summary>
        public static string T(string id) => Copy.TryGetValue(id, out var text) ? text : throw new KeyNotFoundException("No combat copy " + id);

        /// <summary>JS t(id, params): {name} placeholders filled in order of the pairs.</summary>
        public static string T(string id, params (string Key, object Value)[] values) => Fill(T(id), values);

        public static string Fill(string template, params (string Key, object Value)[] values)
        {
            var text = template;
            foreach (var (key, value) in values) text = text.Replace("{" + key + "}", Convert.ToString(value, CultureInfo.InvariantCulture));
            return text;
        }

        public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>JS capitalize.</summary>
        public static string Capitalize(string value) => string.IsNullOrEmpty(value) ? value ?? "" : char.ToUpperInvariant(value[0]) + value.Substring(1);

        /// <summary>JS words(): a camelCase id as a spaced, capitalised label.</summary>
        public static string Words(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var text = System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
            return Capitalize(text);
        }

        /// <summary>JS card face cost: the printed number (X for a variable cost).</summary>
        public static string Cost(int value, bool variable = false) => variable ? "X" : Number(value);

        public static string TurnRibbon(bool enemySide) => T(enemySide ? "js.combat.turnEnemy" : "js.combat.turnPlayer");
    }

    /// <summary>JS cardHotkeyAction: what a key does to the hand (1–9 then Q for the 10th; targeting picks the Nth enemy).</summary>
    public sealed class Core2CardHotkey
    {
        /// <summary>select, target or none.</summary>
        public string Kind { get; set; }
        public int Index { get; set; }

        /// <summary>Null for a key that is not a card key (leave the event alone).</summary>
        public static Core2CardHotkey For(string key, bool targeting, int handSize, int livingEnemies)
        {
            var index = key != null && key.Length == 1 && key[0] >= '1' && key[0] <= '9' ? key[0] - '1' : key == "q" || key == "Q" ? 9 : -1;
            if (index < 0) return null;
            if (targeting && index < 9) return index < livingEnemies ? new Core2CardHotkey { Kind = "target", Index = index } : new Core2CardHotkey { Kind = "none" };
            return index < handSize ? new Core2CardHotkey { Kind = "select", Index = index } : new Core2CardHotkey { Kind = "none" };
        }
    }
}
