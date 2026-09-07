// Portable host test paths. Tests use real native rules and a tiny static serving
// fixture by default. Receipts/state contain ephemeral test credentials: never publish.
internal static class LanTestPaths
{
    public static readonly string RepositoryRoot = FindRepository();
    public static readonly string ContentRoot = Environment.GetEnvironmentVariable("AS_LAN_CONTENT_ROOT") ?? Path.Combine(RepositoryRoot,"GameContent","Unity","Original");
    public static readonly string OutputRoot = CreateOutput();
    public static readonly string WebRoot = CreateWebFixture();
    public static readonly string CompanionDll = Environment.GetEnvironmentVariable("AS_LAN_COMPANION_DLL") ?? FindCompanion();
    public static readonly string JsLib = Environment.GetEnvironmentVariable("AS_LAN_JSLIB") ?? Path.Combine(RepositoryRoot,"Unity","Assets","AshenSpire","Plugins","WebGL","NativeLan.jslib");
    public static readonly string BridgeHarness = Environment.GetEnvironmentVariable("AS_LAN_BRIDGE_HARNESS") ?? Path.Combine(RepositoryRoot,"tools","NativeLan","Tests","Checks","bridge-checks.mjs");
    private static string FindRepository()
    {
        var configured=Environment.GetEnvironmentVariable("AS_LAN_REPOSITORY_ROOT");
        if(!string.IsNullOrEmpty(configured))return Path.GetFullPath(configured);
        for(var directory=new DirectoryInfo(Environment.CurrentDirectory);directory!=null;directory=directory.Parent)
            if(Directory.Exists(Path.Combine(directory.FullName,"Unity","Assets"))&&Directory.Exists(Path.Combine(directory.FullName,"tools","NativeLan")))return directory.FullName;
        throw new InvalidOperationException("Run from the repository or set AS_LAN_REPOSITORY_ROOT.");
    }
    private static string CreateOutput(){var path=Environment.GetEnvironmentVariable("AS_LAN_TEST_OUTPUT")??Path.Combine(Path.GetTempPath(),"AshenSpire.NativeLan.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static string CreateWebFixture(){var configured=Environment.GetEnvironmentVariable("AS_LAN_WEB_ROOT");if(!string.IsNullOrEmpty(configured))return Path.GetFullPath(configured);var path=Path.Combine(OutputRoot,"StaticFixture");Directory.CreateDirectory(Path.Combine(path,"Build"));File.WriteAllText(Path.Combine(path,"index.html"),"<!doctype html><title>Transport test fixture</title>");File.WriteAllBytes(Path.Combine(path,"Build","Web.wasm"),new byte[]{0,97,115,109,1,0,0,0});return path;}
    private static string FindCompanion(){foreach(var config in new[]{"Release","Debug"}){var path=Path.Combine(RepositoryRoot,"tools","NativeLan","Companion","bin",config,"net8.0","AshenSpire.Companion.dll");if(File.Exists(path))return path;}throw new InvalidOperationException("Build the companion first, or set AS_LAN_COMPANION_DLL.");}
}
