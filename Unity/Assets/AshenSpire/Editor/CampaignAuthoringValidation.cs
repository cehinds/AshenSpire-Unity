// Shared editor preflight for both authoring saves and build imports.
// Gameplay validation lives in the domain; this layer verifies referenced sprite files.
using System;
using System.IO;
using System.Linq;
using AshenSpire.Domain;

namespace AshenSpire.Editor
{
    public static class CampaignAuthoringValidation
    {
        public static void Validate(CampaignDefinition content, string artDirectory)
        {
            if (content == null) throw new ArgumentException("The campaign definition is empty.");
            content.Validate();
            foreach (var hero in content.Heroes) RequireName(hero.Name, "Heroes/" + hero.Id);
            foreach (var encounter in content.Encounters) RequireName(encounter.Name, "Encounters/" + encounter.Id);
            foreach (var item in content.Equipment) RequireName(item.Name, "Equipment/" + item.Id);
            var names = content.Heroes.SelectMany(hero => new[] { "idle", "attack1", "attack2", "guard", "hit" }.Select(pose => hero.Art + "_" + pose))
                .Concat(content.Foes.Select(foe => foe.Art)).Concat(content.Encounters.Select(encounter => encounter.Background)).Concat(content.Equipment.Select(item => item.Art));
            var root = Path.GetFullPath(artDirectory) + Path.DirectorySeparatorChar;
            foreach (var name in names.Distinct())
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A referenced sprite name is empty.");
                var path = Path.GetFullPath(Path.Combine(root, name + ".png"));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new ArgumentException("Missing Resources/Art/" + name + ". Supply the sprite before saving.");
            }
        }
        private static void RequireName(string name, string record)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(record + "/Name is required for the player UI.");
        }
    }
}
