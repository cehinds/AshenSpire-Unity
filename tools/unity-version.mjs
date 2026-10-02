#!/usr/bin/env node
// Unity game version tool. GameContent/Unity/version.json is authoritative
// (see docs/Unity-Versioning.md). Version format A.B.C.D:
//   A = release (1 = finished game; owner-only)
//   B = completed feature count
//   C = user story within the current feature
//   D = patch
// BuildNumber increases by one on every bump.
//
// Usage
//   node tools/unity-version.mjs show
//   node tools/unity-version.mjs bump <patch|story|feature|release> [--owner-approved]
//        [--stage "<text>"] [--note "<text>"]... [--feature F0x] [--date YYYY-MM-DD]
//        [--dry-run] [--root <dir>]
//   node tools/unity-version.mjs check --base <git-ref> [--root <dir>]
//
// `check` prints a counted verdict line for tools/verdict.mjs
// (`unity-version: OK — N checks passed.`) and exits 1 on any failure.
// Root CHANGELOG.md belongs to the HTML game's receipt contract and is never touched.
import {readFileSync, writeFileSync, existsSync} from 'node:fs';
import {resolve, join} from 'node:path';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

export const VERSION_PATH = 'GameContent/Unity/version.json';
export const CHANGELOG_PATH = 'docs/Unity-Changelog.md';
export const ROADMAP_PATH = 'docs/Unity-Roadmap.md';
export const GAMEPLAY_PREFIXES = ['Unity/Assets/', 'Unity/Packages/', 'Unity/ProjectSettings/', 'GameContent/Unity/'];
const VERSION_RE = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const BUMPS = ['patch', 'story', 'feature', 'release'];

export function parseVersion(text) {
  const m = VERSION_RE.exec(String(text));
  if (!m) throw Error(`Invalid four-part version: ${text}`);
  return m.slice(1, 5).map(Number);
}
export const formatVersion = (parts) => parts.join('.');

export function compareVersions(a, b) {
  const x = parseVersion(a), y = parseVersion(b);
  for (let i = 0; i < 4; i++) if (x[i] !== y[i]) return x[i] < y[i] ? -1 : 1;
  return 0;
}

export function bumpVersion(version, kind) {
  const [a, b, c, d] = parseVersion(version);
  switch (kind) {
    case 'patch': return formatVersion([a, b, c, d + 1]);
    case 'story': return formatVersion([a, b, c + 1, 0]);
    case 'feature': return formatVersion([a, b + 1, 0, 0]);
    case 'release': return formatVersion([a + 1, 0, 0, 0]);
    default: throw Error(`Unknown bump kind: ${kind} (expected ${BUMPS.join('|')})`);
  }
}

// Which single bump turns `from` into `to`, or null if it is not one legal step.
export function stepKind(from, to) {
  for (const kind of BUMPS) if (bumpVersion(from, kind) === to) return kind;
  return null;
}

export const isGameplayPath = (path) =>
  path !== VERSION_PATH && GAMEPLAY_PREFIXES.some((prefix) => path.startsWith(prefix));

function parseArgs(argv) {
  const opts = {_: [], notes: []};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      if (i + 1 >= argv.length) throw Error(`${arg} needs a value`);
      return argv[++i];
    };
    if (arg === '--owner-approved') opts.ownerApproved = true;
    else if (arg === '--dry-run') opts.dryRun = true;
    else if (arg === '--stage') opts.stage = value();
    else if (arg === '--note') opts.notes.push(value());
    else if (arg === '--feature') opts.feature = value();
    else if (arg === '--date') opts.date = value();
    else if (arg === '--root') opts.root = value();
    else if (arg === '--base') opts.base = value();
    else if (arg.startsWith('--')) throw Error(`Unknown option: ${arg}`);
    else opts._.push(arg);
  }
  return opts;
}

export function readVersionFile(root) {
  const data = JSON.parse(readFileSync(join(root, VERSION_PATH), 'utf8'));
  validateVersionData(data);
  return data;
}

function validateVersionData(data) {
  parseVersion(data.Version);
  if (!Number.isSafeInteger(data.BuildNumber) || data.BuildNumber < 1) throw Error('BuildNumber must be a positive integer');
  if (typeof data.Stage !== 'string' || !data.Stage) throw Error('Stage must be a non-empty string');
}

const CHANGELOG_HEADER = '# AshenSpire Unity changelog\n\nOne entry per Unity version bump, newest first. Written by `node tools/unity-version.mjs bump`.\nThe root CHANGELOG.md belongs to the HTML game and is not updated here.\n\n';

export function prependChangelog(text, entry) {
  if (!text) return CHANGELOG_HEADER + entry;
  const first = text.search(/^## /m);
  if (first === -1) return text.replace(/\n*$/, '\n\n') + entry;
  return text.slice(0, first) + entry + '\n' + text.slice(first);
}

export function changelogEntry(version, build, date, notes) {
  const bullets = (notes.length ? notes : ['No notes recorded.']).map((n) => `- ${n}`).join('\n');
  return `## ${version} · build ${build} · ${date}\n\n${bullets}\n`;
}

// Flip `| F0x | name | status | ...` status cell from in-progress/todo to `done (<version>)`.
export function flipRoadmapFeature(text, feature, version) {
  const lines = text.split('\n');
  for (let i = 0; i < lines.length; i++) {
    if (!lines[i].startsWith(`| ${feature} `)) continue;
    const cells = lines[i].split('|');
    // cells[0] is '' before the leading pipe; status is the third table cell.
    if (cells.length < 5) return {text, changed: false, reason: `row for ${feature} has no status cell`};
    const status = cells[3].trim().toLowerCase();
    if (status !== 'in-progress' && status !== 'todo') return {text, changed: false, reason: `${feature} status is "${cells[3].trim()}", not in-progress/todo`};
    cells[3] = ` done (${version}) `;
    lines[i] = cells.join('|');
    return {text: lines.join('\n'), changed: true};
  }
  return {text, changed: false, reason: `no roadmap table row starts with "| ${feature} "`};
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function bump(root, opts) {
  const kind = opts._[1];
  if (!BUMPS.includes(kind)) throw Error(`bump needs one of: ${BUMPS.join(', ')}`);
  if (kind === 'release' && !opts.ownerApproved) {
    console.error('unity-version: refused — a release bump (1.0.0.0 = the finished game) is owner-only. Pass --owner-approved only when the owner has said yes.');
    return 1;
  }
  if (opts.feature && kind !== 'feature') throw Error('--feature is only valid with `bump feature`');
  const data = readVersionFile(root);
  const next = {...data};
  next.Version = bumpVersion(data.Version, kind);
  next.BuildNumber = data.BuildNumber + 1;
  if (opts.stage !== undefined) next.Stage = opts.stage;
  // Preserve key order: reassigning existing keys on a spread keeps insertion order.
  const date = opts.date || today();
  const versionText = JSON.stringify(next, null, 2) + '\n';

  const changelogFile = join(root, CHANGELOG_PATH);
  const changelogText = prependChangelog(existsSync(changelogFile) ? readFileSync(changelogFile, 'utf8') : '', changelogEntry(next.Version, next.BuildNumber, date, opts.notes));

  let roadmapText = null;
  if (opts.feature) {
    const roadmapFile = join(root, ROADMAP_PATH);
    if (!existsSync(roadmapFile)) console.warn(`unity-version: warning — ${ROADMAP_PATH} not found; roadmap unchanged.`);
    else {
      const flipped = flipRoadmapFeature(readFileSync(roadmapFile, 'utf8'), opts.feature, next.Version);
      if (flipped.changed) roadmapText = flipped.text;
      else console.warn(`unity-version: warning — ${flipped.reason}; roadmap unchanged.`);
    }
  }

  console.log(`${data.Version} (build ${data.BuildNumber}) -> ${next.Version} (build ${next.BuildNumber}) [${kind}]`);
  console.log(`Stage: ${next.Stage}`);
  if (opts.dryRun) {
    console.log('Dry run — nothing written.');
    process.stdout.write(versionText);
    return 0;
  }
  writeFileSync(join(root, VERSION_PATH), versionText);
  writeFileSync(changelogFile, changelogText);
  if (roadmapText !== null) {
    writeFileSync(join(root, ROADMAP_PATH), roadmapText);
    console.log(`Roadmap: ${opts.feature} marked done (${next.Version}).`);
  }
  console.log(`Wrote ${VERSION_PATH} and ${CHANGELOG_PATH}.`);
  return 0;
}

function git(root, args) {
  return execFileSync('git', args, {cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']});
}

export function changedPaths(root, base) {
  let committed;
  try {
    committed = git(root, ['diff', '--name-only', `${base}...HEAD`]);
  } catch {
    committed = git(root, ['diff', '--name-only', base, 'HEAD']);
  }
  // Uncommitted tracked edits count too: the check reads the working tree's version.json.
  const uncommitted = git(root, ['diff', '--name-only', 'HEAD']);
  return [...new Set((committed + '\n' + uncommitted).split('\n').map((s) => s.trim()).filter(Boolean))].sort();
}

function check(root, opts) {
  if (!opts.base) throw Error('check needs --base <git-ref>');
  const failures = [];
  let passed = 0;
  const ok = () => passed++;
  const fail = (message) => failures.push(message);

  const head = readVersionFile(root);
  ok(); // working-tree version.json is valid four-part metadata

  let base;
  try {
    base = JSON.parse(git(root, ['show', `${opts.base}:${VERSION_PATH}`]));
    validateVersionData(base);
    ok();
  } catch (error) {
    console.error(`unity-version: could not read ${VERSION_PATH} at ${opts.base}: ${error.message.split('\n')[0]}`);
    return 2;
  }

  const gameplay = changedPaths(root, opts.base).filter(isGameplayPath);
  const cmp = compareVersions(head.Version, base.Version);
  console.log(`base ${opts.base}: ${base.Version} (build ${base.BuildNumber}); working tree: ${head.Version} (build ${head.BuildNumber})`);
  console.log(`gameplay paths changed: ${gameplay.length}`);
  for (const path of gameplay.slice(0, 20)) console.log(`  ${path}`);
  if (gameplay.length > 20) console.log(`  ... and ${gameplay.length - 20} more`);

  if (cmp < 0) fail(`version went backwards: ${base.Version} -> ${head.Version}`);
  else ok();

  if (gameplay.length) {
    if (cmp > 0) ok();
    else fail(`gameplay files changed but Version ${head.Version} is not greater than base ${base.Version}; run \`node tools/unity-version.mjs bump <kind>\``);
  } else ok();

  if (cmp > 0) {
    const kind = stepKind(base.Version, head.Version);
    if (kind) {
      ok();
      console.log(`bump: ${kind}${kind === 'release' ? ' (owner-only; confirm the owner approved it)' : ''}`);
    } else fail(`${base.Version} -> ${head.Version} is not one legal step (expected one of ${BUMPS.map((k) => bumpVersion(base.Version, k)).join(', ')})`);
    if (head.BuildNumber > base.BuildNumber) ok();
    else fail(`Version changed but BuildNumber did not increase (${base.BuildNumber} -> ${head.BuildNumber})`);
  } else if (cmp === 0) {
    if (head.BuildNumber >= base.BuildNumber) ok();
    else fail(`BuildNumber decreased (${base.BuildNumber} -> ${head.BuildNumber})`);
  }

  for (const message of failures) console.error(`unity-version: FAILED — ${message}`);
  if (failures.length) {
    console.error(`unity-version: ${failures.length} problem(s) found`);
    return 1;
  }
  console.log(`unity-version: OK — ${passed} checks passed.`);
  return 0;
}

function show(root) {
  const data = readVersionFile(root);
  console.log(`Version: ${data.Version}`);
  console.log(`BuildNumber: ${data.BuildNumber}`);
  console.log(`Stage: ${data.Stage}`);
  return 0;
}

export function main(argv) {
  const opts = parseArgs(argv);
  const root = resolve(opts.root || fileURLToPath(new URL('..', import.meta.url)));
  const command = opts._[0];
  if (command === 'show') return show(root);
  if (command === 'bump') return bump(root, opts);
  if (command === 'check') return check(root, opts);
  console.error('usage: unity-version.mjs show | bump <patch|story|feature|release> [options] | check --base <ref>');
  return 2;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(`unity-version: ${error.message}`);
    process.exitCode = 2;
  }
}
