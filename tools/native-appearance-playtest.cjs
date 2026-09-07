// Compiled Unity Web appearance acceptance. Only pointer/keyboard game input.
// Copy beside tools/native-ui-driver.cjs; pass URL and optional output directory.
// Observer JSON supplies saved customization and rendered timeline diagnostics.
const fs=require('node:fs'),path=require('node:path');
const{chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const{NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const output=path.resolve(process.argv[3]||'TestResults/NativeAppearance');fs.mkdirSync(output,{recursive:true});
 const styles=['animated','rendered','classic','glyph'],tints=['gold','ember','frost','rot','grace'],sigils=['⚔','🛡','🔥','🌙','☀','🐺'];
 const summaries=[];
 for(let index=0;index<styles.length;index++){
  if(process.env.NATIVE_APPEARANCE_STYLE&&styles[index]!==process.env.NATIVE_APPEARANCE_STYLE)continue;
  const context=await browser.newContext({viewport:{width:390,height:844},deviceScaleFactor:2});const page=await context.newPage();
  ui=new NativeUiDriver(page,path.join(output,styles[index]));const feedback=[],pendingShots=[];
  page.on('console',message=>{const value=message.text(),prefix='ASHENSPIRE_FEEDBACK ',at=value.indexOf(prefix);if(at<0)return;try{const row=JSON.parse(value.slice(at+prefix.length));feedback.push(row);if(row.Status==='impact'&&pendingShots.length===0)pendingShots.push(page.screenshot({path:path.join(ui.output,'03-combat-impact.png')}));}catch(error){ui.errors.push(error.message);}});
  await ui.open(process.argv[2]);await ui.click('native-new');await ui.click('foundation-mode-standard');
  await ui.fill('native-name','Style '+styles[index]);
  // Exercise all original choices through actual dropdown interactions before saving.
  for(let n=0;n<tints.length;n++)await ui.choose('native-tint',n);
  for(let n=0;n<sigils.length;n++)await ui.choose('native-sigil',n);
  await ui.choose('native-tint',index+1);await ui.choose('native-sigil',index+1);await ui.choose('native-sprite-style',(index+1)%styles.length);await ui.choose('native-sprite-style',index);
  const canvas=await page.locator('#unity-canvas').boundingBox();await page.mouse.move(canvas.x+canvas.width*.9,canvas.y+canvas.height*.5);await page.mouse.wheel(0,-5000);await page.waitForTimeout(400);await ui.shot('01-creator-'+styles[index]);
  await ui.fill('native-seed','1');await ui.command('native-begin');
  const expected={name:'Style '+styles[index],spriteStyle:styles[index],tint:tints[index+1],glyph:sigils[index+1]};
  const identity=()=>Object.entries(expected).every(([key,value])=>ui.state.run.customization[key]===value);
  ui.check(identity(),'native run saves actual chosen style, tint, sigil and name: expected '+JSON.stringify(expected)+'; observed '+JSON.stringify(ui.state.run.customization));
  ui.check(ui.state.phase==='Map','standard Reaver starts on map');
  const route=ui.state.routes.find(row=>['fight','monster'].includes(row.type))||ui.state.routes[0];await ui.command('native-route-'+route.id);ui.check(ui.state.phase==='Combat','seed 1 opening route enters real combat');
  await ui.shot('02-combat-idle-'+styles[index]);
  const row=ui.state.cards.find(row=>row.card.type==='attack'&&row.cost.action<=ui.state.player.energy&&row.cost.mana<=ui.state.player.mana&&row.cost.stamina<=ui.state.player.stamina);ui.check(!!row,'opening hand has a legal attack');
  const target=ui.state.enemies.find(enemy=>enemy.alive);await ui.click('native-target-'+target.id);const id='native-card-'+row.instance.instanceId;while(!ui.has(id)&&ui.has('native-hand-next'))await ui.click('native-hand-next');await ui.click(id);feedback.length=0;await Promise.all(pendingShots);pendingShots.length=0;await ui.command('native-play');
  await ui.until(()=>feedback.some(row=>row.Status==='completed'),'appearance feedback completion');
  const impact=feedback.find(row=>row.Status==='impact');ui.check(impact?.SpriteStyle===styles[index],'combat feedback preserves selected renderer');
  ui.check(styles[index]==='animated'?/^attack[1-4]$/.test(impact.Pose):impact.Pose==='idle','only Animated swaps to an original attack pose');
  ui.check(feedback.find(row=>row.Status==='completed')?.Pose==='idle','feedback settles to idle without style replacement');
  await Promise.all(pendingShots);ui.check(identity(),'combat command retains saved identity');
  const saved=JSON.stringify(ui.state);await ui.click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.command('native-continue');
  ui.check(JSON.stringify(ui.state)===saved,'reload preserves exact game state and appearance');ui.check(identity(),'reloaded customization is unchanged');await ui.shot('04-reloaded-'+styles[index]);
  ui.check(ui.errors.length===0,'no browser or Unity errors');fs.writeFileSync(path.join(ui.output,'feedback.json'),JSON.stringify(feedback,null,2));ui.save(true);summaries.push({style:styles[index],checks:ui.checks.length});await context.close();
 }
 fs.writeFileSync(path.join(output,'summary.json'),JSON.stringify({passed:true,styles:summaries,physicalDevice:false},null,2));console.log('Native appearance checks passed: '+summaries.reduce((sum,row)=>sum+row.checks,0));await browser.close();
})().catch(async error=>{console.error(error);if(ui){ui.errors.push(error.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
