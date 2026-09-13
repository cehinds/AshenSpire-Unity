// OriginalFoundationPanel.cs — original-style wanderer creation and owner previews.
// WIRING: CampaignView mounts this panel; no scene/Inspector setup. No save writes.
// DATA: OriginalContentCatalog. RULES: CreationModel and ActMapGenerator.
// MODIFY: composition in OriginalCreation.uss; card faces in OriginalCardView.
// CreationModel/OriginalCharacterBuilder remain the only attribute/loadout authority.
// The class workspace follows the pinned HTML reference: portrait/resources/relic
// beside class choices on desktop, stacked on phone. Disclosures default open so
// existing controls remain discoverable; their state is local to this view only.
// LIFETIME: callbacks belong to this tree; no global events, timers or Update.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OriginalFoundationPanel
    {
        private readonly VisualElement _root;
        private VisualElement _target;
        private readonly Dictionary<string, bool> _sections = new Dictionary<string, bool>();
        private readonly OriginalContentCatalog _catalog;
        private readonly Action _report, _back;
        private readonly CreationModel _creation;
        private readonly AttributeProgression _progression;
        private readonly OriginalCharacterBuilder _builder;
        private readonly Action<JObject, uint> _start;
        private readonly JObject _profileMeta;
        private readonly JObject _starting = new JObject();
        private string _kit;
        private readonly JObject _setup = new JObject { ["custom"] = new JObject { ["ascension"] = 0, ["mods"] = new JObject(), ["deckMode"] = "standard" }, ["keepsakeId"] = "none", ["customization"] = new JObject { ["name"] = "Forsaken", ["glyph"] = "⚔", ["tint"] = "gold" } };
        private string _seed = "ASHEN", _table = "cards", _search = "";
        public OriginalFoundationPanel(VisualElement root, OriginalContentCatalog catalog, AttributeProgression progression, JObject mechanics, Action report, Action back, Action<JObject, uint> start = null, JObject profile = null)
        {
            _root = root; _target = root; _catalog = catalog; _report = report; _back = back;
            foreach (var resource in new[] { "OriginalCards", "OriginalCreation" })
            {
                var sheet = Resources.Load<StyleSheet>(resource);
                if (sheet != null && !_root.styleSheets.Contains(sheet)) _root.styleSheets.Add(sheet);
            }
            _root.RegisterCallback<GeometryChangedEvent>(e => {
                if (_root.ClassListContains("original-creation")) _root.EnableInClassList("creation-wide", e.newRect.width >= 850);
            });
            _progression = progression; _builder = new OriginalCharacterBuilder(catalog, progression, mechanics); _start = start;
            _profileMeta = (JObject)(profile?.DeepClone() ?? new JObject());
            _creation = new CreationModel(catalog, "reaver", (string)catalog.Data()["attributeRules"]["defaultMode"], progression);
            Creation();
        }
        private void Header(string title)
        {
            _root.Clear(); _target = _root; _root.RemoveFromClassList("original-creation"); _root.RemoveFromClassList("creation-wide"); Label(title, "heading");
            Label(_start == null ? "Original foundation preview · campaign integration is in progress." : "Choose your wanderer, attributes and weapon cards.", "caption");
            if (_start == null)
            {
                var tabs = new VisualElement(); tabs.AddToClassList("foundation-tabs"); _root.Add(tabs);
                tabs.Add(MakeButton("foundation-creation", "Wanderer", Creation));
                tabs.Add(MakeButton("foundation-map", "Routes", Map));
                tabs.Add(MakeButton("foundation-catalog", "Content", Catalog));
            }
            Button("foundation-back", "Back to title", _back);
        }
        private void Creation()
        {
            Header("Prepare your Forsaken");
            _root.AddToClassList("original-creation"); _root.EnableInClassList("creation-wide", _root.contentRect.width >= 850);
            var hero = _catalog.Record("classes", _creation.ClassId);
            var options = new OriginalStartingOptions(_catalog); var kits = options.AvailableKits(_creation.ClassId, _profileMeta);
            if (!kits.Any(x => (string)x["id"] == _kit && (bool?)x["available"] == true)) _kit = (string)kits.First(x => (bool?)x["available"] == true)["id"];
            var preview = _builder.Preview(_creation, _kit, _profileMeta, _starting);
            var classSection = Section("native-creation-class-section", "CLASS · " + (string)hero["name"]);
            var workspace = new VisualElement(); workspace.AddToClassList("creation-workspace"); classSection.Add(workspace);
            var classPreview = new VisualElement(); classPreview.AddToClassList("creation-class-preview"); workspace.Add(classPreview);
            _target = classPreview;
            Label("CLASS PREVIEW", "creation-eyebrow"); Label((string)hero["name"], "creation-class-name");
            var portraitFrame = new VisualElement(); portraitFrame.AddToClassList("creation-portrait-frame"); classPreview.Add(portraitFrame);
            var portrait = new OriginalPlayerFigure(_setup); portrait.AddToClassList("creation-portrait"); portraitFrame.Add(portrait);
            var appearance = OriginalAppearance.Badge("native-preview-appearance", (JObject)_setup["customization"]); classPreview.Add(appearance);
            void RefreshAppearance() { var loadout = (JObject)_builder.Preview(_creation, _kit, _profileMeta, _starting)["loadout"]; portrait.Configure(_creation.ClassId, (JObject)_setup["customization"], OriginalPlayerFigure.ActiveArmour(loadout)); OriginalAppearance.Apply(portrait, appearance, (JObject)_setup["customization"]); }
            RefreshAppearance();
            Label("STARTING RESOURCES", "creation-eyebrow");
            ResourceStrip(classPreview, (JObject)preview["resources"]);
            Label("Includes starting equipment and relic bonuses.", "caption");
            var relic = _catalog.Record("relics", (string)preview["relicId"]);
            Label("CLASS RELIC", "creation-eyebrow");
            var relicPanel = new VisualElement(); relicPanel.AddToClassList("creation-relic"); classPreview.Add(relicPanel);
            _target = relicPanel; Label((string)relic["name"], "node-title"); Label(RelicDescription(relic), "caption");
            var classChoices = new VisualElement(); classChoices.AddToClassList("creation-class-choices"); workspace.Add(classChoices);
            _target = classChoices; Label("CHOOSE", "creation-eyebrow"); Label("Class", "creation-class-name");
            foreach (var record in _catalog.Table("classes"))
            {
                var value = (string)record["id"];
                var chooseClass = MakeButton("foundation-class-" + value, "", () => { _creation.Select(value, _creation.ModeId); _starting.RemoveAll(); Creation(); });
                chooseClass.AddToClassList("creation-class-option");
                if (value == _creation.ClassId) chooseClass.AddToClassList("primary");
                var className = new Label((string)record["name"]); className.AddToClassList("creation-option-name"); chooseClass.Add(className);
                var description = new Label((string)record["description"]); description.AddToClassList("creation-option-description"); chooseClass.Add(description);
                classChoices.Add(chooseClass);
            }
            _target = Section("native-creation-character-section", "CHARACTER · Attributes");
            Choices("foundation-mode", "Allocation", _catalog.Table("creationModes"), _creation.ModeId, value => { _creation.Select(_creation.ClassId, value); Creation(); });
            Label("Unspent points: " + _creation.Remaining, "creation-unspent");
            Label(_creation.TotalPoints + " total points · " + _creation.Minimum + " minimum per attribute", "caption");
            var attributes = new VisualElement(); attributes.AddToClassList("creation-attributes"); _target.Add(attributes);
            var benefits = _progression.Benefits(_creation.Attributes());
            foreach (var attribute in _creation.Attributes().Properties())
            {
                var id = attribute.Name;
                var row = new VisualElement(); row.AddToClassList("creation-attribute"); attributes.Add(row);
                var copy = new VisualElement(); copy.AddToClassList("creation-attribute-copy"); row.Add(copy);
                var title = new Label(AttributeName(id)); title.AddToClassList("creation-attribute-name"); copy.Add(title);
                var benefit = benefits.First(x => (string)x["id"] == id);
                var detail = new Label((string)benefit["perPoint"]); detail.AddToClassList("creation-attribute-benefit"); copy.Add(detail);
                if (benefit["nextAt"] != null) { var milestone = new Label("At " + benefit["nextAt"] + ": " + (string)benefit["milestone"]); milestone.AddToClassList("creation-attribute-benefit"); copy.Add(milestone); }
                var controls = new VisualElement(); controls.AddToClassList("creation-attribute-controls"); row.Add(controls);
                var decrease = MakeButton("attribute-" + id + "-down", "−", () => { _creation.Adjust(id, -1); Creation(); });
                var increase = MakeButton("attribute-" + id + "-up", "+", () => { _creation.Adjust(id, 1); Creation(); });
                decrease.tooltip = "Decrease " + id; increase.tooltip = "Increase " + id;
                decrease.AddToClassList("creation-adjust"); increase.AddToClassList("creation-adjust");
                decrease.SetEnabled(_creation.CanAdjust(id, -1)); increase.SetEnabled(_creation.CanAdjust(id, 1));
                controls.Add(decrease); var amount = new Label(attribute.Value.ToString()); amount.AddToClassList("creation-attribute-value"); controls.Add(amount); controls.Add(increase);
            }
            var resources = _creation.Resources();
            ResourceStrip(_target, resources);
            Label("Base resources before equipment and relic bonuses. " + string.Join(" / ", hero["startingFlaskAllocation"].Children<JProperty>().Select(x => x.Value + " " + x.Name)) + " flask charges.", "caption");
            _target = Section("native-creation-equipment-section", "STARTING EQUIPMENT");
            Label("Opening loadouts", "node-title");
            foreach (JObject kit in kits)
            {
                var selectedKit = (string)kit["id"]; var available = (bool)kit["available"];
                var label = available ? (string)kit["label"] + " · " + OriginalCardText.Humanize((string)kit["rightHand"]) + " / " + (string.IsNullOrEmpty((string)kit["leftHand"]) ? "Empty hand" : OriginalCardText.Humanize((string)kit["leftHand"])) : "Undiscovered loadout";
                var choose = MakeButton("kit-" + selectedKit, label, () => { _kit = selectedKit; _starting.Remove("startingHands"); Creation(); }); choose.SetEnabled(available);
                if (_kit == selectedKit) choose.AddToClassList("primary"); _target.Add(choose);
            }
            if (_start != null)
            {
                foreach (var hand in new[] { "rightHand", "leftHand" })
                {
                    var handId = hand; var rows = options.AvailableHands(_creation.ClassId, handId); rows.Insert(0, new JObject { ["id"] = "", ["name"] = "Empty hand" });
                    StartingChoice("native-start-" + handId, OriginalCardText.Humanize(handId), rows, (string)preview["loadout"]["sets"][handId][0], value =>
                    {
                        var hands = new JObject { ["leftHand"] = preview["loadout"]["sets"]["leftHand"][0].DeepClone(), ["rightHand"] = preview["loadout"]["sets"]["rightHand"][0].DeepClone() };
                        _starting["startingHands"] = OriginalStartingOptions.SelectHand(hands, handId, value); Creation();
                    });
                }
                StartingChoice("native-start-armour", "Armour", options.AvailableArmour(_creation.ClassId, _profileMeta), (string)preview["loadout"]["sets"]["armor"][0], value => { _starting["startingArmourId"] = value; Creation(); });
                StartingChoice("native-start-relic", "Starting relic", options.AvailableRelics(_creation.ClassId), (string)preview["relicId"], value => { _starting["startingRelicId"] = value; Creation(); });
                Label("Starting pools: HP " + preview["resources"]["hp"] + " · MP " + preview["resources"]["mana"] + " · Stamina " + preview["resources"]["stamina"], "stat");
            }
            if (_start != null)
            {
                Button begin = null;
                var shapeValid = true;
                _target = Section("native-creation-appearance-section", "APPEARANCE · Keepsake & custom climb");
                OriginalCustomSetupPanel.Render(_target, _catalog, _setup, _report, RefreshAppearance, valid => { shapeValid = valid; begin?.SetEnabled((bool)preview["canBegin"] && valid); });
                _target = Section("native-creation-seed-section", "SEED");
                var seed = new TextField("Run seed") { name = "native-seed", value = _seed }; seed.AddToClassList("seed-field"); seed.RegisterValueChangedCallback(e => _seed = e.newValue); _target.Add(seed);
                begin = MakeButton("native-begin", "Begin the climb", () =>
                {
                    var player = _builder.Build(_creation, _kit, _profileMeta, _starting);
                    foreach (var property in _setup.Properties()) player[property.Name] = property.Value.DeepClone();
                    _start(player, RandomStreams.ParseSeed(_seed));
                });
                begin.AddToClassList("primary"); begin.SetEnabled((bool)preview["canBegin"] && shapeValid); _root.Add(begin);
            }
            _target = Section("native-creation-cards-section", "YOUR WEAPON CARDS");
            Label("Load " + preview["weight"]["load"] + " / " + preview["weight"]["capacity"] + " · " + (string)preview["weight"]["weightClass"]["id"], "stat");
            foreach (var requirement in preview["requirements"]) Label(requirement["itemId"] + " needs " + requirement["required"] + " " + requirement["attribute"], "notice");
            var cardRail = new VisualElement(); cardRail.AddToClassList("creation-weapon-cards"); _target.Add(cardRail);
            var cardIndex = 0;
            foreach (var group in preview["cards"].GroupBy(x => ((JObject)x["card"]).ToString()))
            {
                var card = (JObject)group.First()["card"];
                var cost = CardMechanics.CostProfile(card, (int?)relic["passives"]?["powerCostReduction"] ?? 0, (JObject)preview["weight"]["weightClass"]);
                var face = new OriginalCardView(_catalog, card, cost, null, false, () => { }, "native-preview-card-" + cardIndex++);
                face.AddToClassList("creation-preview-card"); face.focusable = false;
                var count = new Label(group.Count() + " in starting deck"); count.AddToClassList("creation-card-count"); face.Add(count); cardRail.Add(face);
            }
            Label(hero["cardPool"].Count() + " class reward cards", "caption");
            _target = _root;
            Debug.Log("ASHENSPIRE_FOUNDATION_CREATION " + new JObject { ["classId"] = _creation.ClassId, ["mode"] = _creation.ModeId, ["attributes"] = _creation.Attributes(), ["resources"] = resources, ["remaining"] = _creation.Remaining }.ToString(Newtonsoft.Json.Formatting.None));
            _report();
        }
        private void StartingChoice(string id, string label, JArray rows, string selected, Action<string> changed)
        {
            var values = rows.OfType<JObject>().ToArray(); var labels = values.Select(x => (string)x["label"] ?? (string)x["name"]).ToList();
            var index = Array.FindIndex(values, x => ((string)x["id"] ?? "") == (selected ?? ""));
            var field = new DropdownField(label, labels, Math.Max(0, index)) { name = id }; field.AddToClassList("foundation-field");
            field.RegisterValueChangedCallback(e => changed((string)values[labels.IndexOf(e.newValue)]["id"])); _target.Add(field);
        }
        private void Map()
        {
            Header("ROUTES THROUGH THE SPIRE");
            var seed = new TextField("Seed") { value = _seed, name = "foundation-seed" }; seed.AddToClassList("seed-field"); seed.RegisterValueChangedCallback(e => _seed = e.newValue); _root.Add(seed);
            var body = new VisualElement();
            Button("foundation-generate", "Generate all three acts", Generate); _root.Add(body); Generate();
            void Generate()
            {
                body.Clear();
                try
                {
                    var value = RandomStreams.ParseSeed(_seed); var random = new RandomStreams(value);
                    foreach (var config in ((JObject)_catalog.Data()["mapConfigs"]).Properties())
                    {
                        var graph = ActMapGenerator.Generate((JObject)config.Value, random);
                        var title = new Label("Act " + config.Name + " · " + RandomStreams.DisplaySeed(value)); title.AddToClassList("node-title"); body.Add(title);
                        var route = new VisualElement(); body.Add(route); var visited = new List<string>(); Show((JArray)graph["startIds"]);
                        void Show(JArray ids)
                        {
                            route.Clear(); var path = new Label(visited.Count == 0 ? "Choose an entrance" : "Path: " + string.Join(" → ", visited.Select(id => (string)graph["nodes"][id]["type"]))); path.AddToClassList("caption"); route.Add(path);
                            foreach (var token in ids)
                            {
                                var id = (string)token; var node = graph["nodes"][id];
                                route.Add(MakeButton("route-" + config.Name + "-" + id, "Floor " + node["floor"] + " · " + node["type"], () => { visited.Add(id); Show((JArray)node["next"]); _report(); }));
                            }
                            if (ids.Count == 0) { var end = new Label("Reached this act's boss. Route preview complete."); end.AddToClassList("notice"); route.Add(end); }
                        }
                        Debug.Log("ASHENSPIRE_FOUNDATION_MAP " + new JObject { ["act"] = config.Name, ["seed"] = value, ["graph"] = graph }.ToString(Newtonsoft.Json.Formatting.None));
                    }
                }
                catch (ArgumentException error) { var text = new Label(error.Message); text.AddToClassList("notice"); body.Add(text); }
                _report();
            }
        }
        private void Catalog()
        {
            Header("ASHEN SPIRE COMPENDIUM");
            var names = new List<string> { "cards", "classes", "relics", "statuses", "stances", "enemies", "encounters", "events", "flasks", "equipment.armaments", "equipment.armour", "equipment.startingKits", "unlocks" };
            Select("foundation-table", "Table", names, _table, value => { _table = value; Catalog(); });
            var query = new TextField("Find name or ID") { value = _search, name = "foundation-search" }; query.AddToClassList("seed-field"); query.RegisterValueChangedCallback(e => _search = e.newValue); _root.Add(query);
            var entries = new VisualElement(); Button("foundation-find", "Find content", Render); _root.Add(entries); Render();
            void Render()
            {
                entries.Clear(); var all = _catalog.Table(_table);
                var matches = all.OfType<JObject>().Where(row => string.IsNullOrWhiteSpace(_search) || ((string)row["name"] ?? (string)row["label"] ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 || ((string)row["id"] ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                var count = new Label(matches.Length + " matches · " + all.Count + " records"); count.AddToClassList("notice"); entries.Add(count);
                foreach (var row in matches.Take(30)) entries.Add(MakeButton("record-" + (string)row["id"] + "-" + (string)row["classId"], (string)row["name"] ?? (string)row["label"] ?? (string)row["id"], () => Detail(row)));
                if (matches.Length > 30) { var hint = new Label("Showing the first 30. Narrow the search to find more."); hint.AddToClassList("caption"); entries.Add(hint); }
                _report();
            }
        }
        private void Detail(JObject row)
        {
            Header((string)row["name"] ?? (string)row["label"] ?? (string)row["id"]);
            Label((string)row["description"] ?? (string)row["textTemplate"] ?? (string)row["text"] ?? (string)row["flavor"] ?? "", "lead");
            var json = new TextField("Original record") { value = row.ToString(), multiline = true, isReadOnly = true, name = "foundation-record" }; json.AddToClassList("report-field"); json.AddToClassList("foundation-field"); _root.Add(json);
            Button("foundation-record-back", "Back to results", Catalog); _report();
        }
        private void Select(string id, string label, List<string> choices, string current, Action<string> action)
        {
            var field = new DropdownField(label, choices, choices.IndexOf(current)) { name = id }; field.style.minHeight = 52;
            field.AddToClassList("foundation-field");
            field.RegisterValueChangedCallback(e => action(e.newValue)); _root.Add(field);
        }
        private void Choices(string id, string label, JArray choices, string current, Action<string> action)
        {
            Label(label, "node-title"); var row = new VisualElement(); row.AddToClassList("foundation-choices"); _target.Add(row);
            foreach (var record in choices)
            {
                var value = (string)record["id"];
                var button = MakeButton(id + "-" + value, (string)record["name"] ?? (string)record["label"] ?? value, () => action(value));
                if (value == current) button.AddToClassList("primary"); row.Add(button);
            }
        }
        private VisualElement Section(string id, string title)
        {
            var section = new Foldout { name = id, text = title, value = !_sections.TryGetValue(id, out var open) || open };
            section.AddToClassList("creation-section"); section.Q<Toggle>().name = id + "-toggle";
            section.RegisterValueChangedCallback(e => { if (e.target != section) return; _sections[id] = e.newValue; _report(); });
            _root.Add(section); return section.contentContainer;
        }
        private static string AttributeName(string id)
        {
            switch (id) { case "strength": return "STR · Strength"; case "dexterity": return "DEX · Dexterity"; case "constitution": return "CON · Constitution"; case "wisdom": return "WIS · Wisdom"; case "intelligence": return "INT · Intelligence"; default: return OriginalCardText.Humanize(id); }
        }
        private static string RelicDescription(JObject relic)
        {
            // Bind only literal authored values. This is display data, not trigger
            // execution; unsupported tokens remain visible instead of guessing.
            var effects = new JArray((relic["triggers"] as JArray ?? new JArray()).SelectMany(trigger => trigger["do"] as JArray ?? new JArray()).Select(effect => effect.DeepClone()));
            var values = OriginalCardText.StaticTokens(new JObject { ["effects"] = effects });
            foreach (var modifier in relic["passives"]?["modifiers"] as JArray ?? new JArray())
            {
                var key = (string)modifier["tag"] == "resource.flat" ? (string)modifier["resource"] + "Flat" : (string)modifier["tag"] == "damage.school.flat" ? (string)modifier["school"] + "DamageFlat" : null;
                if (key != null && (modifier["amount"]?.Type == JTokenType.Integer || modifier["amount"]?.Type == JTokenType.Float)) values[key] = ((double?)values[key] ?? 0) + (double)modifier["amount"];
            }
            var text = (string)relic["textTemplate"] ?? (string)relic["description"] ?? (string)relic["text"] ?? (string)relic["flavor"] ?? "";
            foreach (var value in values.Properties()) text = text.Replace("{" + value.Name + "}", Convert.ToString(((JValue)value.Value).Value, System.Globalization.CultureInfo.InvariantCulture));
            return text;
        }
        private static void ResourceStrip(VisualElement parent, JObject resources)
        {
            var strip = new VisualElement(); strip.AddToClassList("creation-resources"); parent.Add(strip);
            foreach (var resource in resources.Properties())
            {
                var name = resource.Name == "energy" ? "ACTIONS" : resource.Name.ToUpperInvariant();
                var item = new Label(name + "  " + resource.Value); item.AddToClassList("creation-resource"); strip.Add(item);
            }
        }
        private void Label(string text, string style) { var label = new Label((text ?? "").Replace("—", " - ").Replace("–", "-")); label.AddToClassList(style); _target.Add(label); }
        private void Button(string id, string text, Action action) => _target.Add(MakeButton(id, text, action));
        private static Button MakeButton(string id, string text, Action action) { var button = new Button(action) { text = text, name = id }; button.AddToClassList("button"); return button; }
    }
}
