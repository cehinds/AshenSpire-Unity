#!/usr/bin/env node
// js-combat-view-oracle.mjs — the JS combat screen's presentation answers at the SPEC pin, for UnityTests/Core2CombatView.
//
//   node UnityTests/Core2CombatView/js-combat-view-oracle.mjs <js-checkout> [--check] [--allow-unpinned]
//
// Writes js-combat-view-oracle.json beside this file. Read-only against the JS checkout (tools/js-core2-shared.mjs pin
// checks). It runs reaction-fights cases 0–9 (tools/oracle-js-core2.mjs: same inputs and driver) and, after every
// command (play a card, End Turn, answer an offer), records what the JS combat screen derives from that dispatch:
//   beats    src/ui/fx.js groupBeats over the dispatch's events (actor, kind, banner, prepared, telegraph, lead, types)
//   floats   src/ui/fx.js guardHitFloatParts + dmgClass for every damageDealt (Poise absorbed, Block, -N, size tier)
//   intents  CombatIntentPresentationModel.combatIntentPresentation(projectCombatIntent(...)) per living enemy, on the
//            player's turn with no offer pending (visibility, title, stance, parts, damage class, tone)
//   offers   reactionChoiceModal.js committedReactionStack → reactionStackEntryName, Back / Resolve, and the incoming
//            line (intentDamageRead of the pending intentPreview)
// plus pure tables: cardHotkeyAction, dmgClass, combatHealthModel badge order, the callout words, actionPacing for the
// default, mobile-fast and Auto settings, and runActionTimeline's stage times over recorded beats (a virtual clock; the
// sprite stage lasts the pace's lunge with the impact at its windup fraction; no hit-stop).
// choiceFights: three content-coverage fights whose decks play the chosen-card and maneuver cards; per command the beats,
// the maneuver picker playCard opens first (cardChoiceModal over cardChoicePlan: eyebrow, title, close and Cancel labels,
// options) and the chosen-card door showPendingDiscard opens (discardChoiceModal: eyebrow, title, prompt, Confirm, Choose
// later, count, ordered, candidate names), answered with the first enabled maneuver and the first N candidates.
import {createHash} from 'node:crypto';
import {existsSync, readFileSync, writeFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {parseArgs, jsIdentity, requirePin, loadJs, toPlain, jsonText} from '../../tools/js-core2-shared.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'js-combat-view-oracle.json');
const USAGE = 'usage: node UnityTests/Core2CombatView/js-combat-view-oracle.mjs <js-checkout> [--check] [--allow-unpinned]';
const args = parseArgs(process.argv.slice(2), USAGE);
let identity;
try { identity = jsIdentity(args.js); requirePin(identity, args.allowUnpinned); }
catch (error) { console.error(`js-combat-view-oracle: ${error.message}`); process.exit(1); }

const js = await loadJs(args.js);
const {profile, registries, load} = js;
const {createRunState} = await load('src/model/state.js');
const {createRng} = await load('src/engine/rng.js');
const {createRunCombat} = await load('src/engine/runCombat.js');
const {dispatch, cardPlayCosts} = await load('src/engine/combat.js');
const {projectCombatIntent} = await load('src/ui/models/CombatIntentProjectionModel.js');
const {combatIntentPresentation, intentDamageRead} = await load('src/ui/models/CombatIntentModel.js');
const {combatHealthModel} = await load('src/ui/models/CombatHealthModel.js');
const {groupBeats, guardHitFloatParts, dmgClass, CALLOUT_KINDS} = await load('src/ui/fx.js');
const {runActionTimeline} = await load('src/ui/actionTimeline.js');
const {actionPacing, setAnimSpeed, setCombatPacing, COMBAT_TIMING} = await load('src/ui/animationPace.js');
const {combatPacingChanges} = await load('src/model/autoCombatPolicy.js');
const {cardHotkeyAction} = await load('src/ui/screens/combat.js');
const {committedReactionStack, reactionStackEntryName} = await load('src/ui/components/reactionChoiceModal.js');
const {t} = await load('src/ui/strings.js');

const ALL_UNLOCKED = {gameProfileUnlocks: {[profile.id]: {completedRunIds: ['oracle-a', 'oracle-b', 'oracle-c']}}};
const reactionTrait = cardId => { const def = registries.cards.get(cardId); return (def.cardTags || def.tags || []).includes('trait:reaction'); };
const plain = value => toPlain(value, 'oracle', []);

// ---- per dispatch: what the screen derives -----------------------------------------------------------------------------
function beatsOf(events) {
  return groupBeats(events).map(beat => ({
    actorId: beat.actorId ?? null, kind: beat.kind ?? null, banner: beat.banner ?? null, prepared: !!beat.prepared, telegraph: !!beat.telegraph,
    lead: beat.lead?.length ?? 0, types: beat.events.map(e => e.type),
  }));
}
function floatsOf(combat, events) {
  const maxHp = id => (id === 'player' ? combat.player : combat.enemies.find(e => e.id === id))?.maxHp ?? 0;
  return events.filter(e => e.type === 'damageDealt').map(e => {
    const parts = guardHitFloatParts(e, maxHp(e.targetId));
    return {targetId: e.targetId, amount: parts.amount, blocked: parts.blocked, poise: parts.poise?.text ?? null, guard: parts.guard?.text ?? null,
      damage: parts.damage?.text ?? null, damageClass: parts.damage?.cls ?? null, hpLoss: e.hpLoss ?? null, hpClass: e.hpLoss > 0 ? dmgClass(e.hpLoss, maxHp(e.targetId)) : null};
  });
}
function intentsOf(combat) {
  if (combat.result || combat.phase !== 'player' || combat.pendingReaction) return null;
  return combat.enemies.filter(e => e.alive).map(enemy => {
    const badge = combatIntentPresentation(projectCombatIntent(combat, enemy.id));
    return {enemyId: enemy.id, visibility: badge.visibility, title: badge.title, stance: badge.stance, damageClass: badge.damageClass ?? null,
      tone: badge.projectedDamage?.tone ?? null, parts: badge.parts.map(p => ({icon: p.icon, value: p.value ?? null, summary: p.summary ?? null}))};
  });
}
function offerOf(combat) {
  const pending = combat.pendingReaction;
  if (!pending) return null;
  const stack = committedReactionStack(combat.reactionStack, pending.groupKey).map(entry => reactionStackEntryName(entry, registries));
  const incoming = intentDamageRead(pending.intentPreview);
  return {offerId: pending.id, sourceId: pending.sourceId, stack, decline: t(stack.length ? 'combat.reaction.resolve' : 'common.back'),
    incoming: incoming ? `${incoming.label} · ${incoming.summary}` : null, options: pending.options.map(o => ({id: o.id, cardId: o.cardId, sp: o.staminaCost, mp: o.manaCost}))};
}

// ---- the fights (tools/oracle-js-core2.mjs reaction-fights inputs and driver) ----------------------------------------------
const FIGHTS = [
  {classId: 'reaver', seed: 42, enemyIds: ['fellWarden'], policy: 'first'},
  {classId: 'reaver', seed: 42, enemyIds: ['gildedKnight'], policy: 'first'},
  {classId: 'reaver', seed: 42, enemyIds: ['courtDuelist'], policy: 'first'},
  {classId: 'rogue', seed: 511, enemyIds: ['courtDuelist'], policy: 'decline'},
  {classId: 'reaver', seed: 1854, enemyIds: ['stitchedKing'], policy: 'first'},
];
const recordedBeats = [];
function fightCase({classId, seed, enemyIds, policy, maxTurns = 30}) {
  const run = createRunState({registries, classId, seed, profileMeta: ALL_UNLOCKED});
  const combat = createRunCombat({registries, run, rng: createRng(seed), enemyIds});
  const steps = [];
  const record = (command, out) => {
    const events = out?.events ?? [];
    const beats = beatsOf(events);
    recordedBeats.push(groupBeats(events));
    steps.push({command, beats, floats: floatsOf(combat, events), intents: intentsOf(combat), offer: offerOf(combat), result: combat.result ?? null});
  };
  steps.push({command: 'start', beats: [], floats: [], intents: intentsOf(combat), offer: offerOf(combat), result: null});
  const answer = () => {
    for (let i = 0; combat.pendingReaction && i < 50; i++) {
      const pending = combat.pendingReaction;
      const optionId = policy === 'first' ? pending.options[0]?.id ?? null : null;
      record(`answer ${optionId ?? 'back'}`, dispatch(combat, {type: 'chooseReaction', offerId: pending.id, optionId}));
    }
  };
  for (let turn = 1; turn <= maxTurns && !combat.result; turn++) {
    answer();
    for (let guard = 0; guard < 20 && !combat.result; guard++) {
      const target = combat.enemies.find(e => e.alive);
      let played = false;
      for (const card of combat.piles.hand) {
        if (reactionTrait(card.cardId)) continue;
        let costs; try { costs = cardPlayCosts(combat, card.instanceId); } catch { continue; }
        if ((costs.stamina || 0) > (combat.player.stamina || 0) || (costs.mana || 0) > (combat.player.mana || 0)) continue;
        for (const targetId of [target?.id, undefined]) {
          try {
            const out = dispatch(combat, {type: 'playCard', cardInstanceId: card.instanceId, ...(targetId ? {targetId} : {})});
            record(`play ${card.cardId}${targetId ? ' ' + targetId : ''}`, out); played = true; break;
          } catch { /* not playable this way */ }
        }
        if (played) break;
      }
      if (!played) break;
      answer();
    }
    if (!combat.result) { record('endTurn', dispatch(combat, {type: 'endTurn'})); answer(); }
  }
  return {input: {classId, seed, enemyIds, policy}, steps, eventDigest: createHash('sha256').update(combat.eventLog.map(e => e.type).join(' ')).digest('hex').slice(0, 24)};
}
const fights = FIGHTS.map(fightCase);

// ---- the card choices (content-coverage inputs that play them; tools/oracle-js-core2.mjs coverageFight's setup) ---------------
// The same driver, except that a card offering a choice records what screens/combat.js playCard opens first
// (cardChoiceModal.js over cardChoicePlan) and plays with the first enabled option, and a pendingCardChoice records what
// showPendingDiscard opens (discardChoiceModal.js) and is answered with its first N zoneCandidates.
const {cardChoicePlan} = await load('src/engine/combat.js');
const {hasCardChoice, zoneCandidates} = await load('src/engine/cardZones.js');
const {resolveCard} = await load('src/model/registries.js');
const cardName = card => resolveCard(registries, card).name;
const CHOICE_FIGHTS = [
  {classId: 'reaver', seed: 41, enemyIds: ['briarHermit'], deck: ['base.shuffle-hand', 'base.scout', 'base.shuffle-deck', 'core.strike', 'base.recall-choose', 'core.strike', 'base.exhaust-choose', 'core.defend', 'base.transmute', 'core.strike']},
  {classId: 'starseer', seed: 42, enemyIds: ['wyrmAspirant'], deck: ['base.transform', 'base.recover-exile', 'base.hide-cards', 'core.strike', 'base.modify-card-stance', 'base.modify-card-range', 'core.strike', 'core.defend', 'base.exhaust', 'core.strike']},
  {classId: 'reaver', seed: 43, enemyIds: ['briarHermit'], deck: ['base.force-stance', 'base.attack-then-defend', 'base.immediate-stance-transition', 'base.conditional-damage-bonus', 'core.strike', 'base.current-stance-variant', 'base.multi-stance', 'core.smash', 'core.strike']},
];
function pickerOf(plan, card) {
  const name = cardName(card);
  return {instanceId: card.instanceId, eyebrow: name, title: plan.kind === 'stance' ? t('cardChoice.stanceTitle') : t('common.choose'), closeLabel: `${t('common.cancel')} ${name}`,
    cancel: t('common.cancel'), options: plan.options.map(o => ({id: o.id, name: `${o.name}${o.active ? ' (active)' : ''}`, text: o.tooltip || '', disabled: !!o.active}))};
}
function doorOf(combat) {
  if (!hasCardChoice(combat)) return null;
  const pending = combat.pendingCardChoice;
  return {eyebrow: registries.cards.get(pending.cardId).name, title: t('combat.cards.choose.title', {count: pending.count}),
    prompt: t('combat.cards.choose.prompt', {from: pending.effect.from, to: pending.effect.to}), confirm: t('combat.cards.choose.confirm'), close: t('combat.discard.later'),
    count: pending.count, ordered: pending.effect.from === pending.effect.to, candidates: zoneCandidates(combat, pending.effect).map(card => ({instanceId: card.instanceId, name: cardName(card)}))};
}
function choiceFightCase({classId, seed, enemyIds, deck, maxTurns = 6}) {
  const run = createRunState({registries, classId, seed, profileMeta: ALL_UNLOCKED});
  run.relics = [];
  run.deck = deck.map((cardId, index) => ({instanceId: `c${index + 1}`, cardId, upgraded: false}));
  const combat = createRunCombat({registries, run, rng: createRng(seed), enemyIds});
  dispatch(combat, {type: 'setReactions', enabled: false});
  const steps = [];
  const record = (command, out, picker = null) => steps.push({command, picker, beats: beatsOf(out?.events ?? []), door: doorOf(combat), result: combat.result ?? null});
  const settle = () => {
    for (let i = 0; i < 50 && hasCardChoice(combat); i++) {
      const pending = combat.pendingCardChoice;
      const ids = zoneCandidates(combat, pending.effect).slice(0, pending.count).map(card => card.instanceId);
      record(`choose ${ids.join(',')}`, dispatch(combat, {type: 'chooseCards', cardInstanceIds: ids}));
    }
  };
  for (let turn = 1; turn <= maxTurns && !combat.result; turn++) {
    for (let guard = 0; guard < 20 && !combat.result; guard++) {
      const target = combat.enemies.find(e => e.alive);
      let played = false;
      for (const card of combat.piles.hand) {
        if (reactionTrait(card.cardId)) continue;
        let costs; try { costs = cardPlayCosts(combat, card.instanceId); } catch { continue; }
        if ((costs.stamina || 0) > (combat.player.stamina || 0) || (costs.mana || 0) > (combat.player.mana || 0)) continue;
        const plan = cardChoicePlan(combat, card.instanceId);
        const picker = plan ? pickerOf(plan, card) : null;
        const choice = plan ? plan.options.find(o => !o.active)?.id : undefined;
        for (const targetId of [target?.id, undefined]) {
          try {
            const out = dispatch(combat, {type: 'playCard', cardInstanceId: card.instanceId, ...(targetId ? {targetId} : {}), ...(choice ? {choice} : {})});
            record(`play ${card.instanceId} ${targetId ?? '-'} ${choice ?? '-'}`, out, picker); played = true; break;
          } catch { /* not playable this way */ }
        }
        if (played) break;
      }
      if (!played) break;
      settle();
    }
    if (!combat.result) { record('endTurn', dispatch(combat, {type: 'endTurn'})); settle(); }
  }
  return {input: {classId, seed, enemyIds}, steps};
}
const choiceFights = CHOICE_FIGHTS.map(choiceFightCase);

// ---- pure tables ---------------------------------------------------------------------------------------------------------
const hotkeys = [];
for (const key of ['1', '5', '9', 'q', 'Q', '0', 'e', 'x']) for (const targeting of [false, true]) for (const handSize of [0, 3, 10]) for (const livingEnemies of [1, 3])
  hotkeys.push({key, targeting, handSize, livingEnemies, expected: cardHotkeyAction(key, {targeting, handSize, livingEnemies})});
const damageClasses = [];
for (const amount of [1, 5, 6, 14, 15, 24, 25, 40]) for (const maxHp of [0, 20, 60, 200]) damageClasses.push({amount, maxHp, expected: dmgClass(amount, maxHp)});
const health = [];
for (const defense of [0, 7]) for (const ward of [0, 3]) for (const poise of [0, 8]) for (const evasion of [0, 2]) for (const precision of [0, 7])
  health.push({input: {defense, ward, poise, poiseMax: 10, evasion, precision}, expected: combatHealthModel({defense, ward, poise, poiseMax: 10, evasion, precision}).badges.map(b => `${b.kind}:${b.value}`)});
const callouts = Object.fromEntries(['hit', 'evadeFailed', 'missed', 'outOfRange', 'dodged', 'staggered', 'countered', 'poiseBreak', 'blockBreak', 'interrupted']
  .map(kind => [kind, {text: t(CALLOUT_KINDS[kind].string), tone: CALLOUT_KINDS[kind].tone}]));
const strings = Object.fromEntries(['combat.reaction.title', 'combat.reaction.eyebrow', 'combat.reaction.play',
  'combat.reaction.stack', 'combat.reaction.resolve', 'common.back', 'common.close', 'combat.endTurn', 'combat.discard', 'combat.exhaust', 'combat.log.title', 'combat.log.full',
  'combat.grid.label', 'combat.reactions.label', 'combat.reactions.enabled', 'combat.reactions.disabled', 'combat.auto.label', 'hud.cinders', 'potions.run.title',
  'combat.intent.damageHp', 'combat.intent.damageBeforeBlock', 'inspector.section.summary', 'inspector.section.state', 'inspector.section.history', 'inspector.section.traits',
  'gameProfile.combat.unreadIntent'].map(id => [id, t(id)]));
strings['combat.reaction.help'] = (await load('src/ui/strings.js')).tFull('combat.reaction.help');
strings['combat.reaction.helpStack'] = (await load('src/ui/strings.js')).tFull('combat.reaction.helpStack');

// ---- pacing and the timeline -----------------------------------------------------------------------------------------------
function paceFor(settings) {
  setAnimSpeed('normal');
  setCombatPacing(settings);
  const pace = actionPacing({reduced: false});
  return {gapMs: pace.gapMs, stepMs: pace.stepMs, bannerMs: pace.bannerMs, lungeMs: pace.spriteSpeed.lungeMs, scale: pace.scale};
}
const mobileFast = combatPacingChanges('combatPacingPreset', 'mobile-fast');
const paces = {default: paceFor({}), mobileFast: paceFor(mobileFast), auto: paceFor({autoCombat: true}), autoMobileFast: paceFor({...mobileFast, autoCombat: true})};
function timeline(beats, pace) {
  let now = 0; const timers = []; const stages = [];
  const setTimer = (fn, ms) => { const timer = {at: now + Math.max(0, ms), fn}; timers.push(timer); return timer; };
  const clearTimer = timer => { const i = timers.indexOf(timer); if (i >= 0) timers.splice(i, 1); };
  const reactionGroups = beat => {
    const groups = []; let group = [], hadHit = false;
    for (const event of beat.events) {
      if (hadHit && ['attackHit', 'attackMissed', 'combatAvoidanceResolved', 'damageDealt'].includes(event.type)) { groups.push({...beat, events: group}); group = []; hadHit = false; }
      group.push(event); if (event.type === 'damageDealt') hadHit = true;
    }
    if (group.length) groups.push({...beat, events: group});
    return groups;
  };
  let done = false;
  const handle = runActionTimeline(beats, {
    onStage: (name, beat) => stages.push({at: Math.round(now * 1000) / 1000, stage: name, beat: beats.indexOf(beat)}),
    onSprite: () => ({impactMs: pace.lungeMs * COMBAT_TIMING.windupFraction, totalMs: pace.lungeMs, cancel() {}}),
    onContact: () => 0, reactionGroups, onReaction: () => 0, onDone: () => { done = true; },
  }, {gapMs: pace.gapMs, intentMs: 0, wordMs: pace.gapMs, afterSpriteMs: pace.gapMs, resultMs: pace.gapMs, reactionMs: pace.gapMs, damageMs: pace.gapMs,
    resetMs: pace.gapMs, stepMs: pace.stepMs, bannerMs: pace.bannerMs, spriteSpeed: {lungeMs: pace.lungeMs}}, {setTimer, clearTimer});
  for (let guard = 0; !done && timers.length && guard < 100000; guard++) {
    timers.sort((a, b) => a.at - b.at);
    const timer = timers.shift(); now = timer.at; timer.fn();
  }
  void handle;
  // Group-stage beat indexes: a reaction group is a copy of its beat, so it reports -1; the C# plan names the beat.
  return {stages: stages.map(s => ({...s, beat: s.beat})), endsAt: Math.round(now * 1000) / 1000};
}
// The timeline cases replay recorded dispatches: the first fight's first three dispatches and its first End Turn.
const firstFight = fights[0];
const endTurnIndex = firstFight.steps.findIndex(step => step.command === 'endTurn') - 1;
const timelineCases = [0, 1, endTurnIndex].filter((i, k, all) => i >= 0 && all.indexOf(i) === k).map(index => ({
  fight: 0, dispatch: index,
  timelines: Object.fromEntries(Object.entries(paces).map(([name, pace]) => [name, timeline(recordedBeats[index], pace)])),
}));

const oracle = plain({
  _: 'Generated by UnityTests/Core2CombatView/js-combat-view-oracle.mjs at the SPEC pin; do not edit.',
  js: {commit: identity.commit, build: identity.build}, profileHash: profile.hash,
  fights, choiceFights, hotkeys, damageClasses, health, callouts, strings, paces, timelines: timelineCases,
});
const text = jsonText(oracle);
if (args.check) {
  if (!existsSync(OUT) || readFileSync(OUT, 'utf8') !== text) { console.error('js-combat-view-oracle.json is stale; rerun the generator'); console.log('js-combat-view-oracle: 0 passed, 1 failed'); process.exit(1); }
  console.log(`js-combat-view-oracle: OK — ${Object.keys(oracle).length + fights.length + choiceFights.length} checks passed`);
} else {
  writeFileSync(OUT, text);
  console.log(`js-combat-view-oracle: wrote ${fights.length} fights (${fights.reduce((n, f) => n + f.steps.length, 0)} steps), ${hotkeys.length} hotkeys, ${health.length} health rows`);
}
