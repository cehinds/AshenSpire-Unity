// Stamp and validate exported Unity players. Source edits require a fresh local build.
// Run after BuildTools.BuildWeb; --check verifies the committed package in CI.
import {createHash} from 'node:crypto';
import {readFileSync,writeFileSync,readdirSync,statSync,mkdirSync,cpSync,existsSync} from 'node:fs';
import {resolve,join,relative} from 'node:path';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const root=resolve(fileURLToPath(new URL('..',import.meta.url)));
function files(dir){return readdirSync(dir).sort().flatMap(n=>{const p=join(dir,n);return statSync(p).isDirectory()?files(p):[p];});}
const sourceFiles=['Unity/Assets','Unity/Packages','Unity/ProjectSettings','GameContent/Unity'].flatMap(p=>files(join(root,p))).sort();
const hash=createHash('sha256');
for(const p of sourceFiles){
 hash.update(relative(root,p).replaceAll('\\','/'));
 const bytes=readFileSync(p);
 hash.update(/\.(png|jpg|webp|ttf|otf)$/i.test(p)?bytes:bytes.toString('utf8').replaceAll('\r\n','\n'));
}
const digest=hash.digest('hex');
const version=JSON.parse(readFileSync(join(root,'GameContent/Unity/version.json'),'utf8'));
if(!/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(version.Version)||!Number.isSafeInteger(version.BuildNumber)||version.BuildNumber<1||!version.Stage)throw Error('Invalid four-part version/build metadata');
const destination=join(root,'Published');
execFileSync(process.env.PYTHON || (process.platform==='win32'?'python':'python3'),[join(root,'tools/validate-companion.py'),root],{cwd:root,stdio:'inherit'});
execFileSync(process.env.PYTHON || (process.platform==='win32'?'python':'python3'),[join(root,'tools/validate-unity-targets.py'),root],{cwd:root,stdio:'inherit'});
function platformStamp(platform,path){
 if(!existsSync(path))throw Error(`Missing ${platform} build receipt. Build Windows and Android before packaging Web.`);
 const stamp=JSON.parse(readFileSync(path,'utf8'));
 if(stamp.sourceDigest!==digest||stamp.version!==version.Version||stamp.buildNumber!==version.BuildNumber||stamp.target!==platform)throw Error(`${platform} was built from different source/version. Rebuild all targets before packaging.`);
 return stamp;
}
function verifyWebExport(directory){
 const stamp=platformStamp('Web',join(directory,'build-source.json'));
 if(!stamp.files?.['index.html']||!stamp.files?.['Build/Web.wasm'])throw Error('Web export is missing Unity-written file hashes; rebuild Web.');
 const actual=files(directory).map(file=>relative(directory,file).replaceAll('\\','/')).filter(file=>file!=='build-source.json').sort();
 if(JSON.stringify(actual)!==JSON.stringify(Object.keys(stamp.files).sort()))throw Error('Web export contains missing or unreceipted files. Preserve and inspect unexpected files before rebuilding.');
 for(const file of actual)if(createHash('sha256').update(readFileSync(join(directory,file))).digest('hex')!==stamp.files[file])throw Error(`Web export differs from its Unity receipt: ${file}`);
 return stamp;
}
const platformReceipts={Windows:platformStamp('Windows',join(destination,'Windows.build-source.json')),Android:platformStamp('Android',join(destination,'Android.build-source.json'))};
if(process.argv.includes('--check')){
 const manifest=JSON.parse(readFileSync(join(destination,'build.json'),'utf8'));
 for(const required of ['Windows.zip','Android.apk','Companion.zip','Companion.build.json','Windows.build-source.json','Android.build-source.json'])if(!manifest.files?.[required])throw Error(`Missing mandatory package manifest entry: ${required}`);
 if(manifest.sourceDigest!==digest)throw new Error('Unity source differs from packaged build. Run tools/build-unity.ps1 before publishing.');
 if(manifest.version!==version.Version||manifest.buildNumber!==version.BuildNumber)throw Error('Package version/build number differs from version.json');
 verifyWebExport(join(destination,'Web'));
 const index=readFileSync(join(destination,'Web/index.html'),'utf8');
 if(index.includes('__ASHENSPIRE_VERSION__')||!index.includes('UNITY '+manifest.version)||!index.includes("productVersion:'"+manifest.version+"'"))throw new Error('Web version differs from package metadata; rebuild Web.');
 if(index.includes('__ASHENSPIRE_BUILD_TOKEN__')||(index.match(new RegExp('\\?build='+manifest.sourceDigest,'g'))||[]).length!==4)throw new Error('Web loader/runtime URLs must all carry the current source digest. Rebuild Web.');
 const renderer=JSON.parse(readFileSync(join(destination,'Web/renderer-workaround.json'),'utf8'));
 if(renderer.UnityVersion!==manifest.unityVersion||renderer.Algorithm!=='consolidate-then-count-plus-two-per-range-v1'||renderer.InputSha256!=='bfe25a4e12ab85d84fd4f905c17c472599a76e42bceeb5ed02349c3dbe50bcac'||!/^[a-f0-9]{64}$/.test(renderer.OutputSha256)||renderer.OutputSha256===renderer.InputSha256)throw new Error('Missing or incompatible Web renderer build receipt.');
 for(const [path,expected] of Object.entries(manifest.files)){
  const actual=createHash('sha256').update(readFileSync(join(destination,path))).digest('hex');
  if(actual!==expected)throw new Error(`Packaged build changed: ${path}`);
 }
 console.log(`Unity package: ${Object.keys(manifest.files).length+4} checks passed`);
}else{
 const build=join(root,'Builds/Web');
 if(!existsSync(join(build,'index.html')))throw new Error('No exported Web player; run Unity BuildTools.BuildWeb first.');
 const buildStamp=verifyWebExport(build);
 if(buildStamp.sourceDigest!==digest)throw new Error('Source changed after Unity exported this player; rebuild before packaging.');
 mkdirSync(destination,{recursive:true});cpSync(build,join(destination,'Web'),{recursive:true});
 verifyWebExport(join(destination,'Web'));
 if(process.platform==='win32'){
  execFileSync('powershell.exe',['-NoProfile','-Command','Compress-Archive -Path Published/Web/* -DestinationPath Published/Web.zip -CompressionLevel Optimal -Force'],{cwd:root});
 }else{
  execFileSync('python3',['-c','import shutil; shutil.make_archive("Published/Web", "zip", "Published/Web")'],{cwd:root});
 }
 const manifest={version:version.Version,buildNumber:version.BuildNumber,stage:version.Stage,unityVersion:readFileSync(join(root,'Unity/ProjectSettings/ProjectVersion.txt'),'utf8').split('\n')[0].split(': ')[1].trim(),sourceCommit:execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).trim(),sourceDigest:digest,builtAt:buildStamp.builtAt,files:{}};
 for(const path of files(join(destination,'Web'))){manifest.files[relative(destination,path).replaceAll('\\','/')]=createHash('sha256').update(readFileSync(path)).digest('hex');}
 manifest.files['Web.zip']=createHash('sha256').update(readFileSync(join(destination,'Web.zip'))).digest('hex');
 for(const file of ['Windows.zip','Android.apk'])manifest.files[file]=createHash('sha256').update(readFileSync(join(destination,file))).digest('hex');
 for(const file of ['Windows.build-source.json','Android.build-source.json','Companion.zip','Companion.build.json']){manifest.files[file]=createHash('sha256').update(readFileSync(join(destination,file))).digest('hex');}
 manifest.platforms={Web:buildStamp,...platformReceipts};
 writeFileSync(join(destination,'build.json'),JSON.stringify(manifest,null,2)+'\n');
 console.log(`Packaged ${Object.keys(manifest.files).length} Unity Web files; source ${digest}`);
}
