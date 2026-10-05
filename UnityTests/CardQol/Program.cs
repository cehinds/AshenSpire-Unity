using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
var root = Path.Combine(Directory.GetCurrentDirectory(), "GameContent/Unity/Original");
var catalog = new OriginalContentCatalog(File.ReadAllText(Path.Combine(root, "content.json")));
var supplement = JObject.Parse(File.ReadAllText(Path.Combine(root, "event-choices.json")));
var mechanics = JObject.Parse(File.ReadAllText(Path.Combine(root, "mechanics.json")));
var progression = new AttributeProgression(JObject.Parse(File.ReadAllText(Path.Combine(root, "progression.json"))));
var checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
JObject Player(string cls) => new OriginalCharacterBuilder(catalog, progression, mechanics).Build(new CreationModel(catalog, cls, "leanStandard", progression));
foreach (var cls in new[] { "reaver", "starseer", "rogue", "herald" })
{
    var game = OriginalGameSession.Start(catalog, supplement, mechanics, Player(cls), 17);
    game.Enter(game.LegalNodeIds[0]); Check(game.Phase == OriginalRunPhase.Combat, cls + " opening combat");
    var before = game.Snapshot(); var events = game.LastEvents; var changed = 0; game.Changed += () => changed++;
    foreach (JObject instance in game.Hand)
    {
        var id = (string)instance["instanceId"]!; var target = (string)game.Enemies[0]["id"]!;
        var preview = game.PreviewCard(id, target);
        Check(JToken.DeepEquals(before, game.Snapshot()) && JToken.DeepEquals(events, game.LastEvents) && changed == 0, cls + " preview preserves snapshot, RNG, receipts and notifications");
        Check(JToken.DeepEquals(preview, game.PreviewCard(id, target)), "Repeated preview is stable");
        if ((bool?)preview["available"] != true) continue;
        var branch = OriginalGameSession.Restore(before); branch.Play(id, target);
        foreach (JObject c in (JArray)preview["changes"]!)
        {
            var actual = (string)c["id"] == "player" ? branch.Player : branch.Enemies.FirstOrDefault(e => (string)e["id"] == (string)c["id"]);
            if (actual == null) continue; // A victory removes the combat surface.
            foreach (JObject value in (JArray)c["values"]!) Check(JToken.DeepEquals(value["after"], actual[(string)value["key"]!]), "Preview matches actual paid pool/HP/guard outcome");
            foreach (JObject status in (JArray)c["statuses"]!) Check((int)status["after"]! == StatusSystem.Stacks((JObject)actual, (string)status["id"]!), "Preview matches actual status outcome");
        }
    }
    Check((bool?)game.PreviewCard("missing", "e1")["available"] == false, "Missing card refused");
    Check(JToken.DeepEquals(before, game.Snapshot()), "Refusal preserves state");
}
var guarded = OriginalGameSession.Start(catalog, supplement, mechanics, Player("reaver"), 17); guarded.Enter(guarded.LegalNodeIds[0]);
var saved = guarded.Snapshot(); saved["run"]!["room"]!["combatSnapshot"]!["enemies"]![0]!["block"] = 15;
guarded = OriginalGameSession.Restore(saved);
var strike = guarded.Hand.OfType<JObject>().First(i => (string)guarded.Resolve(i)["name"] == "Slashing Strike");
var result = guarded.PreviewCard((string)strike["instanceId"]!, "e1");
var enemyChange = result["changes"]!.First(c => (string)c["id"] == "e1");
Check(!enemyChange["values"]!.Any(v => (string)v["key"] == "hp"), "Guard prevents HP loss in preview");
Check((int)enemyChange["values"]!.First(v => (string)v["key"] == "block")["after"]! == 6, "Guard preview resolves real mitigation");
Check((bool?)guarded.PreviewCard((string)strike["instanceId"]!, "invalid")["available"] == false && JToken.DeepEquals(saved, guarded.Snapshot()), "Invalid target preview refuses without mutation");
var coop = new OriginalCoopRun(catalog, supplement, mechanics, 17); coop.AddMember("host", "Host", Player("reaver")); coop.AddMember("guest", "Guest", Player("reaver"));
void Command(string seat, JObject intent) { var seq = (long)coop.View()["party"]!.First(m => (string)m["id"] == seat)["sequence"]! + 1; Check((bool)coop.Execute(seat, seq, intent)["ok"]!, "Co-op setup command accepted"); }
Command("host", new JObject { ["type"] = "start" }); var node = (string)coop.View()["reachableIds"]![0]!;
foreach (var seat in new[] { "host", "guest" }) Command(seat, new JObject { ["type"] = "chooseNode", ["nodeId"] = node });
var coopBefore = coop.Snapshot();
foreach (var seat in new[] { "host", "guest" })
{
    var view = coop.View(seat); Check(JToken.DeepEquals(coopBefore, coop.Snapshot()), "Host-generated previews preserve party state and RNG");
    Check(!view["scene"]!["players"]!.Any(p => p["piles"] != null), "Public preview reveals no peer pile");
    foreach (JObject row in (JArray)view["local"]!["hand"]!)
    {
        foreach (var p in ((JObject)row["previews"]!).Properties())
        {
            Check(p.Value["rng"] == null && p.Value["piles"] == null && p.Value["events"] == null, "Preview contains only public outcome fields");
            if ((bool?)p.Value["available"] != true) continue;
            var copy = OriginalCoopRun.Restore(coopBefore, false); var seq = (long)view["local"]!["sequence"]! + 1;
            Check((bool)copy.Execute(seat, seq, new JObject { ["type"] = "playCard", ["cardInstanceId"] = row["instance"]!["instanceId"]!.DeepClone(), ["targetId"] = p.Name == "_" ? null : p.Name })["ok"]!, "Preview does not promise refused co-op command");
            var actual = copy.View(seat);
            foreach (var change in p.Value["changes"]!)
            {
                var entity = actual["scene"]!["enemies"]!.FirstOrDefault(e => (string)e["id"] == (string)change["id"]) ?? actual["scene"]!["players"]!.FirstOrDefault(e => (string)e["id"] == (string)change["id"])?["entity"];
                if (entity == null) continue;
                foreach (var v in change["values"]!) Check(JToken.DeepEquals(v["after"], entity[(string)v["key"]!]), "Host preview matches actual co-op command outcome");
            }
        }
    }
}
Check(OriginalCardPreview.Random(new JObject { ["effects"] = new JArray(new JObject { ["target"] = "randomEnemy" }) }), "Random-target previews stay unspecified");
Check(!OriginalCardFlick.DistanceMet(0, 100, 0, 37), "Short upward drag is not a flick");
Check(OriginalCardFlick.DistanceMet(0, 100, 0, 36), "64-pixel threshold is inclusive");
Check(!OriginalCardFlick.DistanceMet(0, 100, 64, 36), "Horizontal tie is not a flick");
Check(!OriginalCardFlick.Qualifies(0, 100, 0, 30, 60, .101), "Slow release is not a flick");
Check(OriginalCardFlick.Qualifies(0, 100, 0, 30, 60, .1), "300-pixel-per-second threshold is inclusive");
Check(!OriginalCardFlick.Qualifies(0, 100, 0, 30, 60, 0), "Zero elapsed time is not a flick");
var cachedView = coop.View("host");
JObject Attack(JObject view) => ((JArray)view["local"]!["hand"]!).OfType<JObject>().FirstOrDefault(r => (string)r["card"]!["type"] == "attack" && OriginalCardCostText.IsAffordable((JObject)r["cost"]!, (JObject)view["local"]!["combat"]!["entity"]!))!;
for (var turn = 0; (Attack(cachedView) == null || Attack(coop.View("guest")) == null) && turn < 5; turn++)
{
    Command("host", new JObject { ["type"] = "endTurn" }); Command("guest", new JObject { ["type"] = "endTurn" }); cachedView = coop.View("host");
}
var cachedStrike = Attack(cachedView); Check(cachedStrike != null && Attack(coop.View("guest")) != null, "Normal draws provide attack cards for the peer-refresh scenario");
var cachedTarget = (string)cachedView["scene"]!["enemies"]![0]!["id"]!;
Command("guest", new JObject { ["type"] = "playCard", ["cardInstanceId"] = Attack(coop.View("guest"))["instance"]!["instanceId"]!.DeepClone(), ["targetId"] = cachedTarget });
var refreshedView = coop.View("host"); var refreshedStrike = refreshedView["local"]!["hand"]!.First(r => (string)r["instance"]!["instanceId"] == (string)cachedStrike["instance"]!["instanceId"]);
Check(!JToken.DeepEquals(cachedStrike["previews"]![cachedTarget], refreshedStrike["previews"]![cachedTarget]), "Peer play invalidates preview cache before the next authoritative view");
var bleed = new JObject { ["stacks"] = 0, ["meter"] = new JObject { ["value"] = 3, ["max"] = 12 } };
var bleedBefore = bleed.DeepClone();
Check(OriginalStatusText.Describe("bleed", bleed) == "Bleed buildup 3/12", "Meter display does not hide buildup behind zero stacks");
Check(JToken.DeepEquals(bleed, bleedBefore), "Formatting preserves the status instance");
Check(OriginalStatusText.Describe("bleed", bleed, "Authored bleed") == "Authored bleed buildup 3/12", "Inspection retains authored status name");
Check(OriginalStatusText.Describe("strength", new JObject { ["stacks"] = 2 }) == "Strength 2", "Ordinary stacked statuses retain their value");
Check(OriginalStatusText.Describe("bleed", new JObject { ["stacks"] = 9, ["meter"] = new JObject { ["value"] = 0, ["max"] = 12 } }) == "Bleed buildup 0/12", "Reset meter takes precedence over residual stacks");
Check(OriginalStatusText.Describe("bleed", new JObject { ["meter"] = new JObject { ["value"] = 3 } }) == "Bleed buildup 3", "Missing maximum does not invent a threshold");
Check(OriginalStatusText.Describe("bleed", new JObject { ["meter"] = new JObject { ["value"] = 3, ["max"] = 0 } }) == "Bleed buildup 3", "Nonpositive maximum is omitted");
Check(OriginalStatusText.Describe("strength", null) == "Strength 0", "Absent instance formats without changing a save");
var bleedDefinition = catalog.Record("statuses", "bleed"); var definitionBefore = bleedDefinition.DeepClone();
Check(OriginalStatusText.PreviewName(bleedDefinition) == "Bleed buildup", "Preview distinguishes buildup from stacks using authored mechanics");
Check(JToken.DeepEquals(bleedDefinition, definitionBefore), "Preview label preserves its authored definition");
Check(OriginalStatusText.PreviewName(new JObject { ["id"] = "strength", ["name"] = "Strength" }) == "Strength", "Nonmeter preview does not claim buildup");
var bleedDescription = OriginalStatusText.Description(bleedDefinition);
Check(bleedDescription.Contains("At 7, burst for 15%") && bleedDescription.Contains("min 8, max 35") && bleedDescription.Contains("3 Poise damage") && !bleedDescription.Contains("{"), "Authored Bleed description resolves all numeric bindings");
Check(OriginalStatusText.Description(bleedDefinition, bleed).Contains("At 12, burst"), "Status inspection describes the current meter threshold");
Check(JToken.DeepEquals(bleedDefinition, definitionBefore) && JToken.DeepEquals(bleed, bleedBefore), "Description formatting preserves authored and live state");
Check(OriginalStatusText.Description(new JObject { ["tooltip"] = "Unknown {missing.value}; object {proc}", ["proc"] = new JObject() }) == "Unknown {missing.value}; object {proc}", "Invalid authoring bindings remain visible instead of being hidden");
Check(OriginalStatusText.Description(new JObject { ["id"] = "strength", ["description"] = "Attacks add +1 damage per stack." }) == "Attacks add +1 damage per stack.", "Plain authored status description is unchanged");
JObject LobbySeat(bool connected = true, bool ready = true) => new() { ["connected"] = connected, ["ready"] = ready };
JObject Lobby(params JObject[] seats) => new() { ["started"] = false, ["seats"] = new JArray(seats) };
Check(OriginalCoopLobbyText.StartBlocker(null)!.Contains("Waiting for"), "Absent party data cannot enable Start");
Check(OriginalCoopLobbyText.StartBlocker(new JObject())!.Contains("Waiting for"), "Missing roster cannot enable Start");
Check(OriginalCoopLobbyText.StartBlocker(Lobby())!.Contains("Invite"), "Empty roster explains the required party");
var oneSeat = Lobby(LobbySeat());
Check(OriginalCoopLobbyText.StartBlocker(oneSeat)!.Contains("Invite"), "One ready seat still requires a second wanderer");
var readyLobby = Lobby(LobbySeat(), LobbySeat()); var readyBefore = readyLobby.DeepClone();
Check(OriginalCoopLobbyText.StartBlocker(readyLobby) == null, "Two connected ready seats may start");
Check(OriginalCoopLobbyText.StartBlocker(Lobby(LobbySeat(), LobbySeat(), LobbySeat())) == null, "Three ready seats may start");
Check(OriginalCoopLobbyText.StartBlocker(Lobby(LobbySeat(), LobbySeat(), LobbySeat(), LobbySeat())) == null, "Four ready seats may start");
Check(OriginalCoopLobbyText.StartBlocker(Lobby(LobbySeat(), LobbySeat(ready: false)))!.Contains("I'm ready"), "Choosing peer gets a readable readiness instruction");
Check(OriginalCoopLobbyText.StartBlocker(Lobby(LobbySeat(), LobbySeat(connected: false)))!.Contains("reconnect"), "Disconnected ready peer still blocks Start");
var missingConnected = LobbySeat(); missingConnected.Remove("connected");
Check(OriginalCoopLobbyText.StartBlocker(Lobby(LobbySeat(), missingConnected)) != null, "Missing connection flag does not invent a connected seat");
var missingReady = LobbySeat(); missingReady.Remove("ready");
Check(OriginalCoopLobbyText.StartBlocker(Lobby(LobbySeat(), missingReady)) != null, "Missing readiness flag does not invent consent");
var startedLobby = (JObject)readyLobby.DeepClone(); startedLobby["started"] = true;
Check(OriginalCoopLobbyText.StartBlocker(startedLobby)!.Contains("already begun"), "Started run cannot enable lobby Start");
Check(OriginalCoopLobbyText.StartBlocker(new JObject { ["seats"] = new JArray(LobbySeat(), "invalid seat") }) != null, "Malformed roster entry stays unavailable");
Check(JToken.DeepEquals(readyLobby, readyBefore), "Readiness formatting never mutates the companion snapshot");
Check(OriginalCoopLobbyText.Notice("all_players_must_be_ready", oneSeat)!.Contains("Invite"), "Known readiness refusal explains the party requirement");
Check(OriginalCoopLobbyText.Notice("all_players_must_be_ready", readyLobby)!.Contains("party changed"), "Stale-view refusal has a readable recovery instruction");
Check(OriginalCoopLobbyText.Notice("unknown_protocol_error", readyLobby) == "unknown_protocol_error", "Unrelated errors are preserved");
Check(OriginalCoopLobbyText.Notice(null, readyLobby) == null, "No refusal creates no extra error notice");
Console.WriteLine($"Card QoL domain: {checks} checks passed");
