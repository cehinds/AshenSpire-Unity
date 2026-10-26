// Core2Resources.cs — runtime reader for the JS Core 2 export mirrored under Resources/Core2
// (written by tools/export-js-core2.mjs and by "AshenSpire > 1. Validate and Import Content").
// Not wired into gameplay yet (SPEC.md §0.9: phase 5 combat, phase 8 run layer read it).
using AshenSpire.Domain.Core2;
using UnityEngine;

namespace AshenSpire.Application
{
    public static class Core2Resources
    {
        private static Core2Content _cached;

        /// <summary>The validated Core 2 export, loaded once. Throws InvalidDataException when the mirror is missing or altered.</summary>
        public static Core2Content Load()
        {
            if (_cached != null) return _cached;
            _cached = Core2Content.Load(path =>
            {
                // Resources paths drop the extension: "pack/cards.json" -> "Core2/pack/cards".
                var resource = Core2Content.Folder + "/" + (path.EndsWith(".json") ? path.Substring(0, path.Length - 5) : path);
                var asset = Resources.Load<TextAsset>(resource);
                return asset != null ? asset.text : null;
            });
            return _cached;
        }
    }
}
