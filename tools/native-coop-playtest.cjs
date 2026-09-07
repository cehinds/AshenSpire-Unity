// Two real Unity players. Credentials come from a private local test-host file;
// they are typed into normal controls and never copied into reports or logs.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
let browser,players=[];
(async()=>{
 const output=path.resolve(process.argv[3]||'TestResults/NativeCoopBrowser'),credentials=JSON.parse(fs.readFileSync(process.argv[4],'utf8'));
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 for(let seat=0;seat<2;seat++){
  const context=await browser.newContext({viewport:{width:390,height:844},deviceScaleFactor:2});
  const ui=new NativeUiDriver(await context.newPage(),path.join(output,seat?'Guest':'Host'));players.push(ui);
  const wire=[];ui.page.on('websocket',socket=>{socket.on('framesent',frame=>{try{const message=JSON.parse(frame.payload);wire.push({direction:'sent',type:message.type,hello:message.type==='hello'?{inviteMatches:message.payload.joinToken===credentials.joinToken,hostMatches:message.payload.hostToken===credentials.hostToken,setup:message.payload.setup}:undefined});}catch{}});socket.on('framereceived',frame=>{try{const message=JSON.parse(frame.payload);wire.push({direction:'received',type:message.type,error:message.type==='error'?message.payload:undefined});}catch{}});socket.on('close',()=>fs.writeFileSync(path.join(ui.output,'wire-summary.json'),JSON.stringify(wire,null,2)));});
  await ui.open(process.argv[2]);await ui.click('native-coop');await ui.click('coop-create');await ui.click('foundation-mode-standard');await ui.fill('native-name',seat?'Guest':'Host');await ui.click('native-begin');
  await ui.fill('coop-endpoint',credentials.endpoint);await ui.fill('coop-invite',credentials.joinToken);if(!seat)await ui.fill('coop-host-key',credentials.hostToken);
  await ui.click('coop-connect');await ui.until(()=>ui.coop?.lobby?.seats?.length>=seat+1,'joined host');await ui.coopCommand('coop-ready');await ui.shot('01-lobby');
 }
 const [host,guest]=players;await host.coopCommand('coop-start');await guest.until(()=>guest.coop?.game?.scene?.kind==='map','shared map');
 host.check(host.coop.game.local.id!==guest.coop.game.local.id,'different authenticated seats');
 const node=host.coop.game.reachableIds[0];await host.coopCommand('coop-route-'+node);host.check(host.coop.game.scene.kind==='map','one vote waits for other connected player');await guest.coopCommand('coop-route-'+node);await host.until(()=>host.coop.game.scene.kind==='combat','shared real encounter');
 await host.shot('02-combat');await guest.shot('02-own-hand');
 const guestHand=JSON.stringify(guest.coop.game.local.hand.map(r=>r.instance));const guestSequence=guest.coop.game.local.sequence;
 await guest.click('coop-menu');const beforeReload=guest.coopRevision;await guest.page.reload();await guest.page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await guest.click('native-coop');await guest.click('coop-rejoin');await guest.until(()=>guest.coopRevision>beforeReload&&guest.coop?.game?.local?.sequence===guestSequence&&guest.has('coop-menu'),'rejoined own seat');
 guest.check(JSON.stringify(guest.coop.game.local.hand.map(r=>r.instance))===guestHand,'page reload rejoins same exact hand without extra draw');
 guest.check(guest.coop.game.local.combat.ended===true,'original disconnect semantics end this turn until the next player phase');await guest.shot('03-rejoined-combat');
 // Leave/rejoin ends the old turn. Advance it through actual controls, then prove
 // the returning player receives a real next turn and can pay for a real card.
 const rejoinTurn=guest.coop.game.scene.turn;
 await host.coopCommand('coop-end-turn');
 await guest.until(()=>guest.coop.game.scene.kind==='combat'&&guest.coop.game.scene.turn>rejoinTurn&&!guest.coop.game.local.combat.ended&&guest.has('coop-end-turn'),'rejoined guest next player phase');
 guest.check(guest.coop.game.scene.turn===rejoinTurn+1,'rejoined guest becomes active on exactly the next player turn');
 function affordable(ui){const local=ui.coop.game.local,body=local.combat.entity;return local.hand.filter(r=>(r.cost.variable||r.cost.action<=body.energy)&&r.cost.mana<=body.mana&&r.cost.stamina<=body.stamina&&!['status','curse'].includes(r.card.type));}
 async function selectCard(ui,row){
  const id='coop-card-'+row.instance.instanceId;
  // A peer update now intentionally retains the hand page. Always search from
  // its start so a later policy choice on an earlier page remains reachable.
  for(let page=0;ui.has('coop-cards-prev')&&page<100;page++)await ui.click('coop-cards-prev');
  for(let page=0;!ui.has(id)&&ui.has('coop-cards-next')&&page<100;page++)await ui.click('coop-cards-next');
  await ui.click(id);
 }
 async function playCard(ui,row,selected=false){
  if(!selected)await selectCard(ui,row);
  const target=ui.coop.game.scene.enemies.filter(e=>e.alive).sort((a,b)=>a.hp-b.hp)[0];
  if(target&&ui.has('coop-target-'+target.id))await ui.click('coop-target-'+target.id);
  const before=ui.coop.game.local.sequence;await ui.coopCommand('coop-play');
  await ui.until(()=>ui.coop.game.local.sequence===before+1,'own card accepted once');
 }
 await host.until(()=>host.coop.game.scene.turn>rejoinTurn&&!host.coop.game.local.combat.ended,'host next player phase');
 const held=affordable(host).filter(r=>!r.targets.active).at(-1),guestCard=affordable(guest)[0];
 host.check(!!held&&!!guestCard,'both actual hands contain an affordable card after reconnect');
 await selectCard(host,held);host.check(host.has('coop-play'),'host selected card is playable before peer action');
 const peerRevision=host.coopRevision,firstGuestPlayTurn=guest.coop.game.scene.turn;
 await playCard(guest,guestCard);
 await host.until(()=>host.coopRevision>peerRevision,'guest card broadcast reaches host');
 await host.until(()=>host.has('coop-card-'+held.instance.instanceId)&&host.has('coop-play'),'host card selection and page survive peer action');
 host.check(host.has('coop-play'),'peer action preserves host selected card and its hand page');
 guest.check(firstGuestPlayTurn===rejoinTurn+1&&guest.coop.game.local.sequence===guestSequence+1,'rejoined guest plays an accepted card on the next turn');
 fs.writeFileSync(path.join(guest.output,'first-post-rejoin-play.json'),JSON.stringify({turn:firstGuestPlayTurn,cardInstanceId:guestCard.instance.instanceId,sequence:guest.coop.game.local.sequence},null,2));
 await guest.shot('03b-next-turn-card');await host.shot('03c-selection-retained');
 await playCard(host,held,true);
 let count=0;
 while(host.coop.game.scene.kind==='combat'&&count++<100){
  for(const ui of players){if(ui.coop.game.scene.kind!=='combat')break;const local=ui.coop.game.local,body=local.combat.entity;if(local.combat.ended||!body.alive)continue;
   const rows=local.hand.filter(r=>(r.cost.variable||r.cost.action<=body.energy)&&r.cost.mana<=body.mana&&r.cost.stamina<=body.stamina&&!['status','curse'].includes(r.card.type));
   const score=r=>r.card.effects.reduce((v,e)=>v+(e.op==='damage'?(Number(e.amount)||5)+(e.attributeBonus||0):e.op==='applyStatus'?3:1),0);rows.sort((a,b)=>score(b)-score(a));
   if(rows.length)await playCard(ui,rows[0]);
   else await ui.coopCommand('coop-end-turn');
  }
 }
 await guest.until(()=>guest.coop.game.scene.kind==='rewards','shared reward room');host.check(host.coop.game.scene.kind==='rewards','two Unity clients defeat actual encounter');
 for(const ui of players){await ui.shot('04-rewards');await ui.coopCommand('coop-reward-confirm');}
 await host.until(()=>host.coop.game.scene.kind==='map','party rewards complete');
 for(const ui of players){ui.check(ui.coop.game.local.run.fightsWon===1,'real fight counted once');ui.check(ui.errors.length===0,'no browser or Unity errors');await ui.shot('05-shared-route');ui.save(true);}
 console.log('Two native Unity players completed a shared fight, rewards, and exact-hand rejoin.');await browser.close();
})().catch(async e=>{console.error(e);for(const ui of players){ui.errors.push(e.stack);await ui.shot('failure').catch(()=>{});ui.save(false);}if(browser)await browser.close();process.exitCode=1;});
