// EnemyTelegraph.cs — pure view-model for enemy intent badges and Poise meters (F04).
// Mirrors src/ui/uiContent.js intentBadge/intentTooltip and the enemy Poise bar
// (src/model/resources.js poise source, coop.js `.poisebar`). Reads enemy/player
// JObjects only; never mutates them. UI Toolkit wiring is pending — see docs/Unity-Telegraphs.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    /// <summary>Colour/urgency tier. Tone names match the HTML StatePill data-tone.</summary>
    public enum TelegraphSeverity { Unknown, Defend, Buff, Debuff, Stagger, Attack, Lethal }

    /// <summary>One row of the intent icon table.</summary>
    public sealed class IntentIcon
    {
        public IntentIcon(string kind, string iconId, string glyph, string tone, string colorHex, string assetId, string assetPath, bool dedicatedArt)
        { Kind = kind; IconId = iconId; Glyph = glyph; Tone = tone; ColorHex = colorHex; AssetId = assetId; AssetPath = assetPath; DedicatedArt = dedicatedArt; }
        /// <summary>Intent kind (SPEC §4.6; src/model/schemas.js INTENT_KINDS + "staggered").</summary>
        public string Kind { get; }
        /// <summary>The HTML class on `.intent.lg` (uiContent.js intentBadge `cls`), also the USS class.</summary>
        public string IconId { get; }
        /// <summary>The HTML glyph (uiContent.js INTENT_ICONS).</summary>
        public string Glyph { get; }
        /// <summary>HTML StatePill data-tone ("" = untinted).</summary>
        public string Tone { get; }
        /// <summary>Resolved tone colour from styles/base.css / styles/kit.css.</summary>
        public string ColorHex { get; }
        /// <summary>Framework asset id (src/framework/data/assets.js) of the sprite fallback.</summary>
        public string AssetId { get; }
        /// <summary>Repository-relative file under assets/ for the sprite fallback.</summary>
        public string AssetPath { get; }
        /// <summary>False: the HTML draws intents as glyphs; no dedicated intent art ships in assets/.</summary>
        public bool DedicatedArt { get; }
    }

    public static class IntentIconTable
    {
        // The HTML has no intent image files: intents are glyph StatePills. The only
        // real icon file is the framework's typed ICON fallback, so every row points
        // at it until dedicated art (SPEC §1: game-icons.net) is imported.
        public const string FallbackAssetId = "asset.fallback.icon";
        public const string FallbackAssetPath = "assets/framework/missing.svg";

        public static readonly IReadOnlyList<IntentIcon> Entries = new[]
        {
            new IntentIcon("attack", "attack", "⚔", "danger", "#d4622e", FallbackAssetId, FallbackAssetPath, false),
            new IntentIcon("block", "block", "\U0001F6E1", "frost", "#7fa8c9", FallbackAssetId, FallbackAssetPath, false),
            new IntentIcon("buff", "buff", "↑", "gold", "#c9a227", FallbackAssetId, FallbackAssetPath, false),
            new IntentIcon("debuff", "debuff", "☾", "violet", "#a07ad6", FallbackAssetId, FallbackAssetPath, false),
            new IntentIcon("staggered", "staggered", "✦", "gold", "#c9a227", FallbackAssetId, FallbackAssetPath, false),
            new IntentIcon("unknown", "unknown", "?", "", "#7a6f5a", FallbackAssetId, FallbackAssetPath, false),
        };

        public static IntentIcon For(string kind) => Entries.FirstOrDefault(e => e.Kind == kind) ?? Entries.First(e => e.Kind == "unknown");
    }

    public sealed class IntentDisplay
    {
        internal IntentDisplay(IntentIcon icon, string kind, string label, string title, string body, TelegraphSeverity severity, bool delayed, bool pending, int? damage, int? hits, int? totalDamage, int? block)
        {
            Icon = icon; Kind = kind; ValueText = label; TooltipTitle = title; TooltipBody = body; Severity = severity; Delayed = delayed; Pending = pending;
            Damage = damage; Hits = hits; TotalDamage = totalDamage; Block = block;
        }
        public IntentIcon Icon { get; }
        public string Kind { get; }
        public string IconId => Icon.IconId;
        /// <summary>USS classes for the badge, e.g. "intent lg attack delayed".</summary>
        public string CssClasses => "intent lg " + IconId + (Delayed && Kind == "attack" ? " delayed" : "");
        /// <summary>The pill's label: "7", "6×3", "14 ⌛", "12", "Staggered", "?" or "".</summary>
        public string ValueText { get; }
        /// <summary>Glyph + label as the HTML pill reads (unknown shows its label "?" only, as intentBadge does).</summary>
        public string BadgeText => Kind == "unknown" ? ValueText : ValueText.Length == 0 ? Icon.Glyph : Icon.Glyph + " " + ValueText;
        public bool Dashed => Delayed && Kind == "attack";
        public string TooltipTitle { get; }
        /// <summary>Tooltip body in plain text (HTML &lt;b&gt;/&lt;br&gt; removed, lines joined with \n). Null when the HTML shows a title only.</summary>
        public string TooltipBody { get; }
        public string TooltipText => TooltipBody == null ? TooltipTitle : TooltipTitle + "\n" + TooltipBody;
        public TelegraphSeverity Severity { get; }
        public bool Delayed { get; }
        public bool Pending { get; }
        public int? Damage { get; }
        public int? Hits { get; }
        public int? TotalDamage { get; }
        public int? Block { get; }
    }

    public sealed class PoiseDisplay
    {
        internal PoiseDisplay(bool visible, int current, int max, bool broken, int staggerStacks, int nextMax, string tooltip)
        { Visible = visible; Current = current; Max = max; Broken = broken; StaggerStacks = staggerStacks; NextMax = nextMax; Tooltip = tooltip; }
        /// <summary>False when the entity has no meter (max ≤ 0), matching resources.js poise.read → null.</summary>
        public bool Visible { get; }
        public int Current { get; }
        public int Max { get; }
        /// <summary>Clamped current ÷ max in [0, 1].</summary>
        public double Fraction => Max <= 0 ? 0 : Math.Min(1d, Math.Max(0d, (double)Current / Max));
        /// <summary>Poise broke: the next enemy action is skipped (skipNextTurn / "staggered" intent).</summary>
        public bool Broken { get; }
        /// <summary>The "staggered" status is active (+50% attack damage taken).</summary>
        public bool Staggered => StaggerStacks > 0;
        public int StaggerStacks { get; }
        /// <summary>Near-break cue: coop.js adds `.full` at value ≥ 75% of max.</summary>
        public bool NearBreak => Visible && Current >= Max * NearBreakFraction;
        public const double NearBreakFraction = 0.75;
        /// <summary>Tick marks as fractions of the track (the 75% near-break cut).</summary>
        public IReadOnlyList<double> Ticks => Visible ? new[] { NearBreakFraction } : Array.Empty<double>();
        /// <summary>The threshold after the next fill (max × growthMult, ceiling — SPEC §3.7).</summary>
        public int NextMax { get; }
        public string ValueText => Visible ? Current + "/" + Max : "";
        public string Tone => "poise";
        public string ColorHex => "#c9a227";
        public string Tooltip { get; }
    }

    public sealed class EnemyTelegraph
    {
        internal EnemyTelegraph(string instanceId, string enemyId, bool alive, IntentDisplay intent, PoiseDisplay poise)
        { InstanceId = instanceId; EnemyId = enemyId; Alive = alive; Intent = intent; Poise = poise; }
        public string InstanceId { get; }
        public string EnemyId { get; }
        public bool Alive { get; }
        /// <summary>Null for a dead enemy (the HTML draws no intent for it).</summary>
        public IntentDisplay Intent { get; }
        public PoiseDisplay Poise { get; }
    }

    public static class EnemyTelegraphViewModel
    {
        public const string StaggeredTooltipBody = "Poise broken — this enemy's turn is skipped and it takes +50% damage.";

        /// <summary>"6" or "6×3" — the badge number (uiContent.js intentBadge).</summary>
        public static string FormatDamage(int damage, int hits) => hits > 1 ? damage + "×" + hits : damage.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Builds one enemy's telegraph. <paramref name="previewDamage"/> maps (enemy, authored per-hit damage) to the
        /// modifier-inclusive per-hit number (CombatSession.Telegraphs passes the engine calculator); null shows authored damage.
        /// <paramref name="player"/> is optional and only raises an attack to <see cref="TelegraphSeverity.Lethal"/>.
        /// </summary>
        public static EnemyTelegraph Build(JObject enemy, JObject player = null, Func<JObject, int, int> previewDamage = null,
            string victim = "you", double poiseGrowthMult = 1.25, string staggeredStatusTooltip = null)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            var alive = (bool?)enemy["alive"] ?? ((int?)enemy["hp"] ?? 0) > 0;
            return new EnemyTelegraph((string)enemy["id"], (string)enemy["enemyId"], alive,
                alive ? Intent(enemy, player, previewDamage, victim) : null, Poise(enemy, poiseGrowthMult, staggeredStatusTooltip));
        }

        public static IReadOnlyList<EnemyTelegraph> BuildAll(IEnumerable<JToken> enemies, JObject player = null, Func<JObject, int, int> previewDamage = null,
            string victim = "you", double poiseGrowthMult = 1.25, string staggeredStatusTooltip = null)
            => enemies.Cast<JObject>().Select(e => Build(e, player, previewDamage, victim, poiseGrowthMult, staggeredStatusTooltip)).ToList();

        private static int? Int(JToken token) => token == null || token.Type == JTokenType.Null ? (int?)null : (int)Math.Floor((double)token);

        public static IntentDisplay Intent(JObject enemy, JObject player = null, Func<JObject, int, int> previewDamage = null, string victim = "you")
        {
            var iv = enemy["intent"] as JObject;
            var kind = (string)iv?["kind"];
            var pending = enemy["pendingMove"] is JObject || (bool?)iv?["pending"] == true;
            // SPEC §4.6: Staggered replaces the intent. (uiContent.js tests moveId === null before
            // kind, so the HTML's staggered branch is unreachable and it shows "?"; the spec wins.)
            if (kind == "staggered")
                return new IntentDisplay(IntentIconTable.For("staggered"), "staggered", "Staggered", "Staggered", StaggeredTooltipBody, TelegraphSeverity.Stagger, false, false, null, null, null, null);
            if (iv == null || kind == null || kind == "unknown" || (iv.ContainsKey("moveId") && iv["moveId"].Type == JTokenType.Null))
                return Unknown();
            var delayed = (bool?)iv["delayed"] == true;
            var authored = Int(iv["damage"]);
            if (authored != null)
            {
                var damage = previewDamage != null ? previewDamage(enemy, authored.Value) : authored.Value;
                var hits = Int(iv["hits"]) ?? 1;
                var total = damage * hits;
                var label = FormatDamage(damage, hits) + (delayed ? " ⌛" : "");
                var body = "Attacking " + victim + " for " + damage + (hits > 1 ? " × " + hits + " (" + total + " total)" : "") + " damage (modifiers included).";
                if (pending) body += "\nCommitted: this delayed attack lands this coming turn — Stagger cancels it.";
                else if (delayed) body += "\nDelayed: it holds this turn and strikes the next. Stagger cancels it.";
                var lethal = player != null && total > 0 && total >= ((int?)player["hp"] ?? int.MaxValue) + ((int?)player["block"] ?? 0);
                return new IntentDisplay(IntentIconTable.For("attack"), "attack", label, "Intent: Attack", body, lethal ? TelegraphSeverity.Lethal : TelegraphSeverity.Attack, delayed, pending, damage, hits, total, Int(iv["block"]));
            }
            var block = Int(iv["block"]);
            if (block != null)
                return new IntentDisplay(IntentIconTable.For("block"), "block", block.Value.ToString(CultureInfo.InvariantCulture), "Intent: Defend", "Gaining Block.", TelegraphSeverity.Defend, delayed, pending, null, null, null, block);
            if (kind == "buff")
                return new IntentDisplay(IntentIconTable.For("buff"), "buff", "", "Intent: Buff", "Strengthening itself.", TelegraphSeverity.Buff, delayed, pending, null, null, null, null);
            if (kind == "debuff")
                return new IntentDisplay(IntentIconTable.For("debuff"), "debuff", "", "Intent: Debuff", "Hindering " + victim + ".", TelegraphSeverity.Debuff, delayed, pending, null, null, null, null);
            return Unknown();
        }

        private static IntentDisplay Unknown() => new IntentDisplay(IntentIconTable.For("unknown"), "unknown", "?", "Intent: Unknown", null, TelegraphSeverity.Unknown, false, false, null, null, null, null);

        public static PoiseDisplay Poise(JObject enemy, double growthMult = 1.25, string staggeredStatusTooltip = null)
        {
            var meter = enemy["poiseMeter"] as JObject;
            var max = Int(meter?["max"]) ?? 0;
            var current = Math.Max(0, Int(meter?["value"]) ?? 0);
            var intentKind = (string)(enemy["intent"] as JObject)?["kind"];
            var broken = (bool?)enemy["skipNextTurn"] == true || intentKind == "staggered";
            var stacks = StatusSystem.Stacks(enemy, "staggered");
            var nextMax = max <= 0 ? 0 : growthMult == 1 ? max : (int)Math.Ceiling(max * growthMult);
            var tooltip = max <= 0 ? null : ("Poise\n" + current + " / " + max + " — fill it to Stagger. " + (staggeredStatusTooltip ?? "")).TrimEnd();
            return new PoiseDisplay(max > 0, current, max, broken, stacks, nextMax, tooltip);
        }
    }
}
