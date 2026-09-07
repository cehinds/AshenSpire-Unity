// Own a temporary local companion for the two-player Unity browser check.
// Its invitation keys/save stay in the OS temporary directory, outside artifacts.
const fs=require('node:fs'),os=require('node:os'),path=require('node:path'),{spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..'),folder=fs.mkdtempSync(path.join(os.tmpdir(),'ashenspire-coop-browser-'));
const port=Number(process.env.AS_COOP_TEST_PORT||8795),url=`http://127.0.0.1:${port}`,credentials={endpoint:`ws://127.0.0.1:${port}/lan`};
if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Invalid local test port');
const host=spawn('dotnet',[path.join(root,'tools/NativeLan/Companion/bin/Release/net8.0/AshenSpire.Companion.dll'),'--web-root',path.resolve(process.env.AS_COOP_WEB_ROOT||path.join(root,'Published/Web')),'--content-root',path.join(root,'GameContent/Unity/Original'),'--url',url,'--state',path.join(folder,'host-state.json')],{cwd:root,stdio:['ignore','pipe','pipe'],windowsHide:true});
let pending='',started=false,finished=false,runner;
const timeout=setTimeout(()=>finish(new Error('Local test companion did not become ready')),45000);
function finish(error,code=0){if(finished)return;finished=true;clearTimeout(timeout);if(runner&&!runner.killed)runner.kill();if(!host.killed)host.kill();if(error)console.error(error.message);process.exitCode=error?1:code;}
host.on('error',finish);host.on('exit',()=>{if(!finished)finish(new Error('Local test companion stopped unexpectedly'));});
host.stderr.on('data',()=>{}); // Host diagnostics may contain local secrets; never publish them.
host.stdout.on('data',chunk=>{
 pending+=chunk.toString();let at;
 while((at=pending.indexOf('\n'))>=0){const line=pending.slice(0,at).trim();pending=pending.slice(at+1);if(line.startsWith('Join token: '))credentials.joinToken=line.slice(12);if(line.startsWith('Host token: '))credentials.hostToken=line.slice(12);}
 if(pending.length>8192)pending='';
 if(started||!credentials.joinToken||!credentials.hostToken)return;
 started=true;clearTimeout(timeout);const file=path.join(folder,'credentials.json');fs.writeFileSync(file,JSON.stringify(credentials),{mode:0o600});
 runner=spawn(process.execPath,[path.join(root,'tools/native-coop-playtest.cjs'),url,path.resolve(process.argv[2]||'TestResults/NativeCoopBrowser'),file],{cwd:root,stdio:'inherit',windowsHide:true});
 runner.on('error',finish);runner.on('exit',code=>finish(null,code??1));
});
process.on('SIGINT',()=>finish(new Error('Co-op browser check interrupted')));
process.on('SIGTERM',()=>finish(new Error('Co-op browser check terminated')));
