// WeaponCardProjection.cs — the same card definition drives UI and combat.
// Tune equipment.basicCardProfiles and equipment.modFields in Original/content.json.
// profileOverrides is a host-owned profile-ID map. pointsPerTier=1 enables per-point
// offense; pointsOffset can preserve the baseline value while rewarding investment.
// Persist overrides with the run to prevent live tuning from changing an old save.
// Baseline equipment projection supports level zero; smith upgrade/mount services
// must supply their resolved definition before this component is extended to them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class WeaponCardProjection
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly WeaponCardComposer _composer;
        private readonly WeaponLoadout _locations;
        private readonly JObject _data;
        public WeaponCardProjection(OriginalContentCatalog catalog) { _catalog = catalog; _composer = new WeaponCardComposer(catalog); _locations = new WeaponLoadout(catalog); _data = catalog.Data(); }
        public JObject ProfileReceipt(JObject profile, JObject piece, JObject attributes, JObject profileOverrides = null)
        {
            var rule = (JObject)profile.DeepClone();
            var patch = profileOverrides?[(string)profile["id"]] as JObject;
            foreach (var entry in patch?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                if (!new[] { "baseValue", "scalingStat", "pointsPerTier", "rounding", "gainPerTier", "cap", "pointsOffset" }.Contains(entry.Name)) throw new ArgumentException("Unknown profile override: " + entry.Name);
                rule[entry.Name] = entry.Value.DeepClone();
            }
            var stat = (string)rule["scalingStat"];
            var points = Number(attributes[stat], stat); var perTier = Number(rule["pointsPerTier"], "pointsPerTier");
            if (perTier <= 0) throw new ArgumentException("pointsPerTier must be positive.");
            var offset = rule["pointsOffset"] == null ? 0 : Number(rule["pointsOffset"], "pointsOffset");
            var value = (points - offset) / perTier; double tier;
            switch ((string)rule["rounding"]) { case "floor": tier = Math.Floor(value); break; case "ceil": tier = Math.Ceiling(value); break; case "round": tier = Math.Floor(value + 0.5); break; default: throw new ArgumentException("Unknown profile rounding."); }
            var gain = Number(rule["gainPerTier"], "gainPerTier"); var baseValue = Number(rule["baseValue"], "baseValue");
            var rarity = (string)piece?["rarity"]; var role = (string)profile["role"];
            var bonus = rarity == null ? 0 : (double?)_data["balance"]["equipment"]["rarityBonuses"]?[rarity]?[role] ?? 0;
            var raw = baseValue + tier * gain + bonus;
            var cap = rule["cap"] == null || rule["cap"].Type == JTokenType.Null || rule["cap"].Type == JTokenType.String && (string)rule["cap"] == "" ? (double?)null : Number(rule["cap"], "cap");
            var amount = cap.HasValue ? Math.Min(cap.Value, raw) : raw;
            if (amount < 0 || double.IsInfinity(amount) || double.IsNaN(amount)) throw new ArgumentException("Resolved profile amount must be finite and nonnegative.");
            var receipt = new JObject { ["role"] = role, ["profileId"] = profile["id"].DeepClone(), ["pieceId"] = piece?["id"]?.DeepClone(), ["base"] = baseValue, ["rarityBonus"] = bonus, ["sourceStat"] = stat, ["points"] = points, ["pointsPerTier"] = perTier, ["rounding"] = rule["rounding"].DeepClone(), ["tier"] = tier, ["gainPerTier"] = gain, ["raw"] = raw, ["cap"] = cap, ["value"] = amount };
            receipt["rarity"] = rarity;
            if (offset != 0) receipt["pointsOffset"] = offset;
            return receipt;
        }
        private static double Number(JToken token, string name)
        { if (token == null || token.Type != JTokenType.Integer && token.Type != JTokenType.Float || double.IsInfinity((double)token) || double.IsNaN((double)token)) throw new ArgumentException("Expected finite number: " + name); return (double)token; }
        private static string Numeric(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        public JObject Resolve(JObject instance, JObject loadout, string classId, JObject attributes, JObject profileOverrides = null)
        {
            var card = _catalog.Record("cards", (string)instance["cardId"]); var role = (string)instance["equipmentRole"]; JObject profile = null, piece = null, receipt = null;
            if (role == "attack") { profile = _catalog.Record("equipment.basicCardProfiles", (string)instance["profileId"]); if (instance["weaponId"] != null) piece = _catalog.Record("equipment.armaments", (string)instance["weaponId"]); }
            else if (role == "guard" || role == "technique") { var source = _composer.RoleSource(loadout, classId, role); profile = (JObject)source["profile"]; piece = source["piece"] as JObject; card = _catalog.Record("cards", (string)profile["baseCardId"]); }
            card["tags"] = new JArray(_catalog.Tags("card", card));
            var mods = new List<string>();
            if (profile != null)
            {
                receipt = ProfileReceipt(profile, piece, attributes, profileOverrides);
                if (role == "attack" || role == "guard") mods.Add((role == "attack" ? "damage=" : "block=") + Numeric((double)receipt["value"]));
                mods.AddRange((profile["mods"] ?? new JArray()).Select(x => (string)x));
                card["damageSchool"] = profile["damageSchool"].DeepClone(); card["exposureBuildupPerHit"] = profile["exposureBuildupPerHit"].DeepClone();
                card["name"] = profile["displayName"].DeepClone(); card["icon"] = profile["icon"].DeepClone();
                if (!string.IsNullOrEmpty((string)profile["flavor"])) card["flavor"] = profile["flavor"].DeepClone();
                card["cardTags"] = new JArray(_catalog.Tags("basicCardProfile", profile));
                foreach (var effect in card["effects"].Where(x => (string)x["op"] == "damage")) effect["tags"] = card["cardTags"].DeepClone();
                card["equipmentProfileId"] = profile["id"].DeepClone(); card["equipmentRole"] = role;
            }
            else if ((bool?)instance["upgraded"] == true)
            {
                var upgrade = card["upgrade"] as JObject ?? throw new ArgumentException("Card has no upgrade definition.");
                foreach (var property in upgrade.Properties()) card[property.Name] = property.Value.DeepClone();
                card["name"] = upgrade["name"]?.DeepClone() ?? new JValue((string)card["name"] + "+"); card["upgraded"] = true;
            }
            foreach (var equipped in _locations.Pieces(loadout, classId).OfType<JObject>())
            {
                if (role == "attack" && _composer.Package(equipped) != null && (string)equipped["id"] != (string)instance["weaponId"]) continue;
                foreach (var raw in equipped["mods"] ?? new JArray())
                {
                    var parsed = Parse((string)raw); if (parsed == null) throw new ArgumentException("Malformed equipment modifier: " + raw);
                    var spec = _data["equipment"]["modFields"][(string)parsed["field"]]; if ((string)spec?["scope"] != "card") continue;
                    var targets = _data["equipment"]["targets"];
                    var target = targets.FirstOrDefault(x => (string)x["target"] == (string)parsed["prefix"] && (string)x["classId"] == classId) ?? targets.FirstOrDefault(x => (string)x["target"] == (string)parsed["prefix"] && (string)x["classId"] == "*");
                    if ((string)target?["cardId"] == (string)card["id"]) mods.Add(((string)raw).Substring(((string)raw).IndexOf('.') + 1));
                }
            }
            card = ApplyModifiers(card, mods);
            var level = (int?)instance["smithingLevel"] ?? 0;
            if (level < 0) throw new ArgumentException("Smithing level cannot be negative.");
            if (level > 0)
            {
                var itemId = (string)instance["sourceArmamentId"] ?? (string)piece?["id"];
                if (string.IsNullOrEmpty(itemId)) throw new ArgumentException("Smithed card requires its source armament.");
                card = new ItemUpgradeService(_catalog).ResolveCard(card, role, "armament/" + itemId, level);
            }
            return new JObject { ["instanceId"] = instance["instanceId"]?.DeepClone(), ["card"] = card, ["profileReceipt"] = receipt, ["modifiers"] = new JArray(mods), ["sourceHand"] = instance["sourceHand"]?.DeepClone(), ["weaponId"] = piece?["id"]?.DeepClone() };
        }
        public static JObject Parse(string text)
        {
            var match = Regex.Match(text.Trim(), @"^([A-Za-z]\w*)\.([A-Za-z]\w*)=([+-]?)(\d+(?:\.\d+)?)$"); if (!match.Success) return null;
            return new JObject { ["prefix"] = match.Groups[1].Value, ["field"] = match.Groups[2].Value, ["mode"] = match.Groups[3].Value == "" ? "set" : "add", ["value"] = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * (match.Groups[3].Value == "-" ? -1 : 1) };
        }
        public JObject ApplyModifiers(JObject definition, IEnumerable<string> modifiers)
        {
            var card = (JObject)definition.DeepClone();
            if (!(card["effects"] is JArray)) card["effects"] = new JArray();
            var effects = (JArray)card["effects"];
            var list = modifiers.ToArray(); var touched = new List<JToken>(); var limits = _data["balance"]["equipment"]["limits"];
            foreach (var raw in list)
            {
                var mod = Parse("x." + raw) ?? throw new ArgumentException("Malformed card modifier: " + raw);
                var spec = _data["equipment"]["modFields"][(string)mod["field"]] ?? throw new ArgumentException("Unknown card modifier: " + raw); touched.Add(spec);
                var amount = (double)mod["value"];
                double Adjust(JToken current, double min) => Math.Max(min, (string)mod["mode"] == "set" ? amount : (current?.Type == JTokenType.Integer || current?.Type == JTokenType.Float ? (double)current : 0) + amount);
                var op = (string)spec["op"]; var effect = effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == op);
                switch ((string)spec["apply"])
                {
                    case "cost": card["cost"] = Adjust(card["cost"], (double?)limits["minCost"] ?? 0); break;
                    case "scale": foreach (var e in effects) foreach (var field in new[] { "amount", "stacks" }) if (e[field]?.Type == JTokenType.Integer || e[field]?.Type == JTokenType.Float) e[field] = Math.Max(0, (double)e[field] + amount); break;
                    case "amount":
                        if (effect == null) { if (amount > 0) effects.Add(new JObject { ["op"] = op, ["target"] = spec["effTarget"].DeepClone(), ["amount"] = amount }); }
                        else if (effect["amount"]?.Type == JTokenType.Integer || effect["amount"]?.Type == JTokenType.Float) effect["amount"] = Adjust(effect["amount"], (double?)limits[op == "damage" ? "minDamage" : op == "block" ? "minBlock" : "_"] ?? 0); break;
                    case "hits": if (effect != null) effect["hits"] = Math.Min((double?)limits["maxHits"] ?? 99, Adjust(effect["hits"] ?? new JValue(1), 1)); break;
                    case "status":
                        effect = effects.OfType<JObject>().FirstOrDefault(x => (string)x["op"] == "applyStatus" && (string)x["status"] == (string)spec["status"]);
                        if (effect == null) { if (amount > 0) effects.Add(new JObject { ["op"] = "applyStatus", ["target"] = spec["effTarget"].DeepClone(), ["status"] = spec["status"].DeepClone(), ["stacks"] = amount }); }
                        else if (effect["stacks"]?.Type == JTokenType.Integer || effect["stacks"]?.Type == JTokenType.Float) effect["stacks"] = Adjust(effect["stacks"], 0); break;
                    default: throw new ArgumentException("Unknown modifier application: " + spec["apply"]);
                }
            }
            var template = (string)card["textTemplate"] ?? ""; var tokens = new HashSet<string>(Regex.Matches(template, @"\{([^}:]+)").Cast<Match>().Select(x => x.Groups[1].Value));
            foreach (var spec in touched) { var clause = (string)spec["clause"]; if (string.IsNullOrEmpty(clause)) continue; var token = Regex.Match(clause, @"\{([^}:]+)"); if (token.Success && tokens.Add(token.Groups[1].Value)) template = (template + " " + clause).Trim(); }
            if (list.Length > 0) { card["textTemplate"] = template; card["equipMods"] = new JArray(list); }
            return card;
        }
    }
}
