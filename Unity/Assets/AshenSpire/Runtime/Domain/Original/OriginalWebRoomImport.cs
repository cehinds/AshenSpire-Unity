// OriginalWebRoomImport.cs — converts the original game's in-room run state into the
// native room a restored OriginalRunSession continues from. Called only by
// OriginalWebSaveImport on a draft run copy; it never writes storage.
// Converts: pendingReward (offer, taken/skipped rows; cinders are granted on arrival
// exactly as the original reward screen does on resume), shopStock (remaining stock,
// prices, removal price and the merchant's smith) and the fight-entry receipt the
// original rebuilds a fight from. Anything else in a room is refused by name.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public static class OriginalWebRoomImport
    {
        private static readonly string[] RewardKinds = { "cinders", "card", "flask", "armament", "relic" };
        private static ArgumentException Refuse(string message) => new ArgumentException(message + " Your original save is unchanged.");
        private static void Only(JObject value, string label, params string[] names)
        {
            var unknown = value.Properties().FirstOrDefault(p => !names.Contains(p.Name));
            if (unknown != null) throw Refuse("This save's " + label + " contains unsupported original-game state: " + unknown.Name + ".");
        }
        private static int Whole(JToken value, string label)
        {
            if (value?.Type != JTokenType.Integer || (long)value < 0 || (long)value > int.MaxValue) throw Refuse("Malformed original " + label + ".");
            return (int)value;
        }
        private static string Id(JToken value, string label)
        {
            if (value?.Type != JTokenType.String || string.IsNullOrEmpty((string)value)) throw Refuse("Malformed original " + label + ".");
            return (string)value;
        }
        // The combat pool the original assigns to the current node (main.js enterNode/startFight).
        private static string NodePool(JObject run)
        {
            var nodeId = (string)run["mapNodeId"];
            var node = nodeId == null ? null : run["mapGraph"]?["nodes"]?[nodeId] as JObject;
            if (node == null) throw Refuse("This room is not on the saved route.");
            var kind = (string)node["type"];
            if (kind == "event") kind = (string)node["resolved"]?["kind"];
            switch (kind)
            {
                case "monster": case "fight": return OriginalCustomRunRules.Enabled(run, "allElite") ? "elite" : "normal";
                case "elite": case "boss": return kind;
                default: return kind ?? "unknown";
            }
        }
        internal static void Apply(JObject source, JObject run, OriginalContentCatalog catalog)
        {
            if (run["custom"]?["mapShape"] is JObject shape && shape.HasValues)
                throw Refuse("Custom map-shape climbs cannot be imported yet; the other Custom Climb rules can.");
            if (source["pendingReward"] is JObject reward) Rewards(reward, source, run, catalog);
            else if (source["shopStock"] is JObject stock) Shop(stock, run, catalog);
            else if (source["combatEntered"] is JObject fight) FightEntry(fight, run, catalog);
            else foreach (var key in new[] { "pendingReward", "shopStock", "combatEntered" })
                if (source[key] != null && source[key].Type != JTokenType.Null) throw Refuse("Malformed original " + key + ".");
        }
        private static void Rewards(JObject pending, JObject source, JObject run, OriginalContentCatalog catalog)
        {
            Only(pending, "reward room", "schemaVersion", "source", "after", "rewards", "states", "chosenCardId");
            if (pending["schemaVersion"]?.Type != JTokenType.Integer || (int)pending["schemaVersion"] != 1) throw Refuse("This reward room uses an unsupported original format.");
            var pool = Id(pending["source"], "reward source");
            if (!new[] { "normal", "elite", "boss" }.Contains(pool) || pool != NodePool(run)) throw Refuse("This reward room does not match the fight on its map node.");
            var after = (string)pending["after"];
            if (after != (pool == "boss" ? "advanceAct" : "map")) throw Refuse("This reward room has an unsupported continuation.");
            if (pool == "boss" && (int)run["actNumber"] >= 3 && !OriginalCustomRunRules.Enabled(run, "endless")) throw Refuse("A final-boss reward cannot continue a climb.");
            if (!(pending["rewards"] is JObject offer) || !(pending["states"] is JObject states)) throw Refuse("Malformed original reward room.");
            Only(offer, "reward offer", "title", "cinders", "cardIds", "flaskId", "relicId", "armamentId", "smithingStoneReceipt");
            if (offer["title"] != null && offer["title"].Type != JTokenType.String) throw Refuse("Malformed original reward title.");
            var rewards = new JObject { ["cinders"] = offer["cinders"] == null ? 0 : Whole(offer["cinders"], "reward cinders") };
            if (!(offer["cardIds"] is JArray cards) || cards.Count > 10 || cards.Any(c => c.Type != JTokenType.String) || cards.Values<string>().Distinct().Count() != cards.Count) throw Refuse("Malformed original card offer.");
            foreach (var id in cards.Values<string>()) _ = catalog.Record("cards", id);
            rewards["cardIds"] = cards.DeepClone();
            foreach (var (kind, table) in new[] { ("flask", "flasks"), ("relic", "relics"), ("armament", "equipment.armaments") })
            {
                var value = offer[kind + "Id"];
                if (value == null) continue;
                if (value.Type == JTokenType.Null) { rewards[kind + "Id"] = null; continue; }
                _ = catalog.Record(table, Id(value, kind + " reward"));
                rewards[kind + "Id"] = value.DeepClone();
            }
            if (pool == "boss" && rewards["flaskId"] != null && rewards["flaskId"].Type != JTokenType.Null) throw Refuse("A boss reward never offers a flask.");
            var stones = offer["smithingStoneReceipt"] as JObject;
            if (offer["smithingStoneReceipt"] != null && stones == null) throw Refuse("Malformed original Smithing Stone receipt.");
            var stoneAmount = stones == null ? 0 : Whole(stones["amount"], "Smithing Stone receipt");
            if (stoneAmount > 0 && !((source["smithingRewardClaims"] as JArray) ?? new JArray()).Values<string>().Contains((string)stones["rewardId"]))
                throw Refuse("This reward's Smithing Stones were not claimed by the run.");
            var converted = new JObject();
            foreach (var state in states.Properties())
            {
                var value = state.Value.Type == JTokenType.String ? (string)state.Value : null;
                if (value != "taken" && value != "skipped") throw Refuse("Malformed original reward state: " + state.Name + ".");
                if (state.Name == "smithingStone") { if (value != "taken" || stoneAmount <= 0) throw Refuse("Malformed original Smithing Stone reward state."); continue; }
                if (!RewardKinds.Contains(state.Name)) throw Refuse("Unsupported original reward kind: " + state.Name + ".");
                converted[state.Name] = value;
            }
            if (stoneAmount > 0 && (string)states["smithingStone"] != "taken") throw Refuse("Malformed original Smithing Stone reward state.");
            var chosen = pending["chosenCardId"];
            var chosenId = chosen == null || chosen.Type == JTokenType.Null ? null : Id(chosen, "chosen reward card");
            if ((chosenId != null) != ((string)converted["card"] == "taken") || chosenId != null && !cards.Values<string>().Contains(chosenId)) throw Refuse("The chosen reward card does not match the card offer.");
            // reward.js mountRewards grants pending cinders as the screen opens; a resumed
            // original save therefore never shows them pending. Do the same once, here.
            if (converted["cinders"] == null && (int)rewards["cinders"] > 0)
            {
                run["cinders"] = checked((int)run["cinders"] + (int)rewards["cinders"]);
                converted["cinders"] = "taken";
            }
            run["room"] = new JObject { ["rewards"] = rewards, ["states"] = converted, ["source"] = pool, ["after"] = after };
            run["phase"] = OriginalRunPhase.Rewards.ToString();
        }
        private static void Shop(JObject stock, JObject run, OriginalContentCatalog catalog)
        {
            Only(stock, "merchant", "cards", "relics", "flasks", "removeCost", "smith");
            if (NodePool(run) != "merchant") throw Refuse("This merchant stock is not on a merchant node.");
            var room = new JObject();
            var balance = catalog.Data()["balance"];
            foreach (var (plural, table, stockKey) in new[] { ("cards", "cards", "cardStock"), ("relics", "relics", "relicStock"), ("flasks", "flasks", "flaskStock") })
            {
                if (!(stock[plural] is JArray rows) || rows.Count > (int)balance["shop"][stockKey]) throw Refuse("Malformed original merchant " + plural + ".");
                var items = new JArray();
                foreach (var row in rows)
                {
                    if (!(row is JObject item)) throw Refuse("Malformed original merchant " + plural + ".");
                    Only(item, "merchant item", "id", "cost");
                    _ = catalog.Record(table, Id(item["id"], "merchant item"));
                    items.Add(new JObject { ["id"] = item["id"].DeepClone(), ["cost"] = Whole(item["cost"], "merchant price") });
                }
                room[plural] = items;
            }
            room["removeCost"] = Whole(stock["removeCost"], "card removal price");
            if (stock["smith"] != null)
            {
                if (!(stock["smith"] is JObject smith)) throw Refuse("Malformed original merchant smith.");
                Only(smith, "merchant smith", "nodeKind", "offered", "rolled", "chance", "services");
                var authored = balance["smithing"]["services"]["offeredAt"]["merchant"]; var chance = (int?)authored?["chance"] ?? 0;
                var offered = smith["offered"]?.Type == JTokenType.Boolean && (bool)smith["offered"];
                var expected = new JObject { ["nodeKind"] = "merchant", ["offered"] = offered, ["rolled"] = chance > 0 && chance < 100, ["chance"] = chance, ["services"] = offered ? authored["services"].DeepClone() : new JArray() };
                if (!JToken.DeepEquals(smith, expected) || offered && chance <= 0 || !offered && chance >= 100) throw Refuse("This merchant's smith does not match supported original rules.");
                room["smith"] = expected;
            }
            run["room"] = room;
            run["phase"] = OriginalRunPhase.Shop.ToString();
        }
        private static void FightEntry(JObject fight, JObject run, OriginalContentCatalog catalog)
        {
            Only(fight, "fight", "nodeId", "encounterId");
            if (Id(fight["nodeId"], "fight node") != (string)run["mapNodeId"]) throw Refuse("This fight is not on the saved route.");
            var encounter = catalog.Record("encounters", Id(fight["encounterId"], "fight encounter"));
            var pool = (string)encounter["pool"];
            var act = new OriginalCustomRunRules(catalog.Data()).ContentAct(run);
            if (pool != NodePool(run) || ((int?)encounter["act"] ?? 1) != act) throw Refuse("This fight does not match its map node.");
            // The original rebuilds the fight from this receipt on resume (main.js resumeRun);
            // the native session builds the same opening from the same saved streams.
            run["room"] = new JObject { ["encounterId"] = encounter["id"].DeepClone(), ["pool"] = pool, ["nodeId"] = run["mapNodeId"].DeepClone() };
            run["phase"] = OriginalRunPhase.Combat.ToString();
        }
        /// <summary>Player-facing name of where an imported snapshot resumes, for the import preview.</summary>
        public static string Resumes(JObject snapshot)
        {
            switch ((string)snapshot?["run"]?["phase"])
            {
                case "Rewards": return "Resumes at the fight's rewards";
                case "Shop": return "Resumes at the merchant";
                case "Combat": return "Resumes at the start of the fight";
                default: return "Resumes on the map";
            }
        }
        // A converted run must be able to go on: a route onward, or the act it advances to.
        internal static bool Continues(OriginalGameSession session)
        {
            if (session.Phase == OriginalRunPhase.Map) return session.LegalNodeIds.Length > 0;
            if (session.Phase == OriginalRunPhase.Rewards && (string)session.Room["after"] == "advanceAct") return true;
            var player = session.RunPlayer; var node = (string)player["mapNodeId"];
            return node != null && player["mapGraph"]["nodes"][node]["next"] is JArray next && next.Count > 0
                || session.Phase == OriginalRunPhase.Combat && (string)session.Room["pool"] == "boss";
        }
    }
}
