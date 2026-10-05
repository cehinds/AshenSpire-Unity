// Execute the frozen published rules; no edits or access to player storage.
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const reference=path.resolve(process.argv[2]||'');
const stamp=JSON.parse(fs.readFileSync(path.join(reference,'buildordinal.json'),'utf8'));
if(stamp.ordinal!==898||stamp.digest!=='1b60c22e01')throw Error('Expected published test 898.');
const load=file=>import(pathToFileURL(path.join(reference,file)).href);
const {contentBundle:bundle}=await load('src/content/index.js');
const stats=await load('src/model/derivedStats.js');
const {tagIndex}=await load('src/model/tags.js');
const {locationIds}=await load('src/model/locations.js');
const ids=bundle.attributes.map(a=>a.id);
const cases=[];
const layers=[[],[{defaults:{pointsPerIncrease:2,rounding:'round'}}],
 [{rules:{hp:{base:3,max:30},openingHand:{byClass:{reaver:{base:2,strength:.5,attributeBaseline:1}}}}}],
 [{defaults:{perLevel:.25}},{defaults:{pointsPerIncrease:3},rules:{hp:{gain:2,pointsBaseline:5,multiplier:1.5}}}]];
for(const layerList of layers){
 const resolved=stats.resolveDerivedStatRules(bundle.derivedStatRules,{attributeIds:ids,classFields:['maxHp'],runModifiers:layerList});
 for(const hero of [...bundle.classes,{id:'unknown',maxHp:60}])for(const points of [0,1,4,5,7,8,15,20,41])for(const level of [null,1,2,5,10,21]){
  const attributes=Object.fromEntries(ids.map((id,i)=>[id,points+i%3]));
  const receipts=Object.fromEntries(Object.keys(resolved.rules).map(id=>[id,stats.deriveStat(resolved,id,{attributes,classDef:hero,level:level??undefined})]));
  cases.push({layers:layerList,attributes,classDef:hero,level,resolved,receipts});
 }
}
const index=tagIndex(bundle),externalObjects={location:[...locationIds()]},tagCases=[];
for(const family of bundle.tagFamilies){
 const rows=family.source?family.source.split('.').reduce((at,key)=>at[key],bundle):(externalObjects[family.family]||[]).map(id=>({id}));
 for(const row of rows)tagCases.push({family:family.family,record:row,tags:index.tagIdsOf(family.family,row),kinds:index.kindIdsOf(family.family,row)});
}
const output=path.resolve('TestResults/HtmlParity/test898');fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'stat-tag-reference.json'),JSON.stringify({referenceBuild:898,externalObjects,cases,tagCases},null,2)+'\n');
console.log(JSON.stringify({statCases:cases.length,receipts:cases.length*12,tagCases:tagCases.length,locations:externalObjects.location.length}));
