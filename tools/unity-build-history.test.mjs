// Focused Git-backed regressions for build identity, promotion and preservation.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {mkdirSync,writeFileSync,readFileSync,mkdtempSync,existsSync,readdirSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {collectHistory,materializeHistory,resolvePullRequests,safeArtifactPath,publicHistory} from './unity-build-history.mjs';
const base=resolve('Builds/HistoryTests');mkdirSync(base,{recursive:true});
const root=mkdtempSync(join(base,'case-')),out=join(root,'site');
const git=args=>execFileSync('git',args,{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim();
git(['init']);git(['config','user.name','History fixture']);git(['config','user.email','history@example.invalid']);
let checks=0;
const check=(name,action)=>{action();checks++;console.log(`PASS ${name}`);};
function commit(message){git(['add','Published']);git(['commit','-m',message]);return git(['rev-parse','HEAD']);}
function player(version,text,number){
 mkdirSync(join(root,'Published/Web'),{recursive:true});writeFileSync(join(root,'Published/Web/index.html'),text);
 writeFileSync(join(root,'Published/build.json'),JSON.stringify({version,buildNumber:number,builtAt:'2026-09-07T00:00:00Z',sourceCommit:'a'.repeat(40),files:{'Web/index.html':createHash('sha256').update(text).digest('hex')}}));
 writeFileSync(join(root,'Published/changelog.json'),JSON.stringify({Added:[`Added ${text}`],Changed:[],Fixed:[]}));
}
player('0.9.0','first');const first=commit('First player');
writeFileSync(join(root,'Published/build.json'),readFileSync(join(root,'Published/build.json'),'utf8').replace('0.9.0','0.0.9.0'));commit('Metadata correction only');
player('0.0.9.1','second',901);const second=commit('Second player');
writeFileSync(join(root,'Published/evidence.txt'),'New evidence only');commit('Evidence follow-up');
const history=collectHistory(root,{dev:'HEAD',test:first,release:'missing',main:second});
check('distinct players deduplicate metadata and evidence edits',()=>assert.equal(history.builds.length,2));
check('channel histories use first-parent reachability and no inferred promotion',()=>assert.deepEqual(Object.fromEntries(Object.entries(history.channels).map(([key,value])=>[key,value.length])),{dev:2,test:1,release:0,main:2}));
check('legacy version and its first manifest remain exact',()=>assert.equal(history.builds[0].manifest.version,'0.9.0'));
check('new build number and build date survive',()=>{assert.equal(history.builds[1].buildNumber,901);assert.equal(history.builds[1].builtAt,'2026-09-07T00:00:00Z');});
check('promotion shares immutable archive identity',()=>assert.equal(history.channels.dev[0],history.channels.test[0]));
await resolvePullRequests(history.builds,'cehinds/AshenSpire-Unity','');
check('unavailable PR attribution stays unknown',()=>{assert.deepEqual(history.builds[0].pullRequests,[]);assert.match(history.builds[0].pullRequestStatus,/unknown/);});
history.builds[1].manifest.pullRequest={number:123,url:'https://invalid.example/'};
await resolvePullRequests([history.builds[1]],'cehinds/AshenSpire-Unity','');
check('declared PR uses repository-scoped URL',()=>assert.equal(history.builds[1].pullRequests[0].url,'https://github.com/cehinds/AshenSpire-Unity/pull/123'));
const bytes=materializeHistory(root,out,history);
check('materialized archive preserves exact old player bytes',()=>assert.equal(readFileSync(join(out,'builds',history.builds[0].id,'Web/index.html'),'utf8'),'first'));
check('archive rebuild is idempotent',()=>assert.equal(materializeHistory(root,out,history),bytes));
check('machine-readable index omits internal tree records',()=>assert.equal('tree' in publicHistory(history).builds[0],false));
check('traversal and Windows path escapes rejected',()=>{for(const path of ['Published/../outside','Published/Web/../../bad','Published/Web\\bad','/Published/Web','Published//bad'])assert.throws(()=>safeArtifactPath(path));});
history.builds[0].manifest.files['Web/index.html']='0'.repeat(64);
check('corrupt manifest digest refuses archive',()=>assert.throws(()=>materializeHistory(root,out,history),/hash mismatch/));
history.builds[0].manifest.files['Web/index.html']=createHash('sha256').update('first').digest('hex');
writeFileSync(join(out,'builds',history.builds[0].id,'Web/index.html'),'tampered');
check('existing archive bytes cannot be silently overwritten',()=>assert.throws(()=>materializeHistory(root,out,history),/Archive collision/));
git(['update-ref','refs/remotes/origin/dev',first]);
git(['update-ref','refs/remotes/origin/test',second]);
const defaults=collectHistory(root);
check('default four channel refs and their histories remain unchanged',()=>{
 assert.deepEqual(Object.keys(defaults.channels),['dev','test','release','main']);
 assert.deepEqual(Object.values(defaults.channels).map(ids=>ids.length),[1,2,0,0]);
});
const previews=collectHistory(root,{'pr-42':second,'codex/preview':first});
check('arbitrary named refs add independent memberships without implied promotion',()=>{
 assert.equal(previews.channels['pr-42'].length,2);assert.equal(previews.channels['codex/preview'].length,1);
 for(const channel of ['dev','test','release','main'])assert.deepEqual(previews.channels[channel],[]);
 assert.equal(previews.builds.length,2);
});
check('manifest-only PR states stay explicitly unverified',()=>{
 const pr=history.builds[1].pullRequests[0];assert.equal(pr.state,'unknown');assert.equal(pr.draft,null);assert.equal(pr.merged,null);
});
const fetchBefore=globalThis.fetch;
try{
 globalThis.fetch=async()=>({ok:true,json:async()=>[
  {number:42,state:'open',draft:true,merged_at:null,base:{repo:{full_name:'cehinds/AshenSpire-Unity'}}},
  {number:43,state:'open',draft:false,merged_at:null,base:{repo:{full_name:'cehinds/AshenSpire-Unity'}}},
  {number:44,state:'closed',draft:false,merged_at:null,base:{repo:{full_name:'cehinds/AshenSpire-Unity'}}},
  {number:45,state:'closed',draft:false,merged_at:'2026-09-07',base:{repo:{full_name:'cehinds/AshenSpire-Unity'}}},
  {number:46,state:'open',draft:false,base:{repo:{full_name:'someone/else'}}}
 ]});
 await resolvePullRequests([history.builds[0]],'cehinds/AshenSpire-Unity','test-only');
 check('associated PRs include draft, open, closed and merged state explicitly',()=>{
  assert.deepEqual(history.builds[0].pullRequests.map(pr=>[pr.number,pr.state,pr.draft,pr.merged]),
   [[42,'open',true,false],[43,'open',false,false],[44,'closed',false,false],[45,'merged',false,true]]);
 });
}finally{globalThis.fetch=fetchBefore;}

// A real Git fixture proves data omission still validates committed binary bytes.
const html=`<script src="Build/Web.loader.js?build=one"></script><script>createUnityInstance(canvas,{dataUrl:'Build/Web.data?build=one#payload',frameworkUrl:'Build/Web.framework.js?build=one',codeUrl:'Build/Web.wasm?build=one',productName:'Original Game',streamingAssetsUrl:'StreamingAssets'});</script>`;
const payloads={
 'Web/index.html':Buffer.from(html),
 'Web/Build/Web.loader.js':Buffer.from('loader'),
 'Web/Build/Web.framework.js':Buffer.from('framework'),
 'Web/Build/Web.data':Buffer.from([0,255,13,10,97]),
 'Web/Build/Web.wasm':Buffer.from([0,97,115,109,1,0,0,0]),
 'Web/StreamingAssets/settings.json':Buffer.from('{"save":"same"}')
};
for(const [file,content] of Object.entries(payloads)){
 mkdirSync(join(root,'Published',file,'..'),{recursive:true});writeFileSync(join(root,'Published',file),content);
}
writeFileSync(join(root,'Published/build.json'),JSON.stringify({version:'0.0.14.0',buildNumber:14,files:Object.fromEntries(Object.entries(payloads).map(([file,content])=>[file,createHash('sha256').update(content).digest('hex')]))}));
const complete=commit('Complete exported player');
const realHistory=collectHistory(root,{'pr-42':complete}),build=realHistory.builds.find(item=>item.commit===complete);
const only={builds:[build],channels:{'pr-42':[build.id]}},localOut=join(root,'exact-local'),remoteOut=join(root,'remote-hosted');
materializeHistory(root,localOut,only);
check('default materialization keeps all original runtime and index bytes',()=>{
 for(const [file,content] of Object.entries(payloads))assert.deepEqual(readFileSync(join(localOut,'builds',build.id,file)),content);
 assert.equal(existsSync(join(localOut,'builds',build.id,'hosting.json')),false);
});
const hostedBytes=materializeHistory(root,remoteOut,only,{remoteRuntime:true}),archive=join(remoteOut,'builds',build.id);
check('remote mode omits only data; wasm and all other local files stay byte-identical',()=>{
 assert.equal(existsSync(join(archive,'Web/Build/Web.data')),false);
 for(const [file,content] of Object.entries(payloads))if(!['Web/index.html','Web/Build/Web.data'].includes(file))assert.deepEqual(readFileSync(join(archive,file)),content);
 const hosted=readFileSync(join(archive,'Web/index.html'),'utf8');
 assert.equal(hosted,html.replace('Build/Web.data',`https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/${complete}/Published/Web/Build/Web.data`));
});
check('hosting receipt pins original, derived and omitted payload hashes',()=>{
 const receipt=JSON.parse(readFileSync(join(archive,'hosting.json')));
 assert.equal(receipt.commit,complete);assert.equal(receipt.policy,'exact-commit-raw-data-v1');
 assert.equal(receipt.originalIndexSha256,build.manifest.files['Web/index.html']);
 assert.equal(receipt.hostedIndexSha256,createHash('sha256').update(readFileSync(join(archive,'Web/index.html'))).digest('hex'));
 assert.equal(receipt.runtime[0].sha256,build.manifest.files['Web/Build/Web.data']);
 assert.equal(receipt.runtime.length,1);
});
check('remote materialization is idempotent and counts only emitted bytes',()=>{
 const size=folder=>readdirSync(folder,{withFileTypes:true}).reduce((sum,entry)=>sum+(entry.isDirectory()?size(join(folder,entry.name)):readFileSync(join(folder,entry.name)).length),0);
 assert.equal(hostedBytes,size(archive));assert.equal(materializeHistory(root,remoteOut,only,{remoteRuntime:true}),hostedBytes);
});
check('omitted data with mismatching manifest hash still fails verification',()=>{
 const bad=structuredClone(only);bad.builds[0].manifest.files['Web/Build/Web.data']='0'.repeat(64);
 assert.throws(()=>materializeHistory(root,join(root,'bad-data'),bad,{remoteRuntime:true}),/hash mismatch/);
});
check('remote mode verifies every Web Git blob including omitted data',()=>{
 for(const path of ['Published/Web/Build/Web.data','Published/Web/Build/Web.wasm','Published/Web/StreamingAssets/settings.json']){
  const bad=structuredClone(only);bad.builds[0].tree.find(file=>file.path===path).blob='0'.repeat(40);
  assert.throws(()=>materializeHistory(root,join(root,'bad-blob-'+checks+'-'+path.split('/').at(-1)),bad,{remoteRuntime:true}),/Git blob mismatch/);
 }
});
check('remote mode never silently replaces an exact local archive index',()=>assert.throws(()=>materializeHistory(root,localOut,only,{remoteRuntime:true}),/Archive collision/));
console.log(`${checks} checks passed`);
