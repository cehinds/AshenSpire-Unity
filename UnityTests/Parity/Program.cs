// Differential checks consume results executed by the pinned JavaScript engine,
// not numbers copied from the C# implementation. Run from the repository root.
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
if (args.Length == 2 && args[0] == "--validate")
{
    try { _ = new OriginalContentCatalog(File.ReadAllText(args[1])); Console.WriteLine("Original content validation: 1 checks passed"); }
    catch (ArgumentException error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
    return;
}
var oracle = JObject.Parse(File.ReadAllText(Path.Combine(root, "UnityTests/Parity/reference.json")));
_ = new OriginalContentCatalog(File.ReadAllText(Path.Combine(root, "GameContent/Unity/Original/content.json")));
var content = (JObject)oracle["content"]!;
var catalog = new OriginalContentCatalog(content.ToString());
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
bool Equal(JToken a, JToken b)
{
    if (a == null || b == null) return a == b;
    bool Numeric(JToken x) => x.Type == JTokenType.Float || x.Type == JTokenType.Integer;
    if (Numeric(a) && Numeric(b)) return (double)a == (double)b;
    if (a is JObject ao && b is JObject bo) return ao.Count == bo.Count && ao.Properties().All(p => Equal(p.Value, bo[p.Name]));
    if (a is JArray aa && b is JArray ba) return aa.Count == ba.Count && aa.Zip(ba).All(pair => Equal(pair.First, pair.Second));
    return JToken.DeepEquals(a, b);
}
void Refuses(Action operation, string message) { try { operation(); } catch (ArgumentException) { checks++; return; } throw new Exception(message); }
foreach (var fixture in oracle["rng"]!)
{
    var random = new RandomStreams((uint)fixture["seed"]!);
    foreach (var stream in ((JObject)fixture["streams"]!).Properties()) foreach (var expected in stream.Value) Check(random.Float(stream.Name) == (double)expected, "RNG " + fixture["seed"] + "/" + stream.Name);
    var snapshot = random.Snapshot();
    Check(Equal(JObject.FromObject(snapshot), fixture["counters"]!), "RNG saved counters");
    var resumed = new RandomStreams(random.Seed, snapshot);
    foreach (var stream in ((JObject)fixture["continuation"]!).Properties()) Check(resumed.Float(stream.Name) == (double)stream.Value, "RNG resume " + stream.Name);
}
foreach (var fixture in oracle["seeds"]!)
{
    var seed = RandomStreams.ParseSeed((string)fixture["text"]!);
    Check(seed == (uint)fixture["value"]! && RandomStreams.DisplaySeed(seed) == (string)fixture["display"]!, "Seed vocabulary " + fixture["text"]);
}
Refuses(() => RandomStreams.ParseSeed("A-B"), "Invalid seed accepted");
var independent = new RandomStreams(1); independent.Float("shop"); Check(independent.Float("shuffle") == new RandomStreams(1).Float("shuffle"), "RNG streams coupled");
var before = independent.Snapshot(); Refuses(() => independent.Int("map", 3, 2), "Invalid RNG range accepted"); Check(Equal(JObject.FromObject(before), JObject.FromObject(independent.Snapshot())), "Rejected RNG consumed a draw");
foreach (var fixture in oracle["formulas"]!) Check(FormulaEvaluator.Evaluate(fixture["formula"], (JObject)fixture["context"]!) == (int)fixture["value"]!, "Formula " + fixture["formula"]);
Refuses(() => FormulaEvaluator.Evaluate(JObject.Parse("{\"f\":\"eval\"}"), new JObject()), "Unknown formula accepted");
Refuses(() => FormulaEvaluator.Evaluate(JObject.Parse("{\"f\":\"hpOf\",\"of\":\"enemy\"}"), new JObject()), "Missing entity accepted");
var rules = DerivedStatCalculator.Resolve((JObject)content["derivedStatRules"]!);
foreach (var fixture in oracle["derived"]!) foreach (var value in ((JObject)fixture["values"]!).Properties())
    Check(Equal(DerivedStatCalculator.Receipt(rules, value.Name, (JObject)fixture["attributes"]!, catalog.Record("classes", (string)fixture["classId"]!)), value.Value), "Derived " + fixture["mode"] + "/" + fixture["classId"] + "/" + value.Name);
foreach (var fixture in oracle["tags"]!) Check(Equal(new JArray(catalog.Tags((string)fixture["family"]!, (JObject)fixture["record"]!)), fixture["expected"]!), "Tag index " + fixture["family"] + "/" + fixture["record"]!["id"]);
foreach (var fixture in oracle["flasks"]!)
{
    var initial = fixture["initial"]!;
    var pool = new FlaskChargePool((int)initial["capacity"]!, (int)initial["hp"]!, (int)initial["mana"]!);
    Check(Equal(pool.Snapshot(), initial), "Starting flask allocation");
    pool.Reallocate((int)fixture["adjusted"]!["hp"]!, (int)fixture["adjusted"]!["mana"]!); Check(Equal(pool.Snapshot(), fixture["adjusted"]!), "Flask reallocation");
    var snap = pool.Snapshot(); Refuses(() => pool.Reallocate(50, 1), "Invalid allocation accepted"); Check(Equal(pool.Snapshot(), snap), "Failed flask transaction changed state");
    pool.Spend("hp"); Check(!pool.Spend("hp"), "Empty flask spent"); pool.Refill(); Check(pool.Spend("hp"), "Refill did not restore charges");
    Check(Equal(new FlaskChargePool(snap).Snapshot(), snap), "Flask save restore");
}
foreach (var fixture in oracle["maps"]!)
{
    var random = new RandomStreams((uint)fixture["seed"]!);
    var config = (JObject)oracle["configs"]!.First(x => (string)x["id"]! == (string)fixture["id"]!)["config"]!;
    var graph = ActMapGenerator.Generate(config, random);
    Check(Equal(graph, fixture["map"]!), "Map differs: act " + fixture["id"] + ", seed " + fixture["seed"]);
    Check(Equal(JObject.FromObject(random.Snapshot()), fixture["counters"]!), "Map RNG draw order differs");
}
var copy = catalog.Record("cards", "strike"); copy["name"] = "Mutated"; Check((string)catalog.Record("cards", "strike")["name"]! == "Strike", "Catalog leaked shared mutable data");
var malformed = (JObject)content.DeepClone(); ((JArray)malformed["cards"]!).Add(malformed["cards"]![0]!.DeepClone()); Refuses(() => new OriginalContentCatalog(malformed.ToString()), "Duplicate content accepted");
foreach (var fixture in oracle["statusCases"]!)
{
    var target = (JObject)fixture["initial"]!.DeepClone();
    var player = (string)fixture["kind"]! == "player" ? target : JObject.Parse("{\"id\":\"player\",\"kind\":\"player\",\"alive\":true,\"statuses\":{}}");
    var context = new CombatContext(catalog, player, (string)fixture["kind"]! == "enemy" ? new[] { target } : Array.Empty<JObject>());
    var statuses = new StatusSystem(context);
    foreach (var amount in new[] { 0, 1, 10, 100, 1 }) statuses.Apply(target, (string)fixture["id"]!, amount, player);
    for (var turn = 0; turn < 3; turn++) statuses.DecayAtTurnEnd(target);
    Check(Equal(target, fixture["target"]!), "Status state " + fixture["kind"] + "/" + fixture["id"]);
    Check(Equal(context.Events(), fixture["events"]!), "Status event order " + fixture["kind"] + "/" + fixture["id"]);
    Check(Equal(context.PendingEffects(), fixture["queue"]!), "Status queued effects " + fixture["kind"] + "/" + fixture["id"]);
}
foreach (var fixture in oracle["damages"]!)
{
    var source = (JObject)fixture["source"]!; var target = (JObject)fixture["target"]!;
    var statuses = new StatusSystem(new CombatContext(catalog, source, new[] { target }));
    Check(AttackDamageCalculator.Calculate(statuses, source, target, (double)fixture["base"]!, fixture["tags"]!.Values<string>().ToArray(), (string)fixture["school"]!) == (int)fixture["value"]!, "Damage order " + fixture);
}
var queueContext = new CombatContext(catalog, new JObject(), Array.Empty<JObject>());
var seen = new List<int>();
queueContext.Register("test", action => { var value = (int)action.Effect["value"]!; seen.Add(value); if (value == 1) queueContext.Enqueue(new CombatAction(JObject.Parse("{\"op\":\"test\",\"value\":3}"), null, null, null)); });
queueContext.Submit(new[] { new CombatAction(JObject.Parse("{\"op\":\"test\",\"value\":1}"), null, null, null), new CombatAction(JObject.Parse("{\"op\":\"test\",\"value\":2}"), null, null, null) });
Check(seen.SequenceEqual(new[] { 1, 2, 3 }) && queueContext.PendingCount == 0, "Trigger actions did not resolve FIFO");
try { queueContext.Submit(new[] { new CombatAction(JObject.Parse("{\"op\":\"missing\"}"), null, null, null) }); throw new Exception("Missing capability accepted"); } catch (NotSupportedException) { Check(queueContext.PendingCount == 0, "Unsupported batch was partially queued"); }
foreach (var fixture in oracle["derived"]!)
{
    var creation = new CreationModel(catalog, (string)fixture["classId"]!, (string)fixture["mode"]!);
    Check(creation.CanBegin && creation.Remaining == 0 && Equal(creation.Attributes(), fixture["attributes"]!), "Creation preset mismatch");
    var original = creation.Attributes(); var changes = 0; creation.Changed += () => changes++;
    Check(!creation.Adjust("missing", 1) && !creation.Adjust("strength", 5) && changes == 0 && Equal(creation.Attributes(), original), "Invalid creation edit mutated state");
    var attribute = original.Properties().First(p => (int)p.Value > 10).Name;
    Check(creation.Adjust(attribute, -1) && creation.Remaining == 1 && !creation.CanBegin && changes == 1, "Creation did not return a point");
    Check(creation.Adjust(attribute, 1) && creation.CanBegin && changes == 2 && Equal(creation.Attributes(), original), "Creation budget round trip failed");
    Check(!creation.Adjust(attribute, 1) && changes == 2, "Creation overspent budget");
}
Console.WriteLine($"Original Unity parity: {checks} checks passed");
