// Tests for tools/unity-version.mjs. Run: node --test tools/unity-version.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync, execFileSync} from 'node:child_process';
import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {bumpVersion, stepKind, compareVersions, isGameplayPath, flipRoadmapFeature, prependChangelog, changelogEntry} from './unity-version.mjs';

const TOOL = fileURLToPath(new URL('./unity-version.mjs', import.meta.url));
const ROADMAP = '# Roadmap\n\n| ID | Feature | Status | Notes |\n|----|---------|--------|-------|\n| F01 | Map | done (0.1.0.0) | x |\n| F02 | Combat | in-progress | y |\n| F03 | Shops | todo | z |\n';

function fixture({version = '0.0.14.0', build = 14, roadmap = ROADMAP, git = false} = {}) {
  const root = mkdtempSync(join(tmpdir(), 'unity-version-'));
  mkdirSync(join(root, 'GameContent/Unity'), {recursive: true});
  mkdirSync(join(root, 'docs'), {recursive: true});
  writeVersion(root, version, build);
  if (roadmap) writeFileSync(join(root, 'docs/Unity-Roadmap.md'), roadmap);
  writeFileSync(join(root, 'CHANGELOG.md'), '# HTML changelog\n');
  if (git) {
    const g = (...args) => execFileSync('git', args, {cwd: root, stdio: 'pipe'});
    g('init', '-q', '-b', 'main');
    g('config', 'user.email', 't@example.com');
    g('config', 'user.name', 'Test');
    g('config', 'commit.gpgsign', 'false');
    g('add', '-A');
    g('commit', '-q', '-m', 'base');
    g('tag', 'base');
  }
  return root;
}
function writeVersion(root, version, build, stage = 'Foundation in progress') {
  writeFileSync(join(root, 'GameContent/Unity/version.json'), JSON.stringify({Version: version, BuildNumber: build, Stage: stage}, null, 2) + '\n');
}
function commitAll(root, message = 'change') {
  execFileSync('git', ['add', '-A'], {cwd: root});
  execFileSync('git', ['commit', '-q', '-m', message], {cwd: root});
}
function touch(root, path, text = 'x\n') {
  mkdirSync(join(root, path, '..'), {recursive: true});
  writeFileSync(join(root, path), text);
}
function run(root, ...args) {
  const r = spawnSync(process.execPath, [TOOL, ...args, '--root', root], {encoding: 'utf8'});
  return {code: r.status, out: r.stdout, err: r.stderr};
}
const readJson = (root) => readFileSync(join(root, 'GameContent/Unity/version.json'), 'utf8');

test('pure bump arithmetic for every kind', () => {
  assert.equal(bumpVersion('0.0.14.0', 'patch'), '0.0.14.1');
  assert.equal(bumpVersion('0.0.14.3', 'story'), '0.0.15.0');
  assert.equal(bumpVersion('0.0.14.3', 'feature'), '0.1.0.0');
  assert.equal(bumpVersion('0.4.2.1', 'release'), '1.0.0.0');
  assert.throws(() => bumpVersion('0.0.14', 'patch'));
  assert.equal(compareVersions('0.0.10.0', '0.0.9.9'), 1);
});

test('legal single steps and illegal jumps', () => {
  assert.equal(stepKind('0.0.14.0', '0.0.15.0'), 'story');
  assert.equal(stepKind('0.0.14.0', '0.0.14.1'), 'patch');
  assert.equal(stepKind('0.0.14.0', '0.1.0.0'), 'feature');
  assert.equal(stepKind('0.0.14.0', '1.0.0.0'), 'release');
  assert.equal(stepKind('0.0.14.0', '0.0.16.0'), null);
  assert.equal(stepKind('0.0.14.0', '0.1.0.3'), null);
  assert.equal(stepKind('0.0.14.0', '0.0.14.2'), null);
});

test('gameplay path classification excludes version.json', () => {
  assert.ok(isGameplayPath('Unity/Assets/AshenSpire/Runtime/Domain/X.cs'));
  assert.ok(isGameplayPath('Unity/ProjectSettings/ProjectSettings.asset'));
  assert.ok(isGameplayPath('GameContent/Unity/campaign.json'));
  assert.ok(!isGameplayPath('GameContent/Unity/version.json'));
  assert.ok(!isGameplayPath('docs/Unity-Roadmap.md'));
  assert.ok(!isGameplayPath('tools/unity-version.mjs'));
});

for (const [kind, expected] of [['patch', '0.0.14.1'], ['story', '0.0.15.0'], ['feature', '0.1.0.0']]) {
  test(`bump ${kind} writes version.json with build +1 and preserved format`, () => {
    const root = fixture();
    const r = run(root, 'bump', kind, '--date', '2026-09-24');
    assert.equal(r.code, 0, r.err);
    assert.equal(readJson(root), `{\n  "Version": "${expected}",\n  "BuildNumber": 15,\n  "Stage": "Foundation in progress"\n}\n`);
    assert.equal(readFileSync(join(root, 'CHANGELOG.md'), 'utf8'), '# HTML changelog\n', 'root CHANGELOG.md untouched');
    rmSync(root, {recursive: true, force: true});
  });
}

test('bump release refuses without --owner-approved and succeeds with it', () => {
  const root = fixture({version: '0.7.2.1', build: 40});
  const refused = run(root, 'bump', 'release');
  assert.notEqual(refused.code, 0);
  assert.match(refused.err, /owner-only/);
  assert.match(readJson(root), /"0\.7\.2\.1"/);
  assert.ok(!existsSync(join(root, 'docs/Unity-Changelog.md')));
  const approved = run(root, 'bump', 'release', '--owner-approved', '--stage', 'Released');
  assert.equal(approved.code, 0, approved.err);
  assert.deepEqual(JSON.parse(readJson(root)), {Version: '1.0.0.0', BuildNumber: 41, Stage: 'Released'});
  rmSync(root, {recursive: true, force: true});
});

test('--stage updates Stage and --dry-run writes nothing', () => {
  const root = fixture();
  const before = readJson(root);
  const dry = run(root, 'bump', 'patch', '--stage', 'Combat polish', '--dry-run');
  assert.equal(dry.code, 0, dry.err);
  assert.match(dry.out, /0\.0\.14\.1/);
  assert.match(dry.out, /Combat polish/);
  assert.equal(readJson(root), before);
  assert.ok(!existsSync(join(root, 'docs/Unity-Changelog.md')));
  run(root, 'bump', 'patch', '--stage', 'Combat polish');
  assert.equal(JSON.parse(readJson(root)).Stage, 'Combat polish');
  rmSync(root, {recursive: true, force: true});
});

test('changelog is created with a header, then new entries are prepended', () => {
  const root = fixture();
  run(root, 'bump', 'patch', '--date', '2026-09-01', '--note', 'Fix A', '--note', 'Fix B');
  let log = readFileSync(join(root, 'docs/Unity-Changelog.md'), 'utf8');
  assert.match(log, /^# AshenSpire Unity changelog/);
  assert.match(log, /## 0\.0\.14\.1 · build 15 · 2026-09-01\n\n- Fix A\n- Fix B\n/);
  run(root, 'bump', 'story', '--date', '2026-09-02', '--note', 'Story done');
  log = readFileSync(join(root, 'docs/Unity-Changelog.md'), 'utf8');
  const newer = log.indexOf('## 0.0.15.0 · build 16 · 2026-09-02');
  const older = log.indexOf('## 0.0.14.1 · build 15');
  assert.ok(newer > 0 && older > newer, 'newest entry first');
  assert.equal(log.indexOf('# AshenSpire Unity changelog'), 0, 'header kept on top');
  rmSync(root, {recursive: true, force: true});
});

test('prependChangelog handles a header-only file', () => {
  const out = prependChangelog('# Log\n', changelogEntry('0.0.1.0', 2, '2026-01-01', ['n']));
  assert.equal(out, '# Log\n\n## 0.0.1.0 · build 2 · 2026-01-01\n\n- n\n');
});

test('bump feature --feature flips the roadmap status cell', () => {
  const root = fixture();
  const r = run(root, 'bump', 'feature', '--feature', 'F02');
  assert.equal(r.code, 0, r.err);
  const roadmap = readFileSync(join(root, 'docs/Unity-Roadmap.md'), 'utf8');
  assert.match(roadmap, /^\| F02 \| Combat \| done \(0\.1\.0\.0\) \| y \|$/m);
  assert.match(roadmap, /^\| F03 \| Shops \| todo \| z \|$/m, 'other rows untouched');
  rmSync(root, {recursive: true, force: true});
});

test('roadmap flip is a warning no-op when the row is missing or already done', () => {
  const root = fixture();
  const r = run(root, 'bump', 'feature', '--feature', 'F09');
  assert.equal(r.code, 0, r.err);
  assert.match(r.err, /warning/);
  assert.equal(readFileSync(join(root, 'docs/Unity-Roadmap.md'), 'utf8'), ROADMAP);
  assert.equal(flipRoadmapFeature(ROADMAP, 'F01', '0.2.0.0').changed, false);
  assert.equal(flipRoadmapFeature(ROADMAP, 'F03', '0.2.0.0').changed, true);
  rmSync(root, {recursive: true, force: true});
});

test('check: no changes passes with a counted verdict', () => {
  const root = fixture({git: true});
  const r = run(root, 'check', '--base', 'base');
  assert.equal(r.code, 0, r.err);
  assert.match(r.out, /^unity-version: OK — \d+ checks passed\.$/m);
  rmSync(root, {recursive: true, force: true});
});

test('check: docs-only diff passes without a bump', () => {
  const root = fixture({git: true});
  touch(root, 'docs/notes.md');
  touch(root, 'tools/helper.mjs');
  commitAll(root);
  const r = run(root, 'check', '--base', 'base');
  assert.equal(r.code, 0, r.err);
  rmSync(root, {recursive: true, force: true});
});

test('check: gameplay diff without a bump fails', () => {
  const root = fixture({git: true});
  touch(root, 'Unity/Assets/AshenSpire/Runtime/Domain/Combat.cs');
  commitAll(root);
  const r = run(root, 'check', '--base', 'base');
  assert.equal(r.code, 1);
  assert.match(r.err, /not greater than base/);
  assert.doesNotMatch(r.out, /OK —/);
  rmSync(root, {recursive: true, force: true});
});

test('check: uncommitted gameplay edits count too', () => {
  const root = fixture({git: true});
  touch(root, 'GameContent/Unity/campaign.json', '{}\n');
  commitAll(root);
  writeFileSync(join(root, 'GameContent/Unity/campaign.json'), '{"a":1}\n');
  const r = run(root, 'check', '--base', 'HEAD');
  assert.equal(r.code, 1);
  rmSync(root, {recursive: true, force: true});
});

for (const [version, build, code] of [
  ['0.0.15.0', 15, 0], ['0.0.14.1', 15, 0], ['0.1.0.0', 15, 0],
  ['0.0.16.0', 15, 1], ['0.1.0.3', 15, 1], ['0.0.15.0', 14, 1], ['0.0.13.0', 15, 1],
]) {
  test(`check: gameplay diff with 0.0.14.0/14 -> ${version}/${build} exits ${code}`, () => {
    const root = fixture({git: true});
    touch(root, 'Unity/Packages/manifest.json', '{}\n');
    writeVersion(root, version, build);
    commitAll(root);
    const r = run(root, 'check', '--base', 'base');
    assert.equal(r.code, code, r.out + r.err);
    rmSync(root, {recursive: true, force: true});
  });
}

test('check: works through verdict.mjs', () => {
  const root = fixture({git: true});
  touch(root, 'Unity/Assets/X.cs');
  writeVersion(root, '0.0.14.1', 15);
  commitAll(root);
  const verdict = fileURLToPath(new URL('./verdict.mjs', import.meta.url));
  const r = spawnSync(process.execPath, [verdict, '--', process.execPath, TOOL, 'check', '--base', 'base', '--root', root], {encoding: 'utf8'});
  assert.equal(r.status, 0, r.stdout + r.stderr);
  rmSync(root, {recursive: true, force: true});
});

test('show prints version, build and stage', () => {
  const root = fixture();
  const r = run(root, 'show');
  assert.equal(r.code, 0);
  assert.match(r.out, /Version: 0\.0\.14\.0\nBuildNumber: 14\nStage: Foundation in progress/);
  rmSync(root, {recursive: true, force: true});
});
