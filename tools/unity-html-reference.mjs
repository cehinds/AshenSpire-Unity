// Inventory a frozen published HTML game's source without replacing native saves/content.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {pathToFileURL,fileURLToPath} from 'node:url';
const root=path.resolve(fileURLToPath(new URL('..',import.meta.url)));
const reference=path.resolve(process.argv[2]||'');
const output=path.resolve(process.argv[3]||'TestResults/HtmlParity/test898');
if(!process.argv[2])throw Error('Pass the frozen published source directory.');
const read=file=>JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));
const stamp=read(path.join(reference,'buildordinal.json'));
if(stamp.ordinal!==898||stamp.release!=='0.7.1'||stamp.digest!=='1b60c22e01')throw Error('Expected owner-selected published test build 898.');
const {contentBundle}=await import(pathToFileURL(path.join(reference,'src/content/index.js')).href);
const {validateContent}=await import(pathToFileURL(path.join(reference,'src/model/validate.js')).href);
const validation=validateContent(contentBundle);
if(validation.ok===false||validation.errors?.length)throw Error(JSON.stringify(validation));
const {xpStepCost}=await import(pathToFileURL(path.join(reference,'src/model/xpCurve.js')).href);
const xpCases=[];
for(const curve of [
 {base:100,growth:1.75,roundTo:5,linear:false,multScaler:1.3},
 {base:100,growth:1.75,roundTo:5,linear:true,multScaler:1.3},
 {base:100,growth:1.3,roundTo:1,linear:false,multScaler:1.3},
 {base:2.5,growth:1,roundTo:5,linear:false,multScaler:0},
 {base:1,growth:1,roundTo:0,linear:true,multScaler:0},
])for(let step=0;step<=25;step++)xpCases.push({curve,step,value:xpStepCost(curve,step)});
const native=read(path.join(root,'GameContent/Unity/Original/content.json'));
const nativeCatalog=fs.readFileSync(path.join(root,'Unity/Assets/AshenSpire/Runtime/Domain/Original/OriginalContentCatalog.cs'),'utf8');
const knownArray=nativeCatalog.match(/var known = new\[\] \{([^}]+)\}/)?.[1];
if(!knownArray)throw Error('Cannot inventory the native effect-validation boundary.');
const known=new Set([...knownArray.matchAll(/"([^"]+)"/g)].map(match=>match[1]));
const operations={};
function visit(value,address=''){
 if(!value||typeof value!=='object')return;
 if(typeof value.op==='string')(operations[value.op]||=[]).push(address);
 for(const [key,child]of Object.entries(value))visit(child,address?address+'.'+key:key);
}
visit(contentBundle);
const files=folder=>fs.readdirSync(folder,{withFileTypes:true}).flatMap(entry=>entry.isDirectory()?files(path.join(folder,entry.name)):[path.join(folder,entry.name)]).sort();
const modules=files(path.join(reference,'src')).filter(file=>file.endsWith('.js')).map(file=>{
 const source=fs.readFileSync(file,'utf8');
 return {path:path.relative(reference,file).replaceAll('\\','/'),sha256:crypto.createHash('sha256').update(source).digest('hex'),bytes:Buffer.byteLength(source),exports:[...source.matchAll(/export\s+(?:(?:async\s+)?function|const|let|class)\s+([\w$]+)/g)].map(match=>match[1])};
});
const counts=bundle=>Object.fromEntries(Object.entries(bundle).filter(([,value])=>Array.isArray(value)).map(([key,value])=>[key,value.length]));
const receipt={reference:{channel:'test',url:'https://cehinds.github.io/AshenSpire/test/898/',commit:'0b85909adc103a915aad5d37af53b4c2c66aa894',...stamp},nativeContentVersion:native.version,referenceCounts:counts(contentBundle),nativeCounts:counts(native),newBundleKeys:Object.keys(contentBundle).filter(key=>!(key in native)),operationAddresses:operations,operationsOutsideNativeCatalog:Object.keys(operations).filter(op=>!known.has(op)),moduleCount:modules.length,modules,contentValidated:true,referenceContentSha256:crypto.createHash('sha256').update(JSON.stringify(contentBundle,null,2)+'\n').digest('hex'),artRelease:read(path.join(reference,'art-release.json')),nativeParityVerified:false};
fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'content-reference.json'),JSON.stringify(contentBundle,null,2)+'\n');
fs.writeFileSync(path.join(output,'inventory.json'),JSON.stringify(receipt,null,2)+'\n');
fs.writeFileSync(path.join(output,'xp-curve-reference.json'),JSON.stringify({referenceBuild:898,source:'src/model/xpCurve.js',sourceSha256:modules.find(row=>row.path==='src/model/xpCurve.js').sha256,cases:xpCases},null,2)+'\n');
console.log(JSON.stringify({referenceBuild:stamp.ordinal,moduleCount:modules.length,newBundleKeys:receipt.newBundleKeys,operationsOutsideNativeCatalog:receipt.operationsOutsideNativeCatalog,referenceCounts:receipt.referenceCounts,nativeCounts:receipt.nativeCounts,contentValidated:true,nativeParityVerified:false},null,2));
