#!/usr/bin/env node
// Capture the HTML/JS reference screens for the Unity port (DEVELOPER.md, "Parity harness").
//
//   node tools/js-reference-capture.mjs <js-checkout> [--out DIR] [--only REGEX] [--views desktop,portrait]
//        [--port 8765] [--url http://127.0.0.1:8765/] [--no-dom]
//
// READ-ONLY against the JS checkout: it is served by its own tools/serve.mjs (no build step) and nothing is
// written into it; `git status --porcelain` is compared before and after and the run fails if it changed.
// Default game profile only (Core 2); `?gameProfile=` is never passed. Every screen id in
// tools/parity-screens.json is captured at each view (desktop 1280x720 mouse, portrait 390x844 touch) at
// deviceScaleFactor 1 as <view>-<id>.png, with <view>-<id>.json holding the rects and computed styles of the
// screen's key elements. manifest.json records the JS commit and the result of every shot; --only merges into
// an existing manifest. Routes are the game's own ?shot= dev hooks plus the real flows from a fresh profile.
// --url captures an already-running server instead of starting one (the checkout is still read for the commit).
import {spawn, execFileSync} from 'node:child_process';
import {existsSync, readFileSync, mkdirSync, writeFileSync} from 'node:fs';
import {join, resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {createRequire} from 'node:module';
import {loadScreens, newManifest, readManifest, writeManifest, upsertShot, shotFile, domFile} from './parity-harness.mjs';

const W = (p, ms) => p.waitForTimeout(ms);
const btn = (p, name, exact = false) => p.getByRole('button', {name, exact}).first().click({timeout: 8000});

/** Navigation helpers; `base` is the served root URL. Ported unchanged from the 1837 reference capture. */
function routes(base) {
  const boot = async (p) => { await p.goto(base); await W(p, 3500); await p.locator('.startup-gate').first().click(); await W(p, 3000); };
  const newGame = async (p) => { await boot(p); await btn(p, 'New Game'); await W(p, 1500); };
  const creation = async (p) => { await newGame(p); await btn(p, 'Create character'); await W(p, 1500); await btn(p, 'Start', true); await W(p, 3500); };
  const shot = async (p, q, ms = 5000) => { await p.goto(base + '?shot=' + q); await W(p, ms); };
  const railOrSelect = async (p, label) => {
    const toggle = p.locator('.as-catnav-toggle:visible').first();
    if (await toggle.count()) { await toggle.click(); await W(p, 800); }
    const sel = p.locator('select:visible').first();
    if (await sel.count()) { try { await sel.selectOption({label}, {timeout: 3000}); return; } catch { /* fall through to the rail */ } }
    await p.getByText(label, {exact: true}).first().click({timeout: 5000});
  };
  const selectCard = async (p, i = 4) => { await p.locator(`.hand .card >> nth=${i}`).click({timeout: 5000}); await W(p, 1200); };
  const clickMapNode = async (p) => {
    const box = await p.locator('.map-node.reachable').first().boundingBox();
    await p.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await W(p, 1500);
  };
  // Debug mode (Settings > Advanced > Import, export & debug > Diagnostics) from the combat Quick menu.
  // A ?shot= boot uses memory storage, so this is redone per page.
  const openCombatSettings = async (p) => {
    await shot(p, 'combat', 6000);
    await p.locator('[aria-label="Quick menu"]').first().click(); await W(p, 1200);
    await p.locator('.qn-row:has-text("Settings")').first().click(); await W(p, 2500);
  };
  const debugRow = async (p) => {
    await openCombatSettings(p);
    await p.locator('.modal .set-tab:has-text("Advanced")').first().click(); await W(p, 1500);
    await p.locator('.modal select').first().selectOption({label: 'Import, export & debug'}); await W(p, 1500);
    await p.locator('.modal select').nth(1).selectOption({label: 'Diagnostics'}); await W(p, 1500);
  };
  const enableDebug = async (p) => {
    await debugRow(p);
    await p.locator('.as-toggle[aria-label="Debug mode"], .as-toggle:near(:text("Debug mode"))').first().click(); await W(p, 1000);
    await p.locator('.modal-close').first().click(); await W(p, 2000);
  };
  const firstFight = async (p) => {
    await creation(p); await btn(p, 'Next', true); await W(p, 2000); await btn(p, 'Begin'); await W(p, 5000);
    await btn(p, 'Skip opening'); await W(p, 4000); await clickMapNode(p); await p.locator('.map-enter').first().click(); await W(p, 8000);
  };
  return {
    '01-title': async (p) => { await boot(p); await p.locator('.tm-entry').first().waitFor(); await W(p, 6000); },
    '01b-startup-gate': async (p) => { await p.goto(base); await W(p, 4000); },
    '01c-title-with-continue': async (p) => { await shot(p, 'title', 8000); },
    '02a-newgame-slot': async (p) => { await newGame(p); },
    '02b-newgame-confirm': async (p) => { await newGame(p); await btn(p, 'Create character'); await W(p, 1500); },
    '02c-create-class': async (p) => { await creation(p); },
    '02d-create-class-grid': async (p) => { await creation(p); await p.getByText('Grid', {exact: true}).first().click({timeout: 8000}); await W(p, 1500); },
    '02e-create-review': async (p) => { await creation(p); await btn(p, 'Next', true); await W(p, 2500); },
    '02f-opening-prologue': async (p) => { await creation(p); await btn(p, 'Next', true); await W(p, 2000); await btn(p, 'Begin'); await W(p, 5000); },
    '03-map': async (p) => { await shot(p, 'map'); },
    '03b-map-node-selected': async (p) => { await shot(p, 'map'); await clickMapNode(p); },
    '03c-map-quick-menu': async (p) => { await shot(p, 'map'); await p.locator('[aria-label="Quick menu"]').first().click(); await W(p, 1500); },
    '04-combat': async (p) => { await shot(p, 'combat', 6000); },
    '04b-combat-card-selected': async (p) => { await shot(p, 'combat', 6000); await selectCard(p); },
    '04c-combat-draw-pile': async (p) => { await shot(p, 'combat', 6000); await p.locator('.pile.draw').first().click(); await W(p, 2000); },
    '04d-combat-first-fight-tutorial': firstFight,
    '04e-combat-log': async (p) => { await shot(p, 'combat', 6000); await btn(p, 'Combat log', true); await W(p, 2000); },
    '04f-combat-debug-button': async (p) => { await enableDebug(p); },
    '04g-combat-debug-console': async (p) => { await enableDebug(p); await p.locator('.combat-debug-console').click(); await W(p, 2000); },
    '04h-combat-log-with-console': async (p) => { await enableDebug(p); await btn(p, 'Combat log', true); await W(p, 2000); },
    '05-card-zoom': async (p) => { await shot(p, 'combat', 6000); await selectCard(p); await p.locator('.card-info-button >> nth=4').click({force: true}); await W(p, 2000); },
    '06-rewards': async (p) => { await shot(p, 'reward'); },
    '06b-reward-card-pick': async (p) => { await shot(p, 'reward'); await p.getByText('Choose one of 3.').first().click(); await W(p, 2500); },
    '07a-deck': async (p) => { await shot(p, 'map'); await p.keyboard.press('d'); await W(p, 2500); },
    '07b-equipment': async (p) => { await shot(p, 'map'); await p.keyboard.press('r'); await W(p, 2500); },
    '07c-inventory': async (p) => { await shot(p, 'map'); await p.keyboard.press('r'); await W(p, 2000); await railOrSelect(p, 'Inventory'); await W(p, 2000); },
    '07d-character': async (p) => { await shot(p, 'map'); await p.keyboard.press('t'); await W(p, 2500); },
    '07e-edit-deck': async (p) => { await shot(p, 'map'); await p.keyboard.press('d'); await W(p, 2000); await p.getByRole('tab', {name: 'Edit Deck'}).or(p.getByRole('button', {name: 'Edit Deck', exact: true})).first().click(); await W(p, 2000); },
    '08a-rest-shrine': async (p) => { await shot(p, 'rest'); },
    '08b-rest-services': async (p) => { await shot(p, 'rest'); await p.locator('.modal-body.choice-body').first().evaluate((e) => { e.scrollTop = e.scrollHeight; }); await W(p, 1200); },
    '08c-merchant': async (p) => { await shot(p, 'shop'); },
    '08d-merchant-combat-cards': async (p) => { await shot(p, 'shop'); await btn(p, 'Combat cards'); await W(p, 2000); },
    '08e-event-dialogue': async (p) => { await shot(p, 'event'); },
    '08f-event-choices': async (p) => { await shot(p, 'event'); await btn(p, 'Continue', true); await W(p, 2000); },
    '08g-blacksmith-shot': async (p) => { await shot(p, 'blacksmith'); },
    '09a-settings-general': async (p) => { await boot(p); await btn(p, 'Settings'); await W(p, 2500); },
    '09b-settings-accessibility': async (p) => { await boot(p); await btn(p, 'Settings'); await W(p, 2000); await p.getByText('Accessibility', {exact: true}).first().click({timeout: 8000}); await W(p, 2000); },
    '09c-settings-advanced': async (p) => { await shot(p, 'settings'); },
    '09d-settings-debug-mode': async (p) => { await debugRow(p); await p.locator('.modal :text("Debug mode")').first().scrollIntoViewIfNeeded(); await W(p, 800); },
    '09e-settings-combat-log': async (p) => {
      await openCombatSettings(p); await p.locator('.modal .set-tab:has-text("General")').first().click(); await W(p, 1500);
      await p.locator('.modal select').first().selectOption({label: 'Combat'}); await W(p, 1500);
      await p.locator('.modal select').nth(1).selectOption({label: 'Combat log'}); await W(p, 800);
      await p.mouse.move(640, 5); await p.locator('.modal select').nth(1).focus(); await W(p, 1500);
    },
  };
}

/** The screen ids this script knows how to reach (the node test checks they match tools/parity-screens.json). */
export const ROUTE_IDS = Object.keys(routes('http://x/'));

/** Runs in the page: rects and computed styles of the key selectors plus every visible button. */
function dumpDom(selectors) {
  const props = ['color', 'backgroundColor', 'backgroundImage', 'borderTopColor', 'borderTopWidth', 'borderRadius', 'fontFamily', 'fontSize', 'fontWeight', 'letterSpacing', 'lineHeight', 'textTransform', 'textShadow', 'boxShadow', 'opacity', 'padding'];
  const visible = (e) => { const r = e.getBoundingClientRect(), cs = getComputedStyle(e); return r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none'; };
  const describe = (e) => {
    const r = e.getBoundingClientRect(), cs = getComputedStyle(e), style = {};
    for (const k of props) {
      let v = cs[k];
      if (k === 'backgroundImage' && v.length > 200) v = v.slice(0, 200) + '…';
      if (v && v !== 'none' && v !== 'normal' && v !== '0px' && v !== 'rgba(0, 0, 0, 0)') style[k] = v;
    }
    return {rect: [r.x, r.y, r.width, r.height].map((n) => Math.round(n * 10) / 10), text: (e.innerText || e.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 80), style};
  };
  const elements = {};
  for (const sel of selectors) {
    let list = [];
    try { list = [...document.querySelectorAll(sel)].filter(visible).slice(0, 16); } catch { list = []; }
    elements[sel] = list.map(describe);
  }
  elements['button'] = [...document.querySelectorAll('button, [role=button]')].filter(visible).slice(0, 80).map(describe);
  const root = getComputedStyle(document.documentElement);
  return {
    viewport: {width: innerWidth, height: innerHeight},
    layout: document.documentElement.dataset.layout || null,
    uiZoom: root.getPropertyValue('--ui-zoom').trim() || null,
    bodyZoom: getComputedStyle(document.body).zoom,
    bodyClasses: [...document.body.classList],
    elements,
  };
}

function git(dir, ...args) {
  try { return execFileSync('git', ['-C', dir, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore']}).trim(); } catch { return null; }
}

/** What was captured: commit, subject, build ordinal and whether the tree was clean. */
export function describeCheckout(dir) {
  const ordinalFile = join(dir, 'buildordinal.json');
  const ordinal = existsSync(ordinalFile) ? JSON.parse(readFileSync(ordinalFile, 'utf8')) : {};
  const status = git(dir, 'status', '--porcelain');
  return {
    repo: 'cehinds/AshenSpire (HTML/JS)',
    commit: git(dir, 'rev-parse', 'HEAD'),
    subject: git(dir, 'log', '-1', '--format=%s'),
    branch: git(dir, 'rev-parse', '--abbrev-ref', 'HEAD'),
    release: ordinal.release ?? null,
    buildOrdinal: ordinal.ordinal ?? null,
    clean: status === '',
    profile: 'default (Core 2, ashen.core@2.0.0); ?gameProfile= not passed',
  };
}

function parseArgs(argv) {
  const opts = {js: null, out: 'TestResults/Parity/js', only: null, views: null, port: 8765, url: null, dom: true};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--out') opts.out = argv[++i];
    else if (a === '--only') opts.only = new RegExp(argv[++i]);
    else if (a === '--views') opts.views = argv[++i].split(',');
    else if (a === '--port') opts.port = Number(argv[++i]);
    else if (a === '--url') opts.url = argv[++i];
    else if (a === '--no-dom') opts.dom = false;
    else if (!a.startsWith('--') && !opts.js) opts.js = a;
    else throw Error('unknown argument ' + a);
  }
  if (!opts.js) throw Error('usage: js-reference-capture.mjs <js-checkout> [--out DIR] [--only REGEX] [--views desktop,portrait] [--port N] [--url URL] [--no-dom]');
  return opts;
}

async function waitForServer(url, ms = 30000) {
  const end = Date.now() + ms;
  while (Date.now() < end) {
    try { const r = await fetch(url); if (r.ok) return; } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 250));
  }
  throw Error('JS server did not answer at ' + url);
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const jsDir = resolve(opts.js), out = resolve(opts.out);
  if (!existsSync(join(jsDir, 'index.html')) || !existsSync(join(jsDir, 'tools/serve.mjs'))) throw Error(jsDir + ' is not an AshenSpire JS checkout (index.html, tools/serve.mjs)');
  const screens = loadScreens();
  const views = screens.views.filter((v) => !opts.views || opts.views.includes(v.id));
  const statusBefore = git(jsDir, 'status', '--porcelain');
  const source = describeCheckout(jsDir);
  mkdirSync(out, {recursive: true});
  let manifest = opts.only ? readManifest(out) : null;
  if (!manifest || manifest.source.commit !== source.commit) manifest = newManifest('js', 'tools/js-reference-capture.mjs', source, screens.views);
  manifest.source = source;

  // CommonJS resolution so a global Playwright install (NODE_PATH) is found, as the native harnesses do.
  const {chromium} = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE || 'playwright');
  let server = null;
  const base = opts.url || `http://127.0.0.1:${opts.port}/`;
  if (!opts.url) {
    server = spawn(process.execPath, ['tools/serve.mjs', '--port', String(opts.port), '--no-open', '--no-lan', '--host', '127.0.0.1'], {cwd: jsDir, stdio: ['ignore', 'pipe', 'pipe']});
    server.stderr.on('data', (d) => process.stderr.write('[serve] ' + d));
  }
  let browser, failed = 0;
  try {
    await waitForServer(base);
    browser = await chromium.launch();
    const steps = routes(base);
    for (const view of views) for (const screen of screens.screens) {
      if (opts.only && !opts.only.test(screen.id)) continue;
      const ctx = await browser.newContext({viewport: {width: view.width, height: view.height}, deviceScaleFactor: 1, hasTouch: !!view.touch});
      const page = await ctx.newPage();
      const pageErrors = [];
      page.on('pageerror', (e) => pageErrors.push(e.message.split('\n')[0]));
      const entry = {id: screen.id, view: view.id, png: shotFile(view.id, screen.id), dom: null, status: 'ok'};
      try {
        await steps[screen.id](page);
        await page.screenshot({path: join(out, entry.png)});
        if (opts.dom) {
          const dump = await page.evaluate(dumpDom, [...screens.dom, ...screen.dom.filter((s) => !screens.dom.includes(s))]);
          writeFileSync(join(out, domFile(view.id, screen.id)), JSON.stringify({side: 'js', id: screen.id, view: view.id, ...dump}, null, 1) + '\n');
          entry.dom = domFile(view.id, screen.id);
        }
      } catch (e) {
        failed++;
        entry.status = 'failed';
        entry.error = e.message.split('\n')[0];
        entry.png = null;
      }
      if (pageErrors.length) entry.pageErrors = pageErrors.slice(0, 3);
      upsertShot(manifest, entry, screens);
      writeManifest(out, manifest);
      console.log(`${entry.status === 'ok' ? 'OK  ' : 'FAIL'} ${view.id}-${screen.id}${entry.error ? ': ' + entry.error : ''}${pageErrors.length ? '  pageerrors: ' + pageErrors.slice(0, 2).join(' | ') : ''}`);
      await ctx.close();
    }
  } finally {
    if (browser) await browser.close();
    if (server) server.kill();
  }
  const statusAfter = git(jsDir, 'status', '--porcelain');
  manifest.source.statusUnchanged = statusAfter === statusBefore;
  writeManifest(out, manifest);
  const ok = manifest.shots.filter((s) => s.status === 'ok').length;
  console.log(`js-reference-capture: ${ok}/${manifest.shots.length} shots in ${out} (JS ${source.commit?.slice(0, 8)}, build ${source.buildOrdinal})`);
  if (statusAfter !== statusBefore) { console.error('js-reference-capture: the JS checkout changed during capture (git status differs); it must stay read-only'); process.exit(2); }
  if (failed) process.exit(1);
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  main().catch((e) => { console.error('js-reference-capture: ' + e.message); process.exit(1); });
}
