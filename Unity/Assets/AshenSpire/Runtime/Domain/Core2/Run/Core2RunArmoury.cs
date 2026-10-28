// Core2RunArmoury.cs — the Core 2 Armoury's rules (port plan phase 9), engine-free: what each position holds (Inspect),
// what may replace it, Replace and Unequip with their refusals, carried storage, the combat lock and the new-item flags.
//
// JS at the pin:
//   src/model/armouryRules.js     noncombatEditing (Core: cards.editorWhere "noncombat"), editingLocked (any unresolved
//                                 fight), newArmouryItems / markArmourySeen / armouryHasNew (run.armorySeen: carried ids
//                                 and armour:<class>/<id> refs already looked at, with the @armour mark), the deck doors
//   src/model/loadout.js          ownership (creation armour, bought armour, unlocks, found = carried in a perRun profile),
//                                 fitsSlot, gripRefusal (a two-hander beside an occupied hand), equipmentRequirementReceipt,
//                                 canEquip, equipTransitionPlan / applyEquipTransition (the replaced armament goes to
//                                 storage, refused when storage is full; a moved armament leaves its old cell),
//                                 equipPiece, carriedIds, stampDeck (Core: pools reconciled, gear grants reconciled)
//   engine/kernel/gameConfig.js   resolveGearItem (the Inspect values at rank + Smith tier)
//   src/ui/screens/equipment.js   applyEquipmentChange / positionActions (Replace from owned pieces that fit, Unequip) and
//                                 src/main.js showArmoury (looking clears the glow; a change re-stamps the deck)
//
// Not carried: the set ladder (openedSets; activating another set of a hand), which reads profile unlock rungs this port
// does not keep, and attribute requirements' Smith reductions (no Core 2 piece authors a requirement at the pin).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    /// <summary>One Armoury position (a cell of a slot's sets) and what Inspect shows about the piece in it.</summary>
    public sealed class Core2ArmouryPosition
    {
        public string SlotId { get; set; }
        public int SetIndex { get; set; }
        public bool Active { get; set; }
        public string ItemId { get; set; }
        public string ItemRef { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        /// <summary>The piece's Smith tier.</summary>
        public int Tier { get; set; }
        /// <summary>resolveGearItem at rank + tier: hp, mp, sp, ar, dr, poise, ward, evasion, precision, attributes.</summary>
        public Dictionary<string, int> Values { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The cards the piece lends while worn (gear grantedCards: card × copies).</summary>
        public List<string> GrantedCards { get; set; } = new List<string>();
        /// <summary>The piece's attribute minima with the run's values ({ attributeId, required, actual }).</summary>
        public JArray Requirements { get; set; } = new JArray();
        public bool IsNew { get; set; }

        public JObject ToJson()
        {
            var values = new JObject();
            foreach (var pair in Values) values[pair.Key] = pair.Value;
            return new JObject
            {
                ["slotId"] = SlotId, ["setIndex"] = SetIndex, ["active"] = Active, ["itemId"] = ItemId, ["itemRef"] = ItemRef, ["name"] = Name, ["kind"] = Kind,
                ["tier"] = Tier, ["values"] = values, ["grantedCards"] = new JArray(GrantedCards), ["requirements"] = Requirements.DeepClone(),
            };
        }
    }

    /// <summary>A refused Armoury change: a code (locked, location, grip, requirement, slot, unowned, storage) and its sentence.</summary>
    public sealed class Core2ArmouryRefusal
    {
        public string Code { get; set; }
        public string Text { get; set; }
        public override string ToString() => Code + ": " + Text;
    }

    public static class Core2RunArmoury
    {
        /// <summary>JS COMBAT_EQUIPMENT_CHANGE_DISABLED.</summary>
        public const string CombatChangeDisabled = "Equipment changes are disabled during combat.";
        private const string ArmourMark = "@armour";

        // ---- the combat lock and the editor doors -----------------------------------------------------------------

        /// <summary>JS noncombatEditing: Core edits equipment and deck outside combat only.</summary>
        public static bool NoncombatEditing(Core2RunData data) => (string)data.Module("cards")?["editorWhere"] == "noncombat";

        /// <summary>JS editingLocked: true while a fight has not resolved (a pending reaction is still a fight).</summary>
        public static bool EditingLocked(Core2RunData data, Core2Fight fight) => NoncombatEditing(data) && fight != null && fight.Result == null;

        /// <summary>JS armouryOpensDeckEditor: the profile allows it and the player's Deck editing setting is on.</summary>
        public static bool OpensDeckEditor(Core2RunData data, Core2DeckSettings settings) =>
            NoncombatEditing(data) && (bool?)data.Module("cards")?["editorEnabled"] != false && (settings?.DeckEditing ?? true);

        // ---- new-item flags ---------------------------------------------------------------------------------------

        /// <summary>JS carriedIds: storage, then every set's pieces (the armour set included), once each.</summary>
        public static List<string> CarriedIds(Core2RunLoadout loadout)
        {
            var output = new List<string>(loadout?.Storage ?? new List<string>());
            foreach (var ids in (loadout?.Sets ?? new Dictionary<string, List<string>>()).Values)
                foreach (var id in ids) if (id != null && !output.Contains(id)) output.Add(id);
            return output;
        }

        private static List<string> ArmourRefs(Core2Run run) => run.Loadout.BoughtArmour.Where(p => p.Value != null).Select(p => "armour:" + p.Key + "/" + p.Value).ToList();
        private static List<string> Snapshot(Core2Run run) => CarriedIds(run.Loadout).Concat(ArmourRefs(run)).Concat(new[] { ArmourMark }).ToList();

        /// <summary>JS newArmouryItems: what the run carries that the Armoury has not shown (a run that never opened it sees all as seen).</summary>
        public static List<string> NewItems(Core2Run run)
        {
            if (run?.Loadout == null) return new List<string>();
            if (run.ArmorySeen == null) { run.ArmorySeen = Snapshot(run); return new List<string>(); }
            if (!run.ArmorySeen.Contains(ArmourMark)) run.ArmorySeen = run.ArmorySeen.Concat(ArmourRefs(run)).Concat(new[] { ArmourMark }).ToList();
            var seen = new HashSet<string>(run.ArmorySeen, StringComparer.Ordinal);
            return CarriedIds(run.Loadout).Concat(ArmourRefs(run)).Where(id => !seen.Contains(id)).ToList();
        }

        /// <summary>JS armouryHasNew: the Armoury glows.</summary>
        public static bool HasNew(Core2RunData data, Core2Run run) => NoncombatEditing(data) && NewItems(run).Count > 0;

        /// <summary>JS markArmourySeen: looking clears the glow.</summary>
        public static void MarkSeen(Core2Run run) { if (run?.Loadout != null) run.ArmorySeen = Snapshot(run); }

        // ---- the gates --------------------------------------------------------------------------------------------

        public static JObject Slot(Core2RunData data, string slotId) => Core2RunGear.Slots(data.Content).FirstOrDefault(s => (string)s["id"] == slotId);

        private static string PieceHand(JObject piece) => (string)piece?["hand"] is string h && (h == "left" || h == "right") ? h : null;

        /// <summary>JS fitsSlot: the slot takes the piece's kind, and the piece's hand (if any) is the slot's.</summary>
        public static bool FitsSlot(JObject slot, JObject piece)
        {
            if (slot == null || piece == null) return false;
            if (!Core2Json.Strings(slot["kinds"]).Contains((string)piece["kind"])) return false;
            var may = PieceHand(piece);
            return may == null || may == Core2RunGear.SlotHand(slot);
        }

        /// <summary>The piece an id names in a slot (class armour for the armour slot, an armament otherwise).</summary>
        public static JObject PieceFor(Core2RunData data, Core2Run run, JObject slot, string itemId) =>
            itemId == null ? null : Core2Json.Strings(slot["kinds"]).Contains("armor") ? data.Armour(run.Class, itemId) : data.Armament(itemId);

        /// <summary>JS ownership(...).why: null when the piece is the run's, else unearned or unfound.</summary>
        public static string NotOwnedWhy(Core2RunData data, Core2Run run, JObject piece, IReadOnlyCollection<string> unlocked = null, IReadOnlyCollection<string> found = null)
        {
            if (piece == null) return "unearned";
            var loadout = run.Loadout;
            var grant = loadout.CreationArmourGrant;
            if (grant != null && (string)grant["classId"] == (string)piece["classId"] && (string)grant["id"] == (string)piece["id"]) return null;
            if (loadout.BoughtArmour.Any(p => p.Key == (string)piece["classId"] && p.Value == (string)piece["id"])) return null;
            var unlock = (string)piece["unlock"];
            if (!string.IsNullOrEmpty(unlock)) return unlocked != null && unlocked.Contains(unlock) ? null : "unearned";
            var eq = data.EquipmentBalance;
            if ((string)piece["kind"] != "armor" && (bool?)eq["requireFound"] == true)
            {
                var persistence = (string)eq["persistence"];
                var have = new HashSet<string>(StringComparer.Ordinal);
                if (persistence != "perRun" && found != null) foreach (var id in found) have.Add(id);
                if (persistence != "unlocked") foreach (var id in CarriedIds(loadout)) have.Add(id);
                var basicTag = (string)eq["basicTag"];
                var isBasic = !string.IsNullOrEmpty(basicTag) && Core2Json.Strings(piece["tags"]).Contains(basicTag);
                return isBasic || have.Contains((string)piece["id"]) ? null : "unfound";
            }
            return null;
        }

        private static int HandsRequired(JObject piece) => (int?)piece?["weaponCardPackage"]?["handsRequired"] ?? (int?)piece?["handsRequired"] ?? 1;

        /// <summary>JS gripRefusal: '' or the sentence refusing a two-handed piece beside an occupied other hand.</summary>
        public static string GripRefusal(Core2RunData data, Core2Run run, string slotId, int setIndex, string itemId)
        {
            var slots = Core2RunGear.Slots(data.Content).ToList();
            var slot = slots.FirstOrDefault(s => (string)s["id"] == slotId);
            if (slot == null || Core2RunGear.SlotHand(slot) == null) return "";
            var trial = run.Loadout.Clone();
            foreach (var hand in slots.Where(s => Core2RunGear.SlotHand(s) != null))
            {
                var id = (string)hand["id"];
                if (trial.Sets.TryGetValue(id, out var cells)) trial.Sets[id] = cells.Select(held => itemId != null && held == itemId ? null : held).ToList();
            }
            if (setIndex >= 0 && trial.Sets.TryGetValue(slotId, out var target) && setIndex < target.Count) target[setIndex] = itemId;
            JObject Held(string hand) { var s = slots.FirstOrDefault(r => Core2RunGear.SlotHand(r) == hand); return s == null ? null : Core2RunGear.EquippedIn(data, trial, run.Class, (string)s["id"]); }
            var right = Held("right"); var left = Held("left");
            bool rightTwo = right != null && HandsRequired(right) == 2, leftTwo = left != null && HandsRequired(left) == 2;
            if ((rightTwo && left != null) || (leftTwo && right != null))
            {
                var two = rightTwo ? right : left; var other = rightTwo ? left : right;
                return ((string)two["name"] ?? (string)two["id"]) + " needs both hands, and " + ((string)other["name"] ?? (string)other["id"]) + " is in the other one. Free that hand first.";
            }
            return "";
        }

        /// <summary>JS equipmentRequirementReceipt: each authored attribute minimum against the run's attributes.</summary>
        public static JArray Requirements(Core2Run run, JObject piece, out bool ok)
        {
            ok = true;
            var rows = new JArray();
            foreach (var p in ((piece["requirements"]?["attributes"]) as JObject ?? new JObject()).Properties())
            {
                var required = Math.Max(0, (int)p.Value);
                var has = run.Attributes.TryGetValue(p.Name, out var actual);
                rows.Add(new JObject { ["attributeId"] = p.Name, ["required"] = required, ["actual"] = has ? (JToken)actual : JValue.CreateNull() });
                if (!has || actual < required) ok = false;
            }
            return rows;
        }

        /// <summary>JS equipTransitionPlan: '' when the cell can change, else why (the location is gone, or storage is full).</summary>
        public static string TransitionRefusal(Core2RunData data, Core2RunLoadout loadout, string slotId, int setIndex, string itemId)
        {
            var slot = Slot(data, slotId);
            if (slot == null || !loadout.Sets.TryGetValue(slotId, out var ids) || setIndex < 0 || setIndex >= ids.Count) return "That equipment location is no longer available.";
            if (Core2RunGear.SlotHand(slot) == null) return "";
            var cap = data.EquipmentBalance["storageSlots"]?.Type == JTokenType.Integer ? (int)data.EquipmentBalance["storageSlots"] : 8;
            var previous = ids[setIndex];
            var next = loadout.Storage.Distinct().Where(id => id != itemId).ToList();
            var storesPrevious = previous != null && previous != itemId && !next.Contains(previous);
            if (storesPrevious && next.Count >= cap) return "Inventory is full (" + next.Count + "/" + cap + "). Make room before " + (itemId != null ? "moving this item" : "unequipping this item") + ".";
            return "";
        }

        /// <summary>JS applyEquipTransition: the replaced armament to storage, a moved armament out of its old cell.</summary>
        private static void ApplyTransition(Core2RunData data, Core2RunLoadout loadout, string slotId, int setIndex, string itemId)
        {
            var slot = Slot(data, slotId);
            var ids = loadout.Sets[slotId];
            if (Core2RunGear.SlotHand(slot) == null) { ids[setIndex] = itemId; return; }
            var previous = ids[setIndex];
            var next = loadout.Storage.Distinct().Where(id => id != itemId).ToList();
            var storesPrevious = previous != null && previous != itemId && !next.Contains(previous);
            if (itemId != null)
                foreach (var hand in Core2RunGear.Slots(data.Content).Where(s => Core2RunGear.SlotHand(s) != null).Select(s => (string)s["id"]))
                    if (loadout.Sets.TryGetValue(hand, out var cells))
                        for (var i = 0; i < cells.Count; i++) if (cells[i] == itemId && (hand != slotId || i != setIndex)) cells[i] = null;
            if (storesPrevious) next.Add(previous);
            loadout.Storage = next;
            ids[setIndex] = itemId;
        }

        // ---- Replace and Unequip ----------------------------------------------------------------------------------

        /// <summary>
        /// JS equipPiece + stampDeck: put a piece in a cell (null unequips it to storage). Returns null on success, else the
        /// refusal; a refused change leaves the run untouched. A success re-derives the pools and reconciles gear grants.
        /// </summary>
        public static Core2ArmouryRefusal Equip(Core2RunData data, Core2Run run, string slotId, int setIndex, string itemId, Core2Fight fight = null)
        {
            Core2ArmouryRefusal Refuse(string code, string text) => new Core2ArmouryRefusal { Code = code, Text = text };
            if (EditingLocked(data, fight)) return Refuse("locked", CombatChangeDisabled);
            var slot = Slot(data, slotId);
            if (slot == null || !run.Loadout.Sets.TryGetValue(slotId, out var ids) || setIndex < 0 || setIndex >= ids.Count) return Refuse("location", "That equipment location is no longer available.");
            if (fight != null && fight.Result == null && (bool?)data.EquipmentBalance["allowChangesInCombat"] != true) return Refuse("locked", CombatChangeDisabled);
            var grip = GripRefusal(data, run, slotId, setIndex, itemId);
            if (grip.Length > 0) return Refuse("grip", grip);
            JObject piece = null;
            if (itemId != null)
            {
                piece = PieceFor(data, run, slot, itemId);
                if (piece == null || !FitsSlot(slot, piece)) return Refuse("slot", (itemId) + " does not fit " + (string)slot["label"] + ".");
                Requirements(run, piece, out var meets);
                if (!meets) return Refuse("requirement", ((string)piece["name"] ?? itemId) + " does not meet its attribute requirements.");
                var why = NotOwnedWhy(data, run, piece);
                if (why != null) return Refuse("unowned", ((string)piece["name"] ?? itemId) + " is not yours (" + why + ").");
            }
            var transition = TransitionRefusal(data, run.Loadout, slotId, setIndex, itemId);
            if (transition.Length > 0) return Refuse("storage", transition);
            ApplyTransition(data, run.Loadout, slotId, setIndex, itemId);
            // main.js showArmoury onChange: run.loadout = loadout; stampDeck (Core: pools, then the gear grants).
            Core2RunGear.ReconcilePools(data, run);
            Core2RunGear.ReconcileGrantedCards(data, run);
            return null;
        }

        public static Core2ArmouryRefusal Unequip(Core2RunData data, Core2Run run, string slotId, int setIndex, Core2Fight fight = null) => Equip(data, run, slotId, setIndex, null, fight);

        /// <summary>The picker for a cell (JS authoredFor(slot) ∩ owned): every piece the run owns that fits the slot.</summary>
        public static List<string> ReplaceCandidates(Core2RunData data, Core2Run run, string slotId)
        {
            var slot = Slot(data, slotId);
            if (slot == null) return new List<string>();
            var pieces = Core2Json.Strings(slot["kinds"]).Contains("armor")
                ? data.Content.Armour.Select(a => a.Raw).Where(a => (string)a["classId"] == run.Class)
                : data.Content.Armaments.Select(a => a.Raw);
            return pieces.Where(p => FitsSlot(slot, p) && NotOwnedWhy(data, run, p) == null).Select(p => (string)p["id"]).ToList();
        }

        // ---- Inspect ----------------------------------------------------------------------------------------------

        /// <summary>Every cell of the hand and armour slots with what Inspect shows for the piece in it (empty cells carry no item).</summary>
        public static List<Core2ArmouryPosition> Positions(Core2RunData data, Core2Run run)
        {
            var output = new List<Core2ArmouryPosition>();
            var fresh = new HashSet<string>(run.ArmorySeen == null ? Enumerable.Empty<string>() : NewItems(Core2RunSave.Clone(run)), StringComparer.Ordinal);
            foreach (var slot in Core2RunGear.Slots(data.Content))
            {
                var slotId = (string)slot["id"];
                var armour = Core2Json.Strings(slot["kinds"]).Contains("armor");
                if (Core2RunGear.SlotHand(slot) == null && !armour) continue;
                if (!run.Loadout.Sets.TryGetValue(slotId, out var ids)) continue;
                run.Loadout.Active.TryGetValue(slotId, out var active);
                for (var i = 0; i < ids.Count; i++)
                {
                    var position = new Core2ArmouryPosition { SlotId = slotId, SetIndex = i, Active = i == active, ItemId = ids[i] };
                    if (ids[i] != null) Inspect(data, run, slot, ids[i], position);
                    position.IsNew = ids[i] != null && (fresh.Contains(ids[i]) || (armour && fresh.Contains("armour:" + run.Class + "/" + ids[i])));
                    output.Add(position);
                }
            }
            return output;
        }

        /// <summary>Inspect one piece for a slot: its resolved Core values at its Smith tier, the cards it lends and its minima.</summary>
        public static Core2ArmouryPosition Inspect(Core2RunData data, Core2Run run, JObject slot, string itemId, Core2ArmouryPosition into = null)
        {
            var position = into ?? new Core2ArmouryPosition { SlotId = (string)slot["id"], SetIndex = -1, ItemId = itemId };
            var armour = Core2Json.Strings(slot["kinds"]).Contains("armor");
            var piece = PieceFor(data, run, slot, itemId);
            position.ItemRef = armour ? "armor/" + run.Class + "/" + itemId : "armament/" + itemId;
            position.Name = (string)piece?["name"] ?? itemId;
            position.Kind = (string)piece?["kind"];
            position.Tier = run.ItemUpgradeLevels.TryGetValue(position.ItemRef, out var tier) ? tier : 0;
            var gear = Core2RunGear.GearItem(data.Content, itemId, armour, run.Class);
            var settings = data.Module("gear");
            if (gear != null && settings?["armorBudget"] != null)
            {
                var resolved = Core2EnemyStats.ResolveGearItem(gear, settings, ((int?)gear["rank"] ?? 1) + position.Tier);
                foreach (var key in Core2EnemyStats.GearKeys) position.Values[key] = resolved.Values.TryGetValue(key, out var v) ? v : 0;
                foreach (var grant in ((gear["grantedCards"] as JArray) ?? new JArray()).OfType<JObject>())
                    for (var i = 0; i < (int)grant["copies"]; i++) position.GrantedCards.Add((string)grant["card"]);
            }
            if (piece != null) position.Requirements = Requirements(run, piece, out _);
            return position;
        }
    }
}
