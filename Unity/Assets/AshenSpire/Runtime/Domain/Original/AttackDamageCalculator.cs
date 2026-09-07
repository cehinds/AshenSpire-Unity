// AttackDamageCalculator.cs — original damage preview and resolution arithmetic.
// Floor once after additive bonuses, multipliers, tag vulnerabilities and resistance.
// Effects carry the damage school explicitly; a visual/card tag never invents it.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class AttackDamageCalculator
    {
        public static int Calculate(StatusSystem statuses, JObject source, JObject target, double basis, string[] attackTags, string school = null)
        {
            var damage = basis;
            if ((string)source?["kind"] == "player" && school != null) damage += (double?)source["damageBySchoolAdd"]?[school] ?? 0;
            damage += statuses.Add(source, "attackDamageAdd");
            damage *= statuses.Multiply(source, "damageDealtMult");
            if (target != null) damage *= statuses.Multiply(target, "damageTakenMult");
            if (target != null && attackTags != null && attackTags.Length > 0)
            {
                double additions = 0;
                foreach (var property in ((JObject)target["statuses"]).Properties())
                {
                    if (StatusSystem.Stacks(target, property.Name) <= 0) continue;
                    var vulnerability = statuses.Definition(property.Name)["taggedVulnerability"];
                    if (vulnerability == null || !vulnerability["tags"].Values<string>().Intersect(attackTags).Any()) continue;
                    if ((string)vulnerability["stacking"] == "multiplicative") damage *= (double)vulnerability["mult"];
                    else additions += (double)vulnerability["mult"] - 1;
                }
                if (additions > 0) damage *= 1 + additions;
            }
            if (target != null && school != null)
            {
                if (target["damageResistanceBySchool"]?[school] is JValue resistance) damage *= Math.Max(0, 1 - (double)resistance / 100);
                foreach (var property in ((JObject)target["statuses"]).Properties())
                {
                    if (StatusSystem.Stacks(target, property.Name) <= 0) continue;
                    if ((string)statuses.Definition(property.Name)["schoolDamageVulnerability"]?["school"] == school) damage *= 1 + ((double?)property.Value["stacks"] ?? 0) / 100;
                }
            }
            return Math.Max(0, checked((int)Math.Floor(damage)));
        }
    }
}
