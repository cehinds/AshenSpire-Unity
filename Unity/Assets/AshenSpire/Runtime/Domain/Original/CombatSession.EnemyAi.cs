// CombatSession.EnemyAi.cs — original weighted intents and delayed enemy commitments.
// Preserve authored row order and enemyAI stream consumption; intent previews never roll.
// Stagger cancels a pending move and skips one enemy action, without erasing history.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private void RollIntents(bool firstTurn = false)
        {
            foreach (var enemy in _enemies)
            {
                if (!Alive(enemy)) continue;
                if (enemy["pendingMove"] is JObject) { if (enemy["intent"] is JObject intent) intent["pending"] = true; continue; }
                if ((bool)enemy["skipNextTurn"] || _statuses.Flag(enemy,"skipTurn")) { enemy["intent"] = new JObject { ["kind"] = "staggered", ["moveId"] = null }; continue; }
                var definition = _content.Record("enemies",(string)enemy["enemyId"]); var moveId = firstTurn && definition["firstMove"] != null ? (string)definition["firstMove"] : PickMove(enemy,definition);
                if (moveId == null) { enemy["intent"] = new JObject { ["kind"] = "unknown", ["moveId"] = null }; continue; }
                ((JArray)enemy["movesHistory"]).Add(moveId); var move = definition["moves"][moveId];
                enemy["intent"] = new JObject { ["kind"] = move["intent"], ["moveId"] = moveId, ["damage"] = move["damage"], ["hits"] = move["damage"] != null ? move["hits"] ?? new JValue(1) : null, ["block"] = move["block"], ["delayed"] = move["delay"] != null, ["pending"] = false };
            }
        }
        private string PickMove(JObject enemy,JObject definition)
        {
            var unlocked = ((JArray)enemy["unlockedMoves"]).Values<string>().ToArray(); var entries = ((JObject)definition["moves"]).Properties().Where(p => (bool?)p.Value["locked"] != true || unlocked.Contains(p.Name)).ToList();
            if (entries.Count == 0) return null; var history = ((JArray)enemy["movesHistory"]).Values<string>().ToArray();
            var eligible = entries.Where(p => { if (p.Value["maxConsecutive"] == null) return true; var run = 0; for (var i = history.Length-1; i >= 0 && history[i] == p.Name; i--) run++; return run < (int)p.Value["maxConsecutive"]; }).ToList();
            var pool = eligible.Count > 0 ? eligible : entries; var total = pool.Sum(p => (double)p.Value["weight"]); if (total <= 0) return pool[0].Name;
            var roll = _random.Float("enemyAI") * total; foreach (var entry in pool) { roll -= (double)entry.Value["weight"]; if (roll < 0) return entry.Name; } return pool[pool.Count-1].Name;
        }
        private void EnemyTurn()
        {
            _phase = "enemy"; Emit("enemyTurnStart",new JObject { ["turn"] = _turn });
            foreach (var enemy in _enemies) if (Alive(enemy) && !_statuses.Flag(enemy,"retainBlock")) enemy["block"] = 0; Drain(); if (_result != null) return;
            foreach (var enemy in _enemies)
            {
                if (_result != null) return; if (!Alive(enemy)) continue;
                OwnerHooks(enemy,"ownerTurnStart"); Drain(); if (_result != null) return; if (!Alive(enemy)) continue;
                if ((bool)enemy["skipNextTurn"] || _statuses.Flag(enemy,"skipTurn")) enemy["skipNextTurn"] = false;
                else if (enemy["pendingMove"] is JObject pending)
                { if (_turn >= (int)pending["resolveOnTurn"]) { var moveId = (string)pending["moveId"]; enemy["pendingMove"] = null; ExecuteMove(enemy,moveId); } }
                else if (enemy["intent"] is JObject intent && (string)intent["moveId"] != null)
                {
                    var moveId = (string)intent["moveId"]; var move = _content.Record("enemies",(string)enemy["enemyId"])["moves"][moveId];
                    if (move["delay"] is JObject delay)
                    {
                        var charging = delay["whileCharging"] as JObject ?? new JObject();
                        if (charging["block"] != null) Queue(new JObject { ["op"] = "block", ["target"] = "self", ["amount"] = charging["block"] },enemy,enemy,_player);
                        QueueEffects(charging["effects"],enemy,enemy,_player); enemy["pendingMove"] = new JObject { ["moveId"] = moveId, ["resolveOnTurn"] = _turn + ((int?)delay["turns"] ?? 1) }; intent["pending"] = true;
                    }
                    else ExecuteMove(enemy,moveId);
                }
                Drain(); if (_result != null) return;
                if (Alive(enemy)) { OwnerHooks(enemy,"ownerTurnEnd"); Drain(); if (_result != null) return; if (Alive(enemy)) _statuses.DecayAtTurnEnd(enemy); }
            }
            Emit("enemyTurnEnd",new JObject { ["turn"] = _turn }); Drain();
        }
        private void ExecuteMove(JObject enemy,string moveId)
        {
            var move = _content.Record("enemies",(string)enemy["enemyId"])["moves"][moveId]; var meta = new JObject { ["moveId"] = moveId };
            Emit("enemyMoveStarted",new JObject { ["sourceId"] = enemy["id"], ["enemyId"] = enemy["enemyId"], ["moveId"] = moveId, ["kind"] = move["intent"] });
            if (move["damage"] != null) Queue(new JObject { ["op"] = "damage", ["target"] = "player", ["amount"] = move["damage"], ["hits"] = move["hits"] ?? 1 },enemy,enemy,_player,meta);
            if (move["block"] != null) Queue(new JObject { ["op"] = "block", ["target"] = "self", ["amount"] = move["block"] },enemy,enemy,_player,meta);
            QueueEffects(move["effects"],enemy,enemy,_player,meta);
        }
    }
}
