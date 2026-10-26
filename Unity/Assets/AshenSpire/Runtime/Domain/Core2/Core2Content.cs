// Core2Content.cs — engine-free loader for the JS Core 2 export (SPEC.md §0.1, §0.9 phase 4).
//
// DATA: GameContent/Unity/Core2/** is written by `node tools/export-js-core2.mjs <js-checkout>`
// and never edited by hand. "AshenSpire > 1. Validate and Import Content" validates it with this
// class and mirrors it to Resources/Core2; at runtime pass a reader over Resources.Load<TextAsset>.
// Console tests pass a file reader (UnityTests/Core2Content).
//
// Load() reads manifest.json, refuses any listed file whose sha256 differs (the export is a
// byte-identity artifact of the pinned JS commit), parses every table and checks its ids and
// cross-references. It is not wired into gameplay yet; phase 5 (combat) and phase 8 (run
// layer) consume it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2Content
    {
        /// <summary>Folder of the export under GameContent/Unity and under Unity Resources.</summary>
        public const string Folder = "Core2";
        public const string ManifestPath = "manifest.json";

        public Core2Manifest Manifest { get; }
        /// <summary>profile.json: the resolved profile snapshot without its content tables (modules, schemas, lock, hash).</summary>
        public JObject Profile { get; }
        /// <summary>mechanics.json: fixCoreMechanics, stanceRules, core2ActionRulesVersion.</summary>
        public JObject Mechanics { get; }
        public IReadOnlyList<string> ModuleIds { get; }

        /// <summary>Every card a Core 2 profile defines (core, base and enemy cards; JS enemyCardsMerged).</summary>
        public IReadOnlyList<Core2Card> Cards { get; }
        public IReadOnlyList<Core2Card> EnemyCards { get; }
        /// <summary>Legacy cards the adapter relics, events and flasks name (for example a curse an event adds).</summary>
        public IReadOnlyList<Core2Card> AdapterCards { get; }
        public IReadOnlyList<Core2Class> Classes { get; }
        public IReadOnlyList<Core2Character> Characters { get; }
        public IReadOnlyList<Core2Gear> Gear { get; }
        public IReadOnlyList<Core2Row> EnemyRelics { get; }
        /// <summary>Enemies as the Core 2 runtime registry compiles them (moves included).</summary>
        public IReadOnlyList<Core2Enemy> Enemies { get; }
        public IReadOnlyList<Core2Row> Relics { get; }
        public IReadOnlyList<Core2Row> Events { get; }
        public IReadOnlyList<Core2Row> Flasks { get; }
        public IReadOnlyList<Core2Row> Statuses { get; }
        public IReadOnlyList<Core2Row> Encounters { get; }
        public IReadOnlyList<Core2Row> Seats { get; }
        public IReadOnlyList<Core2Row> Armaments { get; }
        public IReadOnlyList<Core2Row> Armour { get; }
        public Core2Routes Routes { get; }
        public JObject Rewards { get; }
        public JObject Shops { get; }
        public JObject Rests { get; }
        public JObject StarterDecks { get; }
        public JObject ClassUnlocks { get; }
        public JObject Stats { get; }

        private readonly Dictionary<string, JToken> _tables = new Dictionary<string, JToken>(StringComparer.Ordinal);
        private readonly Dictionary<string, Core2Card> _cards;

        /// <summary>Load and validate the export. <paramref name="readText"/> returns the text of a path relative to the export folder (for example "pack/cards.json").</summary>
        public static Core2Content Load(Func<string, string> readText)
        {
            if (readText == null) throw new ArgumentNullException(nameof(readText));
            return new Core2Content(readText);
        }

        /// <summary>Load from a folder on disk (GameContent/Unity/Core2, or a test copy).</summary>
        public static Core2Content LoadFolder(string folder) => Load(path => File.ReadAllText(System.IO.Path.Combine(folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar)), new UTF8Encoding(false)));

        private Core2Content(Func<string, string> readText)
        {
            Manifest = new Core2Manifest(readText(ManifestPath) ?? throw new InvalidDataException("Core2 export: manifest.json is missing"));
            foreach (var file in Manifest.Files)
            {
                var text = readText(file.Path) ?? throw new InvalidDataException("Core2 export: " + file.Path + " is missing");
                if (!HashMatches(text, file.Sha256))
                    throw new InvalidDataException("Core2 export: " + file.Path + " does not match its manifest sha256; regenerate with tools/export-js-core2.mjs, never edit by hand");
                _tables[file.Path] = Core2Json.Parse(text, file.Path);
            }

            Profile = Object("profile.json");
            Mechanics = Object("mechanics.json");
            if ((string)Profile["hash"] != Manifest.ProfileHash) throw new InvalidDataException("profile.json hash differs from manifest.profile.hash");
            if ((int?)Mechanics["core2ActionRulesVersion"] != Manifest.Core2ActionRulesVersion) throw new InvalidDataException("mechanics.json core2ActionRulesVersion differs from the manifest");
            ModuleIds = ((Profile["modules"] as JObject) ?? throw new InvalidDataException("profile.json: modules is required")).Properties().Select(p => p.Name).ToList();

            Cards = Rows(Array("pack/cards.json"), row => new Core2Card(row, "pack/cards"));
            EnemyCards = Rows(Array("pack/enemyCards.json"), row => new Core2Card(row, "pack/enemyCards"));
            AdapterCards = Rows(Array("adapter/cards.json"), row => new Core2Card(row, "adapter/cards"));
            Classes = Rows(Array("pack/classes.json"), row => new Core2Class(row));
            Characters = Rows(Array("pack/characters.json"), row => new Core2Character(row));
            Gear = Rows(Array("pack/gear.json"), row => new Core2Gear(row));
            EnemyRelics = Rows(Array("pack/enemyRelics.json"), row => new Core2Row(row, "pack/enemyRelics"));
            Enemies = Rows(Array("adapter/enemies.json"), row => new Core2Enemy(row));
            Relics = Rows(Array("adapter/relics.json"), row => new Core2Row(row, "adapter/relics"));
            Flasks = Rows(Array("adapter/flasks.json"), row => new Core2Row(row, "adapter/flasks"));
            Statuses = Rows((JArray)Object("adapter/statuses.json")["statuses"], row => new Core2Row(row, "adapter/statuses"));
            Events = Rows((JArray)Object("adapter/events.json")["events"], row => new Core2Row(row, "adapter/events"));
            var encounters = Object("adapter/encounters.json");
            Encounters = Rows((JArray)encounters["encounters"], row => new Core2Row(row, "adapter/encounters", nameRequired: false));
            Seats = Rows((JArray)encounters["seats"], row => new Core2Row(row, "adapter/seats"));
            var equipment = Object("adapter/equipment.json");
            Armaments = Rows((JArray)equipment["armaments"], row => new Core2Row(row, "adapter/equipment.armaments"));
            Armour = Rows((JArray)equipment["armour"], row => new Core2Row(row, "adapter/equipment.armour", nameRequired: false), unique: false);
            Routes = new Core2Routes(Object("pack/routes.json"));
            Rewards = Object("pack/rewards.json");
            Shops = Object("pack/shops.json");
            Rests = Object("pack/rests.json");
            StarterDecks = Object("pack/starterDeck.json");
            ClassUnlocks = Object("pack/classUnlocks.json");
            Stats = Object("pack/stats.json");

            _cards = Cards.Concat(AdapterCards).ToDictionary(c => c.Id, StringComparer.Ordinal);
            Validate();
        }

        /// <summary>Raw JSON of an exported file by its manifest path, for tables without a typed view yet.</summary>
        public JToken Table(string path) => _tables.TryGetValue(path, out var token) ? token : throw new KeyNotFoundException("Core2 export has no " + path);
        public bool HasModule(string id) => ModuleIds.Contains(id);
        public Core2Card Card(string id) => _cards.TryGetValue(id, out var card) ? card : throw new KeyNotFoundException("Unknown Core 2 card " + id);
        public bool HasCard(string id) => id != null && _cards.ContainsKey(id);
        public Core2Class Class(string id) => Classes.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("Unknown Core 2 class " + id);
        public Core2Enemy Enemy(string id) => Enemies.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException("Unknown Core 2 enemy " + id);

        /// <summary>
        /// The opening deck of a class, as JS gameProfileStarterDeck builds it: the class's own
        /// starterDeck.classDecks list when authored, else the shared list plus the class special.
        /// </summary>
        public IReadOnlyList<string> StarterDeck(string classId)
        {
            static IEnumerable<string> Expand(JArray rows) => rows.OfType<JObject>().SelectMany(row => Enumerable.Repeat((string)row["card"], (int?)row["copies"] ?? 0));
            if (StarterDecks["classDecks"]?[classId] is JArray own) return Expand(own).ToList();
            var special = (string)StarterDecks["classSpecials"]?[classId] ?? throw new InvalidDataException("starterDeck.classSpecials." + classId + ": missing card");
            var copies = (int?)StarterDecks["classSpecificCopies"] ?? 0;
            return Expand((JArray)StarterDecks["shared"] ?? new JArray()).Concat(Enumerable.Repeat(special, copies)).ToList();
        }

        private void Validate()
        {
            var problems = new List<string>();
            void Need(bool ok, string message) { if (!ok) problems.Add(message); }
            var gearIds = new HashSet<string>(Gear.Select(g => g.Id).Concat(Armaments.Select(a => a.Id)), StringComparer.Ordinal);
            var enemyCardIds = new HashSet<string>(EnemyCards.Select(c => c.Id), StringComparer.Ordinal);
            var enemyRelicIds = new HashSet<string>(EnemyRelics.Select(r => r.Id), StringComparer.Ordinal);

            foreach (var id in EnemyCards.Select(c => c.Id)) Need(_cards.ContainsKey(id), "enemy card " + id + " is not in pack/cards.json (JS merges enemy cards into the profile cards)");
            foreach (var cls in Classes)
            {
                Need(HasCard(cls.SpecialCardId), "class " + cls.Id + " special " + cls.SpecialCardId + " is not a card");
                foreach (var id in cls.DefaultDeck) Need(HasCard(id), "class " + cls.Id + " defaultDeck card " + id + " is unknown");
                foreach (var id in StarterDeck(cls.Id)) Need(HasCard(id), "class " + cls.Id + " starter card " + id + " is unknown");
                Need(cls.RightHand == null || gearIds.Contains(cls.RightHand), "class " + cls.Id + " right hand " + cls.RightHand + " is not gear");
                Need(cls.LeftHand == null || gearIds.Contains(cls.LeftHand), "class " + cls.Id + " left hand " + cls.LeftHand + " is not gear");
                Need(Characters.Any(c => c.Id == cls.Id && c.IsPlayer), "class " + cls.Id + " has no player character row");
            }
            foreach (var character in Characters)
                foreach (var gear in character.DefaultGear) Need(gearIds.Contains(gear.Value), "character " + character.Id + " gear " + gear.Value + " is unknown");
            var packEnemies = Array("pack/enemies.json").OfType<JObject>().ToList();
            Need(packEnemies.Select(e => (string)e["id"]).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(Enemies.Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal)),
                "pack/enemies.json and adapter/enemies.json name different enemies");
            foreach (var enemy in packEnemies)
            {
                var id = (string)enemy["id"];
                foreach (var card in Core2Json.Strings(enemy["defaultDeck"])) Need(enemyCardIds.Contains(card), "enemy " + id + " deck card " + card + " is not an enemy card");
                foreach (var relic in ((enemy["defaultRelics"] as JArray) ?? new JArray()).OfType<JObject>()) Need(enemyRelicIds.Contains((string)relic["id"]), "enemy " + id + " relic " + (string)relic["id"] + " is unknown");
            }
            var enemyIds = new HashSet<string>(Enemies.Select(e => e.Id), StringComparer.Ordinal);
            var seatIds = new HashSet<string>(Seats.Select(s => s.Id), StringComparer.Ordinal);
            foreach (var encounter in Encounters)
            {
                foreach (var id in Core2Json.Strings(encounter.Raw["enemies"])) Need(enemyIds.Contains(id), "encounter " + encounter.Id + " enemy " + id + " is unknown");
                var seat = Core2Json.OptionalString(encounter.Raw, "seat");
                Need(seat == null || seatIds.Contains(seat), "encounter " + encounter.Id + " seat " + seat + " is unknown");
            }
            var armourKeys = new HashSet<string>(Armour.Select(a => Core2Json.OptionalString(a.Raw, "classId") + ":" + a.Id), StringComparer.Ordinal);
            var armamentIds = new HashSet<string>(Armaments.Select(a => a.Id), StringComparer.Ordinal);
            foreach (var group in ((Rewards["equipmentGroups"] as JObject) ?? new JObject()).Properties())
            {
                var family = (string)group.Value["family"];
                foreach (var member in Core2Json.Strings(group.Value["members"]))
                    Need(family == "armour" ? armourKeys.Contains(member) : armamentIds.Contains(member), "reward group " + group.Name + " member " + member + " is not " + family);
            }
            foreach (var id in Core2Json.Strings(Rewards["eligibility"]?["weaponArts"]?["cardIds"])) Need(HasCard(id), "reward weapon art " + id + " is not a card");
            foreach (var id in Core2Json.Strings(ClassUnlocks["initial"]).Concat(Core2Json.Strings(ClassUnlocks["sequence"])))
                Need(Classes.Any(c => c.Id == id), "class unlock " + id + " is not a class");
            if (problems.Count > 0) throw new InvalidDataException("Core2 export is inconsistent:\n" + string.Join("\n", problems));
        }

        private JObject Object(string path) => Table(path) as JObject ?? throw new InvalidDataException(path + ": expected a JSON object");
        private JArray Array(string path) => Table(path) as JArray ?? throw new InvalidDataException(path + ": expected a JSON array");
        private static IReadOnlyList<T> Rows<T>(JArray rows, Func<JObject, T> make, bool unique = true)
        {
            if (rows == null) throw new InvalidDataException("Core2 export: a table is missing");
            var objects = rows.Select(row => row as JObject ?? throw new InvalidDataException("Core2 export: a table row is not an object")).ToList();
            var list = objects.Select(make).ToList();
            if (unique)
            {
                var duplicate = objects.Select(row => (string)row["id"]).GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new InvalidDataException("Core2 export: duplicate id " + duplicate.Key);
            }
            return list;
        }

        internal static bool HashMatches(string text, string sha256)
        {
            if (Sha256Hex(text) == sha256) return true;
            // A Windows checkout or TextAsset may hand back CRLF; the export is LF-only.
            return text.IndexOf('\r') >= 0 && Sha256Hex(text.Replace("\r\n", "\n")) == sha256;
        }
        public static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
