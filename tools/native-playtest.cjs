// Play a full native Unity climb through real pointer and keyboard input.
// Read-only diagnostics assert state; no JavaScript game commands or state writes.
const {controlReportsForPage}=require('./control-report.cjs');
const {hasRecordedClimb}=require('./native-chronicle-check.cjs');
const fs=require('node:fs'), path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const output=path.resolve(process.argv[3]||'TestResults/Native'); fs.mkdirSync(output,{recursive:true});
const recorded=JSON.parse(fs.readFileSync(process.argv[4]||path.join(__dirname,'../UnityTests/Parity/native-browser-replay.json'),'utf8')).runs[0];
const replay=recorded.trace;
if(recorded.classId!=='reaver')throw Error('This compiled replay driver currently supports the Reaver creation route only.');
const deviceScaleFactor=Number(process.env.ASHENSPIRE_PLAYTEST_DPR||2);
if(![1,2,3].includes(deviceScaleFactor))throw Error('ASHENSPIRE_PLAYTEST_DPR must be 1, 2 or 3');
// The replay must end where the domain run ended. Owner decision (2026-09-24): the bot gate records
// wins instead of requiring them, so the recorded run may be a Defeat; balance is tuned separately.
const endlessCheckpoint=recorded.checkpoint==='endless-act4-map'&&recorded.result==='Map'&&recorded.act===4&&recorded.custom?.mods?.endless===true;
if((!['Victory','Defeat'].includes(recorded.result)&&!endlessCheckpoint)||!Number.isInteger(recorded.act))throw Error('Replay fixture must record a terminal result or an explicit Endless Act-4 checkpoint');
if(replay.length===0||replay[replay.length-1].phase!==recorded.result||replay[replay.length-1].act!==recorded.act)throw Error('Replay trace does not end in its declared state');
let browser,page,controls,state,layout=0,revision=0; const chunks=new Map();
const errors=[],screenshots=[],checks=[],commands=[];
function check(value,name){if(!value)throw Error(name);checks.push(name);}
async function until(predicate, name, timeout = 30000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) { if (predicate()) return; await page.waitForTimeout(80); }
  throw Error('Timed out: ' + name);
}
async function frames() { await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))); }
async function key(value) {
  if (value.includes('+')) { const [modifier, character] = value.split('+'); await page.keyboard.down(modifier); try { await key(character); } finally { await page.keyboard.up(modifier); } return; }
  await page.keyboard.down(value); await frames(); await page.waitForTimeout(100); await page.keyboard.up(value); await frames();
}
// Reuse the measured real-input driver so horizontal hands and utility strips
// are tested through their visible controls as well as older scrolling screens.
const {NativeUiDriver}=require('./native-ui-driver.cjs');
const pointerDriver={get page(){return page;},get controls(){return controls;},get state(){return state;},get layout(){return layout;},
 has:id=>controls?.Controls.some(c=>c.Id===id&&c.Enabled),until,frames,key,click:(...args)=>click(...args)};
async function click(id,change=true,fraction=.5){return NativeUiDriver.prototype.click.call(pointerDriver,id,change,fraction);}
async function shot(name) { await page.waitForTimeout(300); await page.screenshot({ path: path.join(output, name + '.png') }); screenshots.push(name); }

async function command(id){const previous=revision;await click(id);await until(()=>revision>previous,'native state '+id);}
const seen=new Set();
async function capture(){const key=state.act+'-'+state.phase;if(!seen.has(key)){seen.add(key);await shot(String(screenshots.length+1).padStart(2,'0')+'-phone-act'+key);}}
function report(success){return {success,scope:endlessCheckpoint?'Endless Act-4 checkpoint, reload and next room':'full climb to recorded terminal result',checks,screenshots,errors,commands,lastState:state,controls,viewport:{width:390,height:844,deviceScaleFactor},physicalDevice:false};}
(async()=>{
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor});
 const normalizeControls=controlReportsForPage(page,e=>errors.push(e));
 page.on('pageerror',e=>errors.push(e.message));
 page.on('console',m=>{const value=normalizeControls(m.text());if(value===null)return;if(m.type()==='error')errors.push(value);for(const [prefix,receive] of [['ASHENSPIRE_CONTROLS ',d=>{controls=d;layout++;}],['ASHENSPIRE_NATIVE_STATE_CHUNK ',d=>{let parts=chunks.get(d.sequence);if(!parts){parts=[];chunks.set(d.sequence,parts);}parts[d.index]=d.text;if(parts.filter(x=>x!==undefined).length===d.count){state=JSON.parse(parts.join(''));revision++;chunks.delete(d.sequence);}}]]){const at=value.indexOf(prefix);if(at>=0)receive(JSON.parse(value.slice(at+prefix.length)));}});
 const playerUrl=process.argv[2];
 const stamp=await page.request.get(new URL('build-source.json',playerUrl).href);
 if(!stamp.ok())throw Error('Missing compiled player source receipt');
 const sourceReceipt=await stamp.body();fs.writeFileSync(path.join(output,'build-source.json'),sourceReceipt);
 await page.goto(playerUrl);await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});
 await until(()=>pointerDriver.has('native-welcome-continue')||pointerDriver.has('native-new'),'welcome or title');
 if(pointerDriver.has('native-welcome-continue'))await click('native-welcome-continue');
 await until(()=>pointerDriver.has('native-new'),'title');await shot('00-phone-title');
 await click('native-new');await shot('01-phone-assign-points');
 check(controls.Labels.some(t=>t.trim()==='Unspent points: 0'),'Unspent points: 0 (Standard preset, the default)');
 await NativeUiDriver.prototype.assignPoints.call(pointerDriver);await click('native-seed',false,.8);await key('Control+a');await key('Backspace');await page.keyboard.type(String(recorded.seed),{delay:80});await key('Tab');
 if(recorded.custom){
  await click('native-custom-toggle');
  await NativeUiDriver.prototype.choose.call(pointerDriver,'native-deck-mode',['standard','sealed','draft'].indexOf(recorded.custom.deckMode));
  if(recorded.custom.ascension)await NativeUiDriver.prototype.choose.call(pointerDriver,'native-ascension',recorded.custom.ascension);
  for(const [mod,enabled] of Object.entries(recorded.custom.mods||{}))if(enabled)await click('native-mod-'+mod);
  if(recorded.custom.mapShape){
   await click('native-map-shape-toggle');
   for(const key of ['floors','columns'])if(recorded.custom.mapShape[key]!==undefined)await NativeUiDriver.prototype.fill.call(pointerDriver,'native-map-'+key+'-input',String(recorded.custom.mapShape[key]));
   for(const [kind,weight] of Object.entries(recorded.custom.mapShape.typeWeights||{}))await NativeUiDriver.prototype.fill.call(pointerDriver,'native-map-weight-'+kind+'-input',String(weight));
  }
 }
 await shot('02-phone-assigned-creation');await command('native-begin');
 check(state.phase===(recorded.custom?.deckMode==='draft'?'Draft':'Map'),'native configured run starts');
 if(recorded.custom)check(state.run.custom.deckMode===recorded.custom.deckMode&&state.run.custom.ascension===recorded.custom.ascension&&Object.entries(recorded.custom.mods).every(([key,value])=>state.run.custom.mods[key]===value),'compiled setup preserves replay mode and modifiers');
 if(recorded.custom?.mapShape){const actual=state.run.custom.mapShape;check(actual&&['floors','columns'].every(key=>recorded.custom.mapShape[key]===undefined||actual[key]===recorded.custom.mapShape[key])&&Object.entries(recorded.custom.mapShape.typeWeights||{}).every(([key,value])=>actual.typeWeights[key]===value),'compiled setup preserves selected custom map controls');}
 for(let index=0;index<replay.length;index++){
  const action=replay[index];await capture();check(state.phase===action.beforePhase&&state.player.hp===action.beforeHp,'before command '+index+' '+action.command);
  for(const reward of action.unavailableRewards||[]){
   const unavailable=()=>controls.Controls.some(c=>c.Id==='native-reward-'+reward.kind&&!c.Enabled)&&controls.Labels.some(label=>label.includes(reward.reason));
   await until(unavailable,'rendered unavailable '+reward.kind+' reward');
   check(unavailable(),'unavailable '+reward.kind+' is disabled with its real refusal reason');
  }
  const [kind,...parts]=action.command.split(':');const value=parts.join(':');
  if(kind==='play'){
   const instance=state.hand.find(c=>c.instanceId===action.instanceId);check(!!instance,'replay instance '+action.instanceId);
   await click('native-target-'+action.targetId);
   for(let page=0;page<8&&!controls.Controls.some(c=>c.Id==='native-card-'+action.instanceId)&&controls.Controls.some(c=>c.Id==='native-hand-next'&&c.Enabled);page++)await click('native-hand-next');
   await click('native-card-'+action.instanceId);await command('native-play');
  }else if(kind==='draft')await command('native-draft-'+value);
  else if(kind==='enter')await command('native-route-'+value);
  else if(kind==='charge')await command(value==='hp'?'native-crimson':'native-azure');
  else if(kind==='flask'){await click('native-target-'+action.targetId);await command('native-flask-0');}
  else if(kind==='reward')await command('native-reward-'+value+(value==='card'?'-'+action.cardId:''));
  else if(kind==='event')await command('native-choice-'+value);
  else if(kind==='buy')await command('native-buy-relics-'+action.index);
  else if(kind==='level'){await click('native-level-up');await click('native-level-up-'+value);await command('native-level-confirm');}
  else if(kind==='service')await command('native-upgrade-'+action.itemRef.replaceAll('/','-'));
  else {const ids={endTurn:'native-end-turn',catchBreath:'native-breath',continueRewards:'native-rewards-continue',rest:'native-rest',leaveShrine:'native-shrine-leave',leaveEvent:'native-event-leave',leaveShop:'native-shop-leave'};if(!ids[kind])throw Error('Unknown command '+kind);await command(ids[kind]);}
  check(state.phase===action.phase&&state.player.hp===action.hp&&state.player.mana===action.mana&&state.player.stamina===action.stamina,'IL2CPP state agrees with domain after '+index+' '+action.command);
  commands.push({index,command:action.command,phase:state.phase,hp:state.player.hp});
  fs.writeFileSync(path.join(output,'progress.json'),JSON.stringify(report(false),null,2));
  if(index===2||index===70){const expected=JSON.stringify(state);await click('native-menu');controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await command('native-continue');check(JSON.stringify(state)===expected,'persistent page reload resumes exact native state at '+index);}
  if(index%20===0)console.log('Native browser: '+index+'/'+replay.length+' '+state.phase+' act'+state.act);
 }
 await capture();check(state.phase===recorded.result&&state.act===recorded.act,'recorded '+(endlessCheckpoint?'Endless checkpoint':'terminal '+recorded.result)+' in act '+recorded.act+' through actual player controls');
 if(endlessCheckpoint){
  const expected=JSON.stringify(state);await click('native-menu');controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await command('native-continue');
  check(JSON.stringify(state)===expected,'Endless Act-4 map and exact state survive browser reload');
  await command('native-route-'+state.legalNodes[0]);check(state.act===4&&state.phase!=='Map','the next Endless cycle accepts a normal route into its first room');await shot('99-endless-next-cycle-room');
 }else{
 await click('native-menu');await click('native-profile');await shot('99-phone-chronicle');
 check(hasRecordedClimb(controls.Labels,recorded),'completed climb recorded in chronicle as '+recorded.result);
 check(controls.Labels.some(t=>t.trim().startsWith('1 climbs · '+(recorded.result==='Victory'?1:0)+' victories')),'chronicle totals count the one climb');
 await page.setViewportSize({width:1280,height:900});await page.waitForTimeout(1200);await shot('100-desktop-chronicle');
 }
 const finalReceipt=await page.request.get(new URL('build-source.json',playerUrl).href);
 check(finalReceipt.ok()&&(await finalReceipt.body()).equals(sourceReceipt),'compiled player source stayed unchanged throughout the climb');
 check(errors.length===0,'no browser or Unity error logs');fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(report(true),null,2));console.log((endlessCheckpoint?'Native browser Endless checkpoint':'Native browser full climb')+' passed: '+checks.length+' checks, '+commands.length+' commands.');await browser.close();
})().catch(async e=>{errors.push(e.stack||String(e));if(page)await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(report(false),null,2));if(browser)await browser.close();console.error(e);process.exitCode=1;});
