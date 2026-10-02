// OriginalGamepadInput.cs — pure gamepad reading for US-15.3: raw frame → button ids, stick
// deadzones, d-pad/stick navigation with repeat, and right-stick panning. No UnityEngine here:
// Presentation/GamepadDriver.cs copies Input.GetKey(JoystickButtonN) and Input.GetAxisRaw
// ("GamepadAxis1".."GamepadAxis10", ProjectSettings/InputManager.asset) into a GamepadFrame and
// hands the resulting signals to Presentation/GamepadNavigator.cs.
// LAYOUTS: which joystick button / axis is which physical control differs per platform under the
// legacy Input Manager. XInput is the Windows layout; Standard is the W3C standard-gamepad order
// that WebGL exposes; Android is Standard plus the hat-axis d-pad. Only the layouts' sources are
// assumptions; docs/Unity-Settings.md lists what still needs real hardware.
using System;
using System.Collections.Generic;

namespace AshenSpire.Domain.Original
{
    public enum PadDirection { None, Up, Down, Left, Right }

    /// <summary>One frame of raw pad input: joystick buttons 0..19 and axes 1..10 (index 0..9, raw, unscaled).</summary>
    public sealed class GamepadFrame
    {
        public const int ButtonCount = 20, AxisCount = 10;
        public readonly bool[] Buttons = new bool[ButtonCount];
        public readonly float[] Axes = new float[AxisCount];
        public bool Button(int index) => index >= 0 && index < ButtonCount && Buttons[index];
        public double Axis(int index) => index >= 0 && index < AxisCount && !float.IsNaN(Axes[index]) ? Math.Max(-1, Math.Min(1, Axes[index])) : 0;
        public void Clear() { Array.Clear(Buttons, 0, ButtonCount); Array.Clear(Axes, 0, AxisCount); }
    }

    /// <summary>Where a logical button reads from: a joystick button index, or an axis index past a signed threshold.</summary>
    public readonly struct PadSource
    {
        public readonly int Button, Axis, Sign;
        private PadSource(int button, int axis, int sign) { Button = button; Axis = axis; Sign = sign; }
        public static PadSource Key(int button) => new PadSource(button, -1, 0);
        /// <summary>Axis index (0-based: Unity's "X axis" = 0, "3rd axis" = 2 …); sign +1 or -1 is the pressed direction.</summary>
        public static PadSource AxisPast(int axis, int sign) => new PadSource(-1, axis, sign >= 0 ? 1 : -1);
        public bool IsAxis => Axis >= 0;
    }

    /// <summary>A platform's mapping from physical controls to OriginalGamepad.Buttons ids.</summary>
    public sealed class GamepadLayout
    {
        public string Id { get; }
        public string Description { get; }
        /// <summary>Stick axis indices. Raw Y is negative when pushed up on every layout here (W3C, XInput, Android).</summary>
        public int LeftX { get; }
        public int LeftY { get; }
        public int RightX { get; }
        public int RightY { get; }
        private readonly Dictionary<string, PadSource[]> _sources;
        public GamepadLayout(string id, string description, int leftX, int leftY, int rightX, int rightY, IDictionary<string, PadSource[]> sources)
        {
            Id = id; Description = description; LeftX = leftX; LeftY = leftY; RightX = rightX; RightY = rightY;
            _sources = new Dictionary<string, PadSource[]>(StringComparer.Ordinal);
            foreach (var pair in sources)
            {
                var button = OriginalGamepad.Normalize(pair.Key) ?? throw new ArgumentException("Unknown gamepad button: " + pair.Key);
                _sources[button] = pair.Value ?? Array.Empty<PadSource>();
            }
        }
        public IReadOnlyList<PadSource> Sources(string button) => _sources.TryGetValue(OriginalGamepad.Normalize(button) ?? "", out var list) ? list : Array.Empty<PadSource>();
        /// <summary>Strongest reading of <paramref name="button"/> in 0..1 (a pressed key reads 1).</summary>
        public double Level(string button, GamepadFrame frame)
        {
            double level = 0;
            foreach (var source in Sources(button))
                level = Math.Max(level, source.IsAxis ? Math.Max(0, frame.Axis(source.Axis) * source.Sign) : frame.Button(source.Button) ? 1 : 0);
            return level;
        }

        /// <summary>Windows XInput under the legacy Input Manager: A B X Y LB RB Back Start LS RS = buttons 0..9,
        /// LT/RT = 9th/10th axis (0..1), d-pad = 6th axis (right +) / 7th axis (up +), right stick = 4th/5th axis.</summary>
        public static readonly GamepadLayout XInput = new GamepadLayout("xinput", "Xbox / XInput layout (Windows)", 0, 1, 3, 4, new Dictionary<string, PadSource[]>
        {
            ["south"] = new[] { PadSource.Key(0) }, ["east"] = new[] { PadSource.Key(1) }, ["west"] = new[] { PadSource.Key(2) }, ["north"] = new[] { PadSource.Key(3) },
            ["leftShoulder"] = new[] { PadSource.Key(4) }, ["rightShoulder"] = new[] { PadSource.Key(5) },
            ["select"] = new[] { PadSource.Key(6) }, ["start"] = new[] { PadSource.Key(7) },
            ["leftStick"] = new[] { PadSource.Key(8) }, ["rightStick"] = new[] { PadSource.Key(9) },
            ["leftTrigger"] = new[] { PadSource.AxisPast(8, 1) }, ["rightTrigger"] = new[] { PadSource.AxisPast(9, 1) },
            ["dpadUp"] = new[] { PadSource.AxisPast(6, 1) }, ["dpadDown"] = new[] { PadSource.AxisPast(6, -1) },
            ["dpadLeft"] = new[] { PadSource.AxisPast(5, -1) }, ["dpadRight"] = new[] { PadSource.AxisPast(5, 1) },
        });

        /// <summary>W3C "standard" gamepad (what WebGL builds read from the browser Gamepad API): button i is
        /// standard index i (d-pad = 12..15, triggers = 6/7), left stick = axes 0/1, right stick = axes 2/3.</summary>
        public static readonly GamepadLayout Standard = new GamepadLayout("standard", "Standard gamepad layout (WebGL)", 0, 1, 2, 3, StandardSources(false));

        /// <summary>Android: the standard order, plus a d-pad reported as a hat on the 6th/7th axis (up negative)
        /// and Start also on button 10. Assumed from Android's KEYCODE_BUTTON_* order; unverified on hardware.</summary>
        public static readonly GamepadLayout Android = new GamepadLayout("android", "Standard gamepad layout (Android)", 0, 1, 2, 3, StandardSources(true));

        private static Dictionary<string, PadSource[]> StandardSources(bool android)
        {
            var map = new Dictionary<string, PadSource[]>(StringComparer.Ordinal);
            for (var i = 0; i < OriginalGamepad.Buttons.Length; i++) map[OriginalGamepad.Buttons[i]] = new[] { PadSource.Key(i) };
            if (!android) return map;
            map["start"] = new[] { PadSource.Key(9), PadSource.Key(10) };
            map["dpadUp"] = new[] { PadSource.Key(12), PadSource.AxisPast(6, -1) };
            map["dpadDown"] = new[] { PadSource.Key(13), PadSource.AxisPast(6, 1) };
            map["dpadLeft"] = new[] { PadSource.Key(14), PadSource.AxisPast(5, -1) };
            map["dpadRight"] = new[] { PadSource.Key(15), PadSource.AxisPast(5, 1) };
            return map;
        }

        /// <summary>Layout for a platform name ("windows", "webgl", "android", "mac", "linux", "editor-…").
        /// Everything that is not WebGL or Android uses XInput, the only desktop layout this build assumes.</summary>
        public static GamepadLayout ForPlatform(string platform)
        {
            var name = (platform ?? "").Trim().ToLowerInvariant();
            if (name.Contains("webgl")) return Standard;
            if (name.Contains("android")) return Android;
            return XInput;
        }
    }

    /// <summary>Feel constants for pad reading. Times are seconds, sticks are 0..1 after the deadzone.</summary>
    public sealed class GamepadTuning
    {
        public double StickDeadzone { get; set; } = .25;
        /// <summary>How far (after the deadzone) the left stick must lean before it navigates.</summary>
        public double NavigateThreshold { get; set; } = .5;
        public double RepeatDelay { get; set; } = .4;
        public double RepeatInterval { get; set; } = .12;
        /// <summary>Analog buttons (triggers, axis d-pads) press past Press and release below Release.</summary>
        public double PressThreshold { get; set; } = .5;
        public double ReleaseThreshold { get; set; } = .3;
        /// <summary>Right-stick pan, in panel pixels per second at full tilt.</summary>
        public double PanSpeed { get; set; } = 900;
        /// <summary>Frames longer than this (a hitch, a background tab) pan as if they were this long.</summary>
        public double MaxFrame { get; set; } = .1;
    }

    public enum PadSignalKind { Press, Navigate, Pan }

    /// <summary>A reader output. Press: a button went down. Navigate: a direction fired (first or repeat);
    /// Button is the d-pad button for d-pad navigation, null for the stick. Pan: right-stick pixels this frame (Y up positive).</summary>
    public readonly struct PadSignal
    {
        public readonly PadSignalKind Kind; public readonly string Button; public readonly PadDirection Direction; public readonly bool Repeat; public readonly double PanX, PanY;
        public PadSignal(PadSignalKind kind, string button, PadDirection direction, bool repeat, double panX, double panY)
        { Kind = kind; Button = button; Direction = direction; Repeat = repeat; PanX = panX; PanY = panY; }
        public override string ToString() => Kind == PadSignalKind.Pan ? "Pan(" + PanX.ToString("0.##") + "," + PanY.ToString("0.##") + ")" : Kind + "(" + (Button ?? "-") + "," + Direction + (Repeat ? ",repeat" : "") + ")";
    }

    /// <summary>
    /// Stateful frame-to-signal reader. Step once per frame with the frame and a monotonic time in seconds.
    /// Every button (d-pad included) reports a Press on its down edge — rebinding captures those — and the
    /// d-pad or left stick also emit Navigate with a repeat delay; the d-pad wins when both are held.
    /// </summary>
    public sealed class GamepadReader
    {
        private readonly HashSet<string> _down = new HashSet<string>(StringComparer.Ordinal);
        private PadDirection _held = PadDirection.None;
        private string _heldButton;
        private double _nextRepeat, _lastTime = double.NaN;
        private bool _primed;
        public GamepadLayout Layout { get; set; }
        public GamepadTuning Tuning { get; }
        /// <summary>True when the last frame had any button down or a stick past its deadzone.</summary>
        public bool Active { get; private set; }
        public GamepadReader(GamepadLayout layout, GamepadTuning tuning = null) { Layout = layout ?? GamepadLayout.XInput; Tuning = tuning ?? new GamepadTuning(); }

        /// <summary>Forget held buttons and directions (focus loss, a pad unplugged). Buttons still held when input
        /// resumes do not fire until released and pressed again.</summary>
        public void Reset() { _down.Clear(); _held = PadDirection.None; _heldButton = null; _lastTime = double.NaN; _primed = false; Active = false; }

        public IReadOnlyList<PadSignal> Step(GamepadFrame frame, double time)
        {
            var signals = new List<PadSignal>();
            if (frame == null) { Reset(); return signals; }
            var t = Tuning;
            var active = false;
            // Buttons: hysteresis on analog sources, edge-triggered presses. On the first frame after a Reset,
            // anything already held is recorded without firing.
            foreach (var button in OriginalGamepad.Buttons)
            {
                var level = Layout.Level(button, frame);
                var wasDown = _down.Contains(button);
                var isDown = wasDown ? level > t.ReleaseThreshold : level >= t.PressThreshold;
                if (isDown) active = true;
                if (isDown && !wasDown) { _down.Add(button); if (_primed) signals.Add(new PadSignal(PadSignalKind.Press, button, PadDirection.None, false, 0, 0)); }
                else if (!isDown && wasDown) _down.Remove(button);
            }
            // Navigation: d-pad first, else the left stick past its threshold.
            var (lx, ly) = Deadzone(frame.Axis(Layout.LeftX), -frame.Axis(Layout.LeftY), t.StickDeadzone);
            if (lx != 0 || ly != 0) active = true;
            var dpad = DpadDirection(_down);
            var direction = dpad != PadDirection.None ? dpad : Quantize(lx, ly, t.NavigateThreshold);
            var source = dpad != PadDirection.None ? DpadButton(dpad) : null;
            if (direction == PadDirection.None) { _held = PadDirection.None; _heldButton = null; }
            else if (direction != _held || source != _heldButton)
            {
                _held = direction; _heldButton = source; _nextRepeat = time + t.RepeatDelay;
                if (_primed) signals.Add(new PadSignal(PadSignalKind.Navigate, source, direction, false, 0, 0));
            }
            else if (time >= _nextRepeat)
            {
                // At most one repeat per frame; a long hitch does not replay the missed repeats.
                _nextRepeat += t.RepeatInterval;
                if (_nextRepeat <= time) _nextRepeat = time + t.RepeatInterval;
                signals.Add(new PadSignal(PadSignalKind.Navigate, source, direction, true, 0, 0));
            }
            // Pan: right stick, scaled by elapsed time.
            var (rx, ry) = Deadzone(frame.Axis(Layout.RightX), -frame.Axis(Layout.RightY), t.StickDeadzone);
            if (rx != 0 || ry != 0) active = true;
            var dt = double.IsNaN(_lastTime) ? 0 : Math.Max(0, Math.Min(t.MaxFrame, time - _lastTime));
            if ((rx != 0 || ry != 0) && dt > 0 && _primed) signals.Add(new PadSignal(PadSignalKind.Pan, null, PadDirection.None, false, rx * t.PanSpeed * dt, ry * t.PanSpeed * dt));
            _lastTime = time;
            _primed = true;
            Active = active;
            return signals;
        }

        public bool IsDown(string button) => _down.Contains(OriginalGamepad.Normalize(button) ?? "");

        /// <summary>Radial deadzone, rescaled so the output starts at 0 at the edge and reaches 1 at full tilt.</summary>
        public static (double X, double Y) Deadzone(double x, double y, double deadzone)
        {
            var magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= deadzone || magnitude <= 0) return (0, 0);
            var scaled = Math.Min(1, (magnitude - deadzone) / Math.Max(1e-9, 1 - deadzone));
            return (x / magnitude * scaled, y / magnitude * scaled);
        }
        /// <summary>The dominant direction of a deadzoned vector (Y up positive) whose length reaches <paramref name="threshold"/>.</summary>
        public static PadDirection Quantize(double x, double y, double threshold)
        {
            if (Math.Sqrt(x * x + y * y) < threshold || (x == 0 && y == 0)) return PadDirection.None;
            if (Math.Abs(x) > Math.Abs(y)) return x > 0 ? PadDirection.Right : PadDirection.Left;
            return y > 0 ? PadDirection.Up : PadDirection.Down;
        }
        private static PadDirection DpadDirection(HashSet<string> down)
        {
            // Opposites cancel; with a diagonal, vertical wins (lists and settings scroll vertically).
            var up = down.Contains("dpadUp") && !down.Contains("dpadDown");
            var dn = down.Contains("dpadDown") && !down.Contains("dpadUp");
            var left = down.Contains("dpadLeft") && !down.Contains("dpadRight");
            var right = down.Contains("dpadRight") && !down.Contains("dpadLeft");
            return up ? PadDirection.Up : dn ? PadDirection.Down : left ? PadDirection.Left : right ? PadDirection.Right : PadDirection.None;
        }
        public static string DpadButton(PadDirection direction)
        {
            switch (direction)
            {
                case PadDirection.Up: return "dpadUp";
                case PadDirection.Down: return "dpadDown";
                case PadDirection.Left: return "dpadLeft";
                case PadDirection.Right: return "dpadRight";
                default: return null;
            }
        }
        public static PadDirection DpadDirection(string button)
        {
            switch (OriginalGamepad.Normalize(button))
            {
                case "dpadUp": return PadDirection.Up;
                case "dpadDown": return PadDirection.Down;
                case "dpadLeft": return PadDirection.Left;
                case "dpadRight": return PadDirection.Right;
                default: return PadDirection.None;
            }
        }
    }
}
