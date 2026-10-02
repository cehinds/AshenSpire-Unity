#!/usr/bin/env node
// Generate a Markdown test plan for a Unity dev -> test promotion PR.
//
// Usage
//   node tools/unity-test-plan.mjs --base <ref> --head <ref> [--out <file>] [--root <dir>]
//
// Sections: version range (GameContent/Unity/version.json at each ref), commits
// (`git log --oneline base..head`), changed areas grouped by path, features touched
// (docs/Unity-Roadmap.md rows `| F0x | name | status | ...`), automated checks
// (`dotnet run` / `node tools/` commands from .github/workflows/unity-pages.yml at
// head) and a fixed manual smoke checklist.
import {writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

export const AREAS = ['Domain', 'Presentation', 'Content', 'Tools/CI', 'Docs', 'Other'];

export const SMOKE_CHECKLIST = [
  'Start a new run with each class',
  'Navigate the map (pan, zoom, choose a route)',
  'Win one combat and collect the reward',
  'Die in one combat and reach the death screen',
  'Save, reload the page/app and resume the run',
  'Visit a merchant: buy and sell',
  'Visit a shrine',
  'Resolve an event',
  'Change settings and confirm they persist',
  'Play on a phone in portrait orientation',
];

export function classifyPath(path) {
  if (/^Unity\/Assets\/AshenSpire\/Runtime\/(Domain|Application)\//.test(path) || path.startsWith('UnityTests/')) return 'Domain';
  if (/^Unity\/(Assets|Packages|ProjectSettings)\//.test(path)) return 'Presentation';
  if (path.startsWith('GameContent/')) return 'Content';
  if (path.startsWith('tools/') || path.startsWith('.github/')) return 'Tools/CI';
  if (path.startsWith('docs/') || /\.md$/i.test(path)) return 'Docs';
  return 'Other';
}

export function groupPaths(paths) {
  const groups = Object.fromEntries(AREAS.map((a) => [a, []]));
  for (const path of paths) groups[classifyPath(path)].push(path);
  return groups;
}

// Rows `| F01 | name | status | ...` -> [{id, name, status}]
export function parseFeatures(markdown) {
  const features = [];
  for (const line of String(markdown || '').split('\n')) {
    const m = /^\|\s*(F\d+)\s*\|([^|]*)\|([^|]*)\|/.exec(line);
    if (m) features.push({id: m[1], name: m[2].trim(), status: m[3].trim()});
  }
  return features;
}

export function featuresTouched(baseMd, headMd) {
  const before = new Map(parseFeatures(baseMd).map((f) => [f.id, f]));
  const after = parseFeatures(headMd);
  const changed = after.filter((f) => before.get(f.id)?.status !== f.status).map((f) => ({...f, from: before.get(f.id)?.status ?? '(new)'}));
  if (changed.length) return {mode: 'changed', features: changed};
  return {mode: 'in-progress', features: after.filter((f) => /in-progress/i.test(f.status))};
}

// Commands from `run:` values (single-line or block) starting with `dotnet run` or `node tools/`.
export function workflowCommands(yaml) {
  const commands = [];
  const lines = String(yaml || '').split('\n');
  for (let i = 0; i < lines.length; i++) {
    const m = /^(\s*)(?:-\s+)?run:\s*(.*)$/.exec(lines[i]);
    if (!m) continue;
    const value = m[2].trim();
    const body = [];
    if (value === '|' || value === '>' || value === '|-' || value === '>-') {
      const indent = m[1].length;
      while (i + 1 < lines.length && (lines[i + 1].trim() === '' || lines[i + 1].search(/\S/) > indent)) body.push(lines[++i].trim());
    } else body.push(value);
    for (const cmd of body) if (/^(dotnet run\b|node tools\/)/.test(cmd) && !commands.includes(cmd)) commands.push(cmd);
  }
  return commands;
}

function gitIn(root) {
  return (args, {allowFail = false} = {}) => {
    try {
      return execFileSync('git', args, {cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']});
    } catch (error) {
      if (allowFail) return null;
      throw error;
    }
  };
}

function readVersion(git, ref) {
  const text = git(['show', `${ref}:GameContent/Unity/version.json`], {allowFail: true});
  if (!text) return null;
  try { return JSON.parse(text); } catch { return null; }
}

const fmtVersion = (v) => (v ? `${v.Version} · build ${v.BuildNumber} · ${v.Stage}` : '(no version.json)');

export function buildPlan(root, base, head) {
  const git = gitIn(root);
  const baseVersion = readVersion(git, base);
  const headVersion = readVersion(git, head);
  const commits = git(['log', '--oneline', `${base}..${head}`]).split('\n').filter(Boolean);
  const diff = git(['diff', '--name-only', `${base}...${head}`], {allowFail: true}) ?? git(['diff', '--name-only', base, head]);
  const paths = diff.split('\n').map((s) => s.trim()).filter(Boolean).sort();
  const groups = groupPaths(paths);
  const touched = featuresTouched(git(['show', `${base}:docs/Unity-Roadmap.md`], {allowFail: true}), git(['show', `${head}:docs/Unity-Roadmap.md`], {allowFail: true}));
  const commands = workflowCommands(git(['show', `${head}:.github/workflows/unity-pages.yml`], {allowFail: true}));

  const out = [];
  out.push(`# Unity test plan: ${base} → ${head}`, '');
  out.push('## Version', '');
  out.push(`- Base (\`${base}\`): ${fmtVersion(baseVersion)}`);
  out.push(`- Head (\`${head}\`): ${fmtVersion(headVersion)}`);
  if (baseVersion && headVersion && baseVersion.Version === headVersion.Version) out.push('- Note: the version did not change between these refs.');
  out.push('');
  out.push(`## Commits (${commits.length})`, '');
  if (commits.length) for (const c of commits) out.push(`- ${c}`);
  else out.push('- (none)');
  out.push('');
  out.push(`## Changed areas (${paths.length} files)`, '');
  let anyArea = false;
  for (const area of AREAS) {
    if (!groups[area].length) continue;
    anyArea = true;
    out.push(`### ${area} (${groups[area].length})`, '');
    for (const p of groups[area]) out.push(`- \`${p}\``);
    out.push('');
  }
  if (!anyArea) out.push('- (no files changed)', '');
  out.push('## Features touched', '');
  if (!touched.features.length) out.push('- (no roadmap feature rows changed or in progress)');
  else if (touched.mode === 'changed') for (const f of touched.features) out.push(`- ${f.id} ${f.name}: ${f.from} → ${f.status}`);
  else {
    out.push('No feature status changed; features in progress at head:', '');
    for (const f of touched.features) out.push(`- ${f.id} ${f.name}: ${f.status}`);
  }
  out.push('');
  out.push('## Automated checks', '');
  if (commands.length) {
    out.push('From `.github/workflows/unity-pages.yml` at head:', '');
    for (const c of commands) out.push(`- [ ] \`${c}\``);
  } else out.push('- (no `dotnet run` / `node tools/` steps found in .github/workflows/unity-pages.yml)');
  out.push('');
  out.push('## Manual smoke checklist', '');
  for (const item of SMOKE_CHECKLIST) out.push(`- [ ] ${item}`);
  out.push('');
  return out.join('\n');
}

function parseArgs(argv) {
  const opts = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!['--base', '--head', '--out', '--root'].includes(arg)) throw Error(`Unknown argument: ${arg}`);
    if (i + 1 >= argv.length) throw Error(`${arg} needs a value`);
    opts[arg.slice(2)] = argv[++i];
  }
  if (!opts.base || !opts.head) throw Error('usage: unity-test-plan.mjs --base <ref> --head <ref> [--out <file>]');
  return opts;
}

export function main(argv) {
  const opts = parseArgs(argv);
  const root = resolve(opts.root || fileURLToPath(new URL('..', import.meta.url)));
  const plan = buildPlan(root, opts.base, opts.head);
  if (opts.out) {
    writeFileSync(resolve(opts.out), plan);
    console.log(`Wrote test plan to ${opts.out}`);
  } else process.stdout.write(plan);
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(`unity-test-plan: ${error.message.split('\n')[0]}`);
    process.exitCode = 2;
  }
}
