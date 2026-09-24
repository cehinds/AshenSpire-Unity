// Program.cs — F07 feel profile checks. Run from anywhere:
//   dotnet run --project UnityTests/Feel                    (checks; exit 1 on failure)
//   dotnet run --project UnityTests/Feel -- --print-sources  (markdown source table for docs/Unity-Feel.md)
// Every cited value is re-read from the HTML reference (src/**, styles/**) at test time, so a
// retune there without a matching feel-profile.json edit fails here, naming the key and line.
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AshenSpire.Domain;

var root = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "SPEC.md"))
    ? Directory.GetCurrentDirectory()
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string Read(string rel) => File.ReadAllText(Path.Combine(root, rel));
const string ProfilePath = "Unity/Assets/AshenSpire/Resources/Feel/feel-profile.json";
const string DocPath = "docs/Unity-Feel.md";
var inv = CultureInfo.InvariantCulture;

var passed = 0; var failures = new List<string>();
void Check(bool ok, string name) { if (ok) { Console.WriteLine("PASS: " + name); passed++; } else { Console.WriteLine("FAIL: " + name); failures.Add(name); } }
bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) <= eps;

// ---- load -------------------------------------------------------------------------------------
var json = Read(ProfilePath);
var options = new JsonSerializerOptions { IncludeFields = true };
FeelProfile profile = null;
try { profile = JsonSerializer.Deserialize<FeelProfile>(json, options); } catch (Exception e) { Console.WriteLine(e.Message); }
Check(profile != null, "feel-profile.json loads");
if (profile == null) return Finish();
var errors = profile.Validate();
foreach (var e in errors) Console.WriteLine("  " + e);
Check(errors.Count == 0, "feel-profile.json validates (" + profile.Motions.Length + " motions)");

// Every public field of every serialized type is present in the JSON (JsonUtility would silently default a missing one).
var missing = new List<string>();
void Walk(JsonElement element, Type type, string path)
{
    if (type.IsArray)
    {
        if (element.ValueKind != JsonValueKind.Array) { missing.Add(path + " (not an array)"); return; }
        var i = 0;
        foreach (var item in element.EnumerateArray()) Walk(item, type.GetElementType(), path + "[" + i++ + "]");
        return;
    }
    if (type.Namespace != "AshenSpire.Domain" || type.IsEnum) return;
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        if (!element.TryGetProperty(field.Name, out var child)) { missing.Add(path + "." + field.Name); continue; }
        Walk(child, field.FieldType, path + "." + field.Name);
    }
}
using (var doc = JsonDocument.Parse(json)) Walk(doc.RootElement, typeof(FeelProfile), "$");
foreach (var m in missing.Take(10)) Console.WriteLine("  missing " + m);
Check(missing.Count == 0, "every model field is present in feel-profile.json");
Check(FeelProfile.RequiredMotions.All(id => profile.Motion(id) != null) && profile.Motions.Length == FeelProfile.RequiredMotions.Length, "every required motion is present, and nothing unlisted");

// ---- curves: the browser algorithm ------------------------------------------------------------
// Reference by plain bisection on x(t) (a different algorithm from the Newton path under test).
double RefBezier(double x1, double y1, double x2, double y2, double x)
{
    double Bez(double a, double b, double t) => 3 * a * (1 - t) * (1 - t) * t + 3 * b * (1 - t) * t * t + t * t * t;
    double lo = 0, hi = 1;
    for (var i = 0; i < 200; i++) { var mid = (lo + hi) / 2; if (Bez(x1, x2, mid) < x) lo = mid; else hi = mid; }
    return Bez(y1, y2, (lo + hi) / 2);
}
Check(Near(FeelCurves.Ease.Evaluate(0.5), 0.8024033877, 1e-3), "ease(0.5) = 0.8024 (CSS)");
Check(Near(FeelCurves.EaseIn.Evaluate(0.5), 0.3153568, 1e-3), "ease-in(0.5) = 0.3154 (CSS)");
Check(Near(FeelCurves.EaseOut.Evaluate(0.5), 0.6846432, 1e-3), "ease-out(0.5) = 0.6846 (CSS)");
Check(Near(FeelCurves.EaseInOut.Evaluate(0.5), 0.5, 1e-3), "ease-in-out(0.5) = 0.5 (CSS)");
Check(Near(FeelCurves.Linear.Evaluate(0.37), 0.37, 1e-9), "linear is identity");
Check(FeelCurves.Parse("ease").Equals(new CubicBezier(0.25, 0.1, 0.25, 1)) && FeelCurves.Parse("ease-in").Equals(new CubicBezier(0.42, 0, 1, 1))
    && FeelCurves.Parse("ease-out").Equals(new CubicBezier(0, 0, 0.58, 1)) && FeelCurves.Parse("ease-in-out").Equals(new CubicBezier(0.42, 0, 0.58, 1))
    && FeelCurves.Parse("").Equals(FeelCurves.Ease), "named easings map to the CSS bezier constants (empty = ease)");
Check(FeelCurves.Parse("cubic-bezier(.25,.8,.35,1)").Equals(FeelCurves.Parse("cubic-bezier(0.25, 0.8, 0.35, 1)")), "cubic-bezier() parses with or without leading zeros");
Check(!FeelCurves.TryParse("cubic-bezier(1.2, 0, 0, 1)", out _) && !FeelCurves.TryParse("steps(4)", out _), "invalid x control points and unsupported functions are refused");
var worst = 0.0;
foreach (var easing in profile.Motions.Select(m => m.Easing).Distinct().Concat(new[] { "ease", "ease-in", "ease-out", "ease-in-out" }))
{
    var c = FeelCurves.Parse(easing);
    for (var x = 0.0; x <= 1.0001; x += 0.01) worst = Math.Max(worst, Math.Abs(c.Evaluate(x) - RefBezier(c.X1, c.Y1, c.X2, c.Y2, Math.Min(1, x))));
}
Check(worst < 1e-3, "every profile easing matches a reference bezier solve within 1e-3 at 101 points (worst " + worst.ToString("E2", inv) + ")");
Check(FeelCurves.Parse("cubic-bezier(0.2, 0.9, 0.3, 1)").Evaluate(0.5) > 0.9, "num-pop curve overshoots early (y(0.5) > 0.9)");
Check(Near(FeelCurves.Ease.Evaluate(-1), 0) && Near(FeelCurves.Ease.Evaluate(2), 1), "progress outside [0, 1] clamps");

// Keyframe sampling: the timing function eases each interval (CSS), not the whole animation.
var pop = profile.Resolve("damageNumber.pop", new FeelSettings());
var popCurve = FeelCurves.Parse("cubic-bezier(0.2, 0.9, 0.3, 1)");
Check(Near(pop.Sample("scale", 0.16), 1.22, 1e-6) && Near(pop.Sample("y", 1), -48, 1e-6) && Near(pop.Sample("opacity", 0), 0, 1e-6), "num-pop keys hit exactly at their offsets");
Check(Near(pop.Sample("y", 0.65), -2 + (-48 + 2) * popCurve.Evaluate((0.65 - 0.3) / 0.7), 1e-5), "keyframe interval is eased locally (num-pop y at 65%)");
Check(Near(pop.Sample("opacity", 0.5), 1, 1e-9), "opacity holds between its own keys (16%..70%)");
var shake = profile.Resolve("screen.shake", new FeelSettings());
var shakePeak = Enumerable.Range(0, 201).Select(i => Math.Max(Math.Abs(shake.Sample("x", i / 200.0)), Math.Abs(shake.Sample("y", i / 200.0)))).Max();
Check(Near(shakePeak, profile.Thresholds.ShakeMaxPx, 1e-9), "screen shake never exceeds ShakeMaxPx (SPEC §7.4: ≤4 px)");
var bob = profile.Resolve("idle.bob", new FeelSettings());
Check(bob.Loop && Near(bob.SampleAt("y", 3100 * 2.5), -4, 1e-9), "looping motions wrap (idle bob peak at 2.5 cycles)");

// ---- settings: speed, instant, reduced motion, shake, flashes ---------------------------------
FeelSettings S(string speed = "normal", bool reduced = false, bool shakeOn = true, bool flashes = false) => new FeelSettings { Speed = speed, ReducedMotion = reduced, ScreenShake = shakeOn, ReduceFlashes = flashes };
var normal = profile.Pace(S());
var slow = profile.Pace(S("slow"));
var fast = profile.Pace(S("fast"));
var instant = profile.Pace(S("instant"));
Check(normal.BeatMs == 400 && normal.StepMs == 90 && normal.LungeMs == 260 && normal.WindupMs == 143 && normal.BannerBeatMs == 400, "normal pacing: beat 400, step 90, lunge 260, wind-up 143, banner 400");
Check(fast.BeatMs == 180 && fast.StepMs == 45 && fast.LungeMs == 160 && fast.WindupMs == 88 && fast.BannerBeatMs == 260, "fast pacing: banner beat floors at 260 (fx.js Math.max)");
Check(slow.BeatMs == 700 && slow.StepMs == 140 && slow.LungeMs == 340 && slow.WindupMs == 187, "slow pacing: beat 700, step 140, lunge 340");
Check(instant.Instant && instant.BeatMs == 0 && instant.LungeMs == 0 && instant.QueueStepMs == 80, "instant: no pacing, queued effects still 80 ms apart");
Check(profile.Pace(S("bogus")).SpeedId == "normal", "unknown speed falls back to normal (fx.js setAnimSpeed)");
Check(profile.Pace(S(reduced: true)).Instant, "reduced motion paces like instant (fx.js playTimeline)");
Check(profile.Pace(FeelSettings.FromToggles(false, true)).SpeedId == "fast" && profile.Pace(FeelSettings.FromToggles(false, false)).SpeedId == "normal", "Unity Quick animations toggle maps to the HTML fast speed");
Check(profile.Resolve("actor.lunge", S()).DurationMs == 280 && profile.Resolve("actor.lunge", S("fast")).DurationMs == 172 && profile.Resolve("actor.lunge", S("slow")).DurationMs == 366, "lunge-timed motion scales by speed lunge / 260 (280 → 172 fast, 366 slow)");
Check(profile.Resolve("actor.lunge", S()).LifetimeMs == 280 && profile.Resolve("actor.lunge", S("fast")).LifetimeMs == 172, "lunge lifetime follows the speed");
Check(!profile.Resolve("actor.lunge", S("instant")).Play, "instant plays no actor lunge");
Check(profile.Resolve("hit.recoil", S("fast")).DurationMs == 220 && profile.Resolve("stagger.wobble", S("slow")).DurationMs == 550 && profile.Resolve("damageNumber.pop", S("instant")).DurationMs == 540, "fixed effects never scale with speed (SPEC §7.4)");
var r = profile.ReaverAttack(S());
Check(r.FrameMs == 56 && r.ImpactMs == 1736 && r.TotalMs == 3360, "reaver attack normal: 56 ms frames, impact 1736, total 3360");
var rf = profile.ReaverAttack(S("fast"));
Check(rf.FrameMs == 34 && rf.ImpactMs == 31 * 34 && rf.TotalMs == 60 * 34, "reaver attack fast: round(56 × 160/260) = 34 ms frames");
Check(profile.WatchdogMs(3, S()) == 2000 + 3 * (400 + 260 + 4 * 90) && profile.WatchdogMs(2, S(), 3360) == 2000 + 2 * (400 + 3360 + 360), "watchdog budget matches fx.js");

var reducedPop = profile.Resolve("damageNumber.pop", S(reduced: true));
Check(reducedPop.Id == "damageNumber.reduced" && reducedPop.DurationMs == 260 && reducedPop.Play, "reduced motion swaps the number pop for the 260 ms plain variant");
Check(!reducedPop.Has("y") && !reducedPop.Has("scale") && reducedPop.Has("opacity") && Near(reducedPop.Sample("y", 0.8), 0), "reduced motion: zero movement, opacity kept");
var reducedHit = profile.Resolve("hit.recoilHeavy", S(reduced: true));
Check(reducedHit.Play && !reducedHit.Has("x") && !reducedHit.Has("rotate") && reducedHit.Has("brightness"), "reduced motion keeps the hit flash, drops the recoil");
Check(profile.Motions.All(m => profile.Resolve(m.Id, S(reduced: true)).Tracks.All(tr => !FeelProperty.IsMovement(tr.Property))), "reduced motion: no motion anywhere keeps a movement track");
Check(!profile.Resolve("screen.shake", S(reduced: true)).Play && !profile.Resolve("screen.shake", S(shakeOn: false)).Play && profile.Resolve("screen.shake", S()).Play, "shake honours Screen shake and Reduced motion");
Check(!profile.Resolve("idle.bob", S(reduced: true)).Play && !profile.Resolve("card.hover", S(reduced: true)).Play, "movement-only motions do not play under reduced motion");
Check(profile.Resolve("banner.coop", S(reduced: true)).DurationMs == 500 && profile.Resolve("banner.coop", S(reduced: true)).Has("opacity"), "co-op banner keeps a 500 ms fade under reduced motion (kit.css)");
Check(!profile.Resolve("hit.recoil", S(flashes: true)).Play && profile.Resolve("damageNumber.pop", S(flashes: true)).Play, "Reduce flashes suppresses flash() effects, never the numbers");
Check(profile.Resolve("boss.veil", S(reduced: true)).Play && profile.Resolve("boss.veil", S(reduced: true)).Has("opacity"), "reduced motion keeps opacity-only transitions");

// ---- damage number tiers -------------------------------------------------------------------------
var t = profile.Thresholds; var dn = profile.DamageNumbers;
Check(FeelDamageNumbers.Tier(25, t) == "crit" && FeelDamageNumbers.Tier(24, t) == "heavy" && FeelDamageNumbers.Tier(15, t) == "heavy" && FeelDamageNumbers.Tier(14, t) == "normal" && FeelDamageNumbers.Tier(6, t) == "normal" && FeelDamageNumbers.Tier(5, t) == "small", "number tiers match fx.js dmgClass boundaries");
Check(Near(dn.FontScale("crit"), 4.4 / 2.5, 1e-6) && Near(dn.FontScale("heavy"), 3.4 / 2.5, 1e-6) && Near(dn.FontScale("small"), 0.8, 1e-6) && Near(dn.FontScale("normal"), 1), "crit font scale 1.76, heavy 1.36, small 0.8");

// ---- values equal the HTML reference (parsed at test time) --------------------------------------
var specs = new List<Src>();
string Num(double v) => v.ToString("0.####", inv);
string Motion(string id, Func<FeelMotion, double> f) => Num(f(profile.Motion(id)));
string TrackEnd(string id, string prop) { var k = profile.Motion(id).Tracks.First(x => x.Property == prop).Keys; return Num(k[k.Length - 1].V); }
void Add(string key, string file, string pattern, Func<string> expected, double mul = 1, string kind = "num") => specs.Add(new Src(key, file, pattern, expected, mul, kind));
void Anim(string id, string file, string pattern, double mul = 1)
{
    Add("Motions[" + id + "].DurationMs", file, pattern.Replace("(?<e>", "(?:"), () => Motion(id, m => m.DurationMs), mul);
    Add("Motions[" + id + "].Easing", file, pattern.Contains("(?<e>") ? pattern.Replace("(?<d>", "(?:").Replace("(?<e>", "(?<v>") : pattern.Replace("(?<d>", "(?:"), () => profile.Motion(id).Easing, 1, pattern.Contains("(?<e>") ? "ease" : "ease-default");
}
void Life(string id, string file, string pattern) => Add("Motions[" + id + "].LifetimeMs", file, pattern, () => Motion(id, m => m.LifetimeMs));

const string FX = "src/ui/fx.js", COMBAT = "styles/combat.css", BASE = "styles/base.css", KIT = "styles/kit.css", UI = "styles/ui.css";
foreach (var sp in new[] { "slow", "normal", "fast" })
{
    Add("Speeds[" + sp + "].BeatMs", FX, sp + @": \{ beatMs: (?<v>\d+)", () => Num(profile.Speed(sp).BeatMs));
    Add("Speeds[" + sp + "].StepMs", FX, sp + @": \{ beatMs: \d+, stepMs: (?<v>\d+)", () => Num(profile.Speed(sp).StepMs));
    Add("Speeds[" + sp + "].LungeMs", FX, sp + @": \{[^}]*lungeMs: (?<v>\d+)", () => Num(profile.Speed(sp).LungeMs));
}
Add("Speeds[instant].Instant", FX, @"(?<v>instant): null", () => profile.Speed("instant").Instant ? "instant" : "paced", 1, "str");
Add("DefaultSpeed", "src/ui/screens/settings.js", @"key: 'animSpeed', type: 'choice', def: '(?<v>\w+)'", () => profile.DefaultSpeed, 1, "str");
Add("Pacing.QueueStepMs", FX, @"const STEP_MS = (?<v>\d+);", () => Num(profile.Pacing.QueueStepMs));
Add("Pacing.BannerMinBeatMs", FX, @"schedule\(nextBeat, Math\.max\((?<v>\d+), speed\.beatMs\)\)", () => Num(profile.Pacing.BannerMinBeatMs));
Add("Pacing.WindupLungeFraction", FX, @"Math\.round\(speed\.lungeMs \* (?<v>[\d.]+)\)", () => Num(profile.Pacing.WindupLungeFraction));
Add("Pacing.WatchdogBaseMs", FX, @"const budget = (?<v>\d+) \+", () => Num(profile.Pacing.WatchdogBaseMs));
Add("Pacing.WatchdogStepsPerBeat", FX, @"actorBudgetMs \+ (?<v>\d+) \* speed\.stepMs", () => Num(profile.Pacing.WatchdogStepsPerBeat));
Add("Thresholds.HeavyHitDamage", FX, @"const heavy = parts\.residual >= (?<v>\d+);", () => Num(t.HeavyHitDamage));
Add("Thresholds.HeavyHitDamage", FX, @"if \(amount >= (?<v>\d+)\) return 'dmg heavy'", () => Num(t.HeavyHitDamage));
Add("Thresholds.CritDamage", FX, @"if \(amount >= (?<v>\d+)\) return 'dmg crit'", () => Num(t.CritDamage));
Add("Thresholds.SmallDamageBelow", FX, @"if \(amount < (?<v>\d+)\) return 'dmg small'", () => Num(t.SmallDamageBelow));
Add("Thresholds.ShakeMaxPx", COMBAT, @"@keyframes shake \{[^\n]*25% \{ transform: translate\((?<v>\d+)px", () => Num(t.ShakeMaxPx));
Add("DamageNumbers.BaseFontRem", COMBAT, @"font-weight: 800; font-size: (?<v>[\d.]+)rem;", () => Num(dn.BaseFontRem));
Add("DamageNumbers.SmallFontRem", COMBAT, @"\.float-num\.small \{ font-size: (?<v>[\d.]+)rem", () => Num(dn.SmallFontRem));
Add("DamageNumbers.HeavyFontRem", COMBAT, @"\.float-num\.heavy \{ font-size: (?<v>[\d.]+)rem", () => Num(dn.HeavyFontRem));
Add("DamageNumbers.CritFontRem", COMBAT, @"\.float-num\.crit \{ font-size: (?<v>[\d.]+)rem", () => Num(dn.CritFontRem));
Add("DamageNumbers.BurstFontRem", COMBAT, @"\.float-num\.burst \{ color: var\(--ember\); font-size: (?<v>[\d.]+)rem", () => Num(dn.BurstFontRem));
Add("DamageNumbers.JitterPx", FX, @"Math\.random\(\) \* \d+ - (?<v>\d+) : 0\)", () => Num(dn.JitterPx));
Add("DamageNumbers.PairedOffsetPx", FX, @"x: paired \? (?<v>\d+) : 0, jitter: !paired", () => Num(dn.PairedOffsetPx));
Add("DamageNumbers.AnchorHeightFraction", FX, @"const top = b\.top \+ b\.height \* (?<v>[\d.]+) \+ y;", () => Num(dn.AnchorHeightFraction));
Add("DamageNumbers.EdgePadPx", FX, @"^\s*\{ pad: (?<v>\d+) \}\s*$", () => Num(dn.EdgePadPx));
Add("Input.DragSlopPx", "src/ui/components/holdconfirm.js", @"HOLD_POINTER_SLOP = (?<v>\d+);", () => Num(profile.Input.DragSlopPx));
Add("Input.InspectHoldMs", "src/content/balance.js", @"inspectHold: \{\s*ms: (?<v>\d+)", () => Num(profile.Input.InspectHoldMs));
Add("Input.HoldConfirmDefault", "src/content/balance.js", @"holdConfirm: \{\s*def: '(?<v>\w+)'", () => profile.Input.HoldConfirmDefault, 1, "str");
Add("Input.HoldConfirmOffMs", "src/content/balance.js", @"steps: \{ off: (?<v>\d+)", () => Num(profile.Input.HoldConfirmOffMs));
Add("Input.HoldConfirmShortMs", "src/content/balance.js", @"steps: \{ off: \d+, short: (?<v>\d+)", () => Num(profile.Input.HoldConfirmShortMs));
Add("Input.HoldConfirmNormalMs", "src/content/balance.js", @"steps: \{[^}]*normal: (?<v>\d+)", () => Num(profile.Input.HoldConfirmNormalMs));
Add("Input.HoldConfirmLongMs", "src/content/balance.js", @"steps: \{[^}]*long: (?<v>\d+)", () => Num(profile.Input.HoldConfirmLongMs));
Add("Input.HoldBeatAt", "src/content/balance.js", @"holdBeat: \{\s*at: \[(?<v>[^\]]+)\]", () => string.Join(", ", profile.Input.HoldBeatAt.Select(a => Num(a))), 1, "str");
Add("Input.TooltipOpenMs", "src/ui/components/tooltip.js", @"TOOLTIP_TIMING = Object\.freeze\(\{ open: (?<v>\d+)", () => Num(profile.Input.TooltipOpenMs));
Add("Input.TooltipHandoverMs", "src/ui/components/tooltip.js", @"TOOLTIP_TIMING = [^\n]*handover: (?<v>\d+)", () => Num(profile.Input.TooltipHandoverMs));
Add("Input.TooltipFocusMs", "src/ui/components/tooltip.js", @"TOOLTIP_TIMING = [^\n]*focus: (?<v>\d+)", () => Num(profile.Input.TooltipFocusMs));
Add("Sequences.BossIntroHoldMs", "src/ui/components/intro.js", @"timer = setTimeout\(close, (?<v>\d+)\)", () => Num(profile.Sequences.BossIntroHoldMs));
Add("Sequences.BossIntroRemoveMs", "src/ui/components/intro.js", @"setTimeout\(\(\) => veil\.remove\(\), (?<v>\d+)\)", () => Num(profile.Sequences.BossIntroRemoveMs));
Add("Sequences.PoseHoldMs", "src/ui/services/PoseAnimator.js", @"play\(pose, ms = (?<v>\d+)\)", () => Num(profile.Sequences.PoseHoldMs));
Add("Sequences.PoseMinMs", "src/ui/services/PoseAnimator.js", @"setTimeout\(settle, Math\.max\((?<v>\d+), ms\)\)", () => Num(profile.Sequences.PoseMinMs));
Add("Sequences.ReaverFrameMs", "src/ui/reaverAttack.js", @"frameMs: (?<v>\d+),", () => Num(profile.Sequences.ReaverFrameMs));
Add("Sequences.ReaverFrameCount", "src/ui/reaverAttack.js", @"Object\.freeze\(\['F\d+', (?<v>\d+)\]\)", () => Num(profile.Sequences.ReaverFrameCount), 1, "sum");
Add("Sequences.ReaverImpactFrame", "src/ui/reaverAttack.js", @"impactFrameIndex: (?<v>\d+),", () => Num(profile.Sequences.ReaverImpactFrame));
Add("Sequences.ReaverNormalLungeMs", "src/ui/reaverAttack.js", @"NORMAL_LUNGE_MS = (?<v>\d+);", () => Num(profile.Sequences.ReaverNormalLungeMs));
Add("Sequences.SlashRotateJitterDeg", FX, @"Math\.random\(\) \* \d+ - (?<v>\d+)\)\}deg", () => Num(profile.Sequences.SlashRotateJitterDeg));

Anim("card.hover", KIT, @"transition: transform (?<d>\d+)ms, box-shadow \d+ms, border-color");
Add("Motions[card.hover].Tracks[y]", COMBAT, @"\.hand \.card:hover, \.hand \.card\.selected \{\s*transform: translateY\((?<v>-?\d+)px\)", () => TrackEnd("card.hover", "y"));
Add("Motions[card.hover].Tracks[scale]", COMBAT, @"\.hand \.card:hover, \.hand \.card\.selected \{\s*transform: translateY\(-?\d+px\) scale\((?<v>[\d.]+)\)", () => TrackEnd("card.hover", "scale"));
Anim("card.rewardHover", KIT, @"transition: transform (?<d>\d+)ms, box-shadow \d+ms, border-color");
Add("Motions[card.rewardHover].Tracks[y]", KIT, @"\.reward-row \.card:hover \{ transform: translateY\((?<v>-?\d+)px\)", () => TrackEnd("card.rewardHover", "y"));
Add("Motions[card.rewardHover].Tracks[scale]", KIT, @"\.reward-row \.card:hover \{ transform: translateY\(-?\d+px\) scale\((?<v>[\d.]+)\)", () => TrackEnd("card.rewardHover", "scale"));
Anim("card.play", COMBAT, @"transition: transform (?<d>\d+)ms (?<e>[\w-]+), opacity");
Life("card.play", "src/ui/screens/combat.js", @"setTimeout\(\(\) => ghost\.remove\(\), (?<v>\d+)\)");
Add("Motions[card.play].Tracks[scale]", "src/ui/screens/combat.js", @"ghost\.style\.transform = `translate\([^`]*\) scale\((?<v>[\d.]+)\)", () => TrackEnd("card.play", "scale"));
Add("Motions[card.play].Tracks[rotate]", "src/ui/screens/combat.js", @"ghost\.style\.transform = `translate\([^`]*\) scale\([\d.]+\) rotate\((?<v>\d+)deg\)", () => TrackEnd("card.play", "rotate"));
Add("Motions[card.play].Tracks[opacity]", "src/ui/screens/combat.js", @"ghost\.style\.opacity = '(?<v>\d+)'", () => TrackEnd("card.play", "opacity"));
Anim("button.hover", BASE, @"transition: background (?<d>\d+)ms, transform");
Anim("toggle.knob", KIT, @"\.as-toggle \.knob \{[^}]*transition: left (?<d>\d+)ms");
Anim("actor.lunge", COMBAT, @"\.player \.sprite\.act-attack \{ animation: lunge-right (?<d>\d+)ms (?<e>[^;]+);");
Anim("actor.step", COMBAT, @"\.sprite\.act-move \{ animation: act-step (?<d>\d+)ms (?<e>[^;]+);");
Anim("enemy.coopLunge", UI, @"animation: enemy-lunge (?<d>\d+)ms (?<e>[^;]+);");
Anim("hit.flash", COMBAT, @"\.sprite\.hitflash > :first-child \{ animation: hitflash (?<d>\d+)ms (?<e>[^;]+);");
Anim("hit.recoil", COMBAT, @"\.enemy \.sprite\.hitflash > :first-child \{ animation: hit-enemy (?<d>\d+)ms (?<e>[^;]+);");
Life("hit.recoil", FX, @"flash\(anchor, 'hitflash', heavy \? \d+ : (?<v>\d+)\)");
Anim("hit.recoilHeavy", COMBAT, @"\.enemy \.sprite\.hitflash\.hit-heavy > :first-child \{ animation: hit-enemy-heavy (?<d>\d+)ms (?<e>[^;]+);");
Life("hit.recoilHeavy", FX, @"flash\(anchor, 'hit-heavy', (?<v>\d+)\)");
Anim("screen.shake", COMBAT, @"\.combat\.shake \{ animation: shake (?<d>\d+)ms;");
Anim("damageNumber.pop", COMBAT, @"animation: num-pop (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("damageNumber.pop", FX, @"setTimeout\(\(\) => el\.remove\(\), (?<v>\d+)\);\s*return el;");
Anim("damageNumber.reduced", COMBAT, @"\.reduced-motion \.float-num \{ animation: float-up (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("damageNumber.reduced", FX, @"setTimeout\(\(\) => el\.remove\(\), (?<v>\d+)\);\s*return el;");
Anim("banner.turn", COMBAT, @"animation: banner-pop (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("banner.turn", FX, @"el\.textContent = text;\s*layer\.appendChild\(el\);\s*setTimeout\(\(\) => el\.remove\(\), (?<v>\d+)\)");
Anim("banner.coop", KIT, @"animation: as-banner (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("banner.coop", "src/ui/screens/coop.js", @"small \? \d+ : (?<v>\d+)\)");
Add("Motions[banner.coop].ReducedDurationMs", KIT, @"\.reduced-motion \.as-banner \{ animation-duration: (?<v>\d+)ms", () => Motion("banner.coop", m => m.ReducedDurationMs));
Add("Motions[banner.coopSmall].DurationMs", KIT, @"\.as-banner\.small \{[^}]*animation-duration: (?<v>\d+)ms", () => Motion("banner.coopSmall", m => m.DurationMs));
Add("Motions[banner.coopSmall].Easing", KIT, @"animation: as-banner \d+ms (?<v>[^;]+?) forwards;", () => profile.Motion("banner.coopSmall").Easing, 1, "ease");
Life("banner.coopSmall", "src/ui/screens/coop.js", @"small \? (?<v>\d+) :");
Add("Motions[banner.coopSmall].ReducedDurationMs", KIT, @"\.reduced-motion \.as-banner \{ animation-duration: (?<v>\d+)ms", () => Motion("banner.coopSmall", m => m.ReducedDurationMs));
Anim("screen.enter", BASE, @"#app > \* \{ animation: screenIn (?<d>\d+)ms (?<e>[^;]+);");
Anim("toast.enter", UI, @"0\.6\); animation: screenIn (?<d>\d+)ms (?<e>[^;]+);");
Anim("saveSlots.enter", UI, @"\.slot-list \{[^}]*animation: screenIn (?<d>\d+)ms (?<e>[^;]+);");
Anim("eventChoice.enter", UI, @"#choices button \{ animation: screenIn (?<d>\d+)ms (?<e>[^;]+?) backwards;");
Anim("boss.veil", COMBAT, @"animation: bi-veil (?<d>\d+)ms (?<e>[^;]+);");
Anim("boss.rise", COMBAT, @"animation: bi-rise (?<d>\d+)ms (?<e>[^;]+);");
Anim("boss.rule", COMBAT, @"animation: bi-rule (?<d>\d+)ms (?<e>[^;]+);");
Anim("boss.out", COMBAT, @"animation: bi-fade (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("boss.out", "src/ui/components/intro.js", @"setTimeout\(\(\) => veil\.remove\(\), (?<v>\d+)\)");
Anim("reward.taken", KIT, @"animation: reward-took (?<d>\d+)ms (?<e>[^;]+);");
Anim("enemy.death", COMBAT, @"animation: crumble (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Anim("fx.slash", COMBAT, @"animation: slash-sweep (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("fx.slash", FX, @"'fx-slash', (?<v>\d+)\)");
Anim("fx.glyph", COMBAT, @"animation: glyph-pop (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("fx.glyph", FX, @"'fx-glyph', (?<v>\d+), '");
Anim("fx.spark", COMBAT, @"animation: spark-pop (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("fx.spark", FX, @"'fx-spark', (?<v>\d+),");
Anim("stagger.wobble", COMBAT, @"animation: wobble (?<d>\d+)ms (?<e>[^;]+);");
Life("stagger.wobble", FX, @"flash\(anchor, 'wobble', (?<v>\d+)\)");
Anim("relic.proc", COMBAT, @"animation: relicProc (?<d>\d+)ms (?<e>[^;]+);");
Life("relic.proc", FX, @"'proc', (?<v>\d+)\)");
Anim("stance.flare", COMBAT, @"animation: flare (?<d>\d+)ms (?<e>[^;]+?) forwards;");
Life("stance.flare", FX, @"el\.style\.background = [^\n]+\n\s*layer\.appendChild\(el\);\s*setTimeout\(\(\) => el\.remove\(\), (?<v>\d+)\)");
Anim("hud.barFill", BASE, @"\.bar > \.fill \{[^}]*transition: width (?<d>\d+)ms;");
Anim("proc.barDrain", FX, @"'width (?<d>\d+)ms (?<e>[\w-]+)'");
Anim("tooltip.show", KIT, @"#tooltip, #tooltip-2 \{[^}]*transition: opacity (?<d>\d+)ms (?<e>[^;]+);");
Anim("tooltip.fade", KIT, @"#tooltip\.is-fading \{ opacity: 0; transition: opacity (?<d>\d+)ms (?<e>[^;]+);");
Anim("idle.bob", COMBAT, @"\.combatant \.sprite > img \{ animation: sprite-idle (?<d>[\d.]+)s (?<e>[\w-]+) infinite;", 1000);
Add("Motions[idle.bobEnemy].DurationMs", COMBAT, @"\.combatant\.enemy \.sprite > img \{ animation-duration: (?<v>[\d.]+)s;", () => Motion("idle.bobEnemy", m => m.DurationMs), 1000);
Add("Motions[idle.bobEnemy].Easing", COMBAT, @"\.combatant \.sprite > img \{ animation: sprite-idle [\d.]+s (?<v>[\w-]+) infinite;", () => profile.Motion("idle.bobEnemy").Easing, 1, "ease");

var files = new Dictionary<string, string>();
string File0(string rel) => files.TryGetValue(rel, out var text) ? text : files[rel] = ReferenceCss.StripComments(Read(rel));
var rows = new List<(Src Spec, int Line, string Value)>();
var sourceFailures = new List<string>();
foreach (var spec in specs)
{
    var text = File0(spec.File);
    var matches = Regex.Matches(text, spec.Pattern, RegexOptions.Multiline);
    if (matches.Count == 0) { sourceFailures.Add(spec.Key + ": pattern not found in " + spec.File + " /" + spec.Pattern + "/"); continue; }
    var m = matches[0];
    var g = m.Groups["v"].Success ? m.Groups["v"] : m.Groups["d"];
    var line = text.Substring(0, g.Success ? g.Index : m.Index).Count(c => c == '\n') + 1;
    string found; bool ok;
    var expected = spec.Expected();
    switch (spec.Kind)
    {
        case "sum":
            found = Num(matches.Cast<Match>().Sum(x => double.Parse(x.Groups["v"].Value, inv)));
            ok = found == expected; break;
        case "ease":
            found = g.Value.Trim();
            ok = FeelCurves.TryParse(found, out var a) && FeelCurves.TryParse(expected, out var b) && a.Equals(b); break;
        case "ease-default":
            found = "ease (CSS default)";
            ok = FeelCurves.Parse(expected).Equals(FeelCurves.Ease); break;
        case "str":
            found = g.Value.Trim();
            ok = Regex.Replace(found, @"\s+", "") == Regex.Replace(expected, @"\s+", ""); break;
        default:
            found = Num(double.Parse(g.Value, inv) * spec.Mul);
            ok = Near(double.Parse(found, inv), double.Parse(expected, inv), 1e-6); break;
    }
    if (!ok) sourceFailures.Add(spec.Key + ": " + spec.File + ":" + line + " says " + found + ", profile says " + expected);
    rows.Add((spec, line, spec.Kind == "ease-default" ? "ease (CSS default)" : expected));
}
foreach (var f in sourceFailures) Console.WriteLine("  " + f);
Check(sourceFailures.Count == 0, "all " + specs.Count + " cited values equal the HTML reference");
var uncited = profile.Motions.Where(m => !specs.Any(s => s.Key == "Motions[" + m.Id + "].DurationMs") || !specs.Any(s => s.Key == "Motions[" + m.Id + "].Easing")).Select(m => m.Id).ToList();
Check(uncited.Count == 0, "every motion's duration and easing is cited" + (uncited.Count > 0 ? ": " + string.Join(", ", uncited) : ""));
var scalarKeys = new List<string>();
foreach (var section in new (string Name, object Value)[] { ("Pacing", profile.Pacing), ("Thresholds", profile.Thresholds), ("DamageNumbers", profile.DamageNumbers), ("Input", profile.Input), ("Sequences", profile.Sequences) })
    foreach (var field in section.Value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) scalarKeys.Add(section.Name + "." + field.Name);
var uncitedScalars = scalarKeys.Where(k => k != "Pacing.HitStopMs" && !specs.Any(s => s.Key == k)).ToList();
Check(uncitedScalars.Count == 0, "every scalar is cited (HitStopMs is the checked absence)" + (uncitedScalars.Count > 0 ? ": " + string.Join(", ", uncitedScalars) : ""));

// Keyframes: every keyframed motion's tracks equal the live @keyframes, key for key.
var keyframed = new (string Id, string File, string Name)[]
{
    ("actor.lunge", COMBAT, "lunge-right"), ("actor.step", COMBAT, "act-step"), ("enemy.coopLunge", UI, "enemy-lunge"),
    ("hit.flash", COMBAT, "hitflash"), ("hit.recoil", COMBAT, "hit-enemy"), ("hit.recoilHeavy", COMBAT, "hit-enemy-heavy"),
    ("screen.shake", COMBAT, "shake"), ("damageNumber.pop", COMBAT, "num-pop"), ("damageNumber.reduced", COMBAT, "float-up"),
    ("banner.turn", COMBAT, "banner-pop"), ("banner.coop", KIT, "as-banner"), ("banner.coopSmall", KIT, "as-banner"),
    ("screen.enter", BASE, "screenIn"), ("toast.enter", BASE, "screenIn"), ("saveSlots.enter", BASE, "screenIn"), ("eventChoice.enter", BASE, "screenIn"),
    ("boss.veil", COMBAT, "bi-veil"), ("boss.rise", COMBAT, "bi-rise"), ("boss.rule", COMBAT, "bi-rule"), ("boss.out", COMBAT, "bi-fade"),
    ("reward.taken", UI, "reward-took"), ("enemy.death", COMBAT, "crumble"), ("fx.slash", COMBAT, "slash-sweep"), ("fx.glyph", COMBAT, "glyph-pop"),
    ("fx.spark", COMBAT, "spark-pop"), ("stagger.wobble", COMBAT, "wobble"), ("relic.proc", COMBAT, "relicProc"), ("stance.flare", COMBAT, "flare"),
    ("idle.bob", COMBAT, "sprite-idle"), ("idle.bobEnemy", COMBAT, "sprite-idle"),
};
var keyframeFailures = new List<string>();
foreach (var (id, file, name) in keyframed)
{
    var parsed = ReferenceCss.Keyframes(Read(file), name);
    if (parsed == null) { keyframeFailures.Add(id + ": @keyframes " + name + " missing from " + file); continue; }
    var mine = profile.Motion(id).Tracks.Where(x => x.Property != FeelProperty.Travel && x.Property != FeelProperty.Tint).ToDictionary(x => x.Property);
    foreach (var prop in parsed.Keys.Union(mine.Keys))
    {
        if (!parsed.TryGetValue(prop, out var css)) { keyframeFailures.Add(id + "." + prop + ": not in @keyframes " + name); continue; }
        if (!mine.TryGetValue(prop, out var track)) { keyframeFailures.Add(id + "." + prop + ": @keyframes " + name + " animates it, profile does not"); continue; }
        var same = css.Count == track.Keys.Length && css.Zip(track.Keys, (c, k) => Near(c.T, k.T, 1e-6) && Near(c.V, k.V, 1e-5)).All(x => x);
        if (!same) keyframeFailures.Add(id + "." + prop + ": css " + string.Join(" ", css.Select(c => Num(c.T) + ":" + Num(c.V))) + " vs profile " + string.Join(" ", track.Keys.Select(k => Num(k.T) + ":" + Num(k.V))));
    }
}
foreach (var f in keyframeFailures) Console.WriteLine("  " + f);
Check(keyframeFailures.Count == 0, "all " + keyframed.Length + " keyframed motions equal their live @keyframes");
bool Mirrors(string file, string a, string b, params string[] props)
{
    var x = ReferenceCss.Keyframes(Read(file), a); var y = ReferenceCss.Keyframes(Read(file), b);
    return x != null && y != null && props.All(p => x[p].Zip(y[p], (u, v) => Near(u.T, v.T) && Near(u.V, -v.V)).All(q => q));
}
Check(Mirrors(COMBAT, "lunge-right", "lunge-left", "x") && Mirrors(COMBAT, "hit-enemy", "hit-player", "x") && Mirrors(COMBAT, "hit-enemy-heavy", "hit-player-heavy", "x", "rotate"),
    "enemy/player variants are exact x (and rotate) mirrors, so one track plus a sign is faithful");

// Absences the task list names: prove the reference really has none, so a future addition is noticed.
var allCss = string.Join("\n", new[] { BASE, UI, COMBAT, "styles/map.css", KIT }.Select(f => File0(f)));
var uiJs = string.Join("\n", Directory.GetFiles(Path.Combine(root, "src/ui"), "*.js", SearchOption.AllDirectories).Select(File.ReadAllText));
Check(profile.NotInReference.OrderBy(x => x).SequenceEqual(new[] { "buttonPress", "cardDiscard", "cardDraw", "hitStop", "intentReveal", "orbPulse" }), "NotInReference lists the six checked absences");
Check(!Regex.IsMatch(uiJs + allCss, @"hit-?stop|hitpause|freeze-?frame", RegexOptions.IgnoreCase) && profile.Pacing.HitStopMs == 0, "hitStop: the reference has no hit-stop (HitStopMs 0)");
Check(!Regex.IsMatch(allCss, @"@keyframes\s+[\w-]*(intent)", RegexOptions.IgnoreCase) && !Regex.IsMatch(allCss, @"intent[^{}]*\{[^}]*(animation|transition)\s*:", RegexOptions.IgnoreCase), "intentReveal: intents are not animated in the reference");
Check(!Regex.IsMatch(allCss, @"@keyframes\s+[\w-]*(draw|deal)", RegexOptions.IgnoreCase), "cardDraw: no draw/deal animation (a draw is its own paced beat)");
Check(!Regex.IsMatch(allCss, @"@keyframes\s+[\w-]*discard", RegexOptions.IgnoreCase), "cardDiscard: no discard animation");
Check(!Regex.IsMatch(allCss, @"(^|[\s,}])(button|\.as-btn)[^{,]*:active", RegexOptions.Multiline), "buttonPress: no :active press transform on buttons (hover colour only)");
Check(Regex.IsMatch(allCss, @"@keyframes orbPulse") && !Regex.IsMatch(allCss, @"animation(-name)?:\s*orbPulse"), "orbPulse: keyframes exist but nothing binds them (fx.js adds an unstyled .pulse class)");

// ---- doc: every citation row is present with its live file:line ----------------------------------
if (args.Contains("--print-sources"))
{
    Console.WriteLine();
    foreach (var (spec, line, value) in rows) Console.WriteLine("| `" + spec.Key + "` | `" + spec.File + ":" + line + "` | " + value + " |");
}
var docText = File.Exists(Path.Combine(root, DocPath)) ? Read(DocPath) : "";
var docLines = docText.Split('\n');
var docMissing = rows.Where(row => !docLines.Any(l => l.Contains("`" + row.Spec.Key + "`") && l.Contains("`" + row.Spec.File + ":" + row.Line + "`"))).Select(row => row.Spec.Key + " @ " + row.Spec.File + ":" + row.Line).ToList();
foreach (var d in docMissing.Take(10)) Console.WriteLine("  doc missing " + d);
Check(docText.Length > 0 && docMissing.Count == 0, DocPath + " cites every value at its current file:line (" + rows.Count + " rows)");
Check(profile.Motions.All(m => docText.Contains("`" + m.Id + "`") || docText.Contains("Motions[" + m.Id + "]")), DocPath + " names every motion");
Check(docText.Contains("Integration status: data + curves ready, tween wiring pending (needs Unity editor)") && docText.Contains("Feel checklist"), DocPath + " carries the integration status and the feel checklist");

// ---- Unity asset hygiene ---------------------------------------------------------------------------
var metas = new[] { "Unity/Assets/AshenSpire/Resources/Feel.meta", ProfilePath + ".meta", "Unity/Assets/AshenSpire/Runtime/Domain/FeelProfile.cs.meta", "Unity/Assets/AshenSpire/Runtime/Domain/FeelCurves.cs.meta" };
var guids = Directory.GetFiles(Path.Combine(root, "Unity/Assets"), "*.meta", SearchOption.AllDirectories)
    .Select(p => Regex.Match(File.ReadAllText(p), @"^guid: ([0-9a-f]{32})\s*$", RegexOptions.Multiline).Groups[1].Value).Where(x => x.Length > 0).ToList();
Check(metas.All(p => File.Exists(Path.Combine(root, p)) && Regex.IsMatch(Read(p), @"^guid: [0-9a-f]{32}\s*$", RegexOptions.Multiline)), "Feel folder, profile and scripts carry .meta files with 32-hex guids");
Check(guids.Count == guids.Distinct().Count(), "no duplicate guid across Unity/Assets (" + guids.Count + " metas)");
var domainSource = Read("Unity/Assets/AshenSpire/Runtime/Domain/FeelProfile.cs") + Read("Unity/Assets/AshenSpire/Runtime/Domain/FeelCurves.cs");
Check(!Regex.IsMatch(domainSource, @"using UnityEngine|UnityEngine\.") && !domainSource.Contains("System.Random") && !domainSource.Contains("DateTime"), "FeelProfile/FeelCurves are engine-free and deterministic");

return Finish();

int Finish()
{
    if (failures.Count > 0) { Console.WriteLine("Feel: " + failures.Count + " of " + (passed + failures.Count) + " checks FAILED"); return 1; }
    Console.WriteLine("Feel: " + passed + " checks passed");
    return 0;
}

record Src(string Key, string File, string Pattern, Func<string> Expected, double Mul, string Kind);
