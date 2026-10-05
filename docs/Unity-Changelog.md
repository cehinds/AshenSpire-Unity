# AshenSpire Unity changelog

One entry per Unity version bump, newest first. Written by `node tools/unity-version.mjs bump`.
The root CHANGELOG.md belongs to the HTML game and is not updated here.

## 0.0.33.4 · build 51 · 2026-10-05

- Mount current property carriers under saved rules; preserve legacy fights and trigger gates.

## 0.0.33.3 · build 50 · 2026-10-05

- Track Arcane Ward within existing Block under saved current rules; validate solo/co-op provenance and preserve legacy receipts.
- Label shared Stamina once, hide legacy Catch Breath under that ruleset, and route settings-choice controller cancel to Keep.

## 0.0.33.2 · build 49 · 2026-10-05

- Combine settings recovery, painted card routing and saved deck-order adapters with current Unity controller, import, extraction and validation fixes.
- Offer keep/current-defaults for customized device settings after profile loading; persist the choice and preferences together, preserve saved climbs and retry after write refusal.
- Remove the duplicate fullscreen control and apply the desktop windowed default on explicit reset.

## 0.0.33.2 · build 48 · 2026-10-05

- Advance the migration preview beyond refreshed dev 0.0.33.1; retain settings recovery, painted cards and saved deck-order adapters.

## 0.0.33.1 · build 47 · 2026-10-05

- Export cancelled before completion after refreshing remote dev, which already used this version; build 48 advances one patch beyond that verified remote version.
- Restore monotonic versioning against dev; retain settings recovery, painted card routing and saved deck-order work from the migration branch.

## 0.0.30.15 · build 46 · 2026-10-05

- Export cancelled before completion after detecting that the branch version was below dev; carried forward under the corrected build-47 version.
- Share card/profile artwork between native face layouts so the imported starter paintings reach the visible cards.
- Preserve saved deck order through solo/co-op opening draws, hand refresh and discard returns; explicit shuffle effects remain random.
- Intermediate Web migration preview; current-content activation and broader upstream parity remain incomplete.

## 0.0.30.14 · build 45 · 2026-10-05

- Restore visible keyboard focus after resetting or cancelling settings; 22 desktop/portrait player checks cover persistence and saved-climb preservation.
- Import three verified starter paintings. Visual review found the active card layout still used generic art, corrected in build 46.

## 0.0.30.13 · build 44 · 2026-10-05

- Add settings-only reset confirmation and persistence-failure recovery.
- Add opt-in hand refresh and shared turn-Stamina adapters, preserving legacy saved mechanics and equipment-pool behavior.
- Intermediate export; browser review found the return-focus scroll defect corrected in build 45.

## 0.0.33.1 · build 35 · 2026-10-05

- Integrate guided setup, card QoL and illustrated Unity combat with current dev; full parity and owner acceptance remain open.

## 0.0.33.0 · build 34 · 2026-10-02
## 0.0.30.12 · build 43 · 2026-10-05

- One-screen setup defaults, readable route map and consistent 2:3 card faces.

## 0.0.30.11 · build 42 · 2026-10-05

- Readable hand, raised enemy labels, scrollable utilities, painted 2D prefabs and repaired sword hilt.

## 0.0.30.10 · build 41 · 2026-10-04

- Replace scrolling combat toolbar with contextual tools, fitted hand, action orb and pile controls; author seven painted SpriteRenderer prefabs and repair soldier hilt.

## 0.0.30.9 · build 40 · 2026-10-04

- Preserve all eleven reference texture dimensions without NPOT resampling, validate imported dimensions before export, and separate HUD statuses from pool labels.

## 0.0.30.8 · build 39 · 2026-10-04

- Correct portrait actor aspect ratios, separate HUD and tools, position telegraph overlays, and keep resolved card rules compact.

## 0.0.30.7 · build 38 · 2026-10-04

- Apply the owner-selected 4175 battlefield, painted actors, Cormorant typography and card fan in native Unity.
- Port and compare current card cost and stance choices; published test-898 gameplay migration remains incomplete.

## 0.0.30.6 · build 37 · 2026-10-04

- Import native illustrated card documents and title art from published test 898; preserve legacy costs and saves.
- Add current XP ledgers, stat-row arithmetic and tag adapters; full content migration and visual parity remain in progress.

## 0.0.30.5 · build 36 · 2026-10-03

- No notes recorded.

## 0.0.30.4 · build 35 · 2026-10-03

- Resolve authored status inspection numbers and live meter thresholds; preserve unknown bindings and combat state.

## 0.0.30.3 · build 34 · 2026-10-03

- Show status buildup consistently across solo and co-op; use readable card preview/action words.

## 0.0.30.2 · build 33 · 2026-10-02

- Add solo and host-generated co-op card outcome previews without mutating live combat or revealing future draws.
- Add upward drag and flick target play with cancellation, horizontal browsing and inspection preserved.
- Polish card offer alignment, readable filters, keyboard choices and hostile target highlighting.
- Let Escape close co-op card reading and cancel selection without leaving the shared run.

## 0.0.30.1 · build 32 · 2026-10-02

- Add visual card offers, readable card and tag inspection, and searchable deck and pile browsing.
- Add hand hold/right-click inspection and preserve the last legal co-op target.

## 0.0.30.0 · build 31 · 2026-09-30

- Add controller rebinding, accessibility and display preferences, automatic solo rewards, enemy inspection and authoritative co-op feedback.
- Complete Phase 2 Web verification: 6,280 checks in 48 matching cases, all 22 events/62 choices, four viewports, custom modes, all Ascensions/modifiers, three-act victory and Endless Act 4. Owner/device acceptance remains open.

## 0.0.29.0 · build 30 · 2026-09-29

- Sealed and Draft runs can no longer extract equipment cards at the smith

## 0.0.32.0 · build 33 · 2026-10-02

- Sealed/Draft: run schema 6 import, pool-deck marker validation, src/ mirror of HTML reload fixes

## 0.0.31.0 · build 32 · 2026-10-02

- Rewards: only cinders auto-collected by default; gamepad support (legacy input, rebinding); display options apply their styles; owner acceptance recorded

## 0.0.30.0 · build 31 · 2026-10-02

- US-13.3 hold-to-confirm; US-13.2/7.4/8.3 settings a11y+audio; US-15.1-15.3 gameplay/display/gamepad options; US-0.6 web profile and active-room import; US-16.3 content schema validation

## 0.0.29.0 · build 30 · 2026-10-02

- US-4.4/US-13.4: enemy status and card tag explanations on long press/hover (no hover-only information)

## 0.0.28.1 · build 29 · 2026-09-29

- F00 US-0.3: keep the legacy combat flask and inspection content above the fixed action bar.

## 0.0.28.0 · build 28 · 2026-09-28

- Add original-game map-save import with preview, empty-slot protection, frozen rules and browser file/slot reads; broader import and Foundation acceptance remain open.
- Add original-engine card parity, invalid-save, duplicate and failed-storage checks plus a compiled browser import test.

## 0.0.27.0 · build 27 · 2026-09-29

- F00 US-0.4: AshenedSpire branding, welcome and in-game guide; current-build Foundation validation

## 0.0.26.0 · build 26 · 2026-09-29

- Refine title, settings, collection and combat presentation for desktop and phone.

## 0.0.25.0 · build 25 · 2026-09-28

- F00/F10: Preserve damaged profiles, report storage failures and retry finished-run saves. Compact Web records fit all three slots and backups, with verified recovery during legacy Unity record conversion. Compact records require build 25 or newer; owner acceptance remains open.

## 0.0.24.0 · build 24 · 2026-09-28

- F00 US-0.3: Combat pile inspection, retained-card discard choices and saved solo keyboard bindings; compiled acceptance in progress.

## 0.0.23.0 · build 23 · 2026-09-28

- Independent interface audio, live volume and sound previews

## 0.0.22.0 · build 22 · 2026-09-27

- Connect live audio and feedback settings; import music and add compiled enemy portrait review

## 0.0.21.0 · build 21 · 2026-09-25

- F02 US-2.2 / F03: lean stat scale (ruleset 6, ratings, item weight scale), Standard and Assign-points creation modes, web solo hand rules with per-class opening hands of 4-6 (owner 2026-09-24/25)
- Policy bot gate records wins and gates errors (owner 2026-09-25: "Record wins, gate errors")

## 0.0.20.0 · build 20 · 2026-09-25

- F07: feel profile — 39 HTML motion timings, CSS cubic-bezier curves, speed/reduced-motion scaling (domain ready; tween wiring pending Unity editor)

## 0.0.19.0 · build 19 · 2026-09-25

- F11: end-of-run summary matching the HTML game-over screen (domain ready; UI wiring pending Unity editor)

## 0.0.18.0 · build 18 · 2026-09-25

- F04: enemy telegraph view-model — intent glyph/value/tooltip/severity and Poise meter data (domain ready; UI Toolkit wiring pending Unity editor)

## 0.0.17.0 · build 17 · 2026-09-25

- F08: data-driven music director and credited track catalog (domain ready; AudioSource adapter pending Unity editor)

## 0.0.16.0 · build 16 · 2026-09-25

- F16/F15: content mod packs (add/override/remove, validated) and versioned customization settings with key-binding conflicts (domain ready; runtime wiring pending Unity editor)

## 0.0.15.0 · build 15 · 2026-09-24

- F10: three run save slots, verified writes, legacy migration and 20-result archive (domain ready; UI wiring pending Unity editor)
