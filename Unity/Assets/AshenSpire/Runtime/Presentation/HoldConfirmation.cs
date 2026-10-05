using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    // The irreversible action happens only on release after a full hold. Leaving,
    // cancelling, losing focus or detaching always cancels. Ordinary confirmation
    // remains available when the preference is off.
    internal static class HoldConfirmation
    {
        public static void Bind(Button button, Action confirmed, Func<bool> enabled)
        {
            var started = -1d; var pointer = -1; var label = button.text;
            IVisualElementScheduledItem progress = null;
            void Reset()
            {
                started = -1; progress?.Pause(); progress = null;
                button.text = label; button.RemoveFromClassList("hold-confirming");
                if (pointer >= 0 && button.HasPointerCapture(pointer)) button.ReleasePointer(pointer);
                pointer = -1;
            }
            void Start()
            {
                if (started >= 0) return;
                started = Time.realtimeSinceStartupAsDouble;
                button.AddToClassList("hold-confirming");
                progress = button.schedule.Execute(() => button.text = Time.realtimeSinceStartupAsDouble - started >= .7 ? "Release to confirm" : "Keep holding…").Every(50);
            }
            void Release()
            {
                var ready = started >= 0 && Time.realtimeSinceStartupAsDouble - started >= .7 && button.enabledInHierarchy;
                Reset(); if (ready) confirmed();
            }
            button.clicked += () => { if (!enabled()) confirmed(); };
            button.RegisterCallback<PointerDownEvent>(e => { if (!enabled() || e.button != 0) return; pointer = e.pointerId; button.CapturePointer(pointer); Start(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(e => { if (!enabled()) return; var inside = button.worldBound.Contains(e.position); if (inside) Release(); else Reset(); e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerLeaveEvent>(_ => Reset());
            button.RegisterCallback<PointerCancelEvent>(_ => Reset());
            button.RegisterCallback<PointerCaptureOutEvent>(_ => Reset());
            button.RegisterCallback<FocusOutEvent>(_ => Reset());
            button.RegisterCallback<DetachFromPanelEvent>(_ => Reset());
            button.RegisterCallback<KeyDownEvent>(e => { if (enabled() && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space)) { Start(); e.StopImmediatePropagation(); } }, TrickleDown.TrickleDown);
            button.RegisterCallback<KeyUpEvent>(e => { if (enabled() && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Space)) { Release(); e.StopImmediatePropagation(); } }, TrickleDown.TrickleDown);
            button.RegisterCallback<NavigationSubmitEvent>(e => { if (enabled()) e.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
        }
    }
}
