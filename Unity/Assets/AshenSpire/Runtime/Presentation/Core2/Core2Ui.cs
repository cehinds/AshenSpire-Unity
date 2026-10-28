// Core2Ui.cs — small element builders for the Core 2 run screens: the JS kit's button, label, tile and door, styled by
// Resources/Core2/Core2Run.uss (tokens only). Art comes from Resources/Art/js1815 (imported from the JS game with sha256
// provenance in each folder's provenance.json); a missing texture leaves the element plain rather than failing.
// Presentation only: no game state.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public static class Core2Ui
    {
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static StyleSheet _sheet;

        /// <summary>The run screens' sheet (and the JS tokens it reads), added once per root.</summary>
        public static void Style(VisualElement root)
        {
            var tokens = Resources.Load<StyleSheet>("Generated/JsTokens");
            if (tokens != null && !root.styleSheets.Contains(tokens)) root.styleSheets.Add(tokens);
            _sheet ??= Resources.Load<StyleSheet>("Core2Ui/Core2Run");
            if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
        }

        /// <summary>A texture from Resources (cached); null when the art is not imported.</summary>
        public static Texture2D Art(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Textures.TryGetValue(path, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>(path);
            Textures[path] = texture;
            return texture;
        }

        public static T Classes<T>(T element, string classes) where T : VisualElement
        {
            if (!string.IsNullOrEmpty(classes)) foreach (var c in classes.Split(' ')) if (c.Length > 0) element.AddToClassList(c);
            return element;
        }

        public static VisualElement Div(string classes = null, string name = null) => Classes(new VisualElement { name = name }, classes);

        public static Label Text(string text, string classes = null)
        {
            var label = new Label(text ?? "");
            return Classes(label, classes);
        }

        /// <summary>A JS kit button: focusable (keyboard and gamepad reach it), Enter/Space activate it, touch taps it.</summary>
        public static Button Btn(string name, string text, Action onClick, string classes = "", bool enabled = true, string tooltip = null)
        {
            var button = new Button(() => onClick?.Invoke()) { name = name, text = text ?? "", tooltip = tooltip ?? "" };
            Classes(button, "c2-btn " + classes);
            if (classes != null && classes.Contains("c2-btn-primary")) Surface(button, "Art/js1815/ui/button-primary");
            button.SetEnabled(enabled);
            return button;
        }

        /// <summary>A background image (the JS art) on an element; false when the art is missing.</summary>
        public static bool Surface(VisualElement element, string path, bool tint = false)
        {
            var texture = Art(path);
            if (texture == null) return false;
            element.style.backgroundImage = new StyleBackground(texture);
            return true;
        }

        /// <summary>An icon element (a JS engraved icon raster) sized by its class.</summary>
        public static VisualElement Icon(string path, string classes)
        {
            var icon = Div(classes);
            icon.pickingMode = PickingMode.Ignore;
            Surface(icon, path);
            return icon;
        }

        /// <summary>The art for a map node kind (JS nodeIcon; the engraved glyphs as the imported icon set has them).</summary>
        public static string NodeArt(string type) => type switch
        {
            "monster" or "fight" or "elite" or "boss" => "Art/js1815/ui/icon-attack",
            "shrine" => "Art/js1815/ui/icon-rest",
            "merchant" => "Art/js1815/ui/icon-merchant",
            "treasure" => "Art/js1815/ui/icon-reward",
            _ => "Art/js1815/journey/icon-unknown",
        };

        /// <summary>A modal door over a scrim (JS mountRewardDoor / openModal): head, body, foot.</summary>
        public static (VisualElement Scrim, VisualElement Door, VisualElement Head, VisualElement Body, VisualElement Foot) Door(string eyebrow, string title, string status = null, bool wide = false)
        {
            var scrim = Div("c2-scrim");
            var door = Div("c2-door" + (wide ? " c2-door-wide" : ""));
            var head = Div("c2-door-head");
            var titles = Div("c2-col");
            if (!string.IsNullOrEmpty(eyebrow)) titles.Add(Text(eyebrow, "c2-micro"));
            titles.Add(Text(title, "c2-title-m"));
            head.Add(titles);
            if (!string.IsNullOrEmpty(status)) head.Add(Text(status, "c2-caption"));
            var body = new ScrollView(ScrollViewMode.Vertical);
            Classes(body, "c2-door-body");
            var foot = Div("c2-door-foot");
            door.Add(head); door.Add(body); door.Add(foot);
            scrim.Add(door);
            return (scrim, door, head, body.contentContainer, foot);
        }

        /// <summary>A .class-pick tile: glyph, title, lines, and an optional actions column.</summary>
        public static VisualElement Tile(string glyphArt, string title, IEnumerable<string> lines, bool locked = false, bool selected = false)
        {
            var tile = Div("c2-tile" + (locked ? " is-locked" : "") + (selected ? " is-selected" : ""));
            if (glyphArt != null) tile.Add(Icon(glyphArt, "c2-tile-glyph"));
            var body = Div("c2-tile-body");
            if (!string.IsNullOrEmpty(title)) body.Add(Text(title, "c2-tile-title"));
            if (lines != null) foreach (var line in lines) if (!string.IsNullOrEmpty(line)) body.Add(Text(line, "c2-tile-line"));
            tile.Add(body);
            return tile;
        }

        /// <summary>Focus the first enabled button under an element (JS focusFirst when the pad or keyboard is engaged).</summary>
        public static void FocusFirst(VisualElement root, string name = null)
        {
            root?.schedule.Execute(() =>
            {
                Button target = null;
                if (name != null) target = root.Q<Button>(name);
                if (target == null || !target.enabledSelf) root.Query<Button>().ForEach(b => { if (target == null && b.enabledInHierarchy && b.visible) target = b; });
                target?.Focus();
            });
        }
    }
}
