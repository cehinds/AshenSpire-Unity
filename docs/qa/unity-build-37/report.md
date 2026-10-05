# Intermediate native HTML migration preview

Export: `0.0.30.6`, build 37, published test-898 reference.
Source SHA256: `9e491e36c325ee2e16b1a8fa44f18d5240345253b633d5dd2dbfae0e5927a388`.
Unity 6000.6.0f1 Editor export finished successfully on 2026-10-04.
All eight exported payload hashes and the complete export inventory matched
the receipt; the source matched at verification time. Subsequent C# migration
work is newer than this frozen export and needs its own build.

Browser: isolated loopback origin `http://127.0.0.1:8901/`, exported Unity canvas.
No owner saves or original-game checkout were modified. Desktop viewport
1280x720 and portrait viewport 390x844; temporary viewport override reset.

- [x] Native title background, traveler and menu render.
- [x] Quick Start opens a new native run and its route opens a fight.
- [x] Illustrated hand textures render with upright art.
- [x] Selecting Shield Defend and opening inspection shows its rules and preview.
- [x] Inspector Play commits one card: guard 0 to 11, actions 3 to 2,
  hand 4 to 3, discard 0 to 1; HP/MP/SP remain unchanged.
- [x] Reload and Continue restore this fight and spent action/guard state.
- [x] Title and fight render at 390x844 after canvas resize.
- [x] No console errors observed in these interactions.
- [ ] Exact reference gameplay: runtime still uses older content/stats and
  separate action/Stamina budgets (Reaver 49 HP, SP 2 rather than 70 HP, SP 3).
- [ ] Exact reference typography and card layout: current Cinzel text differs
  from Georgia, names and detailed rules can clip, and older action costs need
  their extra badge. Reference light assets have not replaced imported high art.
- [ ] Exact map/combat/creation/reward presentation and complete reference content.
- [ ] Full fight completion, rewards, progression, shops, co-op and device testing.
- [ ] Owner acceptance.

Observed warning: Unity reports deprecated manual persistentDataPath sync;
save/reload nevertheless succeeded here. This bounded check does not establish
all save compatibility or full visual/gameplay parity.

Evidence: `card-inspection-desktop.png`, `combat-desktop.png`,
`combat-portrait.png`, `title-portrait.png`.
