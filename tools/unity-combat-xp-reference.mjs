import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const reference=path.resolve(process.argv[2]||'');
const stamp=JSON.parse(fs.readFileSync(path.join(reference,'buildordinal.json'),'utf8'));
if(stamp.ordinal!==898||stamp.digest!=='1b60c22e01')throw Error('Expected published test 898.');
const load=file=>import(pathToFileURL(path.join(reference,file)).href);
const {contentBundle:bundle}=await load('src/content/index.js');
const {createRegistries}=await load('src/model/registries.js');
const {enemyCombatPower}=await load('src/model/combatPower.js');
const {combatXpReceipt}=await load('src/model/levelup.js');
const registries=createRegistries(bundle),powerCases=[],awardCases=[];
for(const def of bundle.enemies)for(const level of [0,1,2,7,25])for(const damageMult of [0,.5,1,1.75]){
 const enemy={enemyId:def.id,level,damageMult,maxHp:def.hp[1],poiseMeter:{max:def.poiseMax},alive:false,hp:0};
 powerCases.push({enemy,value:enemyCombatPower(registries,enemy)});
}
for(const enemy of [{enemyId:'unknown'},{combatPower:0},{combatPower:4.125},{combatPower:-1},{enemyId:bundle.enemies[0].id,level:2}])powerCases.push({enemy,value:enemyCombatPower(registries,enemy)});
for(const pool of ['normal','elite','boss','unknown'])for(const victory of [false,true])for(const characterMultiplier of [-1,0,.5,1,1.15,2.5])for(const count of [0,1,2,4]){
 const enemies=Array.from({length:count},(_,i)=>({...powerCases[(i*83+count)%powerCases.length].enemy,id:'e'+(i+1),alive:i!==1,hp:i===1?5:0}));
 const options={pool,victory,enemies,characterMultiplier};awardCases.push({options,receipt:combatXpReceipt(registries,options)});
 const synthetic={pool,victory,kills:count,characterMultiplier};awardCases.push({options:synthetic,receipt:combatXpReceipt(registries,synthetic)});
}
const output=path.resolve('TestResults/HtmlParity/test898');fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'combat-xp-reference.json'),JSON.stringify({referenceBuild:898,powerCases,awardCases},null,2)+'\n');
console.log(JSON.stringify({powerCases:powerCases.length,awardCases:awardCases.length}));
