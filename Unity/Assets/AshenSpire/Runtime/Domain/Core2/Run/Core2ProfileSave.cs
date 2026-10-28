// Core2ProfileSave.cs — the profile-level Core 2 save (port plan phase 10): the class unlock ledger, kept apart from any
// run save. A run save is replaced at every checkpoint and deleted when the run ends; the ledger outlives every run.
//
// JS at the pin keeps the ledger on the profile meta, keyed by the game profile's id (meta.gameProfileUnlocks[<id>]
// .completedRunIds; src/model/gameProfileRun.js gameProfileClassAvailable / gameProfileCompletedRunMeta), not by the
// profile hash: a rules update keeps every class a player has unlocked. This save carries the same shape:
//
//   { format: "ashen.core2.profile", formatVersion: 1, gameProfileUnlocks: { core: { completedRunIds: [...] } } }
//
// Loading refuses, by name and without touching the stored text, unreadable text, another format and a newer format
// version (as Core2RunSave does); a profile id other than the build's is kept (it belongs to another profile) but reads
// as an empty ledger for this one.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2Profile
    {
        /// <summary>Every game profile's ledger, by profile id (JS meta.gameProfileUnlocks).</summary>
        public Dictionary<string, Core2UnlockLedger> Unlocks { get; } = new Dictionary<string, Core2UnlockLedger>(StringComparer.Ordinal);

        /// <summary>The ledger of a profile id (created empty on first use).</summary>
        public Core2UnlockLedger Ledger(string profileId)
        {
            if (!Unlocks.TryGetValue(profileId, out var ledger)) Unlocks[profileId] = ledger = new Core2UnlockLedger();
            return ledger;
        }

        /// <summary>The ledger the build's Core 2 profile reads (keyed by its id, e.g. "core").</summary>
        public Core2UnlockLedger Ledger(Core2Content content) => Ledger(ProfileId(content));

        public static string ProfileId(Core2Content content) => (string)content.Profile["id"] ?? throw new InvalidDataException("profile.json has no id");
    }

    public static class Core2ProfileSave
    {
        public const string Format = "ashen.core2.profile";
        public const int FormatVersion = 1;

        public static string Serialize(Core2Profile profile)
        {
            var unlocks = new JObject();
            foreach (var pair in profile.Unlocks.OrderBy(p => p.Key, StringComparer.Ordinal)) unlocks[pair.Key] = pair.Value.ToJson();
            return new JObject { ["format"] = Format, ["formatVersion"] = FormatVersion, ["gameProfileUnlocks"] = unlocks }.ToString(Formatting.None);
        }

        /// <summary>Load a profile save, or throw <see cref="Core2SaveRefused"/> naming why (unreadable, format, newer).</summary>
        public static Core2Profile Deserialize(string text)
        {
            JObject row;
            try { row = Core2Json.ParseObject(text, "Core 2 profile"); }
            catch (InvalidDataException error) { throw new Core2SaveRefused("unreadable", "The profile save is not readable: " + error.Message); }
            catch (JsonException error) { throw new Core2SaveRefused("unreadable", "The profile save is not readable: " + error.Message); }
            if ((string)row["format"] != Format) throw new Core2SaveRefused("format", "The save is not a Core 2 profile (format '" + (string)row["format"] + "').");
            var version = row["formatVersion"]?.Type == JTokenType.Integer ? (int)row["formatVersion"] : 0;
            if (version > FormatVersion) throw new Core2SaveRefused("newer", "The profile save was written by a newer build (format version " + version + ").");
            if (version < 1) throw new Core2SaveRefused("format", "The profile save has no format version.");
            if (!(row["gameProfileUnlocks"] is JObject unlocks)) throw new Core2SaveRefused("unreadable", "The profile save holds no unlock ledger.");
            var profile = new Core2Profile();
            foreach (var pair in unlocks.Properties())
            {
                if (!(pair.Value is JObject ledger) || !(ledger["completedRunIds"] is JArray ids) || ids.Any(id => id.Type != JTokenType.String || string.IsNullOrEmpty((string)id))
                    || ids.Select(id => (string)id).Distinct(StringComparer.Ordinal).Count() != ids.Count)
                    throw new Core2SaveRefused("unreadable", "The profile save's ledger for '" + pair.Name + "' must list unique finished run ids.");
                profile.Unlocks[pair.Name] = Core2UnlockLedger.FromJson(ledger);
            }
            return profile;
        }
    }
}
