# Campaign 0.2 validation

Implemented: four hero loadouts, 18 cards, eight card-effect operations, twelve foes, four enemy-intent operations, three acts/nine stops, route choices, three bosses, six equipment items, rewards, rest/card removal/flasks, victory/defeat/retry, per-channel checksummed save/backup, motion preferences, sound feedback, a development inspector/gallery, JSON record editing and CSV interchange.

## Observed results

- 47 original Unity domain checks and 116 campaign checks passed. All four classes completed twelve seeded policy runs each; 48/48 wins are reachability evidence, not proof of fun or optimal balance.
- The final packaged Web player completed all nine encounters through real pointer commands, bought equipment and reached victory. The same pass saved/reloaded and restored every campaign-state field.
- Separate browser runs exercised Rogue combat/save/resume/defeat, Herald combat/save/resume/reward and Starseer combat/save/resume/reward. All reported zero browser errors.
- Current phone, desktop and landscape screenshots and reports are under `Published/Screenshots` and `Published/ClassEvidence`. Previous 6.6 prototype screenshots are preserved under `Published/History/0.1.0-Unity6.6`.
- Unity 6000.6.0f1 built Web, Windows and Android successfully. No C# compiler warnings/errors were found in the final target logs. The Windows player reached ASHENSPIRE_UI_READY in headless startup.
- Current downloadable artifact sizes: Web ZIP 11,117,695 bytes, Windows ZIP 38,759,724 bytes, Android APK 22,527,796 bytes. The Web player’s local uncompressed resource payload was 33,967,257 bytes; first readiness took 1,819 ms in this desktop run. These are observed desktop/local values, not phone performance guarantees.
- CSV export/import preserved authoritative JSON bytes. Importing a card cost of 99 was rejected, leaving authoritative bytes unchanged. Strict schema validation runs through the actual C# definition model.
- Package validation checks the source digest and nine artifact hashes. The committed enemy atlas matches its source bytes; generated enemy sprites share one scale and foot anchor. Workflow lint passed 76 checks.

## Limits

No Android device was connected to ADB, so installation, physical touch, cutout behavior, frame time and audio on a phone are unverified. Windows graphical gameplay and iOS builds are unverified. Browser tests use desktop pointer input and phone-sized viewports.

The two inherited browser CI inventory failures were explicitly waived by Constantine for this work. The original suite was not disabled or altered. The Unity campaign workflow runs its own domain, package and full-browser checks.

The original browser game's full stamina/mana/Poise systems, status DSL and weapon-card recomposition are outside this adapted campaign. A composed soundtrack and more elaborate animation remain possible improvements. Source-driven automatic Unity compilation still needs runner/licensing setup; current Actions validates packaged players and publishes Pages when channel branches change.

## Owner verification

Open dev, select each class, read and respond to changing intents, equip an Ember Blade and verify card damage increases, use a flask, choose a reward, reload in combat and continue. Try reduced motion/mute settings. Play to a boss and report the displayed version, seed, device/browser and reproduction steps for any problem.
