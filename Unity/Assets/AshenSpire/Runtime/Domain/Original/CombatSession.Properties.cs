// Mounts derive from saved carrier identity. Trigger gates survive swaps and reloads.
using System;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private JObject _propertyInput,_propertyMounts;
        private void SetupProperties(JObject input)
        {
            if(!OriginalPropertyCarriers.Enabled(_mechanics))return;
            _propertyInput=OriginalPropertyCarriers.Input(input);
            _propertyMounts=new OriginalPropertyCarriers(_content).Build(_propertyInput);
            foreach(var mount in _propertyMounts.Properties())foreach(var rule in (JArray)mount.Value["rules"])
                foreach(var hook in rule["triggers"] as JArray ?? new JArray())
                { if(hook["if"] is JObject predicate)ValidatePredicate(predicate);ValidateEffects(hook["do"]); }
        }
        private void RestoreProperties(JToken input)
        {
            if(!OriginalPropertyCarriers.Enabled(_mechanics))return;
            SetupProperties(input as JObject ?? throw new ArgumentException("Missing saved property carrier state."));
        }
        private void PropertyHooks(JObject ev)
        {
            if(_propertyMounts==null)return;
            foreach(var mount in _propertyMounts.Properties())
            {
                var index=0;
                foreach(var rule in (JArray)mount.Value["rules"])foreach(var hook in rule["triggers"] as JArray ?? new JArray())
                {
                    var key="property:player:"+mount.Name+":"+index++;
                    if((string)hook["on"]!=(string)ev["type"]||!Fire(key,(JObject)hook,_player,ev))continue;
                    if((string)mount.Value["kind"]=="relic" && (string)ev["type"]!="relicTriggered")Emit("relicTriggered",new JObject{["relicId"]=mount.Value["id"]});
                }
            }
        }
        private double PropertyMultiplier(string key)=>OriginalPropertyCarriers.Multiplier(_propertyMounts,key);
    }
}
