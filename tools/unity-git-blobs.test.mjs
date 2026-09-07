// Git-backed fixtures prove byte preservation and refusal boundaries without
// assembling the real Pages site or touching any live build/history directory.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {mkdirSync, writeFileSync, readFileSync, existsSync, mkdtempSync, symlinkSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {materializePublished, publishedRelativePath, parseBlobBatch} from './unity-git-blobs.mjs';

const fixture = mkdtempSync(join(tmpdir(), 'ashenspire-git-batch-'));
const root = join(fixture, 'repo'); mkdirSync(root);
const git = args => execFileSync('git', args, {cwd:root, encoding:'utf8', windowsHide:true, stdio:['ignore','pipe','pipe']}).trim();
git(['init']); git(['config','user.name','Batch fixture']); git(['config','user.email','batch@example.invalid']);
git(['config','core.autocrlf','false']);
const expected = new Map([
  ['Published/Web/index.html', Buffer.from('<!doctype html>\r\n<p>Unicode: 灰</p>\n')],
  ['Published/Web/Build/game.wasm', Buffer.from([0,97,115,109,0,10,13,255,128,0,10,0])],
  ['Published/Web/Build/empty.data', Buffer.alloc(0)],
  ['Published/NativeEvidence/art with spaces.png', Buffer.from([255,216,0,10,13,255,217])],
  ['Published/NativeEvidence/灰.txt', Buffer.from('exact Unicode filename and text\n')],
  ['Published/Web.zip', Buffer.from('ZIP stays on GitHub')],
  ['Published/ANDROID.APK', Buffer.from('APK stays on GitHub')]
]);
// Many small evidence blobs should share one Git payload process.
for (let n = 0; n < 128; n++) expected.set(`Published/Evidence/${n}.bin`, Buffer.from([n,0,10,255]));
for (const [file, bytes] of expected) { mkdirSync(join(root,file,'..'), {recursive:true}); writeFileSync(join(root,file),bytes); }
git(['add','Published']); git(['commit','-m','Fixture with binary and text evidence']);
const commit = git(['rev-parse','HEAD']), paths = [...expected.keys()];
let checks = 0;
const check = (name, action) => { action(); checks++; console.log('PASS ' + name); };
const out = join(fixture,'combined');
const result = materializePublished(root,commit,paths,out);
check('many files need one size pass and one payload batch', () => {
  assert.equal(result.gitProcesses,2); assert.equal(result.batches,1); assert.equal(result.files,expected.size-2);
});
check('every text, Unicode, empty and binary blob retains exact bytes', () => {
  let total=0;
  for (const [file, bytes] of expected) {
    if (/\.(zip|apk)$/i.test(file)) continue;
    assert.deepEqual(readFileSync(join(out,publishedRelativePath(file))),bytes,file); total+=bytes.length;
  }
  assert.equal(result.bytes,total);
});
check('ZIP and APK are excluded regardless of extension case', () => {
  assert.equal(existsSync(join(out,'Web.zip')),false); assert.equal(existsSync(join(out,'ANDROID.APK')),false);
});
check('bounded payload batches retain exact contents including a larger single blob', () => {
  const dest=join(fixture,'small-batches');
  const small=materializePublished(root,commit,paths,dest,{batchBytes:16,maxBlobBytes:256});
  assert.ok(small.batches>1); assert.equal(small.bytes,result.bytes);
  for (const [file,bytes] of expected) if (!/\.(zip|apk)$/i.test(file)) assert.deepEqual(readFileSync(join(dest,publishedRelativePath(file))),bytes,file);
});
check('oversized blobs fail before creating output', () => {
  const dest=join(fixture,'too-large');
  assert.throws(()=>materializePublished(root,commit,paths,dest,{batchBytes:8,maxBlobBytes:16}),/byte limit/);
  assert.equal(existsSync(dest),false);
});
check('missing blobs fail before earlier valid files are written', () => {
  const dest=join(fixture,'missing');
  assert.throws(()=>materializePublished(root,commit,['Published/Web/index.html','Published/missing.data'],dest),/Missing or non-blob/);
  assert.equal(existsSync(dest),false);
});
check('tree objects cannot masquerade as files', () => {
  assert.throws(()=>materializePublished(root,commit,['Published/Web'],join(fixture,'tree')),/non-blob/);
});
check('path traversal, control characters and Windows aliases fail before writes', () => {
  const bad=['Published/../escape','Published/Web/../../escape','/Published/file','Published//file','Published/./file',
    'Published/Web\\escape','Published/x\ny','Published/x\ry','Published/x\ty','Published/x\0y','Published/x\x7fy',
    'Published/C:/escape','Published/Web/a:stream','Published/.. /escape','Published/end.','Published/end ',
    'Published/NUL.txt','Published/Web/COM1','Published/LpT9.png'];
  for (const path of bad) {
    assert.throws(()=>materializePublished(root,commit,['Published/Web/index.html',path],join(fixture,'unsafe')),/Unsafe artifact path/);
  }
  assert.equal(existsSync(join(fixture,'unsafe')),false);
});
check('unsafe excluded paths are still rejected', () => {
  assert.throws(()=>materializePublished(root,commit,['Published/../bad.zip'],join(fixture,'excluded-unsafe')),/Unsafe artifact path/);
});
check('nonresolved refs and duplicate paths are rejected', () => {
  assert.throws(()=>materializePublished(root,'HEAD',paths,out),/resolved full commit/);
  assert.throws(()=>materializePublished(root,commit,['Published/Web/index.html','Published/Web/index.html'],out),/Duplicate/);
});
check('case-colliding filenames and directory prefixes fail portably before writes', () => {
  const dest=join(fixture,'case-collision');
  for (const paths of [
    ['Published/Web/index.html','Published/Web/INDEX.HTML'],
    ['Published/Web/index.html','Published/web/different.txt'],
    ['Published/A','Published/a']
  ]) assert.throws(()=>materializePublished(root,commit,paths,dest),/Case-colliding artifact paths/);
  assert.equal(existsSync(dest),false);
});
check('existing output directory links cannot redirect writes', () => {
  const dest=join(fixture,'linked'),external=join(fixture,'outside');mkdirSync(dest);mkdirSync(external);
  symlinkSync(external,join(dest,'Web'),process.platform==='win32'?'junction':'dir');
  assert.throws(()=>materializePublished(root,commit,['Published/Web/index.html'],dest),/Symlink artifact destination/);
  assert.equal(existsSync(join(external,'index.html')),false);
});
check('empty selections require no Git invocation', () => {
  assert.deepEqual(materializePublished(root,commit,[],out),{files:0,bytes:0,batches:0,gitProcesses:0});
});
const oid='a'.repeat(40), row={oid,size:5,path:'Published/binary'}, payload=Buffer.from([0,10,255,13,0]);
const record=Buffer.concat([Buffer.from(`${oid} blob 5\n`),payload,Buffer.from('\n')]);
check('binary record parser treats embedded newlines as payload',()=>assert.deepEqual(parseBlobBatch(record,[row])[0],payload));
check('truncated payload, wrong OID/type/size and trailing protocol bytes fail',()=>{
  for (const invalid of [record.subarray(0,-1),Buffer.from(`${oid} missing\n`),Buffer.from(`${oid} tree 5\nhello\n`),
    Buffer.from(`${'b'.repeat(40)} blob 5\nhello\n`),Buffer.from(`${oid} blob 6\nhello!\n`),Buffer.concat([record,Buffer.from('extra')])]) {
    assert.throws(()=>parseBlobBatch(invalid,[row]),/Git/);
  }
});
check('repeated materialization remains byte-identical',()=>assert.deepEqual(materializePublished(root,commit,paths,out),result));
console.log(`${checks} checks passed; fixture preserved at ${fixture}`);
