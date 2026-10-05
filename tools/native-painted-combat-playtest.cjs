// Real input smoke check for the shared painted face in combat and inspection.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/PaintedCombat');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const summary=[];
 for(const viewport of [{width:1280,height:720},{width:390,height:844}]){
  const context=await browser.newContext({viewport,deviceScaleFactor:1}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,`${viewport.width}x${viewport.height}`));await ui.open(url);
  await ui.command('native-quick-start');
  const route=ui.state.routes.find(row=>['fight','monster'].includes(row.type));
  ui.check(!!route,'quick start offers a reachable opening fight');await ui.command('native-route-'+route.id);
  ui.check(ui.state.phase==='Combat','route enters native combat');await ui.shot('01-opening-hand');
  const legal=ui.state.cards.filter(row=>!row.cost.variable&&row.cost.action<=ui.state.player.energy&&row.cost.mana<=ui.state.player.mana&&row.cost.stamina<=ui.state.player.stamina);
  const row=legal.find(row=>row.instance.cardId==='strike')||legal.find(row=>row.instance.cardId==='defend');
  ui.check(!!row,'opening hand has an affordable basic card');
  const before=JSON.stringify(ui.state),energy=ui.state.player.energy;
  await ui.click('native-card-'+row.instance.instanceId);
  ui.check(JSON.stringify(ui.state)===before,'selecting the painted card does not spend resources');
  // Slow frames can make the pointer press enter the supported hold-to-inspect
  // path directly. Both selection and inspection must leave payment untouched.
  if(!ui.has('native-card-inspection-action')){
   await ui.click('native-combat-menu');await ui.click('native-inspect-card');
  }
  ui.check(ui.has('native-card-inspection-action'),'painted inspector exposes its Play action');
  await ui.shot('02-painted-inspector');await ui.command('native-card-inspection-action');
  ui.check(ui.state.player.energy===energy-row.cost.action,'inspector Play pays one action cost');
  ui.check(!ui.state.hand.some(card=>card.instanceId===row.instance.instanceId),'played basic leaves the hand once');
  const saved=JSON.stringify(ui.state);
  if(ui.has('native-combat-menu'))await ui.click('native-combat-menu');await ui.click('native-menu');
  ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});
  await ui.until(()=>ui.has('native-continue'),'saved fight on title');await ui.command('native-continue');
  ui.check(JSON.stringify(ui.state)===saved,'reload preserves the exact played combat state');
  await ui.shot('03-reloaded-combat');ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  summary.push({viewport,checks:ui.checks.length,physicalDevice:false});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
 console.log('Painted combat player checks passed: '+JSON.stringify(summary));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
