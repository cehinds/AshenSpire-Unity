// Real JavaScript-game save fixtures; writes only the requested test output folder.
import fs from 'node:fs';
import path from 'node:path';
import {contentBundle} from '../src/content/index.js';
import {createRegistries,resolveCard} from '../src/model/registries.js';
import {createRunState,createIdGen} from '../src/model/state.js';
import {createRng,seedToString} from '../src/engine/rng.js';
import {createSaveManager} from '../src/engine/save.js';
import {buildActMap} from '../src/engine/actmap.js';
import {recordProgress,evaluateUnlocks} from '../src/model/unlocks.js';
if (!process.argv[2]) throw new Error('Pass the test fixture output directory.');
const output=path.resolve(process.argv[2]);fs.mkdirSync(output,{recursive:true});
const registries=createRegistries(contentBundle);
for(const classId of ['reaver','starseer','rogue','herald']){
 const run=createRunState({seed:1,classId,registries,idGen:createIdGen('web')});
 // Match the public new-run setup in src/main.js before its first persist.
 run.seedString=seedToString(run.seed);run.customization={name:'Forsaken',glyph:'⚔',tint:'gold'};
 run.custom={ascension:0,mods:{},deckMode:'standard'};
 run.stats={fightsWon:0,damageDealt:0,damageTaken:0};run.path=[];run.seenEvents=[];run.lastEncounters=[];
 const rng=createRng(run.seed,run.streamCounters);
 run.mapGraph=buildActMap(registries,rng,1,undefined,{history:run.history});
 const values=new Map(),storage={getItem:key=>values.get(key)??null,setItem:(key,value)=>values.set(key,value),removeItem:key=>values.delete(key)};
 createSaveManager(storage).saveRun(run,rng);
 fs.writeFileSync(path.join(output,classId+'-map.json'),values.get('sote_run_v1'));
 fs.writeFileSync(path.join(output,classId+'-cards.json'),JSON.stringify(run.deck.map(instance=>({instanceId:instance.instanceId,card:resolveCard(registries,instance)}))));
 console.log(classId+': original schema '+run.schemaVersion+', HP '+run.hp+'/'+run.maxHp+', '+run.deck.length+' cards');
}
// Use the original profile writer and unlock evaluator, including a durable tally
// larger than the retained history. These are isolated generated test records.
const profileValues=new Map(),profileStorage={getItem:key=>profileValues.get(key)??null,setItem:(key,value)=>profileValues.set(key,value),removeItem:key=>profileValues.delete(key)};
const profiles=createSaveManager(profileStorage);
for(let i=0;i<23;i++){
 const result={victory:i%3===0,class:'reaver',className:'Reaver',act:i%3===0?3:2,floor:8,fightsWon:7,damageDealt:120,damageTaken:31,custom:false,ascension:0,bosses:[],seed:'PROFILE-'+i};
 const meta=profiles.recordResult(result);meta.progress=recordProgress(meta.progress,result);
 meta.unlocked=[...(meta.unlocked||[]),...evaluateUnlocks(registries.unlocks,meta)];profiles.saveMeta(meta);
}
const meta=profiles.loadMeta();meta.settings={muteAudio:true,musicVolume:25,reducedMotion:true};
meta.found=[registries.equipment.armaments[0].id];meta.discoveredArmaments=[...meta.found];profiles.saveMeta(meta);
fs.writeFileSync(path.join(output,'profile.json'),profileValues.get('sote_meta_v1'));
fs.writeFileSync(path.join(output,'profile-export.json'),profiles.exportProfile());
console.log('Original profile: '+meta.progress.runs+' climbs, '+meta.results.length+' retained results');
