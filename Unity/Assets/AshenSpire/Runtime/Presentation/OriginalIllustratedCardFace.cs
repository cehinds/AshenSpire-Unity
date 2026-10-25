// Native UI Toolkit renderer for the published Card Studio layer documents.
// Inputs are already resolved gameplay values. Geometry scales from the authored
// canvas; this view has no command, save, target selection or resource authority.
//
// Two references are supported. Build 1815 (the default, the current JS game) is
// Resources/Original/illustrated-reference-1815.json with art in Art/js1815/cards:
// its "face" block adds the combat face of src/ui/components/illustratedCard.js —
// the raised parchment, the title fade, the footer sigil band and its brass braces,
// the right-edge tag rail and the hand's slot badge, plus the rank gem of an upgraded card, the
// cost-shift mark beside a moved SP price and, in a hand, underlined damage figures. Build 898 is the published
// test reference (Original/illustrated-reference, Art/html898) and is drawn as before.
// Regenerate with tools/unity-illustrated-reference.mjs + tools/import-published-ui-art.py.
// FONT: every text layer takes a USS class (OriginalCards.uss owns the typefaces).
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class OriginalIllustratedCardFace : VisualElement
    {
        /// <summary>The 360×540 template: the face is always 2:3.</summary>
        public const float HeightPerWidth = 1.5f;
        public const string Reference1815 = "Original/illustrated-reference-1815";
        public const string Reference898 = "Original/illustrated-reference";
        private static readonly Dictionary<string, JObject> References = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private static readonly Color Dim = new Color(.65f, .65f, .65f, 1);
        private readonly JObject _reference, _document, _face;
        private readonly List<(Label Label, JObject Layer)> _texts = new List<(Label, JObject)>();
        private readonly List<(VisualElement Element, float Size, float Pad)> _marks = new List<(VisualElement, float, float)>();
        private readonly List<Label> _counts = new List<Label>();
        private VisualElement _band, _rail;
        private Label _bandLabel, _slot, _rules, _inspectCue, _stamina, _rankText;
        private VisualElement _shift, _rank;
        private string _rulesSource, _baseRules;
        private OriginalCardRulesFit.Plan? _rulesPlan;
        /// <summary>The escalation step the rules needed at the current size (OriginalCardRulesFit).</summary>
        public OriginalCardRulesFit.Step RulesStep { get; private set; } = OriginalCardRulesFit.Step.FaceLines;
        private float _lastWidth = -1;

        /// <summary>The 898 face (kept for comparison and older callers).</summary>
        public static OriginalIllustratedCardFace Create(JObject card, JObject cost, string title, string rules, string tags)
            => Create(Reference898, card, cost, title, rules, tags);

        /// <summary>
        /// A face from the named reference, or null when that reference is not bundled.
        /// baseRules is the same rules text without attribute bonuses (numbers that rose are
        /// highlighted); dimmed is the unaffordable treatment. The tag rail is derived from
        /// the resolved card itself (OriginalCardRailTags); the reference supplies its art.
        /// </summary>
        public static OriginalIllustratedCardFace Create(string reference, JObject card, JObject cost, string title, string rules, string typeName,
            string baseRules = null, bool dimmed = false)
        {
            var parsed = Load(reference);
            return parsed == null ? null : new OriginalIllustratedCardFace(parsed, card, cost, title, rules, typeName, baseRules, dimmed);
        }

        /// <summary>
        /// The card's logical position in its hand (0-based), set by the hand builder; the badge
        /// shows its quick-play key 1–9. Null (the default, e.g. rewards, decks) shows no badge.
        /// Never derived from sibling order: the hand brings the selected card to the front.
        /// </summary>
        public int? SlotIndex { get => _slotIndex; set { var marked = _slotIndex != null; _slotIndex = value; Slot(); if (marked != (value != null)) RulesMarkup(); } }
        private int? _slotIndex;

        private static JObject Load(string resource)
        {
            if (References.TryGetValue(resource, out var cached)) return cached;
            var asset = Resources.Load<TextAsset>(resource);
            JObject parsed = null;
            if (asset != null)
            {
                parsed = JObject.Parse(asset.text);
                var build = (int?)parsed["referenceBuild"];
                if (build != 898 && build != 1815) throw new ArgumentException("Unknown illustrated card reference.");
            }
            References[resource] = parsed;
            return parsed;
        }

        private OriginalIllustratedCardFace(JObject reference, JObject card, JObject cost, string title, string rules, string typeName,
            string baseRules, bool dimmed)
        {
            _reference = reference;
            _face = reference["face"] as JObject;
            AddToClassList("published-card-face"); AddToClassList("published-card-face--" + (int)reference["referenceBuild"]);
            pickingMode = PickingMode.Ignore;
            var id = (string)card["id"] ?? (string)card["cardId"] ?? "";
            _document = (JObject)(reference["documents"]["cards"]?[id] ?? reference["documents"]["template"]).DeepClone();
            var tint = dimmed ? Dim : Color.white;
            var layout = (int?)cost["mana"] > 0 ? "staminaMana" : "staminaOnly";
            var overrides = _document["costLayouts"]?[layout]?["layers"] as JObject;
            var views = new List<(int Z, VisualElement View)>();
            var lift = (float?)_face?["lift"] ?? 0;
            foreach (JObject original in _document["layers"])
            {
                var layer = (JObject)original.DeepClone();
                var layerId = (string)layer["id"];
                var binding = (string)layer["bind"];
                if (overrides?[layerId] is JObject patch)
                    foreach (var property in patch.Properties()) layer[property.Name] = property.Value.DeepClone();
                // Frozen pre-foundation runs still pay Actions. Keep that cost
                // visible until current rules replace it with Stamina payment.
                if ((layerId == "energy-icon" || layerId == "energy-value")
                    && ((int?)cost["action"] > 0 || (bool?)cost["variable"] == true)) layer["visible"] = true;
                if (_face != null)
                {
                    // The combat face: the parchment rises, the art shortens, the
                    // footer band replaces the tags text (illustratedCard.js model.sigils).
                    if (binding == "tags") continue;
                    if (layerId == "panel" || binding == "rules") layer["y"] = (float)layer["y"] - lift;
                    if (binding == "artwork") { var window = (JObject)_face["artWindow"]; foreach (var k in new[] { "x", "y", "w", "h" }) layer[k] = window[k]; layer["visible"] = true; }
                }
                if ((bool?)layer["visible"] == false) continue;
                VisualElement view;
                int z;
                if ((string)layer["type"] == "image")
                {
                    var texture = binding == "artwork" ? Artwork(card, id, layer) : LoadTexture((string)layer["resource"]);
                    if (texture == null) continue;
                    view = (bool?)layer["clip"] == true ? (VisualElement)new ClippedArtwork(texture, layer, _document, tint)
                        : new Image { image = texture, tintColor = tint, scaleMode = (string)layer["fit"] == "contain" ? ScaleMode.ScaleToFit
                        : (string)layer["fit"] == "cover" ? ScaleMode.ScaleAndCrop : ScaleMode.StretchToFill };
                    z = binding == "artwork" ? 2 : layerId == "base" ? 1 : layerId == "panel" || layerId == "flag" ? 5 : 7;
                    // The base frame's trim plane sits above the art and parchment.
                    if (_face != null && layer["trimResource"] != null && LoadTexture((string)layer["trimResource"]) is Texture2D trim)
                        views.Add((6, Place(new Image { image = trim, tintColor = tint, scaleMode = ScaleMode.StretchToFill }, layer, layerId + "-trim")));
                }
                else
                {
                    var variable = (bool?)cost["variable"] == true;
                    var copy = binding == "name" ? title : binding == "rules" ? (_face != null ? FaceLines(rules) : rules) : binding == "tags" ? typeName
                        : binding == "action" ? variable ? "X" : (string)cost["action"]
                        : binding == "stamina" ? variable ? "X" : (string)cost["stamina"]
                        : binding == "mana" ? (string)cost["mana"] : (string)layer["text"] ?? "";
                    var label = new Label(copy) { tooltip = binding == "rules" ? rules : copy, enableRichText = false };
                    label.AddToClassList("published-card-text"); label.AddToClassList("published-card-text-" + (binding ?? layerId));
                    if (binding == "rules" && _face != null) { _rules = label; _rulesSource = rules; _baseRules = baseRules; }
                    if (binding == "stamina" && _face != null) CostShift(label, card, cost);
                    label.style.whiteSpace = (int?)layer["maxLines"] == 1 ? WhiteSpace.NoWrap : WhiteSpace.Normal;
                    label.style.overflow = Overflow.Hidden;
                    label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
                    label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
                    if (ColorUtility.TryParseHtmlString((string)layer["color"], out var color)) label.style.color = color;
                    label.style.unityTextAlign = (string)layer["align"] == "left" ? TextAnchor.MiddleLeft
                        : (string)layer["align"] == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                    // Weight is USS (OriginalCards.uss): the 1815 face draws bold layers with the
                    // real Liberation Serif Bold file rather than a synthesized bold.
                    label.EnableInClassList("published-card-text--bold", (string)layer["fontWeight"] == "bold");
                    _texts.Add((label, layer)); view = label; z = 8;
                }
                views.Add((z, Place(view, layer, layerId)));
            }
            if (_face != null)
            {
                views.Add((3, Place(Backdrop(_face["titleFade"], tint), (JObject)_face["titleFade"], "title-fade")));
                views.Add((3, Place(Backdrop(_face["footerBackdrop"], tint), (JObject)_face["footerBackdrop"], "footer-backdrop")));
                views.Add((4, Place(Backdrop(_face["actionFrame"], tint), (JObject)_face["actionFrame"], "action-frame")));
                views.Add((7, Band(card, typeName, tint)));
                var rail = Rail(card, tint);
                if (rail != null) views.Add((7, rail));
                var rank = OriginalCardRulesFit.Rank(title, (bool?)card["upgraded"] == true);
                if (rank > 0 && _face["rank"] is JObject gem) views.Add((9, Rank(gem, rank, tint)));
                _slot = new Label { pickingMode = PickingMode.Ignore, name = "published-card-slot" };
                _slot.AddToClassList("published-card-slot"); _slot.style.display = DisplayStyle.None;
                views.Add((9, _slot));
            }
            // Stable: equal planes keep their authored order (illustratedCard.js z-index planes).
            foreach (var entry in views.Select((v, i) => (v.Z, i, v.View)).OrderBy(v => v.Z).ThenBy(v => v.i)) Add(entry.View);
            var glow = new VisualElement { pickingMode = PickingMode.Ignore }; glow.AddToClassList("published-card-glow"); Add(glow);
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        private VisualElement Place(VisualElement view, JObject layer, string id)
        {
            var width = (float)_document["width"]; var height = (float)_document["height"];
            view.name = "published-card-layer-" + id; view.pickingMode = PickingMode.Ignore;
            view.style.position = Position.Absolute;
            view.style.left = Length.Percent((float)layer["x"] / width * 100);
            view.style.top = Length.Percent((float)layer["y"] / height * 100);
            view.style.width = Length.Percent((float)layer["w"] / width * 100);
            view.style.height = Length.Percent((float)layer["h"] / height * 100);
            view.style.opacity = (float?)layer["opacity"] ?? 1;
            view.style.rotate = new Rotate(new Angle((float?)layer["rotation"] ?? 0, AngleUnit.Degree));
            return view;
        }

        private static Texture2D LoadTexture(string resource) => string.IsNullOrEmpty(resource) ? null : Resources.Load<Texture2D>(resource);

        private static Image Backdrop(JToken rect, Color tint)
            => new Image { image = LoadTexture((string)rect?["resource"]), tintColor = tint, scaleMode = ScaleMode.StretchToFill };

        // The reference painting for this card id; otherwise Unity's own painting
        // on the reference template (the 898 illustration, then the owner art).
        private Texture2D Artwork(JObject card, string id, JObject layer)
        {
            var profile = (string)card["equipmentProfileId"] ?? (string)card["profileId"] ?? "";
            if (_face == null)
            {
                var resource = (string)layer["resource"];
                if (string.IsNullOrEmpty(resource))
                    resource = (string)_reference["profiles"]?[profile] ?? (string)_reference["artwork"]?[id]?["resource"];
                return LoadTexture(resource);
            }
            if (LoadTexture((string)_reference["artwork"]?[id]?["resource"]) is Texture2D painted) { layer["fit"] = "fill"; return painted; }
            layer["fit"] = "cover";
            var older = Load(Reference898);
            var fallback = (string)older?["profiles"]?[profile];
            if (fallback == null && (string)older?["artwork"]?[id]?["kind"] == "illustrated") fallback = (string)older["artwork"][id]["resource"];
            var type = (string)card["type"];
            var kind = type == "attack" ? "attack" : type == "skill" && (card["effects"] as JArray)?.Count == 1 && (string)card["effects"][0]["op"] == "block" ? "guard" : "ember";
            return LoadTexture(fallback) ?? LoadTexture("Art/OwnerAppearance/card-" + kind);
        }

        // Footer: the resolved Unity card's type word and the reference sigil for that type
        // (OriginalCardRailTags.FooterSigil). The JS stance identity is not used.
        private VisualElement Band(JObject card, string typeName, Color tint)
        {
            var rect = (JObject)_face["tagsBand"];
            _band = new VisualElement(); _band.AddToClassList("published-card-band");
            _band.style.flexDirection = FlexDirection.Row; _band.style.alignItems = Align.Center; _band.style.justifyContent = Justify.Center;
            var sigils = _reference["sigils"] as JObject;
            var action = OriginalCardRailTags.FooterSigil((string)card["type"], s => sigils?[s]?["resource"] != null);
            if (action != null && LoadTexture((string)sigils[action]["resource"]) is Texture2D texture)
            {
                var holder = Mark(texture, (float)_face["sigil"]["size"], (float)_face["sigil"]["pad"], tint);
                holder.AddToClassList("published-card-sigil"); _band.Add(holder);
            }
            var word = typeName ?? OriginalCardText.Humanize((string)card["type"] ?? "");
            _bandLabel = new Label(word.ToUpperInvariant()) { pickingMode = PickingMode.Ignore, tooltip = word };
            _bandLabel.AddToClassList("published-card-text"); _bandLabel.AddToClassList("published-card-type");
            if (ColorUtility.TryParseHtmlString((string)rect["color"], out var color)) _bandLabel.style.color = color;
            _band.Add(_bandLabel);
            return Place(_band, rect, "tags");
        }

        // The rank gem (JS .card-rank): a blue faceted bar on the parchment's top edge reading "Rank N".
        private VisualElement Rank(JObject rect, int rank, Color tint)
        {
            _rank = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = "Rank " + rank + " · upgraded" };
            _rank.AddToClassList("published-card-rank");
            var bar = new Image { image = LoadTexture((string)rect["resource"]), tintColor = tint, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            bar.AddToClassList("published-card-rank-bar"); bar.style.position = Position.Absolute;
            bar.style.left = bar.style.top = 0; bar.style.right = bar.style.bottom = 0; _rank.Add(bar);
            var text = (JObject)rect["text"];
            _rankText = new Label("Rank " + rank) { pickingMode = PickingMode.Ignore, name = "published-card-rank-text" };
            _rankText.AddToClassList("published-card-text"); _rankText.AddToClassList("published-card-rank-text");
            _rankText.style.position = Position.Absolute;
            // The text box is centred on the gem; Rank N may be wider than its authored box.
            _rankText.style.left = 0; _rankText.style.right = 0;
            _rankText.style.top = Length.Percent((float)text["y"]); _rankText.style.height = Length.Percent((float)text["h"]);
            if (ColorUtility.TryParseHtmlString((string)rect["color"], out var color)) _rankText.style.color = color;
            _rank.Add(_rankText);
            return Place(_rank, rect, "rank");
        }

        // A rule moved the printed SP price (JS .cost-shift): cheaper reads green with a down mark,
        // dearer reads --danger with an up mark. The printed price is the definition's own profile
        // in the same payment model the shown cost uses; nothing here charges or validates.
        private void CostShift(Label label, JObject card, JObject cost)
        {
            int printed;
            try
            {
                var aliased = (int?)cost["action"] == (int?)cost["stamina"];
                printed = (int?)(aliased ? OriginalCardPayment.Profile(card) : CardMechanics.CostProfile(card))["stamina"] ?? 0;
            }
            catch (ArgumentException) { return; }
            var shift = OriginalCardRulesFit.CostShift((int?)cost["stamina"] ?? 0, printed, (bool?)cost["variable"] == true);
            if (shift == 0 || !(_face["costShift"]?[shift < 0 ? "down" : "up"] is JObject mark)) return;
            if (ColorUtility.TryParseHtmlString((string)mark["color"], out var color)) label.style.color = color;
            label.AddToClassList("published-card-cost-shift"); label.AddToClassList(shift < 0 ? "published-card-cost-shift--down" : "published-card-cost-shift--up");
            label.tooltip = (shift < 0 ? "Cheaper" : "Dearer") + " than printed (" + printed + " SP)";
            _shift = new Image { image = LoadTexture((string)mark["resource"]), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, name = "published-card-cost-shift-mark" };
            _shift.style.position = Position.Absolute; _stamina = label; label.Add(_shift);
        }

        // Right-edge rail: up to five engraved marks, each with an optional count chip.
        private VisualElement Rail(JObject card, Color tint)
        {
            var style = (JObject)_face["tagRail"];
            var symbols = _reference["tagSymbols"] as JObject;
            var rows = OriginalCardRailTags.For(card, id => symbols?[id] != null).Select(m => (m.Id, m.Label, m.Count));
            _rail = new VisualElement(); _rail.AddToClassList("published-card-rail"); _rail.pickingMode = PickingMode.Ignore;
            _rail.style.position = Position.Absolute; _rail.style.flexDirection = FlexDirection.Column; _rail.style.alignItems = Align.FlexEnd;
            var width = (float)_document["width"]; var height = (float)_document["height"];
            _rail.style.right = Length.Percent((float)style["right"] / width * 100);
            _rail.style.top = Length.Percent((float)style["top"] / height * 100);
            foreach (var (id, label, count) in rows)
            {
                if (_rail.childCount >= 5) break;
                if (!(LoadTexture((string)symbols?[id ?? ""]) is Texture2D texture)) continue;
                var holder = Mark(texture, (float)style["size"], (float)style["pad"], tint);
                holder.AddToClassList("published-card-tag");
                holder.tooltip = label + (count == null ? "" : " " + count);
                if (count != null)
                {
                    var chip = new Label(count.ToString()) { pickingMode = PickingMode.Ignore };
                    chip.AddToClassList("published-card-text"); chip.AddToClassList("published-card-tag-count");
                    holder.Add(chip); _counts.Add(chip);
                }
                _rail.Add(holder);
            }
            _rail.name = "published-card-layer-tag-rail";
            return _rail.childCount == 0 ? null : _rail;
        }

        // A mark PNG includes a shadow margin (pad card units each side) around its
        // size×size box; the holder lays out at the box and the image overhangs it.
        private VisualElement Mark(Texture2D texture, float size, float pad, Color tint)
        {
            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.style.flexShrink = 0;
            var image = new Image { image = texture, tintColor = tint, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.style.position = Position.Absolute;
            holder.Add(image); _marks.Add((holder, size, pad));
            return holder;
        }

        // The hand shows each slot's quick-play key (1–9) on the card's top edge.
        private void Slot()
        {
            if (_slot == null) return;
            var index = _slotIndex ?? -1;
            _slot.text = index >= 0 && index < 9 ? (index + 1).ToString() : "";
            _slot.style.display = _slot.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // The JS face shows short face lines: one clause per line, no "Deal", no full stops.
        internal static string FaceLines(string rules) => OriginalCardRulesFit.FaceLines(rules);

        // Numbers that rose read green and bold, numbers that fell red (.ic-text .val.up/.down).
        internal static string Highlight(string text, string baseText, string up = "#205e2b", string down = "#8d2424")
            => baseText == null ? null : OriginalCardRulesFit.Markup(text, baseText, false, up, down);
        private void Fit()
        {
            var width = contentRect.width;
            if (width <= 0 || Mathf.Abs(width - _lastWidth) < .1f) return;
            _lastWidth = width; var scale = width / (float)_document["width"];
            style.height = (float)_document["height"] * scale;
            if (_face != null)
            {
                style.minHeight = (float)_document["height"] * scale;
                if (parent is OriginalCardView container)
                {
                    // Border and padding are outside the fixed-ratio face.
                    var outer = width * HeightPerWidth + container.resolvedStyle.paddingTop + container.resolvedStyle.paddingBottom
                        + container.resolvedStyle.borderTopWidth + container.resolvedStyle.borderBottomWidth;
                    container.style.height = outer; container.style.minHeight = outer;
                }
                foreach (var (element, size, pad) in _marks)
                {
                    element.style.width = element.style.height = size * scale;
                    var image = element[0];
                    image.style.left = image.style.top = -pad * scale;
                    image.style.width = image.style.height = (size + pad * 2) * scale;
                }
                var band = (JObject)_face["tagsBand"]; var sigil = (JObject)_face["sigil"]; var rail = (JObject)_face["tagRail"];
                _band.style.paddingLeft = _band.style.paddingRight = (float)band["paddingX"] * scale;
                _band.style.paddingBottom = (float)band["paddingBottom"] * scale;
                for (var i = 0; i < _band.childCount - 1; i++) _band[i].style.marginRight = (float)sigil["gap"] * scale;
                var fontSize = (float)band["fontSize"] * scale;
                _bandLabel.style.fontSize = fontSize; _bandLabel.style.letterSpacing = (float)band["letterSpacing"] * fontSize;
                if (_rail != null) for (var i = 1; i < _rail.childCount; i++) _rail[i].style.marginTop = (float)rail["gap"] * scale;
                foreach (var count in _counts)
                {
                    count.style.right = count.style.bottom = -.016f * (float)_document["width"] * scale;
                    count.style.minWidth = count.style.height = .06f * (float)_document["width"] * scale;
                    count.style.fontSize = .052f * (float)_document["width"] * scale;
                    count.style.borderTopLeftRadius = count.style.borderTopRightRadius = count.style.borderBottomLeftRadius = count.style.borderBottomRightRadius = .03f * (float)_document["width"] * scale;
                }
                Slot();
            }
            foreach (var text in _texts)
            {
                if (text.Label == _rules) { FitRules(text.Layer, scale); continue; }
                var maximum = ((float?)text.Layer["maxFontSize"] ?? (float?)text.Layer["fontSize"] ?? 20) * scale;
                var minimum = ((float?)text.Layer["minFontSize"] ?? (float?)text.Layer["fontSize"] ?? 20) * scale;
                var size = maximum;
                text.Label.style.fontSize = size;
                if ((bool?)text.Layer["autoFit"] == true)
                {
                    var availableWidth = (float)text.Layer["w"] * scale; var availableHeight = (float)text.Layer["h"] * scale;
                    for (var step = 0; step < 8 && size > minimum; step++)
                    {
                        var measured = text.Label.MeasureTextSize(text.Label.text, availableWidth, MeasureMode.AtMost, 0, MeasureMode.Undefined);
                        if (measured.y <= availableHeight + 1 && measured.x <= availableWidth + 1) break;
                        size = Mathf.Max(minimum, size - (maximum - minimum) / 7); text.Label.style.fontSize = size;
                    }
                }
                if (text.Label == _stamina && _shift != null)
                {
                    // ::after { font-size:.5em; vertical-align:top; margin-left:.1em } beside the centred digits.
                    var box = new Vector2((float)text.Layer["w"] * scale, (float)text.Layer["h"] * scale);
                    var digits = text.Label.MeasureTextSize(text.Label.text, box.x, MeasureMode.AtMost, 0, MeasureMode.Undefined);
                    var mark = size * .5f;
                    _shift.style.width = _shift.style.height = mark;
                    _shift.style.left = (box.x + digits.x) / 2 + size * .1f;
                    _shift.style.top = Mathf.Max(0, (box.y - digits.y) / 2 + size * .08f);
                }
            }
            if (_rankText != null && _face["rank"] is JObject gem)
            {
                // clamp(8px, h·text.h/1.15 card units, 20px): JS .card-rank-text.
                var rankSize = (float)gem["h"] * (float)gem["text"]["h"] / 100 * ((float?)gem["fontScale"] ?? 1) * scale;
                _rankText.style.fontSize = Mathf.Clamp(rankSize, 8, 20);
            }
        }
        // The fitted rules as plain or rich text: changed numbers coloured (all views) and, in a hand
        // (SlotIndex set), the damage figures underlined. Markup never changes the measured copy.
        private void RulesMarkup()
        {
            if (_rules == null || !(_rulesPlan is OriginalCardRulesFit.Plan plan)) return;
            _rules.enableRichText = false; _rules.text = plan.Text;
            string baseText = null;
            if (_baseRules != null)
            {
                var baseLines = OriginalCardRulesFit.FaceLines(_baseRules);
                baseText = plan.Step == OriginalCardRulesFit.Step.FaceLines ? baseLines : OriginalCardRulesFit.Joined(baseLines);
            }
            var rules = (JObject)_face?["rules"];
            var marked = OriginalCardRulesFit.Markup(plan.Text, baseText, _slotIndex != null, (string)rules?["up"] ?? "#205e2b", (string)rules?["down"] ?? "#8d2424");
            if (marked != null) { _rules.enableRichText = true; _rules.text = marked; }
        }
        // Rules are never silently clipped: face lines, then running prose, then below the
        // document minimum to a readable floor; past that the face shows an "Inspect card" cue
        // and card inspection prints the complete resolved rules (OriginalCardRulesFit).
        private void FitRules(JObject layer, float scale)
        {
            var label = _rules;
            var maximum = ((float?)layer["maxFontSize"] ?? (float?)layer["fontSize"] ?? 20) * scale;
            var minimum = ((float?)layer["minFontSize"] ?? (float?)layer["fontSize"] ?? 20) * scale;
            label.enableRichText = false;
            var plan = OriginalCardRulesFit.Choose(_rulesSource, (float)layer["w"] * scale, (float)layer["h"] * scale, maximum, minimum,
                OriginalCardRulesFit.FloorFontSize * scale, (copy, size, width) =>
                {
                    label.style.fontSize = size;
                    return label.MeasureTextSize(copy, width, MeasureMode.AtMost, 0, MeasureMode.Undefined).y;
                });
            RulesStep = plan.Step;
            label.style.fontSize = plan.Size; _rulesPlan = plan; RulesMarkup();
            label.EnableInClassList("published-card-rules-overflow", plan.Clipped);
            if (plan.Clipped && _inspectCue == null)
            {
                _inspectCue = new Label("Inspect card") { pickingMode = PickingMode.Ignore, name = "published-card-inspect-cue" };
                _inspectCue.AddToClassList("published-card-text"); _inspectCue.AddToClassList("published-card-inspect-cue");
                Add(Place(_inspectCue, new JObject { ["x"] = layer["x"], ["y"] = (float)layer["y"] + (float)layer["h"] - 14, ["w"] = layer["w"], ["h"] = 14 }, "inspect-cue"));
            }
            if (_inspectCue != null)
            {
                // Shown by USS only outside inspection, whose details print the complete rules.
                _inspectCue.EnableInClassList("published-card-inspect-cue--shown", plan.Clipped);
                _inspectCue.style.fontSize = Mathf.Clamp(15.5f * scale, 8, 12);
            }
        }

        // UI Toolkit has no CSS clip-path. Draw the same convex authored card
        // polygon as a textured mesh, intersected with the artwork's fitted box.
        private sealed class ClippedArtwork : VisualElement
        {
            private readonly Texture2D _texture;
            private readonly JObject _layer, _document;
            private readonly Color _tint;
            public ClippedArtwork(Texture2D texture, JObject layer, JObject document, Color tint)
            { _texture = texture; _layer = layer; _document = document; _tint = tint; generateVisualContent += Paint; RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint()); }
            private static List<Vector2> Cut(List<Vector2> input, Func<Vector2, bool> inside, Func<Vector2, Vector2, Vector2> intersect)
            {
                var output = new List<Vector2>(); if (input.Count == 0) return output;
                var previous = input[input.Count - 1]; var previousInside = inside(previous);
                foreach (var point in input)
                {
                    var pointInside = inside(point);
                    if (pointInside != previousInside) output.Add(intersect(previous, point));
                    if (pointInside) output.Add(point);
                    previous = point; previousInside = pointInside;
                }
                return output;
            }
            private void Paint(MeshGenerationContext context)
            {
                if (contentRect.width <= 0 || contentRect.height <= 0) return;
                var x = (float)_layer["x"]; var y = (float)_layer["y"]; var w = (float)_layer["w"]; var h = (float)_layer["h"];
                var box = new Rect(x, y, w, h); var image = box;
                var fit = (string)_layer["fit"];
                if (fit == "contain" || fit == "cover")
                {
                    var scale = fit == "contain" ? Mathf.Min(w / _texture.width, h / _texture.height) : Mathf.Max(w / _texture.width, h / _texture.height);
                    image = new Rect(x + (w - _texture.width * scale) / 2, y + (h - _texture.height * scale) / 2, _texture.width * scale, _texture.height * scale);
                }
                var clip = new List<Vector2>(); var dw = (float)_document["width"]; var dh = (float)_document["height"];
                if (_document["clipPolygon"] is JArray points)
                    foreach (var point in points) clip.Add(new Vector2((float)point[0] * dw, (float)point[1] * dh));
                else
                    clip.AddRange(new[] { new Vector2(.12f * dw, .04f * dh), new Vector2(.88f * dw, .04f * dh), new Vector2(.95f * dw, .11f * dh), new Vector2(.95f * dw, .9f * dh), new Vector2(.88f * dw, .966f * dh), new Vector2(.12f * dw, .966f * dh), new Vector2(.052f * dw, .9f * dh), new Vector2(.052f * dw, .11f * dh) });
                var left = Mathf.Max(box.xMin, image.xMin); var right = Mathf.Min(box.xMax, image.xMax); var top = Mathf.Max(box.yMin, image.yMin); var bottom = Mathf.Min(box.yMax, image.yMax);
                Vector2 AtX(Vector2 a, Vector2 b, float edge) => new Vector2(edge, a.y + (b.y - a.y) * (edge - a.x) / (b.x - a.x));
                Vector2 AtY(Vector2 a, Vector2 b, float edge) => new Vector2(a.x + (b.x - a.x) * (edge - a.y) / (b.y - a.y), edge);
                clip = Cut(clip, p => p.x >= left, (a, b) => AtX(a, b, left)); clip = Cut(clip, p => p.x <= right, (a, b) => AtX(a, b, right));
                clip = Cut(clip, p => p.y >= top, (a, b) => AtY(a, b, top)); clip = Cut(clip, p => p.y <= bottom, (a, b) => AtY(a, b, bottom));
                if (clip.Count < 3) return;
                var mesh = context.Allocate(clip.Count, (clip.Count - 2) * 3, _texture);
#if UNITY_6000_0_OR_NEWER
                // Unity 6 remaps normalized texture UVs into its atlas itself.
                var region = new Rect(0, 0, 1, 1);
#else
                var region = mesh.uvRegion;
#endif
                foreach (var point in clip)
                    mesh.SetNextVertex(new Vertex { position = new Vector3((point.x - x) * contentRect.width / w, (point.y - y) * contentRect.height / h, Vertex.nearZ), tint = _tint,
                        uv = new Vector2(region.xMin + (point.x - image.xMin) / image.width * region.width, region.yMin + (1 - (point.y - image.yMin) / image.height) * region.height) });
                for (var i = 1; i < clip.Count - 1; i++) { mesh.SetNextIndex(0); mesh.SetNextIndex((ushort)i); mesh.SetNextIndex((ushort)(i + 1)); }
            }
        }
    }
}
