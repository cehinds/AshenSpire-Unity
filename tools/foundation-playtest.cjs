// Real browser input against the compiled Unity foundation preview. Diagnostics are
// read-only; tests never call game commands or replace state through JavaScript.
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const output = path.resolve(process.argv[3] || 'TestResults/Foundation');
fs.mkdirSync(output, { recursive: true });
let browser, page, controls, layout = 0;
const creations = [], maps = [], errors = [], screenshots = [], checks = [];
function check(value, name) { if (!value) throw Error(name); checks.push(name); }
async function until(predicate, name, timeout = 30000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) { if (predicate()) return; await page.waitForTimeout(80); }
  throw Error('Timed out: ' + name);
}
async function frames() { await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))); }
async function key(value) {
  if (value.includes('+')) { const [modifier, character] = value.split('+'); await page.keyboard.down(modifier); try { await key(character); } finally { await page.keyboard.up(modifier); } return; }
  await page.keyboard.down(value); await frames(); await page.waitForTimeout(100); await page.keyboard.up(value); await frames();
}
async function click(id, change = true, fraction = .5) {
  await until(() => controls?.Controls.some(x => x.Id === id && x.Enabled), 'control ' + id);
  for (let step = 0; step < 30; step++) {
    const canvas = await page.locator('#unity-canvas').boundingBox();
    const c = controls.Controls.find(x => x.Id === id);
    const x = canvas.x + (c.X + c.Width * fraction) * canvas.width / controls.PanelWidth;
    const y = canvas.y + (c.Y + c.Height / 2) * canvas.height / controls.PanelHeight;
    if (y < canvas.y + 30 || y > canvas.y + canvas.height - 25) {
      const old = layout; await page.mouse.move(canvas.x + canvas.width * .9, canvas.y + canvas.height * .5);
      await page.mouse.wheel(0, y < canvas.y + 30 ? -320 : 320);
      await until(() => layout > old, 'scroll ' + id); await page.waitForTimeout(300); continue;
    }
    const old = layout; await page.mouse.move(x, y); await frames(); await page.mouse.down(); await page.waitForTimeout(140); await frames(); await page.mouse.up(); await frames();
    if (change) await until(() => layout > old, 'response ' + id);
    await page.waitForTimeout(250); return;
  }
  throw Error('Cannot reach ' + id);
}
async function shot(name) { await page.waitForTimeout(300); await page.screenshot({ path: path.join(output, name + '.png') }); screenshots.push(name); }
function result(success) { return { success, checks, screenshots, errors, creations, maps, controls, browserEmulation: true, physicalDevice: false }; }
(async () => {
  browser = await chromium.launch({ headless: true, ...(process.platform === 'win32' ? { channel: 'msedge' } : {}), args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
  page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 });
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', message => {
    const value = message.text(); if (message.type() === 'error') errors.push(value);
    for (const [prefix, receive] of [
      ['ASHENSPIRE_CONTROLS ', data => { controls = data; layout++; }],
      ['ASHENSPIRE_FOUNDATION_CREATION ', data => creations.push(data)],
      ['ASHENSPIRE_FOUNDATION_MAP ', data => maps.push(data)]]) {
      const index = value.indexOf(prefix); if (index >= 0) receive(JSON.parse(value.slice(index + prefix.length)));
    }
  });
  await page.goto(process.argv[2] || 'http://127.0.0.1:8792/');
  await page.waitForFunction(() => !!window.unityInstance, null, { timeout: 120000 });
  await until(() => controls?.Controls.length, 'Unity controls');
  await shot('01-phone-title'); await click('foundation'); await until(() => creations.length > 0, 'original creation');
  check(creations.at(-1).classId === 'reaver' && creations.at(-1).mode === 'pointbuy', 'Assign points is the default');
  check(Object.values(creations.at(-1).attributes).every(value => value === 5) && creations.at(-1).remaining === 35, 'five minimum attributes leave 35 of 60 points unspent');
  check(!controls.Controls.some(x => x.Id === 'foundation-mode-tuned'), 'Tuned is removed');
  check(creations.at(-1).resources.hp === 40 && creations.at(-1).resources.energy === 2, 'minimum attributes use original resource rules');
  const minimumControls = controls.Controls.filter(x => x.Id.startsWith('attribute-') && x.Id.endsWith('-down'));
  check(minimumControls.length === 5 && minimumControls.every(x => !x.Enabled), 'minimum decrement controls are disabled');
  await shot('02-phone-creation');
  await click('attribute-strength-up'); check(creations.at(-1).remaining === 34 && creations.at(-1).attributes.strength === 6, 'attribute allocation spends exactly one point');
  await click('attribute-strength-down'); check(creations.at(-1).remaining === 35 && creations.at(-1).attributes.strength === 5, 'attribute decrement returns exactly one point');
  await shot('03-phone-attributes');
  await click('foundation-class-starseer');
  await until(() => creations.at(-1).classId === 'starseer', 'Starseer selection');
  check(creations.at(-1).remaining === 35 && Object.values(creations.at(-1).attributes).every(value => value === 5), 'class selection retains minimum allocation defaults');
  await click('foundation-mode-standard');
  check(creations.at(-1).remaining === 0 && creations.at(-1).attributes.intelligence === 14, 'Standard preserves its class preset');
  await click('foundation-mode-pointbuy');
  check(creations.at(-1).remaining === 35 && creations.at(-1).attributes.intelligence === 5, 'Assign points resets to minimum after Standard');
  await shot('04-phone-starseer');
  await click('foundation-map'); await until(() => maps.length >= 3, 'three original act maps');
  await click('foundation-seed', false, .8); await key('Control+a'); await key('Backspace'); await key('1'); await key('Tab');
  await click('foundation-generate'); await until(() => maps.at(-1).seed === 1, 'typed original seed');
  const reference = JSON.parse(fs.readFileSync(path.join(__dirname, '../UnityTests/Parity/reference.json'), 'utf8'));
  const expected = reference.maps.find(x => x.id === '1' && x.seed === 1).map;
  check(JSON.stringify(maps.at(-3).graph) === JSON.stringify(expected), 'built IL2CPP map equals original JavaScript graph');
  await shot('05-phone-routes');
  let visited = 0;
  while (controls.Controls.some(x => x.Id.startsWith('route-1-'))) {
    const id = controls.Controls.find(x => x.Id.startsWith('route-1-')).Id;
    await click(id); if (++visited > 20) throw Error('Route did not terminate');
  }
  check(visited === 13 && controls.Labels.some(x => x.includes('Reached this act')), 'real route controls reach the original boss floor');
  await shot('06-phone-boss-route');
  await click('foundation-catalog'); await click('foundation-search', false, .8);
  for (const letter of 'gorefireSlash') await key(letter);
  await key('Tab'); await click('foundation-find');
  check(controls.Labels.includes('1 matches · 182 records'), 'full imported catalog searches exact original ID');
  await click('record-gorefireSlash-'); check(controls.Controls.some(x => x.Id === 'foundation-record'), 'original nested card record is inspectable');
  await shot('07-phone-card-detail');
  await page.setViewportSize({ width: 844, height: 390 }); await page.waitForTimeout(1500); await shot('08-landscape-card-detail');
  const canvas = await page.locator('#unity-canvas').boundingBox();
  check(controls.Controls.filter(x => x.Width > 0 && x.Height > 0).every(x => x.Height * canvas.height / controls.PanelHeight >= 43.99), 'landscape controls retain 44 CSS-pixel height');
  await page.setViewportSize({ width: 1280, height: 900 }); await page.waitForTimeout(1500);
  await click('foundation-creation'); await shot('09-desktop-creation');
  await click('foundation-back'); check(controls.Controls.some(x => x.Id === 'new'), 'preview returns to preserved playable campaign');
  check(errors.length === 0, 'no browser or Unity error logs');
  fs.writeFileSync(path.join(output, 'checks.json'), JSON.stringify(result(true), null, 2));
  console.log(`Unity foundation browser: ${checks.length} checks passed`);
  await browser.close();
})().catch(async error => {
  errors.push(error.stack || String(error));
  if (page) await page.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
  fs.writeFileSync(path.join(output, 'checks.json'), JSON.stringify(result(false), null, 2));
  if (browser) await browser.close(); console.error(error); process.exitCode = 1;
});
