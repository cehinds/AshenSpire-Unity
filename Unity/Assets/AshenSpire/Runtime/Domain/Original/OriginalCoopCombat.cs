// OriginalCoopCombat.cs — authoritative shared fight, ported from coopCombat.js.
// Transport submits member commands; only this model owns shared enemies, RNG,
// turn readiness and seat bodies/piles. Snapshots are resumable model state.
// Each seat reuses CombatSession's real card/effect/status interpreter. Lobby,
// run rewards and revival between rooms belong to the containing game session.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCoopCombat
    {
        private sealed class Seat { public string Id,Name,ClassId;public JObject Input;public CombatSession Core;public bool Connected=true,Ended; }
        private readonly OriginalContentCatalog _catalog;private readonly JObject _mechanics;private readonly Func<string,JObject,JObject> _resolve;
        private RandomStreams _random;private readonly List<JObject> _enemies=new List<JObject>(),_events=new List<JObject>();
        private readonly Dictionary<string,JObject> _gates=new Dictionary<string,JObject>();private readonly List<Seat> _seats=new List<Seat>();
        private readonly HashSet<string> _hostRejoin=new HashSet<string>(); private bool _annotateMembers; private Seat _active;private int _turn,_counter;private string _phase="setup",_result;private double _factor,_baseMultiplier,_extraMultiplier;
        public string Phase=>_phase;public string Result=>_result;public int Turn=>_turn;
        public static double HpMultiplier(int headcount,double factor=.6)=>1+factor*Math.Max(0,headcount-1);
        public JArray Enemies=>new JArray(_enemies.Select(e=>e.DeepClone()));
        public JArray Players=>new JArray(_seats.Select(s=>new JObject{["id"]=s.Id,["name"]=s.Name,["classId"]=s.ClassId,["connected"]=s.Connected,["ended"]=s.Ended,["entity"]=s.Core.Player,["piles"]=s.Core.Snapshot()["piles"].DeepClone()}));
        public JObject Card(string memberId,JObject instance)=>SeatFor(memberId).Core.ResolvedCard(instance);
        public JObject Cost(string memberId,JObject instance)=>SeatFor(memberId).Core.CardCost(instance);
        private OriginalCoopCombat(OriginalContentCatalog catalog,JObject mechanics,Func<string,JObject,JObject> resolve)
        { _catalog=catalog;_mechanics=(JObject)mechanics.DeepClone();_resolve=resolve??throw new ArgumentNullException(nameof(resolve)); }
        public OriginalCoopCombat(OriginalContentCatalog catalog,JObject mechanics,RandomStreams random,IEnumerable<JObject> players,IEnumerable<string> enemyIds,Func<string,JObject,JObject> resolve,double extraHpMultiplier=1,JArray enemyStatuses=null,bool annotateMembers=false):this(catalog,mechanics,resolve)
        {
            _annotateMembers=annotateMembers;_random=random;var inputs=players.Select(p=>(JObject)p.DeepClone()).ToArray();if(inputs.Length==0||inputs.Any(p=>string.IsNullOrEmpty((string)p["id"]))||inputs.Select(p=>(string)p["id"]).Distinct().Count()!=inputs.Length)throw new ArgumentException("Party requires unique member IDs.");
            _factor=(double?)catalog.Data()["balance"]["coop"]?["headcountHpFactor"]??.6;if(_factor==0)_factor=.6;
            if(extraHpMultiplier<=0||double.IsNaN(extraHpMultiplier)||double.IsInfinity(extraHpMultiplier))throw new ArgumentException("Invalid party HP multiplier.");
            _extraMultiplier=extraHpMultiplier;_baseMultiplier=HpMultiplier(inputs.Length,_factor)*_extraMultiplier;
            foreach(var id in enemyIds)
            {
                var def=catalog.Record("enemies",id);var hp=Math.Max(1,Round(_random.Int("enemyHP",(int)def["hp"][0],(int)def["hp"][1])*_baseMultiplier));
                var e=new JObject{["id"]="e"+(_enemies.Count+1),["kind"]="enemy",["enemyId"]=id,["hp"]=hp,["maxHp"]=hp,["block"]=0,["statuses"]=new JObject(),["poiseMeter"]=new JObject{["value"]=0,["max"]=def["poiseMax"].DeepClone()},["movesHistory"]=new JArray(),["intent"]=null,["pendingMove"]=null,["skipNextTurn"]=false,["unlockedMoves"]=new JArray(),["alive"]=true};
                if(def["arcaneExposure"] is JObject exposure){e["arcaneExposure"]=exposure.DeepClone();if((string)exposure["mode"]=="configured")e["arcaneExposure"]["value"]=0;}if(def["damageResistanceBySchool"]!=null)e["damageResistanceBySchool"]=def["damageResistanceBySchool"].DeepClone();_enemies.Add(e);
            }
            if(_enemies.Count==0)throw new ArgumentException("Party combat requires enemies.");
            foreach(var input in inputs)Add(input,true);
            foreach(var seat in Living())Use(seat,c=>c.CoopEmit("combatStart",new JObject()));
            foreach(var seat in _seats)foreach(var status in seat.Input["startStatuses"] as JArray??new JArray())Use(seat,c=>c.CoopQueue(new JObject{["op"]="applyStatus",["target"]="self",["status"]=status["status"].DeepClone(),["stacks"]=status["stacks"]?.DeepClone()??new JValue(1)},c.CoopBody,c.CoopBody));
            foreach(var enemy in _enemies)foreach(var status in enemyStatuses??new JArray())Use(First(),c=>c.CoopQueue(new JObject{["op"]="applyStatus",["target"]="self",["status"]=status["status"].DeepClone(),["stacks"]=status["stacks"].DeepClone()},enemy,enemy));
            Use(_active??First(),c=>c.CoopDrain());
            Use(_active??First(),c=>c.CoopIntents(true));if(_result==null)StartPlayerPhase();
        }
        private static int Round(double n)=>checked((int)Math.Floor(n+.5));
        private static bool Alive(JObject e)=>(bool?)e?["alive"]==true;
        private Seat SeatFor(string id)=>_seats.FirstOrDefault(s=>s.Id==id)??throw new ArgumentException("Unknown party member "+id);
        private Seat[] Living()=>_seats.Where(s=>s.Connected&&Alive(s.Core.CoopBody)).ToArray();
        private Seat First()=>Living().FirstOrDefault()??_seats.First();
        private JObject Find(string id)=>_seats.FirstOrDefault(s=>s.Id==id)?.Core.CoopBody;
        private string MemberFor(JObject body)=>_seats.FirstOrDefault(s=>ReferenceEquals(s.Core.CoopBody,body))?.Id;
        private void Use(Seat seat,Action<CombatSession> action)
        { _active?.Core.CoopTransferPendingTo(seat.Core);_active=seat;seat.Core.CoopSync(_turn,_phase,_result,_counter);var eventStart=_events.Count;action(seat.Core);_counter=seat.Core.CoopCounter;if(_annotateMembers)foreach(var ev in _events.Skip(eventStart)){if(ev["actorMemberId"]==null)ev["actorMemberId"]=seat.Id;if(new[]{"damageDealt","hpLost","healed"}.Contains((string)ev["type"])&&(string)ev["targetId"]=="player"&&ev["playerId"]==null)ev["playerId"]=seat.Id;} }
        private Seat Add(JObject input,bool initial)
        {
            var id=(string)input["id"];if(string.IsNullOrEmpty(id))throw new ArgumentException("Missing member identity.");
            var seat=new Seat{Id=id,Name=(string)input["name"]??id,ClassId=(string)input["classId"],Input=(JObject)input.DeepClone()};
            seat.Core=new CombatSession(_catalog,_mechanics,_random,input,((JArray)input["deck"]).OfType<JObject>(),_enemies,_events,_gates,c=>_resolve(id,c),id,EndCheck,Find,MemberFor,true);_seats.Add(seat);
            if(!initial){if(_phase=="player")Use(seat,c=>{c.CoopBody["energy"]=c.CoopBody["energyMax"].DeepClone();c.CoopDraw();});Rescale();}return seat;
        }
        private void EndCheck()
        {
            if(_result!=null)return;
            foreach(var seat in _seats)if(Alive(seat.Core.CoopBody)&&(int)seat.Core.CoopBody["hp"]<=0){seat.Core.CoopBody["alive"]=false;_active.Core.CoopEmit("playerDowned",new JObject{["playerId"]=seat.Id});}
            if(!Living().Any())Finish("defeat");else if(_enemies.Count>0&&_enemies.All(e=>!Alive(e)))Finish("victory");
        }
        private void Finish(string result)
        { _result=result;_phase="ended";foreach(var seat in _seats)seat.Core.CoopResult(result);_active.Core.CoopEmit("combatEnd",new JObject{["victory"]=result=="victory"}); }
        private void Rescale()
        {
            var multiplier=HpMultiplier(Math.Max(1,Living().Length),_factor)*_extraMultiplier;var ratio=multiplier/_baseMultiplier;if(Math.Abs(ratio-1)<1e-9)return;
            foreach(var enemy in _enemies.Where(Alive)){enemy["maxHp"]=Math.Max(1,Round((int)enemy["maxHp"]*ratio));enemy["hp"]=Math.Max(1,Math.Min((int)enemy["maxHp"],Round((int)enemy["hp"]*ratio)));}_baseMultiplier=multiplier;
        }
        private JArray Change(Action command)
        {
            var before=Snapshot();var count=_events.Count;try{command();return new JArray(_events.Skip(count).Select(e=>e.DeepClone()));}catch{RestoreState(before);throw;}
        }
        private Seat Actor(string id,bool requireUnended=true)
        { MaterializeHostPresence();if(_result!=null||_phase!="player")throw new InvalidOperationException("Party command requires an active player phase.");var seat=SeatFor(id);if(!seat.Connected||!Alive(seat.Core.CoopBody)||requireUnended&&seat.Ended)throw new ArgumentException("Member cannot act.");return seat; }
        public JObject FriendlyTargets(string memberId,JObject definition)
        {
            var effects=definition["effects"] as JArray??new JArray();var hostile=effects.Any(e=>new[]{"enemy","allEnemies","randomEnemy"}.Contains((string)e["target"]));var ally=effects.Any(e=>(string)e["target"]=="ally");var self=effects.Any(e=>(string)e["target"]=="self");var mode=hostile||!ally&&!self?"none":ally&&self?"mixed":ally?"ally":"self";
            var targets=new JArray();if(mode!="none")foreach(var seat in Living()){var relation=seat.Id==memberId?"self":"ally";if(mode=="self"&&relation!="self"||mode=="ally"&&relation!="ally")continue;targets.Add(new JObject{["id"]=seat.Id,["relationship"]=relation});}
            return new JObject{["mode"]=mode,["active"]=mode!="none",["targets"]=targets,["legalIds"]=new JArray(targets.Select(t=>t["id"].DeepClone()))};
        }
        public JArray Play(string memberId,string instanceId,string targetId=null)=>Change(()=>
        {
            var seat=Actor(memberId);var instance=seat.Core.Hand.OfType<JObject>().FirstOrDefault(c=>(string)c["instanceId"]==instanceId)??throw new ArgumentException("Card is not in this member's hand.");var plan=FriendlyTargets(memberId,seat.Core.ResolvedCard(instance));
            if((bool)plan["active"]){if(targetId==null&&(string)plan["mode"]=="self")targetId=memberId;if(!plan["legalIds"].Values<string>().Contains(targetId))throw new ArgumentException("Invalid friendly target.");}
            Use(seat,c=>c.PlayCard(instanceId,targetId));
        });
        public JArray EndTurn(string memberId)=>Change(()=>
        { MaterializeHostPresence();if(_result!=null||_phase!="player")throw new InvalidOperationException("No player phase.");var seat=_seats.FirstOrDefault(s=>s.Id==memberId);if(seat==null||seat.Ended)return;Use(seat,c=>c.CoopEndTurn());seat.Ended=true;MaybeEndPhase(); });
        public JArray Leave(string memberId)=>Change(()=>
        { MaterializeHostPresence();var seat=_seats.FirstOrDefault(s=>s.Id==memberId);if(seat==null)return;seat.Connected=false;seat.Ended=true;Rescale();if(!Living().Any()){_phase="suspended";return;}MaybeEndPhase(); });
        public JArray Join(JObject player)=>Change(()=>
        { var seat=_seats.FirstOrDefault(s=>s.Id==(string)player["id"]);if(seat==null){MaterializeHostPresence();Add(player,false);return;}if(_hostRejoin.Remove(seat.Id)){seat.Connected=true;return;}seat.Connected=true;seat.Core.CoopBody["alive"]=(int)seat.Core.CoopBody["hp"]>0;Rescale();if(_phase=="suspended"){_phase="player";StartPlayerPhase();} });
        public JArray UseFlask(string memberId,int slot,string targetId=null,string chargeKind=null)=>Change(()=>
        {
            var actor=Actor(memberId,false);var body=actor.Core.CoopBody;var charges=body["flaskCharges"] as JObject;string flaskId=null;
            if(chargeKind!=null){if(chargeKind!="hp"&&chargeKind!="mana")throw new ArgumentException("Unknown charge kind.");flaskId=actor.Core.CoopChargeId(chargeKind);if(charges==null||(int)charges[chargeKind+"Current"]<=0)throw new ArgumentException("No charges.");}
            else {var slots=(JArray)body["flasks"];if(slot<0||slot>=slots.Count)throw new ArgumentException("No utility flask in slot.");flaskId=(string)slots[slot]["flaskId"];}
            var def=_catalog.Record("flasks",flaskId);var ally=_seats.FirstOrDefault(s=>s.Id==targetId);var thrown=ally!=null&&(bool?)def["targeted"]!=true&&Alive(ally.Core.CoopBody);var recipient=thrown?ally:actor;JObject enemyTarget=null;
            if(!thrown){if(targetId!=null&&ally==null){enemyTarget=_enemies.FirstOrDefault(e=>(string)e["id"]==targetId);if(!Alive(enemyTarget))throw new ArgumentException("Invalid flask target.");}else if((bool?)def["targeted"]==true)enemyTarget=_enemies.FirstOrDefault(Alive);}
            if(chargeKind!=null)charges[chargeKind+"Current"]=(int)charges[chargeKind+"Current"]-1;else ((JArray)body["flasks"]).RemoveAt(slot);
            var power=actor.Core.CoopFlaskPower();Use(recipient,c=>{c.CoopEmit(thrown?"flaskThrown":"flaskUsed",new JObject{["flaskId"]=flaskId,["slot"]=slot,["from"]=memberId,["to"]=thrown?targetId:(string)enemyTarget?["id"]});foreach(var effect in def["effects"]??new JArray())c.CoopQueue((JObject)effect,c.CoopBody,enemyTarget??c.CoopBody,power==1?null:new JObject{["amountMult"]=power});c.CoopDrain();});
        });
        private void StartPlayerPhase()
        { _turn++;_phase="player";foreach(var seat in Living()){seat.Ended=false;Use(seat,c=>c.CoopStartTurn());if(_result!=null)return;} }
        private void MaybeEndPhase()
        { if(_result!=null||_phase!="player")return;var living=Living();if(living.Length>0&&living.All(s=>s.Ended)){EnemyPhase();if(_result!=null)return;Use(_active??First(),c=>c.CoopIntents());StartPlayerPhase();} }
        private void EnemyPhase()
        {
            _phase="enemy";Use(_active??First(),c=>c.CoopEmit("enemyTurnStart",new JObject{["turn"]=_turn}));foreach(var enemy in _enemies)if(Alive(enemy)&&!_active.Core.CoopFlag(enemy,"retainBlock"))enemy["block"]=0;Use(First(),c=>c.CoopDrain());if(_result!=null)return;
            foreach(var enemy in _enemies)
            {
                if(_result!=null)return;if(!Alive(enemy))continue;Use(First(),c=>{c.CoopHooks(enemy,"ownerTurnStart");c.CoopDrain();});if(_result!=null||!Alive(enemy))continue;
                if((bool)enemy["skipNextTurn"]||_active.Core.CoopFlag(enemy,"skipTurn"))enemy["skipNextTurn"]=false;
                else if(enemy["pendingMove"] is JObject pending){if(_turn>=(int)pending["resolveOnTurn"]){var move=(string)pending["moveId"];enemy["pendingMove"]=null;Move(enemy,move);}}
                else if(enemy["intent"] is JObject intent&&(string)intent["moveId"]!=null)
                {
                    var moveId=(string)intent["moveId"];var move=_catalog.Record("enemies",(string)enemy["enemyId"])["moves"][moveId];
                    if(move["delay"] is JObject delay){var charging=delay["whileCharging"] as JObject??new JObject();if(charging["block"]!=null)Use(First(),c=>{c.CoopQueue(new JObject{["op"]="block",["target"]="self",["amount"]=charging["block"].DeepClone()},enemy,enemy);c.CoopDrain();});foreach(var effect in charging["effects"]??new JArray())EnemyEffect(enemy,(JObject)effect,null);enemy["pendingMove"]=new JObject{["moveId"]=moveId,["resolveOnTurn"]=_turn+((int?)delay["turns"]??1)};intent["pending"]=true;}
                    else Move(enemy,moveId);
                }
                if(_result!=null)return;if(Alive(enemy)){Use(First(),c=>{c.CoopHooks(enemy,"ownerTurnEnd");c.CoopDrain();if(_result==null&&Alive(enemy))c.CoopDecay(enemy);});if(_result!=null)return;}
            }
            Use(_active??First(),c=>c.CoopEmit("enemyTurnEnd",new JObject{["turn"]=_turn}));Use(First(),c=>c.CoopDrain());
        }
        private void Move(JObject enemy,string moveId)
        {
            var move=_catalog.Record("enemies",(string)enemy["enemyId"])["moves"][moveId];Use(_active??First(),c=>c.CoopEmit("enemyMoveStarted",new JObject{["sourceId"]=enemy["id"].DeepClone(),["enemyId"]=enemy["enemyId"].DeepClone(),["moveId"]=moveId,["kind"]=move["intent"].DeepClone()}));
            if(move["block"]!=null){Use(First(),c=>{c.CoopQueue(new JObject{["op"]="block",["target"]="self",["amount"]=move["block"].DeepClone()},enemy,enemy,new JObject{["moveId"]=moveId});c.CoopDrain();});if(_result!=null)return;}
            foreach(var seat in Living()){if(_result!=null)return;Use(seat,c=>{if(move["damage"]!=null){c.CoopQueue(new JObject{["op"]="damage",["target"]="player",["amount"]=move["damage"].DeepClone(),["hits"]=move["hits"]?.DeepClone()??new JValue(1)},enemy,c.CoopBody,new JObject{["moveId"]=moveId});c.CoopDrain();}});}
            foreach(var effect in move["effects"]??new JArray())EnemyEffect(enemy,(JObject)effect,moveId);
        }
        private void EnemyEffect(JObject enemy,JObject effect,string moveId)
        { foreach(var seat in (string)effect["target"]=="player"?Living():new[]{First()}){if(_result!=null)return;Use(seat,c=>{c.CoopQueue(effect,enemy,(string)effect["target"]=="player"?c.CoopBody:enemy,new JObject{["moveId"]=moveId});c.CoopDrain();});} }
        public void DisconnectForHostRestore(){foreach(var seat in _seats){if(seat.Connected)_hostRejoin.Add(seat.Id);seat.Connected=false;}}
        private void MaterializeHostPresence(){if(_hostRejoin.Count==0)return;_hostRejoin.Clear();Rescale();}
        public JObject Outcome()=>new JObject{["survivors"]=new JObject(_seats.Select(s=>new JProperty(s.Id,new JObject{["hp"]=Math.Max(0,(int)s.Core.CoopBody["hp"]),["downed"]=!Alive(s.Core.CoopBody)}))),["result"]=_result??(_phase=="suspended"?"suspended":null)};
        public JObject Snapshot()=>new JObject{["schemaVersion"]=1,["annotateMembers"]=_annotateMembers,["hostRejoin"]=new JArray(_hostRejoin.OrderBy(id=>id,StringComparer.Ordinal)),["seed"]=_random.Seed,["rng"]=JObject.FromObject(_random.Snapshot()),["turn"]=_turn,["phase"]=_phase,["result"]=_result,["idCounter"]=_counter,["activeMember"]=_active?.Id,["hpFactor"]=_factor,["baseHpMultiplier"]=_baseMultiplier,["extraHpMultiplier"]=_extraMultiplier,["enemies"]=Enemies,["events"]=new JArray(_events.Select(e=>e.DeepClone())),["gates"]=JObject.FromObject(_gates),["seats"]=new JArray(_seats.Select(s=>new JObject{["id"]=s.Id,["name"]=s.Name,["classId"]=s.ClassId,["connected"]=s.Connected,["ended"]=s.Ended,["input"]=s.Input.DeepClone(),["combat"]=s.Core.CoopSnapshot()}))};
        public static OriginalCoopCombat Restore(OriginalContentCatalog catalog,JObject mechanics,JObject snapshot,Func<string,JObject,JObject> resolve)
        {var restored=new OriginalCoopCombat(catalog,mechanics,resolve);restored.RestoreState(snapshot);return restored;}
        private void RestoreState(JObject snapshot)
        {
            if((int?)snapshot["schemaVersion"]!=1||!new[]{"setup","player","enemy","ended","suspended"}.Contains((string)snapshot["phase"]))throw new ArgumentException("Invalid co-op save.");
            _hostRejoin.Clear();foreach(var id in (snapshot["hostRejoin"] as JArray??new JArray()).Values<string>())_hostRejoin.Add(id);_annotateMembers=(bool?)snapshot["annotateMembers"]??false;_turn=(int)snapshot["turn"];_phase=(string)snapshot["phase"];_result=(string)snapshot["result"];_counter=(int)snapshot["idCounter"];
            _factor=(double)snapshot["hpFactor"];_baseMultiplier=(double)snapshot["baseHpMultiplier"];_extraMultiplier=(double)snapshot["extraHpMultiplier"];
            _random=new RandomStreams((uint)snapshot["seed"],((JObject)snapshot["rng"]).ToObject<Dictionary<string,uint>>());_enemies.Clear();_events.Clear();_gates.Clear();_seats.Clear();
            foreach(var enemy in (JArray)snapshot["enemies"])_enemies.Add((JObject)enemy.DeepClone());foreach(var ev in (JArray)snapshot["events"])_events.Add((JObject)ev.DeepClone());foreach(var gate in ((JObject)snapshot["gates"]).Properties())_gates.Add(gate.Name,(JObject)gate.Value.DeepClone());
            foreach(var row in (JArray)snapshot["seats"]){var id=(string)row["id"];if(_seats.Any(s=>s.Id==id))throw new ArgumentException("Duplicate saved member.");var input=(JObject)row["input"].DeepClone();var seat=new Seat{Id=id,Name=(string)row["name"],ClassId=(string)row["classId"],Connected=(bool)row["connected"],Ended=(bool)row["ended"],Input=input};seat.Core=new CombatSession(_catalog,_mechanics,_random,input,Array.Empty<JObject>(),_enemies,_events,_gates,c=>_resolve(id,c),id,EndCheck,Find,MemberFor,false);seat.Core.CoopRestore((JObject)row["combat"]);seat.Core.CoopSync(_turn,_phase,_result,_counter);_seats.Add(seat);}
            _active=_seats.FirstOrDefault(s=>s.Id==(string)snapshot["activeMember"]);
        }
    }
}



