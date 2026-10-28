#!/usr/bin/env node
// unity-ui-tokens.mjs — JS UI data → Unity USS tokens and C# layout constants, plus the raw-value lint.
//
// The HTML game keeps its layout numbers as engine-neutral data (content/config/ui/**,
// content/hud-layout/*.json, src/content/card-layout.json, balance.ui.uiScale) and its
// palette as CSS custom properties (styles/base.css :root). This tool copies those inputs
// into a committed snapshot (GameContent/Unity/JsUi, with a sha256 manifest naming the JS
// commit) and generates, from the snapshot only:
//
//   Unity/Assets/AshenSpire/Resources/Generated/JsTokens.uss        USS variables (--js-*)
//   Unity/Assets/AshenSpire/Runtime/Presentation/Generated/JsUiTokens.g.cs   C# constants
//
// so geometry follows JS on re-import instead of being re-transcribed by eye.
//
//   node tools/unity-ui-tokens.mjs --import <js-checkout>   refresh the snapshot from a JS checkout, then write
//   node tools/unity-ui-tokens.mjs                          write the generated files from the snapshot
//   node tools/unity-ui-tokens.mjs --check                  fail when anything below disagrees (CI)
//
// --check verifies: the snapshot matches its manifest; the generated USS and C# equal a fresh
// generation; each generated file has a .meta; both PanelSettings assets use the JS reference
// sizes (balance.ui.uiScale designW×designH wide, narrowW×narrowH narrow, Scale With Screen
// Size, Expand); and the raw-value lint. The lint refuses raw colours (#hex, rgb(), rgba())
// and raw non-zero lengths/durations in USS: every value must come from a var(--…) token.
// Percentages, 0 and unitless 1 are structural and allowed. Style sheets that predate the
// lint are grandfathered by a raw-value budget (tools/unity-ui-tokens.baseline.json): their
// count may fall, never rise. A NEW style sheet has a budget of zero.
// Values the USS dialect cannot express (calc(), shadows, font stacks, viewport units) are
// listed in a comment in the generated sheet rather than silently dropped.
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync, copyFileSync, rmSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SNAPSHOT = 'GameContent/Unity/JsUi';
const ASSETS = 'Unity/Assets/AshenSpire';
const USS_OUT = `${ASSETS}/Resources/Generated/JsTokens.uss`;
const CS_OUT = `${ASSETS}/Runtime/Presentation/Generated/JsUiTokens.g.cs`;
const BASELINE = 'tools/unity-ui-tokens.baseline.json';
const PANELS = { wide: `${ASSETS}/ExpeditionPanel.asset`, narrow: `${ASSETS}/ExpeditionPanelNarrow.asset` };
// Animation keyframe data, not layout tokens (1.2 MB); the combat presentation phase imports it separately.
const EXCLUDED = new Set(['content/config/ui/presentation/equipmentAnimations.json']);
// The JS root font size is 62.5% (10px) at Text M; rem values convert at that size.
const REM_PX = 10;

const posix = (p) => p.split(sep).join('/');
const sha256 = (buffer) => createHash('sha256').update(buffer).digest('hex');
const kebab = (s) => String(s).replace(/([a-z0-9])([A-Z])/g, '$1-$2').replace(/[^A-Za-z0-9]+/g, '-').replace(/^-+|-+$/g, '').toLowerCase();
const round = (n) => Math.round(n * 1000) / 1000;
function walkFiles(dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir).sort().flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? walkFiles(path) : [path];
  });
}
const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));
const stable = (value) => JSON.stringify(value, null, 2) + '\n';

// ---------------------------------------------------------------- CSS helpers
/** Custom properties declared in the FIRST `:root {…}` block of a stylesheet, comments removed. */
export function rootCustomProperties(css) {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const start = text.search(/(^|\n)\s*:root\s*\{/);
  if (start < 0) return {};
  const open = text.indexOf('{', start);
  let depth = 0, end = open;
  for (; end < text.length; end++) { if (text[end] === '{') depth++; else if (text[end] === '}' && --depth === 0) break; }
  const out = {};
  for (const declaration of text.slice(open + 1, end).split(';')) {
    const match = declaration.match(/^\s*(--[A-Za-z0-9-]+)\s*:\s*([\s\S]+?)\s*$/);
    if (match) out[match[1]] = match[2].replace(/\s+/g, ' ');
  }
  return out;
}

function hexToRgba(hex) {
  let h = hex.slice(1);
  if (h.length <= 4) h = [...h].map((c) => c + c).join('');
  const n = (i) => parseInt(h.slice(i, i + 2), 16);
  const a = h.length === 8 ? round(n(6) / 255) : 1;
  return a === 1 ? `rgb(${n(0)}, ${n(2)}, ${n(4)})` : `rgba(${n(0)}, ${n(2)}, ${n(4)}, ${a})`;
}

/** A CSS value as USS, or null when the USS dialect cannot express it. */
export function cssValueToUss(raw) {
  const v = String(raw).trim();
  if (/^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i.test(v)) return hexToRgba(v);
  const rgb = v.match(/^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/i);
  if (rgb) return rgb[4] === undefined || Number(rgb[4]) === 1 ? `rgb(${+rgb[1]}, ${+rgb[2]}, ${+rgb[3]})` : `rgba(${+rgb[1]}, ${+rgb[2]}, ${+rgb[3]}, ${round(+rgb[4])})`;
  if (v === 'transparent') return 'rgba(0, 0, 0, 0)';
  const num = v.match(/^(-?\d*\.?\d+)(px|rem|%|ms|s)?$/);
  if (!num) return null;
  const n = Number(num[1]);
  switch (num[2]) {
    case 'px': return `${round(n)}px`;
    case 'rem': return `${round(n * REM_PX)}px`;
    case '%': return `${round(n)}%`;
    case 'ms': return `${round(n)}ms`;
    case 's': return `${round(n * 1000)}ms`;
    default: return n === 0 ? '0' : null; // a unitless CSS number is not a length
  }
}

// ---------------------------------------------------------------- token model
// Each token: { name, value (USS) | null, source, note? }
function addToken(tokens, name, value, source) {
  if (tokens.has(name)) {
    const prior = tokens.get(name);
    if (prior.value === value) return;
    throw new Error(`token ${name} is produced twice (${prior.source} and ${source})`);
  }
  tokens.set(name, { name, value, source });
}

const UNIT_BY_SUFFIX = [[/(^|[a-z])Px$|^px$/, 'px'], [/(^|[a-z])(Pct|Percent)$|^pct$/, '%'], [/(^|[a-z])Ms$|^ms$/, 'ms']];
/** Numeric leaves named …Px/…Pct/…Ms and colour strings, flattened under a prefix. */
function flattenConfig(tokens, prefix, value, source, path = []) {
  if (Array.isArray(value)) { value.forEach((item, i) => flattenConfig(tokens, prefix, item, source, [...path, String(i)])); return; }
  if (value && typeof value === 'object') { for (const [key, item] of Object.entries(value)) flattenConfig(tokens, prefix, item, source, [...path, key]); return; }
  const key = path[path.length - 1] ?? '';
  const name = `--js-${prefix}-${path.map(kebab).join('-')}`.replace(/-+$/, '');
  if (typeof value === 'number') {
    const unit = UNIT_BY_SUFFIX.find(([re]) => re.test(key))?.[1];
    if (unit) addToken(tokens, name, `${round(value)}${unit}`, source);
    return;
  }
  if (typeof value === 'string' && /^(#[0-9a-f]{3,8}|rgba?\(.*\))$/i.test(value.trim())) {
    addToken(tokens, name, cssValueToUss(value), source);
  }
}

const CARD_PX_KEYS = new Set(['x', 'y', 'w', 'h', 'width', 'height', 'fontSize', 'lineHeight', 'radius', 'strokeWidth', 'padding', 'letterSpacing']);
function flattenCardLayout(tokens, layout, source) {
  for (const key of ['width', 'height']) if (typeof layout[key] === 'number') addToken(tokens, `--js-card-${key}`, `${round(layout[key])}px`, source);
  for (const layer of layout.layers ?? []) {
    if (!layer || typeof layer.id !== 'string') continue;
    for (const [key, value] of Object.entries(layer)) {
      const name = `--js-card-${kebab(layer.id)}-${kebab(key)}`;
      if (typeof value === 'number' && CARD_PX_KEYS.has(key)) addToken(tokens, name, `${round(value)}px`, source);
      else if (typeof value === 'string' && /^(#[0-9a-f]{3,8}|rgba?\(.*\))$/i.test(value.trim())) addToken(tokens, name, cssValueToUss(value), source);
    }
  }
}

/** Resolve var(--x) references inside a custom-property map (one level deep is enough for base.css). */
function resolveVars(map) {
  const out = {};
  for (const [name, value] of Object.entries(map)) {
    let v = value, guard = 0;
    while (/^var\((--[A-Za-z0-9-]+)\)$/.test(v.trim()) && guard++ < 8) v = map[v.trim().slice(4, -1)] ?? v;
    out[name] = v;
  }
  return out;
}

const FORM_FACTORS = ['mobile', 'tablet', 'desktop', 'widescreen'];
// balance.ui groups copied into the snapshot: the layout decider, touch and accents, and the
// shared-HUD/stage presentation groups whose …Px/…Pct numbers become tokens.
const BALANCE_KEYS = ['uiScale', 'tapSize', 'accents', 'textSize', 'hudPresentation', 'hudQuickSettings', 'combatantStage'];
const BALANCE_TOKEN_GROUPS = ['hudPresentation', 'hudQuickSettings', 'combatantStage'];
const CONFIG_GROUPS = { components: 'component', scenes: 'scene', screens: 'screen', presentation: 'presentation' };

/** Everything the generators need, read from the snapshot directory. */
export function readSnapshot(dir) {
  const config = {};
  for (const file of walkFiles(join(dir, 'config/ui'))) config[posix(relative(join(dir, 'config/ui'), file))] = readJson(file);
  const hud = {};
  for (const ff of FORM_FACTORS) { const file = join(dir, 'hud-layout', ff + '.json'); if (existsSync(file)) hud[ff] = readJson(file); }
  return {
    manifest: readJson(join(dir, 'manifest.json')),
    config, hud,
    cardLayout: readJson(join(dir, 'card-layout.json')),
    themeRoot: readJson(join(dir, 'extracted/theme-root.json')),
    hudDefaults: readJson(join(dir, 'extracted/hud-layout-defaults.json')),
    balanceUi: readJson(join(dir, 'extracted/balance-ui.json')),
  };
}

export function buildTokens(snap) {
  const tokens = new Map(), skipped = [];
  // 1. Theme palette, radii and type steps (styles/base.css :root).
  const theme = resolveVars(snap.themeRoot);
  for (const [name, value] of Object.entries(theme)) {
    const uss = cssValueToUss(value);
    if (uss === null) skipped.push(`${name} (base.css)`); else addToken(tokens, '--js-' + name.slice(2), uss, 'styles/base.css');
  }
  // 2. tokens.json: type scale, layers, motion, touch, vars.
  const t = snap.config['tokens.json'] ?? {};
  for (const [name, step] of Object.entries(t.type?.scale ?? {})) if (typeof step.px === 'number') addToken(tokens, `--js-type-${kebab(name)}`, `${step.px}px`, 'tokens.json');
  for (const [name, px] of Object.entries(t.type?.minimum ?? {})) addToken(tokens, `--js-type-minimum-${kebab(name)}`, `${px}px`, 'tokens.json');
  for (const [name, ms] of Object.entries(t.motion ?? {})) addToken(tokens, `--js-motion-${kebab(name)}`, `${ms}ms`, 'tokens.json');
  for (const [name, row] of Object.entries(t.touch ?? {})) if (typeof row.px === 'number') addToken(tokens, `--js-touch-${kebab(name.replace(/Px$/, ''))}`, `${row.px}px`, 'tokens.json');
  for (const [name, value] of Object.entries(t.vars ?? {})) {
    const unit = UNIT_BY_SUFFIX.find(([re]) => re.test(name))?.[1];
    if (unit) addToken(tokens, `--js-vars-${kebab(name)}`, `${value}${unit}`, 'tokens.json'); else skipped.push(`vars.${name} (tokens.json: no USS unit)`);
  }
  // 3. Component, scene, screen and presentation configs: …Px/…Pct/…Ms numbers and colours.
  for (const [file, doc] of Object.entries(snap.config).sort(([a], [b]) => a.localeCompare(b))) {
    const [group, name] = file.split('/');
    if (!name || !CONFIG_GROUPS[group]) continue;
    flattenConfig(tokens, `${CONFIG_GROUPS[group]}-${kebab(name.replace(/\.json$/, ''))}`, doc, 'config/ui/' + file);
  }
  // 4. Card layout (the shared illustrated card face).
  flattenCardLayout(tokens, snap.cardLayout, 'card-layout.json');
  // 5. Accent palette (balance.ui.accents) and touch default (balance.ui.tapSize.def).
  for (const [name, row] of Object.entries(snap.balanceUi.accents ?? {})) addToken(tokens, `--js-accent-${kebab(name)}`, cssValueToUss(row.hex), 'balance.ui.accents');
  for (const key of BALANCE_TOKEN_GROUPS) if (snap.balanceUi[key]) flattenConfig(tokens, `balance-${kebab(key)}`, snap.balanceUi[key], `balance.ui.${key}`);
  if (typeof snap.balanceUi.tapSize?.def === 'number') addToken(tokens, '--js-tap-target', `${snap.balanceUi.tapSize.def}px`, 'balance.ui.tapSize');
  // 6. HUD form factors: defaults on :root, per-form-factor overrides on .form-factor-<ff>.
  const hudRoot = new Map(), hudBlocks = {};
  for (const [name, value] of Object.entries(snap.hudDefaults)) {
    const uss = cssValueToUss(value);
    if (uss === null) { if (!['initial', 'none'].includes(String(value).trim())) skipped.push(`${name} (hud-layout default)`); continue; }
    hudRoot.set('--js-' + name.slice(2), uss);
  }
  for (const ff of FORM_FACTORS) {
    hudBlocks[ff] = [];
    for (const [name, value] of Object.entries(snap.hud[ff]?.tokens ?? {})) {
      const uss = cssValueToUss(value);
      if (uss === null) { skipped.push(`${name} (hud-layout/${ff})`); continue; }
      hudBlocks[ff].push(['--js-' + name.slice(2), uss]);
    }
  }
  return { tokens, hudRoot, hudBlocks, skipped: [...new Set(skipped)].sort() };
}

export function renderUss(snap, built) {
  const m = snap.manifest;
  const lines = [
    `/* GENERATED by tools/unity-ui-tokens.mjs from ${SNAPSHOT} (JS ${m.source} ${m.commit.slice(0, 10)}, build ${m.build ?? '?'}). Do not edit. */`,
    '/* Units: 1 USS px = 1 JS CSS px at the JS reference sizes (ExpeditionPanel*.asset). rem converts at 10px (Text M). */',
    ':root {',
  ];
  let source = null;
  for (const token of [...built.tokens.values()].sort((a, b) => a.source.localeCompare(b.source) || a.name.localeCompare(b.name))) {
    if (token.source !== source) { source = token.source; lines.push(`    /* ${source} */`); }
    lines.push(`    ${token.name}: ${token.value};`);
  }
  if (built.hudRoot.size) {
    lines.push('    /* content/hud-layout defaults */');
    for (const [name, value] of [...built.hudRoot].sort(([a], [b]) => a.localeCompare(b))) lines.push(`    ${name}: ${value};`);
  }
  lines.push('}');
  for (const ff of FORM_FACTORS) {
    const rows = built.hudBlocks[ff];
    if (!rows?.length) continue;
    lines.push(`/* content/hud-layout/${ff}.json — the app root carries form-factor-${ff} (ViewportLayout.FormFactor). */`, `.form-factor-${ff} {`);
    for (const [name, value] of rows) lines.push(`    ${name}: ${value};`);
    lines.push('}');
  }
  if (built.skipped.length) {
    lines.push('/* Not expressible in USS (calc(), var() chains, shadows, font stacks, unitless or viewport units);');
    for (let i = 0; i < built.skipped.length; i += 4) lines.push(`   ${built.skipped.slice(i, i + 4).join(', ')}`);
    lines.push('*/');
  }
  return lines.join('\n') + '\n';
}

const csNum = (n) => { const s = String(n); return /[.eE]/.test(s) ? s : s + 'd'; };
const pascal = (s) => kebab(s).split('-').map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join('');
export function renderCs(snap) {
  const m = snap.manifest, ui = snap.balanceUi.uiScale ?? {}, ff = ui.formFactor ?? {}, t = snap.config['tokens.json'] ?? {};
  const need = ['designW', 'designH', 'min', 'max', 'narrowW', 'narrowH', 'narrowMax', 'shortWideMinH', 'gateBelowH'];
  for (const key of need) if (typeof ui[key] !== 'number') throw new Error(`balance.ui.uiScale.${key} is missing from the snapshot`);
  const consts = (rows) => rows.map(([name, value, doc]) => `            /// <summary>${doc}</summary>\n            public const double ${name} = ${csNum(value)};`).join('\n');
  const named = Object.entries(ui.named ?? {}).map(([k, v]) => [`Named${pascal(k)}`, v, `balance.ui.uiScale.named.${k}: a named UI size is a CAP on the zoom.`]);
  const layer = Object.entries(t.layer ?? {}).map(([k, v]) => [pascal(k), v, `tokens.json layer.${k} (JS z-index; Unity orders layers by child order).`]);
  const motion = Object.entries(t.motion ?? {}).map(([k, v]) => [pascal(k), v, `tokens.json motion.${k}, milliseconds.`]);
  const type = Object.entries(t.type?.scale ?? {}).filter(([, v]) => typeof v.px === 'number').map(([k, v]) => [pascal(k), v.px, `tokens.json type.scale.${k}, CSS px.`]);
  const touch = [['TapTargetDefault', snap.balanceUi.tapSize?.def ?? 44, 'balance.ui.tapSize.def: the Minimum tap size default, CSS px on glass.'],
    ...Object.entries(t.touch ?? {}).filter(([, v]) => typeof v.px === 'number').map(([k, v]) => [pascal(k.replace(/Px$/, '')), v.px, `tokens.json touch.${k}, CSS px.`])];
  return `// <auto-generated> by tools/unity-ui-tokens.mjs from ${SNAPSHOT} (JS ${m.source} ${m.commit.slice(0, 10)}, build ${m.build ?? '?'}). Do not edit.
// Pure constants (no Unity types): ViewportLayout reads UiScale; UnityTests/Viewport compiles this file.
namespace AshenSpire.Presentation
{
    public static class JsUiTokens
    {
        public const string SourceCommit = "${m.commit}";
        public const int SourceBuild = ${Number.isInteger(m.build) ? m.build : 0};
        /// <summary>JS balance.ui.uiScale: the layout decider (src/main.js layoutForCap) in CSS px.</summary>
        public static class UiScale
        {
${consts([
    ['DesignWidth', ui.designW, 'Wide reference width: the wide zoom is min(w / DesignWidth, h / DesignHeight), rounded to 0.01.'],
    ['DesignHeight', ui.designH, 'Wide reference height.'],
    ['MinZoom', ui.min, 'Zoom clamp floor.'],
    ['MaxZoom', ui.max, 'Zoom clamp ceiling.'],
    ['NarrowWidth', ui.narrowW, 'Narrow reference width: the narrow zoom is min(w / NarrowWidth, h / NarrowHeight), floored to 0.01.'],
    ['NarrowHeight', ui.narrowH, 'Narrow reference height.'],
    ['NarrowMaxWidth', ui.narrowMax, 'The layout is narrow when the viewport width is at most this (width-owned: a height change never flips it).'],
    ['ShortWideMinHeight', ui.shortWideMinH, 'Compact (short-wide) composition floor.'],
    ['GateBelowHeight', ui.gateBelowH, 'Below this height a wide layout is compact or short.'],
    ...named])}
        }
        /// <summary>JS balance.ui.uiScale.formFactor: which content/hud-layout file styles the HUD (FormFactorModel.js).</summary>
        public static class FormFactor
        {
${consts([
    ['WidescreenMinAspect', ff.widescreenMinAspect ?? 0, 'w / h at or above this (with w at least WidescreenAspectMinWidth) is widescreen.'],
    ['WidescreenAspectMinWidth', ff.widescreenAspectMinWidth ?? 0, 'Minimum width for the aspect rule.'],
    ['WidescreenMinWidth', ff.widescreenMinWidth ?? 0, 'Any width at or above this is widescreen.'],
    ['MobileMaxWidth', ff.mobileMaxWidth ?? 0, 'Mobile at or below this width.'],
    ['TabletMaxWidth', ff.tabletMaxWidth ?? 0, 'Tablet at or below this width.']])}
        }
        /// <summary>tokens.json layer: stacking order bottom to top.</summary>
        public static class Layer
        {
${consts(layer)}
        }
        public static class Motion
        {
${consts(motion)}
        }
        public static class TypeScale
        {
${consts(type)}
        }
        public static class Touch
        {
${consts(touch)}
        }
    }
}
`;
}

// ---------------------------------------------------------------- import
function gitHead(repo) {
  const head = readFileSync(join(repo, '.git/HEAD'), 'utf8').trim();
  if (!head.startsWith('ref: ')) return head;
  const ref = head.slice(5);
  if (existsSync(join(repo, '.git', ref))) return readFileSync(join(repo, '.git', ref), 'utf8').trim();
  const packed = existsSync(join(repo, '.git/packed-refs')) ? readFileSync(join(repo, '.git/packed-refs'), 'utf8') : '';
  const line = packed.split('\n').find((l) => l.endsWith(' ' + ref));
  if (!line) throw new Error(`cannot resolve ${ref} in ${repo}`);
  return line.split(' ')[0];
}

function importFrom(js) {
  const out = join(ROOT, SNAPSHOT);
  rmSync(out, { recursive: true, force: true });
  const files = {};
  const put = (rel, buffer) => { const path = join(out, rel); mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, buffer); files[rel] = sha256(buffer); };
  for (const file of walkFiles(join(js, 'content/config/ui'))) {
    const rel = posix(relative(js, file));
    if (!rel.endsWith('.json') || EXCLUDED.has(rel)) continue;
    put('config/ui/' + posix(relative(join(js, 'content/config/ui'), file)), readFileSync(file));
  }
  for (const ff of FORM_FACTORS) put(`hud-layout/${ff}.json`, readFileSync(join(js, `content/hud-layout/${ff}.json`)));
  put('card-layout.json', readFileSync(join(js, 'src/content/card-layout.json')));
  put('extracted/theme-root.json', Buffer.from(stable(rootCustomProperties(readFileSync(join(js, 'styles/base.css'), 'utf8')))));
  put('extracted/hud-layout-defaults.json', Buffer.from(stable(rootCustomProperties(readFileSync(join(js, 'styles/hud-layout.generated.css'), 'utf8')))));
  const balance = readJson(join(js, 'content/source/balanceUi.json'));
  const ui = balance.ui ?? balance;
  put('extracted/balance-ui.json', Buffer.from(stable(Object.fromEntries(BALANCE_KEYS.map((key) => [key, ui[key]])))));
  let build = null;
  try { build = readJson(join(js, 'buildordinal.json')).ordinal ?? null; } catch { /* optional */ }
  const manifest = {
    _: 'Snapshot of the HTML game UI data consumed by tools/unity-ui-tokens.mjs. Refresh with --import <js-checkout>; never hand-edit.',
    source: 'cehinds/AshenSpire', commit: gitHead(js), build,
    excluded: [...EXCLUDED],
    files: Object.fromEntries(Object.entries(files).sort(([a], [b]) => a.localeCompare(b))),
  };
  writeFileSync(join(out, 'manifest.json'), stable(manifest));
  return Object.keys(files).length;
}

// ---------------------------------------------------------------- metas
const USS_META = (guid) => `fileFormatVersion: 2\nguid: ${guid}\nScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 2\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n  script: {fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}\n  disableValidation: 0\n  unsupportedSelectorAction: 0\n`;
const CS_META = (guid) => `fileFormatVersion: 2\nguid: ${guid}\n`;
const FOLDER_META = (guid) => `fileFormatVersion: 2\nguid: ${guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n`;
function metaPaths() {
  return [[USS_OUT + '.meta', USS_META], [dirname(USS_OUT) + '.meta', FOLDER_META], [CS_OUT + '.meta', CS_META], [dirname(CS_OUT) + '.meta', FOLDER_META]];
}

// ---------------------------------------------------------------- panels
export function panelSettings(text) {
  const num = (re) => { const m = text.match(re); return m ? Number(m[1]) : NaN; };
  const ref = text.match(/m_ReferenceResolution:\s*\{x:\s*(\d+),\s*y:\s*(\d+)\}/);
  return { scaleMode: num(/m_ScaleMode:\s*(\d+)/), matchMode: num(/m_ScreenMatchMode:\s*(\d+)/), width: ref ? +ref[1] : NaN, height: ref ? +ref[2] : NaN };
}
function checkPanels(ui) {
  const problems = [];
  const want = { wide: [ui.designW, ui.designH], narrow: [ui.narrowW, ui.narrowH] };
  for (const [kind, path] of Object.entries(PANELS)) {
    const full = join(ROOT, path);
    if (!existsSync(full)) { problems.push(`${path}: missing`); continue; }
    if (!existsSync(full + '.meta')) problems.push(`${path}.meta: missing`);
    const p = panelSettings(readFileSync(full, 'utf8'));
    // PanelScaleMode.ScaleWithScreenSize = 2, PanelScreenMatchMode.Expand = 2 (min of the two ratios, as JS's fit).
    if (p.scaleMode !== 2) problems.push(`${path}: m_ScaleMode ${p.scaleMode}, expected 2 (Scale With Screen Size)`);
    if (p.matchMode !== 2) problems.push(`${path}: m_ScreenMatchMode ${p.matchMode}, expected 2 (Expand)`);
    if (p.width !== want[kind][0] || p.height !== want[kind][1]) problems.push(`${path}: reference ${p.width}x${p.height}, expected JS ${kind} ${want[kind][0]}x${want[kind][1]}`);
  }
  return problems;
}

// ---------------------------------------------------------------- lint
/** Raw colours and raw non-zero lengths/durations in a USS text. var(--…), comments, url()/resource() are ignored. */
export function lintUss(text) {
  const problems = [];
  const body = text.replace(/\/\*[\s\S]*?\*\//g, (c) => c.replace(/[^\n]/g, ' '));
  body.split('\n').forEach((line, i) => {
    for (const decl of line.split(/[;{}]/)) {
      const m = decl.match(/^\s*([-a-zA-Z]+)\s*:\s*(.+?)\s*$/);
      if (!m || m[1].startsWith('--')) continue;
      const value = m[2].replace(/var\(--[A-Za-z0-9-]+\)/g, ' ').replace(/(url|resource)\([^)]*\)/g, ' ');
      if (/#[0-9a-fA-F]{3,8}\b/.test(value) || /\b(rgba?|hsla?)\(/.test(value)) problems.push(`${i + 1}: raw colour in "${decl.trim()}"`);
      // A colour is one finding, not also a run of numbers.
      const numeric = value.replace(/#[0-9a-fA-F]{3,8}\b/g, ' ').replace(/\b(rgba?|hsla?)\([^)]*\)/g, ' ');
      for (const n of numeric.matchAll(/(^|[^A-Za-z0-9_.-])(-?\d*\.?\d+)(px|ms|s|deg|em|rem)?(?![A-Za-z0-9_%.-])/g)) {
        const [, , digits, unit] = n;
        const number = Number(digits);
        if (number === 0 || (!unit && number === 1)) continue;
        problems.push(`${i + 1}: raw value "${digits}${unit ?? ''}" in "${decl.trim()}" (use a --js-* token)`);
      }
    }
  });
  return problems;
}
function styleSheets() {
  return walkFiles(join(ROOT, ASSETS)).filter((f) => f.endsWith('.uss')).map((f) => posix(relative(ROOT, f))).filter((f) => f !== USS_OUT);
}

// ---------------------------------------------------------------- main
function main(argv) {
  const check = argv.includes('--check');
  // --accept-baseline records every sheet's CURRENT raw-value count as its budget. It exists to
  // grandfather the sheets that predate the lint; using it to absorb new raw values defeats it.
  const accept = argv.includes('--accept-baseline') && !check;
  const importAt = argv.indexOf('--import');
  if (importAt >= 0) {
    const js = argv[importAt + 1];
    if (!js || !existsSync(join(js, 'content/config/ui/tokens.json'))) { console.error('unity-ui-tokens: --import needs a JS checkout path (content/config/ui/tokens.json not found)'); process.exit(2); }
    console.log(`unity-ui-tokens: imported ${importFrom(resolve(js))} files into ${SNAPSHOT}`);
  }
  const snapDir = join(ROOT, SNAPSHOT);
  if (!existsSync(join(snapDir, 'manifest.json'))) { console.error(`unity-ui-tokens: ${SNAPSHOT}/manifest.json is missing; run --import <js-checkout>`); process.exit(2); }
  const errors = [];
  let checks = 0;
  const pass = () => { checks++; };
  const fail = (message) => { errors.push(message); };

  // Snapshot integrity.
  const manifest = readJson(join(snapDir, 'manifest.json'));
  const present = walkFiles(snapDir).map((f) => posix(relative(snapDir, f))).filter((f) => f !== 'manifest.json');
  for (const [rel, hash] of Object.entries(manifest.files)) {
    const path = join(snapDir, rel);
    if (!existsSync(path)) fail(`${SNAPSHOT}/${rel}: listed in the manifest but missing`);
    else if (sha256(readFileSync(path)) !== hash) fail(`${SNAPSHOT}/${rel}: sha256 differs from the manifest (re-run --import; never hand-edit the snapshot)`);
    else pass();
  }
  for (const rel of present) if (!manifest.files[rel]) fail(`${SNAPSHOT}/${rel}: not listed in the manifest`);

  const snap = readSnapshot(snapDir);
  const built = buildTokens(snap);
  const outputs = [[USS_OUT, renderUss(snap, built)], [CS_OUT, renderCs(snap)]];
  for (const [rel, text] of outputs) {
    const path = join(ROOT, rel);
    const current = existsSync(path) ? readFileSync(path, 'utf8').replace(/\r\n/g, '\n') : null;
    if (check) { if (current !== text) fail(`${rel} is stale; run node tools/unity-ui-tokens.mjs`); else pass(); }
    else if (current !== text) { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, text); }
  }
  for (const [rel, template] of metaPaths()) {
    const path = join(ROOT, rel);
    if (existsSync(path)) { pass(); continue; }
    if (check) fail(`${rel}: missing Unity .meta for a generated file`);
    else writeFileSync(path, template(randomBytes(16).toString('hex')));
  }
  for (const problem of checkPanels(snap.balanceUi.uiScale ?? {})) fail(problem);
  if (!errors.length) pass();

  // Lint self-test: the lint must catch the planted raw values and accept the token form.
  const planted = lintUss('.x { color: #fff; width: 12px; transition-duration: 150ms; }');
  const clean = lintUss('.x { color: var(--js-bg); width: 100%; flex-grow: 1; margin: 0; -unity-font: resource("Fonts/Inter-Regular"); }');
  if (planted.length !== 3 || clean.length !== 0) fail(`lint self-test: planted ${planted.length}/3, clean ${clean.length}/0`); else pass();

  // Raw-value lint with a budget for grandfathered sheets.
  const baseline = existsSync(join(ROOT, BASELINE)) ? readJson(join(ROOT, BASELINE)) : { sheets: {} };
  const nextBaseline = {};
  for (const sheet of styleSheets()) {
    const problems = lintUss(readFileSync(join(ROOT, sheet), 'utf8'));
    const budget = accept ? problems.length : baseline.sheets[sheet] ?? 0;
    if (problems.length > budget) {
      const extra = problems.length - budget;
      fail(`${sheet}: ${problems.length} raw value(s), budget ${budget}${budget ? ' (grandfathered sheet: the count may only fall)' : ' (new sheets use --js-* tokens only)'}\n    ${problems.slice(0, Math.min(problems.length, extra + 5)).join('\n    ')}`);
    } else pass();
    if (budget) nextBaseline[sheet] = Math.min(budget, problems.length);
  }
  if (!check) {
    const text = stable({ _: 'Raw-value budget per grandfathered USS sheet (tools/unity-ui-tokens.mjs). Counts may only fall; new sheets are not listed and have a budget of 0.', sheets: Object.fromEntries(Object.entries(nextBaseline).sort(([a], [b]) => a.localeCompare(b))) });
    if (!existsSync(join(ROOT, BASELINE)) || readFileSync(join(ROOT, BASELINE), 'utf8') !== text) writeFileSync(join(ROOT, BASELINE), text);
  }

  if (errors.length) {
    for (const error of errors) console.error(error);
    console.log(`unity-ui-tokens: ${checks} passed, ${errors.length} failed`);
    process.exit(1);
  }
  console.log(`${built.tokens.size + built.hudRoot.size} USS tokens, ${built.skipped.length} JS values not expressible in USS (listed in ${USS_OUT}).`);
  console.log(`unity-ui-tokens: OK — ${checks} checks passed.`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { main(process.argv.slice(2)); }
  catch (error) { console.error(`unity-ui-tokens: ${error.message}`); process.exit(2); }
}
