// Enemy telegraph view-model checks (F04): intent badges/tooltips, Poise meter, icon table.
// Run from the repository root: dotnet run --project UnityTests/Telegraphs [-- <repo root>]
using System.Text.RegularExpressions;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
var checks = 0;
var failures = 0;
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));

JObject Enemy(JObject intent, int value = 0, int max = 10, bool skip = false, int hp = 20) => new JObject
{
    ["id"] = "e1", ["kind"] = "enemy", ["enemyId"] = "probe", ["hp"] = hp, ["maxHp"] = 20, ["block"] = 0, ["statuses"] = new JObject(),
    ["poiseMeter"] = new JObject { ["value"] = value, ["max"] = max }, ["movesHistory"] = new JArray(), ["intent"] = intent,
    ["pendingMove"] = null, ["skipNextTurn"] = skip, ["unlockedMoves"] = new JArray(), ["alive"] = hp > 0,
};
// Same shape CombatSession.RollIntents writes.
JObject IntentOf(string moveId, JToken move) => new JObject
{
    ["kind"] = move["intent"], ["moveId"] = moveId, ["damage"] = move["damage"], ["hits"] = move["damage"] != null ? move["hits"] ?? new JValue(1) : null,
    ["block"] = move["block"], ["delayed"] = move["delay"] != null, ["pending"] = false,
};

try
{
    // ---- Icon table ---------------------------------------------------------------------
    var uiContent = Read("src/ui/uiContent.js");
    var iconsMatch = Regex.Match(uiContent, @"export const INTENT_ICONS = \{([^}]*)\}");
    Check(iconsMatch.Success, "HTML INTENT_ICONS table found in src/ui/uiContent.js");
    var htmlIcons = Regex.Matches(iconsMatch.Groups[1].Value, @"(\w+):\s*'([^']*)'").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    foreach (var pair in htmlIcons)
    {
        var row = IntentIconTable.Entries.FirstOrDefault(e => e.Kind == pair.Key);
        Check(row != null, "icon table has a row for HTML intent kind '" + pair.Key + "'");
        if (row != null) Equal(row.Glyph, pair.Value, "icon '" + pair.Key + "' glyph matches HTML INTENT_ICONS");
    }
    Equal(IntentIconTable.Entries.Count, htmlIcons.Count, "icon table has no rows beyond the HTML kinds");
    Equal(IntentIconTable.Entries.Select(e => e.IconId).Distinct().Count(), IntentIconTable.Entries.Count, "icon ids are unique");
    var schemaKinds = Regex.Match(Read("src/model/schemas.js"), @"INTENT_KINDS = Object\.freeze\(\[([^\]]*)\]\)").Groups[1].Value;
    foreach (Match kind in Regex.Matches(schemaKinds, "'(\\w+)'")) Check(IntentIconTable.Entries.Any(e => e.Kind == kind.Groups[1].Value), "schema INTENT_KINDS '" + kind.Groups[1].Value + "' is mapped");
    var frameworkAssets = Read("src/framework/data/assets.js");
    foreach (var row in IntentIconTable.Entries)
    {
        Check(File.Exists(Path.Combine(root, row.AssetPath)), "icon '" + row.IconId + "' asset file exists: " + row.AssetPath);
        Check(Regex.IsMatch(frameworkAssets, "\"id\": \"" + Regex.Escape(row.AssetId) + "\",\\s*\"kind\": \"ICON\",\\s*\"sourcePath\": \"" + Regex.Escape(row.AssetPath) + "\""), "icon '" + row.IconId + "' asset id " + row.AssetId + " is a framework ICON row with that sourcePath");
    }
    // Tone colours resolve to the HTML stylesheet values.
    var css = Read("styles/base.css") + Read("styles/kit.css");
    string Var(string name) => Regex.Match(css, "--" + name + @":\s*(#[0-9a-fA-F]{6})").Groups[1].Value.ToLowerInvariant();
    Equal(IntentIconTable.For("attack").ColorHex, Var("danger"), "attack tone colour = --danger");
    Equal(IntentIconTable.For("block").ColorHex, Var("frost"), "block tone colour = --frost");
    Equal(IntentIconTable.For("buff").ColorHex, Var("gold"), "buff tone colour = --gold");
    Equal(IntentIconTable.For("staggered").ColorHex, Var("gold"), "staggered tone colour = --gold");
    Check(css.Contains(".as-pill[data-tone=\"violet\"] { border-color: " + IntentIconTable.For("debuff").ColorHex), "debuff tone colour = kit.css violet pill");
    Equal(IntentIconTable.For("nonsense").Kind, "unknown", "unmapped kind falls back to the unknown row");

    // ---- Every intent kind authored in the content ------------------------------------
    var content = JObject.Parse(Read("GameContent/Unity/Original/content.json"));
    var seenKinds = new HashSet<string>();
    int moves = 0;
    foreach (JObject definition in content["enemies"]!)
        foreach (var move in ((JObject)definition["moves"]!).Properties())
        {
            var authoredKind = (string)move.Value["intent"]!;
            seenKinds.Add(authoredKind);
            var enemy = Enemy(IntentOf(move.Name, move.Value), max: (int)definition["poiseMax"]!);
            var before = enemy.DeepClone();
            var t = EnemyTelegraphViewModel.Build(enemy);
            if (!JToken.DeepEquals(before, enemy)) throw new Exception("Build mutated " + definition["id"] + "." + move.Name);
            var expected = move.Value["damage"] != null ? "attack" : move.Value["block"] != null ? "block" : authoredKind;
            if (t.Intent.Kind != expected || t.Intent.Icon.Kind != expected) throw new Exception(definition["id"] + "." + move.Name + " displayed as " + t.Intent.Kind + ", expected " + expected);
            if (expected == "attack" && t.Intent.ValueText != EnemyTelegraphViewModel.FormatDamage((int)move.Value["damage"]!, (int?)move.Value["hits"] ?? 1) + (move.Value["delay"] != null ? " ⌛" : ""))
                throw new Exception(definition["id"] + "." + move.Name + " value text " + t.Intent.ValueText);
            if (!t.Poise.Visible || t.Poise.Max != (int)definition["poiseMax"]!) throw new Exception(definition["id"] + " poise meter missing");
            moves++;
        }
    Check(moves > 0, "all " + moves + " authored enemy moves render a mapped, non-mutating intent badge and Poise meter");
    foreach (var kind in seenKinds.OrderBy(k => k)) Check(IntentIconTable.Entries.Any(e => e.Kind == kind), "content intent kind '" + kind + "' has an icon");

    // ---- Per-kind wording (uiContent.js intentBadge / intentTooltip) -------------------
    IntentDisplay Show(JObject intent, string victim = "you", JObject player = null, Func<JObject, int, int> preview = null) => EnemyTelegraphViewModel.Build(Enemy(intent), player, preview, victim).Intent;
    var single = Show(JObject.Parse("{kind:'attack',moveId:'slash',damage:7,hits:1,block:null,delayed:false,pending:false}"));
    Equal(single.ValueText, "7", "single-hit attack value");
    Equal(single.BadgeText, "⚔ 7", "single-hit badge is glyph + number");
    Equal(single.TooltipTitle, "Intent: Attack", "attack tooltip title");
    Equal(single.TooltipBody, "Attacking you for 7 damage (modifiers included).", "single-hit tooltip wording");
    Equal(single.CssClasses, "intent lg attack", "attack USS classes");
    Equal(single.Icon.Tone, "danger", "attack tone");
    Equal(single.Severity, TelegraphSeverity.Attack, "attack severity");
    var multi = Show(JObject.Parse("{kind:'attack',moveId:'flurry',damage:6,hits:3,delayed:false,pending:false}"));
    Equal(multi.ValueText, "6×3", "multi-hit value is 6×3");
    Equal(multi.TotalDamage, (int?)18, "multi-hit total");
    Equal(multi.TooltipBody, "Attacking you for 6 × 3 (18 total) damage (modifiers included).", "multi-hit tooltip wording");
    Equal(EnemyTelegraphViewModel.FormatDamage(3, 6), "3×6", "six-hit formatting");
    Equal(EnemyTelegraphViewModel.FormatDamage(0, 2), "0×2", "zero-damage multi-hit formatting");
    Equal(EnemyTelegraphViewModel.FormatDamage(12, 1), "12", "one hit shows no multiplier");
    Equal(EnemyTelegraphViewModel.FormatDamage(12, 0), "12", "zero hits never shows a multiplier");
    var coop = Show(JObject.Parse("{kind:'attack',moveId:'flurry',damage:4,hits:2}"), "each hero");
    Equal(coop.TooltipBody, "Attacking each hero for 4 × 2 (8 total) damage (modifiers included).", "co-op victim wording");
    var delayed = Show(JObject.Parse("{kind:'attack',moveId:'heldBlade',damage:14,hits:1,delayed:true,pending:false}"));
    Equal(delayed.ValueText, "14 ⌛", "delayed attack shows hourglass");
    Check(delayed.Dashed && delayed.CssClasses == "intent lg attack delayed", "delayed attack is dashed with .delayed class");
    Equal(delayed.TooltipBody, "Attacking you for 14 damage (modifiers included).\nDelayed: it holds this turn and strikes the next. Stagger cancels it.", "delayed tooltip wording");
    var pending = Show(JObject.Parse("{kind:'attack',moveId:'heldBlade',damage:14,hits:1,delayed:true,pending:true}"));
    Check(pending.Pending, "pending flag carried");
    Equal(pending.TooltipBody, "Attacking you for 14 damage (modifiers included).\nCommitted: this delayed attack lands this coming turn — Stagger cancels it.", "committed tooltip wording");
    var block = Show(JObject.Parse("{kind:'block',moveId:'guard',damage:null,hits:null,block:6}"));
    Equal(block.BadgeText, "🛡 6", "block badge");
    Equal(block.TooltipText, "Intent: Defend\nGaining Block.", "block tooltip wording");
    Equal(block.Icon.Tone, "frost", "block tone");
    var buff = Show(JObject.Parse("{kind:'buff',moveId:'warcry'}"));
    Equal(buff.BadgeText, "↑", "buff badge is glyph only");
    Equal(buff.TooltipText, "Intent: Buff\nStrengthening itself.", "buff tooltip wording");
    var debuff = Show(JObject.Parse("{kind:'debuff',moveId:'hex'}"));
    Equal(debuff.TooltipText, "Intent: Debuff\nHindering you.", "debuff tooltip wording");
    Equal(debuff.Severity, TelegraphSeverity.Debuff, "debuff severity");
    var attackAndBlock = Show(JObject.Parse("{kind:'block',moveId:'bash',damage:5,hits:1,block:5}"));
    Equal(attackAndBlock.Kind, "attack", "damage outranks block, as intentBadge does");
    foreach (var (json, label) in new[] { ("null", "no intent"), ("{kind:'unknown',moveId:null}", "unknown kind"), ("{kind:'attack',moveId:null,damage:9}", "null moveId (co-op unknown)"), ("{kind:'weird',moveId:'x'}", "unmapped kind") })
    {
        var u = Show(json == "null" ? null : JObject.Parse(json));
        Check(u.Kind == "unknown" && u.BadgeText == "?" && u.TooltipText == "Intent: Unknown" && u.TooltipBody == null, "unknown intent from " + label);
    }
    var staggered = Show(JObject.Parse("{kind:'staggered',moveId:null}"));
    Check(staggered.Kind == "staggered" && staggered.BadgeText == "✦ Staggered" && staggered.Icon.Tone == "gold", "staggered intent shows the SPEC §4.6 Staggered badge");
    Equal(staggered.TooltipText, "Staggered\nPoise broken — this enemy's turn is skipped and it takes +50% damage.", "staggered tooltip wording");
    var preview = Show(JObject.Parse("{kind:'attack',moveId:'flurry',damage:4,hits:3}"), preview: (e, basis) => basis * 3 / 2);
    Check(preview.ValueText == "6×3" && preview.TotalDamage == 18, "preview delegate supplies the modifier-inclusive per-hit number");
    var lethal = Show(JObject.Parse("{kind:'attack',moveId:'flurry',damage:6,hits:3}"), player: JObject.Parse("{hp:15,block:3}"));
    Equal(lethal.Severity, TelegraphSeverity.Lethal, "attack total ≥ HP + Block is Lethal");
    var survivable = Show(JObject.Parse("{kind:'attack',moveId:'flurry',damage:6,hits:3}"), player: JObject.Parse("{hp:16,block:3}"));
    Equal(survivable.Severity, TelegraphSeverity.Attack, "attack total < HP + Block stays Attack");
    Check(EnemyTelegraphViewModel.Build(Enemy(JObject.Parse("{kind:'attack',moveId:'a',damage:3}"), hp: 0)).Intent == null, "dead enemy has no intent");

    // ---- Poise edge cases -------------------------------------------------------------
    PoiseDisplay Poise(JObject enemy) => EnemyTelegraphViewModel.Poise(enemy, 1.25, "Tip.");
    var empty = Poise(Enemy(null, 0, 10));
    Check(empty.Visible && empty.Fraction == 0 && empty.ValueText == "0/10" && !empty.NearBreak && !empty.Broken, "empty meter");
    var partial = Poise(Enemy(null, 4, 10));
    Check(partial.Fraction == 0.4 && partial.Tooltip == "Poise\n4 / 10 — fill it to Stagger. Tip.", "partial meter fraction and tooltip");
    Check(!Poise(Enemy(null, 7, 10)).NearBreak && Poise(Enemy(null, 8, 10)).NearBreak, "near-break cue starts at 75% (coop.js .full)");
    Check(Poise(Enemy(null, 3, 4)).NearBreak, "near-break at exactly 75%");
    Check(Poise(Enemy(null, 15, 10)).Fraction == 1, "overfull value clamps to a full track");
    Check(Poise(Enemy(null, -3, 10)).Current == 0 && Poise(Enemy(null, -3, 10)).Fraction == 0, "negative value clamps to 0");
    var none = Poise(Enemy(null, 0, 0));
    Check(!none.Visible && none.Fraction == 0 && none.Ticks.Count == 0 && none.ValueText == "" && none.Tooltip == null, "zero max draws no meter");
    var missing = new JObject { ["id"] = "e9", ["hp"] = 5 };
    Check(!EnemyTelegraphViewModel.Build(missing).Poise.Visible, "entity without poiseMeter draws no meter");
    Check(partial.Ticks.SequenceEqual(new[] { 0.75 }), "threshold tick at the near-break cut");
    Equal(Poise(Enemy(null, 0, 10)).NextMax, 13, "next threshold grows by ceil(max × 1.25)");
    Equal(EnemyTelegraphViewModel.Poise(Enemy(null, 0, 10), 1).NextMax, 10, "growth disabled keeps the threshold");
    var broken = Enemy(JObject.Parse("{kind:'staggered',moveId:null}"), 0, 13, skip: true);
    ((JObject)broken["statuses"]!)["staggered"] = new JObject { ["stacks"] = 2 };
    var brokenPoise = Poise(broken);
    Check(brokenPoise.Broken && brokenPoise.Staggered && brokenPoise.StaggerStacks == 2, "broken + staggered state after a fill");
    var windowOnly = Enemy(JObject.Parse("{kind:'attack',moveId:'a',damage:3}"), 2, 13);
    ((JObject)windowOnly["statuses"]!)["staggered"] = new JObject { ["stacks"] = 1 };
    Check(!Poise(windowOnly).Broken && Poise(windowOnly).Staggered, "damage window can outlast the skipped turn");

    // ---- Live CombatSession: engine preview, purity, stagger ---------------------------
    var oracle = JObject.Parse(Read("UnityTests/Parity/combat-reference.json"));
    var catalog = new OriginalContentCatalog(oracle["content"]!.ToString());
    var mechanics = (JObject)oracle["mechanics"]!;
    var definitions = (JObject)oracle["definitions"]!;
    var basis = oracle["fixtures"]![0]!;
    JObject Card(JArray effects) { var d = (JObject)definitions["strike:false"]!.DeepClone(); d["effects"] = effects; return d; }
    CombatSession Make(string enemyId, uint seed, JArray effects) => new CombatSession(catalog, mechanics, new RandomStreams(seed), (JObject)basis["player"]!,
        new[] { new JObject { ["instanceId"] = "owner", ["cardId"] = "strike", ["upgraded"] = false } }, new[] { enemyId }, _ => Card(effects), 1);
    CombatSession attacking = null;
    for (uint seed = 1; seed < 200 && attacking == null; seed++)
    {
        var s = Make("courtDuelist", seed, JArray.Parse("[{op:'applyStatus',target:'self',status:'vulnerable',stacks:1}]"));
        s.EndTurn(); // opener En Garde: +3 Strength, then a rolled move
        if ((string)s.Enemies[0]!["intent"]!["kind"]! == "attack" && s.Enemies[0]!["intent"]!["damage"]!.Type != JTokenType.Null) attacking = s;
    }
    Check(attacking != null, "found a live attacking intent");
    var snapshot = attacking!.Snapshot();
    var live = attacking.Telegraphs();
    Check(JToken.DeepEquals(snapshot, attacking.Snapshot()), "Telegraphs() leaves the combat snapshot unchanged");
    Equal(live.Count, attacking.Enemies.Count, "one telegraph per enemy");
    var authored = (int)attacking.Enemies[0]!["intent"]!["damage"]!;
    Equal(live[0].Intent.Damage, (int?)attacking.PreviewEnemyAttack("e1", authored), "intent number equals the engine preview");
    Equal(live[0].Intent.Damage, (int?)(authored + 3), "intent number includes the enemy's +3 Strength");
    Check(live[0].Poise.Tooltip!.EndsWith("takes 50% more attack damage until the end of your next turn."), "live Poise tooltip carries the staggered status text");
    attacking.PlayCard("owner");
    var vulnerable = attacking.Telegraphs()[0].Intent;
    Equal(vulnerable.Damage, (int?)(int)Math.Floor((authored + 3) * 1.5), "player Vulnerable raises the intent number live (recomputed)");
    Equal(vulnerable.TotalDamage, vulnerable.Damage * vulnerable.Hits, "live total = per-hit × hits");

    var stagger = Make("wanderingSoldier", 1, JArray.Parse("[{op:'poiseDamage',target:'enemy',amount:1}]"));
    var max = (int)stagger.Enemies[0]!["poiseMeter"]!["max"]!;
    stagger.PlayCard("owner", "e1");
    var chipped = stagger.Telegraphs()[0].Poise;
    Check(chipped.Current == 1 && chipped.Max == max && !chipped.Broken, "live poise damage fills the meter");
    var filler = Make("wanderingSoldier", 1, new JArray(new JObject { ["op"] = "poiseDamage", ["target"] = "enemy", ["amount"] = max }));
    filler.PlayCard("owner", "e1");
    var filled = filler.Telegraphs()[0];
    Check(filled.Poise.Broken && filled.Poise.Staggered && filled.Poise.Current == 0 && filled.Poise.Max == (int)Math.Ceiling(max * 1.25), "live fill Staggers, resets and grows the meter");
    Equal(filled.Intent.Kind, "staggered", "live staggered enemy shows the Staggered intent");
    Check(!attacking.Telegraphs().Any(t => t.Intent == null), "living enemies always have an intent");
    try { attacking.PreviewEnemyAttack("missing", 1); Check(false, "unknown enemy preview refused"); } catch (ArgumentException) { Check(true, "unknown enemy preview refused"); }
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: " + error.Message);
}

if (failures > 0) { Console.Error.WriteLine("Telegraphs: " + failures + " failed, " + checks + " passed"); return 1; }
Console.WriteLine("Telegraphs: " + checks + " checks passed");
return 0;
