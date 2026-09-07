# Playable build history

The GitHub Pages library has a current page for `dev`, `test`, `release`, and
`main`, plus a **Browse previous builds** page at `history/`. Each channel lists
its committed browser builds with the build number, original version, build date,
associated PR when known, and short Added / Changed / Fixed notes. The compare
link shows changes since the preceding distinct build in that channel.

Each channel retains a stable build page:

```text
dev/builds/build-<content-identity>/
test/builds/build-<content-identity>/
release/builds/build-<content-identity>/
main/builds/build-<content-identity>/
```

Those pages play an exact archived browser export under
`builds/build-<content-identity>/Web/`. A shared archive prevents promoted builds
from duplicating the same large browser files in every channel. Current links
such as `dev/Web/` remain available. Historical Web ZIP, Windows ZIP, Android APK
and complete evidence link to the original files at their exact Git commit.
Current ZIP/APK downloads, including the co-op companion, use exact-commit GitHub
links too. Pages retains the playable browser files and screenshots; distributing
native packages from GitHub keeps room for the complete browser history.

## Version and provenance

The four fields are **game release · roadmap milestone · incremental upgrade ·
patch**. Foundation work stays below `0.1.0.0`; `1.0.0.0` denotes the completed
game. Old builds retain their original three-part version labels and bytes. The
library does not relabel an old `0.9.0` executable as `0.0.9.0`.

New manifests provide a numeric `buildNumber`. Older manifests receive an
explicit `legacy-<version>-<identity>` identifier; this is not an invented
historical build counter. Dates come from `Published/build.json`, never from the
site assembly date. Missing dates and PRs remain unknown.

PR attribution uses an explicit `pullRequest` number in the build manifest or
GitHub's associated-pull-requests API for the commit that introduced the player.
An issue reference in a commit message is not treated as a PR. API failures do
not invent attribution or block access to an otherwise valid archived build.

## How it is assembled

`tools/unity-build-history.mjs` walks each channel's first-parent Git history of
`Published/build.json`. It identifies the actual committed browser files, not
the version label, and keeps the first occurrence of each distinct player.
Evidence-only commits and channel promotions do not add duplicate players.
Branches without a committed build remain explicitly empty. Git ancestry proves
channel membership; this is a committed-build library, not an assertion that
every historical commit was deployed to Pages at that time.

Only `Published/Web/` and the archived build, changelog and validation JSON are
copied into historical runtime folders. Their stored hashes are checked where
available. Existing archive bytes cannot be overwritten by different bytes.
Source code, credentials, caches and the whole repository are never exported.
The latest channel pages keep their usual screenshots and downloadable players.

`history.json` exposes the machine-readable index. Associated PR metadata can be
refreshed as GitHub attribution becomes available; archived player bytes and
original manifests remain unchanged.

## Verify and preview

```powershell
node tools/unity-build-history.test.mjs
$env:UNITY_PAGES_OUT = 'Builds/HistoryPreview'
node tools/unity-pages.mjs
node tools/unity-pages-links.test.mjs Builds/HistoryPreview
python -m http.server 8795 --directory Builds/HistoryPreview
```

Use a new output directory for each preview. The tests create isolated fixture
repositories under `Builds/HistoryTests`; they do not change project branches.
Set `GITHUB_TOKEN` through the environment for read-only PR lookup; never put a
token in command arguments or output files. The Pages workflow supplies its
read-only token automatically. Deployment still follows the existing branch
workflow and does not promote a build to another channel.

There is an explicit 950 MiB site budget. As history grows, a larger runtime
archive host may be needed. Assembly fails with an actionable size error rather
than silently deleting older builds. Historical save compatibility and bugs are
preserved along with the player; an archived build is not a new supported release.
