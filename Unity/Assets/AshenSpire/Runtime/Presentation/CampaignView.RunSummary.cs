// CampaignView.RunSummary.cs — end-of-run summary hand-off (F11 / US-11.2).
// RunController sets NativeSummary (from RunSummaryTracker, with earned
// unlocks attached) before each Native(...) render; OriginalRunPanel shows it
// through RunSummaryView when the run is in Victory or Defeat.
using AshenSpire.Domain.Original;

namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        /// <summary>Latched summary of the finished native run, or null while the run is live.</summary>
        public RunSummary NativeSummary { get; set; }
    }
}
