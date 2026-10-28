// Core2Json2.cs — small JSON readers for the Core 2 presentation (the domain's Core2Json is internal to its assembly).
// Engine-free.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    public static class Core2Json2
    {
        /// <summary>The strings of a JSON array (null or a non-array reads as empty; non-strings are skipped).</summary>
        public static List<string> Strings(JToken token) =>
            token is JArray array ? array.Where(t => t.Type == JTokenType.String).Select(t => (string)t).ToList() : new List<string>();
        public static bool IsNull(JToken token) => token == null || token.Type == JTokenType.Null;
        public static int Int(JToken token, int fallback = 0) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) ? (int)token : fallback;
        public static string Str(JToken token) => token != null && token.Type == JTokenType.String ? (string)token : null;
    }
}
