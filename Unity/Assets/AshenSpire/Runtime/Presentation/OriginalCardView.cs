// OriginalCardView.cs — original HTML card anatomy, shared by native game views.
// WIRING: attach OriginalCards.uss once to the screen, then construct with the
// resolved card/cost and current player. This component never resolves or spends.
// MODIFY: card icons/tags/type labels/colors in Original/content.json; geometry in
// OriginalCards.uss. Anatomy follows pinned ui/components/card.js and kit.css.
// FONT: Fonts/GlyphFonts.json maps complete authored Unicode strings to bundled
// static monochrome fonts, including supplementary codepoints. Update that map
// after changing icons/tags; see Fonts/README.md. A caller may override card art.
// ACCESS: FullText exposes every label; the same text is the tooltip. Visible
// descriptions and tags wrap without clipping, so touch users lose no details.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OriginalCardView : Button
    {
        // Cache only presentation metadata per immutable/frozen catalog. Do not
        // retain a whole cloned content bundle for every hand card and redraw.
        private sealed class Metadata
        {
            internal readonly JObject Types;
            internal readonly Dictionary<string, JObject> Tags;
            internal Metadata(OriginalContentCatalog catalog)
            {
                var data = catalog.Data();
                Types = (JObject)data["balance"]?["ui"]?["cardTypes"]?.DeepClone() ?? new JObject();
                Tags = ((JArray)data["tags"]).OfType<JObject>().ToDictionary(x => (string)x["id"], x => (JObject)x.DeepClone());
            }
        }
        private static readonly ConditionalWeakTable<OriginalContentCatalog, Metadata> MetadataByCatalog = new ConditionalWeakTable<OriginalContentCatalog, Metadata>();
        private static JObject _glyphFonts;
        private static readonly Dictionary<string, Font> GlyphFontCache = new Dictionary<string, Font>(StringComparer.Ordinal);
        public string FullText { get; }
        public Label Art { get; }

        public OriginalCardView(OriginalContentCatalog catalog, JObject card, JObject cost,
            JObject player, bool selected, Action clicked, string controlId, Font glyphFont = null) : base(clicked)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            name = controlId; AddToClassList("card"); AddToClassList("original-card");
            if (selected) AddToClassList("selected");
            var metadata = MetadataByCatalog.GetValue(catalog, c => new Metadata(c));
            var typeId = (string)card["type"] ?? "";
            var type = metadata.Types[typeId] as JObject;
            var typeName = (string)type?["label"] ?? OriginalCardText.Humanize(typeId);
            var rarity = (string)card["rarity"] ?? "common";
            AddToClassList("rarity-" + rarity); AddToClassList("type-" + typeId);
            if ((bool?)card["upgraded"] == true || ((string)card["name"] ?? "").EndsWith("+", StringComparison.Ordinal)) AddToClassList("upgraded");

            var title = (string)card["name"] ?? (string)card["id"];
            var costText = OriginalCardCostText.Describe(cost);
            var description = OriginalCardText.Describe(card, catalog);
            var shortage = player == null ? null : OriginalCardCostText.Shortage(cost, player);
            if (shortage != null) AddToClassList("unaffordable");

            var medallion = Text((bool?)cost["variable"] == true ? "X" : cost["action"].ToString(), "cost", "original-card-action");
            medallion.tooltip = costText; Add(medallion);
            if ((int)cost["mana"] > 0) Add(Text("◆ " + cost["mana"], "original-card-mana"));
            if ((int)cost["stamina"] > 0) Add(Text("● " + cost["stamina"], "original-card-stamina"));
            Add(Text(title, "card-name", "original-card-name"));
            Art = Text((string)card["icon"] ?? "❖", "original-card-art");
            if (glyphFont != null) Art.style.unityFont = glyphFont;
            else ApplyGlyphFont(Art, (string)card["icon"] ?? "❖");
            Add(Art);
            var band = Text(typeName, "original-card-type");
            if (TryColor((string)type?["color"], out var typeColor))
            {
                band.style.color = typeColor;
                band.style.backgroundColor = Color.Lerp(new Color32(42, 36, 28, 255), typeColor, .15f);
            }
            Add(band);
            // Explicit empty profile tags remain empty; an equipped attack must
            // never accidentally inherit the base card's unrelated tag junction.
            var ids = card["cardTags"] is JArray explicitTags ? explicitTags.Values<string>() : catalog.Tags("card", card);
            var tagNames = new List<string>(); var tagRow = new VisualElement(); tagRow.AddToClassList("original-card-tags"); tagRow.pickingMode = PickingMode.Ignore;
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
            {
                if (!metadata.Tags.TryGetValue(id, out var tag)) continue;
                var label = (string)tag["label"] ?? id; tagNames.Add(label);
                // Keep the symbol and Latin copy separate: an emoji-only font
                // must never be applied to the readable tag name beside it.
                var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                chip.AddToClassList("card-tags"); chip.AddToClassList("original-card-tag");
                chip.style.flexDirection = FlexDirection.Row; chip.style.alignItems = Align.Center;
                var glyph = (string)tag["glyph"] ?? "";
                if (glyph.Length > 0)
                {
                    var symbol = Text(glyph); ApplyGlyphFont(symbol, glyph); symbol.style.marginRight = 3; chip.Add(symbol);
                }
                var tagCopy = Text(label); tagCopy.style.flexShrink = 1; tagCopy.style.minWidth = 0; chip.Add(tagCopy);
                chip.tooltip = (string)tag["blurb"];
                if (TryColor((string)tag["color"], out var color))
                {
                    chip.style.color = color; var edge = new Color(color.r, color.g, color.b, .45f);
                    chip.style.borderTopColor = edge; chip.style.borderBottomColor = edge; chip.style.borderLeftColor = edge; chip.style.borderRightColor = edge;
                    chip.style.backgroundColor = new Color(color.r, color.g, color.b, .12f);
                }
                tagRow.Add(chip);
            }
            if (tagNames.Count > 0) Add(tagRow);
            Add(Text(description, "card-description", "original-card-description"));
            if (shortage != null) Add(Text(shortage, "card-shortage", "original-card-shortage"));
            FullText = title + ". " + typeName + ". " + costText + ". " +
                (tagNames.Count == 0 ? "" : string.Join(", ", tagNames) + ". ") + description +
                (shortage == null ? "" : " " + shortage);
            tooltip = FullText;
        }

        private static void ApplyGlyphFont(Label label, string glyph)
        {
            if (_glyphFonts == null)
            {
                var asset = Resources.Load<TextAsset>("Fonts/GlyphFonts");
                if (asset == null) throw new InvalidOperationException("The authored glyph font map is missing.");
                _glyphFonts = (JObject)JObject.Parse(asset.text)["mapping"];
            }
            var resource = (string)_glyphFonts[glyph];
            // Preserve new authored content verbatim until its font-map coverage
            // is regenerated; never replace an unfamiliar symbol with new art.
            if (resource == null) return;
            if (!GlyphFontCache.TryGetValue(resource, out var font))
            {
                font = Resources.Load<Font>(resource);
                if (font == null) throw new InvalidOperationException("An authored glyph font is missing: " + resource);
                GlyphFontCache.Add(resource, font);
            }
            label.style.unityFont = font;
        }

        private static Label Text(string value, params string[] classes)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore, enableRichText = false };
            foreach (var className in classes) label.AddToClassList(className);
            return label;
        }
        private static bool TryColor(string value, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(value)) return false;
            return ColorUtility.TryParseHtmlString(value[0] == '#' ? value : "#" + value, out color);
        }
    }
}
