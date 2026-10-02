// Gamepad checks (US-15.3): pad layouts, deadzones, repeat timing, button → step plans, spatial
// focus movement, and the Input Manager / wiring the Presentation driver depends on.
// Run from the repository root: dotnet run --project UnityTests/Gamepad [-- <repo root>]
using System.Text.RegularExpressions;
using AshenSpire.Domain.Original;

var root = args.Length > 0 ? args[0] : FindRoot();
var checks = 0;
var failures = 0;
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;
string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
string Steps(IReadOnlyList<PadStep> steps) => string.Join(" ", steps.Select(s => s.ToString()));

try
{
    // ---- Layouts ---------------------------------------------------------------------------
    var frame = new GamepadFrame();
    var x = GamepadLayout.XInput;
    for (var i = 0; i < 10; i++)
    {
        frame.Clear(); frame.Buttons[i] = true;
        var expected = new[] { "south", "east", "west", "north", "leftShoulder", "rightShoulder", "select", "start", "leftStick", "rightStick" }[i];
        var down = OriginalGamepad.Buttons.Where(b => x.Level(b, frame) >= 1).ToArray();
        Check(down.Length == 1 && down[0] == expected, "XInput joystick button " + i + " is " + expected);
    }
    frame.Clear(); frame.Axes[8] = 1; Check(x.Level("leftTrigger", frame) == 1 && x.Level("rightTrigger", frame) == 0, "XInput LT is the 9th axis");
    frame.Clear(); frame.Axes[9] = .7f; Check(Near(x.Level("rightTrigger", frame), .7, 1e-5), "XInput RT is the 10th axis, analog");
    frame.Clear(); frame.Axes[5] = 1; Check(x.Level("dpadRight", frame) == 1 && x.Level("dpadLeft", frame) == 0, "XInput d-pad right is the 6th axis positive");
    frame.Clear(); frame.Axes[6] = 1; Check(x.Level("dpadUp", frame) == 1 && x.Level("dpadDown", frame) == 0, "XInput d-pad up is the 7th axis positive");
    frame.Clear(); frame.Axes[6] = -1; Check(x.Level("dpadDown", frame) == 1, "XInput d-pad down is the 7th axis negative");
    Equal((x.LeftX, x.LeftY, x.RightX, x.RightY), (0, 1, 3, 4), "XInput sticks: X/Y axes and 4th/5th axes");
    var standard = GamepadLayout.Standard;
    for (var i = 0; i < OriginalGamepad.Buttons.Length; i++)
    {
        frame.Clear(); frame.Buttons[i] = true;
        Check(standard.Level(OriginalGamepad.Buttons[i], frame) == 1 && OriginalGamepad.Buttons.Count(b => standard.Level(b, frame) > 0) == 1, "Standard button " + i + " is " + OriginalGamepad.Buttons[i] + " (W3C order)");
    }
    Equal((standard.LeftX, standard.LeftY, standard.RightX, standard.RightY), (0, 1, 2, 3), "Standard sticks: axes 0/1 and 2/3");
    frame.Clear(); frame.Axes[6] = -1; Check(GamepadLayout.Android.Level("dpadUp", frame) == 1 && standard.Level("dpadUp", frame) == 0, "Android adds a hat d-pad (7th axis, up negative); WebGL does not");
    frame.Clear(); frame.Buttons[10] = true; Check(GamepadLayout.Android.Level("start", frame) == 1, "Android Start also reads button 10");
    Equal(GamepadLayout.ForPlatform("webgl").Id, "standard", "WebGL uses the standard layout");
    Equal(GamepadLayout.ForPlatform("android").Id, "android", "Android uses the Android layout");
    Equal(GamepadLayout.ForPlatform("windows").Id, "xinput", "Windows uses XInput");
    Equal(GamepadLayout.ForPlatform("mac").Id, "xinput", "macOS falls back to XInput (unverified)");
    Equal(GamepadLayout.ForPlatform(null).Id, "xinput", "unknown platform falls back to XInput");
    Check(OriginalGamepad.Buttons.All(b => x.Sources(b).Count > 0 && standard.Sources(b).Count > 0 && GamepadLayout.Android.Sources(b).Count > 0), "every button id has a source on every layout");
    frame.Clear(); frame.Axes[0] = float.NaN; frame.Axes[1] = 5; Check(frame.Axis(0) == 0 && frame.Axis(1) == 1 && frame.Axis(42) == 0, "frame axes ignore NaN, clamp to ±1 and read 0 out of range");

    // ---- Deadzone and direction ------------------------------------------------------------
    Equal(GamepadReader.Deadzone(.2, 0, .25), (0.0, 0.0), "inside the deadzone reads zero");
    var dz = GamepadReader.Deadzone(1, 0, .25); Check(Near(dz.X, 1) && dz.Y == 0, "full tilt reads 1");
    dz = GamepadReader.Deadzone(.625, 0, .25); Check(Near(dz.X, .5), "half way past the deadzone reads .5 (rescaled)");
    dz = GamepadReader.Deadzone(.6, .8, .25); Check(Near(Math.Sqrt(dz.X * dz.X + dz.Y * dz.Y), 1) && Near(dz.X / dz.Y, .75), "radial deadzone keeps the direction");
    dz = GamepadReader.Deadzone(1, 1, .25); Check(Math.Sqrt(dz.X * dz.X + dz.Y * dz.Y) <= 1 + 1e-9, "corner magnitude is capped at 1");
    Equal(GamepadReader.Quantize(.9, .1, .5), PadDirection.Right, "mostly right → Right");
    Equal(GamepadReader.Quantize(-.2, -.8, .5), PadDirection.Down, "mostly down → Down (Y up positive)");
    Equal(GamepadReader.Quantize(.3, .3, .5), PadDirection.None, "below the navigate threshold → None");
    Equal(GamepadReader.Quantize(0, .6, .5), PadDirection.Up, "up → Up");

    // ---- Reader: presses, repeat, pan --------------------------------------------------------
    var reader = new GamepadReader(GamepadLayout.XInput);
    var f = new GamepadFrame();
    List<PadSignal> Step(double t) => reader.Step(f, t).ToList();
    f.Buttons[0] = true;
    Equal(Step(0).Count, 0, "a button held on the very first frame does not fire");
    f.Buttons[0] = false; Step(.016);
    f.Buttons[0] = true;
    var s = Step(.032); Check(s.Count == 1 && s[0].Kind == PadSignalKind.Press && s[0].Button == "south", "south press fires once on its down edge");
    Equal(Step(.048).Count, 0, "holding a button never repeats");
    Equal(Step(2).Count, 0, "holding a button for seconds never repeats");
    Check(reader.IsDown("south") && reader.Active, "held state and Active are reported");
    f.Buttons[0] = false; Step(2.016); Check(!reader.Active, "Active clears when released");
    // Trigger hysteresis
    f.Axes[8] = .55f; s = Step(2.032); Check(s.Count == 1 && s[0].Button == "leftTrigger", "LT past .5 presses");
    f.Axes[8] = .4f; Equal(Step(2.048).Count, 0, "LT falling to .4 stays held (release below .3)");
    f.Axes[8] = .55f; Equal(Step(2.064).Count, 0, "LT back up without a release does not press again");
    f.Axes[8] = .2f; Step(2.08); f.Axes[8] = .6f; Equal(Step(2.096).Count, 1, "after release below .3, LT presses again");
    f.Axes[8] = 0; Step(2.11);
    // D-pad navigation with repeat
    var t0 = 3.0;
    f.Axes[6] = 1; s = Step(t0);
    Check(s.Any(p => p.Kind == PadSignalKind.Press && p.Button == "dpadUp"), "d-pad up reports a Press (rebinding capture uses it)");
    Check(s.Count(p => p.Kind == PadSignalKind.Navigate) == 1 && s.First(p => p.Kind == PadSignalKind.Navigate).Direction == PadDirection.Up && s.First(p => p.Kind == PadSignalKind.Navigate).Button == "dpadUp" && !s.First(p => p.Kind == PadSignalKind.Navigate).Repeat, "d-pad up navigates Up immediately, tagged with its button");
    Equal(Step(t0 + .39).Count, 0, "no repeat before the .4 s delay");
    s = Step(t0 + .4); Check(s.Count == 1 && s[0].Repeat && s[0].Direction == PadDirection.Up, "first repeat at .4 s");
    Equal(Step(t0 + .5).Count, 0, "no repeat before the .12 s interval");
    s = Step(t0 + .52); Check(s.Count == 1 && s[0].Repeat, "second repeat .12 s later");
    s = Step(t0 + 5); Check(s.Count == 1 && s[0].Repeat, "a long hitch fires a single repeat, not a burst");
    Equal(Step(t0 + 5.05).Count, 0, "and the interval restarts from the hitch");
    f.Axes[6] = 0; Equal(Step(t0 + 6).Count, 0, "releasing the d-pad fires nothing");
    // Stick navigation
    f.Axes[0] = .4f; Equal(Step(t0 + 7).Count(p => p.Kind == PadSignalKind.Navigate), 0, "a light stick lean below the threshold does not navigate");
    f.Axes[0] = .95f; s = Step(t0 + 7.1); Check(s.Count == 1 && s[0].Direction == PadDirection.Right && s[0].Button == null, "a firm stick lean navigates Right with no button");
    f.Axes[1] = -1f; f.Axes[0] = .1f; s = Step(t0 + 7.2); Check(s.Count == 1 && s[0].Direction == PadDirection.Up && !s[0].Repeat, "stick raw Y negative is Up; a new direction fires immediately");
    f.Axes[5] = -1; s = Step(t0 + 7.3); Check(s.Count(p => p.Kind == PadSignalKind.Navigate) == 1 && s.First(p => p.Kind == PadSignalKind.Navigate).Direction == PadDirection.Left && s.First(p => p.Kind == PadSignalKind.Navigate).Button == "dpadLeft", "the d-pad wins over the stick");
    f.Axes[5] = 0; f.Axes[0] = 0; f.Axes[1] = 0; Step(t0 + 7.4);
    // Pan
    f.Axes[4] = -1; Step(t0 + 8); s = Step(t0 + 8.05);
    Check(s.Count == 1 && s[0].Kind == PadSignalKind.Pan && Near(s[0].PanY, 900 * .05, 1e-6) && Near(s[0].PanX, 0), "right stick up pans +Y at 900 px/s × frame time");
    s = Step(t0 + 9); Check(s.Count == 1 && Near(s[0].PanY, 900 * .1, 1e-6), "a long frame pans at most MaxFrame (.1 s)");
    f.Axes[4] = -.2f; Equal(Step(t0 + 9.05).Count, 0, "right stick inside the deadzone does not pan");
    f.Axes[4] = 0;
    // Reset
    f.Buttons[1] = true; reader.Reset(); Equal(Step(t0 + 10).Count, 0, "after Reset a held button does not fire");
    f.Buttons[1] = false; Step(t0 + 10.1); f.Buttons[1] = true; Equal(Step(t0 + 10.2).Count, 1, "it fires after release and press");
    Equal(reader.Step(null, 1).Count, 0, "a null frame resets and fires nothing");

    // ---- Plans: default bindings ----------------------------------------------------------
    var settings = new OriginalPlayerSettings();
    var pad = settings.GamepadBindings; var keys = settings.KeyBindings;
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "south")), "Key:Return Submit", "south: Play (Enter) then activate the focused control");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "east")), "Key:Escape Cancel Back", "east: Escape, then close a list, then the visible Back");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "west")), "Key:E", "west: End turn (E)");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "north")), "Key:D", "north: Deck (D)");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "start")), "Menu", "start: go to the menu button");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "leftTrigger")), "Key:F", "LT: Crimson flask (F)");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "rightTrigger")), "Key:G", "RT: Azure flask (G)");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "leftStick")), "Key:H", "left stick press: utility flask (H)");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "leftShoulder")), "Key:LeftArrow", "LB: previous enemy");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "rightShoulder")), "Key:RightArrow", "RB: next enemy");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "select")), "", "select is unbound by default");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "dpadUp")), "", "a d-pad press plans nothing (navigation handles it)");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "bogus")), "", "an unknown button plans nothing");
    Equal(Steps(OriginalGamepadNavigation.Plan(null, null, "west")), "Key:E", "null bindings mean defaults");
    // Rebinding is respected: pad and keyboard.
    Check(settings.TryBind("endTurn", "Z", out _), "rebind End turn key to Z");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "west")), "Key:Z", "the pad sends the player's keyboard key for the action");
    Check(!settings.TryBindGamepad("endTurn", "rightShoulder", out var holder) && holder == "targetNext", "a held pad button is refused (same rule as keys)");
    Check(settings.TryBindGamepad("targetNext", "select", out _) && settings.TryBindGamepad("endTurn", "rightShoulder", out _), "free RB, then bind End turn to RB");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "rightShoulder")), "Key:Z", "RB now ends the turn");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "west")), "", "X no longer does anything");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "select")), "Key:RightArrow", "Select now picks the next enemy");
    Check(settings.TryBindGamepad("combatPlay", "north", out var playHolder) == false && playHolder == "combatDeck", "Play cannot take Y while Deck holds it");
    Check(settings.TryBindGamepad("combatDeck", "leftStick", out _) == false, "left stick is held by flask 3");
    settings.BindGamepadSwapping("combatPlay", "north");
    Equal(OriginalGamepadNavigation.ConfirmButton(pad), "north", "Confirm follows Play's binding");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "north")), "Key:Return Submit", "Y now plays and confirms");
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "south")), "Key:D", "A took Deck in the swap");
    settings.ResetGamepadBindings(); settings.ResetKeyBindings();
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "south")), "Key:Return Submit", "reset restores the defaults");
    var reloaded = OriginalPlayerSettings.FromJson(new OriginalPlayerSettings().ToJson(), out _);
    Equal(Steps(OriginalGamepadNavigation.Plan(reloaded.GamepadBindings, reloaded.KeyBindings, "west")), "Key:E", "a saved and reloaded settings object plans the same");

    // ---- Plans: navigation ------------------------------------------------------------------
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Up, "dpadUp")), "Key:PageUp Move:Up", "d-pad up: map scroll (Page Up) where the map has focus, else move focus");
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Up, "dpadUp", true, true)), "Key:PageUp Move:Up", "map scroll repeats while held");
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Left, "dpadLeft")), "Move:Left", "d-pad left only moves focus by default");
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Down, null)), "Move:Down", "the stick only moves focus");
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.None, null)), "", "no direction, no steps");
    settings.TryBindGamepad("endTurn", "dpadLeft", out _);
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Left, "dpadLeft")), "Key:E Move:Left", "a d-pad bound to End turn sends E first");
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Left, "dpadLeft", true, true)), "", "holding it never ends a second turn");
    Equal(Steps(OriginalGamepadNavigation.PlanNavigate(pad, keys, PadDirection.Left, "dpadLeft", true, false)), "Move:Left", "when the first press only moved focus, repeats keep moving");
    settings.ResetGamepadBindings();
    settings.TryBindGamepad("cancel", "select", out _);
    Equal(Steps(OriginalGamepadNavigation.Plan(pad, keys, "select")), "Key:Escape Cancel Back", "cancel follows its rebinding");
    settings.ResetGamepadBindings();

    // ---- Helpers -----------------------------------------------------------------------------
    Check(OriginalGamepadNavigation.IsActivationKey("Return") && OriginalGamepadNavigation.IsActivationKey("Space") && !OriginalGamepadNavigation.IsActivationKey("E"), "activation keys are Enter / Space only");
    foreach (var name in new[] { "back", "close", "native-pile-back", "solo-close", "native-discard-cancel" }) Check(OriginalGamepadNavigation.IsBackControl(name), name + " is a back control");
    foreach (var name in new[] { "native-shop-leave", "background", "native-play", "", null, "cancellation" }) Check(!OriginalGamepadNavigation.IsBackControl(name), (name ?? "null") + " is not a back control");
    Equal(string.Join(",", OriginalGamepadNavigation.MenuControls), "native-menu,menu", "menu controls: native run first, then the campaign Menu");
    var bound = OriginalGamepadNavigation.Bound(pad, OriginalGamepadNavigation.Actions);
    Check(bound.Count == OriginalGamepad.DefaultBindings.Count && bound.All(b => OriginalGamepad.DefaultBindings[b.Action] == b.Button), "Bound lists every default binding");

    // ---- Spatial focus -----------------------------------------------------------------------
    // A 3 × 2 grid, 100 × 40 cells with 20 px gaps, in tree (row) order.
    var grid = new List<PadRect>();
    for (var r = 0; r < 2; r++) for (var c = 0; c < 3; c++) grid.Add(new PadRect(c * 120, r * 60, 100, 40));
    Equal(OriginalGamepadNavigation.Next(grid[0], grid, PadDirection.Right, 0), 1, "grid: right from 0 → 1");
    Equal(OriginalGamepadNavigation.Next(grid[1], grid, PadDirection.Down, 1), 4, "grid: down from 1 → 4 (same column)");
    Equal(OriginalGamepadNavigation.Next(grid[4], grid, PadDirection.Left, 4), 3, "grid: left from 4 → 3");
    Equal(OriginalGamepadNavigation.Next(grid[5], grid, PadDirection.Up, 5), 2, "grid: up from 5 → 2");
    Equal(OriginalGamepadNavigation.Next(grid[2], grid, PadDirection.Right, 2), -1, "grid: right edge → -1 (no wrap)");
    Equal(OriginalGamepadNavigation.Next(grid[0], grid, PadDirection.Up, 0), -1, "grid: top edge → -1");
    Equal(OriginalGamepadNavigation.Next(grid[0], grid, PadDirection.None, 0), -1, "no direction → -1");
    // Hand of cards (overlapping) with an End turn button below-right: right stays in the row.
    var hand = new List<PadRect> { new PadRect(0, 500, 120, 170), new PadRect(90, 500, 120, 170), new PadRect(180, 500, 120, 170), new PadRect(215, 700, 80, 40) };
    Equal(OriginalGamepadNavigation.Next(hand[1], hand, PadDirection.Right, 1), 2, "hand: right moves to the next card even when overlapping");
    Equal(OriginalGamepadNavigation.Next(hand[2], hand, PadDirection.Down, 2), 3, "hand: down reaches the button below");
    Equal(OriginalGamepadNavigation.Next(hand[3], hand, PadDirection.Up, 3), 2, "hand: up from the button returns to the card above it");
    // Aligned beats nearer diagonal.
    var aligned = new List<PadRect> { new PadRect(0, 0, 100, 40), new PadRect(110, 30, 100, 40), new PadRect(0, 100, 100, 40) };
    Equal(OriginalGamepadNavigation.Next(aligned[0], aligned, PadDirection.Down, 0), 2, "down prefers the control directly below over a nearer diagonal");
    // Map nodes: up from the current node picks the nearest node on the next floor, horizontally closest.
    var map = new List<PadRect> { new PadRect(200, 400, 40, 40), new PadRect(80, 300, 40, 40), new PadRect(230, 300, 40, 40), new PadRect(360, 300, 40, 40), new PadRect(210, 200, 40, 40) };
    Equal(OriginalGamepadNavigation.Next(map[0], map, PadDirection.Up, 0), 2, "map: up picks the next-floor node above, not the floor beyond");
    Equal(OriginalGamepadNavigation.Next(map[2], map, PadDirection.Left, 2), 1, "map: left along a floor");
    // Ties keep tree order; invalid rectangles are skipped; self is skipped.
    var tie = new List<PadRect> { new PadRect(0, 0, 10, 10), new PadRect(20, -10, 10, 10), new PadRect(20, 10, 10, 10) };
    Equal(OriginalGamepadNavigation.Next(tie[0], tie, PadDirection.Right, 0), 1, "equal scores keep tree order");
    var invalid = new List<PadRect> { new PadRect(0, 0, 10, 10), new PadRect(20, 0, 0, 10), new PadRect(double.NaN, 0, 10, 10), new PadRect(40, 0, 10, 10) };
    Equal(OriginalGamepadNavigation.Next(invalid[0], invalid, PadDirection.Right, 0), 3, "zero-size and NaN rectangles are never targets");
    Equal(OriginalGamepadNavigation.Next(grid[0], grid, PadDirection.Right), 1, "without a self index the origin rect itself is not chosen (centre must move)");
    // Entry
    var view = new PadRect(0, 0, 400, 300);
    var entry = new List<PadRect> { new PadRect(10, 400, 50, 20), new PadRect(200, 52, 50, 20), new PadRect(20, 50, 50, 20), new PadRect(300, 10, 50, 20) };
    Equal(OriginalGamepadNavigation.Entry(entry, view), 3, "entry: the top-most visible control");
    entry.RemoveAt(3);
    Equal(OriginalGamepadNavigation.Entry(entry, view), 2, "entry: same row (within 8 px) → left-most");
    Equal(OriginalGamepadNavigation.Entry(new List<PadRect> { new PadRect(0, 900, 10, 10) }, view), 0, "entry: off-screen controls are used when nothing is visible");
    Equal(OriginalGamepadNavigation.Entry(new List<PadRect>(), view), -1, "entry: nothing focusable → -1");

    // ---- Consistency with the keyboard list and the settings screen --------------------------
    var keyBindingsSource = Read("Unity/Assets/AshenSpire/Runtime/Presentation/OriginalKeyBindings.cs");
    string[] List(string field) => Regex.Matches(Regex.Match(keyBindingsSource, field + @" = \{(?<v>[^}]*)\}").Groups["v"].Value, "\"(?<a>[^\"]+)\"").Select(m => m.Groups["a"].Value).ToArray();
    var keyboardActions = OriginalGamepad.PadOnlyActions.Concat(List("MapActions")).Concat(List("CombatActions").Where(a => !a.StartsWith("card"))).ToArray();
    Check(keyboardActions.Length > 10 && keyboardActions.SequenceEqual(OriginalGamepadNavigation.Actions), "pad actions = pad-only + keyboard map + combat actions (no card slots), same order");
    Check(OriginalGamepad.DefaultBindings.Keys.All(OriginalGamepadNavigation.Actions.Contains), "every default pad binding is a resolvable action");
    Check(OriginalGamepadNavigation.Actions.Where(a => !OriginalGamepad.PadOnlyActions.Contains(a)).All(a => OriginalGamepad.KeyName(a, null) != null), "every non pad-only action has a default key to dispatch");
    Check(OriginalGamepadNavigation.RepeatingActions.All(OriginalGamepadNavigation.Actions.Contains), "repeating actions are pad actions");

    // ---- Unity wiring --------------------------------------------------------------------------
    var inputManager = Read("Unity/ProjectSettings/InputManager.asset");
    var axes = Regex.Split(inputManager, @"\n  - serializedVersion: 3\n").Skip(1).Select(a => a + "\n").ToArray();
    for (var n = 1; n <= 10; n++)
    {
        var entryText = axes.FirstOrDefault(a => Regex.IsMatch(a, @"m_Name: GamepadAxis" + n + @"\n"));
        Check(entryText != null && entryText.Contains("type: 2\n") && entryText.Contains("axis: " + (n - 1) + "\n") && entryText.Contains("joyNum: 0\n") && entryText.Contains("dead: 0\n") && entryText.Contains("invert: 0\n") && entryText.Contains("sensitivity: 1\n"),
            "InputManager defines GamepadAxis" + n + " as raw joystick axis " + n + " of every pad");
    }
    Check(!axes.Any(a => Regex.IsMatch(a, @"m_Name: (Horizontal|Vertical)\n") && a.Contains("type: 2\n")), "UI Toolkit's Horizontal/Vertical no longer read the stick (GamepadNavigator owns the pad)");
    Check(!axes.Any(a => Regex.IsMatch(a, @"m_Name: (Submit|Cancel)\n") && a.Contains("joystick button")), "Submit/Cancel no longer read pad buttons (rebinding would otherwise be bypassed)");
    Check(axes.Any(a => a.Contains("m_Name: Submit\n") && a.Contains("positiveButton: return\n")) && axes.Any(a => a.Contains("m_Name: Cancel\n") && a.Contains("positiveButton: escape\n")), "keyboard Submit/Cancel are kept");
    Check(Regex.IsMatch(Read("Unity/ProjectSettings/ProjectSettings.asset"), @"activeInputHandler: 0\b"), "the project stays on the legacy Input Manager");
    Check(!Read("Unity/Packages/manifest.json").Contains("com.unity.inputsystem"), "the Input System package is not added");
    var driver = Read("Unity/Assets/AshenSpire/Runtime/Presentation/GamepadDriver.cs");
    Check(driver.Contains("KeyCode.JoystickButton0") && driver.Contains("GamepadFrame.ButtonCount") && Enumerable.Range(1, 10).All(n => driver.Contains("\"GamepadAxis" + n + "\"")), "GamepadDriver reads joystick buttons 0..19 and every GamepadAxis");
    Check(driver.Contains("GamepadLayout.ForPlatform"), "GamepadDriver picks the platform layout");
    var navigator = Read("Unity/Assets/AshenSpire/Runtime/Presentation/GamepadNavigator.cs");
    Check(navigator.Contains("OriginalGamepadNavigation.Plan(") && navigator.Contains("OriginalGamepadNavigation.PlanNavigate(") && navigator.Contains("OriginalGamepadNavigation.Next(") && navigator.Contains("KeyDownEvent.GetPooled"), "GamepadNavigator runs the domain plans and dispatches keys through UI Toolkit");
    Check(!Regex.IsMatch(navigator, @"\bInput\."), "GamepadNavigator never reads Input directly (GamepadDriver does)");
    var controller = Read("Unity/Assets/AshenSpire/Runtime/Application/RunController.cs");
    Check(controller.Contains("AddComponent<GamepadDriver>()") && controller.Contains("gamepad.Navigator = _view.Gamepad") && controller.Contains("gamepad.Navigator = null"), "RunController attaches the driver and detaches it on disable");
    var settingsView = Read("Unity/Assets/AshenSpire/Runtime/Presentation/CampaignView.PlayerSettings.cs");
    Check(settingsView.Contains("Gamepad.Capture = pressed =>") && settingsView.Contains("TryBindGamepad(id, pressed"), "Settings rebinding captures a pad press");
    foreach (var file in new[] { "OriginalGamepadInput.cs", "OriginalGamepadNavigation.cs" })
    {
        var source = Read("Unity/Assets/AshenSpire/Runtime/Domain/Original/" + file);
        Check(!Regex.IsMatch(source, @"using UnityEngine|UnityEngine\.\w+\("), file + " stays engine-free");
        Check(File.Exists(Path.Combine(root, "Unity/Assets/AshenSpire/Runtime/Domain/Original/" + file + ".meta")), file + " has a .meta");
    }
    foreach (var file in new[] { "GamepadDriver.cs", "GamepadNavigator.cs" })
        Check(File.Exists(Path.Combine(root, "Unity/Assets/AshenSpire/Runtime/Presentation/" + file + ".meta")), file + " has a .meta");
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: unexpected exception: " + error);
}

Console.WriteLine(checks + " passed, " + failures + " failed");
return failures == 0 ? 0 : 1;

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "Unity", "ProjectSettings"))) return dir.FullName;
    return ".";
}
