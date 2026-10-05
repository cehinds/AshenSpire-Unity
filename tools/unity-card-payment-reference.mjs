import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const reference=path.resolve(process.argv[2]||'');
const stamp=JSON.parse(fs.readFileSync(path.join(reference,'buildordinal.json'),'utf8'));
if(stamp.ordinal!==898||stamp.digest!=='1b60c22e01')throw Error('Expected published test 898.');
const load=file=>import(pathToFileURL(path.join(reference,file)).href);
const {contentBundle:bundle}=await load('src/content/index.js');
const {createRegistries}=await load('src/model/registries.js');
const {mechanics}=await load('src/framework/data/mechanics.js');
const {cardChoice,assertCardChoice}=await load('src/model/cardChoices.js');
const {bindTurnStamina}=await load('src/model/turnStamina.js');
const registries=createRegistries(bundle),cases=[],choiceCases=[],aliasCases=[];
for(const card of registries.cards.all())for(const upgraded of [false,true]){
 if(upgraded&&!card.upgrade)continue;
 const def=upgraded?{...card,...card.upgrade,name:card.upgrade.name??card.name+'+'}:card;
 for(const weightClass of [null,...mechanics.weight.classes])for(const powerCostReduction of [0,1,3])
  cases.push({def,weightClass,powerCostReduction,profile:registries.framework.costProfile(def,{weightClass,powerCostReduction})});
 const classes=def.effects?.some(e=>e.op==='enterStance'&&e.choose)?[...bundle.classes.map(c=>c.id),'unknown']:['reaver'];
 for(const classId of classes)for(const active of [null,...bundle.stances.map(s=>s.id)]){
  const plan=cardChoice(registries,def,classId,active);const answers=[];
  for(const choice of [null,'unknown',...bundle.stances.map(s=>s.id)]){
   try{answers.push({choice,value:assertCardChoice(plan,choice),accepted:true});}catch{answers.push({choice,accepted:false});}
  }
  choiceCases.push({def,classId,active,plan,answers});
 }
}
const writes=[['energy',2],['stamina',1],['energyMax',7],['maxStamina',5],['energy',0],['stamina',3]];
for(const stamina of [0,1,3,7]){
 const player=bindTurnStamina({energy:9,energyMax:99,stamina,maxStamina:7,mana:2});
 const initial=JSON.parse(JSON.stringify(player)),states=[];
 for(const [key,value] of writes){player[key]=value;states.push(JSON.parse(JSON.stringify(player)));}
 aliasCases.push({stamina,initial,writes,states});
}
const output=path.resolve('TestResults/HtmlParity/test898');fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'card-payment-reference.json'),JSON.stringify({referenceBuild:898,cases,choiceCases,aliasCases},null,2)+'\n');
console.log(JSON.stringify({costCases:cases.length,choiceCases:choiceCases.length,aliasCases:aliasCases.length}));
