using AshenSpire.Application;
var passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; }
foreach (var first in Enum.GetValues<InterruptionSource>())
{
    var state = new InterruptionState();
    Check(!state.IsInterrupted && !state.CanReturn && !state.TryReturn(), "active input cannot return twice");
    Check(!state.Set(first, false) && !state.IsInterrupted, "initial resume callback is harmless");
    Check(state.Set(first, true) && state.IsInterrupted && !state.CanReturn, "background blocks input");
    Check(!state.Set(first, true) && !state.TryReturn(), "duplicate background cannot resume");
    foreach (var second in Enum.GetValues<InterruptionSource>().Where(x => x != first))
    {
        state.Set(second, true);
        state.Set(first, false);
        Check(state.IsInterrupted && !state.CanReturn && !state.TryReturn(), "remaining source prevents early return");
        state.Set(first, true);
        state.Set(second, false);
        Check(!state.CanReturn, "source order does not matter");
    }
    Check(state.Set(first, false) && state.IsInterrupted && state.CanReturn, "foreground waits for explicit return");
    Check(!state.Set(first, false) && state.CanReturn, "duplicate foreground preserves cover");
    state.Set(first, true);
    Check(!state.TryReturn(), "reinterruption before return blocks button");
    state.Set(first, false);
    Check(state.TryReturn() && !state.IsInterrupted && !state.CanReturn, "return clears cover only after all clear");
    Check(!state.TryReturn(), "repeated return is harmless");
}
Console.WriteLine($"Interruption: {passed} checks passed");
