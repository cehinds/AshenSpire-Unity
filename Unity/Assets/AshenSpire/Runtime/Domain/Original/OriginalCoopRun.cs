// OriginalCoopRun.cs — authoritative original shared-route run coordinator.
// The host authenticates a seat before Execute; this model owns sequence receipts,
// room transitions, per-seat inventories and real combat. Transport secrets never
// enter public views. Modify authored content for rewards, maps and event choices.
// Save Snapshot on the host; Restore disconnects seats until transport reattaches.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class OriginalCoopRun
    {
        private OriginalContentCatalog _catalog;
        private JObject _data, _supplement, _mechanics, _state;
        private OriginalRunRules _rules;
        private OriginalRunContent _content;
        private RandomStreams _random;
        private OriginalCoopCombat _combat;
        private readonly Dictionary<string,AttributeProgression> _progression = new Dictionary<string,AttributeProgression>();
        private readonly Dictionary<string,JObject> _profiles = new Dictionary<string,JObject>();
        private JObject Scene => (JObject)_state["scene"];
        private JArray Members => (JArray)_state["members"];
        private IEnumerable<JObject> Living => Members.OfType<JObject>().Where(m => (bool)m["alive"]);
        private IEnumerable<JObject> Present => Living.Where(m => (bool)m["connected"]);
        private int Act => (int)_state["actNumber"];
        private int LastAct => (int?)_data["balance"]["endless"]?["actsPerCycle"] ?? 3;
        private int ContentAct => (Act - 1) % LastAct + 1;
        private static JObject Run(JObject member) => (JObject)member["run"];
        private static JArray Queue(JObject member) => (JArray)member["catchup"];
        private static string Id(JToken member) => (string)member["id"];
        private JObject Member(string id) => Members.OfType<JObject>().FirstOrDefault(m => Id(m) == id) ?? throw new ArgumentException("Unknown party member.");
        private static RandomStreams MemberRandom(JObject member) => new RandomStreams((uint)Run(member)["seed"], ((JObject)Run(member)["streamCounters"]).ToObject<Dictionary<string,uint>>());
        private static void SaveRandom(JObject member, RandomStreams random) => Run(member)["streamCounters"] = JObject.FromObject(random.Snapshot());
        private void Require(string kind) { if ((string)Scene["kind"] != kind) throw new InvalidOperationException("Command requires " + kind + "."); }

        public OriginalCoopRun(OriginalContentCatalog catalog, JObject supplement, JObject mechanics, uint seed, bool endless = false)
        {
            Initialize(catalog.Data(),supplement,mechanics);
            _random = new RandomStreams(seed);
            _state = new JObject { ["schemaVersion"] = 1, ["sessionId"] = Guid.NewGuid().ToString("N"), ["seed"] = seed, ["seedString"] = RandomStreams.DisplaySeed(seed), ["endless"] = endless,
                ["started"] = false, ["actNumber"] = 1, ["floor"] = 0, ["cursorId"] = null, ["path"] = new JArray(), ["history"] = new JArray(),
                ["reachableIds"] = new JArray(), ["mapGraph"] = new JObject(), ["scene"] = new JObject { ["kind"] = "lobby" },
                ["members"] = new JArray(), ["refusedMembers"] = new JArray(), ["nextMemberIndex"] = 0, ["nextCatchupId"] = 0 };
        }
        private void Initialize(JObject data,JObject supplement,JObject mechanics)
        {
            _data = (JObject)data.DeepClone(); _catalog = new OriginalContentCatalog(_data.ToString());
            _supplement = (JObject)supplement.DeepClone(); _mechanics = (JObject)mechanics.DeepClone(); _rules = new OriginalRunRules(_data);
            _supplement["mechanics"] = _mechanics.DeepClone();
            _content = new OriginalRunContent(_catalog,reconcile:new OriginalPlayerProjection(_catalog,_mechanics).Reconcile);
        }
        public void AddMember(string id,string name,JObject createdPlayer)
        {
            if ((string)Scene["kind"] == "complete") throw new InvalidOperationException("The completed run is closed to new seats.");
            if (string.IsNullOrWhiteSpace(id) || Members.Any(m => Id(m) == id)) throw new ArgumentException("Member identity must be unique.");
            if (OriginalCustomRunRules.ActiveMods(createdPlayer["custom"] as JObject).Properties().Any(p => (bool)p.Value && p.Name != "endless")) throw new ArgumentException("Only endless is supported in original cooperative runs.");
            var index = (int)_state["nextMemberIndex"]; var seed = unchecked((uint)_state["seed"] ^ ((uint)(index + 1) * 0x9e3779b1u));
            var projectedPlayer = (JObject)createdPlayer.DeepClone(); new OriginalPlayerProjection(_catalog,_mechanics).Reconcile(projectedPlayer);
            var run = OriginalRunSession.Start(_catalog,_supplement,projectedPlayer,seed,_content).Player();
            run["runId"] = (string)_state["sessionId"] + ":" + index;
            if ((string)run["phase"] == "Draft") throw new ArgumentException("Cooperative sealed/draft rules are not supported by the original shared-run mode.");
            if ((bool)_state["endless"]) { if (!(run["custom"] is JObject)) run["custom"] = new JObject(); if (!(run["custom"]["mods"] is JObject)) run["custom"]["mods"] = new JObject(); run["custom"]["mods"]["endless"] = true; }
            // Map initialization is shared. Per-member reward streams begin at zero.
            run["streamCounters"] = JObject.FromObject(new RandomStreams(seed).Snapshot());
            var member = new JObject { ["id"] = id, ["name"] = (name ?? id).Substring(0,Math.Min(18,(name ?? id).Length)), ["index"] = index,
                ["connected"] = true, ["alive"] = true, ["run"] = run, ["catchup"] = new JArray(), ["sequence"] = 0L, ["cardSeq"] = 0 };
            var before = new JObject { ["schemaVersion"] = 1, ["state"] = StateSnapshot() };
            try { Members.Add(member); _state["nextMemberIndex"] = index + 1; if ((bool)_state["started"]) AttachNewMember(member); }
            catch { RestoreState(before,false); throw; }
        }
        private void AttachNewMember(JObject member)
        {
            var id = Id(member); var kind = (string)Scene["kind"];
            if (kind == "combat") { Stamp(member,"Combat",new JObject { ["encounterId"] = Scene["encounterId"].DeepClone(), ["pool"] = Scene["pool"].DeepClone() }); _combat.Join(CombatPlayer(member)); }
            else if (kind == "event") { Scene["choices"][id] = Choices((string)Scene["eventId"],member); Stamp(member,"Event",new JObject { ["eventId"] = Scene["eventId"].DeepClone() }); }
            else if (kind == "shop" || kind == "shrine")
            {
                var rng = MemberRandom(member); var room = kind == "shop" ? _rules.Shop(rng,Run(member)) : new JObject(); room["smith"] = _rules.SmithServices(rng,kind == "shop" ? "merchant" : "shrine"); SaveRandom(member,rng);
                Scene["rooms"][id] = room.DeepClone(); Stamp(member,kind == "shop" ? "Shop" : "Shrine",room);
            }
            else Stamp(member,kind == "rewards" ? "Rewards" : "Map",new JObject());
            // Fresh seats receive no retroactive event history, combat rewards or
            // catch-up claims. Their join index and own RNG are monotonic as source.
        }
        public void SetConnected(string id,bool connected) => SetConnectedMany(new[] { id },connected);
        public void SetConnectedMany(IEnumerable<string> ids,bool connected)
        {
            var members = ids.Distinct().Select(Member).ToArray();
            foreach (var member in members) member["connected"] = connected;
            foreach (var member in members)
            {
                if (!connected && (string)Scene["kind"] == "rewards" && Scene["offers"]?[Id(member)] != null && (bool?)Scene["done"]?[Id(member)] != true)
                { Enqueue(member,"reward",new JObject { ["offer"] = Scene["offers"][Id(member)].DeepClone() }); Scene["done"][Id(member)] = true; }
                if (_combat != null && _combat.Result == null)
                {
                    if (!connected) _combat.Leave(Id(member));
                    else if ((bool)member["alive"] && Queue(member).Count == 0) _combat.Join(CombatPlayer(member));
                }
            }
            Settle();
        }
        public JObject Execute(string authenticatedMemberId,long sequence,JObject intent)
        {
            if (intent == null || intent["type"]?.Type != JTokenType.String) return Failure("A command type is required.",sequence);
            JObject member;
            try { member = Member(authenticatedMemberId); } catch (ArgumentException e) { return Failure(e.Message,sequence); }
            if (!(bool)member["connected"]) return Failure("Member is disconnected.",sequence);
            var previous = (long)member["sequence"];
            if (sequence == previous && JToken.DeepEquals(member["lastIntent"],intent))
            { var duplicate = (JObject)member["lastResult"].DeepClone(); duplicate["duplicate"] = true; return duplicate; }
            if (sequence != previous + 1) return Failure("Expected the next member command sequence.",sequence);
            var before = new JObject { ["schemaVersion"] = 1, ["state"] = StateSnapshot() };
            try
            {
                if (!(bool)member["alive"] && !((string)intent["type"] == "resolveCatchup" && Queue(member).Count > 0)) throw new InvalidOperationException("This member has fallen.");
                if (Queue(member).Count > 0 && (string)intent["type"] != "resolveCatchup") throw new InvalidOperationException("Resolve outstanding catch-up before acting.");
                var events = Dispatch(member,intent); Settle();
                member = Member(authenticatedMemberId); member["sequence"] = sequence; member["lastIntent"] = intent.DeepClone();
                var result = new JObject { ["ok"] = true, ["sequence"] = sequence, ["events"] = events ?? new JArray() };
                member["lastResult"] = result.DeepClone(); return result;
            }
            catch (Exception e)
            { RestoreState(before,false); return Failure(e.Message,sequence); }
        }
        private static JObject Failure(string message,long sequence) => new JObject { ["ok"] = false, ["error"] = message, ["sequence"] = sequence, ["events"] = new JArray() };
        private JArray Dispatch(JObject member,JObject intent)
        {
            var id = Id(member); var type = (string)intent["type"];
            switch (type)
            {
                case "start": Require("lobby"); if (Id(Members.First) != id) throw new ArgumentException("Only the host can start."); if (!Present.Any()) throw new ArgumentException("No connected party."); _state["started"] = true; BuildMap(); break;
                case "chooseNode": Require("map"); var node = (string)intent["nodeId"]; if (!((JArray)_state["reachableIds"]).Values<string>().Contains(node)) throw new ArgumentException("Node is not reachable."); ((JObject)Scene["votes"])[id] = node; break;
                case "playCard": Require("combat"); return _combat.Play(id,(string)intent["cardInstanceId"],(string)intent["targetId"]);
                case "endTurn": Require("combat"); return _combat.EndTurn(id);
                case "useFlask": Require("combat"); return _combat.UseFlask(id,(int)intent["slot"],(string)intent["targetId"],(string)intent["chargeKind"]);
                case "chooseReward": ChooseReward(member,intent); break;
                case "shrineChoice": Shrine(member,intent); break;
                case "buy": Require("shop"); Solo(member,s => s.BuyShopItem((string)intent["kind"],(int)intent["index"])); break;
                case "leaveShop": Require("shop"); Scene["done"][id] = true; break;
                case "eventChoice": ChooseEvent(member,(string)intent["choiceId"]); break;
                case "eventContinue": Require("event"); if (Scene["next"] == null) throw new InvalidOperationException("The party has not finished choosing."); Scene["ack"][id] = true; break;
                case "resolveCatchup": ResolveCatchup(member,(string)intent["entryId"],(JObject)intent["pick"]); break;
                case "equip": OutsideCombat(); Solo(member,s => s.Equip((string)intent["slotId"],(int)intent["setIndex"],(string)intent["itemId"])); break;
                case "selectSet": OutsideCombat(); Solo(member,s => s.SelectSet((string)intent["slotId"],(int)intent["setIndex"])); break;
                case "service": if (!new[] { "shop","shrine" }.Contains((string)Scene["kind"])) throw new InvalidOperationException("Service requires a shop or shrine."); Solo(member,s => s.UseService((string)intent["service"],(JObject)intent["request"] ?? new JObject())); if ((string)Run(member)["phase"] == "Map" && (string)Scene["kind"] == "shrine") Scene["done"][id] = true; break;
                default: throw new ArgumentException("Unknown cooperative command.");
            }
            return new JArray();
        }
        private void OutsideCombat() { if (!new[] { "map","shop","shrine","event","rewards" }.Contains((string)Scene["kind"])) throw new InvalidOperationException("Equipment change is unavailable in this room."); }
        private void BuildMap()
        {
            _state["mapGraph"] = _rules.BuildAct(_random,ContentAct,(JArray)_state["history"]); _state["floor"] = 0; _state["cursorId"] = null; _state["path"] = new JArray();
            _state["reachableIds"] = _state["mapGraph"]["startIds"].DeepClone(); _state["scene"] = new JObject { ["kind"] = "map", ["votes"] = new JObject() };
            foreach (var member in Living) Stamp(member,"Map",new JObject());
        }
        private void Stamp(JObject member,string phase,JObject room)
        {
            var run = Run(member); run["actNumber"] = Act; run["floor"] = _state["floor"].DeepClone(); run["mapNodeId"] = _state["cursorId"].DeepClone();
            run["mapGraph"] = _state["mapGraph"].DeepClone(); run["path"] = _state["path"].DeepClone(); run["phase"] = phase; run["room"] = room.DeepClone();
        }
        private void Travel(string nodeId)
        {
            var node = (JObject)_state["mapGraph"]["nodes"][nodeId]; _state["cursorId"] = nodeId; _state["floor"] = node["floor"].DeepClone(); ((JArray)_state["path"]).Add(nodeId);
            var kind = (string)node["type"]; var resolved = node["resolved"] as JObject;
            if (kind == "event") kind = (string)resolved["kind"];
            if (new[] { "monster","fight","elite","boss" }.Contains(kind)) EnterCombat(kind == "monster" || kind == "fight" ? "normal" : kind);
            else if (kind == "event") EnterEvent((string)resolved["eventId"]);
            else if (kind == "treasure") { foreach (var m in Living.ToArray()) { var rng = MemberRandom(m); var relic = _rules.Relic(rng,Run(m)); SaveRandom(m,rng); if (relic != null) { if ((bool)m["connected"]) TakeRelic(m,relic,rng); else Enqueue(m,"treasure",new JObject { ["relicId"] = relic }); } } Advance(); }
            else if (kind == "merchant" || kind == "shrine") EnterServices(kind);
            else throw new NotSupportedException("Unsupported map node " + kind);
        }
        private void Advance()
        {
            var node = _state["mapGraph"]["nodes"][(string)_state["cursorId"]]; var next = node["next"] as JArray ?? new JArray();
            _state["reachableIds"] = next.Count > 0 ? next.DeepClone() : new JArray(_state["mapGraph"]["bossId"].DeepClone());
            _state["scene"] = new JObject { ["kind"] = "map", ["votes"] = new JObject() };
            foreach (var m in Living) Stamp(m,"Map",new JObject());
        }
        private void NextAct()
        {
            if (!(bool)_state["endless"] && Act >= LastAct) { Finish("victory"); return; }
            _state["actNumber"] = Act + 1; foreach (var m in Living) { var run = Run(m); run["hp"] = run["maxHp"].DeepClone(); run["mana"] = run["maxMana"].DeepClone(); } BuildMap();
        }
        private void Finish(string result)
        {
            _state["scene"] = new JObject { ["kind"] = "complete", ["result"] = result };
            foreach (var m in Members.OfType<JObject>()) { Run(m)["phase"] = result == "victory" ? "Victory" : "Defeat"; Run(m)["result"] = result; if (result == "defeat") { m["alive"] = false; Run(m)["hp"] = 0; Queue(m).Clear(); } }
        }
        private void Settle()
        {
            if (!(bool)_state["started"]) return;
            if (!Living.Any()) { Finish("defeat"); return; }
            if ((string)Scene["kind"] == "combat" && _combat.Result != null) SettleCombat();
            if ((string)Scene["kind"] == "map")
            {
                var voters = Present.ToArray(); var votes = (JObject)Scene["votes"];
                if (voters.Length > 0 && voters.All(m => votes[Id(m)] != null))
                { var counts = voters.GroupBy(m => (string)votes[Id(m)]).ToDictionary(g => g.Key,g => g.Count()); var max = counts.Values.Max(); Travel((string)votes[Id(voters.First(m => counts[(string)votes[Id(m)]] == max))]); }
            }
            if (new[] { "rewards","shrine","shop" }.Contains((string)Scene["kind"]) && Present.Any())
            {
                var relevant = Present.Where(m => (string)Scene["kind"] != "rewards" || Scene["offers"]?[Id(m)] != null).ToArray();
                if (relevant.All(m => (bool?)Scene["done"]?[Id(m)] == true)) { if ((string)Scene["kind"] == "rewards" && (bool?)Scene["afterBoss"] == true) NextAct(); else Advance(); }
            }
            if ((string)Scene["kind"] == "event") SettleEvent();
            if ((string)Scene["kind"] == "combat" && _combat.Result != null) SettleCombat();
        }
        private JObject Resolve(string memberId,JObject instance)
        {
            var run = Run(Member(memberId));
            if (!_progression.TryGetValue(memberId,out var rules)) { rules = new AttributeProgression((JObject)run["progression"]); _progression[memberId] = rules; _profiles[memberId] = rules.BaselineProfiles(_catalog); }
            var projection = new WeaponCardProjection(_catalog).Resolve(instance,(JObject)run["loadout"],(string)run["classId"],(JObject)run["attributes"],_profiles[memberId]);
            return (JObject)rules.ResolveCard(projection,(JObject)run["attributes"],_catalog)["card"];
        }
        private JObject CombatPlayer(JObject member)
        {
            var player = (JObject)Run(member).DeepClone(); player["id"] = Id(member); player["name"] = member["name"].DeepClone();
            player["energyMax"] = player["energy"].DeepClone(); player["drawPerTurn"] = player["draw"].DeepClone(); player["poiseMax"] = player["poiseThreshold"]?.DeepClone() ?? new JValue(0); return player;
        }
        private void EnterCombat(string pool,string forcedEncounter = null)
        {
            var fighters = Present.Where(m => Queue(m).Count == 0).ToArray(); if (fighters.Length == 0) throw new InvalidOperationException("No party member is ready to enter combat.");
            var encounterId = forcedEncounter ?? _rules.Encounter(_random,ContentAct,pool,Array.Empty<string>()); var encounter = _catalog.Record("encounters",encounterId); pool = (string)encounter["pool"];
            _state["scene"] = new JObject { ["kind"] = "combat", ["encounterId"] = encounterId, ["pool"] = pool };
            foreach (var m in Living) { new OriginalPlayerProjection(_catalog,_mechanics).Reconcile(Run(m)); Stamp(m,"Combat",new JObject { ["encounterId"] = encounterId, ["pool"] = pool }); }
            var loop = (Act - 1) / LastAct; var endless = _data["balance"]["endless"]; var strength = ((int?)endless?["strPerLoop"] ?? 0) * loop;
            _combat = new OriginalCoopCombat(_catalog,_mechanics,_random,fighters.Select(CombatPlayer),encounter["enemies"].Values<string>(),Resolve,
                1 + ((double?)endless?["hpPerLoop"] ?? 0) * loop,strength > 0 ? new JArray(new JObject { ["status"] = "strength", ["stacks"] = strength }) : null,true);
        }
        private void SettleCombat()
        {
            var pool = (string)Scene["pool"]; var outcome = _combat.Outcome(); var battle = _combat.Snapshot(); var result = _combat.Result;
            _random = new RandomStreams((uint)battle["seed"],((JObject)battle["rng"]).ToObject<Dictionary<string,uint>>());
            foreach (var seat in _combat.Players.OfType<JObject>())
            {
                var m = Member(Id(seat)); var run = Run(m); var entity = (JObject)seat["entity"];
                foreach (var key in new[] { "hp","mana","stamina","flasks","flaskCharges","resourceDeficits" }) if (entity[key] != null) run[key] = entity[key].DeepClone();
                if (result == "victory" && (int)run["hp"] <= 0) run["hp"] = (int?)_data["balance"]["coop"]?["reviveHp"] ?? 1;
                if (result == "defeat" && (int)run["hp"] <= 0) m["alive"] = false;
                var events = (JArray)battle["events"];
                run["stats"]["damageDealt"] = (int)run["stats"]["damageDealt"] + events.Where(e => (string)e["type"] == "damageDealt" && (string)e["sourceId"] == "player" && (string)e["actorMemberId"] == Id(m)).Sum(e => (int)e["amount"]);
                run["stats"]["damageTaken"] = (int)run["stats"]["damageTaken"] + events.Where(e => (string)e["type"] == "hpLost" && (string)e["targetId"] == "player" && (string)e["playerId"] == Id(m)).Sum(e => (int)e["amount"]);
            }
            _combat = null;
            if (result == "defeat") { Finish("defeat"); return; }
            var encounter = _catalog.Record("encounters",(string)Scene["encounterId"]);
            foreach (var member in Living)
            {
                var run = Run(member); run["fightsWon"] = (int)run["fightsWon"] + 1; run["stats"]["fightsWon"] = run["fightsWon"].DeepClone();
                if (pool == "boss") foreach (var enemy in encounter["enemies"].Values<string>()) if (!((JArray)run["bossesBeaten"]).Values<string>().Contains(enemy)) ((JArray)run["bossesBeaten"]).Add(enemy);
            }
            EnterRewards(pool);
        }
        public JObject Snapshot()
        {
            return new JObject { ["schemaVersion"] = 1, ["content"] = _data.DeepClone(), ["supplement"] = _supplement.DeepClone(), ["mechanics"] = _mechanics.DeepClone(), ["state"] = StateSnapshot() };
        }
        private JObject StateSnapshot() { var state = (JObject)_state.DeepClone(); var combat = _combat?.Snapshot(); state["sharedRng"] = combat?["rng"].DeepClone() ?? JObject.FromObject(_random.Snapshot()); state["combat"] = combat; return state; }
        public static OriginalCoopRun Restore(JObject snapshot,bool disconnectMembers = true)
        {
            var restored = new OriginalCoopRun(new OriginalContentCatalog(((JObject)snapshot["content"]).ToString()),(JObject)snapshot["supplement"],(JObject)snapshot["mechanics"],(uint)snapshot["state"]["seed"]);
            restored.RestoreState(snapshot,false); restored.ValidateSavedMembers();
            if (disconnectMembers)
            {
                if (restored._combat != null) { foreach (var member in restored.Members) member["connected"] = false; restored._combat.DisconnectForHostRestore(); }
                else restored.SetConnectedMany(restored.Members.Select(Id).ToArray(),false);
            }
            return restored;
        }
        private void ValidateSavedMembers()
        {
            foreach (var member in Members.OfType<JObject>().ToArray())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(Id(member)) || member["connected"]?.Type != JTokenType.Boolean || member["alive"]?.Type != JTokenType.Boolean || (long?)member["sequence"] < 0 || !(member["catchup"] is JArray)) throw new ArgumentException("Invalid member ledger.");
                    OriginalRunSession.Restore(new JObject { ["schemaVersion"] = 1, ["content"] = _data, ["supplement"] = _supplement, ["run"] = Run(member) },_content);
                    new OriginalStartingOptions(_catalog).ValidateSaved(Run(member),Run(member)["profileMeta"] as JObject);
                    var cards = (JArray)Run(member)["deck"]; if (cards.Select(c => (string)c["instanceId"]).Distinct().Count() != cards.Count) throw new ArgumentException("Duplicate owned card instance.");
                }
                catch (Exception error)
                {
                    // A combat save binds all seats into one shared turn. Refuse it
                    // whole if corrupt; do not silently remove an active fighter.
                    if (_combat != null) throw new ArgumentException("Saved combat member is invalid: " + Id(member) + ": " + error.Message);
                    ((JArray)_state["refusedMembers"]).Add(new JObject { ["id"] = Id(member), ["name"] = member["name"]?.DeepClone(), ["reason"] = error.Message, ["raw"] = member.DeepClone() }); member.Remove();
                }
            }
            if (Members.Count == 0) throw new ArgumentException("No valid party member could be restored.");
        }
        private void RestoreState(JObject snapshot,bool disconnect)
        {
            if ((int?)snapshot["schemaVersion"] != 1 || (int?)snapshot["state"]?["schemaVersion"] != 1) throw new ArgumentException("Unsupported cooperative save version.");
            _state = (JObject)snapshot["state"].DeepClone(); _random = new RandomStreams((uint)_state["seed"],((JObject)_state["sharedRng"]).ToObject<Dictionary<string,uint>>());
            _progression.Clear(); _profiles.Clear();
            if (Members.Select(Id).Distinct().Count() != Members.Count) throw new ArgumentException("Duplicate saved seat identity.");
            _combat = _state["combat"] is JObject combat ? OriginalCoopCombat.Restore(_catalog,_mechanics,combat,Resolve) : null;
            if ((string)Scene["kind"] == "combat" && _combat == null) throw new ArgumentException("Combat save is missing its real simulation.");
            if (disconnect) SetConnectedMany(Members.Select(Id).ToArray(),false);
        }
        public JObject View() => View(null);
        public JObject View(string memberId)
        {
            var map = (JObject)_state["mapGraph"].DeepClone(); if (map["nodes"] is JObject nodes) foreach (var node in nodes.Properties()) ((JObject)node.Value).Remove("resolved");
            var scene = (JObject)Scene.DeepClone();
            foreach (var key in new[] { "offers","choices","rooms" }) if (scene[key] is JObject privateRows) foreach (var property in privateRows.Properties().ToArray()) if (property.Name != memberId) property.Remove();
            if (_combat != null) { scene["players"] = _combat.Players; foreach (var row in (JArray)scene["players"]) ((JObject)row).Remove("piles"); scene["enemies"] = _combat.Enemies; scene["turn"] = _combat.Turn; scene["phase"] = _combat.Phase; }
            var party = new JArray(Members.OfType<JObject>().Select(m => new JObject { ["id"] = Id(m), ["name"] = m["name"].DeepClone(), ["index"] = m["index"].DeepClone(), ["connected"] = m["connected"].DeepClone(), ["alive"] = m["alive"].DeepClone(), ["classId"] = Run(m)["classId"].DeepClone(), ["hp"] = Run(m)["hp"].DeepClone(), ["maxHp"] = Run(m)["maxHp"].DeepClone(), ["catchupCount"] = Queue(m).Count, ["sequence"] = m["sequence"].DeepClone() }));
            var view = new JObject { ["schemaVersion"] = 1, ["seed"] = _state["seed"].DeepClone(), ["seedString"] = _state["seedString"].DeepClone(), ["endless"] = _state["endless"].DeepClone(), ["scene"] = scene, ["actNumber"] = Act, ["floor"] = _state["floor"].DeepClone(), ["cursorId"] = _state["cursorId"].DeepClone(), ["reachableIds"] = _state["reachableIds"].DeepClone(), ["map"] = map, ["party"] = party };
            view["refusedMembers"] = new JArray(((JArray)_state["refusedMembers"]).Select(m => new JObject { ["id"] = m["id"]?.DeepClone(), ["name"] = m["name"]?.DeepClone(), ["reason"] = m["reason"]?.DeepClone() }));
            if (memberId != null)
            {
                var member = Member(memberId); var run = (JObject)Run(member).DeepClone(); foreach (var key in new[] { "mapGraph","streamCounters","progression","playerProjectionRules","room" }) run.Remove(key);
                var catchup = (JArray)Queue(member).DeepClone(); foreach (JObject entry in catchup) entry.Remove("rng");
                var local = new JObject { ["id"] = memberId, ["run"] = run, ["catchup"] = catchup, ["sequence"] = member["sequence"].DeepClone() };
                local["deck"] = new JArray(((JArray)Run(member)["deck"]).OfType<JObject>().Select(instance => new JObject { ["instance"] = instance.DeepClone(), ["card"] = Resolve(memberId,instance) }));
                if (_combat != null)
                {
                    var seat = _combat.Players.OfType<JObject>().FirstOrDefault(s => Id(s) == memberId);
                    if (seat != null) { local["combat"] = seat.DeepClone(); local["hand"] = new JArray(((JArray)seat["piles"]["hand"]).OfType<JObject>().Select(card => new JObject { ["instance"] = card.DeepClone(), ["card"] = _combat.Card(memberId,card), ["cost"] = _combat.Cost(memberId,card), ["targets"] = _combat.FriendlyTargets(memberId,_combat.Card(memberId,card)) })); }
                }
                local["room"] = Run(member)["room"].DeepClone(); view["local"] = local;
            }
            return view;
        }
    }
}

