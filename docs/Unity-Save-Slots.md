# Unity save slots and profile archive (F10)

**Integration status: build 25 save-recovery implementation; acceptance remains open.**

The current storage suite passes 148 checks, including thrown I/O failures,
partial writes, recovery, repeated result saves, real authored-record capacity,
legacy compaction and interrupted encoding upgrades. The Unity-compiled
controller/view fixtures pass 36 checks using isolated storage. Build 24's
compiled browser checks already cover combat checkpoint restoration. The first
build-25 browser test reproduced a slot-copy failure at the Web storage limit;
the corrected compact-storage player is being rebuilt. These checks do not prove
physical-device durability or asynchronous browser storage quota handling.

## What this is, in plain words

The HTML game lets you keep three runs going at once, one per save slot, and it
keeps a durable profile with your last 20 run results. Until now the Unity build
kept only one run per channel (Web, Dev, Test, desktop).

The Unity build now has three run slots and the result archive. The title
screen has a **Load** entry that opens the slot picker; **Continue** and **New**
work as before when you only have one run. Normal save/reload has been exercised
in compiled browser players; see the source-specific receipts in `docs/qa`.

On screen:

- **Continue** resumes the slot you saved most recently. With one existing run
  (moved into domain slot 0, shown as "Slot 1", on first start) that is the same run as before.
- **New** starts straight away in the first empty slot. Only when all three
  slots are full does it open the slot picker so you can choose what to
  overwrite.
- **Load** lists the three slots with class, act and floor, seed, last saved
  time (UTC) and play time. Each slot offers Continue, New (Overwrite when it
  holds a run), Copy to the first empty slot, and Delete. Overwrite and Delete
  ask for confirmation first.
- **Collection** (the existing profile view) lists the last 20 finished climbs,
  newest first.
- If a save fails, the current game shows a warning and retains in-memory
  progress for retry. Keep the game open while freeing storage. A verified
  retry clears the failure warning.
- An unreadable profile leaves its records untouched and returns a useful
  title message. A recovered backup is identified explicitly; recent progress
  may be missing.

What the new code already does:

- **Three slots (0, 1, 2).** Each slot remembers the class, act and floor, the
  seed, total play time, when it was last saved (UTC) and the game version.
- **List, load, save, delete and copy** a slot. Copy refuses to overwrite a slot
  that already holds a run unless you explicitly ask it to.
- **Your existing run is kept.** The first time the new code starts on a device,
  the one run saved by the current build is copied into slot 0 exactly as it
  was, together with its previous checkpoint. Historical records stay under
  their old keys. On Web, validated records can be compacted without changing
  their decoded bytes; compact records require this build or a newer compatible
  player. Migration happens once; deleting slot 0 later does not bring it back.
- **Saves are checked after writing.** Every save is read back and compared. If
  the stored bytes do not match or storage throws, the journal reports failure
  and attempts to restore the old primary. A surviving backup is not rolled
  back or deleted. Restoration can itself fail while storage is unavailable;
  the in-memory snapshot remains available for retry.
- **Damaged saves are never thrown away.** A slot whose latest save is damaged
  opens its previous checkpoint. A damaged save that gets overwritten is first
  set aside under its own key. A slot with nothing readable says so and keeps the
  bytes untouched.
- **Last 20 results.** Finishing a run adds its result to the profile. Only the
  20 newest results are kept (oldest leaves first), each labelled with its run
  ID. Win/run totals and unlocks keep counting past 20. Recording the same run
  twice does not count it again, but still retries the verified profile write.
- **Your profile carries over.** The profile uses the same storage key as
  before, so existing unlocks and history are unaffected.
- **One profile journal.** Solo, co-op, result recording and map preferences
  share the same journal instance, preserving the recovery state. A primary
  rejected for an unsupported schema is quarantined even if its checksum is
  valid. Logical record schemas and keys are unchanged.

### Web storage encoding and compatibility

Unity limits Web PlayerPrefs to [1 MiB](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerPrefs.html).
One actual authored slot, backup and copy occupied 1,078,215 UTF-8 bytes before
compression, reproducing the compiled failure. Web native storage now uses an
`ASZ1:` gzip/base64 encoding for large values, with bounded decoding. Plain
records remain readable. Native Windows and Android storage is unchanged.

The capacity fixture stores three complete slots and backups plus 20 results
in **454,286 bytes**. A migration fixture retains both historical records along
with all three slots/backups and a profile in **604,163 bytes**. Validated
historical records use a verified temporary encoding copy before replacement;
interrupted upgrades retry, unreadable records stay untouched, and conflicting
valid copies are preserved instead of choosing one. These are representative
authored fixtures, not an unlimited-storage guarantee.

**Older players cannot decode compact Web records.** Keep using this corrected
build or a newer compatible player after it updates Web saves. This changes
the storage encoding, not gameplay snapshots, checksums or profile rules. It
does not import saves from the original JavaScript game.

## For developers

### Files

| File | Role |
| --- | --- |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalSaveSlots.cs` | Entry point: slots, migration, profile and result archive |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalSaveStorage.cs` | `IOriginalSaveStorage`, delegate adapter, in-memory store |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalSaveJournal.cs` | Existing journal, extended with the storage interface, verified write, `Peek`, `Clear`, corrupt-primary quarantine |
| `Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalProfile.cs` | Existing profile, extended with `ResultArchiveLimit` (20) and `ResultArchive()` |
| `UnityTests/SaveSlots/` | Console checks (`dotnet run --project UnityTests/SaveSlots`) |

All of it is pure C# (no `UnityEngine`), compiled into `AshenSpire.Original`, and
builds as C# 9 on .NET Standard 2.1.

### Entry point for the Application layer

```csharp
var storage = new OriginalDelegateSaveStorage(
    key => PlayerPrefs.GetString(key, ""),
    (key, value) => PlayerPrefs.SetString(key, value),
    PlayerPrefs.Save,
    PlayerPrefs.DeleteKey);
var saves = new OriginalSaveSlots(storage, channel, buildVersion); // version from version.json

saves.MigrateLegacy();                                   // once at boot; idempotent
IReadOnlyList<OriginalSaveSlotInfo> slots = saves.List(); // State + Meta per slot
bool ok = saves.Save(slot, game.Snapshot(), playtimeSeconds);
var snapshot = saves.Load(slot, s => OriginalGameSession.Restore(s), out var meta, out var recovered);
saves.Copy(from, to, overwrite: false); saves.Delete(slot);

var profile = saves.LoadProfile(catalog, out var profileRecovered);
var receipt = saves.RecordResult(profile, game.RunPlayer, victory); // receipt["saved"]
JArray archive = profile.ResultArchive(); // [{ key: runId, result: {...} }], oldest first
```

A file-backed store only needs to implement `IOriginalSaveStorage`
(`Read`/`Write`/`Delete`/`Flush`).

### Storage keys (per channel)

| Key | Contents |
| --- | --- |
| `AshenSpire.Unity.Original.v1.<channel>` (+ `.backup`) | Legacy single run. Read by migration, never written. |
| `AshenSpire.Unity.Original.v1.<channel>.slot<n>` (+ `.backup`, `.corrupt`) | Slot `n` journal |
| `AshenSpire.Unity.Original.v1.<channel>.slots` | Migration marker (`legacyMigration` outcome, UTC time) |
| `AshenSpire.Unity.Profile.v1.<channel>` (+ `.backup`, `.corrupt`) | Profile (unchanged key) |

### Record format

Each slot is an `OriginalSaveJournal` envelope (`schemaVersion`, `data`, `sha256`)
whose data is
`{"schemaVersion":1,"meta":{class,act,floor,seed,playtimeSeconds,lastSavedUtc,version,runId,origin},"snapshot":{...}}`.
The snapshot is stored unchanged. Property order is fixed, JSON has no
whitespace, the time is `yyyy-MM-ddTHH:mm:ssZ` from an injectable clock, and
records are read with date parsing off. Together that makes the stored bytes
deterministic, and it makes migration byte-for-byte.

### Behaviour notes

- **Verified write:** `OriginalSaveJournal.Save` now returns `bool`. It rotates a
  valid previous primary into `.backup`, writes, flushes and reads back. On a
  mismatch it restores the previous primary bytes and returns `false`. Existing
  callers that ignore the result still compile and behave as before on success.
- **Migration outcomes:** `Migrated`, `NoLegacySave`, `SlotOccupied` (legacy
  left alone) and `AlreadyDone` write the marker. `LegacyUnreadable` and
  `WriteFailed` do not, so the next boot tries again.
- **Playtime** is the caller's running total in seconds. The domain does not
  keep a clock of its own.
- **Result archive:** `OriginalProfile.Finish` already kept the last 20 results
  and appends the run ID and the result together. `ResultArchive()` pairs them,
  so no second copy of the history is stored.

### Wiring (Application and Presentation)

| File | Role |
| --- | --- |
| `Runtime/Application/RunController.Slots.cs` | Creates `OriginalSaveSlots` over PlayerPrefs, runs `MigrateLegacy()` once in `OnEnable`, tracks the active slot and play time, saves checkpoints to the active slot, records finished runs with `RecordResult`, and handles load/new/copy/delete |
| `Runtime/Presentation/OriginalSlotPanel.cs` | The slot picker (rows, confirmations); owns no save state |
| `Runtime/Presentation/CampaignView.Slots.cs` | `SlotsRequested` event and `Slots(...)` screen |
| `RunController.cs`, `CampaignView.cs`, `OriginalTitlePanel.cs` | Hook lines only: title `Load` entry (`native-slots`), New/Continue routed through the slots, profile view reads `ResultArchive()` |

Control ids for playtests: `native-continue` and `native-new` are unchanged;
the picker adds `native-slots`, `native-slot-<n>-continue|new|copy|delete`
(`<n>` is 0-based), `native-slot-confirm`, `native-slot-cancel` and
`native-slots-back`.

The profile's map-viewer and co-op settings still save through the existing
profile journal on the same key.

### Still to check (needs the Unity editor)

- Play the title → Load → Continue/New/Copy/Delete flow in the editor and a Web
  player; confirm an existing single save appears in slot 1 and Continue
  resumes it.
- Rerun the browser playtests (`tools/native-*-playtest.cjs`,
  `tools/interruption-playtest.cjs`) against a new build.
