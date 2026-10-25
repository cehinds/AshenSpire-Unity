#!/usr/bin/env node
// Side-by-side parity report (DEVELOPER.md, "Parity harness").
//
//   node tools/parity-report.mjs --a <capture-dir> --b <capture-dir> --out <dir> [--threshold 24]
//
// Pairs <view>-<id>.png from two capture folders (normally --a the JS reference from
// tools/js-reference-capture.mjs and --b a Unity capture from ScreenCaptureTests or
// tools/unity-parity-capture.mjs; JS against JS works too) for every screen in
// tools/parity-screens.json and writes:
//   <out>/index.html       side-by-side table: A | B | diff, pixel-diff score, layout-rect deltas
//   <out>/pairs/*.png      A | B | diff strips (the same images as one PNG per screen)
//   <out>/report.json      the scores and deltas, for scripts
// The pixel score is the fraction of pixels whose largest channel difference exceeds --threshold
// (0 = identical; a size mismatch counts the non-overlapping area as different). Rect deltas need
// a <view>-<id>.json dump on both sides; JS against Unity uses the screen's `pairs`
// ({JS selector: Unity control}), JS against JS compares every key the dumps share.
// Exit code is 0 whenever the report is written: scores are evidence for review, not a gate.
import {existsSync, readFileSync, writeFileSync, mkdirSync} from 'node:fs';
import {join, relative, resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {loadScreens, readManifest, shotFile, domFile, decodePng, encodePng, pixelDiff, sideBySide, rectDeltas} from './parity-harness.mjs';

const readJson = (file) => (existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : null);

/** Describe one capture folder: its manifest (if any) and a display label. */
function side(dir, fallback) {
  const manifest = readManifest(dir);
  const s = manifest?.source || {};
  const label = manifest ? `${manifest.side.toUpperCase()}${s.commit ? ' ' + s.commit.slice(0, 8) : ''}${s.buildOrdinal ? ' build ' + s.buildOrdinal : s.buildNumber ? ' build ' + s.buildNumber : ''}` : fallback;
  return {dir, manifest, label, kind: manifest?.side || null};
}

/**
 * Build the report data and write the pair PNGs. Pure apart from reading the two folders and
 * writing <out>/pairs; returns {a, b, threshold, rows, summary}.
 */
export function buildReport({aDir, bDir, outDir, threshold = 24, screens = loadScreens(), writeImages = true}) {
  const a = side(aDir, 'A'), b = side(bDir, 'B');
  const usePairs = a.kind === 'js' && b.kind === 'unity';
  if (writeImages) mkdirSync(join(outDir, 'pairs'), {recursive: true});
  const rows = [];
  for (const view of screens.views) for (const screen of screens.screens) {
    const png = shotFile(view.id, screen.id), dom = domFile(view.id, screen.id);
    const pa = join(aDir, png), pb = join(bDir, png);
    const row = {view: view.id, id: screen.id, png, pending: screen.unity === null ? screen.pending : null};
    const hasA = existsSync(pa), hasB = existsSync(pb);
    if (!hasA || !hasB) {
      row.status = !hasA && !hasB ? 'missing-both' : !hasA ? 'missing-a' : row.pending && usePairs ? 'pending' : 'missing-b';
      if (hasA) row.a = pa;
      if (hasB) row.b = pb;
      rows.push(row);
      continue;
    }
    const ia = decodePng(readFileSync(pa)), ib = decodePng(readFileSync(pb));
    const d = pixelDiff(ia, ib, {threshold});
    Object.assign(row, {status: 'compared', a: pa, b: pb, score: d.score, meanDelta: d.meanDelta, bbox: d.bbox, sizeMismatch: d.sizeMismatch, sizes: [[ia.width, ia.height], [ib.width, ib.height]]});
    const da = readJson(join(aDir, dom)), db = readJson(join(bDir, dom));
    if (da && db) {
      const pairs = usePairs ? screen.pairs || {} : null;
      row.rects = rectDeltas(da, db, pairs);
    }
    if (writeImages) {
      row.pair = join(outDir, 'pairs', png);
      writeFileSync(row.pair, encodePng(sideBySide([ia, ib, d.diff])));
    }
    rows.push(row);
  }
  const compared = rows.filter((r) => r.status === 'compared');
  const summary = {
    screens: rows.length,
    compared: compared.length,
    pending: rows.filter((r) => r.status === 'pending').length,
    missing: rows.filter((r) => r.status.startsWith('missing')).length,
    meanScore: compared.length ? compared.reduce((s, r) => s + r.score, 0) / compared.length : null,
    worst: [...compared].sort((x, y) => y.score - x.score).slice(0, 5).map((r) => ({view: r.view, id: r.id, score: r.score})),
  };
  return {a: {dir: aDir, label: a.label, source: a.manifest?.source || null}, b: {dir: bDir, label: b.label, source: b.manifest?.source || null}, threshold, rows, summary};
}

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'}[c]));
const pct = (n) => (n == null ? '—' : (n * 100).toFixed(2) + '%');

/** Render the report as one self-contained HTML page (images are linked relative to outDir). */
export function renderHtml(report, outDir) {
  const rel = (p) => esc(relative(outDir, p).split('\\').join('/'));
  const views = [...new Set(report.rows.map((r) => r.view))];
  const rectTable = (rows) => !rows?.length ? '<p class="muted">No rect pairs (needs a DOM/control dump on both sides).</p>' : `<table class="rects"><tr><th>A key</th><th>B key</th><th>#</th><th>A [x,y,w,h]</th><th>B [x,y,w,h]</th><th>Δ</th></tr>${rows.map((r) => r.missing
    ? `<tr class="warn"><td>${esc(r.left)}</td><td>${esc(r.right)}</td><td>${r.index}</td><td colspan="3">count A ${r.countA}, B ${r.countB}</td></tr>`
    : `<tr${r.maxAbs > 8 ? ' class="warn"' : ''}><td>${esc(r.left)}</td><td>${esc(r.right)}</td><td>${r.index}</td><td>${r.a.join(', ')}</td><td>${r.b.join(', ')}</td><td>${r.delta.join(', ')}</td></tr>`).join('')}</table>`;
  const cell = (path, label) => (path ? `<a href="${rel(path)}"><img loading="lazy" src="${rel(path)}" alt="${esc(label)}"></a>` : `<div class="empty">${esc(label)}: none</div>`);
  const section = (view) => report.rows.filter((r) => r.view === view).map((r) => `
<section class="row ${r.status}" id="${esc(r.view + '-' + r.id)}">
  <h3>${esc(r.id)} <span class="tag">${esc(r.status)}</span>${r.status === 'compared' ? ` <span class="score">${pct(r.score)} differ · mean Δ ${pct(r.meanDelta)}${r.sizeMismatch ? ' · SIZE ' + r.sizes.map((s) => s.join('×')).join(' vs ') : ''}</span>` : ''}</h3>
  ${r.pending ? `<p class="muted">Unity pending: ${esc(r.pending)}</p>` : ''}
  <div class="imgs">${cell(r.a, 'A')}${cell(r.b, 'B')}${r.pair ? `<a href="${rel(r.pair)}" class="pair">side-by-side PNG</a>` : ''}</div>
  ${r.status === 'compared' ? `<details><summary>Layout-rect deltas (${(r.rects || []).filter((x) => !x.missing).length})</summary>${rectTable(r.rects)}</details>` : ''}
</section>`).join('');
  const s = report.summary;
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Parity Report</title>
<style>
:root{--bg:#f6f3ee;--fg:#1d1a16;--muted:#6b6357;--line:#d8d0c2;--warn:#a43c35;--ok:#2f6b3f}
@media (prefers-color-scheme:dark){:root{--bg:#141210;--fg:#ece4d4;--muted:#a89571;--line:#3a3226;--warn:#e46a44;--ok:#7fd97f}}
body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif}
h1{font-size:20px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 8px;border-bottom:1px solid var(--line)}h3{font-size:14px;margin:0 0 6px}
.muted{color:var(--muted)}.tag{font-size:11px;padding:1px 6px;border:1px solid var(--line);border-radius:999px;color:var(--muted)}
.score{font-weight:400;color:var(--muted)}.row{padding:12px 0;border-bottom:1px solid var(--line)}
.compared .tag{color:var(--ok)}.missing-a .tag,.missing-b .tag{color:var(--warn)}
.imgs{display:flex;gap:8px;flex-wrap:wrap;align-items:flex-start}.imgs img{max-width:min(48vw,640px);max-height:420px;border:1px solid var(--line)}
.empty{width:200px;height:120px;display:grid;place-items:center;border:1px dashed var(--line);color:var(--muted)}
table{border-collapse:collapse;font:12px ui-monospace,monospace;margin-top:6px}td,th{border:1px solid var(--line);padding:2px 6px;text-align:left}.warn{color:var(--warn)}
.pair{align-self:center}
</style></head><body>
<h1>Parity report</h1>
<p>A: <b>${esc(report.a.label)}</b> (${esc(report.a.dir)}) · B: <b>${esc(report.b.label)}</b> (${esc(report.b.dir)}) · threshold ${report.threshold}/255</p>
<p>${s.compared} compared, ${s.pending} Unity-pending, ${s.missing} missing, of ${s.screens}. Mean differing pixels ${pct(s.meanScore)}.${s.worst.length ? ' Worst: ' + s.worst.map((w) => `<a href="#${esc(w.view + '-' + w.id)}">${esc(w.view + '-' + w.id)}</a> ${pct(w.score)}`).join(', ') : ''}</p>
${views.map((v) => `<h2>${esc(v)}</h2>${section(v)}`).join('\n')}
</body></html>
`;
}

function parseArgs(argv) {
  const opts = {a: null, b: null, out: 'TestResults/Parity/report', threshold: 24};
  for (let i = 0; i < argv.length; i++) {
    const k = argv[i];
    if (k === '--a') opts.a = argv[++i];
    else if (k === '--b') opts.b = argv[++i];
    else if (k === '--out') opts.out = argv[++i];
    else if (k === '--threshold') opts.threshold = Number(argv[++i]);
    else throw Error('unknown argument ' + k);
  }
  if (!opts.a || !opts.b) throw Error('usage: parity-report.mjs --a <capture-dir> --b <capture-dir> [--out DIR] [--threshold 24]');
  return opts;
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  try {
    const opts = parseArgs(process.argv.slice(2));
    const out = resolve(opts.out);
    const report = buildReport({aDir: resolve(opts.a), bDir: resolve(opts.b), outDir: out, threshold: opts.threshold});
    writeFileSync(join(out, 'report.json'), JSON.stringify(report, null, 1) + '\n');
    writeFileSync(join(out, 'index.html'), renderHtml(report, out));
    const s = report.summary;
    console.log(`parity-report: ${s.compared} compared, ${s.pending} pending, ${s.missing} missing; mean ${pct(s.meanScore)} differing -> ${join(out, 'index.html')}`);
  } catch (e) {
    console.error('parity-report: ' + e.message);
    process.exit(1);
  }
}
