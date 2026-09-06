# Unity adaptation specification — checkpoint 0.1

Upstream baseline: `cehinds/AshenSpire` `dev` at `d5c982e777df06221e181c437652b705d2f6abbc`.

The original `SPEC.md` remains the reference for the full migration. This document explicitly defines the smaller adaptation prototype; it does not claim parity.

Current slice: Reaver, 60 health, three energy per turn, five-card draw, eight-card starter deck. Strike/Defend/Footwork preserve the sampled upstream definitions. Two explicitly namespaced prototype rewards compose existing effect primitives. Three sequential encounters use simple visible damage intents, followed by one card or 12 health, with a final victory or defeat. No stamina, mana, Poise, status DSL, equipment recomposition, random map branches or other classes are implemented yet.

Pure domain rules are separate from a Unity controller and reusable UI view. Tags are validated and queryable, but this first schema is intentionally smaller than the upstream normalized tag system. The full domain/family/assignment import is still required.

Saved draw/discard/hand/RNG state is versioned and scoped by web channel. Original JavaScript saves are not imported or overwritten. Development browser output is a conventional static-hostable Unity export.

Acceptance for this checkpoint: compile Web player; run deterministic rule tests; exercise title → map → combat and reload in the exported browser; capture phone/desktop evidence; publish an accurately labeled dev checkpoint. The broader brief remains in `Unity-Build-Brief.md`.
