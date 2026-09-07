// Real pointer/keyboard driver. Diagnostics observe rendered controls and native state.
// No game state writes, Unity SendMessage commands, or network intents are injected.
const {controlReportsForPage}=require('./control-report.cjs');
const fs=require('node:fs'),path=require('node:path');
class NativeUiDriver {
 constructor(page,output){this.page=page;this.output=output;this.controls=null;this.state=null;this.coop=null;this.layout=0;this.revision=0;this.coopRevision=0;this.errors=[];this.checks=[];this.chunks=new Map();fs.mkdirSync(output,{recursive:true});
  const normalizeControls=controlReportsForPage(page,e=>this.errors.push(e));
  page.on('pageerror',e=>this.errors.push(e.message));
  page.on('console',m=>{const v=normalizeControls(m.text());if(v===null)return;if(m.type()==='error')this.errors.push(v);for(const prefix of ['ASHENSPIRE_CONTROLS ','ASHENSPIRE_NATIVE_STATE_CHUNK ','ASHENSPIRE_COOP_STATE_CHUNK ']){const at=v.indexOf(prefix);if(at<0)continue;try{const d=JSON.parse(v.slice(at+prefix.length));if(prefix.includes('CONTROLS')){this.controls=d;this.layout++;}else{const key=prefix+d.sequence;const parts=this.chunks.get(key)||[];parts[d.index]=d.text;this.chunks.set(key,parts);if(parts.filter(x=>x!==undefined).length===d.count){const state=JSON.parse(parts.join(''));if(prefix.includes('COOP')){this.coop=state;this.coopRevision++;}else{this.state=state;this.revision++;}this.chunks.delete(key);}}}catch(e){this.errors.push('Invalid observer JSON: '+e.message);}}});
 }
 check(value,label){if(!value)throw Error(label);this.checks.push(label);}
 has(id){return this.controls?.Controls.some(c=>c.Id===id&&c.Enabled);}
 async until(test,label,timeout=30000){const end=Date.now()+timeout;while(Date.now()<end){if(test())return;await this.page.waitForTimeout(80);}throw Error('Timed out: '+label);}
 async open(url){await this.page.goto(url);const stamp=await this.page.request.get(new URL('build-source.json',url).href);if(!stamp.ok())throw Error('Missing player build receipt');fs.writeFileSync(path.join(this.output,'build-source.json'),await stamp.body());await this.page.waitForFunction(()=>!!window.unityInstance,null,{timeout:120000});await this.until(()=>this.controls?.Controls.length,'title');}
 async frames(){await this.page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));}
 async key(value){if(value.includes('+')){const [mod,key]=value.split('+');await this.page.keyboard.down(mod);try{await this.key(key);}finally{await this.page.keyboard.up(mod);}return;}await this.page.keyboard.down(value);await this.frames();await this.page.waitForTimeout(100);await this.page.keyboard.up(value);await this.frames();}
 async click(id,change=true,fraction=.5){
  // Legacy journey tests select named routes through the board's actual list.
  // These two physical clicks remain one caller-owned gameplay command; neither
  // toolbar nor choice IDs match this route pattern, so redirection cannot loop.
  const route=/^(native|coop)-route-(.+)$/.exec(id);
  if(route&&this.controls?.Controls.some(c=>c.Id===route[1]+'-map-routes')){
   await this.click(route[1]+'-map-routes');
   return this.click(route[1]+'-map-choice-'+route[2],change,fraction);
  }
  await this.until(()=>this.has(id),'control '+id);for(let step=0;step<40;step++){const canvas=await this.page.locator('#unity-canvas').boundingBox(),c=this.controls.Controls.find(x=>x.Id===id);const x=canvas.x+(c.X+c.Width*fraction)*canvas.width/this.controls.PanelWidth,y=canvas.y+(c.Y+c.Height/2)*canvas.height/this.controls.PanelHeight;const bottom=this.state?.phase==='Combat'&&!id.startsWith('coop-')&&!['native-play','native-end-turn'].includes(id)?120:25;if(y<canvas.y+30||y>canvas.y+canvas.height-bottom){const old=this.layout;await this.page.mouse.move(canvas.x+canvas.width*.92,canvas.y+canvas.height*.5);await this.page.mouse.wheel(0,y<canvas.y+30?-320:320);await this.until(()=>this.layout>old,'scroll '+id);await this.page.waitForTimeout(220);continue;}const old=this.layout;await this.page.mouse.move(x,y);await this.frames();await this.page.mouse.down();await this.page.waitForTimeout(140);await this.page.mouse.up();await this.frames();if(change)await this.until(()=>this.layout>old,'response '+id);await this.page.waitForTimeout(250);return;}throw Error('Cannot reach '+id);}
 async fill(id,value){await this.click(id,false,.85);await this.key('Control+a');await this.key('Backspace');await this.page.keyboard.type(value,{delay:80});await this.key('Tab');await this.page.waitForTimeout(200);}
 async choose(id,index){await this.click(id,false,.85);await this.page.waitForTimeout(500);await this.frames();await this.key('Home');for(let n=0;n<index;n++)await this.key('ArrowDown');await this.key('Enter');await this.page.waitForTimeout(700);}
 async command(id){const before=this.revision;await this.click(id);await this.until(()=>this.revision>before,'native command '+id);}
 async coopCommand(id){const before=this.coopRevision;await this.click(id);await this.until(()=>this.coopRevision>before,'shared command '+id);}
 async shot(name){await this.page.waitForTimeout(250);await this.page.screenshot({path:path.join(this.output,name+'.png')});}
 save(success){fs.writeFileSync(path.join(this.output,'checks.json'),JSON.stringify({success,checks:this.checks,errors:this.errors,state:this.state,coop:this.coop,controls:this.controls,physicalDevice:false},null,2));}
}
module.exports={NativeUiDriver};
