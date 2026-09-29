// Compiled player, real file chooser/clicks; isolated browser context only.
// Original localStorage fixtures simulate the original game's three known keys.
// No Unity save, game state or command is injected through the observer bridge.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const [url,outputArg,fixtures]=process.argv.slice(2),output=path.resolve(outputArg);
 if(!url||!fixtures)throw Error('Pass compiled Web URL, evidence folder, original fixtures folder.');
 const input=fs.readFileSync(path.join(fixtures,'reaver-map.json'),'utf8'),original=JSON.parse(input);
 browser=await chromium.launch({...(process.platform==='win32'?{channel:'msedge'}:{}),headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const results=[];
 for(const viewport of [{width:390,height:844},{width:1440,height:900}]){
  const context=await browser.newContext({viewport}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,viewport.width+'x'+viewport.height));
  const labels=()=>ui.controls.Labels||[];
  const contains=text=>labels().some(label=>label.includes(text));
  await ui.open(url);const sourceReceipt=fs.readFileSync(path.join(ui.output,'build-source.json'));await ui.click('native-slots');await ui.click('native-web-import');
  ui.check(ui.has('native-web-import-file'),'compiled Web exposes file chooser');
  await ui.click('native-web-import-browser-0');
  ui.check(contains('No original save'),'missing original browser slot explains how to proceed');
  await ui.shot('01-import-empty');
  async function file(name,bytes){
   const [chooser]=await Promise.all([page.waitForEvent('filechooser'),ui.click('native-web-import-file',false)]);
   await chooser.setFiles({name,mimeType:'application/json',buffer:Buffer.from(bytes)});
  }
  await file('broken.json','{broken');await ui.until(()=>contains('Import refused'),'invalid file refusal');
  ui.check(!ui.has('native-web-import-confirm'),'malformed file cannot be committed');
  await file('original.json',input);await ui.until(()=>ui.has('native-web-import-confirm'),'original preview');
  ui.check(contains('HP '+original.hp+'/'+original.maxHp)&&contains('Destination: Slot 1'),'preview names real resources and empty destination');
  await ui.shot('02-preview');await ui.click('native-web-import-cancel');
  ui.check(!ui.has('native-slot-0-continue'),'cancelling preview writes no save');
  await page.evaluate(value=>localStorage.setItem('sote_run_v1',value),input);
  await ui.click('native-web-import');await ui.click('native-web-import-browser-0');
  ui.check(ui.has('native-web-import-confirm'),'explicit browser-slot read reaches same preview');
  await ui.click('native-web-import-confirm');
  ui.check(ui.has('native-slot-0-continue')&&!ui.has('native-slot-1-continue'),'import occupies only first empty slot');
  ui.check(contains('Original save imported'),'successful persistence is reported');
  await ui.shot('03-imported-slot');
  await ui.click('native-web-import');await file('same-original.json',input);await ui.until(()=>ui.has('native-web-import-confirm'),'duplicate preview');
  await ui.click('native-web-import-confirm');ui.check(contains('already been imported'),'duplicate checkpoint refused');
  await ui.click('native-web-import-back');
  ui.check(!ui.has('native-slot-1-continue'),'duplicate refusal preserves empty destination');
  await ui.command('native-slot-0-continue');
  ui.check(ui.state.phase==='Map','import resumes on original map');
  ui.check(['hp','maxHp','mana','maxMana','stamina','maxStamina'].every(key=>ui.state.player[key]===original[key]),'compiled imported resources match original checkpoint');
  ui.check(JSON.stringify(ui.state.run.deck.map(card=>card.instanceId))===JSON.stringify(original.deck.map(card=>card.instanceId)),'compiled imported deck retains original card identities');
  const checkpoint=JSON.stringify(ui.state);await ui.shot('04-imported-map');
  await ui.click('native-menu');ui.controls=null;ui.state=null;await page.reload();
  await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-slots'),'title after reload');
  await ui.click('native-slots');await ui.command('native-slot-0-continue');
  ui.check(JSON.stringify(ui.state)===checkpoint,'import survives browser reload with exact observer state');
  await ui.command('native-route-'+ui.state.legalNodes[0]);
  ui.check(ui.state.phase==='Combat','imported run enters actual compiled combat');await ui.shot('05-imported-combat');
  const beforeProfile=JSON.stringify(ui.state);
  const profileText=fs.readFileSync(path.join(fixtures,'profile-export.json'),'utf8');
  const originalProfile=JSON.parse(fs.readFileSync(path.join(fixtures,'profile.json'),'utf8'));
  await ui.click('native-menu');await ui.click('native-slots');await ui.click('native-web-profile-import');
  ui.check(contains('IMPORT YOUR ORIGINAL PROFILE'),'profile import has its own clearly labelled flow');
  await file('profile.json',profileText);await ui.until(()=>ui.has('native-web-import-confirm'),'profile preview');
  ui.check(contains('Combined totals: 23 climbs · 8 victories'),'profile preview uses lifetime totals, not truncated history');
  await ui.shot('06-profile-preview');await ui.click('native-web-import-cancel');
  await ui.click('native-web-profile-import');await file('profile.json',profileText);await ui.until(()=>ui.has('native-web-import-confirm'),'profile preview after cancellation');
  ui.check(contains('Combined totals: 23 climbs · 8 victories'),'cancelling profile preview did not add progress');
  await ui.click('native-web-import-confirm');ui.check(contains('Original profile progress imported'),'profile persistence reports success');
  ui.check(ui.has('native-slot-0-continue')&&!ui.has('native-slot-1-continue'),'profile import preserves run slots');
  await ui.click('native-web-profile-import');await file('again.json',profileText);await ui.until(()=>contains('already been imported'),'duplicate profile refusal');
  ui.check(!ui.has('native-web-import-confirm'),'duplicate profile cannot be committed');
  await ui.click('native-web-import-back');await ui.command('native-slot-0-continue');
  ui.check(JSON.stringify(ui.state)===beforeProfile,'profile import leaves active run state unchanged');
  await ui.click('native-menu');await ui.click('native-profile');
  ui.check(contains('23 climbs · 8 victories'),'Chronicle displays imported lifetime progress');
  ui.check(labels().some(label=>label.includes('Seed PROFILE-22')),'Chronicle displays original run history');
  ui.check(labels().filter(label=>label.trim()==='UNLOCKED').length===originalProfile.unlocked.length,'Chronicle displays original earned unlocks');
  await ui.shot('07-imported-chronicle');
  await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-profile'),'title after profile reload');await ui.click('native-profile');
  ui.check(contains('23 climbs · 8 victories')&&contains('Seed PROFILE-22'),'profile progress and history survive a full browser reload');
  ui.check(fs.readFileSync(path.join(fixtures,'profile-export.json'),'utf8')===profileText,'original profile file is unchanged');
  ui.check(await page.evaluate(()=>localStorage.getItem('sote_run_v1'))===input,'original browser save bytes remain unchanged');
  const finalReceipt=await page.request.get(new URL('build-source.json',url).href);
  ui.check(finalReceipt.ok()&&(await finalReceipt.body()).equals(sourceReceipt),'compiled player source remained unchanged during the import test');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  results.push({viewport,checks:ui.checks.length});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,checks:results.reduce((n,r)=>n+r.checks,0),results,physicalDevice:false},null,2));
 console.log('Compiled original-save import passed: '+results.reduce((n,r)=>n+r.checks,0));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
