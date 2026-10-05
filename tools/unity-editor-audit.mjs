// Read-only use of AshenedSpire Editor's real pose validator/sampler. Its HTML
// source adapters do not write Unity assets. Keep the exact reference and samples
// alongside Unity's compiled evidence instead of treating a draft as gameplay.
import {readFile, mkdir, writeFile} from 'node:fs/promises';
import {resolve, join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
const editor = resolve(process.argv[2] || '../AshenedSpire-Editor');
const output = resolve(process.argv[3] || 'TestResults/Phase2/Editor');
const modulePath = join(editor, 'src/native/model/presentationSequence.js');
const model = await import(pathToFileURL(modulePath));
const project = model.starter();
assert.deepEqual(model.validate(project), []);
const samples = [];
for (let time = 0; time <= project.duration; time += project.duration / 20) {
  const normal = model.sample(project, time);
  const reduced = model.sample(project, time, {reducedMotion: true});
  const noFlashes = model.sample(project, time, {reduceFlashes: true});
  assert.equal(noFlashes.effects.length, 0);
  assert.equal(reduced.pose, normal.pose);
  samples.push({time, normal, reduced, noFlashes});
}
assert(samples.some(s => s.normal.effects.length), 'The editor sample must exercise an actual effect.');
const sourceFiles = ['src/native/model/presentationSequence.js', 'src/native/config/generated/ui.js', 'src/native/config/authored.js'];
const hashes = {};
for (const file of sourceFiles) hashes[file] = createHash('sha256').update(await readFile(join(editor, file))).digest('hex');
await mkdir(output, {recursive: true});
await writeFile(join(output, 'pose-project.json'), JSON.stringify(project, null, 2) + '\n');
await writeFile(join(output, 'samples.json'), JSON.stringify(samples, null, 2) + '\n');
await writeFile(join(output, 'receipt.json'), JSON.stringify({passed: true, tool: 'https://github.com/cehinds/AshenedSpire-Editor', hashes, samples: samples.length,
  use: 'Authored pose/effect timing and reduced-flash reference for the Unity presentation pass.',
  boundary: 'Editor sampler only; exported Unity player must be tested separately. No original or editor files modified.'}, null, 2) + '\n');
console.log(`Editor validator and pose sampler: ${samples.length} samples passed; ${output}`);
