# AshenSpire — mobile-first Unity adaptation

Rebuild AshenSpire in Unity as a sprite-based, mobile-first game, with desktop support and convenient browser testing. Preserve its identity, tactical deckbuilding, equipment-driven choices, and progression while improving animation, gameplay pacing, graphics, bugs, and UI consistency.

The result must be understandable and maintainable by Constantine: he should be able to trace how it works, change ordinary content without C#, and modify components with clear instructions.

## 1. Preserve and inspect the existing game

Create a new `cehinds/AshenSpire-Unity` repository starting from the current AshenSpire `dev`, and provide its verified GitHub link. Preserve the original repository, edits, assets, branches, and worktrees. Repository creation and the requested preview publication are authorized; branch promotion and product release remain distinct decisions.

Refresh the source baseline and read its current repository guidance. The planning review inspected `cehinds/AshenSpire` at `d5c982e777df06221e181c437652b705d2f6abbc`; treat that as a dated baseline, not a permanent current ref.

Reuse existing content definitions, stable IDs, tag relationships, rules, test cases, art, and animation metadata wherever useful. Port JavaScript behavior deliberately to C#; do not assume browser renderers are directly reusable in Unity. Preserve current names and map older vocabulary explicitly.

Audit the complete player loop, saves, content pipeline, UI catalog, and asset runtime bindings. Separate live assets from references, alternatives, and unused files. Track bugs as reproduced, suspected, or documentation-only. Do not turn old defect comments into claims of current bugs.

## 2. Mobile is the primary experience

Design for touch, phone readability, safe areas, short play sessions, interruption, and comfortable thumb reach first. Desktop should expand that design appropriately.

Evaluate portrait-first interaction on representative combat and inventory screens before fixing orientation. Never require hover, a keyboard, or precision dragging. Provide tap-to-select and tap-to-target alternatives, clear cancellation, readable card details, and visible targeting feedback.

Keep a full run resumable across short sessions. Define safe checkpoints, app backgrounding, interrupted writes, browser storage limits, and versioned saves. Decide explicitly whether old browser saves will migrate; do not imply compatibility without testing it.

Target native Android/iOS and supported mobile browsers, with desktop support secondary. Establish reference devices and measured budgets for frame time, memory, first download, startup time, and touch response. Identify unavailable device/toolchain checks honestly.

## 3. Prove browser delivery early

Provide an owner-facing build tool with clear actions: Validate Content, Run Tests, Build Web, Preview Web, and Package Build. Expose equivalent documented command-line operations. Show build version/source commit, progress, exact output location, useful failures, and logs.

Constantine confirmed that a web link and downloadable build folder are acceptable. Use Unity's standard web export with HTML, runtime, and asset files. Provide hosted HTTPS execution, a one-action local preview launcher, and a phone-accessible preview address where the network permits it. Measure loading and memory costs, and test browser persistence. Do not promise double-click file execution or offline support without evidence.

## 3a. Regular builds, screenshots, and GitHub Pages

Publish one GitHub Pages hub with `/dev/`, `/test/`, `/release/`, and `/main/` subpages. Each must expose its playable game, downloadable build, channel-specific changelog, exact commit/build version, build date, known issues, and screenshots. Use a visible channel label inside the game as well as on the page.

Automatically validate, build, and publish the affected channel after its branch changes. Also provide a manual rebuild action. Checkpoint each completed playable slice so Constantine gets regular test builds during development. Do not rebuild unchanged source merely to produce a newer timestamp.

Keep branch promotion separate from automatic deployment of an already-updated branch. `/dev/` is active development; `/test/` is a selected testing candidate; `/release/` is a release candidate; `/main/` is the approved stable game. Do not label a candidate stable or promote changes solely because CI passed. If a channel has no selected build yet, show that explicitly.

Assemble all four channels into one complete Pages artifact. Serialize deployment and prevent an older job from replacing a newer channel build. Updating one channel must preserve the others. Keep last successful playable builds available if the next build fails, and display failure/currentness clearly.

Generate each changelog from structured change entries for the commits actually included in that channel: Added, Changed, Fixed, Known Issues, and What to Test. Include visible player-facing summaries rather than raw commit dumps.

Capture plentiful useful screenshots at phone and desktop sizes: startup/menu, character selection, map, combat, targeting/card details, reward, equipment, defeat, and save/resume where implemented. Identify the channel, build, viewport, and scenario. Keep a bounded history and validate interaction separately from screenshots.

Serve assets correctly beneath repository/channel subpaths. Isolate saves, caches, and service workers by application/channel; Pages channels share an origin, so path separation alone does not separate browser storage. Verify hosted pages, build metadata, actual gameplay startup, downloads, and channel links before reporting publication complete.

## 4. Architecture and content

Use composition and reusable capabilities, with clear model/view/controller responsibilities. Preserve the current game's useful presenter/observer boundaries for UI. Keep domain rules and simulation independent of Unity scenes, MonoBehaviours, animation timing, storage, and input devices.

MonoBehaviours adapt Unity lifecycle, input, and presentation to explicit services and models. Keep dependencies visible. Use interfaces when substitution, testing, or a real boundary warrants them. Avoid speculative frameworks, deep inheritance, generic manager classes, and an interface for every class.

Use a validated multi-tag registry with stable tag IDs, domains, families, and queries. Distinguish tags from content identity. Components and systems implement behavior; tags select categories and conditions. Do not reduce the existing tag model to Unity's single GameObject tag field.

Author flat content in CSV and structured definitions in JSON. Provide one documented authoritative source per content type, with deterministic imports into generated Unity/runtime definitions. Distinguish definitions, runtime instances, and saved state. Generated files are never edited manually.

Validate duplicate IDs, missing references, invalid tags, malformed values, unsupported fields, and missing asset bindings. Errors must name the source file, row/record, field, and corrective action. Reject invalid imports without replacing the last valid content set.

Provide templates, a simple table/record editor with validation and previews, and an asset picker. Demonstrate adding an enemy, card/ability, equipment item, and encounter using existing behaviors without C# changes. Demonstrate one new behavior component separately and explain why code is needed.

## 5. Names, folders, and documentation

Follow verified Forge standards when available. The local Forge library establishes engineering and explanation preferences but does not include a detailed department/Dimitar C# naming standard. Until that source is available, label the following as the proposed project convention:

- PascalCase for types, namespaces, methods, properties, and constants; camelCase for parameters and locals; `_camelCase` for private instance fields; `I` prefix for interfaces.
- Descriptive names such as `CombatController`, `CardView`, `EnemyDefinition`, and `SaveGameService`; avoid unexplained abbreviations, catch-all `Utils`, and numbered replacement names.
- Match script file names to their main type. Match namespaces to meaningful source folders. Record and enforce agreed formatting/naming in `.editorconfig` where supported.
- Keep Unity's required project folders intact. Put first-party code beneath a clearly named root such as `Assets/AshenSpire/Runtime`, with `Domain`, `Application`, `Presentation`, and `Infrastructure`, subdivided by real features as needed. Keep `Editor`, `Tests`, `Art`, `Prefabs`, and generated content distinct. This is a proposed project layout, not a universal .NET folder standard; create only the folders the working slice needs.

Every first-party MonoBehaviour must start with a concise, accurate maintenance header explaining:

1. Purpose and responsibility.
2. The GameObject/prefab it belongs on and how to attach/configure it.
3. Required Inspector references and where dependencies come from.
4. Lifecycle, input events, output events, and owned state.
5. Where to change data/tuning, visuals, and behavior, with exact related paths.
6. Important invariants, common failure symptoms, and the quickest meaningful verification.

Use actual paths and component names when implemented. Do not ship generic placeholders or identical boilerplate headers. Add Inspector tooltips and useful validation messages. Document public APIs with XML comments where helpful; inline comments explain reasons and non-obvious mechanics. Update comments in the same change as behavior.

Include an owner guide organized by goals: change damage, add an enemy, add an item, replace a sprite, change an animation, adjust a screen, reproduce a bug, and build a preview. Each recipe should contain one concrete example, expected result, common failure, and verification step.

## 6. Art, animation, gameplay, and UI

Reuse and improve the existing painted class/outfit poses, equipment art, enemy art, icons, and backgrounds. Preserve originals and asset provenance. Establish a coherent painterly sprite direction, including palette, scale, feet/pivots, sockets, layer order, silhouettes, and import settings.

Improve anticipation, impact, recovery, guard, hit reactions, and transitions. Preserve anchor/canvas metadata when importing cropped frames. Keep simulation results independent of animation duration; support reduced motion and speed settings. Verify mixed outfits and weapons rather than relying only on one showcase character.

Review art consistency at phone scale. The sampled painted Reaver and simpler procedural Act 1 background suggest a cohesion opportunity; validate that in gameplay before choosing replacements.

Reuse the existing component catalog's responsibilities and disclosure patterns. Build a Unity component gallery using production components, including empty, selected, disabled, error, loading, and overflow states. Centralize visual tokens and semantic commands.

Preserve the core tactical loop while testing clearer choices, shorter downtime, satisfying rewards, and reasons to continue. Explain which player problem each change addresses. Use playtest observations and simulations together; bot win rates alone do not establish fun.

## 7. Testing and delivery

Provide fast headless domain tests, content validation, Unity EditMode/PlayMode coverage where appropriate, and browser/device smoke checks. Translate useful existing tests and seeded scenarios. Compare selected old/new simulation outcomes before intentionally changing rules.

Include a development-only test panel: choose a seed, load an encounter, select a loadout, inspect tags/statuses, adjust animation speed, reset test saves, and export a bug report. Reports should include version, content version, seed, reproduction steps, and relevant state, with preview and explicit export. No debug cheats in release builds.

Deliver the smallest complete slice first: one class, a small enemy set, a boss, rewards, equipment progression, defeat/retry, and save/resume. Establish acceptance checks before implementation. Show actual touch interactions, packaged browser execution, and content edits taking effect. Screenshots supplement behavior evidence.

Follow Forge's principles: concrete before abstract, plain language before jargon, one bounded objective, diagnostic failures, explicit completion criteria, and verified delivery. This is builder work with maintainable explanations; do not force a quiz or teaching pause into every step.

Deliver source, runnable available builds, the build/test tool, sample data, the component gallery, owner editing recipes, known limitations, and a prioritized next slice. Distinguish implemented, compiled, tested, packaged, device-verified, and published states.
