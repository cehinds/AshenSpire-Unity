// Exercise every public Ascension and modifier through normal creation controls.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]);
 const groups=[[],['toughElites','lessHealing'],['deadlyEnemies','cursedStart'],['expensiveShops','bigBosses'],['allElite','hoarder'],['chaosRewards','glassCannon'],['endless']];
 browser=await chromium.launch({channel:'msedge',headless:true,args:process.env.AS_BROWSER_GPU==='1'?[]:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const results=[];
 for(let ascension=0;ascension<=6;ascension++){
  const context=await browser.newContext({viewport:{width:ascension%2?768:320,height:ascension%2?1024:640}});
  const page=await context.newPage();ui=new NativeUiDriver(page,path.join(output,String(ascension)));
  await ui.open(url);const source=fs.readFileSync(path.join(ui.output,'build-source.json'));
  await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.click('native-custom-toggle');
  await ui.choose('native-ascension',ascension);
  for(const mod of groups[ascension])await ui.click('native-mod-'+mod);
  await ui.shot('01-setup');await ui.command('native-begin');
  ui.check(ui.state.phase==='Map','configured climb enters its map');
  ui.check(ui.state.run.custom.ascension===ascension,'actual run retains Ascension '+ascension);
  ui.check(groups[ascension].every(id=>ui.state.run.custom.mods[id]===true),'actual run retains every selected modifier');
  if(groups[ascension].includes('hoarder'))ui.check(ui.state.run.cinders===250,'Hoarder grants actual starting cinders');
  if(groups[ascension].includes('cursedStart'))ui.check(ui.state.run.deck.some(row=>row.cardId==='guilt'),'Cursed Start adds the actual authored curse');
  const resume=async()=>{const saved=JSON.stringify(ui.state);await ui.click('native-menu');ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'saved title');await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===saved,'exact configured '+ui.state.phase+' reload');};
  await resume();await ui.command('native-route-'+ui.state.legalNodes[0]);
  ui.check(ui.state.phase==='Combat','configured run enters its opening encounter');
  await ui.command('native-end-turn');ui.check(ui.state.phase==='Combat'&&ui.state.turn>1,'configured enemies resolve an actual turn');
  await ui.shot('02-combat');await resume();
  ui.check((await (await page.request.get(new URL('build-source.json',url).href)).body()).equals(source),'exported payload unchanged');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  results.push({ascension,mods:groups[ascension],checks:ui.checks.length});console.log('Ascension '+ascension+': '+ui.checks.length+' checks');await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({success:true,results,ascensions:[0,1,2,3,4,5,6],modifiers:groups.flat(),fullCampaign:false,physicalDevice:false},null,2)+'\n');await browser.close();
})().catch(async e=>{console.error(e);if(ui){ui.errors.push(e.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
