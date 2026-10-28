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
//   scripted-runs  whole runs, floor by floor, under a fixed policy (fights played card by card)
//   scripted-runs-auto  the same orchestration with every fight built and then settled as won (all three acts)
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
    // main.js onCombatEnd, configured branch (reactions are phase 7; class and weapon XP tracks are phase 10).
    runCombatEnd(run, combat);
    const xp = combatXpReceipt(registries, {victory: result === 'victory', pool: enc.pool, enemies: combat.enemies, characterMultiplier: 1});
    const levelAward = awardLevelXp(registries, run, xp.total, {pointsPerLevel: gameModule(run, 'progression').attributePointsPerLevel, grantStats: true});
    const step = {kind: 'fight', nodeId: run.mapNodeId, encounterId: enc.id, pool: enc.pool, hpMult: fight.hpMult, start: fight.start, result: result ?? 'stalled', turns: fight.turns,
      enemies: combat.enemies.map(e => ({enemyId: e.enemyId, maxHp: e.maxHp, hp: e.hp, level: e.level ?? null})), xp: clone(xp), levelUps: levelAward.levelUps};
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
}
