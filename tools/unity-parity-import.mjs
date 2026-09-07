// Export the pinned original content and executable reference results. Never fetches,
// changes the reference checkout, or accepts another revision without an explicit edit.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const reference = path.resolve(process.argv[2] || '');
const baseline = 'b17a7f4543e1710f49fae8b58880121690a314de';
const existingContent = path.join(root, 'GameContent/Unity/Original/content.json');
const existingManifest = path.join(root, 'GameContent/Unity/Original/manifest.json');
if (fs.existsSync(existingContent)) {
  if (!fs.existsSync(existingManifest) || createHash('sha256').update(fs.readFileSync(existingContent)).digest('hex') !== JSON.parse(fs.readFileSync(existingManifest, 'utf8')).contentSha256)
    throw Error('Original fork content has owner edits or no import receipt. Preserve them before importing another baseline.');
}
const git = (...args) => execFileSync('git', ['-C', reference, ...args], { encoding: 'utf8' }).trim();
if (!process.argv[2] || git('rev-parse', 'HEAD') !== baseline) throw Error(`Expected original AshenSpire at ${baseline}`);
if (git('status', '--porcelain', '--', 'src', 'content')) throw Error('Reference source has local changes; export refused.');
const load = relative => import(pathToFileURL(path.join(reference, relative)).href);
const { contentBundle: bundle } = await load('src/content/index.js');
const { validateContent } = await load('src/model/validate.js');
const validation = validateContent(bundle);
if (validation.errors?.length || validation.ok === false) throw Error(JSON.stringify(validation));
const { createRng, STREAM_NAMES, seedFromString, seedToString } = await load('src/engine/rng.js');
const { evaluate } = await load('src/model/formulas.js');
const { generateActMap } = await load('src/engine/mapgen.js');
const { resolveFloorPlan } = await load('src/model/floorplan.js');
const { resolveDerivedStatRules, deriveStat } = await load('src/model/derivedStats.js');
const { tagIndex } = await load('src/model/tags.js');
const { createFlaskCharges, reallocateFlaskCharges } = await load('src/model/gracerefill.js');
const { createRegistries } = await load('src/model/registries.js');
const { applyStatus, decayAtTurnEnd } = await load('src/engine/statuses.js');
const { computeAttackDamage } = await load('src/engine/actions.js');
const registries = createRegistries(bundle);

const seeds = [0, 1, 25, 67, 240987, 0x80000000, 0xffffffff];
const rng = seeds.map(seed => {
  const r = createRng(seed);
  const streams = Object.fromEntries(STREAM_NAMES.map(name => [name, Array.from({ length: 64 }, () => r.float(name))]));
  const restored = createRng(seed, r.getCounters());
  return { seed, streams, counters: r.getCounters(), continuation: Object.fromEntries(STREAM_NAMES.map(name => [name, restored.float(name)])) };
});
const maps = [];
const configs = Object.entries(bundle.mapConfigs).map(([id, config]) => ({ id, config, plan: resolveFloorPlan(config).plan }));
for (const { id, config } of configs) for (const seed of seeds) {
  const r = createRng(seed);
  maps.push({ id, seed, map: generateActMap({ config, rng: r }), counters: r.getCounters() });
}
const formulaCases = [];
const entities = {
  player: { hp: 21, maxHp: 53, block: 9, statuses: { bleed: { meter: { value: 17, max: 40 } }, strength: { stacks: 3 } } },
  enemy: { hp: 11, maxHp: 40, block: 7, statuses: { bleed: { meter: { value: 8, max: 40 } } } }
};
entities.self = entities.player; entities.owner = entities.player; entities.target = entities.enemy;
entities.allEnemies = [entities.enemy, { hp: 13, maxHp: 33, block: 0, statuses: { bleed: { stacks: 5 } } }];
const context = { entities, energySpent: 3, cardsPlayedThisTurn: 4 };
const formulas = [1.9, -1.2, { f: 'add', args: [0.6, 0.6] }, { f: 'mul', args: [2.3, 1.5] },
  { f: 'add', args: [{ f: 'mul', args: [1.6, 1.6] }, 0.5] },
  { f: 'percentMaxHp', of: 'player', pct: 35 }, { f: 'missingHp', of: 'player' },
  { f: 'stacks', of: 'allEnemies', status: 'bleed', per: 4 }, { f: 'stacks', of: 'self', status: 'strength' },
  { f: 'energySpent', per: 2.5 }, { f: 'cardsPlayedThisTurn', per: 1.5 }, { f: 'blockOf', of: 'target' },
  { f: 'hpOf', of: 'enemy', min: 15, max: 20 }];
const seen = new Set();
function walk(value) {
  if (!value || typeof value !== 'object') return;
  if (typeof value.f === 'string') { const key = JSON.stringify(value); if (!seen.has(key)) { seen.add(key); formulas.push(value); } }
  for (const child of Object.values(value)) walk(child);
}
walk(bundle);
for (const formula of formulas) formulaCases.push({ formula, context, value: evaluate(formula, context) });
const derived = [];
const options = { attributeIds: bundle.attributes.map(x => x.id), classFields: ['maxHp', 'maxMana'] };
const resolved = resolveDerivedStatRules(bundle.derivedStatRules, options);
for (const [mode, presets] of Object.entries(bundle.attributeRules.presets)) for (const classDef of bundle.classes) {
  const attributes = presets[classDef.id];
  derived.push({ mode, classId: classDef.id, attributes, values: Object.fromEntries(Object.keys(resolved.rules).map(id => [id, deriveStat(resolved, id, { attributes, classDef })])) });
}
const index = tagIndex(bundle);
const tags = [];
for (const [family, spec] of index.families) {
  let rows = bundle; for (const part of spec.source.split('.')) rows = rows[part];
  for (const record of rows || []) tags.push({ family, record, expected: index.tagIdsOf(family, record) });
}
const flasks = bundle.classes.map(c => {
  const initial = createFlaskCharges(bundle.balance, c.startingFlaskAllocation);
  const adjusted = reallocateFlaskCharges(structuredClone(initial), { hp: 1, mana: initial.capacity - 1 });
  return { classId: c.id, initial, adjusted };
});
const counts = Object.fromEntries(Object.entries(bundle).filter(([, value]) => Array.isArray(value)).map(([id, rows]) => [id, rows.length]));
for (const [id, rows] of Object.entries(bundle.equipment)) if (Array.isArray(rows)) counts['equipment.' + id] = rows.length;
const operations = {};
function collect(value, address = '') {
  if (!value || typeof value !== 'object') return;
  if (typeof value.op === 'string') (operations[value.op] ||= []).push(address);
  for (const [key, child] of Object.entries(value)) collect(child, address ? address + '.' + key : key);
}
collect(bundle);
const statusCases = [];
function entity(kind) { return { id: kind, kind, enemyId: kind === 'enemy' ? bundle.enemies[0].id : undefined, hp: 41, maxHp: 53, block: 5, alive: true, statuses: {} }; }
for (const definition of bundle.statuses) for (const kind of ['player', 'enemy']) {
  const target = entity(kind), initial = structuredClone(target), player = kind === 'player' ? target : entity('player');
  const events = [], queue = [];
  const ctx = { registries, player, enemies: kind === 'enemy' ? [target] : [], emit: (type, data) => events.push({ type, ...structuredClone(data) }), enqueue: action => queue.push(structuredClone(action.effect)) };
  for (const amount of [0, 1, 10, 100, 1]) applyStatus(ctx, target, definition.id, amount, player);
  for (let turn = 0; turn < 3; turn++) decayAtTurnEnd(ctx, target);
  statusCases.push({ id: definition.id, kind, initial, target, events, queue });
}
const damages = [];
for (const definition of bundle.statuses) for (const stance of [null, ...bundle.stances.map(x => x.id)]) {
  const source = entity('player'), target = entity('enemy');
  source.stanceId = stance; source.damageBySchoolAdd = { magic: 2, physical: 1 };
  target.damageResistanceBySchool = { magic: 15, physical: 10 };
  source.statuses[definition.id] = { stacks: 3 }; target.statuses[definition.id] = { stacks: 3 };
  const ctx = { registries, player: source, enemies: [target] };
  for (const school of ['physical', 'magic']) for (const base of [0, 6.6, 19]) {
    const tags = ['physical', 'magic', 'melee'];
    damages.push({ source: structuredClone(source), target: structuredClone(target), base, school, tags,
      value: computeAttackDamage(ctx, source, target, base, tags, { damageSchool: school }) });
  }
}
const json = JSON.stringify(bundle, null, 2) + '\n';
const manifest = { schemaVersion: 1, repository: 'cehinds/AshenSpire', commit: baseline, contentVersion: bundle.version,
  contentSha256: createHash('sha256').update(json).digest('hex'), counts, operations,
  scope: 'Imported data is not proof that every gameplay operation has a Unity implementation.' };
const oracle = { baseline, content: bundle, rng, seeds: ['', '0', 'O', 'ASHEN', 'ZZZZZZZZZZ', '  abc  '].map(text => ({ text, value: seedFromString(text), display: seedToString(seedFromString(text)) })), configs, maps, formulas: formulaCases, derived, tags, flasks, statusCases, damages };
for (const [relative, value] of [['GameContent/Unity/Original/content.json', json], ['GameContent/Unity/Original/manifest.json', JSON.stringify(manifest, null, 2) + '\n'], ['UnityTests/Parity/reference.json', JSON.stringify(oracle, null, 2) + '\n']]) {
  const dest = path.join(root, relative); fs.mkdirSync(path.dirname(dest), { recursive: true }); fs.writeFileSync(dest, value);
}
console.log(JSON.stringify({ baseline, counts, formulaCases: formulaCases.length, mapCases: maps.length, tagCases: tags.length, operations: Object.keys(operations) }, null, 2));
