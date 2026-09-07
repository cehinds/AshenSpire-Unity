// Exports actual pinned original binding/static renderer results; no Unity math.
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
if(!process.argv[2]) throw Error('Usage: node UnityTests/CardText/export-reference.mjs <pinned-original-checkout>');
const root=path.resolve(process.argv[2]);
const out=path.dirname(fileURLToPath(import.meta.url));
const sha=execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).trim();
if(sha!=='b17a7f4543e1710f49fae8b58880121690a314de'||execFileSync('git',['status','--porcelain','--','src'],{cwd:root,encoding:'utf8'}).trim())throw Error('Expected clean pinned original');
const load=p=>import(pathToFileURL(path.join(root,p)).href);
const {contentBundle}=await load('src/content/index.js');
const {createRegistries,resolveCard}=await load('src/model/registries.js');
const {computeTokenBindings,tokenRe}=await load('src/model/validate.js');
const {staticTokens}=await load('src/ui/components/card.js');
const registry=createRegistries(contentBundle), fixtures=[];
function add(id,definition){const tokens=staticTokens(definition);fixtures.push({id,definition,bindings:computeTokenBindings(definition.effects),tokens,text:(definition.textTemplate??'').replace(tokenRe(),(m,key)=>typeof tokens[key]==='number'?String(tokens[key]):m)});}
for(const row of contentBundle.cards)for(const upgraded of[false,true])add(`${row.id}:${upgraded}`,resolveCard(registry,{cardId:row.id,upgraded}));
// Synthetic grammar edges go through the unchanged original oracle too.
add('grammar/repeats',{textTemplate:'{block} {block.2} {block.3} {damage} {damage.2} {hits} {hits.2} {bleed} {bleed.2} {loseMaxHpPct} {unknown} {Block} {block:bad}',effects:[{op:'block',amount:2},{op:'block',amount:{f:'add',args:[2,3]}},{op:'block',amount:7},{op:'damage',amount:3,hits:2},{op:'damage',amount:5,hits:4},{op:'applyStatus',status:'bleed',stacks:1},{op:'applyStatus',status:'bleed',stacks:6},{op:'loseMaxHpPct',pct:0.25},{op:'discard',amount:99}]});
add('grammar/malformed',{textTemplate:'{damage} {hits}',effects:[null,3,{}, {op:3},{op:'applyStatus',stacks:1},{op:'damage'}]});
const bytes=JSON.stringify({sourceCommit:sha,fixtures},null,2)+'\n';
fs.writeFileSync(path.join(out,'card-text-reference.json'),bytes);
const hash=p=>createHash('sha256').update(fs.readFileSync(path.join(root,p))).digest('hex');
fs.writeFileSync(path.join(out,'card-text-reference.receipt.json'),JSON.stringify({sourceCommit:sha,sources:Object.fromEntries(['src/model/validate.js','src/ui/components/card.js','src/model/registries.js'].map(p=>[p,hash(p)])),outputSha256:createHash('sha256').update(bytes).digest('hex'),authoredDefinitions:contentBundle.cards.length*2,fixtures:fixtures.length},null,2)+'\n');
console.log(`Exported ${fixtures.length} original text fixtures.`);
