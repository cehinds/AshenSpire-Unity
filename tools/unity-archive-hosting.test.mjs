import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {planArchiveHosting} from './unity-archive-hosting.mjs';

const hash=value=>createHash('sha256').update(value).digest('hex');
const commit='1234567890abcdef1234567890abcdef12345678';
const original=`<!doctype html><script src="Build/Web.loader.js?build=old#loader"></script>
<script>const channel=location.pathname;createUnityInstance(canvas,{
dataUrl:'Build/Web.data?build=old&v=1#data',frameworkUrl:'Build/Web.framework.js?build=old',codeUrl:"Build/Web.wasm?build=old#code",
streamingAssetsUrl:'StreamingAssets',companyName:'AshenSpire',productName:'AshenSpire Unity',productVersion:'0.0.1.0',saveKey:'unaltered'});</script>`;
function fixture(html=original){
 const paths=['Web/index.html','Web/Build/Web.loader.js','Web/Build/Web.framework.js','Web/Build/Web.data','Web/Build/Web.wasm'];
 return {commit,manifest:{version:'0.0.1.0',files:Object.fromEntries(paths.map(path=>[path,hash(path==='Web/index.html'?html:path)]))},tree:paths.map(path=>({path:'Published/'+path,blob:'b'.repeat(40)}))};
}
function rejected(html,modify=()=>{}){const build=fixture(html);modify(build);assert.throws(()=>planArchiveHosting(html,build));}

test('only the data fetch URL changes and original inputs remain immutable',()=>{
 const build=fixture(),before=JSON.stringify(build),plan=planArchiveHosting(original,build);
 const base='https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/'+commit+'/Published/Web/';
 assert.equal(plan.html,original.replace("dataUrl:'Build/","dataUrl:'"+base+'Build/'));
 assert.equal(JSON.stringify(build),before);
 assert.deepEqual(plan.omittedPaths,['Published/Web/Build/Web.data']);
 assert.equal(plan.dataUrls.dataUrl,base+'Build/Web.data?build=old&v=1#data');
 assert.equal(plan.dataUrls.codeUrl,undefined);
 assert.ok(plan.html.includes('codeUrl:"Build/Web.wasm?build=old#code"'));
 assert.equal(plan.receipt.policy,'exact-commit-raw-data-v1');
 assert.equal(plan.receipt.runtime.length,1);
 assert.equal(plan.receipt.originalIndexSha256,hash(original));
 assert.equal(plan.receipt.hostedIndexSha256,hash(plan.html));
 assert.equal(plan.receipt.commit,commit);
 for(const item of plan.receipt.runtime)assert.equal(item.sha256,build.manifest.files[item.path.slice(10)]);
});
test('hash-only URL suffixes and unqueried URLs remain exact',()=>{
 const html=original.replace('Web.data?build=old&v=1#data','Web.data#piece').replace('Web.wasm?build=old#code','Web.wasm');
 const plan=planArchiveHosting(html,fixture(html));
 assert.ok(plan.dataUrls.dataUrl.endsWith('/Web.data#piece'));assert.ok(plan.html.includes('codeUrl:"Build/Web.wasm"'));
});
test('complete SHA256 Git object IDs remain immutable',()=>{const build=fixture();build.commit='c'.repeat(64);assert.ok(planArchiveHosting(original,build).dataUrls.dataUrl.includes('/'+'c'.repeat(64)+'/'));});
for(const bad of ['main','1234567','a'.repeat(39),'a'.repeat(41),'A'.repeat(40),'../main','a'.repeat(40)+'?x'])test('reject unsafe/incomplete commit '+bad,()=>rejected(original,build=>build.commit=bad));
for(const name of ['dataUrl','codeUrl','frameworkUrl']){
 test('reject duplicate '+name+' even when the other occurrence is unknown',()=>rejected(original.replace('saveKey:',name+":'https://elsewhere.invalid/runtime',saveKey:")));
 test('reject computed '+name+' values',()=>rejected(original.replace(new RegExp(name+':'),name+':variable+')));
 test('reject quoted duplicate '+name+' declarations',()=>rejected(original.replace('saveKey:', '"'+name+'": "Build/other",saveKey:')));
}
test('reject expressions appended to a recognized URL literal',()=>rejected(original.replace("Web.data?build=old&v=1#data',","Web.data?build=old&v=1#data' + suffix,")));
test('reject remote, absolute and escaped payload paths',()=>{
 for(const value of ['https://example.com/Web.data','/Build/Web.data','Build/../Web.data','Build\\\\Web.data'])rejected(original.replace('Build/Web.data?build=old&v=1#data',value));
});
test('reject base tags, absent loader and duplicate local loaders',()=>{
 rejected('<base href="https://example.com/">'+original);
 rejected(original.replace('Build/Web.loader.js','different.js'));
 rejected('<script src="Build/Web.loader.js"></script>'+original);
});
test('reject unsafe tree and manifest paths and case aliases',()=>{
 for(const path of ['Published/Web/../outside','Published/Web\\bad','Published/Web/C:bad','Published/Web//bad','Published/Web/NUL.txt'])rejected(original,b=>b.tree.push({path}));
 rejected(original,b=>b.tree.push({path:'Published/web/other.json'}));
 rejected(original,b=>b.manifest.files['Web/../outside']='f'.repeat(64));
});
test('require original index and both payload hash receipts',()=>{
 for(const path of ['Web/index.html','Web/Build/Web.data','Web/Build/Web.wasm']){
  rejected(original,b=>delete b.manifest.files[path]);
  rejected(original,b=>b.manifest.files[path]='not-a-hash');
  rejected(original,b=>b.tree=b.tree.filter(file=>file.path!=='Published/'+path));
 }
 rejected(original,b=>b.manifest.files['Web/index.html']='f'.repeat(64));
});
test('fail closed on malformed input objects and duplicate paths',()=>{
 for(const build of [null,{},[],{commit,tree:[],manifest:{files:[]}}])assert.throws(()=>planArchiveHosting(original,build));
 assert.throws(()=>planArchiveHosting(null,fixture()));
 rejected(original,b=>b.tree.push({...b.tree[0]}));
 rejected(original,b=>b.tree[0].blob='main');
 rejected(original,b=>b.tree[0].mode='120000');
});
