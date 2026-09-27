// Generate published review metadata from successful, source-matched captures.
// Usage: node tools/unity-review-evidence.mjs <capture-config.json>
// Config supplies gallery, coop, performance, enemyCombat and campaign folders.
// Transport credentials and full state/control dumps are never published here.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const read=file=>JSON.parse(fs.readFileSync(file,'utf8'));
if(!process.argv[2])throw Error('Provide the capture configuration file.');
const config=read(path.resolve(process.argv[2]));
const build=read(path.join(root,'Published/build.json'));
const output=path.join(root,'Published/BuildReview');
const suites=[],screenshots=[],copies=[];
function receipt(folder){
 const stamp=read(path.join(folder,'build-source.json'));
 if(stamp.sourceDigest!==build.sourceDigest||stamp.buildNumber!==build.buildNumber)throw Error('Capture is from a different compiled source: '+folder);
}
function checks(name,folder){
 receipt(folder);const result=read(path.join(folder,'checks.json'));
 if(result.success!==true||result.errors.length)throw Error('Capture did not pass: '+name);
 suites.push({suite:name,success:true,checks:result.checks.length,physicalDevice:false});
}
function shot(folder,file,name){
 const source=path.join(folder,file);
 if(!fs.existsSync(source))throw Error('Missing screenshot: '+source);
 const target='Published/BuildReview/'+name+'.png';
 copies.push([source,path.join(root,target)]);screenshots.push(target);
}
checks('EnemyCatalog',config.gallery);
const gallery=read(path.join(config.gallery,'gallery.json'));
if(!gallery.allCatalogEnemiesRendered||new Set(gallery.screenshots.map(row=>row.id)).size!==19)throw Error('All 19 enemy portraits are required.');
for(const row of gallery.screenshots.filter(row=>row.viewport==='phone'))shot(config.gallery,row.file,'enemy-'+row.id);
for(const seat of ['Host','Guest']){
 const folder=path.join(config.coop,seat);checks('CooperativeBrowser'+seat,folder);
 shot(folder,seat==='Host'?'02-combat.png':'02-own-hand.png','coop-'+seat.toLowerCase());
}
checks('EnemyCombat',config.enemyCombat);
checks('PerformanceSampling',config.performance);
const performance=read(path.join(config.performance,'performance.json'));
for(const scene of ['title','map','combat'])shot(config.performance,scene+'.png',scene);
const campaign=read(path.join(config.campaign,'playtest.json'));
if(campaign.errors.length||!campaign.fullRunVictory||!campaign.resumeStateMatches)throw Error('Campaign/reload acceptance did not pass.');
// The campaign harness records its served version separately; its folder must
// also carry the source receipt supplied by the caller's exact build run.
receipt(config.campaign);
suites.push({suite:'CampaignFull',success:true,fullRunVictory:true,resumeStateMatches:true,physicalDevice:false});
if(screenshots.length>32)throw Error('Published gallery exceeds the hosting limit.');
fs.mkdirSync(output,{recursive:true});
for(const [source,target] of copies)fs.copyFileSync(source,target);
fs.writeFileSync(path.join(output,'enemy-gallery.json'),JSON.stringify(gallery,null,2)+'\n');
fs.writeFileSync(path.join(output,'performance.json'),JSON.stringify(performance,null,2)+'\n');
fs.writeFileSync(path.join(output,'Guide.md'),`# Build ${build.buildNumber} review\n\nVersion ${build.version}; source \`${build.sourceDigest}\`.\n\nThese images come from this compiled Web player using normal controls. All 19 painted portraits load; two players complete a shared fight, rewards and exact-hand rejoin. The gallery does not prove every encounter or physical device.\n\nFeatures F00–F17 remain unaccepted. Owner visual/profile acceptance, complete mode and inventory coverage, listening, target-device budgets, Android and graphical Windows play, iOS delivery and real-player pacing remain open. See [the roadmap](https://github.com/cehinds/AshenSpire-Unity/blob/feature/unity-roadmap-completion/docs/Unity-Roadmap.md) and [draft PR 56](https://github.com/cehinds/AshenSpire-Unity/pull/56).\n`);
fs.writeFileSync(path.join(root,'Published/presentation.json'),JSON.stringify({guide:'Published/BuildReview/Guide.md',screenshots},null,2)+'\n');
fs.writeFileSync(path.join(root,'Published/validation.json'),JSON.stringify({version:build.version,buildNumber:build.buildNumber,sourceDigest:build.sourceDigest,builtAt:build.builtAt,webCompiled:true,windowsCompiled:true,androidCompiled:true,companionPackaged:true,currentPlayerSuites:suites,ownerAccepted:false,physicalDevice:false,limits:['These selected browser suites do not close the roadmap.','Performance uses software WebGL, not target-device budgets.','Historical captures retain their original source receipts in docs/qa/unity-build-22.']},null,2)+'\n');
console.log('Published review: '+screenshots.length+' current screenshots and '+suites.length+' suite summaries.');
