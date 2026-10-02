// HoldConfirmState.cs — the second beat for a DESTRUCTIVE action (US-13.3), as pure state.
// Time is injected (milliseconds from any monotonic clock); nothing here reads a clock,
// schedules work or knows about input devices. Presentation/HoldConfirmButton drives it.
//
// TWO PATHS, ONE COMMIT (HTML: src/ui/components/holdconfirm.js):
//   HOLD — Begin(now) starts the fill; Tick/Release at or after HoldMs commits. Releasing
//          (or Cancel, for a drag or pointer leave) before the threshold aborts and never
//          commits.
//   TAP TWICE — Tap(now) arms; a second Tap within ArmWindowMs commits. This is the
//          keyboard/gamepad and assistive path (Enter/Space submit twice) and keeps quick
//          automated clicks deterministic. An armed state expires on its own.
// Either path commits exactly once; the trailing click after a completed hold finds the
// state Fired and does nothing. Reset() re-arms the control for another use.
//
// DURATION: the original dial `balance.ui.holdConfirm` (src/content/balance.js) is
// off 0 / short 350 / normal 600 / long 1000 ms, default normal. The same values are
// FeelInput.HoldConfirm*Ms in Resources/Feel/feel-profile.json (checked by UnityTests/Feel).
// HoldMs <= 0 means the hold path is off; tap-twice still confirms.
// Engine-independent: C# 9 / netstandard2.1, no UnityEngine.
using System;

namespace AshenSpire.Domain.Original
{
    public sealed class HoldConfirmState
    {
        /// <summary>The original game's default ("normal") hold, in milliseconds.</summary>
        public const int DefaultHoldMs = 600;
        /// <summary>How long a first tap stays armed waiting for the confirming second tap.</summary>
        public const int DefaultArmWindowMs = 4000;

        private double _start, _armedAt;
        private bool _armed;

        public HoldConfirmState(double holdMs = DefaultHoldMs, double armWindowMs = DefaultArmWindowMs)
        {
            if (double.IsNaN(holdMs) || double.IsNaN(armWindowMs) || armWindowMs <= 0) throw new ArgumentOutOfRangeException(nameof(armWindowMs));
            HoldMs = Math.Max(0, holdMs); ArmWindowMs = armWindowMs;
        }

        public double HoldMs { get; }
        public double ArmWindowMs { get; }
        public bool HoldEnabled => HoldMs > 0;
        public bool Holding { get; private set; }
        public bool Fired { get; private set; }
        /// <summary>Total commits since construction; Reset does not clear it (diagnostics/tests).</summary>
        public int Commits { get; private set; }

        public bool IsArmed(double now) => _armed && !Fired && now - _armedAt <= ArmWindowMs;

        /// <summary>Press began. Ignored once fired or when the hold path is off.</summary>
        public void Begin(double now)
        {
            if (Fired || !HoldEnabled) return;
            Holding = true; _start = now;
        }

        /// <summary>Fill fraction 0..1 for the progress bar; 1 after a commit.</summary>
        public double Progress(double now)
        {
            if (Fired) return 1;
            if (!Holding) return 0;
            return Math.Max(0, Math.Min(1, (now - _start) / HoldMs));
        }

        /// <summary>Advance time. Returns true exactly once, at the moment the hold completes.</summary>
        public bool Tick(double now)
        {
            if (!Holding || Fired || now - _start < HoldMs) return false;
            return Fire();
        }

        /// <summary>Press ended. Commits only if the threshold was reached and not yet fired; otherwise aborts.</summary>
        public bool Release(double now)
        {
            if (!Holding) return false;
            if (Tick(now)) return true;
            Holding = false;
            return false;
        }

        /// <summary>Abort an in-progress hold (drag past slop, pointer left, focus lost). Never commits.</summary>
        public void Cancel() => Holding = false;

        /// <summary>A discrete activation (click, Enter, Space, pad submit). First arms; a second within the window commits.</summary>
        public bool Tap(double now)
        {
            if (Fired) return false;
            if (IsArmed(now)) return Fire();
            _armed = true; _armedAt = now;
            return false;
        }

        /// <summary>Disarm without committing.</summary>
        public void Disarm() => _armed = false;

        /// <summary>Return to idle so the same control can be used again.</summary>
        public void Reset()
        {
            Holding = false; Fired = false; _armed = false;
        }

        private bool Fire()
        {
            Fired = true; Holding = false; _armed = false; Commits++;
            return true;
        }
    }
}
