// Render every authored enemy through the compiled compendium using real controls.
// This proves asset loading/registration, not encounter coverage or owner acceptance.
const fs = require('node:fs'), path = require('node:path');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const {NativeUiDriver} = require('./native-ui-driver.cjs');
let browser, ui;
(async () => {
  const url = process.argv[2];
  if (!url) throw Error('Pass the compiled Web URL.');
  const output = path.resolve(process.argv[3] || 'TestResults/EnemyCatalog');
  const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, '../GameContent/Unity/Original/enemy-art.json'), 'utf8')).enemies;
  const observed = [], screenshots = [];
  browser = await chromium.launch({headless: true, ...(process.platform === 'win32' ? {channel: 'msedge'} : {}), args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader']});
  const page = await browser.newPage({viewport: {width: 390, height: 844}, deviceScaleFactor: 1});
  ui = new NativeUiDriver(page, output);
  page.on('console', message => {
    const prefix = 'ASHENSPIRE_ENEMY_ART ', at = message.text().indexOf(prefix);
    if (at >= 0) observed.push(JSON.parse(message.text().slice(at + prefix.length)));
  });
  const started = Date.now();
  await ui.open(url);
  const startupMs = Date.now() - started;
  await ui.click('title-extras');
  await ui.click('foundation');
  await ui.click('foundation-catalog');
  await ui.choose('foundation-table', 5);
  for (const viewport of [{name:'phone', width:390,height:844}, {name:'desktop',width:1440,height:900}]) {
    await page.setViewportSize({width:viewport.width,height:viewport.height});
    for (const [id, expected] of Object.entries(catalog)) {
      const before = observed.length;
      await ui.click('record-' + id + '-');
      await ui.until(() => observed.length > before, 'compiled enemy texture report: ' + id);
      const asset = observed.at(-1);
      ui.check(asset.enemyId === id && asset.resource === expected.resource && asset.width > 0 && asset.height > 0, viewport.name + ': loaded authored painted texture for ' + id);
      const canvas = await page.locator('#unity-canvas').boundingBox();
      // The JSON inspector consumes its own wheel events. Use the outer gutter
      // so every screenshot shows the heading and the complete painted portrait.
      await page.mouse.move(canvas.x + 6, canvas.y + canvas.height * .5);
      for(let step=0;step<20;step++){
        await page.mouse.wheel(0, -1200);
        await page.waitForTimeout(180);
        if(ui.controls.Controls.find(row=>row.Id==='foundation-back')?.Y > 100)break;
      }
      await page.waitForTimeout(400);
      ui.check(ui.controls.Controls.find(row=>row.Id==='foundation-back')?.Y > 0, 'portrait header is on screen: '+id);
      const name = viewport.name + '-' + id;
      await ui.shot(name); screenshots.push({id, viewport:viewport.name, file:name+'.png', asset});
      await ui.click('foundation-record-back');
    }
  }
  ui.check(new Set(observed.map(row=>row.enemyId)).size === Object.keys(catalog).length, 'all authored enemies were rendered by the compiled game');
  ui.check(ui.errors.length === 0, 'no browser or Unity errors');
  const stamp = JSON.parse(fs.readFileSync(path.join(output, 'build-source.json'), 'utf8'));
  fs.writeFileSync(path.join(output,'gallery.json'), JSON.stringify({version:stamp.version,buildNumber:stamp.buildNumber,sourceDigest:stamp.sourceDigest,startupMs,renderer:'Edge software WebGL',screenshots,allCatalogEnemiesRendered:true,allEncountersExercised:false,coopExercised:false,ownerAccepted:false,physicalDevice:false},null,2));
  ui.save(true);
  console.log('Compiled enemy catalog: '+Object.keys(catalog).length+' enemies, '+screenshots.length+' screenshots, '+ui.checks.length+' checks passed.');
  await browser.close();
})().catch(async error => {
  console.error(error);
  if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}
  if(browser)await browser.close();process.exitCode=1;
});
