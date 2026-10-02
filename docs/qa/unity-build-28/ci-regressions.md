# Build 28 follow-up regression findings

The 879 scoped compiled-browser checks in the build-28 report passed. The broader PR pipeline also exposed failures; that total is not an all-CI-green claim.

- [x] Seed/interruption tests waited behind the first-visit welcome. The harness now enters through the actual visible control. Build-28 reruns pass [25 seed checks](seed-entry.json) and [44 interruption checks](interruption.json).
- [x] Feedback navigation passes with gutter scrolling and a sufficient bounded scrolling budget for the expanded Settings screen; [normal/fast/reduced motion, mute, reload and navigation cancellation pass](feedback.json).
- [x] Build 29 passes mobile touch navigation across 12 layouts and six landscape inspections. The old centre swipe changed a settings slider instead of scrolling. The movement assertion now accepts the player lunge or enemy recoil, matching the separate feedback suite: a slow frame can observe the player already settled while the enemy still moves.
- [x] Build 29 fixes the legacy full-campaign flask regression: OriginalTheme's 32px body padding replaced Expedition's 110px footer clearance. A scoped `has-fixed-actions` style restores reachability. Nine focused checks and the complete reference campaign pass; build 28 is not relabeled as fixed. See [build-29 QA](../unity-build-29/README.md).
- [ ] Owner integration must resolve the existing one-step version gate against build 21 on `dev`.

Initial CI run: [36542521820](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36542521820). The initial failure artifacts remain attached to that run. This is a development candidate, not completed Foundation acceptance.
