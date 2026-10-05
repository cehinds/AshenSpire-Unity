// OriginalTitlePanel.cs — the reference game's wordmark, ornaments and menu rhythm.
// WIRING: CampaignView supplies existing callbacks; this view owns no game state.
// MODIFY: OriginalTheme.uss controls spacing/colour; Fonts/Cinzel-Regular supplies
// the reference CSS display family. Keep functional routes in CampaignView.
using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public sealed class OriginalTitlePanel : VisualElement
    {
        public OriginalTitlePanel(Action begin, Action resume, bool canResume, Action collection,
            Action cooperative, Action settings, Action extras, Action saves = null, Action quickStart = null, Action history = null, bool unsavedProgress = false)
        {
            AddToClassList("original-title");
            var wordmark = new Label("ASHENEDSPIRE"); wordmark.AddToClassList("original-wordmark"); Add(wordmark);
            RegisterCallback<GeometryChangedEvent>(_ => wordmark.style.fontSize = Mathf.Clamp(contentRect.width * .09f, 22, 58));
            var subtitle = new Label("A ROGUELIKE DECKBUILDER"); subtitle.AddToClassList("original-subtitle"); Add(subtitle);
            Add(Ornament());
            var menu = new VisualElement(); menu.AddToClassList("title-menu-grid"); Add(menu);
            var resumeButton = canResume && unsavedProgress ? Held("native-continue", "Continue the saved climb", resume) : Entry("native-continue", "Continue", resume, canResume); menu.Add(resumeButton);
            if (canResume) resumeButton.AddToClassList("title-primary");
            if (quickStart != null) { var quick = Entry("native-quick-start", "Quick start", quickStart); quick.AddToClassList("title-quick"); quick.tooltip = "Begin with the recommended character and a fresh seed."; menu.Add(quick); }
            if (saves != null) menu.Add(Entry("native-slots", "Load", saves));
            var start = Entry("native-new", "Begin the climb", begin); start.AddToClassList("title-new"); menu.Add(start);
            if (history != null) menu.Add(Entry("native-history", "Run history", history));
            menu.Add(Entry("native-profile", "Compendium", collection));
            menu.Add(Entry("settings", "Settings", settings));
            menu.Add(Entry("native-coop", "Forsaken together", cooperative));
            Add(Ornament());
            var more = Entry("title-extras", "More", extras); more.AddToClassList("title-more"); Add(more);
            var tagline = new Label("THE EMBER FLOWS UPWARD. FOLLOW IT."); tagline.AddToClassList("original-tagline"); Add(tagline);
        }
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
        private static VisualElement Ornament()
        {
            var row = new VisualElement(); row.AddToClassList("original-ornament");
            var left = new VisualElement(); left.AddToClassList("original-rule"); row.Add(left);
            var diamond = new VisualElement(); diamond.AddToClassList("original-gem"); row.Add(diamond);
            var right = new VisualElement(); right.AddToClassList("original-rule"); row.Add(right); return row;
        }
    }
}
