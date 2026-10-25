#!/usr/bin/env node
// Import the journey-screen art (map HUD, spoils, merchant, rest, event) of the HTML/JS
// game build 0.7.1.1815 into Unity/Assets/AshenSpire/Resources/Art/js1815/journey/.
//
//   node tools/import-js1815-journey-art.mjs /path/to/ashenspire
//
// Same contract as tools/import-js1815-ui-art.mjs: the JS checkout must hold the
// high-resolution pack (.art-cache/hd-assets-v23, fetched by its tools/fetch-art.mjs);
// headless Chromium (Playwright) rasterizes the engraved SVG icons and re-encodes the
// WebP flask paintings as plain canvas PNGs (no timestamp chunks). Every PNG gets a
// .meta with a GUID derived from its resource path (UI art: no mipmaps, no NPOT
// scaling, uncompressed, clamped), and provenance.json records each source path and
// sha256 beside the output sha256. The JS checkout is only read.
//
// Art the other js1815 folders already hold (relic coins and player/enemy figures in
// Art/js1815/combat, gold engraved icons in Art/js1815/ui) is reused, not copied.
import { createHash } from 'node:crypto';
import { execSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const js = process.argv[2];
if (!js) { console.error('usage: node tools/import-js1815-journey-art.mjs /path/to/ashenspire'); process.exit(2); }
const high = join(js, '.art-cache/hd-assets-v23/high/assets');
if (!existsSync(high)) { console.error('missing ' + high + ' (run the JS tools/fetch-art.mjs --pack all)'); process.exit(2); }
const out = join(root, 'Unity/Assets/AshenSpire/Resources/Art/js1815/journey');
mkdirSync(out, { recursive: true });

let playwright;
try { playwright = await import('playwright'); }
catch { playwright = createRequire(join(execSync('npm root -g', { encoding: 'utf8' }).trim(), 'noop.js'))('playwright'); }

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const src = rel => join(high, rel);
const dataUrl = rel => 'data:image/webp;base64,' + readFileSync(src(rel)).toString('base64');
const svgUrl = text => 'data:image/svg+xml;base64,' + Buffer.from(text).toString('base64');

const jobs = [];
const job = (name, sources, recipe, note) => jobs.push({ name, sources, recipe, note });

// ---- Engraved icons (player-polish/ui/icons; the CSS sets currentColor) ------------
// Spoils rows (reward.js KIND_GLYPHS): relic → skill, progression rows → progression; gold.
for (const icon of ['skill', 'progression', 'shield', 'controls', 'unknown']) {
  const rel = 'player-polish/ui/icons/' + icon + '.svg';
  job('icon-' + icon, [rel], { w: 96, h: 96, img: svgUrl(readFileSync(src(rel), 'utf8').replaceAll('currentColor', '#c9a227')) }, 'Engraved icon in --gold, 96 px.');
}
// Rest recovery preview rows (rest.js): outline icons in --parchment (#fdf6e6).
for (const [name, icon] of [['rest-hp', 'health'], ['rest-mp', 'mana'], ['rest-sp', 'stamina'], ['rest-flask-hp', 'flask-health'], ['rest-flask-mp', 'flask-mana']]) {
  const rel = 'player-polish/ui/icons/' + icon + '.svg';
  job(name, [rel], { w: 96, h: 96, img: svgUrl(readFileSync(src(rel), 'utf8').replaceAll('currentColor', '#fdf6e6')) }, 'Recovery preview icon in --parchment, 96 px.');
}

// ---- Flask paintings (content flasks.artAsset): merchant thumbnails, spoils rows ----
for (const [id, art] of [['crimsonFlask', 'crimson'], ['azureFlask', 'azure'], ['flaskOfFerocity', 'ferocity'], ['flaskOfStone', 'stone'],
  ['blightCoating', 'blight'], ['bloodUnction', 'blood'], ['wondrousDraught', 'wondrous']]) {
  const rel = 'ui/flasks/flask-' + art + '.webp';
  job('flask-' + id, [rel], { w: 128, h: 128, img: dataUrl(rel) }, 'Flask painting (flasks.' + id + '.artAsset), 128 px.');
}

const browser = await playwright.chromium.launch();
const page = await browser.newPage();
await page.setContent('<!doctype html><html><body></body></html>');
const render = recipe => page.evaluate(async ({ w, h, img }) => {
  const image = await new Promise((ok, fail) => { const i = new Image(); i.onload = () => ok(i); i.onerror = fail; i.src = img; });
  const canvas = document.createElement('canvas'); canvas.width = w; canvas.height = h;
  const g = canvas.getContext('2d'); g.imageSmoothingQuality = 'high';
  const iw = image.naturalWidth || image.width, ih = image.naturalHeight || image.height;
  const s = Math.min(w / iw, h / ih); const dw = iw * s, dh = ih * s;
  g.drawImage(image, (w - dw) / 2, (h - dh) / 2, dw, dh);
  return canvas.toDataURL('image/png').split(',')[1];
}, recipe);

const metaTemplate = readFileSync(join(root, 'Unity/Assets/AshenSpire/Resources/Art/PlayerComponents/action-neutral.png.meta'), 'utf8');
const templateGuid = metaTemplate.match(/^guid: ([0-9a-f]+)/m)[1];
const guid = key => sha('js1815-journey:' + key).slice(0, 32);
if (!existsSync(out + '.meta')) writeFileSync(out + '.meta', `fileFormatVersion: 2\nguid: ${guid('folder:Art/js1815/journey')}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);

const rows = [];
for (const { name, sources, recipe, note } of jobs) {
  const png = Buffer.from(await render(recipe), 'base64');
  const file = join(out, name + '.png');
  writeFileSync(file, png);
  const resource = 'Art/js1815/journey/' + name;
  writeFileSync(file + '.meta', metaTemplate.replace(templateGuid, guid(resource)));
  rows.push({ resource, size: [recipe.w, recipe.h], outputSha256: sha(png), note,
    sources: sources.map(rel => ({ path: 'assets/' + rel, cache: '.art-cache/hd-assets-v23/high/assets/' + rel, sha256: sha(readFileSync(src(rel))) })) });
}
await browser.close();
const provenance = { reference: 'cehinds/AshenSpire dev 0029e160 (build 0.7.1.1815)', pack: 'hd-assets-v23/high', tool: relative(root, fileURLToPath(import.meta.url)),
  reused: { relicCoins: 'Art/js1815/combat/relics/<relicId>', figures: 'Art/js1815/combat/player/<class>-ready, Art/js1815/combat/enemies/<enemy>-idle', hudButtons: 'Art/js1815/combat/footer/button-armoury, button-menu', goldIcons: 'Art/js1815/ui/icon-*' },
  assets: rows };
writeFileSync(join(out, 'provenance.json'), JSON.stringify(provenance, null, 2) + '\n');
if (!existsSync(join(out, 'provenance.json.meta')))
  writeFileSync(join(out, 'provenance.json.meta'), `fileFormatVersion: 2\nguid: ${guid('Art/js1815/journey/provenance.json')}\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
console.log(`import-js1815-journey-art: OK — ${rows.length} checks passed.`);
