// Git-backed channel storage checks: promotion must save bytes without changing
// channel identity, history, evidence or runtime URL queries. Fixtures are tiny
// synthetic files, not a Unity gameplay or browser acceptance claim.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {mkdtempSync,mkdirSync,writeFileSync,readFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,dirname,resolve,relative} from 'node:path';
import {rewriteChannelPlayer,planChannelStorage,channelAssetUrl} from './unity-channel-storage.mjs';
import {collectHistory,materializeHistory} from './unity-build-history.mjs';
import {materializePublished} from './unity-git-blobs.mjs';

const fixture=mkdtempSync(join(tmpdir(),'ashenspire-channel-storage-'));
const root=join(fixture,'repo');mkdirSync(root);
const git=args=>execFileSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true,stdio:['ignore','pipe','pipe']}).trim();
git(['init']);git(['config','user.name','Channel fixture']);git(['config','user.email','channel@example.invalid']);git(['config','core.autocrlf','false']);
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const runtimeNames=['Web.loader.js','Web.data','Web.framework.js','Web.wasm'];
const suffix='?build='+ 'd'.repeat(64)+'&quality=high#keep';
const modern=`<!doctype html>\r\n<meta charset="utf-8"><title>灰 · unchanged channel</title>
<script src="Build/Web.loader.js${suffix}"></script>
<script>
const channel=location.pathname.split('/').find(p=>['dev','test','release','main'].includes(p))||'local';
createUnityInstance(canvas,{dataUrl:'Build/Web.data${suffix}',frameworkUrl:'Build/Web.framework.js${suffix}',codeUrl:'Build/Web.wasm${suffix}',streamingAssetsUrl:'StreamingAssets',productVersion:'0.0.12.0'});
</script>`;
const bytes=new Map();
function put(path,value){const data=Buffer.isBuffer(value)?value:Buffer.from(value);bytes.set(path,data);mkdirSync(dirname(join(root,path)),{recursive:true});writeFileSync(join(root,path),data);}
function manifest(version,number){put('Published/build.json',JSON.stringify({version,buildNumber:number,sourceCommit:'a'.repeat(40),builtAt:'2026-09-07T00:00:00Z',files:Object.fromEntries([...bytes].filter(([path])=>path.startsWith('Published/Web/')).map(([path,data])=>[path.slice(10),hash(data)]))}));}
function commit(message){git(['add','Published']);git(['commit','-m',message]);return git(['rev-parse','HEAD']);}
function selected(channel,commit){
 const files=git(['ls-tree','-r',commit,'--','Published']).split('\n').filter(Boolean).map(line=>{const m=/^(\d+) blob ([a-f0-9]+)\t(.+)$/.exec(line);assert(m);return {mode:m[1],blob:m[2],path:m[3]};});
 return {channel,commit,files,html:execFileSync('git',['show',commit+':Published/Web/index.html'],{cwd:root,encoding:'utf8',windowsHide:true})};
}
put('Published/Web/index.html','<!doctype html><p>Legacy player, original 0.9.0</p>');
put('Published/changelog.json',JSON.stringify({Added:['Original checkpoint'],Changed:[],Fixed:[]}));
put('Published/NativeEvidence/Guide.md','![Exact screenshot](Shots/phone.png)\r\n');
put('Published/NativeEvidence/Shots/phone.png',Buffer.from([137,80,78,71,0,255,10,13]));
put('Published/AudioEvidence/hit.wav',Buffer.from([82,73,70,70,0,128,255]));
put('Published/validation.json','{"passed":true,"origin":"legacy"}');
put('Published/Web.zip',Buffer.from([80,75,0,255]));
manifest('0.9.0');const legacy=commit('Legacy');
put('Published/Web/index.html',modern);
for(const [index,name] of runtimeNames.entries())put('Published/Web/Build/'+name,Buffer.from([0,10,13,255,index]));
put('Published/Web/build-source.json','{"sourceDigest":"'+ 'd'.repeat(64)+'"}');
put('Published/Web/renderer-workaround.json','{"algorithm":"fixture"}');
manifest('0.0.12.0',12);const current=commit('Current player');
put('Published/validation.json','{"passed":true,"origin":"test promotion"}');
manifest('0.0.12.0',12);const evidence=commit('Same player, new validation');
put('Published/NativeEvidence/Shots/phone.png',Buffer.from([137,80,78,71,1,255,10,13]));
manifest('0.0.12.0',12);const changedEvidence=commit('Different screenshot, same player');
put('Published/Web/Build/Web.wasm',Buffer.from([0,10,13,255,99]));
manifest('0.0.12.0',12);const changedRuntime=commit('Different player, same version');
const history=collectHistory(root,{dev:changedRuntime,test:current,release:legacy,main:evidence});
let checks=0;
function check(name,action){action();checks++;console.log('PASS '+name);}
const clone=value=>structuredClone(value);
const rows=[selected('dev',current),selected('test',evidence),selected('release',changedEvidence),selected('main',changedRuntime)];
const before=JSON.stringify({rows,history});
const plan=planChannelStorage(rows,history.builds);
const channel=name=>plan.channels[name];
const webPath='Published/Web/Build/Web.wasm';
check('promotion and evidence changes retain actual Web archive identity',()=>{
 assert.equal(history.builds.length,3);
 assert.equal(channel('dev').archiveId,channel('test').archiveId);
 assert.equal(channel('dev').archiveId,channel('release').archiveId);
 assert.notEqual(channel('dev').archiveId,channel('main').archiveId);
 assert(history.builds.some(build=>build.id===channel('dev').archiveId));
});
check('planning does not mutate channel or immutable history inputs',()=>assert.equal(JSON.stringify({rows,history}),before));
check('tree enumeration order cannot change immutable identity or evidence reuse',()=>{
 const shuffled=clone(rows);for(const row of shuffled)row.files.reverse();
 const archives=clone(history.builds);for(const build of archives)build.tree.reverse();
 const result=planChannelStorage(shuffled,archives);
 for(const row of rows){assert.equal(result.channels[row.channel].archiveId,channel(row.channel).archiveId);assert.deepEqual(result.channels[row.channel].sharedFolders,channel(row.channel).sharedFolders);}
});
check('identical evidence folders share their complete original tree',()=>{
 assert.equal(channelAssetUrl(plan,'dev','Published/NativeEvidence/Guide.md'),'NativeEvidence/Guide.md');
 assert.equal(channelAssetUrl(plan,'test','Published/NativeEvidence/Guide.md'),'../dev/NativeEvidence/Guide.md');
 assert(!channel('test').copyPaths.some(path=>path.startsWith('Published/NativeEvidence/')));
});
check('one changed screenshot retains the whole differing evidence folder',()=>{
 for(const path of ['Published/NativeEvidence/Guide.md','Published/NativeEvidence/Shots/phone.png'])assert(channel('release').copyPaths.includes(path));
 assert.equal(channelAssetUrl(plan,'release','Published/NativeEvidence/Guide.md'),'NativeEvidence/Guide.md');
 assert.equal(channelAssetUrl(plan,'release','Published/AudioEvidence/hit.wav'),'../dev/AudioEvidence/hit.wav');
});
check('folder membership and excluded downloads still prevent false evidence sharing',()=>{
 for(const mutation of ['added','removed','download']){
  const selectedRows=clone(rows.slice(0,2));const changed=selectedRows[1];
  if(mutation==='removed')changed.files=changed.files.filter(file=>file.path!=='Published/NativeEvidence/Shots/phone.png');
  else changed.files.push({path:'Published/NativeEvidence/'+(mutation==='download'?'archive.zip':'extra.json'),blob:'c'.repeat(40),mode:'100644'});
  const result=planChannelStorage(selectedRows,history.builds);assert.equal(channelAssetUrl(result,'test','Published/NativeEvidence/Guide.md'),'NativeEvidence/Guide.md');assert(result.channels.test.copyPaths.includes('Published/NativeEvidence/Guide.md'));
 }
});
check('sharing works in any channel order and never depends on dev being selected',()=>{
 const selectedRows=[selected('test',current),selected('release',current),selected('main',current)];const result=planChannelStorage(selectedRows,history.builds);
 assert.equal(result.channels.dev,undefined);assert.equal(channelAssetUrl(result,'main','Published/NativeEvidence/Guide.md'),'../test/NativeEvidence/Guide.md');
 assert(result.channels.test.copyPaths.includes('Published/NativeEvidence/Guide.md'));
});
check('URL encoding preserves spaces, Unicode and literal percent signs in evidence names',()=>{
 const selectedRows=clone(rows.slice(0,2)),path='Published/NativeEvidence/灰 100% #?.png';
 for(const row of selectedRows)row.files.push({path,blob:'c'.repeat(40),mode:'100644'});
 const result=planChannelStorage(selectedRows,history.builds),url=new URL(channelAssetUrl(result,'test',path),'https://example.invalid/game/test/');
 assert.equal(url.search,'');assert.equal(url.hash,'');assert.equal(decodeURIComponent(url.pathname),'/game/dev/NativeEvidence/灰 100% #?.png');
});
check('per-channel validation and manifests remain local even when equal',()=>{
 for(const row of rows)for(const path of ['Published/build.json','Published/validation.json','Published/changelog.json']){
  assert(channel(row.channel).copyPaths.includes(path));assert.equal(channelAssetUrl(plan,row.channel,path),path.slice(10));
 }
});
check('same version with changed runtime bytes selects a distinct archived payload',()=>{
 assert.notEqual(channelAssetUrl(plan,'dev',webPath),channelAssetUrl(plan,'main',webPath));
 assert(!channel('dev').copyPaths.includes(webPath));
});
check('modern channels keep loader HTML at their own browser location',()=>{
 for(const row of rows){assert.equal(channel(row.channel).player.rewritten,true);assert.equal(channelAssetUrl(plan,row.channel,'Published/Web/index.html'),'Web/index.html');
  const html=channel(row.channel).player.html;assert(html.includes("const channel=location.pathname.split('/')"));assert(!html.includes('<base'));assert(!html.includes('location.replace'));assert(!html.includes('http-equiv="refresh"'));
 }
});
check('exactly the four runtime URL paths change and every query fragment is retained',()=>{
 const id=channel('dev').archiveId,result=rewriteChannelPlayer(modern,id);assert(result.rewritten);
 let restored=result.html;for(const name of runtimeNames){const original='Build/'+name+suffix,shared='../../builds/'+id+'/Web/Build/'+name+suffix;assert(result.html.includes(shared),name);restored=restored.replace(shared,original);}
 assert.equal(restored,modern);assert.equal(result.html.match(/quality=high#keep/g).length,4);
});
check('queryless runtime URLs also preserve unrelated text',()=>{
 const original=modern.split(suffix).join(''),result=rewriteChannelPlayer(original,channel('dev').archiveId);assert(result.rewritten);
 let restored=result.html;for(const name of runtimeNames)restored=restored.replace('../../builds/'+channel('dev').archiveId+'/Web/Build/'+name,'Build/'+name);assert.equal(restored,original);
});
check('unknown loader shapes fall back to byte-identical local Web storage',()=>{
 const row=selected('dev',legacy),legacyPlan=planChannelStorage([row],history.builds),choice=legacyPlan.channels.dev;
 assert.equal(choice.player.rewritten,false);assert.equal(choice.player.html,row.html);
 for(const file of row.files.filter(file=>file.path.startsWith('Published/Web/')))assert(choice.copyPaths.includes(file.path));
 assert.equal(channelAssetUrl(legacyPlan,'dev','Published/Web/index.html'),'Web/index.html');
 assert.equal(choice.archiveId,history.channels.release[0]);
});
check('partial or duplicate loader patterns never get partly rewritten',()=>{
 const cases=[modern.replace("codeUrl:'Build/Web.wasm","codeUrl:'Other/Web.wasm"),modern.replace('<script src=', '<script src="Build/Web.loader.js'+suffix+'"></script><script src='),'<script>const dataUrl="Build/Web.data";</script>','<base href="/another/">'+modern,modern.replace("dataUrl:'Build/Web.data", "dataUrl:'https://example.invalid/Build/Web.data"),modern.replace("codeUrl:'Build/Web.wasm", "codeUrl:'../Build/Web.wasm")];
 for(const html of cases){const result=rewriteChannelPlayer(html,channel('dev').archiveId);assert.equal(result.rewritten,false);assert.equal(result.html,html);}
});
check('SHA-256 Git object identities are accepted without conflating different blobs',()=>{
 const row=clone(rows[0]);row.commit='a'.repeat(64);for(const file of row.files)file.blob=hash(file.blob);
 const archive={id:'build-'+ 'b'.repeat(20),tree:row.files.filter(file=>file.path.startsWith('Published/Web/')).map(({path,blob})=>({path,blob}))};
 assert.equal(planChannelStorage([row],[archive]).channels.dev.archiveId,archive.id);
 row.files.find(file=>file.path===webPath).blob='e'.repeat(64);assert.throws(()=>planChannelStorage([row],[archive]),/exact immutable archive/);
});
check('non-text templates and unresolved channel commits are refused',()=>{
 for(const html of [null,{},Buffer.from(modern)])assert.throws(()=>rewriteChannelPlayer(html,channel('dev').archiveId));
 for(const commit of ['HEAD','origin/dev','abc','']){const row=clone(rows[0]);row.commit=commit;assert.throws(()=>planChannelStorage([row],history.builds));}
});
check('unsafe archive destinations are refused',()=>{
 for(const id of ['../dev','build-'+ 'a'.repeat(19),'build-'+ 'a'.repeat(21),'build-'+ 'A'.repeat(20),'build-'+ 'a'.repeat(20)+'/../escape','https://outside.invalid','',null])assert.throws(()=>rewriteChannelPlayer(modern,id));
});
check('unknown channel or unplanned artifact cannot invent a public URL',()=>{
 for(const name of ['unknown','../dev','DEV',''])assert.throws(()=>channelAssetUrl(plan,name,'Published/build.json'));
 for(const path of ['Published/missing.txt','Published/../escape','Web/index.html'])assert.throws(()=>channelAssetUrl(plan,'dev',path));
});
check('unsafe paths, special file modes, invalid blobs and collisions refuse planning',()=>{
 const base=selected('dev',current);
 for(const path of ['Published/../escape','Published//bad','Published/./bad','Published/Web\\bad','Published/x\ny','Published/C:/bad','Published/NUL.txt']){
  const row=clone(base);row.files.push({path,blob:'a'.repeat(40),mode:'100644'});assert.throws(()=>planChannelStorage([row],history.builds),path);
 }
 for(const patch of [{blob:'not-a-blob'},{mode:'120000'},{mode:'040000'}]){const row=clone(base);Object.assign(row.files[0],patch);assert.throws(()=>planChannelStorage([row],history.builds));}
 const duplicate=clone(base);duplicate.files.push(clone(duplicate.files[0]));assert.throws(()=>planChannelStorage([duplicate],history.builds));
 const alias=clone(base);alias.files.push({path:'Published/nativeevidence/other.txt',blob:'a'.repeat(40),mode:'100644'});assert.throws(()=>planChannelStorage([alias],history.builds));
});
check('unselected, duplicate and unsafe channel identities are refused',()=>{
 for(const name of ['other','DEV','../dev','']){const row=clone(rows[0]);row.channel=name;assert.throws(()=>planChannelStorage([row],history.builds));}
 assert.throws(()=>planChannelStorage([rows[0],clone(rows[0])],history.builds));
});
check('an unmatched full Web tree is refused rather than trusting version or digest',()=>{
 const altered=clone(rows[0]);altered.files.find(file=>file.path===webPath).blob='f'.repeat(40);
 assert.throws(()=>planChannelStorage([altered],history.builds),/exact immutable archive/);
});
check('missing and additional Web artifacts invalidate archive reuse',()=>{
 for(const mutation of ['missing','extra']){const row=clone(rows[0]);if(mutation==='missing')row.files=row.files.filter(file=>file.path!==webPath);else row.files.push({path:'Published/Web/extra.bin',blob:'a'.repeat(40),mode:'100644'});
  assert.throws(()=>planChannelStorage([row],history.builds),/exact immutable archive/);
 }
});
check('public channel URLs resolve to exact committed bytes after materialization',()=>{
 const out=join(fixture,'site');materializeHistory(root,out,history);
 for(const row of rows){materializePublished(root,row.commit,channel(row.channel).copyPaths,join(out,row.channel));mkdirSync(join(out,row.channel,'Web'),{recursive:true});writeFileSync(join(out,row.channel,'Web/index.html'),channel(row.channel).player.html);}
 for(const row of rows){
  for(const file of row.files.filter(file=>!(/\.(zip|apk)$/i.test(file.path))&&file.path!=='Published/Web/index.html')){
   const url=new URL(channelAssetUrl(plan,row.channel,file.path),'https://example.invalid/AshenSpire-Unity/'+row.channel+'/');assert.equal(url.origin,'https://example.invalid');assert(url.pathname.startsWith('/AshenSpire-Unity/'));
   const target=resolve(out,decodeURIComponent(url.pathname.slice('/AshenSpire-Unity/'.length)));assert(!relative(out,target).startsWith('..'));assert(existsSync(target),file.path);
   const expected=execFileSync('git',['show',row.commit+':'+file.path],{cwd:root,windowsHide:true,maxBuffer:1024*1024});assert.deepEqual(readFileSync(target),expected,file.path+' for '+row.channel);
  }
  const html=readFileSync(join(out,row.channel,'Web/index.html'),'utf8');
  for(const name of runtimeNames){const match=html.match(new RegExp("(?:src=\"|(?:dataUrl|frameworkUrl|codeUrl):')([^\"']*"+name.replaceAll('.','\\.')+"[^\"']*)"));assert(match,name);
   const url=new URL(match[1],'https://example.invalid/AshenSpire-Unity/'+row.channel+'/Web/?channelQuery=retained');assert.equal(url.search,'?build='+ 'd'.repeat(64)+'&quality=high');assert.equal(url.hash,'#keep');
   const target=join(out,decodeURIComponent(url.pathname.slice('/AshenSpire-Unity/'.length)));const expected=execFileSync('git',['show',row.commit+':Published/Web/Build/'+name],{cwd:root,windowsHide:true});assert.deepEqual(readFileSync(target),expected);
  }
 }
 // Sharing an entire evidence folder keeps its own relative image links valid.
 const guide=new URL(channelAssetUrl(plan,'test','Published/NativeEvidence/Guide.md'),'https://example.invalid/AshenSpire-Unity/test/');const image=new URL('Shots/phone.png',guide);
 assert.deepEqual(readFileSync(join(out,image.pathname.slice('/AshenSpire-Unity/'.length))),execFileSync('git',['show',evidence+':Published/NativeEvidence/Shots/phone.png'],{cwd:root,windowsHide:true}));
});
console.log(`${checks} checks passed; fixture preserved at ${fixture}`);
