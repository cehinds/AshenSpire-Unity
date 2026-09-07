// Compiled Unity enemy-art acceptance through real pointer and keyboard input.
// Usage: node tools/native-enemy-art-playtest.cjs <Web URL> [evidence directory]
// Diagnostics only observe loaded assets/state/feedback; no game or save injection.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
const art=[],feedback=[],selections=[],captures=[];
let captureImpact=false;
(async()=>{
 const url=process.argv[2];if(!url)throw Error('Pass the compiled Web URL as the first argument.');
 const catalog=JSON.parse(fs.readFileSync(path.join(__dirname,'../GameContent/Unity/Original/enemy-art.json'),'utf8')).enemies;
 const output=path.resolve(process.argv[3]||'TestResults/NativeEnemyArt');
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const context=await browser.newContext({viewport:{width:390,height:844},deviceScaleFactor:2});
 const page=await context.newPage();ui=new NativeUiDriver(page,output);
 page.on('console',message=>{
  const text=message.text();
  for(const prefix of ['ASHENSPIRE_ENEMY_ART ','ASHENSPIRE_FEEDBACK ']){
   const at=text.indexOf(prefix);if(at<0)continue;
   try{
    const row=JSON.parse(text.slice(at+prefix.length));
    if(prefix.includes('ENEMY_ART'))art.push(row);
    else {
     feedback.push(row);
     if(captureImpact&&row.Cue==='attack'&&row.Status==='impact'){
      captureImpact=false;
      captures.push(page.screenshot({path:path.join(output,'07-phone-attack-impact.png')}).catch(error=>ui.errors.push('Impact screenshot: '+error.message)));
     }
    }
   }catch(error){ui.errors.push('Invalid enemy-art/feedback observer JSON: '+error.message);}
  }
 });
 const top=async()=>{
  const canvas=await page.locator('#unity-canvas').boundingBox();
  await page.mouse.move(canvas.x+canvas.width*.92,canvas.y+canvas.height*.5);
  await page.mouse.wheel(0,-5000);await page.waitForTimeout(350);await ui.frames();
 };
 const validateArt=row=>{
  ui.check(!!catalog[row.enemyId],'loaded enemy belongs to authored catalog: '+row.enemyId);
  ui.check(row.resource===catalog[row.enemyId].resource,'loaded catalog texture: '+row.enemyId+' = '+row.resource);
  ui.check(!row.resource.startsWith('Original/enemy_'),'no legacy enemy texture fallback: '+row.enemyId);
  ui.check(Number.isInteger(row.width)&&row.width>0&&Number.isInteger(row.height)&&row.height>0,'loaded texture has real dimensions: '+row.enemyId);
 };
 const selectAll=async(viewport,ordinal)=>{
  const enemies=ui.state.enemies.filter(enemy=>enemy.alive);
  for(let n=0;n<enemies.length;n++){
   const enemy=enemies[n],before=JSON.stringify(ui.state),first=art.length;
   await ui.click('native-target-'+enemy.id);
   const row=art.slice(first).at(-1);
   if(row){ui.check(row.enemyId===enemy.enemyId,'selected body reports selected enemy '+enemy.id+' on '+viewport);validateArt(row);}
   ui.check(!!catalog[enemy.enemyId]&&!catalog[enemy.enemyId].resource.startsWith('Original/enemy_'),'selected enemy has a painted catalog binding: '+enemy.enemyId);
   ui.check(JSON.stringify(ui.state)===before,'target selection does not mutate combat '+enemy.id+' on '+viewport);
   selections.push({viewport,instanceId:enemy.id,enemyId:enemy.enemyId,expectedResource:catalog[enemy.enemyId].resource,observedAsset:row||null});await top();
   await ui.shot(String(ordinal+n).padStart(2,'0')+'-'+viewport+'-'+enemy.id+'-'+enemy.enemyId);
  }
 };
 await ui.open(url);const initialBuild=fs.readFileSync(path.join(output,'build-source.json'));
 const stamp=JSON.parse(initialBuild);
 ui.check(typeof stamp.version==='string'&&/^\d+\.\d+\.\d+\.\d+$/.test(stamp.version),'served build has a four-part version: '+stamp.version);
 ui.check(Number.isInteger(stamp.buildNumber)&&stamp.buildNumber>=11,'testing an enemy-art-capable build: '+stamp.buildNumber+' / version '+stamp.version);
 await ui.click('native-new');await ui.click('foundation-mode-standard');await ui.fill('native-seed','1');await ui.command('native-begin');
 ui.check(ui.state.phase==='Map','Standard seed 1 starts on the native map');
 for(let n=0;n<12&&ui.state.phase!=='Combat';n++){
  if(ui.state.phase==='Map'){
   const route=ui.state.routes.find(row=>['fight','monster'].includes(row.type))||ui.state.routes[0];
   ui.check(!!route,'reachable route exists');await ui.command('native-route-'+route.id);
  }else if(ui.state.phase==='Event'){
   const choices=ui.controls.Controls.filter(row=>row.Enabled&&row.Id.startsWith('native-choice-'));
   const choice=choices.find(row=>/leave|decline|ignore|walk|refuse/.test(row.Id))||choices.at(-1);
   ui.check(!!choice,'authored event has a selectable choice');await ui.command(choice.Id);
  }else if(ui.state.phase==='EventResult')await ui.command('native-event-leave');
  else throw Error('Unexpected phase before opening combat: '+ui.state.phase);
 }
 ui.check(ui.state.phase==='Combat','normal route enters actual combat');
 const initialEnemies=ui.state.enemies.filter(enemy=>enemy.alive);
 ui.check(initialEnemies.length===3,'seed 1 first fight has three living enemy instances');
 ui.check(initialEnemies.some(enemy=>enemy.enemyId==='blightHound')&&initialEnemies.some(enemy=>enemy.enemyId==='graveWisp'),'opening fight exercises hound and wisp silhouettes');
 await selectAll('phone',1);
 const beforeResize=JSON.stringify(ui.state),layout=ui.layout;
 await page.setViewportSize({width:1440,height:900});await ui.until(()=>ui.layout>layout,'desktop responsive layout');await ui.frames();
 ui.check(JSON.stringify(ui.state)===beforeResize,'desktop resize preserves combat state');await selectAll('desktop',4);
 const desktopCanvas=await page.locator('#unity-canvas').boundingBox();ui.check(desktopCanvas.width>390,'desktop screenshot uses a wider actual canvas');
 const desktopLayout=ui.layout;await page.setViewportSize({width:390,height:844});await ui.until(()=>ui.layout>desktopLayout,'phone responsive layout');
 let attack;
 for(let turns=0;turns<4&&!attack;turns++){
  attack=ui.state.cards.find(row=>row.card.effects?.some(effect=>effect.op==='damage')&&
   (row.cost.variable||row.cost.action<=ui.state.player.energy)&&row.cost.mana<=ui.state.player.mana&&row.cost.stamina<=ui.state.player.stamina);
  if(!attack){await ui.command('native-end-turn');ui.check(ui.state.phase==='Combat','combat remains active while drawing a legal attack');}
 }
 ui.check(!!attack,'real hand supplies an affordable damage card');
 const target=ui.state.enemies.filter(enemy=>enemy.alive).sort((a,b)=>b.hp-a.hp)[0];await ui.click('native-target-'+target.id);
 const cardId='native-card-'+attack.instance.instanceId;
 while(ui.has('native-hand-prev'))await ui.click('native-hand-prev');
 while(!ui.has(cardId)&&ui.has('native-hand-next'))await ui.click('native-hand-next');
 await ui.click(cardId);ui.check(ui.has('native-play'),'selected legal attack enables Play');await top();
 const beforeAttack=JSON.parse(JSON.stringify(ui.state)),firstFeedback=feedback.length;captureImpact=true;
 await ui.command('native-play');
 await ui.until(()=>feedback.slice(firstFeedback).some(row=>row.Cue==='attack'&&row.Status==='completed'),'real attack feedback completes');
 await Promise.all(captures);const attackFeedback=feedback.slice(firstFeedback);
 ui.check(attackFeedback.some(row=>row.Cue==='attack'&&row.Status==='impact'),'real paid attack reaches impact feedback');
 ui.check(captures.length===1,'captured actual attack impact frame');
 ui.check(ui.state.enemies.reduce((sum,enemy)=>sum+enemy.hp,0)<beforeAttack.enemies.reduce((sum,enemy)=>sum+enemy.hp,0),'real attack reduces enemy HP');
 ui.check(JSON.stringify(ui.state)!==JSON.stringify(beforeAttack),'accepted attack changes authoritative native state');
 ui.check(ui.state.phase==='Combat','opening attack leaves combat available for reload acceptance');
 await top();await ui.shot('08-phone-after-attack');
 const saved=JSON.stringify(ui.state),artBeforeReload=art.length;
 await ui.click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.command('native-continue');
 ui.check(JSON.stringify(ui.state)===saved,'page reload restores exact native run, combat, resources and RNG view');
 if(art.length>artBeforeReload)validateArt(art.at(-1));await top();await ui.shot('09-phone-reloaded');
 if(art.length)ui.check(art.every(row=>catalog[row.enemyId]?.resource===row.resource&&!row.resource.startsWith('Original/enemy_')),'every observed enemy render uses catalog art across targeting, attack and reload');
 const latest=await page.request.get(new URL('build-source.json',url).href);ui.check(latest.ok()&&Buffer.compare(await latest.body(),initialBuild)===0,'served build receipt remained unchanged throughout acceptance');
 ui.check(ui.errors.length===0,'no browser or Unity errors');
 fs.writeFileSync(path.join(output,'enemy-art.json'),JSON.stringify({sourceDigest:stamp.sourceDigest,version:stamp.version,buildNumber:stamp.buildNumber,assetObserverAvailable:art.length>0,assetEvidence:art.length?'Runtime asset reports are included.':'Release player omits asset reports. Catalog paths are expectations only; inspect screenshots for rendered art.',selections,observed:art,feedback:attackFeedback,physicalDevice:false,allCatalogEnemiesExercised:false},null,2));
 ui.save(true);console.log('Native enemy-art checks passed: '+ui.checks.length);await browser.close();
})().catch(async error=>{
 console.error(error);if(ui){ui.errors.push(error.stack);await Promise.all(captures);await ui.shot('failure').catch(()=>{});fs.writeFileSync(path.join(ui.output,'enemy-art-failure.json'),JSON.stringify({art,feedback,selections},null,2));ui.save(false);}
 if(browser)await browser.close();process.exitCode=1;
});
