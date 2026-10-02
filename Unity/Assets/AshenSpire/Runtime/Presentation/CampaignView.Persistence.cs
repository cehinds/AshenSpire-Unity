// Visible storage feedback without rebuilding the current game or stealing focus.
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        public void PersistenceNotice(string message)
        {
            if (_disposed || _body == null) return;
            var existing = _body.Q<Label>("persistence-notice");
            if (existing == null && string.IsNullOrEmpty(message)) return;
            if (string.IsNullOrEmpty(message)) existing?.RemoveFromHierarchy();
            else
            {
                if (existing == null)
                {
                    existing = Text(message, "notice"); existing.name = "persistence-notice";
                    existing.AddToClassList("persistence-notice");
                    _body.Insert(0, existing);
                }
                else existing.text = message;
            }
            Report();
        }
    }
}
