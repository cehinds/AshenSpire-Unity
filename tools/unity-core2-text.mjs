#!/usr/bin/env node
// unity-core2-text.mjs — the JS game's interface copy and run-screen tables → one generated C# file the Core 2
// run screens read, so the words on the Unity screens are the JS game's words at the pin, never retyped.
//
// Inputs (a JS checkout at the pin, read-only):
//   src/content/generated/uiStrings.js          every row of content/source/uiStrings.csv (id, extends, short, full, tip)
//   content/config/ui/presentation/uiContent.json   map node kinds (name, blurb, icon), act names, tier numerals, act title templates
//   content/source/speakers.csv, eventSpeakers.json  who speaks each quest-chain event (dialogue door)
//   content/source/attributes.json              attribute labels (STR, DEX…)
// Output:
//   Unity/Assets/AshenSpire/Runtime/Presentation/Core2/Generated/Core2UiText.g.cs   (pure C#: no Unity types)
//
//   node tools/unity-core2-text.mjs <js-checkout>           write the generated file
//   node tools/unity-core2-text.mjs <js-checkout> --check   fail when the committed file is not a fresh generation (CI)
//
// The resolver (extends inheritance, {token} fill that throws on a missing token) is Core2Text.cs, a port of
// src/ui/strings.js; this tool only copies the table.
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = 'Unity/Assets/AshenSpire/Runtime/Presentation/Core2/Generated/Core2UiText.g.cs';

function csv(text) {
  // RFC 4180 rows; '#' comment lines (outside quotes) are skipped, as tools/content-build.mjs does.
  const rows = [];
  let row = [], cell = '', quoted = false, lineStart = true;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (lineStart && !quoted && c === '#') { while (i < text.length && text[i] !== '\n') i++; continue; }
    lineStart = false;
    if (quoted) {
      if (c === '"' && text[i + 1] === '"') { cell += '"'; i++; }
      else if (c === '"') quoted = false;
      else cell += c;
    } else if (c === '"') quoted = true;
    else if (c === ',') { row.push(cell); cell = ''; }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      row.push(cell); cell = '';
      if (row.some((v) => v !== '')) rows.push(row);
      row = []; lineStart = true;
    } else cell += c;
  }
  if (cell !== '' || row.length) { row.push(cell); if (row.some((v) => v !== '')) rows.push(row); }
  const [head, ...body] = rows;
  return body.map((r) => Object.fromEntries(head.map((h, i) => [h.trim(), r[i] ?? ''])));
}

const LS = String.fromCharCode(0x2028), PS = String.fromCharCode(0x2029);
const lit = (s) => JSON.stringify(String(s ?? '')).split(LS).join('\\u2028').split(PS).join('\\u2029');
const sha256 = (text) => createHash('sha256').update(text).digest('hex');

async function render(js) {
  const stringsPath = join(js, 'src/content/generated/uiStrings.js');
  const contentPath = join(js, 'content/config/ui/presentation/uiContent.json');
  const speakersPath = join(js, 'content/source/speakers.csv');
  const eventSpeakersPath = join(js, 'content/source/eventSpeakers.json');
  const attributesPath = join(js, 'content/source/attributes.json');
  for (const p of [stringsPath, contentPath, speakersPath, eventSpeakersPath, attributesPath]) {
    if (!existsSync(p)) throw new Error(`unity-core2-text: ${p} not found (is ${js} a JS checkout?)`);
  }
  // A data: URL import: the generated table has no imports, and this keeps Node from probing the JS package type.
  const { uiStrings } = await import(`data:text/javascript;base64,${Buffer.from(readFileSync(stringsPath)).toString("base64")}`);
  const content = JSON.parse(readFileSync(contentPath, 'utf8'));
  const c = content.components || content;
  const speakers = csv(readFileSync(speakersPath, 'utf8'));
  const eventSpeakers = JSON.parse(readFileSync(eventSpeakersPath, 'utf8'));
  const attributes = JSON.parse(readFileSync(attributesPath, 'utf8'));
  let commit = 'unknown';
  try { commit = execFileSync('git', ['-C', js, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(); } catch { /* a bare export */ }
  const inputs = [readFileSync(stringsPath, 'utf8'), readFileSync(contentPath, 'utf8'), readFileSync(speakersPath, 'utf8'),
    readFileSync(eventSpeakersPath, 'utf8'), readFileSync(attributesPath, 'utf8')].join('\n');

  const cell = (v) => (Array.isArray(v) ? v.join('|') : v == null ? '' : String(v));
  const stringRows = uiStrings.map((r) => `            new[] { ${[r.id, r.extends, r.short, r.full, r.tip].map((v) => lit(cell(v))).join(', ')} },`);
  const nodeRows = Object.entries(c.nodeTypes).map(([type, d]) => `            new[] { ${[type, d.icon, d.name, d.blurb].map(lit).join(', ')} },`);
  const actRows = Object.entries(c.actNames).map(([act, name]) => `            new[] { ${lit(act)}, ${lit(name)} },`);
  const tintRows = Object.entries(c.nodeTint || {}).map(([type, tint]) => `            new[] { ${lit(type)}, ${lit(tint)} },`);
  const speakerRows = speakers.map((s) => `            new[] { ${[s.id, s.name, s.portraitKey].map(lit).join(', ')} },`);
  const eventSpeakerRows = Object.entries(eventSpeakers).map(([e, s]) => `            new[] { ${lit(e)}, ${lit(s)} },`);
  const attributeRows = [...attributes].sort((a, b) => a.order - b.order).map((a) => `            new[] { ${[a.id, a.label, a.shortLabel].map(lit).join(', ')} },`);
  const t = c.copy.actTitle;
  return `// <auto-generated> by tools/unity-core2-text.mjs from the JS game (cehinds/AshenSpire ${commit.slice(0, 10)}). Do not edit.
// Inputs sha256 ${sha256(inputs)}: src/content/generated/uiStrings.js, content/config/ui/presentation/uiContent.json,
// content/source/speakers.csv, content/source/eventSpeakers.json, content/source/attributes.json.
// Pure C# (no Unity types): Core2Text resolves the rows; UnityTests/Core2RunView compiles this file.
namespace AshenSpire.Presentation.Core2
{
    public static class Core2UiData
    {
        public const string JsCommit = ${lit(commit)};

        /// <summary>uiStrings rows: id, extends, short, full, tip (a '|' list cell is joined; Core2Text refuses it).</summary>
        public static readonly string[][] Strings =
        {
${stringRows.join('\n')}
        };

        /// <summary>uiContent nodeTypes: type, icon, name, blurb.</summary>
        public static readonly string[][] NodeTypes =
        {
${nodeRows.join('\n')}
        };

        /// <summary>uiContent nodeTint: type, CSS colour.</summary>
        public static readonly string[][] NodeTint =
        {
${tintRows.join('\n')}
        };

        /// <summary>uiContent actNames: act, title.</summary>
        public static readonly string[][] ActNames =
        {
${actRows.join('\n')}
        };

        public static readonly string[] TierNumerals = { ${c.tierNumerals.map(lit).join(', ')} };
        public const string ActTitleTiered = ${lit(t.tiered)};
        public const string ActTitleNumbered = ${lit(t.numbered)};
        public const string ActTitleCycleSuffix = ${lit(t.cycleSuffix)};

        /// <summary>speakers.csv: id, name, portraitKey.</summary>
        public static readonly string[][] Speakers =
        {
${speakerRows.join('\n')}
        };

        /// <summary>eventSpeakers.json: event id, speaker id (a quest-chain step is spoken).</summary>
        public static readonly string[][] EventSpeakers =
        {
${eventSpeakerRows.join('\n')}
        };

        /// <summary>attributes.json in order: id, label, shortLabel.</summary>
        public static readonly string[][] Attributes =
        {
${attributeRows.join('\n')}
        };
    }
}
`;
}

const args = process.argv.slice(2);
const js = args.find((a) => !a.startsWith('--'));
if (!js) { console.error('usage: node tools/unity-core2-text.mjs <js-checkout> [--check]'); process.exit(2); }
const text = await render(resolve(js));
const out = join(ROOT, OUT);
const current = existsSync(out) ? readFileSync(out, 'utf8').replace(/\r\n/g, '\n') : null;
if (args.includes('--check')) {
  if (current !== text) { console.error(`unity-core2-text: ${OUT} is stale; run node tools/unity-core2-text.mjs <js-checkout>`); process.exit(1); }
  if (!existsSync(out + '.meta')) { console.error(`unity-core2-text: ${OUT}.meta is missing`); process.exit(1); }
  console.log(`unity-core2-text: OK — 2 checks passed`);
} else {
  if (current !== text) { mkdirSync(dirname(out), { recursive: true }); writeFileSync(out, text); }
  console.log(`unity-core2-text: wrote ${OUT}`);
}
