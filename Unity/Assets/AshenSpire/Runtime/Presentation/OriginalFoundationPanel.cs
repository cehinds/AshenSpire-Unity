// OriginalFoundationPanel.cs — development-only owner preview of original systems.
// WIRING: CampaignView mounts this panel; no scene/Inspector setup. No save writes.
// DATA: OriginalContentCatalog. RULES: CreationModel and ActMapGenerator.
// MODIFY: add view tabs here; resource/map/status math belongs in Original components.
// LIFETIME: callbacks belong to this tree; no global events, timers or Update.
// Imported content is inspectable here; this is not yet a complete parity campaign.
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
        private readonly OriginalContentCatalog _catalog;
        private readonly Action _report, _back;
        private readonly CreationModel _creation;
        private string _seed = "ASHEN", _table = "cards", _search = "";
        public OriginalFoundationPanel(VisualElement root, OriginalContentCatalog catalog, Action report, Action back)
        {
            _root = root; _catalog = catalog; _report = report; _back = back;
            _creation = new CreationModel(catalog, "reaver", (string)catalog.Data()["attributeRules"]["defaultMode"]);
            Creation();
        }
        private void Header(string title)
        {
            _root.Clear(); Label(title, "heading");
            Label("Original foundation preview · campaign integration is in progress.", "caption");
            var tabs = new VisualElement(); tabs.AddToClassList("foundation-tabs"); _root.Add(tabs);
            tabs.Add(MakeButton("foundation-creation", "Wanderer", Creation));
            tabs.Add(MakeButton("foundation-map", "Routes", Map));
            tabs.Add(MakeButton("foundation-catalog", "Content", Catalog));
            Button("foundation-back", "Back to title", _back);
        }
        private void Creation()
        {
            Header("THE ORIGINAL WANDERERS");
            Choices("foundation-class", "Class", _catalog.Table("classes"), _creation.ClassId, value => { _creation.Select(value, _creation.ModeId); Creation(); });
            Choices("foundation-mode", "Allocation", _catalog.Table("creationModes"), _creation.ModeId, value => { _creation.Select(_creation.ClassId, value); Creation(); });
            Label("Unspent points: " + _creation.Remaining, "notice");
            Label(_creation.TotalPoints + " total points · " + _creation.Minimum + " minimum per attribute", "caption");
            var hero = _catalog.Record("classes", _creation.ClassId);
            var portrait = new Image { image = Resources.Load<Texture2D>("Art/" + _creation.ClassId + "_idle"), scaleMode = ScaleMode.ScaleToFit }; portrait.AddToClassList("portrait"); _root.Add(portrait);
            Label((string)hero["name"], "node-title"); Label((string)hero["description"], "lead");
            foreach (var attribute in _creation.Attributes().Properties())
            {
                var id = attribute.Name; Label(id + "  " + attribute.Value, "stat");
                var row = new VisualElement(); row.AddToClassList("stats"); _root.Add(row);
                var decrease = MakeButton("attribute-" + id + "-down", "- " + id, () => { _creation.Adjust(id, -1); Creation(); });
                var increase = MakeButton("attribute-" + id + "-up", "+ " + id, () => { _creation.Adjust(id, 1); Creation(); });
                decrease.SetEnabled(_creation.CanAdjust(id, -1)); increase.SetEnabled(_creation.CanAdjust(id, 1));
                row.Add(decrease); row.Add(increase);
            }
            var resources = _creation.Resources();
            Label(string.Join(" · ", resources.Properties().Select(x => (x.Name == "energy" ? "Actions" : x.Name.ToUpperInvariant()) + " " + x.Value)), "lead");
            Label("Base resources before equipment and relic bonuses. " + string.Join(" / ", hero["startingFlaskAllocation"].Children<JProperty>().Select(x => x.Value + " " + x.Name)) + " flask charges.", "caption");
            Label("Opening loadouts", "node-title");
            foreach (var kitId in hero["eligibleStartingKitIds"])
            {
                var kit = _catalog.Record("equipment.startingKits", (string)kitId);
                Label((string)kit["label"] + " · " + (string)kit["rightHand"] + " / " + (string)kit["leftHand"], "caption");
            }
            Label(hero["cardPool"].Count() + " class reward cards", "caption");
            Debug.Log("ASHENSPIRE_FOUNDATION_CREATION " + new JObject { ["classId"] = _creation.ClassId, ["mode"] = _creation.ModeId, ["attributes"] = _creation.Attributes(), ["resources"] = resources, ["remaining"] = _creation.Remaining }.ToString(Newtonsoft.Json.Formatting.None));
            _report();
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
            Label(label, "node-title"); var row = new VisualElement(); row.AddToClassList("foundation-choices"); _root.Add(row);
            foreach (var record in choices)
            {
                var value = (string)record["id"];
                var button = MakeButton(id + "-" + value, (string)record["name"] ?? (string)record["label"] ?? value, () => action(value));
                if (value == current) button.AddToClassList("primary"); row.Add(button);
            }
        }
        private void Label(string text, string style) { var label = new Label(text.Replace("—", " - ").Replace("–", "-")); label.AddToClassList(style); _root.Add(label); }
        private void Button(string id, string text, Action action) => _root.Add(MakeButton(id, text, action));
        private static Button MakeButton(string id, string text, Action action) { var button = new Button(action) { text = text, name = id }; button.AddToClassList("button"); return button; }
    }
}
