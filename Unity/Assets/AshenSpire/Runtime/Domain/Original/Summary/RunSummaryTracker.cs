// RunSummaryTracker.cs — read-only observer that latches the end-of-run summary.
// Attach to an OriginalGameSession (new or restored). It listens to the existing
// Changed event, which fires only after a command commits, and reads copies of
// the run state. It never issues a command, never touches an RNG stream and
// never writes the save, so a tracked run is byte-identical to an untracked one
// (proved by UnityTests/RunSummary). The first Victory/Defeat it sees becomes
// Summary; attach earned unlocks after OriginalProfile.Finish with AttachEarned.
using System;
using System.Collections.Generic;

namespace AshenSpire.Domain.Original
{
    public sealed class RunSummaryTracker : IDisposable
    {
        private readonly OriginalGameSession _session;
        private bool _attached;
        public RunSummary Summary { get; private set; }
        public bool IsComplete => Summary != null;
        /// <summary>Committed commands observed since attaching.</summary>
        public int ObservedCommands { get; private set; }
        /// <summary>Raised once, when the run first reaches Victory or Defeat.</summary>
        public event Action<RunSummary> Completed;

        public RunSummaryTracker(OriginalGameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _session.Changed += OnChanged; _attached = true;
            if (RunSummary.IsTerminal(_session.Phase)) Summary = RunSummary.FromSession(_session); // restored finished save
        }

        private void OnChanged()
        {
            ObservedCommands++;
            if (Summary != null || !RunSummary.IsTerminal(_session.Phase)) return;
            Summary = RunSummary.FromSession(_session);
            Completed?.Invoke(Summary);
        }

        /// <summary>Add OriginalProfile.Finish(...)["newUnlocks"] to the latched summary.</summary>
        public RunSummary AttachEarned(IEnumerable<string> newUnlockIds)
        {
            if (Summary == null) throw new InvalidOperationException("The run has not ended.");
            Summary = Summary.WithEarned(_session.Catalog, newUnlockIds);
            return Summary;
        }

        public void Dispose() { if (_attached) { _session.Changed -= OnChanged; _attached = false; } }
    }
}
