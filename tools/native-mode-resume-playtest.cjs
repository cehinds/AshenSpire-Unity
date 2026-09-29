// Current compiled Custom/Sealed/Draft/Endless setup and exact-resume checks.
// Covers opening combat, not a full victory or an Endless Act-4 transition.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]),results=[];
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 for(const [name,deckMode,index] of [['Custom','standard',0],['Sealed','sealed',1],['Draft','draft',2],['Endless','standard',0]]){
  const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();ui=new NativeUiDriver(page,path.join(output,name));
  await ui.open(url);const source=fs.readFileSync(path.join(ui.output,'build-source.json'));
  const resume=async()=>{const saved=JSON.stringify(ui.state);await ui.click('native-menu');ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'saved title');await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===saved,name+' exact reload in '+ui.state.phase);};
  await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.click('native-custom-toggle');
  await ui.choose('native-deck-mode',index);
  if(name==='Custom'){await ui.choose('native-ascension',1);await ui.click('native-mod-hoarder');}
  if(name==='Endless')await ui.click('native-mod-endless');
  await ui.command('native-begin');ui.check(ui.state.run.custom.deckMode===deckMode,name+' retains chosen deck mode');
  if(name==='Draft'){
   ui.check(ui.state.phase==='Draft','Draft opens with actual offers');await resume();
   for(let round=0;ui.state.phase==='Draft'&&round<4;round++){
    const choice=ui.controls.Controls.find(c=>c.Enabled&&c.Id.startsWith('native-draft-'));ui.check(!!choice,'Draft has an available offer');await ui.command(choice.Id);
    if(round===0)await resume();
   }
  }
  ui.check(ui.state.phase==='Map',name+' reaches the map');
  ui.check(ui.state.run.deck.length>0,name+' has a real starting deck');
  if(name==='Custom')ui.check(ui.state.run.custom.ascension===1&&ui.state.run.custom.mods.hoarder===true,'custom ascension and modifier retained');
  if(name==='Endless')ui.check(ui.state.run.custom.mods.endless===true,'Endless rule retained');
  await ui.shot('01-mode-map');await resume();
  await ui.command('native-route-'+ui.state.legalNodes[0]);ui.check(ui.state.phase==='Combat',name+' enters a normal opening encounter');
  await ui.command('native-end-turn');ui.check(ui.state.phase==='Combat'&&ui.state.turn>1,name+' resolves one enemy turn');
  await ui.shot('02-mode-combat');await resume();
  const latest=await page.request.get(new URL('build-source.json',url).href);ui.check((await latest.body()).equals(source),'served source remains unchanged');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);results.push({name,checks:ui.checks.length});console.log(name+' setup/resume passed: '+ui.checks.length);await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,results,checks:results.reduce((n,r)=>n+r.checks,0),fullCampaign:false,endlessAct4:false,physicalDevice:false},null,2));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
