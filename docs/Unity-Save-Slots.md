# Unity save slots and profile archive (F10)

**Integration status: domain ready, UI wiring pending (needs Unity editor).**

## What this is, in plain words

The HTML game lets you keep three runs going at once, one per save slot, and it
keeps a durable profile with your last 20 run results. Until now the Unity build
kept only one run per channel (Web, Dev, Test, desktop).

This change adds the game rules for three slots and the result archive to the
Unity port's engine-free code. Nothing on screen changes yet: the title-screen
slot picker still has to be built and wired up in the Unity editor.

What the new code already does:

- **Three slots (0, 1, 2).** Each slot remembers the class, act and floor, the
  seed, total play time, when it was last saved (UTC) and the game version.
- **List, load, save, delete and copy** a slot. Copy refuses to overwrite a slot
  that already holds a run unless you explicitly ask it to.
- **Your existing run is kept.** The first time the new code starts on a device,
  the one run saved by the current build is copied into slot 0 exactly as it
  was, together with its previous checkpoint. The old save is left in place, so
  an older build can still open it. This happens once; deleting slot 0 later
  does not bring the old run back.
- **Saves are checked after writing.** Every save is read back and compared. If
  the stored bytes do not match (full storage, interrupted write), the slot keeps
  its previous good save and the save reports failure instead of pretending it
  worked.
- **Damaged saves are never thrown away.** A slot whose latest save is damaged
  opens its previous checkpoint. A damaged save that gets overwritten is first
  set aside under its own key. A slot with nothing readable says so and keeps the
  bytes untouched.
- **Last 20 results.** Finishing a run adds its result to the profile. Only the
  20 newest results are kept (oldest leaves first), each labelled with its run
  ID. Win/run totals and unlocks keep counting past 20. Recording the same run
  twice does nothing.
- **Your profile carries over.** The profile uses the same storage key as
  before, so existing unlocks and history are unaffected.

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

### Pending (needs the Unity editor)

- Swap `RunController`'s `_originalSaves` / `_profileSaves` for one
  `OriginalSaveSlots`, track the active slot and playtime, and call
  `MigrateLegacy()` on boot.
- Build a title-screen slot picker (Load/New/Copy/Delete) and a result-archive
  view.
- Show a failed save (`Save` returning `false`) to the player.
