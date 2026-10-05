// Real UI focus interruption during destructive confirmation, in isolated saves.
const path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 browser=await chromium.launch({headless:true,channel:'msedge',args:process.env.AS_BROWSER_GPU==='1'?[]:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();
 await page.addInitScript(()=>{
  window.testPad={id:'Hold interruption controller',index:0,connected:true,mapping:'standard',axes:[0,0,0,0],buttons:Array.from({length:17},()=>({pressed:false,touched:false,value:0}))};
  Object.defineProperty(navigator,'getGamepads',{value:()=>[{...window.testPad,timestamp:performance.now()},null,null,null]});
 });
 const next=async()=>{await page.evaluate(()=>{window.testPad.buttons[5]={pressed:true,touched:true,value:1};});await page.waitForTimeout(180);await page.evaluate(()=>{window.testPad.buttons[5]={pressed:false,touched:false,value:0};});await page.waitForTimeout(300);};
 ui=new NativeUiDriver(page,path.resolve(process.argv[3]||'TestResults/Phase2/HoldInterruption'));
 await ui.open(process.argv[2]);await ui.click('settings',false);await ui.click('hold-confirm',false);
 await ui.click('settings-section-6',false);await ui.click('back',false);
 await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');
 await ui.click('native-menu');await ui.click('native-slots');await ui.click('native-slot-0-delete');
 for(let step=0;step<4&&!ui.controls.Controls.some(control=>control.Id==='native-slot-confirm'&&control.Focused);step++)await next();
 ui.check(ui.controls.Controls.some(control=>control.Id==='native-slot-confirm'&&control.Focused),'virtual controller focuses the destructive confirmation');
 const at=await ui.stablePoint('native-slot-confirm',.5);
 await page.mouse.move(at.x,at.y);await page.mouse.down();await page.waitForTimeout(180);
 await next();
 ui.check(ui.controls.Controls.some(control=>control.Id==='native-slot-cancel'&&control.Focused),'controller navigation moves UI focus away during hold');
 await page.waitForTimeout(1000);await page.mouse.up();await page.waitForTimeout(300);
 ui.check(ui.has('native-slot-confirm'),'focus interruption cancels the held deletion');
 await ui.click('native-slot-confirm',false);
 ui.check(ui.has('native-slot-confirm'),'a new short hold cannot inherit elapsed time from interruption');
 await ui.click('native-slot-cancel');ui.check(ui.has('native-slot-0-continue'),'interrupted confirmation preserves the saved climb');
 await ui.shot('preserved-slot');ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 console.log('Hold interruption: '+ui.checks.length+' checks passed.');await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
