// Normal-input smoke coverage for the compiled profile/save integration. The
// separate Unity controller fixtures exercise damaged records and I/O failure.
// This browser suite uses a fresh isolated context, never a player's saved games.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2];if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:1});
 ui=new NativeUiDriver(page,path.resolve(process.argv[3]||'TestResults/NativeSaveRecovery'));
 const labels=()=>ui.controls.Labels||[],state=()=>JSON.stringify(ui.state);
 const healthy=()=>!labels().some(text=>text.includes('could not be saved')||text.includes('could not be opened'));
 await ui.open(url);const source=fs.readFileSync(path.join(ui.output,'build-source.json'));
 await ui.click('native-profile');ui.check(ui.has('native-profile-back')&&healthy(),'fresh profile opens without a recovery or storage error');
 await ui.shot('01-fresh-profile');await ui.click('native-profile-back');
 await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');
 ui.check(ui.state.phase==='Map'&&healthy(),'creation starts and saves a normal climb');
 const original=state();await ui.click('native-menu');
 await ui.click('native-slots');ui.check(ui.has('native-slot-0-continue')&&!ui.has('native-slot-1-continue'),'first new climb occupies only slot one');
 await ui.click('native-slot-0-copy');ui.check(ui.has('native-slot-1-continue'),'copy creates a second saved slot');
 await ui.click('native-slot-1-delete');ui.check(ui.has('native-slot-confirm'),'delete requires the existing confirmation');
 await ui.click('native-slot-cancel');ui.check(ui.has('native-slot-1-continue'),'cancelling delete preserves the copied climb');
 await ui.shot('02-saved-slots');await ui.click('native-slots-back');
 console.log('Save recovery: profile, creation and slot copy passed; checking reload.');
 ui.controls=null;ui.state=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-slots'),'reloaded title');
 await ui.click('native-profile');ui.check(ui.has('native-profile-back')&&healthy(),'saved profile loads after browser reload');
 await ui.shot('03-restored-profile');await ui.click('native-profile-back');await ui.click('native-slots');
 ui.check(ui.has('native-slot-0-continue')&&ui.has('native-slot-1-continue'),'both slots survive reload');
 await ui.command('native-slot-0-continue');ui.check(state()===original,'the original saved climb restores exactly');
 await ui.command('native-route-'+ui.state.legalNodes[0]);ui.check(ui.state.phase==='Combat'&&healthy(),'resumed climb can enter combat and save again');
 const combat=state();await ui.click('native-menu');await ui.click('native-profile');await ui.click('native-profile-back');await ui.click('native-slots');
 await ui.command('native-slot-0-continue');ui.check(state()===combat,'profile navigation does not change the current combat checkpoint');
 const latest=await page.request.get(new URL('build-source.json',url).href);
 ui.check(latest.ok()&&(await latest.body()).equals(source),'served player stayed source matched');
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 console.log('Save recovery browser checks passed: '+ui.checks.length);await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
