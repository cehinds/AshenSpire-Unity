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
        private readonly Dictionary<(string Family, string Scope, string Id), List<string>> _kinds = new Dictionary<(string, string, string), List<string>>();
        public TagCatalog(JObject content, JObject externalObjects = null)
        {
            var domains = new HashSet<string>(content["tagDomains"].Select(x => (string)x["id"]), StringComparer.Ordinal);
            var aside = new HashSet<string>(content["tagDomains"].Where(x => x["aside"]?.Type == JTokenType.Boolean && (bool)x["aside"]).Select(x => (string)x["id"]), StringComparer.Ordinal);
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
                if (string.IsNullOrEmpty(source))
                {
                    // Location IDs come from the frozen atlas/rest-service adapter,
                    // never from the tagging rows themselves. Unknown references
                    // remain errors rather than becoming invisible gameplay data.
                    if (externalObjects?[name] is JArray external)
                    {
                        if (name != "location" || scope != "") throw new ArgumentException("Unsupported external tag family: " + name);
                        foreach (var value in external)
                        {
                            var id = value.Type == JTokenType.String ? (string)value : null;
                            if (string.IsNullOrWhiteSpace(id) || !objects.Add((name, "", id))) throw new ArgumentException("Duplicate or empty external tag object.");
                        }
                    }
                    continue;
                }
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
            var seen = new HashSet<(string, string, string, string)>();
            foreach (var row in content["tagging"])
            {
                var family = (string)row["family"]; var scope = (string)row["scope"] ?? ""; var id = (string)row["objectId"]; var tag = (string)row["tagId"];
                var key = (family, scope, id);
                if (!objects.Contains(key) || !tags.TryGetValue(tag, out var domain) || !allowed.Contains((family, domain))) throw new ArgumentException("Invalid tagging row: " + row.ToString(Newtonsoft.Json.Formatting.None));
                if (!seen.Add((family, scope, id, tag))) throw new ArgumentException("Duplicate tagging row: " + tag);
                if (domain == "classification")
                {
                    if (!_kinds.TryGetValue(key, out var kinds)) _kinds[key] = kinds = new List<string>();
                    kinds.Add(tag);
                }
                if (aside.Contains(domain)) continue;
                if (!_index.TryGetValue(key, out var values)) _index[key] = values = new List<string>();
                values.Add(tag);
            }
        }
        public string[] For(string family, JObject record)
            => Read(_index, family, record);
        public string[] KindsFor(string family, JObject record)
            => Read(_kinds, family, record);
        private string[] Read(Dictionary<(string Family, string Scope, string Id), List<string>> index, string family, JObject record)
        {
            if (!_scopeFields.TryGetValue(family, out var scopeField)) throw new ArgumentException("Unknown tag family: " + family);
            var scope = string.IsNullOrEmpty(scopeField) ? "" : (string)record[scopeField] ?? "";
            return index.TryGetValue((family, scope, (string)record["id"]), out var values) ? values.ToArray() : Array.Empty<string>();
        }
    }
}
