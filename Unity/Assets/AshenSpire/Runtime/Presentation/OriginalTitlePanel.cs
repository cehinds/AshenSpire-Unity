// OriginalTitlePanel.cs — the reference game's wordmark, ornaments and menu rhythm.
// WIRING: CampaignView supplies existing callbacks; this view owns no game state.
// MODIFY: OriginalTheme.uss controls spacing/colour and the JS build 1815 look (gold serif
// wordmark, framed menu bars, green NEW GAME, ornate dividers from Art/js1815/ui).
// JsEntry.uss adds the JS 01b startup gate and the 01c Continue preview (glyph, class, facts).
// Keep functional routes in CampaignView. Every control id is read by tools/*-playtest.cjs.
using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class OriginalTitlePanel : VisualElement
    {
        /// <summary>The save Continue resumes, for the JS 01c preview (class · act · floor).</summary>
        public sealed class ContinuePreview
        {
            public string ClassName; public int Act; public int Floor; public int Slot;
            public ContinuePreview(string className, int act, int floor, int slot) { ClassName = className; Act = act; Floor = floor; Slot = slot; }
        }
        public const string Wordmark = "ASHENEDSPIRE";
        public OriginalTitlePanel(Action begin, Action resume, bool canResume, Action collection,
            Action cooperative, Action settings, Action extras, Action saves = null, Action quickStart = null, Action history = null, bool unsavedProgress = false,
            ContinuePreview preview = null)
        {
            AddToClassList("original-title");
            var entrySheet = Resources.Load<StyleSheet>("JsEntry");
            if (entrySheet != null) styleSheets.Add(entrySheet);
            var wordmark = new Label(Wordmark); wordmark.AddToClassList("original-wordmark"); Add(wordmark);
            RegisterCallback<GeometryChangedEvent>(_ => wordmark.style.fontSize = Mathf.Clamp(contentRect.width * .1f, 22, 48));
            var subtitle = new Label("A ROGUELIKE DECKBUILDER"); subtitle.AddToClassList("original-subtitle"); Add(subtitle);
            Add(Ornament());
            var menu = new VisualElement(); menu.AddToClassList("title-menu-grid"); Add(menu);
            var resumeButton = canResume && unsavedProgress ? Held("native-continue", "Continue the saved climb", resume) : Entry("native-continue", "Continue", resume, canResume); menu.Add(resumeButton);
            if (canResume) resumeButton.AddToClassList("title-primary");
            // JS 01c: an available Continue names the exact save it resumes. The held variant keeps
            // its text, which HoldConfirmButton rewrites while armed.
            if (canResume && !unsavedProgress && preview != null) Preview(resumeButton, preview);
            if (quickStart != null) { var quick = Entry("native-quick-start", "Quick start", quickStart); quick.AddToClassList("title-quick"); quick.tooltip = "Begin with the recommended character and a fresh seed."; menu.Add(quick); }
            if (saves != null) menu.Add(Entry("native-slots", "Load", saves));
            var start = Entry("native-new", "Begin the climb", begin); start.AddToClassList("title-new"); menu.Add(start);
            if (history != null) menu.Add(Entry("native-history", "Run history", history));
            menu.Add(Entry("native-profile", "Compendium", collection));
            var settingsEntry = Entry("settings", "Settings", settings); menu.Add(settingsEntry);
            // JS title: SETTINGS carries the engraved gold gear.
            var gear = Resources.Load<Texture2D>("Art/js1815/ui/icon-settings");
            if (gear != null) { var icon = new Image { image = gear, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore }; icon.AddToClassList("title-entry-icon"); settingsEntry.Add(icon); }
            menu.Add(Entry("native-coop", "Forsaken together", cooperative));
            Add(Ornament());
            var more = Entry("title-extras", "More", extras); more.AddToClassList("title-more"); Add(more);
            var tagline = new Label("THE EMBER FLOWS UPWARD. FOLLOW IT."); tagline.AddToClassList("original-tagline"); Add(tagline);
            if (OriginalStartupGate.Wanted()) RegisterCallback<AttachToPanelEvent>(MountGate);
        }
        // The gate covers the whole app root (outside the scrolling body) and hides this menu
        // until it is dismissed, so no input reaches Continue/New underneath it.
        private void MountGate(AttachToPanelEvent _)
        {
            UnregisterCallback<AttachToPanelEvent>(MountGate);
            if (!OriginalStartupGate.Wanted()) return;
            VisualElement host = this;
            while (host.parent != null && !host.ClassListContains("app")) host = host.parent;
            if (host == this) return;
            style.visibility = Visibility.Hidden; SetEnabled(false);
            var gate = new OriginalStartupGate(() =>
            {
                style.visibility = StyleKeyword.Null; SetEnabled(true);
                var first = this.Q<Button>("native-continue");
                (first != null && first.enabledSelf ? first : this.Q<Button>("native-new"))?.Focus();
            });
            host.Add(gate);
            // Later screen chrome (notices) is added after the title; keep the gate on top.
            gate.schedule.Execute(() => { if (gate.parent != null) { gate.BringToFront(); gate.Focus(); } });
        }
        private static void Preview(Button button, ContinuePreview preview)
        {
            var facts = "ACT " + preview.Act + " · FLOOR " + preview.Floor + " · SLOT " + (preview.Slot + 1);
            button.text = ""; button.AddToClassList("title-continue-preview");
            button.tooltip = "Continue · " + preview.ClassName + " · " + facts;
            var glyph = Icon("Art/js1815/ui/icon-journey", "title-continue-glyph"); if (glyph != null) button.Add(glyph);
            var copy = new VisualElement { pickingMode = PickingMode.Ignore }; copy.AddToClassList("title-continue-copy"); button.Add(copy);
            copy.Add(Text("CONTINUE", "title-continue-heading"));
            copy.Add(Text((preview.ClassName ?? "").ToUpperInvariant(), "title-continue-name"));
            copy.Add(Text(facts, "title-continue-facts"));
            var arrow = Icon("Art/js1815/ui/icon-next", "title-continue-arrow"); if (arrow != null) button.Add(arrow);
        }
        private static Image Icon(string resource, string style)
        {
            var texture = Resources.Load<Texture2D>(resource); if (texture == null) return null;
            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore }; image.AddToClassList(style); return image;
        }
        private static Label Text(string value, string style) { var label = new Label(value) { pickingMode = PickingMode.Ignore }; label.AddToClassList(style); return label; }
        private static Button Entry(string id, string label, Action action, bool enabled = true)
        {
            var button = new Button(action) { name = id, text = label.ToUpperInvariant() };
            button.AddToClassList("button"); button.AddToClassList("original-menu-entry"); button.SetEnabled(enabled); return button;
        }
        private static Button Held(string id, string label, Action action)
        {
            var button = Entry(id, label, null);
            HoldConfirmButton.Bind(button, AshenSpire.Domain.Original.ConfirmationPolicy.LoadSlot, action);
            return button;
        }
        internal static VisualElement Ornament()
        {
            var row = new VisualElement(); row.AddToClassList("original-ornament");
            var left = new VisualElement(); left.AddToClassList("original-rule"); row.Add(left);
            var diamond = new VisualElement(); diamond.AddToClassList("original-gem"); row.Add(diamond);
            var right = new VisualElement(); right.AddToClassList("original-rule"); row.Add(right); return row;
        }
    }

    /// <summary>
    /// JS 01b startup gate (src/ui/components/startupGate.js): the folded title over the dark river
    /// citadel, shown once per launch before the title menu. One full-screen button
    /// (native-startup-gate): a click, a tap, Enter/Space or the gamepad confirm dismisses it.
    /// It never appears to the browser harnesses: they serve the player from 127.0.0.1, and a
    /// loopback page skips it unless the URL asks for it with ?gate=1 (?gate=0 or ?nogate skip it
    /// anywhere). Batch mode and ASHENSPIRE_SKIP_STARTUP_GATE=1 skip it too.
    /// </summary>
    public sealed class OriginalStartupGate : Button
    {
        private static bool _done;
        private readonly Action _dismissed;
        private readonly Label _prompt;
        private IVisualElementScheduledItem _pulse;
        public static bool Wanted()
        {
            if (_done || UnityEngine.Application.isBatchMode) return false;
            if (Environment.GetEnvironmentVariable("ASHENSPIRE_SKIP_STARTUP_GATE") == "1") return false;
            if (Uri.TryCreate(UnityEngine.Application.absoluteURL, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                var query = uri.Query ?? "";
                if (query.Contains("gate=0") || query.Contains("nogate")) return false;
                if (query.Contains("gate=1")) return true;
                if (uri.IsLoopback) return false;
            }
            return true;
        }
        public OriginalStartupGate(Action dismissed)
        {
            _dismissed = dismissed;
            name = "native-startup-gate"; text = ""; tooltip = "Enter AshenedSpire";
            AddToClassList("js-startup-gate");
            var sheet = Resources.Load<StyleSheet>("JsEntry"); if (sheet != null) styleSheets.Add(sheet);
            clicked += Dismiss;
            var scene = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore }; scene.AddToClassList("js-startup-scene"); Add(scene);
            bool? portrait = null;
            scene.RegisterCallback<GeometryChangedEvent>(e =>
            {
                var next = e.newRect.width < e.newRect.height; if (portrait == next) return; portrait = next;
                scene.image = Resources.Load<Texture2D>("Art/js1815/entry/startup-gate-" + (next ? "mobile" : "desktop"));
            });
            var lockup = new VisualElement { pickingMode = PickingMode.Ignore }; lockup.AddToClassList("js-startup-lockup"); Add(lockup);
            var plate = new VisualElement { pickingMode = PickingMode.Ignore }; plate.AddToClassList("js-startup-wordmark-plate"); lockup.Add(plate);
            var wordmark = new Label(OriginalTitlePanel.Wordmark) { pickingMode = PickingMode.Ignore }; wordmark.AddToClassList("js-startup-wordmark"); plate.Add(wordmark);
            lockup.RegisterCallback<GeometryChangedEvent>(_ => wordmark.style.fontSize = Mathf.Clamp(resolvedStyle.width * .1f, 26, 54));
            var subtitle = new Label("A ROGUELIKE DECKBUILDER") { pickingMode = PickingMode.Ignore }; subtitle.AddToClassList("js-startup-subtitle"); lockup.Add(subtitle);
            var ornament = OriginalTitlePanel.Ornament(); ornament.pickingMode = PickingMode.Ignore; ornament.AddToClassList("js-startup-ornament"); lockup.Add(ornament);
            var touch = UnityEngine.Application.isMobilePlatform || Input.touchSupported && !Input.mousePresent;
            _prompt = new Label(touch ? "TAP TO CONTINUE" : "PRESS ENTER OR SPACE") { pickingMode = PickingMode.Ignore }; _prompt.AddToClassList("js-startup-prompt"); lockup.Add(_prompt);
            var stamp = new Label("BUILD " + UnityEngine.Application.version) { pickingMode = PickingMode.Ignore }; stamp.AddToClassList("js-startup-stamp"); Add(stamp);
            // The prompt follows the input in use (JS prompts per input family).
            RegisterCallback<PointerMoveEvent>(e => _prompt.text = e.pointerType == "touch" ? "TAP TO CONTINUE" : "CLICK TO CONTINUE");
            RegisterCallback<KeyDownEvent>(e => _prompt.text = "PRESS ENTER OR SPACE");
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (FeelDriver.Settings.ReducedMotion) return;
                _pulse = schedule.Execute(() => _prompt.ToggleInClassList("js-startup-prompt--dim")).Every(1800);
            });
            RegisterCallback<DetachFromPanelEvent>(_ => _pulse?.Pause());
        }
        private void Dismiss()
        {
            if (_done) return;
            _done = true; _pulse?.Pause();
            RemoveFromHierarchy();
            _dismissed?.Invoke();
        }
    }
}
