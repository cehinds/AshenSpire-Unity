#!/usr/bin/env node
// Import the shell (title, menus, map, services) art of the HTML/JS game build
// 0.7.1.1815 into Unity/Assets/AshenSpire/Resources/Art/js1815/ui/.
//
//   node tools/import-js1815-ui-art.mjs /path/to/ashenspire
//
// The JS checkout must hold the high-resolution pack (.art-cache/hd-assets-v23,
// fetched by its tools/fetch-art.mjs). Rendering runs in headless Chromium through
// Playwright: SVGs are rasterized at 2x, WebP paintings are re-encoded as PNG
// (largest edge at most 2048 px) and the composites below are drawn on a canvas,
// so every output is a plain canvas PNG with no timestamp chunks. Each PNG gets a
// .meta with a GUID derived from its resource path (UI art: no mipmaps, no NPOT
// scaling, uncompressed, clamped), and provenance.json records every source path
// and sha256 next to the output sha256. The JS checkout is only read.
import { createHash } from 'node:crypto';
import { execSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const js = process.argv[2];
if (!js) { console.error('usage: node tools/import-js1815-ui-art.mjs /path/to/ashenspire'); process.exit(2); }
const high = join(js, '.art-cache/hd-assets-v23/high/assets');
if (!existsSync(high)) { console.error('missing ' + high + ' (run the JS tools/fetch-art.mjs --pack all)'); process.exit(2); }
const out = join(root, 'Unity/Assets/AshenSpire/Resources/Art/js1815/ui');
mkdirSync(out, { recursive: true });

let playwright;
try { playwright = await import('playwright'); }
catch { playwright = createRequire(join(execSync('npm root -g', { encoding: 'utf8' }).trim(), 'noop.js'))('playwright'); }

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const src = rel => join(high, rel);
const dataUrl = rel => {
  const bytes = readFileSync(src(rel));
  const type = rel.endsWith('.svg') ? 'image/svg+xml' : rel.endsWith('.png') ? 'image/png' : 'image/webp';
  return 'data:' + type + ';base64,' + bytes.toString('base64');
};
const svgUrl = text => 'data:image/svg+xml;base64,' + Buffer.from(text).toString('base64');
const svg = rel => readFileSync(src(rel), 'utf8');

// Every job: name, sources (for provenance), and a draw recipe run in the page.
const jobs = [];
const job = (name, sources, recipe, note) => jobs.push({ name, sources, recipe, note });

// ---- Frames, buttons, divider (styles/player-polish.css) --------------------------
const frame = 'player-polish/ui/components/panel-frame.svg';
const leather = 'player-polish/materials/soot-leather-material.webp';
job('panel-frame', [frame], { w: 720, h: 480, layers: [{ img: dataUrl(frame), fit: 'fill' }] },
  'Brass folio frame alone, 2x. Nine-slice 48 px at -unity-slice-scale 0.25px (CSS border-image 24 / 12px).');
job('panel-folio', [frame, leather], { w: 720, h: 480, layers: [
  { img: dataUrl(leather), fit: 'cover', clipRadius: 6, inset: 3 },
  { color: 'rgba(13,11,8,0.91)', clipRadius: 6, inset: 3 },
  { img: dataUrl(frame), fit: 'fill' }] },
  'Frame over soot leather under a 91% #0d0b08 veil (player-polish.css panel fill), 2x.');
const neutral = 'player-polish/ui/components/button-neutral.svg';
const ready = 'player-polish/ui/components/button-primary-ready.svg';
const focus = 'player-polish/ui/components/button-primary-focus.svg';
job('button-neutral', [neutral], { w: 560, h: 112, layers: [{ img: dataUrl(neutral), fit: 'fill' }] }, 'Title menu entry (.tm-entry), 2x.');
const recolor = (text, fill, stroke) => text.replace(/fill="#18231[0-9a-f]"/, 'fill="' + fill + '"').replace(/fill="#254725"/, 'fill="' + fill + '"').replaceAll('#7fd97f', stroke);
job('button-primary', [ready], { w: 560, h: 112, layers: [{ img: svgUrl(recolor(svg(ready), '#2a513b', '#7fd97f')), fit: 'fill' }] },
  'Title NEW GAME (.slot-new): ready frame on the measured #2a513b fill, 2x.');
job('button-primary-focus', [focus], { w: 560, h: 112, layers: [{ img: dataUrl(focus), fit: 'fill' }] }, 'Primary hover/focus surface, 2x.');
const divider = 'player-polish/ui/components/divider-ornate.svg';
job('divider-ornate', [divider], { w: 960, h: 40, layers: [{ img: dataUrl(divider), fit: 'fill' }] }, '.as-ornament divider, 2x.');
// Settings pill toggles (51x28, --r-pill, panel fill, #85714f border, gold knob when on), 2x.
const pill = on => svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="102" height="56" viewBox="0 0 102 56"><rect x="1.5" y="1.5" width="99" height="53" rx="26.5" fill="${on ? '#3d3420' : '#241d15'}" stroke="#85714f" stroke-width="2"/><circle cx="${on ? 74 : 28}" cy="28" r="19" fill="${on ? '#c9a227' : '#a89571'}"/></svg>`);
job('toggle-off', [], { w: 102, h: 56, layers: [{ img: pill(false), fit: 'fill' }] }, 'Settings pill toggle, off (drawn from the computed CSS).');
job('toggle-on', [], { w: 102, h: 56, layers: [{ img: pill(true), fit: 'fill' }] }, 'Settings pill toggle, on (drawn from the computed CSS).');

// ---- Engraved icons (player-polish/ui/icons, currentColor = --gold) ---------------
for (const icon of ['settings', 'lock', 'close', 'menu', 'deck', 'equipment', 'inventory', 'profile', 'rest', 'merchant', 'reward', 'smith', 'health', 'mana', 'stamina', 'cinders', 'flask-health', 'flask-mana', 'map', 'next', 'back', 'history', 'save', 'coop', 'compendium', 'journey', 'relic', 'attack']) {
  const rel = 'player-polish/ui/icons/' + icon + '.svg';
  if (!existsSync(src(rel))) continue;
  const text = svg(rel).replaceAll('currentColor', '#c9a227');
  job('icon-' + icon, [rel], { w: 96, h: 96, layers: [{ img: svgUrl(text), fit: 'fill' }] }, 'Engraved icon in --gold, 96 px.');
}

// ---- Full-screen paintings ---------------------------------------------------------
// Title: the tower hall (styles/kit.css .tower-interior-city + .tower-door-frame masked
// by tower-door-mask, under the 28%-48% dark wash) behind the title menu.
const hall = (city, door, mask, w, h) => ({ w, h, layers: [
  { img: dataUrl(city), fit: 'cover' },
  { img: dataUrl(door), fit: 'cover', mask: dataUrl(mask), wash: ['rgba(8,7,5,0.28)', 'rgba(8,7,5,0.48)'] },
  { gradient: ['rgba(13,11,8,0.06)', 'rgba(13,11,8,0.25)', 'rgba(13,11,8,0.63)'] }] });
job('scene-title-hall-desktop', ['bg/tower-city-background-unlit.webp', 'bg/tower-entrance-hall.webp', 'bg/tower-door-mask.svg'],
  hall('bg/tower-city-background-unlit.webp', 'bg/tower-entrance-hall.webp', 'bg/tower-door-mask.svg', 1586, 992), 'Title hall composite (desktop).');
job('scene-title-hall-mobile', ['bg/tower-city-background-unlit.webp', 'bg/tower-entrance-hall-phone.webp', 'bg/tower-door-mask-phone.svg'],
  hall('bg/tower-city-background-unlit.webp', 'bg/tower-entrance-hall-phone.webp', 'bg/tower-door-mask-phone.svg', 1024, 1536), 'Title hall composite (phone).');
const painting = (name, rel, w, h, note) => job(name, [rel], { w, h, layers: [{ img: dataUrl(rel), fit: 'cover' }] }, note);
painting('scene-spire-vista-desktop', 'player-polish/scenes/spire-vista-desktop.webp', 1672, 941, 'Creation, history and end screens (.customize, .gameover).');
painting('scene-spire-vista-mobile', 'player-polish/scenes/spire-vista-mobile.webp', 941, 1672, 'Phone spire vista.');
painting('scene-merchant-desktop', 'player-polish/scenes/merchant-desktop.webp', 1672, 941, 'Merchant (.shop-workspace).');
painting('scene-merchant-mobile', 'player-polish/scenes/merchant-mobile.webp', 941, 1672, 'Phone merchant.');
painting('scene-forge-desktop', 'player-polish/scenes/forge-desktop.webp', 1672, 941, 'Blacksmith (.blacksmith-workspace).');
painting('scene-forge-mobile', 'player-polish/scenes/forge-mobile.webp', 941, 1672, 'Phone blacksmith.');
painting('scene-chapel-rest', 'player-polish/scenes/chapel-rest-master.webp', 1254, 1254, 'Shrine/rest campfire (.rest-screen).');
painting('scene-grave-dialogue', 'player-polish/scenes/grave-dialogue-master.webp', 1254, 1254, 'Event dialogue stage.');
painting('material-soot-leather', leather, 512, 512, 'Panel material, downscaled.');

// ---- Map: the act world painting as a soft-edged island (environmentArt.js terrain) --
const island = rel => ({ w: 480, h: 720, layers: [{ img: dataUrl(rel), fit: 'cover', vignette: true }] });
job('map-island-act1', ['environments/the-shattered-gulf-world.webp'], island('environments/the-shattered-gulf-world.webp'), 'Act 1 terrain, radial soft edge (.map-vignette).');
job('map-island-act2', ['environments/the-fractured-realm-world.webp'], island('environments/the-fractured-realm-world.webp'), 'Act 2 terrain, radial soft edge.');
job('map-island-act3', ['environments/the-fivefold-frontier-world.webp'], island('environments/the-fivefold-frontier-world.webp'), 'Act 3 terrain, radial soft edge.');

const browser = await playwright.chromium.launch();
const page = await browser.newPage();
await page.setContent('<!doctype html><html><body></body></html>');
const render = recipe => page.evaluate(async ({ w, h, layers }) => {
  const load = url => new Promise((ok, fail) => { const i = new Image(); i.onload = () => ok(i); i.onerror = fail; i.src = url; });
  const canvas = document.createElement('canvas'); canvas.width = w; canvas.height = h;
  const g = canvas.getContext('2d');
  const place = (img, fit) => {
    const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height;
    if (fit === 'fill') return [0, 0, w, h];
    const s = Math.max(w / iw, h / ih); const dw = iw * s, dh = ih * s; return [(w - dw) / 2, (h - dh) / 2, dw, dh];
  };
  const rounded = (ctx, inset, r) => { ctx.beginPath(); ctx.roundRect(inset, inset, w - 2 * inset, h - 2 * inset, r); ctx.clip(); };
  for (const layer of layers) {
    const c = document.createElement('canvas'); c.width = w; c.height = h; const x = c.getContext('2d');
    if (layer.clipRadius) rounded(x, layer.inset || 0, layer.clipRadius);
    if (layer.color) { x.fillStyle = layer.color; x.fillRect(0, 0, w, h); }
    if (layer.gradient) { const gr = x.createLinearGradient(0, 0, 0, h); layer.gradient.forEach((s, i) => gr.addColorStop(i / (layer.gradient.length - 1), s)); x.fillStyle = gr; x.fillRect(0, 0, w, h); }
    if (layer.img) {
      const img = await load(layer.img); x.drawImage(img, ...place(img, layer.fit));
      if (layer.wash) { const gr = x.createLinearGradient(0, 0, 0, h); gr.addColorStop(0, layer.wash[0]); gr.addColorStop(1, layer.wash[1]); x.fillStyle = gr; x.globalCompositeOperation = 'source-atop'; x.fillRect(0, 0, w, h); x.globalCompositeOperation = 'source-over'; }
      if (layer.mask) { const m = await load(layer.mask); x.globalCompositeOperation = 'destination-in'; x.drawImage(m, ...place(m, 'cover')); x.globalCompositeOperation = 'source-over'; }
      if (layer.vignette) {
        // Elliptical soft edge: opaque to 55% of each half-axis, clear at the edge.
        x.globalCompositeOperation = 'destination-in'; x.save(); x.translate(w / 2, h / 2); x.scale(w / 2, h / 2);
        const gr = x.createRadialGradient(0, 0, .55, 0, 0, 1);
        gr.addColorStop(0, 'rgba(0,0,0,1)'); gr.addColorStop(.5, 'rgba(0,0,0,.7)'); gr.addColorStop(1, 'rgba(0,0,0,0)');
        x.fillStyle = gr; x.fillRect(-1, -1, 2, 2); x.restore(); x.globalCompositeOperation = 'source-over';
      }
    }
    g.drawImage(c, 0, 0);
  }
  return canvas.toDataURL('image/png').split(',')[1];
}, recipe);

const metaTemplate = readFileSync(join(root, 'Unity/Assets/AshenSpire/Resources/Art/PlayerComponents/action-neutral.png.meta'), 'utf8');
const templateGuid = metaTemplate.match(/^guid: ([0-9a-f]+)/m)[1];
const guid = key => sha('js1815-ui:' + key).slice(0, 32);
const folderMeta = (path, key) => { if (!existsSync(path)) writeFileSync(path, `fileFormatVersion: 2\nguid: ${guid(key)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`); };
folderMeta(join(out, '..') + '.meta', 'folder:Art/js1815');
folderMeta(out + '.meta', 'folder:Art/js1815/ui');

const rows = [];
for (const { name, sources, recipe, note } of jobs) {
  const png = Buffer.from(await render(recipe), 'base64');
  const file = join(out, name + '.png');
  writeFileSync(file, png);
  const resource = 'Art/js1815/ui/' + name;
  writeFileSync(file + '.meta', metaTemplate.replace(templateGuid, guid(resource)));
  rows.push({ resource, size: [recipe.w, recipe.h], outputSha256: sha(png), note,
    sources: sources.map(rel => ({ path: 'assets/' + rel, cache: '.art-cache/hd-assets-v23/high/assets/' + rel, sha256: sha(readFileSync(src(rel))) })) });
}
await browser.close();
const provenance = { reference: 'cehinds/AshenSpire dev 0029e160 (build 0.7.1.1815)', pack: 'hd-assets-v23/high', tool: relative(root, fileURLToPath(import.meta.url)), assets: rows };
writeFileSync(join(out, 'provenance.json'), JSON.stringify(provenance, null, 2) + '\n');
if (!existsSync(join(out, 'provenance.json.meta')))
  writeFileSync(join(out, 'provenance.json.meta'), `fileFormatVersion: 2\nguid: ${guid('Art/js1815/ui/provenance.json')}\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
console.log(`import-js1815-ui-art: OK — ${rows.length} checks passed.`);
