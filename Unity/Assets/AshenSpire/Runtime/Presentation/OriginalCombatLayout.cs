// OriginalCombatLayout.cs — bounded original-style combat composition for both modes.
// WIRING: call SetSurface after the outer map shell policy, then add Hud, Field,
// Hand and compact action/utility bands. Controls still invoke caller-owned commands.
// MODIFY: geometry in OriginalCombat.uss. No simulation, resource math or game saves.
// LOOK: the HTML/JS build 1815 combat screen (art in Resources/Art/js1815/combat, imported by
// tools/import-js1815-combat-art.mjs): a turn ribbon and hairline HP band on top, a biome
// painting with small side-on figures (a 3x3 tile board in portrait), red serif intents over
// 7px HP bars, and a fanned hand above the bronze footer CombatChrome mounts.
// Scroll memory belongs to the live game/UI object and room; weak keys do not retain
// ended runs. Restoring the hand uses geometry events, never a gameplay callback.
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public static class OriginalCombatLayout
    {
        private sealed class ScrollMemory { internal string Room; internal float Offset; }
        private static readonly ConditionalWeakTable<object, ScrollMemory> HandMemory = new ConditionalWeakTable<object, ScrollMemory>();
        /// <summary>Resources folder of the imported JS 1815 combat art.</summary>
        public const string ArtRoot = "Art/js1815/combat/";
        /// <summary>Presentation-only floor grid preference (the JS "Grid" checkbox). Session memory, never saved.</summary>
        public static bool GridVisible { get; set; } = true;
        // JS biome per content act (act 1 is the first real fight's Hollow Weald, act 2 the
        // showcase Pale Marches, act 3 the Cinder Reach); two paintings each, picked per room.
        private static readonly string[] Regions = { "hollow-weald", "pale-marches", "cinder-reach" };
        private static readonly string[] SceneVariants = { "1", "3" };
        internal static Texture2D Art(string path) => string.IsNullOrEmpty(path) ? null : Resources.Load<Texture2D>(ArtRoot + path);
        public static void SetSurface(VisualElement body, bool active)
        {
            var wasCombat = body.ClassListContains("combat-screen");
            body.EnableInClassList("combat-screen", active);
            // The battlefield painting belongs to the Field; the surface itself stays plain.
            body.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            // The map policy may already have established a bounded viewport.
            // Removing our class must not reset its height/scroll contract.
            if (!active && (!wasCombat || body.ClassListContains("map-screen"))) return;
            var scroll = body.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) return;
            scroll.contentContainer.style.height = active ? new StyleLength(Length.Percent(100)) : new StyleLength(StyleKeyword.Auto);
            body.style.height = active ? new StyleLength(Length.Percent(100)) : new StyleLength(StyleKeyword.Auto);
            body.style.minHeight = 0; body.style.flexShrink = active ? 1 : 0;
            if (active) scroll.scrollOffset = Vector2.zero;
        }
        /// <summary>The shared run HUD band: turn ribbon, heart + HP numbers on a hairline, relic and status icons, cinders.</summary>
        public static VisualElement Hud(JObject player, JObject run, int act, int turn, string className)
        {
            var hud = new VisualElement { pickingMode = PickingMode.Ignore, name = "combat-hud-band" }; hud.AddToClassList("js-combat-band");
            hud.tooltip = className + " · Act " + act + " · Turn " + turn;
            var ribbon = new VisualElement { pickingMode = PickingMode.Ignore }; ribbon.AddToClassList("js-turn-ribbon");
            ribbon.Add(Label("‹", "js-turn-chevron")); ribbon.Add(Diamond());
            ribbon.Add(Label("Player Turn", "js-turn-text"));
            ribbon.Add(Diamond()); ribbon.Add(Label("›", "js-turn-chevron"));
            hud.Add(ribbon);
            var hp = new VisualElement { pickingMode = PickingMode.Ignore }; hp.AddToClassList("js-hud-hp");
            hp.Add(Icon("icons/status-health", "js-hud-heart"));
            hp.Add(Label(player["hp"] + "/" + player["maxHp"], "js-hud-hp-value"));
            var line = Pool("", player["hp"], player["maxHp"], "health"); line.AddToClassList("js-hud-hp-line"); hp.Add(line);
            hud.Add(hp);
            var icons = new VisualElement { pickingMode = PickingMode.Ignore }; icons.AddToClassList("js-hud-icons");
            foreach (var id in (run?["relics"] as JArray ?? new JArray()).Values<string>())
            {
                var art = Art("relics/" + id); if (art == null) continue;
                var relic = new VisualElement { pickingMode = PickingMode.Ignore }; relic.AddToClassList("js-hud-relic");
                relic.Add(new Image { image = art, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore });
                icons.Add(relic);
            }
            // The player's statuses sit under their own bar on the battlefield, as in the JS tray.
            hud.Add(icons);
            var purse = new VisualElement { pickingMode = PickingMode.Ignore }; purse.AddToClassList("js-hud-cinders");
            purse.Add(Label("CINDERS", "js-hud-cinders-label")); purse.Add(Label(run?["cinders"] + "", "js-hud-cinders-value"));
            hud.Add(purse);
            // Co-op has no CombatChrome footer, so its band also reads mana, stamina and actions (hidden in solo).
            hud.Add(Label("MP " + player["mana"] + "/" + player["maxMana"] + " · SP " + player["stamina"] + "/" + player["maxStamina"] + " · " + player["energy"] + ((int?)player["energy"] == 1 ? " action" : " actions"), "js-hud-resources"));
            return hud;
        }
        private static VisualElement Diamond() { var d = new VisualElement { pickingMode = PickingMode.Ignore }; d.AddToClassList("js-turn-diamond"); return d; }
        internal static Image Icon(string path, string className)
        {
            var image = new Image { image = Art(path), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(className); return image;
        }
        public static VisualElement Pool(string name, JToken current, JToken maximum, string kind)
        {
            var box = new VisualElement(); box.AddToClassList("original-pool"); box.AddToClassList(kind);
            var back = new VisualElement(); back.AddToClassList("original-pool-track");
            var fill = new VisualElement(); fill.AddToClassList("original-pool-fill");
            var max = (float?)maximum ?? 0; var value = (float?)current ?? 0;
            fill.style.width = Length.Percent(max > 0 ? Mathf.Clamp01(value / max) * 100 : 0); back.Add(fill); box.Add(back);
            box.Add(Label((string.IsNullOrEmpty(name) ? "" : name + " ") + current + "/" + maximum, "original-pool-label")); return box;
        }
        // One status as the JS tray shows it: an engraved icon (or initials) with its count.
        private static readonly string[] StatusIcons = { "strength", "dexterity", "weak", "vulnerable", "frail", "bleed", "frost", "insanity", "burn", "regen", "madness", "staggered" };
        private static VisualElement StatusChip(string id, JObject instance)
        {
            var chip = new VisualElement { pickingMode = PickingMode.Ignore }; chip.AddToClassList("js-status-chip");
            var icon = StatusIcons.FirstOrDefault(s => id.StartsWith(s, StringComparison.Ordinal));
            if (icon != null) chip.Add(Icon("icons/status-" + icon, "js-status-icon"));
            else chip.Add(Label(OriginalCardText.Humanize(id).Substring(0, Math.Min(2, id.Length)), "js-status-initials"));
            var meter = instance?["meter"] as JObject;
            var value = (int?)(meter?["value"] ?? instance?["stacks"]);
            if (value != null) chip.Add(Label(value + "", "js-status-count"));
            chip.tooltip = OriginalStatusText.Describe(id, instance);
            return chip;
        }
        // The row under a combatant's bar: Guard as a shield chip, then each status. The label
        // keeps the original-fighter-caption class (status explanations bind to it) and its text
        // for readers; the chips draw over it.
        private static Label StatusRow(JToken block, JObject statuses, string text)
        {
            var row = new Label(text) { pickingMode = PickingMode.Ignore }; row.AddToClassList("original-fighter-caption"); row.AddToClassList("js-status-row");
            if (((int?)block ?? 0) > 0)
            {
                var guard = new VisualElement { pickingMode = PickingMode.Ignore }; guard.AddToClassList("js-status-chip"); guard.AddToClassList("js-guard-chip");
                guard.Add(Icon("icons/status-block", "js-status-icon")); guard.Add(Label(block + "", "js-status-count")); row.Add(guard);
            }
            if (statuses != null) foreach (var status in statuses.Properties()) row.Add(StatusChip(status.Name, status.Value as JObject));
            return row;
        }
        public static VisualElement Field(int act, string roomKey = null)
        {
            var field = new VisualElement(); field.AddToClassList("original-combat-field");
            var region = Regions[Math.Max(0, Math.Min(Regions.Length - 1, act - 1))];
            var variant = SceneVariants[string.IsNullOrEmpty(roomKey) ? 0 : (int)(StableHash(roomKey) % (uint)SceneVariants.Length)];
            var scene = new Image { name = "combat-scene", image = Art("scenes/" + region + "-" + variant), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            scene.AddToClassList("js-combat-scene"); field.Add(scene);
            var grid = new FloorGrid { name = "combat-floor-grid" }; field.Add(grid);
            field.RegisterCallback<GeometryChangedEvent>(_ => PlaceActors(field)); return field;
        }
        private static uint StableHash(string text)
        {
            var value = 2166136261u;
            foreach (var c in text) value = (value ^ c) * 16777619u;
            return value;
        }
        /// <summary>Shows or hides every floor grid in the tree (the footer Grid toggle).</summary>
        public static void ShowGrid(VisualElement root, bool visible)
        {
            GridVisible = visible;
            root.Query<FloorGrid>().ForEach(g => g.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None);
        }
        /// <summary>Height of the read above each figure; OriginalCombat.uss stacks the intent, poise trim, bar and statuses in it.</summary>
        public const float OverheadHeight = 70;
        private const float HudBand = 98;
        // The painting's horizon sits at about 46% of its height; this keeps the floor under the figures.
        private const float SceneHorizon = .457f;
        private static void PlaceActors(VisualElement field)
        {
            var width = field.contentRect.width; var height = field.contentRect.height;
            if (width <= 0 || height <= 0) return;
            var mobile = width < 650;
            field.parent?.EnableInClassList("owner-mobile",mobile);
            var scene = field.Q<Image>("combat-scene");
            if (scene?.image != null)
            {
                // Cover the field with the painting's horizon a third down (a quarter in portrait, above the
                // tile board), zooming in when needed so the floor still reaches the bottom, like the JS framing.
                var texture = scene.image; var horizon = mobile ? .25f : .33f;
                var h = Math.Max(Math.Max(height, width * texture.height / texture.width), height * (1 - horizon) / (1 - SceneHorizon));
                var w = h * texture.width / texture.height;
                var top = Math.Min(0, height * horizon - h * SceneHorizon);
                scene.style.left = (width - w) / 2; scene.style.top = top; scene.style.width = w; scene.style.height = h;
            }
            var grid = field.Q<FloorGrid>();
            grid?.Configure(mobile);
            if (grid != null) grid.style.display = GridVisible ? DisplayStyle.Flex : DisplayStyle.None;
            var actors = field.Children().Where(e => e.ClassListContains("original-fighter-slot")).ToArray();
            var enemies = actors.Where(e => e.ClassListContains("original-enemy-target")).ToArray();
            var players = actors.Where(e => e.ClassListContains("original-player-slot")).ToArray();
            var crowd = enemies.Length;
            foreach (var actor in actors)
            {
                var player = actor.ClassListContains("original-player-slot");
                var index = Array.IndexOf(player ? players : enemies, actor);
                var count = player ? players.Length : enemies.Length;
                var hound = actor.ClassListContains("owner-hound"); var boss = actor.ClassListContains("owner-colossus");
                float center, feet, actorHeight;
                if (mobile)
                {
                    // Portrait: the 3x3 tile board. Enemies stand at the back of the top row, the player on the left lower down.
                    var board = FloorGrid.Board(width, height);
                    var cell = board.width / 3; var row = board.height / 3;
                    center = player ? board.x + cell * (count == 1 ? .5f : (index + .5f) * 2f / count)
                        : count == 1 ? board.x + cell * 2.5f : count == 2 ? board.x + cell * (index == 0 ? .5f : 2.5f) : board.x + board.width * (index + .5f) / count;
                    feet = player ? board.y + row * 2.5f : board.y + row * .55f;
                    actorHeight = height * (player ? .12f : boss ? .16f : hound ? .105f : .115f);
                }
                else
                {
                    // Desktop: the player near-left, enemies spread across the right half on a slightly higher floor.
                    center = player ? width * (count == 1 ? .23f : .12f + index * .22f / Math.Max(1, count - 1))
                        : width * (count == 1 ? .68f : .50f + index * .32f / Math.Max(1, count - 1));
                    feet = height * (player ? .52f : .40f);
                    actorHeight = Mathf.Clamp(height * (player ? .20f : boss ? .26f : hound ? .145f : .17f), 70, player ? 190 : 230);
                    if (!player) feet = Math.Max(feet, HudBand + OverheadHeight + actorHeight); // keep intents clear of the HUD band
                }
                var aspect = player ? actor.Q<OriginalPlayerFigure>()?.OwnerAspect ?? .7f : actor.Q<OriginalEnemyFigure>()?.ArtAspect ?? 1f;
                var actorWidth = actorHeight * aspect;
                var spacing = player ? (mobile ? width * .40f : width * .22f) : width * (mobile ? (crowd > 2 ? .9f / crowd : .40f) : crowd > 1 ? .30f / (crowd - 1) * .95f : .26f);
                var maximumWidth = Math.Min(spacing, mobile ? width * .40f : width * .22f);
                if (actorWidth > maximumWidth) { actorHeight *= maximumWidth / actorWidth; actorWidth = maximumWidth; }
                // The slot (hit box, drop target, selection frame) spans the overhead read (intent,
                // bar, statuses: OverheadHeight, wider than the sprite) and the sprite standing under it.
                var overhead = Mathf.Min(mobile ? 110 : 246, Math.Max(actorWidth, spacing * .92f));
                var slotWidth = Math.Max(overhead, actorWidth);
                actor.style.left = center - slotWidth / 2; actor.style.top = feet - actorHeight - OverheadHeight;
                actor.style.width = slotWidth; actor.style.height = actorHeight + OverheadHeight;
                var figure = actor.Children().FirstOrDefault(c => c.ClassListContains("original-combat-figure"));
                if (figure != null)
                {
                    figure.style.left = (slotWidth - actorWidth) / 2; figure.style.top = OverheadHeight;
                    figure.style.width = actorWidth; figure.style.height = actorHeight;
                }
            }
        }
        public static VisualElement Player(Image image, JObject player, Action clicked = null, string controlId = null)
        {
            VisualElement slot = clicked == null ? new VisualElement() : new Button(clicked);
            slot.name = controlId; slot.AddToClassList("original-fighter-slot"); slot.AddToClassList("original-player-slot");
            slot.EnableInClassList("card-self-armed", clicked != null);
            image.AddToClassList("original-combat-figure"); slot.Add(image);
            slot.Add(Pool("", player["hp"], player["maxHp"], "health"));
            slot.Add(StatusRow(player["block"], player["statuses"] as JObject, "Guard " + player["block"])); return slot;
        }
        public static Button Enemy(JObject enemy, string name, string controlId, Action clicked, bool selected, out Image image)
        {
            var button = new Button(clicked) { name = controlId }; button.AddToClassList("original-fighter-slot"); button.AddToClassList("original-enemy-target");
            if ((string)enemy["enemyId"] == "blightHound" || (string)enemy["enemyId"] == "stitchedHound") button.AddToClassList("owner-hound");
            if ((string)enemy["enemyId"] == "charredColossus") button.AddToClassList("owner-colossus");
            if (selected) button.AddToClassList("selected");
            var intent = enemy["intent"];
            var intentText = OriginalCardText.Humanize((string)intent?["kind"] ?? "Preparing");
            if (intent?["damage"] != null && intent["damage"].Type != JTokenType.Null) intentText += " " + intent["damage"] + ((int?)intent["hits"] > 1 ? " × " + intent["hits"] : "");
            if ((bool?)intent?["pending"] == true) intentText += " · committed";
            button.Add(Label(intentText, "original-enemy-intent"));
            image = OriginalEnemyFigure.Create((string)enemy["enemyId"], (string)intent?["kind"] == "attack"); image.AddToClassList("original-combat-figure"); button.Add(image);
            button.Add(Label(name, "original-fighter-name")); button.Add(Pool("", enemy["hp"], enemy["maxHp"], "health"));
            var status = string.Join(" · ", ((JObject)enemy["statuses"]).Properties().Select(s => OriginalStatusText.Describe(s.Name, s.Value as JObject)));
            var detail = "Guard " + enemy["block"] + (status.Length == 0 ? "" : " · " + status);
            button.Add(StatusRow(enemy["block"], enemy["statuses"] as JObject, detail)); button.tooltip = name + ". " + intentText + ". HP " + enemy["hp"] + "/" + enemy["maxHp"] + ". " + detail;
            return button;
        }
        /// <summary>
        /// Marks an enemy target unselectable without disabling it, so its status explanation
        /// (figure and caption) stays reachable. The caller's click action must also be gated.
        /// The control report treats the class as disabled (see Selectable).
        /// </summary>
        public const string TargetUnavailableClass = "target-unavailable";
        public static void SetTargetAvailable(VisualElement target, bool available)
        {
            target.EnableInClassList(TargetUnavailableClass, !available); target.focusable = available;
        }
        public static bool Selectable(VisualElement control) => control.enabledInHierarchy && !control.ClassListContains(TargetUnavailableClass);
        public static ScrollView Hand(object owner, string roomKey, string id, Action changed = null)
        {
            var memory = HandMemory.GetValue(owner, _ => new ScrollMemory());
            if (memory.Room != roomKey) { memory.Room = roomKey; memory.Offset = 0; }
            var saved = memory.Offset; var ready = false;
            var rail = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = id, horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            rail.AddToClassList("original-hand-rail"); rail.contentContainer.AddToClassList("original-hand-cards");
            rail.horizontalScroller.valueChanged += value => { if (ready) memory.Offset = value; changed?.Invoke(); };
            rail.verticalScroller.valueChanged += _ => changed?.Invoke();
            rail.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (ready || rail.contentContainer.layout.width <= 0) return;
                rail.scrollOffset = new Vector2(saved, 0); ready = true;
            });
            OriginalCardView[] orderedCards = null;
            rail.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (orderedCards == null || orderedCards.Length == 0) orderedCards = rail.contentContainer.Children().OfType<OriginalCardView>().ToArray();
                FitHand(rail, orderedCards);
            });
            return rail;
        }
        private static void FitHand(ScrollView rail, OriginalCardView[] cards)
        {
            var count = cards.Length; var width = rail.contentRect.width;
            if (count == 0 || width <= 0) return;
            var mobile = width < 650;
            var cardWidth = mobile ? 148f : Mathf.Clamp((width - 28) / count - 4, 148, 205);
            // Reserve space for the fan offset, rotation and cost badge. A wide
            // viewport can still be short; width alone must not clip card rules.
            var halfFan = (count - 1) / 2f;
            var tilt = Mathf.Min(90, halfFan * (mobile ? 4 : 2)) * Mathf.Deg2Rad;
            var availableHeight = rail.contentRect.height - 24 - halfFan * 3 - 16;
            if (availableHeight > 0)
                cardWidth = Mathf.Min(cardWidth, Mathf.Max(32, availableHeight / (OriginalIllustratedCardFace.HeightPerWidth + Mathf.Sin(tilt) * .5f)));
            var step = count < 2 ? 0 : Mathf.Min(cardWidth + 4, (width - cardWidth - 28) / (count - 1));
            var start = (width - (cardWidth + step * (count - 1))) / 2;
            // JS fan: cards at most 144px (119px portrait) wide, overlapping by about a third, centred.
            cardWidth = Mathf.Min(cardWidth, mobile ? 119 : 144);
            step = count < 2 ? 0 : Mathf.Min(cardWidth * (mobile ? .50f : .64f), (width - cardWidth - 16) / (count - 1));
            start = (width - (cardWidth + step * (count - 1))) / 2;
            rail.contentContainer.style.height = rail.contentRect.height;
            var keys = rail.contentContainer.Children().Where(e => e.ClassListContains("hand-hotkey")).ToList();
            for (var index = 0; index < count; index++)
            {
                var card = cards[index]; var offset = index - (count - 1) / 2f;
                card.style.position = Position.Absolute;
                card.style.width = cardWidth; card.style.minWidth = cardWidth;
                card.style.height = (cardWidth - 10) * 1.5f + 10;
                card.style.minHeight = (cardWidth - 10) * 1.5f + 10;
                card.style.left = start + step * index;
                card.style.top = 24 + Mathf.Abs(offset) * 3;
                card.style.rotate = new Rotate(new Angle(offset * (mobile ? 3 : 2), AngleUnit.Degree));
                if (card.ClassListContains("selected")) { card.style.top = 0; card.style.rotate = new Rotate(new Angle(0)); card.BringToFront(); }
                // The numbered hotkey tab above each card's top edge (wide layouts only, as in the JS hand).
                if (index >= keys.Count && index < 9) { var tab = Label((index + 1) + "", "hand-hotkey"); rail.contentContainer.Add(tab); keys.Add(tab); }
                if (index < keys.Count)
                {
                    keys[index].style.left = start + step * index + cardWidth / 2 - 9;
                    keys[index].style.top = card.ClassListContains("selected") ? -16 : 8 + Mathf.Abs(offset) * 3;
                }
            }
        }
        public static ScrollView Utilities(string id, Action changed = null)
        {
            var tray = new ScrollView(ScrollViewMode.Horizontal) { name = id, horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            tray.AddToClassList("original-utility-rail"); tray.contentContainer.AddToClassList("original-utility-items");
            tray.horizontalScroller.valueChanged += _ => changed?.Invoke(); return tray;
        }
        public static VisualElement Actions() { var row = new VisualElement(); row.AddToClassList("original-combat-actions"); return row; }
        public static Label Label(string text, string className) { var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(className); return label; }
    }
    /// <summary>
    /// The faint floor grid (desktop: a perspective lattice on the painted floor) or the
    /// portrait 3x3 tile board the figures stand on. Decoration only: never picks input.
    /// </summary>
    public sealed class FloorGrid : VisualElement
    {
        private bool _mobile;
        public FloorGrid()
        {
            pickingMode = PickingMode.Ignore; AddToClassList("js-floor-grid");
            style.position = Position.Absolute; style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            generateVisualContent += Draw;
        }
        public void Configure(bool mobile) { if (_mobile == mobile) { MarkDirtyRepaint(); return; } _mobile = mobile; EnableInClassList("board", mobile); MarkDirtyRepaint(); }
        /// <summary>The portrait board rectangle for a field of this size (x, y, width, height).</summary>
        public static Rect Board(float width, float height) => new Rect(width * .04f, height * .27f, width * .92f, height * .32f);
        private void Draw(MeshGenerationContext context)
        {
            var w = contentRect.width; var h = contentRect.height; if (w <= 0 || h <= 0) return;
            var painter = context.painter2D;
            if (_mobile)
            {
                // A slight perspective: the back edge is narrower than the front.
                var board = Board(w, h); var inset = board.width * .06f;
                Vector2 At(float u, float v) => new Vector2(Mathf.Lerp(board.x + inset * (1 - v), board.xMax - inset * (1 - v), u), board.y + board.height * v);
                painter.lineWidth = 1.2f; painter.strokeColor = new Color(.86f, .82f, .72f, .38f); painter.fillColor = new Color(.05f, .04f, .03f, .18f);
                painter.BeginPath(); painter.MoveTo(At(0, 0)); painter.LineTo(At(1, 0)); painter.LineTo(At(1, 1)); painter.LineTo(At(0, 1)); painter.ClosePath(); painter.Fill(); painter.Stroke();
                for (var i = 1; i < 3; i++)
                {
                    painter.BeginPath(); painter.MoveTo(At(i / 3f, 0)); painter.LineTo(At(i / 3f, 1)); painter.Stroke();
                    painter.BeginPath(); painter.MoveTo(At(0, i / 3f)); painter.LineTo(At(1, i / 3f)); painter.Stroke();
                }
                return;
            }
            // Desktop: a faint lattice from the far floor (about 36% down) to the near floor (66%).
            var far = h * .36f; var near = h * .66f;
            Vector2 P(float u, float v) => new Vector2(Mathf.Lerp(Mathf.Lerp(w * .12f, -w * .04f, v), Mathf.Lerp(w * .88f, w * 1.04f, v), u), Mathf.Lerp(far, near, v));
            painter.lineWidth = 1f; painter.strokeColor = new Color(.92f, .88f, .78f, .13f);
            for (var i = 0; i <= 6; i++) { painter.BeginPath(); painter.MoveTo(P(i / 6f, 0)); painter.LineTo(P(i / 6f, 1)); painter.Stroke(); }
            foreach (var v in new[] { 0f, .3f, .62f, 1f }) { painter.BeginPath(); painter.MoveTo(P(0, v)); painter.LineTo(P(1, v)); painter.Stroke(); }
        }
    }
}
