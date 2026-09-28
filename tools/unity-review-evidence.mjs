// Generate published review metadata from successful, source-matched captures.
// Usage: node tools/unity-review-evidence.mjs <capture-config.json>
// Config supplies the relevant gallery, coop, performance, enemyCombat, campaign
// audio or saveRecovery folders, and a combatTools array of viewport folders. Omitted suites
// are not claimed as validated in this capture.
// Transport credentials and full state/control dumps are never published here.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const read=file=>JSON.parse(fs.readFileSync(file,'utf8'));
if(!process.argv[2])throw Error('Provide the capture configuration file.');
const config=read(path.resolve(process.argv[2]));
if(config.changelog){
 for(const key of ['Added','Changed','Fixed','KnownIssues','WhatToTest'])
  if(!Array.isArray(config.changelog[key])||config.changelog[key].some(text=>typeof text!=='string'||!text.trim()))throw Error('Invalid changelog section: '+key);
}
const build=read(path.join(root,'Published/build.json'));
const reviewDirectory=config.reviewDirectory||'BuildReview';
if(!/^[A-Za-z0-9_-]+$/.test(reviewDirectory))throw Error('Invalid review directory.');
const output=path.join(root,'Published',reviewDirectory);
const suites=[],screenshots=[],copies=[];
function receipt(folder){
 const stamp=read(path.join(folder,'build-source.json'));
 if(stamp.sourceDigest!==build.sourceDigest||stamp.buildNumber!==build.buildNumber||stamp.version!==build.version)throw Error('Capture is from a different compiled source: '+folder);
}
function checks(name,folder){
 receipt(folder);const result=read(path.join(folder,'checks.json'));
 if(result.success!==true||result.errors.length)throw Error('Capture did not pass: '+name);
 suites.push({suite:name,success:true,checks:result.checks.length,physicalDevice:false});
}
function shot(folder,file,name){
 const source=path.join(folder,file);
 if(!fs.existsSync(source))throw Error('Missing screenshot: '+source);
 const target='Published/'+reviewDirectory+'/'+name+'.png';
 copies.push([source,path.join(root,target)]);screenshots.push(target);
}
let gallery,performance;
if(config.gallery){
 checks('EnemyCatalog',config.gallery);
 gallery=read(path.join(config.gallery,'gallery.json'));
if(!gallery.allCatalogEnemiesRendered||new Set(gallery.screenshots.map(row=>row.id)).size!==19)throw Error('All 19 enemy portraits are required.');
for(const row of gallery.screenshots.filter(row=>row.viewport==='phone'))shot(config.gallery,row.file,'enemy-'+row.id);
}
if(config.coop)for(const seat of ['Host','Guest']){
 const folder=path.join(config.coop,seat);checks('CooperativeBrowser'+seat,folder);
 shot(folder,seat==='Host'?'02-combat.png':'02-own-hand.png','coop-'+seat.toLowerCase());
}
if(config.enemyCombat)checks('EnemyCombat',config.enemyCombat);
if(config.performance){
checks('PerformanceSampling',config.performance);
performance=read(path.join(config.performance,'performance.json'));
for(const scene of ['title','map','combat'])shot(config.performance,scene+'.png',scene);
}
if(config.audio){
 checks('InterfaceAudio',config.audio);
 for(const file of ['01-audio-settings','02-persisted-audio'])shot(config.audio,file+'.png',file);
}
if(config.combatTools){
 if(!Array.isArray(config.combatTools)||!config.combatTools.length)throw Error('combatTools must list viewport capture folders.');
 for(const [index,folder] of config.combatTools.entries()){
  checks('SoloCombatControls'+index,folder);
  for(const file of ['01-draw-inspection','02-played-discard','03-restored-draw-pile'])shot(folder,file+'.png','combat-'+index+'-'+file);
 }
}
if(config.saveRecovery){
 checks('ProfileAndSaveSlots',config.saveRecovery);
 for(const file of ['01-fresh-profile','02-saved-slots','03-restored-profile'])shot(config.saveRecovery,file+'.png',file);
}
if(config.campaign){
const campaign=read(path.join(config.campaign,'playtest.json'));
if(campaign.errors.length||!campaign.fullRunVictory||!campaign.resumeStateMatches)throw Error('Campaign/reload acceptance did not pass.');
// The campaign harness records its served version separately; its folder must
// also carry the source receipt supplied by the caller's exact build run.
receipt(config.campaign);
suites.push({suite:'FoundationCampaignFull',success:true,fullRunVictory:true,resumeStateMatches:true,physicalDevice:false});
}
if(!suites.length||!screenshots.length)throw Error('At least one successful suite and current screenshot are required.');
if(screenshots.length>32)throw Error('Published gallery exceeds the hosting limit.');
const audio=config.audio?read(path.join(config.audio,'audio.json')):null;
if(audio&&audio.success!==true)throw Error('Audio observation did not pass.');
fs.mkdirSync(output,{recursive:true});
for(const [source,target] of copies)fs.copyFileSync(source,target);
if(gallery)fs.writeFileSync(path.join(output,'enemy-gallery.json'),JSON.stringify(gallery,null,2)+'\n');
if(performance)fs.writeFileSync(path.join(output,'performance.json'),JSON.stringify(performance,null,2)+'\n');
if(audio)fs.writeFileSync(path.join(output,'audio.json'),JSON.stringify(audio,null,2)+'\n');
if(config.changelog)fs.writeFileSync(path.join(root,'Published/changelog.json'),JSON.stringify(config.changelog,null,2)+'\n');
fs.writeFileSync(path.join(output,'Guide.md'),`# Build ${build.buildNumber} review\n\nVersion ${build.version}; source \`${build.sourceDigest}\`.\n\nThese images come from this compiled Web player using normal controls. Current suites: ${suites.map(s=>s.suite+(s.checks?' ('+s.checks+' checks)':'')).join(', ')}. They do not prove every encounter, full roadmap acceptance or physical-device behavior.\n\n${config.audio?'Interface clicks and combat previews use separate volumes. These checks observe real Web Audio PCM and gain, mute, zero levels and save/reload; listening quality still needs owner review.\n\n':''}Features F00–F17 remain unaccepted. Owner visual/profile acceptance, complete mode and inventory coverage, listening, target-device budgets, Android and graphical Windows play, iOS delivery and real-player pacing remain open. See [the roadmap](https://github.com/cehinds/AshenSpire-Unity/blob/dev/docs/Unity-Roadmap.md).\n`);
fs.writeFileSync(path.join(root,'Published/presentation.json'),JSON.stringify({guide:'Published/'+reviewDirectory+'/Guide.md',screenshots},null,2)+'\n');
fs.appendFileSync(path.join(output,'Guide.md'),'\n## Captures\n\n'+screenshots.map(file=>'- ['+path.basename(file,'.png').replaceAll('-',' ')+']('+path.basename(file)+')').join('\n')+'\n');
fs.writeFileSync(path.join(root,'Published/validation.json'),JSON.stringify({version:build.version,buildNumber:build.buildNumber,sourceDigest:build.sourceDigest,builtAt:build.builtAt,webCompiled:true,windowsCompiled:true,androidCompiled:true,companionPackaged:true,currentPlayerSuites:suites,ownerAccepted:false,physicalDevice:false,limits:['These selected browser suites do not close the roadmap.','Performance uses software WebGL, not target-device budgets.','Historical captures retain their original source receipts in docs/qa/unity-build-22.']},null,2)+'\n');
console.log('Published review: '+screenshots.length+' current screenshots and '+suites.length+' suite summaries.');
