// Native card readability acceptance using actual pointer/keyboard input only.
// Run: node tools/native-card-cost-playtest.cjs <compiled-Web-URL> [output-directory]
// Inspect the PNGs as well as the read-only labels, costs and control receipts.
// No saves, Unity messages, gameplay state or network commands are injected.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');

let browser,ui,observations=[];
(async()=>{
 const url=process.argv[2];if(!url)throw Error('Pass the compiled Unity Web URL');
 const output=path.resolve(process.argv[3]||'TestResults/NativeCardCosts');fs.mkdirSync(output,{recursive:true});
 const replay=JSON.parse(fs.readFileSync(path.join(__dirname,'../UnityTests/Parity/native-browser-replay.json'))).runs.find(run=>run.classId==='reaver'&&run.seed===1).trace;
 const opening=replay.slice(0,5); // Enter, three legal payments, then the first enemy turn.
 const summaries=[];let source;
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 for(const viewport of[{width:320,height:640},{width:390,height:844},{width:1440,height:900}]){
  const context=await browser.newContext({viewport,deviceScaleFactor:viewport.width<500?2:1});const page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,viewport.width+'x'+viewport.height));observations=[];
  const labels=()=>ui.controls.Labels||[];
  const state=()=>JSON.stringify(ui.state);
  const capture=label=>{observations.push({label,state:JSON.parse(state()),labels:[...labels()],controls:ui.controls.Controls});fs.writeFileSync(path.join(ui.output,'readability.json'),JSON.stringify(observations,null,2));};
  const rowFor=id=>ui.state.cards.find(row=>row.instance.instanceId===id);
  const payable=row=>(row.cost.variable||row.cost.action<=ui.state.player.energy)&&row.cost.mana<=ui.state.player.mana&&row.cost.stamina<=ui.state.player.stamina;
  const select=async id=>{
   const control='native-card-'+id;let steps=0;
   while(!ui.has(control)&&steps++<6){if(ui.has('native-hand-next'))await ui.click('native-hand-next');else if(ui.has('native-hand-prev'))await ui.click('native-hand-prev');else break;}
   ui.check(ui.has(control),'card remains selectable: '+id);await ui.click(control);
  };
  const cleanCosts=()=>{
   ui.check(labels().some(label=>label==='1'),'single-action medallion displays its action count');
   ui.check(!labels().some(label=>/\b0 MP\b|\b0 stamina\b|\b1 actions\b/.test(label)),'visible cost labels omit zero MP/stamina and incorrect plural');
  };
  const matches=action=>ui.state.phase===action.phase&&ui.state.player.hp===action.hp&&ui.state.player.mana===action.mana&&ui.state.player.stamina===action.stamina&&ui.state.turn===action.turn;
  await ui.open(url);const initial=fs.readFileSync(path.join(ui.output,'build-source.json'));
  if(source)ui.check(initial.equals(source),'viewport uses the same compiled source');else source=initial;
  await ui.click('native-new');await ui.click('foundation-mode-standard');await ui.fill('native-seed','1');await ui.command('native-begin');
  ui.check(ui.state.phase==='Map','normal Standard Reaver climb starts');await ui.command('native-route-'+opening[0].command.slice('enter:'.length));
  ui.check(matches(opening[0]),'opening fight matches committed seed1 resources');
  const weapon=ui.state.cards.find(row=>row.card.equipmentProfileId==='bladeAttack'||row.instance.profileId==='bladeAttack');
  ui.check(!!weapon,'actual equipped blade supplies a Slashing Strike');
  ui.check(weapon.cost.action===1&&weapon.cost.mana===0&&weapon.cost.stamina===0,'weapon cost comes from authoritative one-action zero-extra-resource profile');
  await select(weapon.instance.instanceId);
  ui.check(labels().some(label=>/^Deal 14 damage\./.test(label)&&label.includes('Includes +8 total damage from Strength.')),'weapon description displays14 total with included8 Strength contribution');
  cleanCosts();ui.check(ui.has('native-play'),'affordable weapon card enables Play');capture('weapon-total');await ui.shot('01-weapon-total-and-cost');
  // The first three payments are the committed real-play oracle; they exhaust
  // actions while leaving a live enemy and unplayed cards for refusal inspection.
  for(const action of opening.slice(1,4)){
   const row=rowFor(action.instanceId);ui.check(!!row&&payable(row),'recorded opening payment is affordable: '+action.instanceId);
   await ui.click('native-target-'+action.targetId);await select(action.instanceId);
   ui.check(ui.has('native-play'),'selected recorded attack enables Play');
   const before=ui.state.player.energy;await ui.command('native-play');
   ui.check(matches(action),'actual command matches HP/MP/stamina/turn oracle: '+action.command);
   ui.check(ui.state.player.energy===before-row.cost.action,'actual action pool pays the observed card cost');
  }
  ui.check(ui.state.phase==='Combat'&&ui.state.player.energy===0,'three real cards exhaust actions without ending the fight');
  const unaffordable=ui.state.cards.find(row=>!row.cost.variable&&row.cost.action===1&&row.cost.mana<=ui.state.player.mana&&row.cost.stamina<=ui.state.player.stamina&&!['curse','status'].includes(row.card.type));
  ui.check(!!unaffordable,'an otherwise payable card remains in hand at zero actions');
  const exhausted=state();await select(unaffordable.instance.instanceId);
  ui.check(state()===exhausted,'inspecting an unaffordable card does not mutate gameplay');
  ui.check(labels().includes('Need 1 more action'),'selected unaffordable card explains its exact action shortage');
  ui.check(!ui.has('native-play')&&ui.controls.Controls.some(control=>control.Id==='native-play'&&!control.Enabled),'Play is disabled while the card remains selectable');
  cleanCosts();capture('unaffordable-selected');await ui.shot('02-selectable-shortage');
  const disabled=ui.controls.Controls.find(control=>control.Id==='native-play'),canvas=await page.locator('#unity-canvas').boundingBox();
  const x=canvas.x+(disabled.X+disabled.Width/2)*canvas.width/ui.controls.PanelWidth,y=canvas.y+(disabled.Y+disabled.Height/2)*canvas.height/ui.controls.PanelHeight;
  ui.check(x>=canvas.x&&x<canvas.x+canvas.width&&y>=canvas.y&&y<canvas.y+canvas.height,'disabled Play footer is actually on screen');
  await page.mouse.move(x,y);await page.mouse.down();await page.waitForTimeout(140);await page.mouse.up();await page.waitForTimeout(300);
  ui.check(state()===exhausted,'physical click on disabled Play leaves gameplay unchanged');
  await ui.command('native-end-turn');ui.check(matches(opening[4]),'enemy turn matches committed HP/resources');ui.check(ui.state.player.energy>0,'next turn restores actions');
  const recovered=ui.state.cards.find(row=>payable(row)&&row.cost.action===1&&!['curse','status'].includes(row.card.type));ui.check(!!recovered,'next hand has a normally payable card');
  await select(recovered.instance.instanceId);ui.check(ui.has('native-play'),'valid card enables Play again after resource recovery');cleanCosts();capture('next-turn-recovery');await ui.shot('03-next-turn-recovery');
  // Reach the save menu through the measured utility rail with real input.
  const saved=state();await ui.click("native-menu");await ui.until(()=>ui.has("native-continue"),"save menu returns to title");
  ui.controls=null;ui.state=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'Continue after reload');await ui.command('native-continue');
  ui.check(state()===saved,'menu and reload restore exact combat state and resource costs');await select(recovered.instance.instanceId);ui.check(ui.has('native-play'),'restored valid card remains playable');cleanCosts();capture('restored-costs');await ui.shot('04-restored-costs');
  const latest=await page.request.get(new URL('build-source.json',url).href);ui.check(latest.ok()&&(await latest.body()).equals(initial),'served build stayed source-matched');
  ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);summaries.push({viewport,checks:ui.checks.length,physicalDevice:false,screenshots:4});console.log('Native card-cost checks passed: '+viewport.width+'x'+viewport.height+' ('+ui.checks.length+')');await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,viewports:summaries,checks:summaries.reduce((sum,row)=>sum+row.checks,0),physicalDevice:false,cooperativeBrowserProof:false,buttonLabelEvidence:'Disabled Play label is captured in screenshots; the read-only control report exposes enabled state and Label text, not Button.text.'},null,2));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);fs.writeFileSync(path.join(ui.output,'readability.json'),JSON.stringify(observations,null,2));}if(browser)await browser.close();process.exitCode=1;});
