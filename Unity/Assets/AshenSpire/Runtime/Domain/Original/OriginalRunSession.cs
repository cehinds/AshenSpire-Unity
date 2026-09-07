// OriginalRunSession.cs — native, engine-independent run and room coordinator.
// OWNERSHIP: views issue commands; only committed commands mutate this session.
// CONTENT: initialize with the character builder and frozen original/supplemental
// catalogs. Callbacks apply real inventory/equipment/effect rules on draft copies;
// they must not write saves, mutate external state, or silently accept unknown ops.
// SAVE: Snapshot owns content, choices, map, pending room and every RNG counter.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public enum OriginalRunPhase { Map, Combat, Rewards, Shop, Shrine, Event, EventResult, Victory, Defeat, Draft }
    public interface IOriginalRunContent
    {
        void ApplyEffects(JObject run, JArray effects, RandomStreams random);
        bool CollectReward(JObject run, string kind, JObject reward, RandomStreams random);
        bool ApplyService(JObject run, string service, JObject request, RandomStreams random);
    }
    public sealed class OriginalRunSession
    {
        private readonly JObject _content, _supplement;
        private readonly OriginalRunRules _rules;
        private readonly IOriginalRunContent _callbacks;
        private JObject _state;
        private OriginalRunSession(JObject content, JObject supplement, JObject state, IOriginalRunContent callbacks)
        {
            _content = (JObject)content.DeepClone(); _supplement = (JObject)supplement.DeepClone();
            _state = (JObject)state.DeepClone(); _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            _rules = new OriginalRunRules(_content); Validate(_state);
        }
        public static OriginalRunSession Start(OriginalContentCatalog catalog, JObject supplemental, JObject createdPlayer, uint seed, IOriginalRunContent callbacks)
        {
            var data = catalog.Data(); var player = (JObject)createdPlayer.DeepClone();
            var classId = (string)player["classId"] ?? (string)player["class"];
            if (!data["classes"].Any(c => (string)c["id"] == classId)) throw new ArgumentException("Unknown created class");
            if (!(player["attributes"] is JObject) || !(player["loadout"] is JObject) || !(player["deck"] is JArray)) throw new ArgumentException("Character builder must supply attributes, loadout and composed deck");
            player["class"] = classId; player["classId"] = classId;
            foreach (var resource in new[] { "Mana", "Stamina" }) if (player[resource.ToLowerInvariant()] == null) player[resource.ToLowerInvariant()] = player["max" + resource]?.DeepClone() ?? throw new ArgumentException("Missing created resource " + resource);
            player["cinders"] = player["cinders"] ?? data["balance"]["startingCinders"].DeepClone();
            player["relics"] = player["relics"] ?? player["relicIds"]?.DeepClone() ?? new JArray(); player["relicIds"] = player["relics"].DeepClone(); player["flasks"] = player["flasks"] ?? new JArray();
            new ItemUpgradeService(catalog).RestampCards(player);
            player["ownedItemRefs"] = new JArray((player["ownedItemRefs"] as JArray ?? new JArray()).Values<string>().Concat(new ItemUpgradeService(catalog).OwnedRefs(player)).Distinct().OrderBy(id => id,StringComparer.Ordinal));
            player["equipmentAttackSlotCount"] = ((JArray)player["deck"]).Count(card => (string)card["equipmentRole"] == "attack");
            player["history"] = new JArray(); player["path"] = new JArray(); player["lastEncounters"] = new JArray();
            player["fightsWon"] = 0; player["bossesBeaten"] = new JArray();
            player["stats"] = new JObject { ["fightsWon"] = 0, ["damageDealt"] = 0, ["damageTaken"] = 0 };
            player["actNumber"] = 1; player["floor"] = 0; player["mapNodeId"] = null;
            player["seed"] = seed; player["streamCounters"] = JObject.FromObject(new RandomStreams(seed).Snapshot());
            player["phase"] = OriginalRunPhase.Map.ToString(); player["room"] = new JObject();
            var selectedShape = OriginalMapShape.Normalize(player["custom"]?["mapShape"]);
            if (selectedShape != null)
            {
                var limits = supplemental["mapShapeLimits"] as JObject ?? throw new ArgumentException("Import authored map-shape limits before starting a shaped run.");
                OriginalMapShape.ResolveAll((JObject)data["mapConfigs"],selectedShape,limits);
                player["mapShapeLimits"] = limits.DeepClone();
            }
            var rng = new RandomStreams(seed); new OriginalCustomRunRules(data).Initialize(player,rng,callbacks);
            player["mapGraph"] = (string)player["phase"] == "Draft" ? new JObject { ["nodes"] = new JObject(), ["startIds"] = new JArray() } : new OriginalRunRules(data).BuildAct(rng,1,(JArray)player["history"],player["custom"]?["mapShape"] as JObject,player["mapShapeLimits"] as JObject);
            player["streamCounters"] = JObject.FromObject(rng.Snapshot());
            return new OriginalRunSession(data,supplemental,player,callbacks);
        }
        public static OriginalRunSession Restore(JObject snapshot, IOriginalRunContent callbacks)
        {
            if ((int?)snapshot["schemaVersion"] != 1) throw new ArgumentException("Unsupported native run save version");
            return new OriginalRunSession((JObject)snapshot["content"],(JObject)snapshot["supplement"],(JObject)snapshot["run"],callbacks);
        }
        public JObject Snapshot() => new JObject { ["schemaVersion"] = 1, ["content"] = _content.DeepClone(), ["supplement"] = _supplement.DeepClone(), ["run"] = _state.DeepClone() };
        public JObject Player() => (JObject)_state.DeepClone();
        public OriginalContentCatalog Catalog() => new OriginalContentCatalog(_content.ToString());
        public JObject Supplemental() => (JObject)_supplement.DeepClone();
        public JObject Map() => (JObject)_state["mapGraph"].DeepClone();
        public JObject Room() => (JObject)_state["room"].DeepClone();
        public OriginalRunPhase Phase => Enum.Parse<OriginalRunPhase>((string)_state["phase"]);
        public int ActNumber => (int)_state["actNumber"];
        public RandomStreams CreateRandom() => Random(_state);
        private static RandomStreams Random(JObject run) => new RandomStreams((uint)run["seed"],((JObject)run["streamCounters"]).Properties().ToDictionary(p => p.Name,p => (uint)p.Value));
        private static void SetPhase(JObject run, OriginalRunPhase phase) => run["phase"] = phase.ToString();
        private static void Require(JObject run, OriginalRunPhase phase) { if ((string)run["phase"] != phase.ToString()) throw new InvalidOperationException("Command requires " + phase); }
        private static string[] Legal(JObject run)
        {
            if ((string)run["phase"] != "Map") return Array.Empty<string>();
            var current = (string)run["mapNodeId"];
            return (current == null ? (JArray)run["mapGraph"]["startIds"] : (JArray)run["mapGraph"]["nodes"][current]["next"]).Values<string>().ToArray();
        }
        public string[] LegalNodeIds() => Legal(_state);
        public JArray DraftChoices() => Phase == OriginalRunPhase.Draft ? (JArray)_state["room"]["draft"]["offer"].DeepClone() : new JArray();
        public bool PickDraft(string cardId) => Change((run,rng) =>
        {
            Require(run,OriginalRunPhase.Draft); var custom = new OriginalCustomRunRules(_content);
            if (!custom.PickDraft(run,cardId,rng)) return false;
            if ((int)run["room"]["draft"]["round"] == (int)run["room"]["draft"]["rounds"]) { run["mapGraph"] = _rules.BuildAct(rng,1,(JArray)run["history"],run["custom"]?["mapShape"] as JObject,run["mapShapeLimits"] as JObject); MapDone(run); }
            return true;
        });
        private bool Change(Func<JObject,RandomStreams,bool> command)
        {
            var draft = (JObject)_state.DeepClone(); var random = Random(draft);
            if (!command(draft,random)) return false;
            draft["streamCounters"] = JObject.FromObject(random.Snapshot()); Validate(draft); _state = draft; return true;
        }
        private void MapDone(JObject run) { run["room"] = new JObject(); SetPhase(run,OriginalRunPhase.Map); }
        public bool EnterNode(string nodeId) => Change((run,rng) =>
        {
            if (!Legal(run).Contains(nodeId)) return false;
            var node = run["mapGraph"]["nodes"][nodeId]; run["mapNodeId"] = nodeId; run["floor"] = node["floor"].DeepClone(); ((JArray)run["path"]).Add(nodeId);
            var kind = (string)node["type"];
            if (kind == "event")
            {
                var resolved = node["resolved"] ?? throw new InvalidOperationException("Unknown node was not resolved at map birth"); kind = (string)resolved["kind"];
                if (kind == "event") { run["room"] = new JObject { ["eventId"] = resolved["eventId"].DeepClone() }; SetPhase(run,OriginalRunPhase.Event); return true; }
            }
            switch (kind)
            {
                case "monster": case "fight": case "elite": case "boss":
                    var pool = kind == "monster" || kind == "fight" ? "normal" : kind;
                    if (pool == "normal" && OriginalCustomRunRules.Enabled(run,"allElite")) pool = "elite";
                    var encounter = _rules.Encounter(rng,new OriginalCustomRunRules(_content).ContentAct(run),pool,((JArray)run["lastEncounters"]).Values<string>());
                    if (pool == "normal") { var last = (JArray)run["lastEncounters"]; last.Add(encounter); if (last.Count > 2) last.RemoveAt(0); }
                    EnterCombat(run,encounter); break;
                case "merchant": var stock = _rules.Shop(rng,run); stock["smith"] = _rules.SmithServices(rng,"merchant"); run["room"] = stock; SetPhase(run,OriginalRunPhase.Shop); break;
                case "shrine":
                    var charges = new FlaskChargePool((JObject)run["flaskCharges"]); charges.Refill(); run["flaskCharges"] = charges.Snapshot();
                    run["room"] = new JObject { ["smith"] = _rules.SmithServices(rng,"shrine") }; SetPhase(run,OriginalRunPhase.Shrine); break;
                case "treasure": BeginRewards(run,new JObject { ["relicId"] = _rules.Relic(rng,run), ["armamentId"] = Drop(rng,run,"treasure") },"treasure","map"); break;
                default: throw new InvalidOperationException("Unknown authored room kind " + kind);
            }
            return true;
        });
        private void EnterCombat(JObject run,string encounterId)
        {
            var enc = _content["encounters"].First(e => (string)e["id"] == encounterId);
            run["room"] = new JObject { ["encounterId"] = encounterId, ["pool"] = enc["pool"].DeepClone(), ["nodeId"] = run["mapNodeId"].DeepClone() }; SetPhase(run,OriginalRunPhase.Combat);
        }
        private static void AcceptCombatRandom(JObject run,RandomStreams supplied)
        {
            if (supplied.Seed != (uint)run["seed"]) throw new ArgumentException("Combat seed differs from run seed");
            foreach (var pair in Random(run).Snapshot()) if (supplied.Snapshot()[pair.Key] < pair.Value) throw new ArgumentException("Combat RNG counters moved backwards");
            run["streamCounters"] = JObject.FromObject(supplied.Snapshot());
        }
        public void SaveCombat(JObject combatSnapshot,RandomStreams streams)
        {
            var draft = (JObject)_state.DeepClone(); Require(draft,OriginalRunPhase.Combat); AcceptCombatRandom(draft,streams);
            draft["room"]["combatSnapshot"] = combatSnapshot.DeepClone(); Validate(draft); _state = draft;
        }
        public void CompleteCombat(string result,JObject player,RandomStreams streams)
        {
            if (!new[] { "victory", "defeat" }.Contains(result)) throw new ArgumentException("Expected victory or defeat");
            var draft = (JObject)_state.DeepClone(); Require(draft,OriginalRunPhase.Combat); AcceptCombatRandom(draft,streams); var rng = Random(draft);
            foreach (var field in new[] { "hp","maxHp","mana","maxMana","stamina","maxStamina","flasks","flaskCharges","deck","loadout","equipmentPoolDeficits" }) if (player[field] != null) draft[field] = player[field].DeepClone();
            if (!(draft["stats"] is JObject)) draft["stats"] = new JObject { ["fightsWon"] = (int?)draft["fightsWon"] ?? 0 };
            // Caller supplies completed-battle totals, never cumulative run totals.
            // Completion's phase guard prevents the same battle being counted twice.
            foreach (var field in new[] { "damageDealt","damageTaken" })
            {
                var amount = (int?)player[field] ?? 0;
                if (amount < 0) throw new ArgumentException("Negative completed-battle statistic " + field);
                draft["stats"][field] = checked(((int?)draft["stats"][field] ?? 0) + amount);
            }
            if (result == "defeat") { draft["hp"] = 0; SetPhase(draft,OriginalRunPhase.Defeat); }
            else
            {
                var pool = (string)draft["room"]["pool"];
                draft["fightsWon"] = ((int?)draft["fightsWon"] ?? 0) + 1;
                draft["stats"]["fightsWon"] = draft["fightsWon"].DeepClone();
                if (pool == "boss")
                {
                    if (!(draft["bossesBeaten"] is JArray)) draft["bossesBeaten"] = new JArray();
                    var defeated = (JArray)draft["bossesBeaten"];
                    var encounter = _content["encounters"].First(e => (string)e["id"] == (string)draft["room"]["encounterId"]);
                    foreach (var id in encounter["enemies"].Values<string>()) if (!defeated.Values<string>().Contains(id)) defeated.Add(id);
                }
                var stones = (int?)_content["balance"]["smithing"]["rewardByPool"][pool] ?? 0;
                if (stones > 0 && !_callbacks.CollectReward(draft,"smithingStone",new JObject { ["amount"] = stones, ["claimId"] = $"combat:{draft["actNumber"]}:{draft["floor"]}:{draft["mapNodeId"]}:{pool}" },rng)) throw new InvalidOperationException("Smithing reward was refused");
                if (pool == "boss" && (int)draft["actNumber"] == 3 && !OriginalCustomRunRules.Enabled(draft,"endless")) SetPhase(draft,OriginalRunPhase.Victory);
                else
                {
                    string armament = pool == "boss" ? Drop(rng,draft,pool) : null;
                    var rewards = new JObject { ["cinders"] = _rules.Cinders(rng,pool,draft) + (pool == "boss" && armament == null ? (int?)_content["balance"]["equipment"]["drops"]["consolationCinders"] ?? 0 : 0), ["cardIds"] = _rules.Cards(rng,pool,draft) };
                    if (pool != "boss") rewards["flaskId"] = _rules.Flask(rng,draft);
                    rewards["relicId"] = pool == "boss" ? _rules.Relic(rng,draft,"boss") : pool == "elite" ? _rules.Relic(rng,draft) : null;
                    rewards["armamentId"] = pool == "boss" ? armament : Drop(rng,draft,pool);
                    BeginRewards(draft,rewards,pool,pool == "boss" ? "advanceAct" : "map");
                }
            }
            draft["streamCounters"] = JObject.FromObject(rng.Snapshot()); Validate(draft); _state = draft;
        }
        private string Drop(RandomStreams rng,JObject run,string source)
        {
            var carried = ((JObject)run["loadout"]["sets"]).Properties().SelectMany(p => ((JArray)p.Value).Values<string>()).Concat((run["loadout"]["storage"] as JArray ?? new JArray()).Values<string>()).Where(id => id != null);
            return _rules.Armament(rng,source,(run["foundArmaments"] as JArray ?? new JArray()).Values<string>(),carried);
        }
        private static void BeginRewards(JObject run,JObject rewards,string source,string after)
        { run["room"] = new JObject { ["rewards"] = rewards, ["states"] = new JObject(), ["source"] = source, ["after"] = after }; SetPhase(run,OriginalRunPhase.Rewards); }
        private static JObject RewardRow(JObject offer,string kind,string cardId)
        {
            switch (kind)
            {
                case "cinders": return (int?)offer["cinders"] > 0 ? new JObject { ["amount"] = offer["cinders"].DeepClone() } : null;
                case "card": return cardId != null && (offer["cardIds"] as JArray ?? new JArray()).Values<string>().Contains(cardId) ? new JObject { ["cardId"] = cardId } : null;
                case "relic": case "flask": case "armament": return !string.IsNullOrEmpty((string)offer[kind + "Id"]) ? new JObject { [kind + "Id"] = offer[kind + "Id"].DeepClone() } : null;
                default: return null;
            }
        }
        private bool Collect(JObject run,RandomStreams rng,string kind,string cardId)
        {
            if ((string)run["room"]["states"][kind] == "taken") return false;
            var row = RewardRow((JObject)run["room"]["rewards"],kind,cardId);
            if (row == null || !_callbacks.CollectReward(run,kind,row,rng)) return false;
            run["room"]["states"][kind] = "taken"; return true;
        }
        public bool CollectReward(string kind,string cardId = null) => Change((run,rng) => { Require(run,OriginalRunPhase.Rewards); return Collect(run,rng,kind,cardId); });
        public bool SkipReward(string kind) => Change((run,rng) => { Require(run,OriginalRunPhase.Rewards); if ((string)run["room"]["states"][kind] == "taken") return false; if (!new[] { "cinders","card","flask","armament","relic" }.Contains(kind)) return false; run["room"]["states"][kind] = "skipped"; return true; });
        public void ContinueRewards(bool autoCollect) => Change((run,rng) =>
        {
            Require(run,OriginalRunPhase.Rewards);
            if (autoCollect) foreach (var kind in new[] { "cinders","card","flask","armament","relic" })
            {
                if (run["room"]["states"][kind] != null) continue;
                var choices = run["room"]["rewards"]["cardIds"] as JArray; string card = null;
                if (kind == "card" && choices != null && choices.Count > 0) card = (string)choices[choices.Count == 1 ? 0 : rng.Int("cardRewards",0,choices.Count - 1)];
                Collect(run,rng,kind,card);
            }
            if ((string)run["room"]["after"] == "advanceAct")
            {
                run["actNumber"] = checked((int)run["actNumber"] + 1); run["floor"] = 0; run["mapNodeId"] = null; run["path"] = new JArray(); run["lastEncounters"] = new JArray();
                var custom = new OriginalCustomRunRules(_content); run["hp"] = Math.Min((int)run["maxHp"],(int)run["hp"] + (int)Math.Floor(((int)run["maxHp"] - (int)run["hp"]) * custom.HealMultiplier(run)));
                run["mapGraph"] = _rules.BuildAct(rng,custom.ContentAct(run),(JArray)run["history"],run["custom"]?["mapShape"] as JObject,run["mapShapeLimits"] as JObject);
            }
            MapDone(run); return true;
        });
        public JArray EventChoices()
        {
            if (Phase != OriginalRunPhase.Event) return new JArray();
            var choices = _supplement["eventChoices"]?[(string)_state["room"]["eventId"]] as JArray ?? throw new InvalidOperationException("Missing authored event choice identities");
            return new JArray(choices.Where(c => OriginalRunRules.HistoryMet(c["requiresHistory"],(JArray)_state["history"])).Select(c => c.DeepClone()));
        }
        public bool ChooseEvent(string choiceId) => Change((run,rng) =>
        {
            Require(run,OriginalRunPhase.Event); var eventId = (string)run["room"]["eventId"];
            var choices = _supplement["eventChoices"]?[eventId] as JArray ?? throw new InvalidOperationException("Missing authored event choice identities");
            var choice = choices.FirstOrDefault(c => (string)c["id"] == choiceId && OriginalRunRules.HistoryMet(c["requiresHistory"],(JArray)run["history"]));
            if (choice == null || ((int?)choice["requires"]?["cinders"] ?? 0) > (int)run["cinders"]) return false;
            _callbacks.ApplyEffects(run,(JArray)choice["effects"].DeepClone(),rng);
            ((JArray)run["history"]).Add(new JObject { ["kind"] = "eventChoice", ["eventId"] = eventId, ["choiceId"] = choiceId, ["actNumber"] = run["actNumber"].DeepClone(), ["floor"] = run["floor"].DeepClone(), ["mapNodeId"] = run["mapNodeId"].DeepClone() });
            run["room"]["choiceId"] = choiceId; run["room"]["resultText"] = choice["resultText"]?.DeepClone(); SetPhase(run,(int)run["hp"] == 0 ? OriginalRunPhase.Defeat : OriginalRunPhase.EventResult); return true;
        });
        public void LeaveEvent() => Change((run,rng) => { Require(run,OriginalRunPhase.EventResult); if (run["combatEntered"]?.Type == JTokenType.String) { var encounter = (string)run["combatEntered"]; run.Remove("combatEntered"); EnterCombat(run,encounter); } else MapDone(run); return true; });
        public void Rest() => Change((run,rng) => { Require(run,OriginalRunPhase.Shrine); if (!_rules.CanRest(run)) throw new InvalidOperationException("A carried relic prevents resting"); run["hp"] = (int)run["hp"] + _rules.RestHeal(run); run["mana"] = run["maxMana"].DeepClone(); MapDone(run); return true; });
        public void LeaveShop() => Change((run,rng) => { Require(run,OriginalRunPhase.Shop); MapDone(run); return true; });
        // Owner-requested Unity escape route: leaving grants no rest benefit.
        public void LeaveShrine() => Change((run,rng) => { Require(run,OriginalRunPhase.Shrine); MapDone(run); return true; });
        public bool BuyShopItem(string kind,int index) => Change((run,rng) =>
        {
            Require(run,OriginalRunPhase.Shop); var plural = kind == "relic" ? "relics" : kind == "flask" ? "flasks" : kind == "card" ? "cards" : throw new ArgumentException("Unknown shop item kind");
            var items = (JArray)run["room"][plural]; if (index < 0 || index >= items.Count) return false;
            var item = items[index]; var cost = (int)item["cost"]; if ((bool?)item["sold"] == true || cost > (int)run["cinders"]) return false;
            if (!_callbacks.CollectReward(run,kind,new JObject { [kind + "Id"] = item["id"].DeepClone() },rng)) return false;
            run["cinders"] = (int)run["cinders"] - cost; run["room"][plural][index]["sold"] = true; return true;
        });
        public bool Service(string service,JObject request) => Change((run,rng) =>
        {
            if ((string)run["phase"] != "Shrine" && (string)run["phase"] != "Shop") return false;
            if (!_callbacks.ApplyService(run,service,(JObject)request.DeepClone(),rng)) return false;
            if ((string)run["phase"] == "Shrine" && new[] { "upgrade","extract","install" }.Contains(service)) MapDone(run);
            return true;
        });
        public bool UseService(string service,JObject request) => Service(service,request);
        public int OpenedSets(string slotId) => _rules.OpenedSets(_state,slotId);
        public bool Equip(string slotId,int setIndex,string itemId) => EquipmentCommand("equip",new JObject { ["slotId"] = slotId, ["setIndex"] = setIndex, ["itemId"] = itemId });
        public bool SelectSet(string slotId,int index) => EquipmentCommand("selectSet",new JObject { ["slotId"] = slotId, ["setIndex"] = index });
        private bool EquipmentCommand(string command,JObject request) => Change((run,rng) =>
        {
            if (new[] { "Combat","Victory","Defeat","Draft" }.Contains((string)run["phase"])) return false;
            return _callbacks.ApplyService(run,command,request,rng);
        });
        private void Validate(JObject run)
        {
            if (!Enum.TryParse<OriginalRunPhase>((string)run["phase"],out var phase) || (int)run["actNumber"] < 1 || (int)run["actNumber"] > 3 && !OriginalCustomRunRules.Enabled(run,"endless")) throw new ArgumentException("Invalid run phase or act");
            new OriginalCustomRunRules(_content).Validate(run);
            if (run["custom"]?["mapShape"] is JObject savedShape)
            {
                if (!JToken.DeepEquals(run["mapShapeLimits"],_supplement["mapShapeLimits"])) throw new ArgumentException("Saved map limits differ from frozen supplement.");
                var act = new OriginalCustomRunRules(_content).ContentAct(run);
                var config = (JObject)OriginalMapShape.Apply((JObject)_content["mapConfigs"][act.ToString()],savedShape,(JObject)run["mapShapeLimits"])["config"];
                if (phase != OriginalRunPhase.Draft && ((int?)run["mapGraph"]?["floors"] != (int)config["floors"] || (int?)run["mapGraph"]?["columns"] != (int)config["columns"])) throw new ArgumentException("Saved graph dimensions differ from selected map shape.");
            }
            foreach (var pair in new[] { ("hp","maxHp"),("mana","maxMana"),("stamina","maxStamina") })
                if (run[pair.Item1]?.Type != JTokenType.Integer || run[pair.Item2]?.Type != JTokenType.Integer || (int)run[pair.Item1] < 0 || (int)run[pair.Item2] < 0 || (int)run[pair.Item1] > (int)run[pair.Item2]) throw new ArgumentException("Invalid run resource " + pair.Item1);
            if ((int)run["cinders"] < 0 || !(run["room"] is JObject) || !(run["mapGraph"]?["nodes"] is JObject) || !(run["path"] is JArray path)) throw new ArgumentException("Invalid run room, currency or map");
            _ = Random(run); _ = new FlaskChargePool((JObject)run["flaskCharges"]);
            string previous = null;
            foreach (var idToken in path)
            {
                var id = (string)idToken; var legal = previous == null ? (JArray)run["mapGraph"]["startIds"] : (JArray)run["mapGraph"]["nodes"][previous]["next"];
                if (!legal.Values<string>().Contains(id)) throw new ArgumentException("Saved route contains illegal edge"); previous = id;
            }
            if (previous != (string)run["mapNodeId"]) throw new ArgumentException("Saved current node differs from path");
            if (previous != null && (int)run["mapGraph"]["nodes"][previous]["floor"] != (int)run["floor"]) throw new ArgumentException("Saved floor differs from node");
            if (previous == null && ((int)run["floor"] != 0 || phase != OriginalRunPhase.Map && phase != OriginalRunPhase.Draft)) throw new ArgumentException("Active room requires a selected node");
            if (phase == OriginalRunPhase.Combat && string.IsNullOrEmpty((string)run["room"]["encounterId"])) throw new ArgumentException("Missing saved combat encounter");
            if (phase == OriginalRunPhase.Victory && ((int)run["actNumber"] != 3 || OriginalCustomRunRules.Enabled(run,"endless") || (string)run["mapGraph"]["nodes"][previous]["type"] != "boss" || (int)run["hp"] <= 0)) throw new ArgumentException("Victory requires the final boss and a living player");
            if (phase == OriginalRunPhase.Defeat && (int)run["hp"] != 0) throw new ArgumentException("Defeat requires zero HP");
            var knownCards = _content["cards"].Select(c => (string)c["id"]).ToHashSet(); var instanceIds = new HashSet<string>();
            foreach (var card in run["deck"] as JArray ?? throw new ArgumentException("Missing saved deck"))
                if (!(card is JObject) || string.IsNullOrEmpty((string)card["instanceId"]) || !instanceIds.Add((string)card["instanceId"]) || !knownCards.Contains((string)card["cardId"])) throw new ArgumentException("Invalid, duplicate or unknown saved card instance");
            if (run["equipmentAttackSlotCount"] != null && (int)run["equipmentAttackSlotCount"] != ((JArray)run["deck"]).Count(card => (string)card["equipmentRole"] == "attack")) throw new ArgumentException("Saved equipment attack quota differs from deck");
        }
    }
}
