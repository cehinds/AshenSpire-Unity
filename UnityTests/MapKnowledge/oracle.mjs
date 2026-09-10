// Export actual pinned original behavior; never execute the C# implementation.
// Usage: node UnityTests/MapKnowledge/oracle.mjs C:/repos/AshenSpire-parity-reference
import fs from 'node:fs';
import path from 'node:path';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
const here=path.dirname(fileURLToPath(import.meta.url)),root=process.argv[2];
const sha='b17a7f4543e1710f49fae8b58880121690a314de';
if(!root || execFileSync('git',['-C',root,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!==sha)throw Error('Pass the pinned original reference checkout.');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const source=await import(pathToFileURL(path.join(root,'src/model/mapknowledge.js')));
const {generateActMap}=await import(pathToFileURL(path.join(root,'src/engine/mapgen.js')));
const {createRng}=await import(pathToFileURL(path.join(root,'src/engine/rng.js')));
const board=read('src/ui/components/mapboard.js');
// Execute the original board's edge loop verbatim with identity coordinates;
// decoding its SVG output catches actual traveled and clipped-lane behavior.
const edgeCode=board.slice(board.indexOf("  let edgeSvg = '';"),board.indexOf('  // ---- the undiscovered ground'));
if(!edgeCode.includes('const isTraveled') || !edgeCode.includes('const isLane'))throw Error('Pinned edge source extraction failed.');
const edgeOracle=new Function('nodes','isDrawn','byId','viewer','laneEdge','x','y',edgeCode+'\nreturn edgeSvg;');
const graph=(rows,starts=['a'],boss='z')=>({nodes:Object.fromEntries(rows.map((n,index)=>[n.id,{floor:index,col:index,type:'monster',next:[],...n}])),startIds:starts,bossId:boss});
const maps=[
 graph([{id:'a',next:['c','b']},{id:'c',next:['s2']},{id:'b',next:['s1']},{id:'s2',type:'shrine',next:['z']},{id:'s1',type:'shrine',next:['z']},{id:'z',type:'boss'}]),
 graph([{id:'a',type:'shrine',next:['b']},{id:'b',type:'event',resolved:{kind:'monster'},next:['c','d']},{id:'c',type:'event',resolved:{kind:'event'},next:['s']},{id:'d',type:'event',resolved:{kind:'treasure'},next:['s']},{id:'s',type:'shrine',next:['z']},{id:'z',type:'boss'}]),
 graph([{id:'a',next:['missing','b']},{id:'b',next:['a','s']},{id:'s',type:'shrine',next:['a']},{id:'z',type:'boss'}]),
 graph([{id:'a',type:'event'},{id:'other',type:'unknown'},{id:'z',type:'boss'}],['a','other']),
 {nodes:{},startIds:[],bossId:null},null
];
// Use authored original configuration and the actual original generator.
const configs=JSON.parse(fs.readFileSync(path.join(here,'../../GameContent/Unity/Original/content.json'),'utf8')).mapConfigs;
for(const config of Object.values(configs))for(const seed of [1,7,42,987654]){
 const map=generateActMap({config,rng:createRng(seed)});
 for(const node of Object.values(map.nodes))if(node.type==='event')node.resolved={kind:['event','monster','shrine','treasure'][node.floor%4]};
 maps.push(map);
}
const fixtures=[],nearest=[];
for(let mapIndex=0;mapIndex<maps.length;mapIndex++){
 const map=maps[mapIndex], nodes=Object.values(map?.nodes||{});
 const walk=[];let at=map?.startIds?.[0];
 while(at&&map.nodes[at]&&!walk.includes(at)){walk.push(at);at=map.nodes[at].next?.at(-1);}
 const paths=[[],walk.slice(0,1),walk.slice(0,3),walk.slice(0,Math.ceil(walk.length/2)),walk, ['missing'],['a','b','a','s']];
 for(const from of [[],map?.startIds||[],...nodes.map(n=>[n.id]),['missing','a','a']]) nearest.push({mapIndex,from,expected:source.nearestShrine({graph:map,from})});
 for(const trail of paths)for(const current of [...new Set([trail.at(-1)||null,nodes.find(n=>n.type==='shrine')?.id||map?.startIds?.[0]||null])])for(const fog of [false,true])for(const reveal of [false,true])for(const glow of [false,true]){
  const run={path:trail,mapNodeId:current},know=source.mapKnowledge({graph:map,run,mode:fog?'fog':'path',reveal});
  const lane=glow?source.shrineLane({graph:map,run}):[];
  const laneSet=new Set(lane),laneEdges=new Set();
  for(let i=0;i+1<lane.length;i++)if(know.drawn.has(lane[i])&&know.drawn.has(lane[i+1]))laneEdges.add(`${lane[i]}>${lane[i+1]}`);
  const expectedNodes=nodes.filter(n=>know.drawn.has(n.id)).map(n=>{
   const rd=source.nodeReading(n,{reveal}),known=know.rung.get(n.id)==='known';
   return {Id:n.id,ShownType:known?rd.shownType:'event',Knowledge:know.rung.get(n.id),Revealed:known&&rd.revealed,Visited:trail.includes(n.id)||n.id===current,Current:n.id===current,ShrineLane:laneSet.has(n.id)};
  });
  const svg=edgeOracle(nodes,id=>know.drawn.has(id),map?.nodes||{},{path:trail},laneEdges,col=>col,floor=>floor);
  const locate=(col,floor)=>nodes.find(n=>String(n.col)===col&&String(n.floor)===floor).id;
  const edges=[...svg.matchAll(/<line class="([^"]+)" x1="([^"]+)" y1="([^"]+)" x2="([^"]+)" y2="([^"]+)"\/>/g)].map(m=>({From:locate(m[2],m[3]),To:locate(m[4],m[5]),Traveled:m[1].split(' ').includes('traveled'),ShrineLane:m[1].split(' ').includes('shrine-lane')}));
  fixtures.push({mapIndex,path:trail,currentId:current,fog,revealUnknown:reveal,shrineGlow:glow,expected:{Nodes:expectedNodes,VisibleIds:expectedNodes.map(n=>n.Id),Edges:edges}});
 }
}
const sourceFiles=['src/model/mapknowledge.js','src/ui/components/mapboard.js','src/engine/mapgen.js','src/engine/rng.js'];
const receipt={originalCommit:sha,sources:Object.fromEntries(sourceFiles.map(p=>[p,createHash('sha256').update(read(p)).digest('hex')])),maps,fixtures,nearest};
fs.writeFileSync(path.join(here,'source-reference.json'),JSON.stringify(receipt)+'\n');
console.log(JSON.stringify({maps:maps.length,fixtures:fixtures.length,nearest:nearest.length}));
