// Core2Manifest.cs — the export manifest written by tools/export-js-core2.mjs
// (GameContent/Unity/Core2/manifest.json). It names the JS commit and build the data came
// from (SPEC.md §0.2), the resolved profile hash, and sha256/bytes/row counts per file.
// Engine-free: no UnityEngine; Newtonsoft only, like the Original catalog.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2ManifestFile
    {
        public string Path { get; }
        public string Sha256 { get; }
        public long Bytes { get; }
        /// <summary>Row counts per table in the file, as the exporter counted them.</summary>
        public IReadOnlyDictionary<string, int> Tables { get; }

        internal Core2ManifestFile(JObject row)
        {
            Path = Core2Json.RequiredString(row, "path", "manifest.files[]");
            Sha256 = Core2Json.RequiredString(row, "sha256", "manifest.files[" + Path + "]");
            Bytes = (long?)row["bytes"] ?? throw new InvalidDataException("manifest.files[" + Path + "].bytes is required");
            Tables = (row["tables"] as JObject)?.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, int>(StringComparer.Ordinal);
            if (Path.Contains("..") || Path.StartsWith("/", StringComparison.Ordinal) || !Path.EndsWith(".json", StringComparison.Ordinal))
                throw new InvalidDataException("manifest.files: invalid path " + Path);
            if (Sha256.Length != 64) throw new InvalidDataException("manifest.files[" + Path + "].sha256 must be 64 hex characters");
        }
    }

    public sealed class Core2Manifest
    {
        public const int SupportedSchemaVersion = 1;
        public const string ExpectedCore = "ashen.core@2.0.0";

        public int SchemaVersion { get; }
        public string JsRepository { get; }
        public string JsCommit { get; }
        public string JsBuild { get; }
        public string JsDigest { get; }
        public string ProfileId { get; }
        public string Core { get; }
        /// <summary>The resolved profile's rules hash (JS SPEC §16): a Core 2 save made under another hash refuses to load.</summary>
        public string ProfileHash { get; }
        public string RuntimeHash { get; }
        public int Core2ActionRulesVersion { get; }
        public IReadOnlyList<Core2ManifestFile> Files { get; }
        public IReadOnlyDictionary<string, int> Counts { get; }
        /// <summary>Effects the JS game runs as code (content/scripts.js); listed so a later phase ports each by hand.</summary>
        public IReadOnlyList<string> ScriptedEffects { get; }
        public JObject Raw { get; }

        public Core2Manifest(string json)
        {
            Raw = Core2Json.ParseObject(json, "manifest.json");
            SchemaVersion = (int?)Raw["schemaVersion"] ?? 0;
            if (SchemaVersion != SupportedSchemaVersion)
                throw new InvalidDataException("manifest.json schemaVersion " + SchemaVersion + " is not supported (expected " + SupportedSchemaVersion + ")");
            var js = Raw["js"] as JObject ?? throw new InvalidDataException("manifest.json: js is required");
            JsRepository = Core2Json.RequiredString(js, "repository", "manifest.js");
            JsCommit = Core2Json.RequiredString(js, "commit", "manifest.js");
            JsBuild = Core2Json.RequiredString(js, "build", "manifest.js");
            JsDigest = Core2Json.RequiredString(js, "digest", "manifest.js");
            if (JsCommit.Length != 40) throw new InvalidDataException("manifest.js.commit must be a full 40-character hash");
            var profile = Raw["profile"] as JObject ?? throw new InvalidDataException("manifest.json: profile is required");
            ProfileId = Core2Json.RequiredString(profile, "id", "manifest.profile");
            Core = Core2Json.RequiredString(profile, "core", "manifest.profile");
            ProfileHash = Core2Json.RequiredString(profile, "hash", "manifest.profile");
            RuntimeHash = Core2Json.RequiredString(profile, "runtimeHash", "manifest.profile");
            Core2ActionRulesVersion = (int?)profile["core2ActionRulesVersion"] ?? 0;
            if (Core != ExpectedCore) throw new InvalidDataException("manifest.profile.core is " + Core + "; the Unity game is " + ExpectedCore + " (SPEC.md §0.1)");
            Files = ((Raw["files"] as JArray) ?? throw new InvalidDataException("manifest.json: files is required"))
                .Select(row => new Core2ManifestFile((JObject)row)).ToList();
            var duplicate = Files.GroupBy(f => f.Path, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidDataException("manifest.files lists " + duplicate.Key + " twice");
            Counts = (Raw["counts"] as JObject)?.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, int>(StringComparer.Ordinal);
            ScriptedEffects = ((Raw["scriptedEffects"] as JArray) ?? new JArray()).Select(row => (string)row["script"]).Where(s => s != null).ToList();
        }

        public Core2ManifestFile File(string path) => Files.FirstOrDefault(f => f.Path == path)
            ?? throw new InvalidDataException("manifest.json does not list " + path);
        public int Count(string key) => Counts.TryGetValue(key, out var value) ? value : throw new InvalidDataException("manifest.counts has no " + key);
    }
}
