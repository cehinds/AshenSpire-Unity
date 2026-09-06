// Read-only Unity control bounds guide real mouse input; no commands or state injection.
// Usage: node tools/campaign-playtest.cjs [url] [evidenceDirectory] [--full]
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const fs=require('node:fs'),path=require('node:path');
let browser,activePage,evidenceDirectory,lastEvidence;
(async()=>{
 const output=path.resolve(process.argv[3]||'Published/Screenshots');fs.mkdirSync(output,{recursive:true});evidenceDirectory=output;
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:1});activePage=page;
 let controls,state,revision=0,layout=0,layoutAtState=0;const errors=[],shots=[];
 page.on('pageerror',e=>errors.push(e.message));
 page.on('console',message=>{const value=message.text();if(message.type()==='error')errors.push(value);let i=value.indexOf('ASHENSPIRE_CAMPAIGN ');if(i>=0){state=JSON.parse(value.slice(i+19));revision++;layoutAtState=layout;}i=value.indexOf('ASHENSPIRE_CONTROLS ');if(i>=0){controls=JSON.parse(value.slice(i+19));layout++;}lastEvidence={state,controls,revision,layout,layoutAtState,errors};});
 const url=process.argv[2]||'http://127.0.0.1:8787';
 async function until(predicate,description,timeout=15000){const end=Date.now()+timeout;while(Date.now()<end){if(predicate())return;await page.waitForTimeout(100);}throw new Error('Timed out: '+description);}
 async function load(){controls=null;await page.goto(url);await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:90000});await until(()=>controls?.Controls.length,'control layout');}
 async function click(id,changesState=false,playEnabled=true){
  await until(()=>controls?.Controls.some(x=>x.Id===id&&x.Enabled&&x.Width>0&&x.Height>0),'enabled control '+id);
  for(let attempt=0;attempt<12;attempt++){
   const control=controls.Controls.find(x=>x.Id===id);if(!control?.Enabled)throw new Error('Disabled control: '+id);
   const viewport=page.viewportSize();const x=(control.X+control.Width/2)*viewport.width/controls.PanelWidth;const y=(control.Y+control.Height/2)*viewport.height/controls.PanelHeight;
   const bottom=viewport.height-(['play','end-turn','inspection-back'].includes(id)?5:controls.Controls.some(x=>x.Id==='end-turn'||x.Id==='inspection-back')?105:20);
   if(y<30||y>bottom){const previous=layout;await page.mouse.move(viewport.width/2,viewport.height/2);await page.mouse.wheel(0,y<30?-450:450);await until(()=>layout>previous,'scroll layout');continue;}
   const previous=changesState?revision:layout;await page.mouse.click(x,y,{delay:120});await until(()=>(changesState?revision:layout)>previous,'result of '+id);
   if(changesState)await until(()=>layout>layoutAtState&&controls.Controls.every(control=>control.Width>0&&control.Height>0),'rendered state after '+id);
   if(id.startsWith('card-'))await until(()=>controls.Controls.some(control=>control.Id==='play'&&control.Enabled===playEnabled),'selected card after '+id);
   await page.waitForTimeout(150);return;
  }throw new Error('Cannot scroll to '+id);
 }
 async function shot(name){await page.mouse.move(0,0);await page.waitForTimeout(300);await page.screenshot({path:path.join(output,name+'.png')});shots.push(name);}
 function assert(value,description){if(!value)throw new Error(description);}
 async function inspect(id,expected,name){
  const before=JSON.stringify(state),beforeRevision=revision;
  await click(id);assert(controls.Labels.some(text=>text.includes(expected)),id+' explanation missing');
  assert(!controls.Controls.some(x=>x.Id==='play'||x.Id==='end-turn'),'combat actions must not remain under inspection');
  if(name)await shot(name);await click('inspection-back');
  assert(JSON.stringify(state)===before&&revision===beforeRevision,id+' changed campaign state');
 }
 const firstLoadStarted=Date.now();await load();const firstLoadMilliseconds=Date.now()-firstLoadStarted;
 const expectedVersion=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../Published/build.json'),'utf8')).version;
 assert((await page.locator('#channel').innerText()).endsWith('UNITY '+expectedVersion),'visible build version differs from package');
 const downloadedResourceBytes=await page.evaluate(()=>performance.getEntriesByType('resource').reduce((sum,item)=>sum+(item.encodedBodySize||0),0));
 const heroOption=process.argv.indexOf('--hero');const hero=heroOption>=0?process.argv[heroOption+1]:'reaver';
 await shot('01-phone-title');await click('new');await shot('02-class-selection');await click('hero-'+hero,true);await shot('03-campaign-map');
 await click('enter-0',true);await shot('04-phone-combat');
 await page.setViewportSize({width:320,height:740});await page.waitForTimeout(700);
 // Unity's transformed float bounds can report 43.99998 for a 44-pixel edge.
 for(const id of ['intent-details','status-details','draw-pile','discard-pile','action-history']){const c=controls.Controls.find(x=>x.Id===id);assert(c&&Math.round(c.Height*740/controls.PanelHeight*100)/100>=44,id+' touch target below 44 pixels');}
 await inspect('intent-details','will attack','18-intent-details');
 await inspect('status-details','ENEMY WEAK','19-status-details');
 await inspect('discard-pile','This pile is empty.','20-empty-discard');
 const beforePile=JSON.stringify(state),beforePileRevision=revision;await click('draw-pile');
 const contentForPiles=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../GameContent/Unity/campaign.json'),'utf8'));
 const grouped=[...new Set(state.Draw)].map(id=>({id,name:contentForPiles.Cards.find(c=>c.Id===id).Name,count:state.Draw.filter(x=>x===id).length})).sort((a,b)=>a.name<b.name?-1:a.name>b.name?1:a.id<b.id?-1:1);
 const shown=controls.Labels.filter(text=>text.includes(' × '));
 assert(shown.length===grouped.length&&shown.every((text,i)=>text.startsWith(grouped[i].count+' × '+grouped[i].name+' · ')),'draw pile must group counts by name without revealing order');
 await shot('21-draw-pile');await click('inspection-back');assert(JSON.stringify(state)===beforePile&&revision===beforePileRevision,'draw grouping changed state');
 await inspect('draw-pile','Draw order stays hidden.');
 await page.setViewportSize({width:390,height:844});await page.waitForTimeout(700);
 const beforePlay=JSON.stringify(state);const first=controls.Controls.find(x=>x.Id.startsWith('card-')&&x.Enabled);await click(first.Id);await shot('05-card-selected');await click('play',true);
 const commandsChangedState=beforePlay!==JSON.stringify(state);await shot('06-card-played');
 await inspect('discard-pile','cards · grouped by name','22-discard-pile');
 await inspect('action-history','energy spent.','23-action-history');
 await click('end-turn',true);await shot('07-next-turn');const saved=JSON.stringify(state);
 await load();await shot('08-resume-menu');await click('continue',true);const resumeStateMatches=saved===JSON.stringify(state);await shot('09-resumed-combat');
 await inspect('action-history','No actions recorded in this session.','25-resumed-history');
 const content=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../GameContent/Unity/campaign.json'),'utf8'));
 const cards=new Map(content.Cards.map(x=>[x.Id,x]));let completed=false;let bought=false;let unaffordableInspected=false;
 for(let step=0;step<1000;step++){
  if(state.Phase===3||state.Phase===4){completed=state.Phase===3;await shot(completed?'15-campaign-victory':'15-campaign-defeat');break;}
  if(state.Phase===0){
   if(!process.argv.includes('--full'))break;
   await click('shop');if(!bought)await shot('12-equipment-forge');
   const item=content.Equipment.find(x=>!state.Items.includes(x.Id)&&x.Price<=state.Cinders);
   if(item){await click('buy-'+item.Id,true);bought=true;}else await click('back');
   if(state.Health<state.MaxHealth-content.RestHealing&&state.Cinders>=15&&!state.Rested)await click('rest',true);
   await click('enter-0',true);if(state.Encounter===3||state.Encounter===6)await shot('13-act-'+content.Encounters[state.Encounter].Act);continue;
  }
  if(state.Phase===2){await shot('10-reward-'+state.Encounter);await click('reward-rest',true);if(state.Phase===0&&state.Encounter===1)await shot('11-next-map');continue;}
  if(process.argv.includes('--defeat')){await click('end-turn',true);continue;}
  const expensive=state.Hand.findIndex(id=>cards.get(id).Cost>state.Energy);
  if(expensive>=0&&!unaffordableInspected){const before=JSON.stringify(state);await click('card-'+expensive,false,false);assert(controls.Labels.some(text=>text.includes('Not enough energy.')),'unaffordable card needs an explanation');await shot('24-unaffordable-card');await click('card-'+expensive,false,false);assert(JSON.stringify(state)===before,'inspection or cancellation changed state');unaffordableInspected=true;}
  if(state.Health<=state.MaxHealth-content.PotionHealing&&state.Potions>0)await click('potion',true);
  let index=state.Hand.findIndex(id=>cards.get(id).Cost<=state.Energy&&cards.get(id).Effects.some(x=>x.Operation==='strength'));
  if(index<0)index=state.Hand.findIndex(id=>cards.get(id).Cost<=state.Energy&&cards.get(id).Tags.includes('attack'));
  if(index<0)index=state.Hand.findIndex(id=>cards.get(id).Cost<=state.Energy);
  if(index>=0){await click('card-'+index);await click('play',true);}else await click('end-turn',true);
 }
 await page.setViewportSize({width:1280,height:900});await page.waitForTimeout(700);await shot('16-desktop');
 await page.setViewportSize({width:844,height:390});await page.waitForTimeout(700);await shot('17-landscape');
 const report={url,firstLoadMilliseconds,downloadedResourceBytes,commandsChangedState,resumeStateMatches,inspectionPreservesState:true,pileGroupingChecked:true,narrowTouchTargetsChecked:true,unaffordableInspected,fullRunRequested:process.argv.includes('--full'),fullRunVictory:completed,equipmentPurchased:bought,finalState:state,screenshots:shots,errors,limits:['Desktop pointer automation; physical phones and native player interaction are not covered.','Load timing is from this desktop test environment and is not a mobile performance budget.']};fs.writeFileSync(path.join(output,'playtest.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
 if(!commandsChangedState||!resumeStateMatches||errors.length||(process.argv.includes('--full')&&!completed)||(process.argv.includes('--defeat')&&state.Phase!==4))process.exitCode=1;
})().catch(async error=>{console.error(error);if(activePage&&evidenceDirectory){await activePage.screenshot({path:path.join(evidenceDirectory,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(evidenceDirectory,'failure.json'),JSON.stringify({error:error.message,...lastEvidence},null,2));}process.exitCode=1;}).finally(async()=>{if(browser)await browser.close();});
