// Protocol tests only: no gameplay simulation and no browser evidence claim.
const assert=require('node:assert/strict'),{EventEmitter}=require('node:events');
const {ControlReportAssembler,controlReportsForPage}=require('./control-report.cjs');
const prefix='ASHENSPIRE_CONTROLS ',chunk='ASHENSPIRE_CONTROLS_CHUNK ';let checks=0;
function check(value,label){assert.ok(value,label);checks++;}
const errors=[];let time=0;const a=new ControlReportAssembler({onError:e=>errors.push(e),now:()=>time});
const text=JSON.stringify({Controls:Array.from({length:250},(_,i)=>({Id:'control-'+i,Enabled:true,X:i,Y:i})),Labels:['🛡','quoted " text','slash \\','line\nend']});
const chunks=(sequence,json=text,size=2500)=>Array.from({length:Math.ceil(json.length/size)},(_,index)=>chunk+JSON.stringify({sequence,index,count:Math.ceil(json.length/size),text:json.slice(index*size,(index+1)*size)}));
const first=chunks(1);let output;
for(const packet of first)output=a.normalize(packet);
check(output===prefix+text,'large exact JSON reconstructed');check(JSON.parse(output.slice(prefix.length)).Controls.length===250,'all controls retained');check(a.pending.size===0,'completed buffers released');
check(a.normalize('ordinary console text')==='ordinary console text','unrelated log unchanged');
check(a.normalize('ASHENSPIRE_NATIVE_STATE_CHUNK {}')==='ASHENSPIRE_NATIVE_STATE_CHUNK {}','native state protocol untouched');
check(a.normalize(prefix+'{}')===prefix+'{}','legacy single report unchanged');
check(a.normalize(first[0])===null&&a.pending.size===0,'completed old sequence ignored');
const reverse=chunks(2).reverse();for(const packet of reverse)output=a.normalize(packet);check(output===prefix+text,'out of order chunks assembled');
const duplicate=chunks(3);check(a.normalize(duplicate[0])===null,'partial chunk suppressed');check(a.normalize(duplicate[0])===null,'identical duplicate suppressed');for(const packet of duplicate.slice(1))output=a.normalize(packet);check(output===prefix+text,'duplicate does not prevent completion');
const older=chunks(4),newer=chunks(5);a.normalize(older[0]);for(const packet of newer)output=a.normalize(packet);check(output===prefix+text&&a.pending.size===0,'new complete report discards older partial');check(a.normalize(older[1])===null,'old late chunk ignored');
const failureCount=errors.length;
for(const row of [{sequence:-1,index:0,count:1,text:'{}'},{sequence:1.5,index:0,count:1,text:'{}'},{sequence:6,index:1,count:1,text:'{}'},{sequence:6,index:-1,count:1,text:'{}'},{sequence:6,index:0,count:129,text:'{}'},{sequence:6,index:0,count:0,text:'{}'},{sequence:6,index:0,count:1,text:1},{sequence:6,index:0,count:1,text:'x'.repeat(4097)}])check(a.normalize(chunk+JSON.stringify(row))===null,'invalid bounds refused');
check(errors.length===failureCount+8,'invalid bounds surfaced as diagnostics');
check(a.normalize(chunk+'{truncated')===null,'truncated envelope refused');
check(a.normalize(chunk+JSON.stringify({sequence:6,index:0,count:1,text:'not JSON'}))===null,'invalid completed JSON refused');
check(a.normalize(chunk+JSON.stringify({sequence:6,index:0,count:1,text:'{"Controls":null}'}))===null,'invalid completed report refused');
a.normalize(chunk+JSON.stringify({sequence:7,index:0,count:2,text:'a'}));a.normalize(chunk+JSON.stringify({sequence:7,index:0,count:2,text:'b'}));check(!a.pending.has(7),'conflicting duplicate releases sequence');
a.normalize(chunk+JSON.stringify({sequence:8,index:0,count:2,text:'a'}));a.normalize(chunk+JSON.stringify({sequence:8,index:1,count:3,text:'b'}));check(!a.pending.has(8),'conflicting count releases sequence');
for(let sequence=10;sequence<30;sequence++)a.normalize(chunk+JSON.stringify({sequence,index:0,count:2,text:'a'}));check(a.pending.size===4,'bounded number of incomplete reports');
time=30001;a.normalize('next log');check(a.pending.size===0,'expired partial reports released');
for(let index=0;index<65;index++)a.normalize(chunk+JSON.stringify({sequence:40,index,count:128,text:'x'.repeat(4096)}));check(!a.pending.has(40),'total assembled length bounded');
a.normalize(chunks(50)[0]);a.normalize(prefix+'{"Controls":[]}');check(a.pending.size===0,'legacy report supersedes pending chunks');
a.reset();for(const packet of chunks(0))output=a.normalize(packet);check(output===prefix+text,'reset permits fresh page sequence zero');
const page=new EventEmitter(),main={};page.mainFrame=()=>main;const normalize=controlReportsForPage(page,e=>errors.push(e));
for(const packet of chunks(100))output=normalize(packet);check(output===prefix+text,'page wrapper completes report');page.emit('framenavigated',{});check(normalize(chunks(1)[0])===null,'child navigation does not reset identity');page.emit('framenavigated',main);for(const packet of chunks(1))output=normalize(packet);check(output===prefix+text,'main navigation resets identity');
const separate=new ControlReportAssembler();for(const packet of chunks(1))output=separate.normalize(packet);check(output===prefix+text,'independent page sequence state');page.emit('close');
const unicode=JSON.stringify({Controls:[],Labels:['🛡']});separate.reset();for(const packet of chunks(1,unicode,1))output=separate.normalize(packet);check(output===prefix+unicode,'split UTF16 surrogate pair reconstructed exactly');
// Exercise the real driver's console listener with an EventEmitter page adapter.
// This proves integration and single layout increments, not browser rendering.
const fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {NativeUiDriver}=require('./native-ui-driver.cjs');
const driverPage=new EventEmitter(),driverMain={};driverPage.mainFrame=()=>driverMain;
const directory=fs.mkdtempSync(path.join(os.tmpdir(),'ashenspire-control-report-'));
try{
 const driver=new NativeUiDriver(driverPage,directory);
 const log=value=>driverPage.emit('console',{text:()=>value,type:()=>'log'});
 for(const packet of chunks(11))log(packet);
 check(driver.layout===1&&driver.controls.Controls.length===250,'real driver receives one complete layout');
 check(driver.errors.length===0,'valid chunks never produce truncated JSON errors');
 log('ASHENSPIRE_NATIVE_STATE_CHUNK '+JSON.stringify({sequence:1,index:0,count:1,text:'{"phase":"Map"}'}));
 check(driver.revision===1&&driver.state.phase==='Map','real driver native-state protocol unchanged');
 driverPage.emit('framenavigated',driverMain);for(const packet of chunks(1))log(packet);
 check(driver.layout===2,'real driver accepts sequence reset after reload');
 log(prefix+'{"Controls":[],"Labels":[]}');check(driver.layout===3&&driver.controls.Controls.length===0,'real driver still accepts single reports');
}finally{fs.rmSync(directory,{recursive:true,force:true});}
console.log(`Control report assembly: ${checks} checks passed.`);
