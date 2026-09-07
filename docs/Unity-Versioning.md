# Game version and build identity

Constantine's version format is:

`<game release>.<roadmap milestone>.<incremental upgrade to milestone>.<patch>`

- `0.0.9.0` is the corrected identity for the ninth pre-foundation increment.
- `0.0.9.1` patches that increment's allocation defaults and version metadata.
- `0.0.10.0` is the current substantial pre-foundation increment, with native
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

The current source file declares version `0.0.10.0` and build number `10`.
This does not change historical records or prove that a channel already hosts
that export. Read its build manifest before identifying a published version.

The historical library preserves bytes and original metadata. A legacy artifact
that says `0.9.0` is not rewritten to pretend it originally shipped as `0.0.9.0`.
Its record can explain the mapping. New exports use the four-part format.

Each build record should show its channel, version, build number, date, source
identity, associated PR and a short change list. An unknown historical PR or
build number is labeled unknown rather than inferred from an issue reference.
