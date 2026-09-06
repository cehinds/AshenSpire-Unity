// Assemble one complete Pages site from all selected channel refs.
// Missing channels are explicit. No source code, credentials or whole repository
// tree is copied: only Published/ and the inherited standalone reference player.
import {execFileSync} from 'node:child_process';
import {mkdirSync,writeFileSync,readFileSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
const root=process.cwd(),out=resolve('_site'),channels=['dev','test','release','main'];
const git=(args,encoding='utf8')=>execFileSync('git',args,{cwd:root,encoding,maxBuffer:150*1024*1024});
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const repo='https://github.com/cehinds/AshenSpire-Unity';
const style=`*{box-sizing:border-box}body{margin:0;background:#11171b;color:#e5e3db;font:16px/1.6 system-ui}main{max-width:1120px;margin:auto;padding:52px 22px}a{color:#e9bd78}h1{font:clamp(32px,6vw,60px)/1.1 Georgia}h2{font:28px Georgia}.kicker{text-transform:uppercase;letter-spacing:.2em;color:#c6a46f;font-size:12px}.muted{color:#a6b0b3}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:20px}.card{border:1px solid #354044;border-radius:14px;background:#1b252a;padding:24px}.button{display:inline-block;padding:12px 20px;border:1px solid #d6aa68;border-radius:8px;background:#d6aa68;color:#152025;text-decoration:none;margin:8px 10px 8px 0;font-weight:650}.secondary{background:none;color:#e9bd78}.shots{display:flex;gap:16px;overflow:auto;padding:8px 0 20px}.shots img{max-height:460px;border:1px solid #354044;border-radius:10px}code{word-break:break-all;font-size:12px}nav{display:flex;gap:20px;flex-wrap:wrap}section{margin:35px 0}li{margin-bottom:8px}.badge{display:inline-block;background:#3e3426;color:#f0ca8b;border-radius:20px;padding:4px 12px;font-size:12px}`;
const page=(title,body)=>`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${escape(title)} · AshenSpire Unity</title><style>${style}</style><main>${body}</main></html>`;
mkdirSync(out,{recursive:true});writeFileSync(join(out,'.nojekyll'),'');
const summaries=[];
for(const channel of channels){
 const dir=join(out,channel);mkdirSync(dir,{recursive:true});
 const ref=`origin/${channel}`;let manifest,paths=[];
 try{manifest=JSON.parse(git(['show',`${ref}:Published/build.json`]));paths=git(['ls-tree','-r','--name-only',ref,'Published']).trim().split('\n').filter(Boolean);}catch{}
 let body=`<nav><a href="../">All builds</a>${channels.filter(c=>c!==channel).map(c=>`<a href="../${c}/">${c}</a>`).join('')}</nav><p class="kicker">${channel} channel</p><h1>AshenSpire</h1>`;
 if(!manifest){body+='<p>No build has been selected for this channel yet.</p><p class="muted">Promotion is separate from publishing. The dev build is the current work in progress.</p>';summaries.push({channel,available:false});}
 else{
  for(const path of paths){const relative=path.slice('Published/'.length);if(relative.includes('..'))throw new Error('Unsafe artifact path');const target=join(dir,relative);mkdirSync(dirname(target),{recursive:true});writeFileSync(target,git(['show',`${ref}:${path}`],null));}
  const commit=git(['rev-parse',ref]).trim();
  let changes={Added:[],Changed:[],Fixed:[],KnownIssues:[],WhatToTest:[]};
  try{changes=JSON.parse(git(['show',`${ref}:Published/changelog.json`]));}catch{}
  const screenshots=paths.filter(p=>/^Published\/Screenshots\/.*\.png$/.test(p)||/^Published\/ClassEvidence\/[^/]+\/(04-phone-combat|10-reward-0|11-next-map|15-campaign-defeat|26-action-impact|32-viewport-1|33-inset-canvas|35-dense-settings)\.png$/.test(p)||/^Published\/FeedbackEvidence\/(26-normal-impact|26-normal-impact-settled|28-reduced-impact|31-interrupted-menu)\.png$/.test(p));
  const windowsDownload=paths.includes('Published/Windows.zip')?'<a class="button secondary" href="Windows.zip" download>Download Windows player</a>':'';
  body+=`<p><span class="badge">${escape(manifest.stage)} · ${escape(manifest.version)}</span></p><p class="muted">Mobile-first sprite deckbuilding: four wanderers, three acts, equipment and a complete expedition.</p><a class="button" href="Web/?build=${escape(manifest.sourceDigest)}">Play ${channel}</a><a class="button secondary" href="Web.zip" download>Download web build</a><p>Built ${escape(manifest.builtAt)} · Unity ${escape(manifest.unityVersion)}</p><p class="muted">Channel commit <a href="${repo}/commit/${commit}"><code>${commit.slice(0,12)}</code></a> · Source <code>${escape(manifest.sourceCommit.slice(0,12))}</code></p>`;
  body+=windowsDownload;
  if(paths.includes('Published/Android.apk'))body+='<a class="button secondary" href="Android.apk" download>Download Android test APK</a>';
  if(paths.includes('Published/MobileEvidence/Guide.md'))body+=`<p><a href="${repo}/blob/${commit}/docs/Mobile-Viewport-0.7.0.md">Mobile viewport changes and testing guide</a></p>`;
  if(paths.includes('Published/AuthoringEvidence/Content-Authoring.md'))body+=`<section><h2>Make it yours</h2><p>Edit cards, effects, rewards and feedback in Unity, or use CSV tables.</p><nav><a href="${repo}/blob/${commit}/docs/Content-Authoring-0.6.0.md">Content editing guide</a><a href="AuthoringEvidence/Cards.csv" download>Cards CSV</a><a href="AuthoringEvidence/FeedbackCues.csv" download>Feedback cues CSV</a></nav></section>`;
  body+=`<section><h2>What changed</h2>${Object.entries(changes).map(([heading,items])=>`<h3>${escape(heading.replace(/([a-z])([A-Z])/g,'$1 $2'))}</h3><ul>${items.map(item=>`<li>${escape(item)}</li>`).join('')}</ul>`).join('')}</section>`;
  if(screenshots.length)body+=`<section><h2>Captured from this checkpoint</h2><div class="shots">${screenshots.map(path=>`<a href="${path.slice(10)}"><img loading="lazy" alt="${escape(path.split('/').pop())}" src="${path.slice(10)}"></a>`).join('')}</div><p class="muted">Screenshots show pixels; test notes distinguish interaction and device verification.</p></section>`;
  const audio=paths.filter(p=>/^Published\/AudioEvidence\/[a-z]+\.wav$/.test(p));
  if(audio.length)body+=`<section><h2>Sound previews</h2><p class="muted">Reference cues before the game's master volume. Physical-device playback still needs testing.</p><div class="grid">${audio.map(path=>`<div class="card"><p>${escape(path.split('/').pop().replace('.wav',''))}</p><audio controls preload="none" style="width:100%" src="${path.slice(10)}"></audio></div>`).join('')}</div></section>`;
  summaries.push({channel,available:true,manifest,commit});
 }
 writeFileSync(join(dir,'index.html'),page(channel,body));
}
const cards=summaries.map(s=>`<article class="card"><p class="kicker">${s.channel}</p><h2>${s.available?escape(s.manifest.version):'Awaiting a build'}</h2><p>${s.available?escape(s.manifest.stage):'No candidate selected'}</p><a href="${s.channel}/">${s.available?'Play, changes & screenshots':'View channel'} →</a></article>`).join('');
writeFileSync(join(out,'index.html'),page('Build library',`<nav><a href="${repo}">Source repository</a><a href="reference/AshenSpire.html">Original browser baseline</a></nav><p class="kicker">AshenSpire · Development library</p><h1>Follow the ember.<br>Play the next build.</h1><p class="muted">A mobile-first Unity adaptation. Every channel keeps its own playable build, changelog and evidence.</p><section class="grid">${cards}</section><p class="muted">Dev: work in progress · Test: selected testing candidate · Release: release candidate · Main: approved stable game.</p>`));
mkdirSync(join(out,'reference'),{recursive:true});writeFileSync(join(out,'reference/AshenSpire.html'),readFileSync(join(root,'AshenSpire.html')));
writeFileSync(join(out,'channels.json'),JSON.stringify(summaries,null,2));
console.log(`Unity Pages: ${channels.length+1} pages assembled`);
