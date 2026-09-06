# Combat presentation — 0.5.0

Commands still resolve immediately. A successful card or enemy turn produces a read-only outcome snapshot; presentation uses it for temporary damage, healing, block and poison labels. The model owns every rule and save. Animation never grants damage or delays input.

## Editing the feel

Edit `GameContent/Unity/campaign.json`, under `Feedback.Cues`, or export the table:

```powershell
python tools/campaign-table.py export FeedbackCues Builds/FeedbackCues.csv
python tools/campaign-table.py import FeedbackCues Builds/FeedbackCues.csv
```

Rows are ordered. The first nonempty `MatchTag` found on a card wins: poison, magic, faith, attack, then skill in the supplied content. Tags belong to the content registry, not Unity GameObject tags. Cards without a match use `guard`. `hit`, `heal` and `reward` are explicitly selected by the controller for their corresponding actions. Required cue IDs must remain present.

| Field | Purpose |
|---|---|
| Poses | JSON array of `idle`, `attack1`, `attack2`, `guard` or `hit`; reused from each hero's existing sprite set. Frames receive equal portions of the timeline. |
| Milliseconds | Total duration, 200–1600 ms. Quick animations halve it. |
| Distance | Maximum lunge in UI units, 0–40. Enemy turns reverse direction. |
| Color | `#RRGGBB` outcome label and soft reaction tint. |
| Frequency / EndFrequency | Start/end of the synthesized pitch sweep, 40–2000 Hz. |
| SoundDuration | Sound length, 0.05–0.8 seconds. |
| Noise | Noise/tonal blend, 0–1. Use more for impact or poison, less for magic and healing. |

`Audio.Volume` remains the master effect volume. The original frequency fields remain for legacy content without a cue library. Audio clips are synthesized and cached when configured. New sounds replace prior sound playback, bounding overlap. Browser playback requires a user gesture.

From the repository root, `dotnet run --project UnityTests/AudioPreview` exports eight PCM WAV previews to `Published/AudioEvidence` using the same generator. These reference clips omit master volume; the dev page provides audio controls to audition them.

Import through **AshenSpire → Validate and Import Content**, then restart Play mode or rebuild and reload a player. CSV lists remain JSON within a cell. Import validation runs before source replacement and keeps a timestamped backup. Invalid cue IDs, tags, poses, colors or ranges are rejected.

## Components and ownership

- `Domain/FeedbackDefinition.cs`: authored cue records, read-only outcome differences and deterministic waveform synthesis. It never consumes campaign RNG.
- `Presentation/CombatFeedback.cs`: a view-owned, cancellable UI Toolkit timeline and texture cache. One timeline runs at a time; view changes and disposal cancel it and reset transforms/tints.
- `Presentation/CampaignView.cs`: renders the stage and passes its current sprite references to the feedback component.
- `Application/RunController.cs`: observes successful command results and dispatches cues. Rejected commands emit no presentation event.
- `Application/GameAudio.cs`: owns one AudioSource and any listener it creates, manages cached clips and persists no gameplay data. Its header explains attachment, data, lifecycle and verification.

Reduced motion keeps static outcome text while suppressing sprite changes, movement and tint flashes. Quick animations shorten motion without changing rules. Mute stops current playback. Existing preference keys persist through reload; campaign save fields are unchanged.

Health labels describe net health changes after command resolution, including block absorption and healing caps. They do not claim gross damage before block or every individual strike in a multi-effect card. Recent action history remains available for the detailed result.

This slice reuses existing sprite pixels. It improves timing and reactions; it does not add new painted sprites, music, rigging or skeletal animation. Physical mobile playback and subjective sound quality still need listening and device testing.
