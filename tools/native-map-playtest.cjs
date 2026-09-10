// Shared map acceptance through real browser input and read-only Unity diagnostics.
// Run: node tools/native-map-playtest.cjs <compiled-Web-URL> [output-directory] [--desktop-only]
// Requires PLAYWRIGHT_MODULE when Playwright is not installed locally. No save,
// gameplay, DOM input-event, Unity command, or network-intent injection is used.
// Screenshots/receipts are source-matched; viewport emulation is not device proof.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');

class MapObserver {
 constructor(page,ui){
  this.value=null;this.revision=0;this.parts=new Map();this.receipts=[];
  page.on('console',message=>{
   const prefix='ASHENSPIRE_MAP_VIEW_CHUNK ',text=message.text(),at=text.indexOf(prefix);if(at<0)return;
   try{
    const chunk=JSON.parse(text.slice(at+prefix.length));
    if(!Number.isInteger(chunk.index)||!Number.isInteger(chunk.count)||chunk.count<1||chunk.count>1024||chunk.index<0||chunk.index>=chunk.count||typeof chunk.text!=='string')throw Error('Invalid map chunk envelope');
    const key=String(chunk.sequence),entry=this.parts.get(key)||{count:chunk.count,parts:[]};
    if(entry.count!==chunk.count)throw Error('Map chunk count changed');
    entry.parts[chunk.index]=chunk.text;this.parts.set(key,entry);
    if(entry.parts.filter(x=>x!==undefined).length===entry.count){this.value=JSON.parse(entry.parts.join(''));this.revision++;this.parts.delete(key);}
   }catch(error){ui.errors.push('Invalid map observer JSON: '+error.message);}
  });
 }
 capture(label){this.receipts.push({label,view:JSON.parse(JSON.stringify(this.value))});}
 reset(){this.value=null;this.parts.clear();}
}
const inside=(rect,bounds,pad=0)=>rect.x>=bounds.x-pad&&rect.y>=bounds.y-pad&&rect.x+rect.width<=bounds.x+bounds.width+pad&&rect.y+rect.height<=bounds.y+bounds.height+pad;
const overlaps=(a,b)=>Math.min(a.x+a.width,b.x+b.width)-Math.max(a.x,b.x)>.5&&Math.min(a.y+a.height,b.y+b.height)-Math.max(a.y,b.y)>.5;
function sameCamera(a,b){return Object.keys(a).length===Object.keys(b).length&&Object.keys(a).every(key=>typeof a[key]==='number'?Math.abs(a[key]-b[key])<.75:a[key]===b[key]);}
let browser,ui,map;

(async()=>{
 const url=process.argv[2];if(!url)throw Error('Pass the compiled Unity Web URL');
 const output=path.resolve(process.argv[3]||'TestResults/NativeMap');fs.mkdirSync(output,{recursive:true});
 const traceBytes=fs.readFileSync(path.join(__dirname,'../UnityTests/Parity/native-browser-replay.json'));
 const traceRun=JSON.parse(traceBytes).runs.find(run=>run.classId==='reaver'&&run.seed===1);
 if(!traceRun)throw Error('Missing seed-1 Reaver replay');
 const firstMap=traceRun.trace.findIndex(row=>row.phase==='Map');
 if(firstMap<1)throw Error('Replay does not return to the map');
 const replay=traceRun.trace.slice(0,firstMap+1);
 const summaries=[];let sourceStamp;
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const desktopOnly=process.argv.slice(4).includes('--desktop-only');
 const cases=[{width:320,height:640},{width:390,height:844},{width:412,height:915},{width:1440,height:900},{width:390,height:844,sealstone:true}];
 for(const config of cases.filter(row=>!desktopOnly||row.width===1440)){
  const viewport={width:config.width,height:config.height},sealstone=!!config.sealstone;
  const phone=viewport.width<500,context=await browser.newContext({viewport,deviceScaleFactor:phone?2:1,hasTouch:phone});
  let activeViewport=viewport;const replayed=[];
  const page=await context.newPage();ui=new NativeUiDriver(page,path.join(output,viewport.width+'x'+viewport.height+(sealstone?'-sealstone':'')));map=new MapObserver(page,ui);
  const settled=async()=>{await page.waitForTimeout(450);await ui.frames();};
  const getBounds=async rect=>{const canvas=await page.locator('#unity-canvas').boundingBox();return {x:canvas.x+rect.x*canvas.width/ui.controls.PanelWidth,y:canvas.y+rect.y*canvas.height/ui.controls.PanelHeight,width:rect.width*canvas.width/ui.controls.PanelWidth,height:rect.height*canvas.height/ui.controls.PanelHeight};};
  const control=async id=>{
   await ui.until(()=>ui.has(id),'map control '+id);
   const c=ui.controls.Controls.find(row=>row.Id===id),rect=await getBounds({x:c.X,y:c.Y,width:c.Width,height:c.Height});
   ui.check(inside(rect,{x:0,y:0,...activeViewport},1),'control is on screen: '+id);
   if(id.startsWith('native-route-'))ui.check(inside(rect,await getBounds(map.value.viewport),1),'route is inside clipped map viewport: '+id);
   await page.mouse.move(rect.x+rect.width/2,rect.y+rect.height/2);await ui.frames();await page.mouse.down();await page.waitForTimeout(140);await page.mouse.up();await settled();
  };
  const mapControl=async id=>{const before=map.revision;await control('native-map-'+id);await ui.until(()=>map.revision>before,'map response '+id);};
  const snapshot=()=>JSON.stringify(ui.state);
  const unchanged=(before,label)=>ui.check(snapshot()===before&&ui.state.phase==='Map',label);
  const wheel=async()=>{const rect=await getBounds(map.value.viewport),before=map.value.camera.scrollTop,revision=map.revision;await page.mouse.move(rect.x+rect.width/2,rect.y+rect.height/2);await page.mouse.wheel(0,before>30?-180:180);await ui.until(()=>map.revision>revision,'map wheel receipt');await settled();ui.check(Math.abs(map.value.camera.scrollTop-before)>1,'wheel moves the map camera');};
  const inspectTargets=async label=>{
   const value=map.value,canvas=await page.locator('#unity-canvas').boundingBox(),bounds=await getBounds(value.viewport);
   ui.check(value.scope==='solo',label+': solo map receipt');
   ui.check(bounds.width>0&&bounds.height>0&&inside(bounds,{x:0,y:0,...activeViewport},1),label+': bounded map viewport is on screen');
   const nodeIds=new Set(value.nodes.map(n=>n.id)),controls=ui.controls.Controls.filter(c=>c.Id.startsWith('native-route-'));
   ui.check(controls.length===nodeIds.size&&controls.every(c=>nodeIds.has(c.Id.slice('native-route-'.length))),label+': no hidden node controls');
   const legal=value.nodes.filter(n=>n.legal);
   ui.check(legal.length===ui.state.legalNodes.length&&legal.every(n=>ui.state.legalNodes.includes(n.id)),label+': board choices match authoritative legal routes');
   for(const node of legal){ui.check(node.width*canvas.width/ui.controls.PanelWidth>=43.5&&node.height*canvas.height/ui.controls.PanelHeight>=43.5,label+': 44 CSS-pixel route '+node.id);}
   ui.check(legal.every((node,index)=>legal.slice(index+1).every(other=>!overlaps(node,other))),label+': reachable targets do not overlap');
   if(value.decisionFits)ui.check(legal.every(n=>inside(n,value.viewport,1)),label+': claimed fit contains every decision target');
   else ui.check(ui.has('native-map-routes'),label+': non-fitting decision has Routes fallback');
   map.capture(label);
  };
  await ui.open(url);const initialBuild=fs.readFileSync(path.join(ui.output,'build-source.json'));
  if(sourceStamp)ui.check(initialBuild.equals(sourceStamp),'all viewports use the same source receipt');else sourceStamp=initialBuild;
  const finish=async()=>{
   const latest=await page.request.get(new URL('build-source.json',url).href);ui.check(latest.ok()&&(await latest.body()).equals(initialBuild),'served player stayed source-matched');
   ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);fs.writeFileSync(path.join(ui.output,'map-views.json'),JSON.stringify(map.receipts,null,2));
   summaries.push({viewport,sealstone,checks:ui.checks.length,touchCancel:phone&&!sealstone,physicalDevice:false});console.log('Map viewport passed: '+viewport.width+'x'+viewport.height+(sealstone?' Sealstone':'')+' ('+ui.checks.length+' checks)');await context.close();
  };
  await ui.click('native-new');await ui.click('foundation-mode-standard');await ui.fill('native-seed',sealstone?'BA':'1');await ui.command('native-begin');
  await ui.until(()=>map.value?.scope==='solo','initial map observer');await settled();
  ui.check(ui.state.phase==='Map'&&map.value.mode==='fog','new profile defaults to solo fog');
  if(sealstone){
   // Actual domain-command oracle: work/MapRoutes/.../verified-395.json,
   // SHA-256 65f5a42539f89028c393cdba038fb60002de5b3d2c7215489a948761a9bfef7e.
   // BA is base-35 seed 395. The receipt is evidence, not injected gameplay.
   ui.check(ui.state.run.seed===395,'BA creates the verified numeric seed395');
   await inspectTargets('sealstone-entrance');await control('native-route-n1_3');await ui.until(()=>ui.state.phase==='Combat','Sealstone opening fight');
   for(const [instance,target,phase] of[['starting:0','e2','Combat'],['starting:3','e1','Combat'],['starting:2','e1','Rewards']]){
    ui.check(ui.state.hand.some(card=>card.instanceId===instance),'Sealstone trace card is in actual hand: '+instance);
    await ui.click('native-target-'+target);while(!ui.has('native-card-'+instance)&&ui.has('native-hand-next'))await ui.click('native-hand-next');
    await ui.click('native-card-'+instance);await ui.command('native-play');ui.check(ui.state.phase===phase&&ui.state.player.hp===64,'verified Sealstone fight command: '+instance+' to '+target);
   }
   await ui.command('native-reward-cinders');ui.check(ui.state.room.states.cinders==='taken','opening cinders are actually claimed');await ui.command('native-rewards-continue');
   await ui.until(()=>map.value?.camera.nodeId==='n1_3','Sealstone unknown-route decision');await settled();await mapControl('fit');
   ui.check(ui.state.legalNodes.includes('n2_2')&&map.value.nodes.some(n=>n.id==='n2_2'&&n.type==='event'&&!n.revealed),'next treasure remains Unknown before Sealstone ownership');
   await inspectTargets('sealstone-before-unknown');await ui.shot('01-before-unknown');
   const future={n4_3:'shrine',n5_5:'shrine',n9_0:'treasure',n9_2:'fight'};
   await mapControl('mode');ui.check(Object.keys(future).every(id=>map.value.nodes.some(n=>n.id===id&&n.type==='event'&&!n.revealed)),'all-paths future outcomes remain Unknown before the claim');map.capture('sealstone-before-paths');await mapControl('mode');
   await control('native-route-n2_2');await ui.until(()=>ui.state.phase==='Rewards','Unknown treasure reward room');
   ui.check(ui.state.room.rewards.relicId==='sealstoneKey','actual Unknown room offers Sealstone Key');await ui.shot('02-sealstone-offer');
   await ui.command('native-reward-relic');ui.check(ui.state.room.states.relic==='taken','Sealstone reward is accepted and marked taken');await ui.command('native-rewards-continue');
   await ui.until(()=>map.value?.camera.nodeId==='n2_2','post-claim revealed map');await settled();await mapControl('fit');
   ui.check(ui.state.player.hp===64&&ui.state.legalNodes.length===2&&['n3_3','n3_2'].every(id=>ui.state.legalNodes.includes(id)),'verified Sealstone route keeps HP and reaches authored next choices');
   ui.check(map.value.mode==='fog'&&map.value.nodes.some(n=>n.id==='n2_2'&&n.current&&n.visited&&n.revealed&&n.type==='treasure'),'claimed Key reveals the current treasure in fog');
   ui.check(Object.keys(future).every(id=>!map.value.nodes.some(n=>n.id===id)),'Key does not expose future hidden fog nodes');
   await inspectTargets('sealstone-revealed-fog');await ui.shot('03-revealed-fog');const fog=snapshot(),fogIds=map.value.nodes.map(n=>n.id).sort();
   await mapControl('mode');ui.check(Object.entries(future).every(([id,type])=>map.value.nodes.some(n=>n.id===id&&n.type===type&&n.revealed&&!n.visited&&!n.current)),'Paths reveals each verified future Unknown outcome after the claim');
   await inspectTargets('sealstone-revealed-paths');await ui.shot('04-revealed-paths');unchanged(fog,'revealed Paths is a display change only');
   await mapControl('mode');ui.check(JSON.stringify(map.value.nodes.map(n=>n.id).sort())===JSON.stringify(fogIds),'returning to fog restores the same hidden-node boundary');
   ui.check(!ui.state.routes.some(n=>'resolved' in n),'Sealstone native route diagnostics still omit raw resolution records');
   fs.writeFileSync(path.join(ui.output,'sealstone-receipt.json'),JSON.stringify({seedText:'BA',seed:395,oracleSha256:'65f5a42539f89028c393cdba038fb60002de5b3d2c7215489a948761a9bfef7e',ownershipEvidence:'Accepted sealstoneKey reward and resulting reveal projection; owned relic array is not exposed by diagnostics.',physicalDevice:false},null,2));
   await finish();continue;
  }
  const initial=snapshot(),fogIds=map.value.nodes.map(n=>n.id),fogEdges=map.value.edges;
  ui.check(!ui.state.routes.some(n=>'resolved' in n),'native route diagnostics omit hidden resolutions');
  await inspectTargets('initial-fog');await ui.shot('01-initial-fog');
  await mapControl('mode');ui.check(map.value.mode==='path','mode button shows all paths');unchanged(initial,'changing visibility does not mutate the run');
  ui.check(map.value.nodes.length>fogIds.length&&fogIds.every(id=>map.value.nodes.some(n=>n.id===id)),'fog excludes future nodes that paths can show');
  ui.check(fogEdges.every(e=>fogIds.includes(e.from)&&fogIds.includes(e.to)),'fog edges connect only visible nodes');
  await inspectTargets('all-paths');await ui.shot('02-all-paths');
  await mapControl('glow');ui.check(map.value.shrineGlow===false,'shrine highlight preference can be disabled');unchanged(initial,'highlight toggle does not mutate the run');
  await mapControl('legend');ui.check(ui.has('native-map-close'),'legend opens a dismissible overlay');
  const coveredRoutes=ui.controls.Controls.filter(c=>c.Id.startsWith('native-route-'));
  ui.check(coveredRoutes.length>0&&coveredRoutes.every(c=>!c.Enabled),'legend disables every underlying node control');
  await ui.shot('03-legend');await mapControl('close');
  await mapControl('routes');ui.check(ui.state.legalNodes.every(id=>ui.has('native-map-choice-'+id)),'Routes lists all authoritative choices');await ui.shot('04-routes');await mapControl('close');unchanged(initial,'opening and closing map overlays never travels');
  for(let step=0;step<4;step++)await mapControl('zoom-in');
  await wheel();unchanged(initial,'wheel camera movement never travels');
  await mapControl('recenter');
  const legal=map.value.nodes.find(n=>n.legal&&inside(n,map.value.viewport,1));
  ui.check(!!legal,'recenter exposes a legal drag target');
  const nodeRect=await getBounds(legal),viewRect=await getBounds(map.value.viewport),x=nodeRect.x+nodeRect.width/2,y=nodeRect.y+nodeRect.height/2;
  const endY=y-viewRect.y>60?y-50:Math.min(viewRect.y+viewRect.height-3,y+50);
  await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x,endY,{steps:12});await page.waitForTimeout(140);await page.mouse.up();await settled();
  unchanged(initial,'drag starting on a legal node suppresses travel');
  await mapControl('recenter');
  const outsideNode=map.value.nodes.find(n=>n.legal&&inside(n,map.value.viewport,1));ui.check(!!outsideNode,'outside-release start is visible');
  const outsideStart=await getBounds(outsideNode),outsideViewport=await getBounds(map.value.viewport);
  await page.mouse.move(outsideStart.x+outsideStart.width/2,outsideStart.y+outsideStart.height/2);await page.mouse.down();
  await page.mouse.move(outsideStart.x+outsideStart.width/2,outsideViewport.y-5,{steps:12});await page.mouse.up();await settled();
  unchanged(initial,'releasing a map gesture outside the viewport never travels');
  if(phone){
   await mapControl('recenter');const n=map.value.nodes.find(row=>row.legal&&inside(row,map.value.viewport,1));ui.check(!!n,'cancellation target is visible');
   const r=await getBounds(n),cdp=await context.newCDPSession(page),touch={x:r.x+r.width/2,y:r.y+r.height/2,id:1,radiusX:1,radiusY:1,force:1};
   await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[touch]});await page.waitForTimeout(120);
   await cdp.send('Input.dispatchTouchEvent',{type:'touchCancel',touchPoints:[]});await settled();await cdp.detach();
   unchanged(initial,'browser touch cancellation suppresses travel');
  }
  await mapControl('zoom-out');ui.check(map.value.camera.zoom<2,'zoom-out reduces manual zoom');
  await mapControl('fit');ui.check(map.value.camera.setting==='Fit','Fit restores automatic framing');
  const fitted={...map.value.camera};await wheel();await mapControl('recenter');ui.check(sameCamera(fitted,map.value.camera),'Recenter restores the measured decision camera');
  if(!phone){
   activeViewport={width:1280,height:800};const beforeResize=map.revision;await page.setViewportSize(activeViewport);await ui.until(()=>map.revision>beforeResize,'same-aspect resize map receipt');await settled();
   for(let step=0;step<8&&map.value.camera.zoom>1.00001;step++){
    const previousZoom=map.value.camera.zoom;await mapControl('zoom-out');await ui.until(()=>map.value.camera.zoom<previousZoom,'observed smaller desktop zoom');
   }
   ui.check(Math.abs(map.value.camera.zoom-1)<.00001,'resized desktop explicitly reaches minimum zoom100%');
   await inspectTargets('desktop-resized-minimum-zoom');unchanged(initial,'same-aspect resize preserves native run');await ui.shot('10-desktop-resized');
   activeViewport=viewport;const beforeRestore=map.revision;await page.setViewportSize(viewport);await ui.until(()=>map.revision>beforeRestore,'desktop original size restored');await settled();await mapControl('fit');
  }
  await mapControl('zoom-in');await wheel();const saved={mode:map.value.mode,glow:map.value.shrineGlow,camera:{...map.value.camera}};
  unchanged(initial,'all viewer gestures leave native gameplay unchanged');await ui.shot('05-saved-camera');map.capture('saved-camera');
  await ui.click('native-deck');ui.check(!ui.has('native-map-fit'),'deck replaces the map surface');await ui.shot('06-deck');
  let revision=map.revision;await ui.click('native-deck-back');await ui.until(()=>map.revision>revision,'remounted map');await settled();
  ui.check(map.value.mode===saved.mode&&map.value.shrineGlow===saved.glow&&sameCamera(saved.camera,map.value.camera),'deck remount restores local camera and preferences');unchanged(initial,'deck remount leaves the run unchanged');
  await ui.click('native-menu');map.reset();ui.controls=null;ui.state=null;await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.until(()=>ui.has('native-continue'),'saved run Continue');await ui.command('native-continue');await ui.until(()=>!!map.value,'restored map');await settled();
  ui.check(map.value.mode===saved.mode&&map.value.shrineGlow===saved.glow&&sameCamera(saved.camera,map.value.camera),'page reload restores local camera, mode and shrine preference');unchanged(initial,'page reload restores exact native run diagnostics');await ui.shot('07-restored-camera');map.capture('restored-camera');
  await mapControl('fit');await inspectTargets('final-fit');
  const choice=map.value.nodes.find(n=>n.legal&&inside(n,map.value.viewport,1));const routeId=choice?.id||ui.state.legalNodes[0];revision=ui.revision;
  ui.check(!!choice,'Fit permits an actual map-node tap without driver route fallback');
  if(phone){
   const rect=await getBounds(choice),cdp=await context.newCDPSession(page);
   ui.check(inside(rect,{x:0,y:0,...activeViewport},1)&&inside(choice,map.value.viewport,1),'fresh touch route is visible inside the map');
   await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:rect.x+rect.width/2,y:rect.y+rect.height/2,id:1,radiusX:1,radiusY:1,force:1}]});await page.waitForTimeout(140);
   await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await settled();await cdp.detach();
  }else await control('native-route-'+routeId);
  await ui.until(()=>ui.revision>revision&&ui.state.phase!=='Map','actual route transition');
  ui.check(ui.state.run.mapNodeId===routeId&&ui.state.run.path.at(-1)===routeId,'actual route tap enters the selected authoritative node');await ui.shot('08-entered-route');
  const matches=row=>ui.state.phase===row.phase&&ui.state.player.hp===row.hp&&ui.state.player.mana===row.mana&&ui.state.player.stamina===row.stamina&&ui.state.turn===row.turn&&ui.state.act===row.act;
  ui.check(replay[0].command==='enter:'+routeId&&matches(replay[0]),'first room matches the committed seed-1 replay');
  replayed.push({command:replay[0].command,phase:ui.state.phase,hp:ui.state.player.hp,mana:ui.state.player.mana,stamina:ui.state.player.stamina});
  for(const action of replay.slice(1)){
   ui.check(ui.state.phase===action.beforePhase&&ui.state.player.hp===action.beforeHp,'replay precondition: '+action.command);
   const [kind,...parts]=action.command.split(':'),value=parts.join(':');
   if(kind==='play'){
    ui.check(ui.state.hand.some(card=>card.instanceId===action.instanceId),'authored replay card is actually in hand: '+action.instanceId);
    await ui.click('native-target-'+action.targetId);
    while(!ui.has('native-card-'+action.instanceId)&&ui.has('native-hand-next'))await ui.click('native-hand-next');
    await ui.click('native-card-'+action.instanceId);await ui.command('native-play');
   }else if(kind==='endTurn')await ui.command('native-end-turn');
   else if(kind==='reward')await ui.command('native-reward-'+value+(value==='card'?'-'+action.cardId:''));
   else if(kind==='continueRewards')await ui.command('native-rewards-continue');
   else throw Error('Unexpected first-fight replay command: '+action.command);
   ui.check(matches(action),'HP, MP, stamina, turn and phase match committed replay: '+action.command);
   replayed.push({command:action.command,phase:ui.state.phase,hp:ui.state.player.hp,mana:ui.state.player.mana,stamina:ui.state.player.stamina});
  }
  await ui.until(()=>map.value?.camera.nodeId===routeId,'post-fight current-node map');await settled();await mapControl('fit');
  ui.check(ui.state.phase==='Map'&&ui.state.legalNodes.length>=2,'real first-fight victory returns to a branching decision');
  await inspectTargets('branching-decision');await ui.shot('09-branching');
  fs.writeFileSync(path.join(ui.output,'first-fight-replay.json'),JSON.stringify({fixture:'UnityTests/Parity/native-browser-replay.json',fixtureSha256:crypto.createHash('sha256').update(traceBytes).digest('hex'),commands:replayed},null,2));
  await finish();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,selection:desktopOnly?'desktop-only':'full-five-cases',viewports:summaries,checks:summaries.reduce((n,row)=>n+row.checks,0),physicalDevice:false,cooperativeBrowserProof:false},null,2));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);if(map)fs.writeFileSync(path.join(ui.output,'map-views.json'),JSON.stringify({receipts:map.receipts,last:map.value},null,2));}if(browser)await browser.close();process.exitCode=1;});
