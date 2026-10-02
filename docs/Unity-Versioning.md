# Game version and build identity

Constantine's version format is `A.B.C.D`:

| Part | Meaning | Who changes it |
|---|---|---|
| **A** release | `1.0.0.0` = the finished game | Owner only |
| **B** completed features | Count of features marked `done` in the [feature tracker](Unity-Roadmap.md#feature-tracker) | Agent, when a feature's acceptance passes and the owner accepts it |
| **C** user story | User stories completed inside the feature currently in progress | Agent, per delivered story |
| **D** patch | Fixes to the current story/feature | Agent, per fix |

Bump rules:

- A fix → **D+1**.
- A user story delivered → **C+1**, **D=0**.
- A feature complete → **B+1**, **C=0**, **D=0**.
- **`BuildNumber` always +1** on every bump, whichever part changed.
- **A** is never bumped by an agent. `1.0.0.0` is the owner's call.

While F00 Foundation is in progress, B stays 0; completing F00 is the first
completed feature and selects `0.1.0.0`, the milestone already reserved below.
Record the feature ID (`F0x`) and story ID (`US-x.y`) from the roadmap in the note.

## Version tool

`GameContent/Unity/version.json` holds `Version`, `BuildNumber` and `Stage`.
Change it with the tool rather than by hand:

```sh
node tools/unity-version.mjs show
node tools/unity-version.mjs bump patch   [--feature F00] [--note "Fix card cost label"]
node tools/unity-version.mjs bump story   [--feature F00] [--note "US-0.2 custom-mode save/resume"]
node tools/unity-version.mjs bump feature [--feature F00] [--note "Foundation accepted"]
node tools/unity-version.mjs check --base origin/dev   # CI gate
```

- `show` prints the current version, build number and stage.
- `bump` applies the rule above, increments `BuildNumber` and appends an entry to
  [Unity-Changelog.md](Unity-Changelog.md) (`docs/Unity-Changelog.md`) with the
  feature and note.
- `check --base origin/dev` is the CI gate: it compares against the base branch
  and fails when Unity sources changed without a valid bump (or the bump breaks
  the rule, e.g. BuildNumber not increased).

**Rebuild requirement.** Any change under `Unity/Assets`, `Unity/Packages`,
`Unity/ProjectSettings` or `GameContent/Unity` — **including `version.json`
itself** — changes the source digest those four trees feed
(`tools/unity-package.mjs`). `node tools/unity-package.mjs --check` then fails
until the owner rebuilds with `.\tools\build-unity.ps1` (Windows, Unity
`6000.6.0f1`) so `Published/` matches the new source. Cloud agents cannot run
the Unity editor, so a version bump in a PR means the PR also needs an owner rebuild.

## Historical notes

The earlier wording of the format was
`<game release>.<roadmap milestone>.<incremental upgrade to milestone>.<patch>`;
the table above is the same four parts with the milestone defined as the
feature tracker. The notes below are preserved as written.

- `0.0.9.0` is the corrected identity for the ninth pre-foundation increment.
- `0.0.9.1` patches that increment's allocation defaults and version metadata.
- `0.0.10.0` was the substantial pre-foundation increment at the time this note was written, with native
  combat/run/equipment integration. Its stage remains Foundation in progress.
- `0.0.10.1` would patch this increment; `0.0.11.0` would be a later substantial
  pre-foundation increment. These are examples, not published versions.
- `0.1.0.0` is reserved for the completed foundation milestone.
- `1.0.0.0` is reserved for the completed game.

An imported catalog, a development preview or a passing subset of rules does not
complete the foundation. Its components must be connected into the native game
flow, with equipment-sourced cards, useful resources, save/restore and tested
original behavior. The parity checklist remains the detailed scope and records
any intentional owner-requested differences from the reference game.

`GameContent/Unity/version.json` is authoritative. The Unity builder reads it into
platform metadata, and the package tool checks the same version. Do not separately
change a label in the UI or hand-edit a generated Web player.

`BuildNumber` is a separate increasing checkpoint number shared by the Web,
Windows and Android exports of the same source checkpoint. The explicit sequence
starts at 10 for the current corrected-format checkpoint; older artifacts retain their
existing Git/build identities. Android also uses this value for its version code.
Increase it for the next delivered source checkpoint. A rebuild of unchanged
source retains the checkpoint number; the source digest and build date identify
the exact export.

The current version and build number are whatever `GameContent/Unity/version.json`
declares (read it, or run `node tools/unity-version.mjs show`); this page does
not repeat the number so it cannot go stale. The file does not change historical
records or prove that a channel already hosts that export. Read its build manifest before identifying a published version.

The historical library preserves bytes and original metadata. A legacy artifact
that says `0.9.0` is not rewritten to pretend it originally shipped as `0.0.9.0`.
Its record can explain the mapping. New exports use the four-part format.

Each build record should show its channel, version, build number, date, source
identity, associated PR and a short change list. An unknown historical PR or
build number is labeled unknown rather than inferred from an issue reference.
