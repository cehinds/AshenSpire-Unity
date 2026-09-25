// EnemyTelegraphView.cs — UI Toolkit binding for enemy intent badges and Poise meters (F04).
// WIRING: OriginalRunPanel/OriginalCoopPanel call Attach(slot, telegraph) on each enemy
// target built by OriginalCombatLayout.Enemy. The slot keeps its name, click and layout;
// Attach swaps the text intent label for the badge and adds the Poise meter under HP.
// DATA: Domain/Original/EnemyTelegraph.cs (EnemyTelegraphViewModel) owns every number and
// every word. MODIFY: look in Resources/EnemyTelegraphs.uss. No rules or saved state here.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public static class EnemyTelegraphView
    {
        public const string StyleSheetResource = "EnemyTelegraphs";
        private const long HoverDelayMs = 350, LongPressMs = 450, TouchHideMs = 4000;
        // Glyphs the authored font map does not list (↑ ☾) are in NotoSansSymbols.
        private const string SymbolFallbackFont = "Fonts/NotoSansSymbols-Regular";
        private static StyleSheet _sheet;
        private static JObject _glyphFonts;
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>(StringComparer.Ordinal);

        /// <summary>Co-op has no live CombatSession on the client: build from the snapshot (authored damage).</summary>
        public static EnemyTelegraph FromSnapshot(JObject enemy, JObject hero, JObject balance)
            => EnemyTelegraphViewModel.Build(enemy, hero, null, "each hero", (double?)balance?["poise"]?["growthMult"] ?? 1.25);

        /// <summary>Replaces the slot's text intent with the badge and adds the Poise meter below the HP pool.</summary>
        public static void Attach(VisualElement slot, EnemyTelegraph telegraph)
        {
            if (slot == null || telegraph == null) return;
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>(StyleSheetResource);
            if (_sheet != null && !slot.styleSheets.Contains(_sheet)) slot.styleSheets.Add(_sheet);
            var old = slot.Children().FirstOrDefault(c => c.ClassListContains("original-enemy-intent"));
            if (telegraph.Intent != null)
            {
                var badge = Badge(telegraph.Intent, telegraph.InstanceId);
                if (old != null)
                {
                    slot.Insert(slot.IndexOf(old), badge); old.RemoveFromHierarchy();
                    if (old is Label oldLabel && !string.IsNullOrEmpty(slot.tooltip)) slot.tooltip = slot.tooltip.Replace(oldLabel.text, telegraph.Intent.TooltipText.Replace("\n", " "));
                }
                else slot.Insert(0, badge);
            }
            if (!telegraph.Poise.Visible) return;
            var hp = slot.Children().FirstOrDefault(c => c.ClassListContains("original-pool"));
            var meter = PoiseMeter(telegraph.Poise, telegraph.InstanceId);
            if (hp != null) slot.Insert(slot.IndexOf(hp) + 1, meter); else slot.Add(meter);
        }

        public static VisualElement Badge(IntentDisplay intent, string instanceId)
        {
            var badge = new VisualElement { name = "enemy-intent-" + instanceId, pickingMode = PickingMode.Position };
            badge.AddToClassList("telegraph-intent");
            foreach (var cls in intent.CssClasses.Split(' ')) if (cls.Length > 0) badge.AddToClassList("telegraph-" + cls);
            badge.AddToClassList("telegraph-severity-" + intent.Severity.ToString().ToLowerInvariant());
            badge.EnableInClassList("telegraph-pending", intent.Pending);
            if (intent.Kind != "unknown") badge.Add(GlyphLabel(intent.Icon.Glyph, "telegraph-intent-glyph"));
            var value = intent.ValueText.EndsWith(" ⌛", StringComparison.Ordinal) ? intent.ValueText.Substring(0, intent.ValueText.Length - 2) : intent.ValueText;
            if (value.Length > 0) badge.Add(Text(value, "telegraph-intent-value"));
            if (value.Length != intent.ValueText.Length) badge.Add(GlyphLabel("⌛", "telegraph-intent-delay"));
            Tooltip(badge, intent.TooltipText);
            return badge;
        }

        public static VisualElement PoiseMeter(PoiseDisplay poise, string instanceId)
        {
            var meter = new VisualElement { name = "enemy-poise-" + instanceId, pickingMode = PickingMode.Position };
            meter.AddToClassList("telegraph-poise");
            meter.EnableInClassList("near-break", poise.NearBreak);
            meter.EnableInClassList("broken", poise.Broken);
            meter.EnableInClassList("staggered", poise.Staggered);
            var track = new VisualElement { pickingMode = PickingMode.Ignore }; track.AddToClassList("telegraph-poise-track");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("telegraph-poise-fill");
            fill.style.width = Length.Percent((float)(poise.Fraction * 100));
            track.Add(fill);
            foreach (var at in poise.Ticks)
            {
                var tick = new VisualElement { pickingMode = PickingMode.Ignore }; tick.AddToClassList("telegraph-poise-tick");
                tick.style.left = Length.Percent((float)(at * 100)); track.Add(tick);
            }
            meter.Add(track);
            if (poise.Broken || poise.Staggered) meter.Add(GlyphLabel("✦", "telegraph-poise-state"));
            Tooltip(meter, poise.Tooltip);
            return meter;
        }

        // Runtime UI Toolkit panels never draw `tooltip`, so the explanation is a floating
        // label on the panel root: after a hover delay (mouse) or on a long press (touch/pen).
        // A long press releases the slot's pointer capture so it does not also select the target.
        private static void Tooltip(VisualElement owner, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            owner.tooltip = text;
            VisualElement popup = null; IVisualElementScheduledItem pending = null, expiry = null;
            void Hide() { pending?.Pause(); expiry?.Pause(); popup?.RemoveFromHierarchy(); popup = null; }
            void Show()
            {
                var root = owner.panel?.visualTree; if (root == null) return;
                popup?.RemoveFromHierarchy();
                popup = new VisualElement { pickingMode = PickingMode.Ignore }; popup.AddToClassList("telegraph-tooltip");
                if (_sheet != null) popup.styleSheets.Add(_sheet);
                var lines = text.Split('\n');
                popup.Add(Text(lines[0], "telegraph-tooltip-title"));
                if (lines.Length > 1) popup.Add(Text(string.Join("\n", lines.Skip(1)), "telegraph-tooltip-body"));
                var anchor = owner.worldBound; const float width = 230;
                var left = Mathf.Clamp(anchor.center.x - width / 2, 4, Mathf.Max(4, root.layout.width - width - 4));
                popup.style.position = Position.Absolute; popup.style.width = width;
                popup.style.left = left; popup.style.top = anchor.yMax + 4;
                root.Add(popup);
            }
            owner.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (e.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
                pending?.Pause(); pending = owner.schedule.Execute(Show).StartingIn(HoverDelayMs);
            });
            owner.RegisterCallback<PointerLeaveEvent>(e => { if (e.pointerType == UnityEngine.UIElements.PointerType.mouse) Hide(); });
            owner.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.pointerType == UnityEngine.UIElements.PointerType.mouse) return;
                var pointerId = e.pointerId; pending?.Pause();
                pending = owner.schedule.Execute(() =>
                {
                    var slot = owner.parent; if (slot != null && slot.HasPointerCapture(pointerId)) slot.ReleasePointer(pointerId);
                    Show(); expiry?.Pause(); expiry = owner.schedule.Execute(Hide).StartingIn(TouchHideMs);
                }).StartingIn(LongPressMs);
            });
            owner.RegisterCallback<PointerUpEvent>(e => { if (e.pointerType != UnityEngine.UIElements.PointerType.mouse) pending?.Pause(); });
            owner.RegisterCallback<PointerCancelEvent>(_ => pending?.Pause());
            owner.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(className); return label;
        }

        private static Label GlyphLabel(string glyph, string className)
        {
            var label = Text(glyph, className);
            if (_glyphFonts == null) _glyphFonts = (JObject)JObject.Parse(Resources.Load<TextAsset>("Fonts/GlyphFonts")?.text ?? "{\"mapping\":{}}")["mapping"];
            var resource = (string)_glyphFonts[glyph] ?? SymbolFallbackFont;
            if (!Fonts.TryGetValue(resource, out var font)) { font = Resources.Load<Font>(resource); Fonts[resource] = font; }
            if (font != null) label.style.unityFont = font;
            return label;
        }
    }
}
