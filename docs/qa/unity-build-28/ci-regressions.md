# Build 28 follow-up regression findings

The 879 scoped compiled-browser checks in the build-28 report passed. The broader PR pipeline also exposed failures; that total is not an all-CI-green claim.

- [x] Seed/interruption tests waited behind the first-visit welcome. The harness now enters through the actual visible control. Build-28 reruns pass [25 seed checks](seed-entry.json) and [44 interruption checks](interruption.json).
- [x] Feedback navigation passes with gutter scrolling and a sufficient bounded scrolling budget for the expanded Settings screen; [normal/fast/reduced motion, mute, reload and navigation cancellation pass](feedback.json).
- [ ] Mobile touch navigation rerun remains pending; the old centre swipe changed a settings slider instead of scrolling.
- [ ] The legacy full-campaign test found a real flask reachability regression: OriginalTheme's 32px body padding replaced Expedition's 110px footer clearance. The fixed action bar hides the last flask control at the bottom of the scroll range. A scoped `has-fixed-actions` style and build-29 patch are being compiled; build 28 is not relabeled as fixed.
- [ ] Owner integration must resolve the existing one-step version gate against build 21 on `dev`.

Initial CI run: [36542521820](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36542521820). The initial failure artifacts remain attached to that run. This is a development candidate, not completed Foundation acceptance.
