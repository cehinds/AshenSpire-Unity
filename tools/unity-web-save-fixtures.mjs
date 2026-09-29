// Real JavaScript-game save fixtures; writes only the requested test output folder.
import fs from 'node:fs';
import path from 'node:path';
import {contentBundle} from '../src/content/index.js';
import {createRegistries,resolveCard} from '../src/model/registries.js';
import {createRunState,createIdGen} from '../src/model/state.js';
import {createRng,seedToString} from '../src/engine/rng.js';
import {createSaveManager} from '../src/engine/save.js';
import {buildActMap} from '../src/engine/actmap.js';
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
