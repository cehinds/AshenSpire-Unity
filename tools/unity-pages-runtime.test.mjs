// Check the assembled site's actual hosting contract and public data availability.
// Run after unity-pages.mjs. Browser playtests separately establish gameplay.
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {createHash} from 'node:crypto';
const root=resolve(process.argv[2]||'_site');
const history=JSON.parse(readFileSync(join(root,'history.json'),'utf8'));
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
assert(history.builds.length>0,'No playable archives found');
let checks=0;
for(const build of history.builds){
 const dir=join(root,'builds',build.id),receipt=JSON.parse(readFileSync(join(dir,'hosting.json'),'utf8'));
 const html=readFileSync(join(dir,'Web/index.html'),'utf8');
 assert.equal(receipt.commit,build.commit);checks++;
 assert.equal(receipt.originalIndexSha256,build.manifest.files['Web/index.html']);checks++;
 assert.equal(sha(html),receipt.hostedIndexSha256);checks++;
 assert.equal(receipt.runtime.length,1);checks++;
 const data=receipt.runtime[0];
 assert.equal(data.path,'Published/Web/Build/Web.data');checks++;
 const expected=`https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/${build.commit}/${data.path}`;
 assert.equal(data.url.split(/[?#]/)[0],expected);checks++;
 assert.equal(data.sha256,build.manifest.files['Web/Build/Web.data']);checks++;
 assert(html.includes(data.url),'Launcher must use its receipted data URL');checks++;
 assert(!existsSync(join(dir,'Web/Build/Web.data')),'No accidental duplicate local data');checks++;
 for(const name of ['Web.loader.js','Web.framework.js','Web.wasm']){
  const path='Web/Build/'+name;
  assert.equal(sha(readFileSync(join(dir,path))),build.manifest.files[path]);checks++;
 }
 const response=await fetch(data.url,{method:'HEAD',signal:AbortSignal.timeout(30000)});
 assert.equal(response.status,200,`Remote data unavailable: ${build.id}`);checks++;
 assert.equal(response.headers.get('access-control-allow-origin'),'*',`Remote data must support cross-origin play: ${build.id}`);checks++;
}
console.log(`${checks} checks passed`);
console.log(`Verified ${history.builds.length} archive launchers, local runtime hashes and public data URLs.`);
