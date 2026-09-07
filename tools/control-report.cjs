// Reassemble only read-only Unity control diagnostics. Each page owns an assembler;
// navigation resets sequence identity. Other logs/state protocols pass unchanged.
const PREFIX='ASHENSPIRE_CONTROLS ',CHUNK='ASHENSPIRE_CONTROLS_CHUNK ';
class ControlReportAssembler {
 constructor({onError=()=>{},now=Date.now}={}){this.onError=onError;this.now=now;this.reset();}
 reset(){this.pending=new Map();this.lastCompleted=-1;}
 normalize(value){
  const time=this.now();
  for(const [sequence,entry] of this.pending)if(time-entry.at>30000){this.pending.delete(sequence);this.onError('Incomplete control report expired.');}
  const at=value.indexOf(CHUNK);
  if(at<0){if(value.includes(PREFIX))this.pending.clear();return value;}
  try{
   const encoded=value.slice(at+CHUNK.length);
   if(encoded.length>32768)throw Error('Control chunk envelope exceeds limit.');
   const row=JSON.parse(encoded);
   if(!row||!Number.isSafeInteger(row.sequence)||row.sequence<0||!Number.isInteger(row.count)||row.count<1||row.count>128||!Number.isInteger(row.index)||row.index<0||row.index>=row.count||typeof row.text!=='string'||row.text.length>4096)throw Error('Invalid control chunk bounds.');
   if(row.sequence<=this.lastCompleted)return null;
   let entry=this.pending.get(row.sequence);
   if(entry&&entry.count!==row.count){this.pending.delete(row.sequence);throw Error('Conflicting control chunk count.');}
   if(!entry){while(this.pending.size>=4)this.pending.delete(this.pending.keys().next().value);entry={count:row.count,parts:new Map(),size:0,at:time};this.pending.set(row.sequence,entry);}
   if(entry.parts.has(row.index)){
    if(entry.parts.get(row.index)!==row.text){this.pending.delete(row.sequence);throw Error('Conflicting duplicate control chunk.');}
    return null;
   }
   entry.parts.set(row.index,row.text);entry.size+=row.text.length;
   if(entry.size>262144){this.pending.delete(row.sequence);throw Error('Control report exceeds limit.');}
   if(entry.parts.size!==entry.count)return null;
   const json=Array.from({length:entry.count},(_,index)=>entry.parts.get(index)).join('');
   this.pending.delete(row.sequence);
   const report=JSON.parse(json);
   if(!report||typeof report!=='object'||Array.isArray(report)||!Array.isArray(report.Controls))throw Error('Invalid complete control report.');
   this.lastCompleted=row.sequence;
   for(const sequence of this.pending.keys())if(sequence<=row.sequence)this.pending.delete(sequence);
   return PREFIX+json;
  }catch(error){this.onError('Invalid control report: '+error.message);return null;}
 }
}
function controlReportsForPage(page,onError){
 const assembler=new ControlReportAssembler({onError});
 page.on('framenavigated',frame=>{if(frame===page.mainFrame())assembler.reset();});
 page.on('close',()=>assembler.reset());
 return value=>assembler.normalize(value);
}
module.exports={ControlReportAssembler,controlReportsForPage};
