// Real JavaScript-game save fixtures; writes only the requested test output folder.
import fs from 'node:fs';
import path from 'node:path';
import {contentBundle} from '../src/content/index.js';
import {createRegistries,resolveCard} from '../src/model/registries.js';
import {createRunState,serializeRun,createIdGen} from '../src/model/state.js';
import {createRng} from '../src/engine/rng.js';
import {buildActMap} from '../src/engine/actmap.js';
if (!process.argv[2]) throw new Error('Pass the test fixture output directory.');
const output=path.resolve(process.argv[2]);fs.mkdirSync(output,{recursive:true});
const registries=createRegistries(contentBundle);
for(const classId of ['reaver','starseer','rogue','herald']){
 const run=createRunState({seed:1,classId,registries,idGen:createIdGen('web')});
 const rng=createRng(run.seed,run.streamCounters);
 run.mapGraph=buildActMap(registries,rng,1,undefined,{history:run.history});
 run.streamCounters=rng.getCounters();run.path=[];run.seedString='1';
 fs.writeFileSync(path.join(output,classId+'-map.json'),serializeRun(run));
 fs.writeFileSync(path.join(output,classId+'-cards.json'),JSON.stringify(run.deck.map(instance=>({instanceId:instance.instanceId,card:resolveCard(registries,instance)}))));
 console.log(classId+': original schema '+run.schemaVersion+', HP '+run.hp+'/'+run.maxHp+', '+run.deck.length+' cards');
}
