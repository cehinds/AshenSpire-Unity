// RunController.Summary.cs — end-of-run summary wiring (F11 / US-11.2).
// BindOriginal creates the RunSummaryTracker BEFORE subscribing RefreshOriginal,
// so the tracker latches the summary first when the run reaches Victory/Defeat.
// RefreshOriginal attaches OriginalProfile.Finish(...)["newUnlocks"] (only the
// first, non-duplicate Finish returns any) and hands the summary to the view.
// The tracker is a read-only observer: no commands, RNG draws or save writes.
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private RunSummaryTracker _summaryTracker;

        private void TrackSummary(OriginalGameSession game)
        {
            _summaryTracker?.Dispose();
            _summaryTracker = game == null ? null : new RunSummaryTracker(game);
        }

        private void AttachSummaryUnlocks(JObject finish)
        {
            if (_summaryTracker?.IsComplete != true || !(finish?["newUnlocks"] is JArray fresh) || fresh.Count == 0) return;
            _summaryTracker.AttachEarned(fresh.Values<string>().ToArray());
        }

        private RunSummary CurrentSummary => _summaryTracker?.Summary;
    }
}
