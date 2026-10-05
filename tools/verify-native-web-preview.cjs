// Temporary, read-only loopback server for the exact receipted player files.
// The server and owned child checks close when verification finishes or fails.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..'),web=path.resolve(process.argv[2]||'');
let server;
async function run(script,args){
 await new Promise((resolve,reject)=>{
  const child=spawn(process.execPath,[path.join(__dirname,script),...args],{cwd:root,stdio:'inherit',windowsHide:true});
  const deadline=setTimeout(()=>{child.kill();reject(Error('Verification exceeded its 30-minute budget: '+script));},30*60*1000);
  child.once('error',error=>{clearTimeout(deadline);reject(error);});
  child.once('exit',code=>{clearTimeout(deadline);code===0?resolve():reject(Error(script+' exited '+code));});
 });
}
(async()=>{
 if(!process.argv[2])throw Error('Pass an exported Web folder.');
 await run('unity-verify-parity-preview.mjs',[web]);
 const receipt=JSON.parse(fs.readFileSync(path.join(web,'build-source.json'),'utf8').replace(/^\uFEFF/,''));
 const allowed=new Set(['build-source.json',...Object.keys(receipt.files)]);
 const mime={'.html':'text/html','.js':'application/javascript','.json':'application/json','.wasm':'application/wasm','.css':'text/css','.png':'image/png','.ico':'image/x-icon'};
 server=http.createServer((request,response)=>{
  if(!['GET','HEAD'].includes(request.method)){response.writeHead(405).end();return;}
  let file;try{file=decodeURIComponent(new URL(request.url,'http://localhost').pathname).replace(/^\//,'')||'index.html';}
  catch{response.writeHead(400).end();return;}
  if(!allowed.has(file)){response.writeHead(404).end();return;}
  const absolute=path.resolve(web,file);
  if(!absolute.startsWith(web+path.sep)){response.writeHead(404).end();return;}
  const headers={'Content-Type':mime[path.extname(file)]||'application/octet-stream','Cache-Control':'no-store'};
  if(file.endsWith('.br')||file.endsWith('.gz')){
   headers['Content-Encoding']=file.endsWith('.br')?'br':'gzip';
   headers['Content-Type']=mime[path.extname(file.slice(0,-3))]||'application/octet-stream';
  }
  response.writeHead(200,headers);
  if(request.method==='HEAD'){response.end();return;}
  fs.createReadStream(absolute).on('error',()=>response.destroy()).pipe(response);
 });
 await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',resolve);});
 const url='http://127.0.0.1:'+server.address().port+'/';
 console.log('Temporary build '+receipt.buildNumber+' verification URL: '+url);
 const checks={
  art:['native-upstream-art-playtest.cjs','UpstreamStarterArt'],
  settings:['native-settings-reset-playtest.cjs','SettingsReset'],
  defaults:['native-settings-defaults-playtest.cjs','SettingsDefaults'],
  combat:['native-painted-combat-playtest.cjs','PaintedCombat']
 };
 const requested=(process.argv[3]||'art,settings,defaults,combat').split(',');
 for(const name of requested){
  if(!checks[name])throw Error('Unknown preview check: '+name);
  const [script,folder]=checks[name];
  await run(script,[url,path.join(root,'TestResults/'+folder+'/build'+receipt.buildNumber)]);
 }
 console.log('Player checks passed; screenshots still require visual review.');
})().catch(error=>{console.error(error);process.exitCode=1;}).finally(()=>{if(server){server.closeAllConnections();server.close();}});
