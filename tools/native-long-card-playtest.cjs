// Real-input acceptance for reading a long card through its vertical hand rail.
// Run: node tools/native-long-card-playtest.cjs <Web-URL-with-build-digest> [output] [expected-digest]
// No game state is injected. Inspect the final PNG as well as bounds/labels.
const fs=require('node:fs'),path=require('node:path');
const repo=path.resolve(__dirname,'..');
const {NativeUiDriver}=require(path.join(repo,'tools/native-ui-driver.cjs'));
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
let browser,ui;
(async()=>{
 const url=process.argv[2];if(!url)throw Error('Provide compiled Web URL');
 const expectedDigest=process.argv[4]||new URL(url).searchParams.get('build');if(!/^[a-f0-9]{64}$/.test(expectedDigest||''))throw Error('Provide expected source digest through build query or fourth argument');
 const output=path.resolve(process.argv[3]||path.join(repo,'TestResults/LongCardReading'));fs.mkdirSync(output,{recursive:true});
 browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});const summaries=[];
 for(const viewport of[{width:320,height:640},{width:1440,height:900}]){
  const ctx=await browser.newContext({viewport,deviceScaleFactor:1}),page=await ctx.newPage();ui=new NativeUiDriver(page,path.join(output,viewport.width+'x'+viewport.height));
  const consolePath=path.join(ui.output,'console.ndjson');page.on('console',m=>fs.appendFileSync(consolePath,JSON.stringify({type:m.type(),text:m.text()})+'\n'));
  await ui.open(url);const stamp=JSON.parse(fs.readFileSync(path.join(ui.output,'build-source.json')));ui.check(stamp.sourceDigest===expectedDigest,'source matches designated compiled build');
  await ui.click('native-new');await ui.click('foundation-mode-standard');await ui.fill('native-seed','1');await ui.command('native-begin');await ui.command('native-route-'+ui.state.legalNodes[0]);
  const card=ui.state.cards.find(row=>row.card.id==='gorefireSlash'||row.card.name==='Gorefire Slash');ui.check(!!card,'authored Gorefire Slash is in starting hand');
  const id='native-card-'+card.instance.instanceId;await ui.click(id);ui.check(ui.has('native-play'),'selected Gorefire Slash is affordable');
  const initialState=JSON.stringify(ui.state),snapshots=[];
  const measure=async label=>{
   const canvas=await page.locator('#unity-canvas').boundingBox(),sx=canvas.width/ui.controls.PanelWidth,sy=canvas.height/ui.controls.PanelHeight;
   const bounds=control=>({id:control.Id,x:canvas.x+control.X*sx,y:canvas.y+control.Y*sy,width:control.Width*sx,height:control.Height*sy,enabled:control.Enabled});
   const row={label,card:bounds(ui.controls.Controls.find(c=>c.Id===id)),play:bounds(ui.controls.Controls.find(c=>c.Id==='native-play')),end:bounds(ui.controls.Controls.find(c=>c.Id==='native-end-turn')),canvas,labels:ui.controls.Labels};
   snapshots.push(row);fs.writeFileSync(path.join(ui.output,'reading.json'),JSON.stringify({snapshots,state:ui.state},null,2));return row;
  };
  let before=await measure('selected-before-scroll');await ui.shot('01-selected-long-card');let current=before,attempts=0;
  do{
   const visibleBottom=Math.min(current.card.y+current.card.height,current.play.y-6);
   const x=Math.max(current.canvas.x+12,Math.min(current.canvas.x+current.canvas.width-12,current.card.x+current.card.width/2));
   const y=Math.max(current.card.y+12,Math.min(visibleBottom-12,current.card.y+current.card.height*.55));
   const old=ui.layout;await page.mouse.move(x,y);await page.mouse.wheel(0,90);await ui.until(()=>ui.layout>old,'vertical hand scroll report');await page.waitForTimeout(200);current=await measure('vertical-scroll-'+(++attempts));
  }while(current.card.y+current.card.height>current.play.y-2&&attempts<8);
  ui.check(current.card.y<before.card.y-1,'real vertical wheel moves long card upwards');
  ui.check(current.card.y+current.card.height<=current.play.y-1,'entire lower card edge and description end are above Play');
  ui.check(current.card.y+current.card.height>current.canvas.y&&current.card.x+current.card.width>current.canvas.x&&current.card.x<current.canvas.x+current.canvas.width,'description end remains inside the actual canvas');
  ui.check(current.labels.some(label=>label.includes('Deal 13 damage.')&&label.includes('Apply 3 Bleed.')&&label.includes('Includes +8 total damage from Strength.')),'full authored long description remains in read-only labels');
  for(const action of[current.play,current.end]){
   ui.check(action.enabled&&action.x>=current.canvas.x-0.1&&action.x+action.width<=current.canvas.x+current.canvas.width+0.1&&action.y>=current.canvas.y&&action.y+action.height<=current.canvas.y+current.canvas.height+0.1,'action remains enabled and fully on canvas: '+action.id);
   ui.check(action.width>=43.9&&action.height>=43.9,'action keeps44CSS target: '+action.id);
  }
  ui.check(Math.abs(current.play.y-before.play.y)<0.2&&Math.abs(current.end.y-before.end.y)<0.2,'scroll stays inside hand and leaves action row stationary');
  ui.check(JSON.stringify(ui.state)===initialState,'inspection and wheel do not mutate game state');
  await ui.shot('02-full-description-after-scroll');
  const latest=await page.request.get(new URL('build-source.json',url).href);ui.check(latest.ok()&&JSON.parse(await latest.text()).sourceDigest===stamp.sourceDigest,'served source remains unchanged');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);summaries.push({viewport,checks:ui.checks.length,scrollAttempts:attempts,cardBottom:current.card.y+current.card.height,playTop:current.play.y,screenshots:2});console.log('Long card reading PASS '+viewport.width+' ('+ui.checks.length+')');await ctx.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,sourceDigest:expectedDigest,runCommand:'node tools/native-long-card-playtest.cjs '+JSON.stringify(url)+' '+JSON.stringify(output)+' '+expectedDigest,viewports:summaries,checks:summaries.reduce((s,v)=>s+v.checks,0),physicalDevice:false,visualInspectionRequired:'Inspect02 screenshots for complete final line above Play'},null,2));await browser.close();
})().catch(async e=>{console.error(e);if(ui){ui.errors.push(e.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
