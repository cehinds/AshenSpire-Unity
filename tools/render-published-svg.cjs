// Rasterize reference vectors with the bundled Sharp runtime; no source edits.
const fs=require('node:fs'),path=require('node:path');
const sharp=require(process.argv[3]);
const reference=path.resolve(process.argv[2]);
const root=path.resolve(__dirname,'..');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'TestResults/HtmlParity/test898/illustrated-reference.json'),'utf8'));
if(manifest.referenceBuild!==898)throw Error('Expected published test 898.');
const output=path.join(root,'TestResults/HtmlParity/test898/svg-rasterized');fs.mkdirSync(output,{recursive:true});
(async()=>{
 const rows=[];
 for(const [key,asset] of Object.entries(manifest.assets))if(asset.href.endsWith('.svg')){
  const source=path.join(reference,'.art-cache/hd-assets-v9/high',asset.href);
  const info=await sharp(source,{density:192}).resize(512,512,{fit:'contain',background:{r:0,g:0,b:0,alpha:0}}).png().toFile(path.join(output,key+'.png'));
  rows.push({key,href:asset.href,width:info.width,height:info.height});
 }
 fs.writeFileSync(path.join(output,'receipt.json'),JSON.stringify({renderer:'sharp',versions:sharp.versions,rows},null,2)+'\n');
 console.log(JSON.stringify({rasterized:rows.length,output}));
})().catch(error=>{console.error(error);process.exitCode=1;});
