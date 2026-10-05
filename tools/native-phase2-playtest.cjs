// Compiled-player UI checks. The virtual Gamepad enters through the browser API
// that Unity polls, never through Unity SendMessage or game-state injection.
const fs = require('node:fs'), path = require('node:path');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const {NativeUiDriver} = require('./native-ui-driver.cjs');
let browser, ui;
(async () => {
  const url = process.argv[2], output = path.resolve(process.argv[3] || 'TestResults/Phase2/Browser');
  if (!url) throw Error('Pass the compiled Web URL.');
  browser = await chromium.launch({headless:true,channel:'msedge',args:process.env.AS_BROWSER_GPU==='1'?[]:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
  for (const viewport of [{width:390,height:844},{width:1440,height:900}]) {
    const page = await browser.newPage({viewport});
    await page.addInitScript(() => {
      window.testPad = {id:'Phase2 standard controller',index:0,connected:true,mapping:'standard',axes:[0,0,0,0],buttons:Array.from({length:17},()=>({pressed:false,touched:false,value:0}))};
      Object.defineProperty(navigator,'getGamepads',{value:()=>[{...window.testPad,timestamp:performance.now()},null,null,null]});
    });
    ui = new NativeUiDriver(page,path.join(output,String(viewport.width)));
    const pad = async (button,hold=180) => {
      await page.evaluate(i=>{window.testPad.buttons[i]={pressed:true,touched:true,value:1};},button);
      await page.waitForTimeout(hold);
      await page.evaluate(i=>{window.testPad.buttons[i]={pressed:false,touched:false,value:0};},button);
      await page.waitForTimeout(400);
    };
    const feedback=[];
    page.on('console',m=>{const t=m.text(),i=t.indexOf('ASHENSPIRE_FEEDBACK ');if(i>=0)try{feedback.push(JSON.parse(t.slice(i+20)));}catch{}});
    console.log('Opening player at '+viewport.width);
    await ui.open(url); console.log('Player open'); await ui.click('settings',false); await ui.until(()=>ui.has('controller-submit'),'controller settings');
    for (const id of ['high-contrast','reduce-flashes','hold-confirm','auto-rewards','map-header-seed','map-header-relics','compact-map-header']) await ui.click(id,false);
    await ui.choose('minimum-tap',2); await ui.choose('accent',2); await ui.choose('card-motif',2);
    const canvas=await page.locator('#unity-canvas').boundingBox();
    ui.check(ui.controls.Controls.filter(c=>/^controller-(submit|cancel|deck)$/.test(c.Id)).every(c=>c.Height*canvas.height/ui.controls.PanelHeight>=63.5),'64-pixel control preference produces physical-size tap targets');
    await ui.shot('01-accessibility');
    await ui.click('controller-submit',false); await pad(9);
    // Unity's legacy WebGL mapping exposes standard Start (browser 9) as button 7.
    ui.check(ui.controls.Controls.some(t=>t.Id==='controller-submit'&&t.Text==='submit · button 7'),'controller rebinding via real Unity gamepad input');
    await ui.click('controller-cancel',false); await pad(9);
    ui.check(ui.controls.Labels.some(t=>t.includes('is used by submit')),'controller binding conflict refuses collision');
    await ui.click('back',false); await page.reload();
    await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000}); await ui.until(()=>ui.has('settings'),'title reloaded');
    await ui.click('settings',false); await ui.until(()=>ui.has('controller-submit'),'settings reloaded');
    ui.check(ui.controls.Controls.some(t=>t.Id==='controller-submit'&&t.Text==='submit · button 7'),'controller binding survives player reload');
    await ui.shot('02-persisted-settings'); await ui.click('settings-section-6',false); await ui.click('back',false);
    await ui.until(()=>ui.has('native-new'),'title for controller navigation');
    for(let n=0;n<24&&!ui.controls.Controls.some(t=>t.Id==='settings'&&t.Focused);n++) await pad(5);
    ui.check(ui.controls.Controls.some(t=>t.Id==='settings'&&t.Focused),'controller bumpers reach title Settings');
    await pad(0);
    ui.check(ui.has('native-new'),'old submit binding no longer activates a control');
    await pad(9); await ui.until(()=>ui.has('controller-submit'),'remapped controller opens Settings');
    await pad(1); await ui.until(()=>ui.has('native-new'),'controller cancel returns to title');
    ui.check(true,'remapped submit and cancel navigate the compiled player');
    await ui.click('native-new'); await ui.useStandard(); await ui.fill('native-seed','1'); await ui.command('native-begin');
    const runId=ui.state.run.runId;
    ui.check(ui.controls.Labels.includes('Seed 1'),'map seed display preference shows the actual run seed');
    await ui.click('native-menu');
    await ui.click('native-slots',false); await ui.until(()=>ui.has('native-slot-0-delete'),'saved slot');
    await ui.click('native-slot-0-delete',false); await ui.click('native-slot-confirm',false);
    ui.check(ui.has('native-slot-confirm'),'short confirmation press does not delete');
    const at=await ui.stablePoint('native-slot-confirm',.5);
    await page.mouse.move(at.x,at.y); await page.mouse.down(); await page.waitForTimeout(900); await page.mouse.up();
    await ui.until(()=>ui.has('native-slot-0-new'),'slot after hold');
    ui.check(!ui.has('native-slot-0-continue'),'completed hold deletes only the test slot');
    await ui.shot('03-held-confirmation');
    await ui.click('native-slots-back'); await ui.click('settings',false);
    await ui.click('settings-section-5',false);
    await ui.click('load-content-mods',false);
    await ui.until(()=>ui.controls.Labels.some(t=>t.startsWith('Loaded: Sample Ember Pack')),'bundled content pack loaded');
    ui.check(ui.controls.Labels.some(t=>t.includes('Mods are disabled in co-op')),'content packs retain shared-game isolation');
    await ui.shot('04-bundled-content-pack');
    ui.check(ui.errors.length===0,'no browser or Unity errors'); ui.save(true);
    fs.writeFileSync(path.join(ui.output,'scope.json'),JSON.stringify({virtualController:true,physicalController:false,physicalDevice:false,testRunId:runId},null,2));
    await page.close();
  }
  await browser.close(); console.log('Phase 2 compiled UI checks passed.');
})().catch(async e=>{console.error(e);if(ui){ui.errors.push(e.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
