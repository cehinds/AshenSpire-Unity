// Execute pinned-original pure geometry and original private camera functions.
// The tiny layout adapter provides numeric viewport/title metrics; this is not
// a browser, font measurement, scrollbar-rounding or real-device acceptance test.
import fs from 'node:fs';import path from 'node:path';import{pathToFileURL,fileURLToPath}from'node:url';import{execFileSync}from'node:child_process';import{createHash}from'node:crypto';
const root=path.resolve(process.argv[2]||''),out=path.dirname(fileURLToPath(import.meta.url));
const sha=execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).trim();
if(sha!=='b17a7f4543e1710f49fae8b58880121690a314de'||execFileSync('git',['status','--porcelain','--','src'],{cwd:root,encoding:'utf8'}).trim())throw Error('Expected clean pinned original checkout');
const load=p=>import(pathToFileURL(path.join(root,p)).href);
const geometry=await load('src/model/mapview.js');
const{buildActMap}=await load('src/engine/actmap.js'),{createRng}=await load('src/engine/rng.js');
const{contentBundle}=await load('src/content/index.js'),{createRegistries}=await load('src/model/registries.js');
const{mapKnowledge}=await load('src/model/mapknowledge.js');const registries=createRegistries(contentBundle);
const source=fs.readFileSync(path.join(root,'src/ui/components/mapboard.js'),'utf8');
function extract(name,indent='  '){const rx=new RegExp('^'+indent+'(?:export )?function '+name+'\\([^]*?^'+indent+'\\}','m'),match=source.match(rx);if(!match)throw Error('Missing original function '+name);return match[0].replace('export function','function');}
const functions=['sizeSvg','apply','framingNodes','contextNodes','entranceFrame','showTitle','haloPeak','centerOnCurrent'].map(name=>extract(name)).join('\n');
const restoredSource=source.slice(source.indexOf('  const fitViewportMatches ='),source.indexOf('  // ---- zoom + centering'));
const execute=new Function('g','graph','request',`
 const {svgWidth,svgHeight,nodeX,nodeY,nodeRadius,framingBox,fitZoom,clampZoom,ZOOM_STEPS,ZOOM_MIN,MAP_ZOOM_DEFAULT}=g;
 ${extract('savedZoom','')} ${extract('snapToLadder','')}
 const nodes=Object.values(graph.nodes),byId=graph.nodes,map=graph,act=graph,run={mapNodeId:request.currentId||null};
 const height=svgHeight(Math.max(...nodes.map(n=>n.floor))),width=svgWidth(graph.columns);
 const reachable=new Set(request.reachableIds),visible=new Set(request.visibleIds),isDrawn=id=>visible.has(id),HALO_PAD=request.haloPad;
 const viewer={meta:{settings:{mapZoom:request.setting}}},setting=request.setting,candidate=request.savedState;
 const svgEl={style:{},setAttribute(){},querySelectorAll(){return[];}},scroll={clientWidth:request.width,clientHeight:request.height,scrollLeft:0,scrollTop:0,get scrollHeight(){return parseFloat(svgEl.style.height)||0;}};
 const titleEl={style:{},setAttribute(){},getBBox(){return request.titleBand?{y:request.titleBand.y0,height:request.titleBand.y1-request.titleBand.y0}:null;}};
 const getComputedStyle=()=>({getPropertyValue:()=>String(request.haloPeak)});
 ${restoredSource}
 const inkBox=framingBox(nodes.filter(n=>isDrawn(n.id)),height)||{x0:0,y0:0,x1:width,y1:height};
 let content={x0:0,y0:0,w:width,h:height},aimX=restored?restored.aimX:(inkBox.x0+inkBox.x1)/2;
 function report(){} function reportEntrance(){}
 ${functions}
 if(restored){sizeSvg();scroll.scrollTop=Math.min(Math.max(0,scroll.scrollHeight-scroll.clientHeight),restored.scrollTop);const fs=framingNodes();if(!run.mapNodeId&&fs.length)entranceFrame(fs,framingBox(fs,height));}else centerOnCurrent();
 const fs=framingNodes(),box=framingBox(fs,height),end=!run.mapNodeId&&fs.length?entranceFrame(fs,box).end:null;
 function miss(b){return b?Math.max(0,-(b.x0-content.x0)*zoom,(b.x1-content.x0)*zoom-scroll.clientWidth,scroll.scrollTop-(b.y0-content.y0)*zoom,(b.y1-content.y0)*zoom-scroll.scrollTop-scroll.clientHeight):0;}
 return {zoom,framing,aimX,scrollTop:scroll.scrollTop,restored:!!restored,titleVisible:titleEl.style.visibility!=='hidden',content:{x0:content.x0,y0:content.y0,x1:content.x0+content.w,y1:content.y0+content.h},decisionMiss:miss(box),entranceMiss:miss(end),entranceEndsFit:end?miss(end)<=.5:null};
`);
const maps=[],fixtures=[];
for(const seed of[1,13,492])for(const act of[1,2,3])for(const shape of[null,{floors:7,columns:2},{floors:15,columns:7}]){
 const rng=createRng(seed),graph=buildActMap(registries,rng,act,shape),rngBefore=rng.getCounters(),mapIndex=maps.length;
 graph.actNumber=act;const height=geometry.svgHeight(Math.max(...Object.values(graph.nodes).map(n=>n.floor)));
 maps.push({seed,shape,graph,rngBefore,positions:Object.fromEntries(Object.values(graph.nodes).map(n=>[n.id,{x:geometry.nodeX(n.col),y:geometry.nodeY(n.floor,height),box:geometry.framingBox([n],height)}]))});
 const byId=graph.nodes,pathIds=[];let current=null;
 for(let walk=0;walk<99;walk++){
  if(walk<2||walk===3||current&&['shrine','boss'].includes(byId[current].type))for(const mode of['fog','path']){
   const know=mapKnowledge({graph,run:{path:pathIds,mapNodeId:current},mode,reveal:false});
   for(const viewport of[{width:432,height:600},{width:433,height:756},{width:1120,height:640}])for(const setting of['Fit','115','200']){
    const request={actNumber:act,currentId:current,reachableIds:current?byId[current].next:graph.startIds,visibleIds:[...know.drawn],startIds:graph.startIds,...viewport,setting,titleBand:{x0:0,y0:8,x1:100,y1:29},haloPad:6,haloPeak:1.35};
    const expected=execute(geometry,graph,request);fixtures.push({mapIndex,request,expected});
   }
  }
  const choices=current?byId[current].next:graph.startIds;if(!choices.length)break;current=choices[0];pathIds.push(current);
 }
 if(JSON.stringify(rng.getCounters())!==JSON.stringify(rngBefore))throw Error('Camera changed original RNG');
}
// Actual original restore predicate and fixed/manual versus viewport-dependent Fit.
for(const fixture of fixtures.slice(0,18))for(const change of['same','width','node','setting','negative','manual']){
 const {request:q,expected:e}=fixture,request=structuredClone(q);
 request.savedState={actNumber:q.actNumber,nodeId:q.currentId,setting:q.setting,zoom:e.zoom,framing:e.framing,scrollLeft:0,scrollTop:e.scrollTop,aimX:e.aimX,viewportWidth:q.width,viewportHeight:q.height};
 if(change==='width')request.width-=100;if(change==='node')request.savedState.nodeId='stale';if(change==='setting')request.savedState.setting='175';if(change==='negative')request.savedState.scrollTop=-1;if(change==='manual'){request.savedState.framing='manual';request.savedState.scrollTop=12;request.width-=50;}
 fixtures.push({mapIndex:fixture.mapIndex,request,expected:execute(geometry,maps[fixture.mapIndex].graph,request)});
}
const helpers=[];for(const tap of[24,36,44,56,60])for(const scale of[.74,.9,1,1.5])for(const z of[1,1.15,1.3,1.5,1.75,2])helpers.push({tap,scale,z,radius:geometry.nodeRadiusFor(tap,z,scale),delivered:geometry.deliveredNodePx(geometry.nodeRadiusFromTap(tap),z,scale),authoredRadius:geometry.nodeRadiusFromTap(tap)});
const data={sourceCommit:sha,constants:{columnPitch:geometry.COL_X,rowPitch:geometry.ROW_H,nodeRadius:geometry.NODE_R,bossRadius:geometry.BOSS_R},maps,fixtures,helpers};const bytes=JSON.stringify(data)+'\n';
fs.writeFileSync(path.join(out,'reference.json'),bytes);
const sourceFiles=['src/model/mapview.js','src/ui/components/mapboard.js','src/model/mapknowledge.js','src/content/balance.js','src/engine/actmap.js','src/engine/rng.js'];
fs.writeFileSync(path.join(out,'reference.receipt.json'),JSON.stringify({sourceCommit:sha,sources:Object.fromEntries(sourceFiles.map(p=>[p,createHash('sha256').update(fs.readFileSync(path.join(root,p))).digest('hex')])),outputSha256:createHash('sha256').update(bytes).digest('hex'),maps:maps.length,cameraFixtures:fixtures.length,helpers:helpers.length,layoutAdapter:'Numeric viewport, title band and content extent; no browser/font/scrollbar rounding claimed'},null,2)+'\n');
console.log('Original viewport export: '+maps.length+' maps, '+fixtures.length+' camera fixtures, '+helpers.length+' helper cases');
