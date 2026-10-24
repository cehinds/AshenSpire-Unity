#!/usr/bin/env node
// Import the HTML/JS build 0.7.1.1815 combat art into its own Unity namespace:
// Resources/Art/js1815/combat/{scenes,player,enemies,footer,icons,relics}.
//
//   node tools/import-js1815-combat-art.mjs [path-to-ashenspire-checkout]
//
// Needs ImageMagick (`convert`, with WebP) and Playwright's Chromium (SVG icons
// are drawn by the browser exactly as the game draws them). The JS checkout is
// read-only; its high-resolution pack lives in .art-cache/hd-assets-v23/high.
// Every PNG gets a deterministic .meta (GUID from its resource path) and a
// provenance row (source path + sha256) in Art/js1815/combat/provenance.json.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const js = process.argv[2] || '/home/user/ashenspire';
const high = join(js, '.art-cache/hd-assets-v23/high');
const resources = join(root, 'Unity/Assets/AshenSpire/Resources');
const out = join(resources, 'Art/js1815/combat');
const template = readFileSync(join(resources, 'Art/html898/0060938b0dfe693ce9df.png.meta'), 'utf8');
const sha = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');
const guid = (key) => createHash('sha256').update('js1815:' + key).digest('hex').slice(0, 32);
const rows = [];

function folderMeta(dir) {
  const key = relative(resources, dir).split('\\').join('/');
  const meta = dir + '.meta';
  if (!existsSync(meta)) writeFileSync(meta, `fileFormatVersion: 2\nguid: ${guid(key)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`);
}
function ensure(dir) {
  mkdirSync(dir, { recursive: true });
  for (let d = dir; d.startsWith(join(resources, 'Art/js1815')); d = dirname(d)) folderMeta(d);
}
function textureMeta(png, compressed) {
  const key = relative(resources, png).split('\\').join('/').replace(/\.png$/, '');
  const old = template.match(/^guid: (\w+)$/m)[1];
  let meta = template.replace(old, guid(key)).replace('enableMipMap: 1', 'enableMipMap: 0').replace('nPOTScale: 1', 'nPOTScale: 0');
  if (!compressed) meta = meta.replace(/textureCompression: 1/g, 'textureCompression: 0');
  writeFileSync(png + '.meta', meta);
  return key;
}
const magick = (...args) => execFileSync('convert', args, { stdio: ['ignore', 'pipe', 'inherit'] });
function record(kind, sources, png, compressed, note) {
  const resource = textureMeta(png, compressed);
  const size = execFileSync('identify', ['-format', '%w %h', png], { encoding: 'utf8' }).trim().split(' ').map(Number);
  rows.push({ kind, resource, sources: sources.map(s => ({ path: relative(js, s), sha256: sha(s) })), size, outputSha256: sha(png), note });
}
// Raster art: convert to PNG, optionally trim to the alpha bounds and fit inside max x max.
function raster(kind, source, name, { trim = false, max = 0, compressed = false, note } = {}) {
  const dir = join(out, kind); ensure(dir);
  const png = join(dir, name + '.png');
  const args = [source, '-strip'];
  if (trim) args.push('-trim', '+repage');
  if (max) args.push('-resize', `${max}x${max}>`);
  magick(...args, '-define', 'png:exclude-chunks=date,time', 'PNG32:' + png);
  record(kind, [source], png, compressed, note);
}

// 1. Battlefield scenes: the combat layers (far, landmark, ground; the game
//    leaves the foreground out of combat) composited at the desktop device size.
const { alternativeArtCatalog: catalog } = await import(pathToFileURL(join(js, 'src/ui/alternativeArtCatalog.js')).href);
for (const sceneId of ['hollow-weald-1', 'hollow-weald-3', 'pale-marches-1', 'pale-marches-3', 'cinder-reach-1', 'cinder-reach-3']) {
  const device = catalog.scenes[sceneId].devices.desktop;
  const dir = join(out, 'scenes'); ensure(dir);
  const png = join(dir, sceneId + '.png');
  const layers = device.layers.filter(l => catalog.sceneLayers[l.id].kind !== 'foreground');
  const args = ['-size', `${device.width}x${device.height}`, 'xc:#17130f'];
  const sources = [];
  for (const layer of layers) {
    const source = join(js, catalog.sceneLayers[layer.id].path); sources.push(source);
    const x = Math.round(layer.x), y = Math.round(layer.y);
    args.push('(', source, '-resize', `${Math.round(layer.width)}x${Math.round(layer.height)}!`, ')', '-geometry', `${x >= 0 ? '+' : ''}${x}${y >= 0 ? '+' : ''}${y}`, '-composite');
  }
  // The layers are 1080-1279px paintings; 1280 wide keeps their detail without upscaling bloat.
  magick(...args, '-resize', '1280x', '-strip', '-define', 'png:exclude-chunks=date,time', 'PNG24:' + png);
  record('scenes', sources, png, true, `${catalog.scenes[sceneId].name}: ${layers.map(l => l.id).join(' + ')} composited at the desktop device geometry (${device.width}x${device.height}), then 1280 wide; ground anchor ${device.groundAnchor}`);
}

// 2. Combatants. Players: the card-action "ready" frame each class idles in.
//    Enemies: the expansion idle and attack-02 frames (attack is shown while the
//    enemy telegraphs an attack, as the game does). Trimmed to their alpha bounds.
for (const cls of ['reaver', 'herald', 'rogue', 'starseer'])
  raster('player', join(js, `assets-display/alternative/card-${cls}-ready.webp`), cls + '-ready', { trim: true, note: 'class-cards ready pose, trimmed to alpha' });
const enemyArt = JSON.parse(readFileSync(join(resources, 'Original/enemy-art.json'), 'utf8')).enemies;
for (const id of Object.keys(enemyArt)) {
  for (const [pose, file] of [['idle', 'idle'], ['attack', 'attack-02']]) {
    const source = join(high, `assets/enemy-poses/expansion/${id}/${file}.webp`);
    if (existsSync(source)) raster('enemies', source, `${id}-${pose}`, { trim: true, max: 384, note: `expansion ${file}, trimmed, at most 384px` });
  }
}

// 3. Footer hardware, stamina orb, top-right controls, flasks.
for (const id of ['plate', 'connector', 'draw', 'spent-cards', 'potions'])
  raster('footer', join(high, `assets/ui/footer/${id}.webp`), id, { note: 'footerArt.js footerAssets' });
for (const id of ['orb', 'frame', 'diamond', 'spent', 'sigil'])
  raster('footer', join(high, `assets/ui/stamina-orb/${id}.webp`), 'orb-' + id, { max: 256, note: 'stamina orb layer' });
for (const id of ['armoury', 'menu'])
  raster('footer', join(high, `assets/ui-components/${id}.webp`), 'button-' + id, { max: 128, note: 'shared run HUD icon button' });

// 4. Relic icons for the HUD row.
const content = JSON.parse(readFileSync(join(root, 'GameContent/Unity/Original/content.json'), 'utf8'));
const relicIds = (Array.isArray(content.relics) ? content.relics.map(r => r.id) : Object.keys(content.relics));
for (const id of relicIds) {
  const source = join(high, `assets/relics/${id}.webp`);
  if (existsSync(source)) raster('relics', source, id, { max: 64, note: 'relic art at HUD size' });
}

// 5. Vector icons drawn by Chromium in white (tinted in USS), 64px.
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const { ACTION_SIGILS } = await import(pathToFileURL(join(js, 'src/content/combatSigils.js')).href);
const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
async function svgIcon(name, svg, sources, note) {
  const dir = join(out, 'icons'); ensure(dir);
  const png = join(dir, name + '.png');
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
  await page.locator('svg').first().screenshot({ path: png, omitBackground: true });
  magick(png, '-strip', '-define', 'png:exclude-chunks=date,time', 'PNG32:' + png);
  record('icons', sources, png, false, note);
}
const sigilSource = join(js, 'src/content/combatSigils.js');
const intents = { attack: 'attack', defend: 'defend', buff: 'power', cast: 'spell', counter: 'counter', smash: 'smash', sweep: 'sweep' };
for (const [name, sigil] of Object.entries(intents)) {
  const mark = ACTION_SIGILS[sigil];
  const paint = mark.solid ? 'fill="#ffffff" stroke="#15120e" stroke-width="1.6" paint-order="stroke" stroke-linejoin="round"' : 'fill="none" stroke="#ffffff" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"';
  await svgIcon('intent-' + name, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="-1 -1 34 34" width="64" height="64"><g ${paint}>${mark.shape}</g></svg>`, [sigilSource], `ACTION_SIGILS.${sigil} (intentIcon.js), white`);
}
await svgIcon('intent-prepare', '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-1 -1 34 34" width="64" height="64"><g fill="none" stroke="#ffffff" stroke-width="2.4" stroke-linecap="round"><path d="M7 3h18M7 29h18M9 3v6l14 14v6M23 3v6L9 23v6M9 8h14M9 25h14"/></g></svg>',
  [join(js, 'src/ui/components/intentIcon.js')], 'INTENT_ICON_SHAPES.prepare (hourglass), white');
for (const id of ['health', 'block', 'poise', 'cinders', 'stamina', 'mana', 'strength', 'dexterity', 'weak', 'vulnerable', 'frail', 'bleed', 'frost', 'insanity', 'burn', 'regen', 'madness', 'staggered', 'unknown']) {
  const source = join(high, `assets/player-polish/ui/icons/${id}.svg`);
  const svg = readFileSync(source, 'utf8').replace(/<title[^>]*>.*?<\/title>/, '').replace(/color="[^"]*"/, 'color="#ffffff"').replace(/width="24" height="24"/, 'width="64" height="64"');
  await svgIcon('status-' + id, svg, [source], 'player-polish engraved icon, white');
}
await browser.close();

rows.sort((a, b) => a.resource.localeCompare(b.resource));
writeFileSync(join(out, 'provenance.json'), JSON.stringify({
  reference: 'cehinds/AshenSpire dev 0029e160 (build 0.7.1.1815)', artPack: 'hd-assets-v23/high', tool: 'tools/import-js1815-combat-art.mjs', assets: rows,
}, null, 2) + '\n');
writeFileSync(join(out, 'provenance.json.meta'), `fileFormatVersion: 2\nguid: ${guid('Art/js1815/combat/provenance.json')}\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
console.log(JSON.stringify({ imported: rows.length, out: relative(root, out) }));
