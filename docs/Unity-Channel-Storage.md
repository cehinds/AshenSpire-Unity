# Unity channel storage

Issue #40 addresses a publishing failure after build 12 was promoted into both Dev and Test. Their selected commit is `4b4a28dfe9aecc1a3292b498cd6a9310a6aef2fc`, containing merged PR #39. The prior assembler produces 1,206.3 MiB for those two channels, exceeding its unchanged 950 MiB guard.

## Representation

- Immutable `builds/<id>/Web` archives retain every original player byte, including the original index and build receipt. Archive identity continues to depend on the complete actual Web tree.
- Each channel keeps its own `/<channel>/Web/index.html`. For a recognized template, the assembler changes only the four loader/data/framework/WASM URLs to the matching immutable archive. Queries remain unchanged. The document URL, Unity product settings and streaming-assets location stay intact.
- `Web/channel-launch.json` records both index hashes, archive identity and selected commit. The channel entry is derived HTML; the original build receipt's index hash belongs to the untouched archived index. Do not claim the derived entry itself is an original exported byte.
- Identical complete evidence folders are stored once. Generated channel links point to the owning folder. A changed file makes the whole folder distinct, preserving relative links and historical evidence boundaries. Small channel manifests, validation and change notes remain local. Evidence folders must keep relative links within their folder; use exact-commit URLs for links outside it. Current evidence contains no parent-folder references. Cross-folder relative links would need explicit resolution before sharing.
- Unsupported player templates retain their entire original Web folder. Missing archive matches fail. Downloads continue to point to exact GitHub commits. No history is pruned and no game version or compiled payload changes.

## Current build presentation

A build can optionally commit `Published/presentation.json` to choose its current guide and screenshots without editing an older shared evidence folder:

```json
{
  "guide": "Published/CombatReadability/Guide.md",
  "screenshots": [
    "Published/CombatReadability/01-phone.png",
    "Published/CombatReadability/02-desktop.png"
  ]
}
```

The guide must be a Markdown file and the screenshot list must contain 1–32 distinct PNG paths, in display order. Every path must name an actual artifact in that channel's selected commit. Paths are limited to 512 characters and the manifest to 32,768 characters. The two documented fields are required; unknown fields, unsafe paths, missing files, wrong formats and malformed JSON stop assembly before evidence materialization. An explicitly invalid manifest never falls back to older screenshots.

With no manifest, the existing NativeEvidence, FoundationEvidence and older screenshot selectors remain unchanged. With a manifest, its guide and gallery replace those current-page selections. Links still resolve through the storage plan, so promotions can share the complete new folder too. Keep the existing `NativeEvidence` tree unchanged when adding a new presentation folder; updating one old screenshot would make that whole folder distinct. The manifest does not alter archived players, runtime identity, downloads or the 950 MiB limit.

## Validation

The Dev/Test assembly is 847.1 MiB, with 341 navigation checks across 54 library pages. A local four-channel fixture selects the same build for all channels and is 847.6 MiB, with 591 navigation checks across 86 pages. This fixture does not create or promote Release/Main branches. All 16 archived players remain; all 101 archived player files compare byte-for-byte with the previous assembler.

The Git-backed storage suite passes 33 groups covering exact archive identity, shared/divergent evidence, query and fragment preservation, legacy fallback, unsafe paths and current-presentation validation. Its nine added presentation groups include actual page assembly with current and absent metadata, promoted-folder sharing, and explicit malformed-manifest refusal. These use small synthetic committed files; they are not new Unity gameplay evidence. The original 24-group receipt is preserved in `Checks/storage-unit.log`; the 33-group rerun is in `Checks/storage-presentation.log`.

The existing build-history suite passes 13 checks and the materializer suite passes 16. The original Node regression passes 136/136; verify-shipped and buildversion pass. An initial workflow edit changed a self-test anchor; it was corrected by preserving the existing step and adding a separate storage step. The original failure and successful rerun are retained. The capacity, archive-byte and compiled-browser results below and above belong to the original build-12 assembly; the presentation extension does not claim a fresh runtime playtest.

The actual compiled player passes 32 browser checks at a 390×844 phone viewport. A single browser profile starts Dev seed 1 and Test seed 2, enters combat in each, closes both documents and revisits each to resume its exact independent state. It also observes correct channel labels, unchanged document URLs, shared runtime URLs, matching source digests and zero browser/Unity errors. HTTP 304 is accepted only when that exact URL previously returned 200 in the same profile. Four screenshots accompany the state/network receipts. This is browser emulation, not physical-phone certification.

The game remains 0.0.12.0 / build 12, source digest `d58bf5ccee7e45bd2afc94de693aad5ca872d41cdbfb754de8cf3c9f6637c772`. Package verification remains 441 companion, 160 native-file and 18 root-package checks.

Run:

```powershell
node tools/unity-channel-storage.test.mjs
node tools/unity-pages.mjs
node tools/unity-pages-links.test.mjs
# Serve the resulting site, then use an installed Playwright module:
node tools/unity-channel-playtest.cjs http://127.0.0.1:8804/ TestResults/ChannelPlayers
```

Use a fresh UNITY_PAGES_OUT for capacity measurements. The assembler preserves existing output and does not clean an old duplicated site. Existing channel refs are read from origin by default; explicit UNITY_PAGES_<CHANNEL>_REF overrides affect only local assembly. GitHub API credentials populate verified merged-PR associations; unknown associations remain labeled unknown.

## Delivery boundary

PR #39 is merged and the initial Test branch points to the exact same Dev commit. The storage changes are a separate candidate for owner review. Those merges alone do not establish live Pages deployment. The last live check still showed the historical 0.9.0 Dev manifest and no Test manifest; current GitHub validation/deployment status must be checked separately.

Further large, genuinely different runtime archives still consume capacity. This change removes duplicate selected-channel payloads; it does not promise unlimited storage or waive the 950 MiB guard.
