// OriginalGamepadNavigation.cs — pure decisions behind gamepad dispatch (US-15.3):
//  • Plan / PlanNavigate: what a pad press or direction does, as ordered steps. A step is tried and,
//    when nothing handled it, the next one runs. Bound actions become the KEY the keyboard binding
//    names (OriginalGamepad.KeyName), so keyboard and pad share one dispatcher: the existing UI
//    Toolkit key handlers (combat, map). Then come the pad's own fallbacks: submit the focused
//    control, cancel/back, or move focus.
//  • Next / Entry: spatial focus movement between on-screen rectangles (cards, enemies' controls,
//    map nodes, menu buttons, settings rows).
// Presentation/GamepadNavigator.cs executes the steps; it decides only "was it handled".
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Original
{
    /// <summary>An axis-aligned rectangle in panel coordinates (Y grows downward, as in UI Toolkit).</summary>
    public readonly struct PadRect
    {
        public readonly double X, Y, Width, Height;
        public PadRect(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
        public double Left => X;
        public double Right => X + Width;
        public double Top => Y;
        public double Bottom => Y + Height;
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;
        public bool Valid => !double.IsNaN(X) && !double.IsNaN(Y) && !double.IsInfinity(X) && !double.IsInfinity(Y) && Width > 0 && Height > 0 && !double.IsNaN(Width) && !double.IsNaN(Height);
        public bool Intersects(PadRect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
        public override string ToString() => "(" + X + "," + Y + " " + Width + "x" + Height + ")";
    }

    public enum PadStepKind
    {
        /// <summary>Dispatch Key (a Unity KeyCode name) as a key press through the keyboard handlers.</summary>
        Key,
        /// <summary>Activate the focused control (button click, toggle flip); with nothing focused, focus the first control.</summary>
        Submit,
        /// <summary>Cancel inside the focused control (close an open dropdown list).</summary>
        Cancel,
        /// <summary>Activate the visible Back / Close / Cancel control (IsBackControl).</summary>
        Back,
        /// <summary>Move focus to the visible menu control (IsMenuControl); never activates it.</summary>
        Menu,
        /// <summary>Move focus (or adjust the focused slider/choice) in Direction.</summary>
        Move,
    }

    public readonly struct PadStep
    {
        public readonly PadStepKind Kind; public readonly string Key; public readonly string Action; public readonly PadDirection Direction;
        public PadStep(PadStepKind kind, string key = null, string action = null, PadDirection direction = PadDirection.None) { Kind = kind; Key = key; Action = action; Direction = direction; }
        public override string ToString() => Kind == PadStepKind.Key ? "Key:" + Key : Kind == PadStepKind.Move ? "Move:" + Direction : Kind.ToString();
    }

    public static class OriginalGamepadNavigation
    {
        /// <summary>Every action a pad button can be bound to, in resolution order: the pad-only actions, then the
        /// map and combat actions of the keyboard list (Presentation/OriginalKeyBindings) without the card slots.</summary>
        public static readonly string[] Actions =
        {
            "cancel", "menu", "mapScrollUp", "mapScrollDown", "mapTop", "mapBottom",
            "combatPlay", "endTurn", "combatDeck", "drawPile", "discardPile", "exhaustPile",
            "flask1", "flask2", "flask3", "targetPrevious", "targetNext",
        };
        /// <summary>Actions whose key is re-sent while a d-pad direction repeats. Everything else (combat) acts once per press.</summary>
        public static readonly string[] RepeatingActions = { "mapScrollUp", "mapScrollDown" };
        /// <summary>Map actions. OriginalMapBoard.Key handles their keys only on the map viewport, so the pad sends
        /// them there (whatever map node has focus) while a map is shown, rather than to the focused element.</summary>
        public static readonly string[] MapActions = { "mapScrollUp", "mapScrollDown", "mapTop", "mapBottom" };
        public static bool IsMapAction(string action) => action != null && Array.IndexOf(MapActions, action) >= 0;

        /// <summary>The button that confirms / activates focused controls: whatever Play is bound to (default south).</summary>
        public static string ConfirmButton(IReadOnlyDictionary<string, string> padBindings)
        {
            var source = padBindings ?? OriginalGamepad.DefaultBindings;
            return source.TryGetValue("combatPlay", out var bound) && OriginalGamepad.Normalize(bound) != null ? OriginalGamepad.Normalize(bound) : "south";
        }
        /// <summary>Keys a focused button or field consumes itself (Enter/Space activate it). The pad goes straight
        /// to Submit for those instead of dispatching the key, exactly as combat ignores Enter on a focused button.</summary>
        public static bool IsActivationKey(string key) => key == "Return" || key == "KeypadEnter" || key == "Space";

        /// <summary>Steps for a button press (not a d-pad direction; see PlanNavigate). Empty when the press does nothing.</summary>
        public static IReadOnlyList<PadStep> Plan(IReadOnlyDictionary<string, string> padBindings, IReadOnlyDictionary<string, string> keyBindings, string button)
        {
            var steps = new List<PadStep>();
            var id = OriginalGamepad.Normalize(button);
            if (id == null || GamepadReader.DpadDirection(id) != PadDirection.None) return steps;
            AddAction(steps, OriginalGamepad.Action(padBindings, id, Actions), keyBindings);
            if (id == ConfirmButton(padBindings) && !steps.Any(s => s.Kind == PadStepKind.Submit)) steps.Add(new PadStep(PadStepKind.Submit));
            return steps;
        }

        /// <summary>
        /// Steps for a navigation signal. A d-pad direction first runs the action bound to that d-pad button
        /// (e.g. map scroll), then moves focus when nothing handled it; the left stick only moves focus.
        /// On a repeat, a bound action re-sends only when it is a RepeatingAction; when the first press of this
        /// hold was handled by its key (<paramref name="keyHandledFirst"/>) and the action does not repeat, a
        /// repeat does nothing, so holding a d-pad bound to End turn never ends several turns.
        /// </summary>
        public static IReadOnlyList<PadStep> PlanNavigate(IReadOnlyDictionary<string, string> padBindings, IReadOnlyDictionary<string, string> keyBindings, PadDirection direction, string dpadButton, bool repeat = false, bool keyHandledFirst = false)
        {
            var steps = new List<PadStep>();
            if (direction == PadDirection.None) return steps;
            var id = OriginalGamepad.Normalize(dpadButton);
            var action = id == null ? null : OriginalGamepad.Action(padBindings, id, Actions);
            if (action != null)
            {
                var repeats = Array.IndexOf(RepeatingActions, action) >= 0;
                if (repeat && keyHandledFirst && !repeats) return steps;
                if (!repeat || repeats) AddAction(steps, action, keyBindings);
            }
            steps.Add(new PadStep(PadStepKind.Move, direction: direction));
            return steps;
        }

        private static void AddAction(List<PadStep> steps, string action, IReadOnlyDictionary<string, string> keyBindings)
        {
            if (action == null) return;
            if (action == "menu") { steps.Add(new PadStep(PadStepKind.Menu, action: action)); return; }
            if (action == "cancel")
            {
                steps.Add(new PadStep(PadStepKind.Key, "Escape", action));
                steps.Add(new PadStep(PadStepKind.Cancel, action: action));
                steps.Add(new PadStep(PadStepKind.Back, action: action));
                return;
            }
            var key = OriginalGamepad.KeyName(action, keyBindings);
            if (key != null) steps.Add(new PadStep(PadStepKind.Key, key, action));
        }

        /// <summary>Back controls by element name: "back", "close", or a name ending in -back, -close or -cancel.</summary>
        public static bool IsBackControl(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name == "back" || name == "close" || name.EndsWith("-back", StringComparison.Ordinal) || name.EndsWith("-close", StringComparison.Ordinal) || name.EndsWith("-cancel", StringComparison.Ordinal);
        }
        /// <summary>Menu controls by element name, in preference order (the native run's Save and return, then the campaign Menu).</summary>
        public static readonly string[] MenuControls = { "native-menu", "menu" };

        /// <summary>
        /// Index of the best rectangle in <paramref name="direction"/> from <paramref name="from"/>, or -1.
        /// A candidate must lie further along the direction (its centre past the origin's centre). Score:
        /// edge gap along the direction + 3 × gap across it (0 when the two overlap across) + 0.5 × centre
        /// offset across it, so the same row/column wins over a nearer diagonal. Ties keep tree order.
        /// </summary>
        public static int Next(PadRect from, IReadOnlyList<PadRect> candidates, PadDirection direction, int self = -1)
        {
            if (candidates == null || direction == PadDirection.None) return -1;
            var best = -1; var bestScore = double.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (i == self) continue;
                var c = candidates[i];
                if (!c.Valid) continue;
                double along, across, offset;
                switch (direction)
                {
                    case PadDirection.Right:
                        if (c.CenterX <= from.CenterX + .5) continue;
                        along = Math.Max(0, c.Left - from.Right); across = Gap(c.Top, c.Bottom, from.Top, from.Bottom); offset = Math.Abs(c.CenterY - from.CenterY); break;
                    case PadDirection.Left:
                        if (c.CenterX >= from.CenterX - .5) continue;
                        along = Math.Max(0, from.Left - c.Right); across = Gap(c.Top, c.Bottom, from.Top, from.Bottom); offset = Math.Abs(c.CenterY - from.CenterY); break;
                    case PadDirection.Down:
                        if (c.CenterY <= from.CenterY + .5) continue;
                        along = Math.Max(0, c.Top - from.Bottom); across = Gap(c.Left, c.Right, from.Left, from.Right); offset = Math.Abs(c.CenterX - from.CenterX); break;
                    default:
                        if (c.CenterY >= from.CenterY - .5) continue;
                        along = Math.Max(0, from.Top - c.Bottom); across = Gap(c.Left, c.Right, from.Left, from.Right); offset = Math.Abs(c.CenterX - from.CenterX); break;
                }
                var score = along + 3 * across + .5 * offset;
                if (score < bestScore - 1e-9) { bestScore = score; best = i; }
            }
            return best;
        }
        private static double Gap(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Max(a0, b0) - Math.Min(a1, b1));

        /// <summary>Where focus enters when nothing usable is focused: the first rectangle in reading order
        /// (rows 8 px apart, then left to right) among those inside <paramref name="view"/>, else among all.</summary>
        public static int Entry(IReadOnlyList<PadRect> candidates, PadRect view)
        {
            if (candidates == null) return -1;
            var indices = Enumerable.Range(0, candidates.Count).Where(i => candidates[i].Valid).ToList();
            var visible = view.Valid ? indices.Where(i => candidates[i].Intersects(view)).ToList() : indices;
            var pool = visible.Count > 0 ? visible : indices;
            if (pool.Count == 0) return -1;
            return pool.OrderBy(i => Math.Floor(candidates[i].Top / 8)).ThenBy(i => candidates[i].Left).ThenBy(i => i).First();
        }

        /// <summary>One "Label · Button" line per pad-bindable action that has a button, for the controls screens.</summary>
        public static IReadOnlyList<(string Action, string Button)> Bound(IReadOnlyDictionary<string, string> padBindings, IEnumerable<string> actions)
        {
            var source = padBindings ?? OriginalGamepad.DefaultBindings;
            return actions.Where(a => source.TryGetValue(a, out var b) && OriginalGamepad.Normalize(b) != null)
                .Select(a => (a, OriginalGamepad.Normalize(source[a]))).ToArray();
        }
    }
}
