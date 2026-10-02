// Exports original-game PROFILE fixtures (sote_meta_v1 bytes) produced by the
// repository's own JavaScript save manager, unlock rules and discovery ledger.
// Usage: node UnityTests/WebSaveImport/export-profile-reference.mjs [original-checkout]
// The checkout defaults to this repository; src/ must be clean so the receipt
// names exactly the code that wrote the bytes. Writes only this test folder.
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
const {createSaveManager, createMemoryStorage, META_KEY, META_SCHEMA_VERSION} = await load('src/engine/save.js');
const {recordProgress, evaluateUnlocks} = await load('src/model/unlocks.js');
const {recordArmamentDiscovery} = await load('src/model/startingKits.js');
const registries = createRegistries(contentBundle);
const className = id => registries.classes.get(id).name;

// main.js runResult(victory) for a finished run, field for field.
function result(victory, classId, act, floor, fightsWon, seed, {bosses = [], custom = false, ascension = 0, name = 'Forsaken', dealt = 40 * fightsWon, taken = 9 * fightsWon} = {}) {
  return {victory, seed, class: classId, className: className(classId), act, floor, fightsWon, damageDealt: dealt, damageTaken: taken, name, custom, ascension, bosses};
}
// main.js finishRun(victory): recordResult, recordProgress, evaluateUnlocks, saveMeta.
function finish(saves, row) {
  const meta = saves.recordResult(row);
  meta.progress = recordProgress(meta.progress, row);
  const fresh = evaluateUnlocks(registries.unlocks, meta);
  if (fresh.length) meta.unlocked = [...(meta.unlocked || []), ...fresh];
  saves.saveMeta(meta);
}
// main.js collectArmament(id, source) after storage accepted the piece.
function collect(saves, id, source, runSeed, progressionMode = 'normal') {
  const meta = saves.loadMeta();
  if ((meta.found || []).includes(id)) return;
  meta.found = [...(meta.found || []), id];
  const recorded = recordArmamentDiscovery(meta, id, {progressionMode, source, runSeed, receiptLimit: registries.balance.equipment.startingKitDiscovery.receiptLimit});
  saves.saveMeta(recorded.meta);
}
function settings(saves, values) { const meta = saves.loadMeta(); meta.settings = {...(meta.settings || {}), ...values}; saves.saveMeta(meta); }
function manager() { const storage = createMemoryStorage(); return {storage, saves: createSaveManager(storage)}; }
const fixtures = {};
function keep(name, storage, saves, note) {
  const bytes = storage.getItem(META_KEY);
  // exportProfile() stamps the wall clock; pin it so regenerated fixtures are byte-stable.
  const loaded = saves.loadMeta();
  const exported = JSON.parse(saves.exportProfile()); exported.exportedAt = '2026-10-02T00:00:00.000Z';
  fixtures[name] = {note, meta: bytes, exported: JSON.stringify(exported, null, 2), status: saves.profileStatus(), loaded};
}

// 1. Fresh profile: the bytes the original writes before any run finishes.
{ const {storage, saves} = manager(); saves.saveMeta(saves.loadMeta()); keep('fresh', storage, saves, 'Fresh schema-2 profile; no runs, unlocks or settings.'); }

// 2. History: five finished runs (two victories), discoveries and settings.
const historyRuns = [
  result(false, 'reaver', 1, 5, 4, 'EMBERFALL'),
  result(true, 'reaver', 3, 15, 14, 'ASHCROWN', {bosses: ['stitchedKing', 'blightedValkyrie', 'fellWarden']}),
  result(false, 'starseer', 2, 9, 8, 'GLOAMING', {bosses: ['stitchedKing']}),
  result(false, 'rogue', 1, 3, 2, 'CUSTOMCLIMB', {custom: true, ascension: 2}),
  result(true, 'herald', 3, 15, 15, 'LASTLIGHT', {bosses: ['stitchedKing', 'blightedValkyrie', 'fellWarden'], name: 'Vigil'}),
];
function historyProfile() {
  const {storage, saves} = manager();
  settings(saves, {reducedMotion: true, screenShake: false, animSpeed: 'fast', musicVolume: 30, sfxVolume: 60, muteAudio: false,
    uiScale: 'L', textSize: 'XL', colorblindSafe: true, musicEnabled: false, highContrast: true, mapMode: 'fog', holdConfirm: 'long', seenTutorial: true});
  finish(saves, historyRuns[0]); collect(saves, 'dagger', 'monster', 'EMBERFALL');
  finish(saves, historyRuns[1]); collect(saves, 'katana', 'elite', 'ASHCROWN');
  finish(saves, historyRuns[2]);
  collect(saves, 'halberd', 'monster', 'CUSTOMCLIMB', 'custom'); finish(saves, historyRuns[3]);
  finish(saves, historyRuns[4]);
  return {storage, saves};
}
{ const {storage, saves} = historyProfile(); keep('history', storage, saves, 'Five finished runs (2 victories, 1 custom), three found armaments, original settings.'); }

// 3. The same profile after three more runs: a later re-export of the same player.
{
  const {storage, saves} = historyProfile();
  finish(saves, result(false, 'starseer', 1, 6, 5, 'SECONDWIND'));
  finish(saves, result(true, 'starseer', 3, 15, 13, 'STARFALL', {bosses: ['stitchedKing', 'blightedValkyrie', 'fellWarden']}));
  collect(saves, 'warhammer', 'boss', 'STARFALL');
  finish(saves, result(false, 'reaver', 2, 10, 9, 'EMBERFALL'));
  keep('historyLater', storage, saves, 'The history profile re-exported after three more finished runs.');
}

// 4. Veteran: 25 runs. The original keeps 20 results; progress counts all 25.
{
  const {storage, saves} = manager();
  const classes = ['reaver', 'starseer', 'rogue', 'herald'];
  for (let i = 0; i < 25; i++) finish(saves, result(i % 3 === 0, classes[i % 4], 1 + (i % 3), 4 + i % 11, 3 + i % 9, 'VET' + i, {bosses: i % 3 === 0 ? ['stitchedKing'] : []}));
  keep('veteran', storage, saves, 'Twenty-five runs: the original history keeps 20 while progress counts every run.');
}

// 5. Older schemas, written as older builds stored them and migrated by the JS loader.
{
  const {storage, saves} = manager();
  storage.setItem(META_KEY, JSON.stringify({schemaVersion: 1, settings: {reducedMotion: true}, results: [historyRuns[0], historyRuns[1]],
    progress: recordProgress(recordProgress(undefined, historyRuns[0]), historyRuns[1]), unlocked: ['winAsReaver', 'beatStitchedKing'], found: ['dagger']}));
  keep('schema1', storage, saves, 'Schema-1 profile (found without discoveredArmaments); the original migrates it.');
}
{
  const {storage, saves} = manager();
  storage.setItem(META_KEY, JSON.stringify({settings: {muteAudio: true}, results: [historyRuns[2]]}));
  keep('schema0', storage, saves, 'Unversioned pre-#67 profile without a progress tally.');
}

// 6. Newer schema: the original refuses and preserves it; the importer must too.
{
  const {storage, saves} = manager();
  storage.setItem(META_KEY, JSON.stringify({schemaVersion: META_SCHEMA_VERSION + 1, settings: {}, results: [historyRuns[0]], progress: recordProgress(undefined, historyRuns[0]), unlocked: [], futureLedger: {x: 1}}));
  keep('newer', storage, saves, 'A profile written by a newer original build.');
}

const data = {sourceCommit: sha, metaSchemaVersion: META_SCHEMA_VERSION, fixtures};
const bytes = JSON.stringify(data, null, 1) + '\n';
fs.writeFileSync(path.join(out, 'profile-reference.json'), bytes);
const sourceFiles = ['src/engine/save.js', 'src/model/unlocks.js', 'src/model/startingKits.js', 'src/content/index.js', 'src/main.js', 'src/ui/screens/settings.js'];
fs.writeFileSync(path.join(out, 'profile-reference.receipt.json'), JSON.stringify({
  sourceCommit: sha,
  sources: Object.fromEntries(sourceFiles.map(p => [p, createHash('sha256').update(fs.readFileSync(path.join(root, p))).digest('hex')])),
  outputSha256: createHash('sha256').update(bytes).digest('hex'),
  fixtures: Object.fromEntries(Object.entries(fixtures).map(([k, f]) => [k, {status: f.status.state, results: (f.loaded.results || []).length, unlocked: (f.loaded.unlocked || []).length}])),
}, null, 2) + '\n');
for (const [k, f] of Object.entries(fixtures)) console.log(`${k}: ${f.status.state}, ${(f.loaded.results || []).length} results, ${(f.loaded.unlocked || []).length} unlocks, ${(f.loaded.found || []).length} found`);
