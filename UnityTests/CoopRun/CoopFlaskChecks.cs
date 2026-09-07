// CoopFlaskChecks.cs — item-owned tier resolution at the party flask boundary.
// The pinned catalog has no tier that increases flaskPowerMult. Verify existing
// tiers preserve real healing, and invalid forged tiers fail without consumption.
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
internal static class CoopFlaskChecks
{
 public static int Run(OriginalContentCatalog catalog,JObject mechanics)
 {
  var checks=0;var upgrades=new ItemUpgradeService(catalog);
  JObject Player(string id,JArray relics,JObject levels)=>new JObject{["id"]=id,["name"]=id,["classId"]="reaver",["hp"]=1,["maxHp"]=80,["mana"]=2,["maxMana"]=2,["stamina"]=2,["maxStamina"]=2,["energyMax"]=3,["drawPerTurn"]=1,["deck"]=new JArray(new JObject{["instanceId"]="c1",["cardId"]="strike",["upgraded"]=false}),["relicIds"]=relics,["itemUpgradeLevels"]=levels,["flasks"]=new JArray(),["flaskCharges"]=new FlaskChargePool(3,3,0).Snapshot(),["attributes"]=new JObject(),["weights"]=new JObject()};
  OriginalCoopCombat Combat(JObject player)=>new OriginalCoopCombat(catalog,mechanics,new RandomStreams(1),new[]{player,Player("guest",new JArray(),new JObject())},new[]{"graveWisp"},(id,card)=>catalog.Record("cards",(string)card["cardId"]));
  int Heal(JObject player){var game=Combat(player);game.UseFlask("host",-1,"host","hp");return(int)game.Players[0]["entity"]["hp"]-1;}
  var baseline=Heal(Player("host",new JArray("crackedTear"),new JObject()));
  foreach(var relic in catalog.Table("relics"))
  {
   var id=(string)relic["id"];var maximum=upgrades.MaximumTier("relic/"+id);if(maximum==0)continue;
   var plain=Heal(Player("host",new JArray("crackedTear",id),new JObject()));
   for(var tier=1;tier<=maximum;tier++){var healed=Heal(Player("host",new JArray("crackedTear",id),new JObject{["relic/"+id]=tier}));if(healed!=plain)throw new Exception("Existing non-flask tier changed flask power");checks++;}
  }
  var forged=Combat(Player("host",new JArray("crackedTear"),new JObject{["relic/crackedTear"]=1}));var saved=forged.Snapshot();bool rejected=false;
  try{forged.UseFlask("host",-1,"host","hp");}catch(ArgumentException){rejected=true;}
  if(!rejected||!JToken.DeepEquals(saved,forged.Snapshot()))throw new Exception("Invalid item tier consumed a flask or mutated combat");checks++;
  if(baseline<=Heal(Player("host",new JArray(),new JObject())))throw new Exception("Authored Cracked Tear passive did not improve actual healing");checks++;
  return checks;
 }
}
