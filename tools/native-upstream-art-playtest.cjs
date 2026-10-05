// Inspect the three adopted starter paintings through normal character creation.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/UpstreamStarterArt');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const summary=[];
 for(const viewport of [{width:1280,height:720},{width:390,height:844}]){
  const context=await browser.newContext({viewport,deviceScaleFactor:1}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,`${viewport.width}x${viewport.height}`));await ui.open(url);
  for(const [classId,cardId,title] of [['starseer','starstonePebble','Starstone Pebble'],['herald','urgentHeal','Urgent Heal'],['rogue','ambush','Ambush']]){
   await ui.click('native-new');await ui.click('foundation-class-'+classId);
   await ui.click('native-creation-next');await ui.click('native-creation-next');await ui.click('native-preview-cards');
   for(let step=0;step<20&&!ui.controls.Labels.some(label=>label.toLowerCase()===title.toLowerCase());step++){
    if(!ui.has('native-preview-next'))break;await ui.click('native-preview-next');
   }
   ui.check(ui.controls.Labels.some(label=>label.toLowerCase()===title.toLowerCase()),title+' is reachable in the native starting deck');
   await ui.shot(cardId);fs.writeFileSync(path.join(ui.output,cardId+'-controls.json'),JSON.stringify(ui.controls,null,2));
   for(let step=0;step<7&&!ui.has('native-new');step++)await ui.click('native-creation-back');
   ui.check(ui.has('native-new'),'creation returns to title after '+title+' inspection');
  }
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  summary.push({viewport,checks:ui.checks.length,requiresVisualReview:true,physicalDevice:false});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));console.log('Starter art player checks: '+JSON.stringify(summary));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
