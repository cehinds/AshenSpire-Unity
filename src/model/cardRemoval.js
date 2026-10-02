// src/model/cardRemoval.js — ported from the original (cehinds/AshenSpire,
// branch claude/fix-sealed-draft-reload). This copy predates the original's
// cardRemoval.js; only the pool-deck rule the Sealed/Draft reload fix needs is
// here, verbatim, so a later reseed replaces this file with the original's.

// THE CUSTOM STARTING DECKS BUILT FROM A POOL (Custom Climb, main.js newRun).
// Sealed and Draft throw the composed starting deck away and deal a new one
// from plain strikes, defends and class-pool cards. createRunState had already
// written the composed deck's birth attack quota (`equipmentAttackSlotCount`),
// so the run named N attack slots its deck never held, and the first full
// restamp (an Armoury swap, the load door) refused it: "attack instance count
// 0 does not match authored N". A Standard deck is composed from the equipment
// and holds every slot of its quota.
export const POOL_DECK_MODES = Object.freeze(['sealed', 'draft']);

export function isPoolDeckRun(run) {
  return POOL_DECK_MODES.includes(run && run.custom && run.custom.deckMode);
}

/**
 * dealtAttackSlotCount(cards) → the birth attack quota a dealt deck was born
 * with: the number of attack-slot instances it holds, which must be exactly
 * `attack:0` … `attack:k−1`, once each (a fresh deal holds none, so 0 — zero is
 * a quota). Anything else is not a deal and throws by name; it is never
 * renumbered.
 */
export function dealtAttackSlotCount(cards) {
  const held = new Set();
  for (const card of cards || []) {
    if (!card || card.equipmentRole !== 'attack') continue;
    const id = card.equipmentAttackSlotId;
    if (typeof id !== 'string' || !/^attack:(0|[1-9]\d*)$/.test(id)) throw new Error(`attack instance '${card.instanceId}' has no valid equipmentAttackSlotId`);
    if (held.has(id)) throw new Error(`duplicate equipmentAttackSlotId '${id}'`);
    held.add(id);
  }
  for (let i = 0; i < held.size; i++) {
    if (!held.has(`attack:${i}`)) throw new Error(`a dealt deck's attack slots must run attack:0..${held.size - 1}; attack:${i} is missing`);
  }
  return held.size;
}
