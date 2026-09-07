// Executes the real Unity .jslib bridge against the real companion WebSocket.
// Emscripten exports are supplied by the harness; no rules or server are mocked.
import vm from 'node:vm';
import fs from 'node:fs';
import assert from 'node:assert/strict';
const messages = new Map(), pending = new Map();
const context = vm.createContext({ WebSocket, TextEncoder, JSON,
 LibraryManager: { library: {} }, mergeInto: (target, source) => Object.assign(target, source), UTF8ToString: x => x,
 SendMessage: (receiver, method, payload) => { assert.equal(method, 'OnNativeLanEvent'); const row = JSON.parse(payload); if (!messages.has(receiver)) messages.set(receiver, []); messages.get(receiver).push(row); pending.get(receiver)?.(); }
});
vm.runInContext(fs.readFileSync(process.env.AS_LAN_JSLIB ?? new URL('../../../../Unity/Assets/AshenSpire/Plugins/WebGL/NativeLan.jslib', import.meta.url),'utf8'),context);
const library = context.LibraryManager.library; context.AshenSpireLan = library.$AshenSpireLan;
let checks = 0;
async function next(receiver, predicate) {
 const end = Date.now() + 10000;
 while (Date.now() < end) {
  const queue = messages.get(receiver) ?? []; const i = queue.findIndex(predicate); if (i >= 0) return queue.splice(i,1)[0];
  await new Promise(resolve => { const timer=setTimeout(resolve,50); pending.set(receiver,()=>{clearTimeout(timer);resolve();}); });
 }
 throw new Error('Timed out waiting for ' + receiver);
}
const send = (name,type,payload,requestId) => library.AS_NativeLan_Send(name,7,JSON.stringify({v:1,type,payload,requestId}));
for (const name of ['host','guest']) library.AS_NativeLan_Connect(name,process.env.LAN_URL,7);
for (const name of ['host','guest']) { await next(name,m=>m.type==='open'); checks++; send(name,'hello',{resumeToken:process.env[name==='host'?'HOST_RESUME':'GUEST_RESUME']},'hello'); }
const seats=[];
for (const name of ['host','guest']) {
 const row=JSON.parse((await next(name,m=>m.type==='message'&&JSON.parse(m.data).type==='welcome')).data); seats.push(row.payload.seatId); assert.equal(row.payload.rejoined,true);checks++;
 send(name,'resync',{},'resync'); const view=JSON.parse((await next(name,m=>m.type==='message'&&JSON.parse(m.data).type==='state')).data);assert.equal(view.payload.game.local.id,row.payload.seatId);checks++;
}
assert.notEqual(seats[0],seats[1]);checks++;
library.AS_NativeLan_Send('guest',7,'x'.repeat(16385));await next('guest',m=>m.type==='close');checks++;
library.AS_NativeLan_Close('host');assert.equal(context.AshenSpireLan.sockets.host,undefined);checks++;
console.log(`PASS ${checks} real WebSocket Unity .jslib bridge checks (Node harness, not browser evidence).`);
