# Unity feel profile (F07 juice pass)

Every "feel" timing the HTML reference uses (card hover, card play, lunges, hit
flash and recoil, screen shake, damage numbers, banners, screen transitions,
boss intro, reward and effect pops, idle bob, hold and drag thresholds) is
now data. The Unity port reads the numbers from that data and no longer
guesses them.

**Integration status: wired; compile-verified against Unity reference assemblies; needs editor play test.**
`Runtime/Presentation` now reads every combat motion from the profile. The code
compiles against Unity's reference assemblies (`node tools/unity-runtime-check.mjs`)
and the beat plan is checked by `UnityTests/Feel`, but nobody has yet played it
in the Unity editor or a player build. Use the feel checklist at the end of this
page for that play test.

### What Unity element uses which profile key

| Unity element | Profile key(s) | Code |
|---|---|---|
| Profile load at startup | `Resources/Feel/feel-profile.json`, validated | `FeelDriver.Load()` from the `CampaignView` constructor |
| Reduced motion / Quick animations toggles | `FeelSettings.FromToggles` (Quick = `Speeds[fast]`) | `FeelDriver.Configure`, `CombatFeedback.Play` |
| Hand card (`OriginalCardView` inside a hand rail, solo and co-op) on pointer hover | `card.hover` (140 ms `ease`, y −56, scale 1.32, bottom-centre origin) | `FeelDriver.HandCardHover` |
| Acting player figure (player turn) | `actor.lunge` for an `attack` cue, else `actor.step`; `Pace.WindupMs` | `CombatFeedback` via `FeelBeat.Plan` |
| Acting enemy figure (enemy turn) | `actor.lunge` mirrored (`side = -1`) after `Pace.BannerBeatMs` | `CombatFeedback` via `FeelBeat.Plan` |
| Damaged enemy / player figure | `hit.recoil`, or `hit.recoilHeavy` at `Thresholds.HeavyHitDamage`; `brightness` → cue-colour flash | `CombatFeedback` |
| Player figure on heal/guard, enemy figure on poison (no damage) | `hit.flash` `brightness` track only (colour flash) | `CombatFeedback` |
| Combat stage (`OriginalRunPanel.Stage`, or the legacy stage) | `screen.shake`, heavy hits only | `CombatFeedback` |
| `combat-feedback` label, enemy turn | `banner.turn` (text `ENEMY TURN`, letter spacing and fade) | `CombatFeedback` |
| `combat-feedback` label, result numbers | `damageNumber.pop` (`damageNumber.reduced` under Reduced motion); font × `DamageNumbers.FontScale(Tier)` | `CombatFeedback` |
| Timeline length and cancel | `LifetimeMs` of every part; a click or navigation still cancels at once | `FeelBeat.TotalMs`, `FeelTween` |

Geometry is unchanged. Feel transforms are visual only: `FeelDriver.SettledBound`
reports every control's bounds with feel transforms removed, and every motion
returns to rest (no inline translate, scale or rotate) when it ends. Pointer
playtests therefore click the same settled geometry as before.

Not wired, because Unity has no matching element yet: `card.rewardHover`,
`card.play`, `enemy.death`, `fx.*`, `stagger.wobble`, `relic.proc`,
`stance.flare`, `boss.*`, `reward.taken`, `screen.enter`, the co-op banners,
HUD bar fills, tooltips and idle bob. `ExpeditionView` (not constructed
anywhere) keeps its old 220 ms pose swap.

Known limits to judge in the editor: the hand rail is a clipping `ScrollView`,
so the hovered card's lifted top can be cut off, and a hovered card is drawn
under the cards to its right (UI Toolkit draws in hierarchy order, and
reordering would change layout).

| File | What it is |
|---|---|
| `Unity/Assets/AshenSpire/Resources/Feel/feel-profile.json` | The data. Load with `Resources.Load<TextAsset>(FeelProfile.ResourcePath)` and `JsonUtility.FromJson<FeelProfile>`. |
| `Unity/Assets/AshenSpire/Runtime/Domain/FeelProfile.cs` | The model: `Pace`, `Resolve`, `ReaverAttack`, `WatchdogMs`, `Validate`, damage-number tiers. |
| `Unity/Assets/AshenSpire/Runtime/Domain/FeelCurves.cs` | CSS `cubic-bezier()` evaluation using the browser algorithm, the named easings, and keyframe sampling. |
| `UnityTests/Feel/` | `dotnet run --project UnityTests/Feel`: re-reads the HTML and fails if any value here has drifted. |

All three are pure C# (no `UnityEngine`, no RNG). Nothing they do changes
combat results.

## Settings semantics

These rules come from SPEC §7.4 and `src/ui/fx.js`.

- **Animation speed** (`slow` / `normal` / `fast` / `instant`) scales
  **pacing only**: the gap between beats, the stagger between steps, and the
  actor lunge. Fixed effect durations (hit flash, glyph, wobble, number pop)
  never change with speed. A `lunge`-timed motion is scaled by
  `speed.LungeMs / 260`, the same rule `reaverAttackTiming` uses.
- **Instant** turns pacing off: the display jumps to the end state. Queued
  effects (numbers, flashes, shake) still play `QueueStepMs` (80 ms) apart,
  like `animateEvents`.
- **Reduced motion** paces like instant. It also removes every movement track
  (`x`, `y`, `scale`, `scaleX`, `rotate`, `letterSpacing`, `travel`) and keeps
  the opacity and colour tracks. A motion with nothing left does not play.
  Damage numbers switch to `damageNumber.reduced`, and the co-op banner runs a
  500 ms fade.
- **Screen shake off** (or Reduced motion) turns off the `Shake` motions.
  **Reduce flashes** turns off the `Flash` motions: everything `fx.js` sends
  through `flash()`, which includes the hit recoil and the stagger wobble.
  Damage numbers are never turned off.
- **Unity's toggles.** `CampaignView` has "Reduced motion" and "Quick
  animations". `FeelSettings.FromToggles(reduced, quick)` maps Quick to the
  HTML `fast` speed. `CombatFeedback` used to halve durations. It now uses this
  model instead. A four-way speed choice is F15's job.

## How Presentation should consume it

```csharp
var profile = JsonUtility.FromJson<FeelProfile>(Resources.Load<TextAsset>(FeelProfile.ResourcePath).text);
var settings = FeelSettings.FromToggles(_reducedMotion, _fast);   // later: the F15 settings row
var pace = profile.Pace(settings);          // BeatMs / StepMs / WindupMs / BannerBeatMs / QueueStepMs
var hit = profile.Resolve(heavy ? "hit.recoilHeavy" : "hit.recoil", settings);
if (hit.Play)
{
    var started = Time.realtimeSinceStartupAsDouble;
    element.schedule.Execute(() =>
    {
        var ms = (Time.realtimeSinceStartupAsDouble - started) * 1000;
        element.style.translate = new Translate((float)(side * hit.SampleAt("x", ms)), (float)hit.SampleAt("y", ms));
        element.style.rotate = new Rotate((float)(side * hit.SampleAt("rotate", ms)));
        element.style.scale = new Scale(Vector2.one * (float)hit.SampleAt("scale", ms));
        element.style.opacity = (float)hit.SampleAt("opacity", ms);
        // brightness/saturate: lerp tint toward white-hot while > 1
    }).Every(16).Until(() => (Time.realtimeSinceStartupAsDouble - started) * 1000 >= hit.LifetimeMs);
}
```

- **Beat order (fx.js `playTimeline`).** Actor lunge (`actor.lunge` or
  `actor.step`). Effects start at `pace.WindupMs`, or at the painted impact
  frame from `profile.ReaverAttack(settings).ImpactMs`. The beat's effects
  then play `pace.StepMs` apart. The HUD updates, then the game waits
  `pace.BeatMs`. A turn banner beat waits `pace.BannerBeatMs`. A click skips
  straight to the end state. Once `profile.WatchdogMs(beats, settings)` has
  passed, the timeline must finish no matter what.
- **Sides.** `x` and `rotate` are written for the player acting
  (`lunge-right`) and the enemy being hit (`hit-enemy`). For the other side,
  multiply by `side = -1`. The test proves `lunge-left`, `hit-player` and
  `hit-player-heavy` are exact mirrors.
- **Timing.** `DurationMs` is how long the animation runs. `LifetimeMs` is
  how long `fx.js` keeps the element or class alive. Remove the element at
  `LifetimeMs`, not at `DurationMs`.
- **Damage numbers.** Choose the tier with
  `FeelDamageNumbers.Tier(amount, profile.Thresholds)` and scale the font by
  `DamageNumbers.FontScale(tier)`: crit 1.76×, heavy 1.36×, small 0.8×,
  burst 1.44×. Spawn at `AnchorHeightFraction` of the target's height with
  ±`JitterPx` of jitter. A guarded hit and a damage number that appear
  together sit ±`PairedOffsetPx` apart, with no jitter. Keep `EdgePadPx`
  from the edges of the layer.
- **Easing.** A `FeelPlayback` carries `Curve`, and `Sample` eases each
  keyframe interval separately, exactly as CSS does. Do not ease the whole
  motion and then interpolate the keys.
- **Replace, don't duplicate.** `CombatFeedback.Play` used to hard-code a sine
  arc, a 12 px label rise and a ×0.5 "fast" factor. These are now
  `actor.lunge` + `hit.recoil` + `damageNumber.pop`. `campaign.json/Feedback`
  keeps its poses, colours and sounds. `NativeFeedbackProjection` does not
  change.

## Every parameter

Each row is re-read from the named `file:line` by `UnityTests/Feel`. If a
value changes in the HTML, the test fails. If a line number moves, the test
also fails until this table is regenerated. To regenerate the rows, run
`dotnet run --project UnityTests/Feel -- --print-sources`. Each row's key is
the path in `feel-profile.json`.

### Pacing by animation speed

| HTML source | Value | JSON key |
|---|---|---|
| `src/ui/fx.js:21` | 700 | `Speeds[slow].BeatMs` |
| `src/ui/fx.js:21` | 140 | `Speeds[slow].StepMs` |
| `src/ui/fx.js:21` | 340 | `Speeds[slow].LungeMs` |
| `src/ui/fx.js:22` | 400 | `Speeds[normal].BeatMs` |
| `src/ui/fx.js:22` | 90 | `Speeds[normal].StepMs` |
| `src/ui/fx.js:22` | 260 | `Speeds[normal].LungeMs` |
| `src/ui/fx.js:23` | 180 | `Speeds[fast].BeatMs` |
| `src/ui/fx.js:23` | 45 | `Speeds[fast].StepMs` |
| `src/ui/fx.js:23` | 160 | `Speeds[fast].LungeMs` |
| `src/ui/fx.js:24` | instant | `Speeds[instant].Instant` |
| `src/ui/screens/settings.js:87` | normal | `DefaultSpeed` |
| `src/ui/fx.js:13` | 80 | `Pacing.QueueStepMs` |
| `src/ui/fx.js:659` | 260 | `Pacing.BannerMinBeatMs` |
| `src/ui/fx.js:702` | 0.55 | `Pacing.WindupLungeFraction` |
| `src/ui/fx.js:620` | 2000 | `Pacing.WatchdogBaseMs` |
| `src/ui/fx.js:620` | 4 | `Pacing.WatchdogStepsPerBeat` |

### Hit thresholds and screen shake

| HTML source | Value | JSON key |
|---|---|---|
| `src/ui/fx.js:755` | 15 | `Thresholds.HeavyHitDamage` |
| `src/ui/fx.js:387` | 15 | `Thresholds.HeavyHitDamage` |
| `src/ui/fx.js:386` | 25 | `Thresholds.CritDamage` |
| `src/ui/fx.js:388` | 6 | `Thresholds.SmallDamageBelow` |
| `styles/combat.css:499` | 4 | `Thresholds.ShakeMaxPx` |

### Damage numbers

| HTML source | Value | JSON key |
|---|---|---|
| `styles/combat.css:330` | 2.5 | `DamageNumbers.BaseFontRem` |
| `styles/combat.css:338` | 2 | `DamageNumbers.SmallFontRem` |
| `styles/combat.css:339` | 3.4 | `DamageNumbers.HeavyFontRem` |
| `styles/combat.css:340` | 4.4 | `DamageNumbers.CritFontRem` |
| `styles/combat.css:343` | 3.6 | `DamageNumbers.BurstFontRem` |
| `src/ui/fx.js:344` | 13 | `DamageNumbers.JitterPx` |
| `src/ui/fx.js:757` | 26 | `DamageNumbers.PairedOffsetPx` |
| `src/ui/fx.js:345` | 0.25 | `DamageNumbers.AnchorHeightFraction` |
| `src/ui/fx.js:359` | 6 | `DamageNumbers.EdgePadPx` |

### Input: hover, drag, hold, tooltip

| HTML source | Value | JSON key |
|---|---|---|
| `src/ui/components/holdconfirm.js:120` | 12 | `Input.DragSlopPx` |
| `src/content/balance.js:741` | 400 | `Input.InspectHoldMs` |
| `src/content/balance.js:661` | normal | `Input.HoldConfirmDefault` |
| `src/content/balance.js:662` | 0 | `Input.HoldConfirmOffMs` |
| `src/content/balance.js:662` | 350 | `Input.HoldConfirmShortMs` |
| `src/content/balance.js:662` | 600 | `Input.HoldConfirmNormalMs` |
| `src/content/balance.js:662` | 1000 | `Input.HoldConfirmLongMs` |
| `src/content/balance.js:713` | 0, 0.42, 0.78 | `Input.HoldBeatAt` |
| `src/ui/components/tooltip.js:38` | 500 | `Input.TooltipOpenMs` |
| `src/ui/components/tooltip.js:38` | 120 | `Input.TooltipHandoverMs` |
| `src/ui/components/tooltip.js:38` | 160 | `Input.TooltipFocusMs` |

### Sequences: boss intro, poses, painted attack

| HTML source | Value | JSON key |
|---|---|---|
| `src/ui/components/intro.js:39` | 2300 | `Sequences.BossIntroHoldMs` |
| `src/ui/components/intro.js:36` | 480 | `Sequences.BossIntroRemoveMs` |
| `src/ui/services/PoseAnimator.js:141` | 260 | `Sequences.PoseHoldMs` |
| `src/ui/services/PoseAnimator.js:151` | 60 | `Sequences.PoseMinMs` |
| `src/ui/reaverAttack.js:29` | 56 | `Sequences.ReaverFrameMs` |
| `src/ui/reaverAttack.js:11` | 60 | `Sequences.ReaverFrameCount` |
| `src/ui/reaverAttack.js:33` | 31 | `Sequences.ReaverImpactFrame` |
| `src/ui/reaverAttack.js:8` | 260 | `Sequences.ReaverNormalLungeMs` |
| `src/ui/fx.js:401` | 25 | `Sequences.SlashRotateJitterDeg` |

### Cards and buttons

| HTML source | Value | JSON key |
|---|---|---|
| `styles/kit.css:668` | 140 | `Motions[card.hover].DurationMs` |
| `styles/kit.css:668` | ease (CSS default) | `Motions[card.hover].Easing` |
| `styles/combat.css:248` | -56 | `Motions[card.hover].Tracks[y]` |
| `styles/combat.css:248` | 1.32 | `Motions[card.hover].Tracks[scale]` |
| `styles/kit.css:668` | 140 | `Motions[card.rewardHover].DurationMs` |
| `styles/kit.css:668` | ease (CSS default) | `Motions[card.rewardHover].Easing` |
| `styles/kit.css:861` | -8 | `Motions[card.rewardHover].Tracks[y]` |
| `styles/kit.css:861` | 1.06 | `Motions[card.rewardHover].Tracks[scale]` |
| `styles/combat.css:478` | 220 | `Motions[card.play].DurationMs` |
| `styles/combat.css:478` | ease-in | `Motions[card.play].Easing` |
| `src/ui/screens/combat.js:1753` | 260 | `Motions[card.play].LifetimeMs` |
| `src/ui/screens/combat.js:1750` | 0.35 | `Motions[card.play].Tracks[scale]` |
| `src/ui/screens/combat.js:1750` | 6 | `Motions[card.play].Tracks[rotate]` |
| `src/ui/screens/combat.js:1751` | 0 | `Motions[card.play].Tracks[opacity]` |
| `styles/base.css:331` | 120 | `Motions[button.hover].DurationMs` |
| `styles/base.css:331` | ease (CSS default) | `Motions[button.hover].Easing` |
| `styles/kit.css:212` | 140 | `Motions[toggle.knob].DurationMs` |
| `styles/kit.css:212` | ease (CSS default) | `Motions[toggle.knob].Easing` |

### Actors, hits and shake

| HTML source | Value | JSON key |
|---|---|---|
| `styles/combat.css:358` | 280 | `Motions[actor.lunge].DurationMs` |
| `styles/combat.css:358` | ease-out | `Motions[actor.lunge].Easing` |
| `styles/combat.css:359` | 260 | `Motions[actor.step].DurationMs` |
| `styles/combat.css:359` | ease-out | `Motions[actor.step].Easing` |
| `styles/ui.css:485` | 400 | `Motions[enemy.coopLunge].DurationMs` |
| `styles/ui.css:485` | cubic-bezier(.25,.8,.35,1) | `Motions[enemy.coopLunge].Easing` |
| `styles/combat.css:382` | 200 | `Motions[hit.flash].DurationMs` |
| `styles/combat.css:382` | ease-out | `Motions[hit.flash].Easing` |
| `styles/combat.css:383` | 220 | `Motions[hit.recoil].DurationMs` |
| `styles/combat.css:383` | cubic-bezier(0.25, 0.8, 0.35, 1) | `Motions[hit.recoil].Easing` |
| `src/ui/fx.js:761` | 220 | `Motions[hit.recoil].LifetimeMs` |
| `styles/combat.css:385` | 340 | `Motions[hit.recoilHeavy].DurationMs` |
| `styles/combat.css:385` | cubic-bezier(0.25, 0.8, 0.35, 1) | `Motions[hit.recoilHeavy].Easing` |
| `src/ui/fx.js:766` | 380 | `Motions[hit.recoilHeavy].LifetimeMs` |
| `styles/combat.css:353` | 200 | `Motions[screen.shake].DurationMs` |
| `styles/combat.css:353` | ease (CSS default) | `Motions[screen.shake].Easing` |
| `styles/combat.css:414` | 550 | `Motions[enemy.death].DurationMs` |
| `styles/combat.css:414` | ease-in | `Motions[enemy.death].Easing` |
| `styles/combat.css:458` | 550 | `Motions[stagger.wobble].DurationMs` |
| `styles/combat.css:458` | ease-in-out | `Motions[stagger.wobble].Easing` |
| `src/ui/fx.js:834` | 600 | `Motions[stagger.wobble].LifetimeMs` |
| `styles/combat.css:504` | 3100 | `Motions[idle.bob].DurationMs` |
| `styles/combat.css:504` | ease-in-out | `Motions[idle.bob].Easing` |
| `styles/combat.css:518` | 2600 | `Motions[idle.bobEnemy].DurationMs` |
| `styles/combat.css:504` | ease-in-out | `Motions[idle.bobEnemy].Easing` |

### Damage numbers and banners

| HTML source | Value | JSON key |
|---|---|---|
| `styles/combat.css:335` | 540 | `Motions[damageNumber.pop].DurationMs` |
| `styles/combat.css:335` | cubic-bezier(0.2, 0.9, 0.3, 1) | `Motions[damageNumber.pop].Easing` |
| `src/ui/fx.js:362` | 600 | `Motions[damageNumber.pop].LifetimeMs` |
| `styles/combat.css:345` | 260 | `Motions[damageNumber.reduced].DurationMs` |
| `styles/combat.css:345` | ease-out | `Motions[damageNumber.reduced].Easing` |
| `src/ui/fx.js:362` | 600 | `Motions[damageNumber.reduced].LifetimeMs` |
| `styles/combat.css:349` | 300 | `Motions[banner.turn].DurationMs` |
| `styles/combat.css:349` | ease-out | `Motions[banner.turn].Easing` |
| `src/ui/fx.js:412` | 320 | `Motions[banner.turn].LifetimeMs` |
| `styles/kit.css:1523` | 1100 | `Motions[banner.coop].DurationMs` |
| `styles/kit.css:1523` | ease-out | `Motions[banner.coop].Easing` |
| `src/ui/screens/coop.js:1150` | 1100 | `Motions[banner.coop].LifetimeMs` |
| `styles/kit.css:1532` | 500 | `Motions[banner.coop].ReducedDurationMs` |
| `styles/kit.css:1525` | 900 | `Motions[banner.coopSmall].DurationMs` |
| `styles/kit.css:1523` | ease-out | `Motions[banner.coopSmall].Easing` |
| `src/ui/screens/coop.js:1150` | 900 | `Motions[banner.coopSmall].LifetimeMs` |
| `styles/kit.css:1532` | 500 | `Motions[banner.coopSmall].ReducedDurationMs` |

### Screen transitions, boss intro and rewards

| HTML source | Value | JSON key |
|---|---|---|
| `styles/base.css:387` | 240 | `Motions[screen.enter].DurationMs` |
| `styles/base.css:387` | ease-out | `Motions[screen.enter].Easing` |
| `styles/ui.css:308` | 200 | `Motions[toast.enter].DurationMs` |
| `styles/ui.css:308` | ease-out | `Motions[toast.enter].Easing` |
| `styles/ui.css:273` | 500 | `Motions[saveSlots.enter].DurationMs` |
| `styles/ui.css:273` | ease-out | `Motions[saveSlots.enter].Easing` |
| `styles/ui.css:437` | 260 | `Motions[eventChoice.enter].DurationMs` |
| `styles/ui.css:437` | ease-out | `Motions[eventChoice.enter].Easing` |
| `styles/combat.css:786` | 480 | `Motions[boss.veil].DurationMs` |
| `styles/combat.css:786` | ease-out | `Motions[boss.veil].Easing` |
| `styles/combat.css:787` | 700 | `Motions[boss.rise].DurationMs` |
| `styles/combat.css:787` | cubic-bezier(0.2, 0.8, 0.3, 1) | `Motions[boss.rise].Easing` |
| `styles/combat.css:788` | 900 | `Motions[boss.rule].DurationMs` |
| `styles/combat.css:788` | ease-out | `Motions[boss.rule].Easing` |
| `styles/combat.css:789` | 460 | `Motions[boss.out].DurationMs` |
| `styles/combat.css:789` | ease-in | `Motions[boss.out].Easing` |
| `src/ui/components/intro.js:36` | 480 | `Motions[boss.out].LifetimeMs` |
| `styles/kit.css:862` | 420 | `Motions[reward.taken].DurationMs` |
| `styles/kit.css:862` | ease-out | `Motions[reward.taken].Easing` |

### Effects, HUD and tooltips

| HTML source | Value | JSON key |
|---|---|---|
| `styles/combat.css:425` | 260 | `Motions[fx.slash].DurationMs` |
| `styles/combat.css:425` | ease-out | `Motions[fx.slash].Easing` |
| `src/ui/fx.js:760` | 300 | `Motions[fx.slash].LifetimeMs` |
| `styles/combat.css:437` | 430 | `Motions[fx.glyph].DurationMs` |
| `styles/combat.css:437` | ease-out | `Motions[fx.glyph].Easing` |
| `src/ui/fx.js:698` | 450 | `Motions[fx.glyph].LifetimeMs` |
| `styles/combat.css:449` | 300 | `Motions[fx.spark].DurationMs` |
| `styles/combat.css:449` | ease-out | `Motions[fx.spark].Easing` |
| `src/ui/fx.js:749` | 320 | `Motions[fx.spark].LifetimeMs` |
| `styles/combat.css:466` | 300 | `Motions[relic.proc].DurationMs` |
| `styles/combat.css:466` | ease-out | `Motions[relic.proc].Easing` |
| `src/ui/fx.js:854` | 320 | `Motions[relic.proc].LifetimeMs` |
| `styles/combat.css:482` | 300 | `Motions[stance.flare].DurationMs` |
| `styles/combat.css:482` | ease-out | `Motions[stance.flare].Easing` |
| `src/ui/fx.js:446` | 320 | `Motions[stance.flare].LifetimeMs` |
| `styles/base.css:380` | 200 | `Motions[hud.barFill].DurationMs` |
| `styles/base.css:380` | ease (CSS default) | `Motions[hud.barFill].Easing` |
| `src/ui/fx.js:815` | 250 | `Motions[proc.barDrain].DurationMs` |
| `src/ui/fx.js:815` | ease-out | `Motions[proc.barDrain].Easing` |
| `styles/kit.css:572` | 140 | `Motions[tooltip.show].DurationMs` |
| `styles/kit.css:572` | ease-out | `Motions[tooltip.show].Easing` |
| `styles/kit.css:587` | 160 | `Motions[tooltip.fade].DurationMs` |
| `styles/kit.css:587` | ease-out | `Motions[tooltip.fade].Easing` |

### Keyframe tracks

Each keyframed motion's `Tracks` are compared key by key with the live
`@keyframes` rule. Where CSS leaves out the 0% or 100% keyframe, the rest
value is used (`none`), as the browser does. Transitions (card hover, card
play, button, toggle, bars, tooltips) have no keyframes. Their end values are
cited in the tables above.

| Motion(s) | `@keyframes` |
|---|---|
| `actor.lunge` | `styles/combat.css` `lunge-right` (mirror: `lunge-left`) |
| `actor.step` | `styles/combat.css` `act-step` |
| `enemy.coopLunge` | `styles/ui.css` `enemy-lunge` |
| `hit.flash` / `hit.recoil` / `hit.recoilHeavy` | `styles/combat.css` `hitflash` / `hit-enemy` / `hit-enemy-heavy` |
| `screen.shake` | `styles/combat.css` `shake`: ±4 px x, ±3 px y, no decay beyond the keyframes |
| `damageNumber.pop` / `damageNumber.reduced` | `styles/combat.css` `num-pop` / `float-up` |
| `banner.turn` / `banner.coop` / `banner.coopSmall` | `styles/combat.css` `banner-pop` / `styles/kit.css` `as-banner` |
| `screen.enter` / `toast.enter` / `saveSlots.enter` / `eventChoice.enter` | `styles/base.css` `screenIn` |
| `boss.veil` / `boss.rise` / `boss.rule` / `boss.out` | `styles/combat.css` `bi-veil` / `bi-rise` / `bi-rule` / `bi-fade` |
| `reward.taken` | `styles/ui.css` `reward-took` |
| `enemy.death` | `styles/combat.css` `crumble` |
| `fx.slash` / `fx.glyph` / `fx.spark` | `styles/combat.css` `slash-sweep` / `glyph-pop` / `spark-pop` |
| `stagger.wobble` / `relic.proc` / `stance.flare` | `styles/combat.css` `wobble` / `relicProc` / `flare` |
| `idle.bob` / `idle.bobEnemy` | `styles/combat.css` `sprite-idle` |

## Not in the reference

The story's list names a few moments that the HTML does not animate.
`NotInReference` lists them. The test proves each one is still missing from
the HTML, so if one is added there later, the test will catch it.

| Id | What the HTML actually does |
|---|---|
| `hitStop` | No hit-stop or freeze frame. `Pacing.HitStopMs` is 0. The weight of a hit comes from the recoil, the flash and the shake. |
| `intentReveal` | Enemy intents are static StatePills (`styles/kit.css`). They appear when the HUD re-renders, with no animation. |
| `cardDraw` | No draw animation. The draws come as their own paced beat (`groupBeats` kind `draw`, `BeatMs`), and then the hand re-renders. |
| `cardDiscard` | No discard animation. The hand re-renders. |
| `buttonPress` | No `:active` transform. A button fades its colour on hover over 120 ms (`button.hover`), and that is all. |
| `orbPulse` | `@keyframes orbPulse` exists, but no rule uses it. `fx.js` adds a `.pulse` class that has no style. |

## Quirks in the reference, and the choices made

- **The reduced-motion override.** `styles/base.css:403` forces
  `animation-duration: 0.01ms !important` on everything under
  `.reduced-motion`. That also collapses the 260 ms `float-up` and the 500 ms
  reduced banner that the stylesheet declares on purpose. The profile follows
  what those declarations intend: opacity fades are kept and movement is
  removed, as this story asks.
- **The lunge is cut short.** In CSS the lunge runs 280 ms, but `fx.js`
  removes the class after `lungeMs` (260 at normal). The profile keeps the
  CSS 280 ms and scales it by speed (172 ms at fast). The lifetime follows
  the speed.
- **Effects that outlast 300 ms.** Heavy flash 380 ms, glyph 450 ms and
  wobble 600 ms are longer than SPEC §7.4's 300 ms target. They are kept as
  they are: the SPEC lists them as known exceptions.

## Feel checklist (owner playtest)

Play each item in Unity next to the HTML build, at the same speed setting.

- [ ] Hovering a hand card lifts it 56 px and scales it to 1.32 over about 140 ms, with no lag and no overshoot.
- [ ] A played card flies to its target, shrinks to 0.35 and fades out in about 220 ms, and never covers End Turn.
- [ ] At normal speed an attack beat reads as lunge, then impact at about 143 ms, then numbers 90 ms apart, then about 400 ms of rest before the next actor.
- [ ] A hit of 15 or more recoils further with a tilt, flashes longer and shakes the screen. A hit under 15 does not shake.
- [ ] The shake is small (4 px at most) and over within 200 ms. It never shakes when Screen shake or Reduced motion is on.
- [ ] Damage numbers pop in with an overshoot and rise about 48 px. Crits of 25 or more are clearly bigger and gold. Guard and damage numbers sit side by side.
- [ ] "ENEMY TURN" / "YOUR TURN" flashes for about 300 ms with its letter spacing closing in, and the next beat waits at least 260 ms.
- [ ] A stagger wobbles the enemy and shows the ✦ glyph. A death crumbles over 550 ms without jumping.
- [ ] The boss intro fades in, rises, holds about 2.3 s and fades out. A click skips it.
- [ ] Screens fade up 8 px in 240 ms. A taken reward row pulses once.
- [ ] Fast feels snappier but not cut off. Slow feels deliberate. Instant jumps to the result but still shows the numbers.
- [ ] With Reduced motion on nothing moves, yet numbers and banners still fade in and out, and every hit is still readable.
- [ ] With Reduce flashes on there are no bright hit flashes, and the numbers still appear.
- [ ] A click during any animation jumps to the end state, and the next input works.
