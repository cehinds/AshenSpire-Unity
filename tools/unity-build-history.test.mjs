// Focused Git-backed regressions for build identity, promotion and preservation.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {mkdirSync,writeFileSync,readFileSync,mkdtempSync} from 'node:fs';
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
console.log(`${checks} checks passed`);
