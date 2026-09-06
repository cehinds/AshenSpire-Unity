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
const destination=join(root,'Published');
if(process.argv.includes('--check')){
 const manifest=JSON.parse(readFileSync(join(destination,'build.json'),'utf8'));
 if(manifest.sourceDigest!==digest)throw new Error('Unity source differs from packaged build. Run tools/build-unity.ps1 before publishing.');
 const index=readFileSync(join(destination,'Web/index.html'),'utf8');
 if(index.includes('__ASHENSPIRE_VERSION__')||!index.includes('UNITY '+manifest.version)||!index.includes("productVersion:'"+manifest.version+"'"))throw new Error('Web version differs from package metadata; rebuild Web.');
 if(index.includes('__ASHENSPIRE_BUILD_TOKEN__')||(index.match(new RegExp('\\?build='+manifest.sourceDigest,'g'))||[]).length!==4)throw new Error('Web loader/runtime URLs must all carry the current source digest. Rebuild Web.');
 for(const [path,expected] of Object.entries(manifest.files)){
  const actual=createHash('sha256').update(readFileSync(join(destination,path))).digest('hex');
  if(actual!==expected)throw new Error(`Packaged build changed: ${path}`);
 }
 console.log(`Unity package: ${Object.keys(manifest.files).length+3} checks passed`);
}else{
 const build=join(root,'Builds/Web');
 if(!existsSync(join(build,'index.html')))throw new Error('No exported Web player; run Unity BuildTools.BuildWeb first.');
 const buildStamp=JSON.parse(readFileSync(join(build,'build-source.json'),'utf8'));
 if(buildStamp.sourceDigest!==digest)throw new Error('Source changed after Unity exported this player; rebuild before packaging.');
 mkdirSync(destination,{recursive:true});cpSync(build,join(destination,'Web'),{recursive:true});
 if(process.platform==='win32'){
  execFileSync('powershell.exe',['-NoProfile','-Command','Compress-Archive -Path Published/Web/* -DestinationPath Published/Web.zip -CompressionLevel Optimal -Force'],{cwd:root});
 }else{
  execFileSync('python3',['-c','import shutil; shutil.make_archive("Published/Web", "zip", "Published/Web")'],{cwd:root});
 }
 const manifest={version:'0.4.0',stage:'Three-act campaign',unityVersion:readFileSync(join(root,'Unity/ProjectSettings/ProjectVersion.txt'),'utf8').split('\n')[0].split(': ')[1].trim(),sourceCommit:execFileSync('git',['rev-parse','HEAD'],{cwd:root,encoding:'utf8'}).trim(),sourceDigest:digest,builtAt:buildStamp.builtAt,files:{}};
 for(const path of files(join(destination,'Web'))){manifest.files[relative(destination,path).replaceAll('\\','/')]=createHash('sha256').update(readFileSync(path)).digest('hex');}
 manifest.files['Web.zip']=createHash('sha256').update(readFileSync(join(destination,'Web.zip'))).digest('hex');
 if(existsSync(join(destination,'Windows.zip')))manifest.files['Windows.zip']=createHash('sha256').update(readFileSync(join(destination,'Windows.zip'))).digest('hex');
 if(existsSync(join(destination,'Android.apk')))manifest.files['Android.apk']=createHash('sha256').update(readFileSync(join(destination,'Android.apk'))).digest('hex');
 writeFileSync(join(destination,'build.json'),JSON.stringify(manifest,null,2)+'\n');
 console.log(`Packaged ${Object.keys(manifest.files).length} Unity Web files; source ${digest}`);
}
