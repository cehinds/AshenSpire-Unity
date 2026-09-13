// Native visual-reference acceptance: real pointer/keyboard/wheel inputs only.
// Run: node <this-script> <compiled-Web-URL> <output-directory> <repo-root>
// The draft driver reads the repo's existing read-only console observers.
// Pixel/font/art claims require inspection of the captured PNGs, not labels alone.
const fs=require('node:fs'),path=require('node:path');
const repo=path.resolve(process.argv[4]||process.env.ASHENSPIRE_REPO_ROOT||path.join(__dirname,'..'));
process.env.ASHENSPIRE_REPO_ROOT=repo;
const {NativeUiDriver}=require('./native-ui-driver.cjs');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
let browser,ui,records=[];
(async()=>{
 const url=process.argv[2];if(!url)throw Error('Provide the freshly compiled Web URL');
 const output=path.resolve(process.argv[3]||'TestResults/NativeVisualParity');fs.mkdirSync(output,{recursive:true});
 const oracle=JSON.parse(fs.readFileSync(path.join(repo,'UnityTests/Parity/native-browser-replay.json'))).runs.find(run=>run.classId==='reaver'&&run.seed===1).trace.slice(0,5);
 const summary=[];let source;
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const viewportFlag=process.argv.find(value=>value.startsWith('--viewport='));
 const requestedWidth=viewportFlag?Number(viewportFlag.slice('--viewport='.length)):null;
 if(requestedWidth!==null&&![390,1440,320].includes(requestedWidth))throw Error('Viewport must be390,1440 or320');
 for(const viewport of[{width:390,height:844},{width:1440,height:900},{width:320,height:640}].filter(value=>requestedWidth===null||value.width===requestedWidth)){
  const context=await browser.newContext({viewport,deviceScaleFactor:1});const page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,viewport.width+'x'+viewport.height));records=[];
  // Preserve the unparsed message so malformed chunk receipts can be diagnosed
  // without weakening the driver's error gate or losing the offending bytes.
  const consolePath=path.join(ui.output,'console.ndjson');
  page.on('console',message=>fs.appendFileSync(consolePath,JSON.stringify({type:message.type(),text:message.text()})+'\n'));
  const controls=()=>ui.controls.Controls,labels=()=>ui.controls.Labels||[],state=()=>JSON.stringify(ui.state);
  const record=label=>{records.push({label,state:ui.state,controls:ui.controls});fs.writeFileSync(path.join(ui.output,'observations.json'),JSON.stringify(records,null,2));};
  const geometry=async control=>{
   const canvas=await page.locator('#unity-canvas').boundingBox(),sx=canvas.width/ui.controls.PanelWidth,sy=canvas.height/ui.controls.PanelHeight;
   return{x:canvas.x+control.X*sx,y:canvas.y+control.Y*sy,width:control.Width*sx,height:control.Height*sy,canvas};
  };
  const checkArena=async()=>{
   const enemies=ui.state.enemies.filter(enemy=>enemy.hp>0);ui.check(enemies.length===3,'opening arena has all three authoritative enemies');
   const boxes=[];
   for(const enemy of enemies){
    const control=controls().find(c=>c.Id==='native-target-'+enemy.id&&c.Enabled);ui.check(!!control,'enemy has a real selectable target: '+enemy.id);
    const box=await geometry(control);boxes.push(box);
    ui.check(box.x>=box.canvas.x-0.1&&box.x+box.width<=box.canvas.x+box.canvas.width+0.1&&box.y>=box.canvas.y&&box.y+box.height<=box.canvas.y+box.canvas.height,'enemy target is simultaneously inside viewport: '+enemy.id);
    ui.check(box.width>=43.9&&box.height>=43.9&&box.width<box.canvas.width*.4,'enemy uses a touch-sized arena column, not a full-width plain row: '+enemy.id);
    ui.check(labels().some(label=>label.trim()===enemy.hp+'/'+enemy.maxHp),'enemy health value is rendered: '+enemy.id);
   }
   ui.check(Math.max(...boxes.map(b=>b.y))-Math.min(...boxes.map(b=>b.y))<12,'enemy targets share the same arena row');
   for(let i=0;i<boxes.length;i++)for(let j=i+1;j<boxes.length;j++)ui.check(boxes[i].x+boxes[i].width<=boxes[j].x+0.1||boxes[j].x+boxes[j].width<=boxes[i].x+0.1,'enemy columns do not overlap');
  };
  const select=async id=>{await ui.click('native-card-'+id);ui.check(ui.has('native-play'),'selected affordable card enables Play');};
  const match=step=>ui.state.phase===step.phase&&ui.state.turn===step.turn&&ui.state.player.hp===step.hp&&ui.state.player.mana===step.mana&&ui.state.player.stamina===step.stamina;
  await ui.open(url);const initial=fs.readFileSync(path.join(ui.output,'build-source.json'));const stamp=JSON.parse(initial);
  ui.check(stamp.buildNumber>=14,'compiled source is build14 or newer');if(source)ui.check(source.equals(initial),'viewport uses the same compiled source');else source=initial;
  ui.check(labels().includes('ASHEN SPIRE'),'title wordmark is present');ui.check(labels().includes('A ROGUELIKE DECKBUILDER'),'title subtitle is present');
  const newBox=await geometry(controls().find(c=>c.Id==='native-new'));
  ui.check(Math.abs(newBox.x+newBox.width/2-(newBox.canvas.x+newBox.canvas.width/2))<25,'title menu is centred in the canvas');
  record('title');await ui.shot('01-title');
  await ui.click('native-new');ui.check(labels().includes('Prepare your Forsaken'),'original-style creator heading is present');record('creator');await ui.shot('02-creation');
  await ui.click('foundation-mode-standard');await ui.fill('native-seed','1');await ui.command('native-begin');ui.check(ui.state.phase==='Map','real Standard Reaver creation enters map');record('map');await ui.shot('03-map');
  await ui.command('native-route-'+oracle[0].command.slice('enter:'.length));ui.check(match(oracle[0]),'first encounter matches committed seed1 resources');
  await checkArena();
  ui.check(ui.state.cards.every(row=>controls().some(c=>c.Id==='native-card-'+row.instance.instanceId)),'entire hand has real controls in one horizontal rail');
  ui.check(labels().includes('1'),'card action numeral is rendered');ui.check(labels().some(text=>/attack/i.test(text)),'card type band text is present');
  ui.check(labels().some(text=>text.includes('Blade')),'authored card tag is rendered');ui.check(labels().some(text=>text==='⚔'||text==='✧'||text==='🗡'),'authored card art glyph is rendered');
  ui.check(labels().some(text=>text.startsWith('Deal 14 damage.')),'existing weapon total survives the card-face redesign');
  ui.check(!labels().some(text=>/\b0 MP\b|\b0 stamina\b/.test(text)),'card labels omit zero resource clutter');
  record('combat-opening');await ui.shot('04-combat');
  const last=ui.state.cards.at(-1).instance.instanceId,lastControl=controls().find(c=>c.Id==='native-card-'+last),beforeLast=await geometry(lastControl),beforeInspect=state();
  await select(last);ui.check(state()===beforeInspect,'scrolling/selecting final card does not mutate gameplay');
  const afterLast=await geometry(controls().find(c=>c.Id==='native-card-'+last));
  if(beforeLast.x+beforeLast.width>beforeLast.canvas.x+beforeLast.canvas.width)ui.check(afterLast.x<beforeLast.x,'off-screen final card was reached by horizontal input');
  record('last-card-selected');await ui.shot('05-last-card');
  // This is the exact committed three-card opening, not a synthetic game state.
  for(const step of oracle.slice(1,4)){
   await ui.click('native-target-'+step.targetId);
   // The last selected card can be this next card. Target selection preserves
   // card selection, so toggle it off first only when this script selected it.
   if(step===oracle[1]&&step.instanceId===last)await ui.click('native-card-'+last);
   const card=ui.state.cards.find(row=>row.instance.instanceId===step.instanceId),energy=ui.state.player.energy;
   await select(step.instanceId);await ui.command('native-play');ui.check(match(step),'real payment matches native oracle: '+step.instanceId);ui.check(ui.state.player.energy===energy-card.cost.action,'observed card action cost was paid');
  }
  ui.check(ui.state.phase==='Combat'&&ui.state.player.energy===0,'opening payments exhaust actions and leave a living enemy');record('paid-cards');await ui.shot('06-paid-cards');
  await ui.command('native-end-turn');ui.check(match(oracle[4]),'actual enemy turn matches committed resources and HP');ui.check(ui.state.player.energy>0,'next turn recovers actions');
  const saved=state();await ui.click('native-menu');await ui.until(()=>ui.has('native-continue'),'menu returns to title');
  ui.controls=null;ui.state=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'Continue after reload');await ui.command('native-continue');
  ui.check(state()===saved,'native reload restores exact combat state');record('reloaded');await ui.shot('07-reloaded');
  const latest=await page.request.get(new URL('build-source.json',url).href);ui.check(latest.ok()&&(await latest.body()).equals(initial),'served source stayed unchanged');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);summary.push({viewport,checks:ui.checks.length,screenshots:7});
  console.log('Native visual parity flow passed: '+viewport.width+'x'+viewport.height+' ('+ui.checks.length+')');await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,viewports:summary,checks:summary.reduce((sum,row)=>sum+row.checks,0),physicalDevice:false,visualInspectionRequired:['serif title font and settled wordmark','warm palette and brass panel framing','actual painted enemies, individual health bars and intent markers','card medallion, glyph, type strip, tags and readable text','phone horizontal hand and utility accessibility'],scope:'Solo presentation and bounded actual first combat; not full visual parity or full campaign acceptance'},null,2));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);fs.writeFileSync(path.join(ui.output,'observations.json'),JSON.stringify(records,null,2));}if(browser)await browser.close();process.exitCode=1;});
