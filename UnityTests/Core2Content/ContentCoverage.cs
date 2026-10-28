// Static content coverage: every Core 2 content row a run can produce, walked for the opcodes, effect targets, predicates,
// formula ops, formula and turn metrics, trigger events, status-rule behaviours and semantic fields it uses, each checked
// against what the engine-free Core 2 domain (Runtime/Domain/Core2) actually handles. A content change that brings in
// something the C# does not run fails here, before a run can reach it.
//
// What is walked (GameContent/Unity/Core2):
//   - player cards: every core.* and base.* card (pack/cards.json; base cards are rewardable through
//     pack/baseCardAdmission.json), the legacy adapter cards (adapter/cards.json) and any card a gear row grants
//     (pack/gear.json grantedCards), with their effects, effect variants, end-of-turn hooks and card fields
//   - property rules (adapter/run.json propertyRules) mounted by a relic (adapter/relics.json), a worn armament or armour
//     piece (adapter/equipment.json) or a class (adapter/run.json classes): their triggers
//   - statuses (adapter/statuses.json): hooks and proc effects of every status, and the behaviours of every status rule
//     (proposalStatusRules) the Core 2 profile enables (JS proposalRuleEnabled with proposalContext's capabilities) whose
//     status some walked effect or burden can apply
//   - enemies (adapter/enemies.json): every move's effects and every phase
//   - flasks (adapter/flasks.json): effects and scripts
//   - events (adapter/events.json): choice effects, run-context predicates and formulas
//
// What "handled" means is read from the C# source, not declared here: the case labels of the dispatching methods
// (Core2Fight.RunOpcode, ResolveRawTargets, Predicate, Core2FightFormula Node / Metric, Core2FightRiders AbilityMetric,
// Core2FightProposal Behaviors, Core2RunEvents Execute / Predicate / Number) and the events the fight emits. A semantic
// field passes when the domain reads it (its quoted name appears in the Core 2 sources), or when every value the content
// gives it is one JS ignores (Inert). Anything JS itself never runs in a Core 2 fight is in Refused, with its reason and a
// guard over the content that keeps the reason true; a Refused entry whose guard fails, or that nothing uses, fails.
// Guards also cover the few C# refusals the content cannot reach (Guards).
using System.Text.RegularExpressions;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

static class ContentCoverage
{
    public sealed class Report
    {
        /// <summary>kind:name → where it is used (first few origins).</summary>
        public readonly SortedDictionary<string, List<string>> Used = new(StringComparer.Ordinal);
        public readonly List<string> Unhandled = new();
        public readonly List<string> Stale = new();
        public readonly List<string> SourceErrors = new();
        public readonly List<string> GuardFailures = new();
        public readonly List<string> RefusedLines = new();
        public int Handled;
    }

    static readonly string[] Presentation =
    {
        "id", "name", "icon", "flavor", "textTemplate", "artKey", "artAsset", "tint", "kindIds", "rarity", "class", "tooltip", "resultText", "label",
        "art", "artFaces", "size", "intent", "enemyId", "text", "speaker",
    };

    /// <summary>Card fields JS reads but whose every Core 2 value changes nothing: field → (the inert value, why).</summary>
    static readonly Dictionary<string, (JToken Value, string Reason)> Inert = new(StringComparer.Ordinal)
    {
        // model/actionCosts.js: 'fixed' keeps the authored cost, which is the configured price Core 2 reads anyway.
        ["card-field:costRule"] = ("fixed", "JS actionCosts.js keeps a 'fixed' card's authored cost; Core 2 prices read the authored cost"),
        // actions.js applyArcaneExposure: a hit builds Arcane Exposure only with exposureBuildupPerHit > 0.
        ["card-field:exposureBuildupPerHit"] = (0, "JS applyArcaneExposure builds nothing at 0 buildup per hit"),
    };

    // ---------------------------------------------------------------------------------------------------------------
    // Source: what the C# handles
    // ---------------------------------------------------------------------------------------------------------------

    sealed class Sources
    {
        public string All = "";
        public readonly Dictionary<string, HashSet<string>> Cases = new(StringComparer.Ordinal);
        public readonly HashSet<string> Emitted = new(StringComparer.Ordinal);
    }

    /// <summary>The body of the first method declared with this name in the file (brace-matched from its opening brace).</summary>
    static string MethodBody(string text, string name)
    {
        var m = Regex.Match(text, @"(?m)^\s*(?:(?:public|private|internal|static|protected|override|sealed)\s+)+[\w<>\[\],\.\?\s\(\)]*?\b" + Regex.Escape(name) + @"\s*\(");
        if (!m.Success) return null;
        var open = text.IndexOf('{', m.Index + m.Length);
        var semi = text.IndexOf(';', m.Index + m.Length);
        var arrow = text.IndexOf("=>", m.Index + m.Length, StringComparison.Ordinal);
        if (open < 0 || (arrow >= 0 && arrow < open && (semi < 0 || arrow < semi))) return null;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }
        return null;
    }

    static HashSet<string> CaseLabels(string body) =>
        new(Regex.Matches(body ?? "", "case \"([^\"]+)\"").Select(m => m.Groups[1].Value), StringComparer.Ordinal);

    static Sources ReadSources(string root, Report report)
    {
        var s = new Sources();
        var folder = Path.Combine(root, "Unity", "Assets", "AshenSpire", "Runtime", "Domain", "Core2");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var texts = files.ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
        s.All = string.Join("\n", texts.Values);
        HashSet<string> From(string file, string method)
        {
            if (!texts.TryGetValue(file, out var text)) { report.SourceErrors.Add(file + " is missing"); return new HashSet<string>(); }
            var body = MethodBody(text, method);
            if (body == null) { report.SourceErrors.Add(file + ": method " + method + " was not found"); return new HashSet<string>(); }
            var labels = CaseLabels(body);
            if (labels.Count == 0) report.SourceErrors.Add(file + ": method " + method + " has no case labels");
            return labels;
        }
        s.Cases["op"] = From("Core2FightActions.cs", "RunOpcode");
        s.Cases["target"] = From("Core2FightActions.cs", "ResolveRawTargets");
        s.Cases["pred"] = From("Core2Fight.cs", "Predicate");
        s.Cases["f"] = From("Core2FightFormula.cs", "Node");
        s.Cases["metric"] = From("Core2FightFormula.cs", "Metric");
        s.Cases["turnMetric"] = From("Core2FightRiders.cs", "AbilityMetric");
        s.Cases["behavior"] = From("Core2FightProposal.cs", "Behaviors");
        s.Cases["run-op"] = From("Core2RunEvents.cs", "Execute");
        s.Cases["run-pred"] = From("Core2RunEvents.cs", "Predicate");
        s.Cases["run-f"] = From("Core2RunEvents.cs", "Number");
        // The ops the queue runs before the opcode switch (the group completion) are part of the interpreter.
        s.Cases["op"].Add("completeCombatAction");
        foreach (Match m in Regex.Matches(s.All, "Emit\\(\"([A-Za-z]+)\"")) s.Emitted.Add(m.Groups[1].Value);
        // Owner hooks fire through FireOwnerHooks(entity, "ownerTurnStart" / "ownerTurnEnd"); hpBelowPct through CheckPhases.
        foreach (Match m in Regex.Matches(s.All, "FireOwnerHooks\\([^,]+, \"([A-Za-z]+)\"\\)")) s.Emitted.Add(m.Groups[1].Value);
        if (s.All.Contains("(string)phase[\"on\"] != \"hpBelowPct\"")) s.Emitted.Add("hpBelowPct");
        return s;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Content walk
    // ---------------------------------------------------------------------------------------------------------------

    sealed class Walker
    {
        public readonly Report Report;
        /// <summary>Every walked effect with where it came from (the guards read them).</summary>
        public readonly List<(JObject Effect, string Origin)> Effects = new();
        public readonly List<(JObject Trigger, string Origin)> Triggers = new();
        public readonly HashSet<string> Applied = new(StringComparer.Ordinal);
        /// <summary>Every value each semantic field takes (Inert reads them).</summary>
        public readonly Dictionary<string, List<JToken>> FieldValues = new(StringComparer.Ordinal);
        public Walker(Report report) { Report = report; }

        public void Use(string kind, string name, string origin)
        {
            if (name == null) return;
            var key = kind + ":" + name;
            if (!Report.Used.TryGetValue(key, out var list)) Report.Used[key] = list = new List<string>();
            if (list.Count < 4 && !list.Contains(origin)) list.Add(origin);
        }

        public void Fields(string kind, JObject row, string origin, params string[] skip)
        {
            foreach (var p in row.Properties())
            {
                if (Presentation.Contains(p.Name) || skip.Contains(p.Name)) continue;
                Use(kind + "-field", p.Name, origin);
                var key = kind + "-field:" + p.Name;
                if (!FieldValues.TryGetValue(key, out var values)) FieldValues[key] = values = new List<JToken>();
                values.Add(p.Value);
            }
        }

        /// <summary>A fight formula (JS src/model/formulas.js evalNode) or a plain number.</summary>
        public void Formula(JToken value, string origin, string context = "fight")
        {
            if (value is not JObject row || row["f"] == null) return;
            var f = (string)row["f"];
            Use(context == "run" ? "run-f" : "f", f, origin);
            if (f == "metric") Use("metric", (string)row["metric"], origin);
            foreach (var arg in row["args"] as JArray ?? new JArray()) Formula(arg, origin, context);
            if (row["base"] != null) Formula(row["base"], origin, context);
        }

        public void Predicate(JToken value, string origin, string context = "fight")
        {
            if (value is not JObject pred) return;
            var p = (string)pred["p"];
            Use(context == "run" ? "run-pred" : "pred", p, origin);
            if (p == "turnMetric") Use("turnMetric", (string)pred["metric"], origin);
            foreach (var sub in pred["preds"] as JArray ?? new JArray()) Predicate(sub, origin, context);
            if (pred["pred"] != null) Predicate(pred["pred"], origin, context);
        }

        /// <summary>One fight effect (JS actions.js executeAction / runOpcode).</summary>
        public void Effect(JObject eff, string origin)
        {
            Effects.Add((eff, origin));
            if (eff["script"]?.Type == JTokenType.String) { Use("script", (string)eff["script"], origin); return; }
            var op = (string)eff["op"];
            Use("op", op, origin);
            if (eff["target"]?.Type == JTokenType.String) Use("target", (string)eff["target"], origin);
            if (eff["status"]?.Type == JTokenType.String) Applied.Add((string)eff["status"]);
            Fields("effect", eff, origin, "op");
            foreach (var p in eff.Properties())
            {
                if (p.Name == "if") Predicate(p.Value, origin);
                else if (p.Name == "changes" && p.Value is JObject changes) foreach (var c in changes.Properties()) { Use("effect-field", "changes." + c.Name, origin); Formula(c.Value, origin); }
                else Formula(p.Value, origin);
            }
        }

        public void Effects2(JToken effects, string origin)
        {
            foreach (var eff in (effects as JArray ?? new JArray()).OfType<JObject>()) Effect(eff, origin);
        }

        /// <summary>A trigger ({ on, if, do, once, limitPerTurn }) on the fight's bus (JS triggers.js maybeFire / firePhase).</summary>
        public void Trigger(JObject trigger, string origin)
        {
            Triggers.Add((trigger, origin));
            Use("on", (string)trigger["on"], origin);
            Fields("trigger", trigger, origin, "on", "do", "if");
            Predicate(trigger["if"], origin);
            Effects2(trigger["do"], origin);
        }

        public void Card(JObject card, string origin)
        {
            Fields("card", card, origin);
            Effects2(card["effects"], origin);
            foreach (var variant in (card["effectVariants"] as JArray ?? new JArray()).OfType<JObject>())
            {
                Predicate(variant["if"], origin + " variant");
                Effects2(variant["effects"], origin + " variant");
            }
            Effects2(card["onTurnEndInHand"], origin + " onTurnEndInHand");
            Formula(card["targetLimit"], origin + " targetLimit");
            foreach (var keyword in card["keywords"] as JArray ?? new JArray()) Use("keyword", (string)keyword, origin);
            if (card["burden"] is JObject burden)
            {
                foreach (var p in burden.Properties()) Use("burden-field", p.Name, origin);
                if (burden["status"]?.Type == JTokenType.String) Applied.Add((string)burden["status"]);
            }
            if (card["costGrowth"] is JObject growth) foreach (var p in growth.Properties()) Use("costGrowth-field", p.Name, origin);
        }
    }

    /// <summary>JS proposalStatusRuntime.js proposalContext + proposalStatusRules.js proposalRuleEnabled for the Core 2 profile.</summary>
    static bool RuleEnabled(Core2Content content, JObject rule)
    {
        var modules = (JObject)content.Profile["modules"];
        var enabled = modules.Properties().Where(p => p.Value.Type != JTokenType.Null && !(p.Value.Type == JTokenType.Boolean && !(bool)p.Value)).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var protections = Core2Json.Strings(modules["defenses"]?["include"]).ToHashSet(StringComparer.Ordinal);
        var capabilities = new Dictionary<string, HashSet<string>> { ["modules"] = enabled, ["elements"] = new HashSet<string>(), ["protections"] = protections };
        foreach (var p in (rule["requires"] as JObject ?? new JObject()).Properties())
            if (capabilities.TryGetValue(p.Name, out var have) && !Core2Json.Strings(p.Value).All(have.Contains)) return false;
        return true;
    }

    /// <summary>JS triggers that need a status-bearing meterFilled (eventStatusIs) to pass.</summary>
    static bool NeedsStatus(JToken pred) => pred is JObject p && ((string)p["p"] == "eventStatusIs" || (string)p["p"] == "all" && (p["preds"] as JArray ?? new JArray()).Any(NeedsStatus));

    public static Report Scan(Core2Content content, string root)
    {
        var report = new Report();
        var w = new Walker(report);
        var run = (JObject)content.Table("adapter/run.json");

        // Player cards: core.* and base.* (pack/cards.json), legacy adapter cards, gear grants.
        var cardIds = new SortedSet<string>(content.Cards.Where(c => c.Id.StartsWith("core.", StringComparison.Ordinal) || c.Id.StartsWith("base.", StringComparison.Ordinal)).Select(c => c.Id), StringComparer.Ordinal);
        foreach (var gear in (JArray)content.Table("pack/gear.json"))
            foreach (var granted in gear["grantedCards"] as JArray ?? new JArray())
                cardIds.Add(granted is JObject g ? (string)(g["card"] ?? g["cardId"]) : (string)granted);
        foreach (var id in cardIds) w.Card(content.Card(id).Raw, "card " + id);
        foreach (var card in content.AdapterCards) w.Card(card.Raw, "adapter card " + card.Id);

        // Property rules mounted by relics, gear and classes.
        var rules = (JObject)run["propertyRules"];
        var carriers = new List<(string Origin, IEnumerable<string> Tags)>();
        foreach (var relic in content.Relics) carriers.Add(("relic " + relic.Id, Core2Json.Strings(relic.Raw["propertyTags"])));
        foreach (var piece in content.Armaments) carriers.Add(("armament " + piece.Id, Core2Json.Strings(piece.Raw["propertyTags"])));
        foreach (var piece in content.Armour) carriers.Add(("armour " + piece.Id, Core2Json.Strings(piece.Raw["propertyTags"])));
        foreach (var cls in ((JObject)run["classes"]).Properties()) carriers.Add(("class " + cls.Name, Core2Json.Strings(cls.Value["propertyTags"])));
        foreach (var (origin, tags) in carriers)
            foreach (var tag in tags)
            {
                if (rules[tag] is not JObject rule) continue;
                foreach (var trigger in (rule["triggers"] as JArray ?? new JArray()).OfType<JObject>()) w.Trigger(trigger, origin + " (" + tag + ")");
            }

        // Statuses: hooks and proc effects.
        foreach (var status in content.Statuses)
        {
            var origin = "status " + status.Id;
            foreach (var hook in (status.Raw["hooks"] as JArray ?? new JArray()).OfType<JObject>()) w.Trigger(hook, origin);
            if (status.Raw["proc"] is JObject proc) w.Effects2(proc["effects"], origin + " proc");
        }

        // Enemies: moves and phases.
        foreach (var enemy in content.Enemies)
        {
            foreach (var move in enemy.Moves) w.Effects2(move.Raw["effects"], "enemy " + enemy.Id + "." + move.Id);
            foreach (var phase in (enemy.Raw["phases"] as JArray ?? new JArray()).OfType<JObject>()) w.Trigger(phase, "enemy " + enemy.Id + " phase");
        }

        // Flasks.
        foreach (var flask in content.Flasks) w.Effects2(flask.Raw["effects"], "flask " + flask.Id);

        // Status rules: the enabled behaviours of every status some walked effect or burden can apply (and their own grants).
        var proposal = (JObject)((JObject)content.Table("adapter/statuses.json"))["proposalStatusRules"];
        var pending = new Queue<string>(w.Applied);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!seen.Add(id) || proposal[id] is not JObject rule || !RuleEnabled(content, rule)) continue;
            foreach (var behavior in (rule["behaviors"] as JArray ?? new JArray()).OfType<JObject>())
            {
                w.Use("behavior", (string)behavior["kind"], "status rule " + id);
                foreach (var key in new[] { "status", "burnStatus" }) if (behavior[key]?.Type == JTokenType.String) pending.Enqueue((string)behavior[key]);
            }
        }

        // Events: run opcodes, predicates and formulas.
        foreach (var ev in content.Events)
            foreach (var choice in (ev.Raw["choices"] as JArray ?? new JArray()).OfType<JObject>())
                foreach (var eff in (choice["effects"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var origin = "event " + ev.Id + " / " + (string)choice["label"];
                    w.Use("run-op", (string)eff["op"], origin);
                    foreach (var p in eff.Properties())
                    {
                        if (p.Name == "if") w.Predicate(p.Value, origin, "run");
                        else w.Formula(p.Value, origin, "run");
                    }
                }

        // ---- refusals and guards (the reason each stays true is checked on the content) ----
        var exposure = content.Cards.Concat(content.EnemyCards).Select(c => c.Raw["exposureBuildupPerHit"]).Concat(((JArray)content.Table("pack/gear.json")).Select(g => g["exposureBuildupPerHit"]))
            .Concat(content.Armaments.Select(a => a.Raw["exposureBuildupPerHit"])).Where(v => v != null && v.Type != JTokenType.Null).ToList();
        var noExposure = exposure.All(v => (double)v <= 0);
        var meterStatuses = content.Statuses.Where(s => s.Raw["meter"] != null).Select(s => s.Id).ToList();
        var refused = new Dictionary<string, (string Reason, bool Holds)>(StringComparer.Ordinal)
        {
            // actions.js:1983 is the only emitter of stanceEntered: the legacy enterStance with a registry stance id (eff.stance).
            // Core 2 enters maneuvers (eff.maneuver / choose 'maneuver', actions.js:1962-1969 → setCombatStance → combatStanceChanged).
            ["on:stanceEntered"] = ("JS emits stanceEntered only for a registry stance (actions.js:1983); every Core 2 enterStance names a maneuver",
                w.Effects.Where(e => (string)e.Effect["op"] == "enterStance").All(e => e.Effect["stance"] == null)),
            // actions.js:868-905 applyArcaneExposure / addArcaneExposure: Arcane Exposure fills only from a hit with
            // exposureBuildupPerHit > 0 (or an arcaneBuildup pour, itself only inside an arcaneBreak trigger).
            ["on:arcaneBreak"] = ("JS raises arcaneBreak only when Arcane Exposure fills (actions.js:868-905); every Core 2 card, enemy card and gear row builds 0 per hit",
                noExposure && w.Effects.Where(e => (string)e.Effect["op"] == "arcaneBuildup").All(e => w.Triggers.Any(t => (string)t.Trigger["on"] == "arcaneBreak" && t.Trigger["do"] is JArray d && d.Contains(e.Effect)))),
            ["op:arcaneBuildup"] = ("only inside an arcaneBreak trigger (Goldbough's resonance), which never fires (on:arcaneBreak)",
                w.Effects.Where(e => (string)e.Effect["op"] == "arcaneBuildup").All(e => w.Triggers.Any(t => (string)t.Trigger["on"] == "arcaneBreak" && t.Trigger["do"] is JArray d && d.Contains(e.Effect)))),
            // actions.js:1927-1929: hpOfferingPaid comes only from a loseHp with offering: true.
            ["on:hpOfferingPaid"] = ("JS raises hpOfferingPaid only for a loseHp offering (actions.js:1927); no Core 2 effect is an offering",
                w.Effects.All(e => e.Effect["offering"] == null)),
            // statuses.js:213 checkMeterFill is the only meterFilled carrying a status; Core 2 statuses fill a proc (procBurst).
            ["on:meterFilled"] = ("JS's only status-bearing meterFilled is a meter status's fill (statuses.js:213); Core 2 statuses are procs, and every meterFilled hook asks eventStatusIs",
                meterStatuses.Count == 0 && w.Triggers.Where(t => (string)t.Trigger["on"] == "meterFilled").All(t => NeedsStatus(t.Trigger["if"]))),
        };
        // The C# refusals left in the domain, each kept unreachable by the content.
        var guards = new Dictionary<string, (string Reason, bool Holds)>(StringComparer.Ordinal)
        {
            ["buildup"] = ("Core2FightStatuses ApplyStatusPressure: an enemy's legacy Poise vessel and Cinder Sight are not modelled — no Core 2 effect uses the buildup opcode",
                w.Effects.All(e => (string)e.Effect["op"] != "buildup")),
            ["charging effects"] = ("Core2FightEnemies StartDelayedMove: no Core 2 enemy move charges with effects (delay.whileCharging.effects)",
                content.Enemies.SelectMany(e => e.Moves).All(m => !(m.Raw["delay"]?["whileCharging"]?["effects"] is JArray a && a.Count > 0))),
            ["status meter"] = ("Core2FightStatuses: no Core 2 status carries a legacy meter", meterStatuses.Count == 0),
            ["ward fill"] = ("Core2FightStatuses: a spell fill against a standing Ward needs the Ward protection, which Core 2's defenses leave out",
                !Core2Json.Strings(content.Profile["modules"]?["defenses"]?["include"]).Contains("ward")),
            ["sleep"] = ("Core2FightStatuses SleepRestoration: no Core 2 effect applies Sleep", !w.Applied.Contains("sleep")),
            ["enemy move targets"] = ("Core2EnemyMoves: every enemy move effect aims at the player, the enemy or the mover",
                content.Enemies.SelectMany(e => e.Moves).SelectMany(m => (m.Raw["effects"] as JArray ?? new JArray()).OfType<JObject>())
                    .All(e => e["target"] == null || new[] { "player", "enemy", "self", "owner" }.Contains((string)e["target"]))),
            ["enemy move tags"] = ("Core2EnemyMoves: no enemy move effect carries its own tags",
                content.Enemies.SelectMany(e => e.Moves).SelectMany(m => (m.Raw["effects"] as JArray ?? new JArray()).OfType<JObject>()).All(e => e["tags"] == null)),
            ["enemy move formulas"] = ("Core2Formula (enemy planning): enemy move numbers are plain numbers, add or mul",
                content.Enemies.SelectMany(e => e.Moves).SelectMany(m => (m.Raw["effects"] as JArray ?? new JArray()).OfType<JObject>())
                    .SelectMany(e => e.Properties().Select(p => p.Value)).All(PlainFormula)),
            ["conditional engagement"] = ("Core2EnemyPlanning: no enemy engagement move effect is conditional",
                content.Enemies.SelectMany(e => e.Moves).SelectMany(m => (m.Raw["effects"] as JArray ?? new JArray()).OfType<JObject>()).All(e => e["if"] == null)),
        };
        foreach (var (key, (reason, holds)) in refused)
        {
            report.RefusedLines.Add(key + ": " + reason);
            if (!holds) report.GuardFailures.Add("refusal " + key + " no longer holds: " + reason);
        }
        foreach (var (key, (reason, holds)) in guards) if (!holds) report.GuardFailures.Add("guard " + key + " no longer holds: " + reason);

        // ---- compare ----
        var sources = ReadSources(root, report);
        foreach (var (key, origins) in report.Used)
        {
            var kind = key.Substring(0, key.IndexOf(':'));
            var name = key.Substring(kind.Length + 1);
            bool handled = kind switch
            {
                "on" => sources.Emitted.Contains(name),
                "script" => sources.All.Contains("\"" + name + "\""),
                _ when Inert.TryGetValue(key, out var inert) => w.FieldValues[key].All(v => JToken.DeepEquals(v, inert.Value)),
                _ when kind.EndsWith("-field", StringComparison.Ordinal) || kind == "keyword" => FieldRead(sources.All, name),
                _ => sources.Cases.TryGetValue(kind, out var set) && set.Contains(name),
            };
            if (handled) report.Handled++;
            else if (!refused.ContainsKey(key)) report.Unhandled.Add(key + "  <- " + string.Join(" | ", origins));
        }
        foreach (var key in refused.Keys) if (!report.Used.ContainsKey(key)) report.Stale.Add(key);
        foreach (var (key, (value, reason)) in Inert) report.RefusedLines.Add(key + " (inert at " + value.ToString(Newtonsoft.Json.Formatting.None) + "): " + reason);
        return report;
    }

    static bool PlainFormula(JToken value) => value is not JObject row || row["f"] == null
        || ((string)row["f"] == "add" || (string)row["f"] == "mul") && (row["args"] as JArray ?? new JArray()).All(PlainFormula);

    /// <summary>A field or keyword the domain reads: its quoted name appears in the Core 2 sources ("changes.x" reads x).</summary>
    static bool FieldRead(string sources, string name)
    {
        var leaf = name.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name;
        return sources.Contains("\"" + leaf + "\"");
    }
}
