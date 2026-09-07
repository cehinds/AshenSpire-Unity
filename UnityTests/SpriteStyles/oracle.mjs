// Reads the actual pinned original rendering functions with a DOM-shaped recorder.
// The recorder captures source branch decisions/CSS geometry, not rendered pixels.
import fs from 'node:fs';import path from 'node:path';import{pathToFileURL,fileURLToPath}from'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));const original=process.argv[2];if(!original)throw Error('Pass the exact original reference checkout path.');
class Element{constructor(tag){this.tag=tag;this.children=[];this.style={};this.dataset={};this.className='';this.innerHTML='';this.classList={add:v=>this.className+=' '+v,contains:v=>this.className.split(' ').includes(v)};}appendChild(el){this.children.push(el);return el;}addEventListener(){}setAttribute(k,v){this[k]=v;}querySelector(selector){if(selector==='svg'&&this.innerHTML.includes('<svg'))return new Element('svg');return null;}}
globalThis.document={createElement:tag=>new Element(tag),body:new Element('body')};globalThis.Image=class extends Element{constructor(){super('img');}};
const {execFileSync}=await import('node:child_process');if(execFileSync('git',['-C',original,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!=='b17a7f4543e1710f49fae8b58880121690a314de')throw Error('Wrong original reference SHA');
const source=await import(pathToFileURL(path.join(original,'src/ui/assets.js')));
const poses=await import(pathToFileURL(path.join(original,'src/ui/services/PoseAnimator.js')));
const data=JSON.parse(fs.readFileSync(path.join(here,'../../GameContent/Unity/Original/sprite-styles.json'),'utf8'));
const choices=[];const geometry=[];const rotations=[];
const styles=[undefined,'animated','rendered','classic','glyph','invalid'];const tints=[undefined,'gold','ember','frost','rot','grace','invalid'];
for(const classId of [...Object.keys(data.classes),'unknown'])for(const style of styles)for(const tint of tints)for(const armour of ['default','oathsworn','starlit','shadow','pilgrim','missing']){
 const customization={spriteStyle:style,tint,glyph:'⚔'};const element=source.playerSprite(customization,classId,armour);
 const stage=element.children[0]?.children[0];
 const resolved=element.className.includes('animated')?'animated':element.className==='class-sprite'?(element.children[0].innerHTML.includes('<svg')?'classic':'rendered'):'glyph';
 choices.push({classId,customization,armour,style:resolved,poseClass:resolved==='animated'?stage.dataset.poseClass:classId});
}
for(const [key,frame]of Object.entries(data.frames)){
 const split=key.split('_'),tint=split.pop(),pose=split.pop(),poseClass=split.join('_');const stage=poses.createPoseStage(poseClass,tint,'geometry');stage.setPose(pose);
 const layer=stage.el.children[0],image=layer.children[0];
 for(const[width,height]of[[150,190],[82.895,105],[330,418]]){
  const layerHeight=parseFloat(layer.style.height)/100*height;const layerWidth=layerHeight*data.canvas.width/data.canvas.height;
  const left=width/2+parseFloat(layer.style.transform.match(/translateX\(([^)]+)\)/)[1])/100*layerWidth;
  const top=parseFloat(layer.style.top)/100*height;
  geometry.push({poseClass,tint,pose,width,height,rect:[left+parseFloat(image.style.left)/100*layerWidth,top+parseFloat(image.style.top)/100*layerHeight,parseFloat(image.style.width)/100*layerWidth,parseFloat(image.style.height)/100*layerHeight]});
 }
}
for(const id of ['seatA','seatB'])for(let n=0;n<7;n++){const stage=poses.createPoseStage('reaver','gold',id);stage.play('attack',60);rotations.push({id,pose:stage.pose});stage.settle();}
fs.writeFileSync(path.join(here,'source-reference.json'),JSON.stringify({choices,geometry,rotations},null,2)+'\n');console.log(JSON.stringify({choices:choices.length,geometry:geometry.length,rotations:rotations.length}));
