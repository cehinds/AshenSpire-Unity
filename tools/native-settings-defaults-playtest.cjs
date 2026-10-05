// Real-input verification of startup settings choices and their saved acknowledgement.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/SettingsDefaults');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const summary=[];
 for(const viewport of [{width:1280,height:720},{width:390,height:844}])for(const choice of ['keep','use']){
  const context=await browser.newContext({viewport,deviceScaleFactor:1}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,`${viewport.width}x${viewport.height}`,choice));await ui.open(url);
  ui.check(!ui.has('settings-defaults-keep'),'default preferences do not interrupt startup');
  await ui.command('native-quick-start');const saved=JSON.stringify(ui.state);
  await ui.click('native-menu');await ui.click('settings');await ui.click('settings-section-1',false);await page.waitForTimeout(400);
  await ui.click('volume-master',false,.8);await ui.key('Home');
  await ui.until(()=>ui.controls.Labels.includes('Master volume · 0%'),'custom audio value');
  ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});
  await ui.until(()=>ui.has('settings-defaults-keep'),'startup defaults choice');
  ui.check(ui.has('settings-defaults-use'),'choice offers explicit keep and defaults actions');await ui.shot('01-choice');
  if(choice==='keep')await ui.key('Escape');else await ui.click('settings-defaults-use');
  await ui.until(()=>ui.has('settings'),'title after settings decision');
  await ui.click('settings');
  ui.check(ui.controls.Labels.includes(`Master volume · ${choice==='keep'?0:100}%`),'decision applies the selected audio preference');
  await ui.shot('02-saved-preferences');
  ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});
  await ui.until(()=>ui.has('native-continue'),'acknowledged startup returns to title');
  ui.check(!ui.has('settings-defaults-keep'),'acknowledgement survives reload without another prompt');
  await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===saved,'settings choice preserves the exact saved climb');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  summary.push({viewport,choice,checks:ui.checks.length,physicalDevice:false});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
 console.log('Settings defaults checks passed: '+JSON.stringify(summary));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
