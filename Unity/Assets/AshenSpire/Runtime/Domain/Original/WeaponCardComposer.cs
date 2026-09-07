// WeaponCardComposer.cs — equipment owns the cards it lends; the run owns its
// stable basic slots and earned cards. Authored rows live in Original/content.json.
// CreateStartingDeck determines the basic quota ONCE. Recompose preserves it when
// gear changes, removes departed item grants, and retains earned/upgraded cards.
// Call ReconcileCombat across ALL four piles; missing mid-fight grants enter discard.
// Smith mount overrides and item upgrade layers are separate services, not implicit
// parameters here. Never use this baseline composer to silently discard their state.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class WeaponCardComposer
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly WeaponLoadout _locations;
        private readonly JObject _data;
        public WeaponCardComposer(OriginalContentCatalog catalog) { _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); _locations = new WeaponLoadout(catalog); _data = catalog.Data(); }
        private JToken Settings => _data["balance"]["equipment"]["startingDeck"];
        private string Source(string role) => (string)Settings?["sources"]?[role] ?? throw new ArgumentException("Missing grant source: " + role);
        private JObject Profile(string id) => _catalog.Record("equipment.basicCardProfiles", id);
        private JObject Hand(JObject loadout, string classId, string hand)
        {
            var slot = _catalog.Table("equipment.slots").FirstOrDefault(x => (string)x["hand"] == hand) ?? throw new ArgumentException("No slot for hand: " + hand);
            return _locations.Equipped(loadout, classId, (string)slot["id"]);
        }
        public JObject RoleSource(JObject loadout, string classId, string role)
        {
            foreach (var source in _data["balance"]["equipment"]["roleSources"]?[role] ?? new JArray())
            {
                var piece = _locations.Equipped(loadout, classId, (string)source["slot"]);
                if (piece == null || source["kinds"] != null && !source["kinds"].Any(x => (string)x == (string)piece["kind"])) continue;
                var profileId = (string)piece[role + "Profile"];
                if (!string.IsNullOrEmpty(profileId)) return new JObject { ["role"] = role, ["slotId"] = source["slot"].DeepClone(), ["piece"] = piece, ["profile"] = Profile(profileId) };
            }
            return new JObject { ["role"] = role, ["slotId"] = null, ["piece"] = null, ["profile"] = Profile((string)_data["balance"]["equipment"]["unarmedProfiles"][role]) };
        }
        public JObject Package(JObject piece)
        {
            if (piece == null) return null;
            var raw = piece["weaponCardPackage"];
            if ((raw == null || raw.Type == JTokenType.Null) && string.IsNullOrEmpty((string)piece["attackProfile"])) return null;
            if (raw != null && raw.Type != JTokenType.Null && !(raw is JObject)) throw new ArgumentException("Weapon package must be an object.");
            var explicitPackage = raw as JObject;
            if (explicitPackage != null && (string)explicitPackage["compatibility"] != "attack-v1") throw new ArgumentException("Incompatible weapon package.");
            var hands = (int?)(explicitPackage?["handsRequired"] ?? piece["handsRequired"]) ?? 1;
            if (hands != 1 && hands != 2) throw new ArgumentException("handsRequired must be 1 or 2.");
            var filler = Profile((string)(explicitPackage?["fillerAttackProfileId"] ?? piece["attackProfile"]));
            void AttackProfile(JObject profile) { if ((string)profile["role"] != "attack" || (string)profile["compatibility"] != "attack-v1") throw new ArgumentException("Incompatible attack profile."); _catalog.Record("cards", (string)profile["baseCardId"]); }
            AttackProfile(filler);
            var priorities = new JArray(); var grants = new JArray(); var arts = new JArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in ReadList(explicitPackage, "priorityAttackRefs"))
            {
                var card = entry.Type == JTokenType.String ? (string)entry : (string)entry["cardId"];
                var profileId = entry.Type == JTokenType.String ? (string)filler["id"] : (string)entry["profileId"] ?? (string)filler["id"];
                _catalog.Record("cards", card); AttackProfile(Profile(profileId));
                if (!seen.Add(card + "|" + profileId)) throw new ArgumentException("Duplicate priority card.");
                priorities.Add(new JObject { ["cardId"] = card, ["profileId"] = profileId });
            }
            seen.Clear();
            void Trackable(string cardId)
            {
                var card = _catalog.Record("cards", cardId);
                if ((string)card["type"] == "power" && !(card["keywords"] ?? new JArray()).Any(x => (string)x == "exhaust")) throw new ArgumentException("Item grants must remain in a tracked pile: " + cardId);
            }
            foreach (var entry in ReadList(explicitPackage, "grantedCards"))
            {
                var card = entry.Type == JTokenType.String ? (string)entry : (string)entry["cardId"];
                var count = entry.Type == JTokenType.String ? 1 : (int?)entry["count"] ?? 1;
                if (count < 1 || !seen.Add(card)) throw new ArgumentException("Invalid or duplicate granted card.");
                Trackable(card); grants.Add(new JObject { ["cardId"] = card, ["count"] = count });
            }
            seen.Clear();
            foreach (var entry in ReadList(explicitPackage, "weaponArtDefaults"))
            {
                if (entry.Type != JTokenType.String || !seen.Add((string)entry)) throw new ArgumentException("Invalid or duplicate weapon art.");
                Trackable((string)entry); arts.Add(entry.DeepClone());
            }
            return new JObject { ["weaponId"] = piece["id"].DeepClone(), ["handsRequired"] = hands, ["priorityAttackRefs"] = priorities, ["grantedCards"] = grants, ["weaponArtDefaults"] = arts, ["fillerAttackProfileId"] = filler["id"].DeepClone(), ["compatibility"] = "attack-v1" };
        }
        private static JArray ReadList(JObject obj, string key) => obj?[key] == null || obj[key].Type == JTokenType.Null ? new JArray() : obj[key] as JArray ?? throw new ArgumentException(key + " must be an array.");
        public JObject BuildAttackPlan(JObject loadout, string classId, int? attackSlotCount = null)
        {
            var count = attackSlotCount ?? (classId != null && (bool?)Settings?["enabled"] == true ? (int)StartingPlan(loadout, classId)["attackCount"] : (int)_data["balance"]["equipment"]["roleCopies"]["attack"]);
            if (count < 0) throw new ArgumentException("Attack quota cannot be negative.");
            var right = Hand(loadout, classId, "right"); var left = Hand(loadout, classId, "left");
            if (right != null && left != null && (string)right["id"] == (string)left["id"]) throw new ArgumentException("Duplicate equipped armament has no distinct identity.");
            var rp = Package(right); var lp = Package(left);
            if (rp != null && (int)rp["handsRequired"] == 2 && left != null || lp != null && (int)lp["handsRequired"] == 2 && right != null) throw new ArgumentException("Two-handed weapon conflicts with occupied offhand.");
            var slots = new JArray();
            void Add(JObject package, string hand, int quota)
            {
                var profile = Profile(package == null ? (string)_data["balance"]["equipment"]["unarmedProfiles"]["attack"] : (string)package["fillerAttackProfileId"]);
                for (var n = 0; n < quota; n++)
                {
                    var priority = package?["priorityAttackRefs"] as JArray;
                    var entry = priority != null && n < priority.Count ? priority[n] : null;
                    slots.Add(new JObject { ["equipmentAttackSlotId"] = "attack:" + slots.Count, ["sourceHand"] = hand, ["weaponId"] = package?["weaponId"]?.DeepClone(), ["cardId"] = entry?["cardId"]?.DeepClone() ?? profile["baseCardId"].DeepClone(), ["profileId"] = entry?["profileId"]?.DeepClone() ?? profile["id"].DeepClone() });
                }
            }
            if (rp != null && lp != null)
            {
                if ((string)right["kind"] == "shield" && (string)left["kind"] != "shield") Add(lp, "left", count);
                else if ((string)left["kind"] == "shield" && (string)right["kind"] != "shield") Add(rp, "right", count);
                else { Add(rp, "right", (count + 1) / 2); Add(lp, "left", count / 2); }
            }
            else if (rp != null) Add(rp, "right", count);
            else if (lp != null) Add(lp, "left", count);
            else Add(null, null, count);
            var fingerprint = string.Join("|", slots.Select(x => string.Join(":", new[] { (string)x["equipmentAttackSlotId"], (string)x["sourceHand"] ?? "-", (string)x["weaponId"] ?? "-", (string)x["cardId"], (string)x["profileId"] })));
            return new JObject { ["attackSlotCount"] = count, ["fingerprint"] = fingerprint, ["slots"] = slots };
        }
        public JArray DesiredGrants(JObject loadout, string classId, JObject itemMounts = null)
        {
            var desired = new JArray();
            void Mint(string instanceId, string cardId, string role, string owner, string source)
            { _catalog.Record("cards", cardId); desired.Add(new JObject { ["instanceId"] = instanceId, ["cardId"] = cardId, ["upgraded"] = false, ["equipmentRole"] = role, ["grantedBy"] = owner, ["grantSource"] = source }); }
            foreach (var piece in _locations.Pieces(loadout, classId).OfType<JObject>())
            {
                if (!(piece["tags"] ?? new JArray()).Any(x => (string)x == "bound")) continue;
                var family = (string)piece["kind"] == "armor" ? "armour" : "armament";
                var scope = family == "armour" ? classId : "";
                var grant = _catalog.Table("equipment.equipmentGrants").FirstOrDefault(x => (string)x["sourceId"] == (string)piece["id"] && (string.IsNullOrEmpty((string)x["family"]) || (string)x["family"] == family) && ((string)x["scope"] ?? "") == scope);
                var counts = new Dictionary<string, int>();
                foreach (var card in grant?["cards"] ?? new JArray())
                { var id = (string)card; counts.TryGetValue(id, out var n); counts[id] = n + 1; var owner = WeaponLoadout.ItemRef(piece); Mint("bound:" + owner + ":" + id + ":" + n, id, "granted", owner, Source(family == "armour" ? "armor" : "weapon")); }
            }
            var right = Hand(loadout, classId, "right"); var left = Hand(loadout, classId, "left");
            var packages = new Dictionary<string, JObject> { ["right"] = Package(right), ["left"] = Package(left) };
            foreach (var hand in new[] { "right", "left" })
            {
                var package = packages[hand]; if (package == null) continue;
                foreach (var grant in package["grantedCards"]) for (var n = 0; n < (int)grant["count"]; n++) Mint("granted:" + (string)package["weaponId"] + ":" + (string)grant["cardId"] + ":" + n, (string)grant["cardId"], "granted", (string)package["weaponId"], Source("weapon"));
            }
            var total = packages.Values.Where(x => x != null).Sum(x => ((JArray)x["weaponArtDefaults"]).Count);
            var artsSeen = new HashSet<string>();
            foreach (var hand in new[] { "right", "left" })
            {
                var package = packages[hand]; if (package == null) continue;
                var quota = packages.Values.All(x => x != null) ? (hand == "right" ? (total + 1) / 2 : total / 2) : total;
                var taken = 0;
                foreach (var art in package["weaponArtDefaults"])
                {
                    if (taken >= quota) break; if (!artsSeen.Add((string)art)) continue; taken++;
                    Mint("weaponArt:" + (string)package["weaponId"] + ":" + (string)art, (string)art, "weaponArt", (string)package["weaponId"], Source("weapon"));
                }
            }
            var mounts = new CardMountService(_catalog);
            desired = mounts.ApplyOverrides(desired, itemMounts);
            if ((right == null) != (left == null) && !packages.Values.Any(x => x != null && (int)x["handsRequired"] == 2))
            {
                var empty = right == null ? "right" : "left";
                var cardId = (string)Profile((string)_data["balance"]["equipment"]["unarmedProfiles"]["technique"])["baseCardId"];
                if (!desired.Any(x => (string)x["equipmentRole"] == "weaponArt" && (string)x["cardId"] == cardId)) Mint("weaponArt:unarmed:" + empty + ":" + cardId, cardId, "weaponArt", "unarmed:" + empty, Source("weapon"));
            }
            foreach (var piece in _locations.Pieces(loadout, classId).OfType<JObject>()) foreach (var extra in mounts.ExtraInstances(WeaponLoadout.ItemRef(piece), itemMounts, Source((string)piece["kind"] == "armor" ? "armor" : "weapon"))) desired.Add(extra.DeepClone());
            return desired;
        }
        public JObject StartingPlan(JObject loadout, string classId)
        {
            var hero = _catalog.Record("classes", classId); var grants = new JArray();
            var technique = RoleSource(loadout, classId, "technique");
            grants.Add(new JObject { ["source"] = Source("weapon"), ["cardId"] = technique["profile"]["baseCardId"].DeepClone(), ["equipmentRole"] = "technique", ["profileId"] = technique["profile"]["id"].DeepClone() });
            foreach (var card in Settings?["global"]?["grants"] ?? new JArray()) grants.Add(new JObject { ["source"] = Source("global"), ["cardId"] = card.DeepClone() });
            if (!string.IsNullOrEmpty((string)hero["startingSignatureCard"])) grants.Add(new JObject { ["source"] = Source("class"), ["cardId"] = hero["startingSignatureCard"].DeepClone() });
            grants = Order(grants, "source");
            var cap = (int)_data["balance"]["startingDeckSize"]; var packageCount = DesiredGrants(loadout, classId).Count;
            var filler = Math.Max(0, cap - grants.Count - packageCount);
            var bias = (double?)(Settings?["classes"]?[classId]?["strikeBias"] ?? Settings?["defaultStrikeBias"]) ?? 0.5;
            if (bias < 0 || bias > 1 || double.IsNaN(bias)) throw new ArgumentException("Strike bias must be between zero and one.");
            var odd = (string)Settings?["oddFillerGoesTo"] != "guard";
            var attacks = Math.Min(filler, Math.Max(0, (int)(odd ? Math.Floor(filler * bias + 0.5) : Math.Ceiling(filler * bias - 0.5))));
            return new JObject { ["size"] = grants.Count + packageCount + filler, ["grants"] = grants, ["filler"] = filler, ["attackCount"] = attacks, ["guardCount"] = filler - attacks, ["bias"] = bias, ["oddGoesToAttack"] = odd, ["cap"] = cap, ["packageCards"] = packageCount };
        }
        private JArray Order(JArray cards, string field)
        {
            var order = (Settings?["sourceOrder"] ?? new JArray()).Select(x => (string)x).ToList();
            return new JArray(cards.OrderBy(x => { var index = order.IndexOf((string)x[field]); return index < 0 ? order.Count : index; }).Select(x => x.DeepClone()));
        }
        public JArray CreateStartingDeck(JObject loadout, string classId)
        {
            var plan = StartingPlan(loadout, classId); var attack = BuildAttackPlan(loadout, classId, (int)plan["attackCount"]); var deck = new JArray();
            foreach (var slot in attack["slots"].OfType<JObject>())
            {
                var card = (JObject)slot.DeepClone(); card["equipmentRole"] = "attack"; card["equipmentPlanFingerprint"] = attack["fingerprint"].DeepClone();
                foreach (var field in new[] { "weaponId", "sourceHand" }) if (string.IsNullOrEmpty((string)card[field])) card.Remove(field);
                deck.Add(card);
            }
            var guard = RoleSource(loadout, classId, "guard")["profile"];
            for (var n = 0; n < (int)plan["guardCount"]; n++) deck.Add(new JObject { ["cardId"] = guard["baseCardId"].DeepClone(), ["equipmentRole"] = "guard", ["profileId"] = guard["id"].DeepClone() });
            foreach (var grant in plan["grants"].OfType<JObject>()) { var card = (JObject)grant.DeepClone(); card["grantSource"] = card["source"].DeepClone(); card.Remove("source"); deck.Add(card); }
            for (var i = 0; i < deck.Count; i++) { deck[i]["instanceId"] = "starting:" + i; deck[i]["upgraded"] = false; }
            foreach (var card in DesiredGrants(loadout, classId)) deck.Add(card.DeepClone());
            return Order(deck, "grantSource");
        }
        public JArray Recompose(JArray deck, JObject loadout, string classId, JObject itemMounts = null)
        {
            var result = (JArray)deck.DeepClone();
            ApplyAttackPlan(BuildAttackPlan(loadout, classId, result.Count(x => (string)x["equipmentRole"] == "attack")), result);
            foreach (var card in result.Where(x => new[] { "guard", "technique" }.Contains((string)x["equipmentRole"])))
            { var profile = RoleSource(loadout, classId, (string)card["equipmentRole"])["profile"]; card["cardId"] = profile["baseCardId"].DeepClone(); card["profileId"] = profile["id"].DeepClone(); }
            var desired = DesiredGrants(loadout, classId, itemMounts); var seen = new HashSet<string>();
            ReconcilePile(result, desired, seen);
            foreach (var card in desired) if (seen.Add((string)card["instanceId"])) result.Add(card.DeepClone());
            return result;
        }
        public JObject ReconcileCombat(JObject piles, JObject loadout, string classId, int attackSlotCount, JObject itemMounts = null)
        {
            var result = (JObject)piles.DeepClone(); var desired = DesiredGrants(loadout, classId, itemMounts); var seen = new HashSet<string>();
            var plan = BuildAttackPlan(loadout, classId, attackSlotCount); var allIds = new HashSet<string>();
            foreach (var name in new[] { "hand", "draw", "discard", "exhaust" })
            {
                var pile = result[name] as JArray ?? throw new ArgumentException("Missing combat pile: " + name);
                foreach (var card in pile) if (!allIds.Add((string)card["instanceId"])) throw new ArgumentException("Card instance appears in multiple piles.");
                ApplyAttackPlan(plan, pile, true); ReconcilePile(pile, desired, seen);
                foreach (var card in pile.Where(x => new[] { "guard", "technique" }.Contains((string)x["equipmentRole"])))
                { var profile = RoleSource(loadout, classId, (string)card["equipmentRole"])["profile"]; card["cardId"] = profile["baseCardId"].DeepClone(); card["profileId"] = profile["id"].DeepClone(); }
            }
            foreach (var card in desired) if (seen.Add((string)card["instanceId"])) ((JArray)result["discard"]).Add(card.DeepClone());
            return result;
        }
        private static void ReconcilePile(JArray pile, JArray desired, HashSet<string> seen)
        {
            foreach (var card in pile.OfType<JObject>().ToArray())
            {
                if (!new[] { "granted", "weaponArt" }.Contains((string)card["equipmentRole"])) continue;
                var wanted = desired.FirstOrDefault(x => (string)x["instanceId"] == (string)card["instanceId"]);
                if (wanted == null) { card.Remove(); continue; }
                if (!seen.Add((string)card["instanceId"])) throw new ArgumentException("Duplicate item-owned card instance.");
                if ((string)card["cardId"] != (string)wanted["cardId"] || ((bool?)card["upgraded"] == true) != ((bool?)wanted["upgraded"] == true)) card.Replace(wanted.DeepClone());
            }
        }
        public static void ApplyAttackPlan(JObject plan, JArray cards, bool allowSubset = false)
        {
            var attacks = cards.OfType<JObject>().Where(x => (string)x["equipmentRole"] == "attack").ToArray();
            if (!allowSubset && attacks.Length != (int)plan["attackSlotCount"]) throw new ArgumentException("Attack quota differs from deck.");
            if (attacks.Any(x => x["equipmentAttackSlotId"] == null))
            { if (allowSubset) throw new ArgumentException("Legacy slots require the full authoritative deck."); for (var n = 0; n < attacks.Length; n++) attacks[n]["equipmentAttackSlotId"] = "attack:" + n; }
            var seen = new HashSet<string>();
            foreach (var card in attacks)
            {
                var id = (string)card["equipmentAttackSlotId"];
                if (!seen.Add(id)) throw new ArgumentException("Duplicate attack slot: " + id);
                var slot = plan["slots"].FirstOrDefault(x => (string)x["equipmentAttackSlotId"] == id) ?? throw new ArgumentException("Unknown attack slot: " + id);
                foreach (var field in new[] { "cardId", "profileId", "sourceHand", "weaponId" })
                { if (string.IsNullOrEmpty((string)slot[field])) card.Remove(field); else card[field] = slot[field].DeepClone(); }
                card["equipmentPlanFingerprint"] = plan["fingerprint"].DeepClone();
            }
        }
    }
}
