const {test}=require('node:test');
const assert=require('node:assert/strict');
const {NativeUiDriver}=require('./native-ui-driver.cjs');

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
