// Deterministic format import of pinned original sprites, with source receipts.
// No repaint, crop, tint synthesis or pose retiming: WebP pixels decode to PNG.
// Classic uses original SVG builders; its text medallion is drawn by native UI.
import fs from 'node:fs';import path from 'node:path';import vm from 'node:vm';
import {fileURLToPath,pathToFileURL} from 'node:url';import{createRequire}from'node:module';
import{execFileSync}from'node:child_process';import{createHash}from'node:crypto';
const packageFolder=path.dirname(fileURLToPath(import.meta.url));
const original=process.argv[2], outputArgument=process.argv[3];
if(!original||!outputArgument)throw Error('Pass exact original reference checkout and an explicit output directory.');
const here=path.resolve(outputArgument);const originalRoot=path.resolve(original).toLowerCase();if(here.toLowerCase()===originalRoot||here.toLowerCase().startsWith(originalRoot+path.sep))throw Error('Output cannot mutate original reference.');fs.mkdirSync(here,{recursive:true});
const require=createRequire(import.meta.url);
const sharp=require(process.env.SHARP_MODULE??'sharp');
const commit='b17a7f4543e1710f49fae8b58880121690a314de';
if(execFileSync('git',['-C',original,'rev-parse','HEAD'],{encoding:'utf8'}).trim()!==commit)throw Error('Wrong source commit');
const pose=await import(pathToFileURL(path.join(original,'src/content/poseSprites.js')));
const styles=await import(pathToFileURL(path.join(original,'src/model/spriteStyle.js')));
const anchors=await import(pathToFileURL(path.join(original,'src/content/classArtAnchors.js')));
const appearance=JSON.parse(fs.readFileSync(path.join(packageFolder,'../../GameContent/Unity/Original/appearance-options.json'),'utf8'));
const assets=fs.readFileSync(path.join(original,'src/ui/assets.js'),'utf8');
const classicCode=assets.slice(assets.indexOf('function sigilMedallion('),assets.indexOf('// Class sprites (assets/sprites/):'));
const builders=vm.runInNewContext(classicCode+';CLASS_SVG');
const output=path.join(here,'Resources/Art/Styles');fs.mkdirSync(output,{recursive:true});
const data={schemaVersion:1,sourceCommit:commit,defaultStyle:styles.DEFAULT_SPRITE_STYLE,styles:styles.SPRITE_STYLES,canvas:pose.POSE_CANVAS,strip:pose.POSE_STRIP,frames:{},classes:{}};
const receipt=[];const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
async function convert(source,name){
 const bytes=fs.readFileSync(path.join(original,source));const target=path.join(output,name+'.png');
 fs.mkdirSync(path.dirname(target),{recursive:true}); await sharp(bytes).png().toFile(target);
 const sourcePixels=await sharp(bytes).ensureAlpha().raw().toBuffer();const pngPixels=await sharp(target).ensureAlpha().raw().toBuffer();
 if(!sourcePixels.equals(pngPixels))throw Error('Decoded pixels changed: '+source);
 receipt.push({source,sourceSha256:hash(bytes),file:name+'.png',sha256:hash(fs.readFileSync(target)),decodedPixelSha256:hash(pngPixels)});
 return 'Art/Styles/'+name;
}
for(const [key,frame]of pose.POSE_FRAMES)data.frames[key]={...frame,resource:await convert('assets/poses/'+frame.f,'Pose/'+key)};
for(const[classId,build]of Object.entries(builders)){
 const originalCall=classicCode.match(new RegExp(classId+':[\\s\\S]*?sigilMedallion\\(([^)]+)\\)'))[1].split(',').map(x=>x.trim());
 const row={rendered:{},classic:{},classicAnchor:[Number(originalCall[0]),Number(originalCall[1])],renderedMedallionPct:anchors.medallionPct(classId)};data.classes[classId]=row;
 for(const tint of appearance.tints){
  row.rendered[tint.id]=await convert('assets/sprites/'+classId+'_'+tint.id+'.webp','Rendered/'+classId+'_'+tint.id);
  const svg=build(tint.color,null);const name='Classic/'+classId+'_'+tint.id;
  fs.mkdirSync(path.dirname(path.join(output,name)),{recursive:true});
  fs.writeFileSync(path.join(output,name+'.svg'),svg);
  await sharp(Buffer.from(svg)).resize(330,420).png().toFile(path.join(output,name+'.png'));
  row.classic[tint.id]='Art/Styles/'+name;
  receipt.push({source:'src/ui/assets.js CLASS_SVG.'+classId,svgSha256:hash(Buffer.from(svg)),file:name+'.png',sha256:hash(fs.readFileSync(path.join(output,name+'.png')))});
 }
}
fs.writeFileSync(path.join(here,'sprite-styles.json'),JSON.stringify(data,null,2)+'\n');
fs.writeFileSync(path.join(here,'asset-receipts.json'),JSON.stringify({sourceCommit:commit,decodedPixelComparisons:pose.POSE_FRAMES.size+Object.keys(builders).length*appearance.tints.length,files:receipt},null,2)+'\n');
console.log(JSON.stringify({frames:Object.keys(data.frames).length,classes:Object.keys(data.classes),resources:receipt.length,decodedPixelComparisons:pose.POSE_FRAMES.size+20}));

