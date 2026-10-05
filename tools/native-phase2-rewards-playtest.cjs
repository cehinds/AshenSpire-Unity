// One real fight, receipt-driven transitions and automatic rewards. No state injection.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 browser=await chromium.launch({headless:true,channel:'msedge',args:process.env.AS_BROWSER_GPU==='1'?[]:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page=await browser.newPage({viewport:{width:390,height:844}});
 ui=new NativeUiDriver(page,path.resolve(process.argv[3]||'TestResults/Phase2/Rewards'));
 const transitions=[];
 page.on('console',message=>{const text=message.text(),prefix='ASHENSPIRE_TRANSITION ',at=text.indexOf(prefix);if(at>=0)try{transitions.push(JSON.parse(text.slice(at+prefix.length)));}catch{}});
 await ui.open(process.argv[2]);await ui.click('settings');await ui.click('auto-rewards',false);
 await ui.click('settings-section-6',false);await ui.click('back');
 await ui.click('native-new');await ui.useStandard();await ui.fill('native-seed','1');await ui.command('native-begin');
 await ui.command('native-route-'+ui.state.routes.find(row=>['fight','monster'].includes(row.type)).id);
 for(let command=0;ui.state.phase==='Combat'&&command<120;command++){
  const s=ui.state,target=s.enemies.filter(e=>e.alive).sort((a,b)=>a.hp-b.hp)[0];
  if(s.player.hp<s.player.maxHp*.45&&s.player.flaskCharges.hpCurrent>0){await ui.command('native-crimson');continue;}
  const score=row=>row.card.effects.reduce((n,e)=>n+(e.op==='damage'?(typeof e.amount==='number'?e.amount:5)*(e.hits||1)+(e.attributeBonus||0):e.op==='applyStatus'?5:e.op==='block'?2:1),0);
  const playable=s.cards.filter(r=>(r.cost.variable||r.cost.action<=s.player.energy)&&r.cost.mana<=s.player.mana&&r.cost.stamina<=s.player.stamina&&!['status','curse'].includes(r.card.type)).sort((a,b)=>score(b)-score(a));
  if(playable.length){const row=playable[0],id='native-card-'+row.instance.instanceId;await ui.click('native-target-'+target.id);for(let p=0;p<8&&!ui.has(id)&&ui.has('native-hand-next');p++)await ui.click('native-hand-next');await ui.click(id);await ui.command('native-play');}
  else if(s.cards.some(r=>r.cost.mana>s.player.mana)&&s.player.flaskCharges.manaCurrent>0&&s.player.energy>0)await ui.command('native-azure');
  else await ui.command('native-end-turn');
 }
 ui.check(ui.state.phase==='Rewards','real opening encounter reaches rewards');
 await ui.until(()=>transitions.some(r=>r.action==='enemy.death'&&r.status==='completed'),'enemy death reaction completes');
 ui.check(transitions.filter(r=>r.action==='screen.enter'&&r.status==='completed').length>=3,'screen and room transitions complete in exported player');
 const offer=ui.state.room.rewards,before=structuredClone(ui.state.run);
 ui.check(ui.controls.Controls.some(c=>c.Id==='native-rewards-continue'&&c.Text==='Collect remaining rewards and continue'),'automatic reward preference changes Continue');
 await ui.shot('01-rewards');await ui.command('native-rewards-continue');
 ui.check(ui.state.phase==='Map','automatic reward Continue returns to map');
 ui.check(ui.state.run.cinders===before.cinders+offer.cinders,'automatic collection grants actual offered cinders');
 if(offer.cardIds?.length)ui.check(ui.state.run.deck.length===before.deck.length+1&&ui.state.run.deck.some(c=>!before.deck.some(b=>b.instanceId===c.instanceId)&&offer.cardIds.includes(c.cardId)),'seeded automatic card belongs to the offered choices');
 const saved=JSON.stringify(ui.state);await ui.click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.command('native-continue');
 ui.check(JSON.stringify(ui.state)===saved,'automatic rewards survive exact reload');
 await ui.shot('02-reloaded');ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);
 fs.writeFileSync(path.join(ui.output,'transitions.json'),JSON.stringify(transitions,null,2));
 console.log('Phase 2 rewards and transitions passed: '+ui.checks.length);await browser.close();
})().catch(async e=>{console.error(e);if(ui){ui.errors.push(e.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
