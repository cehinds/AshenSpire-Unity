// Core2RunFights.cs — how a Core 2 run hands a fight its player, and what the fight leaves behind.
//
// JS at the pin:
//   src/engine/runCombat.js           createRunCombat (the player block: pools, effective attributes, rating receipt,
//                                     relics, the deck in run order, the start statuses worn gear grants), runCombatEnd
//   src/engine/combatCardTactics.js   tacticalCarrier (a card's face: its combat profile, an untyped card dealing the
//                                     wielded piece's damage type, bare hands Blunt), wieldedArmament
//   src/engine/combatEngagement.js    rangeSourceContact / committedRangeSource (what the card's damage covers)
//   src/model/attackTags.js           attackDescriptor, weaponDamageType
//   src/model/combatCardProfile.js    combatProfileFor (the face's authored and tagged identity)
//   src/main.js                       enterCombat (the fight draws on a copy of the run's RNG, whose counters the run
//                                     then adopts), combatMods (seat tier HP, boss tier HP)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public static class Core2RunFights
    {
        private static readonly string[] ProfilePayloadKeys = { "range", "defenseRange", "targeting", "traits", "projectile", "counterCoverage", "counterPayload",
            "evade", "upcast", "stanceTrigger", "mirrors", "stanceExpiry", "schoolEffect", "caster", "breakPoiseBonus", "pronePoiseBonus", "traitValues" };
        private static readonly Dictionary<string, string> LegacyRange = new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "melee", ["near"] = "reach", ["far"] = "ranged" };

        private static string Identity(IEnumerable<string> tags, string prefix)
        {
            var values = tags.Where(t => t.StartsWith(prefix, StringComparison.Ordinal)).Select(t => t.Substring(prefix.Length)).Distinct().ToList();
            return values.Count == 1 ? values[0] : null;
        }

        /// <summary>JS attackDescriptor: the attack's source (source:* tag) and its single damage type (damage:* tag).</summary>
        public static (string Source, string DamageType, JObject Attack) AttackDescriptor(JObject row, JObject attackOverride = null)
        {
            var tags = Core2Json.Strings(row?["cardTags"] ?? row?["tags"]);
            var attack = (JObject)(attackOverride ?? row?["attack"] as JObject)?.DeepClone() ?? new JObject();
            var source = (string)attack["source"] ?? Identity(tags, "source:");
            var damageType = (string)attack["damageType"];
            var types = tags.Where(t => t.StartsWith("damage:", StringComparison.Ordinal)).Select(t => t.Substring(7)).ToList();
            if (attack["components"] == null && damageType == null && types.Count == 1) damageType = types[0];
            return (source, damageType, attack);
        }

        /// <summary>JS combatProfileFor over a card definition (tags and authored payloads; no weapon yet).</summary>
        public static JObject ProfileFor(JObject def)
        {
            var tags = Core2Json.Strings(def["cardTags"] ?? def["tags"]).ToList();
            var attack = def["attack"] as JObject;
            var components = attack?["components"] as JArray;
            var types = (components != null ? components.Select(c => (string)c["type"])
                : attack?["damageType"] != null ? new[] { (string)attack["damageType"] }
                : tags.Where(t => t.StartsWith("damage:", StringComparison.Ordinal)).Select(t => t.Substring(7))).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            var profile = new JObject
            {
                ["camp"] = Identity(tags, "camp:"), ["maneuver"] = Identity(tags, "maneuver:"), ["school"] = Identity(tags, "school:"),
                ["damageType"] = types.Count == 1 ? types[0] : null, ["counterMode"] = Identity(tags, "counter:"),
            };
            if (components != null && types.Count > 1)
            {
                var best = components.OfType<JObject>().Aggregate((JObject)null, (a, c) => a == null || (double)c["weight"] > (double)a["weight"] ? c : a);
                profile["damageType"] = (string)best?["type"];
                profile["damageComponents"] = new JArray(components.OfType<JObject>().Select(c => new JObject { ["type"] = c["type"], ["weight"] = c["weight"] }));
            }
            var range = Identity(tags.Select(t => t.StartsWith("reach:", StringComparison.Ordinal) ? "range:" + (LegacyRange.TryGetValue(t.Substring(6), out var r) ? r : t.Substring(6)) : t), "range:");
            var targeting = Identity(tags, "targeting:");
            if (range != null) profile["range"] = range;
            if (targeting != null) profile["targeting"] = targeting;
            var traits = tags.Where(t => t.StartsWith("trait:", StringComparison.Ordinal)).Select(t => t.Substring(6)).Distinct().ToList();
            if (traits.Count > 0) profile["traits"] = new JArray(traits);
            // tacticalCarrier copies these from the definition onto the carrier, and combatProfileFor reads them there.
            foreach (var key in ProfilePayloadKeys) if (def[key] != null) profile[key] = def[key].DeepClone();
            return profile;
        }

        /// <summary>JS wieldedArmament: the main-hand armament whose attack source is <paramref name="kind"/>.</summary>
        private static JObject Wielded(Core2RunData data, Core2RunLoadout loadout, string classId, string kind)
        {
            if (loadout == null) return null;
            foreach (var slot in Core2RunGear.Slots(data.Content))
            {
                if (Core2RunGear.SlotHand(slot) != "right") continue;
                var equipped = Core2RunGear.EquippedIn(data, loadout, classId, (string)slot["id"]);
                if (equipped != null && AttackDescriptor(equipped).Source == kind) return equipped;
            }
            return null;
        }

        /// <summary>
        /// A card's fight face (JS tacticalCarrier + rangeSourceContact for the player): its combat profile with the damage
        /// type of the piece that deals it, and the range source its damage commits. JSON in the JS shape.
        /// </summary>
        public static JObject Face(Core2RunData data, Core2RunLoadout loadout, string classId, string cardId)
        {
            var def = data.Content.Card(cardId).Raw;
            var profile = ProfileFor(def);
            if (def["combatProfile"] is JObject authored) foreach (var p in authored.Properties()) profile[p.Name] = p.Value.DeepClone();
            var faces = data.Faces;
            var attack = AttackDescriptor(def);
            var weaponPart = attack.Attack["weaponDamage"] != null && attack.Attack["components"] != null;
            var camp = (string)profile["camp"];
            if ((Core2RunJson.IsNull(profile["damageType"]) || weaponPart) && camp != null)
            {
                var kind = attack.Source ?? (camp == "spell" ? "spell" : "weapon");
                var piece = Wielded(data, loadout, classId, kind);
                var school = (string)def["damageSchool"];
                var magical = camp == "spell" || (school != null && school != "physical");
                var unarmedType = faces["unarmed"] is JObject unarmed ? AttackDescriptor(unarmed).DamageType : null;
                var bare = piece != null || (kind != "weapon" && kind != "unarmed") || magical ? null : unarmedType ?? (string)faces["fallbackSource"]?["damageType"];
                var family = piece != null ? (string)faces["equipmentSources"]?["itemFamilies"]?[(string)piece["id"]] ?? (string)faces["equipmentSources"]?["profileFamilies"]?[(string)piece["attackProfile"] ?? ""]
                    : (string)faces["fallbackSource"]?["family"];
                var pieceType = (piece != null ? AttackDescriptor(piece).DamageType : null) ?? bare;
                var rule = attack.Attack["weaponDamage"] as JObject;
                string type = pieceType;
                if (rule != null && pieceType != null)
                    type = Core2Json.Strings(rule["families"]).Contains(family) && Core2Json.Strings(rule["types"]).Contains(pieceType) ? pieceType : Core2Json.Strings(rule["types"]).FirstOrDefault();
                profile["damageType"] = type;
            }
            if (profile["traits"] == null) profile["traits"] = new JArray();
            if (!data.Content.Cards.Any(c => c.Id == cardId))
            {
                // A legacy adapter card (a curse or status an event or enemy adds) takes the expanded defaults
                // (JS applyCombatExpansionCard): a range and targeting, and no stance when it has no maneuver.
                var maneuver = Core2RunJson.IsNull(profile["maneuver"]) ? null : (string)profile["maneuver"];
                if (Core2RunJson.IsNull(profile["range"])) profile["range"] = maneuver == "ranged" || camp == "spell" ? "reach" : "melee";
                if (Core2RunJson.IsNull(profile["targeting"])) profile["targeting"] = maneuver == "sweep" ? "area" : "single";
                if (maneuver == null && profile["stanceTrigger"] == null) profile["stanceTrigger"] = false;
            }
            // The range source the card's damage commits (rangeSourceContact with the card's first damage effect).
            var damage = ((JArray)def["effects"]).OfType<JObject>().FirstOrDefault(e => (string)e["op"] == "damage") ?? new JObject();
            var rangeProfile = (JObject)profile.DeepClone();
            if (damage["combatProfile"] is JObject effectProfile) foreach (var p in effectProfile.Properties()) rangeProfile[p.Name] = p.Value.DeepClone();
            var rangeAttack = AttackDescriptor(def, damage["attack"] as JObject);
            var rangeKind = rangeAttack.Source ?? ((string)rangeProfile["camp"] == "spell" ? "spell" : "weapon");
            var rangePiece = rangeKind == "unarmed" ? null : Wielded(data, loadout, classId, rangeKind);
            var sourceType = rangePiece != null ? rangeKind : rangeKind == "weapon" ? "unarmed" : rangeKind;
            var weapon = sourceType == "weapon";
            var coverage = weapon ? (string)rangePiece["range"] ?? "melee" : sourceType == "unarmed" ? "melee"
                : (string)rangeProfile["range"] ?? (rangePiece != null ? (string)rangePiece["range"] ?? "melee" : null) ?? "melee";
            var bypass = (bool?)rangeProfile["rangeOverride"] == true || (bool?)rangeProfile["adequateReach"] == true || Core2Json.Strings(rangeProfile["traits"]).Contains("adequateReach");
            return new JObject
            {
                ["combatProfile"] = profile,
                ["rangeSource"] = new JObject
                {
                    ["coverage"] = coverage, ["weapon"] = weapon, ["sourceWeaponId"] = weapon ? (string)rangePiece["id"] : null,
                    ["throwCapable"] = weapon && Core2Json.Strings(rangePiece["propertyTags"]).Contains("throw"), ["bypass"] = bypass,
                },
            };
        }

        /// <summary>The primary weapon's range a fight starts the player at (JS initializeCombatRange: the first hand holding a weapon).</summary>
        public static string PrimaryWeaponRange(Core2RunData data, Core2Run run)
        {
            foreach (var hand in new[] { "right", "left" })
                foreach (var slot in Core2RunGear.Slots(data.Content))
                {
                    if (Core2RunGear.SlotHand(slot) != hand) continue;
                    var piece = Core2RunGear.EquippedIn(data, run.Loadout, run.Class, (string)slot["id"]);
                    if (piece != null && AttackDescriptor(piece).Source == "weapon") return (string)piece["range"] ?? "melee";
                }
            return "melee";
        }

        /// <summary>
        /// JS createRunCombat's player block for a Core 2 run: pools (a fight opens with full SP), effective attributes,
        /// the rating receipt, the deck in run order with each card's face, the mounted property rules, the start
        /// statuses worn gear grants, and the encounter's HP multiplier.
        /// </summary>
        public static Core2FightSetup BuildSetup(Core2RunData data, Core2Run run, double enemyHpMult = 1)
        {
            var ratings = Core2RunGear.Ratings(data, run);
            var setup = new Core2FightSetup
            {
                ClassId = run.Class, Hp = run.Hp, MaxHp = run.MaxHp, MaxStamina = run.EnergyMax, Mana = run.Mana, MaxMana = run.MaxMana,
                Level = Math.Max(1, run.Level.Level), Attributes = Core2RunGear.EffectiveAttributes(data, run), Ratings = ratings.Totals,
                BaseEvasionBonus = ratings.BaseEvasionBonus, BasePrecisionBonus = ratings.BasePrecisionBonus,
                PoiseMax = ratings.Totals.TryGetValue("poise", out var poise) ? poise : 0, RelicIds = new List<string>(run.Relics),
                Deck = run.Deck.Select(c => new Core2CardInstance { InstanceId = c.InstanceId, CardId = c.CardId }).ToList(),
                PropertyRules = Core2RunGear.PropertyCarriers(data, run), PrimaryWeaponRange = PrimaryWeaponRange(data, run),
                EnemyHpMult = enemyHpMult, RatingSources = ratings.Sources,
            };
            foreach (var cardId in run.Deck.Select(c => c.CardId).Distinct())
            {
                var face = Face(data, run.Loadout, run.Class, cardId);
                var source = (JObject)face["rangeSource"];
                setup.Cards[cardId] = new Core2CardFace
                {
                    Profile = Core2CombatProfile.FromJson(face["combatProfile"]),
                    RangeSource = new Core2RangeSource
                    {
                        Coverage = (string)source["coverage"], Weapon = (bool)source["weapon"], SourceWeaponId = (string)source["sourceWeaponId"],
                        ThrowCapable = (bool)source["throwCapable"], Bypass = (bool)source["bypass"],
                    },
                };
            }
            foreach (var status in Core2RunGear.RunMods(data, run).StartStatuses) setup.StartStatuses.Add(new KeyValuePair<string, int>(status.Status, status.Stacks));
            setup.PoiseMeterMax = PoiseMeterMax(data, run);
            return setup;
        }

        /// <summary>
        /// JS playerPoiseThresholdReceipt (combat ratings off), the max createCombat stamps on the player's legacy Poise vessel
        /// (stampPlayerPoiseMax; its value starts at 0): the run's derived-stat "poise" row at its level over the fight's
        /// attributes, plus each worn armour piece's poiseThreshold, plus the relics' poiseThresholdAdd passives. A physical
        /// status-pressure fill strains it (Core2FightStatuses ApplyStatusPressure).
        /// </summary>
        public static int PoiseMeterMax(Core2RunData data, Core2Run run)
        {
            var row = Core2DerivedStats.Row(run.DerivedStatRules, "poise");
            var attribute = 0.0;
            if (row != null)
            {
                var rule = (JObject)row.DeepClone();
                if (rule["base"] == null || rule["base"].Type != JTokenType.Integer && rule["base"].Type != JTokenType.Float) rule["base"] = 0;
                attribute = Core2DerivedStats.Value(rule, Core2RunGear.EffectiveAttributes(data, run), Math.Max(1, run.Level.Level), "poise");
            }
            var equipment = Core2RunGear.Worn(data, run.Loadout, run.Class).Where(p => (string)p.Row["kind"] == "armor")
                .Sum(p => p.Row["poiseThreshold"]?.Type == JTokenType.Integer || p.Row["poiseThreshold"]?.Type == JTokenType.Float ? (double)p.Row["poiseThreshold"] : 0);
            var relic = run.Relics.Sum(id => data.Relic(id)["passives"]?["poiseThresholdAdd"] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (double)v : 0);
            return (int)Math.Floor(attribute + equipment + relic);
        }

        /// <summary>The setup as the JS oracle records it (fightStart): the player, the deck, the faces and the property rules.</summary>
        public static JObject SetupJson(Core2RunData data, Core2Run run, Core2FightSetup setup)
        {
            JObject Ints(IReadOnlyDictionary<string, int> map) { var o = new JObject(); foreach (var p in map) o[p.Key] = p.Value; return o; }
            var cards = new JObject();
            foreach (var cardId in run.Deck.Select(c => c.CardId).Distinct()) cards[cardId] = Face(data, run.Loadout, run.Class, cardId);
            var rules = new JArray();
            foreach (var carrier in setup.PropertyRules)
            {
                var tags = carrier.Kind == "relic" ? Core2Json.Strings(data.Relic(carrier.Id)["propertyTags"]) : carrier.Kind == "class" ? data.Class(carrier.Id).PropertyTags
                    : Core2Json.Strings((carrier.Kind == "armour" ? data.Armour(run.Class, carrier.Id) : data.Armament(carrier.Id))["propertyTags"]);
                rules.Add(new JObject
                {
                    ["kind"] = carrier.Kind, ["id"] = carrier.Id,
                    ["rules"] = new JArray(data.CarrierRules(tags).Select(rule =>
                    {
                        var row = new JObject { ["tag"] = rule["tag"] };
                        if (rule["triggers"] != null) row["triggers"] = rule["triggers"].DeepClone();
                        if (rule["passives"] != null) row["passives"] = rule["passives"].DeepClone();
                        return row;
                    })),
                });
            }
            var player = new JObject
            {
                ["classId"] = setup.ClassId, ["hp"] = setup.Hp, ["maxHp"] = setup.MaxHp, ["maxStamina"] = setup.MaxStamina, ["mana"] = setup.Mana, ["maxMana"] = setup.MaxMana,
                ["attributes"] = Ints(setup.Attributes), ["ratings"] = Ints(setup.Ratings), ["level"] = setup.Level, ["baseEvasionBonus"] = setup.BaseEvasionBonus,
                ["basePrecisionBonus"] = setup.BasePrecisionBonus, ["poise"] = new JObject { ["max"] = setup.PoiseMax, ["value"] = setup.PoiseMax },
                ["handSize"] = Math.Max(0, 4 + (setup.Attributes.TryGetValue("intelligence", out var intelligence) ? intelligence : 0)), ["relicIds"] = new JArray(setup.RelicIds),
                ["poiseMeter"] = setup.PoiseMeterMax > 0 ? new JObject { ["value"] = setup.PoiseMeterValue, ["max"] = setup.PoiseMeterMax } : JValue.CreateNull(),
                ["ratingSources"] = new JArray(setup.RatingSources.Select(row => new JObject
                    { ["name"] = row.Name, ["kind"] = row.Kind, ["sourceId"] = row.SourceId, ["slot"] = row.Slot, ["ar"] = row.Ar, ["pr"] = row.Pr })),
            };
            return new JObject
            {
                ["player"] = player, ["deck"] = new JArray(setup.Deck.Select(c => new JObject { ["instanceId"] = c.InstanceId, ["cardId"] = c.CardId })),
                ["primaryWeaponRange"] = setup.PrimaryWeaponRange, ["cards"] = cards, ["propertyRules"] = rules,
            };
        }

        /// <summary>JS combatMods (Core 2: no custom climb): the seat-tier HP ratio, or a boss's tier scale.</summary>
        public static (double Hp, double Damage) EncounterScale(Core2RunData data, Core2Run run, JObject encounter)
        {
            var tier = ((run.ActNumber - 1) % data.ActsPerCycle) + 1;
            var table = (JObject)data.Balance["seatTiers"];
            double Tier(int t) => (double)table[t.ToString(CultureInfo.InvariantCulture)];
            if ((string)encounter["pool"] == "boss")
            {
                var seat = (string)encounter["seat"];
                var baseTier = seat != null ? (int)data.Content.Seats.First(s => s.Id == seat).Raw["baseTier"] : data.ActsPerCycle;
                var ratio = Tier(tier) == Tier(baseTier) ? 1 : Tier(tier) / Tier(baseTier);
                var row = (JObject)data.Balance["bossTiers"][tier.ToString(CultureInfo.InvariantCulture)];
                return (ratio * (double)row["hp"], ratio * (double)row["damage"]);
            }
            var seatId = Core2ActMap.SeatAt(run);
            var seatBase = (int)data.Content.Seats.First(s => s.Id == seatId).Raw["baseTier"];
            return (Tier(tier) == Tier(seatBase) ? 1 : Tier(tier) / Tier(seatBase), 1);
        }

        /// <summary>JS runCombatEnd for a Core 2 run: the pools the fight leaves behind (max SP and energy included) return to the run.</summary>
        public static void End(Core2RunData data, Core2Run run, Core2Fight fight, int staminaDeficit = 0)
        {
            // The fight's equipment deficits: HP and MP as the run carried them in, SP as the last turn start left it.
            run.EquipmentPoolDeficits = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["hp"] = run.EquipmentPoolDeficits.TryGetValue("hp", out var hp) ? hp : 0, ["mana"] = run.EquipmentPoolDeficits.TryGetValue("mana", out var mana) ? mana : 0,
                ["stamina"] = staminaDeficit,
            };
            var p = fight.Player;
            run.Hp = p.Hp; run.MaxHp = p.MaxHp;
            run.Mana = p.Mana; run.MaxMana = p.MaxMana;
            run.Stamina = Math.Min(p.Stamina, p.MaxStamina); run.MaxStamina = p.MaxStamina;
            run.EnergyMax = p.MaxStamina;
            Core2RunGear.ReconcileGrantedCards(data, run);
        }
    }
}
