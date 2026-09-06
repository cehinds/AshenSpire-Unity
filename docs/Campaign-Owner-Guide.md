# Own and extend the campaign

Open `Unity/` in Unity 6.6 (6000.6.0f1). Open `Assets/AshenSpire/Scenes/Expedition.unity` and press Play. Choose a wanderer. The current campaign has four classes, three acts, nine encounter stops, three bosses and repeatable seeded runs.

## Content has one home

`GameContent/Unity/campaign.json` is authoritative. `Unity/Assets/AshenSpire/Resources/campaign.json` is generated. Use **AshenSpire → Campaign Content Editor** to browse Cards, Heroes, Foes, Encounters, Equipment and Tags. Apply a record in memory, then **Save & Import**. Validation rejects bad references; a timestamped source backup goes to `Builds/ContentBackups`. Stop and restart Unity Play mode, or rebuild and reload the exported player after content changes. A new run inside an already-running player uses the previously loaded definitions.

For a spreadsheet workflow:

```powershell
python tools/campaign-table.py export Cards Builds/Cards.csv
# Edit Builds/Cards.csv in your spreadsheet application.
python tools/campaign-table.py import Cards Builds/Cards.csv
```

Campaign card descriptions are generated from effect amounts, current strength and equipment bonuses. The Description field remains an authoring note. Effects and tag lists are JSON inside CSV cells. Preserve the header. You can substitute Foes, Heroes, Equipment, Encounters or Tags. Imports run the strict C# schema validator before replacing the JSON. Bad data leaves the source unchanged. Treat CSV as an editing interchange, not a second authoritative source.

## Concrete editing recipes

| Goal | Change | Verify |
|---|---|---|
| Tune Strike | Cards/strike: damage Amount 6 → 7 | Start a new run; no gear or strength; enemy loses 7 vitality |
| Add a poison card | Duplicate venom, choose a unique Id, edit damage/poison Amount; add its Id to RewardCards | Validate/import; new reward pool can offer it |
| Add an enemy | Duplicate a Foes record with a new Id, Health, Reward and Intents; bind an Art name | Add the Id to an encounter's Options; verify visible intent and payout |
| Add an equipment item | Duplicate emberBlade, new Id and price; use Operation damage, Amount 1, RequiredTag attack | Buy at the forge; attack damage increases once; duplicate purchase is disabled |
| Add a route | Add another existing foe Id to Encounters/Options | Map shows another route; save/resume preserves the selected foe |
| Add an act stop | Append an encounter with Act, Name, Background and valid Options | Progress count expands; victory occurs only after its reward |
| Replace an enemy sprite | Put a PNG under Resources/Art, select it in the editor's sprite picker, copy its stem into Foes/Art | Import validates existence; inspect full silhouette on a phone viewport |
| Change animation | Replace a hero's `_idle`, `_attack1`, `_attack2`, `_guard`, `_hit` PNGs with matching canvases | Test attacks, guarding, taking a hit and Reduced motion |

The editor's record view uses JSON for nested components. It offers duplication and a sprite picker, not arbitrary new code behaviors. The strict command-line validator also rejects unknown fields:

```powershell
dotnet run --project UnityTests/Domain -- --validate-campaign GameContent/Unity/campaign.json
```

## Components and tags

CampaignDefinition contains definitions. CampaignState contains changing resources, piles, statuses, purchases and reward choices. CampaignSession interprets commands. CampaignView observes state and emits semantic events. RunController adapts Unity lifecycle; CampaignSaveStore owns storage. No simulation result depends on animation timing.

Effect components: damage, block, draw, poison, weak, strength, heal and energy. Enemy intent components: attack, guard, charge and poison. Cards compose effects; equipment contributes by querying card/hero tags. Tag records carry stable Id, Domain and Family values. This is the Unity campaign's tag registry, not full parity with every original tag query.

To add a genuinely new behavior, add its operation to CampaignSession's supported operations and effect registry, write a domain test that distinguishes the new outcome, then author content using it. An enemy-wide area effect needs an explicit multi-enemy model first; writing a new string in JSON alone cannot implement that behavior.

## Build and test

```powershell
dotnet run --project UnityTests/Domain
.\tools\build-unity.ps1 -Target Windows
.\tools\build-unity.ps1 -Target Android
.\tools\build-unity.ps1 -Target Web
.\tools\preview-unity.ps1
# Optional same-network phone preview:
.\tools\preview-unity.ps1 -ShareOnLan
```

Build Web last so the package manifest hashes the current downloadable artifacts. Windows outputs `Published/Windows.zip`; Android outputs `Published/Android.apk`; Web outputs `Published/Web/` and `Published/Web.zip`. Android is a local testing APK, not a signed store release. iOS still requires a Mac/Xcode signing and device-validation lane.

The build script reports failures and writes logs under Builds. The browser harness plays through real pointer input, using read-only control bounds from development builds. Run `node tools/campaign-playtest.cjs http://127.0.0.1:8788 Builds/BrowserEvidence --full` with Playwright available to capture a complete expedition.

## Saves, settings and bug reports

Campaign saves use a new per-channel key and retain a checksummed previous record. The original Expedition.v1 and JavaScript saves are not imported or overwritten. Browser storage can be cleared by the browser/user; no cloud synchronization is claimed. Menu and pause preserve progress.

Settings include Reduced motion, Quick animations and Mute sound. The Audio record controls synthesized sound frequencies, duration and volume. How-to-play text explains combat rules. Development builds expose a component gallery and an expedition inspector; the inspector previews a bug report and offers Copy report. Include version, seed, device/browser, reproduction steps, expected result and actual result. Debug state output and inspection are disabled on test/release/main hosted channels.

## What remains beyond this campaign

This is a complete start-to-ending campaign, not full mechanical parity with the original browser game. Original stamina/mana/Poise systems, full status DSL, weapon-driven card recomposition and the complete content roster are not ported. Physical-device performance, iOS builds, touch-device coverage, a composed soundtrack and richer frame animation remain separate work. Automatic Unity compilation requires a dedicated runner/licensing decision; Pages validates and publishes already-exported builds.

## Combat clarity (0.3.0)

Intent and status controls open read-only explanations. Draw/discard inspection groups cards without exposing draw order. Recent actions retain twelve results during the current session; they are not part of saved state. Unaffordable cards remain inspectable but cannot be played. Edit CampaignSession.cs for explanations and effect feedback, CampaignView.cs for inspection navigation, and Expedition.uss for appearance.
