// Read-only Unity control bounds guide real mouse input; no commands or state injection.
// Usage: node tools/campaign-playtest.cjs [url] [evidenceDirectory] [--full]
const {controlReportsForPage}=require('./control-report.cjs');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
let browser,activePage,evidenceDirectory,lastEvidence,server,lastInput;const scrollStops=[];
(async()=>{
 const output=path.resolve(process.argv[3]||'Published/Screenshots');fs.mkdirSync(output,{recursive:true});evidenceDirectory=output;
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const dprIndex=process.argv.indexOf('--dpr'),deviceScaleFactor=dprIndex<0?1:Number(process.argv[dprIndex+1]);
 if(!Number.isFinite(deviceScaleFactor)||deviceScaleFactor<1||deviceScaleFactor>4)throw Error('Use --dpr between 1 and 4');
 const touch=process.argv.includes('--touch');
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor,hasTouch:touch,isMobile:touch,...(touch?{userAgent:'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/'+browser.version()+' Mobile Safari/537.36'}:{})});activePage=page;
 const touchSession=touch?await page.context().newCDPSession(page):null;
 async function renderedTouchFrame(){
  // CDP acknowledges dispatch before Unity's frame loop consumes the event.
  // Cross two animation frames so input phases remain distinct on slow renderers.
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 }
 async function pressKey(key){
  await page.keyboard.down(key);await renderedTouchFrame();await page.waitForTimeout(120);
  await page.keyboard.up(key);await renderedTouchFrame();
 }
 async function tap(x,y){
  if(!touch){await page.mouse.click(x,y,{delay:120});return;}
  // Hold a real touch across player frames, just as mouse clicks use a 120 ms press.
  await touchSession.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]});
  await renderedTouchFrame();
  await page.waitForTimeout(120);
  await touchSession.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
  await renderedTouchFrame();
 }
 async function scroll(distance){
  const beforeScroll=layout;
  const box=await page.locator('#unity-canvas').boundingBox();
  if(!touch){await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.wheel(0,distance);return;}
  const contentHeight=box.height-(controls?.Controls.some(c=>c.Id==='end-turn'||c.Id==='inspection-back')?110:0);
  const x=box.x+box.width/2,start=box.y+contentHeight*(distance>0?.8:.2),end=box.y+contentHeight*(distance>0?.2:.8);
  await touchSession.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y:start}]});
  await renderedTouchFrame();
  for(let step=1;step<=10;step++){await touchSession.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y:start+(end-start)*step/10}]});await renderedTouchFrame();}
  await touchSession.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
  await renderedTouchFrame();
  // On slow software renderers, the browser can finish dispatching a swipe before
  // Unity processes it. Start the settling window only after its first report.
  await until(()=>layout>beforeScroll,'Unity received touch scroll');
  // ScrollView deliberately consumes a touch while inertia is active. Stop it
  // with a separate tap in blank padding, then aim at a control. The body has
  // 18 panel units of side padding; eight units left of every named control is
  // inside the scroll viewport and outside all interactive control rectangles.
  const beforeStop=JSON.stringify(state),beforeIds=controls.Controls.map(c=>c.Id).join('|');
  const panelX=Math.min(...controls.Controls.map(c=>c.X))-8;
  const stopX=box.x+panelX*box.width/controls.PanelWidth,stopY=box.y+contentHeight/2;
  const panelY=(stopY-box.y)*controls.PanelHeight/box.height;
  assert(stopX>box.x&&stopX<box.x+box.width,'scroll stop outside canvas');
  assert(!controls.Controls.some(c=>panelX>=c.X&&panelX<=c.X+c.Width&&panelY>=c.Y&&panelY<=c.Y+c.Height),'scroll stop overlaps a control');
  await tap(stopX,stopY);
  // ScrollView inertia can continue after release. Use stable observed geometry before
  // choosing the next tap, rather than accepting a scroll report as a button response.
  let signature=JSON.stringify(controls),stableSince=Date.now();
  await until(()=>{const next=JSON.stringify(controls);if(next!==signature){signature=next;stableSince=Date.now();}return Date.now()-stableSince>=500;},'settled touch scroll');
  assert(JSON.stringify(state)===beforeStop&&controls.Controls.map(c=>c.Id).join('|')===beforeIds,'padding stop changed game state or navigation');
  scrollStops.push({x:stopX,y:stopY,stateUnchanged:true,controlsUnchanged:true});
 }
 let controls,state,revision=0,layout=0,layoutAtState=0;const errors=[],shots=[],feedbackEvents=[],soundEvents=[],impactCaptures=[];let captureNextImpact=null;
 const normalizeControls=controlReportsForPage(page,e=>errors.push(e));
 page.on('pageerror',e=>errors.push(e.message));
 page.on('console',message=>{const value=normalizeControls(message.text());if(value===null)return;if(value.startsWith('ASHENSPIRE_FEEDBACK ')){const event=JSON.parse(value.slice(20));feedbackEvents.push(event);if(event.Status==='impact'&&captureNextImpact){const name=captureNextImpact;captureNextImpact=null;impactCaptures.push(page.screenshot({path:path.join(output,name+'.png')}).then(()=>shots.push(name)));}}if(value.startsWith('ASHENSPIRE_SOUND '))soundEvents.push(value.slice(17));if(message.type()==='error')errors.push(value);let i=value.indexOf('ASHENSPIRE_CAMPAIGN ');if(i>=0){state=JSON.parse(value.slice(i+19));revision++;layoutAtState=layout;}i=value.indexOf('ASHENSPIRE_CONTROLS ');if(i>=0){controls=JSON.parse(value.slice(i+19));layout++;}lastEvidence={state,controls,revision,layout,layoutAtState,errors,lastInput,feedbackEvents,soundEvents,scrollStops};});
 let url=process.argv[2]||'http://127.0.0.1:8787';
 const upgradeIndex=process.argv.indexOf('--upgrade-from');let servedDirectory;
 if(upgradeIndex>=0){
  if(!process.argv[upgradeIndex+1])throw new Error('Provide the previous Web folder after --upgrade-from');
  servedDirectory=path.resolve(process.argv[upgradeIndex+1]);
  server=http.createServer((request,response)=>{
   const pathname=decodeURIComponent(new URL(request.url,'http://localhost').pathname);
   const file=path.resolve(servedDirectory,'.'+(pathname==='/'?'/index.html':pathname));
   if(!file.startsWith(servedDirectory+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile()){response.writeHead(404).end();return;}
   response.writeHead(200,{'Content-Type':{'.wasm':'application/wasm','.js':'application/javascript','.html':'text/html','.json':'application/json'}[path.extname(file)]||'application/octet-stream','Cache-Control':'no-store'});fs.createReadStream(file).pipe(response);
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));url='http://127.0.0.1:'+server.address().port+'/';
 }
 async function until(predicate,description,timeout=15000){const end=Date.now()+timeout;while(Date.now()<end){if(predicate())return;await page.waitForTimeout(100);}throw new Error('Timed out: '+description);}
 async function load(){controls=null;await page.goto(url);await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:90000});await until(()=>controls?.Controls.length,'control layout');}
 // Unity applies browser resize and UI Toolkit geometry over separate frames.
 // Wait for the new aspect ratio and stable bounds, never for a passing size assertion.
 const viewportEvidence=[];
 async function resize(width,height,padding=null){
  const previous=layout;if(padding!==null)await page.locator('#frame').evaluate((frame,value)=>frame.style.padding=value,padding);
  await page.setViewportSize({width,height});const canvas=await page.locator('#unity-canvas').boundingBox();
  await until(()=>layout>previous&&Math.abs(controls.PanelWidth/controls.PanelHeight-canvas.width/canvas.height)<.001,'Unity geometry for '+width+'x'+height);
  let signature=JSON.stringify(controls),stableSince=Date.now();
  await until(()=>{const next=JSON.stringify(controls);if(next!==signature){signature=next;stableSince=Date.now();}return Date.now()-stableSince>=500&&Math.abs(controls.PanelWidth/controls.PanelHeight-canvas.width/canvas.height)<.001;},'stable viewport geometry');
  viewportEvidence.push({width,height,canvas,panelWidth:controls.PanelWidth,panelHeight:controls.PanelHeight,layout});
 }
 async function click(id,changesState=false,playEnabled=true){
  if (["new","continue","gallery","foundation"].includes(id) && controls?.Controls.some(c=>c.Id==="title-extras")) await click("title-extras");
  await until(()=>controls?.Controls.some(x=>x.Id===id&&x.Enabled&&x.Width>0&&x.Height>0),'enabled control '+id);
  for(let attempt=0;attempt<24;attempt++){
   const canvas=await page.locator('#unity-canvas').boundingBox();
   const control=controls.Controls.find(x=>x.Id===id);if(!control?.Enabled)throw new Error('Disabled control: '+id);
   const x=canvas.x+(control.X+control.Width/2)*canvas.width/controls.PanelWidth;const y=canvas.y+(control.Y+control.Height/2)*canvas.height/controls.PanelHeight;
   const bottom=canvas.y+canvas.height-(['play','end-turn','inspection-back'].includes(id)?5:controls.Controls.some(x=>x.Id==='end-turn'||x.Id==='inspection-back')?105:20);
   if(y<canvas.y+30||y>bottom){const previous=layout;await scroll(y<canvas.y+30?-450:450);await until(()=>layout>previous,'scroll layout');continue;}
   const previous=changesState?revision:layout;lastInput={id,x,y,canvas,control,layout,revision};await tap(x,y);await until(()=>(changesState?revision:layout)>previous,'result of '+id);
   if(changesState)await until(()=>layout>layoutAtState&&controls.Controls.every(control=>control.Width>0&&control.Height>0),'rendered state after '+id);
   if(id.startsWith('card-'))await until(()=>controls.Controls.some(control=>control.Id==='play'&&control.Enabled===playEnabled),'selected card after '+id);
   await page.waitForTimeout(150);return;
  }throw new Error('Cannot scroll to '+id);
 }
 async function shot(name){await page.mouse.move(0,0);await page.waitForTimeout(300);await page.screenshot({path:path.join(output,name+'.png')});shots.push(name);}
 function assert(value,description){if(!value)throw new Error(description);}
 async function inspect(id,expected,name){
  const before=JSON.stringify(state),beforeRevision=revision;
  await click(id);await until(()=>controls.Labels.some(text=>text.includes(expected)),'inspection content '+id);assert(controls.Labels.some(text=>text.includes(expected)),id+' explanation missing');
  assert(!controls.Controls.some(x=>x.Id==='play'||x.Id==='end-turn'),'combat actions must not remain under inspection');
  if(name)await shot(name);await click('inspection-back');
  assert(JSON.stringify(state)===before&&revision===beforeRevision,id+' changed campaign state');
 }
 const firstLoadStarted=Date.now();await load();const firstLoadMilliseconds=Date.now()-firstLoadStarted;
 const expectedVersion=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../Published/build.json'),'utf8')).version;
 const previousVisibleVersion=await page.locator('#channel').innerText();
 if(upgradeIndex<0)assert(previousVisibleVersion.endsWith('UNITY '+expectedVersion),'visible build version differs from package');
 const downloadedResourceBytes=await page.evaluate(()=>performance.getEntriesByType('resource').reduce((sum,item)=>sum+(item.encodedBodySize||0),0));
 const heroOption=process.argv.indexOf('--hero');const hero=heroOption>=0?process.argv[heroOption+1]:'reaver';
 await shot('01-phone-title');await click('new');await shot('02-class-selection');
 if(upgradeIndex<0){
  const seed=controls.Controls.find(control=>control.Id==='seed');assert(seed,'seed input missing');const viewport=page.viewportSize();
  await tap((seed.X+seed.Width*.8)*viewport.width/controls.PanelWidth,(seed.Y+seed.Height/2)*viewport.height/controls.PanelHeight);
  await renderedTouchFrame();await pressKey('3');
  if(touch){await shot('02-seed-entered');await pressKey('Enter');await pressKey('Tab');await shot('02-seed-committed');}
  await renderedTouchFrame();
 }
 await click('hero-'+hero,true);if(upgradeIndex<0)assert(state.Seed===3,'seed entry did not take effect');await shot('03-campaign-map');
 const authoredContent=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../GameContent/Unity/campaign.json'),'utf8'));
 if(upgradeIndex<0)assert(JSON.stringify(state.Deck)===JSON.stringify(authoredContent.Heroes.find(item=>item.Id===hero).Deck),'starter deck differs from class definition');
 await click('enter-0',true);await shot('04-phone-combat');
 const mobileLayout=[];let landscapeInspectionChecks=0;
 if(process.argv.includes('--mobile-layout')){
  const repeatIndex=process.argv.indexOf('--landscape-repeats'),landscapeRepeats=repeatIndex<0?1:Number(process.argv[repeatIndex+1]);
  assert(Number.isInteger(landscapeRepeats)&&landscapeRepeats>=1&&landscapeRepeats<=5,'Use --landscape-repeats between 1 and 5');
  async function checkTargets(name){
   const canvas=await page.locator('#unity-canvas').boundingBox();
   const buffer=await page.locator('#unity-canvas').evaluate(c=>({width:c.width,height:c.height}));
   const renderDensity=buffer.width/canvas.width;
   assert(renderDensity<=1.51&&(deviceScaleFactor===1||renderDensity>1),'render density cap changed');
   const heights=controls.Controls.filter(c=>c.Width>0&&c.Height>0).map(c=>({id:c.Id,height:c.Height*canvas.height/controls.PanelHeight,width:c.Width*canvas.width/controls.PanelWidth}));
   assert(heights.length&&heights.every(c=>Math.round(c.height*100)/100>=44),name+' control below 44 CSS pixels');
   mobileLayout.push({name,canvas,buffer,renderDensity,heights,stateUnchanged:true});await shot(name);
  }
  const before=JSON.stringify(state),beforeRevision=revision;
  for(const [width,height] of [[320,740],[740,320],[844,390],[390,844],[320,740]]){
   await resize(width,height);await checkTargets('32-viewport-'+mobileLayout.length);
   if(touch&&width>height){await inspect('intent-details','will attack');landscapeInspectionChecks++;}
   assert(JSON.stringify(state)===before&&revision===beforeRevision,'rotation changed campaign state');
  }
  for(let repeat=1;touch&&repeat<landscapeRepeats;repeat++){
   for(const [width,height] of [[740,320],[844,390]]){
    await resize(width,height);await checkTargets('32-repeat-'+repeat+'-'+width);
    await inspect('intent-details','will attack');landscapeInspectionChecks++;
    assert(JSON.stringify(state)===before&&revision===beforeRevision,'repeated rotation changed campaign state');
   }
  }
  await resize(390,844,'24px 18px 20px 12px');await checkTargets('33-inset-canvas');
  await inspect('intent-details','will attack','34-inset-inspection');
  assert(JSON.stringify(state)===before&&revision===beforeRevision,'inset inspection changed state');
  await click('menu');await click('settings');await checkTargets('35-dense-settings');
  await click('back');await click('new');await checkTargets('36-dense-seed');
  await click('back');await click('continue',true);assert(JSON.stringify(state)===before,'layout navigation changed save');
  await resize(390,844,'0px');
 }
 if(process.argv.includes('--feedback-only')){
  async function probe(id,name,reduced,fast,muted){
   const before=feedbackEvents.length,sounds=soundEvents.length;captureNextImpact=name;
   await click(id,true);await until(()=>feedbackEvents.slice(before).some(e=>e.Status==='completed'),'feedback completion');await Promise.all(impactCaptures);
   const events=feedbackEvents.slice(before),start=events.find(e=>e.Status==='started'),impact=events.find(e=>e.Status==='impact'),end=events.find(e=>e.Status==='completed');
   assert(start&&impact&&end&&start.Reduced===reduced&&start.Fast===fast,'feedback preferences differ');
   assert(end.PlayerX===0,'feedback did not reset translation');
   assert(reduced?impact.PlayerX===0:Math.abs(impact.PlayerX)>0,'motion mode did not control translation');
   assert(muted?soundEvents.length===sounds:soundEvents.length>sounds,'mute did not control sound dispatch');
   await shot(name+'-settled');return start;
  }
  async function settings(ids){const before=JSON.stringify(state);await click('menu');await click('settings');for(const id of ids)await click(id);await shot('30-feedback-settings');await click('back');await click('continue',true);assert(JSON.stringify(state)===before,'settings changed campaign state');}
  const firstAttack=state.Hand.findIndex(id=>authoredContent.Cards.find(c=>c.Id===id).Tags.includes('attack'));assert(firstAttack>=0,'attack required');await click('card-'+firstAttack);
  const normal=await probe('play','26-normal-impact',false,false,false);
  await settings(['fast-motion']);const fast=await probe('end-turn','27-fast-impact',false,true,false);
  const normalCue=authoredContent.Feedback.Cues.find(c=>c.Id===normal.Cue),fastCue=authoredContent.Feedback.Cues.find(c=>c.Id===fast.Cue);
  assert(Math.abs(normal.Duration-normalCue.Milliseconds/1000)<.001&&Math.abs(fast.Duration-fastCue.Milliseconds/2000)<.001,'fast duration differs from authored half duration');
  await settings(['fast-motion','reduced-motion','mute-sound']);await probe('end-turn','28-reduced-impact',true,false,true);
  const saved=JSON.stringify(state);await load();await click('continue',true);assert(saved===JSON.stringify(state),'settings reload changed save');await probe('end-turn','29-persisted-impact',true,false,true);
  await settings(['reduced-motion','mute-sound']);const before=feedbackEvents.length;
  // This probe must navigate during a 600 ms cue. The general click helper waits
  // for geometry and adds a settling delay, which can consume that whole window.
  // Capture stable bounds first, then react to the actual cue-start event.
  const canvas=await page.locator('#unity-canvas').boundingBox();
  const point=id=>{const c=controls.Controls.find(c=>c.Id===id&&c.Enabled);assert(c,'interruption control missing: '+id);return {x:canvas.x+(c.X+c.Width/2)*canvas.width/controls.PanelWidth,y:canvas.y+(c.Y+c.Height/2)*canvas.height/controls.PanelHeight};};
  const turnPoint=point('end-turn'),menuPoint=point('menu');
  await page.mouse.click(turnPoint.x,turnPoint.y,{delay:120});
  await until(()=>feedbackEvents.slice(before).some(e=>e.Status==='started'),'interruption cue started');
  await page.mouse.click(menuPoint.x,menuPoint.y,{delay:120});
  await until(()=>feedbackEvents.slice(before).some(e=>e.Status==='cancelled'),'navigation cancels timeline');await page.waitForTimeout(900);
  assert(!feedbackEvents.slice(before).some(e=>e.Status==='completed'),'cancelled timeline continued');await shot('31-interrupted-menu');
  const report={url,feedbackEvents,soundEvents,screenshots:shots,errors,normalFastReduced:true,preferencesPersisted:true,navigationCancelled:true,limits:['Sound dispatch and synthesized samples are checked; audible listening and physical mobile playback remain unverified.']};fs.writeFileSync(path.join(output,'playtest.json'),JSON.stringify(report,null,2)+'\n');if(errors.length)throw new Error('Browser errors');console.log(JSON.stringify(report,null,2));return;
 }
 await resize(320,740);
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
 await resize(390,844);
 const beforePlay=JSON.stringify(state);const first=controls.Controls.find(x=>x.Id.startsWith('card-')&&x.Enabled);await click(first.Id);await shot('05-card-selected');if(upgradeIndex<0)captureNextImpact='26-action-impact';await click('play',true);if(upgradeIndex<0){await until(()=>feedbackEvents.some(e=>e.Status==='completed'),'first feedback settled');await Promise.all(impactCaptures);assert(feedbackEvents.some(e=>e.Status==='impact'&&Math.abs(e.PlayerX)>0),'normal feedback did not move');}
 const commandsChangedState=beforePlay!==JSON.stringify(state);await shot('06-card-played');
 await inspect('discard-pile','cards · grouped by name','22-discard-pile');
 await inspect('action-history','energy spent.','23-action-history');
 await click('end-turn',true);await shot('07-next-turn');const saved=JSON.stringify(state);
 if(upgradeIndex>=0)servedDirectory=path.resolve(__dirname,'../Published/Web');
 await load();assert((await page.locator('#channel').innerText()).endsWith('UNITY '+expectedVersion),'resumed player has the wrong visible version');await shot('08-resume-menu');await click('continue',true);const resumeStateMatches=saved===JSON.stringify(state);await shot('09-resumed-combat');
 await inspect('action-history','No actions recorded in this session.','25-resumed-history');
 const content=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../GameContent/Unity/campaign.json'),'utf8'));
 const cards=new Map(content.Cards.map(x=>[x.Id,x]));let completed=false;let bought=false;let unaffordableInspected=false;let affinityRewardTaken=false;let rewardOffersChecked=0;
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
  if(state.Phase===2){
   const affinity=content.Heroes.find(item=>item.Id===hero).RewardTags;
   assert(new Set(state.Rewards).size===3&&state.Rewards.filter(id=>cards.get(id).Tags.some(tag=>affinity.includes(tag))).length===2&&state.Rewards.filter(id=>cards.get(id).Tags.includes(content.CommonRewardTag)).length===1,'reward offer must contain two affinity and one shared card');rewardOffersChecked++;
   assert(controls.Labels.some(text=>text.startsWith('Shared card ·')),'reward labels must identify shared choices');await shot('10-reward-'+state.Encounter);
   if(!affinityRewardTaken){const chosen=state.Rewards.find(id=>cards.get(id).Tags.some(tag=>affinity.includes(tag)));const before=state.Deck.length;await click('reward-'+chosen,true);assert(state.Deck.length===before+1&&state.Deck.includes(chosen),'reward was not added to the deck');affinityRewardTaken=true;}
   else await click('reward-rest',true);
   if(state.Phase===0&&state.Encounter===1)await shot('11-next-map');continue;
  }
  if(process.argv.includes('--defeat')){await click('end-turn',true);continue;}
  const expensive=state.Hand.findIndex(id=>cards.get(id).Cost>state.Energy);
  if(expensive>=0&&!unaffordableInspected){const before=JSON.stringify(state);await click('card-'+expensive,false,false);assert(controls.Labels.some(text=>text.includes('Not enough energy.')),'unaffordable card needs an explanation');await shot('24-unaffordable-card');await click('card-'+expensive,false,false);assert(JSON.stringify(state)===before,'inspection or cancellation changed state');unaffordableInspected=true;}
  if(state.Health<=state.MaxHealth-content.PotionHealing&&state.Potions>0)await click('potion',true);
  let index=state.Hand.findIndex(id=>cards.get(id).Cost<=state.Energy&&cards.get(id).Effects.some(x=>x.Operation==='strength'));
  if(index<0)index=state.Hand.findIndex(id=>cards.get(id).Cost<=state.Energy&&cards.get(id).Tags.includes('attack'));
  if(index<0)index=state.Hand.findIndex(id=>cards.get(id).Cost<=state.Energy);
  if(index>=0){await click('card-'+index);await click('play',true);}else await click('end-turn',true);
 }
 await resize(1280,900);await shot('16-desktop');
 await resize(844,390);await shot('17-landscape');
 const report={url,deviceScaleFactor,input:touch?'emulated touch taps, swipes and padding taps to stop inertia; keyboard seed entry':'mouse',scrollStops,mobileLayout,landscapeInspectionChecks,feedbackEvents,soundEvents,viewportEvidence,firstLoadMilliseconds,downloadedResourceBytes,commandsChangedState,resumeStateMatches,upgradedFrom:upgradeIndex>=0?previousVisibleVersion:null,affinityRewardTaken,rewardOffersChecked,inspectionPreservesState:true,pileGroupingChecked:true,narrowTouchTargetsChecked:true,unaffordableInspected,fullRunRequested:process.argv.includes('--full'),fullRunVictory:completed,equipmentPurchased:bought,finalState:state,screenshots:shots,errors,limits:['Desktop browser automation; physical phones and native player interaction are not covered.','Insets are synthetic CSS padding, not a physical-notch test.','Load timing is from this desktop test environment and is not a mobile performance budget.']};fs.writeFileSync(path.join(output,'playtest.json'),JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report,null,2));
 if(!commandsChangedState||!resumeStateMatches||errors.length||(!process.argv.includes('--defeat')&&!affinityRewardTaken)||(process.argv.includes('--full')&&!completed)||(process.argv.includes('--defeat')&&state.Phase!==4))process.exitCode=1;
})().catch(async error=>{console.error(error);if(activePage&&evidenceDirectory){await activePage.screenshot({path:path.join(evidenceDirectory,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(evidenceDirectory,'failure.json'),JSON.stringify({error:error.message,...lastEvidence},null,2));}process.exitCode=1;}).finally(async()=>{if(browser)await browser.close();if(server)server.close();});
