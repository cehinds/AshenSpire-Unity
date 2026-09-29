// Same-origin, real-input upgrade from an exported older player to this player.
// The HTTP fixture switches immutable build roots. No game state/storage is injected.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui,server;
(async()=>{
 const oldRoot=path.resolve(process.argv[2]),newRoot=path.resolve(process.argv[3]),output=path.resolve(process.argv[4]);
 const before=JSON.parse(fs.readFileSync(path.join(oldRoot,'build-source.json'))),after=JSON.parse(fs.readFileSync(path.join(newRoot,'build-source.json')));
 if(!Number.isInteger(before.buildNumber)||!Number.isInteger(after.buildNumber)||before.buildNumber>=after.buildNumber)throw Error('Supply two finalized builds in increasing order.');
 const firstBrandedWelcome=before.buildNumber<27&&after.buildNumber>=27;
 let activeRoot=oldRoot;
 server=http.createServer((req,res)=>{
  const relative=decodeURIComponent(new URL(req.url,'http://localhost').pathname).replace(/^\/+/, '')||'index.html';
  const file=path.resolve(activeRoot,relative);
  if(!file.startsWith(activeRoot+path.sep)||!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404);res.end();return;}
  const types={'.html':'text/html','.js':'application/javascript','.wasm':'application/wasm','.json':'application/json','.png':'image/png'};
  res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream','Cache-Control':'no-store'});fs.createReadStream(file).pipe(res);
 });
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(8796,'127.0.0.1',resolve);});
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();
 ui=new NativeUiDriver(page,output);await ui.open('http://127.0.0.1:8796/');
 fs.copyFileSync(path.join(output,'build-source.json'),path.join(output,'previous-build-source.json'));
 await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');await ui.command('native-route-'+ui.state.legalNodes[0]);
 ui.check(ui.state.phase==='Combat','previous build creates an actual saved combat');
 const saved=JSON.stringify(ui.state);await ui.shot('01-build'+before.buildNumber+'-combat');await ui.click('native-menu');
 await ui.until(()=>ui.has('native-continue'),'old title offers resume');await page.waitForTimeout(1000);
 activeRoot=newRoot;ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has(firstBrandedWelcome?'native-welcome-continue':'native-continue'),'upgraded title or first branded welcome');
 ui.check((await page.title()).includes('AshenedSpire'),'upgraded player has the new name');
 if(firstBrandedWelcome)await ui.click('native-welcome-continue');
 ui.check(ui.has('native-continue'),'existing save remains available after the rename');
 await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===saved,'upgrade preserves complete run, combat, hand and RNG state');await ui.shot('02-build'+after.buildNumber+'-restored-combat');
 await ui.click('native-menu');ui.controls=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'subsequent reloaded title');
 ui.check(!ui.has('native-welcome-continue'),'new acknowledgement survives another reload');
 await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===saved,'upgraded save remains exact on the second reload');
 const served=await page.request.get('http://127.0.0.1:8796/build-source.json');
 ui.check(JSON.parse(await served.text()).sourceDigest===after.sourceDigest,'fixture serves the expected final source');
 fs.writeFileSync(path.join(output,'build-source.json'),JSON.stringify(after,null,2));
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 fs.writeFileSync(path.join(output,'scope.json'),JSON.stringify({fromBuild:before.buildNumber,toBuild:after.buildNumber,from:before.sourceDigest,to:after.sourceDigest,sameOrigin:true,realSavedRun:true,physicalDevice:false,quotaExhaustion:false},null,2));
 console.log('Real-input branding upgrade passed: '+ui.checks.length);await browser.close();await new Promise(resolve=>server.close(resolve));
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();if(server)server.close();process.exitCode=1;});
