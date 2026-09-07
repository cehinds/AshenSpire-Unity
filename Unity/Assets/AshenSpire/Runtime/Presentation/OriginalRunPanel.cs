// OriginalRunPanel.cs — touch-first views for the native original-game session.
// WIRING: CampaignView mounts this tree inside its existing safe-area scroll shell.
// COMMANDS: delegate to OriginalGameSession; refresh/save are application-owned.
// MODIFY: labels/layout here; original content and domain components own all rules.
// MAP: OriginalMapBoard owns display preferences; route choices still enter the session.
// No global subscriptions, saved state, timers or MonoBehaviour lifecycle here.
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class OriginalRunPanel
    {
        private readonly VisualElement _root;
        private readonly OriginalGameSession _game;
        private readonly Action _report, _menu;
        private readonly VisualElement _actionHost;
        private readonly bool _diagnostics;
        private readonly OriginalMapViewServices _mapView;
        private VisualElement _actions;
        private string _target, _selected;
        private int _handPage;
        private Label _notice;
        private static int _diagnosticSequence;
        public Image PlayerImage { get; private set; }
        public Image EnemyImage { get; private set; }
        public VisualElement Stage { get; private set; }
        public OriginalRunPanel(VisualElement root, VisualElement actionHost, OriginalGameSession game, Action report, Action menu, bool diagnostics, OriginalMapViewServices mapView = null)
        { _root = root; _actionHost = actionHost; _game = game; _report = report; _menu = menu; _diagnostics = diagnostics; _mapView = mapView; Render(); }
        private void Render()
        {
            _mapView?.SetMapSurface?.Invoke(_game.Phase == OriginalRunPhase.Map);
            _root.Clear(); _root.AddToClassList("native-run"); _actions?.RemoveFromHierarchy(); var p = _game.Player; var run = _game.RunPlayer;
            Text("ACT " + _game.ActNumber + " · " + _game.Phase.ToString().ToUpperInvariant(), "heading");
            _root.Add(OriginalAppearance.Badge("native-run-appearance", run["customization"] as JObject));
            Text("HP " + p["hp"] + "/" + p["maxHp"] + " · MP " + p["mana"] + "/" + p["maxMana"] + " · Stamina " + p["stamina"] + "/" + p["maxStamina"], "stat");
            Text(run["cinders"] + " cinders · " + ((int?)run["smithingStones"] ?? 0) + " Smithing Stones", "caption");
            _notice = Text("", "notice"); _notice.style.display = DisplayStyle.None;
            switch (_game.Phase)
            {
                case OriginalRunPhase.Draft: Draft(); break;
                case OriginalRunPhase.Map: Routes(); break;
                case OriginalRunPhase.Combat: Combat(); break;
                case OriginalRunPhase.Rewards: Rewards(); break;
                case OriginalRunPhase.Shop: Shop(); break;
                case OriginalRunPhase.Shrine: Shrine(); break;
                case OriginalRunPhase.Event: Event(); break;
                case OriginalRunPhase.EventResult:
                    Text((string)_game.Room["resultText"] ?? "Your choice is made.", "lead"); Button("native-event-leave", "Continue the climb", _game.LeaveEvent); break;
                case OriginalRunPhase.Victory:
                    Text("THE SPIRE FALLS SILENT", "node-title"); Text("All three acts are complete. Your final run remains saved.", "lead"); break;
                case OriginalRunPhase.Defeat:
                    Text("ASH RETURNS TO ASH", "node-title"); Text("The climb ends here. Your run remains available to inspect.", "lead"); break;
            }
            Button("native-deck", "Deck and equipment", Deck);
            Button("native-menu", "Save and return to title", _menu);
            if (_diagnostics) ReportNativeState();
            _report();
        }
        private void ReportNativeState()
        {
            // Web console messages have a platform size limit. Chunk read-only
            // diagnostics so large hands never truncate the JSON QA observes.
            var player = _game.Player;
            var summary = new JObject();
            foreach (var key in new[] { "classId", "hp", "maxHp", "mana", "maxMana", "stamina", "maxStamina", "energy", "block", "statuses", "flaskCharges", "flasks" })
                if (player[key] != null) summary[key] = player[key].DeepClone();
            var room = _game.Room; room.Remove("combatSnapshot");
            var run = _game.RunPlayer; var visibleRun = new JObject();
            foreach (var key in new[] { "attributes", "cinders", "smithingStones", "loadout", "deck", "custom", "keepsakeId", "customization", "startingKitId", "startingKitSnapshot", "mapShapeLimits", "path", "mapNodeId", "runId", "seed" }) if (run[key] != null) visibleRun[key] = run[key].DeepClone();
            visibleRun["mapDimensions"] = new JObject { ["floors"] = _game.Map["floors"]?.DeepClone(), ["columns"] = _game.Map["columns"]?.DeepClone() };
            var cards = new JArray(_game.Hand.OfType<JObject>().Select(card => new JObject { ["instance"] = card.DeepClone(), ["card"] = _game.Resolve(card), ["cost"] = _game.Cost(card) }));
            var map = _game.Map;
            var routes = new JArray(_game.LegalNodeIds.Select(id => { var node = (JObject)map["nodes"][id].DeepClone(); node.Remove("resolved"); return node; }));
            var json = new JObject { ["phase"] = _game.Phase.ToString(), ["act"] = _game.ActNumber, ["turn"] = _game.Turn, ["player"] = summary, ["hand"] = _game.Hand, ["cards"] = cards, ["run"] = visibleRun, ["enemies"] = _game.Enemies, ["room"] = room, ["legalNodes"] = new JArray(_game.LegalNodeIds), ["routes"] = routes }.ToString(Newtonsoft.Json.Formatting.None);
            var sequence = ++_diagnosticSequence; var count = (json.Length + 2999) / 3000;
            for (var index = 0; index < count; index++)
                Debug.Log("ASHENSPIRE_NATIVE_STATE_CHUNK " + new JObject { ["sequence"] = sequence, ["index"] = index, ["count"] = count, ["text"] = json.Substring(index * 3000, Math.Min(3000, json.Length - index * 3000)) }.ToString(Newtonsoft.Json.Formatting.None));
        }
        private void Routes()
        {
            Text("Choose a connected route", "node-title");
            var run = _game.RunPlayer;
            var relicIds = (run["relics"] as JArray ?? new JArray()).Values<string>();
            var revealUnknown = relicIds.Any(id => (bool?)_game.Catalog.Record("relics", id)["passives"]?["revealUnknown"] == true);
            var board = new OriginalMapBoard(_game.Map, (run["path"] as JArray ?? new JArray()).Values<string>(),
                (string)run["mapNodeId"], _game.LegalNodeIds, _game.ActNumber,
                (string)run["runId"] ?? run["seed"]?.ToString() ?? "solo", "solo", revealUnknown, _mapView,
                id => Execute(() => _game.Enter(id)), _diagnostics);
            board.style.flexGrow = 1; board.style.flexShrink = 1; board.style.minHeight = 0;
            _root.Add(board);
        }
        private int ContentAct => new OriginalCustomRunRules(_game.Catalog.Data()).ContentAct(_game.RunPlayer);
        private void Draft()
        {
            Text("Shape your opening deck", "node-title");
            Text("Choose one card. Your current offer is saved, so returning keeps the same choices.", "caption");
            foreach (var token in _game.DraftChoices)
            {
                var id = (string)token; var card = _game.Catalog.Record("cards", id);
                Button("native-draft-" + id, (string)card["name"] + "\n" + OriginalCardText.Describe(card, _game.Catalog), () => _game.PickDraft(id));
            }
        }
        private void Combat()
        {
            Text("Turn " + _game.Turn + " · " + _game.Player["energy"] + " actions · " + _game.Player["block"] + " guard", "node-title");
            var stage = new VisualElement(); Stage = stage; stage.AddToClassList("stage"); stage.AddToClassList("native-stage");
            stage.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Art/background" + ContentAct));
            var figure = new OriginalPlayerFigure(_game); figure.Configure((string)_game.RunPlayer["classId"], _game.RunPlayer["customization"] as JObject, OriginalPlayerFigure.ActiveArmour(_game.RunPlayer["loadout"] as JObject)); figure.AddToClassList("fighter"); PlayerImage = figure; stage.Add(PlayerImage);
            OriginalAppearance.Apply(PlayerImage, null, _game.RunPlayer["customization"] as JObject);
            var enemies = _game.Enemies.Where(x => (bool?)x["alive"] == true).ToArray();
            if (!enemies.Any(x => (string)x["id"] == _target)) _target = (string)enemies.FirstOrDefault()?["id"];
            var selectedEnemy = enemies.FirstOrDefault(x => (string)x["id"] == _target);
            if (selectedEnemy != null) { EnemyImage = OriginalEnemyFigure.Create((string)selectedEnemy["enemyId"]); stage.Add(EnemyImage); }
            _root.Add(stage);
            foreach (var enemy in enemies)
            {
                var id = (string)enemy["id"]; var def = _game.Catalog.Record("enemies", (string)enemy["enemyId"]);
                var target = Button("native-target-" + id, (string)def["name"] + " · HP " + enemy["hp"] + "/" + enemy["maxHp"] + " · Guard " + enemy["block"], () => { _target = id; Render(); }); target.AddToClassList("native-target");
                if (id == _target) target.AddToClassList("primary");
                var intent = enemy["intent"] as JObject;
                var intentText = OriginalCardText.Humanize((string)intent?["kind"] ?? "Preparing");
                if (intent?["damage"] != null && intent["damage"].Type != JTokenType.Null) intentText += " " + intent["damage"] + ((int?)intent["hits"] > 1 ? " × " + intent["hits"] : "");
                if ((bool?)intent?["pending"] == true) intentText += " · committed attack";
                target.text += "\nIntent: " + intentText;
                var statuses = enemy["statuses"] as JObject;
                if (statuses != null && statuses.Count > 0) Text(string.Join(" · ", statuses.Properties().Select(x => OriginalCardText.Humanize(x.Name) + " " + (x.Value["stacks"] ?? x.Value["meter"]?["value"]))), "caption");
            }
            Text("Choose a card, then confirm its target", "caption");
            var hand = new VisualElement(); hand.AddToClassList("hand"); hand.AddToClassList("native-hand"); _root.Add(hand);
            var instances = _game.Hand.OfType<JObject>().ToArray();
            var pages = Math.Max(1, (instances.Length + 3) / 4); _handPage = Math.Min(_handPage, pages - 1);
            foreach (var instance in instances.Skip(_handPage * 4).Take(4))
            {
                var id = (string)instance["instanceId"]; var card = _game.Resolve(instance); var cost = _game.Cost(instance);
                var control = new Button(() => { _selected = _selected == id ? null : id; Render(); }) { name = "native-card-" + id };
                control.AddToClassList("card"); if (_selected == id) control.AddToClassList("selected");
                control.Add(Label((string)card["name"], "card-name"));
                control.Add(Label(CostText(cost), "cost"));
                control.Add(Label(OriginalCardText.Describe(card, _game.Catalog), "card-description")); hand.Add(control);
            }
            if (pages > 1)
            {
                var pager = new VisualElement(); pager.AddToClassList("inspection-row"); _root.Add(pager);
                Button("native-hand-prev", "‹ Previous cards", () => { _handPage--; Render(); }, pager).SetEnabled(_handPage > 0);
                var count = Label((_handPage + 1) + " / " + pages, "native-page-count"); pager.Add(count);
                Button("native-hand-next", "More cards ›", () => { _handPage++; Render(); }, pager).SetEnabled(_handPage + 1 < pages);
            }
            var selected = _game.Hand.OfType<JObject>().FirstOrDefault(x => (string)x["instanceId"] == _selected);
            _actions = new VisualElement(); _actions.AddToClassList("actions"); _actions.AddToClassList("native-actions"); _actionHost.Add(_actions);
            var play = Button("native-play", selected == null ? "Select a card" : "Play " + (string)_game.Resolve(selected)["name"], () => _game.Play(_selected, _target), _actions);
            play.AddToClassList("primary"); play.SetEnabled(selected != null && CanPay(_game.Cost(selected)));
            Button("native-breath", "Catch Breath · 1 action → 1 stamina", _game.CatchBreath).SetEnabled((int)_game.Player["energy"] > 0 && (int)_game.Player["stamina"] < (int)_game.Player["maxStamina"]);
            Button("native-end-turn", "End turn", _game.EndTurn, _actions);
            var charges = _game.Player["flaskCharges"];
            Button("native-crimson", "Crimson Flask · " + charges["hpCurrent"] + " left", () => _game.DrinkCharge("hp")).SetEnabled((int)charges["hpCurrent"] > 0 && (int)_game.Player["hp"] < (int)_game.Player["maxHp"]);
            Button("native-azure", "Azure Flask · " + charges["manaCurrent"] + " left", () => _game.DrinkCharge("mana")).SetEnabled((int)charges["manaCurrent"] > 0 && (int)_game.Player["mana"] < (int)_game.Player["maxMana"]);
            foreach (var flask in (_game.Player["flasks"] as JArray ?? new JArray()).Select((value,index) => (value,index)))
            {
                var slot = flask.index; var definition = _game.Catalog.Record("flasks", (string)flask.value["flaskId"]);
                Button("native-flask-" + slot, "Use " + (string)definition["name"], () => _game.DrinkFlask(slot, (bool?)definition["targeted"] == true ? _target : null));
            }
            foreach (var entry in _game.LastEvents.TakeLast(4)) Text(OriginalCardText.Humanize((string)entry["type"]), "caption");
        }
        private bool CanPay(JObject cost) => ((bool?)cost["variable"] == true || (int)cost["action"] <= (int)_game.Player["energy"]) && (int)cost["mana"] <= (int)_game.Player["mana"] && (int)cost["stamina"] <= (int)_game.Player["stamina"];
        private static string CostText(JObject cost) => ((bool?)cost["variable"] == true ? "X" : cost["action"].ToString()) + " actions · " + cost["mana"] + " MP · " + cost["stamina"] + " stamina";
        private void Rewards()
        {
            Text("Spoils of the climb", "node-title"); var room = _game.Room; var offers = room["rewards"];
            foreach (var kind in new[] { "cinders", "relic", "flask", "armament" })
            {
                if (room["states"][kind] != null) continue;
                if (kind == "cinders") { if ((int?)offers[kind] > 0) Button("native-reward-cinders", "Collect " + offers[kind] + " cinders", () => _game.Reward("cinders")); }
                else if (!string.IsNullOrEmpty((string)offers[kind + "Id"]))
                {
                    var table = kind == "armament" ? "equipment.armaments" : kind + "s"; var row = _game.Catalog.Record(table, (string)offers[kind + "Id"]);
                    var refusal = OriginalRewardAvailability.Refusal(_game.Catalog.Data(), _game.RunPlayer, kind, (string)row["id"]);
                    Button("native-reward-" + kind, "Take " + (string)row["name"], () => _game.Reward(kind)).SetEnabled(refusal == null);
                    if (refusal != null) Text(refusal + " You can leave this reward behind.", "caption");
                }
            }
            if (room["states"]["card"] == null) foreach (var token in offers["cardIds"] ?? new JArray())
            { var id = (string)token; var card = _game.Catalog.Record("cards", id); Button("native-reward-card-" + id, (string)card["name"] + "\n" + OriginalCardText.Describe(card, _game.Catalog), () => _game.Reward("card", id)); }
            Button("native-rewards-continue", "Continue · leave unclaimed rewards", _game.ContinueRewards);
        }
        private void Shop()
        {
            Text("The wandering merchant", "node-title");
            foreach (var kind in new[] { "cards", "relics", "flasks" })
                foreach (var entry in (_game.Room[kind] as JArray ?? new JArray()).Select((value, index) => (value, index)))
                {
                    var row = _game.Catalog.Record(kind, (string)entry.value["id"]); var index = entry.index;
                    var refusal = OriginalRewardAvailability.Refusal(_game.Catalog.Data(), _game.RunPlayer, kind.TrimEnd('s'), (string)row["id"]);
                    Button("native-buy-" + kind + "-" + index, (string)row["name"] + " · " + entry.value["cost"] + " cinders", () => _game.Buy(kind.TrimEnd('s'), index)).SetEnabled((bool?)entry.value["sold"] != true && (int)entry.value["cost"] <= (int)_game.RunPlayer["cinders"] && refusal == null);
                    if (refusal != null) Text(refusal, "caption");
                }
            Services();
            foreach (JObject row in new OriginalRunServices(_game.Catalog).Sellables(_game.RunPlayer))
            {
                var sale = (JObject)row.DeepClone();
                Button("native-sell-" + row["kind"] + "-" + row["index"], "Sell " + row["name"] + " · receive " + row["price"] + " cinders", () => _game.Service("sell", sale));
            }
            Button("native-shop-leave", "Return to the route", _game.LeaveShop);
        }
        private void Shrine()
        {
            Text("A moment of grace", "node-title"); Text("Rest restores health and mana. Your shared flask charges are refilled on arrival.", "lead");
            var pool = _game.RunPlayer["flaskCharges"]; Text("Shared flask allocation", "stat");
            for (var hp = 0; hp <= (int)pool["capacity"]; hp++)
            {
                var health = hp; var mana = (int)pool["capacity"] - hp;
                Button("native-flask-split-" + hp, health + " Crimson / " + mana + " Azure", () => _game.Service("reallocateFlasks", new JObject { ["hp"] = health, ["mana"] = mana }));
            }
            Services();
            var levels = new OriginalRunServices(_game.Catalog).LevelPlan(_game.RunPlayer, 1);
            Button("native-level-up", "Level up · next point " + levels["cost"] + " cinders", () => LevelUp(new JObject())).SetEnabled((bool)levels["offerable"]);
            var canRest = new OriginalRunRules(_game.Catalog.Data()).CanRest(_game.RunPlayer);
            Button("native-rest", "Rest and continue", _game.Rest).SetEnabled(canRest);
            if (!canRest) Text("Your relic prevents resting. Choose another shrine service.", "notice");
            Button("native-shrine-leave", "Leave without resting", _game.LeaveShrine);
        }
        private void LevelUp(JObject pending)
        {
            _root.Clear(); _actions?.RemoveFromHierarchy(); Text("STRENGTH FROM ASH", "heading"); _notice = Text("", "notice");
            var service = new OriginalRunServices(_game.Catalog); var run = _game.RunPlayer;
            var budget = service.LevelBudget(run); var count = pending.Properties().Sum(p => (int)p.Value);
            var cost = budget["costs"].Take(count).Sum(x => (int)x);
            Text(count + " points · " + cost + " of " + run["cinders"] + " cinders", "stat");
            foreach (var row in service.LevelPlan(run, 1)["attributes"])
            {
                var id = (string)row["id"]; var added = (int?)pending[id] ?? 0;
                Text(OriginalCardText.Humanize(id) + " " + run["attributes"][id] + (added > 0 ? " → " + ((int)run["attributes"][id] + added) : ""), "node-title");
                var buttons = new VisualElement(); buttons.AddToClassList("inspection-row"); _root.Add(buttons);
                Button("native-level-down-" + id, "− " + OriginalCardText.Humanize(id), () => { pending[id] = added - 1; LevelUp(pending); }, buttons).SetEnabled(added > 0);
                Button("native-level-up-" + id, "+ " + OriginalCardText.Humanize(id), () => { pending[id] = added + 1; LevelUp(pending); }, buttons).SetEnabled(count < (int)budget["levels"]);
            }
            Button("native-level-confirm", "Spend " + cost + " cinders", () => _game.Service("levelUp", new JObject { ["allocation"] = pending })).SetEnabled(count > 0);
            Button("native-level-cancel", "Cancel · keep your cinders", Render); _report();
        }
        private void Services()
        {
            var smith = _game.Room["smith"];
            var offered = (bool?)smith?["offered"] == true;
            var services = smith?["services"] as JArray ?? new JArray();
            if (_game.Phase == OriginalRunPhase.Shop)
                Button("native-remove-card", "Remove a card · " + _game.Room["removeCost"] + " cinders", RemoveCards);
            if (!offered) return;
            var service = new ItemUpgradeService(_game.Catalog);
            foreach (var item in service.OwnedRefs(_game.RunPlayer))
            {
                JObject plan; try { plan = service.Plan(_game.RunPlayer, item); } catch (ArgumentException) { continue; }
                if (services.Any(x => (string)x == "upgrade")) Button("native-upgrade-" + item.Replace('/', '-'), "Improve " + plan["itemName"] + " to +" + plan["nextLevel"] + " · " + plan["cost"] + " stones", () => _game.Service("upgrade", new JObject { ["itemRef"] = item })).SetEnabled((bool)plan["affordable"]);
            }
            if (services.Any(x => (string)x == "extract" || (string)x == "install")) Button("native-card-mounts", "Extract or install weapon cards", Mounts);
        }
        private void RemoveCards()
        {
            _root.Clear(); _actions?.RemoveFromHierarchy(); Text("REMOVE A CARD", "heading"); _notice = Text("", "notice");
            foreach (var card in ((JArray)_game.RunPlayer["deck"]).OfType<JObject>().Where(x => string.IsNullOrEmpty((string)x["grantedBy"]) && string.IsNullOrEmpty((string)x["equipmentAttackSlotId"])))
            { var id = (string)card["instanceId"]; Button("native-remove-" + id, "Remove " + (string)_game.Resolve(card)["name"], () => _game.Service("removeCard", new JObject { ["instanceId"] = id })); }
            Button("native-service-back", "Back", Render); _report();
        }
        private void Mounts()
        {
            _mapView?.SetMapSurface?.Invoke(false);
            _root.Clear(); _actions?.RemoveFromHierarchy(); Text("WEAPON CARD MOUNTS", "heading"); _notice = Text("", "notice");
            var mounts = new CardMountService(_game.Catalog); var run = _game.RunPlayer;
            var services = _game.Room["smith"]["services"] as JArray ?? new JArray();
            foreach (var item in new ItemUpgradeService(_game.Catalog).OwnedRefs(run).Where(x => !x.StartsWith("relic/", StringComparison.Ordinal)))
                foreach (var row in mounts.MountRows(item, run["itemMounts"] as JObject))
                {
                    var key = (string)row["mountKey"]; Text(OriginalCardText.Humanize(item.Split('/').Last()) + " · " + ((string)row["cardName"] ?? "Open mount"), "stat");
                    if ((bool)row["extractable"] && services.Any(x => (string)x == "extract")) Button("native-extract-" + key, "Extract card · " + mounts.Cost("extract") + " stones", () => _game.Service("extract", new JObject { ["itemRef"] = item, ["mountKey"] = key }));
                    if (new[] { "fallback", "empty", "open" }.Contains((string)row["state"]) && services.Any(x => (string)x == "install"))
                        foreach (var card in ((JArray)run["deck"]).OfType<JObject>().Where(x => string.IsNullOrEmpty((string)x["equipmentRole"]) && _game.Catalog.Tags("card", _game.Catalog.Record("cards", (string)x["cardId"])).Intersect(row["accepts"].Values<string>()).Any()))
                        { var id = (string)card["instanceId"]; Button("native-install-" + key + "-" + id, "Install " + (string)_game.Resolve(card)["name"] + " · " + mounts.Cost("install") + " stones", () => _game.Service("install", new JObject { ["itemRef"] = item, ["mountKey"] = key, ["instanceId"] = id })); }
                }
            Button("native-mounts-back", "Back", Render); _report();
        }
        private void Event()
        {
            var record = _game.Catalog.Record("events", (string)_game.Room["eventId"]);
            Text((string)record["name"], "node-title"); Text((string)record["text"], "lead");
            foreach (var choice in _game.EventChoices) { var id = (string)choice["id"]; Button("native-choice-" + id, (string)choice["label"], () => _game.ChooseEvent(id)); }
        }
        private void Deck()
        {
            _mapView?.SetMapSurface?.Invoke(false);
            _root.Clear(); _actions?.RemoveFromHierarchy(); Text("YOUR DECK & EQUIPMENT", "heading"); var run = _game.RunPlayer;
            foreach (var item in new WeaponLoadout(_game.Catalog).Pieces((JObject)run["loadout"], (string)run["classId"])) Text((string)item["name"], "stat");
            if (_game.Phase != OriginalRunPhase.Victory && _game.Phase != OriginalRunPhase.Defeat) Button("native-equipment", _game.Phase == OriginalRunPhase.Combat ? "Switch prepared weapon sets" : "Change equipment and weapon sets", Equipment);
            foreach (var instance in ((JArray)run["deck"]).OfType<JObject>()) { var card = _game.Resolve(instance); Text((string)card["name"], "stat"); Text(OriginalCardText.Describe(card, _game.Catalog), "caption"); }
            Button("native-deck-back", "Back to run", Render); _report();
        }
        private void Equipment()
        {
            _mapView?.SetMapSurface?.Invoke(false);
            _root.Clear(); _actions?.RemoveFromHierarchy(); Text("EQUIPMENT", "heading"); _notice = Text("", "notice");
            var run = _game.RunPlayer; var upgrades = new ItemUpgradeService(_game.Catalog);
            var owned = upgrades.OwnedRefs(run).Where(x => !x.StartsWith("relic/", StringComparison.Ordinal)).Select(upgrades.Definition).ToArray();
            foreach (var slot in _game.Catalog.Table("equipment.slots").OfType<JObject>())
            {
                var slotId = (string)slot["id"]; Text(OriginalCardText.Humanize(slotId), "node-title");
                var combat = _game.Phase == OriginalRunPhase.Combat;
                if (combat && (string)slot["swap"] != "combat") { Text("Fastened until this fight ends", "caption"); continue; }
                for (var index = 0; index < _game.OpenedSets(slotId); index++)
                {
                    var set = index; var current = (string)run["loadout"]["sets"][slotId][index];
                    Text("Set " + (index + 1) + " · " + (string.IsNullOrEmpty(current) ? "Empty" : OriginalCardText.Humanize(current)), "caption");
                    if ((int)run["loadout"]["active"][slotId] != index)
                    {
                        var allowance = combat && (string)_game.Catalog.Data()["balance"]["equipment"]["swapCostKind"] == "allowance";
                        var price = combat ? (int)_game.SwapPrice(slotId, set)["cost"] : 0;
                        var button = Button("native-set-" + slotId + "-" + index, "Use set " + (index + 1) + (combat ? allowance ? " · 1 swap (" + _game.SwapsLeft + " left)" : " · " + price + " actions" : ""), () => _game.SelectSet(slotId, set));
                        button.SetEnabled(!combat || (allowance ? _game.SwapsLeft > 0 : (int)_game.Player["energy"] >= price));
                    }
                    if (combat) continue;
                    foreach (var item in owned.Where(x => WeaponLoadout.Fits(slot, x)))
                    { var id = (string)item["id"]; Button("native-equip-" + slotId + "-" + index + "-" + id, "Equip " + (string)item["name"], () => _game.Equip(slotId, set, id)); }
                    if (!string.IsNullOrEmpty(current)) Button("native-empty-" + slotId + "-" + index, "Empty this slot", () => _game.Equip(slotId, set, null));
                }
            }
            Button("native-equipment-back", "Back to deck", Deck); _report();
        }
        private Image Picture(string art) { var image = new Image { image = Resources.Load<Texture2D>("Art/" + art), scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("fighter"); return image; }
        private Label Text(string text, string style) { var label = Label(text, style); _root.Add(label); return label; }
        private static Label Label(string text, string style) { var label = new Label(text ?? ""); label.AddToClassList(style); return label; }
        private Button Button(string id, string text, Action command, VisualElement parent = null)
        {
            var button = new Button(() => Execute(command)) { text = text, name = id }; button.AddToClassList("button"); (parent ?? _root).Add(button); return button;
        }
        private void Execute(Action command)
        {
            try { command(); }
            catch (ArgumentException error) { _notice.text = error.Message; _notice.style.display = DisplayStyle.Flex; _report(); }
            catch (InvalidOperationException error) { _notice.text = error.Message; _notice.style.display = DisplayStyle.Flex; _report(); }
        }
    }
}
