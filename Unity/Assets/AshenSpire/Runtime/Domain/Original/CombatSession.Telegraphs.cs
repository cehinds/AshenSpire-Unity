// CombatSession.Telegraphs.cs — read-only entry point for enemy intent/Poise view-models (F04).
// Intent numbers use the same calculator as enemy attacks (src/engine/combat.js previewIntent):
// attacker Strength/Weak and the player's Vulnerable are included. Nothing here mutates state.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        /// <summary>Modifier-inclusive per-hit damage of an enemy attack of <paramref name="basis"/> against the player.</summary>
        public int PreviewEnemyAttack(string enemyInstanceId, int basis)
        {
            var enemy = _enemies.FirstOrDefault(e => (string)e["id"] == enemyInstanceId) ?? throw new ArgumentException("Unknown enemy instance: " + enemyInstanceId);
            return AttackDamageCalculator.Calculate(_statuses, enemy, _player, basis, null);
        }

        /// <summary>One telegraph per enemy in row order. <paramref name="victim"/> is "you" solo, "each hero" in co-op.</summary>
        public IReadOnlyList<EnemyTelegraph> Telegraphs(string victim = "you")
        {
            var growth = (double?)(_balance?["poise"] as JObject)?["growthMult"] ?? 1.25;
            string staggeredTooltip = null;
            try { staggeredTooltip = (string)_statuses.Definition("staggered")["tooltip"]; } catch (ArgumentException) { }
            return EnemyTelegraphViewModel.BuildAll(Enemies, Player, (enemy, basis) => PreviewEnemyAttack((string)enemy["id"], basis), victim, growth, staggeredTooltip);
        }
    }
}
