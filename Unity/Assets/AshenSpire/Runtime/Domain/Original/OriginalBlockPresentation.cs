using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    // Original build 913: Ward is provenance within Block, never a second pool.
    public static class OriginalBlockPresentation
    {
        public static bool Enabled(JObject mechanics)
        {
            var flag = mechanics?["block"]?["wardProvenance"];
            if (flag == null) return false;
            if (flag.Type != JTokenType.Boolean) throw new ArgumentException("Ward provenance must be true or false.");
            return (bool)flag;
        }
        private static int Whole(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return 0;
            var number = (double)value;
            return double.IsNaN(number) || double.IsInfinity(number) ? 0 : (int)Math.Max(0, Math.Min(int.MaxValue, Math.Floor(number)));
        }
        public static int Ward(JObject entity) => Math.Min(Whole(entity?["block"]), Whole(entity?["wardBlock"]));
        public static int Defense(JObject entity) => Whole(entity?["block"]) - Ward(entity);
        public static string Label(JObject entity, string ordinary = "Block") => ordinary + " " + Defense(entity)
            + (Ward(entity) > 0 ? " · Arcane Ward " + Ward(entity) : "");
        public static void Reconcile(JObject entity)
        {
            if (entity?.Property("wardBlock") != null) entity["wardBlock"] = Ward(entity);
        }
        public static void Receipt(JObject entity, JObject receipt)
        {
            if (entity?.Property("wardBlock") != null) receipt["wardBlockRemaining"] = Ward(entity);
        }
        public static void Validate(JObject entity)
        {
            if (entity?.Property("wardBlock") == null) return;
            var ward = entity["wardBlock"]; var total = entity["block"];
            if (ward.Type != JTokenType.Integer || total == null || total.Type != JTokenType.Integer
                || (long)ward < 0 || (long)ward > (long)total)
                throw new ArgumentException("Saved wardBlock must be a whole number between zero and Block.");
        }
        public static bool IsMagical(JObject face)
        {
            var school = (string)face?["damageSchool"];
            if (!string.IsNullOrEmpty(school)) return school != "physical";
            var tags = face?["cardTags"] as JArray ?? face?["tags"] as JArray;
            if (tags != null && tags.Any(tag => new[] { "magic", "magical", "arcane", "holy", "fire", "spell" }
                .Contains(((string)(tag is JObject row ? row["id"] : tag) ?? "").Split(':').Last()))) return true;
            var mana = face?["manaCost"];
            return mana != null && (mana.Type == JTokenType.Integer || mana.Type == JTokenType.Float) && (double)mana > 0;
        }
    }
}
