# Playable build library

The Pages library provides `/dev/`, `/test/`, `/release/`, `/main/`, `/history/`, and `/batches/`. Missing channels stay explicit. A development preview does not promote a protected branch or imply that game CI passed.

Each distinct committed browser player has `/builds/<content-id>/` with a Play button, build number, original version/date, exact commit, associated PR state, short change notes, original manifests, downloads, and available screenshots. Each channel and development branch also has build subpages. Build identity derives from committed Web blob IDs, so evidence-only changes and channel promotions do not duplicate a player. Legacy version labels remain unchanged.

## Hosting and integrity

Pages retains loader, framework, WebAssembly, launch pages, evidence, and indexes. Only `Web.data` is fetched from raw.githubusercontent.com at the full immutable archive commit. Keeping WASM on Pages preserves streaming compilation and avoids raw GitHub's incorrect WASM MIME fallback. Raw-host availability is an external dependency.

The assembler validates all Git blob identities and recorded SHA256 values, including omitted data bytes. The original export is unchanged in Git and downloads. A separate `hosting.json` records the original/derived launcher hashes and exact data URL/hash. Unknown templates fail closed. No archive is silently pruned and the 950 MiB size budget remains unchanged. Keep source branches and build commits reachable; deleting development branches can remove their membership from the next generated library.

## CI and publication

`Unity checkpoint and Pages` validates packaged Unity exports, domain tests, and actual browser behavior for PRs and channel/development branch pushes. Per-ref/SHA concurrency prevents unrelated batches from canceling each other's pending validation.

`Publish playable build library` runs from trusted default-branch tooling when checkpoint CI completes, or on manual dispatch. It refreshes repository refs, validates archive/storage contracts, checks generated links, verifies local runtime hashes and public data availability/CORS, uploads one combined site, then deploys it. Completed previews remain browseable even if game CI failed; each batch links to its CI results and PR state. The library run validates publication integrity, not game completion.

This pipeline publishes committed exports; it does not claim unattended Unity Editor compilation. Produce the Web/desktop/mobile packages using the existing Unity build tools, commit the completed package and change notes on the batch branch, then push it. The checkpoint CI and library update follow once the workflows have been installed on dev.

Only the Pages workflow owns deployment. Its global concurrency serializes publication; dev/test/main/release refs are read without mutation. The separate library workflow replaces deployment steps in the older checkpoint workflow.

## Bootstrap restriction

At preparation time, GitHub's `github-pages` environment permits only `dev`. The new workflow is prepared on `codex/pages-channel-storage` in PR41, not installed on dev. The owner can review/merge PR41 to activate automatic publication. Alternatively, after explicit owner approval for this permission change, permit exactly the publishing branch and dispatch the existing checkpoint workflow there with `publish_library_only=true`; this calls the same reviewed library workflow without promoting game branches. Do not silently broaden deployment to all branches.

## Validation checkpoint

The full local collection contains 21 archived players and is approximately 775 MiB. Local navigation and integrity evidence, browser screenshots, and exact final check counts accompany PR41. Real data-only cross-origin play passed creation, map, combat and card damage with zero browser errors. This is browser verification, not a physical-phone test or evidence of a completed public deployment.
