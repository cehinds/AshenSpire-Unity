# Build 25: profile recovery and save retries

Version **0.0.25.0**, build **25**. Source digest:
`b357d41b5ca103dcc40241eb1fd0637d23920564b18d1fb90d1ba0937131e929`.
Recorded source commit: `5ebcf61665461ed21c8834d2c15ae2a5613f9455`.

Damaged profiles now return a useful title message and preserve existing records.
A recovered backup is identified explicitly. Solo, co-op, result history and map
preferences share one recovery journal. Failed writes keep progress in memory,
show a warning and retry; repeated finished-run saves cannot falsely report
success. Unsupported primary records are quarantined before replacement, even
when their checksums are valid. Logical schemas, keys and game rules are unchanged.
Web adds compact ASZ1 storage: existing plain records are readable, but compact
records require this corrected build or a newer compatible player.

## Verification

- **148 storage checks**, including 84 new regression assertions covering thrown
  reads/writes/flushes, partial writes, failed rollback, first-save failure,
  repeated result persistence, backup recovery, unsupported profile schemas,
  full three-slot capacity, retained legacy data and interrupted compaction.
  Failure-injection regressions also fail against the previous journal before the fix.
- **36 Unity-compiled controller/view checks**: failed title actions retain both
  records, recovered backup warnings, shared journal ownership, failed result
  retry, immediate warning updates and map preference retry flags. These use
  isolated storage, not actual player records or physical input.
- **16 compiled Web checks** at 390×844 using normal pointer/keyboard
  input: fresh/saved Collection, character creation, slot copy, cancelled delete,
  reload, exact checkpoint restoration and resumed combat. No injected game data.
- Packaged companion: **8 + 6 checks** with two browser players,
  a shared fight, rewards, exact-hand rejoin and next-turn actions.
- Current compiled enemy catalog: **78 checks**, all **19 painted enemies**,
  **38 phone/desktop captures**. This proves asset loading and registration, not
  all natural encounters or owner visual acceptance. [Portrait review](../../../Published/BuildReview25/Guide.md).
- Standard Domain, Parity, CardText, CardCosts, HandRules, MapKnowledge and
  MapViewport prechecks passed before the storage-encoding correction; game-rule
  sources are unchanged. The 148 storage checks and compilation were rerun for
  the correction. C# 9/.NET Standard compilation had zero warnings/errors;
  runtime reference compilation passed **125 checks** with
  only the three previously documented Unity 6 reference-package gaps.
- Build version gate: **six checks** against build 24. Review/browser tool
  regression tests: **12 passed**.
- Unity 6000.6.0f1 exported Web, Windows and Android; the companion was packaged.
  **452 companion, 163 native-file and 20 package checks** passed. All exported
  receipts and copied downloads match the source digest above.

## Limits

The first compiled build-25 test reproduced a real copy failure: one authored
slot, backup and copy used 1,078,215 UTF-8 bytes, exceeding Web PlayerPrefs'
[1 MiB limit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerPrefs.html).
The corrected normal-input test fills all three slots and updates their
backups. Fixed-clock domain capacity fixtures use 454,290 bytes for three slots/backups and
20 results, or 604,139 bytes with retained historical records and a profile.
Historical compaction verifies a temporary recovery copy before replacement.

The final all-target export and its Web-only retry encountered a Unity Package
Manager IPC startup timeout. Packaging reuses the earlier successful corrected
Web export, whose source digest and files match the tested player and native
exports. Both startup failure logs were preserved; no receipts were rewritten.
The first portrait sweep stopped scrolling before some headings were fully
visible. The capture check now waits for Unity frames and requires the header's
full position before taking a screenshot. The repeated 38-capture pass uses that
stronger check; the earlier captures remain preserved as diagnostic evidence.

This remains a local candidate. Build 22's [draft PR #56](https://github.com/cehinds/AshenSpire-Unity/pull/56)
awaits owner merge, and dev remains build 21. No gate was weakened, no agent
merge occurred and no channel was promoted. F00–F17 remain unaccepted.

The real player copy test covers Unity's Web PlayerPrefs limit. Injected C#
storage failures still do not prove asynchronous IndexedDB quota handling,
physical-device save durability or a complete upgrade matrix. The browser suite
uses fresh isolated contexts. Full modes/content/inventory acceptance, real
Android and graphical Windows play, iOS delivery, device performance targets,
owner visual/profile acceptance and real-player pacing remain open. The
original JavaScript save-import decision is still pending.
