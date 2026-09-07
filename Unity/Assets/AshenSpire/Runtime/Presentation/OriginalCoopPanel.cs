// OriginalCoopPanel.cs — touch-first, member-specific views of an authoritative co-op run.
// WIRING: CampaignView mounts this inside its safe-area scroll body after every server view.
// INPUT: OriginalCoopRun.View(memberId), authored catalog and event-choice supplement.
// COMMANDS: send receives an intent only; the application owns authentication, sequence,
// transport, saving and rejoin. Call ShowError after a refused/failed send to unlock this view.
// MODIFY: labels/layout here; all prices, legal targets and outcomes remain domain-owned.
// No MonoBehaviour, global subscriptions, timers, simulation calls or persistent state.
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OriginalCoopPanel
    {
        private readonly VisualElement _root;
        private readonly JObject _view, _supplement, _balance;
        private readonly OriginalContentCatalog _catalog;
        private readonly Action<JObject> _send;
        private readonly Action _report, _menu;
        private VisualElement _body;
        private Label _notice;
        private readonly CoopPanelState _ui;
        private string _selected { get => _ui.SelectedCard; set => _ui.SelectedCard = value; }
        private string _target { get => _ui.Target; set => _ui.Target = value; }
        private string _rewardCard { get => _ui.RewardCard; set => _ui.RewardCard = value; }
        private bool _takeRelic { get => _ui.TakeRelic; set => _ui.TakeRelic = value; }
        private bool _takeFlask { get => _ui.TakeFlask; set => _ui.TakeFlask = value; }
        private int _page { get => _ui.HandPage; set => _ui.HandPage = value; }
        private bool _pending;
        private ItemUpgradeService _upgrades;
        private ItemUpgradeService Upgrades => _upgrades ?? (_upgrades = new ItemUpgradeService(_catalog));
        private JObject Local => (JObject)_view["local"];
        private JObject Run => (JObject)Local["run"];
        private JObject Scene => (JObject)_view["scene"];
        private string Id => (string)Local["id"];
        private JArray Party => (JArray)_view["party"];
        private JObject Room => (JObject)Local["room"];
        private bool Done => (bool?)Scene["done"]?[Id] == true;
        private JObject Body => Local["combat"]?["entity"] as JObject ?? Run;

        public OriginalCoopPanel(VisualElement root, JObject view, OriginalContentCatalog catalog, JObject supplemental, Action<JObject> send, Action report, Action menu, CoopPanelState uiState = null)
        {
            _root = root; _view = (JObject)view.DeepClone(); _catalog = catalog;
            _ui = uiState ?? new CoopPanelState(); _ui.Reconcile(_view);
            _supplement = supplemental; _balance = (JObject)catalog.Data()["balance"].DeepClone(); _send = send; _report = report; _menu = menu;
            Render();
        }
        public void ShowError(string message)
        {
            _pending = false; Render(); _notice.text = message; _notice.style.display = DisplayStyle.Flex; _report?.Invoke();
        }
        private void Render()
        {
            _root.Clear(); _root.AddToClassList("native-run");
            _notice = new Label { name = "coop-notice" }; _notice.AddToClassList("notice"); _notice.style.display = DisplayStyle.None; _root.Add(_notice);
            _body = new VisualElement(); _root.Add(_body);
            Text("ACT " + _view["actNumber"] + " · " + Human((string)Scene["kind"]) + " · Seed " + _view["seedString"], "heading");
            if (Local == null) { Text("Waiting for your player snapshot…", "lead"); Footer(); return; }
            Text("HP " + Body["hp"] + "/" + Body["maxHp"] + " · MP " + Body["mana"] + "/" + Body["maxMana"] + " · Stamina " + Body["stamina"] + "/" + Body["maxStamina"], "stat");
            Text(Run["cinders"] + " cinders · " + Run["smithingStones"] + " Smithing Stones", "caption");
            foreach (var member in Party)
            {
                var seat = (Scene["players"] as JArray)?.FirstOrDefault(p => (string)p["id"] == (string)member["id"]);
                var hp = seat?["entity"]?["hp"] ?? member["hp"]; var max = seat?["entity"]?["maxHp"] ?? member["maxHp"];
                Text((string)member["name"] + ((string)member["id"] == Id ? " · You" : "") + " · HP " + hp + "/" + max + ((bool?)member["connected"] == true ? "" : " · Disconnected") + ((bool?)seat?["ended"] == true ? " · Turn ended" : "") + ((int?)hp <= 0 ? " · Downed" : ""), "caption");
            }
            if (Local["catchup"] is JArray queue && queue.Count > 0) Catchup((JObject)queue[0]);
            else switch ((string)Scene["kind"])
            {
                case "lobby": Text("Gather your party before the climb begins.", "lead"); Command("start", "Begin the shared climb", new JObject { ["type"] = "start" }, (string)Party.FirstOrDefault()?["id"] == Id); break;
                case "map": Routes(); break;
                case "combat": Combat(); break;
                case "rewards": Rewards(Scene["offers"]?[Id] as JObject, null); break;
                case "shop": Shop(); break;
                case "shrine": Shrine(); break;
                case "event": Event(); break;
                case "complete": Text((string)Scene["result"] == "victory" ? "THE SPIRE FALLS SILENT" : "ASH RETURNS TO ASH", "node-title"); Text("Your party's climb is complete.", "lead"); break;
                default: Text("Waiting for the next room…", "lead"); break;
            }
            Footer();
            switch (_ui.Surface)
            {
                case "deck": Deck(); break;
                case "equipment": Equipment(); break;
                case "mounts": Mounts(); break;
                case "flasks": Flasks(); break;
            }
        }
        private void Main() { _ui.Surface = "main"; Render(); }
        private void Footer()
        {
            if (Local != null) Button("deck", "Deck and equipment", Deck);
            _body.SetEnabled(!_pending);
            var leave = new Button(() => _menu?.Invoke()) { text = "Disconnect and return to title", name = "coop-menu" }; leave.AddToClassList("button"); _root.Add(leave); _report?.Invoke();
        }
        private void Routes()
        {
            Text("Vote for the party's next route", "node-title");
            foreach (var value in _view["reachableIds"] ?? new JArray())
            {
                var id = (string)value; var node = _view["map"]?["nodes"]?[id];
                var voters = Party.Where(m => (string)Scene["votes"]?[(string)m["id"]] == id).Select(m => (string)m["name"]);
                Command("route-" + id, "Floor " + node?["floor"] + " · " + Human((string)node?["type"]) + (voters.Any() ? "\nVotes: " + string.Join(", ", voters) : ""), new JObject { ["type"] = "chooseNode", ["nodeId"] = id });
            }
            Text("The route advances once connected party members have voted.", "caption");
        }
        private void Combat()
        {
            var seat = Local["combat"] as JObject;
            if (seat == null) { Text("Waiting to enter the shared fight.", "lead"); return; }
            var alive = (bool?)seat["entity"]?["alive"] == true; var active = (string)Scene["phase"] == "player" && alive && (bool?)seat["connected"] == true;
            var mayPlay = active && (bool?)seat["ended"] != true;
            Text("Turn " + Scene["turn"] + " · " + Body["energy"] + " actions · " + Body["block"] + " guard", "node-title");
            var stage = new VisualElement(); stage.AddToClassList("stage"); stage.AddToClassList("native-stage");
            var acts = (int?)_balance["endless"]?["actsPerCycle"] ?? 3;
            stage.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Art/background" + (((int)_view["actNumber"] - 1) % acts + 1)));
            var figure = new OriginalPlayerFigure(Run); figure.Configure((string)Run["classId"], Run["customization"] as JObject, OriginalPlayerFigure.ActiveArmour(Run["loadout"] as JObject)); figure.AddToClassList("fighter"); stage.Add(figure);
            var enemies = (Scene["enemies"] as JArray ?? new JArray()).Where(e => (bool?)e["alive"] == true).ToArray();
            var hand = (Local["hand"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
            var selected = hand.FirstOrDefault(c => (string)c["instance"]?["instanceId"] == _selected);
            var friendly = (bool?)selected?["targets"]?["active"] == true;
            var legal = friendly ? selected["targets"]["legalIds"].Values<string>().ToArray() : enemies.Select(e => (string)e["id"]).ToArray();
            if (!legal.Contains(_target)) _target = legal.FirstOrDefault();
            var foe = enemies.FirstOrDefault(e => (string)e["id"] == _target) ?? enemies.FirstOrDefault();
            if (foe != null) stage.Add(Picture("Original/enemy_" + (string)foe["enemyId"])); _body.Add(stage);
            foreach (var enemy in enemies)
            {
                var id = (string)enemy["id"]; var intent = enemy["intent"];
                var label = Name("enemies", (string)enemy["enemyId"]) + " · HP " + enemy["hp"] + "/" + enemy["maxHp"] + " · Guard " + enemy["block"] + "\nIntent: " + Human((string)intent?["kind"]);
                if (intent?["damage"]?.Type != JTokenType.Null && intent?["damage"] != null) label += " " + intent["damage"] + ((int?)intent["hits"] > 1 ? " × " + intent["hits"] : "");
                var target = Button("target-" + id, label, () => { _target = id; Render(); }); target.SetEnabled(!friendly); if (_target == id) target.AddToClassList("primary");
                Statuses(enemy["statuses"] as JObject);
            }
            if (friendly) foreach (var id in legal)
            { var targetId = id; var target = Button("ally-" + id, "Target " + MemberName(id), () => { _target = targetId; Render(); }); if (_target == id) target.AddToClassList("primary"); }
            var cards = new VisualElement(); cards.AddToClassList("hand"); cards.AddToClassList("native-hand"); _body.Add(cards);
            var pages = Math.Max(1, (hand.Length + 3) / 4); _page = Math.Max(0, Math.Min(_page, pages - 1));
            foreach (var row in hand.Skip(_page * 4).Take(4))
            {
                var id = (string)row["instance"]["instanceId"]; var card = (JObject)row["card"]; var cost = row["cost"];
                var control = new Button(() => { _selected = id == _selected ? null : id; _target = null; Render(); }) { name = "coop-card-" + id };
                control.AddToClassList("card"); if (_selected == id) control.AddToClassList("selected");
                control.Add(Label((string)card["name"], "card-name")); control.Add(Label(Cost(cost), "cost")); control.Add(Label(OriginalCardText.Describe(card, _catalog), "card-description")); cards.Add(control);
            }
            if (pages > 1) { Button("cards-prev", "‹ Previous cards", () => { _page--; Render(); }).SetEnabled(_page > 0); Text((_page + 1) + " / " + pages, "caption"); Button("cards-next", "More cards ›", () => { _page++; Render(); }).SetEnabled(_page + 1 < pages); }
            Command("play", selected == null ? "Select a card" : "Play " + selected["card"]["name"] + (_target == null ? "" : " → " + (friendly ? MemberName(_target) : Name("enemies", (string)enemies.FirstOrDefault(e => (string)e["id"] == _target)?["enemyId"]))), new JObject { ["type"] = "playCard", ["cardInstanceId"] = _selected, ["targetId"] = _target }, mayPlay && selected != null && !CardMechanics.HasProperty(CardMechanics.FromDefinition((JObject)selected["card"]), "internal.unplayable") && CanPay(selected["cost"]) && (!friendly || legal.Contains(_target)));
            Command("end-turn", (bool?)seat["ended"] == true ? "Waiting for the party" : "End turn", new JObject { ["type"] = "endTurn" }, mayPlay);
            Button("flasks", "Use or throw a flask", Flasks).SetEnabled(active);
            if (!alive) Text("You are downed. Living allies can finish the fight.", "notice");
            Statuses(Body["statuses"] as JObject);
        }
        private void Flasks()
        {
            _ui.Surface = "flasks";
            _body.Clear(); Text("FLASKS", "heading"); Text("Choose a recipient. Using a flask consumes your charge or utility flask.", "caption");
            var charges = Body["flaskCharges"];
            foreach (var member in Scene["players"] ?? new JArray())
            {
                if ((bool?)member["entity"]?["alive"] != true) continue;
                var target = (string)member["id"]; var p = member["entity"];
                foreach (var kind in new[] { "hp", "mana" })
                {
                    var pool = kind; var max = kind == "hp" ? "maxHp" : "maxMana";
                    Command("charge-" + pool + "-" + target, (pool == "hp" ? "Crimson" : "Azure") + " → " + MemberName(target) + " · " + charges?[pool + "Current"] + " left", new JObject { ["type"] = "useFlask", ["slot"] = -1, ["chargeKind"] = pool, ["targetId"] = target }, (int?)charges?[pool + "Current"] > 0 && (int?)p[pool] < (int?)p[max]);
                }
            }
            foreach (var row in (Body["flasks"] as JArray ?? new JArray()).Select((value, index) => (value, index)))
            {
                var def = _catalog.Record("flasks", (string)row.value["flaskId"]); var slot = row.index;
                var recipients = (bool?)def["targeted"] == true ? (Scene["enemies"] as JArray ?? new JArray()).Where(e => (bool?)e["alive"] == true).Select(e => ((string)e["id"], Name("enemies", (string)e["enemyId"]))) : (Scene["players"] as JArray ?? new JArray()).Where(p => (bool?)p["entity"]?["alive"] == true).Select(p => ((string)p["id"], MemberName((string)p["id"])));
                foreach (var recipient in recipients) Command("utility-" + slot + "-" + recipient.Item1, (string)def["name"] + " → " + recipient.Item2, new JObject { ["type"] = "useFlask", ["slot"] = slot, ["targetId"] = recipient.Item1 });
            }
            Button("flasks-back", "Back to combat", Main); _report?.Invoke();
        }
        private void Rewards(JObject offer, string catchupId)
        {
            Text(catchupId == null ? "Spoils of the climb" : "Your saved spoils", "node-title");
            if (offer == null || catchupId == null && Done) { Text("Waiting for the party to finish choosing.", "lead"); return; }
            Text(offer["cinders"] + " cinders have already been added to your purse.", "caption");
            foreach (var token in offer["cards"] ?? new JArray())
            { var id = (string)token; var card = _catalog.Record("cards", id); var b = Button("reward-card-" + id, (string)card["name"] + "\n" + OriginalCardText.Describe(card, _catalog), () => { _rewardCard = id; Render(); }); if (_rewardCard == id) b.AddToClassList("primary"); }
            Button("reward-skip-card", _rewardCard == null ? "No card selected" : "Skip card", () => { _rewardCard = null; Render(); });
            if ((string)offer["relicId"] != null) { var toggle = new Toggle("Take " + Name("relics", (string)offer["relicId"])) { value = _takeRelic }; toggle.RegisterValueChangedCallback(e => _takeRelic = e.newValue); _body.Add(toggle); }
            if ((string)offer["flaskId"] != null)
            { var available = (Run["flasks"] as JArray)?.Count < (int)_balance["flaskSlots"]; var toggle = new Toggle("Take " + Name("flasks", (string)offer["flaskId"]) + (available ? "" : " · utility slots full")) { value = available && _takeFlask }; toggle.SetEnabled(available); toggle.RegisterValueChangedCallback(e => _takeFlask = e.newValue); _body.Add(toggle); }
            var pick = new JObject { ["cardId"] = _rewardCard, ["takeRelic"] = _takeRelic, ["flask"] = _takeFlask };
            // Read toggle state when pressed, not when constructing this view.
            Button("reward-confirm", "Confirm rewards and continue", () => { pick["takeRelic"] = _takeRelic; pick["flask"] = _takeFlask; Send(catchupId == null ? AddType(pick, "chooseReward") : CatchupIntent(catchupId, pick)); });
        }
        private void Shop()
        {
            if (Done) { Text("Waiting for your companions at the merchant.", "lead"); return; }
            Text("The wandering merchant", "node-title");
            foreach (var kind in new[] { "cards", "relics", "flasks" }) foreach (var row in (Room[kind] as JArray ?? new JArray()).Select((value, index) => (value, index)))
                Command("buy-" + kind + "-" + row.index, Name(kind, (string)row.value["id"]) + " · " + row.value["cost"] + " cinders" + ((bool?)row.value["sold"] == true ? " · Sold" : ""), new JObject { ["type"] = "buy", ["kind"] = kind.TrimEnd('s'), ["index"] = row.index }, (bool?)row.value["sold"] != true && (int?)row.value["cost"] <= (int?)Run["cinders"] && OriginalRewardAvailability.Refusal(_catalog.Data(), Run, kind.TrimEnd('s'), (string)row.value["id"]) == null);
            Services(); Command("shop-leave", "Finished shopping", new JObject { ["type"] = "leaveShop" });
        }
        private void Shrine()
        {
            if (Done) { Text("Waiting for your companions at the shrine.", "lead"); return; }
            Text("A moment of grace", "node-title"); Text("Your shared flask charges were refilled on arrival.", "caption");
            var pool = Run["flaskCharges"];
            for (var hp = 0; hp <= (int)pool["capacity"]; hp++)
                Command("split-" + hp, hp + " Crimson / " + ((int)pool["capacity"] - hp) + " Azure", new JObject { ["type"] = "shrineChoice", ["choice"] = "reallocate", ["allocation"] = new JObject { ["hp"] = hp, ["mana"] = (int)pool["capacity"] - hp } });
            Command("rest", "Rest and continue", new JObject { ["type"] = "shrineChoice", ["choice"] = "rest" }, new OriginalRunRules(_catalog.Data()).CanRest(Run));
            foreach (var member in Party.Where(m => (string)m["id"] != Id && (bool?)m["alive"] == true))
                Command("mend-" + member["id"], "Mend " + (string)member["name"] + " and continue", new JObject { ["type"] = "shrineChoice", ["choice"] = "mend", ["targetId"] = member["id"].DeepClone() }, (int?)member["hp"] < (int?)member["maxHp"]);
            Services(); Command("shrine-leave", "Leave without resting", new JObject { ["type"] = "shrineChoice", ["choice"] = "leave" });
        }
        private void Services()
        {
            var smith = Room["smith"]; var services = smith?["services"] as JArray ?? new JArray();
            if ((bool?)smith?["offered"] == true && services.Values<string>().Any(service => service == "extract" || service == "install"))
                Button("mounts", "Extract or install weapon cards", Mounts);
            if ((bool?)smith?["offered"] == true && services.Values<string>().Contains("upgrade"))
            {
                var upgrades = Upgrades;
                foreach (var item in upgrades.OwnedRefs(Run))
                { JObject plan; try { plan = upgrades.Plan(Run, item); } catch (ArgumentException) { continue; }
                    Command("upgrade-" + item, "Improve " + plan["itemName"] + " to +" + plan["nextLevel"] + " · " + plan["cost"] + " stones", Service("upgrade", new JObject { ["itemRef"] = item }), (bool)plan["affordable"]); }
            }
            if ((string)Scene["kind"] == "shop")
            {
                foreach (var card in (Run["deck"] as JArray ?? new JArray()).Where(c => string.IsNullOrEmpty((string)c["grantedBy"]) && string.IsNullOrEmpty((string)c["equipmentAttackSlotId"])))
                    Command("remove-" + card["instanceId"], "Remove " + Name("cards", (string)card["cardId"]) + " · " + Room["removeCost"] + " cinders", Service("removeCard", new JObject { ["instanceId"] = card["instanceId"].DeepClone() }), (int?)Room["removeCost"] <= (int?)Run["cinders"]);
                foreach (var sale in new OriginalRunServices(_catalog).Sellables(Run)) Command("sell-" + sale["kind"] + "-" + sale["index"], "Sell " + sale["name"] + " · receive " + sale["price"] + " cinders", Service("sell", (JObject)sale.DeepClone()));
            }
            if ((string)Scene["kind"] == "shrine")
            {
                var plan = new OriginalRunServices(_catalog).LevelPlan(Run, 1);
                foreach (var attribute in plan["attributes"] ?? new JArray())
                    Command("level-" + attribute["id"], "+1 " + Human((string)attribute["id"]) + " · " + plan["cost"] + " cinders", Service("levelUp", new JObject { ["allocation"] = new JObject { [(string)attribute["id"]] = 1 } }), (bool?)plan["offerable"] == true);
            }
        }
        private bool CanChangeEquipment => new[] { "map", "rewards", "shop", "shrine", "event" }.Contains((string)Scene["kind"]) && !Done
            && (Local["catchup"] as JArray ?? new JArray()).Count == 0 && (bool?)Party.FirstOrDefault(m => (string)m["id"] == Id)?["alive"] == true;
        private void Deck()
        {
            _ui.Surface = "deck";
            _body.Clear(); Text("YOUR DECK & EQUIPMENT", "heading");
            var locations = new WeaponLoadout(_catalog); var levels = Run["itemUpgradeLevels"] as JObject;
            foreach (var piece in locations.Pieces((JObject)Run["loadout"], (string)Run["classId"]).OfType<JObject>())
                Text((string)piece["name"] + " +" + ((int?)levels?[WeaponLoadout.ItemRef(piece)] ?? 0), "stat");
            Button("equipment", "Change equipment and prepared sets", Equipment).SetEnabled(CanChangeEquipment);
            if ((string)Scene["kind"] == "combat") Text("Prepared sets and mounts can be changed between fights.", "caption");
            var rows = Local["deck"] as JArray;
            Text("Run deck · " + (Run["deck"] as JArray ?? new JArray()).Count + " cards", "node-title");
            if (rows == null) Text("Waiting for the host's projected deck details.", "notice");
            foreach (var row in rows ?? new JArray())
            {
                var instance = row["instance"]; var card = row["card"] as JObject;
                if (card == null) continue;
                Text((string)card["name"], "stat"); Text(OriginalCardText.Describe(card, _catalog), "caption");
                var source = CardMountService.Owner(instance) ?? ((string)instance["sourceArmamentId"] == null ? null : "armament/" + (string)instance["sourceArmamentId"]);
                Text((source == null ? "Run-owned card" : "From " + ItemName(source)) + ((int?)instance["smithingLevel"] > 0 ? " · item +" + instance["smithingLevel"] : "") + ((bool?)instance["upgraded"] == true ? " · upgraded" : ""), "caption");
            }
            if (Local["combat"]?["piles"] is JObject piles)
                Text("This fight: " + string.Join(" · ", piles.Properties().Where(p => p.Value is JArray).Select(p => Human(p.Name) + " " + ((JArray)p.Value).Count)), "caption");
            Button("deck-back", "Back to the climb", Main); _report?.Invoke();
        }
        private void Equipment()
        {
            _ui.Surface = "equipment";
            _body.Clear(); Text("EQUIPMENT & PREPARED SETS", "heading");
            if (!CanChangeEquipment) { Text("Finish your current choice or fight before changing equipment.", "notice"); Button("equipment-back", "Back to deck", Deck); _report?.Invoke(); return; }
            var run = Run; var loadout = (JObject)run["loadout"]; var classId = (string)run["classId"];
            var locations = new WeaponLoadout(_catalog); var upgrades = Upgrades; var rules = new OriginalRunRules(_catalog.Data());
            var refs = upgrades.OwnedRefs(run).Concat((run["ownedItemRefs"] as JArray ?? new JArray()).Values<string>()).ToHashSet();
            var pieces = refs.Where(item => !item.StartsWith("relic/", StringComparison.Ordinal)).Select(upgrades.Definition).ToArray();
            foreach (var slot in _catalog.Table("equipment.slots").OfType<JObject>())
            {
                var slotId = (string)slot["id"]; var opened = rules.OpenedSets(run, slotId);
                Text(Human(slotId) + " · " + opened + " / " + slot["sets"] + " sets unlocked", "node-title");
                for (var index = 0; index < opened; index++)
                {
                    var set = index; var current = (string)loadout["sets"][slotId][index]; var active = (int)loadout["active"][slotId] == index;
                    var equipped = pieces.FirstOrDefault(piece => (string)piece["id"] == current && WeaponLoadout.Fits(slot, piece));
                    Text("Set " + (set + 1) + " · " + (equipped == null ? string.IsNullOrEmpty(current) ? "Empty" : Human(current) : (string)equipped["name"]) + (active ? " · Active" : ""), "stat");
                    if (!active)
                    {
                        var next = (JObject)loadout.DeepClone(); next["active"][slotId] = set; var piece = locations.Equipped(next, classId, slotId); string refusal = null;
                        if (piece != null && !(bool)locations.RequirementReceipt(piece, (JObject)run["attributes"], run["itemUpgradeLevels"] as JObject)["ok"]) refusal = "Attribute requirements are not met.";
                        try { new WeaponCardComposer(_catalog).BuildAttackPlan(next, classId); } catch (ArgumentException error) { refusal = error.Message; }
                        Command("set-" + slotId + "-" + set, "Use set " + (set + 1) + (refusal == null ? "" : " · " + refusal), new JObject { ["type"] = "selectSet", ["slotId"] = slotId, ["setIndex"] = set }, refusal == null);
                    }
                    foreach (var piece in pieces.Where(piece => WeaponLoadout.Fits(slot, piece)))
                    {
                        var id = (string)piece["id"]; if (current == id) continue;
                        var receipt = locations.Equip(loadout, classId, slotId, set, id, refs, (JObject)run["attributes"], false, run["itemUpgradeLevels"] as JObject);
                        var ok = (bool)receipt["ok"];
                        Command("equip-" + slotId + "-" + set + "-" + id, "Equip " + (string)piece["name"] + " +" + ((int?)run["itemUpgradeLevels"]?[WeaponLoadout.ItemRef(piece)] ?? 0) + (ok ? "" : " · " + (string)receipt["reason"]), new JObject { ["type"] = "equip", ["slotId"] = slotId, ["setIndex"] = set, ["itemId"] = id }, ok);
                    }
                    if (!string.IsNullOrEmpty(current))
                    {
                        var receipt = locations.Equip(loadout, classId, slotId, set, null, refs, (JObject)run["attributes"], false, run["itemUpgradeLevels"] as JObject);
                        Command("empty-" + slotId + "-" + set, "Empty this slot" + ((bool)receipt["ok"] ? "" : " · " + receipt["reason"]), new JObject { ["type"] = "equip", ["slotId"] = slotId, ["setIndex"] = set, ["itemId"] = null }, (bool)receipt["ok"]);
                    }
                }
                if (opened < (int)slot["sets"]) Text("Earn more prepared sets through Chronicle unlocks.", "caption");
            }
            Button("equipment-back", "Back to deck", Deck); _report?.Invoke();
        }
        private void Mounts()
        {
            _ui.Surface = "mounts";
            _body.Clear(); Text("WEAPON CARD MOUNTS", "heading");
            var services = Room["smith"]?["services"] as JArray ?? new JArray();
            if (!CanChangeEquipment || (bool?)Room["smith"]?["offered"] != true || !new[] { "shop", "shrine" }.Contains((string)Scene["kind"]))
            { Text("Mount changes require an available smith service.", "notice"); Button("mounts-back", "Back to the room", Main); _report?.Invoke(); return; }
            var mounts = new CardMountService(_catalog); var upgrades = Upgrades; var stones = ItemUpgradeService.Stones(Run);
            Text(stones + " Smithing Stones · extracting adds a run-owned card; installing consumes that card.", "caption");
            foreach (var item in upgrades.OwnedRefs(Run).Where(item => !item.StartsWith("relic/", StringComparison.Ordinal)))
            {
                Text(ItemName(item), "node-title");
                var rows = mounts.MountRows(item, Run["itemMounts"] as JObject);
                if (rows.Count == 0) Text("No authored card mounts.", "caption");
                foreach (var row in rows)
                {
                    var key = (string)row["mountKey"]; Text(((string)row["cardName"] ?? "Open mount") + " · " + Human((string)row["state"]), "stat");
                    if ((bool)row["extractable"] && services.Values<string>().Contains("extract"))
                        Command("extract-" + key, "Extract " + row["cardName"] + " · " + mounts.Cost("extract") + " stones", Service("extract", new JObject { ["itemRef"] = item, ["mountKey"] = key }), stones >= mounts.Cost("extract"));
                    if (!new[] { "fallback", "empty", "open" }.Contains((string)row["state"]) || !services.Values<string>().Contains("install")) continue;
                    var cards = (Run["deck"] as JArray ?? new JArray()).Where(card => string.IsNullOrEmpty((string)card["equipmentRole"]) && _catalog.Tags("card", _catalog.Record("cards", (string)card["cardId"])).Intersect(row["accepts"].Values<string>()).Any()).ToArray();
                    if (cards.Length == 0) Text("No run-owned cards match this mount's tags.", "caption");
                    foreach (var card in cards)
                    {
                        var instance = (string)card["instanceId"]; var projection = (Local["deck"] as JArray)?.FirstOrDefault(value => (string)value["instance"]?["instanceId"] == instance)?["card"] as JObject;
                        Command("install-" + key + "-" + instance, "Install " + ((string)projection?["name"] ?? Name("cards", (string)card["cardId"])) + " · " + mounts.Cost("install") + " stones", Service("install", new JObject { ["itemRef"] = item, ["mountKey"] = key, ["instanceId"] = instance }), stones >= mounts.Cost("install"));
                    }
                }
            }
            Button("mounts-back", "Back to the room", Main); _report?.Invoke();
        }
        private string ItemName(string itemRef) => (string)Upgrades.Definition(itemRef)["name"] ?? Human(itemRef);
        private void Event()
        {
            var eventId = (string)Scene["eventId"]; EventHeader(eventId);
            if (Scene["results"]?[Id] is JObject result) Text((string)result["resultText"] ?? "Your choice is made.", "lead");
            if (Scene["next"] != null) { Command("event-continue", "Continue with the party", new JObject { ["type"] = "eventContinue" }, (bool?)Scene["ack"]?[Id] != true); return; }
            if (Done) { Text("Waiting for the party to choose.", "caption"); return; }
            foreach (var choice in Scene["choices"]?[Id] ?? new JArray())
                Command("event-" + choice["id"], (string)choice["label"], new JObject { ["type"] = "eventChoice", ["choiceId"] = choice["id"].DeepClone() }, ((int?)choice["requires"]?["cinders"] ?? 0) <= (int)Run["cinders"]);
        }
        private void Catchup(JObject entry)
        {
            var id = (string)entry["id"]; Text("Catch up · Act " + entry["act"] + ", floor " + entry["floor"], "node-title");
            switch ((string)entry["type"])
            {
                case "reward": Rewards((JObject)entry["offer"], id); break;
                case "treasure": Command("catchup-treasure", "Collect " + Name("relics", (string)entry["relicId"]), CatchupIntent(id, new JObject())); break;
                case "event":
                    EventHeader((string)entry["eventId"]);
                    if (entry["done"] is JObject done) { Text((string)done["resultText"] ?? "Your choice is made.", "lead"); Command("catchup-continue", "Continue", CatchupIntent(id, new JObject { ["continue"] = true })); break; }
                    foreach (var choice in _supplement["eventChoices"]?[(string)entry["eventId"]] ?? new JArray())
                        if ((entry["open"] as JArray ?? new JArray()).Values<string>().Contains((string)choice["id"]))
                            Command("catchup-choice-" + choice["id"], (string)choice["label"], CatchupIntent(id, new JObject { ["choiceId"] = choice["id"].DeepClone() }), ((int?)choice["requires"]?["cinders"] ?? 0) <= Math.Min((int)Run["cinders"], (int)entry["purse"]));
                    break;
            }
        }
        private void EventHeader(string id) { var record = _catalog.Record("events", id); Text((string)record["name"], "node-title"); Text((string)record["text"], "lead"); }
        private bool CanPay(JToken cost) => ((bool?)cost["variable"] == true || (int)cost["action"] <= (int)Body["energy"]) && (int)cost["mana"] <= (int)Body["mana"] && (int)cost["stamina"] <= (int)Body["stamina"];
        private static string Cost(JToken cost) => ((bool?)cost["variable"] == true ? "X" : cost["action"].ToString()) + " actions · " + cost["mana"] + " MP · " + cost["stamina"] + " stamina";
        private static JObject AddType(JObject value, string type) { var result = (JObject)value.DeepClone(); result["type"] = type; return result; }
        private static JObject Service(string service, JObject request) => new JObject { ["type"] = "service", ["service"] = service, ["request"] = request };
        private static JObject CatchupIntent(string id, JObject pick) => new JObject { ["type"] = "resolveCatchup", ["entryId"] = id, ["pick"] = pick };
        private string MemberName(string id) => (string)Party.FirstOrDefault(m => (string)m["id"] == id)?["name"] ?? id;
        private string Name(string table, string id) => id == null ? "" : (string)_catalog.Record(table, id)["name"] ?? Human(id);
        private static string Human(string value) => OriginalCardText.Humanize(value ?? "Waiting");
        private static Label Label(string text, string style) { var label = new Label(text); label.AddToClassList(style); return label; }
        private Label Text(string text, string style) { var label = Label(text, style); _body.Add(label); return label; }
        private void Statuses(JObject statuses) { if (statuses != null && statuses.Count > 0) Text(string.Join(" · ", statuses.Properties().Select(s => Human(s.Name) + " " + (s.Value["stacks"] ?? s.Value["meter"]?["value"]))), "caption"); }
        private static Image Picture(string art) { var image = new Image { image = Resources.Load<Texture2D>("Art/" + art), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("fighter"); return image; }
        private Button Button(string id, string text, Action action) { var button = new Button(() => { try { action(); } catch (Exception e) { ShowError(e.Message); } }) { name = "coop-" + id, text = text }; button.AddToClassList("button"); _body.Add(button); return button; }
        private Button Command(string id, string text, JObject intent, bool enabled = true) { var button = Button(id, text, () => Send(intent)); button.SetEnabled(enabled); return button; }
        private void Send(JObject intent)
        {
            if (_pending) return;
            _pending = true; _body.SetEnabled(false); _notice.text = "Waiting for the party host…"; _notice.style.display = DisplayStyle.Flex;
            try { _send((JObject)intent.DeepClone()); } catch (Exception e) { ShowError(e.Message); }
        }
    }
}
