// FeelTween.cs — one scheduled, cancellable UI Toolkit tween driven by wall-clock time.
// WIRING: FeelDriver (card hover) and CombatFeedback (combat beat) use it; nothing else
// should run its own per-frame timer. Steps receive elapsed milliseconds; the owner samples
// FeelPlayback/FeelCurves itself so CSS per-keyframe easing stays in one place (FeelCurves).
// A tween stops when cancelled, when it finishes, or when its element leaves the panel.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed class FeelTween
    {
        private const long FrameMs = 16;
        private IVisualElementScheduledItem _item;
        private Action<string> _finished;
        public bool Running => _item != null;

        /// <summary>Runs `step(elapsedMs)` every frame until `durationMs`, then `finished("completed")`.</summary>
        public static FeelTween Run(VisualElement owner, double durationMs, Action<double> step, Action<string> finished = null)
        {
            var tween = new FeelTween { _finished = finished };
            var started = Time.realtimeSinceStartupAsDouble;
            step(0);
            tween._item = owner.schedule.Execute(() =>
            {
                if (owner.panel == null) { tween.Stop("detached"); return; }
                var elapsed = (Time.realtimeSinceStartupAsDouble - started) * 1000;
                step(Math.Min(elapsed, durationMs));
                if (elapsed >= durationMs) tween.Stop("completed");
            }).Every(FrameMs);
            return tween;
        }

        /// <summary>Stops without running further steps. The owner restores its own end state.</summary>
        public void Stop(string status = "cancelled")
        {
            if (_item == null) return;
            _item.Pause(); _item = null;
            var finished = _finished; _finished = null;
            finished?.Invoke(status);
        }
    }
}
