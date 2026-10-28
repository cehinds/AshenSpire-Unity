// Core2FightBurdens.cs — burden cards a player card puts on an enemy (base.restrained, the status-card cards): an overlay of
// generated cards beside the enemy's authored move plan. Each enemy turn draws one into its hand; held burdens skip its
// actions, tax its moves out of its SP pool, or hold a status; at its turn end they leave its hand.
//
// JS at the pin:
//   src/engine/enemyBurdens.js       addEnemyBurden (draw or hand, at most 20; enemyBurdenAdded), beginEnemyBurdens (the
//                                    discard reshuffled on `shuffle` when the draw is empty; one drawn; the turn's budget
//                                    and tax; skipActions; the held status applied; enemyBurdenDrawn), allowBurdenMove (a
//                                    taxed move must fit the budget: enemyBurdenBlocked), endEnemyBurdens (held cards to the
//                                    discard, Ethereal or Exhaust ones exhausted; the held status removed)
//   src/engine/combat.js             enemyOwnerTurnStart (beginEnemyBurdens with the enemy's SP pool), executeMovePayload
//                                    (allowBurdenMove after the SP is paid: 'blocked'), enemyPhase (endEnemyBurdens first at
//                                    the enemy's turn end)
//   src/model/actionCosts.js         enemyPoolOf (ENEMY_SP_ROW on the blight-shifted attributes, each term floored, plus
//                                    equipmentSp; a fixed spPool replaces it)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>JS enemy.burdenPiles and the turn's burden budget and tax.</summary>
    public sealed class Core2BurdenPiles
    {
        public List<Core2CardInstance> Draw = new List<Core2CardInstance>(), Hand = new List<Core2CardInstance>(),
            Discard = new List<Core2CardInstance>(), Exhaust = new List<Core2CardInstance>();
        public int? Budget, Tax;
    }

    public sealed partial class Core2Combatant
    {
        /// <summary>JS enemy.burdenPiles: null until a burden is added.</summary>
        public Core2BurdenPiles BurdenPiles { get; set; }
        /// <summary>JS enemy.proposalIntentRevealedUntilByOwner: the turn through which each seat reads this enemy's intent (revealIntent).</summary>
        public Dictionary<string, int> IntentRevealedUntil { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed partial class Core2Fight
    {
        private JObject BurdenRule(string cardId) => Content.Card(cardId).Raw["burden"] as JObject
            ?? throw new InvalidOperationException("Generated enemy card must define burden behavior");

        /// <summary>JS addEnemyBurden.</summary>
        private void AddEnemyBurden(Core2Combatant enemy, string cardId, int count, string pile)
        {
            if (pile != "draw" && pile != "hand") throw new InvalidOperationException("Enemy burden pile must be draw or hand");
            BurdenRule(cardId);
            enemy.BurdenPiles ??= new Core2BurdenPiles();
            var target = pile == "draw" ? enemy.BurdenPiles.Draw : enemy.BurdenPiles.Hand;
            for (var i = 0; i < Math.Min(20, Math.Max(0, count)); i++) target.Add(new Core2CardInstance { InstanceId = "gen" + (++_generatedCards), CardId = cardId });
            Emit("enemyBurdenAdded", new JObject { ["targetId"] = enemy.Id, ["cardId"] = cardId, ["count"] = count });
        }

        /// <summary>JS enemyPoolOf: the enemy's SP pool read off its (blight-shifted) attributes, each term floored, plus equipmentSp.</summary>
        private int EnemyPool(Core2Combatant enemy)
        {
            var def = EnemyDef(enemy).Raw;
            if (def["spPool"]?.Type == JTokenType.Integer && (int)def["spPool"] >= 0) return (int)def["spPool"];
            var costs = (JObject)Content.Mechanics["actionCosts"];
            var attributes = (def["attributes"] as JObject ?? new JObject()).Properties().ToDictionary(p => p.Name, p => (double)p.Value, StringComparer.Ordinal);
            if ((bool?)def["blighted"] == true)
                foreach (var shift in ((JObject)costs["enemyBlightShift"]).Properties())
                    attributes[shift.Name] = Math.Max(0, (attributes.TryGetValue(shift.Name, out var v) ? v : 0) + (double)shift.Value);
            var row = (JObject)costs["enemySpRow"];
            var sp = (double)row["base"];
            foreach (var term in row.Properties().Where(p => p.Name != "base"))
                sp += Math.Floor((attributes.TryGetValue(term.Name, out var v) ? v : 0) * (double)term.Value);
            var equipment = def["equipmentSp"]?.Type == JTokenType.Integer || def["equipmentSp"]?.Type == JTokenType.Float ? (double)def["equipmentSp"] : 0;
            return Math.Max(0, (int)Math.Floor(sp + equipment));
        }

        /// <summary>JS beginEnemyBurdens.</summary>
        private void BeginEnemyBurdens(Core2Combatant enemy)
        {
            var piles = enemy.BurdenPiles;
            if (piles == null) return;
            if (piles.Draw.Count == 0 && piles.Discard.Count > 0) { piles.Draw = Rng.Shuffle("shuffle", piles.Discard); piles.Discard = new List<Core2CardInstance>(); }
            var card = piles.Draw.FirstOrDefault();
            if (card != null) piles.Draw.RemoveAt(0);
            piles.Budget = Math.Max(0, EnemyPool(enemy)); piles.Tax = 0;
            if (card != null) piles.Hand.Add(card);
            foreach (var held in piles.Hand)
            {
                var rule = BurdenRule(held.CardId);
                if (((int?)rule["skipActions"] ?? 0) != 0) enemy.SkipNextTurn = true;
                piles.Tax += Math.Max(0, (int?)rule["staminaTax"] ?? 0) + Math.Max(0, (int?)rule["manaTax"] ?? 0);
                if (rule["status"] != null)
                    Enqueue(new Core2FightAction { Effect = new JObject { ["op"] = "applyStatus", ["target"] = "self", ["status"] = rule["status"], ["stacks"] = 1 }, Source = enemy, Owner = enemy, Target = enemy });
                Emit("enemyBurdenDrawn", new JObject { ["targetId"] = enemy.Id, ["cardId"] = held.CardId, ["cardInstanceId"] = held.InstanceId, ["skip"] = ((int?)rule["skipActions"] ?? 0) != 0, ["tax"] = piles.Tax });
            }
        }

        /// <summary>JS allowBurdenMove: a move the held burdens tax must fit the turn's burden budget, which it then spends.</summary>
        private bool AllowBurdenMove(Core2Combatant enemy, int baseCost, JObject move)
        {
            var piles = enemy.BurdenPiles;
            if (piles == null || !(piles.Tax > 0)) return true;
            var profile = move["combatProfile"] as JObject ?? new JObject();
            var range = (string)move["range"] ?? (string)profile["range"] ?? "melee";
            var type = (string)move["type"] ?? ((string)move["intent"] == "attack" ? "attack" : "skill");
            var tax = piles.Hand.Sum(card =>
            {
                var r = BurdenRule(card.CardId);
                var ranges = Core2Json.Strings(r["ranges"]).ToList(); var types = Core2Json.Strings(r["cardTypes"]).ToList();
                return (ranges.Count == 0 || ranges.Contains(range)) && (types.Count == 0 || types.Contains(type)) ? ((int?)r["staminaTax"] ?? 0) + ((int?)r["manaTax"] ?? 0) : 0;
            });
            if (tax == 0) return true;
            var cost = Math.Max(0, baseCost) + tax;
            if (cost > (piles.Budget ?? 0)) { Emit("enemyBurdenBlocked", new JObject { ["targetId"] = enemy.Id, ["cost"] = cost, ["remaining"] = piles.Budget ?? 0 }); return false; }
            piles.Budget -= cost;
            return true;
        }

        /// <summary>JS endEnemyBurdens.</summary>
        private void EndEnemyBurdens(Core2Combatant enemy)
        {
            var piles = enemy.BurdenPiles;
            if (piles == null) return;
            foreach (var card in piles.Hand)
            {
                var def = Content.Card(card.CardId);
                var keywords = Core2Json.Strings(def.Raw["keywords"]).ToList();
                (keywords.Contains("ethereal") || keywords.Contains("exhaust") ? piles.Exhaust : piles.Discard).Add(card);
                var rule = BurdenRule(card.CardId);
                if (rule["status"] != null)
                    Enqueue(new Core2FightAction { Effect = new JObject { ["op"] = "removeStatus", ["target"] = "self", ["status"] = rule["status"], ["amount"] = 1 }, Source = enemy, Owner = enemy, Target = enemy });
            }
            piles.Hand = new List<Core2CardInstance>(); piles.Tax = null; piles.Budget = null;
        }
    }
}
