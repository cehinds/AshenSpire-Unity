// Verify a real previous-player save in the upgraded player on the same origin.
// Usage: node tools/unity-upgrade-playtest.cjs previousWeb currentWeb evidenceDirectory
// Needs PLAYWRIGHT_MODULE or an installed playwright module. No save/state injection.
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
let browser, server;
(async () => {
    const [previous, current, evidence] = process.argv.slice(2).map(value => path.resolve(value));
    if (!previous || !current || !evidence) throw new Error('Provide previousWeb, currentWeb and evidenceDirectory.');
    fs.mkdirSync(evidence, {recursive:true});
    let directory = previous;
    server = http.createServer((request, response) => {
        const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
        const file = path.resolve(directory, '.' + (pathname === '/' ? '/index.html' : pathname));
        if (!file.startsWith(directory + path.sep)) { response.writeHead(403).end(); return; }
        if (!fs.existsSync(file) || !fs.statSync(file).isFile()) { response.writeHead(404).end(); return; }
        const type = {'.wasm':'application/wasm','.js':'application/javascript','.html':'text/html','.json':'application/json','.png':'image/png'}[path.extname(file)] || 'application/octet-stream';
        response.writeHead(200, {'Content-Type':type, 'Cache-Control':'no-store'});
        fs.createReadStream(file).pipe(response);
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const url = `http://127.0.0.1:${server.address().port}/`;
    browser = await chromium.launch({headless:true, ...(process.platform === 'win32' ? {channel:'msedge'} : {}), args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
    const page = await browser.newPage({viewport:{width:390,height:844}, deviceScaleFactor:1});
    let ready = false;
    const states = [], errors = [], versions = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => {
        const text = message.text();
        if (message.type() === 'error') errors.push(text);
        if (text.includes('ASHENSPIRE_UI_READY')) ready = true;
        if (text.includes('Initialize engine version:')) versions.push(text);
        const marker = text.indexOf('ASHENSPIRE_STATE ');
        if (marker >= 0) states.push(JSON.parse(text.slice(marker + 'ASHENSPIRE_STATE '.length)));
    });
    async function load() {
        ready = false;
        await page.goto(url);
        await page.waitForFunction(() => !!window.unityInstance, null, {timeout:60000});
        for (let i=0; i<300 && !ready; i++) await page.waitForTimeout(100);
        if (!ready) throw new Error('Player UI did not initialize.');
        await page.waitForTimeout(700);
    }
    async function click(x,y) { await page.mouse.click(x,y,{delay:100}); await page.waitForTimeout(600); }
    await load();
    await click(195,490); await click(195,415); // Start and enter encounter in old player.
    await click(70,580); await click(95,784); await click(290,784);
    const saved = states.at(-1);
    if (saved?.Phase !== 1 || saved?.Turn !== 2) throw new Error('Previous player did not create the expected combat save.');
    await page.waitForTimeout(1000);
    await page.mouse.move(0,0);
    await page.screenshot({path:path.join(evidence,'01-previous-player-save.png')});
    directory = current;
    await load();
    await page.screenshot({path:path.join(evidence,'02-upgraded-continue-menu.png')});
    const count = states.length;
    await click(195,550);
    const resumed = states.at(-1);
    const exactSaveMatch = states.length > count && JSON.stringify(saved) === JSON.stringify(resumed);
    await page.mouse.move(0,0);
    await page.screenshot({path:path.join(evidence,'03-upgraded-resumed-combat.png')});
    await click(290,784);
    const canContinuePlaying = states.at(-1)?.Turn === saved.Turn + 1;
    const engineUpgradeObserved = versions.some(value => value.includes('6000.4.10f1')) && versions.some(value => value.includes('6000.6.0f1'));
    const report = {exactSaveMatch,canContinuePlaying,engineUpgradeObserved,versions,saved,resumed,errors,limits:['Desktop Edge using a phone viewport; physical touch and devices are not covered.']};
    fs.writeFileSync(path.join(evidence,'upgrade-playtest.json'),JSON.stringify(report,null,2)+'\n');
    console.log(JSON.stringify(report,null,2));
    if (!exactSaveMatch || !canContinuePlaying || !engineUpgradeObserved || errors.length) process.exitCode = 1;
})().catch(error => {console.error(error); process.exitCode=1;}).finally(async () => {
    if (browser) await browser.close();
    if (server) server.close();
});
