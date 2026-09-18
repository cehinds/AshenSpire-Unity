# Pages archive capacity: read-only audit

PR41 is open at `cfcd7f2bf4b13a1d6914dc64b86540096f946313`. Measurements use that worktree's history/channel-storage helpers without materializing, editing or publishing a site. Local remote-tracking `origin/dev` and `origin/test` both resolve to `4b4a28dfe9aecc1a3292b498cd6a9310a6aef2fc`; they were not fetched or represented as freshly verified live branch refs.

**Build13 plus build14 will exceed the unchanged950MiB budget. Exact duplicate removal alone is insufficient.**

| Scenario | Measured or estimated MiB |
|---|---:|
| Existing two-channel assembled build12 preview |847.125 |
| Build12 selected planned artifact bytes,16 archives |825.268 |
| Existing reference/generated-page overhead |21.857 |
| With preserved build13 commit79dcdbd,17 archives, planned artifact bytes |925.209 |
| With13 plus existing overhead (estimate) |947.066 |
| Current generated14 Web (9a5445…, not final forthcoming export) |99.109 |
| With13 and this14 player, before new14 evidence (estimate) |1046.175 |
| Excess above950 |96.175 |
| All remaining exact duplicate bytes in with13 plan |87.858 |
| Even removing every known duplicate, with14 (estimate) |958.317 |

The build13 estimate is already within2.934MiB of the guard. Generated pages differ slightly as new history entries are added; no final assembly or gate pass is claimed. Final14 output and evidence are not yet known. The current14 Web measures103,923,822bytes. Raw size and duplicate receipts are in `git-planned-bytes.json`.

## Largest remaining duplicates

Two pairs of older archive WASM binaries account for roughly49.57MiB of recoverable duplicate storage:

- 25,994,385bytes repeated at `build-551108fbfa0811306a32` and `build-96f1a98f8c15738c1be5`.
- 25,985,649bytes repeated at `build-20092a47c718ce1f70bf` and `build-de5fd6d6090e6b00acf8`.

Both paths end `Web/Build/Web.wasm`. These are exact Git blob matches, not similar builds. Across the with13 plan, archive-only duplicates total53,995,645bytes; evidence-only duplicates38,041,476bytes; cross-category duplicates88,974bytes.

The largest evidence groups are repeated historical screenshots: the432,402-byte mobile combat PNG appears five times; a420,531-byte class-selection PNG appears five times; a357,619-byte failure PNG appears five times. Copies of the442,564-byte draft-return screenshot occur across renderer and seed evidence. Full membership and blob identities are in the receipt.

## What a sound storage change can and cannot do

PR41 already shares identical evidence *folders between channels* and points channel launchers at a shared immutable archive. Promoting the same build to another channel no longer duplicates its runtime. The remaining runtime duplicates are individual blobs inside different archived Web trees.

A content-addressed payload pool could retain each distinct WASM/data/framework/loader byte exactly once. Derived launch pages would resolve their four payload URLs to the pool, while original index bytes and manifests would need explicit preservation. This is **not** a transparent change under the current promise that each archived player tree, including its original entryHTML and relative runtime paths, stays byte-for-byte materialized. Do not silently rewrite archived HTML, replace binary paths with HTML redirects, or assert link validation proves original bytes survived.

Whole-folder evidence sharing is safest for relative links. Per-file sharing must rewrite every reference or retain a compatible URL mapping. Hash equality alone does not make removing an old path safe. Even a perfect implementation of all known duplicate sharing still misses the budget by8.317MiB before new evidence, so it cannot be the complete solution.

Hard links or symlinks are not a capacity workaround: the Pages upload contract disallows them. [Official upload-pages-artifact requirements](https://github.com/actions/upload-pages-artifact/blob/main/README.md)

## Smallest viable direction to investigate next

Keep every archived runtime tree and channel/build entry URL in the main site, and move whole evidence folder trees to a **separate free GitHub Pages project** (or link exact-commit evidence in GitHub). Current selected evidence/channel bytes are164.046MiB, enough to restore working room while preserving the games unchanged. A separate Pages project preserves normal browser image/text serving and internal relative links; the main gallery links would explicitly target it.

This requires a deliberate hosting/URL decision and verification. It does not automatically preserve direct legacy evidence URLs, and no new repository or publication is authorized by this audit. Exact-commit GitHub links preserve the source bytes but provide a different viewing experience. Raw images may work in galleries, whereas raw JavaScript/WASM hosting is not an equivalent runtime server.

For ongoing regular builds, a single finite950MiB site cannot retain an unbounded sequence of unique100MiB players. After the immediate evidence move, establish an archive-shard policy or separately verified lossless compressed-payload launcher. Neither should remove original artifacts, invalidate current saves, weaken the limit, or be presented as already implemented.

GitHub documents a1GB maximum published-site size; the repository's950MiB guard remains unchanged. [GitHub Pages limits](https://docs.github.com/en/pages/getting-started-with-github-pages/github-pages-limits)

No archives, evidence, repository files, limits or running jobs were changed. No Pages build was executed or published during this audit.
