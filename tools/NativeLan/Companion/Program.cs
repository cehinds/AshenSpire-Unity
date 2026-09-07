// Program.cs — start the local native cooperative companion and compiled Unity site.
// Default is loopback. Choose --url http://0.0.0.0:8797 explicitly for a trusted LAN;
// use the machine's LAN address in peer browsers. No port forwarding is performed.
using AshenSpire.Companion;
using AshenSpire.Transport;
using Microsoft.Extensions.DependencyInjection;
var values = new Dictionary<string,string>(StringComparer.Ordinal);
for (int i=0;i<args.Length;i+=2)
{
    if (i+1>=args.Length || !new[] { "--web-root", "--content-root", "--url", "--state", "--recover-backup" }.Contains(args[i]) || !values.TryAdd(args[i],args[i+1])) throw new ArgumentException("Usage: --web-root <compiled Web folder> --content-root <Original JSON folder> [--url http://127.0.0.1:8797] [--state <private host save>] [--recover-backup true]");
}
if (!values.TryGetValue("--web-root",out var webRoot) || !values.TryGetValue("--content-root",out var contentRoot)) throw new ArgumentException("Both --web-root and --content-root are required.");
var statePath = Path.GetFullPath(values.GetValueOrDefault("--state",Path.Combine(Environment.CurrentDirectory,"work","NativeLan","host-state.json")));
if (values.TryGetValue("--recover-backup",out var recover)) { if (recover != "true") throw new ArgumentException("Use --recover-backup true only for explicit recovery."); Console.WriteLine("Preserved damaged primary: " + LanStateStore.RecoverBackup(statePath)); }
var options = new LanOptions(webRoot,values.GetValueOrDefault("--url","http://127.0.0.1:8797"),LanOptions.NewToken(),LanOptions.NewToken()) { StatePath = statePath };
await using var app = LanHost.Build(options,new OriginalLanGameFactory(contentRoot));
await app.StartAsync();
Console.WriteLine("Native companion: " + string.Join(", ",app.Urls));
Console.WriteLine("Protocol: " + LanHost.Subprotocol);
// These credentials are local terminal output, not public HTTP or game snapshots.
var credentials = app.Services.GetRequiredService<LanRoom>().LocalCredentials();
Console.WriteLine("Join token: " + credentials.JoinToken);
Console.WriteLine("Host token: " + credentials.HostToken);
Console.WriteLine("Private host save: " + statePath);
Console.WriteLine("Successful intents are saved before acknowledgment. Restart with the same --state file; clients rejoin with their saved seat tokens.");
await app.WaitForShutdownAsync();
