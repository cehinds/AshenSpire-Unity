// Compiled solo combat controls: real keyboard/pointer input and read-only reports.
// No saved games, gameplay commands, or network intents are injected.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver,selectedCases}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2];if(!url)throw Error('Pass the compiled Web URL.');
 const output=path.resolve(process.argv[3]||'TestResults/NativeCombatTools');fs.mkdirSync(output,{recursive:true});
 const viewports=process.env.AS_LAYOUT_MATRIX==='1'?[{width:320,height:640},{width:390,height:844},{width:768,height:1024},{width:1440,height:900}]:[{width:320,height:640},{width:1440,height:900}],summaries=[];
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:process.env.AS_BROWSER_GPU==='1'?[]:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 for(const index of selectedCases(viewports.length)){
  const viewport=viewports[index],context=await browser.newContext({viewport,deviceScaleFactor:1}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,viewport.width+'x'+viewport.height));
  const state=()=>JSON.stringify(ui.state),labels=()=>ui.controls.Labels||[];
  const settle=()=>page.waitForTimeout(400);
  const keyboard=async(key,predicate,label)=>{await ui.key(key);await ui.until(predicate,label);};
  await ui.open(url);
  const source=fs.readFileSync(path.join(ui.output,'build-source.json'));
  await ui.click('settings',false);await ui.until(()=>ui.has('key-endTurn'),'combat settings');
  await ui.click('key-endTurn');await ui.key('Escape');await ui.until(()=>labels().includes('Unchanged.'),'cancel binding capture');
  await ui.click('key-endTurn');await ui.key('z');await ui.until(()=>labels().some(s=>s.includes('end turn is now Z')),'bind end turn to Z');
  await ui.click('key-combatDeck');await ui.key('z');await ui.until(()=>labels().some(s=>s.includes('already used by Combat: end turn')),'occupied key refused');
  ui.check(labels().some(s=>s.includes('already used by Combat: end turn')),'rebinding refuses a conflicting combat key');
  await ui.click('back',false);await ui.until(()=>ui.has('native-new'),'title');
  ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-new'),'reloaded title');
  await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');
  const map=state();await ui.key('z');await settle();ui.check(state()===map,'combat binding does not act on the map');
  await ui.command('native-route-'+ui.state.legalNodes[0]);ui.check(ui.state.phase==='Combat','normal seeded climb enters combat');
  const start=state();
  await ui.key('f');await ui.key('g');await settle();
  ui.check(state()===start,'flask shortcuts respect disabled full-health and full-mana controls');
  for(const [kind,key] of [['draw','u'],['discard','j'],['exhaust','k']]){
   if(kind==='draw')await ui.click('native-pile-'+kind);else await keyboard(key,()=>ui.has('native-pile-back'),'open '+kind+' by keyboard');
   ui.check(ui.has('native-pile-back'),'pile has an accessible return control: '+kind);
   ui.check(!ui.has('native-end-turn')&&!ui.has('native-play'),'pile replaces combat actions: '+kind);
   if(kind==='draw')ui.check(labels().some(s=>s.includes('Draw order stays hidden')),'draw viewer explains hidden ordering');
   else ui.check(labels().includes('This pile is empty.'),'empty pile is explicitly labelled: '+kind);
   await ui.key('z');await ui.key('Enter');await ui.key('f');await settle();
   ui.check(state()===start&&ui.has('native-pile-back'),'inspection blocks end turn, play and flask shortcuts: '+kind);
   await ui.shot('01-'+kind+'-inspection');
   await keyboard('Escape',()=>ui.has('native-end-turn'),'Escape closes '+kind);
   ui.check(state()===start,'opening and closing pile leaves run unchanged: '+kind);
  }
  await keyboard('d',()=>ui.has('native-deck-back'),'keyboard opens inventory');
  await ui.key('z');await settle();ui.check(state()===start,'inventory blocks end turn shortcut');
  await keyboard('Escape',()=>ui.has('native-end-turn'),'Escape closes inventory');
  await ui.click('native-combat-keys');
  ui.check(labels().includes('Combat: end turn · Z'),'combat help displays the saved binding');
  await ui.key('z');await settle();ui.check(state()===start,'help blocks combat shortcuts');
  await ui.shot('02-combat-controls');await keyboard('Escape',()=>ui.has('native-end-turn'),'Escape closes help');
  await ui.click('native-inspect-enemy');
  ui.check(ui.has('native-inspection-back') && labels().some(text=>text.startsWith('HP ')), 'enemy inspection exposes health and telegraph');
  await ui.key('z'); await settle();
  ui.check(state()===start, 'enemy inspection does not advance combat');
  await ui.shot('02-enemy-inspection'); await keyboard('Escape',()=>ui.has('native-end-turn'),'Escape closes enemy inspection');
  const first=ui.state.cards[0],cost=first.cost;
  ui.check(cost.action<=ui.state.player.energy&&cost.mana<=ui.state.player.mana&&cost.stamina<=ui.state.player.stamina,'first seeded card can be played');
  await keyboard('1',()=>ui.has('native-play'),'positional key selects first card');
  ui.check(state()===start,'card selection never spends resources');
  await keyboard('Escape',()=>!ui.has('native-play'),'Escape clears selected card');
  ui.check(state()===start,'cancelling selection never spends resources');
  await ui.key('Enter');await settle();ui.check(state()===start,'Play key does nothing without a selected playable card');
  await keyboard('1',()=>ui.has('native-play'),'select again');
  const energy=ui.state.player.energy,revision=ui.revision;
  await keyboard('Enter',()=>ui.revision>revision,'Enter plays selected card');
  ui.check(ui.state.player.energy===energy-cost.action,'keyboard play pays exactly one card cost');
  ui.check(!ui.state.hand.some(c=>c.instanceId===first.instance.instanceId),'played card leaves hand once');
  await keyboard('j',()=>ui.has('native-pile-back'),'inspect nonempty discard pile');
  ui.check(labels().includes(first.card.name),'discard inspection shows resolved played card name');
  await ui.shot('02-played-discard');await keyboard('Escape',()=>ui.has('native-end-turn'),'close discard');
  const turn=ui.state.turn,beforeTurn=state();
  await ui.key('e');await settle();ui.check(state()===beforeTurn,'old End Turn key is inactive after persisted rebind');
  await page.keyboard.down('z');await page.keyboard.down('z');await page.waitForTimeout(600);
  ui.check(state()===beforeTurn,'holding or repeating End Turn does not submit before release');
  await page.keyboard.up('z');await ui.until(()=>ui.state.turn>turn,'release submits End Turn');await settle();
  ui.check(ui.state.turn===turn+1,'one key release submits exactly one enemy turn');
  if(ui.state.player.hp<ui.state.player.maxHp&&ui.has('native-crimson')){
   const hp=ui.state.player.hp,charges=ui.state.player.flaskCharges.hpCurrent;
   await keyboard('f',()=>ui.state.player.hp>hp,'Crimson shortcut heals');
   ui.check(ui.state.player.flaskCharges.hpCurrent===charges-1,'flask shortcut consumes exactly one available charge');
  }
  const saved=state();await ui.click('native-menu');await ui.until(()=>ui.has('native-continue'),'saved title');
  ui.controls=null;ui.state=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'resume available');await ui.command('native-continue');
  ui.check(state()===saved,'combat state survives save/reload after keyboard actions');
  await keyboard('u',()=>ui.has('native-pile-back'),'inspection works after reloading combat');await ui.key('z');await settle();
  ui.check(state()===saved,'reloaded inspection still blocks combat actions');
  await ui.shot('03-restored-draw-pile');await keyboard('Escape',()=>ui.has('native-end-turn'),'close restored inspection');
  const latest=await page.request.get(new URL('build-source.json',url).href);
  ui.check(latest.ok()&&(await latest.body()).equals(source),'served build stayed source matched');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  summaries.push({viewport,checks:ui.checks.length,physicalDevice:false,discardChoiceFixture:false});
  console.log('Compiled combat controls passed: '+viewport.width+'x'+viewport.height+' ('+ui.checks.length+')');await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,viewports:summaries,checks:summaries.reduce((sum,v)=>sum+v.checks,0),scope:'Solo pile inspection, keyboard safety/rebinding and save/resume. Shipped hand rules do not prompt for optional discards; chooser requires separate fixture validation.'},null,2));
 await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
