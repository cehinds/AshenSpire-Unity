# Archive hosting and build 28 publication

- [ ] **US-0.8 — public hosting acceptance**
  - [x] Build 28 Web, Windows, Android and companion downloads are public; all four downloaded files match their packaged SHA-256 receipts.
  - [x] Retain all 30 available archived players while reducing the assembled Pages site from 1,105.8 MiB to 382.5 MiB, below its 950 MiB budget.
  - [x] Keep WebAssembly on Pages for every selected channel and the four newest previews.
  - [x] Older WebAssembly and all archive data URLs point to their exact public Git commit; original payload bytes and hashes are verified before omission from Pages.
  - [x] 27 archive-plan tests, 26 history checks, 35 channel-storage checks, 3,447 navigation checks and 545 runtime/hash/CORS checks passed.
  - [x] Browser startup verified for the earliest Unity 6.4 archive, build 20 with remote WebAssembly, and build 28 with local WebAssembly. Screenshots and expected fallback logs are in [hosting evidence](qa/unity-build-28/hosting/checks.json).
  - [ ] Owner merge into `dev`, successful Pages deployment and verification of the public build-28 player.
  - [ ] Recover builds 15–19 if their distinct exported players exist outside the available committed history; those archives were not found and have not been invented.

The existing public deployment failed its size guard in [run 36539320478](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36539320478). It preserved the previous site. The revised assembler has been validated locally; **it has not yet been deployed**.

GitHub Pages currently permits deployments from `dev` only. [AGENTS.md](../AGENTS.md) and [CI/CD](CI-CD.md#branch-flow) reserve merges into `dev` for the owner. Once this change is merged, the existing build-library workflow can publish the candidate. No branch permissions or channel selections were changed.

The accumulated candidate is also blocked by the existing one-step version gate against `dev`: the base is build 21 and this branch is build 28. `unity-version check --base origin/dev` rejects that jump. Earlier candidates must follow the owner's integration sequence; this change does not weaken the gate or claim the draft is merge-ready.

Historical players receive WebAssembly from GitHub's raw endpoint with a generic binary MIME type. Unity 6.4 and 6.6 fall back from streaming compilation to ArrayBuffer instantiation. They log this handled fallback at console-error level; the validation receipt preserves those messages and distinguishes them from uncaught or unexpected errors. All three sampled players finished loading and displayed their compiled UI. This does not certify every old gameplay flow or old-browser compatibility. Current channel players and the four latest previews retain their existing streaming path.

The downloads are pinned to archive commit `4dc0e35e75e523203ce9c2d7a4aad0d5607ee3ae`:

- [Windows](https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/4dc0e35e75e523203ce9c2d7a4aad0d5607ee3ae/Published/Windows.zip)
- [Android](https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/4dc0e35e75e523203ce9c2d7a4aad0d5607ee3ae/Published/Android.apk)
- [Web files](https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/4dc0e35e75e523203ce9c2d7a4aad0d5607ee3ae/Published/Web.zip)
- [Co-op companion](https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/4dc0e35e75e523203ce9c2d7a4aad0d5607ee3ae/Published/Companion.zip)

See [download verification](qa/unity-build-28/public-downloads.json). Phase 1 and owner acceptance remain open.
