// OriginalFoundationPanel.Review.cs — JS build 1815 creator details (customize.js, 02c-02e):
// the header's List/Grid control, portrait button and close X, and the Review step's
// document tiles (CHARACTER sheet, ARMOUR, MAIN HAND, RELIC) and starting deck.
// Presentation only: reads the same builder preview the step already shows; no rules,
// no save writes. Styles in JsEntry.uss. New control ids: native-creation-view-list,
// native-creation-view-grid, native-creation-portrait, native-creation-exit (not "-close":
// gamepad B would take a "-close" control for Back), native-review-card-<n>.
using System;
using System.Globalization;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class OriginalFoundationPanel
    {
        private bool _classGrid;

        private void HeaderTools(VisualElement header, Action appearance)
        {
            var tools = new VisualElement(); tools.AddToClassList("js-creation-tools"); header.Add(tools);
            if (_step == 0)
            {
                // JS .cz-viewtoggle: a two-segment List / Grid control; the chosen segment is gold.
                var toggle = new VisualElement(); toggle.AddToClassList("js-view-toggle"); tools.Add(toggle);
                foreach (var grid in new[] { false, true })
                {
                    var mode = grid;
                    var segment = MakeButton(grid ? "native-creation-view-grid" : "native-creation-view-list", grid ? "Grid" : "List", () => { if (_classGrid == mode) return; _classGrid = mode; Creation(); });
                    segment.AddToClassList("js-view-segment"); segment.EnableInClassList("js-view-segment--first", !grid); segment.EnableInClassList("js-view-segment--on", _classGrid == grid);
                    segment.tooltip = grid ? "Show classes as tiles" : "Show classes as a list";
                    toggle.Add(segment);
                }
            }
            // JS: the round class portrait opens the character's look (here: appearance & custom climb).
            var portrait = MakeButton("native-creation-portrait", "", appearance); portrait.AddToClassList("js-creation-portrait");
            portrait.tooltip = "Appearance and custom climb";
            var face = new OriginalPlayerFigure(_setup); face.UseOwnerBattleArt(_creation.ClassId); face.pickingMode = PickingMode.Ignore; face.AddToClassList("js-creation-portrait-figure");
            portrait.Add(face); tools.Add(portrait);
            var exit = MakeButton("native-creation-exit", "", _back); exit.AddToClassList("js-creation-icon-button"); exit.tooltip = "Back to title";
            var close = Resources.Load<Texture2D>("Art/js1815/ui/icon-close");
            if (close != null) exit.Add(new Image { image = close, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
            else exit.text = "×";
            tools.Add(exit);
        }

        private static Label ReviewText(string text, string style) { var label = new Label(text ?? "") { pickingMode = PickingMode.Ignore }; label.AddToClassList(style); return label; }

        private void ReviewDocuments(VisualElement body, JObject preview, JObject hero, JObject relic, Func<string, string> item)
        {
            var row = new VisualElement(); row.AddToClassList("js-review-documents"); body.Add(row);
            VisualElement Document(string micro, string style)
            {
                var column = new VisualElement { pickingMode = PickingMode.Ignore }; column.AddToClassList("js-review-document"); row.Add(column);
                column.Add(ReviewText(micro, "js-review-micro"));
                var tile = new VisualElement { pickingMode = PickingMode.Ignore }; tile.AddToClassList("js-review-tile"); tile.AddToClassList(style); column.Add(tile);
                return tile;
            }
            // CHARACTER: class in muted caps, the wanderer's name in gold serif, attributes, then pools.
            var sheet = Document("CHARACTER", "js-review-sheet");
            sheet.Add(ReviewText(((string)hero["name"] ?? "").ToUpperInvariant(), "js-review-sheet-class"));
            sheet.Add(ReviewText((string)_setup["customization"]?["name"] ?? "Forsaken", "js-review-sheet-name"));
            Grid(sheet, _creation.Attributes().Properties().Select(p => (Short(p.Name), Number(p.Value))));
            var resources = (JObject)preview["resources"];
            Grid(sheet, resources.Properties().Select(p => (ResourceName(p.Name), Number(p.Value))));
            // ARMOUR: the chosen armour worn by the class figure, its poise and blurb.
            var armourId = (string)preview["loadout"]?["sets"]?["armor"]?[0];
            var armour = Safe(() => _catalog.Record("equipment.armour", armourId, _creation.ClassId));
            var armourTile = Document("ARMOUR", "js-review-item");
            Item(armourTile, (string)armour?["name"] ?? "Armour", () =>
            {
                var figure = new OriginalPlayerFigure(_setup); figure.pickingMode = PickingMode.Ignore;
                figure.Configure(_creation.ClassId, (JObject)_setup["customization"], OriginalPlayerFigure.ActiveArmour((JObject)preview["loadout"])); return figure;
            }, new[] { ("POISE", (string)armour?["poiseThreshold"] ?? "—") }, (string)armour?["blurb"], "ARMOR SET");
            // MAIN HAND: the right-hand armament, its ratings and requirement.
            var weaponId = (string)preview["loadout"]?["sets"]?["rightHand"]?[0];
            var weapon = string.IsNullOrEmpty(weaponId) ? null : Safe(() => _catalog.Record("equipment.armaments", weaponId));
            var stats = new System.Collections.Generic.List<(string, string)>();
            if (weapon?["attackRating"] != null) stats.Add(("AR", (string)weapon["attackRating"]));
            if (weapon?["poiseThreshold"] != null) stats.Add(("POISE", (string)weapon["poiseThreshold"]));
            foreach (var requirement in (weapon?["requirements"]?["attributes"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                stats.Add((requirement.Name.ToUpperInvariant(), (string)requirement.Value));
            Item(Document("MAIN HAND", "js-review-item"), item("rightHand"), () => Art("weapon_" + weaponId), stats.ToArray(),
                (string)weapon?["blurb"], ((string)weapon?["rarity"] ?? "").ToUpperInvariant());
            // RELIC: art, the bound description, STARTER.
            Item(Document("RELIC", "js-review-item"), (string)relic["name"], () => Art((string)preview["relicId"]), Array.Empty<(string, string)>(),
                RelicDescription(relic), "STARTER");
        }

        private void ReviewDeck(VisualElement body, JObject preview, JObject relic)
        {
            body.Add(ReviewText("STARTING DECK", "js-review-micro"));
            var deck = new VisualElement(); deck.AddToClassList("js-review-deck"); body.Add(deck);
            var index = 0;
            foreach (var group in preview["cards"].GroupBy(x => ((JObject)x["card"]).ToString()))
            {
                var card = (JObject)group.First()["card"];
                var cost = CardMechanics.CostProfile(card, (int?)relic["passives"]?["powerCostReduction"] ?? 0, (JObject)preview["weight"]["weightClass"]);
                var holder = new VisualElement { pickingMode = PickingMode.Ignore }; holder.AddToClassList("js-review-deck-card"); deck.Add(holder);
                var face = new OriginalCardView(_catalog, card, cost, null, false, () => { }, "native-review-card-" + index++);
                face.AddToClassList("creation-preview-card"); face.focusable = false; holder.Add(face);
                holder.Add(ReviewText("× " + group.Count(), "js-review-deck-count"));
            }
        }

        private static void Grid(VisualElement tile, System.Collections.Generic.IEnumerable<(string Name, string Value)> rows)
        {
            var grid = new VisualElement { pickingMode = PickingMode.Ignore }; grid.AddToClassList("js-review-grid"); tile.Add(grid);
            foreach (var (name, value) in rows)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore }; cell.AddToClassList("js-review-cell"); grid.Add(cell);
                cell.Add(ReviewText(name, "js-review-cell-name")); cell.Add(ReviewText(value, "js-review-cell-value"));
            }
        }

        private static void Item(VisualElement tile, string title, Func<VisualElement> art, (string Name, string Value)[] stats, string text, string footer)
        {
            tile.Add(ReviewText(title, "js-review-item-title"));
            var window = new VisualElement { pickingMode = PickingMode.Ignore }; window.AddToClassList("js-review-item-art"); tile.Add(window);
            var picture = Safe(art); if (picture != null) { picture.AddToClassList("js-review-item-picture"); window.Add(picture); }
            if (stats.Length > 0)
            {
                var strip = new VisualElement { pickingMode = PickingMode.Ignore }; strip.AddToClassList("js-review-item-stats"); tile.Add(strip);
                foreach (var (name, value) in stats)
                {
                    var stat = new VisualElement { pickingMode = PickingMode.Ignore }; stat.AddToClassList("js-review-item-stat"); strip.Add(stat);
                    stat.Add(ReviewText(value, "js-review-item-stat-value")); stat.Add(ReviewText(name, "js-review-item-stat-name"));
                }
            }
            if (!string.IsNullOrEmpty(text)) tile.Add(ReviewText(text, "js-review-item-text"));
            if (!string.IsNullOrEmpty(footer)) tile.Add(ReviewText(footer, "js-review-item-footer"));
        }

        // The item's visible pixels (PlayerComponentArt applies item-bounds); its caption is hidden in USS.
        private static VisualElement Art(string resource) => string.IsNullOrEmpty(resource) ? null : PlayerComponentArt.Item(resource, "");
        private static T Safe<T>(Func<T> read) where T : class { try { return read(); } catch (Exception) { return null; } }
        private static string Number(JToken value) => value == null ? "—" : Convert.ToString(((JValue)value).Value, CultureInfo.InvariantCulture);
        private static string Short(string attribute)
        {
            switch (attribute) { case "strength": return "STR"; case "dexterity": return "DEX"; case "constitution": return "CON"; case "wisdom": return "WIS"; case "intelligence": return "INT"; default: return attribute.ToUpperInvariant(); }
        }
        private static string ResourceName(string resource)
        {
            switch (resource) { case "hp": return "HP"; case "mana": return "MP"; case "stamina": return "SP"; case "energy": return "ACTIONS"; default: return resource.ToUpperInvariant(); }
        }
    }
}
