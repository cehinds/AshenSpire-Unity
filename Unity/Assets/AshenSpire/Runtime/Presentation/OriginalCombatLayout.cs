// OriginalCombatLayout.cs — bounded original-style combat composition for both modes.
// WIRING: call SetSurface after the outer map shell policy, then add Hud, Field,
// Hand and compact action/utility bands. Controls still invoke caller-owned commands.
// MODIFY: geometry in OriginalCombat.uss. No simulation, resource math or game saves.
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
        public static void SetSurface(VisualElement body, bool active)
        {
            var wasCombat = body.ClassListContains("combat-screen");
            body.EnableInClassList("combat-screen", active);
            body.style.backgroundImage = active ? new StyleBackground(Resources.Load<Texture2D>("Art/OwnerAppearance/courtyard")) : new StyleBackground(StyleKeyword.None);
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
        public static VisualElement Hud(JObject player, JObject run, int act, int turn, string className)
        {
            var hud = new VisualElement(); hud.AddToClassList("original-combat-hud");
            var identity = new VisualElement(); identity.AddToClassList("original-hud-identity");
            identity.Add(Label(className, "original-hud-title"));
            identity.Add(Label("ACT " + act + " · TURN " + turn + " · " + run["cinders"] + " cinders", "original-hud-meta"));
            hud.Add(identity);
            var bars = new VisualElement(); bars.AddToClassList("original-hud-pools");
            bars.Add(Pool("HP", player["hp"], player["maxHp"], "health"));
            bars.Add(Pool("MP", player["mana"], player["maxMana"], "mana"));
            bars.Add(Pool("SP", player["stamina"], player["maxStamina"], "stamina")); hud.Add(bars);
            hud.Add(Label("Block " + player["block"] + "  ·  " + run["cinders"] + " cinders", "combat-resources"));
            var statuses = player["statuses"] as JObject;
            if (statuses != null && statuses.Count > 0) hud.Add(Label(string.Join(" · ", statuses.Properties().Select(s => OriginalStatusText.Describe(s.Name, s.Value as JObject))), "original-combat-party"));
            return hud;
        }
        public static VisualElement Pool(string name, JToken current, JToken maximum, string kind)
        {
            var box = new VisualElement(); box.AddToClassList("original-pool"); box.AddToClassList(kind);
            var back = new VisualElement(); back.AddToClassList("original-pool-track");
            var fill = new VisualElement(); fill.AddToClassList("original-pool-fill");
            var max = (float?)maximum ?? 0; var value = (float?)current ?? 0;
            fill.style.width = Length.Percent(max > 0 ? Mathf.Clamp01(value / max) * 100 : 0); back.Add(fill); box.Add(back);
            box.Add(Label(name + " " + current + "/" + maximum, "original-pool-label")); return box;
        }
        public static VisualElement Field(int act)
        {
            var field = new VisualElement(); field.AddToClassList("original-combat-field");
            field.RegisterCallback<GeometryChangedEvent>(_ => PlaceActors(field)); return field;
        }
        private static void PlaceActors(VisualElement field)
        {
            var width = field.contentRect.width; var height = field.contentRect.height;
            if (width <= 0 || height <= 0) return;
            var mobile = width < 650;
            field.parent?.EnableInClassList("owner-mobile",mobile);
            var floor = height * (mobile ? .60f : .69f);
            var actors = field.Children().Where(e => e.ClassListContains("original-fighter-slot")).ToArray();
            var enemies = actors.Where(e => e.ClassListContains("original-enemy-target")).ToArray();
            var players = actors.Where(e => e.ClassListContains("original-player-slot")).ToArray();
            foreach (var actor in actors)
            {
                var player = actor.ClassListContains("original-player-slot");
                var index = Array.IndexOf(enemies, actor);
                var center = player ? width * (players.Length == 1 ? mobile ? .19f : .23f : .11f + Array.IndexOf(players,actor) * .26f / Math.Max(1,players.Length - 1))
                    : width * (enemies.Length == 1 ? .72f : .59f + index * .31f / Math.Max(1,enemies.Length - 1));
                var hound = actor.ClassListContains("owner-hound"); var boss = actor.ClassListContains("owner-colossus");
                var actorHeight = height * (player ? mobile ? .34f : .46f : hound ? .16f : boss ? .48f : mobile ? .23f : .32f);
                var aspect = player ? actor.Q<OriginalPlayerFigure>()?.OwnerAspect ?? .7f : actor.Q<OriginalEnemyFigure>()?.ArtAspect ?? 1f;
                var actorWidth = actorHeight * aspect;
                var maximumWidth = width * (player ? players.Length > 1 ? .42f / players.Length : mobile ? .5f : .32f : mobile ? .48f : enemies.Length > 3 ? .15f : .28f);
                if (actorWidth > maximumWidth) { actorHeight *= maximumWidth / actorWidth; actorWidth = maximumWidth; }
                var actorFloor = player ? floor : height * (mobile ? .57f : .60f);
                actor.style.left = center - actorWidth / 2; actor.style.top = actorFloor - actorHeight;
                actor.style.width = actorWidth; actor.style.height = actorHeight;
            }
        }
        public static VisualElement Player(Image image, JObject player, Action clicked = null, string controlId = null)
        {
            VisualElement slot = clicked == null ? new VisualElement() : new Button(clicked);
            slot.name = controlId; slot.AddToClassList("original-fighter-slot"); slot.AddToClassList("original-player-slot");
            slot.EnableInClassList("card-self-armed", clicked != null);
            image.AddToClassList("original-combat-figure"); slot.Add(image);
            slot.Add(Pool("", player["hp"], player["maxHp"], "health"));
            slot.Add(Label("Guard " + player["block"], "original-fighter-caption")); return slot;
        }
        public static Button Enemy(JObject enemy, string name, string controlId, Action clicked, bool selected, out Image image)
        {
            var button = new Button(clicked) { name = controlId }; button.AddToClassList("original-fighter-slot"); button.AddToClassList("original-enemy-target");
            if ((string)enemy["enemyId"] == "blightHound") button.AddToClassList("owner-hound");
            if ((string)enemy["enemyId"] == "charredColossus") button.AddToClassList("owner-colossus");
            if (selected) button.AddToClassList("selected");
            var intent = enemy["intent"];
            var intentText = OriginalCardText.Humanize((string)intent?["kind"] ?? "Preparing");
            if (intent?["damage"] != null && intent["damage"].Type != JTokenType.Null) intentText += " " + intent["damage"] + ((int?)intent["hits"] > 1 ? " × " + intent["hits"] : "");
            if ((bool?)intent?["pending"] == true) intentText += " · committed";
            button.Add(Label(intentText, "original-enemy-intent"));
            image = OriginalEnemyFigure.Create((string)enemy["enemyId"]); image.AddToClassList("original-combat-figure"); button.Add(image);
            button.Add(Label(name, "original-fighter-name")); button.Add(Pool("", enemy["hp"], enemy["maxHp"], "health"));
            var status = string.Join(" · ", ((JObject)enemy["statuses"]).Properties().Select(s => OriginalStatusText.Describe(s.Name, s.Value as JObject)));
            var detail = "Guard " + enemy["block"] + (status.Length == 0 ? "" : " · " + status);
            button.Add(Label(detail, "original-fighter-caption")); button.tooltip = name + ". " + intentText + ". HP " + enemy["hp"] + "/" + enemy["maxHp"] + ". " + detail;
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
            var step = count < 2 ? 0 : Mathf.Min(cardWidth + 4, (width - cardWidth - 28) / (count - 1));
            var start = (width - (cardWidth + step * (count - 1))) / 2;
            rail.contentContainer.style.height = 310;
            for (var index = 0; index < count; index++)
            {
                var card = cards[index]; var offset = index - (count - 1) / 2f;
                card.style.position = Position.Absolute;
                card.style.width = cardWidth; card.style.minWidth = cardWidth;
                card.style.height = (cardWidth - 10) * 1.5f + 10;
                card.style.minHeight = (cardWidth - 10) * 1.5f + 10;
                card.style.left = start + step * index;
                card.style.top = 24 + Mathf.Abs(offset) * 3;
                card.style.rotate = new Rotate(new Angle(offset * (mobile ? 4 : 2), AngleUnit.Degree));
                if (card.ClassListContains("selected")) { card.style.top = 0; card.style.rotate = new Rotate(new Angle(0)); card.BringToFront(); }
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
}
