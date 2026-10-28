// Core2RunSkills.cs — class XP and used-weapon XP for a Core 2 run (port plan phase 10), engine-free.
//
// JS at the pin:
//   src/model/skills.js               skillTracks (weapon groups, Spellcraft, armour, maneuvers, dual-wield, classes),
//                                     xpToNext (balance.skill.xp for item tracks, balance.skill.class.xp for classes; the
//                                     exponential curve rounds with no epsilon), skillMaxLevel, awardSkillXp (a Core run
//                                     queues no legacy draft, rank-up, attribute or feat rewards: queueRewards false)
//   src/model/abilitySkillActivation.js activateAbilitySkill (Spellcraft and Combat Maneuvers open at level 1 with one
//                                     draft the first time they are paid: a Core run carries progressionRulesVersion 1)
//   src/model/classTree.js            awardClassXp (a won fight: perWin, plus bossKill for a boss, x the multiplier)
//   src/engine/skillXp.js             recordSkillXp (Core: cardResolved, damageDealt, blockGained, combatEnd), favoredMult,
//                                     skillXpReceipt (fractions floored once), settleConfiguredSkillProgression
//   src/model/gameProfileProgression.js configuredSkillXpMultiplier (classXpMultiplier / weaponXpMultiplier, clamped 0–10,
//                                     1 when absent), configuredCharacterLevels, skillLevelNotices
//   src/model/rewardprogress.js       combatXpGains (the spoils' XP lines)
//   src/main.js                       onCombatEnd (configured): xpBefore, the victory receipt and victoryTitle
//
// A Core run has no class mastery (run.classMasteryState is absent), so no class-track milestone, unlock or quest XP
// applies. The level notices' card-change rows compare every owned card's primary number with and without the skill
// flat bonus (every flatEvery levels of a weapon track, for cards carrying one of the held pieces' card schools); no
// Core 2 card carries such a school at the pin (UnityTests/Core2Run checks it), so those rows are empty and no next
// milestone exists, exactly as JS computes them.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>JS awardSkillXp's result: { skillId, before, after, levelUps, gained, skillAwards }.</summary>
    public sealed class Core2SkillAward
    {
        public string SkillId { get; set; }
        public int Before { get; set; }
        public int After { get; set; }
        public int LevelUps { get; set; }
        public int Gained { get; set; }
        /// <summary>JS returns skillAwards only when XP was paid (an unpaid award returns early without it).</summary>
        public bool Paid { get; set; }
        public JObject ToJson()
        {
            var row = new JObject { ["skillId"] = SkillId, ["before"] = Before, ["after"] = After, ["levelUps"] = LevelUps, ["gained"] = Gained };
            if (Paid) row["skillAwards"] = new JArray();
            return row;
        }
    }

    /// <summary>The skill XP one settled fight paid: the class award (null on a loss or zero pay) and one award per paid track.</summary>
    public sealed class Core2SkillSettlement
    {
        public Core2SkillAward ClassAward { get; set; }
        public List<Core2SkillAward> TrackAwards { get; } = new List<Core2SkillAward>();
        /// <summary>The fight's weapon receipt after the multiplier (track → whole XP), in the order the fight paid it.</summary>
        public List<KeyValuePair<string, int>> Receipt { get; } = new List<KeyValuePair<string, int>>();
        /// <summary>Armament ids the fight's cards were dealt by, first use first (display only).</summary>
        public List<string> UsedArmaments { get; } = new List<string>();
        public IEnumerable<Core2SkillAward> Awards => new[] { ClassAward }.Where(a => a != null).Concat(TrackAwards);
    }

    /// <summary>The progression settings a run reads from its profile's progression module (overridable for a test or an old save).</summary>
    public sealed class Core2ProgressionRules
    {
        public bool CharacterLevels { get; set; } = true;
        public double ClassXpMultiplier { get; set; } = 1;
        public double WeaponXpMultiplier { get; set; } = 1;

        /// <summary>JS configuredSkillXpMultiplier: a finite value clamped to 0–10; a missing one reads 1 (an old run's rate).</summary>
        public static double Multiplier(JToken value) => value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) && !double.IsNaN((double)value) && !double.IsInfinity((double)value)
            ? Math.Max(0, Math.Min(10, (double)value)) : 1;

        public static Core2ProgressionRules From(Core2RunData data)
        {
            var module = data.Module("progression");
            return new Core2ProgressionRules
            {
                CharacterLevels = (bool?)module?["characterLevels"] == true,
                ClassXpMultiplier = Multiplier(module?["classXpMultiplier"]),
                WeaponXpMultiplier = Multiplier(module?["weaponXpMultiplier"]),
            };
        }
    }

    public static class Core2RunSkills
    {
        public const string Spellcraft = "item:magic-focus";
        public const string Maneuvers = "combatManeuvers";
        public static string ClassSkillId(string classId) => "class:" + classId;

        public static JObject Track(Core2RunData data, string skillId) => ((JArray)data.Skill["tracks"]).OfType<JObject>().FirstOrDefault(t => (string)t["id"] == skillId);
        public static string KindOf(Core2RunData data, string skillId) => (string)Track(data, skillId)?["kind"];

        private static JObject Curve(Core2RunData data, string kind) => (JObject)data.Skill[kind == "class" ? "classXp" : "xp"];

        /// <summary>JS xpStepCost: linear base + step × base × multScaler, or base × growth^step; rounded to roundTo.</summary>
        public static int XpStepCost(JObject curve, int step, double exponentialEpsilon)
        {
            var b = (double)curve["base"];
            var linear = (bool?)curve["linear"] == true;
            var raw = linear ? b + step * b * (double)curve["multScaler"] : b * Math.Pow((double)curve["growth"], step);
            var unit = curve["roundTo"]?.Type == JTokenType.Integer && (int)curve["roundTo"] > 0 ? (int)curve["roundTo"] : 1;
            var epsilon = linear ? 1e-9 : exponentialEpsilon;
            return (int)Math.Max(unit, Math.Floor(raw / unit + epsilon + 0.5) * unit);
        }

        /// <summary>JS skills.js xpToNext: the step from <paramref name="level"/> (level 0 is the first step).</summary>
        public static int XpToNext(Core2RunData data, string kind, int level) => XpStepCost(Curve(data, kind), level > 0 ? level : 0, 0);

        /// <summary>JS skillMaxLevel: the track's ceiling (null for none).</summary>
        public static int? MaxLevel(Core2RunData data, string kind)
        {
            var cap = Curve(data, kind == "ability" ? "weapon" : kind)["maxLevel"];
            return cap?.Type == JTokenType.Integer && (int)cap > 0 ? (int)cap : (int?)null;
        }

        /// <summary>JS awardSkillXp with queueRewards false (a Core run): the ledger climbs as far as the XP buys.</summary>
        public static Core2SkillAward Award(Core2RunData data, Core2Run run, string skillId, double amount)
        {
            var kind = KindOf(data, skillId) ?? throw new InvalidOperationException("awardSkillXp: '" + skillId + "' is not a skill track");
            if ((skillId == Spellcraft || skillId == Maneuvers) && amount > 0)
            {
                // JS activateAbilitySkill (progressionRulesVersion 1): the track opens at level 1 with one draft queued.
                var opened = Row(run, skillId);
                if (opened.Level <= 0) { opened.Level = 1; opened.PendingDrafts += 1; }
            }
            var row = Row(run, skillId);
            var before = row.Level;
            var gain = double.IsNaN(amount) || double.IsInfinity(amount) ? 0 : (int)Math.Floor(amount);
            if (gain <= 0) return new Core2SkillAward { SkillId = skillId, Before = before, After = before };
            row.Xp += gain;
            var cap = MaxLevel(data, kind);
            var cost = XpToNext(data, kind, row.Level);
            while ((cap == null || row.Level < cap.Value) && row.Xp >= cost)
            {
                row.Xp -= cost;
                row.Level += 1;
                cost = XpToNext(data, kind, row.Level);
            }
            return new Core2SkillAward { SkillId = skillId, Before = before, After = row.Level, LevelUps = row.Level - before, Gained = gain, Paid = true };
        }

        private static Core2SkillRow Row(Core2Run run, string skillId)
        {
            if (!run.Skills.TryGetValue(skillId, out var row)) run.Skills[skillId] = row = new Core2SkillRow();
            return row;
        }

        /// <summary>JS awardClassXp (no class mastery): a won fight pays perWin, plus bossKill for a boss, × the multiplier.</summary>
        public static Core2SkillAward AwardClassXp(Core2RunData data, Core2Run run, bool victory, string pool, double multiplier)
        {
            if (!victory || run.Class == null) return null;
            var xp = Curve(data, "class");
            var b = ((double?)xp["perWin"] ?? 0) + (pool == "boss" ? (double?)xp["bossKill"] ?? 0 : 0);
            var amount = (int)Math.Floor(b * multiplier);
            if (!(amount > 0)) return null;
            return Award(data, run, ClassSkillId(run.Class), amount);
        }

        /// <summary>JS settleConfiguredSkillProgression: the weapon receipt × weaponXpMultiplier (floored per track), then the class award.</summary>
        public static Core2SkillSettlement Settle(Core2RunData data, Core2Run run, IEnumerable<KeyValuePair<string, int>> receipt, bool victory, string pool, Core2ProgressionRules rules)
        {
            var settlement = new Core2SkillSettlement();
            foreach (var pair in receipt)
            {
                var scaled = (int)Math.Floor(pair.Value * rules.WeaponXpMultiplier);
                settlement.Receipt.Add(new KeyValuePair<string, int>(pair.Key, scaled));
            }
            foreach (var pair in settlement.Receipt) settlement.TrackAwards.Add(Award(data, run, pair.Key, pair.Value));
            settlement.ClassAward = AwardClassXp(data, run, victory, pool, rules.ClassXpMultiplier);
            return settlement;
        }

        /// <summary>JS skillLevelNotices: one notice per crossing, class first, with the weapons that earned it and the level's effects.</summary>
        public static JArray LevelNotices(Core2RunData data, IEnumerable<Core2SkillAward> awards, IReadOnlyList<string> usedArmaments)
        {
            var notices = new JArray();
            foreach (var award in awards.Where(a => a != null && a.LevelUps > 0))
            {
                var track = Track(data, award.SkillId);
                var kind = (string)track?["kind"];
                var weapons = new List<string>();
                foreach (var id in usedArmaments ?? Array.Empty<string>())
                {
                    var piece = data.Armament(id);
                    if (piece != null && Core2Json.Strings(piece["itemTypeTags"]).Contains(award.SkillId) && !weapons.Contains((string)piece["name"])) weapons.Add((string)piece["name"]);
                }
                var notice = new JObject { ["skillId"] = award.SkillId, ["label"] = (string)track?["label"] ?? award.SkillId, ["before"] = award.Before, ["after"] = award.After };
                if (weapons.Count > 0) notice["weapons"] = new JArray(weapons);
                var max = kind != null ? MaxLevel(data, kind) : null;
                // No Core 2 card carries a held piece's card school, so no level changes a card number (see the header).
                notice["effects"] = new JObject { ["version"] = 1, ["changes"] = new JArray(), ["nextMilestone"] = JValue.CreateNull(), ["maxLevel"] = max.HasValue ? (JToken)max.Value : JValue.CreateNull() };
                notices.Add(notice);
            }
            return notices;
        }

        /// <summary>JS combatXpGains: the XP each ledger was paid ({ level, levelDiscarded?, tracks }).</summary>
        public static JObject XpGains(IEnumerable<KeyValuePair<string, int>> receipt, IEnumerable<Core2SkillAward> awards, int levelGained, int levelDiscarded = 0)
        {
            var tracks = new JObject();
            void Add(string id, double xp)
            {
                if (string.IsNullOrEmpty(id) || !(xp > 0)) return;
                tracks[id] = ((int?)tracks[id] ?? 0) + (int)Math.Floor(xp);
            }
            foreach (var pair in receipt ?? Enumerable.Empty<KeyValuePair<string, int>>()) Add(pair.Key, pair.Value);
            foreach (var award in awards ?? Enumerable.Empty<Core2SkillAward>()) if (award != null) Add(award.SkillId, award.Gained);
            var row = new JObject { ["level"] = levelGained > 0 ? levelGained : 0 };
            if (levelDiscarded > 0) row["levelDiscarded"] = levelDiscarded;
            row["tracks"] = tracks;
            return row;
        }

        /// <summary>JS onCombatEnd xpBefore: the character ledger and each track's level and XP before the fight paid them.</summary>
        public static JObject XpBefore(Core2Run run)
        {
            var tracks = new JObject();
            foreach (var pair in run.Skills) tracks[pair.Key] = new JObject { ["level"] = pair.Value.Level, ["xp"] = pair.Value.Xp };
            return new JObject { ["character"] = run.Level.ToJson(), ["tracks"] = tracks };
        }

        /// <summary>JS victoryTitle: a boss falls by name; an elite is vanquished; otherwise VICTORY.</summary>
        public static string VictoryTitle(Core2Content content, JObject encounter)
        {
            var pool = (string)encounter["pool"];
            if (pool == "boss")
            {
                var id = Core2Json.Strings(encounter["enemies"]).First();
                var name = content.Enemies.FirstOrDefault(e => e.Id == id)?.Name ?? id;
                return name.ToUpperInvariant() + " FALLS";
            }
            return pool == "elite" ? "ELITE VANQUISHED" : "VICTORY";
        }

        /// <summary>
        /// JS favoredMult: the product of every mounted skillXpMult passive whose carrier's own tags name the track. Only the
        /// class card carries scope tags (src/engine/properties.js), so this is the class card's `favored` rule for the item
        /// types its tags name (Reaver and Rogue: Blade; Starseer and Herald: Spellcraft).
        /// </summary>
        public static double FavoredMult(Core2RunData data, Core2Run run, string skillId)
        {
            var birth = data.Class(run.Class);
            if (!birth.Tags.Contains(skillId)) return 1;
            var m = 1.0;
            foreach (var rule in data.CarrierRules(birth.PropertyTags))
                if (rule["passives"]?["skillXpMult"] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) m *= (double)v;
            return m;
        }

        /// <summary>The weapon group of an armament (JS groupOfPiece: its first item type that is not armour).</summary>
        public static string GroupOfPiece(JObject piece) => piece == null ? null : Core2Json.Strings(piece["itemTypeTags"]).FirstOrDefault(t => t != "item:armor");
    }

    /// <summary>
    /// JS recordSkillXp for a Core run, listening on a fight's event bus: a resolved card records its weapon group (and the
    /// armament that dealt it), each hit or Block a card's piece lent pays perHit to that group, and a won fight pays
    /// perWinEquipped to every group used (killMult for the group that landed the killing hit), each × favoredMult.
    /// </summary>
    public sealed class Core2SkillXpRecorder
    {
        private readonly Core2RunData _data;
        private readonly Core2Run _run;
        private readonly Core2Fight _fight;
        private readonly Dictionary<string, (bool Has, string Id)> _usedByCard = new Dictionary<string, (bool, string)>(StringComparer.Ordinal);
        /// <summary>Instance id → card id, from every event that names both (a card an enemy shuffled in is learnt when drawn or played).</summary>
        private readonly Dictionary<string, string> _cardIds = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The receipt in the order the fight first touched each group (JS receipt.xp insertion order).</summary>
        public List<KeyValuePair<string, double>> Xp { get; } = new List<KeyValuePair<string, double>>();
        public string KillGroup { get; private set; }
        public List<string> UsedArmaments { get; } = new List<string>();

        public Core2SkillXpRecorder(Core2RunData data, Core2Run run, Core2Fight fight)
        {
            _data = data; _run = run; _fight = fight;
        }

        private JObject Rows => (JObject)_data.Skill["xp"];

        private (bool Has, string Id) Used(string cardId)
        {
            if (!_usedByCard.TryGetValue(cardId, out var used)) _usedByCard[cardId] = used = Core2RunFights.UsedArmament(_data, _run.Loadout, _run.Class, cardId);
            return used;
        }

        /// <summary>JS groupOfCard: the committed armament wins; otherwise the piece a granted card names.</summary>
        private (string Group, string ArmamentId, bool Known) GroupOf(string instanceId)
        {
            if (instanceId == null) return (null, null, false);
            if (!_cardIds.TryGetValue(instanceId, out var cardId)) cardId = _run.Deck.FirstOrDefault(c => c.InstanceId == instanceId)?.CardId;
            if (cardId == null) return (null, null, false);
            var used = Used(cardId);
            if (used.Has) return (used.Id == null ? null : Core2RunSkills.GroupOfPiece(_data.Armament(used.Id)), used.Id, true);
            var grantedBy = _run.Deck.Concat(_run.Sideboard).FirstOrDefault(c => c.InstanceId == instanceId)?.GrantedBy;
            if (grantedBy == null || grantedBy.StartsWith("armor/", StringComparison.Ordinal)) return (null, null, false);
            var id = grantedBy.StartsWith("armament/", StringComparison.Ordinal) ? grantedBy.Substring("armament/".Length) : grantedBy;
            return (Core2RunSkills.GroupOfPiece(_data.Armament(id)), id, true);
        }

        private int Index(string group) => Xp.FindIndex(p => p.Key == group);
        private void Touch(string group) { if (Index(group) < 0) Xp.Add(new KeyValuePair<string, double>(group, 0)); }
        private void Pay(string group, double amount, double mult)
        {
            if (group == null || !(amount > 0)) return;
            var i = Index(group);
            if (i < 0) Xp.Add(new KeyValuePair<string, double>(group, amount * mult));
            else Xp[i] = new KeyValuePair<string, double>(group, Xp[i].Value + amount * mult);
        }
        private void Remember(string armamentId) { if (armamentId != null && !UsedArmaments.Contains(armamentId)) UsedArmaments.Add(armamentId); }

        public void Handle(Core2FightEvent ev)
        {
            var d = ev.Data;
            if (d["cardInstanceId"]?.Type == JTokenType.String && d["cardId"]?.Type == JTokenType.String && (ev.Type == "cardPlayed" || ev.Type == "cardResolved" || ev.Type == "cardDrawn"))
                _cardIds[(string)d["cardInstanceId"]] = (string)d["cardId"];
            switch (ev.Type)
            {
                case "cardResolved":
                {
                    if ((string)d["sourceId"] != null && (string)d["sourceId"] != _fight.Player.Id) return;
                    var (group, armament, _) = GroupOf((string)d["cardInstanceId"]);
                    if (group == null) return;
                    Touch(group);
                    Remember(armament);
                    return;
                }
                case "damageDealt":
                {
                    if ((string)d["sourceId"] != _fight.Player.Id || (string)d["targetId"] == _fight.Player.Id || !((double?)d["amount"] > 0)) return;
                    var (group, armament, _) = GroupOf((string)d["cardInstanceId"]);
                    if (group == null) return;
                    Touch(group);
                    Remember(armament);
                    Pay(group, (double)Rows["perHit"], Core2RunSkills.FavoredMult(_data, _run, group));
                    var target = _fight.Enemies.FirstOrDefault(e => e.Id == (string)d["targetId"]);
                    if (target != null && (target.Hp <= 0 || !target.Alive)) KillGroup = group;
                    return;
                }
                case "blockGained":
                {
                    if ((string)d["targetId"] != _fight.Player.Id || !((double?)d["amount"] > 0)) return;
                    var (group, armament, _) = GroupOf((string)d["cardInstanceId"]);
                    if (group == null) return;
                    Touch(group);
                    Remember(armament);
                    Pay(group, (double)Rows["perHit"], Core2RunSkills.FavoredMult(_data, _run, group));
                    return;
                }
                case "combatEnd":
                {
                    if ((bool?)d["victory"] != true) return;
                    foreach (var group in Xp.Select(p => p.Key).ToList())
                        Pay(group, (double)Rows["perWinEquipped"] * (KillGroup == group ? (double)Rows["killMult"] : 1), Core2RunSkills.FavoredMult(_data, _run, group));
                    return;
                }
            }
        }

        /// <summary>JS skillXpReceipt: each track's accumulated XP floored once; only positive totals are paid.</summary>
        public List<KeyValuePair<string, int>> Receipt() => Xp.Select(p => new KeyValuePair<string, int>(p.Key, (int)Math.Floor(p.Value))).Where(p => p.Value > 0).ToList();
    }
}
