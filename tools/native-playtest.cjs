// Play a full native Unity climb through real pointer and keyboard input.
// Read-only diagnostics assert state; no JavaScript game commands or state writes.
const {controlReportsForPage}=require('./control-report.cjs');
const fs=require('node:fs'), path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const output=path.resolve(process.argv[3]||'TestResults/Native'); fs.mkdirSync(output,{recursive:true});
const replay=JSON.parse(fs.readFileSync(process.argv[4]||path.join(__dirname,'../UnityTests/Parity/native-browser-replay.json'),'utf8')).runs[0].trace;
let browser,page,controls,state,layout=0,revision=0; const chunks=new Map();
const errors=[],screenshots=[],checks=[],commands=[];
function check(value,name){if(!value)throw Error(name);checks.push(name);}
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
  // The bounded map can clip a route's canvas coordinates. Use its real Routes
  // list for this full-climb replay; direct map taps have separate acceptance.
  const route = /^(native|coop)-route-(.+)$/.exec(id);
  if (route && controls?.Controls.some(c => c.Id === route[1] + '-map-routes')) {
    await click(route[1] + '-map-routes');
    return click(route[1] + '-map-choice-' + route[2], change, fraction);
  }
  await until(() => controls?.Controls.some(x => x.Id === id && x.Enabled), 'control ' + id);
  for (let step = 0; step < 30; step++) {
    const canvas = await page.locator('#unity-canvas').boundingBox();
    const c = controls.Controls.find(x => x.Id === id);
    const x = canvas.x + (c.X + c.Width * fraction) * canvas.width / controls.PanelWidth;
    const y = canvas.y + (c.Y + c.Height / 2) * canvas.height / controls.PanelHeight;
    if (y < canvas.y + 30 || y > canvas.y + canvas.height - (state?.phase === 'Combat' && !['native-play','native-end-turn'].includes(id) ? 120 : 25)) {
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

async function command(id){const previous=revision;await click(id);await until(()=>revision>previous,'native state '+id);}
const seen=new Set();
async function capture(){const key=state.act+'-'+state.phase;if(!seen.has(key)){seen.add(key);await shot(String(screenshots.length+1).padStart(2,'0')+'-phone-act'+key);}}
function report(success){return {success,checks,screenshots,errors,commands,lastState:state,controls,viewport:{width:390,height:844},physicalDevice:false};}
(async()=>{
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:2});
 const normalizeControls=controlReportsForPage(page,e=>errors.push(e));
 page.on('pageerror',e=>errors.push(e.message));
 page.on('console',m=>{const value=normalizeControls(m.text());if(value===null)return;if(m.type()==='error')errors.push(value);for(const [prefix,receive] of [['ASHENSPIRE_CONTROLS ',d=>{controls=d;layout++;}],['ASHENSPIRE_NATIVE_STATE_CHUNK ',d=>{let parts=chunks.get(d.sequence);if(!parts){parts=[];chunks.set(d.sequence,parts);}parts[d.index]=d.text;if(parts.filter(x=>x!==undefined).length===d.count){state=JSON.parse(parts.join(''));revision++;chunks.delete(d.sequence);}}]]){const at=value.indexOf(prefix);if(at>=0)receive(JSON.parse(value.slice(at+prefix.length)));}});
 await page.goto(process.argv[2]);await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await until(()=>controls?.Controls.length,'title');await shot('00-phone-title');
 await click('native-new');await shot('01-phone-assign-points');
 check(controls.Labels.some(t=>t.includes('35')),'35 points initially unspent');
 await click('foundation-mode-standard');await click('native-seed',false,.8);await key('Control+a');await key('Backspace');await key('1');await key('Tab');await shot('02-phone-standard-creation');await command('native-begin');
 check(state.phase==='Map','native three-act run starts');
 for(let index=0;index<replay.length;index++){
  const action=replay[index];await capture();check(state.phase===action.beforePhase&&state.player.hp===action.beforeHp,'before command '+index+' '+action.command);
  const [kind,...parts]=action.command.split(':');const value=parts.join(':');
  if(kind==='play'){
   const instance=state.hand.find(c=>c.instanceId===action.instanceId);check(!!instance,'replay instance '+action.instanceId);
   await click('native-target-'+action.targetId);
   while(!controls.Controls.some(c=>c.Id==='native-card-'+action.instanceId)&&controls.Controls.some(c=>c.Id==='native-hand-next'&&c.Enabled))await click('native-hand-next');
   await click('native-card-'+action.instanceId);await command('native-play');
  }else if(kind==='enter')await command('native-route-'+value);
  else if(kind==='charge')await command(value==='hp'?'native-crimson':'native-azure');
  else if(kind==='flask'){await click('native-target-'+action.targetId);await command('native-flask-0');}
  else if(kind==='reward')await command('native-reward-'+value+(value==='card'?'-'+action.cardId:''));
  else if(kind==='event')await command('native-choice-'+value);
  else if(kind==='buy')await command('native-buy-relics-'+action.index);
  else if(kind==='service')await command('native-upgrade-'+action.itemRef.replaceAll('/','-'));
  else {const ids={endTurn:'native-end-turn',catchBreath:'native-breath',continueRewards:'native-rewards-continue',rest:'native-rest',leaveShrine:'native-shrine-leave',leaveEvent:'native-event-leave',leaveShop:'native-shop-leave'};if(!ids[kind])throw Error('Unknown command '+kind);await command(ids[kind]);}
  check(state.phase===action.phase&&state.player.hp===action.hp&&state.player.mana===action.mana&&state.player.stamina===action.stamina,'IL2CPP state agrees with domain after '+index+' '+action.command);
  commands.push({index,command:action.command,phase:state.phase,hp:state.player.hp});
  fs.writeFileSync(path.join(output,'progress.json'),JSON.stringify(report(false),null,2));
  if(index===2||index===70){const expected=JSON.stringify(state);await click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await command('native-continue');check(JSON.stringify(state)===expected,'persistent page reload resumes exact native state at '+index);}
  if(index%20===0)console.log('Native browser: '+index+'/'+replay.length+' '+state.phase+' act'+state.act);
 }
 await capture();check(state.phase==='Victory'&&state.act===3,'three-act victory through actual player controls');
 await click('native-menu');await click('native-profile');await shot('99-phone-chronicle');
 check(controls.Labels.some(t=>t.toLowerCase().includes('victor')),'completed climb recorded in chronicle');
 await page.setViewportSize({width:1280,height:900});await page.waitForTimeout(1200);await shot('100-desktop-chronicle');
 check(errors.length===0,'no browser or Unity error logs');fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(report(true),null,2));console.log('Native browser full climb passed: '+checks.length+' checks, '+commands.length+' commands.');await browser.close();
})().catch(async e=>{errors.push(e.stack||String(e));if(page)await page.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify(report(false),null,2));if(browser)await browser.close();console.error(e);process.exitCode=1;});
