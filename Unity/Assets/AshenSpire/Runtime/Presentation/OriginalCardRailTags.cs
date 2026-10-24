// OriginalCardRailTags.cs — which marks the illustrated card's right-edge tag rail shows.
// The marks come from the RESOLVED Unity card (the same effects and numbers its rules text
// uses), never from the frozen JS reference: the reference only supplies the symbol art.
// Order follows JS cardSideTags: category or camp, damage identity, block, evade, statuses
// (at most three), capped at five. An effect with no JS symbol is omitted.
// No UnityEngine dependency: UnityTests/CardText compiles this file and checks the counts.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation
{
    public static class OriginalCardRailTags
    {
        public sealed class Mark
        {
            public string Id { get; }
            public string Label { get; }
            public int? Count { get; }
            public Mark(string id, string label, int? count) { Id = id; Label = label; Count = count; }
        }

        /// <summary>The rail for a resolved card; hasSymbol says whether the reference has art for an id.</summary>
        public static IReadOnlyList<Mark> For(JObject card, Func<string, bool> hasSymbol)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (hasSymbol == null) throw new ArgumentNullException(nameof(hasSymbol));
            var effects = (card["effects"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
            var marks = new List<Mark>();
            void Add(string id, string label, int? count)
            {
                if (marks.Count < 5 && hasSymbol(id) && marks.All(m => m.Id != id)) marks.Add(new Mark(id, label, count));
            }
            var type = (string)card["type"];
            var damages = effects.Any(e => (string)e["op"] == "damage");
            var school = (string)card["damageSchool"];
            if (type == "skill" || type == "power") Add("category:" + type, type == "skill" ? "Skill" : "Power", null);
            else if (damages) Add(school == "magic" || school == "arcane" ? "camp:spell" : "camp:physical", school == "magic" || school == "arcane" ? "Spell" : "Physical", null);
            if (damages && !string.IsNullOrEmpty(school) && school != "physical" && school != "magic") Add("damage:" + school, Humanize(school), null);
            var blocks = effects.Where(e => (string)e["op"] == "block").ToArray();
            if (blocks.Length > 0) Add("effect:block", "Block", blocks.Length == 1 ? Resolved(blocks[0]) : null);
            if (effects.Any(e => (string)e["op"] == "dodgeRoll"))
                foreach (var id in new[] { "effect:evade", "status:evade", "status:base.evade" }) if (hasSymbol(id)) { Add(id, "Evade", null); break; }
            var statuses = effects.Where(e => (string)e["op"] == "applyStatus" && e["status"] != null).GroupBy(e => (string)e["status"]).Take(3);
            foreach (var group in statuses)
            {
                // Several applications never add up into one printed amount.
                var only = group.Count() == 1 ? group.First() : null;
                Add("status:" + group.Key, Humanize(group.Key), only == null ? null : Whole(only["stacks"]));
            }
            return marks;
        }

        /// <summary>
        /// The footer word: the resolved Unity card's type label (balance.ui.cardTypes), never the
        /// JS reference's stance word. OriginalCardView passes this to the face as typeName.
        /// </summary>
        public static string TypeLabel(JObject card, JObject cardTypes)
        {
            var type = (string)card?["type"] ?? "";
            return (string)cardTypes?[type]?["label"] ?? OriginalCardText.Humanize(type);
        }

        /// <summary>
        /// The footer sigil for a Unity card type: the reference mark for that type when it has
        /// one, otherwise the generic mark for its family (status for curses, skill otherwise).
        /// </summary>
        public static string FooterSigil(string type, Func<string, bool> hasSigil)
        {
            if (hasSigil == null) throw new ArgumentNullException(nameof(hasSigil));
            if (!string.IsNullOrEmpty(type) && hasSigil(type)) return type;
            var generic = type == "curse" ? "status" : "skill";
            return hasSigil(generic) ? generic : null;
        }

        // The rules text's own number: the authored amount plus a folded attribute
        // bonus on a single-hit, single-repeat effect (OriginalCardText.Describe).
        private static int? Resolved(JObject effect)
        {
            var amount = Whole(effect["amount"]);
            if (amount == null) return null;
            var bonus = (int?)effect["attributeBonus"] ?? 0;
            var single = One(effect["repeat"]) && ((string)effect["op"] != "damage" || One(effect["hits"]));
            return bonus > 0 && single ? amount + bonus : amount;
        }

        private static bool One(JToken value) => value == null || ((value.Type == JTokenType.Integer || value.Type == JTokenType.Float) && (double)value == 1);

        private static int? Whole(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return null;
            var number = (double)value;
            return number >= 0 && number <= int.MaxValue && number == Math.Truncate(number) ? (int)number : (int?)null;
        }

        private static string Humanize(string id)
        {
            var tail = id.Substring(id.LastIndexOf('.') + 1).Replace('-', ' ');
            return tail.Length == 0 ? id : char.ToUpperInvariant(tail[0]) + tail.Substring(1);
        }
    }
}
