// Real browser target switches, pointer/keyboard input and read-only Unity diagnostics.
// A raw CDP connection avoids Playwright's automatic focus/visibility emulation.
// Usage: node tools/interruption-playtest.cjs URL OUTPUT [--baseline | --seed-only]
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const assert=(value,message)=>{if(!value)throw Error(message);};
const output=path.resolve(process.argv[3]||'TestResults/Interruption');fs.mkdirSync(output,{recursive:true});
const seedOnly=process.argv.includes('--seed-only');
let child,ws,send,session,controls,state,layout=0,revision=0,lastInput;
const interruptions=[],feedback=[],sounds=[],visibility=[],screenshots=[],errors=[],checks=[];
const record=(name,value)=>{assert(value,name);checks.push(name);};
async function until(predicate,name,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){if(await predicate())return;await sleep(50);}throw Error('Timed out: '+name);}
const game=(method,params={})=>send(method,params,session);
const read=async expression=>(await game('Runtime.evaluate',{expression,returnByValue:true})).result.value;
const canvas=()=>read('(()=>{const r=document.querySelector("#unity-canvas").getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}})()');
async function point(id){const box=await canvas(),c=controls.Controls.find(x=>x.Id===id);assert(c,'Missing '+id);return{x:box.x+(c.X+c.Width*(id==='seed'?.8:.5))*box.width/controls.PanelWidth,y:box.y+(c.Y+c.Height/2)*box.height/controls.PanelHeight};}
async function tap(p){await game('Input.dispatchMouseEvent',{type:'mouseMoved',...p});await sleep(80);await game('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...p});await sleep(120);await game('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...p});}
async function click(id,changesState=false){
 await until(()=>controls?.Controls.some(x=>x.Id===id&&x.Enabled),'enabled '+id);
 for(let i=0;i<24;i++){
  const p=await point(id),box=await canvas();
  const bottom=box.y+box.height-(['play','end-turn','inspection-back','return-to-game'].includes(id)?5:controls.Controls.some(x=>x.Id==='end-turn'||x.Id==='inspection-back')?105:20);
  if(p.y<box.y+25||p.y>bottom){const old=layout;await game('Input.dispatchMouseEvent',{type:'mouseWheel',x:box.x+box.width/2,y:box.y+box.height/2,deltaX:0,deltaY:p.y<box.y+25?-400:400});await until(()=>layout>old,'scroll');await sleep(300);continue;}
  const old=changesState?revision:layout;lastInput={id,...p};await tap(p);await until(()=>(changesState?revision:layout)>old,'response '+id);await sleep(250);return;
 }throw Error('Cannot reach '+id);
}
async function shot(name){await sleep(250);const {data}=await game('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,name+'.png'),Buffer.from(data,'base64'));screenshots.push(name);}
async function key(key,code,windowsVirtualKeyCode){await game('Input.dispatchKeyEvent',{type:'keyDown',key,code,windowsVirtualKeyCode});await game('Input.dispatchKeyEvent',{type:'keyUp',key,code,windowsVirtualKeyCode});}
async function typeDigits(value){for(const digit of value){await game('Input.dispatchKeyEvent',{type:'keyDown',key:digit,code:'Digit'+digit,text:digit,unmodifiedText:digit,windowsVirtualKeyCode:48+Number(digit)});await sleep(120);await game('Input.dispatchKeyEvent',{type:'keyUp',key:digit,code:'Digit'+digit,windowsVirtualKeyCode:48+Number(digit)});}}
async function selectAll(){await game('Input.dispatchKeyEvent',{type:'keyDown',key:'a',code:'KeyA',windowsVirtualKeyCode:65,modifiers:2});await sleep(120);await game('Input.dispatchKeyEvent',{type:'keyUp',key:'a',code:'KeyA',windowsVirtualKeyCode:65,modifiers:2});}
async function seedTarget(name){const box=await canvas(),field=controls.Controls.find(x=>x.Id==='seed');record(name+' seed touch height at least 44 CSS pixels',field.Height*box.height/controls.PanelHeight>=43.995);}
function evidence(success){return {success,checks,visibility,interruptions,feedback,sounds,screenshots,errors,lastInput,state,controls,revision,layout,physicalDevice:false};}
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
   if(message.params.type==='error')errors.push(value);
  }
 };
 send=(method,params={},sessionId)=>new Promise((resolve,reject)=>{const n=++id,timer=setTimeout(()=>{pending.delete(n);reject(Error('CDP timeout '+method));},30000);pending.set(n,{resolve,reject,timer});ws.send(JSON.stringify({id:n,method,params,sessionId}));});
 const target=(await send('Target.createTarget',{url:'about:blank'})).targetId;
 session=(await send('Target.attachToTarget',{targetId:target,flatten:true})).sessionId;
 await game('Runtime.enable');await game('Page.enable');
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
  record('keyboard selection replacement and backspace survive interruption and rotation',state.Seed===240987);
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
