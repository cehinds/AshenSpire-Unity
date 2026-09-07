// CoopPanelState.cs — transient presentation choices for one shared-run room.
// Never save this object to the host, and never mutate a host view to retain UI.
// Reconcile on each snapshot; room changes reset choices and removed cards/targets
// are forgotten. Opening a deck/equipment page does not pause the shared simulation.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Presentation
{
    public sealed class CoopPanelState
    {
        private string _roomKey;
        public string SelectedCard, Target, RewardCard;
        public string Surface = "main";
        public bool TakeRelic = true, TakeFlask;
        public int HandPage;
        public void Reconcile(JObject view)
        {
            var local=view["local"] as JObject; var run=local?["run"] as JObject;
            var scene=view["scene"] as JObject; var catchup=(local?["catchup"] as JArray)?.FirstOrDefault() as JObject;
            var key=new JArray(run?["runId"],local?["id"],view["actNumber"],view["cursorId"],scene?["kind"],scene?["eventId"],catchup?["id"]).ToString(Newtonsoft.Json.Formatting.None);
            if(key!=_roomKey){_roomKey=key;SelectedCard=Target=RewardCard=null;Surface="main";TakeRelic=true;TakeFlask=false;HandPage=0;}
            var hand=(local?["hand"] as JArray??new JArray()).OfType<JObject>().ToArray();
            var selected=hand.FirstOrDefault(card=>(string)(card["instance"] as JObject)?["instanceId"]==SelectedCard);
            if(selected==null)SelectedCard=null;
            var targets=selected?["targets"] as JObject;
            var legal=(bool?)targets?["active"]==true
                ?(targets["legalIds"] as JArray??new JArray()).Values<string>()
                :(scene?["enemies"] as JArray??new JArray()).OfType<JObject>().Where(enemy=>(bool?)enemy["alive"]==true).Select(enemy=>(string)enemy["id"]);
            if(!legal.Contains(Target))Target=null;
            HandPage=Math.Max(0,Math.Min(HandPage,Math.Max(0,(hand.Length-1)/4)));
            var offer=catchup!=null?catchup["offer"] as JObject:(scene?["offers"] as JObject)?[(string)local?["id"]??""] as JObject;
            if(!(offer?["cards"] as JArray??new JArray()).Values<string>().Contains(RewardCard))RewardCard=null;
            if(Surface=="flasks"&&(string)scene?["kind"]!="combat")Surface="main";
        }
    }
}
