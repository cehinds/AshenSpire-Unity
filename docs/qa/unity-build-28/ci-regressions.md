# Build 28 follow-up regression findings

The 879 scoped compiled-browser checks in the build-28 report passed. The broader PR pipeline also exposed failures; that total is not an all-CI-green claim.

- [x] Seed/interruption tests waited behind the first-visit welcome. The harness now enters through the actual visible control. The build-28 seed rerun passes [25 checks](seed-entry.json).
- [ ] Feedback/mobile tests cannot reach Back within their old scrolling bound after the Settings expansion. Their scrolling must use the gutter rather than change slider values; broader reruns remain pending.
- [ ] The legacy full-campaign test found a real flask reachability regression: OriginalTheme's 32px body padding replaced Expedition's 110px footer clearance. The fixed action bar hides the last flask control at the bottom of the scroll range. A scoped `has-fixed-actions` style and build-29 patch are being compiled; build 28 is not relabeled as fixed.
- [ ] Owner integration must resolve the existing one-step version gate against build 21 on `dev`.

Initial CI run: [36542521820](https://github.com/cehinds/AshenSpire-Unity/actions/runs/36542521820). The initial failure artifacts remain attached to that run. This is a development candidate, not completed Foundation acceptance.
