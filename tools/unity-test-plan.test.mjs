// Tests for tools/unity-test-plan.mjs. Run: node --test tools/unity-test-plan.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync, execFileSync} from 'node:child_process';
import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {classifyPath, parseFeatures, featuresTouched, workflowCommands, SMOKE_CHECKLIST} from './unity-test-plan.mjs';

const TOOL = fileURLToPath(new URL('./unity-test-plan.mjs', import.meta.url));
const WORKFLOW = `name: x
jobs:
  build:
    steps:
      - name: Domain
        run: dotnet run --project UnityTests/Domain
      - name: Block
        run: |
          dotnet run --project UnityTests/Parity
          python3 UnityTests/Parity/authoring-checks.py
          node tools/control-report.test.cjs
      - name: Digest
        run: node --test tools/unity-source-digest.test.mjs
      - name: Package
        run: node tools/unity-package.mjs --check
      - uses: actions/upload-artifact@v4
`;
const roadmap = (f2, f3) => `# Roadmap\n\n| ID | Feature | Status |\n|---|---|---|\n| F01 | Map | done (0.1.0.0) |\n| F02 | Combat | ${f2} |\n| F03 | Shops | ${f3} |\n`;

function write(root, path, text) {
  mkdirSync(dirname(join(root, path)), {recursive: true});
  writeFileSync(join(root, path), text);
}
function repo() {
  const root = mkdtempSync(join(tmpdir(), 'unity-test-plan-'));
  const g = (...args) => execFileSync('git', args, {cwd: root, stdio: 'pipe'});
  g('init', '-q', '-b', 'main');
  g('config', 'user.email', 't@example.com');
  g('config', 'user.name', 'Test');
  g('config', 'commit.gpgsign', 'false');
  write(root, 'GameContent/Unity/version.json', JSON.stringify({Version: '0.0.14.0', BuildNumber: 14, Stage: 'Foundation in progress'}, null, 2) + '\n');
  write(root, 'docs/Unity-Roadmap.md', roadmap('in-progress', 'todo'));
  write(root, '.github/workflows/unity-pages.yml', WORKFLOW);
  g('add', '-A'); g('commit', '-q', '-m', 'base'); g('tag', 'base');
  write(root, 'Unity/Assets/AshenSpire/Runtime/Domain/Combat.cs', 'class C {}\n');
  write(root, 'GameContent/Unity/version.json', JSON.stringify({Version: '0.1.0.0', BuildNumber: 15, Stage: 'Foundation in progress'}, null, 2) + '\n');
  write(root, 'docs/Unity-Roadmap.md', roadmap('done (0.1.0.0)', 'todo'));
  g('add', '-A'); g('commit', '-q', '-m', 'Finish combat feature');
  write(root, 'Unity/Assets/AshenSpire/Runtime/Presentation/Hud.cs', 'class H {}\n');
  write(root, 'tools/new-tool.mjs', '\n');
  g('add', '-A'); g('commit', '-q', '-m', 'HUD polish');
  return root;
}

test('classifies paths into areas', () => {
  assert.equal(classifyPath('Unity/Assets/AshenSpire/Runtime/Domain/X.cs'), 'Domain');
  assert.equal(classifyPath('UnityTests/Domain/Program.cs'), 'Domain');
  assert.equal(classifyPath('Unity/Assets/AshenSpire/Runtime/Presentation/X.cs'), 'Presentation');
  assert.equal(classifyPath('Unity/ProjectSettings/ProjectSettings.asset'), 'Presentation');
  assert.equal(classifyPath('GameContent/Unity/campaign.json'), 'Content');
  assert.equal(classifyPath('tools/unity-version.mjs'), 'Tools/CI');
  assert.equal(classifyPath('.github/workflows/unity-pages.yml'), 'Tools/CI');
  assert.equal(classifyPath('docs/Unity-Roadmap.md'), 'Docs');
  assert.equal(classifyPath('README.md'), 'Docs');
  assert.equal(classifyPath('package.json'), 'Other');
});

test('parses roadmap features and detects status changes', () => {
  assert.deepEqual(parseFeatures(roadmap('todo', 'todo')).map((f) => f.id), ['F01', 'F02', 'F03']);
  const changed = featuresTouched(roadmap('in-progress', 'todo'), roadmap('done (0.1.0.0)', 'todo'));
  assert.equal(changed.mode, 'changed');
  assert.deepEqual(changed.features.map((f) => [f.id, f.from, f.status]), [['F02', 'in-progress', 'done (0.1.0.0)']]);
  const same = featuresTouched(roadmap('in-progress', 'todo'), roadmap('in-progress', 'todo'));
  assert.equal(same.mode, 'in-progress');
  assert.deepEqual(same.features.map((f) => f.id), ['F02']);
  assert.equal(featuresTouched(null, null).features.length, 0);
});

test('extracts dotnet run / node tools/ commands from single-line and block run steps', () => {
  assert.deepEqual(workflowCommands(WORKFLOW), [
    'dotnet run --project UnityTests/Domain',
    'dotnet run --project UnityTests/Parity',
    'node tools/control-report.test.cjs',
    'node tools/unity-package.mjs --check',
  ]);
});

test('reads the real unity-pages workflow', () => {
  const yaml = readFileSync(fileURLToPath(new URL('../.github/workflows/unity-pages.yml', import.meta.url)), 'utf8');
  const commands = workflowCommands(yaml);
  assert.ok(commands.includes('dotnet run --project UnityTests/Domain'));
  assert.ok(commands.includes('node tools/unity-package.mjs --check'));
  assert.ok(commands.every((c) => /^(dotnet run|node tools\/)/.test(c)));
});

test('generates a full plan from a git range and writes --out', () => {
  const root = repo();
  const out = join(root, 'plan.md');
  const r = spawnSync(process.execPath, [TOOL, '--base', 'base', '--head', 'HEAD', '--out', out, '--root', root], {encoding: 'utf8'});
  assert.equal(r.status, 0, r.stderr);
  const plan = readFileSync(out, 'utf8');
  assert.match(plan, /Base \(`base`\): 0\.0\.14\.0 · build 14/);
  assert.match(plan, /Head \(`HEAD`\): 0\.1\.0\.0 · build 15/);
  assert.match(plan, /## Commits \(2\)/);
  assert.match(plan, /Finish combat feature/);
  assert.match(plan, /HUD polish/);
  assert.match(plan, /### Domain \(1\)\n\n- `Unity\/Assets\/AshenSpire\/Runtime\/Domain\/Combat\.cs`/);
  assert.match(plan, /### Presentation \(1\)/);
  assert.match(plan, /### Content \(1\)/);
  assert.match(plan, /### Tools\/CI \(1\)/);
  assert.match(plan, /### Docs \(1\)/);
  assert.match(plan, /F02 Combat: in-progress → done \(0\.1\.0\.0\)/);
  assert.match(plan, /- \[ \] `dotnet run --project UnityTests\/Domain`/);
  assert.doesNotMatch(plan, /python3/);
  for (const item of SMOKE_CHECKLIST) assert.ok(plan.includes(`- [ ] ${item}`), item);
  rmSync(root, {recursive: true, force: true});
});

test('prints to stdout without --out and refuses missing refs', () => {
  const root = repo();
  const r = spawnSync(process.execPath, [TOOL, '--base', 'base', '--head', 'base', '--root', root], {encoding: 'utf8'});
  assert.equal(r.status, 0, r.stderr);
  assert.match(r.stdout, /## Commits \(0\)/);
  assert.match(r.stdout, /version did not change/);
  assert.match(r.stdout, /F02 Combat: in-progress/);
  const bad = spawnSync(process.execPath, [TOOL, '--base', 'base', '--root', root], {encoding: 'utf8'});
  assert.equal(bad.status, 2);
  rmSync(root, {recursive: true, force: true});
});
