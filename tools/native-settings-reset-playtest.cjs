// Settings recovery through real input, with an isolated browser profile per viewport.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/SettingsReset');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const summary=[];
 for(const viewport of [{width:1280,height:720},{width:390,height:844}]){
  const context=await browser.newContext({viewport,deviceScaleFactor:1}),page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,`${viewport.width}x${viewport.height}`));
  const settingsJump=async label=>{
   const id=ui.controls.Controls.find(c=>c.Id.startsWith('settings-section-')&&c.Text===label)?.Id;
   if(!id)throw Error('Missing settings section: '+label);
   // Unity limits each wheel event. Reach the index using bounded repeated
   // gestures; one giant delta still moves only a short distance in the player.
   const canvas=await page.locator('#unity-canvas').boundingBox();
   await page.mouse.move(canvas.x+8,canvas.y+canvas.height/2);
   for(let step=0;step<120;step++){
    const control=ui.controls.Controls.find(c=>c.Id===id);
    if(control&&control.Y>=0&&control.Y+control.Height<ui.controls.PanelHeight)break;
    await page.mouse.wheel(0,-1200);await page.waitForTimeout(120);
   }
   await ui.click(id,false);await page.waitForTimeout(400);
  };
  await ui.open(url);
  await ui.command('native-quick-start');await ui.until(()=>ui.has('native-menu'),'new saved climb');
  const savedRun=JSON.stringify(ui.state);
  await ui.click('native-menu');await ui.until(()=>ui.has('settings'),'title after saving');
  await ui.click('settings');
  ui.check(ui.controls.Controls.filter(control=>control.Id==='fullscreen').length===1,'fullscreen has one consistent settings control');
  await settingsJump('AUDIO');
  await ui.click('volume-master',false,.8);await ui.key('Home');
  await ui.until(()=>ui.controls.Labels.includes('Master volume · 0%'),'changed audio preference');
  await settingsJump('RESTORE DEFAULTS');
  await ui.click('settings-reset');
  ui.check(ui.has('settings-reset-confirm')&&ui.has('back'),'reset requires an explicit confirmation');
  await ui.shot('01-confirmation');
  await ui.key('Escape');await ui.until(()=>ui.has('settings-reset'),'Escape returns to settings');
  ui.check(ui.controls.Labels.includes('Master volume · 0%'),'cancel preserves preference');
  await ui.click('settings-reset');await ui.click('back');
  ui.check(ui.controls.Labels.includes('Master volume · 0%'),'Keep my settings preserves preference');
  await ui.click('settings-reset');await ui.click('settings-reset-confirm');
  await ui.until(()=>ui.has('volume-master'),'settings after confirmed reset');
  ui.check(ui.controls.Labels.includes('Master volume · 100%'),'confirmation restores audio defaults');
  ui.check(ui.controls.Labels.includes('Text size · 100%'),'confirmation restores text default');
  ui.check(!ui.has('screen-shake-intensity'),'confirmation restores disabled screen shake');
  // Return focus is observable through normal keyboard activation of the reset button.
  await ui.key('Enter');await ui.until(()=>ui.has('settings-reset-confirm'),'focus returns to reset control');
  ui.check(ui.has('settings-reset-confirm'),'keyboard focus returns to the reset button');
  await ui.key('Escape');await ui.until(()=>ui.has('settings-reset'),'cancel focused reset');
  await settingsJump('AUDIO');await ui.shot('02-restored-audio');
  await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});
  await ui.until(()=>ui.has('settings'),'title after player reload');
  ui.check(ui.has('native-continue'),'saved climb remains available after reset/reload');
  await ui.click('settings');
  ui.check(ui.controls.Labels.includes('Master volume · 100%'),'default persists through reload');
  await settingsJump('HOW TO PLAY');await ui.click('back');
  await ui.command('native-continue');
  ui.check(JSON.stringify(ui.state)===savedRun,'saved climb is byte-equivalent through settings reset');
  await ui.shot('03-preserved-climb');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
  summary.push({viewport,checks:ui.checks.length,physicalDevice:false});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify(summary,null,2));
 console.log('Settings reset player checks passed: '+JSON.stringify(summary));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
