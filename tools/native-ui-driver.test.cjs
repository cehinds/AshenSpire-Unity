const {test}=require('node:test');
const assert=require('node:assert/strict');
const {NativeUiDriver,LEAN_CLASS_ALLOCATIONS}=require('./native-ui-driver.cjs');

function fixture(mode){
 const ui=Object.create(NativeUiDriver.prototype);
 ui.layout=0;ui.state=null;
 const control={Id:'coop-end-turn',X:20,Y:100,Width:100,Height:40,Enabled:true};
 ui.controls={PanelWidth:400,PanelHeight:800,Controls:[control]};
 let pointer={},pressed=false,moved=false,pending=0,samples=0;
 const result={accepted:0,cancelled:0,wrong:0,presses:0};
 const shift=()=>{control.Y-=60;ui.layout++;moved=true;};
 ui.page={
  locator:()=>({boundingBox:async()=>({x:0,y:0,width:400,height:800})}),
  evaluate:async()=>{},
  waitForTimeout:async ms=>{
   if(mode==='during-press'&&ms===140&&!moved)shift();
   if(mode==='scroll-settling'&&ms===80&&samples++<4){control.Y-=10;ui.layout++;}
   if(pending&&--pending===0)ui.layout++;
  },
  mouse:{
   move:async(x,y)=>{pointer={x,y};if(mode==='before-press'&&!moved)shift();},
   wheel:async()=>{control.Y=200;ui.layout++;},
   down:async()=>{pressed=true;result.presses++;},
   up:async()=>{
    assert.ok(pressed);pressed=false;
    if(pointer.x>400){result.cancelled++;return;}
    if(Math.abs(pointer.y-(control.Y+20))>.5){result.wrong++;return;}
    result.accepted++;
    if(mode==='late-response')pending=5;else ui.layout++;
   }
  }
 };
 return {ui,result};
}

test('scroll motion settles before the only accepted click',async()=>{
 const {ui,result}=fixture('scroll-settling');await ui.click('coop-end-turn');
 assert.deepEqual(result,{accepted:1,cancelled:0,wrong:0,presses:1});
});
test('a redraw after pointer movement refreshes the target before pressing',async()=>{
 const {ui,result}=fixture('before-press');await ui.click('coop-end-turn');
 assert.deepEqual(result,{accepted:1,cancelled:0,wrong:0,presses:1});
});
test('a redraw during press cancels outside the canvas instead of hitting a neighbor',async()=>{
 const {ui,result}=fixture('during-press');await ui.click('coop-end-turn');
 assert.deepEqual(result,{accepted:1,cancelled:1,wrong:0,presses:2});
});
test('a delayed command response never causes a second released command',async()=>{
 const {ui,result}=fixture('late-response');await ui.click('coop-end-turn');
 assert.deepEqual(result,{accepted:1,cancelled:0,wrong:0,presses:1});
});

function leanFixture(){
 const values={strength:1,dexterity:1,constitution:1,wisdom:1,intelligence:1};let remaining=3;const clicks=[];
 const controls=()=>({Controls:Object.keys(values).map(id=>({Id:'attribute-'+id+'-up',Enabled:remaining>0&&values[id]<4}))});
 const ui={get controls(){return controls();},click:async id=>{clicks.push(id);const attr=/^attribute-([a-z]+)-up$/.exec(id)[1];if(remaining<1||values[attr]>=4)throw Error('disabled '+id);values[attr]++;remaining--;}};
 return {ui,values,clicks,get remaining(){return remaining;}};
}
test('lean class table spends exactly the three Assigned points',()=>{
 for(const row of Object.values(LEAN_CLASS_ALLOCATIONS)){
  const values=Object.values(row);
  assert.equal(values.length,5);assert.equal(values.reduce((a,b)=>a+b,0),8);assert.ok(values.every(v=>v>=1&&v<=4));
 }
});
test('assignPoints clicks each +attribute (target-1) times in fixed order',async()=>{
 const f=leanFixture();await NativeUiDriver.prototype.assignPoints.call(f.ui);
 assert.deepEqual(f.values,LEAN_CLASS_ALLOCATIONS.reaver);assert.equal(f.remaining,0);
 assert.deepEqual(f.clicks,['attribute-strength-up','attribute-strength-up','attribute-constitution-up']);
 const s=leanFixture();await NativeUiDriver.prototype.assignPoints.call(s.ui,LEAN_CLASS_ALLOCATIONS.starseer);
 assert.deepEqual(s.values,LEAN_CLASS_ALLOCATIONS.starseer);
});
test('assignPoints refuses to finish while a +attribute control is still enabled',async()=>{
 const f=leanFixture();
 await assert.rejects(NativeUiDriver.prototype.assignPoints.call(f.ui,{strength:2}),/Creation points remain/);
});
