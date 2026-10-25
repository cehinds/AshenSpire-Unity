#!/usr/bin/env node
// Import the entry (startup gate) art of the HTML/JS game build 0.7.1.1815 into
// Unity/Assets/AshenSpire/Resources/Art/js1815/entry/.
//
//   node tools/import-js1815-entry-art.mjs /path/to/ashenspire
//
// Same method as tools/import-js1815-ui-art.mjs: the JS checkout must hold the
// high-resolution pack (.art-cache/hd-assets-v23); headless Chromium (Playwright)
// draws each output on a canvas, so every PNG is a plain canvas PNG with no
// timestamp chunks. USS has no gradients, so the gate's CSS washes are baked in.
// Each PNG gets a .meta with a GUID derived from its resource path (UI art: no
// mipmaps, no NPOT scaling, uncompressed, clamped), and provenance.json records
// every source path and sha256 next to the output sha256. The JS checkout is only read.
import { createHash } from 'node:crypto';
import { execSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const js = process.argv[2];
if (!js) { console.error('usage: node tools/import-js1815-entry-art.mjs /path/to/ashenspire'); process.exit(2); }
const high = join(js, '.art-cache/hd-assets-v23/high/assets');
if (!existsSync(high)) { console.error('missing ' + high + ' (run the JS tools/fetch-art.mjs --pack all)'); process.exit(2); }
const out = join(root, 'Unity/Assets/AshenSpire/Resources/Art/js1815/entry');
mkdirSync(out, { recursive: true });

let playwright;
try { playwright = await import('playwright'); }
catch { playwright = createRequire(join(execSync('npm root -g', { encoding: 'utf8' }).trim(), 'noop.js'))('playwright'); }

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const src = rel => join(high, rel);
const dataUrl = rel => 'data:image/webp;base64,' + readFileSync(src(rel)).toString('base64');

const jobs = [];
const job = (name, sources, recipe, note) => jobs.push({ name, sources, recipe, note });
// Startup gate (styles/kit.css .startup-gate .tower-city-unlit and .startup-gate::after):
// the unlit river citadel, cover-fitted, under a 180deg wash rgba(8,7,5,.28 -> .48) and a
// centred elliptical wash rgba(7,6,4,.25 -> .5).
const citadel = 'bg/river-citadel-unlit.webp';
const gate = (w, h) => ({ w, h, layers: [
  { img: dataUrl(citadel), fit: 'cover' },
  { linear: ['rgba(8,7,5,0.28)', 'rgba(8,7,5,0.48)'] },
  { radial: ['rgba(7,6,4,0.25)', 'rgba(7,6,4,0.5)'] }] });
job('startup-gate-desktop', [citadel], gate(1586, 992), 'Startup gate (01b), landscape: river citadel with the gate washes baked in.');
job('startup-gate-mobile', [citadel], gate(640, 1138), 'Startup gate (01b), portrait crop of the same composite.');
// The wordmark's dark backplate (kit.css .startup-wordmark::before): a horizontal band
// rgba(7,6,4,.68) between 15% and 85%, masked vertically (opaque 22%..78%).
job('wordmark-backplate', [], { w: 512, h: 96, layers: [{ backplate: true }] }, 'Wordmark backplate (.startup-wordmark::before), drawn from the CSS.');

const browser = await playwright.chromium.launch();
const page = await browser.newPage();
await page.setContent('<!doctype html><html><body></body></html>');
const render = recipe => page.evaluate(async ({ w, h, layers }) => {
  const load = url => new Promise((ok, fail) => { const i = new Image(); i.onload = () => ok(i); i.onerror = fail; i.src = url; });
  const canvas = document.createElement('canvas'); canvas.width = w; canvas.height = h;
  const g = canvas.getContext('2d');
  for (const layer of layers) {
    if (layer.img) {
      const img = await load(layer.img);
      const s = Math.max(w / img.naturalWidth, h / img.naturalHeight), dw = img.naturalWidth * s, dh = img.naturalHeight * s;
      g.drawImage(img, (w - dw) / 2, (h - dh) / 2, dw, dh);
    }
    if (layer.linear) { const gr = g.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, layer.linear[0]); gr.addColorStop(1, layer.linear[1]); g.fillStyle = gr; g.fillRect(0, 0, w, h); }
    if (layer.radial) {
      // CSS "ellipse at center" (farthest-corner): radii sqrt(2) times the half sizes.
      g.save(); g.translate(w / 2, h / 2); g.scale(w / 2 * Math.SQRT2, h / 2 * Math.SQRT2);
      const gr = g.createRadialGradient(0, 0, 0, 0, 0, 1); gr.addColorStop(0, layer.radial[0]); gr.addColorStop(1, layer.radial[1]);
      g.fillStyle = gr; g.fillRect(-1, -1, 2, 2); g.restore();
    }
    if (layer.backplate) {
      const band = document.createElement('canvas'); band.width = w; band.height = h; const b = band.getContext('2d');
      const across = b.createLinearGradient(0, 0, w, 0);
      across.addColorStop(0, 'rgba(7,6,4,0)'); across.addColorStop(.15, 'rgba(7,6,4,.68)'); across.addColorStop(.85, 'rgba(7,6,4,.68)'); across.addColorStop(1, 'rgba(7,6,4,0)');
      b.fillStyle = across; b.fillRect(0, 0, w, h);
      const down = b.createLinearGradient(0, 0, 0, h);
      down.addColorStop(0, 'rgba(0,0,0,0)'); down.addColorStop(.22, 'rgba(0,0,0,1)'); down.addColorStop(.78, 'rgba(0,0,0,1)'); down.addColorStop(1, 'rgba(0,0,0,0)');
      b.globalCompositeOperation = 'destination-in'; b.fillStyle = down; b.fillRect(0, 0, w, h);
      g.drawImage(band, 0, 0);
    }
  }
  return canvas.toDataURL('image/png').split(',')[1];
}, recipe);

const metaTemplate = readFileSync(join(root, 'Unity/Assets/AshenSpire/Resources/Art/PlayerComponents/action-neutral.png.meta'), 'utf8');
const templateGuid = metaTemplate.match(/^guid: ([0-9a-f]+)/m)[1];
const guid = key => sha('js1815-entry:' + key).slice(0, 32);
if (!existsSync(out + '.meta'))
  writeFileSync(out + '.meta', `fileFormatVersion: 2\nguid: ${guid('folder:Art/js1815/entry')}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);

const rows = [];
for (const { name, sources, recipe, note } of jobs) {
  const png = Buffer.from(await render(recipe), 'base64');
  const file = join(out, name + '.png');
  writeFileSync(file, png);
  const resource = 'Art/js1815/entry/' + name;
  writeFileSync(file + '.meta', metaTemplate.replace(templateGuid, guid(resource)));
  rows.push({ resource, size: [recipe.w, recipe.h], outputSha256: sha(png), note,
    sources: sources.map(rel => ({ path: 'assets/' + rel, cache: '.art-cache/hd-assets-v23/high/assets/' + rel, sha256: sha(readFileSync(src(rel))) })) });
}
await browser.close();
const provenance = { reference: 'cehinds/AshenSpire dev 0029e160 (build 0.7.1.1815)', pack: 'hd-assets-v23/high', tool: relative(root, fileURLToPath(import.meta.url)), assets: rows };
writeFileSync(join(out, 'provenance.json'), JSON.stringify(provenance, null, 2) + '\n');
if (!existsSync(join(out, 'provenance.json.meta')))
  writeFileSync(join(out, 'provenance.json.meta'), `fileFormatVersion: 2\nguid: ${guid('Art/js1815/entry/provenance.json')}\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
console.log(`import-js1815-entry-art: OK — ${rows.length} checks passed.`);
