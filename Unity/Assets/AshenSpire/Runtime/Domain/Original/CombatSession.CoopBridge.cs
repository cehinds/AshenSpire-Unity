// CombatSession.CoopBridge.cs — internal adapter for the original party loop.
// Shared enemies/RNG/events remain real domain state; each seat owns its body,
// piles and counters. Optional callbacks leave the solo engine path unchanged.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed partial class CombatSession
    {
        private Action _coopEndCheck;
        private Func<string,JObject> _coopFind;
        private Func<JObject,string> _coopMemberFor;
        private string _coopSeatKey;
        internal CombatSession(OriginalContentCatalog content,JObject mechanics,RandomStreams rng,JObject input,IEnumerable<JObject> deck,List<JObject> enemies,List<JObject> events,Dictionary<string,JObject> gates,Func<JObject,JObject> resolver,string seatKey,Action endCheck,Func<string,JObject> find,Func<JObject,string> memberFor,bool initialize)
            :this(content,mechanics,rng,MakePlayer(input),(JObject)(input["attributes"]?.DeepClone()??new JObject()),(JObject)(input["weights"]?.DeepClone()??new JObject()),resolver)
        {
            _events=events;_triggerState=gates;_enemies.AddRange(enemies);_coopSeatKey=seatKey;_coopEndCheck=endCheck;_coopFind=find;_coopMemberFor=memberFor;
            foreach(var school in new[]{"physical","magic","arcane","holy","fire"})if(_player["damageBySchoolAdd"][school]==null)_player["damageBySchoolAdd"][school]=0;
            if((int?)input["poiseMax"]>0)_player["poiseMeter"]=new JObject{["value"]=0,["max"]=input["poiseMax"].DeepClone()};
            if(!initialize)return;
            var cards=deck.Select(c=>(JObject)c.DeepClone()).ToArray();if(cards.Select(c=>(string)c["instanceId"]).Distinct().Count()!=cards.Length||cards.Any(c=>string.IsNullOrEmpty((string)c["instanceId"])))throw new ArgumentException("Duplicate or empty seat card identity.");
            foreach(var card in cards)ValidateEffects(ResolvedCard(card)["effects"]);
            var shuffled=_random.Shuffle("shuffle",cards);
            _piles["draw"].AddRange(shuffled.Where(c=>CardMechanics.HasProperty(CardMechanics.FromDefinition(ResolvedCard(c)),"lifecycle.innate")));
            _piles["draw"].AddRange(shuffled.Where(c=>!CardMechanics.HasProperty(CardMechanics.FromDefinition(ResolvedCard(c)),"lifecycle.innate")));
        }
        internal JObject CoopBody=>_player;
        internal int CoopCounter=>_idCounter;
        internal void CoopSync(int turn,string phase,string result,int counter){_turn=turn;_phase=phase;_result=result;_idCounter=counter;}
        internal void CoopResult(string result){_result=result;if(result!=null)_phase="ended";}
        internal void CoopEmit(string type,JObject payload)=>Emit(type,payload);
        internal void CoopDrain()=>Drain();
        internal void CoopTransferPendingTo(CombatSession destination)
        {
            if(ReferenceEquals(this,destination))return;_context.TransferPendingTo(destination._context);
            foreach(var entry in _metadata)destination._metadata.Add(entry.Key,entry.Value);_metadata.Clear();
            foreach(var entry in _carriers)destination._carriers.Add(entry.Key,entry.Value);_carriers.Clear();
        }
        internal void CoopQueue(JObject effect,JObject source,JObject target,JObject meta=null)=>Queue(effect,source,source,target,meta);
        internal void CoopHooks(JObject entity,string name)=>OwnerHooks(entity,name);
        internal bool CoopFlag(JObject entity,string name)=>_statuses.Flag(entity,name);
        internal void CoopDecay(JObject entity)=>_statuses.DecayAtTurnEnd(entity);
        internal void CoopIntents(bool first=false)=>RollIntents(first);
        internal void CoopDraw()=>Draw((int)_player["drawPerTurn"]);
        internal double CoopFlaskPower()=>((JArray)_player["relicIds"]).Aggregate(1d,(value,id)=>value*((double?)new ItemUpgradeService(_content).ResolveItem("relic/" + (string)id,(int?)_player["itemUpgradeLevels"]?["relic/" + (string)id] ?? 0)["passives"]?["flaskPowerMult"]??1));
        internal string CoopChargeId(string kind)=>(string)_content.Table("flasks").OfType<JObject>().First(f=>FlaskKind(f)==kind)["id"];
        internal JObject CoopSnapshot(){var saved=Snapshot();return new JObject{["player"]=saved["player"].DeepClone(),["piles"]=saved["piles"].DeepClone(),["catchBreathUses"]=saved["catchBreathUses"].DeepClone()};}
        internal void CoopStartTurn()
        {
            _catchBreathUses=0;_player["counters"]["cardsPlayedThisTurn"]=0;_player["counters"]["staminaSpentThisTurn"]=0;
            if(!_statuses.Flag(_player,"retainBlock"))_player["block"]=0;else{var cap=BlockCap(_player);if(cap.HasValue)_player["block"]=Math.Min((int)_player["block"],cap.Value);}
            _player["energy"]=_player["energyMax"].DeepClone();Draw((int)_player["drawPerTurn"]);Emit("playerTurnStart",new JObject{["turn"]=_turn,["playerId"]=_coopSeatKey});OwnerHooks(_player,"ownerTurnStart");Drain();
        }
        internal void CoopEndTurn()
        {
            Emit("playerTurnEnd",new JObject{["turn"]=_turn,["playerId"]=_coopSeatKey});OwnerHooks(_player,"ownerTurnEnd");Drain();if(_result!=null)return;
            _statuses.DecayAtTurnEnd(_player);var wallet=Wallet();var before=(int)_player["stamina"];wallet.EndTurn();CopyWallet(wallet);
            if((int)_player["stamina"]!=before)Emit("staminaRecovered",new JObject{["amount"]=(int)_player["stamina"]-before,["reason"]="idle",["playerId"]=_coopSeatKey});
            _player["counters"]["staminaSpentThisTurn"]=0;var exhaust=new List<JObject>();var discard=new List<JObject>();
            foreach(var card in _piles["hand"].ToArray()){var fate=CardMechanics.EndTurnFate(CardMechanics.FromDefinition(ResolvedCard(card)));if(fate=="keep")continue;_piles["hand"].Remove(card);(fate=="exhaust"?exhaust:discard).Add(card);}
            foreach(var card in exhaust){_piles["exhaust"].Add(card);CardEvent("cardExhausted",card,"ethereal");}foreach(var card in discard){_piles["discard"].Add(card);CardEvent("cardDiscarded",card,"turnEnd");}_player["energy"]=0;Drain();
        }
        internal void CoopRestore(JObject snapshot)
        {
            _player.RemoveAll();foreach(var p in ((JObject)snapshot["player"]).Properties())_player[p.Name]=p.Value.DeepClone();
            var ids=new HashSet<string>();foreach(var pile in _piles.Keys){_piles[pile].Clear();foreach(var card in (JArray)snapshot["piles"][pile]){var copy=(JObject)card.DeepClone();if(!ids.Add((string)copy["instanceId"]))throw new ArgumentException("Duplicate saved seat card.");ResolvedCard(copy);_piles[pile].Add(copy);}}
            _catchBreathUses=(int?)snapshot["catchBreathUses"]??0;Wallet();
        }
    }
}

