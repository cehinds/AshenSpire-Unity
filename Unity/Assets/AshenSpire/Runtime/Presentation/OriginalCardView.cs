// OriginalCardView.cs — original HTML card anatomy, shared by native game views.
// WIRING: attach OriginalCards.uss once to the screen, then construct with the
// resolved card/cost and current player. This component never resolves or spends.
// MODIFY: card icons/tags/type labels/colors in Original/content.json; geometry in
// OriginalCards.uss. Anatomy follows pinned ui/components/card.js and kit.css.
// FONT: Fonts/GlyphFonts.json maps complete authored Unicode strings to bundled
// static monochrome fonts, including supplementary codepoints. Update that map
// after changing icons/tags; see Fonts/README.md. A caller may override card art.
// MOTIF: the class cardTint layer and rarity pip follow the root card-motif-* classes (US-15.1).
// ACCESS: FullText exposes every label and tag blurb; the same text is the tooltip.
// Tag chips open their blurb on hover or long press (EnemyTelegraphView.Explain), and
// pile inspection lists the blurbs, so touch users lose no details (US-13.4).
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
            /// <summary>Class id → its cardTint (the card motif hue). Colorless cards have none.</summary>
            internal readonly Dictionary<string, Color> Tints = new Dictionary<string, Color>(StringComparer.Ordinal);
            internal Metadata(OriginalContentCatalog catalog)
            {
                var data = catalog.Data();
                foreach (var klass in (data["classes"] as JArray ?? new JArray()).OfType<JObject>())
                    if ((string)klass["id"] is string id && TryColor((string)klass["cardTint"], out var tint)) Tints[id] = tint;
                Types = (JObject)data["balance"]?["ui"]?["cardTypes"]?.DeepClone() ?? new JObject();
                Tags = ((JArray)data["tags"]).OfType<JObject>().ToDictionary(x => (string)x["id"], x => (JObject)x.DeepClone());
            }
        }
        private static readonly ConditionalWeakTable<OriginalContentCatalog, Metadata> MetadataByCatalog = new ConditionalWeakTable<OriginalContentCatalog, Metadata>();
        private static JObject _glyphFonts;
        private static readonly Dictionary<string, Font> GlyphFontCache = new Dictionary<string, Font>(StringComparer.Ordinal);
        public string FullText { get; }
        public Label Art { get; }
        internal Func<OriginalCardView> Ghost { get; }
        private bool _suppressClick;

        public OriginalCardView(OriginalContentCatalog catalog, JObject card, JObject cost,
            JObject player, bool selected, Action clicked, string controlId, Font glyphFont = null, Action inspect = null, Func<Vector2, bool, bool> drop = null, Action<Vector2?, bool> aim = null) : base()
        {
            this.clicked += () => { if (!_suppressClick) clicked?.Invoke(); };
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
            // Card motif (Settings → Card motif): one unpickable layer behind the face, tinted with the
            // owning class's cardTint. OriginalCards.uss shapes it per root class card-motif-* and sets
            // its opacity per motif-strength-*; the rarity pip shows in Accent mode only.
            var classId = (string)card["class"];
            if (classId != null && metadata.Tints.TryGetValue(classId, out var classTint))
            {
                var motif = new VisualElement { pickingMode = PickingMode.Ignore }; motif.AddToClassList("original-card-motif");
                motif.style.backgroundColor = classTint; Add(motif); AddToClassList("has-class-tint");
            }
            var pip = new VisualElement { pickingMode = PickingMode.Ignore }; pip.AddToClassList("original-card-pip"); Add(pip);
            AddToClassList("class-" + ((string)card["class"] ?? "neutral"));
            if ((bool?)card["upgraded"] == true || ((string)card["name"] ?? "").EndsWith("+", StringComparison.Ordinal)) AddToClassList("upgraded");

            var title = (string)card["name"] ?? (string)card["id"];
            var costText = OriginalCardCostText.Describe(cost);
            var description = OriginalCardText.Describe(card, catalog);
            var shortage = player == null ? null : OriginalCardCostText.Shortage(cost, player);
            if (shortage != null) AddToClassList("unaffordable");

            var medallion = Text((bool?)cost["variable"] == true ? "X" : cost["action"].ToString(), "cost", "original-card-action");
            medallion.tooltip = costText; Add(medallion);
            if ((int)cost["mana"] > 0) Add(Text("◆ " + cost["mana"], "original-card-mana"));
            if (!OriginalCardCostText.UsesTurnStamina(cost) && (int)cost["stamina"] > 0) Add(Text("● " + cost["stamina"], "original-card-stamina"));
            Add(Text(title, "card-name", "original-card-name"));
            Art = Text((string)card["icon"] ?? "❖", "original-card-art");
            if (glyphFont != null) Art.style.unityFont = glyphFont;
            else ApplyGlyphFont(Art, (string)card["icon"] ?? "❖");
            Add(Art);
            var band = Text(typeName, "original-card-type");
            if (TryColor((string)type?["color"], out var typeColor))
            {
                band.style.color = Color.Lerp(typeColor, new Color32(245, 231, 204, 255), .65f);
                band.style.backgroundColor = Color.Lerp(new Color32(42, 36, 28, 255), typeColor, .15f);
            }
            Add(band);
            // Explicit empty profile tags remain empty; an equipped attack must
            // never accidentally inherit the base card's unrelated tag junction.
            var ids = StatusExplainer.TagIds(catalog, card);
            var tagNames = new List<string>(); var tagLines = new List<string>(); var tagRow = new VisualElement(); tagRow.AddToClassList("original-card-tags"); tagRow.pickingMode = PickingMode.Ignore;
            foreach (var explained in StatusExplainer.CardTags(ids, id => metadata.Tags.TryGetValue(id, out var row) ? row : null))
            {
                var tag = metadata.Tags[explained.Id];
                var label = explained.Label; tagNames.Add(label); tagLines.Add(explained.Line);
                // Keep the symbol and Latin copy separate: an emoji-only font
                // must never be applied to the readable tag name beside it.
                // US-13.4: the blurb opens on hover or long press (no hover-only text);
                // a long press releases the card's capture so it does not select the card.
                var chip = new VisualElement { pickingMode = PickingMode.Position };
                chip.AddToClassList("card-tags"); chip.AddToClassList("original-card-tag");
                chip.style.flexDirection = FlexDirection.Row; chip.style.alignItems = Align.Center;
                var glyph = (string)tag["glyph"] ?? "";
                if (glyph.Length > 0)
                {
                    var symbol = Text(glyph); ApplyGlyphFont(symbol, glyph); symbol.style.marginRight = 3; chip.Add(symbol);
                }
                var tagCopy = Text(label); tagCopy.style.flexShrink = 1; tagCopy.style.minWidth = 0; chip.Add(tagCopy);
                EnemyTelegraphView.Explain(chip, string.IsNullOrEmpty(explained.Blurb) ? null : label + "\n" + explained.Blurb);
                if (TryColor((string)tag["color"], out var color))
                {
                    chip.style.color = Color.Lerp(color, new Color32(245, 231, 204, 255), .65f);
                    var edge = new Color(color.r, color.g, color.b, .45f);
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
                (shortage == null ? "" : " " + shortage) +
                (tagLines.Count == 0 ? "" : "\n" + string.Join("\n", tagLines));
            tooltip = FullText;
            var illustrated = new OwnerCardFace(card,cost,title,OriginalCardText.Describe(card,catalog,includeRatingBreakdown:false),typeName);
            if (illustrated != null)
            {
                Clear(); AddToClassList("owner-card"); Add(illustrated);
                if (shortage != null) Add(Text(shortage,"card-shortage","original-card-shortage"));
            }
            // card.hover lift/scale, only inside a hand rail. The lift is drawn by an unnamed,
            // unpickable copy in FeelDriver's overlay, so this control never moves or reorders.
            Ghost = () => { var copy = new OriginalCardView(catalog, card, cost, player, selected, null, "", glyphFont); copy.Art.text = Art.text; copy.Art.style.unityFont = Art.style.unityFont; return copy; };
            FeelDriver.HandCardHover(this);
            if (inspect != null || drop != null) BindInspection(inspect, drop, aim);
        }

        private void BindInspection(Action inspect, Func<Vector2, bool, bool> drop, Action<Vector2?, bool> aim)
        {
            IVisualElementScheduledItem hold = null; var down = false; var dragging = false; var start = Vector2.zero; var pointer = -1;
            var points = new List<(Vector2 Position, double Time)>();
            void Sample(Vector2 position) { var now = (double)Time.realtimeSinceStartup; points.Add((position, now)); while (points.Count > 2 && points[1].Time < now - .12) points.RemoveAt(0); }
            void Cancel()
            {
                down = false; dragging = false; hold?.Pause(); hold = null;
                RemoveFromClassList("card-dragging"); aim?.Invoke(null, false);
                if (pointer >= 0 && this.HasPointerCapture(pointer)) this.ReleasePointer(pointer);
                pointer = -1;
            }
            void Suppress() { _suppressClick = true; schedule.Execute(() => _suppressClick = false).StartingIn(100); }
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1 && inspect != null) { Cancel(); Suppress(); e.StopImmediatePropagation(); inspect(); return; }
                if (e.button != 0) return;
                Cancel(); down = true; start = e.position; pointer = e.pointerId;
                points.Clear(); Sample(start);
                if (inspect != null) hold = schedule.Execute(() => { if (!down || dragging) return; Suppress(); Cancel(); inspect(); }).StartingIn(550);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!down) return;
                var delta = (Vector2)e.position - start;
                Sample(e.position);
                if (delta.magnitude > 12) { hold?.Pause(); hold = null; }
                // Horizontal motion belongs to the scrolling hand. Only upward
                // motion arms a card drag, so browsing never plays a card.
                if (!dragging && drop != null && delta.y < -18 && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                { dragging = true; Suppress(); this.CapturePointer(e.pointerId); AddToClassList("card-dragging"); }
                if (dragging) { aim?.Invoke(e.position, OriginalCardFlick.DistanceMet(start.x, start.y, e.position.x, e.position.y)); e.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(e =>
            {
                var play = dragging; var position = (Vector2)e.position;
                var now = (double)Time.realtimeSinceStartup;
                var recent = points.FirstOrDefault(p => p.Time >= now - .12 && p.Time < now);
                var flick = play && OriginalCardFlick.Qualifies(start.x, start.y, position.x, position.y, recent.Position.y, now - recent.Time);
                if (play) { Suppress(); e.StopImmediatePropagation(); }
                Cancel();
                if (play) drop(position, flick);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerLeaveEvent>(_ => { if (!dragging) { hold?.Pause(); hold = null; } });
            RegisterCallback<PointerCancelEvent>(_ => Cancel());
            RegisterCallback<PointerCaptureOutEvent>(_ => Cancel());
            RegisterCallback<DetachFromPanelEvent>(_ => Cancel());
            RegisterCallback<FocusOutEvent>(_ => { if (!dragging) Cancel(); });
            RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && dragging) { Suppress(); Cancel(); e.StopImmediatePropagation(); } }, TrickleDown.TrickleDown);
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
