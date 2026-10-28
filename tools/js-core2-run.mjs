// The Core 2 run layer's JS oracles (port plan phase 8), used by tools/oracle-js-core2.mjs.
//
// Every family calls the JS game's own run-layer functions at the SPEC §0.2 pin. The scripted runs replay the
// orchestration src/main.js performs between screens (newRun → startClimb → enterNode → startFightNow/enterCombat →
// onCombatEnd's configured branch → beginGameProfileReward / showGameProfileReward, showShop, showRest, showEvent,
// advanceAct), transcribed here call for call because main.js mounts DOM screens and cannot run headless. Where this
// transcription chooses (which node to travel to, which offer to take) it follows a fixed policy named in each case.
//
// Families and the JS functions they call:
//   run-creation   createRunState (+ main.js newRun's seat draw), gameProfileRatingReceipt, gameProfileEffectiveAttributes
//   run-maps       buildActMap → buildGameProfileActMap / buildBranchedActMap, resolveUnknownNode, eligibleEventIds
//   card-faces     createRunCombat → resolveCombatCard, tacticalCarrier, rangeSourceContact (each card of the profile
//                  dealt by each armament a run can wield)
//   reward-offers  gameProfileRewardOffers (combat and level sources), claimGameProfileReward
//   shop-stock     gameProfileShopStock, purchaseGameProfileShopOffer
//   rest-outcomes  beginGameProfileRest, smithingPlan, commitGameProfileRest (smith, levelUp)
//   level-up       combatXpReceipt, awardLevelXp, xpToNext
//   scripted-runs  whole runs, floor by floor, under a fixed policy (fights played card by card), with phase 10's class
//                  and used-weapon XP (settleConfiguredSkillProgression, skillLevelNotices) and the victory receipt
//   scripted-runs-auto  the same orchestration with every fight built and then settled as won (all three acts)
// Phase 9 and 10 families:
//   adept-entitlements  activeAdeptTraining, collectedAdeptCardIds, unlimitedCardIds, deckCopyLimit, knownCollectedCardIds,
//                  collectedCardProblems, recordCollectedCards, deckCardEquipmentEligible (per held main-hand armament)
//   deck-edits     openDeckEdit (add by tile key, remove, move, undo, confirm, cancel) with the collection tiles
//                  (deckEditorModel), deckEditRefusal, rewardDeckAddRefusal / claimGameProfileRewardToDeck
//   armoury        equipPiece + stampDeck (Replace, Unequip, moves, storage, ownership and slot refusals), ownership,
//                  editingLocked, newArmouryItems / markArmourySeen, the Inspect rows (resolveGearItem at rank + Smith
//                  tier, gear grantedCards, equipmentRequirementReceipt)
//   skill-progression  xpToNext per track kind, awardSkillXp (queueRewards false), settleConfiguredSkillProgression under
//                  varied multipliers, skillLevelNotices, combatXpGains, configuredCharacterLevels with grandfathered
//                  rest points (commitGameProfileRest levelUp)
export async function runFamilies({js, family, plain, fightStart, settle, ALL_UNLOCKED, CLASSES}) {
  const {profile, registries, load} = js;
  const {createRunState} = await load('src/model/state.js');
  const {createRng} = await load('src/engine/rng.js');
  const {createRunCombat, runCombatEnd} = await load('src/engine/runCombat.js');
  const {dispatch, cardPlayCosts} = await load('src/engine/combat.js');
  const {buildActMap, drawSeatOrder, bossEncounterForNode} = await load('src/engine/actmap.js');
  const {rollEncounter, rollRuneReward} = await load('src/engine/encounters.js');
  const {gameProfileRatingReceipt, gameProfileEffectiveAttributes, gameProfileCompletedRunMeta} = await load('src/model/gameProfileRun.js');
  const {gameProfileRewardOffers, claimGameProfileReward, gameProfileShopStock, purchaseGameProfileShopOffer} = await load('src/model/gameProfileRewards.js');
  const {beginGameProfileRest, commitGameProfileRest} = await load('src/model/gameProfileRest.js');
  const {smithingPlan} = await load('src/model/smithing.js');
  const {combatXpReceipt, awardLevelXp, xpToNext} = await load('src/model/levelup.js');
  const {commitEventChoice, choiceAffordable} = await load('src/engine/quests.js');
  const {availableEventChoices} = await load('src/model/quests.js');
  const {eventChoicesWithHistory} = await load('src/content/events.js');
  const {resolveCombatCard} = await load('src/engine/combatExpansion.js');
  const {tacticalCarrier} = await load('src/engine/combatCardTactics.js');
  const {rangeSourceContact} = await load('src/engine/combatEngagement.js');
  const {seatAtTier, seatTierHpMult, bossTierScale} = await load('src/model/seats.js');
  const {gameModule} = await load('src/model/gameProfile.js');
  const {skillXpReceipt, settleConfiguredSkillProgression} = await load('src/engine/skillXp.js');
  const {skillLevelNotices, configuredCharacterLevels, configuredSkillXpMultiplier} = await load('src/model/gameProfileProgression.js');
  const {combatXpGains} = await load('src/model/rewardprogress.js');
  const {xpToNext: skillXpToNext, awardSkillXp} = await load('src/model/skills.js');
  const {activeAdeptTraining, collectedAdeptCardIds, deckCopyLimit} = await load('src/model/deckCopyLimit.js');
  const {knownCollectedCardIds, collectedCardProblems, recordCollectedCards} = await load('src/model/collectedCards.js');
  const {unlimitedCardIds, deckCardEquipmentEligible, deckEditRefusal} = await load('src/model/deckRules.js');
  const {openDeckEdit, deckEditorModel} = await load('src/ui/models/DeckEditorModel.js');
  const {rewardDeckAddRefusal, claimGameProfileRewardToDeck} = await load('src/model/gameProfileRewards.js');
  const {equipPiece, ownership, stampDeck, fitsSlot, equipmentRequirementReceipt} = await load('src/model/loadout.js');
  const {editingLocked, newArmouryItems, markArmourySeen, armouryHasNew} = await load('src/model/armouryRules.js');
  const {resolveGearItem} = await load('engine/kernel/gameConfig.js');

  const clone = value => structuredClone(plain(value));
  const counters = rng => Object.fromEntries(Object.entries(rng.getCounters()).sort());
  const newRun = (classId, seed) => {
    // main.js newRun: createRunState, then the seat order drawn once on a fresh rng's `seats` stream.
    const run = createRunState({registries, classId, seed, profileMeta: ALL_UNLOCKED, runId: `oracle-${classId}-${seed}`});
    run.stats = {fightsWon: 0, damageDealt: 0, damageTaken: 0}; run.path = []; run.seenEvents = []; run.lastEncounters = [];
    const rng = createRng(seed);
    run.seatOrder = drawSeatOrder(registries, rng, {firstSeat: null});
    return {run, rng};
  };
  const tier = run => run.actNumber; // no Endless in Core 2
  const seat = run => seatAtTier(run.seatOrder, tier(run));

  // The run fields this port carries (a Core 2 save's own field names); everything else JS keeps is legacy bookkeeping.
  const deckView = cards => (cards || []).map(card => ({instanceId: card.instanceId, cardId: card.cardId}));
  function runView(run, rng) {
    const ratings = gameProfileRatingReceipt(registries, run);
    return clone({
      class: run.class, actNumber: run.actNumber, floor: run.floor, mapNodeId: run.mapNodeId, path: run.path,
      hp: run.hp, maxHp: run.maxHp, mana: run.mana, maxMana: run.maxMana, stamina: run.stamina, maxStamina: run.maxStamina,
      energyMax: run.energyMax, drawPerTurn: run.drawPerTurn, maxHpAdjustment: run.maxHpAdjustment,
      equipmentPoolBonuses: run.equipmentPoolBonuses, equipmentPoolDeficits: run.equipmentPoolDeficits,
      cinders: run.cinders, attributes: run.attributes, effectiveAttributes: gameProfileEffectiveAttributes(registries, run),
      level: run.level, levelUps: run.levelUps, levelPoints: run.levelPoints,
      ratings: ratings.totals, baseEvasionBonus: ratings.baseEvasionBonus || 0, basePrecisionBonus: ratings.basePrecisionBonus || 0,
      deck: deckView(run.deck), sideboard: deckView(run.sideboard), collectedCardIds: run.collectedCardIds || [],
      relics: run.relics, flasks: run.flasks, flaskCharges: run.flaskCharges,
      loadout: {sets: run.loadout.sets, active: run.loadout.active, storage: run.loadout.storage, boughtArmour: run.loadout.boughtArmour || []},
      itemUpgradeLevels: run.itemUpgradeLevels || {}, seatOrder: run.seatOrder, history: run.history, seenEvents: run.seenEvents,
      lastEncounters: run.lastEncounters, bossesBeaten: run.bossesBeaten || [], fightsWon: run.stats?.fightsWon ?? 0,
      skills: run.skills || {},
      rng: rng ? counters(rng) : null,
    });
  }

  // ── run creation ─────────────────────────────────────────────────────────────────────────────
  const creation = [];
  for (const classId of CLASSES) for (const seed of [1, 511, 1854, 4242]) {
    const {run, rng} = newRun(classId, seed);
    creation.push({input: {classId, seed}, expected: {run: runView(run, rng)}});
  }
  family('run-creation', 'phase 8', 'A new Core 2 run per class and seed: six-point attributes, starter deck with gear grants, fixed loadout, starting relics, flask charges, derived pools, ratings and the seat order.',
    ['src/model/state.js createRunState', 'src/main.js newRun (drawSeatOrder)', 'src/model/gameProfileRun.js gameProfileRatingReceipt'], creation);

  // ── maps ─────────────────────────────────────────────────────────────────────────────────────
  const maps = [];
  const mapHistories = [[], [{kind: 'eventChoice', eventId: 'lastLantern', choiceId: 'buyOil', actNumber: 1, floor: 2, mapNodeId: 'n2_0'},
    {kind: 'eventChoice', eventId: 'graveOfTheNameless', choiceId: 'payRespects', actNumber: 1, floor: 4, mapNodeId: 'n4_1'}]];
  for (const seed of [1, 2, 3, 7, 42, 511, 1854, 9001, 31337, 77777]) {
    const {run, rng} = newRun('reaver', seed);
    for (let act = 1; act <= 3; act++) {
      run.actNumber = act;
      const history = mapHistories[(seed + act) % 2];
      const before = counters(rng);
      const graph = buildActMap(registries, rng, seat(run), act, null, {history});
      maps.push({input: {seed, act, seat: seat(run), counters: before, history}, expected: {graph: clone(graph), counters: counters(rng)}});
    }
  }
  family('run-maps', 'phase 8', 'The branched act map for fixed seeds and acts: shared opening row, 1–5 branches with one boss each, rests on depths 3/6/9, guaranteed elites, 15% shops, fog types, event pre-rolls with quest gates.',
    ['src/engine/actmap.js buildActMap/buildBranchedActMap', 'src/engine/encounters.js resolveUnknownNode/eligibleEventIds'], maps);

  // ── card faces ───────────────────────────────────────────────────────────────────────────────
  const faces = [];
  const faceCards = [...new Set([...profile.content.cards.filter(card => card.id.startsWith('core.') || card.id.startsWith('base.')).map(card => card.id), 'guilt'])];
  const wieldable = (registries.equipment.armaments || []).map(row => row.id);
  for (const rightHand of [null, ...wieldable]) {
    const {run} = newRun('reaver', 1);
    run.loadout.sets.rightHand = [rightHand, null, null]; run.loadout.sets.leftHand = [null, null, null];
    const combat = createRunCombat({registries, rng: createRng(1), run, enemyIds: ['wanderingSoldier']});
    const out = {};
    for (const cardId of faceCards) {
      if (!registries.cards.has(cardId)) continue;
      const def = resolveCombatCard(combat, {instanceId: 'face', cardId, upgraded: false});
      const carrier = tacticalCarrier(def, {}, combat, combat.player);
      const damage = (def.effects || []).find(effect => effect.op === 'damage') || {};
      const source = rangeSourceContact(combat, combat.player, null, carrier, damage, '0');
      out[cardId] = {combatProfile: carrier.combatProfile, rangeSource: {coverage: source.coverage, weapon: source.weapon, sourceWeaponId: source.sourceWeaponId,
        throwCapable: source.throwCapable, bypass: source.bypass}};
    }
    faces.push({input: {classId: 'reaver', rightHand}, expected: {faces: clone(out)}});
  }
  family('card-faces', 'phase 8', 'Each player card\'s fight face (combat profile with the wielded piece\'s damage type, and the range source its damage commits) for every armament in the main hand.',
    ['src/engine/combatCardTactics.js tacticalCarrier', 'src/engine/combatEngagement.js rangeSourceContact'], faces);

  // ── reward offers and shop stock ─────────────────────────────────────────────────────────────
  // A run varied by earlier claims: each case starts from a fresh run of the class, claims the listed earlier offers
  // (index 0) and then rolls the offer under test. The input carries the run state and the RNG counters.
  const rewards = [], shops = [];
  for (const classId of CLASSES) for (const seed of [3, 19, 77, 1854, 60606]) {
    const {run, rng} = newRun(classId, seed);
    for (let i = 0; i < 4; i++) {
      const source = i === 2 ? 'level' : 'combat';
      const offerId = `1:n${i + 1}_0:${source}`;
      const state = runView(run, rng);
      const before = counters(rng);
      const offer = gameProfileRewardOffers(registries, run, rng, {offerId, source});
      const expected = {offer: clone(offer), counters: counters(rng)};
      let claimed = null;
      try { claimed = clone(claimGameProfileReward(registries, run, offerId, 0)); } catch (error) { claimed = {error: error.message}; }
      expected.claimed = claimed; expected.after = runView(run, rng);
      rewards.push({input: {classId, seed, offerId, source, counters: before, run: state, takeIndex: 0}, expected});
    }
    const state = runView(run, rng);
    const before = counters(rng);
    const stock = gameProfileShopStock(registries, run, rng, {shopId: '1:n4_1'});
    const expected = {stock: clone(stock), counters: counters(rng), purchases: []};
    run.cinders = 25;
    for (let index = 0; index < stock.options.length && expected.purchases.length < 3; index++) {
      try { expected.purchases.push({index, receipt: clone(purchaseGameProfileShopOffer(registries, run, '1:n4_1', index))}); }
      catch (error) { expected.purchases.push({index, error: error.message}); }
    }
    expected.after = runView(run, rng);
    shops.push({input: {classId, seed, shopId: '1:n4_1', counters: before, run: state, cindersBeforePurchases: 25}, expected});
  }
  family('reward-offers', 'phase 8', 'Victory and level reward offers (groups cards 60 / equipment+relics 10 / potions 30, three offers, take one, stance-coverage reservation, owned basics skipped) and the claim.',
    ['src/model/gameProfileRewards.js gameProfileRewardOffers/claimGameProfileReward', 'engine/kernel/rewardPolicy.js pickRewardCategory', 'src/model/stanceCoverage.js reserveStanceCards'], rewards);
  family('shop-stock', 'phase 8', 'A Core 2 shop: three offers per category (weapons, relics, combat cards, weapon arts, potions, armor) at price 10, and purchases until cinders run out.',
    ['src/model/gameProfileRewards.js gameProfileShopStock/purchaseGameProfileShopOffer'], shops);

  // ── rest outcomes ────────────────────────────────────────────────────────────────────────────
  const restNode = run => {
    run.mapGraph = {nodes: {n3_0: {id: 'n3_0', floor: 3, type: 'shrine', actualType: 'rest', next: []}}};
    run.mapNodeId = 'n3_0'; run.floor = 3;
  };
  const rests = [];
  for (const classId of CLASSES) for (const [seed, hpLoss, xp] of [[5, 30, 0], [6, 7, 260], [8, 0, 900]]) {
    const {run, rng} = newRun(classId, seed);
    run.hp = Math.max(1, run.hp - hpLoss); run.mana = 0; run.flaskCharges.hpCurrent = 0;
    if (xp) awardLevelXp(registries, run, xp, {pointsPerLevel: gameModule(run, 'progression').attributePointsPerLevel, grantStats: true});
    restNode(run);
    const state = runView(run, rng);
    const visit = clone(beginGameProfileRest(run));
    const healed = runView(run, rng);
    const candidates = smithingPlan(registries, run, undefined, {free: true}).candidates.map(row => ({itemRef: row.itemRef, currentLevel: row.currentLevel, nextLevel: row.nextLevel}));
    const services = [];
    if (candidates.length) {
      try { services.push({service: 'smith', itemRef: candidates[0].itemRef, receipt: clone(commitGameProfileRest(registries, run, 'smith', {itemRef: candidates[0].itemRef})), after: runView(run, rng)}); }
      catch (error) { services.push({service: 'smith', itemRef: candidates[0].itemRef, error: error.message}); }
    }
    const attributeId = ['strength', 'constitution', 'wisdom', 'intelligence'][seed % 4];
    try { services.push({service: 'levelUp', attributeId, receipt: clone(commitGameProfileRest(registries, run, 'levelUp', {attributeId})), after: runView(run, rng)}); }
    catch (error) { services.push({service: 'levelUp', attributeId, error: error.message}); }
    rests.push({input: {classId, seed, run: state, visitId: '1:n3_0'}, expected: {visit, healed, candidates, services, visitAfter: clone(run.gameProfileRestVisits['1:n3_0'])}});
  }
  family('rest-outcomes', 'phase 8', 'A Core 2 rest: full recovery on arrival (HP, SP, MP, flask charges), the free Smith (one tier on the chosen item, pools re-derived) and Level Up (one earned point), each usable once per visit.',
    ['src/model/gameProfileRest.js beginGameProfileRest/commitGameProfileRest', 'src/model/smithing.js smithingPlan/commitSmithing', 'src/model/levelup.js applyLevelUp/rederivePools'], rests);

  // ── level-up ─────────────────────────────────────────────────────────────────────────────────
  const levels = [];
  for (let level = 1; level <= 21; level++) levels.push({input: {kind: 'xpToNext', level}, expected: {xp: xpToNext(registries, level)}});
  for (const classId of CLASSES) for (const gains of [[0], [150, 60], [199, 1, 250], [1000], [5000, 9000]]) {
    const {run} = newRun(classId, 9);
    const awards = gains.map(gain => clone(awardLevelXp(registries, run, gain, {pointsPerLevel: gameModule(run, 'progression').attributePointsPerLevel, grantStats: true})));
    levels.push({input: {kind: 'award', classId, gains}, expected: {awards, level: clone(run.level), pools: [run.hp, run.maxHp, run.mana, run.maxMana, run.stamina, run.maxStamina, run.energyMax, run.drawPerTurn]}});
  }
  for (const encounter of registries.encounters.all()) {
    const enemies = encounter.enemies.map((enemyId, i) => {
      const def = registries.enemies.get(enemyId);
      return {enemyId, alive: false, hp: 0, maxHp: Math.round((def.hp[0] + def.hp[1]) / 2) + i, level: 1};
    });
    levels.push({input: {kind: 'combatXp', pool: encounter.pool, enemies}, expected: clone(combatXpReceipt(registries, {victory: true, pool: encounter.pool, enemies}))});
  }
  family('level-up', 'phase 8', 'Character XP (fight XP from defeated enemies\' combat power and levels), the level curve and climbing, one attribute point per level, pools re-derived at the new level.',
    ['src/model/levelup.js combatXpReceipt/awardLevelXp/xpToNext', 'src/model/combatPower.js enemyCombatPower'], levels);

  // ── scripted runs ────────────────────────────────────────────────────────────────────────────
  const POLICY = 'travel to next[(path length + seed) % count]; fights: reactions off, play the first affordable card in hand order at the first living enemy, End Turn; '
    + 'rewards: take offer 0; shops: buy offers in order while cinders last (at most 3); rests: Smith the first candidate, then Level Up the listed attribute while points wait; '
    + 'events: the first open, affordable choice whose effects can run in Core 2 (swapClass refuses there)';
  // The auto-resolved variant (scripted-runs-auto) builds every fight for real (setup, enemy HP rolls, the shuffle, the
  // first plans and combatStart triggers all draw as in play) and then settles it as won without playing a card, so a run
  // reaches the later acts whatever the combat domain has ported; the played variant checks the fights themselves.
  let autoFights = false;
  function playFight(run, rng, nodeId, encounterId) {
    const enc = registries.encounters.get(encounterId);
    run.combatEntered = {nodeId, encounterId};
    const boss = bossTierScale(registries, {encounter: enc, tier: tier(run)});
    const hpMult = boss ? boss.hp : seatTierHpMult(registries, seat(run), tier(run));
    const damageMult = boss ? boss.damage : 1;
    const entryRng = createRng(rng.seed, rng.getCounters());
    const combat = createRunCombat({registries, rng: entryRng, run, settings: {}, enemyIds: enc.enemies, encounter: enc, hpMult, enemyDamageMult: damageMult, enemyStatuses: [], playerStatuses: []});
    rng.restoreCounters(entryRng.getCounters()); combat.rng = rng;
    const start = fightStart(combat, run);
    dispatch(combat, {type: 'setReactions', enabled: false});
    let turns = 0;
    if (autoFights) {
      for (const enemy of combat.enemies) { enemy.hp = 0; enemy.alive = false; }
      combat.result = 'victory';
    }
    for (let turn = 1; turn <= 60 && !combat.result; turn++) {
      turns = turn;
      settle(combat);
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
        settle(combat);
      }
      if (!combat.result) { dispatch(combat, {type: 'endTurn'}); settle(combat); }
    }
    return {combat, enc, start, turns, hpMult, damageMult};
  }
  function settleFight(run, rng, fight, steps) {
    const {combat, enc} = fight;
    const result = combat.result;
    // main.js onCombatEnd, configured branch: the skill tracks (class and used-weapon XP) paid once, their level notices,
    // then character XP (only while progression.characterLevels is on) and the victory receipt.
    runCombatEnd(run, combat);
    const xpBefore = {character: {...run.level}, tracks: Object.fromEntries(Object.entries(run.skills || {}).map(([id, row]) => [id, {level: row.level || 0, xp: row.xp || 0}]))};
    const skillStateBefore = {classMasteryState: structuredClone(run.classMasteryState)};
    const trackReceipt = skillXpReceipt(combat);
    for (const id of Object.keys(trackReceipt)) trackReceipt[id] = Math.floor(trackReceipt[id] * 1); // featMultiplier: a Core run has no feats
    const {trackAwards, classAward} = settleConfiguredSkillProgression(registries, run, combat, {receipt: trackReceipt, victory: result === 'victory', pool: enc.pool, classMultiplier: 1});
    const progression = skillLevelNotices(registries, [classAward, ...trackAwards], {run, beforeState: skillStateBefore, usedArmaments: combat.skillXp?.[combat.playerKey || 'player']?.usedArmaments || []});
    const xp = combatXpReceipt(registries, {victory: result === 'victory', pool: enc.pool, enemies: combat.enemies, characterMultiplier: 1});
    const levelXp = configuredCharacterLevels(run) ? xp.total : 0;
    const levelAward = awardLevelXp(registries, run, levelXp, {pointsPerLevel: gameModule(run, 'progression').attributePointsPerLevel, grantStats: true});
    const title = enc.pool === 'boss' ? `${registries.enemies.get(enc.enemies[0]).name.toUpperCase()} FALLS` : enc.pool === 'elite' ? 'ELITE VANQUISHED' : 'VICTORY';
    const victoryReceipt = result === 'victory' ? {title, xpBefore, xpGains: combatXpGains({awards: [classAward, ...trackAwards], levelGained: levelAward.gained})} : null;
    const step = {kind: 'fight', nodeId: run.mapNodeId, encounterId: enc.id, pool: enc.pool, hpMult: fight.hpMult, start: fight.start, result: result ?? 'stalled', turns: fight.turns,
      enemies: combat.enemies.map(e => ({enemyId: e.enemyId, maxHp: e.maxHp, hp: e.hp, level: e.level ?? null})), xp: clone(xp), levelUps: levelAward.levelUps,
      progression: clone(progression), victoryReceipt: clone(victoryReceipt)};
    if (result !== 'victory') {
      run.hp = 0; run.combatEntered = null;
      step.run = runView(run, rng); steps.push(step);
      return {done: true, victory: false};
    }
    run.stats.fightsWon += 1;
    run.combatEntered = null;
    run.cinders += Math.floor(rollRuneReward(registries, rng, enc.pool, run.relics));
    const boss = enc.pool === 'boss';
    if (boss) run.bossesBeaten = [...new Set([...(run.bossesBeaten || []), ...enc.enemies])];
    step.run = runView(run, rng); steps.push(step);
    if (boss && run.actNumber >= (gameModule(run, 'routes')?.acts || 3)) return {done: true, victory: true};
    takeRewards(run, rng, 'combat', levelAward.levelUps, steps);
    if (boss) advanceAct(run, rng, steps);
    return {done: false};
  }
  function takeRewards(run, rng, source, levelOffers, steps) {
    // beginGameProfileReward then showGameProfileReward's Continue (level offers follow, one per level earned).
    let offerId = `${run.actNumber}:${run.mapNodeId}:${source}`;
    let pending = {offerId, source, levelOffers};
    gameProfileRewardOffers(registries, run, rng, {offerId, source});
    for (;;) {
      const offer = run.gameProfileRewardOffers[pending.offerId];
      let claimed = null;
      if (offer.options.length) { try { claimed = clone(claimGameProfileReward(registries, run, pending.offerId, 0)); } catch (error) { claimed = {error: error.message}; } }
      steps.push({kind: 'reward', offerId: pending.offerId, source: pending.source, offer: clone(offer), claimed, run: runView(run, rng)});
      if (pending.levelOffers > 0) {
        const nextId = `${pending.offerId}:level:${pending.levelOffers}`;
        gameProfileRewardOffers(registries, run, rng, {offerId: nextId, source: 'level'});
        pending = {...pending, offerId: nextId, source: 'level', levelOffers: pending.levelOffers - 1};
        continue;
      }
      break;
    }
  }
  function advanceAct(run, rng, steps) {
    run.actNumber += 1; run.floor = 0; run.mapNodeId = null; run.path = []; run.lastEncounters = [];
    run.hp = run.maxHp;
    run.mapGraph = buildActMap(registries, rng, seat(run), tier(run), null, {history: run.history});
    steps.push({kind: 'advanceAct', act: run.actNumber, graph: clone(run.mapGraph), run: runView(run, rng)});
  }
  function scriptedRun({classId, seed, levelAttribute}, auto = false) {
    autoFights = auto;
    const {run, rng} = newRun(classId, seed);
    run.mapGraph = buildActMap(registries, rng, seat(run), tier(run), null, {history: run.history});
    const steps = [{kind: 'start', graph: clone(run.mapGraph), run: runView(run, rng)}];
    let outcome = null;
    for (let guard = 0; guard < 45 && !outcome; guard++) {
      const graph = run.mapGraph;
      const options = run.mapNodeId ? graph.nodes[run.mapNodeId].next : graph.startIds;
      const nodeId = options[(run.path.length + seed) % options.length];
      // enterNode
      const node = graph.nodes[nodeId];
      run.mapNodeId = nodeId;
      if (!run.path.includes(nodeId)) run.path.push(nodeId);
      run.floor = node.floor;
      let kind = node.type;
      steps.push({kind: 'travel', nodeId, type: node.type, actualType: node.actualType});
      if (kind === 'event') {
        const res = node.resolved || {kind: 'fight'};
        if (res.kind === 'event') {
          run.seenEvents.push(res.eventId);
          const def = registries.events.get(res.eventId);
          const choices = availableEventChoices(eventChoicesWithHistory(def), run).map(row => row.choice)
            .filter(choice => choiceAffordable(choice, run) && !(choice.effects || []).some(effect => effect.op === 'swapClass'));
          const choice = choices[0];
          const receipt = clone(commitEventChoice({run, registries, rng}, {eventId: res.eventId, choiceId: choice.id}));
          steps.push({kind: 'event', eventId: res.eventId, choiceId: choice.id, completions: receipt.completions, run: runView(run, rng)});
          if (run.combatEntered) {
            const encounterId = typeof run.combatEntered === 'string' ? run.combatEntered : run.combatEntered.encounterId;
            run.combatEntered = null;
            const settled = settleFight(run, rng, playFight(run, rng, nodeId, encounterId), steps);
            if (settled.done) outcome = settled;
          }
          continue;
        }
        kind = res.kind;
      }
      if (kind === 'monster' || kind === 'fight' || kind === 'elite' || kind === 'boss') {
        const pool = kind === 'elite' ? 'elite' : kind === 'boss' ? 'boss' : 'normal';
        const encounterId = pool === 'boss' ? bossEncounterForNode(registries, graph, nodeId, {seat: seat(run), tier: tier(run)})
          : rollEncounter(registries, rng, {pool, seat: seat(run), exclude: run.lastEncounters});
        if (pool === 'normal') { run.lastEncounters.push(encounterId); if (run.lastEncounters.length > 2) run.lastEncounters.shift(); }
        const settled = settleFight(run, rng, playFight(run, rng, nodeId, encounterId), steps);
        if (settled.done) outcome = settled;
      } else if (kind === 'shrine') {
        const visit = clone(beginGameProfileRest(run, `${run.actNumber}:${run.mapNodeId}`));
        const candidates = smithingPlan(registries, run, undefined, {free: true}).candidates.map(row => row.itemRef);
        const services = [];
        if (candidates.length) { try { commitGameProfileRest(registries, run, 'smith', {itemRef: candidates[0]}); services.push({service: 'smith', itemRef: candidates[0]}); } catch (error) { services.push({service: 'smith', error: error.message}); } }
        if (run.level.unspentPoints > 0) { commitGameProfileRest(registries, run, 'levelUp', {attributeId: levelAttribute}); services.push({service: 'levelUp', attributeId: levelAttribute}); }
        steps.push({kind: 'rest', visit, candidates, services, run: runView(run, rng)});
      } else if (kind === 'merchant') {
        const shopId = `${run.actNumber}:${nodeId}`;
        const stock = gameProfileShopStock(registries, run, rng, {shopId});
        const purchases = [];
        for (let index = 0; index < stock.options.length && purchases.length < 3; index++) {
          if (run.cinders < stock.price) break;
          try { purchaseGameProfileShopOffer(registries, run, shopId, index); purchases.push(index); } catch { /* unavailable */ }
        }
        steps.push({kind: 'shop', shopId, stock: clone(stock), purchases, run: runView(run, rng)});
      } else if (kind === 'treasure') {
        takeRewards(run, rng, 'combat', 0, steps);
      } else throw new Error(`scripted run: unknown node kind ${kind}`);
    }
    const finish = outcome ? {victory: outcome.victory, unlocked: gameProfileCompletedRunMeta(run, {}, {finishId: run.runId, victory: outcome.victory}).unlocked} : null;
    return {steps, finish};
  }
  progressFamilies();

  const scripted = [
    {classId: 'reaver', seed: 11, levelAttribute: 'constitution'},
    {classId: 'reaver', seed: 1854, levelAttribute: 'strength'},
    {classId: 'rogue', seed: 23, levelAttribute: 'dexterity'},
    {classId: 'starseer', seed: 31, levelAttribute: 'intelligence'},
    {classId: 'herald', seed: 47, levelAttribute: 'wisdom'},
    {classId: 'reaver', seed: 90210, levelAttribute: 'constitution'},
  ].map(input => ({input: {...input, policy: POLICY}, expected: scriptedRun(input)}));
  family('scripted-runs', 'phase 8', 'Whole Core 2 runs under a fixed policy: map travel, fights with their setups, victory spoils (cinders, XP, offers), level offers, shops, rests, events with quest gates, act changes and the finish with its class unlock.',
    ['src/main.js newRun/startClimb/enterNode/startFightNow/enterCombat/onCombatEnd/beginGameProfileReward/showShop/showRest/showEvent/advanceAct', 'src/engine/runCombat.js createRunCombat/runCombatEnd'], scripted);
  const AUTO_POLICY = POLICY.replace('fights: reactions off, play the first affordable card in hand order at the first living enemy, End Turn',
    'fights: built for real, then settled as won before the first card (every enemy defeated)');
  const autoRuns = [
    {classId: 'reaver', seed: 2, levelAttribute: 'constitution'},
    {classId: 'reaver', seed: 1854, levelAttribute: 'strength'},
    {classId: 'rogue', seed: 23, levelAttribute: 'dexterity'},
    {classId: 'rogue', seed: 606, levelAttribute: 'constitution'},
    {classId: 'starseer', seed: 31, levelAttribute: 'intelligence'},
    {classId: 'starseer', seed: 4040, levelAttribute: 'wisdom'},
    {classId: 'herald', seed: 47, levelAttribute: 'wisdom'},
    {classId: 'herald', seed: 777, levelAttribute: 'strength'},
  ].map(input => ({input: {...input, policy: AUTO_POLICY}, expected: scriptedRun(input, true)}));
  family('scripted-runs-auto', 'phase 8', 'Whole Core 2 runs to the end of act 3 with every fight built and then settled as won: the run layer across all three acts (spoils, level offers, rests with Smith and Level Up, shops, events and quest steps, act changes, the final boss and the class unlock).',
    ['src/main.js (as scripted-runs)', 'src/engine/runCombat.js createRunCombat/runCombatEnd'], autoRuns);

  // ── phase 9 and 10: collection, deck editing, armoury, skill progression ────────────────────────────────────────────
  // Each case's input carries the run state its operations start from (stateView: the run view plus the full card
  // instances, the mint counter and the Armoury's seen list), and the operations themselves, so the port replays them
  // verbatim; every operation calls the JS game's own function on that state.
  function progressFamilies() {
    const fullCards = cards => (cards || []).map(card => ({...card}));
    const stateView = (run, rng) => clone({...runView(run, rng), deck: fullCards(run.deck), sideboard: fullCards(run.sideboard),
      editMintCounter: run.editMintCounter ?? null, armorySeen: run.armorySeen ?? null, loadout: {...runView(run, rng).loadout, creationArmourGrant: run.loadout.creationArmourGrant ?? null}});
    const pileView = run => clone({deck: run.deck.map(c => c.instanceId + '=' + c.cardId), sideboard: (run.sideboard || []).map(c => c.instanceId + '=' + c.cardId),
      collectedCardIds: run.collectedCardIds || [], editMintCounter: run.editMintCounter ?? null});
    const coreCards = profile.content.cards.filter(card => card.id.startsWith('core.')).map(card => card.id);
    const someBase = profile.content.cards.filter(card => card.id.startsWith('base.')).map(card => card.id).slice(0, 24);
    // Grow a run's collection the way play does: level offers (combat cards only), the first option claimed to the sideboard.
    function grow(run, rng, rounds) {
      for (let i = 0; i < rounds; i++) {
        const offerId = `1:grow${i}:level`;
        const offer = gameProfileRewardOffers(registries, run, rng, {offerId, source: 'level'});
        if (offer.options.length) { try { claimGameProfileReward(registries, run, offerId, i % offer.options.length); } catch { /* unavailable */ } }
      }
    }

    // ── adept entitlements ──
    const adept = [];
    for (const classId of CLASSES) for (const [label, setup] of [
      ['starter', () => {}],
      ['collected', run => { run.collectedCardIds = [...new Set([...(run.collectedCardIds || []), 'core.smash', 'core.sweep', 'core.evade', 'core.cantrip', 'core.manaSurge', 'core.advance'])]; }],
      ['sideboard', run => { run.sideboard.push({instanceId: 'side1', cardId: 'core.sweep', upgraded: false}, {instanceId: 'side2', cardId: 'core.cantrip', upgraded: false}, {instanceId: 'side3', cardId: 'core.strike', upgraded: false}); }],
      ['malformed', run => { run.collectedCardIds = ['core.strike', 'core.strike', 'not.a.card']; }],
    ]) {
      const {run, rng} = newRun(classId, 33);
      setup(run);
      const state = stateView(run, rng);
      const problems = collectedCardProblems(run);
      let recorded = null;
      if (!problems.length) { recorded = recordCollectedCards(run); }
      const expected = {problems, recorded, known: knownCollectedCardIds(run), training: activeAdeptTraining(run), adeptIds: collectedAdeptCardIds(run),
        unlimited: unlimitedCardIds(run), copyLimits: Object.fromEntries([...coreCards, ...someBase].map(id => [id, deckCopyLimit(registries, id, {}, run)])),
        collectedAfter: run.collectedCardIds};
      // Equipment eligibility of every Core and sampled base card for each main-hand armament (off hand empty).
      const eligibility = {};
      for (const rightHand of [null, ...wieldable]) {
        const probe = structuredClone(run);
        probe.loadout.sets.rightHand = [rightHand, null, null]; probe.loadout.sets.leftHand = [null, null, null];
        eligibility[rightHand ?? 'none'] = [...coreCards, ...someBase].filter(id => deckCardEquipmentEligible(registries, probe, {cardId: id}));
      }
      expected.eligibility = eligibility;
      adept.push({input: {classId, label, run: state, cards: [...coreCards, ...someBase], hands: [null, ...wieldable]}, expected: clone(expected)});
    }
    // A lent card is eligible only while its piece is worn (boneSceptre lends Mana Surge).
    for (const classId of CLASSES) {
      const {run, rng} = newRun(classId, 34);
      run.loadout.sets.rightHand = ['boneSceptre', null, null];
      stampDeck(registries, run);
      const state = stateView(run, rng);
      const granted = run.deck.filter(card => card.grantedBy);
      const worn = granted.map(card => deckCardEquipmentEligible(registries, run, card));
      const probe = structuredClone(run);
      probe.loadout.sets.rightHand = [null, null, null];
      const bare = granted.map(card => deckCardEquipmentEligible(registries, probe, card));
      adept.push({input: {classId, label: 'granted', run: state, granted: clone(granted)}, expected: clone({worn, bare})});
    }
    family('adept-entitlements', 'phase 9', 'Core 2 collection knowledge and Adept entitlements: the collected-card ledger and its refusals, Martial (Basic) and Magic (Cantrip) Adept, the unlimited cards, copy limits, and each card\'s equipment eligibility per held weapon (a lent card only while its piece is worn).',
      ['src/model/collectedCards.js knownCollectedCardIds/collectedCardProblems/recordCollectedCards', 'src/model/deckCopyLimit.js activeAdeptTraining/collectedAdeptCardIds/deckCopyLimit',
        'src/model/deckRules.js unlimitedCardIds/deckCardEquipmentEligible'], adept);

    // ── deck edits ──
    const tileView = run => {
      const model = deckEditorModel({registries, run, settings: {}});
      return clone(model.collection.map(tile => ({key: tile.key, unlimited: !!tile.unlimited, owned: Number.isFinite(tile.owned) ? tile.owned : null, inDeck: tile.inDeck,
        addable: !!tile.addable, refused: !!tile.refusal, removeInstanceId: tile.removeInstanceId ?? null})).sort((a, b) => a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
    };
    const SETTINGS = [
      ['default', {}],
      ['min12', {deckMinSize: 12}],
      ['max11', {deckMaxUnlimited: false, deckMaxSize: 11}],
      ['maxBelowMin', {deckMaxUnlimited: false, deckMaxSize: 5}],
      ['ordered', {playInDeckOrder: true}],
    ];
    const edits = [];
    let editCase = 0;
    for (const classId of CLASSES) for (const seed of [7, 1854]) for (const [settingsLabel, settings] of SETTINGS) {
      if ((editCase++ % 2) && settingsLabel !== 'default') continue; // every class and seed gets the defaults; the variants rotate
      const {run, rng} = newRun(classId, seed);
      grow(run, rng, 3);
      if (classId === 'starseer' || classId === 'herald') run.collectedCardIds = [...new Set([...(run.collectedCardIds || []), 'core.cantrip'])];
      const state = stateView(run, rng);
      // The operations: every unlimited mint, each sideboard card by id, two removals, an Undo, a bad mint, moves, Confirm.
      const ops = [];
      for (const id of unlimitedCardIds(run)) ops.push({op: 'add', key: `basic:${id}`});
      for (const card of run.sideboard) ops.push({op: 'add', key: `card:${card.cardId}`});
      ops.push({op: 'remove', instanceId: run.deck[0].instanceId});
      ops.push({op: 'remove', instanceId: run.deck[run.deck.length - 1].instanceId});
      ops.push({op: 'undo'});
      ops.push({op: 'add', key: 'basic:core.reaverSpecial'});
      ops.push({op: 'add', key: `card:${run.deck[1].cardId}`});
      ops.push({op: 'moveTo', instanceId: run.deck[2].instanceId, index: 0});
      ops.push({op: 'tiles'});
      for (let i = 0; i < 4; i++) ops.push({op: 'remove', instanceId: run.deck[3 + i].instanceId});
      ops.push({op: 'confirm'});
      ops.push({op: 'tiles'});
      const session = openDeckEdit(registries, run, settings);
      const results = [{op: 'open', tiles: tileView(run), piles: pileView(run)}];
      let closed = false;
      for (const op of ops) {
        const row = {op: op.op};
        try {
          if (op.op === 'add') { const r = session.add(op.key); row.ok = r.ok; row.refused = !!r.refusal; }
          else if (op.op === 'remove') { const r = session.remove(op.instanceId); row.ok = r.ok; }
          else if (op.op === 'undo') row.ok = session.undo().ok;
          else if (op.op === 'moveTo') row.ok = session.moveTo(op.instanceId, op.index).ok;
          else if (op.op === 'tiles') row.tiles = tileView(run);
          else if (op.op === 'confirm') {
            if (closed) { row.skipped = true; } else {
              const r = session.confirm(); row.ok = r.ok; row.refusal = r.refusal;
              if (!r.ok) { session.cancel(); row.cancelled = true; }
              closed = true;
            }
          }
        } catch (error) { row.error = error.message; }
        row.piles = pileView(run);
        results.push(row);
      }
      // A won card straight into the deck: refusal codes for each option, then the claim of the first card option.
      const offerId = `1:deck${seed}:combat`;
      const offer = gameProfileRewardOffers(registries, run, rng, {offerId, source: 'combat'});
      const offerState = stateView(run, rng);
      const refusals = offer.options.map((option, index) => rewardDeckAddRefusal(registries, run, offerId, index, settings));
      const cardIndex = offer.options.findIndex(option => ['combatCards', 'weaponArts'].includes(option.kind));
      let toDeck = null;
      if (cardIndex >= 0) {
        try { toDeck = {index: cardIndex, option: clone(claimGameProfileRewardToDeck(registries, run, offerId, cardIndex, settings)), piles: pileView(run)}; }
        catch (error) { toDeck = {index: cardIndex, error: error.message, code: error.code ?? null}; }
        const again = rewardDeckAddRefusal(registries, run, offerId, cardIndex, settings);
        toDeck.againRefusal = again;
      }
      edits.push({input: {classId, seed, settingsLabel, settings, run: state, ops, offer: clone(offer), offerRun: offerState},
        expected: {results, refusals, toDeck, bounds: clone({count10: deckEditRefusal(10, settings), count9: deckEditRefusal(9, settings), count41: deckEditRefusal(41, settings), count1: deckEditRefusal(1, settings)})}});
    }
    family('deck-edits', 'phase 9', 'The Core 2 deck editor session: Adept mints (a kept copy back first, else edit:N), sideboard cards back by tile, removals (a pristine Adept basic is deleted, other cards kept), copy limits, Undo, Play in deck order moves, Confirm only inside the bounds (else Cancel restores everything), the collection tiles, the bound sentences, and a won card straight into the deck.',
      ['src/ui/models/DeckEditorModel.js openDeckEdit/deckEditorModel', 'src/model/deckRules.js addBasicCard/moveToSideboard/moveFromSideboard/deckEditRefusal',
        'src/model/gameProfileRewards.js rewardDeckAddRefusal/claimGameProfileRewardToDeck'], edits);

    // ── armoury ──
    const handSlots = ['rightHand', 'leftHand'];
    const inspectRows = run => {
      const rows = [];
      const settings = gameModule(run, 'gear');
      for (const slot of registries.equipment.slots) {
        const armorSlot = slot.kinds.includes('armor');
        if (!handSlots.includes(slot.id) && !armorSlot) continue;
        (run.loadout.sets[slot.id] || []).forEach((itemId, setIndex) => {
          if (!itemId) return;
          const piece = armorSlot ? registries.equipment.armour.find(row => row.classId === run.class && row.id === itemId) : registries.equipment.armaments.find(row => row.id === itemId);
          const gear = profile.content.gear.find(item => (item.source?.id || item.id) === itemId && (item.kind === 'armor') === armorSlot && (!item.classes || item.classes.includes(run.class)));
          const itemRef = armorSlot ? `armor/${run.class}/${itemId}` : `armament/${itemId}`;
          const tier = run.itemUpgradeLevels?.[itemRef] || 0;
          const resolved = gear ? resolveGearItem(gear, settings, {rank: (gear.rank ?? 1) + tier}).values : {};
          const values = Object.fromEntries(['ar', 'dr', 'poise', 'hp', 'mp', 'sp', 'ward', 'evasion', 'precision', 'strength', 'dexterity', 'constitution', 'wisdom', 'intelligence']
            .map(key => [key, gear ? resolved[key] || 0 : null]).filter(([, v]) => v !== null));
          const requirements = piece ? equipmentRequirementReceipt(registries, piece, run.attributes, {itemUpgradeLevels: run.itemUpgradeLevels}).requirements.map(row => ({attributeId: row.attributeId, required: row.required, actual: row.actual})) : [];
          rows.push({slotId: slot.id, setIndex, active: setIndex === (run.loadout.active[slot.id] || 0), itemId, itemRef, name: piece?.name ?? itemId, kind: piece?.kind ?? null, tier,
            values, grantedCards: (gear?.grantedCards || []).flatMap(grant => Array(grant.copies).fill(grant.card)), requirements});
        });
      }
      return clone(rows);
    };
    const armoury = [];
    for (const classId of CLASSES) for (const seed of [12, 77]) {
      const {run, rng} = newRun(classId, seed);
      markArmourySeen(run); // the Armoury opened at birth: everything carried is seen
      const carried = new Set([...run.loadout.storage, ...Object.values(run.loadout.sets).flat().filter(Boolean)]);
      const extra = ['boneSceptre', 'kiteShield', 'katana', 'shortbow', 'ashStaff'].filter(id => !carried.has(id)).slice(0, 4);
      run.loadout.storage.push(...extra);
      const lockedArmour = registries.equipment.armour.filter(row => row.classId === classId && row.unlock);
      run.loadout.boughtArmour = [{classId, id: lockedArmour[0].id}];
      run.itemUpgradeLevels[`armament/${extra[0]}`] = 1;
      const state = stateView(run, rng);
      const newBefore = newArmouryItems(run);
      const glow = armouryHasNew(run);
      const otherClassArmour = registries.equipment.armour.find(row => row.classId !== classId && row.unlock === '');
      const notCarried = registries.equipment.armaments.map(row => row.id).find(id => !carried.has(id) && !extra.includes(id));
      const ops = [
        {slotId: 'rightHand', setIndex: 0, itemId: extra[0]},
        {slotId: 'leftHand', setIndex: 1, itemId: extra[1]},
        {slotId: 'leftHand', setIndex: 0, itemId: extra[2]},
        {slotId: 'rightHand', setIndex: 1, itemId: extra[2]},
        {slotId: 'rightHand', setIndex: 0, itemId: null},
        {slotId: 'rightHand', setIndex: 0, itemId: notCarried},
        {slotId: 'rightHand', setIndex: 0, itemId: lockedArmour[0].id},
        {slotId: 'armor', setIndex: 0, itemId: lockedArmour[0].id},
        {slotId: 'armor', setIndex: 0, itemId: lockedArmour[1]?.id ?? lockedArmour[0].id},
        {slotId: 'armor', setIndex: 0, itemId: otherClassArmour.id},
        {slotId: 'armor', setIndex: 0, itemId: 'default'},
        {slotId: 'rightHand', setIndex: 5, itemId: extra[0]},
        {fillStorage: true},
        {slotId: 'rightHand', setIndex: 1, itemId: null},
        {slotId: 'rightHand', setIndex: 0, itemId: extra[3] ?? extra[0]},
      ];
      const results = [];
      for (const op of ops) {
        if (op.fillStorage) {
          for (const id of registries.equipment.armaments.map(row => row.id)) {
            if (run.loadout.storage.length >= registries.balance.equipment.storageSlots) break;
            if (!run.loadout.storage.includes(id) && !Object.values(run.loadout.sets).flat().includes(id)) run.loadout.storage.push(id);
          }
          results.push({op: 'fillStorage', storage: clone(run.loadout.storage)});
          continue;
        }
        const owned = ownership(registries, {meta: {}, loadout: run.loadout});
        const ok = equipPiece(registries, run.loadout, op.slotId, op.setIndex, op.itemId, owned,
          {inCombat: false, attributes: run.attributes, itemUpgradeLevels: run.itemUpgradeLevels, classId: run.class});
        if (ok) stampDeck(registries, run);
        const v = runView(run, rng);
        results.push({op: 'equip', ...op, ok, loadout: v.loadout, deck: v.deck, pools: [v.hp, v.maxHp, v.mana, v.maxMana, v.stamina, v.maxStamina], ratings: v.ratings,
          equipmentPoolBonuses: v.equipmentPoolBonuses});
      }
      const candidates = Object.fromEntries(['rightHand', 'leftHand', 'armor'].map(slotId => {
        const slot = registries.equipment.slots.find(row => row.id === slotId);
        const owned = ownership(registries, {meta: {}, loadout: run.loadout});
        const authored = slot.kinds.includes('armor') ? registries.equipment.armour.filter(row => row.classId === run.class && fitsSlot(slot, row)) : registries.equipment.armaments.filter(row => fitsSlot(slot, row));
        return [slotId, authored.filter(piece => owned.has(piece)).map(piece => piece.id)];
      }));
      const newAfter = newArmouryItems(run);
      const inspect = inspectRows(run);
      markArmourySeen(run);
      armoury.push({input: {classId, seed, run: state, ops}, expected: clone({newBefore, glow, results, candidates, newAfter, inspect, seenAfter: run.armorySeen, newAfterLook: newArmouryItems(run),
        locked: {unresolved: editingLocked(run, {result: undefined}), resolved: editingLocked(run, {result: 'victory'}), none: editingLocked(run, null)}})});
    }
    family('armoury', 'phase 9', 'The Core 2 Armoury: Replace (owned pieces only; the replaced armament to storage; a moved armament leaves its old cell), Unequip, wrong-slot, unowned, bad-location and full-storage refusals, the deck re-stamped (pools, gear grants) after each change, the picker candidates, the new-item glow, the combat lock and the Inspect rows.',
      ['src/model/loadout.js equipPiece/ownership/stampDeck/fitsSlot/equipmentRequirementReceipt', 'src/model/armouryRules.js editingLocked/newArmouryItems/markArmourySeen/armouryHasNew',
        'engine/kernel/gameConfig.js resolveGearItem'], armoury);

    // ── skill progression ──
    const skills = [];
    for (const kind of ['weapon', 'focus', 'class']) for (let level = 0; level <= 21; level++) skills.push({input: {kind: 'xpToNext', track: kind, level}, expected: {xp: skillXpToNext(registries, kind, level)}});
    for (const classId of CLASSES) for (const gains of [[0.5], [0], [5], [99, 1], [100, 500, 2000], [100000]]) {
      const {run} = newRun(classId, 21);
      const awards = [];
      for (const skillId of ['item:blade', 'item:shield', 'item:magic-focus', `class:${classId}`]) for (const gain of gains) awards.push(clone(awardSkillXp(registries, run, skillId, gain, {queueRewards: false})));
      skills.push({input: {kind: 'award', classId, gains}, expected: clone({awards, skills: run.skills})});
    }
    const MULTIPLIERS = [{weapon: 1, cls: 1}, {weapon: 0.5, cls: 2}, {weapon: 0, cls: 0}, {weapon: 12, cls: -3}, {weapon: null, cls: null}, {weapon: 1.5, cls: 1.25}];
    let settleCase = 0;
    for (const classId of CLASSES) for (const multipliers of MULTIPLIERS) for (const [victory, pool] of [[true, 'normal'], [true, 'boss'], [false, 'elite']]) {
      const {run} = newRun(classId, 22);
      if (multipliers.weapon === null) delete run.gameProfile.modules.progression.weaponXpMultiplier; else run.gameProfile.modules.progression.weaponXpMultiplier = multipliers.weapon;
      if (multipliers.cls === null) delete run.gameProfile.modules.progression.classXpMultiplier; else run.gameProfile.modules.progression.classXpMultiplier = multipliers.cls;
      const pre = {'item:blade': {xp: 80 + (settleCase % 3) * 10, level: settleCase % 2, pendingDrafts: 0}, [`class:${classId}`]: {xp: 390, level: 0, pendingDrafts: 0}};
      run.skills = structuredClone(pre);
      const receipt = [['item:blade', 37 + settleCase], ['item:magic-focus', 12], ['item:shield', 3]];
      const usedArmaments = ['greatsword', 'boneSceptre', 'dagger', 'kiteShield'];
      const awards = settleConfiguredSkillProgression(registries, run, {}, {receipt: Object.fromEntries(receipt), victory, pool, classMultiplier: 1});
      const all = [awards.classAward, ...awards.trackAwards];
      skills.push({input: {kind: 'settle', classId, multipliers, victory, pool, skills: pre, receipt, usedArmaments},
        expected: clone({weaponMultiplier: configuredSkillXpMultiplier(run), classMultiplier: configuredSkillXpMultiplier(run, 'class'), classAward: awards.classAward, trackAwards: awards.trackAwards,
          notices: skillLevelNotices(registries, all, {run, beforeState: {}, usedArmaments}), gains: combatXpGains({receipt: Object.fromEntries(receipt), awards: all, levelGained: 37, levelDiscarded: 3}),
          victoryGains: combatXpGains({awards: all, levelGained: 12}), skills: run.skills})});
      settleCase++;
    }
    // Character levels off (an old save's profile): no character XP is banked, and earned points stay claimable at a rest.
    for (const classId of CLASSES) for (const [characterLevels, unspent] of [[false, 0], [false, 1], [true, 0], [true, 1]]) {
      const {run, rng} = newRun(classId, 23);
      run.gameProfile.modules.progression.characterLevels = characterLevels;
      run.level.unspentPoints = unspent;
      run.mapGraph = {nodes: {n3_0: {id: 'n3_0', floor: 3, type: 'shrine', actualType: 'rest', next: []}}};
      run.mapNodeId = 'n3_0'; run.floor = 3;
      const state = stateView(run, rng);
      beginGameProfileRest(run);
      let levelUp;
      try { commitGameProfileRest(registries, run, 'levelUp', {attributeId: 'strength'}); levelUp = {ok: true, attributes: clone(run.attributes), level: clone(run.level)}; }
      catch (error) { levelUp = {error: error.message.split(' (level')[0]}; }
      skills.push({input: {kind: 'characterLevels', classId, characterLevels, unspent, run: state}, expected: {characterLevels: configuredCharacterLevels(run), levelXpOf100: configuredCharacterLevels(run) ? 100 : 0, levelUp}});
    }
    family('skill-progression', 'phase 10', 'Core 2 class and used-weapon XP: each track kind\'s curve, awards climbing to the cap (Spellcraft opening at level 1), the fight settlement under class/weapon XP multipliers (clamped 0–10, absent = 1), the level notices, the spoils\' XP lines, and character levels switched off with grandfathered rest points.',
      ['src/model/skills.js xpToNext/awardSkillXp', 'src/engine/skillXp.js settleConfiguredSkillProgression', 'src/model/gameProfileProgression.js configuredSkillXpMultiplier/skillLevelNotices/configuredCharacterLevels',
        'src/model/rewardprogress.js combatXpGains', 'src/model/gameProfileRest.js commitGameProfileRest'], skills);
  }
}
