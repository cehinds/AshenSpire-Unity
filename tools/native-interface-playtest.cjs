// Compiled menu navigation using normal input. Screenshots require visual review.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/NativeInterface');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const summary=[];
 for(const viewport of [{width:390,height:844},{width:1440,height:900}]){
  const context=await browser.newContext({viewport,deviceScaleFactor:1}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,String(viewport.width)));
  await ui.open(url);await ui.shot('01-title');
  await ui.click('settings');
  for(let section=0;section<6;section++)ui.check(ui.has('settings-section-'+section),'settings section is reachable: '+section);
  await ui.shot('02-settings');
  await ui.click('settings-section-1',false);await page.waitForTimeout(400);
  const volume=await ui.stablePoint('volume-master',.5);
  ui.check(volume.y>volume.canvas.y&&volume.y<volume.canvas.y+volume.canvas.height*.45,'Audio navigation puts volume controls near the top');
  await ui.shot('03-audio');
  await ui.click('settings-section-2',false);await page.waitForTimeout(400);
  const scale=await ui.stablePoint('text-scale',.5);
  ui.check(scale.y>scale.canvas.y&&scale.y<scale.canvas.y+scale.canvas.height*.45,'Accessibility navigation puts text size near the top');
  await ui.shot('04-accessibility');
  await ui.click('back');await ui.click('native-profile');
  ui.check(ui.has('native-profile-back'),'collection has a working return control');await ui.shot('05-collection');
  await ui.click('native-profile-back');await ui.click('native-slots');
  ui.check(ui.has('native-slot-0-new')&&ui.has('native-slot-1-new')&&ui.has('native-slot-2-new'),'all empty save slots remain available');
  await ui.shot('06-slots');await ui.click('native-slots-back');
  ui.check(ui.has('native-new'),'menu navigation returns to the new climb action');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  summary.push({viewport,checks:ui.checks.length});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
 console.log(JSON.stringify(summary));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
