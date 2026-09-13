// Touch-sized compiled-game acceptance for custom drafting and paid room services.
const path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,ui;
(async()=>{
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:2});
 ui=new NativeUiDriver(page,path.resolve(process.argv[3]||'TestResults/NativeFeatures'));
 await ui.open(process.argv[2]);await ui.click('native-new');ui.check(!ui.has('native-begin'),'unspent points cannot start a run');
 ui.check(!ui.controls.Controls.some(c=>c.Id.includes('tuned')),'Tuned is absent');await ui.click('foundation-mode-standard');
 await ui.fill('native-seed','1');await ui.click('native-custom-toggle');await ui.choose('native-deck-mode',2);await ui.click('native-mod-hoarder');await ui.shot('01-custom-draft-setup');
 await ui.command('native-begin');ui.check(ui.state.phase==='Draft','Draft starts before the map');ui.check(ui.state.run.custom.deckMode==='draft','chosen deck mode retained');
 const pickDraft=async()=>{let choice;await ui.until(()=>{choice=ui.controls?.Controls.find(c=>c.Id.startsWith('native-draft-')&&c.Enabled);return !!choice;},'rendered draft offers');await ui.command(choice.Id);};
 await pickDraft();
 const draft=JSON.stringify(ui.state);await ui.click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===draft,'draft offers and selected cards survive page reload');
 while(ui.state.phase==='Draft')await pickDraft();
 ui.check(ui.state.phase==='Map'&&ui.state.run.deck.length===10,'three choices complete the ten-card drafted deck');await ui.shot('02-drafted-map');
 let shrine=false,shop=false,commands=0;
 while(!(shrine&&shop)&&commands++<330){const s=ui.state;
  if(s.phase==='Map'){
   const priorities={shrine:shrine?5:0,merchant:shop?4:1,treasure:2,event:3,monster:4,fight:4,elite:6,boss:7};
   const route=[...s.routes].sort((a,b)=>(priorities[a.type]??8)-(priorities[b.type]??8))[0];await ui.command('native-route-'+route.id);
  }else if(s.phase==='Combat'){
   const target=s.enemies.filter(e=>e.alive).sort((a,b)=>a.hp-b.hp)[0];
   if(s.player.hp<s.player.maxHp*.45&&s.player.flaskCharges.hpCurrent>0){await ui.command('native-crimson');continue;}
   const score=row=>row.card.effects.reduce((n,e)=>n+(e.op==='damage'?(typeof e.amount==='number'?e.amount:5)*(e.hits||1)+(e.attributeBonus||0):e.op==='applyStatus'?5:e.op==='block'?2:e.op==='draw'?3:e.op==='heal'?3:1),0);
   const playable=s.cards.filter(r=>(r.cost.variable||r.cost.action<=s.player.energy)&&r.cost.mana<=s.player.mana&&r.cost.stamina<=s.player.stamina&&!['status','curse'].includes(r.card.type)).sort((a,b)=>score(b)-score(a));
   if(playable.length){const row=playable[0],id='native-card-'+row.instance.instanceId;await ui.click('native-target-'+target.id);for(let page=0;page<8&&!ui.has(id)&&ui.has('native-hand-next');page++)await ui.click('native-hand-next');await ui.click(id);await ui.command('native-play');}
   else if(s.cards.some(r=>r.cost.mana>s.player.mana)&&s.player.flaskCharges.manaCurrent>0&&s.player.energy>0)await ui.command('native-azure');
   else await ui.command('native-end-turn');
  }else if(s.phase==='Rewards'){
   const reward=ui.controls.Controls.find(c=>c.Id.startsWith('native-reward-')&&c.Enabled&&!c.Id.startsWith('native-reward-card-'));
   if(reward)await ui.command(reward.Id);else await ui.command('native-rewards-continue');
  }else if(s.phase==='Shrine'){
   if(!shrine){const before=JSON.stringify(s.run);await ui.click('native-level-up');await ui.click('native-level-up-constitution');await ui.click('native-level-cancel');ui.check(JSON.stringify(ui.state.run)===before,'cancelled level purchase preserves attributes and cinders');
    const con=ui.state.run.attributes.constitution,hp=ui.state.player.maxHp,cinders=ui.state.run.cinders;await ui.click('native-level-up');await ui.click('native-level-up-constitution');await ui.command('native-level-confirm');
    ui.check(ui.state.run.attributes.constitution===con+1&&ui.state.player.maxHp===hp+2&&ui.state.run.cinders<cinders,'one CON point purchases real HP improvement');await ui.command('native-flask-split-0');ui.check(ui.state.player.flaskCharges.hp===0,'flask allocation can dedicate every charge to MP');await ui.command('native-flask-split-3');await ui.shot('03-shrine-level-and-flasks');shrine=true;}
   await ui.command('native-rest');
  }else if(s.phase==='Shop'){
   if(!shop){const buy=ui.controls.Controls.find(c=>c.Enabled&&c.Id.startsWith('native-buy-relics-'))||ui.controls.Controls.find(c=>c.Enabled&&c.Id.startsWith('native-buy-flasks-'));ui.check(!!buy,'merchant offers an affordable item');const before=s.run.cinders;await ui.command(buy.Id);ui.check(ui.state.run.cinders<before,'merchant purchase spends cinders');
    const sale=ui.controls.Controls.find(c=>c.Enabled&&c.Id.startsWith('native-sell-'));ui.check(!!sale,'purchased item can be offered for resale');const purse=ui.state.run.cinders;await ui.command(sale.Id);ui.check(ui.state.run.cinders>purse,'resale returns actual cinders');await ui.shot('04-merchant-resale');shop=true;}
   await ui.command('native-shop-leave');
  }else if(s.phase==='Event'){const choice=ui.controls.Controls.filter(c=>c.Enabled&&c.Id.startsWith('native-choice-'));const safe=choice.find(c=>/leave|decline|ignore|walk|refuse/.test(c.Id))||choice[choice.length-1];await ui.command(safe.Id);}
  else if(s.phase==='EventResult')await ui.command('native-event-leave');
  else throw Error('Unexpected phase before room acceptance: '+s.phase);
  if(commands%20===0)console.log('Native feature journey: '+commands+' actions, '+ui.state.phase);
 }
 ui.check(shrine&&shop,'drafted climb reaches and uses both paid service rooms');
 const current=JSON.stringify(ui.state);await ui.click('native-menu');await page.reload();await page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await ui.command('native-continue');ui.check(JSON.stringify(ui.state)===current,'purchases and custom state persist through reload');
 ui.check(ui.errors.length===0,'no browser or Unity errors');ui.save(true);console.log('Native feature checks passed: '+ui.checks.length);await browser.close();
})().catch(async e=>{console.error(e);if(ui){ui.errors.push(e.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
