// OriginalRunContent.cs — native basic ownership and authored run-effect handlers.
// Currency, ordinary cards, relics, utility flasks and armament storage are owned
// here. Item upgrading/equipping needs the supplied equipment transaction service;
// an unavailable service throws by name instead of spending for a silent no-op.
// Call only through OriginalRunSession, which commits a copy after validation.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalRunContent : IOriginalRunContent
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _data;
        private readonly Func<JObject,string,JObject,RandomStreams,bool> _equipmentService;
        private readonly Action<JObject> _reconcile;
        public OriginalRunContent(OriginalContentCatalog catalog, Func<JObject,string,JObject,RandomStreams,bool> equipmentService = null, Action<JObject> reconcile = null)
        { _catalog = catalog; _data = catalog.Data(); _equipmentService = equipmentService; _reconcile = reconcile; }
        private static JArray Array(JObject run,string key) => run[key] as JArray ?? throw new ArgumentException("Missing inventory " + key);
        private static bool Composed(JToken card) => !string.IsNullOrEmpty((string)card["grantedBy"]) || !string.IsNullOrEmpty((string)card["equipmentAttackSlotId"]);
        private static string NewCardId(JObject run)
        {
            var next = (int?)run["nextCardInstance"] ?? 0; string id;
            do { id = "nativeRun" + ++next; } while (Array(run,"deck").Any(c => (string)c["instanceId"] == id));
            run["nextCardInstance"] = next; return id;
        }
        private void AddCard(JObject run,string id)
        { _catalog.Record("cards",id); Array(run,"deck").Add(new JObject { ["instanceId"] = NewCardId(run), ["cardId"] = id, ["upgraded"] = false }); }
        public bool CollectReward(JObject run,string kind,JObject reward,RandomStreams rng)
        {
            if (OriginalRewardAvailability.Refusal(_data, run, kind, (string)reward[kind + "Id"]) != null) return false;
            switch (kind)
            {
                case "cinders": var amount = (int)reward["amount"]; if (amount < 0) throw new ArgumentException("Negative reward"); run["cinders"] = checked((int)run["cinders"] + amount); return true;
                case "card": AddCard(run,(string)reward["cardId"]); return true;
                case "flask":
                    var flask = (string)reward["flaskId"]; _catalog.Record("flasks",flask);
                    Array(run,"flasks").Add(new JObject { ["flaskId"] = flask }); return true;
                case "relic":
                    var relic = (string)reward["relicId"]; _catalog.Record("relics",relic);
                    Array(run,"relics").Add(relic); run["relicIds"] = run["relics"].DeepClone(); SyncFlaskGrowth(run); _reconcile?.Invoke(run); return true;
                case "armament":
                    var armament = (string)reward["armamentId"]; _catalog.Record("equipment.armaments",armament);
                    var loadout = (JObject)run["loadout"]; var storage = (JArray)loadout["storage"];
                    storage.Add(armament);
                    if (!(run["foundArmaments"] is JArray)) run["foundArmaments"] = new JArray();
                    if (!((JArray)run["foundArmaments"]).Values<string>().Contains(armament)) ((JArray)run["foundArmaments"]).Add(armament);
                    if (run["ownedItemRefs"] is JArray owned && !owned.Values<string>().Contains("armament/" + armament)) owned.Add("armament/" + armament);
                    return true;
                case "smithingStone":
                    var claim = (string)reward["claimId"] ?? throw new ArgumentException("Smithing grant requires a durable claim");
                    if (!(run["smithingRewardClaims"] is JArray)) run["smithingRewardClaims"] = new JArray();
                    var claims = (JArray)run["smithingRewardClaims"]; if (claims.Values<string>().Contains(claim)) return true;
                    var stones = (int)reward["amount"]; if (stones <= 0) throw new ArgumentException("Invalid smithing grant");
                    run["smithingStones"] = checked(((int?)run["smithingStones"] ?? 0) + stones); claims.Add(claim); return true;
                default: throw new NotSupportedException("Unknown reward kind " + kind);
            }
        }
        private bool Predicate(JToken condition,RandomStreams rng)
        {
            if (condition == null) return true;
            switch ((string)condition["p"])
            {
                case "random": return rng.Float("misc") * 100 < (double)condition["pct"];
                case "all": return ((JArray)condition["preds"]).All(p => Predicate(p,rng));
                case "any": return ((JArray)condition["preds"]).Any(p => Predicate(p,rng));
                case "not": return !Predicate(condition["pred"],rng);
                default: throw new NotSupportedException("Unsupported run predicate " + condition["p"]);
            }
        }
        public void ApplyEffects(JObject run,JArray effects,RandomStreams rng)
        {
            // The original run facade snapshots max HP once for this effect list.
            var facade = new JObject { ["hp"] = run["hp"].DeepClone(), ["maxHp"] = run["maxHp"].DeepClone(), ["block"] = 0, ["statuses"] = new JObject() };
            int Number(JToken value) => FormulaEvaluator.Evaluate(value,new JObject { ["entities"] = new JObject { ["self"] = facade.DeepClone(), ["owner"] = facade.DeepClone(), ["target"] = facade.DeepClone() } });
            void ApplyOwnership(Action change)
            {
                // A resource projection may replace the entire run. Carry pending
                // event wounds into that projection and retain its HP adjustment,
                // while formulas keep the original facade's max-HP snapshot.
                if (_reconcile != null) run["hp"] = Math.Min((int)facade["hp"],(int)run["maxHp"]);
                var beforeHp = (int)run["hp"];
                change();
                facade["hp"] = (int)facade["hp"] + (int)run["hp"] - beforeHp;
            }
            foreach (JObject effect in effects)
            {
                if (!Predicate(effect["if"],rng)) continue;
                switch ((string)effect["op"])
                {
                    case "damage": case "loseHp": facade["hp"] = Math.Max(0,(int)facade["hp"] - Number(effect["amount"])); break;
                    case "heal": facade["hp"] = Math.Min((int)facade["maxHp"],(int)facade["hp"] + Math.Max(0,Number(effect["amount"]))); break;
                    case "restoreMana": run["mana"] = Math.Min((int)run["maxMana"],(int)run["mana"] + Math.Max(0,Number(effect["amount"]))); break;
                    case "addCinders": run["cinders"] = Math.Max(0,checked((int)run["cinders"] + Number(effect["amount"]))); break;
                    case "addCardToDeck": AddCard(run,(string)effect["card"]); break;
                    case "removeCardFromDeck":
                        var candidates = Array(run,"deck").Where(card => !Composed(card) && (effect["card"] == null || (string)card["cardId"] == (string)effect["card"])).ToList();
                        if (candidates.Count > 0) { var selected = (bool?)effect["random"] == true ? rng.Int("misc",0,candidates.Count-1) : 0; candidates[selected].Remove(); }
                        break;
                    case "upgradeCard":
                        ApplyOwnership(() => UpgradeCard(run,effect,rng));
                        break;
                    case "addRelic":
                        var relic = (string)effect["id"];
                        if (relic == null && (bool?)effect["random"] == true)
                        {
                            var pool = _catalog.Table("relics").Where(r => !Array(run,"relics").Values<string>().Contains((string)r["id"]) && ((string)r["pool"] ?? "reward") == "reward").Select(r => (string)r["id"]).ToArray();
                            if (pool.Length > 0) relic = pool[rng.Int("relicRewards",0,pool.Length-1)];
                        }
                        if (relic != null) ApplyOwnership(() => CollectReward(run,"relic",new JObject { ["relicId"] = relic },rng)); break;
                    case "addFlask":
                        if (Array(run,"flasks").Count >= (int)_data["balance"]["flaskSlots"]) break;
                        var id = (string)effect["id"];
                        if (id == null && (bool?)effect["random"] == true) { var pool = _catalog.Table("flasks"); if (pool.Count > 0) id = (string)pool[rng.Int("flaskRewards",0,pool.Count-1)]["id"]; }
                        if (id != null) CollectReward(run,"flask",new JObject { ["flaskId"] = id },rng); break;
                    case "addFlaskCapacity":
                        var kind = (string)effect["kind"]; var amount = (int)effect["amount"];
                        if (!new[] { "hp","mana" }.Contains(kind) || amount <= 0) throw new ArgumentException("Invalid charge capacity grant");
                        var f = (JObject)run["flaskCharges"]; foreach (var key in new[] { "capacity",kind,kind + "Current","granted" }) f[key] = (int)f[key] + amount; break;
                    case "loseMaxHpPct":
                        var before = (int)run["maxHp"]; run["maxHp"] = Math.Max(1,(int)Math.Floor(before * (1 - Number(effect["pct"]) / 100d)));
                        run["maxHpAdjustment"] = ((int?)run["maxHpAdjustment"] ?? 0) + (int)run["maxHp"] - before; break;
                    case "startCombat": var encounter = (string)effect["encounterId"]; _catalog.Record("encounters",encounter); run["combatEntered"] = encounter; break;
                    default: throw new NotSupportedException("Unsupported authored run operation " + effect["op"]);
                }
            }
            run["hp"] = Math.Min((int)facade["hp"],(int)run["maxHp"]);
        }
        public bool ApplyService(JObject run,string service,JObject request,RandomStreams rng)
        {
            if (service == "levelUp" || service == "sell") return new OriginalRunServices(_catalog, _reconcile, SyncFlaskGrowth).Apply(run, service, request);
            if (service == "equip" || service == "selectSet") return ChangeEquipment(run,service,request);
            if (new[] { "upgrade", "extract", "install" }.Contains(service))
            {
                if ((bool?)run["room"]["smith"]?["offered"] != true || !(run["room"]["smith"]?["services"] as JArray ?? new JArray()).Values<string>().Contains(service)) return false;
                JObject result;
                if (service == "upgrade") result = new ItemUpgradeService(_catalog).Commit(run,(string)request["itemRef"]);
                else if (service == "extract") result = new CardMountService(_catalog).Extract(run,(string)request["itemRef"],(string)request["mountKey"]);
                else result = new CardMountService(_catalog).Install(run,(string)request["itemRef"],(string)request["mountKey"],(string)request["instanceId"]);
                Replace(run,(JObject)result["run"]); _reconcile?.Invoke(run); return true;
            }
            if (service == "reallocateFlasks")
            {
                if ((string)run["phase"] != "Shrine") return false;
                var pool = new FlaskChargePool((JObject)run["flaskCharges"]); pool.Reallocate((int)request["hp"],(int)request["mana"]); run["flaskCharges"] = pool.Snapshot(); return true;
            }
            if (service == "removeCard")
            {
                if ((string)run["phase"] != "Shop" || Array(run,"deck").Count <= 1) return false;
                var card = Array(run,"deck").FirstOrDefault(c => (string)c["instanceId"] == (string)request["instanceId"] && !Composed(c));
                var cost = (int)run["room"]["removeCost"]; if (card == null || cost > (int)run["cinders"]) return false;
                card.Remove(); run["cinders"] = (int)run["cinders"] - cost; run["removesPurchased"] = ((int?)run["removesPurchased"] ?? 0) + 1;
                run["room"]["removeCost"] = (int)_data["balance"]["shop"]["removeBase"] + (int)_data["balance"]["shop"]["removeStep"] * (int)run["removesPurchased"]; return true;
            }
            if (_equipmentService == null) throw new NotSupportedException("Service requires a registered transaction: " + service);
            return _equipmentService(run,service,request,rng);
        }
        private bool ChangeEquipment(JObject run,string command,JObject request)
        {
            if (!new[] { "Map","Rewards","Shop","Shrine","Event","EventResult" }.Contains((string)run["phase"])) return false;
            var slotId = (string)request["slotId"]; var index = (int)request["setIndex"]; var slot = _catalog.Record("equipment.slots",slotId);
            var rules = new OriginalRunRules(_data);
            if (index < 0 || index >= rules.OpenedSets(run,slotId)) return false;
            var locations = new WeaponLoadout(_catalog); var upgrades = new ItemUpgradeService(_catalog);
            var classId = (string)run["class"]; var previous = (JObject)run["loadout"]; JObject next,receipt;
            var owned = (run["ownedItemRefs"] as JArray ?? new JArray()).Values<string>().Concat(upgrades.OwnedRefs(run)).ToHashSet();
            foreach (var itemRef in owned) upgrades.Definition(itemRef);
            if (command == "equip")
            {
                var itemId = (string)request["itemId"]; if ((string)previous["sets"][slotId][index] == itemId) return false;
                receipt = locations.Equip(previous,classId,slotId,index,itemId,owned,(JObject)run["attributes"],false,run["itemUpgradeLevels"] as JObject);
                if ((bool?)receipt["ok"] != true) return false;
                next = (JObject)receipt["loadout"];
            }
            else
            {
                if ((int)previous["active"][slotId] == index) return false;
                next = (JObject)previous.DeepClone(); next["active"][slotId] = index;
                var piece = locations.Equipped(next,classId,slotId);
                if (piece != null && !(bool)locations.RequirementReceipt(piece,(JObject)run["attributes"],run["itemUpgradeLevels"] as JObject)["ok"]) return false;
                receipt = new JObject { ["ok"] = true, ["slotId"] = slotId, ["setIndex"] = index, ["previousSet"] = previous["active"][slotId].DeepClone() };
            }
            if (_reconcile == null) throw new NotSupportedException("Equipment changes require the player resource/weight reconciliation callback");
            var composer = new WeaponCardComposer(_catalog); var beforeDeck = (JArray)run["deck"];
            var quota = (int?)run["equipmentAttackSlotCount"] ?? beforeDeck.Count(card => (string)card["equipmentRole"] == "attack");
            if (quota != beforeDeck.Count(card => (string)card["equipmentRole"] == "attack")) throw new ArgumentException("Equipment attack quota was changed");
            composer.BuildAttackPlan(next,classId,quota);
            var deck = composer.Recompose(beforeDeck,next,classId,run["itemMounts"] as JObject);
            if (deck.Count(card => (string)card["equipmentRole"] == "attack") != quota) throw new InvalidOperationException("Equipment change altered the born attack quota");
            var ids = new System.Collections.Generic.HashSet<string>();
            if (deck.Any(card => string.IsNullOrEmpty((string)card["instanceId"]) || !ids.Add((string)card["instanceId"]))) throw new InvalidOperationException("Equipment produced duplicate card instances");
            run["loadout"] = next; run["deck"] = deck; run["ownedItemRefs"] = new JArray(owned.OrderBy(id => id,StringComparer.Ordinal)); run["equipmentAttackSlotCount"] = quota;
            upgrades.RestampCards(run); _reconcile(run);
            receipt["command"] = command; receipt["attackSlotCount"] = quota; receipt.Remove("loadout"); run["lastEquipmentReceipt"] = receipt;
            return true;
        }
        private static void Replace(JObject destination,JObject source)
        { destination.RemoveAll(); foreach (var property in source.Properties()) destination.Add(property.Name,property.Value.DeepClone()); }
        private void UpgradeCard(JObject run,JObject effect,RandomStreams rng)
        {
            if (_equipmentService != null) { if (!_equipmentService(run,"upgradeCard",(JObject)effect.DeepClone(),rng)) throw new InvalidOperationException("Card upgrade transaction was refused"); return; }
            var upgrades = new ItemUpgradeService(_catalog); var composer = new WeaponCardComposer(_catalog);
            string Owner(JToken card)
            {
                var explicitId = (string)card["sourceArmamentId"] ?? (string)card["weaponId"];
                if (!string.IsNullOrEmpty(explicitId)) return "armament/" + explicitId;
                var role = (string)card["equipmentRole"];
                if (new[] { "attack","guard","technique" }.Contains(role)) { var piece = composer.RoleSource((JObject)run["loadout"],(string)run["class"],role)["piece"]; if (piece != null && piece.Type != JTokenType.Null) return "armament/" + (string)piece["id"]; }
                return CardMountService.Owner(card);
            }
            var choices = new System.Collections.Generic.List<(string Item,JToken Card)>();
            var ownerOrder = Array(run,"deck").Select(Owner).Where(id => id != null).Distinct();
            foreach (var itemRef in ownerOrder)
            {
                if (!itemRef.StartsWith("armament/",StringComparison.Ordinal) || ((int?)run["itemUpgradeLevels"]?[itemRef] ?? 0) >= upgrades.MaximumTier(itemRef)) continue;
                if (!Array(run,"deck").Any(c => Owner(c) == itemRef && (effect["card"] == null || (string)c["cardId"] == (string)effect["card"]))) continue;
                upgrades.Plan(run,itemRef); choices.Add((itemRef,null));
            }
            foreach (var card in Array(run,"deck"))
                if (card["sourceArmamentId"] == null && card["grantedBy"] == null && (bool?)card["upgraded"] != true && (effect["card"] == null || (string)card["cardId"] == (string)effect["card"]) && _catalog.Record("cards",(string)card["cardId"])["upgrade"] != null) choices.Add((null,card));
            if (choices.Count == 0) return;
            var chosen = choices[(bool?)effect["random"] == true ? rng.Int("misc",0,choices.Count-1) : 0];
            if (chosen.Item != null) Replace(run,(JObject)upgrades.Commit(run,chosen.Item,true)["run"]); else chosen.Card["upgraded"] = true;
            _reconcile?.Invoke(run);
        }
        private void SyncFlaskGrowth(JObject run)
        {
            var charges = (JObject)run["flaskCharges"]; var expected = new JObject { ["hp"] = 0,["mana"] = 0 };
            foreach (var row in _data["balance"]["flaskGrowth"] as JArray ?? new JArray())
            {
                var source = (string)row["source"]; var id = (string)row["id"]; bool held;
                if (source == "relic") held = Array(run,"relics").Values<string>().Contains(id);
                else if (source == "talisman") { var active = (int?)run["loadout"]?["active"]?["talisman"] ?? 0; held = (string)run["loadout"]?["sets"]?["talisman"]?[active] == id; }
                else continue; // Original reserved/non-binding growth source rows.
                if (held) expected[(string)row["kind"]] = (int)expected[(string)row["kind"]] + (int)row["amount"];
            }
            foreach (var kind in new[] { "hp","mana" })
            {
                var delta = (int)expected[kind] - (int)charges["grown"][kind]; if (delta == 0) continue;
                charges["capacity"] = (int)charges["capacity"] + delta;
                if (delta > 0) { charges[kind] = (int)charges[kind] + delta; charges[kind + "Current"] = (int)charges[kind + "Current"] + delta; }
                else { var take = -delta; var from = Math.Min(take,(int)charges[kind]); charges[kind] = (int)charges[kind] - from; var other = kind == "hp" ? "mana" : "hp"; charges[other] = (int)charges[other] - (take-from); foreach (var k in new[] { "hp","mana" }) charges[k+"Current"] = Math.Min((int)charges[k+"Current"],(int)charges[k]); }
            }
            charges["grown"] = expected;
        }
    }
}
