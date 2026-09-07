// TagCatalog.cs — normalized family/scope/object/tag joins from original content.
// Tags are gameplay data, never Unity tags. Armour IDs are scoped by class; preserve
// the entire key. Callers receive copies so a UI cannot edit the authoritative index.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class TagCatalog
    {
        private readonly Dictionary<string, string> _scopeFields = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<(string Family, string Scope, string Id), List<string>> _index = new Dictionary<(string, string, string), List<string>>();
        public TagCatalog(JObject content)
        {
            var domains = new HashSet<string>(content["tagDomains"].Select(x => (string)x["id"]), StringComparer.Ordinal);
            var tags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in content["tags"])
            {
                var id = (string)tag["id"]; var domain = (string)tag["domain"];
                if (string.IsNullOrWhiteSpace(id) || !domains.Contains(domain) || tags.ContainsKey(id)) throw new ArgumentException("Invalid tag: " + id);
                tags.Add(id, domain);
            }
            var objects = new HashSet<(string, string, string)>();
            foreach (var family in content["tagFamilies"])
            {
                var name = (string)family["family"];
                var scope = (string)family["scopeField"] ?? "";
                _scopeFields.Add(name, scope);
                var source = (string)family["source"];
                if (string.IsNullOrEmpty(source)) continue;
                var rows = content.SelectToken(source) as JArray ?? throw new ArgumentException("Missing family source: " + source);
                foreach (var record in rows)
                {
                    var id = (string)record["id"];
                    var key = (name, scope == "" ? "" : (string)record[scope], id);
                    if (string.IsNullOrWhiteSpace(id) || !objects.Add(key)) throw new ArgumentException("Duplicate or empty object in " + source + ": " + id);
                }
            }
            var allowed = new HashSet<(string, string)>();
            foreach (var row in content["tagFamilyDomains"])
            {
                var family = (string)row["family"]; var domain = (string)row["domain"];
                if (!_scopeFields.ContainsKey(family) || !domains.Contains(domain) || !allowed.Add((family, domain))) throw new ArgumentException("Invalid tag family/domain pair.");
            }
            foreach (var row in content["tagging"])
            {
                var family = (string)row["family"]; var scope = (string)row["scope"] ?? ""; var id = (string)row["objectId"]; var tag = (string)row["tagId"];
                var key = (family, scope, id);
                if (!objects.Contains(key) || !tags.TryGetValue(tag, out var domain) || !allowed.Contains((family, domain))) throw new ArgumentException("Invalid tagging row: " + row.ToString(Newtonsoft.Json.Formatting.None));
                if (!_index.TryGetValue(key, out var values)) _index[key] = values = new List<string>();
                if (values.Contains(tag)) throw new ArgumentException("Duplicate tagging row: " + tag);
                values.Add(tag);
            }
        }
        public string[] For(string family, JObject record)
        {
            if (!_scopeFields.TryGetValue(family, out var scopeField)) throw new ArgumentException("Unknown tag family: " + family);
            var scope = string.IsNullOrEmpty(scopeField) ? "" : (string)record[scopeField] ?? "";
            return _index.TryGetValue((family, scope, (string)record["id"]), out var values) ? values.ToArray() : Array.Empty<string>();
        }
    }
}
