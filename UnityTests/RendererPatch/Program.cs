using System.Reflection;
using System.Runtime.Loader;
using AshenSpire.Editor.Rendering;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine.UIElements.UIR;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var original = GpuUpdaterStaged.Run(new uint[] { 0, 4500 }, new uint[] { 4000, 4000 });
Check(original[0] < original[1], "baseline fixture must reproduce allocation before range growth");
byte[] input = File.ReadAllBytes(Assembly.GetExecutingAssembly().Location);
ModuleDefinition Read() => ModuleDefinition.ReadModule(new MemoryStream(input));
byte[] Rewrite(ModuleDefinition module)
{
    using var bytes = new MemoryStream(); module.Write(bytes); return bytes.ToArray();
}
using var candidate = Read();
WebStagingPatch.Apply(candidate);
byte[] patched = Rewrite(candidate);
var context = new AssemblyLoadContext("RendererPatchExecution", true);
try
{
    var assembly = context.LoadFromStream(new MemoryStream(patched));
    var run = assembly.GetType(WebStagingPatch.TypeName)!.GetMethod("Run")!;
    int[] Execute(uint[] starts, uint[] counts) => (int[])run.Invoke(null, new object[] { starts, counts })!;
    foreach (var (starts, counts) in new[] {
        (new uint[] {0,4500},new uint[] {4000,4000}),
        (new uint[] {1,11,21},new uint[] {1,1,1}),
        (new uint[] {0,8193},new uint[] {8191,1}),
        (new uint[] {1},new uint[] {8192}),
        (new uint[] {0,262145},new uint[] {262144,260000}),
        (Array.Empty<uint>(),Array.Empty<uint>()) })
    {
        var result = Execute(starts, counts);
        Check(result[0] >= result[1], "consolidation, sparse alignment and tier boundaries fit allocated capacity");
    }
    var random = new Random(29);
    for (int sample = 0; sample < 1000; sample++)
    {
        var starts = new uint[random.Next(1,65)]; var counts = new uint[starts.Length]; uint offset = (uint)random.Next(2);
        for(int i=0;i<starts.Length;i++){starts[i]=offset;counts[i]=(uint)random.Next(1,10000);offset+=counts[i]+(uint)random.Next(0,1000);}
        var result = Execute(starts, counts);
        Check(result[0] >= result[1], "random sparse/dense aligned copy fits reserved capacity");
    }
    bool rejectedOverflow = false;
    try { Execute(new uint[] { 0 }, new uint[] { uint.MaxValue }); }
    catch(TargetInvocationException e) when(e.InnerException is OverflowException){rejectedOverflow=true;}
    Check(rejectedOverflow,"unsafe capacity overflow fails explicitly");
}
finally { context.Unload(); }
void Reject(Action<ModuleDefinition> mutate, string name)
{
    using var module = Read(); mutate(module); bool rejected = false;
    try { WebStagingPatch.Apply(module); } catch(InvalidDataException){rejected=true;}
    Check(rejected,name);
}
Reject(module => module.Types.Single(t=>t.FullName==WebStagingPatch.TypeName).Name="DifferentRenderer", "unknown renderer rejected");
Reject(WebStagingPatch.Apply,"double patch rejected");
Reject(module => {
    var method=module.Types.Single(t=>t.FullName==WebStagingPatch.TypeName).Methods.Single(m=>m.Name=="PrepareCopyRanges");
    method.Body.Instructions.First(i=>i.OpCode==OpCodes.Ldc_R4).Operand=.8f;
},"changed consolidation contract rejected");
Console.WriteLine($"Web renderer patch: {checks} checks passed");
