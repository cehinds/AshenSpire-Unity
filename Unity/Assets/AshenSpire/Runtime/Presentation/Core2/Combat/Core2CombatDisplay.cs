// Core2CombatDisplay.cs — the display state the combat screen paints while a dispatch's beats play (JS combat.js `disp`,
// takeSnapshot + applyCombatDisplayReceipt). A command resolves the whole fight synchronously (an End Turn runs the enemy
// phase at once), so the screen keeps a copy of the numbers as they stood before the command and moves them event by
// event as each beat reaches its HUD stage; when the beats are done it is replaced by a fresh snapshot of the fight.
// Plain C#: every number here comes from an event the domain emitted or from the fight itself.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2DisplayFighter
    {
        public string Id { get; set; }
        public bool Alive { get; set; } = true;
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Block { get; set; }
        public int Barrier { get; set; }
        public int Ward { get; set; }
        public int Poise { get; set; }
        public int PoiseMax { get; set; }
        public Dictionary<string, int> Statuses { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public string Stance { get; set; }

        public Core2DisplayFighter Clone()
        {
            var copy = (Core2DisplayFighter)MemberwiseClone();
            copy.Statuses = new Dictionary<string, int>(Statuses, StringComparer.Ordinal);
            return copy;
        }

        public int Stacks(string id) => Statuses.TryGetValue(id, out var n) ? n : 0;
    }

    public sealed class Core2CombatDisplay
    {
        public Dictionary<string, Core2DisplayFighter> Fighters { get; private set; } = new Dictionary<string, Core2DisplayFighter>(StringComparer.Ordinal);
        public int Sp { get; set; }
        public int MaxSp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public List<Core2CardInstance> Hand { get; set; } = new List<Core2CardInstance>();
        public int Draw { get; set; }
        public int Discard { get; set; }
        public int Exhaust { get; set; }
        /// <summary>The side whose turn the ribbon names (JS enemyPlayback).</summary>
        public bool EnemySide { get; set; }

        public Core2DisplayFighter this[string id] => id != null && Fighters.TryGetValue(id, out var f) ? f : null;

        /// <summary>JS takeSnapshot: the fight's numbers as they stand.</summary>
        public static Core2CombatDisplay Snapshot(Core2Fight fight)
        {
            var display = new Core2CombatDisplay
            {
                Sp = fight.Player.Stamina, MaxSp = fight.Player.MaxStamina, Mp = fight.Player.Mana, MaxMp = fight.Player.MaxMana,
                Hand = fight.Hand.ToList(), Draw = fight.Draw.Count, Discard = fight.Discard.Count, Exhaust = fight.Exhaust.Count,
                EnemySide = fight.Phase == "enemy",
            };
            foreach (var entity in new[] { fight.Player }.Concat(fight.Enemies)) display.Fighters[entity.Id] = Fighter(entity);
            return display;
        }

        private static Core2DisplayFighter Fighter(Core2Combatant entity) => new Core2DisplayFighter
        {
            Id = entity.Id, Alive = entity.Alive && entity.Hp > 0, Hp = Math.Max(0, entity.Hp), MaxHp = entity.MaxHp, Block = entity.Block, Barrier = entity.Barrier,
            Ward = entity.Ward?.Value ?? 0, Poise = entity.Poise?.Value ?? 0, PoiseMax = entity.Poise?.Max ?? 0, Stance = entity.Stance?.Maneuver,
            Statuses = entity.Statuses.Where(p => p.Value.Stacks > 0).ToDictionary(p => p.Key, p => p.Value.Stacks, StringComparer.Ordinal),
        };

        public Core2CombatDisplay Clone()
        {
            var copy = (Core2CombatDisplay)MemberwiseClone();
            copy.Hand = Hand.ToList();
            copy.Fighters = Fighters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
            return copy;
        }

        private Core2CombatDisplay() { }

        /// <summary>
        /// JS applyCombatDisplayReceipt: move the shown numbers by one event. Events that carry a running total (Block,
        /// Poise, a status) set it; HP moves by the HP an event says was lost or healed.
        /// </summary>
        public void Apply(Core2FightEvent ev)
        {
            var d = ev.Data;
            var target = this[(string)d["targetId"]];
            switch (ev.Type)
            {
                case "damageDealt":
                    if (target != null) target.Block = Math.Max(0, target.Block - ((int?)d["blocked"] ?? 0));
                    break;
                case "hpLost":
                    if (target != null) target.Hp = Math.Max(0, target.Hp - ((int?)d["amount"] ?? 0));
                    break;
                case "healed":
                    if (target != null) target.Hp = Math.Min(target.MaxHp, target.Hp + ((int?)d["amount"] ?? 0));
                    break;
                case "blockGained":
                    if (target != null) target.Block = (int?)d["total"] ?? target.Block + ((int?)d["amount"] ?? 0);
                    break;
                case "blockBroken":
                    if (target != null) target.Block = 0;
                    break;
                case "persistentPoiseChanged":
                    if (target != null) { target.Poise = (int?)d["value"] ?? target.Poise; target.PoiseMax = (int?)d["max"] ?? target.PoiseMax; }
                    break;
                case "poiseBroken":
                    if (target != null) target.Poise = 0;
                    break;
                case "statusApplied":
                    if (target != null && d["status"] != null) target.Statuses[(string)d["status"]] = (int?)d["total"] ?? target.Stacks((string)d["status"]) + ((int?)d["stacks"] ?? 0);
                    break;
                case "statusRemoved":
                    if (target != null && d["status"] != null)
                    {
                        var left = target.Stacks((string)d["status"]) - ((int?)d["amount"] ?? 0);
                        if (left > 0) target.Statuses[(string)d["status"]] = left; else target.Statuses.Remove((string)d["status"]);
                    }
                    break;
                case "statusExpired":
                    if (target != null && d["status"] != null) target.Statuses.Remove((string)d["status"]);
                    break;
                case "combatStanceChanged":
                    if (target != null) target.Stance = (string)d["maneuver"] == "neutral" ? null : (string)d["maneuver"];
                    break;
                case "enemyDied":
                    if (target != null) { target.Alive = false; target.Hp = 0; }
                    break;
                case "staminaSpent":
                    if (((string)d["sourceKind"] ?? "player") == "player") Sp = Math.Max(0, Sp - ((int?)d["amount"] ?? 0));
                    break;
                case "manaSpent":
                    if (((string)d["sourceKind"] ?? "player") == "player") Mp = Math.Max(0, Mp - ((int?)d["amount"] ?? 0));
                    break;
                case "staminaRecovered":
                    if ((string)d["targetId"] == "player") Sp = Math.Min(MaxSp, Sp + ((int?)d["amount"] ?? 0));
                    break;
                case "manaRestored":
                    if ((string)d["targetId"] == "player") Mp = Math.Min(MaxMp, Mp + ((int?)d["amount"] ?? 0));
                    break;
                case "cardPlayed":
                {
                    var id = (string)d["cardInstanceId"];
                    var at = Hand.FindIndex(c => c.InstanceId == id);
                    if (at >= 0) { Hand.RemoveAt(at); Discard += 1; }
                    break;
                }
                case "cardExhausted":
                {
                    var id = (string)d["cardInstanceId"];
                    var at = Hand.FindIndex(c => c.InstanceId == id);
                    if (at >= 0) Hand.RemoveAt(at); else Discard = Math.Max(0, Discard - 1);
                    Exhaust += 1;
                    break;
                }
                case "cardDrawn":
                    Hand.Add(new Core2CardInstance { InstanceId = (string)d["cardInstanceId"], CardId = (string)d["cardId"] });
                    Draw = Math.Max(0, Draw - 1);
                    break;
                case "deckShuffled":
                    Draw = (int?)d["size"] ?? Draw;
                    Discard = 0;
                    break;
                case "enemyTurnStart":
                    EnemySide = true;
                    break;
                case "playerTurnStart":
                    EnemySide = false;
                    break;
            }
        }
    }
}
