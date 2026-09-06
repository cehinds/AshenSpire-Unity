// InterruptionState.cs — UI return eligibility, independent of Unity and campaign rules.
// Add lifecycle sources here only when they represent a real interruption. Clearing
// a source never resumes input: the player must explicitly return after all clear.
using System;

namespace AshenSpire.Application
{
    [Flags]
    public enum InterruptionSource { NativePause = 1, BrowserHidden = 2, DesktopFocus = 4 }

    public sealed class InterruptionState
    {
        private InterruptionSource _sources;
        public bool IsInterrupted { get; private set; }
        public bool CanReturn => IsInterrupted && _sources == 0;

        public bool Set(InterruptionSource source, bool active)
        {
            var before = _sources;
            if (active) _sources |= source;
            else _sources &= ~source;
            if (active) IsInterrupted = true;
            return before != _sources;
        }

        public bool TryReturn()
        {
            if (!CanReturn) return false;
            IsInterrupted = false;
            return true;
        }
    }
}
