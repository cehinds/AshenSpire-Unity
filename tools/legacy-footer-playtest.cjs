// Observe compiled geometry and campaign state; use ordinary pointer input only.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
const output=path.resolve(process.argv[3]||'TestResults/LegacyFooter');
fs.mkdirSync(output,{recursive:true});
let browser,page,ui,state,revision=0;
const samples=[];
(async()=>{
 browser=await chromium.launch({headless:true,...(process.platform==='win32'?{channel:'msedge'}:{}),args:['--enable-unsafe-swiftshader','--use-angle=swiftshader']});
 page=await browser.newPage({viewport:{width:390,height:844}});
 ui=new NativeUiDriver(page,output);
 page.on('console',message=>{const text=message.text(),prefix='ASHENSPIRE_CAMPAIGN ',at=text.indexOf(prefix);if(at>=0){state=JSON.parse(text.slice(at+prefix.length));revision++;}});
 await ui.open(process.argv[2]||'http://127.0.0.1:8787/');
 await ui.click('title-extras');await ui.click('new');await ui.click('hero-reaver');
 await ui.until(()=>state?.Phase===0,'legacy map');
 await ui.click('enter-0');await ui.until(()=>state?.Phase===1,'legacy combat');
 const healing=JSON.parse(fs.readFileSync(path.join(__dirname,'../GameContent/Unity/campaign.json'),'utf8')).PotionHealing;
 for(const [width,height] of [[390,844],[740,320]]){
  await page.setViewportSize({width,height});
  await ui.until(()=>Math.abs(ui.controls.PanelWidth/ui.controls.PanelHeight-width/height)<.001,'resized combat');
  // Some enemy turns guard. Reach real damage through turns, never by editing HP.
  for(let turn=0;turn<4&&state.Health===state.MaxHealth;turn++){
   const old=revision;await ui.click('end-turn');await ui.until(()=>revision>old,'enemy turn');
  }
  ui.check(state.Health>0&&state.Health<state.MaxHealth&&state.Potions>0,'damaged survivor has a flask at '+width+'x'+height);
  let visible=false,previousY=null,stagnant=0,geometry;
  for(let step=0;step<40;step++){
   await page.waitForTimeout(350);
   const canvas=await page.locator('#unity-canvas').boundingBox(),report=ui.controls;
   const flask=report.Controls.find(c=>c.Id==='potion'),play=report.Controls.find(c=>c.Id==='play');
   const scale=canvas.height/report.PanelHeight;
   geometry={width,height,flaskTop:flask.Y*scale,flaskBottom:(flask.Y+flask.Height)*scale,footerTop:(play.Y-10)*scale,touchHeight:flask.Height*scale};
   if(geometry.flaskTop>=4&&geometry.flaskBottom<=geometry.footerTop){visible=true;break;}
   stagnant=previousY!==null&&Math.abs(previousY-flask.Y)<.1?stagnant+1:0;previousY=flask.Y;
   if(stagnant>=5)break;
   await page.mouse.move(canvas.x+6,canvas.y+canvas.height/2);await page.mouse.wheel(0,geometry.flaskTop<4?-300:300);
  }
  samples.push(geometry);
  await ui.shot(width+'x'+height+'-flask-visible');
  ui.check(visible,'entire flask button is reachable above the fixed action bar at '+width+'x'+height);
  ui.check(geometry.touchHeight>=43.995,'flask touch target retains 44 CSS pixels at '+width+'x'+height);
  const before={health:state.Health,potions:state.Potions,revision};
  await ui.click('potion');await ui.until(()=>revision>before.revision,'flask consumed');
  ui.check(state.Potions===before.potions-1&&state.Health===Math.min(state.MaxHealth,before.health+healing),'visible flask spends one charge and restores the expected vitality at '+width+'x'+height);
 }
 ui.check(ui.errors.length===0,'no browser or Unity errors');
 ui.save(true);fs.writeFileSync(path.join(output,'geometry.json'),JSON.stringify(samples,null,2));
 console.log('Legacy footer: '+ui.checks.length+' checks passed.');
})().catch(async error=>{if(ui){ui.errors.push(error.stack||String(error));ui.save(false);await ui.shot('failure').catch(()=>{});}fs.writeFileSync(path.join(output,'geometry.json'),JSON.stringify(samples,null,2));console.error(error);process.exitCode=1;}).finally(async()=>{if(browser)await browser.close();});
