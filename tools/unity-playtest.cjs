// Play the exported Unity canvas through real pointer input; capture honest evidence.
// Needs Playwright and Edge on Windows (Chromium elsewhere). No game state injection.
// Usage: node tools/unity-playtest.cjs [url] [outputDirectory]
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
let activeBrowser;
(async () => {
    const output = path.resolve(process.argv[3] || 'Published/Screenshots');
    fs.mkdirSync(output, {recursive: true});
    const browser = await chromium.launch({headless: true, ...(process.platform === 'win32' ? {channel: 'msedge'} : {}), args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader']});
    activeBrowser = browser;
    const page = await browser.newPage({viewport: {width: 390, height: 844}, deviceScaleFactor: 1});
    const errors = [];
    let uiReady = false;
    const states = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => {
        const text = message.text();
        if (message.type() === 'error') errors.push(text);
        if (text.includes('ASHENSPIRE_UI_READY')) uiReady = true;
        const marker = text.indexOf('ASHENSPIRE_STATE ');
        if (marker >= 0) states.push(JSON.parse(text.slice(marker + 'ASHENSPIRE_STATE '.length)));
    });
    const url = process.argv[2] || 'http://127.0.0.1:8787';
    async function ready() {
        await page.waitForFunction(() => !!window.unityInstance, null, {timeout: 60000});
        for (let i=0; i<300 && !uiReady; i++) await page.waitForTimeout(100);
        if (!uiReady) throw new Error('Unity did not report an initialized player UI.');
        await page.waitForTimeout(700);
    }
    async function shot(name) { await page.waitForTimeout(200); await page.mouse.move(0, 0); await page.waitForTimeout(350); return page.screenshot({path: path.join(output, name + '.png')}); }
    await page.goto(url); await ready();
    await shot('01-phone-title');
    await page.mouse.click(195, 490, {delay: 100}); await page.waitForTimeout(300);
    await shot('02-phone-map');
    await page.mouse.click(195, 415, {delay: 100}); await page.waitForTimeout(300);
    await shot('03-phone-combat'); const initialCombat = states.at(-1);
    await page.mouse.click(70, 580, {delay: 100}); await shot('04-phone-card-selected');
    await page.mouse.click(95, 784, {delay: 100}); await shot('05-phone-card-played'); const afterPlay = states.at(-1);
    await page.mouse.click(290, 784, {delay: 100}); const before = await shot('06-phone-next-turn'); const savedState = states.at(-1);
    uiReady = false; await page.reload(); await ready(); await shot('07-phone-resume-menu');
    await page.mouse.click(195, 550, {delay: 100}); const after = await shot('08-phone-resumed-combat');
    const resumePixelsMatch = before.equals(after);
    const resumeStateMatches = JSON.stringify(savedState) === JSON.stringify(states.at(-1)) && !!savedState;
    const commandsChangedState = initialCombat?.Phase === 1 && afterPlay?.Energy < initialCombat.Energy && savedState?.Turn === initialCombat.Turn + 1;
    await page.setViewportSize({width: 1280, height: 900}); await page.waitForTimeout(500);
    await shot('09-desktop-combat');
    await page.setViewportSize({width: 844, height: 390}); await page.waitForTimeout(500);
    await shot('10-landscape-combat');
    const report = {url, scenarios: 10, phoneViewport: '390x844', desktopViewport: '1280x900', landscapeViewport: '844x390', commandsChangedState, resumeStateMatches, resumePixelsMatch, savedState, errors,
        limits: ['Desktop browser with phone-sized viewport; no physical phone validation.', 'Pointer input exercised; device touch gestures and native Android/iOS not tested.', 'Reward and full-run victory covered by domain simulation, not this browser pass.']};
    fs.writeFileSync(path.join(output, 'playtest.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2));
    await browser.close();
    if (errors.length || !resumeStateMatches || !commandsChangedState) process.exitCode = 1;
})().catch(async error => { console.error(error); if (activeBrowser) await activeBrowser.close(); process.exitCode = 1; });
