// Shared pieces of the JS-to-Unity parity harness (DEVELOPER.md, "Parity harness"):
// the screen list (tools/parity-screens.json), file names, the capture manifest,
// a dependency-free PNG codec, the pixel diff and the layout-rect deltas.
// Used by tools/js-reference-capture.mjs, tools/unity-parity-capture.mjs and
// tools/parity-report.mjs; tested by tools/parity-report.test.mjs.
import {readFileSync, writeFileSync, existsSync, mkdirSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {inflateSync, deflateSync, crc32} from 'node:zlib';

export const SCREENS_FILE = join(dirname(fileURLToPath(import.meta.url)), 'parity-screens.json');
export const MANIFEST_FILE = 'manifest.json';
export const MANIFEST_SCHEMA = 'ashen.parity-capture/1';
const STEP_KEYS = new Set(['click', 'match', 'index', 'command', 'ifPresent', 'repeat', 'standard', 'fill', 'key', 'wait']);

// ---------------------------------------------------------------- screen list

/** Read and validate the screen list. Throws on a malformed entry, so a typo fails the node test. */
export function loadScreens(file = SCREENS_FILE) {
  const list = JSON.parse(readFileSync(file, 'utf8'));
  validateScreens(list);
  return list;
}

export function validateScreens(list) {
  if (list.schema !== 'ashen.parity-screens/1') throw Error('parity screens: unknown schema ' + list.schema);
  if (!Array.isArray(list.views) || !list.views.length) throw Error('parity screens: no views');
  const viewIds = new Set();
  for (const v of list.views) {
    if (!/^[a-z]+$/.test(v.id) || viewIds.has(v.id)) throw Error('parity screens: bad or duplicate view id ' + v.id);
    if (!(v.width > 0 && v.height > 0)) throw Error('parity screens: bad size for view ' + v.id);
    viewIds.add(v.id);
  }
  const ids = new Set();
  for (const s of list.screens) {
    if (!/^[0-9]{2}[a-z]?-[a-z0-9-]+$/.test(s.id)) throw Error('parity screens: bad id ' + s.id);
    if (ids.has(s.id)) throw Error('parity screens: duplicate id ' + s.id);
    ids.add(s.id);
    if (!Array.isArray(s.dom)) throw Error('parity screens: ' + s.id + ' needs a dom selector list');
    if (s.unity === null) {
      if (!s.pending) throw Error('parity screens: ' + s.id + ' has no Unity steps and no pending note');
    } else {
      if (!s.unity || !Array.isArray(s.unity.steps)) throw Error('parity screens: ' + s.id + ' needs unity.steps or unity: null');
      s.unity.steps.forEach((step, i) => validateStep(step, s.id + ' step ' + i));
    }
    for (const [sel, control] of Object.entries(s.pairs || {})) {
      if (typeof sel !== 'string' || typeof control !== 'string' || !control) throw Error('parity screens: ' + s.id + ' bad pair ' + sel);
      if (isPattern(control)) new RegExp(control.slice(1, -1));
    }
  }
  return true;
}

/** One Unity step: exactly one action (click | match | standard | fill | key | wait) plus modifiers. */
export function validateStep(step, where = 'step') {
  for (const k of Object.keys(step)) if (!STEP_KEYS.has(k)) throw Error(where + ': unknown key ' + k);
  const actions = ['click', 'match', 'standard', 'fill', 'key', 'wait'].filter((k) => k in step);
  if (actions.length !== 1) throw Error(where + ': needs exactly one action, has ' + (actions.join(',') || 'none'));
  if ('match' in step) new RegExp(step.match);
  if ('fill' in step && !(Array.isArray(step.fill) && step.fill.length === 2)) throw Error(where + ': fill is [controlId, value]');
  if ('repeat' in step && !(Number.isInteger(step.repeat) && step.repeat > 0)) throw Error(where + ': repeat must be a positive integer');
  return true;
}

export const isPattern = (value) => typeof value === 'string' && value.length > 2 && value.startsWith('/') && value.endsWith('/');

// ------------------------------------------------------------------ file names

export const shotFile = (view, id) => `${view}-${id}.png`;
export const domFile = (view, id) => `${view}-${id}.json`;

// -------------------------------------------------------------------- manifest

/** A new manifest. side is 'js' or 'unity'; source says what was captured (commit, build, URL). */
export function newManifest(side, tool, source, views) {
  return {schema: MANIFEST_SCHEMA, side, tool, source, views: views.map(({id, width, height, touch}) => ({id, width, height, touch: !!touch})), shots: []};
}

/**
 * Add or replace one shot entry, keeping entries in screen-list order (view, then screen),
 * so a partial re-run (--only) merges into an earlier manifest deterministically.
 */
export function upsertShot(manifest, entry, screens) {
  const order = new Map();
  manifest.views.forEach((v, vi) => screens.screens.forEach((s, si) => order.set(v.id + '/' + s.id, vi * 10000 + si)));
  const key = (e) => e.view + '/' + e.id;
  manifest.shots = manifest.shots.filter((e) => key(e) !== key(entry));
  manifest.shots.push(entry);
  manifest.shots.sort((a, b) => (order.get(key(a)) ?? 1e9) - (order.get(key(b)) ?? 1e9) || key(a).localeCompare(key(b)));
  return manifest;
}

export function readManifest(dir) {
  const file = join(dir, MANIFEST_FILE);
  if (!existsSync(file)) return null;
  const m = JSON.parse(readFileSync(file, 'utf8'));
  if (m.schema !== MANIFEST_SCHEMA) throw Error(file + ': unknown schema ' + m.schema);
  return m;
}

export function writeManifest(dir, manifest) {
  mkdirSync(dir, {recursive: true});
  writeFileSync(join(dir, MANIFEST_FILE), JSON.stringify(manifest, null, 2) + '\n');
}

// ------------------------------------------------------------------- PNG codec

const PNG_SIGNATURE = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

/** Decode an 8-bit, non-interlaced PNG (grey, RGB, palette, grey+alpha, RGBA) to RGBA. */
export function decodePng(buffer) {
  if (!buffer.subarray(0, 8).equals(PNG_SIGNATURE)) throw Error('not a PNG');
  let at = 8, width = 0, height = 0, depth = 0, type = 0, interlace = 0, palette = null, alpha = null;
  const idat = [];
  while (at < buffer.length) {
    const length = buffer.readUInt32BE(at), kind = buffer.toString('latin1', at + 4, at + 8), data = buffer.subarray(at + 8, at + 8 + length);
    at += 12 + length;
    if (kind === 'IHDR') { width = data.readUInt32BE(0); height = data.readUInt32BE(4); depth = data[8]; type = data[9]; interlace = data[12]; }
    else if (kind === 'PLTE') palette = data;
    else if (kind === 'tRNS') alpha = data;
    else if (kind === 'IDAT') idat.push(data);
    else if (kind === 'IEND') break;
  }
  if (depth !== 8 || interlace !== 0) throw Error(`unsupported PNG (depth ${depth}, interlace ${interlace})`);
  const channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[type];
  if (!channels) throw Error('unsupported PNG colour type ' + type);
  const raw = inflateSync(Buffer.concat(idat)), stride = width * channels, rows = Buffer.alloc(stride * height);
  for (let y = 0, src = 0; y < height; y++) {
    const filter = raw[src++], row = y * stride, prev = row - stride;
    for (let x = 0; x < stride; x++, src++) {
      const a = x >= channels ? rows[row + x - channels] : 0, b = y ? rows[prev + x] : 0, c = y && x >= channels ? rows[prev + x - channels] : 0;
      let v = raw[src];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) { const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
      else if (filter !== 0) throw Error('bad PNG filter ' + filter);
      rows[row + x] = v & 255;
    }
  }
  const data = Buffer.alloc(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    const s = i * channels, d = i * 4;
    if (type === 6) { data[d] = rows[s]; data[d + 1] = rows[s + 1]; data[d + 2] = rows[s + 2]; data[d + 3] = rows[s + 3]; }
    else if (type === 2) { data[d] = rows[s]; data[d + 1] = rows[s + 1]; data[d + 2] = rows[s + 2]; data[d + 3] = 255; }
    else if (type === 0) { data[d] = data[d + 1] = data[d + 2] = rows[s]; data[d + 3] = 255; }
    else if (type === 4) { data[d] = data[d + 1] = data[d + 2] = rows[s]; data[d + 3] = rows[s + 1]; }
    else { const p = rows[s]; data[d] = palette[p * 3]; data[d + 1] = palette[p * 3 + 1]; data[d + 2] = palette[p * 3 + 2]; data[d + 3] = alpha && p < alpha.length ? alpha[p] : 255; }
  }
  return {width, height, data};
}

/** Encode RGBA pixels as an RGB PNG (filter 0). Alpha is dropped: report images are opaque. */
export function encodePng({width, height, data}) {
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    const row = y * (width * 3 + 1);
    for (let x = 0; x < width; x++) { const s = (y * width + x) * 4, d = row + 1 + x * 3; raw[d] = data[s]; raw[d + 1] = data[s + 1]; raw[d + 2] = data[s + 2]; }
  }
  const chunk = (kind, body) => {
    const head = Buffer.alloc(8); head.writeUInt32BE(body.length, 0); head.write(kind, 4, 'latin1');
    const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), body])) >>> 0, 0);
    return Buffer.concat([head, body, crc]);
  };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([PNG_SIGNATURE, chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw, {level: 6})), chunk('IEND', Buffer.alloc(0))]);
}

// ------------------------------------------------------------------ pixel diff

/**
 * Compare two RGBA images. A pixel differs when any channel differs by more than `threshold` (0-255).
 * Images of different sizes are compared over their shared top-left area and flagged sizeMismatch;
 * the score is then also counted against the larger area, so a size mismatch can never score 0.
 * Returns {score: differing fraction 0..1, differing, total, meanDelta 0..1, bbox, sizeMismatch, diff}.
 */
export function pixelDiff(a, b, {threshold = 24} = {}) {
  const width = Math.min(a.width, b.width), height = Math.min(a.height, b.height);
  const total = Math.max(a.width * a.height, b.width * b.height);
  const diff = {width, height, data: Buffer.alloc(width * height * 4)};
  let differing = 0, sum = 0, minX = width, minY = height, maxX = -1, maxY = -1;
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const ia = (y * a.width + x) * 4, ib = (y * b.width + x) * 4, d = (y * width + x) * 4;
    const delta = Math.max(Math.abs(a.data[ia] - b.data[ib]), Math.abs(a.data[ia + 1] - b.data[ib + 1]), Math.abs(a.data[ia + 2] - b.data[ib + 2]));
    sum += delta;
    if (delta > threshold) {
      differing++;
      diff.data[d] = 255; diff.data[d + 1] = 40; diff.data[d + 2] = 90;
      if (x < minX) minX = x; if (y < minY) minY = y; if (x > maxX) maxX = x; if (y > maxY) maxY = y;
    } else {
      // Faint greyscale of A, so the differing pixels read in context.
      const g = (a.data[ia] * 0.3 + a.data[ia + 1] * 0.59 + a.data[ia + 2] * 0.11) * 0.35;
      diff.data[d] = diff.data[d + 1] = diff.data[d + 2] = g;
    }
    diff.data[d + 3] = 255;
  }
  const outside = total - width * height;
  return {
    score: (differing + outside) / total, differing: differing + outside, total,
    meanDelta: width * height ? sum / (width * height * 255) : 1,
    bbox: maxX < 0 ? null : [minX, minY, maxX - minX + 1, maxY - minY + 1],
    sizeMismatch: a.width !== b.width || a.height !== b.height, diff,
  };
}

/** Lay images out left to right on a dark strip (the side-by-side PNG). */
export function sideBySide(images, gap = 8) {
  const height = Math.max(...images.map((i) => i.height)), width = images.reduce((w, i) => w + i.width, 0) + gap * (images.length - 1);
  const out = {width, height, data: Buffer.alloc(width * height * 4)};
  for (let i = 0; i < width * height; i++) { out.data[i * 4] = 32; out.data[i * 4 + 1] = 28; out.data[i * 4 + 2] = 24; out.data[i * 4 + 3] = 255; }
  let left = 0;
  for (const img of images) {
    for (let y = 0; y < img.height; y++) img.data.copy(out.data, (y * width + left) * 4, y * img.width * 4, (y + 1) * img.width * 4);
    left += img.width + gap;
  }
  return out;
}

// ----------------------------------------------------------------- rect deltas

/** The elements a dump lists under `key`; a /regex/ key gathers every matching key in dump order. */
export function elementsFor(dump, key) {
  const elements = dump?.elements || {};
  if (!isPattern(key)) return elements[key] || [];
  const re = new RegExp(key.slice(1, -1));
  return Object.keys(elements).filter((k) => re.test(k)).flatMap((k) => elements[k]);
}

/**
 * Layout-rect deltas between two dumps. With `pairs` ({jsSelector: unityControl}) the left key is
 * looked up in `a` and the right key in `b`; without pairs every key present in both is compared
 * (JS against JS). Elements pair up by index. Rects are [x, y, width, height] in CSS/screen pixels.
 */
export function rectDeltas(a, b, pairs = null) {
  const keyPairs = pairs ? Object.entries(pairs) : Object.keys(a?.elements || {}).filter((k) => b?.elements && k in b.elements).map((k) => [k, k]);
  const rows = [];
  for (const [left, right] of keyPairs) {
    const ea = elementsFor(a, left), eb = elementsFor(b, right);
    const n = Math.min(ea.length, eb.length);
    for (let i = 0; i < n; i++) {
      const ra = ea[i].rect, rb = eb[i].rect, d = rb.map((v, k) => Math.round((v - ra[k]) * 10) / 10);
      rows.push({left, right, index: i, a: ra, b: rb, delta: d, maxAbs: Math.max(...d.map(Math.abs))});
    }
    if (ea.length !== eb.length) rows.push({left, right, index: n, countA: ea.length, countB: eb.length, missing: true});
  }
  return rows;
}
