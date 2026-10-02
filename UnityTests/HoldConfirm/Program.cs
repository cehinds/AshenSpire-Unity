// Hold-to-confirm checks (US-13.3): confirmation policy data and HoldConfirmState.
// Run from the repository root: dotnet run --project UnityTests/HoldConfirm [-- <repo root>]
using System.Text.RegularExpressions;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
var checks = 0;
var failures = 0;
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
byte[] Bytes(string relative) => File.ReadAllBytes(Path.Combine(root, relative));

try
{
    // ---- Data: the copy, its Resources mirror and the import list -----------------------
    const string reference = "content/framework/confirmationPolicies.json";
    const string authored = "GameContent/Unity/Original/confirmation-policies.json";
    const string mirror = "Unity/Assets/AshenSpire/Resources/Original/confirmation-policies.json";
    Check(Bytes(authored).SequenceEqual(Bytes(reference)), "GameContent copy is byte-identical to the original " + reference);
    Check(Bytes(mirror).SequenceEqual(Bytes(authored)), "Resources/Original mirror matches GameContent");
    Check(File.Exists(Path.Combine(root, mirror + ".meta")) && Regex.IsMatch(Read(mirror + ".meta"), @"guid: [0-9a-f]{32}"), "Resources mirror has a Unity .meta with a guid");
    Check(Regex.IsMatch(Read("Unity/Assets/AshenSpire/Editor/BuildTools.cs"), "\"confirmation-policies\""), "BuildTools.ImportContent copies and validates confirmation-policies");
    Equal(ConfirmationPolicy.ResourcePath, "Original/confirmation-policies", "runtime resource path names the mirror");

    // ---- Every DESTRUCTIVE action is classified -----------------------------------------
    var json = Read(authored);
    var policy = new ConfirmationPolicy(json);
    var raw = JObject.Parse(json);
    var levels = raw["policies"].ToDictionary(p => (string)p["id"], p => (string)p["level"]);
    var actions = raw["actions"].OfType<JObject>().ToList();
    Equal(policy.Actions.Count, actions.Count, "every action in the data is loaded");
    foreach (var row in actions)
    {
        var id = (string)row["id"];
        var destructive = levels[(string)row["policyId"]] == "DESTRUCTIVE";
        Equal(policy.RequiresHold(id), destructive, id + (destructive ? " requires hold-to-confirm" : " commits without a hold"));
        Equal(policy.Level(id).ToString().ToUpperInvariant(), levels[(string)row["policyId"]], id + " level matches its policy");
    }
    var expected = new[] { "action.abandonRun", "action.deleteSave", "action.loadSlot", "action.overwriteSave", "action.quitWithoutSaving", "action.removeCard" };
    var destructiveIds = actions.Where(a => (bool)a["destructive"]).Select(a => (string)a["id"]).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    Check(expected.All(destructiveIds.Contains), "known destructive actions are flagged in data");
    Check(policy.DestructiveActions.SequenceEqual(destructiveIds), "DestructiveActions equals every action flagged destructive (" + destructiveIds.Length + ")");
    foreach (var id in new[] { ConfirmationPolicy.RemoveCard, ConfirmationPolicy.OverwriteSave, ConfirmationPolicy.DeleteSave })
        Check(policy.RequiresHold(id), "wired action " + id + " is DESTRUCTIVE");
    Check(Throws(() => policy.Level("action.unknown")), "an unruled action id throws instead of skipping confirmation");
    Check(Throws(() => new ConfirmationPolicy(json.Replace("\"policyId\": \"policy.cardRemove\", \"destructive\": true", "\"policyId\": \"policy.cardRemove\", \"destructive\": false"))), "destructive flag disagreeing with its policy is rejected");
    Check(Throws(() => new ConfirmationPolicy(json.Replace("\"policyId\": \"policy.deleteSave\"", "\"policyId\": \"policy.missing\""))), "an action bound to an unknown policy is rejected");
    Check(Throws(() => new ConfirmationPolicy(json.Replace("\"level\": \"DESTRUCTIVE\" }", "\"level\": \"LOUD\" }"))), "an unknown level is rejected");

    // ---- Hold duration matches the original dial ----------------------------------------
    var balance = Read("src/content/balance.js");
    var normal = Regex.Match(balance, @"holdConfirm: \{\s*def: 'normal',\s*steps: \{[^}]*normal: (?<v>\d+)");
    Check(normal.Success, "HTML balance.ui.holdConfirm default 'normal' step found");
    Equal(HoldConfirmState.DefaultHoldMs, int.Parse(normal.Groups["v"].Value), "DefaultHoldMs equals the original normal hold");
    Equal((int)JObject.Parse(Read("Unity/Assets/AshenSpire/Resources/Feel/feel-profile.json"))["Input"]["HoldConfirmNormalMs"], HoldConfirmState.DefaultHoldMs, "feel-profile HoldConfirmNormalMs (used by HoldConfirmButton) equals DefaultHoldMs");

    // ---- Early release never commits ----------------------------------------------------
    foreach (var releaseAt in new[] { 0.0, 1, 300, 599.9 })
    {
        var s = new HoldConfirmState();
        s.Begin(1000);
        Check(!s.Tick(1000 + releaseAt), "tick before threshold does not commit (" + releaseAt + " ms)");
        Check(!s.Release(1000 + releaseAt), "release at " + releaseAt + " ms aborts");
        Check(!s.Holding && !s.Fired && s.Commits == 0, "aborted hold leaves nothing committed (" + releaseAt + " ms)");
        Check(!s.Tick(5000), "time passing after an abort never commits (" + releaseAt + " ms)");
        Equal(s.Progress(5000), 0.0, "aborted hold shows an empty bar");
    }
    var dragged = new HoldConfirmState();
    dragged.Begin(0); dragged.Cancel();
    Check(!dragged.Tick(10000) && !dragged.Release(10000) && dragged.Commits == 0, "cancel (drag/leave) never commits");
    var progress = new HoldConfirmState();
    progress.Begin(0);
    Equal(progress.Progress(300), 0.5, "progress is half way at 300 of 600 ms");
    Equal(progress.Progress(-50), 0.0, "progress clamps at 0");

    // ---- A completed hold commits exactly once ------------------------------------------
    var held = new HoldConfirmState();
    held.Begin(0);
    Check(held.Tick(600), "hold commits when the bar fills");
    Check(!held.Tick(601) && !held.Tick(5000), "further ticks do not commit again");
    Check(!held.Release(700), "the trailing release does not commit again");
    Check(!held.Tap(710) && !held.Tap(720), "trailing clicks do not commit or arm a fired control");
    held.Begin(800);
    Check(!held.Holding && !held.Tick(5000), "a new press on a fired control does nothing until reset");
    Equal(held.Commits, 1, "completed hold committed exactly once");
    Equal(held.Progress(900), 1.0, "fired control shows a full bar");
    var atRelease = new HoldConfirmState();
    atRelease.Begin(0);
    Check(atRelease.Release(650), "releasing after the threshold without an intervening tick commits");
    Check(!atRelease.Tick(700) && !atRelease.Release(700), "and only once");
    Equal(atRelease.Commits, 1, "release-path commits exactly once");

    // ---- Tap twice (keyboard / assistive / quick-click path) ----------------------------
    var tapped = new HoldConfirmState();
    Check(!tapped.Tap(0), "first tap only arms");
    Check(tapped.IsArmed(10) && tapped.Commits == 0, "armed after one tap, nothing committed");
    Check(tapped.Tap(500), "second tap within the window commits");
    Check(!tapped.Tap(600) && tapped.Commits == 1, "a third tap does not commit again");
    var expired = new HoldConfirmState();
    expired.Tap(0);
    Check(!expired.IsArmed(HoldConfirmState.DefaultArmWindowMs + 1), "arming expires after the window");
    Check(!expired.Tap(HoldConfirmState.DefaultArmWindowMs + 1) && expired.Commits == 0, "a late second tap re-arms instead of committing");
    Check(expired.Tap(HoldConfirmState.DefaultArmWindowMs + 100), "then a prompt tap commits");
    var disarmed = new HoldConfirmState();
    disarmed.Tap(0); disarmed.Disarm();
    Check(!disarmed.Tap(10) && disarmed.Commits == 0, "disarm (focus lost) requires two fresh taps");
    var quickClick = new HoldConfirmState();
    quickClick.Begin(0); quickClick.Release(80); quickClick.Tap(80);
    Check(quickClick.Commits == 0 && quickClick.IsArmed(100), "a quick pointer click (press, early release, click) arms without committing");
    quickClick.Begin(400); quickClick.Release(470);
    Check(quickClick.Tap(470) && quickClick.Commits == 1, "a second quick click confirms");

    // ---- Re-arm after reset -------------------------------------------------------------
    var reused = new HoldConfirmState();
    reused.Begin(0); reused.Tick(600); reused.Reset();
    Check(!reused.Fired && !reused.Holding && !reused.IsArmed(700) && reused.Progress(700) == 0, "reset returns to idle");
    reused.Begin(1000);
    Check(!reused.Release(1200), "after reset an early release still aborts");
    reused.Begin(2000);
    Check(reused.Tick(2600) && reused.Commits == 2, "after reset a completed hold commits again, once");
    reused.Reset();
    Check(!reused.Tap(3000) && reused.Tap(3100) && reused.Commits == 3, "after reset tap-twice commits again");

    // ---- Hold path off (dial 'off' = 0 ms) ----------------------------------------------
    var off = new HoldConfirmState(0);
    off.Begin(0);
    Check(!off.HoldEnabled && !off.Holding && !off.Tick(10000) && !off.Release(10000), "with the hold off, pressing never commits");
    Check(!off.Tap(0) && off.Tap(10), "with the hold off, tap-twice still confirms");
    Check(Throws(() => new HoldConfirmState(600, 0)), "a zero arm window is rejected");

    // ---- Presentation wiring (source contract; the compiled UI is checked by playtests) ---
    var runPanel = Read("Unity/Assets/AshenSpire/Runtime/Presentation/OriginalRunPanel.cs");
    Check(Regex.IsMatch(runPanel, @"Destructive\(""native-remove-"" \+ id,[^;]*ConfirmationPolicy\.RemoveCard"), "merchant card removal routes through the hold-to-confirm button");
    var slotPanel = Read("Unity/Assets/AshenSpire/Runtime/Presentation/OriginalSlotPanel.cs");
    Check(slotPanel.Contains("ConfirmationPolicy.OverwriteSave") && slotPanel.Contains("ConfirmationPolicy.DeleteSave"), "slot overwrite and delete name their policy actions");
    Check(Regex.IsMatch(slotPanel, @"_control\(""native-slot-confirm"",[^;]*null,") && slotPanel.Contains("HoldConfirmButton.Bind(commit, actionId, confirmed"), "native-slot-confirm has no plain click and commits only through HoldConfirmButton");
    var button = Read("Unity/Assets/AshenSpire/Runtime/Presentation/HoldConfirmButton.cs");
    Check(button.Contains("Policy.RequiresHold(actionId)") && button.Contains("HoldConfirmNormalMs"), "HoldConfirmButton reads the policy and the feel-profile duration");
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: " + error);
}

if (failures > 0) { Console.Error.WriteLine("HoldConfirm: " + failures + " failed, " + checks + " passed"); return 1; }
Console.WriteLine("HoldConfirm: " + checks + " checks passed");
return 0;
