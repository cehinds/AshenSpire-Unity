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
            Action cooperative, Action settings, Action extras, Action saves = null, bool unsavedProgress = false)
        {
            AddToClassList("original-title");
            var wordmark = new Label("ASHENEDSPIRE"); wordmark.AddToClassList("original-wordmark"); Add(wordmark);
            RegisterCallback<GeometryChangedEvent>(_ => wordmark.style.fontSize = Mathf.Clamp(contentRect.width * .09f, 22, 58));
            var subtitle = new Label("A ROGUELIKE DECKBUILDER"); subtitle.AddToClassList("original-subtitle"); Add(subtitle);
            Add(Ornament());
            var invitation = new Label(canResume ? "Your climb is waiting." : "Gather your cards. Brave the Spire.");
            invitation.AddToClassList("original-title-invitation"); Add(invitation);
            // US-13.3 loadSlot: when the climb in memory could not be saved, Continue would replace it with
            // the older saved checkpoint, so it needs a hold or a second tap (HoldConfirmButton).
            var primary = canResume ? (unsavedProgress ? Held("native-continue", "Continue the saved climb", resume) : Entry("native-continue", "Continue the climb", resume)) : Entry("native-new", "Begin a new climb", begin);
            primary.AddToClassList("title-primary"); Add(primary);
            var menu = new VisualElement(); menu.AddToClassList("title-menu-grid"); Add(menu);
            menu.Add(canResume ? Entry("native-new", "New climb", begin) : Entry("native-continue", "Continue", resume, false));
            if (saves != null) menu.Add(Entry("native-slots", "Saved climbs", saves));
            menu.Add(Entry("native-profile", "Collection", collection));
            menu.Add(Entry("native-coop", "Climb together", cooperative));
            menu.Add(Entry("settings", "Settings", settings));
            menu.Add(Entry("title-extras", "Extras", extras));
            Add(Ornament());
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
