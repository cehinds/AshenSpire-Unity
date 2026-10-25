#!/usr/bin/env node
// Capture the Unity WebGL player for the parity harness (DEVELOPER.md, "Parity harness").
//
//   node tools/unity-parity-capture.mjs <player-url> [--out DIR] [--only REGEX] [--views desktop,portrait]
//
// <player-url> serves a Unity Web build (Published/Web, or a local build), for example
//   python3 -m http.server 8787 --bind 127.0.0.1 --directory Published/Web
// It follows the native harness conventions (tools/native-ui-driver.cjs): real pointer and keyboard input
// on the controls the player reports in development (loopback or /dev/ URLs); no game state is written.
// For every screen in tools/parity-screens.json whose `unity` is not null it opens a fresh browser context
// at the view size (desktop 1280x720, portrait 390x844 with touch), replays `unity.steps`, and writes
// <view>-<id>.png plus <view>-<id>.json (each reported control's rect in page pixels, keyed by control id),
// the same names as tools/js-reference-capture.mjs. manifest.json records the player's build-source.json.
import {mkdirSync, writeFileSync} from 'node:fs';
import {join, resolve, dirname} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {createRequire} from 'node:module';
import {loadScreens, newManifest, readManifest, writeManifest, upsertShot, shotFile, domFile} from './parity-harness.mjs';

const require = createRequire(import.meta.url);
const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/** Replay one screen's steps with the native driver. Step vocabulary: DEVELOPER.md, "Parity harness". */
export async function runSteps(ui, steps) {
  const page = ui.page;
  const enabled = () => ui.controls?.Controls.filter((c) => c.Enabled) || [];
  const present = async (id, ms = 1500) => { const end = Date.now() + ms; while (Date.now() < end) { if (ui.has(id)) return true; await page.waitForTimeout(100); } return ui.has(id); };
  for (const step of steps) {
    for (let n = 0; n < (step.repeat || 1); n++) {
      if ('wait' in step) await page.waitForTimeout(step.wait);
      else if (step.standard) await ui.useStandard();
      else if (step.fill) await ui.fill(step.fill[0], step.fill[1]);
      else if (step.key) await ui.key(step.key);
      else {
        let id = step.click;
        if (step.match) {
          const re = new RegExp(step.match);
          const found = enabled().filter((c) => re.test(c.Id))[step.index || 0];
          if (!found) { if (step.ifPresent) break; throw Error('no enabled control matches ' + step.match); }
          id = found.Id;
        }
        if (step.ifPresent && !(await present(id))) break;
        if (step.command) await ui.command(id); else await ui.click(id);
      }
    }
  }
  await page.waitForTimeout(800);
  await ui.frames();
}

/** The controls report as a parity dump: rects in page pixels, keyed by control id, in report order. */
export function controlsDump(report, canvas, view, id) {
  const sx = canvas.width / report.PanelWidth, sy = canvas.height / report.PanelHeight, elements = {};
  const r1 = (n) => Math.round(n * 10) / 10;
  for (const c of report.Controls) {
    (elements[c.Id] ||= []).push({rect: [r1(canvas.x + c.X * sx), r1(canvas.y + c.Y * sy), r1(c.Width * sx), r1(c.Height * sy)], text: (c.Text || '').trim().replace(/\s+/g, ' ').slice(0, 80), enabled: !!c.Enabled});
  }
  return {side: 'unity', id, view: view.id, viewport: {width: view.width, height: view.height}, panel: {width: report.PanelWidth, height: report.PanelHeight}, rootClasses: report.RootClasses || [], labels: (report.Labels || []).slice(0, 120), elements};
}

function parseArgs(argv) {
  const opts = {url: null, out: 'TestResults/Parity/unity-web', only: null, views: null};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out') opts.out = argv[++i];
    else if (a === '--only') opts.only = new RegExp(argv[++i]);
    else if (a === '--views') opts.views = argv[++i].split(',');
    else if (!a.startsWith('--') && !opts.url) opts.url = a;
    else throw Error('unknown argument ' + a);
  }
  if (!opts.url) throw Error('usage: unity-parity-capture.mjs <player-url> [--out DIR] [--only REGEX] [--views desktop,portrait]');
  return opts;
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  process.env.ASHENSPIRE_REPO_ROOT ||= repo;
  const {NativeUiDriver} = require('./native-ui-driver.cjs');
  const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
  const screens = loadScreens();
  const out = resolve(opts.out);
  mkdirSync(out, {recursive: true});
  const stamp = await (await fetch(new URL('build-source.json', opts.url))).json();
  const source = {repo: 'AshenSpire-Unity (WebGL player)', url: opts.url, version: stamp.version ?? null, buildNumber: stamp.buildNumber ?? null, sourceDigest: stamp.sourceDigest ?? null, builtAt: stamp.builtAt ?? null};
  let manifest = opts.only ? readManifest(out) : null;
  if (!manifest || manifest.source.sourceDigest !== source.sourceDigest) manifest = newManifest('unity', 'tools/unity-parity-capture.mjs', source, screens.views);
  manifest.source = source;
  const browser = await chromium.launch({headless: true, ...(process.platform === 'win32' ? {channel: 'msedge'} : {}), args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader']});
  let failed = 0;
  try {
    for (const view of screens.views.filter((v) => !opts.views || opts.views.includes(v.id))) for (const screen of screens.screens) {
      if (screen.unity === null || (opts.only && !opts.only.test(screen.id))) continue;
      const ctx = await browser.newContext({viewport: {width: view.width, height: view.height}, deviceScaleFactor: 1, hasTouch: !!view.touch});
      const page = await ctx.newPage();
      const ui = new NativeUiDriver(page, join(out, '.driver', view.id + '-' + screen.id));
      const entry = {id: screen.id, view: view.id, png: shotFile(view.id, screen.id), dom: null, status: 'ok'};
      try {
        await ui.open(opts.url);
        await runSteps(ui, screen.unity.steps);
        await page.screenshot({path: join(out, entry.png)});
        const canvas = await page.locator('#unity-canvas').boundingBox();
        if (ui.controls && canvas) {
          writeFileSync(join(out, domFile(view.id, screen.id)), JSON.stringify(controlsDump(ui.controls, canvas, view, screen.id), null, 1) + '\n');
          entry.dom = domFile(view.id, screen.id);
        }
      } catch (e) {
        failed++;
        entry.status = 'failed';
        entry.error = e.message.split('\n')[0];
        entry.png = null;
        await page.screenshot({path: join(out, '.driver', `${view.id}-${screen.id}.FAILED.png`)}).catch(() => {});
      }
      if (ui.errors.length) entry.pageErrors = ui.errors.slice(0, 3).map((e) => String(e).split('\n')[0]);
      upsertShot(manifest, entry, screens);
      writeManifest(out, manifest);
      console.log(`${entry.status === 'ok' ? 'OK  ' : 'FAIL'} ${view.id}-${screen.id}${entry.error ? ': ' + entry.error : ''}`);
      await ctx.close();
    }
  } finally {
    await browser.close();
  }
  const ok = manifest.shots.filter((s) => s.status === 'ok').length;
  console.log(`unity-parity-capture: ${ok}/${manifest.shots.length} shots in ${out} (player ${source.version}, build ${source.buildNumber})`);
  if (failed) process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  main().catch((e) => { console.error('unity-parity-capture: ' + e.message); process.exit(1); });
}
