// Exercise the actual .jslib listeners, including page cache events and disposal.
const {readFileSync}=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const document=new EventTarget(),window=new EventTarget(),messages=[];
document.hidden=false;
const Module={SendMessage:(...args)=>messages.push(args)};
const library={};vm.runInNewContext(readFileSync('Unity/Assets/Plugins/WebGL/BrowserVisibility.jslib','utf8'),{document,window,Module,UTF8ToString:x=>x,LibraryManager:{library},mergeInto:Object.assign});
let checks=0;const check=(value,expected)=>{assert.deepEqual(value,expected);checks++;};
library.AshenSpire_WatchVisibility('ExpeditionRoot');check(messages.pop(),['ExpeditionRoot','OnBrowserVisibilityChanged',0]);
document.hidden=true;document.dispatchEvent(new Event('visibilitychange'));check(messages.pop()[2],1);
window.dispatchEvent(new Event('pagehide'));check(messages.pop()[2],1);
document.hidden=false;document.dispatchEvent(new Event('visibilitychange'));check(messages.pop()[2],1);
window.dispatchEvent(new Event('pageshow'));check(messages.pop()[2],0);
library.AshenSpire_WatchVisibility('ReplacementRoot');messages.length=0;
document.hidden=true;document.dispatchEvent(new Event('visibilitychange'));check(messages.length,1);check(messages.pop()[0],'ReplacementRoot');
library.AshenSpire_UnwatchVisibility();library.AshenSpire_UnwatchVisibility();
document.dispatchEvent(new Event('visibilitychange'));window.dispatchEvent(new Event('pagehide'));window.dispatchEvent(new Event('pageshow'));check(messages.length,0);
library.AshenSpire_WatchVisibility('ReenabledRoot');check(messages.pop()[2],1);
console.log(`Browser visibility bridge: ${checks} checks passed`);
