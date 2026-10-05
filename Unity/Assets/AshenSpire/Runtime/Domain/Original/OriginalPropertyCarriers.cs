// Derive held property rules from frozen content and each combat seat's kit.
// Definitions are not duplicated in combat saves; the native run already freezes content.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalPropertyCarriers
    {
        private readonly OriginalContentCatalog _catalog;
        private readonly JObject _data;
        private readonly Dictionary<string,JObject> _rules = new Dictionary<string,JObject>(StringComparer.Ordinal);
        public OriginalPropertyCarriers(OriginalContentCatalog catalog)
        {
            _catalog=catalog; _data=catalog.Data();
            var table=_data["propertyRules"] as JArray ?? throw new ArgumentException("Mounted properties require a propertyRules array.");
            foreach(var token in table)
            {
                var row=token as JObject ?? throw new ArgumentException("Each property rule must be an object.");
                var tag=(string)row["tag"];
                if(string.IsNullOrEmpty(tag) || _rules.ContainsKey(tag)) throw new ArgumentException("Duplicate or missing property rule tag.");
                _rules.Add(tag,(JObject)row.DeepClone());
            }
        }
        public static bool Enabled(JObject mechanics)
        {
            var value=mechanics?["properties"]?["mounted"];
            if(value==null)return false;
            if(value.Type!=JTokenType.Boolean)throw new ArgumentException("properties.mounted must be true or false.");
            return (bool)value;
        }
        public static JObject Input(JObject player)
        {
            var result=new JObject();
            foreach(var key in new[]{"classId","class","classUnequipped","coreTags","skills","loadout","itemUpgradeLevels","relicIds","relics","companionIds","sigilSlots","attunedSigils"})
                if(player[key]!=null)result[key]=player[key].DeepClone();
            if(result["companionIds"]==null && player["companions"] is JArray companions)result["companionIds"]=new JArray(companions.Select(row=>row["id"]?.DeepClone()));
            return result;
        }
        private string[] Tags(string family,JObject row)
        {
            if(row["propertyTags"] is JArray explicitTags)return explicitTags.Values<string>().ToArray();
            var scope=family=="armor"?(string)row["classId"]??"":"";
            return (_data["tagging"] as JArray ?? new JArray()).Where(t=>(string)t["family"]==family && (string)t["objectId"]==(string)row["id"] && ((string)t["scope"]??"")==scope)
                .Select(t=>(string)t["tagId"]).Where(_rules.ContainsKey).ToArray();
        }
        public JArray Rules(IEnumerable<string> tags)
        {
            var ordered=tags.ToArray();var held=ordered.ToHashSet();var result=new JArray();
            foreach(var tag in ordered)
            {
                if(!_rules.TryGetValue(tag,out var rule))throw new ArgumentException("Unknown held property: "+tag);
                if((rule["requires"] as JArray ?? new JArray()).Values<string>().Any(t=>!held.Contains(t)))continue;
                if((rule["excludes"] as JArray ?? new JArray()).Values<string>().Any(held.Contains))continue;
                result.Add(rule.DeepClone());
            }
            return result;
        }
        public JObject Build(JObject input)
        {
            var mounted=new SortedDictionary<string,JObject>(StringComparer.Ordinal);
            void Add(string kind,JObject row,string instance=null,string heldBy=null,IEnumerable<string> tags=null,JArray scopeTags=null)
            {
                var id=(string)row["id"];var family=kind=="armour"?"armor":kind;
                var rules=Rules(tags??Tags(family,row));if(rules.Count==0)return;
                var key=kind+":"+(instance??id);
                if(mounted.ContainsKey(key))return; // The same item may occupy two hands; it still confers once.
                var mount=new JObject{["kind"]=kind,["id"]=id,["instanceId"]=instance??id,["rules"]=rules,["scopeTags"]=scopeTags??new JArray()};
                if(heldBy!=null)mount["heldBy"]=heldBy;
                mounted.Add(key,mount);
            }
            var classId=(string)input["classId"]??(string)input["class"];
            var sigils=(_data["sigils"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(r=>(string)r["id"],r=>r,StringComparer.Ordinal);
            if(input["loadout"] is JObject loadout)
            {
                var kit=new WeaponLoadout(_catalog);var upgrades=new ItemUpgradeService(_catalog);
                foreach(var slot in _catalog.Table("equipment.slots"))
                {
                    var piece=kit.Equipped(loadout,classId,(string)slot["id"]);if(piece==null)continue;
                    var itemRef=WeaponLoadout.ItemRef(piece);
                    piece=upgrades.ResolveItem(itemRef,(int?)input["itemUpgradeLevels"]?[itemRef]??0);
                    Add((string)piece["kind"]=="armor"?"armour":"armament",piece,itemRef);
                    foreach(var token in input["sigilSlots"]?[itemRef] as JArray ?? new JArray())
                        if(token.Type==JTokenType.String && sigils.TryGetValue((string)token,out var sigil))Add("sigil",sigil,heldBy:itemRef);
                }
            }
            foreach(var id in (input["relicIds"] as JArray ?? input["relics"] as JArray ?? new JArray()).Values<string>())Add("relic",_catalog.Record("relics",id));
            if(classId!=null && (bool?)input["classUnequipped"]!=true)
            {
                var hero=_catalog.Record("classes",classId);var own=Tags("class",hero);
                var tree=(_data["classTree"] as JArray ?? new JArray()).Where(r=>(string)r["classId"]==classId).Select(r=>(string)r["nodeId"]).ToHashSet();
                var picked=(input["coreTags"] as JArray ?? new JArray()).Values<string>().Where(t=>tree.Contains(t)&&_rules.ContainsKey(t)&&!own.Contains(t));
                Add("class",hero,tags:own.Concat(picked),scopeTags:new JArray(_catalog.Tags("class",hero)));
            }
            var companions=(_data["companions"] as JArray ?? new JArray()).OfType<JObject>().ToDictionary(r=>(string)r["id"],r=>r,StringComparer.Ordinal);
            foreach(var id in (input["companionIds"] as JArray ?? new JArray()).Values<string>())if(companions.TryGetValue(id,out var companion))Add("companion",companion);
            foreach(var id in (input["attunedSigils"] as JArray ?? new JArray()).Values<string>())if(sigils.TryGetValue(id,out var sigil)&&(string)sigil["rarity"]=="legendary")Add("sigil",sigil);
            return new JObject(mounted.Select(p=>new JProperty(p.Key,p.Value)));
        }
        public static double Multiplier(JObject mounts,string key)
        {
            var value=1d;
            foreach(var mount in (mounts??new JObject()).Properties().OrderBy(p=>p.Name,StringComparer.Ordinal))
                foreach(var rule in mount.Value["rules"] as JArray ?? new JArray())
                { var n=rule["passives"]?[key];if(n?.Type==JTokenType.Integer||n?.Type==JTokenType.Float)value*=(double)n; }
            return value;
        }
        public static double Sum(JObject mounts,string key)
        {
            var value=0d;
            foreach(var mount in (mounts??new JObject()).Properties().OrderBy(p=>p.Name,StringComparer.Ordinal))
                foreach(var rule in mount.Value["rules"] as JArray ?? new JArray())
                { var n=rule["passives"]?[key];if(n?.Type==JTokenType.Integer||n?.Type==JTokenType.Float)value+=(double)n; }
            return value;
        }
    }
}
