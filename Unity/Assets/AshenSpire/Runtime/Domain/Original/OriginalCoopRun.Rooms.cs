// OriginalCoopRun.Rooms.cs — per-member rewards, services and delayed event choices.
// Offers and event eligibility freeze on entry. Absent members replay their own
// queue with reserved random counters, never another seat's inventory or purse.
// Merchant services reuse the native solo transaction model (the old co-op server
// skipped merchants); shared voting and event rendezvous follow tools/session.mjs.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class OriginalCoopRun
    {
        private void Enqueue(JObject member,string type,JObject value)
        {
            var next = (long)_state["nextCatchupId"] + 1; _state["nextCatchupId"] = next;
            var entry = (JObject)value.DeepClone(); entry["id"] = "catchup" + next; entry["type"] = type;
            entry["act"] = Act; entry["floor"] = _state["floor"].DeepClone(); entry["mapNodeId"] = _state["cursorId"].DeepClone(); Queue(member).Add(entry);
        }
        private void EnterRewards(string pool)
        {
            _state["scene"] = new JObject { ["kind"] = "rewards", ["pool"] = pool, ["afterBoss"] = pool == "boss", ["offers"] = new JObject(), ["done"] = new JObject() };
            var count = Living.Count();
            foreach (var member in Living.ToArray())
            {
                var run = Run(member); var rng = MemberRandom(member); var cards = _rules.Cards(rng,pool,run);
                if (count > 1) { var ids = new[] { "rallyingBanner","sharedFlame","ashOath" }; cards.Add(ids[rng.Int("cardRewards",0,ids.Length - 1)]); }
                var cinders = _rules.Cinders(rng,pool,run); var flask = pool == "boss" ? null : _rules.Flask(rng,run);
                var relic = pool == "boss" ? _rules.Relic(rng,run,"boss") : pool == "elite" ? _rules.Relic(rng,run) : null;
                var offer = new JObject { ["cards"] = cards, ["cinders"] = cinders, ["flaskId"] = flask, ["relicId"] = relic, ["pool"] = pool };
                _content.CollectReward(run,"cinders",new JObject { ["amount"] = cinders },rng);
                var stones = (int?)_data["balance"]["smithing"]["rewardByPool"]?[pool] ?? 0;
                if (stones > 0) _content.CollectReward(run,"smithingStone",new JObject { ["amount"] = stones, ["claimId"] = $"coop:{Act}:{_state["floor"]}:{pool}:{Id(member)}" },rng);
                SaveRandom(member,rng); Stamp(member,"Rewards",offer);
                if ((bool)member["connected"]) Scene["offers"][Id(member)] = offer.DeepClone(); else Enqueue(member,"reward",new JObject { ["offer"] = offer.DeepClone() });
            }
        }
        private void AddCard(JObject member,string cardId)
        {
            _catalog.Record("cards",cardId); var sequence = (int)member["cardSeq"]; var deck = (JArray)Run(member)["deck"]; string instance;
            do { instance = "m" + (int)member["index"] + "c" + sequence++; } while (deck.Any(c => (string)c["instanceId"] == instance));
            deck.Add(new JObject { ["cardId"] = cardId, ["instanceId"] = instance, ["upgraded"] = false }); member["cardSeq"] = sequence;
        }
        private void TakeRelic(JObject member,string relicId,RandomStreams rng,bool boss = false)
        {
            var run = Run(member);
            if (((JArray)run["relics"]).Values<string>().Contains(relicId)) relicId = boss ? _rules.Relic(rng,run,"boss") : _rules.Relic(rng,run);
            if (relicId != null && !_content.CollectReward(run,"relic",new JObject { ["relicId"] = relicId },rng)) throw new InvalidOperationException("Relic reward could not be collected.");
        }
        private void ApplyReward(JObject member,JObject offer,JObject pick)
        {
            var rng = MemberRandom(member); var card = (string)pick["cardId"];
            if (card != null) { if (!((JArray)offer["cards"]).Values<string>().Contains(card)) throw new ArgumentException("Card is not in this member's offer."); AddCard(member,card); }
            if ((bool?)pick["takeRelic"] == true && (string)offer["relicId"] != null) TakeRelic(member,(string)offer["relicId"],rng,(string)offer["pool"] == "boss");
            if ((bool?)pick["flask"] == true && (string)offer["flaskId"] != null && !_content.CollectReward(Run(member),"flask",new JObject { ["flaskId"] = offer["flaskId"].DeepClone() },rng)) throw new InvalidOperationException("No free utility-flask slot.");
            SaveRandom(member,rng);
        }
        private void ChooseReward(JObject member,JObject pick)
        {
            Require("rewards"); var id = Id(member); var offer = Scene["offers"]?[id] as JObject;
            if (offer == null || (bool?)Scene["done"]?[id] == true) throw new InvalidOperationException("This reward is already resolved or belongs to another member.");
            ApplyReward(member,offer,pick); Scene["done"][id] = true;
        }
        private void EnterServices(string kind)
        {
            var shop = kind == "merchant";
            _state["scene"] = new JObject { ["kind"] = shop ? "shop" : "shrine", ["done"] = new JObject(), ["rooms"] = new JObject() };
            foreach (var member in Living)
            {
                var rng = MemberRandom(member); var room = shop ? _rules.Shop(rng,Run(member)) : new JObject(); room["smith"] = _rules.SmithServices(rng,kind);
                if (!shop) { var charges = new FlaskChargePool((JObject)Run(member)["flaskCharges"]); charges.Refill(); Run(member)["flaskCharges"] = charges.Snapshot(); }
                SaveRandom(member,rng); Stamp(member,shop ? "Shop" : "Shrine",room); Scene["rooms"][Id(member)] = room.DeepClone();
            }
        }
        private void Solo(JObject member,Func<OriginalRunSession,bool> action)
        {
            if ((bool?)Scene["done"]?[Id(member)] == true) throw new InvalidOperationException("This member has completed the room.");
            var snapshot = new JObject { ["schemaVersion"] = 1, ["content"] = _data.DeepClone(), ["supplement"] = _supplement.DeepClone(), ["run"] = Run(member).DeepClone() };
            var session = OriginalRunSession.Restore(snapshot,_content); if (!action(session)) throw new InvalidOperationException("The requested transaction is not available.");
            member["run"] = session.Player(); if (Scene["rooms"] is JObject rooms) rooms[Id(member)] = session.Room();
        }
        private void Shrine(JObject member,JObject request)
        {
            Require("shrine"); var id = Id(member); if ((bool?)Scene["done"]?[id] == true) throw new InvalidOperationException("The shrine action has already been used.");
            switch ((string)request["choice"])
            {
                case "rest": Solo(member,s => { s.Rest(); return true; }); break;
                case "leave": break;
                case "mend": var ally = Member((string)request["targetId"]); if (Id(ally) == id || !(bool)ally["alive"]) throw new ArgumentException("Mend requires another living party member."); var run = Run(ally); run["hp"] = Math.Min((int)run["maxHp"],(int)run["hp"] + (int)Math.Ceiling((int)run["maxHp"] * ((double?)_data["balance"]["coop"]?["mendHealPct"] ?? 30) / 100)); break;
                case "reallocate": Solo(member,s => s.UseService("reallocateFlasks",(JObject)request["allocation"])); return;
                case "smith": Solo(member,s => s.UseService("upgrade",(JObject)request["request"] ?? new JObject { ["itemRef"] = request["targetId"]?.DeepClone() })); break;
                default: throw new ArgumentException("Unknown shrine choice.");
            }
            Scene["done"][id] = true;
        }
        private JArray Choices(string eventId,JObject member) => new JArray(((_supplement["eventChoices"]?[eventId] as JArray) ?? throw new ArgumentException("Missing authored event choices.")).Where(c => OriginalRunRules.HistoryMet(c["requiresHistory"],(JArray)Run(member)["history"])).Select(c => c.DeepClone()));
        private void EnterEvent(string eventId)
        {
            _state["scene"] = new JObject { ["kind"] = "event", ["eventId"] = eventId, ["choices"] = new JObject(), ["picks"] = new JObject(), ["results"] = new JObject(), ["done"] = new JObject(), ["ack"] = new JObject() };
            foreach (var member in Living) { Scene["choices"][Id(member)] = Choices(eventId,member); Stamp(member,"Event",new JObject { ["eventId"] = eventId }); }
        }
        private void RecordChoice(JObject member,string eventId,string choiceId,JToken act,JToken floor,JToken node)
        { ((JArray)Run(member)["history"]).Add(new JObject { ["kind"] = "eventChoice", ["eventId"] = eventId, ["choiceId"] = choiceId, ["actNumber"] = act.DeepClone(), ["floor"] = floor.DeepClone(), ["mapNodeId"] = node.DeepClone() }); }
        private JObject ApplyEvent(JObject member,JObject choice,RandomStreams rng,int available)
        {
            if (((int?)choice["requires"]?["cinders"] ?? 0) > available) throw new InvalidOperationException("The event choice is unaffordable.");
            _content.ApplyEffects(Run(member),(JArray)choice["effects"].DeepClone(),rng);
            if ((int)Run(member)["hp"] <= 0) { Run(member)["hp"] = 0; member["alive"] = false; }
            return new JObject { ["choiceId"] = choice["id"].DeepClone(), ["resultText"] = choice["resultText"]?.DeepClone(), ["encounterId"] = Run(member)["combatEntered"]?.DeepClone() };
        }
        private void ChooseEvent(JObject member,string choiceId)
        {
            Require("event"); var id = Id(member); if (Scene["next"] != null || (bool?)Scene["done"]?[id] == true) throw new InvalidOperationException("This event choice is already resolved.");
            var choice = ((JArray)Scene["choices"][id]).OfType<JObject>().FirstOrDefault(c => (string)c["id"] == choiceId) ?? throw new ArgumentException("The event choice is not available to this member.");
            var rng = MemberRandom(member); var result = ApplyEvent(member,choice,rng,(int)Run(member)["cinders"]); SaveRandom(member,rng);
            RecordChoice(member,(string)Scene["eventId"],choiceId,_state["actNumber"],_state["floor"],_state["cursorId"]);
            Scene["picks"][id] = choiceId; Scene["results"][id] = result; Scene["done"][id] = true;
        }
        private void SettleEvent()
        {
            var present = Present.ToArray(); if (present.Length == 0) return;
            if (Scene["next"] == null)
            {
                if (!present.All(m => (bool?)Scene["done"]?[Id(m)] == true)) return;
                var forcing = Living.FirstOrDefault(m => (string)Scene["results"]?[Id(m)]?["encounterId"] != null);
                var encounter = forcing == null ? null : (string)Scene["results"][Id(forcing)]["encounterId"];
                foreach (var absent in Living.Where(m => !(bool)m["connected"] && (bool?)Scene["done"]?[Id(m)] != true))
                {
                    var rng = MemberRandom(absent); var open = ((JArray)Scene["choices"][Id(absent)]).OfType<JObject>().Where(c => !((JArray)c["effects"]).Any(e => (string)e["op"] == "startCombat" && (string)e["encounterId"] != encounter)).ToArray();
                    Enqueue(absent,"event",new JObject { ["eventId"] = Scene["eventId"].DeepClone(), ["open"] = new JArray(open.Select(c => c["id"].DeepClone())), ["rng"] = JObject.FromObject(rng.Snapshot()), ["purse"] = Run(absent)["cinders"].DeepClone() });
                    var reserved = rng.Snapshot().ToDictionary(p => p.Key,p => unchecked(p.Value + 256)); SaveRandom(absent,new RandomStreams(rng.Seed,reserved));
                }
                var canonical = present.FirstOrDefault(m => Scene["picks"]?[Id(m)] != null) ?? Members.OfType<JObject>().FirstOrDefault(m => Scene["picks"]?[Id(m)] != null);
                if (canonical != null) ((JArray)_state["history"]).Add(new JObject { ["kind"] = "eventChoice", ["eventId"] = Scene["eventId"].DeepClone(), ["choiceId"] = Scene["picks"][Id(canonical)].DeepClone(), ["actNumber"] = Act, ["floor"] = _state["floor"].DeepClone(), ["mapNodeId"] = _state["cursorId"].DeepClone() });
                foreach (var m in Members.OfType<JObject>()) Run(m).Remove("combatEntered");
                Scene["next"] = new JObject { ["kind"] = encounter == null ? "advance" : "combat", ["encounterId"] = encounter };
            }
            if (present.All(m => (bool?)Scene["ack"]?[Id(m)] == true)) { var encounter = (string)Scene["next"]["encounterId"]; if (encounter == null) Advance(); else EnterCombat(null,encounter); }
        }
        private void ResolveCatchup(JObject member,string entryId,JObject pick)
        {
            var queue = Queue(member); if (queue.Count == 0 || (string)queue[0]["id"] != entryId) throw new ArgumentException("Only the next catch-up entry can be resolved.");
            var entry = (JObject)queue[0]; var type = (string)entry["type"];
            if (type == "reward") { ApplyReward(member,(JObject)entry["offer"],pick); queue.RemoveAt(0); }
            else if (type == "treasure") { var rng = MemberRandom(member); TakeRelic(member,(string)entry["relicId"],rng); SaveRandom(member,rng); queue.RemoveAt(0); }
            else if (type == "event")
            {
                if (entry["done"] != null) { if ((bool?)pick["continue"] != true) throw new InvalidOperationException("Acknowledge this catch-up result before continuing."); queue.RemoveAt(0); }
                else
                {
                    var id = (string)pick["choiceId"]; if (!((JArray)entry["open"]).Values<string>().Contains(id)) throw new ArgumentException("The choice was not available when this event occurred.");
                    var choice = ((JArray)_supplement["eventChoices"][(string)entry["eventId"]]).OfType<JObject>().First(c => (string)c["id"] == id);
                    var rng = new RandomStreams((uint)Run(member)["seed"],((JObject)entry["rng"]).ToObject<Dictionary<string,uint>>());
                    entry["done"] = ApplyEvent(member,choice,rng,Math.Min((int)Run(member)["cinders"],(int)entry["purse"])); Run(member).Remove("combatEntered");
                    RecordChoice(member,(string)entry["eventId"],id,entry["act"],entry["floor"],entry["mapNodeId"]);
                    if (!(bool)member["alive"]) while (queue.Count > 1) queue.RemoveAt(1);
                }
            }
            else throw new ArgumentException("Unsupported catch-up entry.");
            if (queue.Count == 0 && _combat != null && _combat.Result == null && (bool)member["alive"] && (bool)member["connected"]) _combat.Join(CombatPlayer(member));
        }
    }
}

