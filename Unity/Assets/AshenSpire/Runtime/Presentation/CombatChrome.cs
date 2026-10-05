using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public static class CombatChrome
    {
        public static void Mount(VisualElement root, VisualElement tools, JObject player, int turn, System.Action changed, bool turnStamina = false)
        {
            root.AddToClassList("combat-reframed");
            var energy = OriginalCombatLayout.Label(player["energy"] + "", "combat-energy-value");
            var orb = new VisualElement(); orb.AddToClassList("combat-energy");
            orb.Add(energy); orb.Add(OriginalCombatLayout.Label(turnStamina ? "STAMINA" : "ACTIONS", "combat-energy-caption")); root.Add(orb);
            var heading = new VisualElement(); heading.AddToClassList("combat-encounter");
            heading.Add(OriginalCombatLayout.Label("ASHENEDSPIRE · ENCOUNTER", "combat-eyebrow"));
            heading.Add(OriginalCombatLayout.Label("The Ashen Crossing", "combat-location")); root.Add(heading);
            var turnLabel = OriginalCombatLayout.Label("TURN " + turn + "\nYour turn", "combat-turn"); root.Add(turnLabel);
            Move("native-pile-draw", "combat-draw", "Draw");
            Move("native-pile-discard", "combat-discard", "Discard");
            Move("native-deck", "combat-deck", "Deck");
            tools.AddToClassList("combat-tools-popup");
            if (tools is ScrollView scroll)
            {
                scroll.mode = ScrollViewMode.Vertical;
                scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                // Diagnostics must observe geometry after this nested popup
                // scrolls, otherwise real-input QA targets its old coordinates.
                scroll.verticalScroller.valueChanged += _ => scroll.schedule.Execute(() => changed?.Invoke()).StartingIn(1);
            }
            tools.style.display = DisplayStyle.None;
            var open = false;
            var toggle = new Button(() =>
            {
                open = !open; tools.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open) tools.BringToFront(); changed?.Invoke();
            }) { name = "native-combat-menu", text = "Menu" };
            toggle.AddToClassList("button"); toggle.AddToClassList("combat-menu"); root.Add(toggle);
            void Move(string id, string className, string label)
            {
                var button = root.Q<Button>(id); if (button == null) return;
                var old = button.text; var separator = old.LastIndexOf('·');
                button.text = label + (separator < 0 ? "" : "  " + old.Substring(separator + 1).Trim());
                button.AddToClassList(className); root.Add(button);
                if (className == "combat-draw" || className == "combat-discard")
                {
                    var art = new Image { image = Resources.Load<Texture2D>("Art/CombatRefresh/ember-card-back"), pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
                    art.AddToClassList("combat-pile-art"); button.Add(art);
                }
            }
        }
    }
}
