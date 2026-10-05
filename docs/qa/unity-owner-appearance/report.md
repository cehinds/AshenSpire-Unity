# Owner appearance preview evidence

Reference: the owner's Starseer screenshot and local `http://127.0.0.1:4175/`.
Read-only appearance source: `D:/repos/TheAshenedSpire`. Gameplay reference:
published HTML test 898. These checks do not certify the full gameplay migration.

- [ ] Finish appearance parity across the game.
  - [x] Eleven imported textures match the decoded pixels of the reference crops.
  - [x] Cormorant Garamond imported with the OFL license.
  - [x] Build 38 (`0.0.30.7`) compiled and exported in Unity 6000.6.0f1.
  - [x] Before subsequent source edits, its source digest and all eight payload
    files matched the export receipt:
    `08acc39066b5fe1df859bf80029925aff4c01d109061e6d89bb1ff687fcc77cc`.
  - [x] A fresh isolated run created a Starseer and entered a soldier encounter.
  - [x] Default 1280x720 and portrait 390x844 actual player screenshots captured.
  - [x] Inspector play spent one action, moved the card to discard and reduced
    the soldier's HP from 25 to 18, matching its copied preview.
  - [x] No observed console errors; one existing persistent filesystem sync warning.
  - [ ] Successful automated drag/drop: the attempted drag instead opened the
    long-press inspector under intermittent local tool latency.
  - [x] Reload/continue through the documented browser tab API restored the
    Starseer fight with two actions, five cards, one discarded card and 18/25
    soldier HP. The earlier native wrapper timed out without reloading.
  - [x] Build-39 corrected export, source/eight-payload receipt and desktop/portrait checks.
  - [x] Build-40 texture-dimension correction and fresh player checks.
    - [x] Actual Unity compilation/export and all eleven imported PNG dimensions.
    - [x] Source/eight-payload receipt, digest
      `59f51ce9999d1472f043cabc2b39c490d0be7e8b154c3f3b934e42b5b3cc936f`.
    - [x] Runtime soldier texture 626x548; desktop and portrait proportions checked.
    - [x] Fresh Starseer inspected and played Staff Magic Strike: actions 3 to 2,
      hand 6 to 5, discard 0 to 1 and soldier HP 25 to 18.
    - [x] Direct browser reload and Continue retained that exact combat state.
    - [x] Utility tray horizontal scrolling observed; no console errors.
  - [ ] Full title/map/services/co-op appearance and owner acceptance.

The first export exposed portrait actor aspect constraints, overlap between
HUD and utility controls, and rating explanations overflowing small card faces.
Build-39 source corrects aspect sizing and HUD spacing, moves real intent/Poise
badges outside enemy figures, and uses compact resolved rules on the face.
The full rating explanation remains in inspection; no folded bonus is removed
from the resolved card amount. Build 40 additionally disables the background
import preset's NPOT resizing; all eleven textures retain their original PNG
dimensions. Its corrected player was verified at 1280x720 and 390x844.

Screenshots:

- `reference-desktop.png` and `reference-portrait.png`: live appearance reference.
- `build38-desktop-initial.png` and `build38-portrait-initial.png`: initial native
  export, including the regressions described above.
- `build38-inspection.png`: native card inspection and copied combat preview.
- `build39-desktop.png` and `build39-portrait.png`: intermediate layout corrections.
- `build40-desktop.png` and `build40-portrait.png`: corrected final texture proportions.
- `build40-inspection.png` and `build40-reloaded.png`: inspection and restored combat.
- `build40-controls.json`: observable post-reload control and state report.

Playable local preview: `http://127.0.0.1:8904/` (build 40, version `0.0.30.9`).
Portrait retains horizontal hand/tools navigation. Gesture validation remains
open; no successful drag/drop is claimed from the failed earlier automated attempt.

Current-rule models/commands and compact-card checks: 40,399 passed.
Legacy co-op checks before that presentation-only change: 6,587 passed.
No physical-device, controller, publication, CI or owner-acceptance claim is made.
