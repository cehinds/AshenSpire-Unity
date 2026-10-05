// Exercise real candidate metadata refusals without changing delivery bytes.
// node tools/unity-stage-candidate.test.mjs <verified candidate directory>
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const root=path.resolve(fileURLToPath(new URL('..',import.meta.url)));
if(!process.argv[2]) throw Error('Pass a verified candidate directory.');
const candidate=path.resolve(process.argv[2]);
const original=fs.readFileSync(path.join(candidate,'candidate.json'));
const manifest=JSON.parse(original);
const probe=path.join(candidate,'validation-probe-'+crypto.randomUUID());
fs.mkdirSync(probe);
fs.symlinkSync(path.join(candidate,'Published'),path.join(probe,'Published'),'junction');
let checks=0;
function run(value,shouldPass,label){
    fs.writeFileSync(path.join(probe,'candidate.json'),JSON.stringify(value));
    const result=spawnSync(process.execPath,[path.join(root,'tools/unity-stage-candidate.mjs'),probe,'--check'],{cwd:root,encoding:'utf8'});
    if(result.error) throw result.error;
    assert.equal(result.status===0,shouldPass,label+': '+result.stderr);
    console.log('PASS '+label); checks++;
}
run(manifest,true,'unchanged candidate metadata validates');
for(const [label,mutate] of [
    ['wrong recorded download size',m=>m.downloads['Web.zip'].bytes++],
    ['wrong recorded download hash',m=>m.downloads['Web.zip'].sha256='0'.repeat(64)],
    ['wrong recorded raw Web size',m=>m.webRawPayloadBytes++],
    ['wrong nested platform provenance',m=>m.platforms.Windows.sourceDigest='0'.repeat(64)],
]) {
    const altered=structuredClone(manifest); mutate(altered); run(altered,false,label+' refused');
}
assert.ok(fs.readFileSync(path.join(candidate,'candidate.json')).equals(original),'Original delivery manifest changed'); checks++;
console.log(`Candidate metadata: ${checks} checks passed; preserved probe ${probe}`);
