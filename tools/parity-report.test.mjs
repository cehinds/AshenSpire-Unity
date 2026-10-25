// Tests for the parity harness logic: the screen list, the capture manifest, the PNG codec,
// the pixel diff, the rect deltas and the report. Run: node --test tools/parity-report.test.mjs
// No browser or Unity is needed; the captures themselves are exercised by hand (DEVELOPER.md).
import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync, writeFileSync, readFileSync, readdirSync, statSync, existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {deflateSync, crc32} from 'node:zlib';
import {loadScreens, validateScreens, validateStep, shotFile, domFile, newManifest, upsertShot, writeManifest, readManifest, decodePng, encodePng, pixelDiff, sideBySide, rectDeltas} from './parity-harness.mjs';
import {buildReport, renderHtml} from './parity-report.mjs';
import {ROUTE_IDS} from './js-reference-capture.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const screens = loadScreens();

const image = (width, height, fill) => {
  const data = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) { const [r, g, b] = fill(i % width, Math.floor(i / width)); data[i * 4] = r; data[i * 4 + 1] = g; data[i * 4 + 2] = b; data[i * 4 + 3] = 255; }
  return {width, height, data};
};
const gradient = (w, h) => image(w, h, (x, y) => [(x * 37) & 255, (y * 53) & 255, (x * y) & 255]);

test('the screen list is valid and every screen has a JS route', () => {
  assert.equal(validateScreens(screens), true);
  assert.deepEqual([...ROUTE_IDS].sort(), screens.screens.map((s) => s.id).sort());
  assert.deepEqual(screens.views.map((v) => [v.id, v.width, v.height, v.touch]), [['desktop', 1280, 720, false], ['portrait', 390, 844, true]]);
});

test('every Unity step names a control the Unity presentation code creates', () => {
  const sources = [];
  (function walk(dir) {
    for (const name of readdirSync(dir)) {
      const p = join(dir, name);
      if (statSync(p).isDirectory()) walk(p); else if (name.endsWith('.cs')) sources.push(readFileSync(p, 'utf8'));
    }
  })(join(root, 'Unity/Assets/AshenSpire/Runtime/Presentation'));
  const literals = new Set(sources.flatMap((s) => [...s.matchAll(/"(-?[a-z][a-zA-Z0-9-]*)"/g)].map((m) => m[1])));
  // A dynamic id ("settings-section-" + index) is accepted through its literal prefix.
  const known = (id) => literals.has(id) || [...literals].some((p) => p.endsWith('-') && id.startsWith(p));
  const ids = screens.screens.filter((s) => s.unity).flatMap((s) => s.unity.steps.flatMap((st) => [st.click, st.fill?.[0]].filter(Boolean)));
  assert.ok(ids.length > 10);
  for (const id of new Set(ids)) assert.ok(known(id), 'no Unity control named ' + id);
  const prefixes = screens.screens.filter((s) => s.unity).flatMap((s) => s.unity.steps.filter((st) => st.match).map((st) => st.match.replace(/^\^/, '')));
  // Prefixed ids are composed as prefix + "-route-" + id, so the shared part may be its own literal.
  for (const p of new Set(prefixes)) assert.ok(literals.has(p) || literals.has(p.replace(/^[a-z]+/, '')), 'no Unity control prefix ' + p);
});

test('step and screen validation reject malformed entries', () => {
  assert.throws(() => validateStep({click: 'a', key: 'd'}), /exactly one action/);
  assert.throws(() => validateStep({tap: 'a'}), /unknown key tap/);
  assert.throws(() => validateStep({fill: ['only-id']}), /fill is/);
  assert.throws(() => validateStep({click: 'a', repeat: 0}), /repeat/);
  assert.equal(validateStep({match: '^native-card-', index: 2, command: true}), true);
  const bad = structuredClone(screens);
  bad.screens.push({...bad.screens[0]});
  assert.throws(() => validateScreens(bad), /duplicate id/);
  const noNote = structuredClone(screens);
  noNote.screens[0] = {id: '01-title', dom: [], unity: null};
  assert.throws(() => validateScreens(noNote), /pending note/);
});

test('file names are deterministic', () => {
  assert.equal(shotFile('desktop', '04-combat'), 'desktop-04-combat.png');
  assert.equal(domFile('portrait', '09a-settings-general'), 'portrait-09a-settings-general.json');
});

test('the manifest keeps screen-list order and replaces re-captured shots', () => {
  const m = newManifest('js', 'tool', {commit: 'abc'}, screens.views);
  const ids = screens.screens.map((s) => s.id);
  upsertShot(m, {id: ids[3], view: 'portrait', status: 'ok'}, screens);
  upsertShot(m, {id: ids[5], view: 'desktop', status: 'ok'}, screens);
  upsertShot(m, {id: ids[1], view: 'desktop', status: 'failed'}, screens);
  upsertShot(m, {id: ids[1], view: 'desktop', status: 'ok'}, screens);
  assert.deepEqual(m.shots.map((s) => s.view + '/' + s.id + '/' + s.status), [`desktop/${ids[1]}/ok`, `desktop/${ids[5]}/ok`, `portrait/${ids[3]}/ok`]);
  const dir = mkdtempSync(join(tmpdir(), 'parity-manifest-'));
  writeManifest(dir, m);
  assert.deepEqual(readManifest(dir), m);
  assert.equal(readManifest(join(dir, 'none')), null);
});

test('PNG encode/decode round-trips, and every filter type decodes', () => {
  const img = gradient(13, 7);
  const back = decodePng(encodePng(img));
  assert.equal(back.width, 13); assert.equal(back.height, 7);
  assert.ok(back.data.equals(img.data));
  // Hand-filtered RGBA PNG using filters 0-4 on successive rows.
  const w = 9, h = 10, bpp = 4, src = gradient(w, h).data, raw = [];
  for (let y = 0; y < h; y++) {
    const f = y % 5; raw.push(f);
    for (let x = 0; x < w * bpp; x++) {
      const i = y * w * bpp + x, cur = src[i], a = x >= bpp ? src[i - bpp] : 0, b = y ? src[i - w * bpp] : 0, c = y && x >= bpp ? src[i - w * bpp - bpp] : 0;
      const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
      const pred = [0, a, b, (a + b) >> 1, pa <= pb && pa <= pc ? a : pb <= pc ? b : c][f];
      raw.push((cur - pred) & 255);
    }
  }
  const chunk = (kind, body) => { const head = Buffer.alloc(8); head.writeUInt32BE(body.length); head.write(kind, 4, 'latin1'); const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), body])) >>> 0); return Buffer.concat([head, body, crc]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6;
  const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(Buffer.from(raw))), chunk('IEND', Buffer.alloc(0))]);
  assert.ok(decodePng(png).data.equals(src));
  assert.throws(() => decodePng(Buffer.from('nope')), /not a PNG/);
});

test('pixel diff scores identical, one-pixel and size-mismatched images', () => {
  const a = gradient(20, 10);
  assert.equal(pixelDiff(a, gradient(20, 10)).score, 0);
  assert.equal(pixelDiff(a, gradient(20, 10)).bbox, null);
  const b = gradient(20, 10); b.data[(3 * 20 + 5) * 4] ^= 255;
  const d = pixelDiff(a, b);
  assert.equal(d.differing, 1); assert.equal(d.score, 1 / 200); assert.deepEqual(d.bbox, [5, 3, 1, 1]);
  const small = pixelDiff(a, b, {threshold: 255});
  assert.equal(small.differing, 0);
  const sized = pixelDiff(a, gradient(20, 8));
  assert.equal(sized.sizeMismatch, true); assert.equal(sized.score, 40 / 200);
  const strip = sideBySide([a, gradient(5, 4)], 3);
  assert.deepEqual([strip.width, strip.height], [28, 10]);
});

test('rect deltas compare shared keys (JS vs JS) and paired keys with patterns (JS vs Unity)', () => {
  const js = {elements: {'.hand .card': [{rect: [10, 20, 100, 150]}, {rect: [120, 20, 100, 150]}], '.pile.draw': [{rect: [0, 600, 50, 80]}], body: [{rect: [0, 0, 1280, 720]}]}};
  const js2 = {elements: {'.hand .card': [{rect: [12, 20, 100, 150]}], body: [{rect: [0, 0, 1280, 720]}]}};
  const same = rectDeltas(js, js2);
  assert.deepEqual(same.map((r) => [r.left, r.index, r.delta?.join(','), !!r.missing]), [['.hand .card', 0, '2,0,0,0', false], ['.hand .card', 1, undefined, true], ['body', 0, '0,0,0,0', false]]);
  const unity = {elements: {'native-card-starting:5': [{rect: [15, 30, 90, 140]}], 'native-card-starting:4': [{rect: [110, 30, 90, 140]}], 'native-pile-draw': [{rect: [4, 610, 60, 60]}]}};
  const paired = rectDeltas(js, unity, {'.hand .card': '/^native-card-/', '.pile.draw': 'native-pile-draw'});
  assert.deepEqual(paired.map((r) => r.delta), [[5, 10, -10, -10], [-10, 10, -10, -10], [4, 10, 10, -20]]);
  assert.equal(paired[2].maxAbs, 20);
});

test('the report pairs captures, marks pending and missing screens, and writes the side-by-side PNG', () => {
  const base = mkdtempSync(join(tmpdir(), 'parity-report-'));
  const a = join(base, 'js'), b = join(base, 'unity'), out = join(base, 'out');
  const small = {views: [screens.views[0]], screens: [
    {id: '01-title', dom: [], pairs: {'.slot-new': 'native-new'}, unity: {steps: []}},
    {id: '02a-newgame-slot', dom: [], unity: null, pending: 'Phase 11'},
    {id: '04-combat', dom: [], unity: {steps: []}},
  ], schema: 'ashen.parity-screens/1'};
  const ma = newManifest('js', 't', {commit: '0123456789abcdef', buildOrdinal: 1837}, small.views);
  const mb = newManifest('unity', 't', {version: '0.0.28.1', buildNumber: 29}, small.views);
  writeManifest(a, ma); writeManifest(b, mb);
  const img = gradient(32, 18), changed = gradient(32, 18);
  for (let i = 0; i < 32 * 4; i++) changed.data[i] = 0; // top row black
  writeFileSync(join(a, 'desktop-01-title.png'), encodePng(img));
  writeFileSync(join(b, 'desktop-01-title.png'), encodePng(changed));
  writeFileSync(join(a, 'desktop-01-title.json'), JSON.stringify({elements: {'.slot-new': [{rect: [10, 10, 100, 44]}]}}));
  writeFileSync(join(b, 'desktop-01-title.json'), JSON.stringify({elements: {'native-new': [{rect: [12, 9, 98, 50]}]}}));
  writeFileSync(join(a, 'desktop-02a-newgame-slot.png'), encodePng(img));
  writeFileSync(join(a, 'desktop-04-combat.png'), encodePng(img));
  const report = buildReport({aDir: a, bDir: b, outDir: out, screens: small});
  assert.deepEqual(report.rows.map((r) => [r.id, r.status]), [['01-title', 'compared'], ['02a-newgame-slot', 'pending'], ['04-combat', 'missing-b']]);
  const title = report.rows[0];
  assert.ok(title.score > 0 && title.score <= 1 / 18 + 1e-9);
  assert.deepEqual(title.rects.map((r) => r.delta), [[2, -1, -2, 6]]);
  assert.ok(existsSync(title.pair));
  assert.equal(decodePng(readFileSync(title.pair)).width, 32 * 3 + 16);
  assert.equal(report.a.label, 'JS 01234567 build 1837');
  assert.equal(report.b.label, 'UNITY build 29');
  assert.deepEqual([report.summary.compared, report.summary.pending, report.summary.missing], [1, 1, 1]);
  const html = renderHtml(report, out);
  assert.match(html, /<title>Parity Report<\/title>/);
  assert.match(html, /Unity pending: Phase 11/);
  assert.match(html, /src="\.\.\/js\/desktop-01-title\.png"/);
  // JS against JS (no pairs): shared keys are compared directly.
  const self = buildReport({aDir: a, bDir: a, outDir: join(base, 'self'), screens: small, writeImages: false});
  assert.equal(self.rows[0].score, 0);
  assert.equal(self.rows[1].status, 'compared');
});
