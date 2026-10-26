// Shared helpers for tools/export-js-core2.mjs and tools/oracle-js-core2.mjs.
// Both run the JS game's own modules at the pinned commit (SPEC.md §0.2), read-only
// against the JS checkout, and write deterministic JSON that a --check run compares.
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync, statSync, rmSync} from 'node:fs';
import {join, dirname, relative, sep} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';

// The JS package.json has no "type": "module", so Node warns once per run while loading its
// modules. That warning is about the JS checkout, not a finding; keep it out of the verdict output.
const emitWarning = process.emitWarning.bind(process);
process.emitWarning = (warning, ...rest) => {
  const code = typeof rest[0] === 'object' ? rest[0]?.code : rest[1];
  if (code === 'MODULE_TYPELESS_PACKAGE_JSON' || String(warning?.message ?? warning).includes('Module type of file')) return;
  emitWarning(warning, ...rest);
};

export const root = join(dirname(fileURLToPath(import.meta.url)), '..');
export const CORE = 'ashen.core@2.0.0';
export const JS_REPOSITORY = 'https://github.com/cehinds/AshenSpire';

export function parseArgs(argv, usage) {
  const args = {check: false, allowUnpinned: false, js: null};
  for (const arg of argv) {
    if (arg === '--check') args.check = true;
    else if (arg === '--allow-unpinned') args.allowUnpinned = true;
    else if (arg === '--help' || arg === '-h') { console.log(usage); process.exit(0); }
    else if (arg.startsWith('--')) { console.error(`Unknown option ${arg}\n${usage}`); process.exit(2); }
    else if (!args.js) args.js = arg;
    else { console.error(`Unexpected argument ${arg}\n${usage}`); process.exit(2); }
  }
  args.js ||= process.env.ASHEN_JS_CHECKOUT || null;
  if (!args.js) { console.error(usage); process.exit(2); }
  return args;
}

/** The pin recorded in SPEC.md §0.2 (the one home of the pin). */
export function specPin() {
  const spec = readFileSync(join(root, 'SPEC.md'), 'utf8');
  const commit = /\|\s*Commit \(the pin\)\s*\|\s*`([0-9a-f]{40})`/.exec(spec)?.[1];
  const build = /\|\s*Build\s*\|\s*`(\d+\.\d+\.\d+\.\d+)`, digest `([0-9a-f]+)`/.exec(spec);
  const profile = /\|\s*Profile\s*\|\s*`([^`]+)`/.exec(spec)?.[1];
  if (!commit || !build || !profile) throw new Error('SPEC.md §0.2 pin table not found (Commit (the pin), Build, Profile rows)');
  return {commit, build: build[1], digest: build[2], profile};
}

/** Commit, cleanliness and build stamp of the JS checkout. */
export function jsIdentity(js) {
  if (!existsSync(join(js, 'src/model/gameProfile.js'))) throw new Error(`${js} is not an AshenSpire JS checkout (src/model/gameProfile.js missing)`);
  const git = (...a) => execFileSync('git', ['-C', js, ...a], {encoding: 'utf8'}).trim();
  const commit = git('rev-parse', 'HEAD');
  const dirty = git('status', '--porcelain', '--untracked-files=no');
  const ordinal = JSON.parse(readFileSync(join(js, 'buildordinal.json'), 'utf8'));
  const game = JSON.parse(readFileSync(join(js, 'games/ashenspire/game.json'), 'utf8'));
  return {commit, dirty: dirty.length > 0, build: `${ordinal.release}.${ordinal.ordinal}`, release: ordinal.release, ordinal: ordinal.ordinal, digest: ordinal.digest, gameCore: game.core};
}

export function requirePin(identity, allowUnpinned) {
  const pin = specPin();
  const problems = [];
  if (identity.dirty) problems.push('the JS checkout has uncommitted changes to tracked files');
  if (identity.commit !== pin.commit) problems.push(`JS HEAD ${identity.commit} is not the SPEC §0.2 pin ${pin.commit}`);
  if (identity.build !== pin.build || identity.digest !== pin.digest) problems.push(`JS build ${identity.build}/${identity.digest} is not the SPEC §0.2 build ${pin.build}/${pin.digest}`);
  if (identity.gameCore !== CORE || pin.profile !== CORE) problems.push(`games/ashenspire/game.json selects ${identity.gameCore}; SPEC pins ${pin.profile}; this tool exports ${CORE}`);
  if (problems.length && !allowUnpinned) throw new Error(problems.join('\n') + '\n(--allow-unpinned writes anyway, for a pin-bump PR only)');
  return {pin, problems};
}

export async function loadJs(js) {
  const load = rel => import(pathToFileURL(join(js, rel)).href);
  const gameProfile = await load('src/model/gameProfile.js');
  const catalogModule = await load('src/content/generated/gameProfiles.js');
  const {contentBundle} = await load('src/content/index.js');
  const {createRegistries} = await load('src/model/registries.js');
  const profile = gameProfile.resolveGameProfile('default');
  const byCore = gameProfile.resolveGameProfile(CORE);
  if (profile.hash !== byCore.hash || profile.author?.core !== CORE) throw new Error(`JS 'default' profile is not ${CORE}`);
  // The exact path a Core 2 run takes in src/main.js: applyGameProfileToContent(contentBundle, profile) -> createRegistries.
  const bundle = gameProfile.applyGameProfileToContent(contentBundle, profile);
  const registries = gameProfile.bindGameProfileRegistries(createRegistries(bundle), profile);
  return {load, gameProfile, catalog: catalogModule.gameProfileCatalog, contentBundle, bundle, registries, profile};
}

/** Plain JSON copy that records anything JSON would drop (AshenedSpire Tools/export-content.mjs pattern). */
export function toPlain(value, path, gaps, seen = new WeakSet()) {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) { gaps.push({path, kind: 'nonFiniteNumber', value: String(value)}); return null; }
    return Object.is(value, -0) ? 0 : value;
  }
  if (value === undefined) return undefined;
  if (typeof value === 'function') { gaps.push({path, kind: 'function', name: value.name || '(anonymous)'}); return undefined; }
  if (typeof value === 'symbol' || typeof value === 'bigint') { gaps.push({path, kind: typeof value}); return undefined; }
  if (seen.has(value)) { gaps.push({path, kind: 'cycle'}); return undefined; }
  seen.add(value);
  let out;
  if (Array.isArray(value)) out = value.map((v, i) => { const x = toPlain(v, `${path}[${i}]`, gaps, seen); return x === undefined ? null : x; });
  else if (value instanceof Map) { gaps.push({path, kind: 'Map'}); out = Object.fromEntries([...value].map(([k, v]) => [k, toPlain(v, `${path}.${k}`, gaps, seen)])); }
  else if (value instanceof Set) { gaps.push({path, kind: 'Set'}); out = [...value].map((v, i) => toPlain(v, `${path}[${i}]`, gaps, seen)); }
  else {
    const proto = Object.getPrototypeOf(value);
    if (proto !== Object.prototype && proto !== null) gaps.push({path, kind: 'classInstance', ctor: proto?.constructor?.name});
    out = {};
    for (const key of Object.keys(value)) { const v = toPlain(value[key], `${path}.${key}`, gaps, seen); if (v !== undefined) out[key] = v; }
  }
  seen.delete(value);
  return out;
}

/** Recursively sorted keys: only for objects this tool builds itself, never for JS content whose key order is read (weights, moves). */
export function sortKeys(value) {
  if (Array.isArray(value)) return value.map(sortKeys);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(Object.keys(value).sort().map(key => [key, sortKeys(value[key])]));
}

export const jsonText = value => JSON.stringify(value, null, 1) + '\n';
export const sha256 = text => createHash('sha256').update(text).digest('hex');

/** Every file below dir, as forward-slash paths relative to dir. */
export function listFiles(dir) {
  if (!existsSync(dir)) return [];
  return (function walk(d) {
    return readdirSync(d).sort().flatMap(name => {
      const p = join(d, name);
      return statSync(p).isDirectory() ? walk(p) : [relative(dir, p).split(sep).join('/')];
    });
  })(dir);
}

/** Write (or, with check, compare) a whole output tree: {relative path: text}. Extra files are stale. */
export function syncTree(dir, files, check, label) {
  const want = new Map(Object.entries(files));
  const have = listFiles(dir);
  const diffs = [];
  for (const [path, text] of want) {
    const full = join(dir, path);
    if (!existsSync(full)) diffs.push(`missing ${label}/${path}`);
    else if (readFileSync(full, 'utf8') !== text) diffs.push(`differs ${label}/${path}`);
  }
  for (const path of have) if (!want.has(path)) diffs.push(`stale ${label}/${path}`);
  if (check) return diffs;
  for (const path of have) if (!want.has(path)) rmSync(join(dir, path));
  for (const [path, text] of want) {
    const full = join(dir, path);
    mkdirSync(dirname(full), {recursive: true});
    if (!existsSync(full) || readFileSync(full, 'utf8') !== text) writeFileSync(full, text);
  }
  return diffs;
}

/** Stable Unity GUID for a generated asset path, so regenerated .meta files never churn. */
export const unityGuid = path => createHash('md5').update('AshenSpire.Core2:' + path).digest('hex');
export const folderMeta = path => `fileFormatVersion: 2\nguid: ${unityGuid(path)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`;
export const textMeta = path => `fileFormatVersion: 2\nguid: ${unityGuid(path)}\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`;
