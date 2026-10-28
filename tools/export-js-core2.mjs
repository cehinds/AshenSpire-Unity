#!/usr/bin/env node
// Export the JS game's fully resolved Core 2 data (profile `ashen.core@2.0.0`, SPEC.md §0.1)
// for the Unity port: GameContent/Unity/Core2/** plus its byte-identical Resources mirror
// (Unity/Assets/AshenSpire/Resources/Core2/**, with stable .meta files).
//
//   node tools/export-js-core2.mjs <js-checkout>            write the export
//   node tools/export-js-core2.mjs <js-checkout> --check    exit 1 if the committed export differs
//   (--allow-unpinned: write from a commit other than the SPEC §0.2 pin, for a pin-bump PR only)
//
// Read-only against the JS checkout. It runs the JS game's own resolver, the exact path a
// Core 2 run takes in src/main.js:
//   resolveGameProfile('default')                                  -> the profile snapshot
//   createRegistries(applyGameProfileToContent(contentBundle, p))  -> the runtime registries
// and writes:
//   profile.json       the snapshot without its content tables (modules, schemas, lock, hash)
//   mechanics.json     FixCore values (content/source/fixCoreMechanics.json), stance defaults,
//                      core2ActionRulesVersion
//   pack/<key>.json    every resolved profile content table (games/ashenspire/packs/core + base cards)
//   adapter/*.json     legacy-adapter tables a Core 2 run reads from the registries: relics,
//                      events, flasks, statuses, enemies (compiled moves), encounters/seats/map
//                      configs, equipment rows the reward groups name, and the legacy cards those
//                      tables reference
//   ui/combat.json     combat presentation data (phase 7): pacing controls, Auto speed, mobile-fast preset, the
//                      Combat settings rows, the combat log's sizes, palette and copy
//   manifest.json      JS commit/build, profile hash, sha256/bytes/row counts per file, gaps
// JS key order is preserved inside content (move, weight and category order is read by the
// game); only objects this tool builds (manifest, file maps) are key-sorted.
import {join} from 'node:path';
import {readFileSync, writeFileSync} from 'node:fs';
import {root, CORE, JS_REPOSITORY, parseArgs, jsIdentity, requirePin, loadJs, toPlain, sortKeys, jsonText, sha256, syncTree, folderMeta, textMeta} from './js-core2-shared.mjs';

const USAGE = 'usage: node tools/export-js-core2.mjs <js-checkout> [--check] [--allow-unpinned]';
const OUT = 'GameContent/Unity/Core2';
const MIRROR = 'Unity/Assets/AshenSpire/Resources/Core2';

const args = parseArgs(process.argv.slice(2), USAGE);
let identity, pinCheck;
try { identity = jsIdentity(args.js); pinCheck = requirePin(identity, args.allowUnpinned); }
catch (error) { console.error(`export-js-core2: ${error.message}`); process.exit(1); }

const js = await loadJs(args.js);
const {profile, registries, bundle, catalog} = js;
const gaps = [];
const plain = (value, path) => toPlain(value, path, gaps);
const files = {};
const tables = {};
const put = (path, value, counts) => { files[path] = jsonText(value); tables[path] = counts; };
const count = value => Array.isArray(value) ? value.length : value && typeof value === 'object' ? Object.keys(value).length : 1;

// 1. Profile snapshot (content tables go to pack/).
const contentKeys = Object.keys(profile.content);
const scalars = Object.fromEntries(contentKeys.filter(key => !profile.content[key] || typeof profile.content[key] !== 'object').map(key => [key, profile.content[key]]));
const {content: _content, ...snapshot} = profile;
put('profile.json', plain({...snapshot, runtimeHash: catalog.runtimeHash, contentKeys, contentScalars: scalars}, 'profile'),
  {modules: Object.keys(profile.modules).length, schemas: Object.keys(profile.schemas || {}).length, contentKeys: contentKeys.length});

// 2. Rule values the Core 2 domain reads.
const {fixCoreMechanics} = await js.load('src/content/generated/fixCoreMechanics.js');
const {stanceRules} = await js.load('src/content/stanceRules.js');
const {CORE2_ACTION_RULES_VERSION} = await js.load('src/model/core2ActionPolicy.js');
// The damage-type table magicShareOf reads (src/content/combatRules.js; a Core 2 fight has no ratingsRules
// override) and the Counter coverage defaults armExpansionCounter reads (combatExpansionMatchups.counter,
// what a Core 2 fight's combatExpansionRules.matchups carries).
const {combatRules} = await js.load('src/content/combatRules.js');
const {combatExpansionMatchups} = await js.load('src/content/combatMatchups.js');
const damageTypes = {damageTypes: combatRules.damageTypes, schools: combatRules.schools, magicSplit: combatRules.magicSplit};
const counterRules = {defaultCoverage: combatExpansionMatchups.counter.defaultCoverage, maxReturnsPerCycle: combatExpansionMatchups.counter.maxReturnsPerCycle};
// The enemy move price table and SP row an enemy plan reads (src/content/actionCosts.js; phase 5c): a move without
// its own `sp` costs its stance's table price (model/actionCosts.js enemyMoveSpOf, stanceCost/actionStance).
const {ACTION_COST_TABLE, ENEMY_SP_ROW} = await js.load('src/content/actionCosts.js');
const actionCosts = {table: ACTION_COST_TABLE, enemySpRow: ENEMY_SP_ROW};
// The expansion status-control rules a Core 2 fight snapshots (combatExpansionRules.statuses = content/combatStatusRules.js):
// active-status caps, pressure gauges, chance recovery and its attribute profiles (phase 5c fight loop).
const {combatStatusRules} = await js.load('src/content/combatStatusRules.js');
// The legacy Poise meter a physical status-pressure fill strains (combatStatusControl.js applyStatusPressure): its growth
// after a fill (actions.js growPoiseMeter: balance.poise.growthMult) and what the player's fill costs (actions.js
// staggerPlayer: balance.stagger.player actionLoss and statuses), read off the profile-bound registries.
const poiseMeter = {growthMult: registries.balance.poise.growthMult, playerStagger: registries.balance.stagger.player};
put('mechanics.json', plain({core2ActionRulesVersion: CORE2_ACTION_RULES_VERSION, fixCoreMechanics, stanceRules, damageTypes, counterRules, actionCosts,
  statusControl: combatStatusRules, poiseMeter}, 'mechanics'),
  {fixCoreMechanics: count(fixCoreMechanics), stanceRules: count(stanceRules), damageTypes: count(damageTypes.damageTypes), counterCoverage: count(counterRules.defaultCoverage),
    actionCostTable: count(actionCosts.table), statusControl: count(combatStatusRules.statuses)});

// 2b. Combat presentation data the Unity combat screen reads (phase 7): the two pacing controls and their bounds, the Auto
// speed and the mobile-fast preset (content/config/ui/presentation/actionPacing.json via uiConfig), the animation speed
// table, the Combat settings rows (and Accessibility's one-tap switch) with their defaults, the combat log's sizes and
// actor palette, and the UI copy the log draws (content/source/uiStrings.csv; {tokens} left in place).
{
  const {uiConfig} = await js.load('src/config/generated/ui.js');
  const {ANIM_SPEEDS, COMBAT_TIMING} = await js.load('src/ui/animationPace.js');
  const {settingsRows} = await js.load('src/ui/screens/settings.js');
  const {COMBAT_LOG_SIZES, COMBAT_LOG_DEFAULT_SIZE} = await js.load('src/ui/models/CombatToolsModel.js');
  const {COMBAT_LOG_PALETTE_DEFAULTS} = await js.load('src/ui/models/CombatLogPaletteModel.js');
  const {t, tFull} = await js.load('src/ui/strings.js');
  const SETTING_KEYS = ['autoCombat', 'combatSpeed', 'autoCombatSpeed', 'combatStepPause', 'animationStepDuration', 'combatPacingPreset', 'animSpeed',
    'combatLogConsole', 'combatLogPlayerColor', 'combatLogEnemyColor', 'oneTapPlay'];
  const rows = settingsRows().filter(row => SETTING_KEYS.includes(row.key))
    .map(row => Object.fromEntries(['cat', 'key', 'type', 'def', 'min', 'max', 'step', 'choices', 'label', 'note'].filter(key => row[key] !== undefined).map(key => [key, row[key]])));
  const strings = {
    'combat.log.title': t('combat.log.title'), 'combat.log.full': t('combat.log.full'), 'combat.log.actions': t('combat.log.actions'), 'combat.log.roles': t('combat.log.roles'),
    'combat.log.role.player': t('combat.log.role.player'), 'combat.log.role.enemy': t('combat.log.role.enemy'), 'combat.log.round': t('combat.log.round', {round: '{round}'}),
    'combat.log.empty': tFull('combat.log.empty'), 'combat.log.size.small': t('combat.log.size.small'), 'combat.log.size.medium': t('combat.log.size.medium'), 'combat.log.size.large': t('combat.log.size.large'),
    'combat.receipt.hitLabel': t('combat.receipt.hitLabel'), 'combat.receipt.poiseAbsorbed': t('combat.receipt.poiseAbsorbed', {n: '{n}'}), 'combat.receipt.poiseDamage': t('combat.receipt.poiseDamage', {n: '{n}'}),
    'combat.range.actionSkippedSp': t('combat.range.actionSkippedSp', {required: '{required}', available: '{available}'}), 'combat.range.actionSkippedRange': t('combat.range.actionSkippedRange'),
  };
  put('ui/combat.json', plain({actionPacing: uiConfig.presentation.actionPacing, animSpeeds: ANIM_SPEEDS, combatTiming: COMBAT_TIMING, settings: rows,
    combatLog: {sizes: COMBAT_LOG_SIZES, defaultSize: COMBAT_LOG_DEFAULT_SIZE, palette: COMBAT_LOG_PALETTE_DEFAULTS, paletteBackground: '#43403d', minimumContrast: 4.5,
      detailToggle: {full: 'Full detail', compact: 'Compact log'}}, strings}, 'ui'), {settings: rows.length, strings: Object.keys(strings).length});
}

// 3. Core pack: every resolved profile content table.
for (const key of contentKeys) if (!(key in scalars)) put(`pack/${key}.json`, plain(profile.content[key], `content.${key}`), {[key]: count(profile.content[key])});

// 4. Legacy-adapter tables, from the runtime registries a Core 2 run binds.
const all = registry => registry.all().map(row => row);
const relics = all(registries.relics), flasks = all(registries.flasks), statuses = all(registries.statuses), events = all(registries.events);
const enemies = all(registries.enemies), encounters = all(registries.encounters), seats = all(registries.seats);
put('adapter/relics.json', plain(relics, 'relics'), {relics: relics.length});
put('adapter/flasks.json', plain(flasks, 'flasks'), {flasks: flasks.length});
put('adapter/statuses.json', plain({statuses, proposalStatusRules: bundle.proposalStatusRules}, 'statuses'), {statuses: statuses.length});
put('adapter/events.json', plain({events, eventHistoryRequirements: bundle.eventHistoryRequirements, eventChoiceIds: bundle.eventChoiceIds, questChains: bundle.questChains, eventSpeakers: bundle.eventSpeakers, speakers: bundle.speakers}, 'events'),
  {events: events.length, questChains: count(bundle.questChains), speakers: count(bundle.speakers)});
put('adapter/enemies.json', plain(enemies, 'enemies'), {enemies: enemies.length, moves: enemies.reduce((n, e) => n + Object.keys(e.moves || {}).length, 0)});
put('adapter/encounters.json', plain({encounters, seats, mapConfigs: bundle.mapConfigs}, 'encounters'), {encounters: encounters.length, seats: seats.length, mapConfigs: count(bundle.mapConfigs)});
const equipment = {armaments: registries.equipment.armaments, armour: registries.equipment.armour, slots: registries.equipment.slots};
put('adapter/equipment.json', plain(equipment, 'equipment'), {armaments: equipment.armaments.length, armour: equipment.armour.length, slots: count(equipment.slots)});

// Legacy cards the adapter tables name (event, relic and enemy-move grants), beyond the profile's own cards.
const profileCardIds = new Set(profile.content.cards.map(card => card.id));
const referenced = new Set();
(function scan(value) {
  if (typeof value === 'string') { if (!profileCardIds.has(value) && registries.cards.has(value)) referenced.add(value); return; }
  if (value && typeof value === 'object') for (const child of Object.values(value)) scan(child);
})(JSON.parse(files['adapter/relics.json'] + '').concat(JSON.parse(files['adapter/events.json']).events, JSON.parse(files['adapter/flasks.json'])));
// Enemy moves name the cards they shuffle into the player's piles (addCard, e.g. a curse's Dazed).
(function scan(value) {
  if (Array.isArray(value)) { for (const child of value) scan(child); return; }
  if (!value || typeof value !== 'object') return;
  if (value.op === 'addCard' && typeof value.card === 'string' && !profileCardIds.has(value.card) && registries.cards.has(value.card)) referenced.add(value.card);
  for (const child of Object.values(value)) scan(child);
})(JSON.parse(files['adapter/enemies.json']));
const adapterCards = [...referenced].sort().map(id => registries.cards.get(id));
put('adapter/cards.json', plain(adapterCards, 'adapterCards'), {cards: adapterCards.length});

// Scripted effects are JS functions: listed for phase 8, never exported as data.
const scripts = [];
(function scan(value, path) {
  if (value && typeof value === 'object') {
    if (typeof value.script === 'string') scripts.push({path, script: value.script});
    for (const [key, child] of Object.entries(value)) scan(child, `${path}.${key}`);
  }
})({relics: JSON.parse(files['adapter/relics.json']), events: JSON.parse(files['adapter/events.json']).events, flasks: JSON.parse(files['adapter/flasks.json'])}, 'adapter');

// 5. Manifest.
const sums = {
  classes: profile.content.classes.length,
  characters: profile.content.characters.length,
  profileCards: profile.content.cards.length,
  coreCards: profile.content.cards.filter(card => card.id.startsWith('core.')).length,
  baseCards: profile.content.cards.filter(card => card.id.startsWith('base.')).length,
  enemyCards: profile.content.enemyCards.length,
  enemies: profile.content.enemies.length,
  enemyRelics: profile.content.enemyRelics.length,
  gear: profile.content.gear.length,
  gearRightHand: profile.content.gear.filter(g => g.slot === 'rightHand').length,
  gearLeftHand: profile.content.gear.filter(g => g.slot === 'leftHand').length,
  gearArmour: profile.content.gear.filter(g => !['rightHand', 'leftHand'].includes(g.slot)).length,
  relics: relics.length, events: events.length, flasks: flasks.length, statuses: statuses.length,
  encounters: encounters.length, seats: seats.length, adapterCards: adapterCards.length,
  armaments: equipment.armaments.length, armour: equipment.armour.length,
};
const manifest = sortKeys({
  schemaVersion: 1,
  generator: 'tools/export-js-core2.mjs',
  js: {repository: JS_REPOSITORY, commit: identity.commit, build: identity.build, release: identity.release, ordinal: identity.ordinal, digest: identity.digest},
  profile: {id: profile.id, core: CORE, hash: profile.hash, runtimeHash: catalog.runtimeHash, core2ActionRulesVersion: CORE2_ACTION_RULES_VERSION,
    portability: profile.portability},
  counts: sums,
  files: Object.keys(files).sort().map(path => ({path, sha256: sha256(files[path]), bytes: Buffer.byteLength(files[path]), tables: tables[path]})),
  gaps: gaps.map(gap => ({...gap})),
  scriptedEffects: scripts,
});
files['manifest.json'] = jsonText(manifest);

// 6. Resources mirror (consumed by Resources.Load at runtime; BuildTools "Validate and Import Content" rewrites it too).
const mirror = {};
for (const [path, text] of Object.entries(files)) { mirror[path] = text; mirror[path + '.meta'] = textMeta(`${MIRROR}/${path}`); }
for (const folder of new Set(Object.keys(files).filter(p => p.includes('/')).map(p => p.slice(0, p.lastIndexOf('/'))))) mirror[folder + '.meta'] = folderMeta(`${MIRROR}/${folder}`);

const diffs = [
  ...syncTree(join(root, OUT), files, args.check, OUT),
  ...syncTree(join(root, MIRROR), mirror, args.check, MIRROR),
];
const topMeta = join(root, MIRROR + '.meta');
const wantTop = folderMeta(MIRROR);
let topText = null; try { topText = readFileSync(topMeta, 'utf8'); } catch { /* absent */ }
if (topText !== wantTop) {
  if (args.check) diffs.push(`differs ${MIRROR}.meta`);
  else writeFileSync(topMeta, wantTop);
}

const pinNote = pinCheck.problems.length ? ` (UNPINNED: ${pinCheck.problems.join('; ')})` : '';
if (args.check) {
  for (const line of diffs) console.error(line);
  if (diffs.length) { console.log(`export-js-core2: ${Object.keys(files).length + Object.keys(mirror).length + 1 - diffs.length} passed, ${diffs.length} failed`); console.error(`Regenerate: node tools/export-js-core2.mjs ${args.js}`); process.exit(1); }
  console.log(`JS ${identity.commit.slice(0, 8)} build ${identity.build}, profile ${profile.hash.slice(0, 19)}${pinNote}`);
  console.log(`export-js-core2: OK — ${Object.keys(files).length + Object.keys(mirror).length + 1} checks passed`);
} else {
  console.log(`JS ${identity.commit.slice(0, 8)} build ${identity.build}, profile ${profile.hash.slice(0, 19)}${pinNote}`);
  console.log(`Wrote ${Object.keys(files).length} files to ${OUT} and its Resources mirror; ${gaps.length} non-JSON value(s) recorded, ${scripts.length} scripted effect(s).`);
  console.log(JSON.stringify(sums));
}
