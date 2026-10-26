#!/usr/bin/env node
// Golden fixtures for the Core 2 rules port plan phase 5 implements (SPEC.md §0.9), produced by
// running the JS game's own model and engine functions at the SPEC §0.2 pin.
//
//   node tools/oracle-js-core2.mjs <js-checkout>            write UnityTests/Core2Content/Oracles/*.json
//   node tools/oracle-js-core2.mjs <js-checkout> --check    exit 1 if the committed fixtures differ
//
// Every case is { "input": {...}, "expected": {...} } in plain data, so a C# port can replay the
// input and compare. Inputs are built on a real Core 2 fight (createRunState + createRunCombat on
// registries bound to resolveGameProfile('default')), the same fixture shape the JS FixCore tests use.
// Families and the JS functions they call:
//   starter-decks   gameProfileStarterDeck                          (checked now by UnityTests/Core2Content)
//   pricing         stanceStaminaCost, stanceManaCost; cardPlayCosts on live hands   (phase 5a)
//   hit-chance      attackHitPreview; resolveAttackHit with fixed dice              (phase 5a)
//   poise-absorb    applyAttackDamage (Block, then Poise, Staggered)                (phase 5a)
//   matchups        applyAttackDamage by carrier/target stance; stanceEntryBonuses,
//                   stanceRatingBonus, stanceEffectiveness                          (phase 5b)
//   counter-return  counterReturn, counterBeats                                     (phase 5b)
//   enemy-planning  enemyPlanActions, enemyMoveQuote, maxStamina per enemy           (phase 5c)
//   damage-ranges   projectCombatIntent(...).projectedDamage, with its inputs        (phase 5a)
//   scripted-fights whole fights under a fixed play policy and seed                  (phase 5 complete)
import {join} from 'node:path';
import {root, parseArgs, jsIdentity, requirePin, loadJs, toPlain, syncTree} from './js-core2-shared.mjs';

const USAGE = 'usage: node tools/oracle-js-core2.mjs <js-checkout> [--check] [--allow-unpinned]';
const OUT = 'UnityTests/Core2Content/Oracles';
const args = parseArgs(process.argv.slice(2), USAGE);
let identity;
try { identity = jsIdentity(args.js); requirePin(identity, args.allowUnpinned); }
catch (error) { console.error(`oracle-js-core2: ${error.message}`); process.exit(1); }

const js = await loadJs(args.js);
const {profile, registries, load} = js;
const {createRunState} = await load('src/model/state.js');
const {createRng} = await load('src/engine/rng.js');
const {createRunCombat} = await load('src/engine/runCombat.js');
const {dispatch, cardPlayCosts} = await load('src/engine/combat.js');
const {resolveCombatCard} = await load('src/engine/combatExpansion.js');
const {stanceStaminaCost, stanceManaCost, stanceOf, stanceEntryBonuses, stanceRatingBonus, stanceEffectiveness} = await load('src/model/stanceRules.js');
const {attackHitPreview, resolveAttackHit} = await load('src/engine/stanceHit.js');
const {applyAttackDamage} = await load('src/engine/actions.js');
const {counterReturn, counterBeats} = await load('src/engine/counterRule.js');
const {enemyMoveQuote} = await load('src/engine/combatRoundsAdapter.js');
const {enemyPlanActions} = await load('src/engine/normalPreview.js');
const {projectCombatIntent} = await load('src/ui/models/CombatIntentProjectionModel.js');
const {enemyMoveCarrier, expandedEnemyMove, tacticalCarrier} = await load('src/engine/combatCardTactics.js');
const {rangeHitContact, rangeSourceContact} = await load('src/engine/combatEngagement.js');
const {proposalTargetPlan} = await load('src/engine/proposalStatusRuntime.js');
const {attributesOf, levelOf} = await load('src/engine/counterReturn.js');
const {magicShareOf} = await load('src/model/combatRatings.js');
const {gameProfileStarterDeck} = await load('src/model/gameProfileRun.js');
const {equippedIn, slotHand} = await load('src/model/loadout.js');
const {attackDescriptor} = await load('src/model/attackTags.js');
const {CORE2_ACTION_RULES_VERSION} = await load('src/model/core2ActionPolicy.js');

const CLASSES = ['reaver', 'starseer', 'rogue', 'herald'];
const ALL_UNLOCKED = {gameProfileUnlocks: {[profile.id]: {completedRunIds: ['oracle-a', 'oracle-b', 'oracle-c']}}};
const runOf = new WeakMap();
function fight({classId = 'reaver', seed = 511, enemyIds = ['wanderingSoldier']} = {}) {
  const run = createRunState({registries, classId, seed, profileMeta: ALL_UNLOCKED});
  const combat = createRunCombat({registries, run, rng: createRng(seed), enemyIds});
  runOf.set(combat, run);
  return combat;
}
const plain = value => toPlain(value, 'oracle', []);
const clone = value => structuredClone(plain(value));

/** Move the seeded Accuracy stream to the next natural 20 (tests/accuracyFixture.mjs). */
function primeNatural20(combat) {
  const original = combat.rng.getCounters();
  const search = createRng(combat.rng.seed, original);
  for (let i = 0; i < 100000; i++) {
    const before = search.getCounters().combatAvoidance || 0;
    if (search.int('combatAvoidance', 1, 20) === 20) { combat.rng.restoreCounters({...original, combatAvoidance: before}); return; }
  }
  throw new Error('no natural 20 in reach');
}

const families = {};
function family(id, phase, description, sources, cases, extra = {}) {
  families[id] = {family: id, phase, description, sources, js: {commit: identity.commit, build: identity.build}, profileHash: profile.hash,
    core2ActionRulesVersion: CORE2_ACTION_RULES_VERSION, caseCount: cases.length, ...extra, cases};
}

// ── starter decks ──────────────────────────────────────────────────────────────────────────────
family('starter-decks', 'phase 4 (loader)', 'The opening deck each class starts a Core 2 run with (packs/core/starterDeck.json classDecks; SPEC §0.10 item 1).',
  ['src/model/gameProfileRun.js gameProfileStarterDeck'],
  CLASSES.map(classId => ({input: {classId}, expected: {deck: gameProfileStarterDeck(profile, classId)}})));

// ── pricing ────────────────────────────────────────────────────────────────────────────────────
const carriers = {
  physicalAttack: {type: 'attack', combatProfile: {camp: 'physical', maneuver: 'attack', range: 'melee'}, cardTags: []},
  physicalReaction: {type: 'attack', combatProfile: {camp: 'physical', maneuver: 'counter', range: 'melee'}, cardTags: ['trait:reaction']},
  physicalXCost: {type: 'attack', cost: 'X', combatProfile: {camp: 'physical', maneuver: 'attack', range: 'melee'}, cardTags: []},
  spellCasting: {type: 'spell', combatProfile: {camp: 'spell', maneuver: 'casting', range: 'ranged'}, cardTags: []},
  spellReaction: {type: 'spell', combatProfile: {camp: 'spell', maneuver: 'counter', range: 'melee'}, cardTags: ['trait:reaction']},
  spellMirroredAttack: {type: 'spell', combatProfile: {camp: 'spell', maneuver: 'attack', mirrors: 'attack', range: 'melee'}, cardTags: []},
};
const stances = {
  none: null,
  physicalAttack: {camp: 'physical', maneuver: 'attack', range: 'melee'},
  physicalCounter: {camp: 'physical', maneuver: 'counter', range: 'melee'},
  physicalDefend: {camp: 'physical', maneuver: 'defend', range: 'melee'},
  spellCasting: {camp: 'spell', maneuver: 'casting', range: 'ranged'},
  spellMirroredAttack: {camp: 'spell', maneuver: 'attack', mirrored: true, range: 'melee'},
};
const pricing = [];
for (const classId of CLASSES) for (const unequipped of [false, true]) for (const phase of ['player', 'enemy'])
  for (const [carrierId, carrier] of Object.entries(carriers)) for (const [stanceId, stance] of Object.entries(stances)) for (const base of [0, 1, 2, 3, 4]) {
    if (unequipped && (phase === 'enemy' || base !== 3)) continue;
    const player = {id: 'player', kind: 'player', classId, ...(stance ? {combatStance: stance} : {}), ...(unequipped ? {classUnequipped: true} : {})};
    const ctx = {gameProfile: profile, combatExpansionVersion: 2, core2ActionRulesVersion: CORE2_ACTION_RULES_VERSION, phase, player};
    pricing.push({input: {kind: 'rule', classId, classUnequipped: unequipped, phase, carrier: carrierId, stance: stanceId, base},
      expected: {sp: stanceStaminaCost(base, player, carrier, ctx, 0), spWithMpLane: stanceStaminaCost(base, player, carrier, ctx, 2), mp: stanceManaCost(base, player, carrier, ctx)}});
  }
// Live hands: every player card of the profile, priced by the real fight with no stance and in its own stance.
const playerCards = profile.content.cards.filter(card => card.id.startsWith('core.') || card.id.startsWith('base.'));
for (const classId of CLASSES) {
  const combat = fight({classId});
  for (const card of playerCards) for (const inStance of [false, true]) for (const stamina of card.cost === 'X' ? [0, 3] : [5]) {
    const hand = [{instanceId: 'oracle', cardId: card.id, upgraded: false}];
    combat.piles.hand = hand;
    combat.player.stamina = stamina; combat.player.mana = 5;
    delete combat.player.combatStance; delete combat.player.concurrentStances;
    const stance = stanceOf(resolveCombatCard(combat, hand[0]), combat.player);
    if (inStance) { if (!stance) continue; combat.player.combatStance = stance; }
    let expected;
    try { expected = {costs: cardPlayCosts(combat, 'oracle')}; } catch (error) { expected = {error: error.message}; }
    pricing.push({input: {kind: 'hand', classId, cardId: card.id, stance: inStance ? stance : null, stamina, mana: 5}, expected});
  }
}
family('pricing', 'phase 5a', 'Card SP/MP prices: same-stance Adept discount (Martial SP / Magic MP), successive-Spell chain, X cost, own-turn floor 1 and matching-reaction floor 0.',
  ['src/model/stanceRules.js stanceStaminaCost/stanceManaCost', 'src/engine/combat.js cardPlayCosts', 'src/engine/gameProfileCombat.js profileCardCosts'], pricing);

// ── hit chance ─────────────────────────────────────────────────────────────────────────────────
const hit = [];
{
  const combat = fight();
  const ctx = {...combat}; delete ctx.attributesForEntity;
  const enemy = combat.enemies[0];
  for (const dex of [0, 2, 4]) for (const level of [1, 3]) for (const precision of [0, 2]) for (const maneuver of ['attack', 'smash', 'sweep', 'counter'])
    for (const targetDex of [0, 3, 6]) for (const evade of [0, 2]) for (const mode of ['normal', 'advantage', 'disadvantage']) {
      const attributes = {strength: 1, dexterity: dex, constitution: 1, wisdom: 1, intelligence: 1};
      const source = {...combat.player, attributes, level, statuses: precision ? {precision: {stacks: precision}} : {},
        ...(mode === 'advantage' ? {accuracyAdvantage: true} : {}), ...(mode === 'disadvantage' ? {accuracyDisadvantage: true} : {})};
      const target = {...enemy, attributes: {dexterity: targetDex}, statuses: evade ? {evade: {stacks: evade}} : {}, combatStance: {maneuver: 'attack', camp: 'physical', range: 'melee'}};
      const carrier = {combatProfile: {camp: 'physical', maneuver, range: 'melee'}};
      const p = attackHitPreview(ctx, source, target, carrier);
      hit.push({input: {kind: 'preview', attacker: {attributes, level, precision}, mode, carrier: carrier.combatProfile, target: {dexterity: targetDex, evade}},
        expected: {accuracy: p.accuracy, evasion: p.evasion, need: p.need, mode: p.mode, inRange: p.inRange, hitChance: p.hitChance}});
    }
  // Fixed dice: natural 1 always misses, natural 20 always hits, otherwise die + Accuracy >= Evasion.
  for (const [accuracyBonus, targetDex] of [[0, 0], [0, 6], [30, 0], [-30, 6]]) for (let die = 1; die <= 20; die++) {
    const source = {...combat.player, attributes: {strength: 1, dexterity: 1, constitution: 1, wisdom: 1, intelligence: 1}, level: 1, statuses: {}, basePrecisionBonus: accuracyBonus};
    const target = {...enemy, attributes: {dexterity: targetDex}, statuses: {}, combatStance: {maneuver: 'attack', camp: 'physical', range: 'melee'}};
    const carrier = {combatProfile: {camp: 'physical', maneuver: 'attack', range: 'melee'}};
    const local = {...ctx, rng: {int: () => die}, emit: () => {}};
    const r = resolveAttackHit(local, source, target, carrier, {suppressOutcomeEvent: true});
    hit.push({input: {kind: 'roll', die, basePrecisionBonus: accuracyBonus, target: {dexterity: targetDex}}, expected: {accuracy: r.accuracy, evasion: r.evasion, hit: r.hit}});
  }
}
family('hit-chance', 'phase 5a', 'd20 + Precision against a fixed Evasion: exact enumerated hit % (normal, advantage, disadvantage) and natural 1/20 rolls.',
  ['src/engine/stanceHit.js attackHitPreview/resolveAttackHit'], hit);

// ── Block → Poise absorb ───────────────────────────────────────────────────────────────────────
function strike({amount, block = 0, poise = 0, vulnerable = 0, targetStance = null, maneuver = 'attack', camp = 'physical'}) {
  const combat = fight();
  const target = combat.enemies[0];
  Object.assign(target, {hp: 500, maxHp: 500, block, barrier: 0, statuses: vulnerable ? {vulnerable: {stacks: vulnerable}} : {}, attributes: {dexterity: 0},
    persistentPoise: {value: poise, max: Math.max(poise, 20)}, ratings: {ar: 0, dr: 0, pr: 0, ward: 0, poise: 20}});
  delete target.combatCounter;
  if (targetStance) target.combatStance = {maneuver: targetStance, camp: 'physical', range: 'melee'}; else delete target.combatStance;
  delete combat.player.combatStance;
  const carrier = {skipRatingBonus: true, combatProfile: {camp, maneuver, range: 'melee'}};
  primeNatural20(combat);
  const start = combat.eventLog.length;
  applyAttackDamage(combat, combat.player, target, amount, [], carrier);
  const dealt = combat.eventLog.slice(start).filter(row => row.type === 'damageDealt').at(-1) || {};
  return {amount: dealt.amount ?? 0, blocked: dealt.blocked ?? 0, poiseAbsorbed: dealt.poiseAbsorbed ?? 0, poiseDamage: dealt.poiseDamage ?? 0,
    hpLoss: 500 - target.hp, blockAfter: target.block || 0, poiseAfter: target.persistentPoise?.value ?? 0,
    targetStanceAfter: target.combatStance?.maneuver ?? null, vulnerableAfter: target.statuses?.vulnerable?.stacks ?? 0};
}
const poiseCases = [];
for (const amount of [1, 4, 7, 8, 12, 20, 30]) for (const block of [0, 3, 10]) for (const poise of [0, 5, 20]) for (const vulnerable of [0, 1])
  poiseCases.push({input: {amount, block, poise, vulnerable}, expected: strike({amount, block, poise, vulnerable})});
family('poise-absorb', 'phase 5a', 'One physical Attack hit (natural 20) into Block, then Poise absorption, Vulnerable x1.25, flooring, and a Poise break to the Staggered stance.',
  ['src/engine/actions.js applyAttackDamage', 'content/source/fixCoreMechanics.json poise/staggered'], poiseCases);

// ── matchups ───────────────────────────────────────────────────────────────────────────────────
const matchups = [];
for (const maneuver of ['attack', 'smash', 'sweep']) for (const targetStance of [null, 'attack', 'smash', 'sweep', 'defend', 'counter', 'casting', 'staggered'])
  for (const [block, poise] of [[0, 20], [10, 20], [10, 3]])
    matchups.push({input: {kind: 'hit', maneuver, targetStance, amount: 12, block, poise}, expected: strike({amount: 12, block, poise, targetStance, maneuver})});
{
  const combat = fight();
  for (const card of playerCards) {
    const def = resolveCombatCard(combat, {instanceId: 'm', cardId: card.id, upgraded: false});
    const stance = stanceOf(def, combat.player);
    const entry = stanceEntryBonuses(def, combat);
    const source = {...combat.player, combatStance: stance};
    matchups.push({input: {kind: 'stance', cardId: card.id}, expected: {stance, entry,
      ratings: stance ? Object.fromEntries(['ar', 'pr', 'dr', 'ward'].map(id => [id, stanceRatingBonus(combat, source, id)])) : null,
      sameStanceEffectiveness: stance ? stanceEffectiveness(combat, source, def) : 1}});
  }
}
family('matchups', 'phase 5b', 'Attack/Smash/Sweep into each target stance (Smash vs Defend/Prepared x1.25 + 25% Block, Poise -50%), and each player card\'s stance identity, entry grants, active ratings and same-stance x1.25.',
  ['src/engine/actions.js applyAttackDamage', 'src/model/stanceRules.js stanceOf/stanceEntryBonuses/stanceRatingBonus/stanceEffectiveness'], matchups);

// ── counter returns ────────────────────────────────────────────────────────────────────────────
const counters = [];
{
  const combat = fight();
  for (const maneuver of ['attack', 'smash', 'sweep', 'counter', 'defend', 'evade', 'casting', 'prepared']) counters.push({input: {kind: 'beats', maneuver}, expected: {beats: counterBeats(combat, maneuver)}});
  for (let incoming = 0; incoming <= 30; incoming++) for (const advantaged of [false, true]) counters.push({input: {kind: 'return', incoming, advantaged}, expected: counterReturn(combat, incoming, advantaged)});
}
family('counter-return', 'phase 5b', 'Counter return per hit: 50% of incoming, advantage bonuses (Block, Poise), and which incoming maneuvers a Counter beats.',
  ['src/engine/counterRule.js counterReturn/counterBeats'], counters);

// ── enemy SP planning ──────────────────────────────────────────────────────────────────────────
const planning = [];
// The player each seed's fights start with (the run layer is phase 8): cases name it by seed.
const planningStarts = {};
for (const seed of [511, 7]) for (const enemy of registries.enemies.all()) {
  const combat = fight({seed, enemyIds: [enemy.id]});
  planningStarts[seed] ??= fightStart(combat);
  const live = combat.enemies[0];
  const def = registries.enemies.get(enemy.id);
  const plan = enemyPlanActions(combat, live).map(entry => entry.id);
  const quotes = Object.fromEntries(Object.keys(def.moves).map(moveId => {
    const q = enemyMoveQuote(def.moves[moveId], {enemy: live, moveId, combat});
    return [moveId, {sp: q.sp, advanceSp: q.advanceSp, totalSp: q.totalSp, route: q.rangeQuote?.route ?? null, available: q.rangeQuote?.available ?? null}];
  }));
  planning.push({input: {seed, classId: 'reaver', enemyId: enemy.id},
    expected: {maxStamina: live.maxStamina, stamina: live.stamina, intentMoveId: live.intent?.moveId ?? null, plannedMoves: (live.plannedMoves || []).map(m => m.moveId), plan, quotes}});
}
family('enemy-planning', 'phase 5c', 'Each enemy\'s SP pool, the opening plan it commits within that budget, and the SP quote of every move (multi-hit moves cost at least 2).',
  ['src/engine/normalPreview.js enemyPlanActions', 'src/engine/combatRoundsAdapter.js enemyMoveQuote'], planning, {starts: planningStarts});

// ── damage ranges ──────────────────────────────────────────────────────────────────────────────
// input.state is what the projection reads at that moment (phase 5a replays it): the attacker and
// defender as the fight built them, the move's carrier with its magic share (damage typing), and the
// range contact (engagement bands). Building that state (enemy planning, ratings from gear, range)
// is phase 5b/5c/8 work; the fight-level families check it end to end.
const statusStacks = entity => Object.fromEntries(Object.entries(entity.statuses || {}).filter(([, row]) => row?.stacks > 0).map(([id, row]) => [id, row.stacks]).sort());
function rangeState(combat, enemy) {
  const def = registries.enemies.get(enemy.enemyId);
  const move = expandedEnemyMove(enemy, def.moves[enemy.intent.moveId] || {}, enemy.intent.moveId, combat);
  const carrier = enemyMoveCarrier(enemy, move, enemy.intent.moveId, combat);
  const contact = rangeHitContact(combat, enemy, combat.player, carrier);
  const proposal = proposalTargetPlan(combat, enemy, combat.player, carrier);
  const pool = row => row ? {value: row.value, max: row.max} : null;
  return {
    base: enemy.intent.damage ?? null, hits: enemy.intent.hits ?? 1, damageEffects: (move.effects || []).filter(row => row.op === 'damage').length,
    attacker: {kind: enemy.kind, level: levelOf(combat, enemy), levelProfile: !!def.levelProfile, attributes: attributesOf(combat, enemy), ratings: enemy.ratings,
      statuses: statusStacks(enemy), stance: enemy.combatStance ?? null, basePrecisionBonus: enemy.basePrecisionBonus || 0,
      accuracyAdvantage: !!enemy.accuracyAdvantage, accuracyDisadvantage: !!enemy.accuracyDisadvantage},
    defender: {kind: combat.player.kind, hp: combat.player.hp, block: combat.player.block || 0, barrier: combat.player.barrier || 0,
      poise: pool(combat.player.persistentPoise), ward: pool(combat.player.persistentWard), ratings: combat.player.ratings, statuses: statusStacks(combat.player),
      stance: combat.player.combatStance ?? null, attributes: attributesOf(combat, combat.player), level: levelOf(combat, combat.player), baseEvasionBonus: combat.player.baseEvasionBonus || 0},
    carrier: {type: carrier.type ?? null, tags: carrier.tags || [], combatProfile: carrier.combatProfile, mirrors: carrier.mirrors ?? null,
      magicShare: magicShareOf({...carrier.combatProfile, ...(Number.isFinite(carrier.magicShare) ? {magicShare: carrier.magicShare} : {})}, combat.ratingsRules)},
    contact: contact ? {inRange: !!contact.inRange, precisionDelta: contact.precisionDelta || 0} : null,
    proposal: {allowed: proposal.allowed !== false, accuracyDelta: proposal.accuracyDelta || 0, unavoidable: !!proposal.unavoidable,
      perceptionChance: proposal.perceptionChance ?? null, evadeChance: proposal.evadeChance || 0},
  };
}
const ranges = [];
for (const enemy of registries.enemies.all()) for (const [block, stance] of [[0, null], [5, null], [0, 'defend'], [0, 'evade']]) {
  const combat = fight({enemyIds: [enemy.id]});
  const live = combat.enemies[0];
  live.intentRevealed = true; live.proposalIntentRevealedUntil = 99;
  combat.player.block = block;
  if (stance) combat.player.combatStance = {camp: 'physical', maneuver: stance, range: 'melee'}; else delete combat.player.combatStance;
  const shown = projectCombatIntent(combat, live.id);
  const d = shown?.projectedDamage;
  ranges.push({input: {classId: 'reaver', seed: 511, enemyId: enemy.id, playerBlock: block, playerStance: stance, ...(live.intent?.damage != null ? {state: rangeState(combat, live)} : {})},
    expected: {hidden: !!shown?.hidden, moveId: live.intent?.moveId ?? null, damage: live.intent?.damage ?? null, hits: live.intent?.hits ?? null,
      projected: d ? {min: d.min ?? null, max: d.max ?? null, label: d.label ?? null, uncertain: d.uncertain ?? null, reason: d.reason ?? null} : null}});
}
family('damage-ranges', 'phase 5a', 'The intent damage range ("0–12") shown for each enemy\'s opening move against the player\'s current Block and stance; input.state is the projection\'s attacker, defender, carrier and range contact.',
  ['src/ui/models/CombatIntentProjectionModel.js projectCombatIntent', 'src/engine/gameProfileCombat.js profileDamageReceipt', 'src/engine/combatDamagePreview.js applyReceiptToPreviewTarget'], ranges);

// ── scripted fights ────────────────────────────────────────────────────────────────────────────
function settle(combat) { for (let i = 0; combat.pendingReaction && i < 50; i++) dispatch(combat, {type: 'chooseReaction', offerId: combat.pendingReaction.id, optionId: null}); }
// What the run layer hands the fight (phase 8 builds it from the run): the player as createRunCombat stamped it, the deck
// in run order (the shuffle reorders it), the property rules mounted for the player's relics, class card and gear
// (engine/properties.js, balance values resolved), and the primary weapon's range (combatEngagement.js initializeCombatRange).
function fightStart(combat) {
  const p = combat.player;
  let primary = null;
  for (const hand of ['right', 'left']) {
    for (const slot of registries.equipment?.slots || []) if (slotHand(slot) === hand) {
      const piece = combat.loadout && equippedIn(registries, combat.loadout, p.classId, slot.id);
      if (piece && attackDescriptor(piece).source === 'weapon') { primary = piece; break; }
    }
    if (primary) break;
  }
  const run = runOf.get(combat);
  return {
    player: {classId: p.classId, hp: run.hp, maxHp: run.maxHp, maxStamina: p.energyMax, mana: run.mana, maxMana: run.maxMana,
      attributes: p.attributes, ratings: p.ratings, level: combat.characterLevel ?? 1, baseEvasionBonus: p.baseEvasionBonus || 0,
      basePrecisionBonus: p.basePrecisionBonus || 0, poise: p.persistentPoise, handSize: combat.handMax, relicIds: p.relicIds,
      ...(p.classUnequipped ? {classUnequipped: true} : {})},
    deck: run.deck.map(card => ({instanceId: card.instanceId, cardId: card.cardId})),
    primaryWeaponRange: primary?.range || 'melee',
    // Each deck card's face as the player's gear resolves it (combatCardTactics.js tacticalCarrier: the damage type the
    // wielded piece gives an untyped card) and the range source its damage commits (combatEngagement.js rangeSourceContact).
    cards: Object.fromEntries([...new Set(run.deck.map(card => card.cardId))].map(cardId => {
      const def = resolveCombatCard(combat, {instanceId: 'oracle', cardId, upgraded: false});
      const carrier = tacticalCarrier(def, {}, combat, p);
      const damage = (def.effects || []).find(effect => effect.op === 'damage') || {};
      const source = rangeSourceContact(combat, p, null, carrier, damage, '0');
      return [cardId, {combatProfile: carrier.combatProfile, rangeSource: {coverage: source.coverage, weapon: source.weapon, sourceWeaponId: source.sourceWeaponId,
        throwCapable: source.throwCapable, bypass: source.bypass}}];
    })),
    propertyRules: Object.values(combat.propertyMounts?.player || {}).map(mount => ({kind: mount.kind, id: mount.id,
      rules: mount.rules.map(rule => ({tag: rule.tag, ...(rule.triggers ? {triggers: rule.triggers} : {}), ...(rule.passives ? {passives: rule.passives} : {})}))})),
  };
}
function scripted({classId, seed, enemyIds, maxTurns = 30}) {
  const combat = fight({classId, seed, enemyIds});
  const start = fightStart(combat);
  dispatch(combat, {type: 'setReactions', enabled: false});
  const turns = [];
  for (let turn = 1; turn <= maxTurns && !combat.result; turn++) {
    settle(combat);
    const plays = [];
    for (let guard = 0; guard < 20 && !combat.result; guard++) {
      const target = combat.enemies.find(e => e.alive);
      let played = null;
      for (const card of combat.piles.hand) {
        let costs; try { costs = cardPlayCosts(combat, card.instanceId); } catch { continue; }
        if ((costs.stamina || 0) > (combat.player.stamina || 0) || (costs.mana || 0) > (combat.player.mana || 0)) continue;
        for (const targetId of [target?.id, undefined]) {
          try { dispatch(combat, {type: 'playCard', cardInstanceId: card.instanceId, ...(targetId ? {targetId} : {})}); played = card.cardId; break; } catch { /* not playable this way */ }
        }
        if (played) break;
      }
      if (!played) break;
      plays.push(played); settle(combat);
    }
    const snapshot = {turn, plays, player: {hp: combat.player.hp, block: combat.player.block || 0, stamina: combat.player.stamina, mana: combat.player.mana, stance: combat.player.combatStance?.maneuver ?? null},
      enemies: combat.enemies.map(e => ({enemyId: e.enemyId, hp: e.hp, block: e.block || 0, poise: e.persistentPoise?.value ?? null, stamina: e.stamina ?? null, intent: e.intent?.moveId ?? null, alive: e.alive}))};
    if (!combat.result) { dispatch(combat, {type: 'endTurn'}); settle(combat); }
    snapshot.after = {playerHp: combat.player.hp, enemyHp: combat.enemies.map(e => e.hp)};
    turns.push(snapshot);
  }
  const types = {};
  for (const event of combat.eventLog) types[event.type] = (types[event.type] || 0) + 1;
  return {start, result: combat.result ?? null, turns, eventCounts: Object.fromEntries(Object.entries(types).sort()),
    damageDealt: combat.eventLog.filter(e => e.type === 'damageDealt').map(e => ({source: e.sourceId, target: e.targetId, amount: e.amount, blocked: e.blocked ?? 0})),
    rngCounters: Object.fromEntries(Object.entries(combat.rng.getCounters()).sort())};
}
const fights = [
  {classId: 'reaver', seed: 511, enemyIds: ['wanderingSoldier']},
  {classId: 'reaver', seed: 1854, enemyIds: ['blightHound', 'graveWisp']},
  {classId: 'rogue', seed: 42, enemyIds: ['fellWarden']},
  {classId: 'starseer', seed: 9, enemyIds: ['huskBrute']},
  {classId: 'herald', seed: 77, enemyIds: ['lanternMoth']},
].map(input => { const {start, ...expected} = scripted(input);
  return {input: {...input, policy: 'reactions off; each turn play the first affordable card in hand order at the first living enemy, then End Turn', start}, expected}; });
family('scripted-fights', 'phase 5 (all three PRs)', 'Whole Core 2 fights with fixed seeds and a fixed play policy: per-turn plays and pools, every damage event, event counts and final RNG counters.',
  ['src/engine/runCombat.js createRunCombat', 'src/engine/combat.js dispatch/cardPlayCosts'], fights);

// ── write ──────────────────────────────────────────────────────────────────────────────────────
// One case per line: small files, readable diffs when the pin moves.
function oracleText(value) {
  const {cases, ...head} = clone(value);
  const header = JSON.stringify(head, null, 1).replace(/\n}$/, '');
  return `${header},\n "cases": [\n${cases.map(row => '  ' + JSON.stringify(row)).join(',\n')}\n ]\n}\n`;
}
const files = Object.fromEntries(Object.entries(families).map(([id, value]) => [`${id}.json`, oracleText(value)]));
const diffs = syncTree(join(root, OUT), files, args.check, OUT);
const cases = Object.values(families).reduce((n, f) => n + f.caseCount, 0);
if (args.check) {
  for (const line of diffs) console.error(line);
  if (diffs.length) { console.log(`oracle-js-core2: ${Object.keys(files).length - diffs.length} passed, ${diffs.length} failed`); console.error(`Regenerate: node tools/oracle-js-core2.mjs ${args.js}`); process.exit(1); }
  console.log(`oracle-js-core2: OK — ${Object.keys(files).length} checks passed`);
} else {
  console.log(`Wrote ${Object.keys(files).length} oracle families (${cases} cases) to ${OUT}: ${Object.values(families).map(f => `${f.family} ${f.caseCount}`).join(', ')}`);
}
