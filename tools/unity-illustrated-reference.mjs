// Reuse the published Card Studio documents instead of recreating their layout.
//   node tools/unity-illustrated-reference.mjs <published-898-tree>
//   node tools/unity-illustrated-reference.mjs <ashenspire-checkout> --build 1815
// 898 writes TestResults/HtmlParity/test898/illustrated-reference.json (unchanged).
// 1815 reads the JS dev checkout (0.7.1.1815) and its hd-assets-v23 art cache, renders
// the composited card paintings and SVG marks with Playwright Chromium, and writes
// TestResults/HtmlParity/js1815/{illustrated-reference.json,rendered/}. Then run
// python3 tools/import-published-ui-art.py <same checkout> --build 1815.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execSync} from 'node:child_process';
import {pathToFileURL} from 'node:url';
const args=process.argv.slice(2);
const buildFlag=args.indexOf('--build');
const build=buildFlag>=0?Number(args[buildFlag+1]):898;
const source=path.resolve(args.find((a,i)=>!a.startsWith('--')&&args[i-1]!=='--build')||'');
const stamp=JSON.parse(fs.readFileSync(path.join(source,'buildordinal.json'),'utf8'));
const load=file=>import(pathToFileURL(path.join(source,file)).href);
const sha=value=>crypto.createHash('sha256').update(value).digest('hex');
if(build===898)await build898();
else if(build===1815)await build1815();
else throw Error('Unknown reference build '+build);

async function build898(){
 if(stamp.ordinal!==898||stamp.digest!=='1b60c22e01')throw Error('Expected published test 898.');
 const {CARD_COMPONENTS}=await load('src/content/cardComponents.generated.js');
 const {illustratedArtwork}=await load('src/ui/components/illustratedCard.js');
 const {playingCardArtwork}=await load('src/ui/cardArtwork.js');
 const {contentBundle}=await load('src/content/index.js');
 const docs=structuredClone(CARD_COMPONENTS),assets={};
 function asset(href,trim){
  if(!href)return null;
  const key=sha(JSON.stringify([href,trim||null])).slice(0,20);
  assets[key]={href,trim:trim||null,resource:'Art/html898/'+key};return assets[key].resource;
 }
 for(const doc of [docs.template,...Object.values(docs.cards)])for(const layer of doc.layers){
  if(layer.type==='image'&&layer.href)layer.resource=asset(layer.href,layer.trim);
 }
 const artwork={};
 for(const card of contentBundle.cards){
  const art=illustratedArtwork({...card,cardId:card.id},card.id);
  if(art)artwork[card.id]={...art,resource:asset(art.path)};
 }
 const profiles={};
 for(const profile of contentBundle.equipment.basicCardProfiles){
  const art=playingCardArtwork({profileId:profile.id});if(art)profiles[profile.id]=asset(art);
 }
 assets['title-city-tower']={href:'assets/bg/title-city-tower.webp',trim:null,resource:'Art/html898/title-city-tower'};
 assets['title-traveler']={href:'assets/player-polish/illustrations/title-traveler.webp',trim:null,resource:'Art/html898/title-traveler'};
 const output=path.resolve('TestResults/HtmlParity/test898/illustrated-reference.json');
 fs.writeFileSync(output,JSON.stringify({referenceBuild:898,sourceDigest:stamp.digest,sourceSha256:sha(fs.readFileSync(path.join(source,'src/content/cardComponents.generated.js'))),documents:docs,artwork,profiles,assets},null,2)+'\n');
 console.log(JSON.stringify({documents:Object.keys(docs.cards).length,assets:Object.keys(assets).length,svg:Object.values(assets).filter(a=>a.href.endsWith('.svg')).length,output}));
}

// ---------------------------------------------------------------------------
// Build 1815. Geometry below is copied from the JS source it names, not invented:
// illustratedCard.js (sigil lift .061, footer band rect, actionBaseFrame) and
// styles/illustrated-cards.css (title fade, footer backdrop, tag rail, sigil band).
async function build1815(){
 if(stamp.ordinal!==1815||stamp.digest!=='86d6b73880')throw Error('Expected JS dev build 0.7.1.1815.');
 const cache=path.join(source,'.art-cache/hd-assets-v23');
 const out=path.resolve('TestResults/HtmlParity/js1815');
 const rendered=path.join(out,'rendered');
 fs.rmSync(rendered,{recursive:true,force:true});fs.mkdirSync(rendered,{recursive:true});
 const {CARD_COMPONENTS}=await load('src/content/cardComponents.generated.js');
 const {illustratedArtwork}=await load('src/ui/components/illustratedCard.js');
 const {cardArtLayersHtml}=await load('src/ui/cardArtLayers.js');
 const {contentBundle}=await load('src/content/index.js');
 const {createRegistries}=await load('src/model/registries.js');
 const {playingCardModel,playingCardDefinition}=await load('src/model/playingCard.js');
 const {combatProfileFor,combatProfileTags}=await load('src/model/combatCardProfile.js');
 const {cardSideTags,cardTagRailHtml}=await load('src/ui/components/cardTagSymbols.js');
 const {ACTION_SIGILS,mirrorSigil}=await load('src/content/combatSigils.js');
 const registries=createRegistries(contentBundle);
 const unityContent=JSON.parse(fs.readFileSync(path.resolve('Unity/Assets/AshenSpire/Resources/Original/content.json'),'utf8'));
 const prefix='Art/js1815/cards/';
 const assets={};
 const located=href=>{
  for(const base of [path.join(cache,'high'),path.join(cache,'common'),source]){const file=path.join(base,href);if(fs.existsSync(file))return file;}
  throw Error('Missing art '+href);
 };
 function fileAsset(href,{trim=null,cut=null,maxSide=1024}={}){
  if(!href)return null;
  const key=sha(JSON.stringify([href,trim,cut])).slice(0,20);
  assets[key]??={href,file:path.relative(source,located(href)),trim,cut,maxSide,resource:prefix+key};
  return assets[key].resource;
 }
 // Rendered assets carry their exact HTML so the receipt can be reproduced.
 const jobs=[];
 function renderAsset(kind,html,width,height,scale,provenance){
  const key=kind+'-'+sha(html+'|'+width+'|'+height+'|'+scale).slice(0,16);
  if(!assets[key]){assets[key]={rendered:'rendered/'+key+'.png',kind,size:[width,height],scale,provenance,htmlSha256:sha(html),resource:prefix+key};jobs.push({key,html,width,height,scale});}
  return assets[key].resource;
 }
 const fileUrl=href=>pathToFileURL(located(href)).href;

 // Template: keep the drawing data only. Card documents differ from the
 // template only where a card overrides more than its artwork href.
 const keep=['id','name','width','height','clipShape','layers','clipPolygon','equipmentArtwork','costLayouts','referenceRects'];
 const slim=doc=>Object.fromEntries(keep.filter(k=>doc[k]!==undefined).map(k=>[k,structuredClone(doc[k])]));
 const template=slim(CARD_COMPONENTS.template);
 const strip=doc=>JSON.stringify(doc.layers.map(l=>l.bind==='artwork'?{...l,href:null,imageWidth:null,imageHeight:null}:l));
 const bespoke={};
 for(const [id,doc] of Object.entries(CARD_COMPONENTS.cards))if(strip(doc)!==strip(CARD_COMPONENTS.template))bespoke[id]=slim(doc);
 const W=template.width,H=template.height;
 // illustratedCard.js: the base, panel and flag split into fill and trim planes.
 const interiors={base:'6% 10%,12% 5%,88% 5%,94% 10%,94% 90%,88% 96%,12% 96%,6% 90%'};
 for(const doc of [template,...Object.values(bespoke)])for(const layer of doc.layers){
  if(layer.type!=='image'||!layer.href)continue;
  // A card without 1815 art keeps Unity's own painting, never the placeholder.
  if(layer.bind==='artwork'){delete layer.href;continue;}
  layer.resource=fileAsset(layer.href,{trim:layer.trim||null});
  if(interiors[layer.id])layer.trimResource=fileAsset(layer.href,{trim:layer.trim||null,cut:interiors[layer.id].split(',').map(p=>p.trim().split(' ').map(v=>parseFloat(v)/100))});
 }
 // The combat face (model.sigils) raises the parchment and shortens the art.
 const lift=H*.061;
 const art=template.layers.find(l=>l.bind==='artwork');
 const artWindow={x:art.x,y:art.y,w:art.w,h:art.h-lift};
 // Only cards the Unity catalog can deal are painted; others use the face metadata alone.
 const unityCards=new Set(unityContent.cards.map(c=>c.id));
 const artScale=1;
 const rules={combatExpansionVersion:2};
 const artwork={},kinds={};
 const cards={};
 for(const card of contentBundle.cards){
  const ref={cardId:card.id};
  // The current game deals every card under expanded combat (combatExpansionVersion 2):
  // it decides the Evade/Counter identity, the art family and the footer word.
  const def=playingCardDefinition(registries,ref,{rules});
  const composition=illustratedArtwork(ref,card.id,{definition:def,registries});
  if(composition&&unityCards.has(card.id)){
   const cover=composition.fit==='cover'||composition.path.startsWith('assets/cards/extended/');
   const img=`<img src="${fileUrl(composition.path)}" style="position:absolute;inset:0;width:100%;height:100%;object-fit:${cover?'cover':'contain'};object-position:${composition.position||'50% 50%'};${composition.filter?`filter:${composition.filter};`:''}">`;
   let layers=cardArtLayersHtml(composition);
   for(const sprite of composition.componentArt?.sprites||[])layers=layers.split(`src="${sprite.path}"`).join(`src="${fileUrl(sprite.path)}"`);
   const html=`<div id="shot" style="position:relative;width:${artWindow.w}px;height:${artWindow.h}px;overflow:hidden">${img}${layers}</div>`;
   artwork[card.id]={kind:composition.kind,code:composition.componentArt?.code||null,source:composition.path,fit:'fill',resource:renderAsset('art',html,artWindow.w,artWindow.h,artScale,composition.path)};
   kinds[composition.kind]=(kinds[composition.kind]||0)+1;
  }
  const raw=playingCardModel(registries,ref,{definition:def,rules});
  const combatTags=combatProfileTags(combatProfileFor(def),registries);
  const model={...raw,tags:[...raw.tags,...combatTags.filter(t=>!raw.tags.some(e=>e.id===t.id))]};
  const side=cardSideTags(model,def,registries,null,rules);
  const sigils=raw.sigils;
  const mark=sigils.mirror?mirrorSigil(sigils.mirror).mark:ACTION_SIGILS[sigils.action];
  cards[card.id]={action:sigils.action,mirror:sigils.mirror?mirrorSigil(sigils.mirror).id:null,label:mark.label,
   tags:side.slice(0,5).map(t=>({id:t.id,label:t.label,...(Number.isInteger(t.count)&&t.count>=0?{count:t.count}:{})}))};
 }
 // Marks: one PNG per distinct SVG shape, coloured and shadowed as the CSS draws them.
 // Units are card units (a 360-wide card); pad leaves room for the drop shadows.
 const tagStyle={size:W*.08,gap:W*.02,right:W*.09,top:H*.16,pad:8,color:'#e8bd78',
  filter:`drop-shadow(0 0 ${W*.0065}px #000) drop-shadow(0 0 ${W*.011}px #000) drop-shadow(0 ${W*.007}px ${W*.007}px #000)`};
 const sigilStyle={size:W*.115,gap:W*.04,pad:8,color:'#efd09a',filter:`drop-shadow(0 ${W*.005}px ${W*.005}px #080604)`};
 const markScale=2.5;
 function markAsset(svg,style){
  const box=style.size+style.pad*2;
  const html=`<div id="shot" style="position:relative;width:${box}px;height:${box}px"><div style="position:absolute;left:${style.pad}px;top:${style.pad}px;width:${style.size}px;height:${style.size}px;color:${style.color};filter:${style.filter}">${svg.replace('<svg ','<svg style="display:block;width:100%;height:100%" ')}</div></div>`;
  return renderAsset('mark',html,box,box,markScale,'svg');
 }
 const sigilSvg=id=>`<svg viewBox="0 0 32 32" fill="${ACTION_SIGILS[id].solid?'currentColor':'none'}" fill-rule="evenodd" stroke="${ACTION_SIGILS[id].solid?'none':'currentColor'}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${ACTION_SIGILS[id].shape}</svg>`;
 const sigils=Object.fromEntries(Object.entries(ACTION_SIGILS).map(([id,m])=>[id,{label:m.label,resource:markAsset(sigilSvg(id),sigilStyle)}]));
 const tagSvg=id=>cardTagRailHtml([{id,label:id}]).match(/<svg[^>]*>.*?<\/svg>/s)[0];
 const tagIds=new Set([...contentBundle.tags.map(t=>t.id),...unityContent.tags.map(t=>t.id),...Object.values(cards).flatMap(c=>c.tags.map(t=>t.id))]);
 const tagSymbols={};
 for(const id of [...tagIds].sort())tagSymbols[id]=markAsset(tagSvg(id),tagStyle);
 // CSS-drawn backdrops and the footer's SVG braces, rendered from their own rules.
 const face={
  lift,artWindow,
  tagsBand:{x:W*.1,y:H*.858,w:W*.8,h:H*.116,paddingX:W*.03,paddingBottom:W*.006,fontSize:W*.07,letterSpacing:.085,color:'#efd09a'},
  titleFade:{x:W*.06,y:H*.035,w:W*.88,h:H*.22,resource:renderAsset('fade',`<div id="shot" style="width:${W*.88}px;height:${H*.22}px;border-radius:${W*.03}px ${W*.03}px 0 0;background:linear-gradient(to bottom,#15110ff5 0%,#15110ff5 48%,#15110fb3 66%,#15110f00 100%)"></div>`,W*.88,H*.22,2,'styles/illustrated-cards.css .card-title-fade')},
  footerBackdrop:{x:W*.06,y:H*.853,w:W*.88,h:H*.113,resource:renderAsset('fade',`<div id="shot" style="width:${W*.88}px;height:${H*.113}px;background:linear-gradient(#100d0980,#08070540)"></div>`,W*.88,H*.113,2,'styles/illustrated-cards.css .card-footer-backdrop')},
  actionFrame:{x:0,y:H*.84,w:W,h:H*.16,resource:renderAsset('frame',`<div id="shot" style="width:${W}px;height:${H*.16}px;overflow:hidden;position:relative"><svg viewBox="0 0 360 540" preserveAspectRatio="none" style="position:absolute;left:0;top:${-H*.84}px;width:${W}px;height:${H}px"><path d="M16 460H344M16 458V483L48 525H312L344 483V458" fill="none" stroke="#17110b" stroke-width="5"/><path d="M17 460H343M17 458V483L49 525H311L343 483V458" fill="none" stroke="#a37b43" stroke-width="2.4"/><path d="M19 460H341M19 462V482L50 523H310L341 482V462" fill="none" stroke="#e0bc7c" stroke-width=".7"/><path d="M20 467 39 482 20 501M340 467 321 482 340 501" fill="none" stroke="#21180e" stroke-width="4"/><path d="M20 467 39 482 20 501M340 467 321 482 340 501" fill="none" stroke="#a98247" stroke-width="1.4"/></svg></div>`,W,H*.16,2.5,'src/ui/components/illustratedCard.js actionBaseFrame')},
  tagRail:{...tagStyle,filter:undefined},
  sigil:{...sigilStyle,filter:undefined},
  rules:{up:'#205e2b',down:'#8d2424'},
  title:{stroke:'#100b06',strokeWidth:W*.0065},
 };
 await renderAll(jobs,out);
 const output=path.join(out,'illustrated-reference.json');
 const sourceCommit=execSync('git -C '+JSON.stringify(source)+' rev-parse HEAD',{encoding:'utf8'}).trim();
 fs.writeFileSync(output,JSON.stringify({referenceBuild:1815,sourceDigest:stamp.digest,sourceCommit,
  sourceSha256:sha(fs.readFileSync(path.join(source,'src/content/cardComponents.generated.js'))),
  documents:{template,cards:bespoke},face,artwork,profiles:{},cards,sigils,tagSymbols,assets},null,1)+'\n');
 console.log(JSON.stringify({bespoke:Object.keys(bespoke).length,cards:Object.keys(cards).length,artwork:Object.keys(artwork).length,kinds,
  rendered:jobs.length,files:Object.values(assets).filter(a=>a.href).length,output}));
}

async function renderAll(jobs,out){
 let playwright;
 try{playwright=await import('playwright');}
 catch{playwright=await import(pathToFileURL(path.join(execSync('npm root -g',{encoding:'utf8'}).trim(),'playwright/index.mjs')).href);}
 const browser=await playwright.chromium.launch();
 try{
  const blank=path.join(out,'blank.html');fs.writeFileSync(blank,'<!doctype html><html><body style="margin:0;background:transparent"></body></html>');
  for(const scale of [...new Set(jobs.map(j=>j.scale))]){
   const page=await browser.newPage({deviceScaleFactor:scale,viewport:{width:800,height:800}});
   await page.goto(pathToFileURL(blank).href);
   for(const job of jobs.filter(j=>j.scale===scale)){
    await page.evaluate(html=>{document.body.innerHTML=html;},job.html);
    await page.evaluate(()=>Promise.all([...document.images].map(i=>i.complete?null:new Promise(r=>{i.onload=i.onerror=r;}))));
    const broken=await page.evaluate(()=>[...document.images].filter(i=>!i.naturalWidth).map(i=>i.src));
    if(broken.length)throw Error('Unloaded image '+broken.join(', '));
    await page.locator('#shot').screenshot({path:path.join(out,'rendered',job.key+'.png'),omitBackground:true});
   }
   await page.close();
  }
  fs.rmSync(blank);
 }finally{await browser.close();}
}
