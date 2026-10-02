// Exports original-game ACTIVE-ROOM run saves (sote_run_v1 bytes) written by the
// repository's own JavaScript save manager, plus what the original does next.
// Usage: node UnityTests/WebSaveImport/export-room-reference.mjs [original-checkout]
// The checkout defaults to this repository; src/ must be clean so the receipt
// names exactly the code that wrote the bytes. Writes only this test folder.
//
// Each room is reached the way src/main.js reaches it: the same roll functions,
// in the same order, on the same RNG streams, persisted through createSaveManager.
// Two steps are not played out and are named here instead: the route to a deep
// room is walked without playing the rooms before it (the run arrives with its
// starting deck and HP), and a reward room is entered as if the fight was won
// with no HP lost. A few lines of screen code (reward.js, shop.js) apply a take
// or a purchase; those lines are mirrored below with their source location and
// the screen files are hashed into the receipt so a change to them is visible.
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL, fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';

const out = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(process.argv[2] ?? path.join(out, '..', '..'));
const sha = execFileSync('git', ['rev-parse', 'HEAD'], {cwd: root, encoding: 'utf8'}).trim();
if (execFileSync('git', ['status', '--porcelain', '--', 'src'], {cwd: root, encoding: 'utf8'}).trim()) throw Error('Expected a clean original src/ tree.');
const load = p => import(pathToFileURL(path.join(root, p)).href);
const {contentBundle} = await load('src/content/index.js');
const {createRegistries} = await load('src/model/registries.js');
const {createRunState, createIdGen, createDeck} = await load('src/model/state.js');
const {createRng, seedToString} = await load('src/engine/rng.js');
const {createSaveManager, createMemoryStorage} = await load('src/engine/save.js');
const {isPoolDeckRun, dealtAttackSlotCount} = await load('src/model/cardRemoval.js');
const {buildActMap} = await load('src/engine/actmap.js');
const {rollEncounter, rollRuneReward, rollCardRewardIds, rollFlaskDrop, rollRelicReward, rollArmamentDrop, buildShopStock} = await load('src/engine/encounters.js');
const {createCombat} = await load('src/engine/combat.js');
const {commitCombatSnapshot} = await load('src/engine/combatSnapshot.js');
const {grantSmithingReward} = await load('src/model/smithing.js');
const {smithServicesAt} = await load('src/model/cardExtraction.js');
const {runMods, addToStorage, carriedIds, resolveSwapCostRule, isEquipmentComposedInstance, stampDeck} = await load('src/model/loadout.js');
const {activeMods, endlessActInfo, ENDLESS_HP_PER_LOOP, ENDLESS_STR_PER_LOOP} = await load('src/content/customMods.js');
const {syncFlaskGrowth} = await load('src/model/flaskgrowth.js');
const {rewardPlan, resolveContinue} = await load('src/model/rewardplan.js');
const {flaskSlotCap} = await load('src/model/gracerefill.js');
const registries = createRegistries(contentBundle);
const clone = v => structuredClone(v);

// main.js beginClimb → startClimb for a fresh run, field for field.
function climb(classId, seed, custom = {ascension: 0, mods: {}, deckMode: 'standard'}, draftPicks = 0) {
  const storage = createMemoryStorage(); const saves = createSaveManager(storage);
  const run = createRunState({seed, classId, registries, idGen: createIdGen('web'), profileMeta: saves.loadMeta()});
  run.seedString = seedToString(run.seed); run.customization = {name: 'Forsaken', glyph: '⚔', tint: 'gold'};
  run.custom = clone(custom); run.stats = {fightsWon: 0, damageDealt: 0, damageTaken: 0};
  run.path = []; run.seenEvents = []; run.lastEncounters = [];
  const rng = createRng(seed);
  const mods = activeMods(run.custom); const deckMode = run.custom.deckMode || 'standard';
  if (deckMode === 'sealed') {
    const pool = registries.classes.get(classId).cardPool.slice(); const ids = ['strike', 'strike', 'strike', 'strike', 'defend', 'defend', 'defend'];
    for (let i = 0; i < 3 && pool.length; i++) { const id = rng.pick('misc', pool); pool.splice(pool.indexOf(id), 1); ids.push(id); }
    run.deck = createDeck(ids, createIdGen('rc'));
  } else if (deckMode === 'draft') run.deck = createDeck(['strike', 'strike', 'strike', 'strike', 'defend', 'defend', 'defend'], createIdGen('rc'));
  // main.js newRun: the dealt deck's birth attack quota is the slots it holds (the Sealed/Draft reload fix).
  if (isPoolDeckRun(run)) run.equipmentAttackSlotCount = dealtAttackSlotCount(run.deck);
  if (mods.cursedStart) run.deck.push(...createDeck(['guilt'], createIdGen('cx')));
  if (mods.hoarder) run.cinders += registries.balance.customMods.hoarderCinders;
  if (deckMode === 'draft') {
    // ui/screens/draft.js: three rounds, offer three from the pool, the player picks the first offer.
    const pool = registries.classes.get(classId).cardPool.slice(); const idGen = createIdGen('df');
    for (let round = 0; round < (draftPicks || 3); round++) {
      const local = pool.slice(); const offer = [];
      for (let i = 0; i < 3 && local.length; i++) { const id = rng.pick('cardRewards', local); local.splice(local.indexOf(id), 1); offer.push(id); }
      run.deck.push({instanceId: idGen(), cardId: offer[0], upgraded: false}); pool.splice(pool.indexOf(offer[0]), 1);
    }
  }
  // main.js startClimb: a dealt deck (picks included) gets its equipment faces (a subset stamp).
  if (isPoolDeckRun(run)) stampDeck(registries, run, run.deck, {adoptEquipmentBonuses: false, reconcileEquipmentPools: false});
  const g = {run, rng, saves, storage};
  g.run.mapGraph = buildActMap(registries, rng, contentAct(g), runShape(g), {history: run.history});
  return g;
}
const endlessOn = g => !!(g.run.custom && activeMods(g.run.custom).endless);
const contentAct = g => endlessOn(g) ? endlessActInfo(g.run.actNumber).contentAct : g.run.actNumber;
const runShape = g => (g.run.custom && g.run.custom.mapShape) || null;
const persist = g => g.saves.saveRun(g.run, g.rng, 1);
const bytes = g => g.storage.getItem('sote_run_v1');
// Shortest legal route to the first node (lowest floor) matching `want`.
function routeTo(g, want) {
  const nodes = g.run.mapGraph.nodes; const parent = new Map(); const queue = [...g.run.mapGraph.startIds];
  for (const id of queue) parent.set(id, null);
  const hits = [];
  for (let i = 0; i < queue.length; i++) { const id = queue[i]; if (want(nodes[id])) hits.push(id); for (const n of nodes[id].next) if (!parent.has(n)) { parent.set(n, id); queue.push(n); } }
  if (!hits.length) throw Error('No node matches on this map.');
  hits.sort((a, b) => nodes[a].floor - nodes[b].floor);
  const route = []; for (let id = hits[0]; id; id = parent.get(id)) route.unshift(id);
  return route;
}
// main.js enterNode: route bookkeeping for each step walked (rooms before the last not played).
function walk(g, route) { for (const id of route) { g.run.mapNodeId = id; g.run.path.push(id); g.run.floor = g.run.mapGraph.nodes[id].floor; } return g.run.mapGraph.nodes[route.at(-1)]; }
function combatMods(g, pool) {
  const mods = activeMods(g.run.custom); let hpMult = 1; const enemyStatuses = []; const playerStatuses = []; const cm = registries.balance.customMods;
  if ((pool === 'elite' || pool === 'boss') && mods.toughElites) hpMult *= cm.toughElitesHpMult;
  if (pool === 'boss' && mods.bigBosses) hpMult *= cm.bigBossesHpMult;
  if (mods.deadlyEnemies) enemyStatuses.push({status: 'strength', stacks: 1});
  if (mods.glassCannon) playerStatuses.push({status: 'glassCannon', stacks: 1});
  if (mods.endless) { const {loop} = endlessActInfo(g.run.actNumber); if (loop > 0) { hpMult *= 1 + ENDLESS_HP_PER_LOOP * loop; enemyStatuses.push({status: 'strength', stacks: ENDLESS_STR_PER_LOOP * loop}); } }
  return {hpMult, enemyStatuses, playerStatuses};
}
// main.js startFight + enterCombat (the entry receipt is persisted before the fight is built).
function startFight(g, pool) {
  if (pool === 'normal' && activeMods(g.run.custom).allElite) pool = 'elite';
  const encounterId = rollEncounter(registries, g.rng, {pool, act: contentAct(g), exclude: g.run.lastEncounters});
  if (pool === 'normal') { g.run.lastEncounters.push(encounterId); if (g.run.lastEncounters.length > 2) g.run.lastEncounters.shift(); }
  g.run.combatEntered = {nodeId: g.run.mapNodeId, encounterId}; persist(g);
  return encounterId;
}
function buildCombat(g, encounterId) {
  const run = g.run; const enc = registries.encounters.get(encounterId); const cm = combatMods(g, enc.pool);
  return createCombat({registries, rng: g.rng, player: {classId: run.class, attributes: run.attributes, maxHp: run.maxHp, hp: run.hp, maxMana: run.maxMana, mana: run.mana, maxStamina: run.maxStamina, stamina: run.stamina, energyMax: run.energyMax, drawPerTurn: run.drawPerTurn, damageBySchoolAdd: run.damageBySchoolAdd, equipmentProfileRuleSnapshot: run.equipmentProfileRuleSnapshot, equipmentAttackSlotCount: run.equipmentAttackSlotCount, equipmentPoolDeficits: run.equipmentPoolDeficits, itemUpgradeLevels: run.itemUpgradeLevels, itemMounts: run.itemMounts, armamentLevels: run.armamentLevels, deck: run.deck, relicIds: run.relics, flasks: run.flasks, flaskCharges: run.flaskCharges, loadout: run.loadout},
    enemyIds: enc.enemies, hpMult: cm.hpMult, enemyStatuses: cm.enemyStatuses, swapCostRule: resolveSwapCostRule(registries, g.saves.loadMeta()), playerStatuses: [...cm.playerStatuses, ...runMods(registries, run.loadout, run.class).startStatuses]});
}
const opening = c => ({turn: c.turn, phase: c.phase, energy: c.player.energy, hand: c.piles.hand.map(x => x.instanceId), draw: c.piles.draw.map(x => x.instanceId), discard: c.piles.discard.map(x => x.instanceId),
  enemies: c.enemies.map(e => ({enemyId: e.enemyId, hp: e.hp, maxHp: e.maxHp, intent: e.intent && (e.intent.moveId ?? e.intent.id ?? null)})), counters: c.rng.getCounters()});
const rollDrop = (g, source) => rollArmamentDrop(registries, g.rng, {source, found: g.saves.loadMeta().found || [], carried: carriedIds(g.run.loadout)});
// main.js onCombatEnd(victory) after an unplayed fight, then beginPendingReward + persist.
function winFight(g, encounterId) {
  const run = g.run; const enc = registries.encounters.get(encounterId);
  run.stats.fightsWon += 1; run.combatEntered = null;
  const smithingStoneReceipt = grantSmithingReward(registries, run, enc.pool, `combat:${run.actNumber}:${run.floor}:${run.mapNodeId || 'unknown'}:${enc.pool}`);
  const chaos = !!activeMods(run.custom).chaosRewards; let rewards;
  if (enc.pool === 'boss') {
    run.bossesBeaten = run.bossesBeaten || []; for (const id of enc.enemies) if (!run.bossesBeaten.includes(id)) run.bossesBeaten.push(id);
    const bossArmament = rollDrop(g, 'boss'); const drops = registries.balance.equipment.drops || {};
    rewards = {title: `${registries.enemies.get(enc.enemies[0]).name.toUpperCase()} FALLS`, cinders: rollRuneReward(registries, g.rng, 'boss', run.relics) + (bossArmament ? 0 : drops.consolationCinders || 0),
      cardIds: rollCardRewardIds(registries, g.rng, {classId: run.class, pool: 'boss', relicIds: run.relics, flatRarity: chaos}), relicId: rollRelicReward(registries, g.rng, run.relics, {rarities: ['boss']}), armamentId: bossArmament, smithingStoneReceipt};
  } else rewards = {title: enc.pool === 'elite' ? 'ELITE VANQUISHED' : 'VICTORY', cinders: rollRuneReward(registries, g.rng, enc.pool, run.relics),
    cardIds: rollCardRewardIds(registries, g.rng, {classId: run.class, pool: enc.pool, relicIds: run.relics, flatRarity: chaos}), flaskId: rollFlaskDrop(registries, g.rng, run),
    relicId: enc.pool === 'elite' ? rollRelicReward(registries, g.rng, run.relics) : null, armamentId: rollDrop(g, enc.pool), smithingStoneReceipt};
  run.pendingReward = {schemaVersion: 1, source: enc.pool, after: enc.pool === 'boss' ? 'advanceAct' : 'map', rewards: clone(rewards), states: rewards.smithingStoneReceipt?.amount > 0 ? {smithingStone: 'taken'} : {}, chosenCardId: null};
  persist(g);
}
// ui/screens/reward.js mountRewards: the plan, the per-kind apply table and persistProgress.
function rewardScreen(g) {
  const run = g.run; const checkpoint = run.pendingReward; const rewards = checkpoint.rewards;
  const plan = rewardPlan(rewards, {flaskSlotsFree: Math.max(0, flaskSlotCap(registries.balance) - run.flasks.length), armamentSlotsFree: Math.max(0, (registries.balance.equipment.storageSlots || 8) - ((run.loadout || {}).storage || []).length)});
  const states = {...(checkpoint.states || {}), ...(rewards.smithingStoneReceipt?.amount > 0 ? {smithingStone: 'taken'} : {})}; let chosenCardId = checkpoint.chosenCardId || null;
  const save = () => { checkpoint.states = {...states}; checkpoint.chosenCardId = chosenCardId; persist(g); };
  const apply = {
    cinders(row) { run.cinders += row.amount; return true; }, // reward.js:120
    card(row) { run.deck.push({instanceId: `r${run.deck.length}_${row.cardId}`, cardId: row.cardId, upgraded: false}); chosenCardId = row.cardId; return true; }, // reward.js:128
    flask(row) { run.flasks.push({flaskId: row.flaskId}); return true; }, // reward.js:134
    relic(row) { run.relics.push(row.relicId); syncFlaskGrowth(registries, run); return true; }, // reward.js:139
    armament(row) { return addToStorage(run.loadout, row.armamentId, registries.balance.equipment.storageSlots || 8); }, // main.js collectArmament
  };
  const take = (kind, cardId) => { const row = plan.rows.find(r => r.kind === kind); if (!row || states[kind] || row.blockedBy) return false; if (!apply[kind](cardId ? {...row, cardId} : row)) return false; states[kind] = 'taken'; save(); return true; };
  const grantCinders = () => take('cinders'); // reward.js grantCinders on arrival
  const finish = mode => { const {take: rows} = resolveContinue(plan, states, mode, n => g.rng.int('cardRewards', 0, n - 1)); for (const row of rows) if (apply[row.kind](row)) { states[row.kind] = 'taken'; save(); } };
  return {plan, take, grantCinders, finish};
}
// main.js mountPendingReward onDone → advanceAct or map.
function leaveRewards(g) {
  const after = g.run.pendingReward.after; delete g.run.pendingReward;
  if (after === 'advanceAct') {
    const run = g.run; run.actNumber += 1; run.floor = 0; run.mapNodeId = null; run.path = []; run.lastEncounters = [];
    if (activeMods(run.custom).lessHealing) run.hp = Math.min(run.maxHp, run.hp + Math.floor((run.maxHp - run.hp) * registries.balance.customMods.lessHealingMult)); else run.hp = run.maxHp;
    run.mapGraph = buildActMap(registries, g.rng, contentAct(g), runShape(g), {history: run.history});
  }
  persist(g);
}
// main.js enterNode 'merchant'.
function enterMerchant(g) {
  const stock = buildShopStock(registries, g.rng, g.run); const mods = activeMods(g.run.custom); let pm = 1;
  if (mods.expensiveShops) pm *= registries.balance.customMods.expensiveShopsMult; if (mods.hoarder) pm *= registries.balance.customMods.hoarderShopMult;
  if (pm !== 1) { for (const kind of ['cards', 'relics', 'flasks']) for (const item of stock[kind]) item.cost = Math.ceil(item.cost * pm); stock.removeCost = Math.ceil(stock.removeCost * pm); }
  stock.smith = smithServicesAt(registries, 'merchant', g.rng); g.run.shopStock = stock; persist(g);
}
// ui/screens/shop.js purchase handlers (lines noted), each followed by onChanged → persist.
const shop = {
  card(g, i) { const run = g.run, stock = run.shopStock, item = stock.cards[i]; if (run.cinders < item.cost) throw Error('poor'); run.cinders -= item.cost; run.deck.push({instanceId: `s${run.deck.length}_${item.id}`, cardId: item.id, upgraded: false}); stock.cards.splice(i, 1); persist(g); }, // shop.js:171
  relic(g, i) { const run = g.run, stock = run.shopStock, item = stock.relics[i]; if (run.cinders < item.cost) throw Error('poor'); run.cinders -= item.cost; run.relics.push(item.id); syncFlaskGrowth(registries, run); stock.relics.splice(i, 1); persist(g); }, // shop.js:189
  flask(g, i) { const run = g.run, stock = run.shopStock, item = stock.flasks[i]; if (run.cinders < item.cost || run.flasks.length >= flaskSlotCap(registries.balance)) throw Error('refused'); run.cinders -= item.cost; run.flasks.push({flaskId: item.id}); stock.flasks.splice(i, 1); persist(g); }, // shop.js:203
  remove(g, instanceId) { const run = g.run, stock = run.shopStock; const idx = run.deck.findIndex(c => c.instanceId === instanceId); if (idx < 0 || isEquipmentComposedInstance(run.deck[idx]) || run.cinders < stock.removeCost) throw Error('refused'); run.cinders -= stock.removeCost; run.deck.splice(idx, 1); run.removesPurchased = (run.removesPurchased || 0) + 1; stock.removeCost = registries.balance.shop.removeBase + registries.balance.shop.removeStep * run.removesPurchased; persist(g); }, // shop.js:237
};
const view = run => ({cinders: run.cinders, hp: run.hp, maxHp: run.maxHp, actNumber: run.actNumber, floor: run.floor, mapNodeId: run.mapNodeId, deck: run.deck.map(c => c.cardId), relics: run.relics, flasks: run.flasks.map(f => f.flaskId), storage: run.loadout.storage, flaskCharges: run.flaskCharges, smithingStones: run.smithingStones ?? 0, removesPurchased: run.removesPurchased ?? 0, flaskChancePct: run.flaskChancePct ?? null, streamCounters: run.streamCounters});

const fixtures = {};
const keep = (name, g, note, expect = {}) => { fixtures[name] = {note, save: bytes(g), expect: clone(expect)}; };

// 1. Combat: the entry receipt every fight persists, and an exact committed-turn snapshot.
for (const classId of ['reaver', 'starseer', 'rogue', 'herald']) {
  const g = climb(classId, 1); const node = walk(g, [g.run.mapGraph.startIds[0]]);
  if (node.type !== 'monster') throw Error('Expected a monster start node.');
  const encounterId = startFight(g, 'normal'); const entry = bytes(g);
  const combat = buildCombat(g, encounterId);
  fixtures[classId + '-combat-entry'] = {note: 'Fight entry receipt persisted by enterCombat; the original rebuilds this fight on resume.', save: entry, expect: {encounterId, opening: opening(combat)}};
  commitCombatSnapshot({run: g.run, combat, nodeId: g.run.mapNodeId, encounterId}); persist(g);
  keep(classId + '-combat-snapshot', g, 'Exact committed-turn combat snapshot (Save Game during a fight).');
}

// 2. Rewards: normal (cinders granted on arrival, then a card taken), elite and boss.
{
  const g = climb('reaver', 1); walk(g, [g.run.mapGraph.startIds[0]]); const enc = startFight(g, 'normal'); buildCombat(g, enc); winFight(g, enc);
  keep('reward-normal-fresh', g, 'Reward checkpoint exactly as beginPendingReward persists it, before the screen grants cinders.');
  const screen = rewardScreen(g); screen.grantCinders();
  keep('reward-normal', g, 'Reward screen after cinders were granted on arrival.');
  const pick = g.run.pendingReward.rewards.cardIds[1]; screen.take('card', pick); const taken = view(g.run);
  keep('reward-normal-card', g, 'Second card offer taken; flask/armament rows still pending.', {afterCardTake: taken, pick});
  const manual = climbClone(g); rewardScreen(manual).finish('manual'); leaveRewards(manual);
  const auto = climbClone(g); rewardScreen(auto).finish('auto'); leaveRewards(auto);
  fixtures['reward-normal-card'].expect.manualContinue = view(manual.run); fixtures['reward-normal-card'].expect.autoContinue = view(auto.run);
  const fresh = climbFrom(fixtures['reward-normal-fresh'].save); const rs = rewardScreen(fresh); rs.grantCinders(); rs.take('card', fresh.run.pendingReward.rewards.cardIds[1]);
  if (JSON.stringify(view(fresh.run)) !== JSON.stringify(taken)) throw Error('Reward replay differs.');
  fixtures['reward-normal-fresh'].expect.afterCinders = view(climbAfter(fixtures['reward-normal-fresh'].save, s => s.grantCinders()).run);
}
function climbClone(g) { return climbFrom(bytes(g)); }
function climbFrom(save) { const storage = createMemoryStorage(); const saves = createSaveManager(storage); storage.setItem('sote_run_v1', save); const run = saves.loadRun(registries, 1); return {run, rng: createRng(run.seed, run.streamCounters), saves, storage}; }
function climbAfter(save, act) { const g = climbFrom(save); act(rewardScreen(g)); return g; }
for (const [name, type, seed] of [['reward-elite', 'elite', 3], ['reward-boss', 'boss', 1]]) {
  const g = climb('starseer', seed); g.run.cinders = 0; walk(g, routeTo(g, n => n.type === type));
  const enc = startFight(g, type); buildCombat(g, enc); winFight(g, enc); rewardScreen(g).grantCinders();
  const expect = {offer: clone(g.run.pendingReward.rewards)};
  const manual = climbClone(g); rewardScreen(manual).finish('manual'); leaveRewards(manual); expect.manualContinue = view(manual.run);
  if (type === 'boss') expect.nextMap = clone(manual.run.mapGraph);
  const auto = climbClone(g); rewardScreen(auto).finish('auto'); leaveRewards(auto); expect.autoContinue = view(auto.run);
  for (const kind of ['relic', 'armament', 'flask']) { const t = climbClone(g); if (rewardScreen(t).take(kind)) expect[kind + 'Take'] = view(t.run); }
  keep(name, g, `${type} reward checkpoint after cinders were granted.`, expect);
}

// 3. Merchant: fresh stock, a partly bought stock, and a custom-priced stock.
{
  const g = climb('rogue', 2); walk(g, routeTo(g, n => n.type === 'merchant')); g.run.cinders = 600; enterMerchant(g);
  const expect = {stock: clone(g.run.shopStock)};
  for (const [kind, i] of [['card', 0], ['card', 2], ['relic', 1], ['flask', 0]]) { const t = climbClone(g); if ((t.run.shopStock[kind + 's'] || [])[i]) { shop[kind](t, i); expect[kind + i] = {...view(t.run), stock: clone(t.run.shopStock)}; } }
  { const t = climbClone(g); const card = t.run.deck.find(c => !isEquipmentComposedInstance(c)); shop.remove(t, card.instanceId); expect.remove = {...view(t.run), instanceId: card.instanceId, stock: clone(t.run.shopStock)}; }
  keep('merchant-fresh', g, 'Merchant stock as enterNode persists it (600 cinders granted for the fixture).', expect);
  shop.card(g, 1); shop.relic(g, 0); const removed = g.run.deck.find(c => !isEquipmentComposedInstance(c)).instanceId; shop.remove(g, removed);
  const partial = {stock: clone(g.run.shopStock), state: view(g.run)};
  for (const [kind, i] of [['card', 0], ['flask', 1]]) { const t = climbClone(g); if ((t.run.shopStock[kind + 's'] || [])[i]) { shop[kind](t, i); partial[kind + i] = view(t.run); } }
  { const t = climbClone(g); const card = t.run.deck.find(c => !isEquipmentComposedInstance(c)); shop.remove(t, card.instanceId); partial.remove = {...view(t.run), instanceId: card.instanceId, removeCost: t.run.shopStock.removeCost}; }
  keep('merchant-partial', g, 'Merchant after buying a card and a relic and removing a card (sold rows leave the stock).', partial);
}
{
  const g = climb('herald', 4, {ascension: 6, mods: {hoarder: true}, deckMode: 'standard'}); walk(g, routeTo(g, n => n.type === 'merchant')); g.run.cinders += 600; enterMerchant(g);
  const t = climbClone(g); shop.card(t, 0);
  keep('merchant-custom', g, 'Ascension 6 (expensive shops) plus Hoarder: prices carry both multipliers (600 cinders added for the fixture).', {stock: clone(g.run.shopStock), card0: view(t.run)});
}

// 4. Event: enterNode persists the seen event before the event screen; a reload resumes on the map.
{
  const g = climb('reaver', 1); const route = routeTo(g, n => n.type === 'event' && n.resolved && n.resolved.kind === 'event'); const node = walk(g, route);
  g.run.seenEvents.push(node.resolved.eventId); persist(g);
  keep('event-entered', g, 'Event room entered: the original reload shows the map from this node.', {eventId: node.resolved.eventId, next: node.next});
}

// 5. Modes: sealed, draft, ascension + chaos rules and Endless (act 4, second loop).
for (const [name, classId, custom] of [
  ['mode-sealed', 'starseer', {ascension: 0, mods: {}, deckMode: 'sealed'}],
  ['mode-draft', 'rogue', {ascension: 0, mods: {}, deckMode: 'draft'}],
  ['mode-chaos', 'reaver', {ascension: 4, mods: {allElite: true, glassCannon: true, chaosRewards: true, hoarder: true}, deckMode: 'standard'}],
  ['mode-endless', 'herald', {ascension: 0, mods: {endless: true}, deckMode: 'standard'}],
]) {
  const g = climb(classId, 5, custom);
  if (name === 'mode-endless') for (let i = 0; i < 3; i++) { g.run.pendingReward = {after: 'advanceAct'}; leaveRewards(g); }
  persist(g); const save = bytes(g);
  const start = g.run.mapGraph.startIds.find(id => g.run.mapGraph.nodes[id].type === 'monster'); walk(g, [start]);
  const encounterId = startFight(g, 'normal'); const combat = buildCombat(g, encounterId);
  fixtures[name] = {note: `${JSON.stringify(custom)} map checkpoint${name === 'mode-endless' ? ' at act 4' : ''}.`, save, expect: {start, encounterId, lastEncounters: clone(g.run.lastEncounters), opening: opening(combat), hpMult: combatMods(g, registries.encounters.get(encounterId).pool).hpMult, deck: JSON.parse(save).deck.map(c => c.cardId), cinders: JSON.parse(save).cinders}};
}

// What the original's own load door says about each save (src/engine/save.js loadRun).
for (const fixture of Object.values(fixtures)) {
  const storage = createMemoryStorage(); const saves = createSaveManager(storage); storage.setItem('sote_run_v1', fixture.save);
  saves.loadRun(registries, 1); const status = saves.runStatus(); fixture.originalReload = {state: status.state, reason: status.reason ?? null};
}
const text = JSON.stringify({sourceCommit: sha, fixtures}, null, 1) + '\n';
fs.writeFileSync(path.join(out, 'room-reference.json'), text);
const hash = p => createHash('sha256').update(fs.readFileSync(path.join(root, p))).digest('hex');
const sources = Object.fromEntries(['src/engine/save.js', 'src/model/state.js', 'src/engine/encounters.js', 'src/engine/combat.js', 'src/engine/combatSnapshot.js', 'src/engine/actmap.js', 'src/model/rewardplan.js', 'src/main.js', 'src/ui/screens/reward.js', 'src/ui/screens/shop.js', 'src/ui/screens/draft.js', 'src/model/cardRemoval.js', 'src/content/index.js'].map(p => [p, hash(p)]));
const receipt = {sourceCommit: sha, sources, outputSha256: createHash('sha256').update(text).digest('hex'), fixtures: Object.fromEntries(Object.entries(fixtures).map(([k, v]) => [k, {note: v.note, bytes: v.save.length, originalReload: v.originalReload.state}]))};
fs.writeFileSync(path.join(out, 'room-reference.receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
console.log(`room reference: ${Object.keys(fixtures).length} fixtures, ${text.length} bytes`);
