// Exercise assembled channel players through actual input. Keep one browser
// profile across channel visits to verify that sharing runtime assets does not
// merge their saves. Diagnostics and network observations are read-only.
// node tools/unity-channel-playtest.cjs <site-root-url> [output-directory]
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
const output=path.resolve(process.argv[3]||'TestResults/ChannelPlayers');
const root=process.argv[2];if(!root)throw Error('Pass the assembled site root URL');
const expected=new Map(),results=[],successfulUrls=new Set();
let browser,context,active;
(async()=>{
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 context=await browser.newContext({viewport:{width:390,height:844},deviceScaleFactor:2,hasTouch:true});
 for(const visit of [{channel:'dev',seed:1},{channel:'test',seed:2},{channel:'dev',resume:true},{channel:'test',resume:true}]){
  const label=visit.channel+(visit.resume?'-resume':'-new'),page=await context.newPage();
  active=new NativeUiDriver(page,path.join(output,label));const requests=[];
  page.on('response',response=>{if(/\/Web\/Build\/Web\.(loader\.js|framework\.js|data|wasm)(?:\?|$)/.test(response.url()))requests.push({url:response.url(),status:response.status()});});
  const url=new URL(visit.channel+'/Web/',root.endsWith('/')?root:root+'/');
  await active.open(url.href);
  const stamp=JSON.parse(fs.readFileSync(path.join(active.output,'build-source.json'),'utf8'));
  fs.writeFileSync(path.join(active.output,'runtime-requests.json'),JSON.stringify(requests,null,2));
  active.check(new URL(page.url()).pathname===url.pathname,'channel document URL stays unchanged');
  active.check((await page.locator('#channel').textContent()).startsWith(visit.channel.toUpperCase()+' · UNITY '),'visible channel label is correct');
  // A later channel can revalidate the exact same cached data URL with 304.
  // Accept that only after this profile observed a successful 200 for the URL.
  const loaded=requests.every(row=>{
   if(!/\/builds\/build-[a-f0-9]+\/Web\/Build\//.test(new URL(row.url).pathname))return false;
   if(row.status===200){successfulUrls.add(row.url);return true;}
   return row.status===304&&successfulUrls.has(row.url);
  });
  active.check(requests.length>=4&&loaded,'runtime loads successfully from the immutable shared archive or its verified browser cache');
  active.check(new Set(requests.map(row=>new URL(row.url).pathname.replace(/\/Build\/.*/,''))).size===1,'all runtime files belong to one immutable archive');
  active.check(requests.every(row=>new URL(row.url).searchParams.get('build')===stamp.sourceDigest),'runtime URLs retain the packaged source digest');
  if(visit.resume){
   await active.until(()=>active.has('native-continue'),'saved channel run');
   await active.command('native-continue');
   active.check(JSON.stringify(active.state)===expected.get(visit.channel),'channel resumes its own exact saved run');
  }else{
   active.check(!active.has('native-continue'),'new channel does not inherit the other channel save');
   await active.click('native-new');await active.click('foundation-mode-standard');await active.fill('native-seed',String(visit.seed));await active.command('native-begin');
   active.check(active.state.phase==='Map'&&active.state.run.seed===visit.seed,'real input starts the intended channel seed');
   const node=active.state.legalNodes[0];await active.command('native-route-'+node);
   active.check(active.state.phase==='Combat','real route selection enters combat');
   expected.set(visit.channel,JSON.stringify(active.state));
  }
  await active.shot(label);await active.click('native-menu');
  active.check(active.errors.length===0,'no browser or Unity runtime errors');active.save(true);
  fs.writeFileSync(path.join(active.output,'runtime-requests.json'),JSON.stringify(requests,null,2));
  results.push({visit:label,checks:active.checks.length,sourceDigest:stamp.sourceDigest,physicalDevice:false});
  await page.close();active=null;
 }
 fs.mkdirSync(output,{recursive:true});fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({success:true,visits:results},null,2));
 console.log(results.reduce((sum,row)=>sum+row.checks,0)+' channel player checks passed');
})().catch(error=>{active?.save(false);console.error(error);process.exitCode=1;}).finally(async()=>{await context?.close();await browser?.close();});
