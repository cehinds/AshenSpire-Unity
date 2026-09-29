import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const library={},messages=[],reads=[];let value=null,blocked=false;
vm.runInNewContext(fs.readFileSync(new URL('../Unity/Assets/Plugins/WebGL/OriginalSaveImport.jslib',import.meta.url),'utf8'),{
 LibraryManager:{library},mergeInto:Object.assign,UTF8ToString:value=>value,TextEncoder,
 Module:{SendMessage:(owner,method,message)=>messages.push({owner,method,...JSON.parse(message)})},
 window:{localStorage:{getItem:key=>{reads.push(key);if(blocked)throw Error('blocked');return value;}}}
});
assert.equal(reads.length,0);let checks=1;
for(let slot=0;slot<3;slot++){
 value='{"fixture":'+slot+'}';library.AshenedSpire_ReadOriginalSlot('receiver',slot);
 assert.equal(reads.at(-1),['sote_run_v1','sote_run_v1_s2','sote_run_v1_s3'][slot]);
 assert.deepEqual(messages.at(-1),{owner:'receiver',method:'OnOriginalSaveRead',save:value});checks+=2;
}
value=null;library.AshenedSpire_ReadOriginalSlot('receiver',0);assert.match(messages.at(-1).error,/No original save/);checks++;
value='é'.repeat(524289);library.AshenedSpire_ReadOriginalSlot('receiver',0);assert.match(messages.at(-1).error,/1 MB/);checks++;
blocked=true;library.AshenedSpire_ReadOriginalSlot('receiver',0);assert.match(messages.at(-1).error,/could not read/);checks++;
const before=reads.length;library.AshenedSpire_ReadOriginalSlot('receiver',3);assert.equal(reads.length,before);assert.match(messages.at(-1).error,/1 to 3/);checks+=2;
console.log('Original-save browser bridge: '+checks+' checks passed.');
