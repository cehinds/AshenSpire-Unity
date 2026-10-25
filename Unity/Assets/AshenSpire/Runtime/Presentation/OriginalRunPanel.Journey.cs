// OriginalRunPanel.Journey.cs — the HTML/JS build 1815 journey details, presentation only.
// LOOK: the shared run HUD (heart + HP over a proportional 3 px rule, relic coins, CINDERS and the
// Armoury / Quick-menu icon buttons), the Quick menu dropdown (JS 03c), spoils rows with engraved
// icons and NEW / TAKEN chips (06, 06b), merchant category tabs and thumbnails (08c), the rest
// recovery preview (08a) and the event stage figures (08e). Geometry lives in Resources/JsJourney.uss;
// art in Art/js1815/journey (tools/import-js1815-journey-art.mjs) plus the combat relic, figure and
// footer art Art/js1815/combat already holds.
// RULES: none. Every decoration reads the session; every control here opens an existing screen or
// calls an existing command. Existing control ids, texts and enabled states are left as they were:
// decorations are children that never take pointer input.
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed partial class OriginalRunPanel
    {
        private const string JourneyArtRoot = "Art/js1815/journey/";
        private static StyleSheet _journeySheet;
        private VisualElement _quickMenu;
        // JS eventSpeakers (src/content/generated/eventSpeakers.js + speakers.js, build 1815): who stands
        // on the right of the dialogue stage, and the enemy art that portrays them. Presentation copy.
        private static readonly (string Event, string Name, string Portrait)[] EventSpeakers =
        {
            ("graveOfTheNameless", "The Nameless", "graveWisp"),
            ("namelessRest", "The Nameless", "graveWisp"),
            ("namelessKeeper", "The Keeper of the Nameless", "emberStarvedPilgrim"),
        };

        private void JourneyStyles()
        {
            if (_journeySheet == null) _journeySheet = Resources.Load<StyleSheet>("JsJourney");
            if (_journeySheet != null && !_root.styleSheets.Contains(_journeySheet)) _root.styleSheets.Add(_journeySheet);
        }
        private static Texture2D JourneyArt(string name) => Resources.Load<Texture2D>(JourneyArtRoot + name);
        private static Image JourneyPicture(Texture2D texture, string className)
        {
            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(className); return image;
        }

        // ---- Shared run HUD (styles/shared-run-hud.css) ------------------------------------------------
        // The map keeps it in the flow above the act strip. Other run screens are framed panels, so the band
        // sits in the panel's top margin and is stretched to the scroll width (see PlaceBleedHud).
        private void JourneyHud(JObject player, JObject run)
        {
            JourneyStyles(); _quickMenu = null;
            var bleed = _game.Phase != OriginalRunPhase.Map;
            var hud = new VisualElement { name = "native-run-hud", pickingMode = PickingMode.Ignore };
            hud.AddToClassList("js-journey-hud"); hud.AddToClassList(bleed ? "js-hud-bleed" : "js-hud-inflow");
            hud.tooltip = "Act " + _game.ActNumber + " · MP " + player["mana"] + "/" + player["maxMana"] + " · SP " + player["stamina"] + "/" + player["maxStamina"];
            var hp = new VisualElement { pickingMode = PickingMode.Ignore }; hp.AddToClassList("js-journey-hp");
            hp.Add(OriginalCombatLayout.Icon("icons/status-health", "js-journey-heart"));
            hp.Add(OriginalCombatLayout.Label(player["hp"] + "/" + player["maxHp"], "js-journey-hp-value"));
            var rule = new VisualElement { pickingMode = PickingMode.Ignore }; rule.AddToClassList("js-journey-hp-rule");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("js-journey-hp-fill");
            var max = (float?)player["maxHp"] ?? 0; var now = (float?)player["hp"] ?? 0;
            fill.style.width = Length.Percent(max > 0 ? Mathf.Clamp01(now / max) * 100 : 0);
            rule.Add(fill); hp.Add(rule); hud.Add(hp);
            hud.Add(HudExtras(run));
            var tools = new VisualElement { pickingMode = PickingMode.Ignore }; tools.AddToClassList("js-journey-hud-tools");
            HudButton(tools, "native-hud-armoury", "footer/button-armoury", "Armoury · equipment and weapon sets", Equipment);
            HudButton(tools, "native-hud-quick", "footer/button-menu", "Quick menu", ToggleQuickMenu);
            hud.Add(tools);
            var purse = new VisualElement { pickingMode = PickingMode.Ignore }; purse.AddToClassList("js-journey-cinders");
            purse.Add(OriginalCombatLayout.Label("CINDERS", "js-journey-cinders-label")); purse.Add(OriginalCombatLayout.Label(run["cinders"] + "", "js-journey-cinders-value"));
            var stones = (int?)run["smithingStones"] ?? 0;
            if (stones > 0) { purse.Add(OriginalCombatLayout.Label("STONES", "js-journey-cinders-label")); purse.Add(OriginalCombatLayout.Label(stones + "", "js-journey-cinders-value")); }
            purse.tooltip = run["cinders"] + " cinders · " + stones + " Smithing Stones";
            hud.Add(purse);
            _root.Add(hud);
            if (!bleed) return;
            // The panel's top margin holds the band; event screens already leave their stage there.
            var reserve = _game.Phase != OriginalRunPhase.Event && _game.Phase != OriginalRunPhase.EventResult;
            var body = _root;
            hud.RegisterCallback<AttachToPanelEvent>(_ => { if (reserve) body.style.marginTop = 108; });
            hud.RegisterCallback<DetachFromPanelEvent>(_ => { if (reserve) body.style.marginTop = StyleKeyword.Null; });
            if (reserve && body.panel != null) body.style.marginTop = 108;
            hud.RegisterCallback<GeometryChangedEvent>(_ => PlaceBleedHud(body, hud));
            if (!body.ClassListContains("js-hud-bleed-host"))
            {
                body.AddToClassList("js-hud-bleed-host");
                body.RegisterCallback<GeometryChangedEvent>(_ => PlaceBleedHud(body, body.Q("native-run-hud")));
            }
        }
        // Stretch the band from the panel's margin to the full scroll width. Layout rects (not world
        // bounds), so the screen-enter slide never skews it; equal values raise no further events.
        private static void PlaceBleedHud(VisualElement body, VisualElement hud)
        {
            if (hud == null || !hud.ClassListContains("js-hud-bleed") || body.parent == null) return;
            var width = body.parent.layout.width; if (float.IsNaN(width) || width < 1 || float.IsNaN(body.layout.x)) return;
            hud.style.left = -body.layout.x - body.resolvedStyle.borderLeftWidth;
            hud.style.top = -body.layout.y - body.resolvedStyle.borderTopWidth;
            hud.style.width = width;
        }
        // Row 2: relic coins (Settings → Relics in map header hides them, map-header-relics) and, on the
        // map, the run seed (Settings → Seed in map header, map-header-seed).
        private VisualElement HudExtras(JObject run)
        {
            var row = new VisualElement { name = "native-map-header-extras", pickingMode = PickingMode.Ignore }; row.AddToClassList("js-journey-hud-extras");
            var coins = new VisualElement { pickingMode = PickingMode.Ignore }; coins.AddToClassList("js-journey-relics"); coins.AddToClassList("map-header-relics");
            var names = new System.Collections.Generic.List<string>();
            foreach (var id in (run["relics"] as JArray ?? new JArray()).Values<string>())
            {
                var name = (string)_game.Catalog.Record("relics", id)?["name"] ?? id; names.Add(name);
                var coin = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = name }; coin.AddToClassList("js-journey-relic");
                var art = OriginalCombatLayout.Art("relics/" + id);
                if (art != null) coin.Add(JourneyPicture(art, "js-journey-relic-art"));
                else coin.Add(OriginalCombatLayout.Label(name.Substring(0, Math.Min(1, name.Length)), "js-journey-relic-initial"));
                coins.Add(coin);
            }
            if (names.Count > 0) { coins.tooltip = "Relics · " + string.Join(", ", names); row.Add(coins); }
            var seed = (string)run["seedString"] ?? run["seed"]?.ToString();
            if (_game.Phase == OriginalRunPhase.Map && !string.IsNullOrEmpty(seed)) row.Add(OriginalCombatLayout.Label("Seed " + seed, "map-header-seed"));
            return row;
        }
        private void HudButton(VisualElement parent, string id, string art, string tip, Action command)
        {
            var button = Button(id, "", command, parent); button.tooltip = tip;
            button.AddToClassList("js-journey-icon-button"); button.Add(OriginalCombatLayout.Icon(art, "js-journey-icon-art"));
        }

        // ---- Quick menu (JS 03c): a dropdown under the top-right icon --------------------------------
        // Rows are the run panel's own doors: deck, equipment, controls, save and return to title.
        private void ToggleQuickMenu()
        {
            if (_quickMenu?.parent != null) { _quickMenu.RemoveFromHierarchy(); _quickMenu = null; _report(); return; }
            var hud = _root.Q("native-run-hud"); if (hud == null) return;
            var menu = new VisualElement { name = "native-quick-menu-panel" }; menu.AddToClassList("js-quick-menu");
            menu.Add(OriginalCombatLayout.Label("QUICK MENU", "js-quick-eyebrow"));
            void Row(string id, string text, Action command, string hint = null, bool exit = false)
            {
                var row = Button(id, text, () => { _quickMenu?.RemoveFromHierarchy(); _quickMenu = null; command(); }, menu);
                row.AddToClassList("js-quick-row"); row.EnableInClassList("js-quick-exit", exit);
                if (hint != null) row.Add(OriginalCombatLayout.Label(hint, "js-quick-chip"));
            }
            void Rule() { var rule = new VisualElement { pickingMode = PickingMode.Ignore }; rule.AddToClassList("js-quick-rule"); menu.Add(rule); }
            var keys = _settings?.KeyBindings ?? OriginalPlayerSettings.DefaultKeyBindings;
            string Key(string action) => keys.TryGetValue(action, out var key) ? OriginalKeyBindings.DisplayKey(key) : null;
            Row("native-quick-controls", "Controls", JourneyControls);
            Rule();
            Row("native-quick-deck", "Deck", Deck, Key("combatDeck"));
            Row("native-quick-equipment", "Equipment", Equipment);
            Rule();
            Row("native-quick-save", "Save and Quit", _menu, null, true);
            // Under the HUD's right edge, in the body's own coordinates (the HUD may sit in the margin).
            var width = Math.Min(310f, Math.Max(200f, hud.layout.width - 16));
            menu.style.width = width;
            menu.style.left = hud.layout.xMax - width - 8;
            menu.style.top = hud.layout.yMax - 18;
            _root.Add(menu); menu.BringToFront(); _quickMenu = menu;
            menu.Q<Button>("native-quick-controls")?.Focus(); _report();
        }
        // The Quick menu's Controls row: the map and screen shortcuts as currently bound. Read-only.
        private void JourneyControls()
        {
            Inspection("CONTROLS", Render);
            var keys = _settings?.KeyBindings ?? OriginalPlayerSettings.DefaultKeyBindings;
            foreach (var action in OriginalKeyBindings.MapActions.Concat(new[] { "combatDeck" }))
                if (keys.TryGetValue(action, out var key)) Text(OriginalKeyBindings.DisplayKey(key) + " · " + OriginalKeyBindings.Label(action), "caption");
            Text("Tap a lit route to climb at once, or point at a room (or focus it) and press Enter in the band below the map. Drag or scroll to explore.", "caption");
            Text("Esc · close this screen\nTab · move between controls\nEnter · activate the focused button", "caption");
            Text("Change shortcuts in Settings → Controls.", "caption");
            Button("native-journey-keys-back", "Back", Render); _report();
        }

        // ---- Spoils (JS 06, 06b) -----------------------------------------------------------------------
        private static readonly string[] RewardKinds = { "cinders", "card", "flask", "armament", "relic" };
        private static Texture2D RewardIcon(string kind) =>
            kind == "relic" ? JourneyArt("icon-skill") : Resources.Load<Texture2D>("Art/js1815/ui/icon-" + (kind == "cinders" ? "cinders" : kind == "card" ? "deck" : kind == "flask" ? "flask-health" : kind == "armament" ? "attack" : "reward"));
        private bool RewardOffered(string kind)
        {
            var offers = _game.Room["rewards"];
            return kind == "cinders" ? (int?)offers?["cinders"] > 0 : kind == "card" ? (offers?["cardIds"] as JArray)?.Count > 0 : !string.IsNullOrEmpty((string)offers?[kind + "Id"]);
        }
        private string RewardTitle(string kind)
        {
            var offers = _game.Room["rewards"];
            if (kind == "cinders") return offers?["cinders"] + " cinders";
            if (kind == "card") return "Card";
            var table = kind == "armament" ? "equipment.armaments" : kind + "s";
            return (string)_game.Catalog.Record(table, (string)offers?[kind + "Id"])?["name"] ?? OriginalCardText.Humanize(kind);
        }
        // "VICTORY" head: eyebrow, gold title and the claimed count at the right.
        private void SpoilsHead()
        {
            JourneyStyles();
            var states = _game.Room["states"];
            var offered = RewardKinds.Count(RewardOffered); var taken = RewardKinds.Count(kind => (string)states?[kind] == "taken");
            var head = new VisualElement { pickingMode = PickingMode.Ignore }; head.AddToClassList("js-spoils-head");
            head.Add(OriginalCombatLayout.Label("CLAIM YOUR SPOILS", "js-spoils-eyebrow"));
            head.Add(OriginalCombatLayout.Label("VICTORY", "js-spoils-title"));
            head.Add(OriginalCombatLayout.Label(taken + " of " + offered + " claimed", "js-spoils-count"));
            _root.Add(head);
            // Claimed and left-behind rows stay listed, dimmed, with their TAKEN / SKIPPED chip.
            foreach (var kind in RewardKinds)
            {
                var state = (string)states?[kind]; if (state == null || !RewardOffered(kind)) continue;
                var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("js-reward-row"); row.AddToClassList("js-reward-locked");
                row.Add(JourneyPicture(RewardIcon(kind), "js-reward-icon"));
                var copy = new VisualElement { pickingMode = PickingMode.Ignore }; copy.AddToClassList("js-reward-copy");
                copy.Add(OriginalCombatLayout.Label(RewardTitle(kind), "js-reward-title"));
                copy.Add(OriginalCombatLayout.Label(state == "taken" ? "TAKEN" : "SKIPPED", "js-chip"));
                row.Add(copy); _root.Add(row);
            }
        }
        // A pending take button as a framed reward row: engraved icon at the left, NEW chip at the right.
        private void RewardRow(Button button, string kind)
        {
            if (button == null) return;
            button.AddToClassList("js-reward-row-button");
            button.Add(JourneyPicture(RewardIcon(kind), "js-reward-row-icon"));
            if (kind == "flask") { var art = JourneyArt("flask-" + (string)_game.Room["rewards"]?["flaskId"]); if (art != null) button.Add(JourneyPicture(art, "js-reward-row-thumb")); }
            if (kind == "relic") { var art = OriginalCombatLayout.Art("relics/" + (string)_game.Room["rewards"]?["relicId"]); if (art != null) button.Add(JourneyPicture(art, "js-reward-row-thumb")); }
            var chip = OriginalCombatLayout.Label("NEW", "js-chip"); chip.AddToClassList("js-chip-new"); button.Add(chip);
        }
        // The card choice heading row, and the gold NEW pill above each offered card (06b).
        private void CardChoiceHead()
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("js-reward-row"); row.AddToClassList("js-reward-card-head");
            row.Add(JourneyPicture(RewardIcon("card"), "js-reward-icon"));
            var copy = new VisualElement { pickingMode = PickingMode.Ignore }; copy.AddToClassList("js-reward-copy");
            var title = new VisualElement { pickingMode = PickingMode.Ignore }; title.AddToClassList("js-reward-title-line");
            title.Add(OriginalCombatLayout.Label("Card", "js-reward-title")); var chip = OriginalCombatLayout.Label("NEW", "js-chip"); chip.AddToClassList("js-chip-new"); title.Add(chip);
            copy.Add(title);
            copy.Add(OriginalCombatLayout.Label("Choose one of " + ((_game.Room["rewards"]?["cardIds"] as JArray)?.Count ?? 0) + " · tap a face to inspect", "js-reward-body"));
            row.Add(copy); _root.Add(row);
        }
        private static void NewPill(VisualElement grid)
        {
            if (grid.childCount == 0) return;
            var pill = OriginalCombatLayout.Label("NEW", "js-new-pill"); grid[grid.childCount - 1].Insert(0, pill);
        }
        private Label SectionLabel(string text, string name = null)
        {
            var label = Text(text, "js-section-eyebrow"); if (name != null) label.name = name; return label;
        }

        // ---- Merchant (JS 08c, 08d) ---------------------------------------------------------------------
        // Category tabs jump to their section; every offer stays in the tree, so nothing is ever hidden
        // from a pointer, a pad or the browser harnesses.
        private void ShopTabs(params (string Id, string Text)[] tabs)
        {
            JourneyStyles();
            var grid = new VisualElement(); grid.AddToClassList("js-shop-tabs"); _root.Add(grid);
            foreach (var tab in tabs)
            {
                var id = tab.Id;
                var button = Button("native-shop-tab-" + id, tab.Text, () =>
                {
                    foreach (var other in grid.Query<Button>().ToList()) other.EnableInClassList("js-active", other.name == "native-shop-tab-" + id);
                    var target = _root.Q("native-shop-section-" + id);
                    if (target != null) _root.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(target);
                    _report();
                }, grid);
                button.AddToClassList("js-shop-tab"); button.EnableInClassList("js-active", tab.Id == tabs[0].Id);
            }
        }
        // A relic or flask offer row: framed 60 px thumbnail at the left, a green Buy plate at the right.
        private static void ShopOffer(Button button, Texture2D art, bool available, bool sold)
        {
            if (button == null) return;
            button.AddToClassList("js-shop-offer");
            var frame = new VisualElement { pickingMode = PickingMode.Ignore }; frame.AddToClassList("js-shop-thumb");
            if (art != null) frame.Add(JourneyPicture(art, "js-shop-thumb-art"));
            button.Add(frame);
            var buy = OriginalCombatLayout.Label(sold ? "Sold" : "Buy", "js-shop-buy"); buy.EnableInClassList("js-shop-buy-off", !available); button.Add(buy);
        }
        private static Texture2D OfferArt(string kind, string id) =>
            kind == "relics" ? OriginalCombatLayout.Art("relics/" + id) : kind == "flasks" ? JourneyArt("flask-" + id) : null;

        // ---- Rest (JS 08a, 08b): the recovery preview table -------------------------------------------
        // What resting here would leave: HP gains the shrine heal (OriginalRunRules.RestHeal, the same
        // number Rest applies), Mana refills, stamina and the flask charges (refilled on arrival) hold.
        private void RecoveryPreview()
        {
            JourneyStyles();
            var run = _game.RunPlayer; var rules = new OriginalRunRules(_game.Catalog.Data());
            var canRest = rules.CanRest(run); var heal = canRest ? rules.RestHeal(run) : 0;
            var charges = run["flaskCharges"];
            Text("RECOVERY PREVIEW", "js-section-eyebrow");
            var table = new VisualElement { name = "native-recovery-preview", pickingMode = PickingMode.Ignore }; table.AddToClassList("js-recovery");
            void Line(string icon, string label, int before, int after)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("js-recovery-row");
                row.Add(JourneyPicture(JourneyArt(icon), "js-recovery-icon"));
                row.Add(OriginalCombatLayout.Label(label, "js-recovery-label"));
                row.Add(OriginalCombatLayout.Label(before + " →", "js-recovery-before"));
                row.Add(OriginalCombatLayout.Label(after + "", "js-recovery-after"));
                var gain = OriginalCombatLayout.Label(after > before ? "+" + (after - before) : "—", "js-recovery-gain"); row.Add(gain);
                table.Add(row);
            }
            int Value(JToken token) => (int?)token ?? 0;
            Line("rest-hp", "HP", Value(run["hp"]), Value(run["hp"]) + heal);
            Line("rest-mp", "MP", Value(run["mana"]), canRest ? Math.Max(Value(run["mana"]), Value(run["maxMana"])) : Value(run["mana"]));
            Line("rest-sp", "SP", Value(run["stamina"]), Value(run["stamina"]));
            Line("rest-flask-hp", "Healing flasks", Value(charges?["hpCurrent"]), Value(charges?["hpCurrent"]));
            Line("rest-flask-mp", "MP flasks", Value(charges?["manaCurrent"]), Value(charges?["manaCurrent"]));
            _root.Add(table);
        }

        // ---- Event (JS 08e, 08f): the stage above the dialogue box --------------------------------------
        // The player at the left ("You", faded as in the JS stage) and the speaker at the right with their
        // name, standing in the painted band the theme leaves above the dialogue panel.
        private void EventStage(string eventId)
        {
            JourneyStyles();
            var stage = new VisualElement { name = "native-event-stage", pickingMode = PickingMode.Ignore }; stage.AddToClassList("js-event-stage");
            var classId = (string)_game.RunPlayer["classId"];
            var you = new VisualElement { pickingMode = PickingMode.Ignore }; you.AddToClassList("js-event-figure"); you.AddToClassList("js-event-player");
            var self = OriginalCombatLayout.Art("player/" + classId + "-ready");
            if (self != null) you.Add(JourneyPicture(self, "js-event-figure-art"));
            you.Add(OriginalCombatLayout.Label("You", "js-event-name"));
            stage.Add(you);
            var speaker = EventSpeakers.FirstOrDefault(s => s.Event == eventId);
            if (speaker.Event != null)
            {
                var them = new VisualElement { pickingMode = PickingMode.Ignore }; them.AddToClassList("js-event-figure"); them.AddToClassList("js-event-speaker");
                var art = OriginalCombatLayout.Art("enemies/" + speaker.Portrait + "-idle");
                if (art != null) them.Add(JourneyPicture(art, "js-event-figure-art"));
                them.Add(OriginalCombatLayout.Label(speaker.Name, "js-event-name"));
                stage.Add(them);
            }
            // First in paint order: the HUD band draws over the stage, as the JS band over the dialogue scene.
            _root.Insert(0, stage);
        }
    }
}
