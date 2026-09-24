# Modding AshenSpire (Unity build)

A **mod pack** is a folder of JSON files that adds, changes or removes game content:
cards, relics, enemies, encounters, events, flasks, classes, equipment and more. Packs
contain data only. They cannot add code, and they cannot add new kinds of effects. They
build on the effect operations the game already supports.

> **Integration status: wired; compile-verified against Unity reference assemblies; needs
> editor play test.** The loader, its validation and the sample pack are finished and
> tested in plain .NET (`UnityTests/Mods`). The game reads `StreamingAssets/Mods` only when
> the player turns on **Settings → Content mods → Load content mods** (off by default).
> With it off, nothing is read and the shipped content is used exactly as before. See
> [Wiring it in](#wiring-it-in-application-layer).

## Folder layout

```
Unity/Assets/StreamingAssets/Mods/
  sample-ember-pack/          <- one folder per pack; folder name = pack id
    mod.json                  <- the manifest (required)
    cards.json                <- any number of content files, any names ending .json
```

The loader reads every `*.json` file in the pack folder except `mod.json`, in
alphabetical order. It ignores Unity `.meta` files.

## The manifest: `mod.json`

```json
{
  "id": "sample-ember-pack",
  "name": "Sample Ember Pack",
  "version": "1.0.0",
  "gameVersionMin": "0.5.5",
  "loadOrder": 100,
  "dependsOn": [],
  "author": "AshenSpire",
  "description": "Adds Ember Brand and makes Shield Bash hit harder."
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Lowercase letters, digits, `.`, `_`, `-` (up to 64 characters). **Must match the folder name.** |
| `name` | yes | Display name. |
| `version` | yes | Your pack's version, dotted numbers (`1`, `1.2`, `1.2.3`). |
| `gameVersionMin` | no | Oldest game content version the pack works with. It is compared with `"version"` at the top of `GameContent/Unity/Original/content.json` (currently `0.5.5`). If the game is older, the pack is refused. |
| `loadOrder` | no | Whole number, default `0`. Lower numbers load first. When two packs change the same record, the pack that loads **last** wins. |
| `dependsOn` | no | Ids of packs that must load first. A missing dependency, or a cycle (A needs B, B needs A), refuses the pack. |
| `author`, `description` | no | Free text. |

The loader refuses any other field, so a typo such as `"loadorder"` is reported
instead of being silently ignored.

**Load order:** a pack's dependencies always load before it. Apart from that, packs
load by `loadOrder`, then by `id` alphabetically. The order is the same every time.

## Content files: add, override, remove

A content file has the same shape as `content.json`, but it only contains the tables
you change. Each record must be complete and use **exactly** the same fields as the
shipped records. The easiest way to write one is to copy a record from `content.json`
and edit it.

```json
{
  "cards": [
    { "id": "emberBrand", "name": "Ember Brand", "class": "colorless", "...": "..." },
    { "id": "shieldBash", "name": "Shield Bash", "class": "reaver",   "...": "..." }
  ],
  "remove": {
    "cards": ["someUnusedCard"]
  }
}
```

- **Add:** a record whose `id` does not exist yet is added at the end of the table.
- **Override:** a record whose `id` already exists **replaces** that record completely
  and keeps its position. Fields are not merged, so include every field.
- **Remove:** list ids under `"remove"`, table by table. Removals run before adds and
  overrides within the same pack. You cannot both remove and supply the same id in one
  pack. To replace a record, supply it without removing it.

**Tables you can change:** `cards`, `relics`, `statuses`, `stances`, `resources`,
`keywords`, `enemies`, `encounters`, `events`, `flasks`, `classes`, `unlocks`,
`tagDomains`, `tags`, `attributes`, `creationModes`, `balance.flaskGrowth`,
`balance.graceRefill`, `equipment.armaments`, `equipment.slots`,
`equipment.basicCardProfiles`, `equipment.startingKits`, `equipment.equipmentGrants`,
`characterCreation.equipmentSections`, `characterCreation.keepsakes`.

A nested table is written with a dot as its key, for example
`{ "equipment.armaments": [ ... ] }`. The loader works this list out from
`content.json` itself: any table whose records all have a unique `id` can be modded.
Tables without ids, such as `tagging` or the armour list keyed by class, cannot be
modded yet.

A new card does not show up in rewards on its own. To put it in a class's pool, override
that class record in `classes` and add the card id to its `cardPool`.

## The sample pack

`Unity/Assets/StreamingAssets/Mods/sample-ember-pack/` shows both operations:

- **adds** `emberBrand`, a common colorless attack: deal 5 damage and apply 2 Burn
  (upgraded: 7 damage, 3 Burn).
- **overrides** `shieldBash`: damage goes from 5 to 6 (upgraded: 8 to 9). Everything else
  stays as shipped.

## Validation errors

Bad packs never crash the game. The loader returns a list of errors. Each error has a
code, the pack id, the file and position, and a plain message. A pack with any error is
**refused whole**, and the game keeps the content it had before that pack. Other packs
still load, unless they depend on the refused one.

| Code | What it means | Typical fix |
|---|---|---|
| `manifest-missing` | Folder has no `mod.json`. | Add one. |
| `manifest-invalid` | Required field missing, unknown field, bad id/version/loadOrder, or `id` does not match the folder. | Fix the named field. |
| `json-invalid` | A file is not valid JSON (or repeats a key). | Check commas and quotes. |
| `duplicate-mod` | Two packs claim the same id. | Rename one. |
| `game-version-too-old` | `gameVersionMin` is newer than this game's content. | Update the game, or lower `gameVersionMin` if the pack really works. |
| `missing-dependency` | A `dependsOn` pack is not installed. | Install it or remove the dependency. |
| `dependency-rejected` | A dependency was itself refused, or is stuck in a cycle. | Fix the dependency first. |
| `dependency-cycle` | Packs depend on each other in a loop; the message shows the loop (`a -> b -> a`). | Break the loop. |
| `unknown-table` | A key in a content file is not a moddable table; the message lists the valid ones. | Check spelling. |
| `record-invalid` | A record is not an object with a string `id`, or `remove` is malformed. | Fix the record. |
| `duplicate-record` | Same id twice in one pack, or both removed and supplied. | Keep one. |
| `schema-mismatch` | A record is missing a field every shipped record has, or has the wrong type (e.g. `effects` must be an array). | Copy a shipped record and edit it. |
| `remove-unknown` | Removing an id that does not exist (at that point in load order). | Check the id / load order. |
| `validation-failed` | The game's own content check refused the result: an unknown status/card/relic id in an effect, a class pool naming a missing card, removing a card something still uses, an unknown effect `op`, and so on. The message is the checker's. | Fix the reference it names. |

These are the same checks the shipped content passes (`OriginalContentCatalog` and
`OriginalContentValidation`). A modded game follows the same rules as the base game.

## Testing a pack

From the repository root (.NET 8 SDK):

```
dotnet run --project UnityTests/Mods
```

This runs the loader and settings checks. It includes loading the sample pack straight
from `Unity/Assets/StreamingAssets/Mods`, and it ends with `Mods: N checks passed`. It
exits with code 1 if a check fails.

The suite expects the sample pack to be the only pack in `StreamingAssets/Mods`. To
check your own pack, keep it in a separate folder (for example `scratch/Mods/my-pack/`).
Then point the loader at that folder from a scratch program or a new check in
`UnityTests/Mods/ModPackChecks.cs`:

```csharp
var result = OriginalModPacks.Load(File.ReadAllText("GameContent/Unity/Original/content.json"),
    new OriginalModDirectorySource("scratch"));          // reads scratch/Mods/<pack>/
foreach (var error in result.Errors) Console.WriteLine(error);   // [code] pack: file#table[i]: message
foreach (var change in result.Changes) Console.WriteLine(change); // pack add|override|remove table/id
```

## Wiring it in (Application layer)

The single entry point is:

```csharp
OriginalModLoadResult OriginalModPacks.Load(
    string baseContentJson,            // Resources/Original/content text, as today
    IOriginalModFileSource files,      // mods folder access
    string root = "Mods",              // folder under `files` with one sub-folder per pack
    string gameVersion = null);        // defaults to content.json "version"
```

`result.Catalog` is an ordinary `OriginalContentCatalog`. With no packs installed it is
record-for-record identical to today's catalog, and `result.ContentJson` is the
unchanged input string.

How it is wired (`Unity/Assets/AshenSpire/Runtime/Application/RunController.Settings.cs`):

1. **Opt-in.** `OriginalPlayerSettings.LoadContentMods` (`loadContentMods` in
   `AshenSpire.Settings.v1`) is off by default. When it is off, `ModdedCatalog()` returns
   null without touching the file system. Both catalog builders
   (`RunController.LoadOriginalProfile` and `OpenFoundation`) then build the shipped catalog
   exactly as before, so the sample pack does not load.
2. **When content loads.** The first time a new run, the profile, co-op or the foundation
   screen needs the catalog, `OriginalModPacks.Load` runs with
   `new OriginalModDirectorySource(Application.streamingAssetsPath)`. The accepted catalog
   is used. Turning the toggle on in Settings loads the packs at once, so the result can be
   shown. Changing the toggle drops the cached catalog and profile, so the next route into
   play uses the new choice. A run in progress keeps the content stored in its own
   snapshot.
3. **Platforms.** Desktop and the editor read the folder directly. WebGL is skipped with a
   message in Settings, because StreamingAssets can only be read there with web requests.
   Android is skipped the same way: its StreamingAssets path is inside the APK
   (`jar:file://…`), and `System.IO` cannot read that. Reading it through `UnityWebRequest`
   into an `OriginalModMemorySource` is the remaining work for both platforms.
4. **Visible results.** Settings → Content mods lists each loaded pack
   (`Loaded: name version (id)`), each refused pack (`Refused: id`), and the loader errors
   (`[code] pack: file: message`, up to 12; the rest go to the player log). If the base
   content itself is refused, a note says so. In development builds `ASHENSPIRE_MODS` logs
   a JSON summary.
5. **Co-op (not done).** Every seat must run the same packs. The companion validates
   characters against its own data. Comparing a hash of `result.ContentJson` before a
   shared run starts is still to do. Until then, keep mods off for co-op.

Play test in the editor: turn on Load content mods. Check that Settings lists
`Loaded: Sample Ember Pack 1.0.0 (sample-ember-pack)`. Start a new run as a class that
has Shield Bash and check that it deals 6 damage (the sample pack does not put Ember Brand
in any reward pool). Turn the toggle off, start another run, and check that Shield Bash is
back to 5.
