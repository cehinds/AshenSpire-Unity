// Assemble one complete Pages site from all selected channel refs.
// Missing channels are explicit. No source code, credentials or whole repository
// tree is copied: only Published/ and the inherited standalone reference player.
import {execFileSync} from 'node:child_process';
import {mkdirSync,writeFileSync,readFileSync,readdirSync,statSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {collectHistory,resolvePullRequests,materializeHistory,publicHistory} from './unity-build-history.mjs';
const root=process.cwd(),out=resolve(process.env.UNITY_PAGES_OUT || '_site'),channels=['dev','test','release','main'];
const git=(args,encoding='utf8')=>execFileSync('git',args,{cwd:root,encoding,maxBuffer:150*1024*1024,stdio:['ignore','pipe','pipe']});
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const repo='https://github.com/cehinds/AshenSpire-Unity';
const style=`*{box-sizing:border-box}body{margin:0;background:#11171b;color:#e5e3db;font:16px/1.6 system-ui}main{max-width:1120px;margin:auto;padding:52px 22px}a{color:#e9bd78}h1{font:clamp(32px,6vw,60px)/1.1 Georgia}h2{font:28px Georgia}.kicker{text-transform:uppercase;letter-spacing:.2em;color:#c6a46f;font-size:12px}.muted{color:#a6b0b3}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:20px}.card{border:1px solid #354044;border-radius:14px;background:#1b252a;padding:24px}.button{display:inline-block;padding:12px 20px;border:1px solid #d6aa68;border-radius:8px;background:#d6aa68;color:#152025;text-decoration:none;margin:8px 10px 8px 0;font-weight:650}.secondary{background:none;color:#e9bd78}.shots{display:flex;gap:16px;overflow:auto;padding:8px 0 20px}.shots img{max-height:460px;border:1px solid #354044;border-radius:10px}code{word-break:break-all;font-size:12px}nav{display:flex;gap:20px;flex-wrap:wrap}section{margin:35px 0}li{margin-bottom:8px}.badge{display:inline-block;background:#3e3426;color:#f0ca8b;border-radius:20px;padding:4px 12px;font-size:12px}`;
const page=(title,body)=>`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${escape(title)} · AshenSpire Unity</title><style>${style}</style><main>${body}</main></html>`;
mkdirSync(out,{recursive:true});writeFileSync(join(out,'.nojekyll'),'');
const history=collectHistory(root);
await resolvePullRequests(history.builds,'cehinds/AshenSpire-Unity');
materializeHistory(root,out,history);
const buildById=new Map(history.builds.map(build=>[build.id,build]));
const dateLabel=value=>value&&!Number.isNaN(Date.parse(value))?new Date(value).toISOString().slice(0,16).replace('T',' ')+' UTC':'Build date unknown';
const prLinks=build=>build.pullRequests.length?build.pullRequests.map(pr=>`<a href="${escape(pr.url)}">PR #${pr.number}</a>`).join(', '):'<span class="muted">Associated PR unknown</span>';
const shortChanges=build=>Object.entries(build.changes).map(([kind,items])=>items.length?`<h3>${escape(kind)}</h3><ul>${items.slice(0,3).map(item=>`<li>${escape(item)}</li>`).join('')}</ul>`:'').join('') || '<p class="muted">No structured change notes were recorded for this build.</p>';
function buildCard(build,prefix,previous){return `<article class="card"><p class="kicker">Build ${escape(build.buildNumber)}</p><h2>${escape(build.manifest.version)}</h2><p>${escape(dateLabel(build.builtAt))} · ${prLinks(build)}</p><a class="button" href="${prefix}builds/${build.id}/">Open this build</a>${previous?`<a href="${repo}/compare/${previous.commit}...${build.commit}">Changes since previous channel build</a>`:'<p class="muted">First recorded build in this channel.</p>'}${shortChanges(build)}</article>`;}
function buildPage(build,prefix){
 const exact=`${repo}/blob/${build.commit}/Published`;
 return `<nav><a href="${prefix}">Latest channels</a><a href="${prefix}history/">All previous builds</a></nav><p class="kicker">Archived build ${escape(build.buildNumber)}</p><h1>AshenSpire ${escape(build.manifest.version)}</h1><p>Built ${escape(build.builtAt || 'date unknown')} · ${prLinks(build)}</p><p class="muted">Archived player bytes and version labels are preserved. Older versions may have known bugs and different save formats.</p><a class="button" href="${prefix}builds/${build.id}/Web/">Play this build</a>${['Web.zip','Windows.zip','Android.apk','Companion.zip'].filter(file=>build.paths.includes(`Published/${file}`)).map(file=>`<a class="button secondary" href="${exact}/${file}?raw=true">Download ${escape(file)}</a>`).join('')}<p>Source <code>${escape(build.manifest.sourceCommit || 'unknown')}</code> · <a href="${repo}/commit/${build.commit}">Archive commit ${build.commit.slice(0,12)}</a></p><p><a href="${prefix}builds/${build.id}/build.json">Original build manifest</a> · <a href="${exact}">All evidence at this exact commit</a></p><section><h2>Changes recorded with this build</h2>${shortChanges(build)}<a href="${exact}/changelog.json">Full archived changelog</a></section>`;
}
for(const build of history.builds){writeFileSync(join(out,'builds',build.id,'index.html'),page(`Build ${build.buildNumber}`,buildPage(build,'../../')));}
for(const channel of channels){
 for(const id of history.channels[channel]){
  const build=buildById.get(id),dir=join(out,channel,'builds',id);mkdirSync(dir,{recursive:true});
  writeFileSync(join(dir,'index.html'),page(`${channel} build ${build.buildNumber}`,buildPage(build,'../../../')));
 }
}
const summaries=[];
for(const channel of channels){
 const dir=join(out,channel);mkdirSync(dir,{recursive:true});
 const ref=`origin/${channel}`;let manifest,paths=[];
 try{manifest=JSON.parse(git(['show',`${ref}:Published/build.json`]));paths=git(['ls-tree','-r','--name-only',ref,'Published']).trim().split('\n').filter(Boolean);}catch{}
 let body=`<nav><a href="../">All builds</a>${channels.filter(c=>c!==channel).map(c=>`<a href="../${c}/">${c}</a>`).join('')}</nav><p class="kicker">${channel} channel</p><h1>AshenSpire</h1>`;
 if(!manifest){body+='<p>No build has been selected for this channel yet.</p><p class="muted">Promotion is separate from publishing. The dev build is the current work in progress.</p>';summaries.push({channel,available:false});}
 else{
  // Downloads retain their exact committed bytes on GitHub. Pages keeps playable
  // runtimes and evidence without duplicating every large platform archive.
  for(const path of paths){if(/\.(zip|apk)$/i.test(path))continue;const relative=path.slice('Published/'.length);if(relative.includes('..'))throw new Error('Unsafe artifact path');const target=join(dir,relative);mkdirSync(dirname(target),{recursive:true});writeFileSync(target,git(['show',`${ref}:${path}`],null));}
  const commit=git(['rev-parse',ref]).trim();
  let changes={Added:[],Changed:[],Fixed:[],KnownIssues:[],WhatToTest:[]};
  try{changes=JSON.parse(git(['show',`${ref}:Published/changelog.json`]));}catch{}
  let screenshots=paths.filter(p=>/^Published\/MobileEvidence\/02-seed-(entered|committed)\.png$/.test(p)||/^Published\/SeedEvidence\/(03-draft-before|seed-01-first-campaign|seed-02-selected|seed-03-invalid|seed-05-landscape-unfocused|seed-07-landscape-selected|seed-09-portrait-return)\.png$/.test(p)||/^Published\/Screenshots\/.*\.png$/.test(p)||/^Published\/InterruptionEvidence\/(03-draft-returned|04-map-landscape-paused|05-selection-returned|06-inspection-returned|07-feedback-return|11-reloaded)\.png$/.test(p)||/^Published\/FeedbackEvidence\/(26-normal-impact|26-normal-impact-settled|28-reduced-impact|31-interrupted-menu)\.png$/.test(p));
  if(paths.includes('Published/FoundationEvidence/gallery.json')){
   screenshots=JSON.parse(git(['show',`${ref}:Published/FoundationEvidence/gallery.json`]));
   if(!Array.isArray(screenshots)||screenshots.some(p=>typeof p!=='string'||!paths.includes(p)||!/^Published\/FoundationEvidence\/(Browser|Campaign)\/[^/]+\.png$/.test(p)))throw new Error('Invalid foundation screenshot manifest');
  }
  const windowsDownload=paths.includes('Published/Windows.zip')?`<a class="button secondary" href="${repo}/blob/${commit}/Published/Windows.zip?raw=true">Download Windows player</a>`:'';
  body+=`<p><span class="badge">${escape(manifest.stage)} · ${escape(manifest.version)}</span></p><p class="muted">Mobile-first sprite deckbuilding: four wanderers, three acts, equipment and a complete expedition.</p><a class="button" href="Web/?build=${escape(manifest.sourceDigest)}">Play ${channel}</a><a class="button secondary" href="${repo}/blob/${commit}/Published/Web.zip?raw=true">Download web build</a><p>Built ${escape(manifest.builtAt)} · Unity ${escape(manifest.unityVersion)}</p><p class="muted">Channel commit <a href="${repo}/commit/${commit}"><code>${commit.slice(0,12)}</code></a> · Source <code>${escape(manifest.sourceCommit.slice(0,12))}</code></p>`;
  body+=windowsDownload;
  if(paths.includes('Published/Companion.zip'))body+=`<a class="button secondary" href="${repo}/blob/${commit}/Published/Companion.zip?raw=true">Download co-op companion for Windows</a><p class="muted">For a shared climb, unzip the companion beside the downloaded Web folder and run Start-Companion.cmd. Open the local address it prints; invite players using its join code.</p>`;
  body+=`<p class="muted">Build ${escape(manifest.buildNumber ?? 'number not recorded in this legacy manifest')} · <a href="#history">Browse this channel's build history</a></p>`;
  if(paths.includes('Published/Android.apk'))body+=`<a class="button secondary" href="${repo}/blob/${commit}/Published/Android.apk?raw=true">Download Android test APK</a>`;
  if(paths.includes('Published/FoundationEvidence/Guide.md'))body+=`<section><h2>Faithful Unity rebuild</h2><p>The original foundation preview is available from the title screen. Full original gameplay integration is in progress; the existing campaign remains playable.</p><nav><a href="${repo}/blob/${commit}/docs/Unity-Parity.md">Foundation and parity checklist</a><a href="FoundationEvidence/Original-Cards.csv" download>Original cards CSV</a><a href="FoundationEvidence/Original-Cards.csv.receipt.json" download>CSV import receipt</a><a href="validation.json">Current validation evidence</a></nav></section>`;
  if(paths.includes('Published/InterruptionEvidence/Guide.md'))body+=`<p><a href="${repo}/blob/${commit}/docs/Interruption-Return-0.8.0.md">Interruption, return and phone testing guide</a></p>`;
  if(paths.includes('Published/SeedEvidence/Guide.md'))body+=`<p><a href="${repo}/blob/${commit}/docs/Seed-Entry-0.8.1.md">Seed entry colors and phone testing guide</a></p>`;
  if(paths.includes('Published/RendererEvidence/Guide.md'))body+=`<p><a href="${repo}/blob/${commit}/docs/Web-Renderer-0.8.2.md">Web rotation fix and build maintenance guide</a></p>`;
  if(paths.includes('Published/ControlEvidence/Guide.md'))body+=`<p><a href="${repo}/blob/${commit}/docs/Control-Diagnostics-0.8.3.md">Tab-return diagnostics and test maintenance guide</a></p>`;
  if(paths.includes('Published/MobileEvidence/Guide.md'))body+=`<p><a href="${repo}/blob/${commit}/docs/Mobile-Viewport-0.7.0.md">Mobile viewport changes and testing guide</a></p>`;
  if(paths.includes('Published/AuthoringEvidence/Content-Authoring.md'))body+=`<section><h2>Make it yours</h2><p>Edit cards, effects, rewards and feedback in Unity, or use CSV tables.</p><nav><a href="${repo}/blob/${commit}/docs/Content-Authoring-0.6.0.md">Content editing guide</a><a href="AuthoringEvidence/Cards.csv" download>Cards CSV</a><a href="AuthoringEvidence/FeedbackCues.csv" download>Feedback cues CSV</a></nav></section>`;
  body+=`<section><h2>What changed</h2>${Object.entries(changes).map(([heading,items])=>`<h3>${escape(heading.replace(/([a-z])([A-Z])/g,'$1 $2'))}</h3><ul>${items.map(item=>`<li>${escape(item)}</li>`).join('')}</ul>`).join('')}</section>`;
  if(screenshots.length)body+=`<section><h2>Captured from this checkpoint</h2><div class="shots">${screenshots.map(path=>`<a href="${path.slice(10)}"><img loading="lazy" alt="${escape(path.split('/').pop())}" src="${path.slice(10)}"></a>`).join('')}</div><p class="muted">Screenshots show pixels; test notes distinguish interaction and device verification.</p></section>`;
  const audio=paths.filter(p=>/^Published\/AudioEvidence\/[a-z]+\.wav$/.test(p));
  if(audio.length)body+=`<section><h2>Sound previews</h2><p class="muted">Reference cues before the game's master volume. Physical-device playback still needs testing.</p><div class="grid">${audio.map(path=>`<div class="card"><p>${escape(path.split('/').pop().replace('.wav',''))}</p><audio controls preload="none" style="width:100%" src="${path.slice(10)}"></audio></div>`).join('')}</div></section>`;
  summaries.push({channel,available:true,manifest,commit});
 }
 const channelBuilds=history.channels[channel].map(id=>buildById.get(id));
 body+=`<section id="history"><h2>Previous ${channel} builds</h2><p class="muted">${channelBuilds.length} distinct committed browser builds, newest first. Each build keeps its original version and date.</p><div class="grid">${channelBuilds.map((build,i)=>buildCard(build,'',channelBuilds[i-1])).reverse().join('')}</div></section>`;
 writeFileSync(join(dir,'index.html'),page(channel,body));
}
const cards=summaries.map(s=>`<article class="card"><p class="kicker">${s.channel}</p><h2>${s.available?escape(s.manifest.version):'Awaiting a build'}</h2><p>${s.available?escape(s.manifest.stage):'No candidate selected'}</p><a href="${s.channel}/">${s.available?'Play, changes & screenshots':'View channel'} →</a></article>`).join('');
writeFileSync(join(out,'index.html'),page('Build library',`<nav><a href="${repo}">Source repository</a><a href="history/">Browse previous builds</a><a href="reference/AshenSpire.html">Original browser snapshot (initial fork)</a></nav><p class="kicker">AshenSpire · Development library</p><h1>Follow the ember.<br>Play the next build.</h1><p class="muted">A mobile-first Unity rebuild of the original game. Every channel keeps its own playable build, changelog and evidence.</p><section class="grid">${cards}</section><p class="muted">Dev: work in progress · Test: selected testing candidate · Release: release candidate · Main: approved stable game.</p>`));
mkdirSync(join(out,'history'),{recursive:true});
writeFileSync(join(out,'history','index.html'),page('Previous builds',`<nav><a href="../">Latest channels</a></nav><p class="kicker">Playable development history</p><h1>Previous builds</h1><p>Version format: game release · roadmap milestone · incremental upgrade · patch. Foundation work remains below 0.1.0.0; 1.0.0.0 denotes the completed game. Older three-part labels are retained as originally built.</p>${channels.map(channel=>{const items=history.channels[channel].map(id=>buildById.get(id));return `<section id="${channel}"><h2><a href="../${channel}/#history">${channel}</a></h2>${items.length?`<div class="grid">${items.map((build,i)=>buildCard(build,`../${channel}/`,items[i-1])).reverse().join('')}</div>`:'<p>No build selected for this channel.</p>'}</section>`;}).join('')}`));
writeFileSync(join(out,'history.json'),JSON.stringify(publicHistory(history),null,2));
mkdirSync(join(out,'reference'),{recursive:true});writeFileSync(join(out,'reference/AshenSpire.html'),readFileSync(join(root,'AshenSpire.html')));
writeFileSync(join(out,'channels.json'),JSON.stringify(summaries,null,2));
const siteBytes=dir=>readdirSync(dir).reduce((total,name)=>{const path=join(dir,name),stat=statSync(path);return total+(stat.isDirectory()?siteBytes(path):stat.size);},0);
const bytes=siteBytes(out),budget=950*1024*1024;
if(bytes>budget)throw new Error(`Pages build library is ${(bytes/1024/1024).toFixed(1)} MiB, over the 950 MiB publication budget. Move historical runtime hosting before adding more builds; history was not silently pruned.`);
console.log(`Unity Pages: ${history.builds.length} archived players across ${channels.length} channels; ${(bytes/1024/1024).toFixed(1)} MiB assembled`);
