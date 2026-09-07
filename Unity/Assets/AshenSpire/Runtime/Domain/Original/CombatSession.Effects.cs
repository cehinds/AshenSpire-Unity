// CombatSession.Effects.cs — closed native combat effect interpreter and pile operations.
// Preserve FIFO ordering: effects may enqueue reactions but never recursively execute them.
// New content uses existing opcodes; unknown opcodes and scripts are rejected explicitly.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private static readonly string[] CombatOperations = { "damage", "block", "dodgeRoll", "applyStatus", "removeStatus", "draw", "discard", "exhaust", "addCard", "gainEnergy", "restoreMana", "loseHp", "heal", "shuffleDiscardIntoDraw", "enterStance", "poiseDamage", "stagger" };
        private void ValidateEffects(JToken effects)
        {
            foreach (var token in effects as JArray ?? new JArray())
            {
                if (!(token is JObject effect)) throw new ArgumentException("Effect must be an object.");
                if ((string)effect["script"] == "wondrousDraught") continue;
                if (effect["script"] != null || !CombatOperations.Contains((string)effect["op"])) throw new NotSupportedException("Native combat effect unavailable: " + (effect["script"] ?? effect["op"]));
                if (effect["if"] is JObject predicate) ValidatePredicate(predicate);
                var target = (string)effect["target"]; if (target != null && !new[] { "self", "owner", "player", "enemy", "allEnemies", "randomEnemy", "ally" }.Contains(target)) throw new ArgumentException("Unknown effect target: " + target);
            }
        }
        private void QueueEffects(JToken effects,JObject source,JObject owner,JObject target,JObject meta = null,JObject carrier = null)
        { foreach (var effect in effects as JArray ?? new JArray()) Queue((JObject)effect,source,owner,target,meta,carrier); }
        private void Queue(JObject effect,JObject source,JObject owner,JObject target,JObject meta = null,JObject carrier = null)
        {
            var copy = (JObject)effect.DeepClone(); if (copy["script"] != null) copy["op"] = "__script";
            var action = new CombatAction(copy,source,owner,target); if (meta != null) _metadata.Add(action,(JObject)meta.DeepClone()); if (carrier != null) _carriers.Add(action,(JObject)carrier.DeepClone()); _context.Enqueue(action);
        }
        private void Execute(CombatAction action)
        {
            _metadata.TryGetValue(action,out var meta); _metadata.Remove(action); _carriers.TryGetValue(action,out var carrier); _carriers.Remove(action);
            if (_result != null) return;
            var effect = action.Effect;
            if (effect["script"] != null) { WondrousDraught(action,meta); EndCheck(); return; }
            if (effect["if"] is JObject predicate && !Predicate(predicate,action.Owner ?? action.Source,action.Source,action.Target,carrier,meta,null)) return;
            var repeat = Number(effect["repeat"],1,action,null,meta); if (repeat > 10000) throw new ArgumentException("Effect repeat exceeds command budget.");
            for (var r = 0; r < repeat && _result == null; r++) ExecuteOpcode(action,meta,carrier,r == 0);
            EndCheck();
        }
        private void ExecuteOpcode(CombatAction action,JObject meta,JObject carrier,bool firstRepeat)
        {
            var effect = action.Effect; var op = (string)effect["op"];
            switch (op)
            {
                case "damage":
                    var hits = Math.Max(0,Number(effect["hits"],1,action,null,meta)); if (hits > 10000) throw new ArgumentException("Hit count exceeds command budget.");
                    var tags = effect["tags"]?.Values<string>().ToArray() ?? carrier?["tags"]?.Values<string>().ToArray() ?? Array.Empty<string>();
                    var bonus = firstRepeat && effect["attributeBonus"] != null ? CardMechanics.Nonnegative(effect["attributeBonus"],"attribute bonus") : 0;
                    for (var h = 0; h < hits; h++) foreach (var target in Targets(action,(string)effect["target"])) if (Alive(target))
                    { var basis = Number(effect["amount"],0,action,target,meta) + AttributeHitBonus(bonus,hits,h); Attack(action.Source,target,basis,tags,carrier); }
                    break;
                case "block": foreach (var target in Targets(action,(string)effect["target"])) GainBlock(target,Number(effect["amount"],0,action,target,meta)); break;
                case "dodgeRoll":
                    if ((string)action.Source?["id"] != "player") break;
                    var roll = _random.Int("misc",1,_weightSystem.DodgeDie); var weight = WeightClass(); var receipt = _weightSystem.Dodge(roll,(int?)_attributes["dexterity"] ?? 10,weight);
                    Emit("dodgeRolled",new JObject { ["sourceId"] = "player", ["roll"] = roll, ["check"] = receipt["check"], ["difficulty"] = receipt["difficulty"], ["success"] = receipt["success"], ["temporaryGuard"] = receipt["temporaryGuard"], ["weightClass"] = weight["id"] });
                    if ((bool)receipt["success"] && (int)receipt["temporaryGuard"] > 0) GainBlock(_player,(int)receipt["temporaryGuard"]); break;
                case "applyStatus": foreach (var target in Targets(action,(string)effect["target"])) _statuses.Apply(target,(string)effect["status"],Number(effect["stacks"],1,action,target,meta),action.Source); break;
                case "removeStatus": foreach (var target in Targets(action,(string)effect["target"])) _statuses.Remove(target,(string)effect["status"],"consumed"); break;
                case "draw": Draw(Math.Max(0,Number(effect["amount"],1,action,null,meta))); break;
                case "discard": Discard(Math.Max(0,Number(effect["amount"],1,action,null,meta)),(bool?)effect["random"] ?? false,false); break;
                case "exhaust": Discard(Math.Max(0,Number(effect["amount"],1,action,null,meta)),(bool?)effect["random"] ?? false,true); break;
                case "addCard":
                    _content.Record("cards",(string)effect["card"]); var count = Math.Max(1,Number(effect["count"],1,action,null,meta)); if (count > 10000) throw new ArgumentException("Generated card count exceeds command budget.");
                    var pileName = (string)effect["pile"] ?? "discard"; if (!_piles.ContainsKey(pileName)) throw new ArgumentException("Unknown pile: " + pileName);
                    for (var i = 0; i < count; i++)
                    {
                        var card = new JObject { ["instanceId"] = "gen" + ++_idCounter, ["cardId"] = effect["card"], ["upgraded"] = false };
                        if (pileName == "hand" && _piles["hand"].Count >= HandMaximum) { _piles["discard"].Add(card); CardEvent("cardDiscarded",card,"handFull"); continue; }
                        var pile = _piles[pileName]; var position = (string)effect["position"] ?? "random"; pile.Insert(position == "top" ? 0 : position == "bottom" ? pile.Count : _random.Int("shuffle",0,pile.Count),card);
                    }
                    break;
                case "gainEnergy": var energy = Math.Max(0,Number(effect["amount"],1,action,null,meta)); _player["energy"] = checked((int)_player["energy"] + energy); Emit("energyGained",new JObject { ["amount"] = energy }); break;
                case "restoreMana":
                    var mana = Math.Max(0,Number(effect["amount"],1,action,null,meta)); foreach (var target in Targets(action,(string)effect["target"])) { var before = (int?)target["mana"] ?? throw new ArgumentException("Target has no mana pool."); target["mana"] = Math.Min((int)target["maxMana"],checked(before + mana)); Emit("manaRestored",new JObject { ["targetId"] = target["id"], ["amount"] = (int)target["mana"] - before }); } break;
                case "loseHp": foreach (var target in Targets(action,(string)effect["target"])) LoseHp(target,Number(effect["amount"],0,action,target,meta),(string)effect["cause"] ?? "effect"); break;
                case "heal": foreach (var target in Targets(action,(string)effect["target"])) Heal(target,Number(effect["amount"],0,action,target,meta) + (firstRepeat && effect["attributeBonus"] != null ? CardMechanics.Nonnegative(effect["attributeBonus"],"healing attribute bonus") : 0)); break;
                case "shuffleDiscardIntoDraw": Reshuffle(); break;
                case "enterStance":
                    var id = (string)effect["stance"]; var stance = _content.Record("stances",id); if ((string)_player["stanceId"] == id) break;
                    if (_player["stanceId"].Type != JTokenType.Null) Emit("stanceExited",new JObject { ["stance"] = _player["stanceId"] }); _player["stanceId"] = id; Emit("stanceEntered",new JObject { ["stance"] = id }); QueueEffects(stance["onEnter"],_player,_player,action.Target,meta); break;
                case "poiseDamage": foreach (var target in Targets(action,(string)effect["target"])) PoiseDamage(target,Number(effect["amount"],0,action,target,meta)); break;
                case "stagger": foreach (var target in Targets(action,(string)effect["target"])) Stagger(target); break;
                default: throw new NotSupportedException("Native combat opcode unavailable: " + op);
            }
        }
        // A point bonus belongs to the whole attack, not to every individual hit.
        public static int AttributeHitBonus(int bonus,int hits,int hitIndex)
        { if (bonus < 0 || hits < 0 || hitIndex < 0 || hitIndex >= hits) throw new ArgumentException("Invalid attribute hit allocation."); return bonus / hits + (hitIndex < bonus % hits ? 1 : 0); }
        private int Number(JToken value,int fallback,CombatAction action,JObject target,JObject meta)
        {
            if (value == null) return fallback;
            var context = new JObject { ["entities"] = new JObject { ["self"] = action.Source?.DeepClone(), ["owner"] = (action.Owner ?? action.Source)?.DeepClone(), ["target"] = (target ?? action.Target)?.DeepClone(), ["enemy"] = (target ?? action.Target)?.DeepClone(), ["player"] = _player.DeepClone(), ["allEnemies"] = new JArray(_enemies.Where(Alive).Select(e => e.DeepClone())) }, ["energySpent"] = meta?["energySpent"] ?? 0, ["cardsPlayedThisTurn"] = Counter("cardsPlayedThisTurn") };
            var result = value.Type == JTokenType.Integer || value.Type == JTokenType.Float ? checked((int)Math.Floor((double)value)) : FormulaEvaluator.Evaluate(value,context);
            var mult = (double?)meta?["amountMult"] ?? 1; return mult == 1 ? result : checked((int)Math.Ceiling(result * mult));
        }
        private List<JObject> Targets(CombatAction action,string target)
        {
            JObject entity;
            switch (target)
            {
                case null: entity = action.Target ?? action.Source; break;
                case "self": entity = action.Source; break;
                case "owner": entity = action.Owner ?? action.Source; break;
                case "player": entity = _player; break;
                case "enemy": entity = (string)action.Target?["kind"] == "enemy" && Alive(action.Target) ? action.Target : (string)action.Source?["kind"] == "enemy" ? _player : _enemies.FirstOrDefault(Alive); break;
                case "allEnemies": return _enemies.Where(Alive).ToList();
                case "randomEnemy": var living = _enemies.Where(Alive).ToList(); return living.Count == 0 ? living : new List<JObject> { living[_random.Int("misc",0,living.Count-1)] };
                case "ally": entity = (string)action.Target?["kind"] == "player" && action.Target != action.Source && Alive(action.Target) ? action.Target : action.Source; break;
                default: throw new ArgumentException("Unknown effect target: " + target);
            }
            return entity == null ? new List<JObject>() : new List<JObject> { entity };
        }
        private void Attack(JObject source,JObject target,int basis,string[] tags,JObject carrier)
        {
            if (!Alive(target)) return;
            var damage = AttackDamageCalculator.Calculate(_statuses,source,target,basis,tags,(string)carrier?["damageSchool"]);
            var blocked = Math.Min((int)target["block"],damage); target["block"] = (int)target["block"] - blocked; var loss = damage - blocked; if (loss > 0) target["hp"] = (int)target["hp"] - loss;
            Emit("damageDealt",new JObject { ["sourceId"] = source?["id"], ["targetId"] = target["id"], ["amount"] = damage, ["blocked"] = blocked, ["isAttack"] = true });
            if (loss > 0) { Emit("hpLost",new JObject { ["targetId"] = target["id"], ["amount"] = loss, ["cause"] = "attack" }); ArcaneExposure(source,target,carrier); } AfterHpChange(target);
        }
        private void ArcaneExposure(JObject source,JObject target,JObject carrier)
        {
            if ((string)target["kind"] != "enemy" || !(target["arcaneExposure"] is JObject config) || carrier == null) return;
            var school = (string)carrier["damageSchool"]; var perHit = (int?)carrier["exposureBuildupPerHit"] ?? 0; var mapped = school == null ? 0 : (double?)_balance["arcaneExposure"]?["schoolBuildupMultipliers"]?[school] ?? 0; if (perHit <= 0 || mapped <= 0) return;
            var mode = (string)config["mode"]; var locked = mode == "configured" && StatusSystem.Stacks(target,(string)config["onBreak"]["status"]) > 0;
            if (mode == "immune" || locked) { Emit("arcaneExposureRefused",new JObject { ["targetId"] = target["id"], ["sourceId"] = source?["id"], ["reason"] = locked ? "locked" : "immune", ["school"] = school, ["attempted"] = perHit }); return; }
            if (mode != "configured") throw new ArgumentException("Unknown Arcane Exposure mode.");
            var amount = (int)Math.Floor(perHit * mapped * (double)config["buildupMultiplier"]); if (amount <= 0) return; config["value"] = (int)config["value"] + amount;
            Emit("arcaneExposureChanged",new JObject { ["targetId"] = target["id"], ["sourceId"] = source?["id"], ["school"] = school, ["amount"] = amount, ["value"] = config["value"], ["threshold"] = config["threshold"] });
            if ((int)config["value"] < (int)config["threshold"]) return; config["value"] = 0; var onBreak = config["onBreak"];
            Emit("arcaneBreak",new JObject { ["targetId"] = target["id"], ["sourceId"] = source?["id"], ["school"] = school, ["threshold"] = config["threshold"], ["status"] = onBreak["status"], ["value"] = onBreak["value"], ["duration"] = onBreak["duration"] });
            _statuses.Apply(target,(string)onBreak["status"],(int)onBreak["value"],source); if (target["statuses"][(string)onBreak["status"]] is JObject status) status["duration"] = onBreak["duration"];
        }
        private void LoseHp(JObject target,int amount,string cause)
        { if (!Alive(target) || amount <= 0) return; target["hp"] = (int)target["hp"] - amount; Emit("hpLost",new JObject { ["targetId"] = target["id"], ["amount"] = amount, ["cause"] = cause }); AfterHpChange(target); }
        private void Heal(JObject target,int amount)
        { if (!Alive(target)) return; var requested = Math.Max(0,amount); var gained = Math.Min(requested,(int)target["maxHp"] - (int)target["hp"]); target["hp"] = (int)target["hp"] + gained; var receipt = new JObject { ["targetId"] = target["id"], ["amount"] = gained, ["requested"] = requested }; var recipient = _coopMemberFor?.Invoke(target); if (recipient != null) receipt["playerId"] = recipient; Emit("healed",receipt); AfterHpChange(target); }
        private void AfterHpChange(JObject target)
        { if ((int)target["hp"] <= 0 && Alive(target)) { target["hp"] = 0; target["alive"] = false; if ((string)target["kind"] == "enemy") Emit("enemyDied",new JObject { ["targetId"] = target["id"], ["enemyId"] = target["enemyId"] }); } CheckPhases(); }
        private int? BlockCap(JObject entity)
        {
            int? cap = null; foreach (var property in ((JObject)entity["statuses"]).Properties()) { var value = (int?)_statuses.Definition(property.Name)["modifiers"]?["blockCap"]; if (value.HasValue) cap = cap.HasValue ? Math.Min(cap.Value,value.Value) : value; }
            if ((string)entity["kind"] == "player" && (string)entity["stanceId"] != null) { var value = (int?)_content.Record("stances",(string)entity["stanceId"])["modifiers"]?["blockCap"]; if (value.HasValue) cap = cap.HasValue ? Math.Min(cap.Value,value.Value) : value; } return cap;
        }
        private void GainBlock(JObject target,int basis)
        { if (!Alive(target)) return; var amount = Math.Max(0,(int)Math.Floor((basis + _statuses.Add(target,"blockAdd")) * _statuses.Multiply(target,"blockGainedMult"))); var cap = BlockCap(target); if (cap.HasValue) amount = Math.Min(amount,Math.Max(0,cap.Value-(int)target["block"])); target["block"] = (int)target["block"] + amount; Emit("blockGained",new JObject { ["targetId"] = target["id"], ["amount"] = amount }); }
        private void Stagger(JObject enemy)
        { if (!Alive(enemy) || (string)enemy["kind"] != "enemy") return; var cancelled = (enemy["pendingMove"] as JObject)?["moveId"]; enemy["pendingMove"] = null; enemy["skipNextTurn"] = true; enemy["intent"] = new JObject { ["kind"] = "staggered", ["moveId"] = null }; Emit("enemyStaggered",new JObject { ["targetId"] = enemy["id"], ["enemyId"] = enemy["enemyId"], ["cancelledMove"] = cancelled }); }
        private void PoiseDamage(JObject enemy,int amount)
        {
            if (!Alive(enemy) || (string)enemy["kind"] != "enemy") return; var meter = (JObject)enemy["poiseMeter"]; meter["value"] = (int)meter["value"] + Math.Max(0,amount); var config = _balance["poise"] as JObject ?? new JObject(); var guard = 0;
            while ((int)meter["value"] >= (int)meter["max"])
            {
                if (++guard > 100 || (int)meter["max"] <= 0) throw new ArgumentException("Invalid poise fill loop."); meter["value"] = (int)meter["value"] - (int)meter["max"];
                Emit("meterFilled",new JObject { ["targetId"] = enemy["id"], ["meter"] = "poise", ["threshold"] = meter["max"] }); Stagger(enemy); QueueEffects(config["onFill"],enemy,enemy,enemy);
                var growth = (double?)config["growthMult"] ?? 1.25; if (growth != 1 && !AllCombatants().Where(Alive).Any(e => _statuses.Flag(e,"meterMaxGrowthDisabled"))) meter["max"] = (int)Math.Ceiling((int)meter["max"] * growth);
            }
        }
        private int HandMaximum => (int?)_balance["handMax"] ?? 10;
        private void Draw(int amount)
        {
            if (amount > 10000) throw new ArgumentException("Draw exceeds command budget.");
            for (var i = 0; i < amount; i++) { if (_piles["draw"].Count == 0) { if (_piles["discard"].Count == 0) return; Reshuffle(); } var card = _piles["draw"][0]; _piles["draw"].RemoveAt(0); if (_piles["hand"].Count >= HandMaximum) { _piles["discard"].Add(card); CardEvent("cardDiscarded",card,"handFull"); } else { _piles["hand"].Add(card); Emit("cardDrawn",new JObject { ["cardInstanceId"] = card["instanceId"], ["cardId"] = card["cardId"] }); } }
        }
        private void Discard(int amount,bool random,bool exhaust)
        { for (var i = 0; i < amount && _piles["hand"].Count > 0; i++) { var hand = _piles["hand"]; var index = random ? _random.Int("misc",0,hand.Count-1) : hand.Count-1; var card = hand[index]; hand.RemoveAt(index); _piles[exhaust ? "exhaust" : "discard"].Add(card); CardEvent(exhaust ? "cardExhausted" : "cardDiscarded",card,"effect"); } }
        private void Reshuffle() { var cards = _piles["draw"].Concat(_piles["discard"]).ToArray(); _piles["discard"].Clear(); _piles["draw"] = _random.Shuffle("shuffle",cards); Emit("deckShuffled",new JObject { ["size"] = cards.Length }); }
        private void WondrousDraught(CombatAction action,JObject meta)
        {
            if ((string)action.Effect["script"] != "wondrousDraught") throw new NotSupportedException("Unknown native script.");
            var pool = _content.Table("flasks").OfType<JObject>().Where(f => (string)f["id"] != "wondrousDraught" && !(f["effects"] as JArray ?? new JArray()).Any(e => e["script"] != null)).ToList();
            if (pool.Count == 0) return; var first = pool[_random.Int("misc",0,pool.Count-1)]; QueueEffects(first["effects"],action.Source,action.Owner,action.Target,meta); pool.Remove(first); if (pool.Count > 0) QueueEffects(pool[_random.Int("misc",0,pool.Count-1)]["effects"],action.Source,action.Owner,action.Target,meta);
        }
    }
}
