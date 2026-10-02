// EnemyTelegraphView.cs — UI Toolkit binding for enemy intent badges and Poise meters (F04).
// WIRING: OriginalRunPanel/OriginalCoopPanel call Attach(slot, telegraph) on each enemy
// target built by OriginalCombatLayout.Enemy. The slot keeps its name, click and layout;
// Attach swaps the text intent label for the badge and adds the Poise meter under HP.
// DATA: Domain/Original/EnemyTelegraph.cs (EnemyTelegraphViewModel) owns every number and
// every word. MODIFY: look in Resources/EnemyTelegraphs.uss. No rules or saved state here.
// Explain(element, text) is the shared hover/long-press panel (also card tag blurbs, US-13.4);
// ExplainStatuses binds StatusExplainer rows to an enemy's figure and status row (US-4.4).
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
        // A touch that travels this far is a scroll or drag, not a long press.
        private const float LongPressSlop = 12;
        // Glyphs the authored font map does not list (↑ ☾) are in NotoSansSymbols.
        private const string SymbolFallbackFont = "Fonts/NotoSansSymbols-Regular";
        private static StyleSheet _sheet;
        private static JObject _glyphFonts;
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>(StringComparer.Ordinal);

        /// <summary>Co-op uses the host's per-seat preview; older companions explicitly show base damage.</summary>
        public static EnemyTelegraph FromSnapshot(JObject enemy, JObject hero, JObject balance)
            => EnemyTelegraphViewModel.FromSnapshot(enemy, hero, (double?)balance?["poise"]?["growthMult"] ?? 1.25);

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

        /// <summary>
        /// US-4.4: the enemy figure and its status row explain every active status (authored
        /// tooltip text) on hover or long press. A short tap still selects the target.
        /// </summary>
        public static void ExplainStatuses(VisualElement slot, string enemyName, string instanceId, JObject enemy, IReadOnlyList<StatusExplanation> statuses)
        {
            if (slot == null) return;
            var text = StatusExplainer.Panel(enemyName + " · statuses", statuses, "Guard " + (enemy?["block"] ?? 0) + " · HP " + enemy?["hp"] + "/" + enemy?["maxHp"]);
            var caption = slot.Children().FirstOrDefault(c => c.ClassListContains("original-fighter-caption"));
            if (caption != null)
            {
                caption.name = "enemy-status-" + instanceId; caption.pickingMode = PickingMode.Position;
                caption.AddToClassList("telegraph-status-row");
                Explain(caption, text);
            }
            var figure = slot.Children().FirstOrDefault(c => c.ClassListContains("original-combat-figure"));
            if (figure != null) { figure.pickingMode = PickingMode.Position; Explain(figure, text); }
        }

        // Runtime UI Toolkit panels never draw `tooltip`, so the explanation is a floating
        // label on the panel root: after a hover delay (mouse) or on a long press (touch/pen).
        // A long press releases any ancestor's pointer capture (an enemy slot or a card
        // button) so it does not also select that control. The `tooltip` property is kept.
        /// <summary>Hover/long-press explanation panel; the first line of `text` is the title.</summary>
        public static void Explain(VisualElement owner, string text)
        {
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>(StyleSheetResource);
            Tooltip(owner, text);
        }

        private static void Tooltip(VisualElement owner, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            owner.tooltip = text;
            VisualElement popup = null; IVisualElementScheduledItem pending = null, expiry = null, leaving = null;
            // Owners sit inside Buttons whose Clickable captures the pointer after pointer-down,
            // so move/up/cancel go to that ancestor, not to the owner. Track the press from the
            // panel root in the TrickleDown phase (it sees every captured event) and unregister
            // as soon as the press ends, fires or the owner leaves the panel.
            var press = new LongPressTracker(LongPressSlop);
            VisualElement trackRoot = null;
            EventCallback<PointerMoveEvent> onMove = null; EventCallback<PointerUpEvent> onUp = null; EventCallback<PointerCancelEvent> onCancel = null;
            void StopTracking()
            {
                if (trackRoot == null) return;
                trackRoot.UnregisterCallback(onMove, TrickleDown.TrickleDown);
                trackRoot.UnregisterCallback(onUp, TrickleDown.TrickleDown);
                trackRoot.UnregisterCallback(onCancel, TrickleDown.TrickleDown);
                trackRoot = null;
            }
            void CancelPress() { press.Cancel(); pending?.Pause(); StopTracking(); }
            onMove = e => { if (press.Move(e.pointerId, e.position.x, e.position.y)) CancelPress(); };
            onUp = e => { if (press.End(e.pointerId)) CancelPress(); };
            onCancel = e => { if (press.End(e.pointerId)) CancelPress(); };
            void Hide() { CancelPress(); expiry?.Pause(); leaving?.Pause(); popup?.RemoveFromHierarchy(); popup = null; }
            void Show()
            {
                var root = owner.panel?.visualTree; if (root == null) return;
                popup?.RemoveFromHierarchy();
                // Pickable so a long body can be scrolled; the height is bounded to the viewport.
                popup = new VisualElement { pickingMode = PickingMode.Position }; popup.AddToClassList("telegraph-tooltip");
                if (_sheet != null) popup.styleSheets.Add(_sheet);
                var lines = text.Split('\n');
                popup.Add(Text(lines[0], "telegraph-tooltip-title"));
                if (lines.Length > 1)
                {
                    var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
                    scroll.AddToClassList("telegraph-tooltip-scroll"); scroll.style.flexShrink = 1; scroll.style.minHeight = 0;
                    scroll.Add(Text(string.Join("\n", lines.Skip(1)), "telegraph-tooltip-body"));
                    popup.Add(scroll);
                }
                var anchor = owner.worldBound; const float width = 230;
                var first = PopupPlacement.Place(anchor.center.x, anchor.yMin, anchor.yMax, width, 0, root.layout.width, root.layout.height);
                popup.style.position = Position.Absolute; popup.style.width = width; popup.style.flexDirection = FlexDirection.Column;
                popup.style.left = first.Left; popup.style.top = first.Top; popup.style.maxHeight = first.MaxHeight;
                // Keep long explanations on screen: below, else above, else clamped (bounded height).
                var shown = popup;
                shown.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var limit = root.layout.height; if (shown.layout.height <= 0 || limit <= 0) return;
                    var at = PopupPlacement.Place(anchor.center.x, anchor.yMin, anchor.yMax, width, shown.layout.height, root.layout.width, limit);
                    if (!Mathf.Approximately(shown.resolvedStyle.top, at.Top)) shown.style.top = at.Top;
                    if (!Mathf.Approximately(shown.resolvedStyle.maxHeight.value, at.MaxHeight)) shown.style.maxHeight = at.MaxHeight;
                });
                shown.RegisterCallback<PointerEnterEvent>(_ => leaving?.Pause());
                shown.RegisterCallback<PointerLeaveEvent>(e => { if (e.pointerType == UnityEngine.UIElements.PointerType.mouse) Hide(); });
                shown.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.pointerType == UnityEngine.UIElements.PointerType.mouse) return;
                    expiry?.Pause(); expiry = owner.schedule.Execute(Hide).StartingIn(TouchHideMs); // reading/scrolling keeps it open
                });
                root.Add(popup);
            }
            owner.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (e.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
                leaving?.Pause(); if (popup != null) return;
                pending?.Pause(); pending = owner.schedule.Execute(Show).StartingIn(HoverDelayMs);
            });
            // A short grace lets the mouse move onto the popup to scroll it.
            owner.RegisterCallback<PointerLeaveEvent>(e =>
            {
                if (e.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
                pending?.Pause(); leaving?.Pause(); leaving = owner.schedule.Execute(Hide).StartingIn(150);
            });
            owner.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.pointerType == UnityEngine.UIElements.PointerType.mouse) return;
                CancelPress();
                var root = owner.panel?.visualTree; if (root == null) return;
                var pointerId = e.pointerId; press.Begin(pointerId, e.position.x, e.position.y);
                trackRoot = root;
                root.RegisterCallback(onMove, TrickleDown.TrickleDown);
                root.RegisterCallback(onUp, TrickleDown.TrickleDown);
                root.RegisterCallback(onCancel, TrickleDown.TrickleDown);
                pending = owner.schedule.Execute(() =>
                {
                    StopTracking();
                    if (!press.Fire(pointerId)) return; // released, cancelled or moved: a tap or scroll
                    for (var up = owner.parent; up != null; up = up.parent) if (up.HasPointerCapture(pointerId)) up.ReleasePointer(pointerId);
                    Show(); expiry?.Pause(); expiry = owner.schedule.Execute(Hide).StartingIn(TouchHideMs);
                }).StartingIn(LongPressMs);
            });
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
