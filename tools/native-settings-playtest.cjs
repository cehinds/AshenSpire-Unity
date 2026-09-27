// Compiled settings and inventory navigation through normal pointer/keyboard input.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/NativeSettings');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:1});ui=new NativeUiDriver(page,output);
 await ui.open(url);await ui.click('settings',false);await ui.until(()=>ui.has('volume-master'),'settings controls');
 const labels=()=>ui.controls.Labels||[];
 ui.check(!ui.has('screen-shake-intensity'),'screen shake defaults off');
 await ui.click('screen-shake');ui.check(ui.has('screen-shake-intensity'),'enabling shake enables its intensity slider');
 await ui.click('screen-shake');
 ui.check(!ui.has('screen-shake-intensity'),'disabling screen shake disables its intensity slider');
 await ui.click('screen-shake');
 // Toggle checkmarks are not part of the read-only bounds/label report.
 for(const id of ['hit-stop','instant-animations'])await ui.click(id,false);
 const expected=[];
 for(const [id,label] of [['volume-master','Master volume'],['volume-music','Music volume'],['volume-sfx','Sound effects'],['volume-ui','Interface sounds'],['text-scale','Text size']]){
  const before=labels().find(text=>text.startsWith(label+' · '));
  await ui.click(id,false,.8);await ui.key('ArrowLeft');await page.waitForTimeout(200);
  const after=labels().find(text=>text.startsWith(label+' · '));
  ui.check(after&&after!==before,'real slider input changes '+label);expected.push(after);
 }
 await ui.choose('colorblind-palette',2);
 await ui.click('reduced-motion',false);await ui.shot('01-accessibility-settings');
 await ui.click('back',false);await ui.until(()=>ui.has('settings'),'title');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('settings'),'title after reload');await ui.click('settings',false);await ui.until(()=>ui.has('volume-master'),'reloaded settings');
 for(const label of expected)ui.check(labels().includes(label),'setting persists through player reload: '+label);
 ui.check(ui.has('screen-shake-intensity'),'screen shake on persists through reload');
 await ui.shot('02-persisted-settings'); // Palette/checkmarks require visual review.
 await ui.click('back',false);await ui.until(()=>ui.has('native-new'),'title before creation');await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');
 const before=JSON.stringify(ui.state);await ui.click('native-deck');ui.check(!ui.has('native-map-fit'),'inventory replaces map');await ui.shot('03-inventory');await ui.click('native-deck-back');
 ui.check(JSON.stringify(ui.state)===before,'opening and closing inventory preserves run state');
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 fs.writeFileSync(path.join(output,'scope.json'),JSON.stringify({settingsPersistence:true,inventoryNavigation:true,audibleMusicAccepted:false,allInventoryActions:false,physicalDevice:false},null,2));
 console.log('Compiled settings and inventory checks passed: '+ui.checks.length);await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
