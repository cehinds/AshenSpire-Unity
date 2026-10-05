// Execute the owner's frozen published JS rules; never edits either game or saves.
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const reference=path.resolve(process.argv[2]||'');
const output=path.resolve('TestResults/HtmlParity/test898');
const stamp=JSON.parse(fs.readFileSync(path.join(reference,'buildordinal.json'),'utf8'));
if(stamp.ordinal!==898||stamp.digest!=='1b60c22e01')throw Error('Expected published test 898.');
const load=file=>import(pathToFileURL(path.join(reference,file)).href);
const {contentBundle}=await load('src/content/index.js');
const {mechanics}=await load('src/framework/data/mechanics.js');
const {createRegistries}=await load('src/model/registries.js');
const levels=await load('src/model/levelup.js');
const skills=await load('src/model/skills.js');
const cases=[];
for(const cap of [0,1,3])for(const ceiling of [null,2,20])for(const level of [1,2,10])for(const xp of [0,99,100,500])for(const gain of [-5,0,1,99,100,500,9999]){
 const bundle={...contentBundle,balance:structuredClone(contentBundle.balance)};
 bundle.balance.level.maxLevelsPerFight=cap;
 if(ceiling===null)delete bundle.balance.levelUp.maxLevels;else bundle.balance.levelUp.maxLevels=ceiling;
 const registries=createRegistries(bundle);
 const run={level:{level,xp,unspentPoints:2},cinders:999};
 const bank=levels.bankLevelXp(registries,run,gain);
 const claims=[];
 while(levels.pendingLevelCount(registries,run)>0)claims.push(levels.claimBankedLevel(registries,run));
 const refused=levels.claimBankedLevel(registries,run);
 cases.push({cap,ceiling,level,xp,gain,climb:levels.climbLevels(registries,{level,xp,gain}),bank,claims,refused,ledger:run.level,cinders:run.cinders});
}
const registries=createRegistries(contentBundle),tracks=skills.skillTracks(registries);
const skillCases=[];
for(const track of tracks)for(const level of [0,1,4,10])for(const gain of [-1,0,1,100,1000,9999]){
 const run={skills:{[track.id]:{xp:25,level,pendingDrafts:2}},deck:[],sideboard:[]};
 const bank=skills.bankSkillXp(registries,run,track.id,gain),claims=[];
 while(skills.pendingSkillLevelCount(registries,run,track.id)>0)claims.push(skills.claimBankedSkillLevel(registries,run,track.id));
 const refused=skills.claimBankedSkillLevel(registries,run,track.id);
 const spent=skills.spendSkillDraft(run,track.id);
 skillCases.push({id:track.id,level,gain,bank,claims,refused,spent,ledger:run.skills[track.id]});
}
const heldCases=[];
for(const piece of contentBundle.equipment.armaments){
 const run={loadout:{sets:{rightHand:[piece.id],leftHand:[null]},active:{rightHand:0,leftHand:0}},skills:{},deck:[],sideboard:[]};
 for(const card of contentBundle.cards)run.deck.push({instanceId:'deck:'+card.id,cardId:card.id,upgraded:false});
 run.sideboard=run.deck.splice(Math.floor(run.deck.length/2));
 // Equipment-owned cards must survive the standing upgrade without being upgraded.
 run.deck.push({instanceId:'bound',cardId:contentBundle.cards[0].id,upgraded:false,sourceArmamentId:piece.id});
 for(const id of [...(piece.itemTypeTags||[]),'dualWield'].filter(id=>tracks.some(t=>t.id===id))){
  const copy=structuredClone(run);copy.skills[id]={level:contentBundle.balance.skill.upgradeAt-1,xp:skills.xpToNext(registries,skills.skillKindOf(registries,id),contentBundle.balance.skill.upgradeAt-1),pendingDrafts:0};
  heldCases.push({piece:piece.id,id,run:structuredClone(copy),schools:skills.skillSchools(registries,copy.loadout,id),claim:skills.claimBankedSkillLevel(registries,copy,id),after:copy});
 }
}
fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'earned-progression-reference.json'),JSON.stringify({referenceBuild:898,mechanics,tracks,cases,skillCases,heldCases},null,2)+'\n');
console.log(JSON.stringify({characterCases:cases.length,skillCases:skillCases.length,heldCases:heldCases.length,tracks:tracks.length}));
