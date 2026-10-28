// Core2CombatBeats.cs — how a dispatch's events play back: grouped into beats (one per card play, enemy move, Counter
// return, enemy reaction, turn banner or draw), each beat run through the JS D11 stage order with one pause at every
// boundary, and the words and numbers each stage shows (callouts over a fighter, floaters rising from it). Plain C#: the
// combat controller steps the plan by the clock; the console suite asserts the order, the text and the times.
//
// JS at the pin:
//   src/ui/fx.js             groupBeats (beats, lead events, banners YOUR TURN / ENEMY TURN, draw beats), playTimeline
//                            (onAction word, onResult Hit / Missed / Out of range, onNumbers Dodged / Hit — Evade Failed and
//                            each event's numbers, reactionGroups split at each further hit), guardHitFloatParts (the
//                            hit's Barrier, Ward, "Poise absorbed N", Block and "-N" parts), dmgClass (size tiers),
//                            CALLOUTS (the callout table and tones)
//   src/ui/actionTimeline.js runActionTimeline (intent → action → sprite → contact → result → reaction → damage → hud →
//                            resolution, then max(reset, step) before the next beat; a banner holds bannerMs)
//   src/ui/animationPace.js  actionPacing (gap at every boundary; step, banner and sprite speeds), COMBAT_TIMING
//                            (windupFraction: the impact lands at 55% of the lunge)
// Not ported: hit-stop holds (JS hitStopMsFor) and authored sprite clips; the sprite stage lasts the pace's lunge.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2CombatBeat
    {
        public string ActorId { get; set; }
        /// <summary>attack, act, counter, reveal, banner, draw, or null for a loose run of events.</summary>
        public string Kind { get; set; }
        /// <summary>YOUR TURN or ENEMY TURN on a turn boundary.</summary>
        public string Banner { get; set; }
        public bool Prepared { get; set; }
        /// <summary>A prepared contact's telegraph (JS combatContactPrepared): the action word and pose, no result yet.</summary>
        public bool Telegraph { get; set; }
        /// <summary>A Counter's return (JS combatCounterTriggered beat).</summary>
        public bool CounterReturn { get; set; }
        /// <summary>An enemy reaction's move (enemyMoveStarted with reaction: true) or its push onto the stack.</summary>
        public bool EnemyReaction { get; set; }
        /// <summary>The word the actor shows as it moves (the card or move name; Counter; Flask).</summary>
        public string ActionWord { get; set; }
        public List<Core2FightEvent> Events { get; set; } = new List<Core2FightEvent>();
        public int Lead { get; set; }
    }

    public sealed class Core2Floater
    {
        public string TargetId { get; set; }
        public string Text { get; set; }
        /// <summary>dmg (with a size tier: small, heavy, crit, pct20, pct35), blk small, poise small, ward small, barrier small, heal, buff, debuff.</summary>
        public string Class { get; set; }
    }

    public sealed class Core2Callout
    {
        public string TargetId { get; set; }
        public string Kind { get; set; }
        public string Text { get; set; }
        /// <summary>white, gold, red, block, barrier, ward (JS CALLOUTS tone).</summary>
        public string Tone { get; set; }
    }

    /// <summary>One stage of one beat at its time from the start of the playback.</summary>
    public sealed class Core2TimelineStep
    {
        public double AtMs { get; set; }
        /// <summary>intent, action, sprite, contact, result, reaction, damage, hud, resolution.</summary>
        public string Stage { get; set; }
        public int Beat { get; set; }
        /// <summary>The events of this stage's reaction group (all of the beat's events for a beat without groups).</summary>
        public List<Core2FightEvent> Events { get; set; } = new List<Core2FightEvent>();
        public List<Core2Callout> Callouts { get; set; } = new List<Core2Callout>();
        public List<Core2Floater> Floaters { get; set; } = new List<Core2Floater>();
    }

    public sealed class Core2Timeline
    {
        public List<Core2CombatBeat> Beats { get; set; } = new List<Core2CombatBeat>();
        public List<Core2TimelineStep> Steps { get; set; } = new List<Core2TimelineStep>();
        public double DurationMs { get; set; }
    }

    /// <summary>The waits one playback uses (JS actionPacing), from Core2CombatPacing.Pace.</summary>
    public sealed class Core2BeatTiming
    {
        public double GapMs { get; set; }
        public double StepMs { get; set; }
        public double BannerMs { get; set; }
        public double LungeMs { get; set; }
        public double WindupFraction { get; set; } = 0.55;

        public static Core2BeatTiming From(Core2ActionPace pace, double windupFraction = 0.55) => new Core2BeatTiming
        {
            GapMs = pace.GapMs, StepMs = pace.StepMs, BannerMs = pace.BannerMs, WindupFraction = windupFraction,
            LungeMs = pace.Scale == 0 ? 0 : pace.SpriteSpeed.TryGetValue("lungeMs", out var lunge) ? lunge : 0,
        };
    }

    public static class Core2CombatBeats
    {
        public const string BannerPlayer = "YOUR TURN";
        public const string BannerEnemy = "ENEMY TURN";
        private const double HugeHitShare = 0.35, BigHitShare = 0.2;

        private static bool IsLoose(Core2CombatBeat beat) => beat.ActorId == null && beat.Banner == null && beat.Kind == null && beat.Events.Count > 0;

        /// <summary>JS groupBeats, with the Counter-return and enemy-reaction beats marked and each actor's word named.</summary>
        public static List<Core2CombatBeat> Group(IReadOnlyList<Core2FightEvent> events, Core2Content content)
        {
            var beats = new List<Core2CombatBeat>();
            var cur = new Core2CombatBeat();
            void Push() { if (cur.Events.Count > 0 || cur.Banner != null || cur.ActorId != null) beats.Add(cur); }
            void StartActor(string actorId, string kind, Core2FightEvent e)
            {
                var lead = IsLoose(cur) ? cur.Events : new List<Core2FightEvent>();
                if (lead.Count == 0) Push();
                cur = new Core2CombatBeat { ActorId = actorId, Kind = kind, Lead = lead.Count };
                cur.Events.AddRange(lead);
                cur.Events.Add(e);
            }
            foreach (var e in events)
            {
                var d = e.Data;
                switch (e.Type)
                {
                    case "cardPlayed":
                        StartActor((string)d["sourceId"] ?? "player", (string)d["cardType"] == "attack" ? "attack" : "act", e);
                        cur.ActionWord = content.HasCard((string)d["cardId"]) ? content.Card((string)d["cardId"]).Name : (string)d["cardId"];
                        break;
                    case "flaskUsed":
                        StartActor("player", "act", e);
                        cur.ActionWord = content.Flasks.FirstOrDefault(f => f.Id == (string)d["flaskId"])?.Name ?? "Flask";
                        break;
                    case "enemyMoveStarted":
                    case "combatHitStarted":
                        StartActor((string)d["sourceId"], (string)d["kind"] == "attack" ? "attack" : "act", e);
                        cur.Prepared = (bool?)d["contactPrepared"] == true;
                        cur.EnemyReaction = (bool?)d["reaction"] == true;
                        cur.ActionWord = MoveName(content, (string)d["enemyId"], (string)d["moveId"]);
                        break;
                    case "combatContactPrepared":
                        StartActor((string)d["sourceId"], "attack", e);
                        cur.Telegraph = true;
                        cur.ActionWord = MoveName(content, (string)d["enemyId"], (string)d["moveId"]);
                        break;
                    case "combatEnemyReactionStacked":
                        // JS keeps the push inside the beat that provoked it; the screen names it there (Numbers: the
                        // enemy's reaction word), and the move itself plays later as its own enemyMoveStarted beat.
                        cur.Events.Add(e);
                        cur.EnemyReaction = true;
                        break;
                    case "enemyActorTurnStarted":
                        StartActor((string)d["sourceId"], "reveal", e);
                        break;
                    case "combatCounterTriggered":
                        StartActor((string)d["sourceId"], "counter", e);
                        cur.Prepared = (bool?)d["contactPrepared"] == true;
                        cur.CounterReturn = true;
                        cur.ActionWord = Core2CombatText.T("combat.callout.countered");
                        break;
                    case "enemyTurnStart":
                        Push();
                        cur = new Core2CombatBeat { Banner = BannerEnemy, Kind = "banner" };
                        cur.Events.Add(e);
                        break;
                    case "playerTurnStart":
                        Push();
                        cur = new Core2CombatBeat { Banner = BannerPlayer, Kind = "banner" };
                        cur.Events.Add(e);
                        break;
                    case "cardDrawn":
                        if (cur.ActorId != null && cur.ActorId != "player" && cur.Kind != "draw")
                        {
                            Push();
                            cur = new Core2CombatBeat { Kind = "draw" };
                        }
                        cur.Events.Add(e);
                        break;
                    default:
                        cur.Events.Add(e);
                        break;
                }
            }
            Push();
            return beats;
        }

        private static string MoveName(Core2Content content, string enemyId, string moveId)
        {
            if (moveId == null) return null;
            var def = enemyId == null ? null : content.Enemies.FirstOrDefault(e => e.Id == enemyId);
            return (string)def?.Move(moveId)?.Raw["name"] ?? Core2CombatText.Words(moveId);
        }

        /// <summary>JS reactionGroups: a multi-hit beat splits before each further accuracy or damage receipt.</summary>
        public static List<List<Core2FightEvent>> ReactionGroups(Core2CombatBeat beat)
        {
            var groups = new List<List<Core2FightEvent>>();
            var group = new List<Core2FightEvent>();
            var hadHit = false;
            foreach (var e in beat.Events)
            {
                if (hadHit && (e.Type == "attackHit" || e.Type == "attackMissed" || e.Type == "combatAvoidanceResolved" || e.Type == "damageDealt"))
                {
                    groups.Add(group); group = new List<Core2FightEvent>(); hadHit = false;
                }
                group.Add(e);
                if (e.Type == "damageDealt") hadHit = true;
            }
            if (group.Count > 0) groups.Add(group);
            return groups;
        }

        private static Core2Callout Callout(string targetId, string kind)
        {
            var (id, tone) = kind switch
            {
                "hit" => ("combat.callout.hit", "white"),
                "evadeFailed" => ("combat.callout.evadeFailed", "white"),
                "missed" => ("combat.callout.missed", "white"),
                "outOfRange" => ("combat.callout.outOfRange", "white"),
                "dodged" => ("combat.callout.dodged", "white"),
                "staggered" => ("combat.callout.staggered", "gold"),
                "countered" => ("combat.callout.countered", "red"),
                "poiseBreak" => ("combat.callout.poiseBreak", "gold"),
                "blockBreak" => ("combat.callout.blockBreak", "block"),
                "interrupted" => ("combat.callout.interrupted", "interrupt"),
                _ => throw new ArgumentException("Unknown callout " + kind),
            };
            return new Core2Callout { TargetId = targetId, Kind = kind, Text = Core2CombatText.T(id), Tone = tone };
        }

        /// <summary>JS onResult: the accuracy word once per target — Hit, or Missed (and Out of range for a range miss).</summary>
        public static List<Core2Callout> ResultCallouts(IEnumerable<Core2FightEvent> group)
        {
            var said = new HashSet<string>(StringComparer.Ordinal);
            var callouts = new List<Core2Callout>();
            var events = group.ToList();
            foreach (var e in events)
            {
                var kind = e.Type == "attackHit" ? "hit" : e.Type == "attackMissed" || e.Type == "attackEvaded" ? "missed" : null;
                var target = (string)e.Data["targetId"];
                if (kind == null || target == null || said.Contains(target)) continue;
                callouts.Add(Callout(target, kind));
                said.Add(target);
                if (kind == "missed" && ((string)e.Data["missReason"] == "range" || (string)e.Data["reason"] == "range")) callouts.Add(Callout(target, "outOfRange"));
            }
            foreach (var e in events)
            {
                var target = (string)e.Data["targetId"];
                if (e.Type == "damageDealt" && target != null && !said.Contains(target)) { callouts.Add(Callout(target, "hit")); said.Add(target); }
            }
            return callouts;
        }

        /// <summary>JS dmgClass: the floater's size tier from the hit and the target's max HP.</summary>
        public static string DamageClass(int amount, int targetMaxHp)
        {
            var pct = targetMaxHp > 0 ? amount / (double)targetMaxHp : 0;
            if (pct >= HugeHitShare) return "dmg pct35";
            if (amount >= 25) return "dmg crit";
            if (pct >= BigHitShare) return "dmg pct20";
            if (amount >= 15) return "dmg heavy";
            if (amount < 6) return "dmg small";
            return "dmg";
        }

        /// <summary>
        /// JS onNumbers for one group: Dodged / Hit — Evade Failed and the break and stagger words, then each event's numbers —
        /// a hit's Poise absorbed, Block and "-N" (guardHitFloatParts), other HP loss, heals, Block gained and "+N Status".
        /// </summary>
        public static (List<Core2Callout> Callouts, List<Core2Floater> Floaters) Numbers(IEnumerable<Core2FightEvent> group, Core2Content content, Func<string, int> maxHp)
        {
            var callouts = new List<Core2Callout>();
            var floaters = new List<Core2Floater>();
            void Float(string target, string text, string cls) { if (target != null) floaters.Add(new Core2Floater { TargetId = target, Text = text, Class = cls }); }
            foreach (var e in group)
            {
                var d = e.Data;
                var target = (string)d["targetId"];
                switch (e.Type)
                {
                    case "attackMissed":
                        if ((string)d["missReason"] == "accuracy" && (bool?)d["evadeAttempted"] == true) callouts.Add(Callout(target, "dodged"));
                        break;
                    case "attackHit":
                        if ((bool?)d["evadeAttempted"] == true && (bool?)d["bypass"] != true) callouts.Add(Callout(target, "evadeFailed"));
                        break;
                    case "damageDealt":
                    {
                        var amount = Math.Max(0, (int?)d["amount"] ?? 0);
                        var blocked = Math.Min(amount, Math.Max(0, (int?)d["blocked"] ?? 0));
                        var residual = amount - blocked;
                        var poise = Math.Max(0, (int?)d["poiseAbsorbed"] ?? 0);
                        var hpLoss = (int?)d["hpLoss"];
                        if (poise > 0) Float(target, Core2CombatText.T("combat.receipt.poiseAbsorbed", ("n", poise)), "poise small");
                        if (blocked > 0) Float(target, Core2CombatText.Number(blocked), "blk small");
                        // Core 2's receipt names the HP lost after Poise; the "-N" is that loss.
                        var loss = hpLoss ?? residual;
                        if (loss > 0) Float(target, "-" + loss, DamageClass(loss, maxHp(target)));
                        break;
                    }
                    case "hpLost":
                        if ((string)d["cause"] != "attack") Float(target, "-" + ((int?)d["amount"] ?? 0), DamageClass((int?)d["amount"] ?? 0, maxHp(target)));
                        break;
                    case "healed":
                        if (((int?)d["amount"] ?? 0) > 0) Float(target, "+" + (int)d["amount"], "heal");
                        break;
                    case "blockGained":
                        if (((int?)d["amount"] ?? 0) > 0) Float(target, "+" + (int)d["amount"], "blk");
                        break;
                    case "statusApplied":
                    {
                        var stacks = (int?)d["stacks"] ?? 0;
                        if (stacks <= 0) break;
                        var status = (string)d["status"];
                        var row = content.Statuses.FirstOrDefault(s => s.Id == status);
                        var harmful = (string)d["sourceKind"] == "enemy" && (string)d["targetKind"] == "player" || (string)d["sourceKind"] == "player" && (string)d["targetKind"] == "enemy";
                        Float(target, "+" + stacks + " " + ((string)row?.Raw["name"] ?? Core2CombatText.Words(status)), harmful ? "debuff" : "buff");
                        break;
                    }
                    case "persistentPoiseChanged":
                        if ((string)d["source"] == "poiseDamage" && ((int?)d["spent"] ?? 0) > 0)
                            Float(target, Core2CombatText.T("combat.receipt.poiseDamage", ("n", (int)d["spent"])), "poise small");
                        break;
                    case "combatEnemyReactionStacked":
                        callouts.Add(new Core2Callout { TargetId = (string)d["sourceId"], Kind = "enemyReaction", Text = MoveName(content, (string)d["enemyId"], (string)d["moveId"]), Tone = "word" });
                        break;
                    case "poiseBroken": callouts.Add(Callout(target, "poiseBreak")); break;
                    case "blockBroken": callouts.Add(Callout(target, "blockBreak")); break;
                    case "enemyStaggered": case "playerStaggered": callouts.Add(Callout(target ?? (string)d["sourceId"], "staggered")); break;
                    case "combatCounterTriggered": callouts.Add(Callout(target, "countered")); break;
                }
            }
            return (callouts, floaters);
        }

        /// <summary>JS runActionTimeline over the grouped beats: every stage at its time, with its words and numbers.</summary>
        public static Core2Timeline Plan(IReadOnlyList<Core2FightEvent> events, Core2Content content, Core2BeatTiming timing, Func<string, int> maxHp)
        {
            var beats = Group(events, content);
            var plan = new Core2Timeline { Beats = beats };
            var t = 0.0;
            var gap = Math.Max(0, timing.GapMs);
            var step = Math.Max(0, timing.StepMs);
            Core2TimelineStep Add(double at, string stage, int beat, List<Core2FightEvent> group)
            {
                var row = new Core2TimelineStep { AtMs = at, Stage = stage, Beat = beat, Events = group };
                plan.Steps.Add(row);
                return row;
            }
            for (var index = 0; index < beats.Count; index++)
            {
                var beat = beats[index];
                if (beat.Banner != null || beat.ActorId == null || beat.Kind == "reveal")
                {
                    Add(t, "intent", index, beat.Events);
                    var numbers = Numbers(beat.Events, content, maxHp);
                    var damage = Add(t, "damage", index, beat.Events);
                    damage.Callouts.AddRange(numbers.Callouts); damage.Floaters.AddRange(numbers.Floaters);
                    Add(t, "hud", index, beat.Events);
                    Add(t, "resolution", index, beat.Events);
                    t += beat.Banner != null ? Math.Max(0, timing.BannerMs) : step;
                    continue;
                }
                Add(t, "intent", index, beat.Events);
                Add(t, "action", index, beat.Events);
                var sprite = t + (beat.Prepared ? 0 : gap);
                Add(sprite, "sprite", index, beat.Events);
                var total = Math.Max(0, timing.LungeMs);
                var impact = total * timing.WindupFraction;
                var contact = sprite + impact;
                Add(contact, "contact", index, beat.Events);
                var after = contact + (total - impact);
                if (beat.Telegraph)
                {
                    Add(after, "resolution", index, beat.Events);
                    t = after + Math.Max(gap, step);
                    continue;
                }
                var x = after + gap;
                var groups = ReactionGroups(beat);
                for (var g = 0; g < groups.Count; g++)
                {
                    var group = groups[g];
                    Add(x, "result", index, group).Callouts.AddRange(ResultCallouts(group));
                    Add(x + gap, "reaction", index, group);
                    var numbers = Numbers(group, content, maxHp);
                    var damage = Add(x + 2 * gap, "damage", index, group);
                    damage.Callouts.AddRange(numbers.Callouts); damage.Floaters.AddRange(numbers.Floaters);
                    Add(x + 2 * gap, "hud", index, group);
                    x = x + 3 * gap;
                }
                Add(x, "resolution", index, beat.Events);
                t = x + Math.Max(gap, step);
            }
            plan.DurationMs = t;
            return plan;
        }
    }
}
