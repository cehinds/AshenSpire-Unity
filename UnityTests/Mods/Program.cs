// Mod packs (OriginalModPacks) and customization settings (OriginalPlayerSettings).
// Run from anywhere: dotnet run --project UnityTests/Mods [-- <repository root>]
// Prints one PASS line per check and exits 1 on the first failure.
var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var passed = 0;
void Check(bool result, string name) { if (!result) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
try
{
    ModPackChecks.Run(root, Check);
    SettingsChecks.Run(Check);
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message.StartsWith("FAIL: ") ? error.Message : "FAIL: unexpected " + error);
    Environment.ExitCode = 1;
    return;
}
Console.WriteLine($"Mods: {passed} checks passed");
