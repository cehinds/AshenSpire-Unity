// Check the assembled site's actual hosting contract and public data availability.
// Run after unity-pages.mjs. Browser playtests separately establish gameplay.
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {createHash} from 'node:crypto';
import {retainedCodeBuildIds} from './unity-archive-hosting.mjs';
const root=resolve(process.argv[2]||'_site');
const history=JSON.parse(readFileSync(join(root,'history.json'),'utf8'));
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
assert(history.builds.length>0,'No playable archives found');
const localCode=retainedCodeBuildIds(history.builds,['dev','test','release','main'].flatMap(channel=>(history.channels[channel]??[]).slice(-1)));
let checks=0;
for(const build of history.builds){
 const dir=join(root,'builds',build.id),receipt=JSON.parse(readFileSync(join(dir,'hosting.json'),'utf8'));
 const html=readFileSync(join(dir,'Web/index.html'),'utf8');
 assert.equal(receipt.commit,build.commit);checks++;
 assert.equal(receipt.originalIndexSha256,build.manifest.files['Web/index.html']);checks++;
 assert.equal(sha(html),receipt.hostedIndexSha256);checks++;
 const remoteNames=['Web.data',...(localCode.has(build.id)?[]:['Web.wasm'])];
 assert.deepEqual(receipt.runtime.map(item=>item.path),remoteNames.map(name=>'Published/Web/Build/'+name));checks++;
 assert.equal(receipt.policy,localCode.has(build.id)?'exact-commit-raw-data-v1':'exact-commit-raw-data-code-v2');checks++;
 for(const item of receipt.runtime){
  const relative=item.path.slice('Published/'.length);
  const expected=`https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/${build.commit}/${item.path}`;
  assert.equal(item.url.split(/[?#]/)[0],expected);checks++;
  assert.equal(item.sha256,build.manifest.files[relative]);checks++;
  assert(html.includes(item.url),'Launcher must use its receipted runtime URL');checks++;
  assert(!existsSync(join(dir,relative)),'No accidental duplicate remote payload');checks++;
  const response=await fetch(item.url,{method:'HEAD',signal:AbortSignal.timeout(30000)});
  assert.equal(response.status,200,`Remote payload unavailable: ${build.id}`);checks++;
  assert.equal(response.headers.get('access-control-allow-origin'),'*',`Remote payload must support cross-origin play: ${build.id}`);checks++;
 }
 for(const name of ['Web.loader.js','Web.framework.js',...(localCode.has(build.id)?['Web.wasm']:[])]){
  const path='Web/Build/'+name;
  assert.equal(sha(readFileSync(join(dir,path))),build.manifest.files[path]);checks++;
 }
}
console.log(`${checks} checks passed`);
console.log(`Verified ${history.builds.length} archive launchers, local runtime hashes and public data URLs.`);
