// Core2Fight.cs — a whole Core 2 fight, engine-free: setup, the event bus and property triggers, the action queue, the
// turn order (player turn, enemy phase, round refresh) and the end of the fight. The rules of one hit, one price and one
// stance stay in the 5a/5b types; this file runs them in the order the JS engine does.
//
// JS at the pin:
//   src/engine/runCombat.js        createRunCombat (the run hands the fight its player; here Core2FightSetup)
//   src/engine/combat.js           createCombat (enemy HP on enemyHP, the shuffled deck, combatStart, the first
//                                  intents, the first player turn), drainQueue / endCheck / finishCombat,
//                                  startPlayerTurn, endPlayerTurn / finishPlayerHand, enemyTurnStart,
//                                  enemyOwnerTurnStart, enemyPhase, completeEndTurn / finishReactionPhase
//   src/engine/triggers.js         emitEvent, scanTriggers (property mounts in sorted source-key order; a relic source
//                                  announces relicTriggered after it fires), maybeFire (once / limitPerTurn),
//                                  fireOwnerHooks (status hooks on ownerTurnStart / ownerTurnEnd), checkPhases
//   src/engine/gameProfileCombat.js refreshProfileRound (cards.roundShuffle entireDeck: every card back, one shuffle)
// Reactions are phase 7: a Core 2 fight here runs with the player's reactions switched off (JS setReactions false), so
// each incoming contact is announced (combatContactPrepared) and reported unavailable (combatReactionUnavailable).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2CardInstance
    {
        public string InstanceId { get; set; }
        public string CardId { get; set; }
        public override string ToString() => CardId + "#" + InstanceId;
    }

    /// <summary>One event on the fight's bus (JS combat.eventLog row): its type and payload.</summary>
    public sealed class Core2FightEvent
    {
        public string Type { get; set; }
        public JObject Data { get; set; }
        public override string ToString() => Type + " " + Data.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>A card face as the player's gear resolves it: its combat profile and the range source its damage commits.</summary>
    public sealed class Core2CardFace
    {
        public Core2CombatProfile Profile { get; set; }
        public Core2RangeSource RangeSource { get; set; }
    }

    /// <summary>A mounted property carrier (relic, class card, gear) and its triggers in rule order (JS propertyMounts[owner][source]).</summary>
    public sealed class Core2PropertyCarrier
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public List<JObject> Triggers { get; set; } = new List<JObject>();
        /// <summary>JS propertySourceKey: kind:instanceId (gear mounts as armament/&lt;id&gt;).</summary>
        public string SourceKey => Kind + ":" + (Kind == "armament" ? "armament/" + Id : Kind == "armour" ? "armour/" + Id : Id);
    }

    /// <summary>
    /// What the run hands a Core 2 fight (JS createRunCombat's player block): the player's pools, attributes and ratings,
    /// the deck in run order, each card's resolved face, the mounted property rules and the primary weapon's range.
    /// Building it from a run is phase 8; the scripted-fights oracle records it (input.start).
    /// </summary>
    public sealed class Core2FightSetup
    {
        public string ClassId { get; set; }
        public bool ClassUnequipped { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int MaxStamina { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }
        public int Level { get; set; } = 1;
        public Dictionary<string, int> Attributes { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Ratings { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public int BaseEvasionBonus { get; set; }
        public int BasePrecisionBonus { get; set; }
        public int PoiseMax { get; set; }
        public List<string> RelicIds { get; set; } = new List<string>();
        public List<Core2CardInstance> Deck { get; set; } = new List<Core2CardInstance>();
        public Dictionary<string, Core2CardFace> Cards { get; set; } = new Dictionary<string, Core2CardFace>(StringComparer.Ordinal);
        public List<Core2PropertyCarrier> PropertyRules { get; set; } = new List<Core2PropertyCarrier>();
        public string PrimaryWeaponRange { get; set; } = "melee";

        public static Core2FightSetup FromJson(JObject start)
        {
            var player = (JObject)start["player"];
            Dictionary<string, int> Ints(JToken token) => token is JObject row
                ? row.Properties().ToDictionary(p => p.Name, p => (int)Math.Floor((double)p.Value), StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
            var setup = new Core2FightSetup
            {
                ClassId = (string)player["classId"], ClassUnequipped = (bool?)player["classUnequipped"] ?? false,
                Hp = (int)player["hp"], MaxHp = (int)player["maxHp"], MaxStamina = (int)player["maxStamina"], Mana = (int)player["mana"], MaxMana = (int)player["maxMana"],
                Level = (int?)player["level"] ?? 1, Attributes = Ints(player["attributes"]), Ratings = Ints(player["ratings"]),
                BaseEvasionBonus = (int?)player["baseEvasionBonus"] ?? 0, BasePrecisionBonus = (int?)player["basePrecisionBonus"] ?? 0,
                PoiseMax = (int?)player["poise"]?["max"] ?? (int?)player["ratings"]?["poise"] ?? 0,
                RelicIds = Core2Json.Strings(player["relicIds"]).ToList(),
                PrimaryWeaponRange = (string)start["primaryWeaponRange"] ?? "melee",
                Deck = ((JArray)start["deck"]).OfType<JObject>().Select(c => new Core2CardInstance { InstanceId = (string)c["instanceId"], CardId = (string)c["cardId"] }).ToList(),
            };
            foreach (var card in (start["cards"] as JObject ?? new JObject()).Properties())
            {
                var source = card.Value["rangeSource"] as JObject;
                setup.Cards[card.Name] = new Core2CardFace
                {
                    Profile = Core2CombatProfile.FromJson(card.Value["combatProfile"]),
                    RangeSource = source == null ? null : new Core2RangeSource
                    {
                        Coverage = (string)source["coverage"] ?? "melee", Weapon = (bool?)source["weapon"] ?? false, SourceWeaponId = (string)source["sourceWeaponId"],
                        ThrowCapable = (bool?)source["throwCapable"] ?? false, Bypass = (bool?)source["bypass"] ?? false,
                    },
                };
            }
            foreach (var mount in (start["propertyRules"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var carrier = new Core2PropertyCarrier { Kind = (string)mount["kind"], Id = (string)mount["id"] };
                foreach (var rule in (mount["rules"] as JArray ?? new JArray()).OfType<JObject>())
                    foreach (var trigger in (rule["triggers"] as JArray ?? new JArray()).OfType<JObject>()) carrier.Triggers.Add(trigger);
                setup.PropertyRules.Add(carrier);
            }
            return setup;
        }

        /// <summary>A bare player for enemy-only work (plans, quotes): the class's attributes, a Melee weapon, no deck.</summary>
        public static Core2FightSetup Minimal(Core2Content content, string classId)
        {
            var cls = content.Class(classId);
            return new Core2FightSetup
            {
                ClassId = classId, Hp = cls.MaxHp, MaxHp = cls.MaxHp, MaxStamina = 3, Mana = 0, MaxMana = 0,
                Attributes = cls.Attributes.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            };
        }
    }

    /// <summary>One queued action (JS { effect, source, owner, target, card, meta }).</summary>
    public sealed class Core2FightAction
    {
        public JObject Effect { get; set; }
        public Core2Combatant Source { get; set; }
        public Core2Combatant Owner { get; set; }
        public Core2Combatant Target { get; set; }
        public Core2Carrier Card { get; set; }
        public string MoveId { get; set; }
        public bool Preparing { get; set; }
        /// <summary>JS meta.triggeredBy: the property or status whose trigger enqueued this action.</summary>
        public JObject TriggeredBy { get; set; }
        /// <summary>JS meta.event: the event a trigger fired on (its target is the trigger's contextual target).</summary>
        public Core2FightEvent Event { get; set; }
        public Core2ActionGroup Group { get; set; }
        /// <summary>JS meta.contactIndex (perHit): which contact of the group this unit is; -1 for an ungrouped or whole action.</summary>
        public int ContactIndex { get; set; } = -1;
        public bool ContactCompletion { get; set; }
        public bool GroupCompletion { get; set; }
        /// <summary>JS meta.contactEffectFirst: this unit is the first contact of its damage effect.</summary>
        public bool ContactEffectFirst { get; set; } = true;
        public int ContactEffectIndex { get; set; }
        public bool CounterReaction { get; set; }
        /// <summary>JS meta.combatCounterInterrupted: the returning entity was staggered or lost its Counter before the return ran.</summary>
        public bool CounterInterrupted { get; set; }
        public int EnergySpent { get; set; }
        public int ManaSpent { get; set; }
    }

    public sealed partial class Core2Fight
    {
        public Core2Content Content { get; }
        public Core2Rules Rules => Ctx.Rules;
        public Core2CombatContext Ctx { get; }
        public Core2Rng Rng { get; }
        public uint Seed { get; }
        public Core2Combatant Player { get; private set; }
        public List<Core2Combatant> Enemies { get; } = new List<Core2Combatant>();
        public Core2RangeState Range { get; set; }
        public Core2FightSetup Setup { get; private set; }

        public List<Core2CardInstance> Draw { get; set; } = new List<Core2CardInstance>();
        public List<Core2CardInstance> Hand { get; set; } = new List<Core2CardInstance>();
        public List<Core2CardInstance> Discard { get; set; } = new List<Core2CardInstance>();
        public List<Core2CardInstance> Exhaust { get; set; } = new List<Core2CardInstance>();
        public int HandMax { get; private set; }

        public List<Core2FightEvent> Events { get; } = new List<Core2FightEvent>();
        /// <summary>Optional observer of every event as it is emitted (a parity trace).</summary>
        public Action<Core2FightEvent> OnEvent { get; set; }
        /// <summary>victory, defeat, or null while the fight goes on.</summary>
        public string Result { get; private set; }
        /// <summary>setup, player, enemy or ended (JS combat.phase).</summary>
        public string Phase { get => Ctx.Phase; private set => Ctx.Phase = value; }
        public int Turn => Ctx.Turn;
        /// <summary>JS enemyKnowledge.nextSerial: the last presented enemy action's serial.</summary>
        public int KnowledgeSerial { get; set; }
        /// <summary>JS combat.followTrace: per enemy, each follow-up's own read and outcome this enemy phase.</summary>
        public Dictionary<string, List<Core2FollowTrace>> FollowTrace { get; } = new Dictionary<string, List<Core2FollowTrace>>(StringComparer.Ordinal);
        /// <summary>The player's reaction switch (JS player.reactionsEnabled); reactions themselves are phase 7.</summary>
        public bool ReactionsEnabled { get; set; } = true;

        private readonly List<Core2FightAction> _queue = new List<Core2FightAction>();
        private readonly Dictionary<string, (int Fires, int Turn, int TurnFires)> _triggerState = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        private int _expansionSerial;
        private int _pendingExpansionActions;
        private int _commands;
        private int _generatedCards; // JS combat._idCounter (nextInstanceId: gen1, gen2, ...)

        private Core2Fight(Core2Content content, Core2CombatContext ctx, uint seed)
        {
            Content = content; Ctx = ctx; Seed = seed; Rng = new Core2Rng(seed);
        }

        public Core2Enemy EnemyDef(Core2Combatant enemy) => Content.Enemy(enemy.EnemyId);

        // ================================================================================================================
        // Setup (JS createCombat, configured profile with FixCore)
        // ================================================================================================================

        /// <summary>
        /// JS createRunCombat → createCombat: roll each enemy's HP, stamp the player and enemies, shuffle the deck, announce
        /// the fight, plan and present the first intents, and start the first player turn. The deck shuffle, HP rolls and
        /// plans draw on the fight's own seeded streams.
        /// </summary>
        public static Core2Fight Create(Core2Content content, Core2FightSetup setup, IReadOnlyList<string> enemyIds, uint seed, string combatKey = null,
            Action<string, double, uint> rngTrace = null)
        {
            var fight = new Core2Fight(content, Core2CombatContext.NewFight(content), seed);
            fight.Rng.OnDraw = rngTrace;
            fight.Phase = "setup";
            fight.Ctx.Turn = 0;
            fight.Setup = setup;
            fight.CombatKey = combatKey ?? seed.ToString(CultureInfo.InvariantCulture) + "/1/0/" + string.Join(",", enemyIds);
            var p = fight.Player = new Core2Combatant
            {
                Id = "player", Side = Core2Side.Player, ClassId = setup.ClassId, ClassUnequipped = setup.ClassUnequipped,
                Hp = setup.Hp, MaxHp = setup.MaxHp, MaxStamina = setup.MaxStamina, Stamina = setup.MaxStamina, Mana = setup.Mana, MaxMana = setup.MaxMana,
                Level = setup.Level, RelicIds = new List<string>(setup.RelicIds), RangeKey = "player:player",
            };
            for (var i = 0; i < enemyIds.Count; i++)
            {
                var def = content.Enemy(enemyIds[i]);
                var hp = fight.Rng.Int("enemyHP", def.HpMin, def.HpMax);
                fight.Enemies.Add(new Core2Combatant
                {
                    Id = "e" + (i + 1), Side = Core2Side.Enemy, EnemyId = def.Id, Hp = hp, MaxHp = hp, MaxHpRolled = hp,
                    HasLevelProfile = def.Raw["levelProfile"] is JObject, RangeKey = "enemy:e" + (i + 1),
                });
                fight.Emit("enemySpawned", new JObject { ["targetId"] = "e" + (i + 1), ["enemyId"] = def.Id });
            }
            // JS initializeCombatRange: the player at its primary weapon's range, each enemy at its authored band.
            fight.Range = Core2RangeState.Create(fight.Rules.RangePolicy, new[] { new Core2RangeActor("player:player", setup.PrimaryWeaponRange ?? "melee") }
                .Concat(fight.Enemies.Select(e => new Core2RangeActor(e.RangeKey, (string)fight.EnemyDef(e).Raw["startingRange"] ?? "melee"))));
            // Configured ratings and the enemy stat blocks (gameProfileEnemyRatings).
            p.Ratings = new Dictionary<string, int>(StringComparer.Ordinal) { ["ar"] = 0, ["dr"] = 0, ["pr"] = 0, ["ward"] = 0, ["poise"] = setup.PoiseMax };
            foreach (var r in setup.Ratings) p.Ratings[r.Key] = r.Value;
            p.Attributes = new Dictionary<string, int>(setup.Attributes, StringComparer.Ordinal);
            p.BaseEvasionBonus = setup.BaseEvasionBonus; p.BasePrecisionBonus = setup.BasePrecisionBonus;
            foreach (var enemy in fight.Enemies)
            {
                var def = fight.EnemyDef(enemy);
                var block = Core2EnemyStats.Resolve(content, enemy.EnemyId, enemy.Id, seed, Core2EnemyStats.BaseRatings(def));
                enemy.Attributes = block.Attributes; enemy.Level = block.Level;
                enemy.MaxStamina = block.MaxStamina; enemy.Stamina = block.MaxStamina; enemy.MaxMana = block.MaxMana; enemy.Mana = block.MaxMana;
                enemy.BaseEvasionBonus = block.BaseEvasionBonus; enemy.BasePrecisionBonus = block.BasePrecisionBonus; enemy.Ratings = block.Ratings;
            }
            // Pools: Ward is off in Core 2; Poise starts full at its rating (JS persistentPoiseMaximum, poisePool 1).
            foreach (var entity in new[] { p }.Concat(fight.Enemies))
            {
                var max = Math.Max(0, Core2Statuses.RatingValue(fight.Rules, entity, "poise"));
                entity.Poise = new Core2Pool(max, max);
                entity.Ward = new Core2Pool(0, 0);
                entity.Barrier = 0;
            }
            foreach (var mount in setup.PropertyRules) fight._mounts.Add(mount);
            fight.Draw = fight.OrderedDrawPile(fight.Rng.Shuffle("shuffle", setup.Deck.Select(c => new Core2CardInstance { InstanceId = c.InstanceId, CardId = c.CardId })));
            fight.HandMax = Math.Max(0, fight.Rules.HandBase + p.Attribute(fight.Rules.HandAttribute));
            fight.Emit("combatStart", new JObject());
            fight.DrainQueue();
            fight.RollIntents(true);
            if (fight.Result == null) fight.StartPlayerTurn();
            return fight;
        }

        /// <summary>JS combatKey: seed/act/floor/node (the oracle fights are act 1, floor 0, keyed by their enemy ids).</summary>
        public string CombatKey { get; private set; }

        /// <summary>JS orderedDrawPile: Innate cards to the top, otherwise the shuffled order.</summary>
        private List<Core2CardInstance> OrderedDrawPile(List<Core2CardInstance> pile)
        {
            bool Innate(Core2CardInstance c) => Core2Json.Strings(Content.Card(c.CardId).Raw["keywords"]).Contains("innate");
            return pile.Where(Innate).Concat(pile.Where(c => !Innate(c))).ToList();
        }

        // ================================================================================================================
        // Events and property triggers (JS triggers.js)
        // ================================================================================================================

        private readonly List<Core2PropertyCarrier> _mounts = new List<Core2PropertyCarrier>();
        /// <summary>The action whose execution is emitting (JS ctx._abilityAction): its card and move label the event.</summary>
        private Core2FightAction _currentAction;
        private int _emitDepth;

        /// <summary>JS emitEvent: label the event with the current action, log it, then scan the triggers listening for it.</summary>
        public Core2FightEvent Emit(string type, JObject payload)
        {
            var data = payload ?? new JObject();
            var action = _currentAction;
            if (action?.Card != null)
            {
                var labelled = new JObject();
                if (action.Card.Id != null && action.MoveId == null) labelled["cardId"] = action.Card.Id;
                if (action.Source != null) { labelled["sourceId"] = action.Source.Id; labelled["sourceKind"] = action.Source.Side == Core2Side.Enemy ? "enemy" : "player"; }
                foreach (var p in data.Properties()) labelled[p.Name] = p.Value;
                data = labelled;
            }
            if (action?.Preparing == true) { if (data["preparing"] == null) data["preparing"] = true; }
            else if (action?.MoveId != null && data["moveId"] == null) data["moveId"] = action.MoveId;
            if (action?.TriggeredBy != null && data["triggeredBy"] == null) data["triggeredBy"] = action.TriggeredBy.DeepClone();
            var ev = new Core2FightEvent { Type = type, Data = data };
            Events.Add(ev);
            OnEvent?.Invoke(ev);
            if (++_emitDepth > 64) throw new InvalidOperationException("Trigger recursion exceeded 64 (event '" + type + "')");
            try { ScanTriggers(ev); }
            finally { _emitDepth--; }
            return ev;
        }

        /// <summary>
        /// JS scanTriggers (no foundation ruleset): every mounted property of the player in sorted source-key order, then
        /// every living combatant's status hooks, fire as they are found; a relic source announces relicTriggered after it
        /// fires. Enemy phases listening on a bus event fire last.
        /// </summary>
        private void ScanTriggers(Core2FightEvent ev)
        {
            if (Player == null) return;
            foreach (var mount in _mounts.OrderBy(m => m.SourceKey, StringComparer.Ordinal))
            {
                for (var i = 0; i < mount.Triggers.Count; i++)
                {
                    var trigger = mount.Triggers[i];
                    if ((string)trigger["on"] != ev.Type) continue;
                    var origin = new JObject { ["kind"] = mount.Kind, ["id"] = mount.Id };
                    if (MaybeFire("property:player:" + mount.SourceKey + ":" + i, trigger, Player, ev, origin) && mount.Kind == "relic" && ev.Type != "relicTriggered")
                        Emit("relicTriggered", new JObject { ["relicId"] = mount.Id });
                }
            }
            foreach (var entity in new[] { Player }.Concat(Enemies).ToList())
            {
                if (!entity.Alive) continue;
                foreach (var statusId in entity.Statuses.Keys.ToList())
                {
                    var hooks = StatusHooks(statusId);
                    for (var i = 0; i < hooks.Count; i++)
                        if ((string)hooks[i]["on"] == ev.Type)
                            MaybeFire("status:" + entity.Id + ":" + statusId + ":" + i, hooks[i], entity, ev, new JObject { ["kind"] = "status", ["id"] = statusId });
                }
            }
            foreach (var enemy in Enemies.Where(e => e.Alive).ToList())
            {
                var phases = EnemyDef(enemy).Raw["phases"] as JArray ?? new JArray();
                for (var i = 0; i < phases.Count; i++)
                    if ((string)phases[i]["on"] == ev.Type) FirePhase(enemy, (JObject)phases[i], i, ev);
            }
        }

        private IReadOnlyList<JObject> StatusHooks(string statusId)
        {
            var row = Content.Statuses.FirstOrDefault(s => s.Id == statusId);
            return (row?.Raw["hooks"] as JArray ?? new JArray()).OfType<JObject>().ToList();
        }

        /// <summary>JS maybeFire: once / limitPerTurn gates, the trigger's predicate, then its effects are queued.</summary>
        private bool MaybeFire(string key, JObject trigger, Core2Combatant owner, Core2FightEvent ev, JObject origin)
        {
            _triggerState.TryGetValue(key, out var st);
            if (!_triggerState.ContainsKey(key)) st = (0, -1, 0);
            if ((bool?)trigger["once"] == true && st.Fires > 0) return false;
            if (st.Turn != Turn) { st.Turn = Turn; st.TurnFires = 0; }
            if (trigger["limitPerTurn"] != null && trigger["limitPerTurn"].Type != JTokenType.Null && st.TurnFires >= (int)trigger["limitPerTurn"]) { _triggerState[key] = st; return false; }
            var target = EventEntity(ev);
            if (trigger["if"] is JObject predicate && !Predicate(predicate, owner, target, ev, null)) { _triggerState[key] = st; return false; }
            st.Fires += 1; st.TurnFires += 1;
            _triggerState[key] = st;
            foreach (var effect in (trigger["do"] as JArray ?? new JArray()).OfType<JObject>())
                Enqueue(new Core2FightAction { Effect = effect, Source = owner, Owner = owner, Target = target, Event = ev, TriggeredBy = new JObject { ["kind"] = origin["kind"], ["id"] = origin["id"], ["ownerId"] = owner?.Id } });
            return true;
        }

        /// <summary>JS resolveEventEntity: the event's target (or enemy) when it names one.</summary>
        private Core2Combatant EventEntity(Core2FightEvent ev)
        {
            var id = (string)ev?.Data["targetId"] ?? (string)ev?.Data["enemyId"];
            return id == null ? null : Find(id);
        }

        public Core2Combatant Find(string id) => Player?.Id == id ? Player : Enemies.FirstOrDefault(e => e.Id == id);

        /// <summary>JS evalPredicate for the predicates Core 2 property rules use.</summary>
        private bool Predicate(JObject pred, Core2Combatant owner, Core2Combatant target, Core2FightEvent ev, Core2FightAction action)
        {
            switch ((string)pred["p"])
            {
                case "all": return (pred["preds"] as JArray ?? new JArray()).OfType<JObject>().All(p => Predicate(p, owner, target, ev, action));
                case "any": return (pred["preds"] as JArray ?? new JArray()).OfType<JObject>().Any(p => Predicate(p, owner, target, ev, action));
                case "not": return !Predicate((JObject)pred["pred"], owner, target, ev, action);
                case "eventIsAttack": return (bool?)ev?.Data["isAttack"] == true;
                case "eventSourceIsOwner": return (string)ev?.Data["sourceId"] == owner?.Id;
                case "eventTargetIsOwner": return (string)ev?.Data["targetId"] == owner?.Id;
                case "healPositive": return ((int?)ev?.Data["amount"] ?? 0) > 0;
                case "hasStatus":
                {
                    var of = (string)pred["of"];
                    var entity = of == "owner" || of == "self" || of == null ? owner : of == "target" ? target : of == "player" ? Player : owner;
                    return (entity?.StatusStacks((string)pred["status"]) ?? 0) > 0;
                }
                case "skillLevelAtLeast": return false;
                case "cardTagIs":
                {
                    // The action's card (its authored tags), else the event's card tags.
                    var tag = (string)pred["tag"];
                    if (action?.Card != null) return action.Card.Tags.Contains(tag);
                    return ev?.Data["cardTags"] is JArray tags && tags.Any(t => (string)t == tag);
                }
                case "resourcePaid":
                {
                    var paid = (string)pred["resource"] == "mana" ? action?.ManaSpent ?? 0 : action?.EnergySpent ?? 0;
                    return paid >= ((int?)pred["amount"] ?? 1);
                }
            }
            throw new NotSupportedException("Predicate '" + (string)pred["p"] + "' is not ported");
        }

        /// <summary>JS firePhase: an enemy phase fires once by default, queues its effects and unlocks its moves.</summary>
        private readonly HashSet<string> _phasesFired = new HashSet<string>(StringComparer.Ordinal);
        private void FirePhase(Core2Combatant enemy, JObject phase, int index, Core2FightEvent ev)
        {
            var key = "phase:" + enemy.Id + ":" + index;
            var once = (bool?)phase["once"] != false;
            if (once && _phasesFired.Contains(key)) return;
            if (phase["if"] is JObject) throw new NotSupportedException("A conditional enemy phase is not ported");
            _phasesFired.Add(key);
            foreach (var effect in (phase["do"] as JArray ?? new JArray()).OfType<JObject>())
                Enqueue(new Core2FightAction { Effect = effect, Source = enemy, Owner = enemy, Target = Player, Event = ev });
            foreach (var moveId in Core2Json.Strings(phase["unlockMoves"])) if (!enemy.UnlockedMoves.Contains(moveId)) enemy.UnlockedMoves.Add(moveId);
        }

        /// <summary>JS checkPhases: hpBelowPct phases after any HP change.</summary>
        private void CheckPhases()
        {
            foreach (var enemy in Enemies.Where(e => e.Alive).ToList())
            {
                var phases = EnemyDef(enemy).Raw["phases"] as JArray ?? new JArray();
                for (var i = 0; i < phases.Count; i++)
                {
                    var phase = (JObject)phases[i];
                    if ((string)phase["on"] != "hpBelowPct") continue;
                    if (enemy.Hp > enemy.MaxHp * (double)phase["pct"] / 100) continue;
                    FirePhase(enemy, phase, i, new Core2FightEvent { Type = "hpBelowPct", Data = new JObject { ["targetId"] = enemy.Id } });
                }
            }
        }

        // ================================================================================================================
        // The queue (JS drainQueue / endCheck / finishCombat)
        // ================================================================================================================

        public void Enqueue(Core2FightAction action) => _queue.Add(action);

        /// <summary>JS drainQueue: run every queued action in order, checking the end after each.</summary>
        private void DrainQueue()
        {
            var guard = 0;
            while (_queue.Count > 0)
            {
                if (++guard > 1000) throw new InvalidOperationException("Action queue did not drain (possible infinite trigger loop)");
                var action = _queue[0];
                BeforeReactionAction(action);
                _queue.RemoveAt(0);
                StartQueuedEnemy(action);
                ExecuteAction(action);
                EndCheck();
                if (Result != null) { _queue.Clear(); return; }
            }
            EndCheck();
        }

        /// <summary>JS endCheck: defeat when the player falls, victory when every enemy has.</summary>
        private void EndCheck()
        {
            if (_pendingExpansionActions > 0 || Result != null || _pendingPlay != null) return;
            if (!Player.Alive || Player.Hp <= 0) Finish("defeat");
            else if (Enemies.Count > 0 && Enemies.All(e => !e.Alive)) Finish("victory");
        }

        private void Finish(string result)
        {
            Result = result;
            Phase = "ended";
            _queue.Clear();
            _pendingExpansionActions = 0;
            Emit("combatEnd", new JObject { ["victory"] = result == "victory" });
            _queue.Clear();
        }

        // ================================================================================================================
        // Turn order (JS startPlayerTurn, endPlayerTurn, enemy phase, finishReactionPhase)
        // ================================================================================================================

        /// <summary>JS startPlayerTurn (Core 2): decay, the tactical turn, the SP refill less a Stagger's loss, the draw, the start event.</summary>
        private void StartPlayerTurn()
        {
            Ctx.Turn += 1;
            Phase = "player";
            var p = Player;
            DecayAtTurnStart(p);
            _commands = 0;
            p.CardsPlayedThisTurn = 0;
            // (2) Block is lost (no Core 2 relic keeps any).
            p.Block = 0;
            p.Barrier = 0;
            StartTacticalTurn(p);
            p.Stamina = Rules.Includes("sp") ? Math.Max(0, p.MaxStamina - p.PendingStaminaLoss) : 0;
            if (Rules.ManaPerTurn > 0) p.Mana = Math.Min(p.MaxMana, p.Mana + Rules.ManaPerTurn);
            p.PendingStaminaLoss = 0;
            DrawCards(Math.Max(1, HandMax));
            Emit("playerTurnStart", new JObject { ["turn"] = Turn });
            FireOwnerHooks(p, "ownerTurnStart");
            DrainQueue();
            if (Result == null) RecoverStatusesAtOwnerStart(p);
        }

        /// <summary>JS startTacticalTurn: the owner cycle advances, the Counter lapses, an expiring stance (Staggered) ends.</summary>
        private void StartTacticalTurn(Core2Combatant entity)
        {
            entity.OwnerCycle += 1;
            ClearCounter(entity);
            if (entity.Stance?.ExpiresOnOwnerCycle is int expiry && entity.OwnerCycle >= expiry) entity.Stance = null;
        }

        /// <summary>JS doEndTurn → completeEndTurn: the player's turn end, the enemy phase, then the round refresh and the next turn.</summary>
        public void EndTurn()
        {
            if (Result != null) throw new InvalidOperationException("Combat is over");
            if (Phase != "player") throw new InvalidOperationException("Not the player turn");
            EndPlayerTurn();
            if (Result != null) return;
            EnemyPhase();
            if (Result != null) return;
            // JS finishReactionPhase (reactions in use): the deferred hand cleanup, then new intents and the next turn.
            FinishPlayerHand();
            if (Result != null) return;
            RollIntents(false);
            StartPlayerTurn();
        }

        /// <summary>JS endPlayerTurn: the end event and owner hooks, then temporary Precision expires (FixCore turn-end decay).</summary>
        private void EndPlayerTurn()
        {
            var p = Player;
            Emit("playerTurnEnd", new JObject { ["turn"] = Turn });
            FireOwnerHooks(p, "ownerTurnEnd");
            DrainQueue();
            if (Result != null) return;
            DecayAtTurnEnd(p);
            p.StaminaSpentThisTurn = 0;
        }

        /// <summary>
        /// JS finishPlayerHand with Core 2's whole-deck round refresh: Ethereal cards exhaust, every other card in hand returns
        /// to the deck with the discard pile, and the deck is shuffled once (refreshProfileRound); unspent SP is lost.
        /// </summary>
        private void FinishPlayerHand()
        {
            var toDiscard = new List<Core2CardInstance>();
            var toExhaust = new List<Core2CardInstance>();
            foreach (var card in Hand)
            {
                if (Core2Json.Strings(Content.Card(card.CardId).Raw["keywords"]).Contains("ethereal")) toExhaust.Add(card);
                else toDiscard.Add(card);
            }
            Hand = new List<Core2CardInstance>();
            foreach (var card in toExhaust)
            {
                Exhaust.Add(card);
                Emit("cardExhausted", new JObject { ["cardInstanceId"] = card.InstanceId, ["cardId"] = card.CardId, ["reason"] = "ethereal" });
            }
            if (Rules.RoundShuffle == "entireDeck")
            {
                Draw.AddRange(toDiscard);
                Draw.AddRange(Discard);
                Discard = new List<Core2CardInstance>();
                Draw = Rng.Shuffle("shuffle", Draw);
                Emit("deckShuffled", new JObject { ["size"] = Draw.Count, ["reason"] = "configuredRoundRefresh" });
            }
            else Discard.AddRange(toDiscard);
            Player.Stamina = 0;
            DrainQueue();
        }

        /// <summary>JS drawCards: draw up to the hand limit; an empty draw pile takes the shuffled discard.</summary>
        public void DrawCards(int n)
        {
            for (var i = 0; i < n; i++)
            {
                if (Hand.Count >= HandMax) return;
                if (Draw.Count == 0)
                {
                    if (Discard.Count == 0) return;
                    Draw.AddRange(Discard);
                    Discard = new List<Core2CardInstance>();
                    Draw = Rng.Shuffle("shuffle", Draw);
                    Emit("deckShuffled", new JObject { ["size"] = Draw.Count });
                }
                var card = Draw[0];
                Draw.RemoveAt(0);
                Hand.Add(card);
                Emit("cardDrawn", new JObject { ["cardInstanceId"] = card.InstanceId, ["cardId"] = card.CardId });
            }
        }

        /// <summary>JS fireOwnerHooks: an entity's status hooks on its own turn start or end (Venom's HP loss).</summary>
        private void FireOwnerHooks(Core2Combatant entity, string hookName)
        {
            if (!entity.Alive) return;
            var ev = new Core2FightEvent { Type = hookName, Data = new JObject { ["ownerId"] = entity.Id } };
            foreach (var statusId in entity.Statuses.Keys.ToList())
            {
                var hooks = StatusHooks(statusId);
                for (var i = 0; i < hooks.Count; i++)
                    if ((string)hooks[i]["on"] == hookName)
                        MaybeFire("status:" + entity.Id + ":" + statusId + ":" + i + ":" + hookName, hooks[i], entity, ev, new JObject { ["kind"] = "status", ["id"] = statusId });
            }
        }
    }
}
