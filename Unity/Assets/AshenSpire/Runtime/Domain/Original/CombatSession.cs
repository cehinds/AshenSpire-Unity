// CombatSession.cs — native deterministic combat command owner; no Unity lifecycle or UI.
// Supply final card definitions through resolveCard, and stamped derived pools in player.
// UI submits commands and animates returned events; it never changes combat state.
// Save Snapshot() only after commands return: effects and triggers drain synchronously.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private readonly OriginalContentCatalog _content;
        private readonly JObject _mechanics, _balance, _player, _attributes, _weights;
        private readonly RandomStreams _random;
        private readonly Func<JObject, JObject> _resolveCard;
        private readonly List<JObject> _enemies = new List<JObject>();
        private readonly Dictionary<string, List<JObject>> _piles = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
        private readonly List<JObject> _events = new List<JObject>();
        private readonly Dictionary<CombatAction, JObject> _metadata = new Dictionary<CombatAction, JObject>();
        private readonly Dictionary<CombatAction, JObject> _carriers = new Dictionary<CombatAction, JObject>();
        private readonly Dictionary<string, JObject> _triggerState = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly CombatContext _context;
        private readonly StatusSystem _statuses;
        private readonly WeightSystem _weightSystem;
        private int _turn, _idCounter, _emitDepth, _catchBreathUses;
        private string _phase = "setup", _result;
        public string Phase => _phase;
        public string Result => _result;
        public int Turn => _turn;
        public JObject Player => (JObject)_player.DeepClone();
        public JArray Enemies => new JArray(_enemies.Select(e => e.DeepClone()));
        public JArray Hand => new JArray(_piles["hand"].Select(c => c.DeepClone()));
        public JObject ResolvedCard(JObject instance) => (JObject)_resolveCard((JObject)instance.DeepClone()).DeepClone();
        public JObject CardCost(JObject instance) => CardMechanics.CostProfile(ResolvedCard(instance), PassiveSum("powerCostReduction"), WeightClass());
        public JObject WeightClass() => (JObject)_weightSystem.Compute((int?)_attributes["constitution"] ?? 10, (int?)_attributes["strength"] ?? 10, _weights)["weightClass"];
        public CombatSession(OriginalContentCatalog content, JObject mechanics, RandomStreams random, JObject player, IEnumerable<JObject> deck, IEnumerable<string> enemyIds, Func<JObject,JObject> resolveCard, double enemyHpMultiplier = 1, JArray enemyStatuses = null)
            : this(content, mechanics, random, MakePlayer(player), (JObject)(player["attributes"]?.DeepClone() ?? new JObject()), (JObject)(player["weights"]?.DeepClone() ?? new JObject()), resolveCard)
        {
            var sourceDeck = deck.Select(c => (JObject)c.DeepClone()).ToList();
            if (sourceDeck.Any(c => string.IsNullOrWhiteSpace((string)c["instanceId"])) || sourceDeck.Select(c => (string)c["instanceId"]).Distinct(StringComparer.Ordinal).Count() != sourceDeck.Count) throw new ArgumentException("Deck instance IDs must be unique.");
            foreach (var card in sourceDeck) ValidateEffects(ResolvedCard(card)["effects"]);
            foreach (var id in enemyIds)
            {
                var definition = _content.Record("enemies", id);
                var hp = _random.Int("enemyHP", (int)definition["hp"][0], (int)definition["hp"][1]);
                if (double.IsNaN(enemyHpMultiplier) || double.IsInfinity(enemyHpMultiplier) || enemyHpMultiplier <= 0) throw new ArgumentException("Invalid enemy HP multiplier."); if (enemyHpMultiplier != 1) hp = Math.Max(1,checked((int)Math.Floor(hp * enemyHpMultiplier + 0.5)));
                var enemy = new JObject { ["id"] = "e" + (_enemies.Count + 1), ["kind"] = "enemy", ["enemyId"] = id, ["hp"] = hp, ["maxHp"] = hp, ["block"] = 0, ["statuses"] = new JObject(), ["poiseMeter"] = new JObject { ["value"] = 0, ["max"] = definition["poiseMax"].DeepClone() }, ["movesHistory"] = new JArray(), ["intent"] = null, ["pendingMove"] = null, ["skipNextTurn"] = false, ["unlockedMoves"] = new JArray(), ["alive"] = true };
                if (definition["arcaneExposure"] is JObject exposure) { enemy["arcaneExposure"] = exposure.DeepClone(); if ((string)exposure["mode"] == "configured") enemy["arcaneExposure"]["value"] = 0; }
                if (definition["damageResistanceBySchool"] != null) enemy["damageResistanceBySchool"] = definition["damageResistanceBySchool"].DeepClone();
                _enemies.Add(enemy); Emit("enemySpawned", new JObject { ["targetId"] = enemy["id"], ["enemyId"] = id });
            }
            if (_enemies.Count == 0) throw new ArgumentException("Combat requires enemies.");
            var shuffled = _random.Shuffle("shuffle", sourceDeck);
            _piles["draw"].AddRange(shuffled.Where(c => CardMechanics.HasProperty(CardMechanics.FromDefinition(ResolvedCard(c)), "lifecycle.innate")));
            _piles["draw"].AddRange(shuffled.Where(c => !CardMechanics.HasProperty(CardMechanics.FromDefinition(ResolvedCard(c)), "lifecycle.innate")));
            Emit("combatStart", new JObject()); foreach (var status in player["startStatuses"] as JArray ?? new JArray()) Queue(new JObject { ["op"] = "applyStatus", ["target"] = "self", ["status"] = status["status"], ["stacks"] = status["stacks"] ?? 1 },_player,_player,_player);
            foreach (var enemy in _enemies) foreach (var status in enemyStatuses ?? new JArray()) Queue(new JObject { ["op"] = "applyStatus", ["target"] = "self", ["status"] = status["status"], ["stacks"] = status["stacks"] ?? 1 },enemy,enemy,enemy);
            Drain(); RollIntents(true); if (_result == null) StartPlayerTurn();
        }
        private CombatSession(OriginalContentCatalog content, JObject mechanics, RandomStreams random, JObject player, JObject attributes, JObject weights, Func<JObject,JObject> resolveCard)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content)); _mechanics = (JObject)mechanics.DeepClone(); _balance = (JObject)content.Data()["balance"];
            _random = random ?? throw new ArgumentNullException(nameof(random)); _resolveCard = resolveCard ?? throw new ArgumentNullException(nameof(resolveCard));
            _player = player; _attributes = attributes; _weights = weights; _weightSystem = new WeightSystem(mechanics);
            foreach (var pile in new[] { "draw", "hand", "discard", "exhaust", "sealed", "removed" }) _piles[pile] = new List<JObject>();
            _context = new CombatContext(content, _player, _enemies); _statuses = new StatusSystem(_context);
            foreach (var op in CombatOperations) _context.Register(op, Execute);
            _context.Register("__script", Execute);
            _context.Emitted += OnEvent;
        }
        public JArray PlayCard(string instanceId, string targetId = null)
        {
            RequirePlayerTurn(); var start = _events.Count;
            var instance = _piles["hand"].FirstOrDefault(c => (string)c["instanceId"] == instanceId) ?? throw new ArgumentException("Card is not in hand: " + instanceId);
            var definition = ResolvedCard(instance); var view = CardMechanics.FromDefinition(definition);
            if (CardMechanics.HasProperty(view,"internal.unplayable")) throw new ArgumentException("Card is unplayable: " + instance["cardId"]);
            ValidateEffects(definition["effects"]);
            var target = targetId == null ? null : Find(targetId);
            if (targetId != null && !Alive(target)) throw new ArgumentException("Target is not alive: " + targetId);
            if (target == null && ((JArray)definition["effects"]).Any(e => (string)e["target"] == "enemy")) target = _enemies.FirstOrDefault(Alive) ?? throw new ArgumentException("No living enemy.");
            var profile = CardCost(instance); var cost = (bool)profile["variable"] ? (int)_player["energy"] : (int)profile["action"];
            var wallet = Wallet(); if (!wallet.TryPayProfile(profile, true, (bool)profile["variable"] ? (int?)cost : null)) throw new ArgumentException("Insufficient actions, mana or stamina.");
            CopyWallet(wallet);
            if (cost > 0 || (bool)profile["variable"]) Emit("energySpent", new JObject { ["amount"] = cost });
            if ((int)profile["mana"] > 0) Emit("manaSpent", new JObject { ["amount"] = profile["mana"] });
            if ((int)profile["stamina"] > 0) Emit("staminaSpent", new JObject { ["amount"] = profile["stamina"] });
            _piles["hand"].Remove(instance); Increment("cardsPlayedThisTurn"); Increment("cardsPlayedThisCombat");
            var meta = new JObject { ["energySpent"] = cost, ["manaSpent"] = profile["mana"], ["staminaSpent"] = profile["stamina"], ["ordinalThisTurn"] = Counter("cardsPlayedThisTurn"), ["ordinalThisCombat"] = Counter("cardsPlayedThisCombat"), ["attackOrdinal"] = null };
            if ((string)definition["type"] == "attack") { Increment("attacksPlayedThisCombat"); meta["attackOrdinal"] = Counter("attacksPlayedThisCombat"); }
            var carrier = new JObject { ["instanceId"] = instance["instanceId"], ["cardId"] = instance["cardId"], ["upgraded"] = instance["upgraded"] ?? false, ["type"] = definition["type"], ["tags"] = definition["cardTags"]?.DeepClone() ?? definition["tags"]?.DeepClone() ?? new JArray(_content.Tags("card",definition)), ["damageSchool"] = instance["damageSchool"] ?? definition["damageSchool"], ["exposureBuildupPerHit"] = instance["exposureBuildupPerHit"] ?? definition["exposureBuildupPerHit"] };
            QueueEffects(definition["effects"], _player, _player, target, meta, carrier);
            Emit("cardPlayed", new JObject { ["cardInstanceId"] = instance["instanceId"], ["cardId"] = instance["cardId"], ["cardType"] = definition["type"], ["targetId"] = target?["id"], ["ordinalThisTurn"] = meta["ordinalThisTurn"], ["ordinalThisCombat"] = meta["ordinalThisCombat"], ["energySpent"] = cost, ["manaSpent"] = profile["mana"], ["staminaSpent"] = profile["stamina"] });
            Drain();
            if (_result == null)
            {
                var destination = CardMechanics.AfterPlay(view); var pile = destination == "EXHAUST_PILE" ? "exhaust" : destination == "HAND" ? "hand" : destination == "REMOVED_FROM_PLAY" ? "removed" : destination == "SEALED" ? "sealed" : "discard";
                _piles[pile].Add(instance);
                if (pile == "exhaust") CardEvent("cardExhausted", instance, "played"); Drain();
            }
            else _piles["removed"].Add(instance);
            return Since(start);
        }
        public JArray EndTurn()
        {
            RequirePlayerTurn(); var start = _events.Count;
            Emit("playerTurnEnd", new JObject { ["turn"] = _turn }); OwnerHooks(_player,"ownerTurnEnd"); Drain(); if (_result != null) return Since(start);
            _statuses.DecayAtTurnEnd(_player);
            var wallet = Wallet(); var before = (int)_player["stamina"]; wallet.EndTurn(); CopyWallet(wallet);
            if ((int)_player["stamina"] != before) Emit("staminaRecovered", new JObject { ["amount"] = (int)_player["stamina"] - before, ["reason"] = "idle" });
            var exhaust = new List<JObject>(); var discard = new List<JObject>();
            foreach (var card in _piles["hand"].ToArray()) { var fate = CardMechanics.EndTurnFate(CardMechanics.FromDefinition(ResolvedCard(card))); if (fate == "keep") continue; _piles["hand"].Remove(card); (fate == "exhaust" ? exhaust : discard).Add(card); }
            foreach (var card in exhaust) { _piles["exhaust"].Add(card); CardEvent("cardExhausted",card,"ethereal"); }
            foreach (var card in discard) { _piles["discard"].Add(card); CardEvent("cardDiscarded",card,"turnEnd"); }
            _player["energy"] = 0; Drain(); if (_result != null) return Since(start);
            EnemyTurn(); if (_result == null) { RollIntents(); StartPlayerTurn(); } return Since(start);
        }
        public JArray CatchBreath()
        {
            RequirePlayerTurn(); var config = _mechanics["stamina"]?["catchBreath"] as JObject ?? throw new NotSupportedException("Catch Breath is not enabled in this ruleset.");
            var cost = CardMechanics.Nonnegative(config["actionCost"], "Catch Breath action cost"); var recovery = CardMechanics.Nonnegative(config["recovery"], "Catch Breath recovery"); var limit = CardMechanics.Nonnegative(config["usesPerTurn"], "Catch Breath limit");
            if (recovery == 0 || _catchBreathUses >= limit || (int)_player["stamina"] >= (int)_player["maxStamina"]) throw new ArgumentException("Catch Breath is unavailable.");
            var start = _events.Count; var wallet = Wallet(); if (!wallet.TryPay(new JArray(new JObject { ["resource"] = "action", ["amount"] = cost }))) throw new ArgumentException("Insufficient actions for Catch Breath.");
            var before = (int)_player["stamina"]; wallet.RecoverStamina(recovery); CopyWallet(wallet); _catchBreathUses++;
            if (cost > 0) Emit("energySpent",new JObject { ["amount"] = cost }); Emit("staminaRecovered",new JObject { ["amount"] = (int)_player["stamina"] - before, ["reason"] = "catchBreath" }); Drain(); return Since(start);
        }
        private void StartPlayerTurn()
        {
            _turn++; _phase = "player"; _catchBreathUses = 0; _player["counters"]["cardsPlayedThisTurn"] = 0;
            if (!_statuses.Flag(_player,"retainBlock")) _player["block"] = 0; else { var cap = BlockCap(_player); if (cap.HasValue) _player["block"] = Math.Min((int)_player["block"],cap.Value); }
            var wallet = Wallet(); wallet.BeginTurn((int)_player["energyMax"]); CopyWallet(wallet); Draw((int)_player["drawPerTurn"]);
            Emit("playerTurnStart",new JObject { ["turn"] = _turn }); OwnerHooks(_player,"ownerTurnStart"); Drain();
        }
        public JObject Snapshot()
        {
            if (_context.PendingCount != 0) throw new InvalidOperationException("Cannot save a pending combat command.");
            return new JObject { ["schemaVersion"] = 1, ["seed"] = _random.Seed, ["rng"] = JObject.FromObject(_random.Snapshot()), ["turn"] = _turn, ["phase"] = _phase, ["result"] = _result, ["idCounter"] = _idCounter, ["catchBreathUses"] = _catchBreathUses, ["player"] = _player.DeepClone(), ["attributes"] = _attributes.DeepClone(), ["weights"] = _weights.DeepClone(), ["enemies"] = Enemies, ["piles"] = new JObject(_piles.Select(p => new JProperty(p.Key,new JArray(p.Value.Select(c => c.DeepClone()))))), ["triggerState"] = JObject.FromObject(_triggerState), ["events"] = new JArray(_events.Select(e => e.DeepClone())) };
        }
        public static CombatSession Restore(OriginalContentCatalog content, JObject mechanics, JObject snapshot, Func<JObject,JObject> resolveCard)
        {
            if ((int?)snapshot["schemaVersion"] != 1 || !new[] { "player", "enemy", "ended" }.Contains((string)snapshot["phase"])) throw new ArgumentException("Unsupported combat snapshot.");
            var random = new RandomStreams((uint)snapshot["seed"], ((JObject)snapshot["rng"]).ToObject<Dictionary<string,uint>>());
            var session = new CombatSession(content,mechanics,random,(JObject)snapshot["player"].DeepClone(),(JObject)snapshot["attributes"].DeepClone(),(JObject)snapshot["weights"].DeepClone(),resolveCard);
            session._turn = CardMechanics.Nonnegative(snapshot["turn"],"turn"); session._phase = (string)snapshot["phase"]; session._result = (string)snapshot["result"]; session._idCounter = CardMechanics.Nonnegative(snapshot["idCounter"],"instance counter"); session._catchBreathUses = CardMechanics.Nonnegative(snapshot["catchBreathUses"],"Catch Breath uses");
            foreach (var enemy in (JArray)snapshot["enemies"]) { var copy = (JObject)enemy.DeepClone(); content.Record("enemies",(string)copy["enemyId"]); session._enemies.Add(copy); }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pile in session._piles.Keys) foreach (var card in (JArray)snapshot["piles"][pile]) { var copy = (JObject)card.DeepClone(); var id = (string)copy["instanceId"]; if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("Duplicate saved card instance."); session.ResolvedCard(copy); session._piles[pile].Add(copy); }
            foreach (var entry in ((JObject)snapshot["triggerState"]).Properties()) session._triggerState.Add(entry.Name,(JObject)entry.Value.DeepClone());
            foreach (var entry in (JArray)snapshot["events"]) session._events.Add((JObject)entry.DeepClone());
            session.Wallet(); return session;
        }
        private static JObject MakePlayer(JObject input)
        {
            var hp = CardMechanics.Nonnegative(input["maxHp"],"max HP"); if (hp == 0) throw new ArgumentException("Maximum HP must be positive.");
            return new JObject { ["id"] = "player", ["kind"] = "player", ["classId"] = input["classId"], ["hp"] = input["hp"] ?? hp, ["maxHp"] = hp, ["mana"] = input["mana"] ?? input["maxMana"] ?? 0, ["maxMana"] = input["maxMana"] ?? 0, ["stamina"] = input["stamina"] ?? input["maxStamina"] ?? 0, ["maxStamina"] = input["maxStamina"] ?? 0, ["block"] = 0, ["energy"] = 0, ["energyMax"] = CardMechanics.Nonnegative(input["energyMax"],"actions"), ["drawPerTurn"] = CardMechanics.Nonnegative(input["drawPerTurn"],"draw"), ["statuses"] = new JObject(), ["stanceId"] = null, ["relicIds"] = input["relicIds"]?.DeepClone() ?? new JArray(), ["itemUpgradeLevels"] = input["itemUpgradeLevels"]?.DeepClone() ?? new JObject(), ["damageBySchoolAdd"] = input["damageBySchoolAdd"]?.DeepClone() ?? new JObject(), ["flasks"] = input["flasks"]?.DeepClone() ?? new JArray(), ["flaskCharges"] = input["flaskCharges"]?.DeepClone(), ["counters"] = new JObject { ["cardsPlayedThisTurn"] = 0, ["cardsPlayedThisCombat"] = 0, ["attacksPlayedThisCombat"] = 0, ["staminaSpentThisTurn"] = 0 }, ["alive"] = true };
        }
        private ResourceWallet Wallet() => new ResourceWallet(new JObject { ["action"] = _player["energy"], ["mana"] = _player["mana"], ["stamina"] = _player["stamina"], ["maxMana"] = _player["maxMana"], ["maxStamina"] = _player["maxStamina"], ["staminaSpentThisTurn"] = Counter("staminaSpentThisTurn") },_mechanics);
        private void CopyWallet(ResourceWallet wallet) { var state = wallet.Snapshot(); _player["energy"] = state["action"]; _player["mana"] = state["mana"]; _player["stamina"] = state["stamina"]; _player["counters"]["staminaSpentThisTurn"] = state["staminaSpentThisTurn"]; }
        private void RequirePlayerTurn() { if (_result != null || _phase != "player") throw new InvalidOperationException("Command requires an active player turn."); }
        private static bool Alive(JObject entity) => (bool?)entity?["alive"] == true;
        private JObject Find(string id) => _coopFind?.Invoke(id) ?? (id == "player" ? _player : _enemies.FirstOrDefault(e => (string)e["id"] == id));
        private int Counter(string key) => (int?)_player["counters"][key] ?? 0;
        private void Increment(string key) => _player["counters"][key] = checked(Counter(key) + 1);
        private JArray Since(int start) => new JArray(_events.Skip(start).Select(e => e.DeepClone()));
        private void Emit(string type,JObject payload) => _context.Emit(type,payload);
        private void CardEvent(string type,JObject card,string reason) => Emit(type,new JObject { ["cardInstanceId"] = card["instanceId"], ["cardId"] = card["cardId"], ["reason"] = reason });
        private void Drain() { _context.Drain(); EndCheck(); }
        private void EndCheck()
        {
            if (_coopEndCheck != null) { _coopEndCheck(); return; }
            if (_result != null) return;
            if (!Alive(_player) || (int)_player["hp"] <= 0) _result = "defeat"; else if (_enemies.Count > 0 && _enemies.All(e => !Alive(e))) _result = "victory";
            if (_result != null) { _phase = "ended"; Emit("combatEnd",new JObject { ["victory"] = _result == "victory" }); }
        }
    }
}
