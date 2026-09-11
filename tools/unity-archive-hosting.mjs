// Pure hosting plan for immutable Unity archives. Only the data fetch location
// changes; wasm stays on Pages for the correct streaming MIME type. Loader,
// framework, product identity and save configuration also stay local.
// Receipts identify original and derived HTML plus exact committed payload hashes.
// This does not establish remote availability/CORS or fetch/modify any artifact.
import {createHash} from 'node:crypto';
import {publishedRelativePath} from './unity-git-blobs.mjs';

const objectId=/^(?:[a-f0-9]{40}|[a-f0-9]{64})$/;
const sha256=/^[a-f0-9]{64}$/;
const hash=value=>createHash('sha256').update(value).digest('hex');
const remoteRoot='https://raw.githubusercontent.com/cehinds/AshenSpire-Unity/';
const payloads=[['dataUrl','Web.data']];
const plainObject=value=>value!==null&&typeof value==='object'&&!Array.isArray(value);
const propertyPattern=(name,file)=>new RegExp('(\\b'+name+'\\s*:\\s*)(["\'])(Build\\/'+file.replaceAll('.','\\.')+'(?:[?#][^"\'<>\\\\\\x00-\\x20]*)?)\\2(?=\\s*[,}])','g');

export function planArchiveHosting(originalHtml,build){
 if(typeof originalHtml!=='string'||!plainObject(build)||!objectId.test(build.commit))throw Error('Archive hosting requires HTML and a complete commit ID');
 if(/<base\b/i.test(originalHtml))throw Error('Unsupported archive player: base element');
 if(!Array.isArray(build.tree)||!plainObject(build.manifest?.files))throw Error('Archive hosting requires its exact artifact tree and manifest');
 const paths=new Set(),spellings=new Map();
 for(const file of build.tree){
  const path=file?.path;const relative=publishedRelativePath(path);
  if(!relative.startsWith('Web/'))throw Error('Archive tree must contain only Web artifacts');
  if(!objectId.test(file.blob)||file.mode!=null&&file.mode!=='100644')throw Error('Unsupported archive blob');
  if(paths.has(path))throw Error('Duplicate archive artifact');
  const parts=relative.split('/');
  for(let n=1;n<=parts.length;n++){
   const prefix=parts.slice(0,n).join('/'),lower=prefix.toLowerCase();
   if(spellings.has(lower)&&spellings.get(lower)!==prefix)throw Error('Case-colliding archive artifact');
   spellings.set(lower,prefix);
  }
  paths.add(path);
 }
 for(const [path,value] of Object.entries(build.manifest.files)){
  publishedRelativePath('Published/'+path);
  if(!sha256.test(value))throw Error('Archive manifest contains an invalid SHA256');
 }
 const required=['Web/index.html','Web/Build/Web.loader.js','Web/Build/Web.framework.js','Web/Build/Web.data','Web/Build/Web.wasm'];
 for(const path of required){
  if(!paths.has('Published/'+path)||!sha256.test(build.manifest.files[path]))throw Error('Missing receipted archive artifact: '+path);
 }
 const originalIndexSha256=hash(originalHtml);
 if(originalIndexSha256!==build.manifest.files['Web/index.html'])throw Error('Original archive index does not match its manifest');
 const patterns={
  dataUrl:propertyPattern('dataUrl','Web.data'),
  codeUrl:propertyPattern('codeUrl','Web.wasm'),
  frameworkUrl:propertyPattern('frameworkUrl','Web.framework.js')
 };
 for(const [name,pattern] of Object.entries(patterns)){
  if([...originalHtml.matchAll(new RegExp('(?:\\b'+name+'|["\']'+name+'["\'])\\s*:','g'))].length!==1||[...originalHtml.matchAll(pattern)].length!==1)
   throw Error('Unsupported archive player: expected one literal '+name);
 }
 const loader=/(<script\b[^>]*\bsrc\s*=\s*)(["'])(Build\/Web\.loader\.js(?:[?#][^"'<>\\\x00-\x20]*)?)\2/gi;
 if([...originalHtml.matchAll(loader)].length!==1)throw Error('Unsupported archive player: expected one local loader');
 let html=originalHtml;const dataUrls={},omittedPaths=[],runtime=[];
 for(const [name,file] of payloads){
  const path='Published/Web/Build/'+file;
  html=html.replace(patterns[name],(_,prefix,quote,url)=>{
   const suffix=url.slice(('Build/'+file).length);
   const remote=remoteRoot+build.commit+'/'+path+suffix;
   dataUrls[name]=remote;
   return prefix+quote+remote+quote;
  });
  omittedPaths.push(path);
  runtime.push({path,url:dataUrls[name],sha256:build.manifest.files[path.slice('Published/'.length)]});
 }
 return {html,omittedPaths,dataUrls,receipt:{schemaVersion:1,policy:'exact-commit-raw-data-v1',commit:build.commit,
  originalIndexSha256,hostedIndexSha256:hash(html),runtime}};
}
