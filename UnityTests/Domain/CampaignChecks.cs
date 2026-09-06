using AshenSpire.Domain;
using System.Text.Json;
public static class CampaignChecks
{
    public static void Run(string root)
    {
        var json=File.ReadAllText(Path.Combine(root,"GameContent/Unity/campaign.json"));
        var options=new JsonSerializerOptions{IncludeFields=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
        CampaignDefinition Content()=>JsonSerializer.Deserialize<CampaignDefinition>(json,options)!;
        var passed=0;
        void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}throw new Exception("FAIL: expected rejection "+name);}
        var content=Content();content.Validate();
        var run=new CampaignSession(content,"reaver",42);
        Check(!run.Play(0)&&!run.EndTurn()&&!run.Reward(null),"campaign commands require the correct phase");
        Check(!run.Enter(99)&&run.Enter(1)&&run.Enemy.Id=="blightHound","route chooses the authored foe");
        var snapshot=JsonSerializer.Serialize(run.State,options);
        var resumed=new CampaignSession(Content(),JsonSerializer.Deserialize<CampaignState>(snapshot,options)!);
        run.EndTurn();resumed.EndTurn();
        Check(JsonSerializer.Serialize(run.State,options)==JsonSerializer.Serialize(resumed.State,options),"campaign resume preserves intent, piles, RNG and next turn");
        run.State.Hand=new(){"venom"};run.State.Energy=3;run.State.EnemyHealth=3;run.State.EnemyBlock=3;
        run.Play(0);Check(run.State.EnemyHealth==3&&run.State.EnemyPoison==3,"block absorbs direct damage while poison is applied");
        var health=run.State.Health;run.EndTurn();Check(run.State.Phase==RunPhase.Reward&&run.State.Health==health,"lethal poison prevents the enemy action");
        Check(!run.Reward("strike"),"unoffered rewards are rejected");
        var reward=run.State.Rewards[0];Check(run.Reward(reward)&&run.State.Deck.Contains(reward)&&!run.Reward(reward),"reward claimed once and added to deck");
        run.State.Cinders=100;var before=run.State.Cinders;
        Check(run.Buy("emberBlade")&&!run.Buy("emberBlade")&&run.State.Cinders==before-30,"equipment costs once and cannot be duplicated");
        run.State.Strength=99;
        Check(run.Describe(run.Card("strike"))=="Deal 8 damage.","map card text includes equipment and excludes expired combat strength");
        run.Enter(0);run.State.Hand=new(){"strike"};run.State.Energy=3;var enemyHealth=run.State.EnemyHealth;run.Play(0);
        Check(run.State.EnemyHealth==enemyHealth-8,"equipment composes damage by attack tag");
        Check(!run.Buy("goldenHeart")&&!run.Rest()&&!run.RemoveCard(0),"map services cannot be used during combat");
        run.State.Health-=20;var potions=run.State.Potions;Check(run.DrinkPotion()&&run.State.Potions==potions-1,"flask heals and consumes a charge");
        run.State.Health=0;run.State.Phase=RunPhase.Defeat;Check(!run.Play(0)&&!run.EndTurn()&&!run.DrinkPotion(),"defeat closes combat commands");
        var bad=Content();bad.Encounters[0].Options[0]="missing";Reject(()=>bad.Validate(),"dangling encounter references rejected");
        bad=Content();bad.Equipment[0].RequiredTag="missing";Reject(()=>bad.Validate(),"unknown equipment queries rejected");
        var broken=JsonSerializer.Deserialize<CampaignState>(snapshot,options)!;broken.Route=99;Reject(()=>new CampaignSession(Content(),broken),"invalid saved route rejected");
        var totalWins=0;
        foreach(var hero in content.Heroes)
        {
            var wins=0;
            for(uint seed=1;seed<=12;seed++)
            {
                var campaign=new CampaignSession(Content(),hero.Id,seed);
                for(var step=0;step<3000&&campaign.State.Phase!=RunPhase.Victory&&campaign.State.Phase!=RunPhase.Defeat;step++)
                {
                    var s=campaign.State;
                    if(s.Phase==RunPhase.Map){foreach(var item in new[]{"emberBlade","ironward","goldenHeart","duelistEdge","pilgrimMail"})campaign.Buy(item);if(s.Health<s.MaxHealth-16)campaign.Rest();campaign.Enter(0);}
                    else if(s.Phase==RunPhase.Reward)campaign.Reward(null);
                    else
                    {
                        if(s.Health<=s.MaxHealth-20)campaign.DrinkPotion();
                        var index=s.Hand.FindIndex(id=>campaign.Card(id).Cost<=s.Energy&&campaign.Card(id).Effects.Any(x=>x.Operation=="strength"));
                        if(index<0)index=s.Hand.FindIndex(id=>campaign.Card(id).Cost<=s.Energy&&campaign.Card(id).HasTag("attack"));
                        if(index<0)index=s.Hand.FindIndex(id=>campaign.Card(id).Cost<=s.Energy);
                        if(index>=0)campaign.Play(index);else campaign.EndTurn();
                    }
                }
                Check(campaign.State.Phase==RunPhase.Victory||campaign.State.Phase==RunPhase.Defeat,$"{hero.Name} seed {seed} terminates");
                if(campaign.State.Phase==RunPhase.Victory){wins++;Check(campaign.State.FoesDefeated==content.Encounters.Length,"victory requires all nine encounters");}
            }
            Check(wins>0,hero.Name+" can complete all three acts");totalWins+=wins;
        }
        Console.WriteLine($"Campaign: {passed} checks passed; {totalWins}/48 policy runs won. Not a human fun assessment.");
    }
}
