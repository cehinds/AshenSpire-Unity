// OriginalModSources.cs — records and file access for original content mod packs.
// The loader never touches a platform file API directly: Unity passes a source over
// StreamingAssets/Mods, tests pass OriginalModMemorySource. Paths always use '/'.
// Errors are data (OriginalModError); nothing in this file throws for bad mod input.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AshenSpire.Domain.Original
{
    /// <summary>Read-only view of a mods folder. Names are immediate children, never full paths.</summary>
    public interface IOriginalModFileSource
    {
        IReadOnlyList<string> Directories(string path);
        IReadOnlyList<string> Files(string path);
        /// <summary>Returns the file text, or null when the file does not exist.</summary>
        string ReadText(string path);
    }

    /// <summary>In-memory mods folder keyed by '/'-separated relative path (tests, WebGL prefetch).</summary>
    public sealed class OriginalModMemorySource : IOriginalModFileSource
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        public OriginalModMemorySource Add(string path, string text) { _files[Normalize(path)] = text ?? ""; return this; }
        public IReadOnlyList<string> Directories(string path) => Children(path).Where(x => x.Nested).Select(x => x.Name).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        public IReadOnlyList<string> Files(string path) => Children(path).Where(x => !x.Nested).Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        public string ReadText(string path) => _files.TryGetValue(Normalize(path), out var text) ? text : null;
        private IEnumerable<(string Name, bool Nested)> Children(string path)
        {
            var prefix = Normalize(path); prefix = prefix.Length == 0 ? "" : prefix + "/";
            foreach (var key in _files.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = key.Substring(prefix.Length); var slash = rest.IndexOf('/');
                yield return slash < 0 ? (rest, false) : (rest.Substring(0, slash), true);
            }
        }
        internal static string Normalize(string path) => (path ?? "").Replace('\\', '/').Trim('/');
    }

    /// <summary>Plain System.IO source for desktop builds, the editor and .NET tests.
    /// Android/WebGL StreamingAssets are not a file system; copy them into a memory source first.</summary>
    public sealed class OriginalModDirectorySource : IOriginalModFileSource
    {
        private readonly string _root;
        public OriginalModDirectorySource(string root) { _root = root ?? throw new ArgumentNullException(nameof(root)); }
        private string Full(string path) { var relative = OriginalModMemorySource.Normalize(path); return relative.Length == 0 ? _root : Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)); }
        public IReadOnlyList<string> Directories(string path) { var full = Full(path); return Directory.Exists(full) ? Directory.GetDirectories(full).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray() : Array.Empty<string>(); }
        public IReadOnlyList<string> Files(string path) { var full = Full(path); return Directory.Exists(full) ? Directory.GetFiles(full).Select(Path.GetFileName).Where(x => !x.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToArray() : Array.Empty<string>(); }
        public string ReadText(string path) { var full = Full(path); return File.Exists(full) ? File.ReadAllText(full) : null; }
    }

    /// <summary>Parsed mod.json. Only accepted fields are kept; unknown fields are refused.</summary>
    public sealed class OriginalModManifest
    {
        public string Id; public string Name; public string Version; public string GameVersionMin; public int LoadOrder;
        public string[] DependsOn = Array.Empty<string>(); public string Description; public string Author;
        /// <summary>Folder the manifest was read from, relative to the mods root.</summary>
        public string Folder;
        public override string ToString() => Id + "@" + Version;
    }

    /// <summary>Stable error codes; UI copy and docs/MODDING.md key off these strings.</summary>
    public static class OriginalModErrorCodes
    {
        public const string BaseContentInvalid = "base-content-invalid";
        public const string ManifestMissing = "manifest-missing";
        public const string ManifestInvalid = "manifest-invalid";
        public const string DuplicateMod = "duplicate-mod";
        public const string GameVersionTooOld = "game-version-too-old";
        public const string MissingDependency = "missing-dependency";
        public const string DependencyCycle = "dependency-cycle";
        public const string DependencyRejected = "dependency-rejected";
        public const string JsonInvalid = "json-invalid";
        public const string UnknownTable = "unknown-table";
        public const string RecordInvalid = "record-invalid";
        public const string DuplicateRecord = "duplicate-record";
        public const string SchemaMismatch = "schema-mismatch";
        public const string RemoveUnknown = "remove-unknown";
        public const string ValidationFailed = "validation-failed";
    }

    public sealed class OriginalModError
    {
        public string Code; public string ModId; public string Path; public string Message;
        public OriginalModError(string code, string modId, string path, string message) { Code = code; ModId = modId; Path = path; Message = message; }
        public override string ToString() => "[" + Code + "] " + (string.IsNullOrEmpty(ModId) ? "" : ModId + ": ") + (string.IsNullOrEmpty(Path) ? "" : Path + ": ") + Message;
    }

    public sealed class OriginalModLoadResult
    {
        /// <summary>Validated catalog: base content plus every accepted pack. Null only when base content itself is invalid.</summary>
        public OriginalContentCatalog Catalog;
        /// <summary>content.json-shaped document behind Catalog. With no accepted pack this is the base string, unchanged.</summary>
        public string ContentJson;
        /// <summary>Accepted packs in the order they were applied.</summary>
        public IReadOnlyList<OriginalModManifest> Loaded = Array.Empty<OriginalModManifest>();
        /// <summary>IDs (or folder names) of packs that were refused; see Errors for why.</summary>
        public IReadOnlyList<string> Rejected = Array.Empty<string>();
        public IReadOnlyList<OriginalModError> Errors = Array.Empty<OriginalModError>();
        /// <summary>One line per applied change, e.g. "sample-ember-pack override cards/shieldBash".</summary>
        public IReadOnlyList<string> Changes = Array.Empty<string>();
        public bool Succeeded => Catalog != null && Errors.Count == 0;
    }
}
