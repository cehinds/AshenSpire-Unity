// Published test-898 character and skill ledgers. Rules are frozen per run;
// callers provide pool reconciliation when committing an earned character level.
// Banking changes no pools. Claims retain surplus XP and queue skill drafts.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalEarnedProgression
    {
        private readonly JObject _data, _mechanics;
        private readonly Func<JObject, int> _reconcile;
        public OriginalEarnedProgression(JObject data, JObject mechanics, Func<JObject, int> reconcile = null)
        { _data = (JObject)data.DeepClone(); _mechanics = (JObject)mechanics.DeepClone(); _reconcile = reconcile; }
        private JObject Balance => _data["balance"] as JObject ?? new JObject();
        public static JObject EmptyLevel() => new JObject { ["xp"] = 0, ["level"] = 1, ["unspentPoints"] = 0 };
        private static bool Whole(JToken token) => token?.Type == JTokenType.Integer || token?.Type == JTokenType.Float && Finite((double)token) && Math.Floor((double)token) == (double)token;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Gain(double value) => Finite(value) ? Math.Max(0, Math.Floor(value)) : 0;
        private static int Positive(JToken value, int fallback) => Whole(value) && (double)value > 0 && (double)value <= int.MaxValue ? (int)value : fallback;
        public static int CharacterLevel(JObject run) => Positive(run?["level"]?["level"], 1);
        public double CharacterCost(int level)
        {
            var curve = (JObject)(Balance["level"]?["xp"] as JObject ?? new JObject()).DeepClone();
            if (!Finite((double?)curve["base"] ?? 0) || ((double?)curve["base"] ?? 0) <= 0) curve["base"] = 100;
            if (!Finite((double?)curve["growth"] ?? 0) || ((double?)curve["growth"] ?? 0) <= 0) curve["growth"] = 1.15;
            return OriginalXpCurve.StepCost(curve, Math.Max(0, level - 1));
        }
        private int Points(int? overridePoints) => overridePoints > 0 ? overridePoints.Value : Positive(Balance["levelUp"]?["pointsPerLevel"], 1);
        private int? Ceiling => Whole(Balance["levelUp"]?["maxLevels"]) ? (int?)Balance["levelUp"]["maxLevels"] : null;
        public JObject Climb(int level = 1, double xp = 0, double gain = 0)
        {
            var start = Math.Max(1, level); var next = start;
            var bank = (Finite(xp) ? xp : 0) + Gain(gain);
            var cap = Positive(Balance["level"]?["maxLevelsPerFight"], 0);
            string cappedBy = null; var discarded = 0d;
            var cost = CharacterCost(next);
            while (bank >= cost)
            {
                if (cap > 0 && next - start >= cap) { cappedBy = "fight"; discarded = bank - (cost - 1); bank = cost - 1; break; }
                if (Ceiling.HasValue && next >= Ceiling.Value) { cappedBy = "level"; break; }
                bank -= cost; next = checked(next + 1); cost = CharacterCost(next);
            }
            return new JObject { ["level"] = next, ["xp"] = bank, ["levelUps"] = next - start, ["capped"] = cappedBy != null, ["cappedBy"] = cappedBy, ["discarded"] = discarded };
        }
        public int PendingCharacterLevels(JObject run)
        {
            var row = run?["level"] as JObject ?? EmptyLevel(); var level = CharacterLevel(run);
            var xp = Whole(row["xp"]) ? Math.Max(0, (double)row["xp"]) : 0; var count = 0;
            while ((!Ceiling.HasValue || level < Ceiling.Value) && xp >= CharacterCost(level))
            { xp -= CharacterCost(level); level = checked(level + 1); count = checked(count + 1); }
            return count;
        }
        public JObject BankCharacter(JObject run, double amount)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (!(run["level"] is JObject)) run["level"] = EmptyLevel();
            var row = (JObject)run["level"]; var before = CharacterLevel(run); var gain = Gain(amount);
            var discarded = gain > 0 ? (double)Climb(before, (double)row["xp"], gain)["discarded"] : 0;
            if (gain > 0) row["xp"] = (double)row["xp"] + gain - discarded;
            return new JObject { ["before"] = before, ["after"] = before, ["levelUps"] = 0, ["pendingLevelUps"] = PendingCharacterLevels(run), ["points"] = 0, ["thresholds"] = 0, ["gained"] = gain, ["discarded"] = discarded };
        }
        public JObject ClaimCharacter(JObject run, int? pointsPerLevel = null, bool grantStats = true)
        {
            if (run == null || PendingCharacterLevels(run) < 1) return null;
            var draft = (JObject)run.DeepClone(); var before = CharacterLevel(draft); var cost = CharacterCost(before);
            var points = grantStats ? Points(pointsPerLevel) : 0; var row = (JObject)draft["level"];
            row["xp"] = (double)row["xp"] - cost; row["level"] = before + 1; row["unspentPoints"] = checked((int)row["unspentPoints"] + points);
            var thresholds = _reconcile?.Invoke(draft) ?? 0;
            run.RemoveAll(); foreach (var property in draft.Properties()) run[property.Name] = property.Value.DeepClone();
            return new JObject { ["before"] = before, ["after"] = before + 1, ["points"] = points, ["spent"] = cost, ["thresholds"] = thresholds, ["remaining"] = PendingCharacterLevels(run) };
        }
        public JArray Tracks()
        {
            var tracks = new JArray();
            foreach (var node in (_data["nodes"] as JArray ?? new JArray()).OfType<JObject>())
                if ((string)node["parentId"] == "itemType" && (string)node["id"] != "item:armor")
                    tracks.Add(new JObject { ["id"] = node["id"].DeepClone(), ["kind"] = (string)node["id"] == "item:magic-focus" ? "focus" : "weapon", ["label"] = node["label"]?.DeepClone() });
            foreach (var weight in _mechanics["weight"]?["classes"] as JArray ?? new JArray())
                tracks.Add(new JObject { ["id"] = "armour:" + weight["id"], ["kind"] = "armour", ["label"] = ((string)weight["label"] ?? (string)weight["id"]) + " armour" });
            tracks.Add(new JObject { ["id"] = "dualWield", ["kind"] = "dual", ["label"] = "Dual-wield" });
            foreach (var hero in _data["classes"] as JArray ?? new JArray()) tracks.Add(new JObject { ["id"] = "class:" + hero["id"], ["kind"] = "class", ["label"] = hero["name"]?.DeepClone() ?? hero["id"].DeepClone() });
            return tracks;
        }
        private string Kind(string id) => (string)Tracks().FirstOrDefault(track => (string)track["id"] == id)?["kind"];
        public double SkillCost(string kind, int level)
        {
            if (!new[] { "weapon", "armour", "focus", "dual", "class" }.Contains(kind)) throw new ArgumentException("Unknown skill kind " + kind);
            var curve = (kind == "class" ? Balance["skill"]?["class"]?["xp"] : Balance["skill"]?["xp"]) as JObject ?? throw new ArgumentException("Skill curve is not authored.");
            return OriginalXpCurve.StepCost(curve, Math.Max(0, level), 0);
        }
        public int PendingSkillLevels(JObject run, string id)
        {
            var kind = Kind(id); var row = run?["skills"]?[id] as JObject;
            if (kind == null || row == null) return 0;
            var level = (int)row["level"]; var xp = (double)row["xp"]; var count = 0;
            while (xp >= SkillCost(kind, level)) { xp -= SkillCost(kind, level); level = checked(level + 1); count = checked(count + 1); }
            return count;
        }
        private JObject SkillRow(JObject run, string id)
        {
            if (Kind(id) == null) throw new ArgumentException("Unknown skill track " + id);
            if (!(run["skills"] is JObject)) run["skills"] = new JObject();
            if (!(run["skills"][id] is JObject)) run["skills"][id] = new JObject { ["xp"] = 0, ["level"] = 0, ["pendingDrafts"] = 0 };
            return (JObject)run["skills"][id];
        }
        public JObject BankSkill(JObject run, string id, double amount)
        {
            var row = SkillRow(run, id); var before = (int)row["level"]; var gain = Gain(amount); row["xp"] = (double)row["xp"] + gain;
            return new JObject { ["skillId"] = id, ["before"] = before, ["after"] = before, ["levelUps"] = 0, ["pendingLevelUps"] = PendingSkillLevels(run, id), ["upgraded"] = new JArray(), ["gained"] = gain };
        }
        public string[] Schools(JObject loadout, string id)
        {
            var kind = Kind(id); if (!new[] { "weapon", "focus", "dual" }.Contains(kind)) return Array.Empty<string>();
            var schools = (_data["nodes"] as JArray ?? new JArray()).Where(n => (string)n["parentId"] == "card").Select(n => (string)n["id"]).ToHashSet();
            var held = new[] { "rightHand", "leftHand" }.Select(slot => {
                var index = (int?)loadout?["active"]?[slot] ?? 0;
                return (string)(loadout?["sets"]?[slot] as JArray)?.ElementAtOrDefault(index);
            }).Where(item => item != null).ToArray();
            return (_data["equipment"]?["armaments"] as JArray ?? new JArray()).OfType<JObject>().Where(piece => held.Contains((string)piece["id"]) && (kind == "dual" || Tags("armament",piece).Contains(id)))
                .SelectMany(piece => Tags("armament",piece)).Where(schools.Contains).Distinct().ToArray();
        }
        // The published content stores tags in its canonical join, not on rows.
        private string[] Tags(string family, JObject row) => (_data["tagging"] as JArray ?? new JArray())
            .Where(tag => (string)tag["family"] == family && (string)tag["objectId"] == (string)row["id"] && ((string)tag["scope"] ?? "") == "")
            .Select(tag => (string)tag["tagId"]).ToArray();
        private JArray UpgradeCards(JObject run, string id, int level)
        {
            var upgraded = new JArray(); var at = Positive(Balance["skill"]?["upgradeAt"], 0); if (at == 0 || level < at) return upgraded;
            var schools = Schools(run["loadout"] as JObject, id); if (schools.Length == 0) return upgraded;
            foreach (var card in (run["deck"] as JArray ?? new JArray()).Concat(run["sideboard"] as JArray ?? new JArray()).OfType<JObject>())
            {
                if ((bool?)card["upgraded"] == true || card["sourceArmamentId"]?.Type == JTokenType.String || new[] { "granted", "weaponArt" }.Contains((string)card["equipmentRole"])) continue;
                var definition = (_data["cards"] as JArray ?? new JArray()).FirstOrDefault(c => (string)c["id"] == (string)card["cardId"]);
                if (!(definition is JObject row) || !Tags("card",row).Any(schools.Contains)) continue;
                card["upgraded"] = true; upgraded.Add(card["instanceId"]?.DeepClone());
            }
            return upgraded;
        }
        public JObject ClaimSkill(JObject run, string id)
        {
            var kind = Kind(id); var row = run?["skills"]?[id] as JObject;
            if (kind == null || row == null || (double)row["xp"] < SkillCost(kind, (int)row["level"])) return null;
            var before = (int)row["level"]; row["xp"] = (double)row["xp"] - SkillCost(kind, before); row["level"] = before + 1; row["pendingDrafts"] = checked((int)row["pendingDrafts"] + 1);
            return new JObject { ["skillId"] = id, ["before"] = before, ["after"] = before + 1, ["levelUps"] = 1, ["upgraded"] = UpgradeCards(run, id, before + 1), ["gained"] = 0 };
        }
        public bool SpendSkillDraft(JObject run, string id)
        { var row = run?["skills"]?[id] as JObject; if (row == null || (int)row["pendingDrafts"] <= 0) return false; row["pendingDrafts"] = (int)row["pendingDrafts"] - 1; return true; }
    }
}
