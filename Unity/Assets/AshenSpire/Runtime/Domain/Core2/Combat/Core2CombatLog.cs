// Core2CombatLog.cs — the combat log model: the public, executed receipts of a fight as the JS log writes them, grouped
// by round, with the view the log draws (a role legend, a "Round N" heading per round, one line per entry and, in full
// detail, each hit's damage receipt) and the drawer's Small/Medium/Large heights. Never a prediction or an unstarted
// intent: only events that happened are read. Presentation (the drawer, the full-log door, drag-to-resize) is the UI
// phase's; this is the data it draws.
//
// JS at the pin:
//   src/model/combatLog.js                     combatLogEntries (every line's words, its actors and their sides),
//                                              reactionUnavailableNote and the refusal / range-skip sentences
//   src/ui/components/combatLogModal.js        renderCombatLogEntries (legend, Round N headings, entries, detail lines),
//                                              the Full detail / Compact log toggle
//   src/ui/components/damageReceipt.js         damageReceiptLines (the full-detail lines of one receipt)
//   src/ui/models/CombatToolsModel.js          COMBAT_LOG_SIZES, COMBAT_LOG_DEFAULT_SIZE, combatLogHeight,
//                                              combatLogSnapSize, combatLogDragScale
//   src/ui/models/CombatLogPaletteModel.js     combatLogPalette, hasCombatLogColorContrast
//   ui/combat.json (export)                    the log's UI copy and sizes (content/source/uiStrings.csv)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One run of an entry's text: plain words, or a named actor (its id and side, player or enemy).</summary>
    public sealed class Core2LogSegment
    {
        public string Text { get; set; }
        public string ActorId { get; set; }
        /// <summary>player or enemy for a named actor; null for plain words.</summary>
        public string Side { get; set; }
    }

    /// <summary>One line of the combat log (JS combatLogEntries row): the event it came from, its round and its words.</summary>
    public sealed class Core2LogEntry
    {
        /// <summary>The index of the event in the fight's event list.</summary>
        public int EventIndex { get; set; }
        public int Round { get; set; }
        public string Text { get; set; }
        public List<Core2LogSegment> Segments { get; set; } = new List<Core2LogSegment>();
        /// <summary>The hit's damage receipt (JS event.damageReceipt), for the full-detail lines; null for other entries.</summary>
        public JObject DamageReceipt { get; set; }
    }

    /// <summary>One row of the drawn log (JS renderCombatLogEntries node): legend, heading, entry, detail or empty.</summary>
    public sealed class Core2LogViewRow
    {
        public string Kind { get; set; }
        public string Text { get; set; }
        /// <summary>The entry an entry or detail row belongs to; null for the legend, a heading or the empty note.</summary>
        public Core2LogEntry Entry { get; set; }
        public override string ToString() => Kind + " " + Text;
    }

    public static class Core2CombatLog
    {
        public const string PlayerName = "Forsaken";

        private static readonly Dictionary<string, string> AttackKindWords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sweep"] = "a Sweep", ["ranged"] = "a ranged attack", ["spell"] = "a spell", ["area"] = "an area attack", ["statusOnly"] = "a status effect", ["attack"] = "this attack",
        };
        private static readonly Dictionary<string, string> ResourceWords = new Dictionary<string, string>(StringComparer.Ordinal) { ["stamina"] = "SP", ["mana"] = "MP" };
        private static readonly Dictionary<string, string> SourceWords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["poise"] = "Poise overflow", ["defendStance"] = "Defend stance", ["sleep"] = "Sleep", ["barrier"] = "Barrier overflow", ["rating"] = "rating change", ["recovery"] = "Recovery", ["flux"] = "flux",
        };
        private static readonly Dictionary<string, string> ImpactWords = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hit"] = "the hit", ["poiseDamage"] = "Poise damage", ["trait:impact"] = "Impact", ["trait:guard"] = "a Defend guard", ["charge:break"] = "a charged break",
        };
        private static readonly Dictionary<string, string> PreventionWords = new Dictionary<string, string>(StringComparer.Ordinal) { ["invulnerability"] = "Invulnerability", ["decoy"] = "Decoy" };

        /// <summary>JS word: camelCase to words, first letter capital.</summary>
        public static string Word(string value)
        {
            var text = Regex.Replace(value ?? "", "([a-z])([A-Z])", "$1 $2");
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static JObject Ui(Core2Content content) => (JObject)content.Table("ui/combat.json");
        private static string UiString(Core2Content content, string id) => (string)Ui(content)["strings"][id] ?? throw new KeyNotFoundException("ui/combat.json has no string " + id);
        private static string Fill(string template, params (string Token, object Value)[] tokens)
        {
            foreach (var (token, value) in tokens) template = template.Replace("{" + token + "}", Convert.ToString(value, CultureInfo.InvariantCulture));
            return template;
        }

        // ---- names (JS row, nameLookup, statusName, originName, moveName) -------------------------------------------

        private static string CardName(Core2Content content, string id) => id != null && content.HasCard(id) ? content.Card(id).Name : null;
        private static string StatusName(Core2Content content, string id) => content.Statuses.FirstOrDefault(s => s.Id == id)?.Name ?? Word(id);
        private static string OriginName(Core2Content content, JToken origin)
        {
            var id = (string)origin?["id"];
            if (id == null) return null;
            var kind = (string)origin["kind"];
            if (kind == "status") return StatusName(content, id);
            IReadOnlyList<Core2Row> table = kind == "relic" ? content.Relics : kind == "flask" ? content.Flasks : kind == "event" ? content.Events : null;
            return table?.FirstOrDefault(row => row.Id == id)?.Name ?? Word(id);
        }

        /// <summary>
        /// JS combatLogEntries: one entry per logged event (two for an avoidance with two rolls), each naming who or what
        /// caused it and who it landed on. <paramref name="enemies"/> are the fight's combatants (names come from their enemy
        /// rows); the player is "Forsaken" unless named.
        /// </summary>
        public static List<Core2LogEntry> Entries(Core2Content content, IReadOnlyList<Core2FightEvent> events, IReadOnlyList<Core2Combatant> enemies, string playerName = PlayerName)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = playerName ?? PlayerName };
            var enemyIdOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var enemy in enemies ?? Array.Empty<Core2Combatant>())
            {
                names[enemy.Id] = content.Enemies.FirstOrDefault(e => e.Id == enemy.EnemyId)?.Name ?? "Enemy";
                enemyIdOf[enemy.Id] = enemy.EnemyId;
            }
            string PlainName(string id) => id != null && names.TryGetValue(id, out var name) ? name : id == "player" ? PlayerName : "Enemy";
            var playerIds = new HashSet<string>(new[] { "player" }, StringComparer.Ordinal);
            var references = new List<Core2LogSegment>();
            string Name(string id)
            {
                var side = id != null && playerIds.Contains(id) ? "player" : id != null && enemyIdOf.ContainsKey(id) ? "enemy" : null;
                references.Add(new Core2LogSegment { ActorId = id, Text = PlainName(id), Side = side });
                return "\u0001" + (references.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0002";
            }
            string MoveName(string actorId, string moveId, string enemyId = null)
            {
                var def = actorId != null && enemyIdOf.TryGetValue(actorId, out var id) ? content.Enemies.FirstOrDefault(e => e.Id == id) : null;
                def ??= enemyId == null ? null : content.Enemies.FirstOrDefault(e => e.Id == enemyId);
                return (string)def?.Move(moveId)?.Raw["name"] ?? Word(moveId);
            }
            (string ActorKey, string Label)? playing = null, moving = null;
            var rows = new List<Core2LogEntry>();
            var round = 1;
            for (var index = 0; index < (events?.Count ?? 0); index++)
            {
                var ev = events[index];
                var data = ev.Data ?? new JObject();
                string S(string key) => data[key]?.Type == JTokenType.String ? (string)data[key] : null;
                int? I(string key) => data[key] != null && (data[key].Type == JTokenType.Integer || data[key].Type == JTokenType.Float) ? (int?)(int)Math.Floor((double)data[key]) : null;
                double? D(string key) => data[key] != null && (data[key].Type == JTokenType.Integer || data[key].Type == JTokenType.Float) ? (double?)data[key] : null;
                bool B(string key) => data[key]?.Type == JTokenType.Boolean && (bool)data[key];
                if (ev.Type == "playerTurnStart" && I("turn") is int turnNo) round = Math.Max(round, turnNo);
                var sourceKey = S("sourcePlayerId") ?? S("sourceId") ?? S("playerId");
                var targetKey = S("targetPlayerId") ?? (S("targetId") == "player" ? S("playerId") ?? S("sourcePlayerId") ?? "player" : S("targetId"));
                var source = Name(sourceKey);
                var target = Name(targetKey);

                (string ActorKey, string Label, bool Preparing) CauseOf()
                {
                    if (B("preparing")) return (sourceKey, null, true);
                    var origin = data["triggeredBy"] as JObject;
                    var card = CardName(content, S("cardId"));
                    if (card != null) return (sourceKey, card, false);
                    if (S("moveId") != null) return (sourceKey, MoveName(S("sourceId"), S("moveId")), false);
                    if ((string)origin?["id"] != null)
                    {
                        var owner = (string)origin["ownerId"];
                        return (owner == S("targetId") ? targetKey : owner == S("sourceId") ? sourceKey : owner, OriginName(content, origin), false);
                    }
                    if (S("statusId") != null) return (null, StatusName(content, S("statusId")) + " flux", false);
                    var said = S("source") ?? S("reason");
                    if (said != null && SourceWords.TryGetValue(said, out var words)) return (sourceKey, words, false);
                    foreach (var act in new[] { moving, playing })
                        if (act?.Label != null && (sourceKey == null || sourceKey == act.Value.ActorKey)) return (act.Value.ActorKey, act.Value.Label, false);
                    return (sourceKey, null, false);
                }
                string CauseText(string self = null)
                {
                    self ??= targetKey;
                    var (actorKey, label, preparing) = CauseOf();
                    var actor = actorKey != null ? Name(actorKey) : null;
                    if (preparing) return actorKey != null && actorKey != self ? actor + " preparing" : "preparing";
                    if (label != null) return actorKey != null && actorKey != self ? actor + "'s " + label : label;
                    return actorKey != null && actorKey != self ? "from " + actor : null;
                }
                string Paren(string text) => text != null ? " (" + text + ")" : "";
                string AttackText() => source + "'s " + (CauseOf().Label ?? "attack");
                string StripFrom(string text) => text.StartsWith("from ", StringComparison.Ordinal) ? text.Substring(5) : text;

                var lines = new List<string>();
                switch (ev.Type)
                {
                    case "cardPreparing":
                        playing = (sourceKey, CardName(content, S("cardId")));
                        break;
                    case "cardResolved": playing = null; break;
                    case "enemyActorTurnStarted": case "enemyTurnEnd": moving = null; break;
                    case "playerTurnStart": case "playerTurnEnd": case "enemyTurnStart": moving = playing = null; break;
                    case "cardPlayed":
                    {
                        var card = CardName(content, S("cardId")) ?? "a card";
                        playing = (sourceKey, card == "a card" ? null : card);
                        var costs = new List<string> { (I("staminaSpent") ?? I("energySpent") ?? 0) + " SP" };
                        if ((I("manaSpent") ?? 0) != 0) costs.Add(I("manaSpent") + " MP");
                        var on = S("targetId") != null && targetKey != sourceKey ? " on " + target : "";
                        lines.Add(source + " played " + card + ((I("upcastTier") ?? 0) != 0 ? " (Upcast " + I("upcastTier") + ")" : "") + on + " · " + string.Join(", ", costs) + ".");
                        break;
                    }
                    case "enemyMoveStarted":
                    {
                        var move = MoveName(S("sourceId"), S("moveId"), S("enemyId"));
                        moving = (sourceKey, move);
                        lines.Add(B("reaction") ? source + " reacts with " + move + "." : source + " used " + move + ".");
                        break;
                    }
                    case "combatCounterTriggered":
                    {
                        var label = CauseOf().Label;
                        lines.Add(source + " counterattacked " + target + (label != null ? " with " + label : "") + ".");
                        break;
                    }
                    case "attackHit": case "attackMissed":
                    {
                        var outcome = ev.Type == "attackHit" ? "hit" : "missed";
                        var dice = data["dice"] is JArray diceRow && diceRow.Count > 0 ? string.Join(", ", diceRow.Select(d => d.ToString())) : data["d20"] != null && data["d20"].Type != JTokenType.Null ? data["d20"].ToString() : "automatic";
                        var accuracy = I("accuracy") ?? 0; var evasion = I("evasion") ?? 0; var chance = data["hitChance"] != null && data["hitChance"].Type != JTokenType.Null ? Num(D("hitChance").Value) : "100";
                        var mode = S("mode") ?? "normal";
                        if (data["evasionDie"] != null && data["evasionDie"].Type != JTokenType.Null)
                            lines.Add(source + " " + outcome + " " + target + ": d20 [" + dice + "] + Precision " + accuracy + " = " + data["attackTotal"] + " vs d20 [" + data["evasionDie"] + "] + Evasion " + evasion + " = " + data["evasionTotal"] + " · " + mode + " · " + chance + "% chance.");
                        else if (B("fixedDefense") && data["dice"] is JArray fixedDice && fixedDice.Count > 0)
                            lines.Add(source + " " + outcome + " " + target + ": d20 [" + dice + "] + Precision " + accuracy + " = " + data["attackTotal"] + " vs Evasion " + evasion + " · " + mode + " · " + chance + "% chance.");
                        else if (B("opposed") || B("fixedDefense"))
                        {
                            var why = S("missReason") == "range" ? "out of range" : B("bypass") ? "Evasion bypassed" : "automatic";
                            lines.Add(source + " " + outcome + " " + target + ": " + why + " · Precision " + accuracy + " · " + chance + "% chance.");
                        }
                        else lines.Add(source + " " + outcome + " " + target + ": d20 [" + dice + "] + Precision " + accuracy + " vs Evasion " + evasion + " · " + mode + " · stance ×" + (data["stanceFactor"]?.ToString() ?? "1") + " · " + chance + "% chance" + (S("missReason") == "range" ? " (out of range)" : "") + ".");
                        break;
                    }
                    case "damageDealt":
                    {
                        var hit = AttackText() + " hit " + target;
                        if (B("silencedImmune")) { lines.Add(hit + ": " + target + " is immune to Force (Silenced)."); break; }
                        var barrier = I("barrierAbsorbed") ?? 0; var ward = I("wardAbsorbed") ?? 0; var poise = I("poiseAbsorbed") ?? 0; var poiseDamage = I("poiseDamage") ?? 0;
                        var blocked = I("blocked") ?? 0; var amount = I("amount") ?? 0;
                        var hp = Math.Max(0, amount - blocked);
                        var layered = amount + barrier + ward + poise;
                        var incoming = I("incoming") ?? layered + Math.Max(0, (I("resisted") ?? 0) - barrier - ward);
                        var typed = incoming - layered;
                        var baseAmount = I("base") ?? incoming;
                        var mod = incoming - baseAmount;
                        var kind = S("damageType") != null ? Word(S("damageType")) + " " : "";
                        var parts = new List<string>();
                        if (mod != 0) parts.Add("modifiers " + (mod > 0 ? "+" : "−") + Math.Abs(mod) + " (" + baseAmount + " → " + incoming + ")");
                        if (typed > 0) parts.Add(kind + "resisted " + typed + " (" + incoming + " → " + layered + ")");
                        if (typed < 0) parts.Add(kind + "weakness +" + (-typed) + " (" + incoming + " → " + layered + ")");
                        if (barrier != 0) parts.Add("Barrier absorbed " + barrier + " (" + ((I("barrierRemaining") ?? 0) + barrier) + " → " + (I("barrierRemaining") ?? 0) + ")");
                        if (ward != 0) parts.Add("Ward absorbed " + ward + " (" + ((I("wardRemaining") ?? 0) + ward) + " → " + (I("wardRemaining") ?? 0) + ")");
                        if (blocked != 0) parts.Add("Block absorbed " + blocked + " (" + ((I("blockRemaining") ?? 0) + blocked) + " → " + (I("blockRemaining") ?? 0) + ")");
                        if (poise != 0) parts.Add("Poise absorbed " + poise + " (" + ((I("poiseRemaining") ?? 0) + poise) + " → " + (I("poiseRemaining") ?? 0) + ")");
                        if (poiseDamage != 0) parts.Add("Poise damage " + poiseDamage + " (" + ((I("poiseRemaining") ?? 0) + poiseDamage) + " → " + (I("poiseRemaining") ?? 0) + ")");
                        if ((I("forceImmune") ?? 0) > 0) parts.Add(I("forceImmune") + " Force IMMUNE (Silenced)");
                        lines.Add(hit + " for " + baseAmount + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "") + "; " + hp + " HP dealt.");
                        break;
                    }
                    case "barrierBroken": lines.Add(target + "'s Barrier broke" + Paren(CauseText()) + "."); break;
                    case "wardBroken": case "poiseBroken":
                    {
                        var pool = ev.Type == "wardBroken" ? "Ward" : "Poise";
                        string toll = data["poiseBreak"]?.Type == JTokenType.Boolean ? ((bool)data["poiseBreak"] ? "staggered" : null)
                            : B("interrupted") ? "interrupted"
                            : D("staminaLoss") is double loss ? (loss != 0 ? "−" + Num(loss) + " Stamina next turn" : null)
                            : pool == "Poise" ? "staggered" : null;
                        var by = CauseText();
                        var bits = new[] { by != null ? "by " + StripFrom(by) : null, toll }.Where(b => b != null).ToList();
                        lines.Add(target + "'s " + pool + " broke" + Paren(bits.Count > 0 ? string.Join("; ", bits) : null) + ".");
                        break;
                    }
                    case "persistentWardChanged":
                        if ((I("restored") ?? 0) > 0)
                        {
                            var by = CauseText();
                            lines.Add(target + " restored " + I("restored") + " Ward (" + ((I("value") ?? 0) - I("restored")) + " → " + I("value") + ")" + (by != null ? " from " + StripFrom(by) : "") + ".");
                        }
                        break;
                    case "barrierGained": lines.Add(target + " gained " + I("amount") + " Barrier" + Paren(CauseText()) + "."); break;
                    case "ratingImpact":
                    {
                        if (!((I("amount") ?? 0) > 0)) break;
                        var meter = S("meter") == "ward" ? "disruption" : "stagger";
                        var cause = S("cause") ?? "";
                        var split = cause.Split(':');
                        var by = CauseText();
                        var what = ImpactWords.TryGetValue(cause, out var word) ? word : split[0] == "rider" && split.Length > 1 ? Word(split[1]) : null;
                        var from = what != null ? " from " + what + Paren(by) : by != null ? " from " + StripFrom(by) : "";
                        lines.Add(target + "'s " + meter + " meter took " + I("amount") + from + " (" + I("value") + "/" + I("max") + ((I("breaks") ?? 0) > 0 ? ", broke" : "") + ").");
                        break;
                    }
                    case "blockGained": lines.Add(target + " gained " + I("amount") + " Block" + Paren(CauseText()) + "."); break;
                    case "healed": lines.Add(target + " recovered " + I("amount") + " HP" + Paren(CauseText()) + "."); break;
                    case "hpLost":
                    {
                        if (S("cause") == "attack") break;
                        var why = CauseText() ?? (S("cause") != null && S("cause") != "effect" ? Word(S("cause")) : null);
                        lines.Add(target + " lost " + I("amount") + " HP" + Paren(why) + ".");
                        break;
                    }
                    case "statusApplied":
                    {
                        var stacks = (I("stacks") ?? 0) > 1 ? I("stacks") + " " : "";
                        lines.Add(target + " gained " + stacks + StatusName(content, S("status")) + Paren(CauseText()) + ".");
                        break;
                    }
                    case "statusExpired": lines.Add(target + "'s " + StatusName(content, S("status")) + " expired."); break;
                    case "enemyDied":
                    {
                        var by = CauseText();
                        lines.Add(target + " was defeated" + (by != null ? " by " + StripFrom(by) : "") + ".");
                        break;
                    }
                    case "playerDowned": lines.Add(Name(S("playerId") ?? "player") + " was downed" + (S("reason") != null ? " (" + Word(S("reason")) + ")" : "") + "."); break;
                    case "combatReactionChosen": if (S("cardInstanceId") == null) lines.Add(Name(S("ownerId")) + " skipped the reaction."); break;
                    case "combatRangeReturnUnavailable": lines.Add(RangeReturnUnavailableText(data)); break;
                    case "combatRangeActionUnavailable": lines.Add(RangeActionUnavailableText(content, data)); break;
                    case "combatReactionUnavailable": lines.Add(ReactionUnavailableText(content, data, Name(S("sourceId")), Name(S("ownerId")))); break;
                    case "combatGuardBroken":
                    {
                        var who = S("sourceId") != null || S("sourcePlayerId") != null ? AttackText() + " broke " : "Broke ";
                        lines.Add(who + (S("targetId") != null || S("targetPlayerId") != null ? target + "'s" : "the") + " Guard!" + ((I("poise") ?? 0) > 0 ? " (+" + I("poise") + " Poise pressure)" : ""));
                        break;
                    }
                    case "impactDealt":
                        if ((I("poiseDamage") ?? 0) != 0 || (I("wardDamage") ?? 0) != 0) lines.Add(AttackText() + " dealt " + (I("poiseDamage") ?? 0) + " Poise and " + (I("wardDamage") ?? 0) + " Ward pressure to " + target + ".");
                        break;
                }
                foreach (var line in lines)
                {
                    var segments = new List<Core2LogSegment>();
                    var cursor = 0;
                    foreach (Match match in Regex.Matches(line, "\u0001(\\d+)\u0002"))
                    {
                        if (match.Index > cursor) segments.Add(new Core2LogSegment { Text = line.Substring(cursor, match.Index - cursor) });
                        var reference = references[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)];
                        segments.Add(new Core2LogSegment { Text = reference.Text, ActorId = reference.ActorId, Side = reference.Side });
                        cursor = match.Index + match.Length;
                    }
                    if (cursor < line.Length) segments.Add(new Core2LogSegment { Text = line.Substring(cursor) });
                    rows.Add(new Core2LogEntry
                    {
                        EventIndex = index, Round = round, Text = string.Concat(segments.Select(s => s.Text)), Segments = segments,
                        DamageReceipt = data["damageReceipt"] as JObject,
                    });
                }
            }
            return rows;
        }

        /// <summary>JS Number → String for a finite value (whole numbers without a decimal point).</summary>
        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string RangeReturnUnavailableText(JObject data)
        {
            var reason = (string)data["reason"] == "stamina" ? "not enough SP to Advance (need " + JsText(data["required"]) + ", have " + JsText(data["available"]) + ")" : "out of range";
            return "Counter return skipped: " + reason + ". Defense kept.";
        }

        private static string RangeActionUnavailableText(Core2Content content, JObject data) => (string)data["reason"] == "stamina"
            ? Fill(UiString(content, "combat.range.actionSkippedSp"), ("required", JsText(data["required"])), ("available", JsText(data["available"])))
            : UiString(content, "combat.range.actionSkippedRange");

        /// <summary>JS template interpolation of a value: undefined and null print as words.</summary>
        private static string JsText(JToken value) => value == null ? "undefined" : value.Type == JTokenType.Null ? "null" : value.ToString();

        /// <summary>JS reactionUnavailableText: why a held reaction card was not offered.</summary>
        public static string ReactionUnavailableText(Core2Content content, JObject data, string source, string owner)
        {
            var cardId = (string)data["cardId"] ?? (string)(data["cardIds"] as JArray)?.FirstOrDefault();
            var card = cardId != null && content.HasCard(cardId) ? content.Card(cardId) : null;
            var verb = card != null && card.Tags.Contains("maneuver:sweep") ? "Sweep" : "Counter";
            switch ((string)data["reason"])
            {
                case "notCovered": return "No reaction: Counter can't answer " + (AttackKindWords.TryGetValue((string)data["attackKind"] ?? "", out var kind) ? kind : "this attack") + ".";
                case "cost":
                    return (string)data["resource"] == "hp"
                        ? "Not enough HP to " + (card?.Name ?? "use that card") + " (need " + JsText(data["need"]) + ", have " + JsText(data["have"]) + ")."
                        : "Not enough " + (ResourceWords.TryGetValue((string)data["resource"] ?? "", out var word) ? word : "SP") + " to " + verb + " (need " + JsText(data["need"]) + ", have " + JsText(data["have"]) + ").";
                case "switchOff": return "No reaction: Reactions are off.";
                case "attackerStaggered": return "No reaction: " + source + " is staggered.";
                case "attackerControlled": return "No reaction: " + source + " can't act.";
                case "controlled": return "No reaction: " + owner + " must recover from control first.";
                case "unplayable": return "No reaction: " + (card?.Name ?? "that card") + " can't be played.";
                case "alreadyAnswered": return "One reaction per attack: the rest of this attack can't be answered.";
                default: return "No reaction available.";
            }
        }

        // ---- the drawn log (JS renderCombatLogEntries, damageReceiptLines) ---------------------------------------------

        /// <summary>
        /// JS renderCombatLogEntries: the role legend (when <paramref name="legend"/>), a "Round N" heading whenever the round
        /// changes, one row per entry and — in full detail — its damage receipt's lines; the empty note when there is nothing.
        /// The drawer draws compact without the legend; the full log draws the legend and starts in full detail.
        /// </summary>
        public static List<Core2LogViewRow> View(Core2Content content, IReadOnlyList<Core2LogEntry> entries, bool detailed, bool legend)
        {
            var rows = new List<Core2LogViewRow>();
            if (legend) rows.Add(new Core2LogViewRow { Kind = "legend", Text = UiString(content, "combat.log.role.player") + " | " + UiString(content, "combat.log.role.enemy") });
            int? round = null;
            foreach (var entry in entries ?? Array.Empty<Core2LogEntry>())
            {
                if (entry.Round != round)
                {
                    round = entry.Round;
                    rows.Add(new Core2LogViewRow { Kind = "heading", Text = Fill(UiString(content, "combat.log.round"), ("round", entry.Round)) });
                }
                rows.Add(new Core2LogViewRow { Kind = "entry", Text = entry.Text, Entry = entry });
                if (detailed) foreach (var line in DamageReceiptLines(content, entry.DamageReceipt)) rows.Add(new Core2LogViewRow { Kind = "detail", Text = line, Entry = entry });
            }
            if (entries == null || entries.Count == 0) rows.Add(new Core2LogViewRow { Kind = "empty", Text = UiString(content, "combat.log.empty") });
            return rows;
        }

        /// <summary>The full log's detail toggle label (JS: "Compact log" while detailed, else "Full detail").</summary>
        public static string DetailToggleLabel(Core2Content content, bool detailed) => (string)Ui(content)["combatLog"]["detailToggle"][detailed ? "compact" : "full"];

        /// <summary>JS damageReceiptLines number(): up to four decimals, no trailing zeros; "?" for a missing value.</summary>
        private static string ReceiptNumber(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return "?";
            return Math.Round((double)value, 4, MidpointRounding.AwayFromZero).ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool Finite(JToken value) => value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float);

        /// <summary>JS damageReceiptLines: per hit, incoming → HP, its parts, each source's operation, the reductions and the final HP; then the total.</summary>
        public static List<string> DamageReceiptLines(Core2Content content, JObject receipt)
        {
            var lines = new List<string>();
            if (!(receipt?["hits"] is JArray hits)) return lines;
            var index = 0;
            foreach (var hit in hits.OfType<JObject>())
            {
                lines.Add(UiString(content, "combat.receipt.hitLabel") + " " + (index + 1) + ": " + ReceiptNumber(hit["incoming"]) + " incoming → " + ReceiptNumber(hit["hpLoss"]) + " HP damage");
                if (Finite(hit["physical"]) && Finite(hit["magical"])) lines.Add("Components: " + ReceiptNumber(hit["physical"]) + " physical + " + ReceiptNumber(hit["magical"]) + " magical");
                foreach (var row in (hit["sources"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var amount = row["amount"];
                    var operation = (string)row["operation"] == "multiply" || Finite(row["multiplier"])
                        ? "×" + ReceiptNumber(row["multiplier"] ?? amount)
                        : (Finite(amount) && (double)amount >= 0 ? "+" : "−") + ReceiptNumber(Finite(amount) ? (JToken)Math.Abs((double)amount) : null);
                    lines.Add(((string)row["label"] ?? (string)row["id"]) + ": " + operation + (row["stage"] != null ? " (" + row["stage"] + ")" : ""));
                }
                var reductions = new List<string>();
                if (Finite(hit["wardReduced"]) && (double)hit["wardReduced"] > 0) reductions.Add("Ward −" + ReceiptNumber(hit["wardReduced"]));
                if (Finite(hit["poiseReduced"]) && (double)hit["poiseReduced"] > 0) reductions.Add(Fill(UiString(content, "combat.receipt.poiseAbsorbed"), ("n", ReceiptNumber(hit["poiseReduced"]))));
                if (Finite(hit["blocked"]) && (double)hit["blocked"] > 0) reductions.Add("Block −" + ReceiptNumber(hit["blocked"]));
                if (Finite(hit["poiseDamage"]) && (double)hit["poiseDamage"] > 0) reductions.Add(Fill(UiString(content, "combat.receipt.poiseDamage"), ("n", ReceiptNumber(hit["poiseDamage"]))));
                if (reductions.Count > 0) lines.Add(string.Join("; ", reductions));
                lines.Add("Final HP damage: " + ReceiptNumber(hit["hpLoss"]) + (hit["rounding"] != null && hit["rounding"].Type != JTokenType.Null && hit["rounding"].ToString() != "0" && hit["rounding"].ToString() != "" ? " (" + hit["rounding"] + ")" : ""));
                index++;
            }
            lines.Add(hits.Count + " hit" + (hits.Count == 1 ? "" : "s") + " · " + ReceiptNumber(receipt["totalDamage"]) + " total HP damage");
            return lines;
        }

        // ---- the drawer's sizes and palette (JS CombatToolsModel, CombatLogPaletteModel) --------------------------------

        public static IReadOnlyList<string> Sizes(Core2Content content) => Core2Json.Strings(Ui(content)["combatLog"]["sizes"]);
        public static string DefaultSize(Core2Content content) => (string)Ui(content)["combatLog"]["defaultSize"];

        /// <summary>
        /// JS combatLogHeight: Small is about half a card, Medium half the viewport, Large all the space between the menu and
        /// the dock; on a short screen the three stops are spread within the space available so they stay distinct.
        /// </summary>
        public static double Height(string size, double cardHeight, double viewportHeight, double dockTop, double menuBottom, double gap = 8)
        {
            var available = Math.Max(0, dockTop - menuBottom - gap);
            var small = Math.Min(available, cardHeight / 2);
            var medium = Math.Min(available, viewportHeight / 2);
            if (medium >= available || medium <= small)
            {
                small = Math.Min(small, available / 3);
                medium = (small + available) / 2;
            }
            var wanted = size == "Large" ? available : size == "Medium" ? medium : small;
            return Math.Max(0, Math.Min(available, wanted));
        }

        /// <summary>JS combatLogSnapSize: the stop nearest a dragged height (Small first on a tie).</summary>
        public static string SnapSize(double height, IReadOnlyDictionary<string, double> heights, IReadOnlyList<string> sizes)
        {
            var nearest = "Small";
            foreach (var size in sizes) if (Math.Abs(heights[size] - height) < Math.Abs(heights[nearest] - height)) nearest = size;
            return nearest;
        }

        /// <summary>JS combatLogDragScale: how far the log grows per pixel the button is dragged.</summary>
        public static double DragScale(IReadOnlyDictionary<string, double> heights, double viewportHeight, double handleBottom)
        {
            var travel = Math.Max(24, Math.Min(120, viewportHeight - handleBottom - 8));
            return Math.Max(1, (heights["Large"] - heights["Small"]) / travel);
        }

        /// <summary>JS combatLogPalette: the player and enemy name colors, each the default unless the chosen one is readable.</summary>
        public static (string Player, string Enemy) Palette(Core2Content content, string player, string enemy)
        {
            var log = Ui(content)["combatLog"];
            var background = (string)log["paletteBackground"];
            var minimum = (double)log["minimumContrast"];
            return (HasContrast(player, background, minimum) ? player : (string)log["palette"]["player"], HasContrast(enemy, background, minimum) ? enemy : (string)log["palette"]["enemy"]);
        }

        private static readonly Regex Hex = new Regex("^#[0-9a-fA-F]{6}$");

        /// <summary>JS hasCombatLogColorContrast: WCAG contrast of a #rrggbb color against the drawer's background.</summary>
        public static bool HasContrast(string color, string background, double minimum)
        {
            if (color == null || background == null || !Hex.IsMatch(color) || !Hex.IsMatch(background)) return false;
            double Channel(string hex, int offset)
            {
                var c = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            double Luminance(string hex) => 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
            var a = Luminance(color); var b = Luminance(background);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05) >= minimum;
        }
    }
}
