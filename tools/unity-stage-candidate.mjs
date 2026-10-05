// Local, source-bound delivery candidates. Existing Published/ releases stay intact.
// node tools/unity-stage-candidate.mjs <new candidate directory> <companion folder>
// node tools/unity-stage-candidate.mjs <candidate directory> --check
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {unitySourceDigest} from './unity-source-digest.mjs';

const root = path.resolve(fileURLToPath(new URL('..', import.meta.url)));
const [outputArgument, companionArgument] = process.argv.slice(2);
if (!outputArgument || !companionArgument) throw Error('Pass a candidate directory and companion folder, or --check.');
const output = path.resolve(outputArgument), published = path.join(output, 'Published');
const relativeOutput = path.relative(path.join(root, 'Builds'), output);
if (!relativeOutput || relativeOutput.startsWith('..') || path.isAbsolute(relativeOutput)) throw Error('Store delivery candidates beneath this repository\'s Builds directory.');
// Some inherited shells advertise a removed Python installation. Prefer a valid
// explicit path, otherwise resolve the ordinary interpreter through PATH.
const python = process.env.PYTHON && fs.existsSync(process.env.PYTHON) ? process.env.PYTHON : (process.platform === 'win32' ? 'python' : 'python3');
execFileSync(python,['--version'],{stdio:'pipe'});
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const files = folder => fs.readdirSync(folder, {withFileTypes:true}).flatMap(e => e.isDirectory() ? files(path.join(folder, e.name)) : [path.join(folder, e.name)]).sort();
const sourceDigest = () => unitySourceDigest(['Unity/Assets','Unity/Packages','Unity/ProjectSettings','GameContent/Unity'].flatMap(dir => files(path.join(root, dir))).map(p => path.relative(root,p)), p => fs.readFileSync(path.join(root,p)));
const digest = sourceDigest(), version = read(path.join(root,'GameContent/Unity/version.json'));
function verifyExport(directory, platform) {
    const receipt = read(path.join(directory,'build-source.json'));
    assert.equal(receipt.sourceDigest,digest,platform+' source differs from the current source');
    assert.equal(receipt.version,version.Version); assert.equal(receipt.buildNumber,version.BuildNumber); assert.equal(receipt.target,platform);
    const actual = files(directory).map(p=>path.relative(directory,p).replaceAll('\\','/')).filter(p=>p!=='build-source.json').sort();
    assert.deepEqual(actual,Object.keys(receipt.files).sort(),platform+' unreceipted payload');
    for(const [name,hash] of Object.entries(receipt.files)) assert.equal(sha(fs.readFileSync(path.join(directory,name))),hash,platform+'/'+name);
    return receipt;
}
function validatePackages() {
    execFileSync(python,[path.join(root,'tools/validate-unity-targets.py'),output],{stdio:'inherit'});
    execFileSync(python,[path.join(root,'tools/validate-companion.py'),root,published],{stdio:'inherit'});
}
if (companionArgument === '--check') {
    const manifest = read(path.join(output,'candidate.json'));
    assert.equal(manifest.sourceDigest,digest); assert.equal(manifest.version,version.Version); assert.equal(manifest.buildNumber,version.BuildNumber);
    assert.deepEqual(files(published).map(p=>path.relative(output,p).replaceAll('\\','/')).sort(),Object.keys(manifest.files).sort(),'Unreceipted candidate payload');
    for(const [file,hash] of Object.entries(manifest.files)) assert.equal(sha(fs.readFileSync(path.join(output,file))),hash,file);
    for(const file of ['Web.zip','Windows.zip','Android.apk','Companion.zip']) {
        const bytes=fs.statSync(path.join(published,file)).size;
        assert.ok(bytes<=100*1024*1024,file+' exceeds the existing size gate');
        assert.equal(manifest.downloads[file].bytes,bytes,file+' recorded size differs');
        assert.equal(manifest.downloads[file].sha256,manifest.files['Published/'+file],file+' recorded hash differs');
    }
    assert.equal(manifest.ordinaryGitLimitBytes,100*1024*1024); assert.deepEqual(manifest.oversizedDownloads,[]);
    const webReceipt=verifyExport(path.join(published,'Web'),'Web');
    assert.deepEqual(manifest.platforms.Web,webReceipt);
    assert.equal(manifest.webRawPayloadBytes,Object.keys(webReceipt.files).filter(p=>/Web\.(data|wasm|framework\.js|loader\.js)$/.test(p)).reduce((sum,p)=>sum+fs.statSync(path.join(published,'Web',p)).size,0),'Recorded Web payload size differs');
    for(const platform of ['Windows','Android']) {
        const receipt=read(path.join(published,platform+'.build-source.json'));
        assert.equal(receipt.sourceDigest,digest); assert.equal(receipt.version,version.Version); assert.equal(receipt.buildNumber,version.BuildNumber);
        assert.equal(receipt.target,platform); assert.deepEqual(manifest.platforms[platform],receipt);
    }
    validatePackages();
    assert.equal(sourceDigest(),digest,'Source changed during verification');
    console.log('Delivery candidate matches current source and all recorded files.');
} else {
    if(fs.existsSync(output)) throw Error('Candidate already exists; preserve it and use --check.');
    const receipts = Object.fromEntries(['Web','Windows','Android'].map(p=>[p,verifyExport(path.join(root,'Builds',p),p)]));
    const companion = path.resolve(companionArgument), stamp = read(path.join(companion,'BuildStamp.json'));
    assert.equal(stamp.version,version.Version); assert.equal(stamp.buildNumber,version.BuildNumber);
    fs.mkdirSync(published,{recursive:true});
    fs.cpSync(path.join(root,'Builds/Web'),path.join(published,'Web'),{recursive:true});
    for(const platform of ['Windows','Android']) fs.copyFileSync(path.join(root,'Builds',platform,'build-source.json'),path.join(published,platform+'.build-source.json'));
    fs.copyFileSync(path.join(root,'Builds/Android/AshenSpire.apk'),path.join(published,'Android.apk'));
    fs.copyFileSync(companion+'.zip',path.join(published,'Companion.zip'));
    fs.copyFileSync(path.join(companion,'BuildStamp.json'),path.join(published,'Companion.build.json'));
    const zipCode = 'import pathlib,sys,zipfile\nbase=pathlib.Path(sys.argv[1])\nwith zipfile.ZipFile(sys.argv[2],"w",zipfile.ZIP_DEFLATED,compresslevel=9) as archive:\n for f in sorted(base.rglob("*")):\n  if f.is_file(): archive.write(f,f.relative_to(base).as_posix())\n';
    for(const platform of ['Web','Windows']) execFileSync(python,['-c',zipCode,path.join(root,'Builds',platform),path.join(published,platform+'.zip')],{stdio:'inherit'});
    validatePackages();
    assert.equal(sourceDigest(),digest,'Source changed during packaging');
    const manifestFiles = Object.fromEntries(files(published).map(p=>[path.relative(output,p).replaceAll('\\','/'),sha(fs.readFileSync(p))]));
    const downloads = Object.fromEntries(['Web.zip','Windows.zip','Android.apk','Companion.zip'].map(name=>[name,{bytes:fs.statSync(path.join(published,name)).size,sha256:manifestFiles['Published/'+name]}]));
    const oversized = Object.entries(downloads).filter(([,r])=>r.bytes>100*1024*1024).map(([name])=>name);
    const manifest = {version:version.Version,buildNumber:version.BuildNumber,sourceDigest:digest,sourceCommit:execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).trim(),sourceWorktreeDirty:execFileSync('git',['status','--porcelain'],{cwd:root,encoding:'utf8'}).trim().length>0,builtAt:new Date().toISOString(),platforms:receipts,files:manifestFiles,downloads,ordinaryGitLimitBytes:100*1024*1024,oversizedDownloads:oversized,webRawPayloadBytes:Object.keys(receipts.Web.files).filter(p=>/Web\.(data|wasm|framework\.js|loader\.js)$/.test(p)).reduce((sum,p)=>sum+fs.statSync(path.join(published,'Web',p)).size,0),physicalDeviceTested:false,graphicalWindowsTested:false,published:false};
    fs.writeFileSync(path.join(output,'candidate.json'),JSON.stringify(manifest,null,2)+'\n');
    if(oversized.length) throw Error('Candidate preserved, but these downloads exceed the existing size gate: '+oversized.join(', '));
    console.log('Staged build '+version.BuildNumber+' delivery candidate: '+output);
}
