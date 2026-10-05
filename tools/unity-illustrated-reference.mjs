// Reuse the published Card Studio documents instead of recreating their layout.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {pathToFileURL} from 'node:url';
const source=path.resolve(process.argv[2]||'');
const stamp=JSON.parse(fs.readFileSync(path.join(source,'buildordinal.json'),'utf8'));
if(stamp.ordinal!==898||stamp.digest!=='1b60c22e01')throw Error('Expected published test 898.');
const load=file=>import(pathToFileURL(path.join(source,file)).href);
const {CARD_COMPONENTS}=await load('src/content/cardComponents.generated.js');
const {illustratedArtwork}=await load('src/ui/components/illustratedCard.js');
const {playingCardArtwork}=await load('src/ui/cardArtwork.js');
const {contentBundle}=await load('src/content/index.js');
const docs=structuredClone(CARD_COMPONENTS),assets={};
function asset(href,trim){
 if(!href)return null;
 const key=crypto.createHash('sha256').update(JSON.stringify([href,trim||null])).digest('hex').slice(0,20);
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
fs.writeFileSync(output,JSON.stringify({referenceBuild:898,sourceDigest:stamp.digest,sourceSha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(source,'src/content/cardComponents.generated.js'))).digest('hex'),documents:docs,artwork,profiles,assets},null,2)+'\n');
console.log(JSON.stringify({documents:Object.keys(docs.cards).length,assets:Object.keys(assets).length,svg:Object.values(assets).filter(a=>a.href.endsWith('.svg')).length,output}));
