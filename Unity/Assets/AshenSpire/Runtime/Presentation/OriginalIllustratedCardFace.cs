// Native UI Toolkit renderer for the published Card Studio layer documents.
// Inputs are already resolved gameplay values. Geometry scales from the authored
// canvas; this view has no command, save, target selection or resource authority.
//
// Two references are supported. Build 1815 (the default, the current JS game) is
// Resources/Original/illustrated-reference-1815.json with art in Art/js1815/cards:
// its "face" block adds the combat face of src/ui/components/illustratedCard.js —
// the raised parchment, the title fade, the footer sigil band and its brass braces,
// the right-edge tag rail and the hand's slot badge. Build 898 is the published
// test reference (Original/illustrated-reference, Art/html898) and is drawn as before.
// Regenerate with tools/unity-illustrated-reference.mjs + tools/import-published-ui-art.py.
// FONT: every text layer takes a USS class (OriginalCards.uss owns the typefaces).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        private Label _bandLabel, _slot;
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
        public int? SlotIndex { get => _slotIndex; set { _slotIndex = value; Slot(); } }
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
                    if (binding == "rules" && _face != null)
                    {
                        var marked = Highlight(copy, baseRules == null ? null : FaceLines(baseRules));
                        if (marked != null) { label.enableRichText = true; label.text = marked; }
                    }
                    label.style.whiteSpace = (int?)layer["maxLines"] == 1 ? WhiteSpace.NoWrap : WhiteSpace.Normal;
                    label.style.overflow = Overflow.Hidden;
                    label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
                    label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
                    if (ColorUtility.TryParseHtmlString((string)layer["color"], out var color)) label.style.color = color;
                    label.style.unityTextAlign = (string)layer["align"] == "left" ? TextAnchor.MiddleLeft
                        : (string)layer["align"] == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                    label.style.unityFontStyleAndWeight = (string)layer["fontWeight"] == "bold" ? FontStyle.Bold : FontStyle.Normal;
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
        internal static string FaceLines(string rules)
        {
            if (string.IsNullOrWhiteSpace(rules)) return "";
            var lines = Regex.Split(rules.Trim(), @"(?<=[.!?])\s+")
                .Select(s => Regex.Replace(s.Trim().TrimEnd('.'), @"^Deal (?=\d)", ""))
                .Where(s => s.Length > 0);
            return string.Join("\n", lines);
        }

        // Numbers that rose read green and bold, numbers that fell red (.ic-text .val.up/.down).
        internal static string Highlight(string text, string baseText, string up = "#205e2b", string down = "#8d2424")
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') >= 0 || baseText == null) return null;
            var now = Regex.Matches(text, @"\d+"); var was = Regex.Matches(baseText, @"\d+");
            if (now.Count == 0 || now.Count != was.Count) return null;
            var output = new StringBuilder(); var at = 0; var changed = false;
            for (var i = 0; i < now.Count; i++)
            {
                output.Append(text, at, now[i].Index - at); at = now[i].Index + now[i].Length;
                if (!long.TryParse(now[i].Value, out var a) || !long.TryParse(was[i].Value, out var b) || a == b) { output.Append(now[i].Value); continue; }
                output.Append("<color=").Append(a > b ? up : down).Append("><b>").Append(now[i].Value).Append("</b></color>"); changed = true;
            }
            output.Append(text, at, text.Length - at);
            return changed ? output.ToString() : null;
        }

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
