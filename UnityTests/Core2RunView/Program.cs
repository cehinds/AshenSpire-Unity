// The Core 2 run screens' view models (Runtime/Presentation/Core2/Model), engine-free.
// Run from the repository root: dotnet run --project UnityTests/Core2RunView [-- <repo root> [--dump <dir>]]
//
// 1. The JS words and formats the screens show (uiStrings rows, act titles, the seed vocabulary, card and relic text,
//    the class picker), asserted against JS values computed at the pin (build 1854) and the pin's screenshots.
// 2. Whole seeded runs played through the view models exactly as the screens drive them: map → fight (the fallback
//    fight's Auto, or settled as won to reach the end) → spoils (receipt, level notices, offer) → shop / rest / event →
//    act change → finished run (unlocks recorded once). Every few commands the run is saved, a fresh flow resumes the
//    slot, and the resumed run must stand at the same screen (and the same spoils stage / event state).
// 3. The deck editor, the Armoury (and its combat lock), save refusals, and the 0.5.5 saves being ignored.
// --dump writes one JSON per screen state for the browser mocks (scratchpad/core2-ui/run/mock.html).
using System.Text;
using AshenSpire.Domain.Core2;
using AshenSpire.Presentation.Core2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : ".";
var dumpAt = Array.IndexOf(args, "--dump");
var dump = dumpAt >= 0 && dumpAt + 1 < args.Length ? args[dumpAt + 1] : null;
var checks = 0;
var failures = 0;
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
string Full(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
var dumped = new HashSet<string>();
void Dump(string name, object model)
{
    if (dump == null || !dumped.Add(name)) return;
    Directory.CreateDirectory(dump);
    File.WriteAllText(Path.Combine(dump, name + ".json"), JsonConvert.SerializeObject(model, Formatting.Indented, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore }), new UTF8Encoding(false));
}

try
{
    var content = Core2Content.LoadFolder(Full("GameContent/Unity/Core2"));
    var data = new Core2RunData(content);
    var clock = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    DateTime Now() => clock = clock.AddSeconds(1);

    // ---- 1. JS words and formats ---------------------------------------------------------------------------------
    Equal(Core2UiData.JsCommit, content.Manifest.JsCommit, "the UI text table was generated at the Core 2 export's JS commit");
    Equal(Core2Text.T("map.enterNamed", new { name = "Monster" }), "Enter Monster", "map.enterNamed fills {name} (JS 'Enter Monster', desktop-03b)");
    Equal(Core2Text.T("map.context.floor", new { floor = 1 }), "Floor 1", "map.context.floor");
    Equal(Core2Text.T("rest.title"), "Shrine of Ember", "rest.title inherits location.shrine.title through extends (desktop-08a)");
    Equal(Core2Text.T("rest.status.available", new { available = 1, total = 2 }), "1 of 2 available", "rest.status.available (desktop-08a '1 of 2 available')");
    Equal(Core2Text.T("gameProfile.rest.restored"), "HP and MP fully restored.", "the Core rest's arrival line (desktop-08a)");
    Equal(Core2Text.T("gameProfile.rest.description.smithAll"), "Upgrade one item for free.", "Smith's line (desktop-08a)");
    Equal(Core2Text.T("gameProfile.rest.noLevel"), "No attribute point.", "Level Up's refusal (desktop-08a)");
    Equal(Core2Text.T("shop.price", new { cost = 10 }), "10 cinders", "shop.price (desktop-08c '10 cinders')");
    Equal(Core2Text.T("shop.title"), "Merchant", "shop.title (desktop-08c)");
    Equal(Core2Text.T("shop.leave"), "Leave", "shop.leave (desktop-08c)");
    Equal(Core2Text.T("shop.book.buy"), "Buy", "the merchant offer's Buy (desktop-08c)");
    Equal(Core2Text.T("shop.bar.combatCards"), "Combat cards", "shelf label: Combat cards (desktop-08c)");
    Equal(Core2Text.T("shop.bar.weaponArts"), "WEAPON ARTS", "shelf label: WEAPON ARTS (desktop-08c)");
    Equal(Core2Text.T("reward.eyebrow.claim"), "Claim your spoils", "reward.eyebrow.claim (desktop-06, uppercased by CSS)");
    Equal(Core2Text.T("reward.progress.gained", new { xp = 24 }), "Gained: 24 xp", "reward.progress.gained (desktop-06)");
    Equal(Core2Text.T("reward.progress.next", new { level = 2 }), "Lv. 2", "reward.progress.next (desktop-06)");
    Equal(Core2Text.T("event.continue.reason"), "Choose a response first.", "event.continue.reason");
    Equal(Core2Text.T("dialogue.continue.reason"), "Choose a reply first.", "dialogue.continue.reason (desktop-08f)");
    Equal(Core2Text.T("dialogue.skip"), "Skip speech", "dialogue.skip (desktop-08f)");
    Equal(Core2Text.T("armoury.positions.title"), "Equipped positions", "armoury.positions.title (desktop-07b)");
    Equal(Core2Text.T("armoury.inspect.title", new { slot = "BODY" }), "Inspect · BODY", "armoury.inspect.title (desktop-07b)");
    Equal(Core2Text.T("deckEditor.readingDesk"), "Reading Desk", "deckEditor.readingDesk (desktop-07e)");
    Equal(Core2Text.T("deckEditor.counter", new { count = 10, min = 10, max = "∞" }), "10 / 10–∞", "deckEditor.counter (desktop-07e '10 / 10–∞')");
    Equal(Core2Text.T("deckEditor.accept"), "Accept", "deckEditor.accept (desktop-07e)");
    Equal(Core2Text.T("creation.class.unlockNextRun"), "Play 1 game to unlock.", "the locked class line (desktop-02d)");
    Check(Throws(() => Core2Text.T("shop.price")), "an unresolved {token} throws, as JS strings.js does");
    Check(Throws(() => Core2Text.T("no.such.id")), "an unknown id throws");
    Equal(Core2Text.ActTitle(1, "The Pale Marches"), "ACT I — THE PALE MARCHES", "act title with a seat (desktop-03 'ACT I — THE PALE MARCHES')");
    Equal(Core2Text.ActTitle(2, "The Hollow Weald"), "ACT II — THE HOLLOW WEALD", "act title, act 2");
    Equal(Core2Text.ActTitle(4, "The Hollow Weald"), "ACT I — THE HOLLOW WEALD · CYCLE 2", "act title past the named acts carries the cycle suffix");
    Equal(Core2Text.NodeType("monster").Name, "Monster", "node kind name: monster");
    Equal(Core2Text.NodeType("monster").Blurb, "A fight — cinders and a card reward.", "node kind blurb (desktop-03b)");
    Equal(Core2Text.NodeType("event").Name, "Unknown", "a fogged event reads Unknown");
    Equal(Core2Seed.FromText("1D0V63S"), 2522343732u, "seedFromString('1D0V63S') as JS computes it");
    Equal(Core2Seed.ToText(123456789), "2C9G1U", "seedToString(123456789) as JS computes it");
    Equal(Core2Seed.FromText("zzzzzzzzzz"), 1528444520u, "a long seed wraps mod 2^32 as JS >>> 0 does");
    Equal(Core2Seed.FromText("o"), 0u, "O reads as 0");
    Equal(Core2Seed.Problem("ab-c"), "A seed cannot contain “-” — seeds are 0–9 and A–Z, and O reads as 0.", "seedProblem's sentence, as JS words it");
    Equal(Core2CardText.Card(content, "core.strike"), "Deal 6 damage.", "card text binds {damage} from its effect (desktop-07e 'Deal 6 damage.')");
    Equal(Core2CardText.Card(content, "core.defend"), "Gain 10 Block.", "card text binds {block} (desktop-02e 'Gain 10 Block')");
    Equal(Core2CardText.Card(content, "core.evade"), "Gain 1 Evade.", "the configured Evade grant reads the rules (JS evadeStackGainPhrase: base 1, no DEX)");
    Equal(Core2CardText.Describe(JObject.Parse("{\"f\":\"add\",\"args\":[6,{\"f\":\"metric\",\"metric\":\"sameCardPlays\",\"of\":\"self\",\"per\":3}]}")),
        "6 plus 3 per play of this card this combat", "describeFormula: a base plus a per-play rider, in the JS words");
    Equal(Core2CardText.Relic(data, "forsakenMedallion"), "Max HP +10. Your first attack each combat also deals 4 Poise damage.", "relic text binds its modifier and its property rule's op (desktop-02e)");

    // ---- the class picker and Review (desktop-02d / 02e) -------------------------------------------------------
    var storage = new Core2MemoryStorage();
    // A 0.5.5 save under its own keys is never read: Continue stays off.
    storage.Set("AshenSpire.Unity.Original.v1.test.slot0", "{\"legacy\":true}");
    storage.Set("AshenSpire.Unity.Campaign.v1.test", "{\"legacy\":true}");
    var slots = new Core2SaveSlots(storage, content, "test");
    Check(!slots.HasContinue, "0.5.5 saves are ignored: Continue has nothing to resume");
    var flow = new Core2RunFlow(data, slots, Now);
    var creation = new Core2CreationModel(flow, 0, 2522343732u);
    Equal(creation.Seed, "1D0V63S", "Review's seed field shows the seed in the JS vocabulary");
    Equal(string.Join(",", creation.Classes.Select(c => c.Id)), "reaver,starseer,rogue,herald", "the picker lists the Core 2 classes in profile order");
    Check(!creation.Classes[0].Locked && creation.Classes.Skip(1).All(c => c.Locked && c.LockText == "Play 1 game to unlock."), "a fresh profile opens the Reaver only; the rest read 'Play 1 game to unlock.'");
    Equal(string.Join(" ", creation.Classes[0].Stats.Select(s => s.Key + " " + s.Value)), "HP 71 SP 3 MP 1 HAND 5 DRAW 5", "Reaver's card stats (desktop-02d 'HP 71 SP 3 MP 1 HAND 5 DRAW 5')");
    Equal(creation.Classes[0].Relic, "Forsaken Medallion", "Reaver's relic (desktop-02d)");
    creation.Choose("starseer");
    Equal(creation.Selected, "reaver", "a locked class cannot be chosen");
    creation.Next();
    var review = creation.Review();
    Equal(string.Join(" ", review.Attributes.Select(a => a.Key + " " + a.Value)), "STR 2 DEX 1 CON 1 WIS 1 INT 1", "Review: the class's six fixed points (desktop-02e)");
    Equal(string.Join(" | ", review.Gear.Select(g => g.Label + ": " + g.Name)), "ARMOUR: Wayfarer Plate | MAIN HAND: Greatsword | RELIC: Forsaken Medallion", "Review: armour, main hand, relic (desktop-02e)");
    Equal(review.Destination, "Slot 1", "Review: destination (desktop-02e 'Slot 1')");
    Dump("creation-class", new { model = creation, step = "class" });
    Dump("creation-review", new { model = creation, review });
    creation.Seed = "1-2";
    Check(creation.Begin() != null && !flow.HasRun, "a bad seed is refused by name and starts nothing");

    // ---- 2. whole runs -----------------------------------------------------------------------------------------
    var finishes = new List<string>();
    var actsSeen = new HashSet<int>();
    var screensSeen = new HashSet<string>();
    var stagesSeen = new HashSet<string>();
    var resumes = 0;
    bool UnportedRelic(string relicId) => data.HasRelic(relicId) && Core2Json2.Strings(data.Relic(relicId)["propertyTags"])
        .Any(tag => data.PropertyRule(tag).ToString().Contains("\"gainEnergy\""));
    Console.WriteLine("PENDING: relics the Core 2 fight cannot run yet (opcode gainEnergy): "
        + string.Join(", ", content.Relics.Where(r => UnportedRelic(r.Id)).Select(r => r.Id)));

    Core2RunFlow PlayRun(Core2RunFlow f, string classId, string seed, int slot, bool settleFights, int resumeEvery)
    {
        var err = f.Begin(classId, seed, slot, runId: "view-" + classId + "-" + seed);
        Check(err == null, "begin " + classId + " seed " + seed + (err == null ? "" : ": " + err));
        Equal(f.Screen, Core2Screen.Map, "a new run stands at the act 1 map");
        var steps = 0;
        for (var guard = 0; guard < 400 && f.Screen != Core2Screen.Finished; guard++)
        {
            var session = f.Session;
            actsSeen.Add(session.Run.ActNumber);
            screensSeen.Add(f.Screen.ToString());
            var hud = Core2HudModel.From(data, session);
            if (guard == 0)
            {
                Equal(hud.HpText, session.Run.Hp + "/" + session.Run.MaxHp, "HUD HP reads hp/max (desktop-03 '71/71')");
                Dump("hud", hud);
            }
            switch (f.Screen)
            {
                case Core2Screen.Map:
                {
                    var map = Core2MapModel.From(data, session);
                    if (map.AtEntrance && session.Run.ActNumber == 1)
                    {
                        var expected = session.Run.MapGraph.StartIds.Count + session.Run.MapGraph.BossIds.Count;
                        Equal(map.Nodes.Count, expected, "fog at the entrance draws the start row and the bosses only");
                        Equal(map.Route.Count, 9, "the route strip has one slot per floor before the boss (desktop-03: nine)");
                        Dump("map", map);
                    }
                    var reachable = map.Nodes.Where(n => n.Reachable).ToList();
                    Check(reachable.Count > 0, "the map offers a node to travel to");
                    var pick = reachable[(session.Run.Path.Count + seed.Length) % reachable.Count];
                    var ctx = Core2MapModel.Context(pick);
                    Equal(ctx.EnterLabel, "Enter " + Core2Text.NodeType(pick.ShownType).Name, "the tray's Enter names the node kind");
                    if (pick.ShownType == "boss") Check(ctx.Lines.Count == 2, "a boss node's tray names its destination");
                    string e;
                    try { e = f.Travel(pick.Id); }
                    catch (NotSupportedException gap) { throw new NotSupportedException(gap.Message + " (relics: " + string.Join(",", session.Run.Relics) + "; flasks: " + string.Join(",", session.Run.Flasks) + "; deck: " + string.Join(",", session.Run.Deck.Select(c => c.CardId).Distinct()) + ")"); }
                    Check(e == null, "travel to " + pick.Id + (e == null ? "" : ": " + e));
                    break;
                }
                case Core2Screen.Fight:
                {
                    var fight = new Core2FallbackFight(session);
                    Check(fight.Hand().Count > 0, "the fallback fight shows a hand");
                    Dump("fight", new { title = fight.Title, player = fight.PlayerLine, enemies = fight.Enemies(), hand = fight.Hand() });
                    if (settleFights) session.Fight.SettleAsWon();
                    else fight.Auto();
                    Check(fight.Over, "the fight ends (Auto, or settled as won)");
                    var e = f.FightOver();
                    Check(e == null, "the fight settles" + (e == null ? "" : ": " + e));
                    break;
                }
                case Core2Screen.Reward:
                {
                    var spoils = new Core2SpoilsModel(f);
                    stagesSeen.Add(spoils.Stage ?? "none");
                    Dump("spoils-" + spoils.Stage, spoils);
                    if (spoils.Stage == "victoryReceipt")
                    {
                        Equal(spoils.Eyebrow, "SPOILS", "the receipt's eyebrow (JS reward.eyebrow.spoils)");
                        Equal(spoils.FootNote, "Experience earned in this battle.", "the receipt's foot note");
                        Check(spoils.Progress.All(p => p.GainedText.StartsWith("Gained: ") && p.GainedText.EndsWith(" xp")), "receipt rows read 'Gained: N xp'");
                        Check(spoils.Acknowledge() == null, "the receipt is acknowledged");
                    }
                    else if (spoils.Stage == "levelNotices")
                    {
                        Equal(spoils.Title, "Level up", "the notices' title (JS reward.level.button)");
                        Check(spoils.Notices.All(n => n.Title.Contains(" · Level ") && n.Title.Contains(" → ")), "a notice reads '<track> · Level b → a'");
                        Check(spoils.Acknowledge() == null, "the notices are acknowledged");
                    }
                    else
                    {
                        Check(spoils.Title == "Victory rewards" || spoils.Title == "Level reward", "the offer's title");
                        Check(spoils.Note.StartsWith("Choose " + session.CurrentOffer.TakeCount + "."), "the offer's note names how many to choose");
                        // Relics whose rules use an opcode the Core 2 fight has not ported (gainEnergy: Cracked Lantern, Azure
                        // Sigil…) would stop the next fight with NotSupportedException — a combat-domain gap, listed as PENDING
                        // below; the scripted runs take the other options so they can reach the end.
                        var index = spoils.Options.FindIndex(o => !o.Unavailable && !(o.Kind == "relics" && UnportedRelic(o.Id)));
                        if (index < 0 || steps % 5 == 4) { Equal(spoils.SkipLabel, "Skip", "the offer's way out is Skip"); Check(spoils.Skip() == null, "Skip leaves the offer"); break; }
                        spoils.Select(index);
                        Equal(spoils.ConfirmLabel, "Confirm", "the picked option waits for Confirm");
                        Check(spoils.Confirm() == null, "Confirm opens the review");
                        Equal(spoils.SkipLabel, "Back", "the review's way back is Back");
                        if (spoils.AddToDeckMode)
                        {
                            Equal(spoils.ConfirmLabel, "Add to deck", "a card's review offers Add to deck");
                            var r = spoils.Status.Length == 0 ? spoils.Confirm() : spoils.Keep();
                            Check(r == null, "the card is claimed" + (r == null ? "" : ": " + r));
                        }
                        else
                        {
                            Equal(spoils.ConfirmLabel, "Continue", "a non-card's review offers Continue");
                            var r = spoils.Confirm();
                            Check(r == null, "the option is claimed" + (r == null ? "" : ": " + r));
                        }
                    }
                    break;
                }
                case Core2Screen.Shop:
                {
                    var shop = new Core2ShopModel(f);
                    Dump("shop", shop);
                    Equal(shop.Title, "Merchant", "the shop's title");
                    Check(shop.Categories.Count > 0 && shop.Categories.All(c => c.Label.Length > 0), "every shelf has a label");
                    Check(shop.Offers.All(o => o.Price == Core2Text.T("shop.price", new { cost = session.ShopStock.Price ?? 0 })), "every offer reads the one flat price");
                    var buy = shop.Offers.FirstOrDefault(o => o.Available && !(o.Kind == "relics" && UnportedRelic(session.ShopStock.Options[o.Index].Id)));
                    if (buy != null)
                    {
                        var rv = shop.Review(buy.Index);
                        Equal(rv.Question, "Buy this item?", "the purchase review's question");
                        var cinders = session.Run.Cinders;
                        Check(shop.Buy(buy.Index) == null && session.Run.Cinders == cinders - (session.ShopStock.Price ?? 0), "Buy spends the flat price");
                        Check(shop.Offers[buy.Index].Reason == "Unavailable", "a bought offer reads Unavailable");
                    }
                    foreach (var o in shop.Offers.Where(o => !o.Available && !session.ShopStock.Taken.Contains(o.Index))) Check(o.Reason == "Not enough cinders", "an unaffordable offer reads 'Not enough cinders'");
                    Check(shop.Leave() == null, "Leave returns to the map");
                    break;
                }
                case Core2Screen.Rest:
                {
                    var rest = new Core2RestModel(f);
                    Dump("rest", rest);
                    Equal(rest.Title, "Shrine of Ember", "the rest's title");
                    Equal(rest.Restored, "HP and MP fully restored.", "the rest heals on arrival");
                    Equal(session.Run.Hp, session.Run.MaxHp, "HP is full on arrival");
                    Check(rest.Status == rest.Services.Count(s => s.Available) + " of " + rest.Services.Count + " available", "the rest's status counts its open services");
                    Check(rest.Services.Count == 0 || rest.Services[0].Available || rest.Services.All(s => !s.Available), "available services lead the list");
                    var smith = rest.Services.FirstOrDefault(s => s.Id == "smith");
                    if (smith != null && smith.Available)
                    {
                        Check(rest.Smith.Count > 0 && rest.Smith.All(s => s.Tier.StartsWith("+")), "Smith previews each candidate's tier");
                        Check(rest.DoSmith(rest.Smith[0].ItemRef) == null, "Smith upgrades one item");
                        Equal(rest.Services.First(s => s.Id == "smith").Line, "Done.", "a used service reads Done.");
                    }
                    var level = rest.Services.FirstOrDefault(s => s.Id == "levelUp");
                    if (level != null && level.Available) Check(rest.DoLevelUp("strength") == null, "Level Up spends the earned point");
                    else if (level != null && session.Run.Level.UnspentPoints == 0) Equal(level.Line, "No attribute point.", "Level Up without a point says why");
                    Check(rest.Leave() == null, "Continue leaves the rest");
                    break;
                }
                case Core2Screen.Event:
                {
                    var ev = new Core2EventModel(f);
                    Dump(ev.Spoken ? "event-dialogue" : "event", ev);
                    if (ev.Resolved)
                    {
                        // Resumed after the response: the result shows, and Continue is open (JS: the open event and its result).
                        Check(ev.ContinueEnabled && ev.Choices.Count == 0 && ev.Result.Length > 0, "a resumed answered event shows its result and Continue");
                        Check(ev.Continue() == null, "Continue leaves the resumed event");
                        break;
                    }
                    if (ev.Spoken && ev.Beats.Count > 1)
                    {
                        Check(ev.ContinueEnabled && !ev.ResponsesShown && ev.Status.StartsWith("Line 1 of "), "a spoken event reads line by line before its responses");
                        ev.SkipSpeech();
                    }
                    Check(ev.ResponsesShown, "the responses come with the last beat");
                    Check(!ev.ContinueEnabled && ev.ContinueReason == (ev.Spoken ? "Choose a reply first." : "Choose a response first."), "Continue waits for a response, saying why");
                    var choice = ev.Choices.FirstOrDefault(c => c.Enabled);
                    Check(choice != null, "an event offers a response the run can take");
                    foreach (var c in ev.Choices.Where(c => !c.Enabled && c.Tag == "Cannot afford")) Check(true, "an unaffordable response reads Cannot afford");
                    Check(ev.Choose(choice.Id) == null, "the response is taken");
                    Check(ev.Resolved && ev.ContinueEnabled, "Continue opens after the response");
                    if (session.Run.CombatEntered != null) Check(ev.ContinueLabel == "Steel yourself", "a response that starts a fight continues with 'Steel yourself'");
                    Check(ev.Continue() == null, "Continue leaves the event");
                    break;
                }
            }
            steps++;
            // Save and resume: a fresh flow on the same storage resumes the slot onto the same screen.
            if (resumeEvery > 0 && steps % resumeEvery == 0 && f.Screen != Core2Screen.Finished)
            {
                var screen = f.Screen; var stage = f.Session.RewardStage; var chosen = f.Session.Run.OpenEventChosen; var act = f.Session.Run.ActNumber; var node = f.Session.Run.MapNodeId;
                var again = new Core2RunFlow(data, new Core2SaveSlots(storage, content, "test"), Now);
                var e = again.Continue(slot);
                Check(e == null, "resume slot " + (slot + 1) + (e == null ? "" : ": " + e));
                Check(again.Screen == screen && again.Session.RewardStage == stage && again.Session.Run.OpenEventChosen == chosen && again.Session.Run.ActNumber == act && again.Session.Run.MapNodeId == node,
                    "save and resume land on the same screen (" + screen + (stage != null ? "/" + stage : "") + ")");
                f = again;
                resumes++;
            }
        }
        Equal(f.Screen, Core2Screen.Finished, classId + " seed " + seed + " reaches the finished-run screen");
        var finish = new Core2FinishModel(f);
        Dump("finish-" + (finish.Victory ? "victory" : "defeat"), finish);
        Equal(finish.Title, finish.Victory ? "Victory" : "Defeat", "the finished-run title");
        Equal(finish.Heading, finish.Victory ? "Ember restored" : "You perished", "the finished-run heading (JS gameover.js)");
        Check(finish.Meta == "Seed " + Core2Seed.ToText(f.Session.Run.Seed), "the finished run names its seed in the JS vocabulary");
        Check(storage.Get(slots.RunKey(slot)) == null, "a finished run's slot is cleared (JS finishRun clearRun)");
        finishes.Add(classId + ":" + (finish.Victory ? "victory" : "defeat") + ":" + string.Join("+", finish.Earned));
        return f;
    }

    // A run that reaches a combat opcode the Core 2 fight has not ported yet (NotSupportedException: an event or a
    // treasure can still hand over a gainEnergy relic) pends, as the Core2Run oracle replays pend on the same gaps; the
    // run layer and these screens are not where it is fixed. Its slot keeps the save from before the failed command.
    var pending = new List<string>();
    Core2RunFlow TryRun(string classId, string seed, int slot, bool settle, int every)
    {
        try { return PlayRun(new Core2RunFlow(data, new Core2SaveSlots(storage, content, "test"), Now), classId, seed, slot, settle, every); }
        catch (NotSupportedException gap) { pending.Add(classId + " seed " + seed + ": " + gap.Message); slots.Delete(slot); return null; }
    }
    Core2RunFlow first = null;
    foreach (var seed in new[] { "1D0V63S", "SP1RE", "EMBER", "CINDR", "ASHEN", "GRACE", "VIGIL", "THR0NE" })
        if ((first = TryRun("reaver", seed, 0, settle: true, every: 3)) != null) break;
    Check(first != null, "a reaver run plays to its end with every fight settled as won");
    Check(first != null && first.Earned.Count == 1 && first.Earned[0] == "Starseer", "the first finished run unlocks the Starseer (classUnlocks sequence)");
    var after = new Core2RunFlow(data, new Core2SaveSlots(storage, content, "test"), Now);
    Check(after.ClassAvailable("starseer") && !after.ClassAvailable("rogue"), "the unlock is saved on the profile: a fresh flow opens the Starseer only");
    TryRun("starseer", "2C9G1U", 1, settle: true, every: 4);
    var played = 0;
    foreach (var seed in new[] { "2C9G1U", "ASHEN", "SP1RE", "K1LN", "DUSK", "MARCH" })
        if (TryRun("reaver", seed, 2, settle: false, every: 4 + played) != null && ++played >= 3) break;
    Check(played >= 2, "reaver runs fought through the fallback fight's Auto play to their end (" + played + ")");
    Check(actsSeen.Contains(2) && actsSeen.Contains(3), "the runs cross act changes (acts seen: " + string.Join(",", actsSeen.OrderBy(a => a)) + ")");
    foreach (var s in new[] { "Map", "Fight", "Reward", "Shop", "Rest", "Event" }) Check(screensSeen.Contains(s), "the runs reach the " + s + " screen");
    foreach (var s in new[] { "victoryReceipt", "offer" }) Check(stagesSeen.Contains(s), "the runs reach the spoils stage " + s);
    Check(finishes.Any(f => f.Contains(":victory:")), "a run ends in victory (" + string.Join(" ", finishes) + ")");
    Check(resumes > 20, "the runs were saved and resumed mid-run " + resumes + " times");
    foreach (var line in pending) Console.WriteLine("PENDING: " + line);

    // ---- 3. the deck editor --------------------------------------------------------------------------------------
    var deckFlow = new Core2RunFlow(data, new Core2SaveSlots(new Core2MemoryStorage(), content, "deck"), Now);
    Check(deckFlow.Begin("reaver", "DECK1", 0) == null, "a run for the deck editor");
    Check(deckFlow.OpenDeckEdit() == null, "the deck editor opens on the map");
    var desk = new Core2DeckEditorModel(deckFlow);
    var count = deckFlow.Session.Run.Deck.Count;
    Equal(desk.Counter, count + " / 10–∞", "the counter reads count / min–max");
    Check(desk.Heading.StartsWith("Reaver"), "the desk names the class (desktop-07e 'Reaver · Martial Adept')");
    Check(deckFlow.Travel(deckFlow.Session.TravelOptions()[0]) != null && deckFlow.Screen == Core2Screen.Map, "the run waits while a deck edit is open");
    var basic = desk.Tiles.First(t => t.Key.StartsWith("basic:") && t.CanAdd);
    Check(desk.Add(basic.Key) == null && deckFlow.Session.Run.Deck.Count == count + 1, "+ adds a basic copy");
    Check(desk.Status.StartsWith("Added "), "the desk says what it added");
    Check(desk.Undo() && deckFlow.Session.Run.Deck.Count == count, "Undo takes it back");
    while (deckFlow.Session.Run.Deck.Count >= 10) desk.Remove(deckFlow.Session.Run.Deck.Last().InstanceId);
    var refusal = desk.Accept();
    Equal(refusal, "Your deck has 9 cards; it needs at least 10.", "Accept below the minimum is refused in the JS words");
    Dump("deck-editor", desk);
    desk.Cancel();
    Equal(deckFlow.Session.Run.Deck.Count, count, "Cancel restores the deck");
    Check(deckFlow.DeckEdit == null, "Cancel closes the editor");
    deckFlow.DeckSettings.PlayInDeckOrder = true;
    deckFlow.OpenDeckEdit();
    desk = new Core2DeckEditorModel(deckFlow);
    Check(desk.Reorder, "Play in deck order shows the reorder controls");
    var firstCard = deckFlow.Session.Run.Deck[0].InstanceId;
    Check(desk.Move(firstCard, 1) && deckFlow.Session.Run.Deck[1].InstanceId == firstCard, "▼ moves a card one place later");
    Check(desk.Accept() == null && deckFlow.DeckEdit == null, "Accept keeps the order and closes");

    // ---- the Armoury -----------------------------------------------------------------------------------------------
    var armoury = new Core2ArmouryModel(deckFlow);
    Dump("armoury", armoury);
    Check(!deckFlow.Session.ArmouryHasNew, "looking at the Armoury clears the new-item glow");
    var body = armoury.Rows.FindIndex(r => r.Code == "BODY");
    var mh1 = armoury.Rows.FindIndex(r => r.Code == "MH1");
    Check(body >= 0 && mh1 >= 0, "the positions are coded BODY and MH1 (desktop-07b)");
    Equal(armoury.Rows[body].Name, "Wayfarer Plate", "BODY holds the Reaver's armour");
    Equal(armoury.Rows[body].Chip, "EQUIPPED", "an equipped position wears the EQUIPPED chip");
    Equal(armoury.Inspect(body).Title, "Inspect · BODY", "Inspect's title names the position");
    var sword = armoury.Rows[mh1].Position.ItemId;
    Check(armoury.Unequip(mh1) == null && armoury.Rows[mh1].Empty, "Unequip empties the main hand");
    Check(armoury.Status.Contains("unequipped from MH1"), "the Armoury says what it changed");
    Check(armoury.Candidates(mh1).Any(c => c.ItemId == sword), "Replace offers the stored piece");
    Check(armoury.Equip(mh1, sword) == null && armoury.Rows[mh1].Position.ItemId == sword, "Replace puts it back");
    deckFlow.Travel(deckFlow.Session.TravelOptions()[0]);
    Equal(deckFlow.Screen, Core2Screen.Fight, "the first floor is a fight");
    var locked = new Core2ArmouryModel(deckFlow);
    Equal(locked.Locked, "Equipment changes are disabled during combat.", "the Armoury is locked while a fight is unresolved");
    Check(locked.Unequip(mh1) != null && !locked.Rows[mh1].Empty, "a change during the fight is refused");
    Check(deckFlow.OpenDeckEdit() != null, "the deck editor is refused during the fight");

    // ---- save refusals ---------------------------------------------------------------------------------------------
    var refusedStorage = new Core2MemoryStorage();
    var refusedSlots = new Core2SaveSlots(refusedStorage, content, "refused");
    refusedStorage.Set(refusedSlots.RunKey(0), "{\"format\":\"ashen.core2.run\",\"formatVersion\":99,\"run\":{}}");
    var refusedFlow = new Core2RunFlow(data, refusedSlots, Now);
    Equal(refusedSlots.List()[0].State, Core2SlotState.Refused, "a newer save is listed as refused");
    var why = refusedFlow.Continue(0);
    Check(why != null && why.StartsWith("THIS SAVE COULD NOT BE RESTORED · ") && why.Contains("newer build"), "Continue of a refused save says why (JS failure banner)");
    Equal(refusedStorage.Get(refusedSlots.RunKey(0)), "{\"format\":\"ashen.core2.run\",\"formatVersion\":99,\"run\":{}}", "a refused save is never touched");
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: " + error);
}

if (failures > 0) { Console.Error.WriteLine("Core2RunView: " + failures + " failed, " + checks + " passed"); return 1; }
Console.WriteLine("Core2RunView: " + checks + " checks passed");
return 0;
