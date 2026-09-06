# Content authoring — 0.6.0

Open **AshenSpire → Campaign Content Editor** in Unity. Fields now edit a shared draft directly; switching records does not discard work. Save & Import writes the authoritative JSON only after validation. The game, card balance and save schema are unchanged in this slice.

## Add a card

1. Choose Cards and search by ID or name. Select an existing card with the behavior you want.
2. Duplicate it. The editor generates a readable ID such as `bloodrushCopy`, then `bloodrushCopy2`; replace the new ID with your preferred stable name.
3. Edit Name, Cost, Tags and the nested Effects fields. Array foldouts let you add, remove and reorder entries. Effects remain reusable operation/amount components.
4. Use Add this card to reward catalog if it should be offered as a reward. Include the correct class or shared tag; Save checks affinity rules. Alternatively, add its ID to a hero's Deck.
5. Validate draft, then Save & Import. Restart Play mode or rebuild/reload a player to use the new content.

Keep existing IDs stable: authored records and saved expeditions refer to them. Duplicate to add content. Remove row affects only the draft and is undoable; Save rejects dangling references. Undo/Redo also covers nested fields, duplication and reward enrollment. Undo remains useful after saving because the disk baseline is tracked separately.

## Tables and settings

Cards, Heroes, Foes, Encounters, Equipment, Tags and FeedbackCues all use structured fields and searchable record lists. Campaign settings exposes energy, draw size, healing, starting flasks, the shared reward tag, reward catalog and active sound volume. Cue timing and sound synthesis belong in FeedbackCues, not the legacy frequency fields.

The sprite picker writes a Resources/Art-relative name. Heroes select an `_idle` sprite and use its prefix; all five poses must exist. Nested Art folders are supported. Save and build import now use the same sprite preflight, including equipment artwork. Missing player-facing names are rejected before source replacement.

The read-only JSON preview is useful for copying a record. CSV remains available through `tools/campaign-table.py`; imports now use the same name and sprite preflight as the editor, in addition to strict schema/reference checks.

## Drafts, recovery and conflicts

Checkpoint draft saves unsaved work to `Builds/AuthoringDrafts/campaign-draft.json`. Closing the window or recompiling also checkpoints it. Previous checkpoint bytes remain in `.previous-*.json` files. A new editor window recovers an unsaved checkpoint. This is recovery at explicit checkpoints/window shutdown, not a continuous autosave or a guarantee against a crash before checkpointing.

Save compares the file with the hash captured when loaded. If CSV, Git or another tool changed the source, the editor retains your draft and refuses the stale save. Review the current source and checkpoint before using Reload from disk. Reload asks before discarding in-memory edits and keeps the prior draft in a checkpoint backup. This is optimistic conflict detection, not a distributed lock against writers racing at the exact instant of replacement.

Successful saves retain the exact old source bytes under `Builds/ContentBackups`, then atomically replace the source. If import fails afterward, the editor explicitly says the source was saved and identifies the import failure. Fix it and run Validate and Import Content; do not assume the source is unchanged.

## Checks

```powershell
dotnet run --project UnityTests/Authoring
dotnet run --project UnityTests/Domain
.\tools\android-preflight.ps1
```

In Unity, **AshenSpire → Run Authoring Checks** exercises actual SerializedObject editing, Undo/Redo, duplicates, reward enrollment and recovery serialization without changing authoritative content. The equivalent batch entry point is `AshenSpire.Editor.CampaignAuthoringChecks.Run`.

Physical Android testing is pending when no authorized device appears in the preflight report. Connect a device with USB debugging enabled and authorize the computer, then rerun the preflight. The script only reads inventory and game-installation status; it does not install or launch the APK or clear saves. A connected device alone is not gameplay evidence.

## Implementation map

- `Editor/CampaignContentWindow.cs`: structured form, search, selection, sprite picker and owner messages.
- `Editor/CampaignContentDraft.cs`: transient Unity Undo target and reusable record operations.
- `Editor/CampaignContentFile.cs`: source/hash checks, atomic replacement, exact backups and draft checkpoints; no Unity dependency.
- `Editor/CampaignAuthoringValidation.cs`: shared display-name and sprite checks for saves, CSV imports and build imports.
- `Editor/CampaignAuthoringChecks.cs`: native Editor API regression checks.

No new scene component, prefab wiring or runtime package is required. Draft objects are editor-only and do not enter game assets or saved expeditions.
