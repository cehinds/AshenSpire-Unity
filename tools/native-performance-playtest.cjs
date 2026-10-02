// Measure the compiled player using normal controls; software-browser evidence only.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 const url=process.argv[2],output=path.resolve(process.argv[3]||'TestResults/NativePerformance');
 if(!url)throw Error('Pass the compiled Web URL.');
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:1});
 ui=new NativeUiDriver(page,output);const started=Date.now();await ui.open(url);const startupMs=Date.now()-started;
 const scenes=[];
 async function sample(scene){
  const metrics=await page.evaluate(async()=>{
   const deltas=[];let previous;
   await new Promise(resolve=>{function frame(now){if(previous!==undefined)deltas.push(now-previous);previous=now;if(deltas.length>=180)resolve();else requestAnimationFrame(frame);}requestAnimationFrame(frame);});
   deltas.sort((a,b)=>a-b);
   const module=window.unityInstance?.Module;
   return {samples:deltas.length,rafMedianMs:deltas[Math.floor(deltas.length*.5)],rafP95Ms:deltas[Math.floor(deltas.length*.95)],rafWorstMs:deltas.at(-1),jsHeapUsedBytes:performance.memory?.usedJSHeapSize??null,wasmReservedBytes:module?.HEAPU8?.buffer.byteLength??module?.HEAP8?.buffer.byteLength??null};
  });scenes.push({scene,...metrics});await ui.shot(scene);
 }
 await sample('title');await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');await sample('map');
 for(let step=0;step<12&&ui.state.phase!=='Combat';step++){
  if(ui.state.phase==='Map'){const route=ui.state.routes.find(row=>['fight','monster'].includes(row.type))||ui.state.routes[0];await ui.command('native-route-'+route.id);}
  else if(ui.state.phase==='Event'){const choices=ui.controls.Controls.filter(row=>row.Enabled&&row.Id.startsWith('native-choice-'));await ui.command((choices.find(row=>/leave|decline|ignore|walk|refuse/.test(row.Id))||choices.at(-1)).Id);}
  else if(ui.state.phase==='EventResult')await ui.command('native-event-leave');
  else throw Error('Unexpected scene: '+ui.state.phase);
 }
 ui.check(ui.state.phase==='Combat','normal controls reach combat');await sample('combat');
 const resources=await page.evaluate(()=>performance.getEntriesByType('resource').filter(row=>/Web\.(data|wasm|framework\.js|loader\.js)/.test(row.name)).map(row=>({url:row.name,transferBytes:row.transferSize,decodedBytes:row.decodedBodySize,durationMs:row.duration})));
 const receipt=JSON.parse(fs.readFileSync(path.join(output,'build-source.json'),'utf8'));
 fs.writeFileSync(path.join(output,'performance.json'),JSON.stringify({version:receipt.version,buildNumber:receipt.buildNumber,sourceDigest:receipt.sourceDigest,startupMs,scenes,resources,physicalDevice:false,renderer:'Edge SwiftShader software WebGL',limitations:['Animation-frame timing is browser scheduling, not Unity GPU frame time.','WASM memory is reserved linear memory, not total process usage.','Localhost transfers do not establish mobile-network loading time.','No target-phone budgets or device acceptance claimed.']},null,2));
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);console.log('Compiled performance measurements saved for title, map and combat.');await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
