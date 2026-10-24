// CombatChrome.cs — the solo combat's bronze footer and top-right controls, after the
// HTML/JS build 1815 screen (styles/combat-visuals.css, src/ui/components/footerArt.js):
// left, the Grid toggle, the stamina orb (green gem, blue mana pips) and the draw pile;
// centre, the End Turn plaque; right, the discard/exhaust stack and the potion flasks;
// top right, 44px Deck and Menu icon buttons. Art lives in Resources/Art/js1815/combat.
// WIRING: OriginalRunPanel.Render calls Mount after the combat tree is built. It moves
// the existing named controls (native-pile-*, native-deck) instead of re-creating them,
// so every command, id, keyboard and gamepad route stays the same. Presentation only.
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public static class CombatChrome
    {
        public static void Mount(VisualElement root, VisualElement tools, JObject player, int turn, System.Action changed)
        {
            root.AddToClassList("combat-reframed");
            // The footer band sits under the hand, so the fanned cards overlap its top edge.
            var band = new VisualElement { name = "combat-footer", pickingMode = PickingMode.Ignore }; band.AddToClassList("js-combat-footer");
            var fade = new VisualElement { pickingMode = PickingMode.Ignore }; fade.AddToClassList("js-footer-fade"); band.Add(fade);
            var fadeSoft = new VisualElement { pickingMode = PickingMode.Ignore }; fadeSoft.AddToClassList("js-footer-fade-soft"); band.Add(fadeSoft);
            foreach (var side in new[] { "left", "right" })
            {
                var rail = OriginalCombatLayout.Icon("footer/connector", "js-footer-rail"); rail.AddToClassList("js-footer-rail-" + side); band.Add(rail);
            }
            var hand = root.Q(className: "original-hand-rail");
            if (hand != null && hand.parent == root) root.Insert(root.IndexOf(hand), band); else root.Add(band);

            // Stamina orb: frame, glowing gem, the stamina number; mana pips arc over it.
            var orb = new VisualElement { name = "combat-stamina-orb", pickingMode = PickingMode.Position }; orb.AddToClassList("js-stamina-orb");
            orb.tooltip = "Stamina " + player["stamina"] + "/" + player["maxStamina"] + " · Mana " + player["mana"] + "/" + player["maxMana"] + " · Actions " + player["energy"];
            orb.Add(OriginalCombatLayout.Icon("footer/orb-frame", "js-orb-frame"));
            orb.Add(OriginalCombatLayout.Icon("footer/orb-orb", "js-orb-gem"));
            orb.Add(OriginalCombatLayout.Label(player["stamina"] + "", "js-orb-value"));
            orb.Add(OriginalCombatLayout.Label("SP", "js-orb-caption"));
            var fullMana = (int?)player["maxMana"] ?? 0; var maxMana = Mathf.Clamp(fullMana, 0, 7); var mana = (int?)player["mana"] ?? 0;
            // The arc holds seven decorative pips. A larger pool lights them in proportion and prints the real
            // current/maximum beside the orb, since solo combat shows no other mana number (tooltips do not render).
            var litPips = fullMana > maxMana ? Mathf.CeilToInt(mana * maxMana / (float)fullMana) : mana;
            if (fullMana > maxMana) orb.Add(OriginalCombatLayout.Label("MP " + mana + "/" + fullMana, "js-orb-mana"));
            for (var pip = 0; pip < maxMana; pip++)
            {
                var gem = OriginalCombatLayout.Icon(pip < litPips ? "footer/orb-diamond" : "footer/orb-spent", "js-orb-pip");
                // Centred over the top of the ring and fanning around it (radius ~2.8 pip widths, ~2 pip heights).
                var angle = (pip - (maxMana - 1) / 2f) * 24f;
                gem.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
                gem.style.translate = new Translate(Length.Percent(Mathf.Sin(angle * Mathf.Deg2Rad) * 280), Length.Percent((1 - Mathf.Cos(angle * Mathf.Deg2Rad)) * 200));
                orb.Add(gem);
            }
            root.Add(orb);
            root.Add(OriginalCombatLayout.Label(player["energy"] + ((int?)player["energy"] == 1 ? " action" : " actions"), "js-actions-caption"));

            // The JS "Grid" checkbox: shows or hides the floor grid / tile board. Presentation only.
            var grid = new Toggle("Grid") { name = "native-grid-toggle", value = OriginalCombatLayout.GridVisible };
            grid.AddToClassList("js-grid-toggle");
            grid.RegisterValueChangedCallback(e => { OriginalCombatLayout.ShowGrid(root, e.newValue); changed?.Invoke(); });
            root.Add(grid);

            var exhausted = Count(root.Q<Button>("native-pile-exhaust"));
            Move("native-pile-draw", "combat-draw", count => count + "\nDRAW", "footer/draw");
            Move("native-pile-discard", "combat-discard", count => "Discard " + count + "\nExhaust " + exhausted, "footer/spent-cards");
            Move("native-deck", "combat-deck", null, "footer/button-armoury");

            tools.AddToClassList("combat-tools-popup");
            if (tools is ScrollView scroll)
            {
                scroll.mode = ScrollViewMode.Vertical;
                scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            tools.style.display = DisplayStyle.None;
            var open = false;
            void Toggle()
            {
                open = !open; tools.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open) tools.BringToFront(); changed?.Invoke();
            }
            // The flask cluster opens the same tray, where Crimson, Azure and utility flasks live.
            var potions = new Button(Toggle) { name = "native-potions", text = "Potions", tooltip = "Potions: Crimson, Azure and utility flasks" };
            potions.AddToClassList("button"); potions.AddToClassList("combat-potions");
            potions.Add(Art("footer/potions"));
            root.Add(potions);
            var menu = new Button(Toggle) { name = "native-combat-menu", text = "Menu", tooltip = "Combat menu" };
            menu.AddToClassList("button"); menu.AddToClassList("combat-menu"); menu.Add(Art("footer/button-menu"));
            root.Add(menu);
            // Command refusals (the panel's notice) read over the battlefield, not under it.
            root.Q(className: "notice")?.BringToFront();

            void Move(string id, string className, System.Func<int, string> label, string art)
            {
                var button = root.Q<Button>(id); if (button == null) return;
                if (label != null) button.text = label(Count(button));
                button.AddToClassList(className); root.Add(button);
                button.Add(Art(art));
            }
        }
        // "Draw pile · 5" → 5. The utility buttons carry their pile's count after the separator.
        private static int Count(Button button)
        {
            var text = button?.text ?? ""; var separator = text.LastIndexOf('·');
            return separator >= 0 && int.TryParse(text.Substring(separator + 1).Trim(), out var value) ? value : 0;
        }
        private static Image Art(string path)
        {
            var image = OriginalCombatLayout.Icon(path, "combat-pile-art"); return image;
        }
    }
}
