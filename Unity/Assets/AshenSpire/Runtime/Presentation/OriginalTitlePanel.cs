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
            Action cooperative, Action settings, Action extras)
        {
            AddToClassList("original-title");
            var wordmark = new Label("ASHEN SPIRE"); wordmark.AddToClassList("original-wordmark"); Add(wordmark);
            RegisterCallback<GeometryChangedEvent>(_ => wordmark.style.fontSize = Mathf.Clamp(contentRect.width * .112f, 28, 62));
            var subtitle = new Label("A ROGUELIKE DECKBUILDER"); subtitle.AddToClassList("original-subtitle"); Add(subtitle);
            Add(Ornament());
            Add(Entry("native-continue", "Continue", resume, canResume));
            Add(Entry("native-new", "New", begin));
            Add(Entry("native-profile", "Collection", collection));
            Add(Entry("native-coop", "Climb together", cooperative));
            Add(Entry("settings", "Settings", settings));
            Add(Entry("title-extras", "Extras", extras));
            Add(Ornament());
            var tagline = new Label("THE EMBER FLOWS UPWARD. FOLLOW IT."); tagline.AddToClassList("original-tagline"); Add(tagline);
        }
        private static Button Entry(string id, string label, Action action, bool enabled = true)
        {
            var button = new Button(action) { name = id, text = label.ToUpperInvariant() };
            button.AddToClassList("button"); button.AddToClassList("original-menu-entry"); button.SetEnabled(enabled); return button;
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
