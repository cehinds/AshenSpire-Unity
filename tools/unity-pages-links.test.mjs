// Check generated library navigation without mistaking original game content
// for Pages navigation. Run after assembling a fresh preview directory.
import assert from 'node:assert/strict';
import {existsSync,readFileSync,readdirSync,statSync} from 'node:fs';
import {resolve,join,dirname,relative} from 'node:path';
const root=resolve(process.argv[2] || '_site');
let pages=0,links=0;
function walk(dir){
 for(const name of readdirSync(dir)){
  const path=join(dir,name);
  if(statSync(path).isDirectory()){if(!['Web','reference'].includes(name))walk(path);continue;}
  if(name!=='index.html')continue;
  pages++;
  const html=readFileSync(path,'utf8');
  assert.match(html,/<title>/);
  for(const match of html.matchAll(/(?:href|src)="([^"]+)"/g)){
   const url=match[1].split(/[?#]/)[0];
   if(!url||/^[a-z]+:/i.test(url))continue;
   const target=resolve(dirname(path),decodeURIComponent(url));
   assert(!relative(root,target).startsWith('..'),`Link escapes site: ${path}: ${url}`);
   assert(existsSync(target),`Broken local link: ${path}: ${url}`);
   if(statSync(target).isDirectory())assert(existsSync(join(target,'index.html')),`Missing directory index: ${target}`);
   links++;
  }
 }
}
walk(root);
assert(pages>=5,'Expected library and four channel pages');
assert(links>0,'No library links checked');
console.log(`${links} checks passed`);
console.log(`Validated navigation across ${pages} generated library pages.`);
