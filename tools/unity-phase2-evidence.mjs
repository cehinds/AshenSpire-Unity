// Archive only successful checks made against the exact exported Web payload.
import {readFile,writeFile,mkdir,copyFile,stat} from 'node:fs/promises';
import {readdirSync,readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {resolve,join} from 'node:path';
import assert from 'node:assert/strict';
import {unitySourceDigest} from './unity-source-digest.mjs';
const root=resolve(process.argv[2]||'TestResults/Phase2');
const output=resolve(process.argv[3]||'docs/qa/unity-build-31');
const json=async file=>JSON.parse(await readFile(file,'utf8'));
const build=await json('Builds/Web/build-source.json');
const files=folder=>readdirSync(folder,{withFileTypes:true}).flatMap(entry=>entry.isDirectory()?files(join(folder,entry.name)):[join(folder,entry.name)]);
const sourceFiles=['Unity/Assets','Unity/Packages','Unity/ProjectSettings','GameContent/Unity'].flatMap(files);
assert.equal(unitySourceDigest(sourceFiles,file=>readFileSync(file)),build.sourceDigest,'Source changed after export');
for(const [file,hash] of Object.entries(build.files)) assert.equal(createHash('sha256').update(await readFile(join('Builds/Web',file))).digest('hex'),hash,'Changed player file: '+file);
const events=['Events-respect-1','Events-respect-2','Events-respect-3','Events-PolicyRune','Events-PolicyAvatar','Events-Rest-dig','Events-Rest-respect','Events-Rest-face'];
const cases=['Browser/390','Browser/1440','Rewards','Combat/320x640','Combat/390x844','Combat/768x1024','Combat/1440x900','Coop/Host','Coop/Guest',...['320','390','768','1440'].map(size=>'Layout/'+size),...['320x640','390x844','768x1024','1440x900'].map(size=>'Onboarding/'+size),'Audio','Appearance/animated','Appearance/rendered','Appearance/classic','Appearance/glyph','Features',...['320','768','1440'].map(size=>'FeaturesLayout/'+size),'Victory','Endless','HoldInterruption',...['Custom','Sealed','Draft','Endless'].map(mode=>'Modes/'+mode),...[0,1,2,3,4,5,6].map(level=>'CustomMatrix/'+level),...events];
const results=[];
const coveredChoices=new Set(),eventResults=[];
for(const name of cases){
 const receipt=await json(join(root,name,'build-source.json'));
 assert.equal(receipt.sourceDigest,build.sourceDigest,'Stale source: '+name);
 assert.deepEqual(receipt.files,build.files,'Different exported payload: '+name);
 assert((await stat(join(root,name,'checks.json'))).mtimeMs >= (await stat(join(root,name,'build-source.json'))).mtimeMs,'Case has not finished since opening this build: '+name);
 const result=await json(join(root,name,'checks.json'));
 assert.equal(result.success,true,'Failed case: '+name);
 assert.deepEqual(result.errors,[],'Errors in '+name);
 if(name==='Victory')assert.equal(result.lastState.phase,'Victory','Victory replay did not finish');
 if(name==='Endless'){
  assert.equal(result.lastState.act,4,'Endless replay did not enter Act 4');
  assert(['fellWarden','stitchedKing','blightedValkyrie'].every(id=>result.bosses.some(row=>row.enemyId===id)),'Missing compiled boss encounter');
 }
 if(events.includes(name)){
  const fixtureFile=name.startsWith('Events-respect-')?'events-respect-'+name.at(-1)+'-replay.json':name==='Events-PolicyRune'?'PolicyRune-replay.json':name==='Events-PolicyAvatar'?'PolicyAvatar-replay.json':'Rest-'+name.split('-').at(-1)+'-replay.json';
  const fixture=(await json(join(root,fixtureFile))).runs[0];
  assert.deepEqual([...result.eventChoices].sort(),[...fixture.eventChoiceFilter].sort(),'Missing event branches: '+name);
  for(const choice of result.eventChoices)coveredChoices.add(choice);
  eventResults.push({name,fixture:fixtureFile,checks:result.checks.length,commands:result.commands.length,choices:result.eventChoices,viewport:result.viewport,finalPhase:result.lastState.phase,finalAct:result.lastState.act,scope:result.scope});
 }
 results.push({name,passed:true,checks:result.checks.length,verified:result.checks});
}
const authored=(await json('GameContent/Unity/Original/event-choices.json')).eventChoices;
const expectedChoices=Object.entries(authored).flatMap(([id,rows])=>rows.map(row=>id+':'+row.id));
assert.equal(Object.keys(authored).length,22);assert.equal(expectedChoices.length,62);
assert.deepEqual([...coveredChoices].sort(),expectedChoices.sort(),'Incomplete authored event coverage');
const custom=await json(join(root,'CustomMatrix/summary.json'));
assert.deepEqual(custom.ascensions,[0,1,2,3,4,5,6]);
const options=await json('GameContent/Unity/Original/custom-run-options.json');
assert.deepEqual([...custom.modifiers].sort(),[...options.difficulty,...options.chaos].map(row=>row.id).sort(),'Missing public custom modifiers');
await mkdir(output,{recursive:true});
await copyFile('Builds/Web/build-source.json',join(output,'build-source.json'));
await copyFile(join(root,'Editor/receipt.json'),join(output,'editor-receipt.json'));
await copyFile(join(root,'Rewards/transitions.json'),join(output,'transitions.json'));
await writeFile(join(output,'event-summary.json'),JSON.stringify({success:true,sourceDigest:build.sourceDigest,events:Object.keys(authored).length,choices:coveredChoices.size,coverage:[...coveredChoices].sort(),cases:eventResults,saveInjection:false,physicalDevice:false},null,2)+'\n');
await writeFile(join(output,'mode-layout-summary.json'),JSON.stringify({success:true,sourceDigest:build.sourceDigest,viewports:[{width:320,height:640},{width:390,height:844},{width:768,height:1024},{width:1440,height:900}],custom,fullSealedAndDraftCampaign:false,physicalDevice:false},null,2)+'\n');
for(const [source,name] of [['Browser/390/03-held-confirmation.png','phone-high-contrast.png'],['Rewards/01-rewards.png','phone-rewards.png'],['Combat/320x640/02-enemy-inspection.png','phone-enemy-inspection.png'],['Coop/Host/06-host-restart-rejoined.png','coop-restored.png']]) await copyFile(join(root,source),join(output,name));
for(const [source,name] of [['Layout/320/07-large-text-title.png','small-phone-large-text.png'],['FeaturesLayout/320/04-merchant-resale.png','small-phone-merchant.png'],['Events-Rest-dig/event-namelessKeeper-returnCinders.png','nameless-keeper-phone.png'],['Events-Rest-dig/event-namelessRest-restAmongStones.png','nameless-rest-phone.png'],['Onboarding/320x640/death-320.png','small-phone-death.png'],['Onboarding/320x640/chronicle-320.png','small-phone-chronicle.png']]) await copyFile(join(root,source),join(output,name));
for(const id of ['fellWarden','stitchedKing','blightedValkyrie']){
 const result=await json(join(root,'Endless/checks.json')),boss=result.bosses.find(row=>row.enemyId===id);
 await copyFile(join(root,'Endless',boss.file),join(output,boss.file));
}
for(const name of ['Victory','Endless']){
 const result=await json(join(root,name,'checks.json'));
 await writeFile(join(output,name.toLowerCase()+'-summary.json'),JSON.stringify({success:true,sourceDigest:build.sourceDigest,scope:result.scope,checks:result.checks.length,commands:result.commands.length,bosses:result.bosses||[],finalPhase:result.lastState.phase,finalAct:result.lastState.act,errors:result.errors,physicalDevice:false},null,2)+'\n');
}
const appearance=await json(join(root,'Appearance/summary.json'));
const summary={success:true,version:build.version,buildNumber:build.buildNumber,sourceDigest:build.sourceDigest,checks:results.reduce((sum,r)=>sum+r.checks,0),cases:results,authoredEvents:22,authoredChoices:62,renderer:'Edge default GPU (AS_BROWSER_GPU=1)',appearanceAllTintAndSigilChoices:appearance.allTintAndSigilChoices,virtualController:true,physicalController:false,physicalDevice:false,ownerAcceptance:false};
await writeFile(join(output,'browser-summary.json'),JSON.stringify(summary,null,2)+'\n');
console.log(`${summary.checks} checks in ${results.length} cases match ${build.sourceDigest}.`);
