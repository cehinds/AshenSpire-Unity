#!/usr/bin/env node
// Import the HTML/JS build 0.7.1.1815 equipment art used by the collection modal
// (deck / equipment / inventory / character, JS 07a–07d) into its own Unity namespace:
// Resources/Art/js1815/collection/{armaments,armour}.
//
//   node tools/import-js1815-collection-art.mjs [path-to-ashenspire-checkout]
//
// Needs ImageMagick (`convert`, with WebP). The JS checkout is read-only; its
// high-resolution pack lives in .art-cache/hd-assets-v23/high. Every PNG gets a
// deterministic .meta (GUID from its resource path) and a provenance row
// (source path + sha256) in Art/js1815/collection/provenance.json.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const js = process.argv[2] || '/home/user/ashenspire';
const high = join(js, '.art-cache/hd-assets-v23/high');
const resources = join(root, 'Unity/Assets/AshenSpire/Resources');
const out = join(resources, 'Art/js1815/collection');
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
  for (let d = dir; d.startsWith(join(resources, 'Art/js1815/')); d = dirname(d)) folderMeta(d);
}
function textureMeta(png) {
  const key = relative(resources, png).split('\\').join('/').replace(/\.png$/, '');
  const old = template.match(/^guid: (\w+)$/m)[1];
  // UI art: no mipmaps, NPOT unchanged, uncompressed (as the Art/html898 metas).
  const meta = template.replace(old, guid(key)).replace('enableMipMap: 1', 'enableMipMap: 0').replace('nPOTScale: 1', 'nPOTScale: 0').replace(/textureCompression: 1/g, 'textureCompression: 0');
  writeFileSync(png + '.meta', meta);
  return key;
}
function raster(kind, source, name, max, note) {
  const dir = join(out, kind); ensure(dir);
  const png = join(dir, name + '.png');
  execFileSync('convert', [source, '-strip', '-resize', `${max}x${max}>`, '-define', 'png:exclude-chunks=date,time', 'PNG32:' + png], { stdio: ['ignore', 'pipe', 'inherit'] });
  const resource = textureMeta(png);
  const size = execFileSync('identify', ['-format', '%w %h', png], { encoding: 'utf8' }).trim().split(' ').map(Number);
  rows.push({ kind, resource, sources: [{ path: relative(js, source), sha256: sha(source) }], size, outputSha256: sha(png), note });
}

const content = JSON.parse(readFileSync(join(root, 'GameContent/Unity/Original/content.json'), 'utf8'));
const list = (table) => Array.isArray(table) ? table : Object.values(table);
// Armaments: the square inventory icon the JS item card shows in its art window.
for (const item of list(content.equipment.armaments)) {
  const source = join(high, `assets/equipment/icon_${item.artKey || item.id}.webp`);
  if (existsSync(source)) raster('armaments', source, item.id, 256, 'equipment icon_<artKey>, at most 256px');
}
// Armour: the painted body portrait (body_<class>_<artKey>) on the armour card.
for (const item of list(content.equipment.armour)) {
  const source = join(high, `assets/equipment/body_${item.classId}_${item.artKey || item.id}.webp`);
  if (existsSync(source)) raster('armour', source, `${item.classId}-${item.id}`, 320, 'equipment body_<class>_<artKey>, at most 320px');
}
rows.sort((a, b) => a.resource.localeCompare(b.resource));
const provenance = join(out, 'provenance.json');
writeFileSync(provenance, JSON.stringify({ reference: 'cehinds/AshenSpire dev 0029e160 (build 0.7.1.1815)', pack: 'hd-assets-v23/high', tool: 'tools/import-js1815-collection-art.mjs', assets: rows }, null, 2) + '\n');
if (!existsSync(provenance + '.meta')) writeFileSync(provenance + '.meta', `fileFormatVersion: 2\nguid: ${guid('Art/js1815/collection/provenance.json')}\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`);
console.log(JSON.stringify({ imported: rows.length }));
