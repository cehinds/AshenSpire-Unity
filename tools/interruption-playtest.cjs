// Real browser target switches, pointer/keyboard input and read-only Unity diagnostics.
// A raw CDP connection avoids Playwright's automatic focus/visibility emulation.
// Usage: node tools/interruption-playtest.cjs URL OUTPUT [--baseline | --seed-only] [--slow-input]
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(value,message)=>{if(!value)throw Error(message);};
const output=path.resolve(process.argv[3]||'TestResults/Interruption');fs.mkdirSync(output,{recursive:true});
const seedOnly=process.argv.includes('--seed-only');
let child,ws,send,session,controls,state,layout=0,revision=0,lastInput;
const interruptions=[],feedback=[],sounds=[],visibility=[],screenshots=[],errors=[],errorContexts=[],checks=[],seedPixels=[],seedCampaigns=[];
const record=(name,value)=>{assert(value,name);checks.push(name);};
async function until(predicate,name,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){if(await predicate())return;await sleep(50);}throw Error('Timed out: '+name);}
const game=(method,params={})=>send(method,params,session);
const read=async expression=>(await game('Runtime.evaluate',{expression,returnByValue:true})).result.value;
const canvas=()=>read('(()=>{const r=document.querySelector("#unity-canvas").getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}})()');
async function point(id){const box=await canvas(),c=controls.Controls.find(x=>x.Id===id);assert(c,'Missing '+id);return{x:box.x+(c.X+c.Width*(id==='seed'?.8:.5))*box.width/controls.PanelWidth,y:box.y+(c.Y+c.Height/2)*box.height/controls.PanelHeight};}
// Give UI Toolkit separate rendered frames for hover, press and release/focus.
// A wall-clock hold can expire inside one slow player frame at phone pixel density.
async function tap(p){
 await game('Input.dispatchMouseEvent',{type:'mouseMoved',...p});await sleep(80);await inputFrames();
 await game('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...p});await sleep(120);await inputFrames();
 await game('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...p});await inputFrames();
}
async function click(id,changesState=false){
 await until(()=>controls?.Controls.some(x=>x.Id===id&&x.Enabled),'enabled '+id);
 for(let i=0;i<24;i++){
  const p=await point(id),box=await canvas();
  const bottom=box.y+box.height-(['play','end-turn','inspection-back','return-to-game'].includes(id)?5:controls.Controls.some(x=>x.Id==='end-turn'||x.Id==='inspection-back')?105:20);
  if(p.y<box.y+25||p.y>bottom){const old=layout;await game('Input.dispatchMouseEvent',{type:'mouseWheel',x:box.x+box.width/2,y:box.y+box.height/2,deltaX:0,deltaY:p.y<box.y+25?-400:400});await until(()=>layout>old,'scroll');await sleep(300);continue;}
  const old=changesState?revision:layout;lastInput={id,...p};await tap(p);await until(()=>(changesState?revision:layout)>old,'response '+id);await sleep(250);return;
 }throw Error('Cannot reach '+id);
}
async function shot(name){
 await sleep(250);const {data}=await game('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,name+'.png'),Buffer.from(data,'base64'));screenshots.push(name);
 if(seedOnly&&['03-draft-before','seed-02-selected','seed-04-edited','seed-05-landscape-unfocused','seed-06-landscape-focused','seed-07-landscape-selected','seed-09-portrait-return'].includes(name)){
  const box=await canvas(),field=controls.Controls.find(x=>x.Id==='seed');
  const region={x:box.x+(field.X+field.Width*.54)*box.width/controls.PanelWidth,y:box.y+(field.Y+field.Height*.2)*box.height/controls.PanelHeight,width:field.Width*.4*box.width/controls.PanelWidth,height:field.Height*.6*box.height/controls.PanelHeight};
  // Read pixels from the captured frame in a detached canvas. No game state or DOM changes.
  const sample=async(data,region)=>{
   const image=new Image();image.src='data:image/png;base64,'+data;await image.decode();
   const surface=document.createElement('canvas');surface.width=image.width;surface.height=image.height;const context=surface.getContext('2d');context.drawImage(image,0,0);
   const scale=image.width/innerWidth,pixels=context.getImageData(Math.round(region.x*scale),Math.round(region.y*scale),Math.round(region.width*scale),Math.round(region.height*scale)).data,counts=new Map();
   for(let i=0;i<pixels.length;i+=4){const rgb=[pixels[i],pixels[i+1],pixels[i+2]].join(',');counts.set(rgb,(counts.get(rgb)||0)+1);}
   const luminance=rgb=>rgb.split(',').map(Number).map(c=>c/255).map(c=>c<=.04045?c/12.92:((c+.055)/1.055)**2.4).reduce((sum,c,i)=>sum+c*[.2126,.7152,.0722][i],0);
   const colors=[...counts].filter(([,count])=>count>=10).map(([rgb,count])=>({rgb,count,luminance:luminance(rgb)})).sort((a,b)=>b.count-a.count);
   const foreground=colors.reduce((a,b)=>a.luminance>b.luminance?a:b),backgrounds=colors.slice(0,2).filter(c=>c.count>pixels.length/4*.02);
   return{foreground,backgrounds,contrast:Math.min(...backgrounds.map(c=>(foreground.luminance+.05)/(c.luminance+.05)))};
  };
  const result=(await game('Runtime.evaluate',{expression:'('+sample.toString()+')('+JSON.stringify(data)+','+JSON.stringify(region)+')',awaitPromise:true,returnByValue:true})).result.value;
  seedPixels.push({name,...result});record(name+' rendered digits have at least 4.5 contrast',result.foreground.luminance>.45&&result.contrast>=4.5);
 }
}
// Let the player consume each press/release before the next key or modifier.
// Wall-clock holds alone can place release and the next press in one slow frame.
async function inputFrames(){await game('Runtime.evaluate',{expression:'new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))',awaitPromise:true});}
async function key(key,code,windowsVirtualKeyCode,extra={}){await game('Input.dispatchKeyEvent',{type:'keyDown',key,code,windowsVirtualKeyCode,...extra});await sleep(120);await inputFrames();await game('Input.dispatchKeyEvent',{type:'keyUp',key,code,windowsVirtualKeyCode,modifiers:extra.modifiers||0});await inputFrames();}
async function typeDigits(value){for(const digit of value)await key(digit,'Digit'+digit,48+Number(digit),{text:digit,unmodifiedText:digit});}
async function selectAll(){await game('Input.dispatchKeyEvent',{type:'keyDown',key:'Control',code:'ControlLeft',windowsVirtualKeyCode:17,modifiers:2});await inputFrames();await key('a','KeyA',65,{modifiers:2});await game('Input.dispatchKeyEvent',{type:'keyUp',key:'Control',code:'ControlLeft',windowsVirtualKeyCode:17,modifiers:0});await inputFrames();}
async function seedTarget(name){const box=await canvas(),field=controls.Controls.find(x=>x.Id==='seed');record(name+' seed touch height at least 44 CSS pixels',field.Height*box.height/controls.PanelHeight>=43.995);}
function verifySeed(stage){
 seedCampaigns.push({stage,expected:240987,observed:state?.Seed,revision,state:JSON.parse(JSON.stringify(state))});
 record(stage+' campaign starts with the exact typed seed',state?.Seed===240987);
}
function evidence(success){return {success,checks,seedPixels,seedCampaigns,visibility,interruptions,feedback,sounds,screenshots,errors,errorContexts,lastInput,state,controls,revision,layout,physicalDevice:false};}
(async()=>{
 const profileRoot=path.resolve('Builds/BrowserProfiles');fs.mkdirSync(profileRoot,{recursive:true});
 const profile=fs.mkdtempSync(path.join(profileRoot,'Interruption-'));
 const executable=process.env.INTERRUPTION_BROWSER||(process.platform==='win32'?'C:/Program Files/Google/Chrome/Application/chrome.exe':require(process.env.PLAYWRIGHT_MODULE||'playwright').chromium.executablePath());
 child=spawn(executable,['--headless=new','--no-sandbox','--no-first-run','--no-default-browser-check','--disable-sync','--disable-extensions','--disable-features=msForceBrowserSignIn','--disable-backgrounding-occluded-windows','--disable-renderer-backgrounding','--enable-unsafe-swiftshader','--use-angle=swiftshader','--remote-debugging-port=0','--user-data-dir='+profile,'--no-startup-window'],{windowsHide:true,stdio:'ignore'});
 await until(()=>fs.existsSync(path.join(profile,'DevToolsActivePort')),'browser startup');
 const port=fs.readFileSync(path.join(profile,'DevToolsActivePort'),'utf8').split('\n')[0];
 const version=await(await fetch('http://127.0.0.1:'+port+'/json/version')).json();
 ws=new WebSocket(version.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject;});
 let id=0;const pending=new Map();
 ws.onmessage=e=>{
  const message=JSON.parse(e.data);
  if(message.id){const p=pending.get(message.id);if(!p)return;pending.delete(message.id);clearTimeout(p.timer);message.error?p.reject(Error(JSON.stringify(message.error))):p.resolve(message.result);return;}
  if(message.sessionId!==session)return;
  if(message.method==='Runtime.exceptionThrown')errors.push(message.params.exceptionDetails.text);
  if(message.method==='Runtime.consoleAPICalled'){
   const value=message.params.args.map(x=>x.value??x.description??'').join(' ');
   if(value.startsWith('ASHENSPIRE_CONTROLS ')){try{controls=JSON.parse(value.slice(20));layout++;}catch(error){errors.push('Invalid control diagnostics: '+error.message);}}
   if(value.startsWith('ASHENSPIRE_CAMPAIGN ')){state=JSON.parse(value.slice(20));revision++;}
   if(value.startsWith('ASHENSPIRE_INTERRUPTION '))interruptions.push(JSON.parse(value.slice(24)));
   if(value.startsWith('ASHENSPIRE_FEEDBACK '))feedback.push(JSON.parse(value.slice(20)));
   if(value.startsWith('ASHENSPIRE_SOUND '))sounds.push(value.slice(17));
   if(message.params.type==='error'){errors.push(value);errorContexts.push({message:value,lastScreenshot:screenshots.at(-1),lastInput,revision,layout});}
  }
 };
 send=(method,params={},sessionId)=>new Promise((resolve,reject)=>{const n=++id,timer=setTimeout(()=>{pending.delete(n);reject(Error('CDP timeout '+method));},30000);pending.set(n,{resolve,reject,timer});ws.send(JSON.stringify({id:n,method,params,sessionId}));});
 const target=(await send('Target.createTarget',{url:'about:blank'})).targetId;
 session=(await send('Target.attachToTarget',{targetId:target,flatten:true})).sessionId;
 await game('Runtime.enable');await game('Page.enable');
 if(process.argv.includes('--slow-input'))await game('Emulation.setCPUThrottlingRate',{rate:6});
 await game('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:seedOnly?3:1,mobile:false});
 await game('Page.navigate',{url:process.argv[2]||'http://127.0.0.1:8787/'});
 await until(()=>controls?.Controls.some(x=>x.Id==='new'),'Unity ready',120000);
 const other=(await send('Target.createTarget',{url:'about:blank',background:true})).targetId;
 async function hide(){await send('Target.activateTarget',{targetId:other});await until(async()=>await read('document.visibilityState')==='hidden','real hidden document');visibility.push('hidden');}
 async function foreground(){await send('Target.activateTarget',{targetId:target});await game('Page.bringToFront');await until(async()=>await read('document.visibilityState')==='visible','real visible document');visibility.push('visible');}
 async function interrupt(name){
  const snapshot=JSON.stringify(state),oldRevision=revision,count=interruptions.length;
  await hide();await until(()=>interruptions.length>count&&interruptions.at(-1).Blocked&&!interruptions.at(-1).CanReturn,'background gate');
  record(name+' stops audio',!interruptions.at(-1).AudioPlaying);
  if(name==='09-repeat'){await game('Page.setWebLifecycleState',{state:'frozen'});await sleep(300);await game('Page.setWebLifecycleState',{state:'active'});record('browser freeze leaves return blocked until visible',interruptions.at(-1).Blocked&&!interruptions.at(-1).CanReturn);}
  await foreground();await until(()=>interruptions.at(-1)?.CanReturn&&controls?.Controls.some(x=>x.Id==='return-to-game'&&x.Enabled),'return cover');
  await shot(name+'-paused');record(name+' preserves campaign while covered',snapshot===JSON.stringify(state)&&oldRevision===revision);
  record(name+' exposes only the return control',controls.Controls.length===1);
  if(name==='04-map'){
   await game('Emulation.setDeviceMetricsOverride',{width:740,height:320,deviceScaleFactor:1,mobile:false});
   await until(()=>controls.PanelWidth>controls.PanelHeight,'landscape cover');await sleep(300);
   const box=await canvas(),button=controls.Controls[0];
   record('landscape return retains 44 pixel target',button.Height*box.height/controls.PanelHeight>=43.995);
   record('landscape return fits the viewport',button.Y>=0&&button.Y+button.Height<=controls.PanelHeight);
   await shot('04-map-landscape-paused');
   await game('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});
   await until(()=>controls.PanelHeight>controls.PanelWidth,'portrait cover');await sleep(300);
  }
  await click('return-to-game');record(name+' returns without a command',snapshot===JSON.stringify(state)&&oldRevision===revision&&!interruptions.at(-1).Blocked);
  await shot(name+'-returned');
 }
 await shot('01-title');
 if(process.argv.includes('--baseline')){
  await hide();await sleep(300);await foreground();await sleep(600);
  record('0.7 lacks interruption gate',interruptions.length===0&&!controls.Controls.some(x=>x.Id==='return-to-game'));
  await shot('02-legacy-return');fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(evidence(true),null,2));return;
 }
 await interrupt('02-title');await click('new');
 if(seedOnly){await shot('seed-01-empty');await seedTarget('portrait');}
 await tap(await point('seed'));await sleep(200);
 await typeDigits('240987');
 await shot('03-draft-before');
 if(seedOnly){
  // Commit the first draft before a later replacement can conceal missing digits.
  // Re-enter through the menu using real input; the disposable profile owns this save.
  await click('hero-reaver',true);verifySeed('first draft');await shot('seed-01-first-campaign');
  await click('menu');await click('new');await tap(await point('seed'));await typeDigits('240987');
  await selectAll();await shot('seed-02-selected');
  await typeDigits('42949672950');
  const oldRevision=revision;await tap(await point('hero-reaver'));await sleep(500);
  record('out of range seed stays on hero screen',revision===oldRevision&&controls.Controls.some(x=>x.Id==='seed'));
  await shot('seed-03-invalid');
  await tap(await point('seed'));await selectAll();await typeDigits('240986');
  await key('Backspace','Backspace',8);await typeDigits('7');await shot('seed-04-edited');
  await interrupt('03-draft');
  await game('Emulation.setDeviceMetricsOverride',{width:740,height:320,deviceScaleFactor:3,mobile:false});
  await until(()=>controls.PanelWidth>controls.PanelHeight,'seed landscape');await sleep(300);await seedTarget('landscape');
  await shot('seed-05-landscape-unfocused');await tap(await point('seed'));await shot('seed-06-landscape-focused');
  await selectAll();await shot('seed-07-landscape-selected');await interrupt('seed-08-landscape');
  await game('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:3,mobile:false});
  await until(()=>controls.PanelHeight>controls.PanelWidth,'seed portrait return');await sleep(300);
  await shot('seed-09-portrait-return');await click('hero-reaver',true);
  verifySeed('keyboard selection replacement and backspace after interruption and rotation');
  await shot('seed-10-created-campaign');record('no browser or Unity errors',errors.length===0);
  fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(evidence(true),null,2));
  console.log('Seed entry browser: '+checks.length+' checks passed; '+screenshots.length+' screenshots');return;
 }
 await interrupt('03-draft');await click('hero-reaver',true);record('seed draft survived interruption',state.Seed===240987);
 await interrupt('04-map');await click('enter-0',true);
 await click('card-0');const selected=controls.Controls.find(x=>x.Id==='play').Enabled;const beforeSelection=JSON.stringify(controls);
 await interrupt('05-selection');record('selected card and scroll geometry preserved',controls.Controls.find(x=>x.Id==='play').Enabled===selected&&JSON.stringify(controls)===beforeSelection);
 await click('intent-details');const inspection=JSON.stringify(controls);await interrupt('06-inspection');record('inspection content and scroll preserved',JSON.stringify(controls)===inspection);await click('inspection-back');
 // Start a cue through input, then switch immediately on its observed start.
 const endPoint=await point('end-turn'),feedbackStart=feedback.length;await tap(endPoint);
 await until(()=>feedback.slice(feedbackStart).some(x=>x.Status==='started'),'cue start');
 const afterTurn=JSON.stringify(state),afterRevision=revision;await hide();await foreground();
 await until(()=>controls?.Controls.some(x=>x.Id==='return-to-game'&&x.Enabled),'feedback return cover');
 await sleep(800);
 record('active feedback cancelled without later completion',feedback.slice(feedbackStart).some(x=>x.Status==='cancelled')&&!feedback.slice(feedbackStart).some(x=>x.Status==='completed'));
 const returnPoint=await point('return-to-game');record('blocked input probe avoids return button',Math.abs(endPoint.y-returnPoint.y)>80);
 await tap(endPoint);await sleep(250);
 record('covered underlying input cannot advance a turn',afterTurn===JSON.stringify(state)&&revision===afterRevision&&interruptions.at(-1).Blocked);
 if(interruptions.at(-1).Blocked)await click('return-to-game');
 await shot('07-feedback-return');
 await interrupt('08-repeat');await interrupt('09-repeat');
 await click('menu');await click('settings');await click('mute-sound');
 await interrupt('10-muted');record('temporary suspension preserves mute',interruptions.at(-1).Muted);
 const saved=JSON.stringify(state);controls=null;await game('Page.reload',{ignoreCache:true});await until(()=>controls?.Controls.some(x=>x.Id==='continue'),'reload with save',120000);
 await click('continue',true);record('reload resumes identical saved campaign',JSON.stringify(state)===saved);await shot('11-reloaded');
 record('no browser or Unity errors',errors.length===0);
 fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(evidence(true),null,2));console.log('Interruption browser: '+checks.length+' checks passed; '+screenshots.length+' screenshots');
})().catch(async error=>{errors.push(error.stack);try{if(session)await shot('failure');}catch{}fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify(evidence(false),null,2));console.error(error);process.exitCode=1;}).finally(async()=>{try{if(send)await send('Browser.close');}catch{}ws?.close();if(child&&child.exitCode===null)child.kill();});
