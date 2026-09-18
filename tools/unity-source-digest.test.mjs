import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {unitySourceDigest} from './unity-source-digest.mjs';

const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
const digest=entries=>unitySourceDigest(Object.keys(entries),path=>entries[path]);

test('directory-prefix fixtures match the explicit Unity ordinal byte stream',()=>{
 const files={
  'Unity/Assets/Resources/OriginalCards.uss':Buffer.from('cards\r\n'),
  'Unity/Assets/Resources/Original/content.json':Buffer.from('{}\r\n'),
  'Unity/Assets/Resources/Original.meta':Buffer.from('meta\r\n')
 };
 const expected=sha('Unity/Assets/Resources/Original.metameta\n'+
  'Unity/Assets/Resources/Original/content.json{}\n'+
  'Unity/Assets/Resources/OriginalCards.usscards\n');
 assert.equal(digest(files),expected);
 const windows=Object.keys(files).map(path=>path.replaceAll('/','\\'));
 assert.equal(unitySourceDigest(windows,path=>files[path]),expected);
 // The old Windows ordering really disagrees: this fixture catches the bug.
 const oldOrder=windows.sort().map(path=>path.replaceAll('\\','/'));
 assert.notEqual(sha(oldOrder.map(path=>path+files[path].toString().replaceAll('\r\n','\n')).join('')),expected);
});

test('enumeration order and checkout path separators cannot change the receipt',()=>{
 const entries={'z/file.cs':Buffer.from('z'),'A/file.cs':Buffer.from('a'),'a/file.cs':Buffer.from('lower')};
 const paths=Object.keys(entries);
 const expected=sha('A/file.csaa/file.cslowerz/file.csz');
 for(const order of [paths,paths.toReversed(),[paths[1],paths[2],paths[0]]]){
  assert.equal(unitySourceDigest(order,path=>entries[path]),expected);
  assert.equal(unitySourceDigest(order.map(path=>path.replaceAll('/','\\')),path=>entries[path]),expected);
 }
});

test('text CRLF is normalized, preserving lone CR and authored UTF-8',()=>{
 assert.equal(digest({'a.cs':Buffer.from('α\r\nβ\rγ\n')}),sha('a.csα\nβ\rγ\n'));
 assert.equal(digest({'a.cs':Buffer.from('one\r\ntwo\r\n')}),digest({'a.cs':Buffer.from('one\ntwo\n')}));
 assert.notEqual(digest({'a.cs':Buffer.from('one\rtwo')}),digest({'a.cs':Buffer.from('one\ntwo')}));
});

for(const extension of ['png','jpg','webp','ttf','otf','TTF'])test(extension+' payload preserves every binary byte',()=>{
 const path='Unity/Assets/sample.'+extension,bytes=Buffer.from([0xff,0,13,10,0x80,1]);
 assert.equal(digest({[path]:bytes}),sha(Buffer.concat([Buffer.from(path),bytes])));
 assert.notEqual(digest({[path]:bytes}),digest({[path]:Buffer.from([0xff,0,10,0x80,1])}));
 assert.notEqual(digest({[path]:bytes}),digest({[path]:Buffer.from([0xff,0,13,10,0x80,2])}));
});

test('renaming a file changes its receipt even when bytes are identical',()=>{
 assert.notEqual(digest({'a.cs':Buffer.from('same')}),digest({'b.cs':Buffer.from('same')}));
});
