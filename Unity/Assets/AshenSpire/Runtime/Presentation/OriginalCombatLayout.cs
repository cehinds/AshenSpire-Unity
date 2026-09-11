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
            var statuses = player["statuses"] as JObject;
            if (statuses != null && statuses.Count > 0) hud.Add(Label(string.Join(" · ", statuses.Properties().Select(s => OriginalCardText.Humanize(s.Name) + " " + (s.Value["stacks"] ?? s.Value["meter"]?["value"]))), "original-combat-party"));
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
            field.style.backgroundImage = new StyleBackground(Resources.Load<Texture2D>("Art/background" + act)); return field;
        }
        public static VisualElement Player(Image image, JObject player)
        {
            var slot = new VisualElement(); slot.AddToClassList("original-fighter-slot"); slot.AddToClassList("original-player-slot");
            image.AddToClassList("original-combat-figure"); slot.Add(image);
            slot.Add(Pool("", player["hp"], player["maxHp"], "health"));
            slot.Add(Label("Guard " + player["block"], "original-fighter-caption")); return slot;
        }
        public static Button Enemy(JObject enemy, string name, string controlId, Action clicked, bool selected, out Image image)
        {
            var button = new Button(clicked) { name = controlId }; button.AddToClassList("original-fighter-slot"); button.AddToClassList("original-enemy-target");
            if (selected) button.AddToClassList("selected");
            var intent = enemy["intent"];
            var intentText = OriginalCardText.Humanize((string)intent?["kind"] ?? "Preparing");
            if (intent?["damage"] != null && intent["damage"].Type != JTokenType.Null) intentText += " " + intent["damage"] + ((int?)intent["hits"] > 1 ? " × " + intent["hits"] : "");
            if ((bool?)intent?["pending"] == true) intentText += " · committed";
            button.Add(Label(intentText, "original-enemy-intent"));
            image = OriginalEnemyFigure.Create((string)enemy["enemyId"]); image.AddToClassList("original-combat-figure"); button.Add(image);
            button.Add(Label(name, "original-fighter-name")); button.Add(Pool("", enemy["hp"], enemy["maxHp"], "health"));
            var status = string.Join(" · ", ((JObject)enemy["statuses"]).Properties().Select(s => OriginalCardText.Humanize(s.Name) + " " + (s.Value["stacks"] ?? s.Value["meter"]?["value"])));
            var detail = "Guard " + enemy["block"] + (status.Length == 0 ? "" : " · " + status);
            button.Add(Label(detail, "original-fighter-caption")); button.tooltip = name + ". " + intentText + ". HP " + enemy["hp"] + "/" + enemy["maxHp"] + ". " + detail;
            return button;
        }
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
            return rail;
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
