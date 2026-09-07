// CombatSession.Triggers.cs — ordered declarative hooks, gate receipts and predicates.
// Reactions are queued after the emitting action. Once/per-turn gates are saved state.
// Owner-relative hooks are dispatched only for that combatant's own turn boundary.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private static readonly string[] PredicateNames = { "inStance", "hasStatus", "hasBlock", "hpBelowPct", "firstCardThisTurn", "firstAttackThisCombat", "cardTypeIs", "eventIsAttack", "eventSourceIsOwner", "eventTargetIsOwner", "eventStatusIs", "everyNthCardThisCombat", "random", "all", "any", "not" };
        private static void ValidatePredicate(JObject predicate)
        {
            if (!PredicateNames.Contains((string)predicate["p"])) throw new NotSupportedException("Unknown native predicate: " + predicate["p"]);
            foreach (var child in predicate["preds"] as JArray ?? new JArray()) ValidatePredicate((JObject)child);
            if (predicate["pred"] is JObject nested) ValidatePredicate(nested);
        }
        private bool Predicate(JObject predicate,JObject owner,JObject source,JObject target,JObject card,JObject meta,JObject ev)
        {
            switch ((string)predicate["p"])
            {
                case "inStance": return (string)_player["stanceId"] == (string)predicate["stance"];
                case "hasStatus": return StatusSystem.Stacks(PredicateEntity((string)predicate["of"],owner,source,target),(string)predicate["status"]) >= ((int?)predicate["atLeast"] ?? 1);
                case "hasBlock": return (int?)PredicateEntity((string)predicate["of"],owner,source,target)?["block"] > 0;
                case "hpBelowPct": var entity = PredicateEntity((string)predicate["of"],owner,source,target); return entity != null && (int)entity["hp"] <= (double)entity["maxHp"] * (double)predicate["pct"] / 100;
                case "firstCardThisTurn": return meta?["ordinalThisTurn"] != null ? (int)meta["ordinalThisTurn"] == 1 : Counter("cardsPlayedThisTurn") == 0;
                case "firstAttackThisCombat": return meta?["attackOrdinal"] != null && meta["attackOrdinal"].Type != JTokenType.Null ? (int)meta["attackOrdinal"] == 1 : Counter("attacksPlayedThisCombat") == 0;
                case "cardTypeIs": return (string)(card?["type"] ?? ev?["cardType"]) == (string)predicate["type"];
                case "eventIsAttack": return (bool?)ev?["isAttack"] == true;
                case "eventSourceIsOwner": return ev != null && owner != null && (string)ev["sourceId"] == (string)owner["id"];
                case "eventTargetIsOwner": return ev != null && owner != null && (string)ev["targetId"] == (string)owner["id"];
                case "eventStatusIs": return ev != null && (string)ev["status"] == (string)predicate["status"];
                case "everyNthCardThisCombat": var ordinal = (int?)meta?["ordinalThisCombat"] ?? Counter("cardsPlayedThisCombat"); var n = CardMechanics.Nonnegative(predicate["n"],"card interval"); if (n == 0) throw new ArgumentException("Card interval must be positive."); return ordinal > 0 && ordinal % n == 0;
                case "random": return _random.Float("misc") * 100 < (double)predicate["pct"];
                case "all": return ((JArray)predicate["preds"]).All(p => Predicate((JObject)p,owner,source,target,card,meta,ev));
                case "any": return ((JArray)predicate["preds"]).Any(p => Predicate((JObject)p,owner,source,target,card,meta,ev));
                case "not": return !Predicate((JObject)predicate["pred"],owner,source,target,card,meta,ev);
                default: throw new NotSupportedException("Unknown native predicate: " + predicate["p"]);
            }
        }
        private JObject PredicateEntity(string name,JObject owner,JObject source,JObject target)
        { switch(name) { case "player": return _player; case "self": return source ?? owner ?? _player; case "owner": return owner ?? source; case "enemy": case "target": return target; default: throw new ArgumentException("Unknown predicate entity: " + name); } }
        private IEnumerable<JObject> AllCombatants() { yield return _player; foreach (var enemy in _enemies) yield return enemy; }
        private void OnEvent(JObject ev)
        {
            _events.Add((JObject)ev.DeepClone()); if (++_emitDepth > 64) { _emitDepth--; throw new InvalidOperationException("Trigger recursion exceeded 64."); }
            try
            {
                var eventName = (string)ev["type"];
                foreach (var idToken in (JArray)_player["relicIds"])
                {
                    var id = (string)idToken; var hooks = _content.Record("relics",id)["triggers"] as JArray ?? new JArray();
                    for (var i = 0; i < hooks.Count; i++) if ((string)hooks[i]["on"] == eventName && Fire("relic:player:"+id+":"+i,(JObject)hooks[i],_player,ev) && eventName != "relicTriggered") Emit("relicTriggered",new JObject { ["relicId"] = id });
                }
                var stanceId = (string)_player["stanceId"];
                if (stanceId != null) { var hooks = _content.Record("stances",stanceId)["hooks"] as JArray ?? new JArray(); for (var i = 0; i < hooks.Count; i++) if ((string)hooks[i]["on"] == eventName) Fire("stance:player:"+stanceId+":"+i,(JObject)hooks[i],_player,ev); }
                foreach (var entity in AllCombatants()) if (Alive(entity)) foreach (var status in ((JObject)entity["statuses"]).Properties().ToArray())
                { var hooks = _statuses.Definition(status.Name)["hooks"] as JArray ?? new JArray(); for (var i = 0; i < hooks.Count; i++) if ((string)hooks[i]["on"] == eventName) Fire("status:"+entity["id"]+":"+status.Name+":"+i,(JObject)hooks[i],entity,ev); }
                foreach (var enemy in _enemies) if (Alive(enemy))
                { var phases = _content.Record("enemies",(string)enemy["enemyId"])["phases"] as JArray ?? new JArray(); for (var i = 0; i < phases.Count; i++) if ((string)phases[i]["on"] == eventName) FirePhase(enemy,(JObject)phases[i],i,ev); }
            }
            finally { _emitDepth--; }
        }
        private bool Fire(string key,JObject trigger,JObject owner,JObject ev)
        {
            var state = Gate(key); if ((bool?)trigger["once"] == true && (int)state["fires"] > 0) return false;
            if ((int)state["turn"] != _turn) { state["turn"] = _turn; state["turnFires"] = 0; }
            if (trigger["limitPerTurn"] != null && (int)state["turnFires"] >= (int)trigger["limitPerTurn"]) return false;
            var target = Find((string)(ev["targetId"] ?? ev["enemyId"]));
            if (trigger["if"] is JObject predicate && !Predicate(predicate,owner,null,target,null,null,ev)) return false;
            state["fires"] = (int)state["fires"] + 1; state["turnFires"] = (int)state["turnFires"] + 1;
            QueueEffects(trigger["do"],owner,owner,target,new JObject { ["event"] = ev.DeepClone() }); return true;
        }
        private JObject Gate(string key)
        { if (_coopSeatKey != null) key = key.Replace(":player:", ":" + _coopSeatKey + ":"); if (!_triggerState.TryGetValue(key,out var state)) { state = new JObject { ["fires"] = 0, ["turn"] = -1, ["turnFires"] = 0 }; _triggerState.Add(key,state); } return state; }
        private void OwnerHooks(JObject entity,string hookName)
        {
            if (!Alive(entity)) return; var ev = new JObject { ["type"] = hookName, ["ownerId"] = entity["id"] };
            foreach (var status in ((JObject)entity["statuses"]).Properties().ToArray()) { var hooks = _statuses.Definition(status.Name)["hooks"] as JArray ?? new JArray(); for (var i = 0; i < hooks.Count; i++) if ((string)hooks[i]["on"] == hookName) Fire("status:"+entity["id"]+":"+status.Name+":"+i+":"+hookName,(JObject)hooks[i],entity,ev); }
            var stanceId = (string)entity["stanceId"]; if ((string)entity["kind"] == "player" && stanceId != null) { var hooks = _content.Record("stances",stanceId)["hooks"] as JArray ?? new JArray(); for (var i = 0; i < hooks.Count; i++) if ((string)hooks[i]["on"] == hookName) Fire("stance:player:"+stanceId+":"+i+":"+hookName,(JObject)hooks[i],entity,ev); }
        }
        private void CheckPhases()
        {
            foreach (var enemy in _enemies) if (Alive(enemy)) { var phases = _content.Record("enemies",(string)enemy["enemyId"])["phases"] as JArray ?? new JArray(); for (var i = 0; i < phases.Count; i++) { var phase = (JObject)phases[i]; if ((string)phase["on"] == "hpBelowPct" && (int)enemy["hp"] <= (double)enemy["maxHp"] * (double)phase["pct"] / 100) FirePhase(enemy,phase,i,new JObject { ["type"] = "hpBelowPct", ["targetId"] = enemy["id"] }); } }
        }
        private void FirePhase(JObject enemy,JObject phase,int index,JObject ev)
        {
            var state = Gate("phase:"+enemy["id"]+":"+index); if ((bool?)phase["once"] != false && (int)state["fires"] > 0) return;
            if (phase["if"] is JObject predicate && !Predicate(predicate,enemy,null,_player,null,null,ev)) return;
            state["fires"] = (int)state["fires"] + 1; QueueEffects(phase["do"],enemy,enemy,_player,new JObject { ["event"] = ev.DeepClone() });
            var unlocked = (JArray)enemy["unlockedMoves"]; foreach (var move in phase["unlockMoves"] as JArray ?? new JArray()) if (!unlocked.Values<string>().Contains((string)move)) unlocked.Add(move.DeepClone());
        }
        private JObject RelicDefinition(string id) => new ItemUpgradeService(_content).ResolveItem("relic/" + id,(int?)_player["itemUpgradeLevels"]?["relic/" + id] ?? 0);
        private int PassiveSum(string key) => ((JArray)_player["relicIds"]).Sum(id => (int?)RelicDefinition((string)id)["passives"]?[key] ?? 0);
    }
}
