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
 const back=async()=>{if(ui.has('settings-section-5'))await ui.click('settings-section-5',false);await ui.click('back',false);};
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
 // US-15.1 display options: each choice must reach the app root as a class (OriginalDisplayOptions.RootClasses)
 // and UI size must change the panel scale (a smaller size leaves a wider logical panel).
 const rootHas=name=>(ui.controls.RootClasses||[]).includes(name);
 ui.check(['ui-size-auto','accent-gold','card-motif-wash','motif-strength-normal','map-header-comfortable'].every(rootHas)&&!rootHas('no-control-hints'),'display defaults reach the root classes');
 const widthBefore=ui.controls.PanelWidth;
 await ui.choose('ui-size',1);await ui.until(()=>rootHas('ui-size-s'),'UI size S class');await page.waitForTimeout(600);
 ui.check(ui.controls.PanelWidth>widthBefore*1.1,'UI size S scales the panel down (logical width '+widthBefore+' → '+ui.controls.PanelWidth+')');
 for(const [id,index,cls] of [['accent-color',2,'accent-frost'],['card-motif',3,'card-motif-band'],['card-motif-strength',2,'motif-strength-strong'],['map-header-density',1,'map-header-compact']]){
  await ui.choose(id,index);await ui.until(()=>rootHas(cls),id+' class');ui.check(rootHas(cls),id+' applies immediately as '+cls);
 }
 for(const [id,cls] of [['map-header-seed','map-header-no-seed'],['control-hints','no-control-hints']]){
  await ui.click(id,false);await ui.until(()=>rootHas(cls),id+' class');ui.check(rootHas(cls),id+' off applies immediately as '+cls);
 }
 const displayClasses=['ui-size-s','accent-frost','card-motif-band','motif-strength-strong','map-header-compact','map-header-no-seed','no-control-hints'];
 await ui.click('reduced-motion',false);await ui.shot('01-accessibility-settings');
 await back();await ui.until(()=>ui.has('settings'),'title');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('settings'),'title after reload');await ui.click('settings',false);await ui.until(()=>ui.has('volume-master'),'reloaded settings');
 for(const label of expected)ui.check(labels().includes(label),'setting persists through player reload: '+label);
 ui.check(ui.has('screen-shake-intensity'),'screen shake on persists through reload');
 for(const cls of displayClasses)ui.check(rootHas(cls),'display option persists through reload: '+cls);
 // Back to Auto so the inventory check below runs at the default size.
 await ui.choose('ui-size',0);await ui.until(()=>rootHas('ui-size-auto'),'UI size Auto class');
 await ui.shot('02-persisted-settings'); // Palette/checkmarks require visual review.
 await back();await ui.until(()=>ui.has('native-new'),'title before creation');await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');
 const before=JSON.stringify(ui.state);await ui.click('native-deck');ui.check(!ui.has('native-map-fit'),'inventory replaces map');await ui.shot('03-inventory');await ui.click('native-deck-back');
 ui.check(JSON.stringify(ui.state)===before,'opening and closing inventory preserves run state');
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 fs.writeFileSync(path.join(output,'scope.json'),JSON.stringify({settingsPersistence:true,displayOptionClasses:true,displayOptionVisuals:false,inventoryNavigation:true,audibleMusicAccepted:false,allInventoryActions:false,physicalDevice:false},null,2));
 console.log('Compiled settings and inventory checks passed: '+ui.checks.length);await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
