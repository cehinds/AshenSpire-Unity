// Real pointer/keyboard driver. Diagnostics observe rendered controls and native state.
// No game state writes, Unity SendMessage commands, or network intents are injected.
const fs=require('node:fs'),path=require('node:path');
const controlsModule=fs.existsSync(path.join(__dirname,'control-report.cjs'))?path.join(__dirname,'control-report.cjs'):path.join(process.env.ASHENSPIRE_REPO_ROOT||process.cwd(),'tools/control-report.cjs');
const {controlReportsForPage}=require(controlsModule);
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
  const route=/^(native|coop)-route-(.+)$/.exec(id);
  if(route&&this.controls?.Controls.some(c=>c.Id===route[1]+'-map-routes')){
   await this.click(route[1]+'-map-routes');
   return this.click(route[1]+'-map-choice-'+route[2],change,fraction);
  }
  await this.until(()=>this.has(id),'control '+id);
  // Bounds are observations, not gameplay state. A tall card may be clipped by
  // its rail; click an exposed interior patch, never its off-screen centre.
  let stagnant=0,lastGeometry='',lastReason='';
  for(let step=0;step<36;step++){
   const canvas=await this.page.locator('#unity-canvas').boundingBox(),report=this.controls;
   const c=report.Controls.find(control=>control.Id===id&&control.Enabled);
   if(!c){await this.until(()=>this.has(id),'remounted control '+id);continue;}
   const sx=canvas.width/report.PanelWidth,sy=canvas.height/report.PanelHeight;
   const rect={left:canvas.x+c.X*sx,top:canvas.y+c.Y*sy,width:c.Width*sx,height:c.Height*sy};
   rect.right=rect.left+rect.width;rect.bottom=rect.top+rect.height;
   const card=/^(native|coop)-card-/.test(id);
   const prefix=id.startsWith('coop-')?'coop':'native';
   const play=report.Controls.find(control=>control.Id===prefix+'-play');
   const boundary={left:canvas.x+4,top:canvas.y+4,right:canvas.x+canvas.width-4,bottom:canvas.y+canvas.height-4};
   if(card&&play)boundary.bottom=Math.min(boundary.bottom,canvas.y+play.Y*sy-4);
   const exposed={left:Math.max(rect.left,boundary.left),top:Math.max(rect.top,boundary.top),right:Math.min(rect.right,boundary.right),bottom:Math.min(rect.bottom,boundary.bottom)};
   const wantW=Math.max(1,Math.min(rect.width,boundary.right-boundary.left)-2),wantH=Math.min(44,Math.max(1,rect.height-4));
   const horizontal=exposed.right-exposed.left+0.1<wantW;
   const vertical=exposed.bottom-exposed.top+0.1<wantH;
   if(!horizontal&&!vertical){
    // Retain text-field fraction positioning when it is exposed. Card centres
    // can be below a rail clip; prefer the upper card face above its midpoint.
    const x=Math.max(exposed.left+2,Math.min(exposed.right-2,rect.left+rect.width*fraction));
    const preferredY=card?rect.top+Math.min(50,rect.height*.3):rect.top+rect.height/2;
    const y=Math.max(exposed.top+Math.min(wantH/2,12),Math.min(exposed.bottom-Math.min(wantH/2,12),preferredY));
    const old=this.layout;await this.page.mouse.move(x,y);await this.frames();
    await this.page.mouse.down();await this.page.waitForTimeout(140);await this.page.mouse.up();await this.frames();
    if(change)await this.until(()=>this.layout>old,'response '+id);
    await this.page.waitForTimeout(250);return;
   }
   const geometry=[c.X,c.Y,c.Width,c.Height].map(value=>Math.round(value*10)).join(',');
   stagnant=geometry===lastGeometry?stagnant+1:0;lastGeometry=geometry;
   if(stagnant>=5)throw Error('Cannot reach '+id+': no movement after bounded '+lastReason+' scroll; '+JSON.stringify({rect,boundary}));
   if(vertical){
    // Use a point within the visible rail when possible, otherwise outer body.
    const x=Math.max(boundary.left+8,Math.min(boundary.right-8,rect.left+rect.width/2));
    const y=exposed.bottom<=exposed.top?canvas.y+canvas.height*.5:Math.max(boundary.top+20,Math.min(boundary.bottom-20,(rect.top+rect.bottom)/2));
    await this.page.mouse.move(x,y);await this.page.mouse.wheel(0,rect.top<boundary.top?-260:260);lastReason='vertical';
   }else{
    // Horizontal wheel over the actual card/utility row reaches its own rail,
    // including the menu below Play. It never uses a fixed footer reservation.
    const x=canvas.x+canvas.width*.5;
    const y=Math.max(exposed.top+8,Math.min(exposed.bottom-8,rect.top+Math.min(50,rect.height/2)));
    await this.page.mouse.move(x,y);await this.page.mouse.wheel(rect.left<boundary.left?-240:240,0);lastReason='horizontal';
   }
   await this.page.waitForTimeout(300);
  }
  throw Error('Cannot reach '+id+' after bounded measured scrolling');
 }
 async fill(id,value){await this.click(id,false,.85);await this.key('Control+a');await this.key('Backspace');await this.page.keyboard.type(value,{delay:80});await this.key('Tab');await this.page.waitForTimeout(200);}
 async choose(id,index){await this.click(id,false,.85);await this.page.waitForTimeout(500);await this.frames();await this.key('Home');for(let n=0;n<index;n++)await this.key('ArrowDown');await this.key('Enter');await this.page.waitForTimeout(700);}
 async command(id){const before=this.revision;await this.click(id);await this.until(()=>this.revision>before,'native command '+id);}
 async coopCommand(id){const before=this.coopRevision;await this.click(id);await this.until(()=>this.coopRevision>before,'shared command '+id);}
 async shot(name){await this.page.waitForTimeout(250);await this.page.screenshot({path:path.join(this.output,name+'.png')});}
 save(success){fs.writeFileSync(path.join(this.output,'checks.json'),JSON.stringify({success,checks:this.checks,errors:this.errors,state:this.state,coop:this.coop,controls:this.controls,physicalDevice:false},null,2));}
}
module.exports={NativeUiDriver};
