// Own a temporary local companion for the two-player Unity browser check.
// Its invitation keys/save stay in the OS temporary directory, outside artifacts.
const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),{spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..'),folder=fs.mkdtempSync(path.join(os.tmpdir(),'ashenspire-coop-browser-'));
const port=Number(process.env.AS_COOP_TEST_PORT||8795),url=`http://127.0.0.1:${port}`,credentials={endpoint:`ws://127.0.0.1:${port}/lan`};
if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Invalid local test port');
const packaged=process.env.AS_COOP_COMPANION_EXE;
const hostArgs=[...(packaged?[]:[path.join(root,'tools/NativeLan/Companion/bin/Release/net8.0/AshenSpire.Companion.dll')]),'--web-root',path.resolve(process.env.AS_COOP_WEB_ROOT||path.join(root,'Published/Web')),'--content-root',path.resolve(process.env.AS_COOP_CONTENT_ROOT||path.join(root,'GameContent/Unity/Original')),'--url',url,'--state',path.join(folder,'host-state.json')];
let host,pending='',started=false,finished=false,runner,restarting=false,restarted=false,restartTimer;
if(process.env.AS_COOP_RESTART==='1'){
 credentials.restartRequest=path.join(folder,'restart-request');credentials.restartAck=path.join(folder,'restart-ack');
 restartTimer=setInterval(()=>{
  if(restarting||restarted||!fs.existsSync(credentials.restartRequest))return;
  restarting=true;host.once('exit',()=>{if(!finished){pending='';launchHost(true);}});host.kill();
 },250);
}
const timeout=setTimeout(()=>finish(new Error('Local test companion did not become ready')),45000);
function finish(error,code=0){if(finished)return;finished=true;clearTimeout(timeout);clearInterval(restartTimer);if(runner&&!runner.killed)runner.kill();if(host&&!host.killed)host.kill();if(error)console.error(error.message);process.exitCode=error?1:code;}
function launchHost(isRestart=false){
 host=spawn(packaged||'dotnet',hostArgs,{cwd:root,stdio:['ignore','pipe','pipe'],windowsHide:true});
 let joinSeen=false,hostSeen=false;
 host.on('error',finish);host.on('exit',()=>{if(!finished&&(!restarting||isRestart))finish(new Error('Local test companion stopped unexpectedly'));});
host.stderr.on('data',()=>{}); // Host diagnostics may contain local secrets; never publish them.
host.stdout.on('data',chunk=>{
 pending+=chunk.toString();let at;
 while((at=pending.indexOf('\n'))>=0){const line=pending.slice(0,at).trim();pending=pending.slice(at+1);if(line.startsWith('Join token: ')){credentials.joinToken=line.slice(12);joinSeen=true;}if(line.startsWith('Host token: ')){credentials.hostToken=line.slice(12);hostSeen=true;}}
 if(pending.length>8192)pending='';
 if(isRestart){if(joinSeen&&hostSeen&&!restarted){restarted=true;restarting=false;fs.writeFileSync(credentials.restartAck,'ready');}return;}
 if(started||!credentials.joinToken||!credentials.hostToken)return;
 started=true;clearTimeout(timeout);const file=path.join(folder,'credentials.json');fs.writeFileSync(file,JSON.stringify(credentials),{mode:0o600});
 runner=spawn(process.execPath,[path.join(root,'tools/native-coop-playtest.cjs'),url,path.resolve(process.argv[2]||'TestResults/NativeCoopBrowser'),file],{cwd:root,stdio:'inherit',windowsHide:true});
 runner.on('error',finish);runner.on('exit',code=>finish(null,code??1));
});
}
launchHost();
process.on('SIGINT',()=>finish(new Error('Co-op browser check interrupted')));
process.on('SIGTERM',()=>finish(new Error('Co-op browser check terminated')));
