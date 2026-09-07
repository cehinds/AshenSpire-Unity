// OriginalCustomSetupPanel.cs — editable setup choices for the original climb.
// Mount inside the creator. The caller owns the draft; domain initialization
// validates and freezes it only when Begin is pressed. No save or RNG writes here.
// Labels come from Original/custom-run-options; magnitudes remain authored balance.
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public static class OriginalCustomSetupPanel
    {
        public static void Render(VisualElement root, OriginalContentCatalog catalog, JObject setup, Action report, Action appearanceChanged = null, Action<bool> shapeValidityChanged = null)
        {
            var options = JObject.Parse(Resources.Load<TextAsset>("Original/custom-run-options").text);
            var custom = (JObject)setup["custom"]; var cosmetic = (JObject)setup["customization"];
            var name = new TextField("Wanderer name") { name = "native-name", value = (string)cosmetic["name"], maxLength = 128 };
            name.AddToClassList("seed-field"); name.RegisterValueChangedCallback(e => { cosmetic["name"] = e.newValue; appearanceChanged?.Invoke(); }); root.Add(name);
            var appearance = OriginalAppearance.Options;
            Choice("native-tint", "Portrait tint", (JArray)appearance["tints"], (string)cosmetic["tint"], value => { cosmetic["tint"] = value; appearanceChanged?.Invoke(); });
            Choice("native-sigil", "Sigil", (JArray)appearance["sigils"], (string)cosmetic["glyph"], value => { cosmetic["glyph"] = value; appearanceChanged?.Invoke(); });
            Choice("native-sprite-style", "Sprite style", OriginalPlayerFigure.Catalog.Styles, (string)cosmetic["spriteStyle"] ?? "animated", value => { cosmetic["spriteStyle"] = value; appearanceChanged?.Invoke(); });
            Choice("native-keepsake", "Keepsake", (JArray)catalog.Data()["characterCreation"]["keepsakes"], (string)setup["keepsakeId"], value => setup["keepsakeId"] = value);
            var foldout = new Foldout { text = "Custom climb", value = false, name = "native-custom" }; root.Add(foldout);
            foldout.Q<Toggle>().name = "native-custom-toggle";
            var explanation = new Label("Custom climbs keep original unlock progression and have separate win-rate records."); explanation.AddToClassList("caption"); foldout.Add(explanation);
            var ascension = new DropdownField("Ascension", Enumerable.Range(0, 7).Select(x => x.ToString()).ToList(), (int)custom["ascension"]) { name = "native-ascension" };
            ascension.AddToClassList("foundation-field"); ascension.RegisterValueChangedCallback(e => { custom["ascension"] = int.Parse(e.newValue); report(); }); foldout.Add(ascension);
            Choice("native-deck-mode", "Starting deck", (JArray)options["deckModes"], (string)custom["deckMode"], value => custom["deckMode"] = value, foldout);
            foreach (var option in options["difficulty"].Concat(options["chaos"]))
            {
                var id = (string)option["id"]; var toggle = new Toggle((string)option["label"]) { name = "native-mod-" + id, value = (bool?)custom["mods"][id] == true };
                toggle.AddToClassList("setting"); toggle.RegisterValueChangedCallback(e => { custom["mods"][id] = e.newValue; report(); }); foldout.Add(toggle);
                var description = new Label((string)option["desc"]); description.AddToClassList("caption"); foldout.Add(description);
            }
            OriginalMapShapePanel.Render(foldout, catalog, setup, options, report, shapeValidityChanged);
            foldout.RegisterValueChangedCallback(_ => report());
            void Choice(string id, string label, JArray rows, string selected, Action<string> changed, VisualElement parent = null)
            {
                var items = rows.OfType<JObject>().ToArray(); var labels = items.Select(x => (string)x["label"] ?? (string)x["name"] ?? (string)x["id"]).ToList();
                var index = Array.FindIndex(items, x => (string)x["id"] == selected);
                var field = new DropdownField(label, labels, Math.Max(0, index)) { name = id }; field.AddToClassList("foundation-field");
                field.RegisterValueChangedCallback(e => { changed((string)items[labels.IndexOf(e.newValue)]["id"]); report(); }); (parent ?? root).Add(field);
            }
        }
    }
}
