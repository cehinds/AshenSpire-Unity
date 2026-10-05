// Actual compiled UI audio: real controls, read-only diagnostics and Web Audio observation.
// The observer records source PCM/gain at playback; it never changes game or audio state.
const fs = require('node:fs'), path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const { NativeUiDriver } = require('./native-ui-driver.cjs');
let browser, ui;
(async () => {
 const url = process.argv[2], output = path.resolve(process.argv[3] || 'TestResults/NativeAudio');
 if (!url) throw Error('Pass a compiled Web URL.');
 browser = await chromium.launch({headless:true, ...(process.platform==='win32'?{channel:'msedge'}:{}), args:process.env.AS_BROWSER_GPU==='1'?[]:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page = await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:1});
 await page.addInitScript(() => {
  const edges = new WeakMap(), starts = [];
  const connect = AudioNode.prototype.connect;
  AudioNode.prototype.connect = function(destination, ...args) {
   const result = connect.call(this,destination,...args);
   if (destination instanceof AudioNode) { const targets=edges.get(this)||new Set(); targets.add(destination); edges.set(this,targets); }
   return result;
  };
  const disconnect = AudioNode.prototype.disconnect;
  AudioNode.prototype.disconnect = function(...args) {
   const result=disconnect.apply(this,args);
   if (!args.length || typeof args[0]==='number') edges.delete(this); else edges.get(this)?.delete(args[0]);
   return result;
  };
  function gain(node,seen=new Set()) {
   if (node instanceof AudioDestinationNode) return 1;
   if (seen.has(node)) return 0;
   const next=new Set(seen);next.add(node);
   const level=node instanceof GainNode?node.gain.value:1;
   return level*[...(edges.get(node)||[])].reduce((sum,target)=>sum+gain(target,next),0);
  }
  const start=AudioBufferSourceNode.prototype.start;
  AudioBufferSourceNode.prototype.start=function(...args) {
   if (this.buffer && this.buffer.duration<1) {
    const pcm=this.buffer.getChannelData(0);let peak=0;
    for (const value of pcm) peak=Math.max(peak,Math.abs(value));
    starts.push({duration:this.buffer.duration,peak,gain:gain(this),contextState:this.context.state});
   }
   return start.apply(this,args);
  };
  window.__audioObservation=starts;
 });
 ui = new NativeUiDriver(page,output);
 const sounds=[];
 page.on('console',message=>{
  const text=message.text(), match=/ASHENSPIRE_UI_SOUND ([\d.Ee+-]+)/.exec(text);
  if(match)sounds.push({bus:'ui',gain:Number(match[1])});
  else if(text.includes('ASHENSPIRE_SOUND '))sounds.push({bus:'sfx',cue:text.split('ASHENSPIRE_SOUND ')[1].trim()});
 });
 const count=bus=>sounds.filter(s=>s.bus===bus).length;
 const settled=()=>page.waitForTimeout(450);
 async function expectClick(id,bus,delta,label) {
  const before=count(bus), sources=await page.evaluate(()=>window.__audioObservation.length);
  await ui.click(id,false);await settled();ui.check(count(bus)-before===delta,label);
  if(delta===0)ui.check(await page.evaluate(()=>window.__audioObservation.length)===sources,label+' (no Web Audio transient started)');
 }
 const value=label=>Number((ui.controls.Labels||[]).find(s=>s.startsWith(label+' · '))?.match(/· (\d+)%/)?.[1]);
 async function endpoint(id,label,key,expected){await ui.click(id,false,.8);await ui.key(key);await ui.until(()=>value(label)===expected,label+' '+expected);}
 async function middle(id,label){await ui.click(id,false,.8);await ui.key('ArrowLeft');await settled();ui.check(value(label)>0&&value(label)<100,label+' has a nonzero intermediate value');return value(label)/100;}

 await ui.open(url);
 await expectClick('settings','ui',1,'opening Settings produces exactly one interface cue');
 await ui.until(()=>ui.has('preview-interface-sound'),'audio previews');
 await expectClick('preview-interface-sound','ui',1,'interface preview plays once');
 const base=sounds.filter(s=>s.bus==='ui').at(-1).gain;
 await expectClick('preview-interface-sound','ui',1,'control refresh does not duplicate audio callbacks');
 const keyboardBefore=count('ui');await ui.key('Enter');await settled();
 ui.check(count('ui')-keyboardBefore===1,'keyboard submit produces exactly one interface cue');
 const target=await ui.stablePoint('preview-interface-sound',.5),cancelledBefore=count('ui');
 await page.mouse.move(target.x,target.y);await page.mouse.down();await ui.frames();
 await page.mouse.move(1,1);await ui.frames();await page.mouse.up();await settled();
 ui.check(count('ui')===cancelledBefore,'dragging away from a button cancels its sound');
 const pcm=await page.evaluate(()=>window.__audioObservation.filter(s=>s.duration>.06&&s.duration<.07));
 ui.check(pcm.length>=2&&pcm.every(s=>s.peak>0&&s.gain>0&&s.contextState==='running'),'interface cues reach running Web Audio with nonzero PCM and gain');
 await endpoint('volume-sfx','Sound effects','Home',0);
 await expectClick('preview-sound-effect','sfx',0,'SFX zero silences combat preview');
 await expectClick('preview-interface-sound','ui',1,'SFX zero leaves interface sounds audible');
 await endpoint('volume-ui','Interface sounds','Home',0);
 await expectClick('preview-interface-sound','ui',0,'UI zero silences interface preview');
 await endpoint('volume-sfx','Sound effects','End',100);
 await expectClick('preview-sound-effect','sfx',1,'UI zero leaves combat preview audible');
 const master=await middle('volume-master','Master volume');
 const level=await middle('volume-ui','Interface sounds');
 await expectClick('preview-interface-sound','ui',1,'live interface volume applies without reopening Settings');
 const effective=sounds.filter(s=>s.bus==='ui').at(-1).gain;
 ui.check(Math.abs(effective-base*master*level)<.0001,'interface gain equals campaign tuning times master times UI level');
 const scaled=await page.evaluate(()=>window.__audioObservation.filter(s=>s.duration>.06&&s.duration<.07).at(-1));
 ui.check(Math.abs(scaled.gain-effective)<.0001,'actual Web Audio gain matches the selected interface bus');
 await ui.click('mute-sound',false);await settled();
 await expectClick('preview-interface-sound','ui',0,'mute suppresses interface preview');
 await expectClick('preview-sound-effect','sfx',0,'mute suppresses combat preview');
 await ui.click('mute-sound',false);await settled();
 await endpoint('volume-master','Master volume','Home',0);
 await expectClick('preview-interface-sound','ui',0,'master zero suppresses interface preview');
 await expectClick('preview-sound-effect','sfx',0,'master zero suppresses combat preview');
 await endpoint('volume-master','Master volume','End',100);
 await ui.shot('01-audio-settings');
 await ui.click('settings-section-6',false);await ui.click('back',false);await ui.until(()=>ui.has('settings'),'title');
 await expectClick('settings','ui',1,'reopening Settings produces one cue');
 await expectClick('preview-interface-sound','ui',1,'newly created preview binds once');
 const saved=value('Interface sounds');
 await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('settings'),'title after reload');
 await expectClick('settings','ui',1,'title button audio works after reload');
 await ui.until(()=>ui.has('preview-interface-sound'),'reloaded audio settings');
 ui.check(value('Interface sounds')===saved,'interface volume survives player reload');
 await expectClick('preview-interface-sound','ui',1,'reloaded preview plays exactly once');
 ui.check(Math.abs(sounds.filter(s=>s.bus==='ui').at(-1).gain-base*saved/100)<.0001,'reloaded setting controls actual playback gain');
 await ui.shot('02-persisted-audio');
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 fs.writeFileSync(path.join(output,'audio.json'),JSON.stringify({success:true,checks:ui.checks,initialPcm:pcm,scaledPcm:scaled,volumeCalculation:{campaign:base,master,ui:level,observed:effective},observations:await page.evaluate(()=>window.__audioObservation),settingsPersisted:true,physicalDevice:false,listeningAccepted:false},null,2));
 console.log('Compiled audio: '+ui.checks.length+' checks passed.');await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);const audio=await ui.page.evaluate(()=>window.__audioObservation).catch(()=>null);fs.writeFileSync(path.join(ui.output,'audio-failure.json'),JSON.stringify(audio,null,2));}if(browser)await browser.close();process.exitCode=1;});
