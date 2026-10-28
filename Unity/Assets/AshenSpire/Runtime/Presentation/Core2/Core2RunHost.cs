// Core2RunHost.cs — the Core 2 run on screen (JS src/main.js: showMap / enterCombat / showGameProfileReward / showShop /
// showRest / showEvent / showFinishedRun). It owns a Core2RunFlow (which owns the Core2RunSession and saves after every
// command), shows the screen for flow.Screen through the ScreenRouter, and redraws on every accepted command.
//
// FIGHT: while Screen is Fight the host mounts Core2Screens.FightScreenFactory's screen (the combat phase's), or, when
// none is registered, Core2FallbackFightScreen (play a card, End turn, Auto). The fight screen calls back when
// Fight.Result is set; the host then settles the fight through the flow.
// INPUT: every control is a focusable Button, so keyboard arrows / Enter and GamepadNavigator reach it; the screen root
// takes the JS hot keys (D deck, R Armoury, T stats, M menu, Esc back/close); touch taps buttons and drags the map.
// FAILURE: a command the combat domain cannot run yet (NotSupportedException) is reported and the host leaves for the
// title; the slot keeps the save from before that command.
// Presentation only: every rule is AshenSpire.Domain.Core2's; every word is Core2Text's (the JS table).
using System;
using System.Linq;
using AshenSpire.Domain.Core2;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation.Core2
{
    public sealed partial class Core2RunHost : IDisposable
    {
        private enum Overlay { None, Armoury, Deck, Menu }

        private readonly ScreenRouter _router;
        private readonly VisualElement _root;
        private readonly Core2RunFlow _flow;
        private readonly Action<string> _exit;
        private readonly Action _openSettings;
        private ICore2FightScreen _fight;
        private Overlay _overlay;
        private VisualElement _screen;
        private bool _disposed;
        private Core2Screen? _shown;
        private Core2SpoilsModel _spoils;
        private Core2ShopModel _shop;
        private Core2RestModel _rest;
        private Core2EventModel _event;
        private string _status = "";

        /// <summary>Raised after every redraw (the CampaignView diagnostics report and the music cue read it).</summary>
        public event Action Rendered;
        public Core2RunFlow Flow => _flow;

        /// <param name="exit">Back to the title, with a one-line notice (null for none).</param>
        public Core2RunHost(ScreenRouter router, VisualElement root, Core2RunFlow flow, Action<string> exit, Action openSettings = null)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _exit = exit;
            _openSettings = openSettings;
            Core2Ui.Style(root);
            _flow.Changed += OnChanged;
        }

        public void Start() => Render();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flow.Changed -= OnChanged;
            UnmountFight();
        }

        private void OnChanged() { if (!_disposed) Render(); }

        /// <summary>Run a screen command; a refusal is shown on the screen's status line, a domain gap leaves the run.</summary>
        private void Run(Func<string> command)
        {
            try
            {
                var error = command();
                if (error != null) { _status = error; Render(); }
            }
            catch (NotSupportedException gap) { Fail(gap); }
            catch (Exception error) when (error is ArgumentException || error is System.Collections.Generic.KeyNotFoundException) { Fail(error); }
        }

        private void Fail(Exception error)
        {
            Debug.LogException(error);
            var exit = _exit;
            Dispose();
            _flow.Release();
            exit?.Invoke("This climb reached something the Core 2 rules cannot run yet: " + error.Message + " Your last save is kept.");
        }

        // ---- the dispatcher --------------------------------------------------------------------------------------

        private bool _rendering, _again;

        /// <summary>Redraw for the flow's screen. A command accepted while a screen is being built (opening the Armoury
        /// clears its glow, which saves) asks for one more pass instead of nesting a second build inside the first.</summary>
        private void Render()
        {
            if (_disposed || _flow.Session == null) return;
            if (_rendering) { _again = true; return; }
            _rendering = true;
            try
            {
                for (var pass = 0; pass < 3; pass++)
                {
                    _again = false;
                    RenderScreen();
                    if (!_again || _disposed || _flow.Session == null) break;
                }
            }
            catch (NotSupportedException gap) { _rendering = false; Fail(gap); return; }
            finally { _rendering = false; }
            Rendered?.Invoke();
        }

        private void RenderScreen()
        {
            var screen = _flow.Screen;
            if (screen == Core2Screen.Fight) { MountFight(); return; }
            UnmountFight();
            if (_shown != screen)
            {
                // A new screen starts clean: no model, selection, open review or modal carries over from the last one.
                _spoils = null; _shop = null; _rest = null; _event = null; _status = ""; _selectedNode = null;
                _review = null; _shopInspect = null; _restPanel = null; _restPick = null; _inspecting = false; _armouryRow = null; _armouryReplace = false;
            }
            _shown = screen;
            var body = _router.Show("core2:" + screen.ToString().ToLowerInvariant());
            ScreenRouter.SetFixed(body, true);
            _screen = Core2Ui.Div("c2-screen c2-fill");
            body.Add(_screen);
            _screen.focusable = true;
            _screen.RegisterCallback<KeyDownEvent>(OnKey);
            if (screen != Core2Screen.Finished) _screen.Add(Hud());
            switch (screen)
            {
                case Core2Screen.Map: BuildMap(_screen, interactive: _overlay == Overlay.None); break;
                case Core2Screen.Reward: BuildMap(_screen, interactive: false); BuildSpoils(_screen); break;
                case Core2Screen.Shop: BuildShop(_screen); break;
                case Core2Screen.Rest: BuildRest(_screen); break;
                case Core2Screen.Event: BuildEvent(_screen); break;
                case Core2Screen.Finished: BuildFinish(_screen); break;
            }
            BuildOverlay(_screen);
            Core2Ui.InertBehindTopDoor(_screen);
            _screen.schedule.Execute(() => { if (_screen.focusController?.focusedElement == null) Core2Ui.FocusFirst(_screen); });
        }

        // ---- the fight seam --------------------------------------------------------------------------------------

        private void MountFight()
        {
            if (_fight != null) return;
            _overlay = Overlay.None;
            _shown = Core2Screen.Fight;
            var body = _router.Show("core2:fight");
            ScreenRouter.SetFixed(body, true);
            _fight = Core2Screens.FightScreenFactory?.Invoke() ?? new Core2FallbackFightScreen();
            try { _fight.Mount(body, _flow.Session, OnFightOver); }
            catch (NotSupportedException gap) { _fight = null; Fail(gap); }
        }

        private void UnmountFight()
        {
            var fight = _fight;
            _fight = null;
            fight?.Unmount();
        }

        private void OnFightOver()
        {
            if (_disposed || _flow.Session?.Fight?.Result == null) return;
            UnmountFight();
            Run(() => _flow.FightOver());
        }

        // ---- the HUD band (JS runHud) ----------------------------------------------------------------------------

        private VisualElement Hud()
        {
            var model = Core2HudModel.From(_flow.Data, _flow.Session);
            var hud = Core2Ui.Div("c2-hud");
            var left = Core2Ui.Div("c2-hud-left");
            var hp = Core2Ui.Div("c2-hud-hp");
            hp.Add(Core2Ui.Icon("Art/js1815/ui/icon-health", "c2-relic"));
            hp.Add(Core2Ui.Text(model.HpText, "c2-hud-hp-text"));
            var rule = Core2Ui.Div("c2-hud-rule");
            var fill = Core2Ui.Div("c2-hud-rule-fill");
            fill.style.width = Length.Percent((float)(model.HpFraction * 100));
            rule.Add(fill);
            hp.Add(rule);
            left.Add(hp);
            var relics = Core2Ui.Div("c2-hud-relics");
            foreach (var (id, name, text) in model.Relics)
            {
                var relic = Core2Ui.Icon("Art/js1815/ui/icon-relic", "c2-relic");
                relic.tooltip = name + " — " + text;
                relic.pickingMode = PickingMode.Position;
                relics.Add(relic);
            }
            left.Add(relics);
            hud.Add(left);
            var right = Core2Ui.Div("c2-hud-right");
            var tools = Core2Ui.Div("c2-hud-tools");
            var armoury = Core2Ui.Btn("open-armoury", "", () => Open(Overlay.Armoury), "c2-icon-btn" + (model.ArmouryNew ? " c2-glow" : ""), tooltip: model.ArmouryLabel);
            Core2Ui.Surface(armoury, "Art/js1815/ui/icon-equipment");
            tools.Add(armoury);
            var menu = Core2Ui.Btn("open-menu", "", () => Open(Overlay.Menu), "c2-icon-btn", tooltip: "Menu");
            Core2Ui.Surface(menu, "Art/js1815/ui/icon-menu");
            tools.Add(menu);
            right.Add(tools);
            var cinders = Core2Ui.Div("c2-hud-cinders");
            cinders.Add(Core2Ui.Text(model.CindersLabel, "c2-hud-cinders-label"));
            cinders.Add(Core2Ui.Text(model.Cinders, "c2-hud-cinders-value"));
            right.Add(cinders);
            hud.Add(right);
            return hud;
        }

        // ---- overlays: the quick menu, the Armoury, the Reading Desk ---------------------------------------------

        private void Open(Overlay overlay)
        {
            // Leaving the Reading Desk for another door cancels its edit, as Close does: no invisible edit stays open.
            if (_overlay == Overlay.Deck && overlay != Overlay.Deck && _flow.DeckEdit != null) _flow.CancelDeckEdit();
            if (overlay == Overlay.Deck)
            {
                var error = _flow.DeckEdit == null ? _flow.OpenDeckEdit() : null;
                if (error != null) { _status = error; Render(); return; }
            }
            _overlay = overlay;
            Render();
        }

        private void Close()
        {
            if (_overlay == Overlay.Deck && _flow.DeckEdit != null) { _flow.CancelDeckEdit(); }
            _overlay = Overlay.None;
            Render();
        }

        private void BuildOverlay(VisualElement screen)
        {
            switch (_overlay)
            {
                case Overlay.Menu: BuildMenu(screen); break;
                case Overlay.Armoury: BuildArmoury(screen); break;
                case Overlay.Deck: BuildDeckEditor(screen); break;
            }
        }

        /// <summary>JS quick menu (desktop-03c): Settings, Inventory, Character, Save, Save and Quit.</summary>
        private void BuildMenu(VisualElement screen)
        {
            var door = Core2Ui.Door("QUICK MENU", "Menu");
            if (_openSettings != null) door.Body.Add(Core2Ui.Btn("quick-settings", "Settings", () => { _overlay = Overlay.None; _openSettings(); }));
            door.Body.Add(Core2Ui.Btn("quick-inventory", "Inventory", () => Open(Overlay.Armoury)));
            door.Body.Add(Core2Ui.Btn("quick-character", "Character", () => Open(Overlay.Armoury)));
            door.Body.Add(Core2Ui.Btn("quick-save", "Save", () => { _flow.Save(); _router.Toast("Saved.", 1500); Close(); }));
            door.Body.Add(Core2Ui.Btn("quick-save-quit", "Save and Quit", () => { _flow.Save(); var exit = _exit; Dispose(); _flow.Release(); exit?.Invoke(null); }));
            door.Foot.Add(Core2Ui.Btn("quick-close", Core2Text.T("common.close"), Close, "c2-btn-fill"));
            screen.Add(door.Scrim);
            Core2Ui.FocusFirst(door.Door);
        }

        // ---- keys (JS input.js actions; the pad sends the same keys through GamepadNavigator) ---------------------

        private void OnKey(KeyDownEvent evt)
        {
            if (evt.target is TextField) return;
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    if (_overlay != Overlay.None) { Close(); evt.StopPropagation(); }
                    else if (_flow.Screen == Core2Screen.Map && _selectedNode != null) { _selectedNode = null; Render(); evt.StopPropagation(); }
                    break;
                case KeyCode.M: if (_overlay == Overlay.None && _flow.Screen != Core2Screen.Finished) { Open(Overlay.Menu); evt.StopPropagation(); } break;
                case KeyCode.R: if (_overlay == Overlay.None && _flow.Screen == Core2Screen.Map) { Open(Overlay.Armoury); evt.StopPropagation(); } break;
                case KeyCode.D: if (_overlay == Overlay.None && _flow.Screen == Core2Screen.Map) { Open(Overlay.Deck); evt.StopPropagation(); } break;
                case KeyCode.T: if (_overlay == Overlay.None && _flow.Screen == Core2Screen.Map) { Open(Overlay.Armoury); evt.StopPropagation(); } break;
            }
        }

        private Label StatusLine(string fallback = "")
        {
            var text = string.IsNullOrEmpty(_status) ? fallback : _status;
            _status = "";
            return Core2Ui.Text(text, "c2-status");
        }
    }
}
