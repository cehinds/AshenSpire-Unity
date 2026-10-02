// HoldConfirmButton.cs — hold-to-confirm for DESTRUCTIVE actions (US-13.3).
// RULES live in Domain/Original/HoldConfirmState (pure, time-injected) and the policy
// table in Resources/Original/confirmation-policies.json (ConfirmationPolicy). This file
// only maps UI Toolkit input onto that state and paints it.
// INPUT:
//   pointer — press and hold; a bar fills along the bottom edge and the action commits
//             when it is full. Release early, drag past the slop or leave the button and
//             nothing happens.
//   tap     — a quick click, Enter, Space or pad submit ARMS the button ("Tap again to
//             confirm"); a second activation within the arm window commits. Holding a
//             key that auto-repeats therefore also confirms. Browser playtests that need
//             to commit click the same id twice.
// The fill is state, not decoration, so it ignores reduced motion (HTML rule).
// Element ids are untouched; the bar is an unnamed child with class hold-confirm-fill.
using System;
using AshenSpire.Domain.Original;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Presentation
{
    public static class HoldConfirmButton
    {
        private static ConfirmationPolicy _policy;
        public static ConfirmationPolicy Policy
        {
            get
            {
                if (_policy != null) return _policy;
                var asset = Resources.Load<TextAsset>(ConfirmationPolicy.ResourcePath);
                if (asset == null) throw new InvalidOperationException("Missing Resources/" + ConfirmationPolicy.ResourcePath + ".json.");
                return _policy = new ConfirmationPolicy(asset.text);
            }
        }

        /// <summary>Wire <paramref name="button"/> for <paramref name="actionId"/>: a DESTRUCTIVE action
        /// gets hold/tap-twice; any other level commits on a plain click.</summary>
        public static void Bind(Button button, string actionId, Action commit, Action changed = null)
        {
            if (Policy.RequiresHold(actionId)) Attach(button, commit, changed);
            else button.clicked += commit;
        }

        public static HoldConfirmState Attach(Button button, Action commit, Action changed = null)
        {
            var input = FeelDriver.Profile.Input;
            var state = new HoldConfirmState(input.HoldConfirmNormalMs);
            var slop = Mathf.Max(1f, input.DragSlopPx);
            var label = button.text;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("hold-confirm-fill");
            button.Add(fill);
            button.AddToClassList("hold-confirm");
            button.tooltip = state.HoldEnabled ? "Hold to confirm, or activate twice" : "Activate twice to confirm";
            var origin = Vector3.zero; var wasArmed = false;
            IVisualElementScheduledItem ticker = null;
            double Now() => Time.realtimeSinceStartupAsDouble * 1000.0;
            void Paint()
            {
                var now = Now(); var armed = state.IsArmed(now);
                fill.style.width = Length.Percent((float)(state.Progress(now) * 100));
                button.EnableInClassList("hold-confirm-holding", state.Holding);
                button.EnableInClassList("hold-confirm-armed", armed);
                if (armed != wasArmed)
                {
                    wasArmed = armed; button.text = armed ? "Tap again to confirm · " + label : label;
                    changed?.Invoke();
                }
                if (!state.Holding && !armed) ticker?.Pause();
            }
            void Commit()
            {
                commit();
                // Back to idle in case the view stays (e.g. the command was refused); a trailing
                // click can then only re-arm, never commit.
                state.Reset(); Paint();
            }
            void Run()
            {
                if (ticker == null) ticker = button.schedule.Execute(() => { if (state.Tick(Now())) Commit(); else Paint(); }).Every(16);
                else ticker.Resume();
            }
            button.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || !button.enabledInHierarchy) return;
                origin = e.position; state.Begin(Now()); Run(); Paint();
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (state.Holding && (e.position - origin).magnitude > slop) { state.Cancel(); Paint(); }
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(e =>
            {
                if (state.Release(Now())) Commit(); else Paint();
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (state.Holding) { state.Cancel(); Paint(); } });
            button.RegisterCallback<PointerCancelEvent>(_ => { state.Cancel(); Paint(); });
            button.RegisterCallback<PointerCaptureOutEvent>(_ => { if (state.Holding) { state.Cancel(); Paint(); } });
            button.RegisterCallback<FocusOutEvent>(_ => { state.Cancel(); state.Disarm(); Paint(); });
            button.clicked += () =>
            {
                if (state.Tap(Now())) Commit();
                else { Run(); Paint(); }
            };
            return state;
        }
    }
}
