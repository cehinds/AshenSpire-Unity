// Native Custom Climb map shape acceptance through real pointer/keyboard input.
// Copy beside native-ui-driver.cjs. Never inject gameplay, saves, or Unity commands.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const output=path.resolve(process.argv[3]||'TestResults/NativeMapShape');fs.mkdirSync(output,{recursive:true});
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const summaries=[];
 for(const viewport of[{width:390,height:844},{width:1280,height:800}]){
  const context=await browser.newContext({viewport,deviceScaleFactor:2});const page=await context.newPage();ui=new NativeUiDriver(page,path.join(output,viewport.width===390?'phone':'desktop'));
  await ui.open(process.argv[2]);const initialBuild=fs.readFileSync(path.join(ui.output,'build-source.json'));
  await ui.click('native-new');await ui.click('foundation-mode-standard');await ui.fill('native-seed','1');
  // Closed custom shape must not roll 72 probe maps during each creator redraw.
  ui.check(ui.controls.Labels.some(x=>x.includes('Open Run shape to sample map density.')),'closed map section defers sampling');
  await ui.click('native-custom-toggle');await ui.click('native-map-shape-toggle');
  await ui.until(()=>ui.controls.Labels.some(x=>x.includes('Measured across 24 probe seeds')),'measured map readout');
  const weightIds=[...new Set(ui.controls.Controls.filter(c=>/^native-map-weight-[^-]+$/.test(c.Id)).map(c=>c.Id))];ui.check(weightIds.length>0,'weight controls derive from authored map types');
  for(const id of['native-map-floors','native-map-columns',...weightIds]){
   const input=ui.controls.Controls.find(c=>c.Id===id+'-input');ui.check(!!input,'stable numeric input '+id);
   const canvas=await page.locator('#unity-canvas').boundingBox();ui.check(input.Height*canvas.height/ui.controls.PanelHeight>=43.5,'44 CSS-pixel numeric input '+id);
  }
  await ui.fill('native-map-floors-input','7');await ui.fill('native-map-columns-input','2');
  await ui.until(()=>ui.controls.Labels.some(x=>x.includes('7 floors × 2 columns')),'caps reflected in actual resolver');
  // Exhaust all roll weights: the draft must refuse Begin, rather than silently
  // accepting Monster fallback as though the requested distribution were valid.
  for(const id of weightIds)await ui.fill(id+'-input','0');
  await ui.until(()=>!ui.has('native-begin'),'all-zero distribution disables Begin');ui.check(ui.controls.Labels.some(x=>x.includes('At least one node weight must be positive')),'invalid shape explains refusal');await ui.shot('01-invalid-all-zero');
  await ui.click('native-map-shape-reset');await ui.until(()=>ui.has('native-begin'),'reset restores valid Begin');ui.check(!ui.controls.Labels.some(x=>x.includes('At least one node weight must be positive')),'reset clears refusal');
  await ui.fill('native-map-floors-input','7');await ui.fill('native-map-columns-input','2');
  ui.check(weightIds.includes('native-map-weight-monster')&&weightIds.includes('native-map-weight-merchant'),'original Monster and Merchant knobs available');
  await ui.fill('native-map-weight-monster-input','0');await ui.fill('native-map-weight-merchant-input','100');
  ui.check(ui.controls.Labels.some(x=>x.includes('falls back to Monster')),'zero Monster caveat stays visible');await ui.shot('02-shaped-setup');
  await ui.command('native-begin');
  const matches=()=>ui.state?.run?.mapDimensions?.floors===7&&ui.state.run.mapDimensions.columns===2&&ui.state.run.custom.mapShape.floors===7&&ui.state.run.custom.mapShape.columns===2&&ui.state.run.custom.mapShape.typeWeights.monster===0&&ui.state.run.custom.mapShape.typeWeights.merchant===100;
  ui.check(ui.state.phase==='Map'&&matches(),'actual graph and frozen settings use selected caps and weights');ui.check(ui.state.run.mapShapeLimits.minColumns===2&&ui.state.run.mapShapeLimits.maxWeight===100,'authored limits frozen with run');
  ui.check(ui.state.routes.every(n=>n.col>=0&&n.col<2),'actual reachable columns obey cap');await ui.shot('03-generated-map');
  await ui.command('native-route-'+ui.state.routes[0].id);ui.check(ui.state.phase==='Combat','shaped original first floor executes combat');
  const attack=ui.state.cards.find(row=>row.card.type==='attack'&&row.cost.action<=ui.state.player.energy&&row.cost.mana<=ui.state.player.mana&&row.cost.stamina<=ui.state.player.stamina);ui.check(!!attack,'normal starting character has playable attack');
  const enemy=ui.state.enemies.find(e=>e.alive);await ui.click('native-target-'+enemy.id);const cardId='native-card-'+attack.instance.instanceId;
  while(!ui.has(cardId)&&ui.has('native-hand-next'))await ui.click('native-hand-next');await ui.click(cardId);await ui.command('native-play');
  ui.check(matches(),'actual combat command preserves selected map configuration');
  const snapshot=JSON.stringify(ui.state);await ui.click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.command('native-continue');
  ui.check(JSON.stringify(ui.state)===snapshot,'reload restores exact native combat and shaped run');ui.check(matches(),'resumed graph remains shaped');await ui.shot('04-restored-combat');
  const latest=await page.request.get(new URL('build-source.json',process.argv[2]).href);ui.check(Buffer.compare(await latest.body(),initialBuild)===0,'served build remained source-matched throughout test');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);summaries.push({viewport,checks:ui.checks.length});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,viewports:summaries,physicalDevice:false,nextActBrowserProof:false},null,2));console.log('Native map shape checks passed: '+summaries.reduce((n,r)=>n+r.checks,0));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
