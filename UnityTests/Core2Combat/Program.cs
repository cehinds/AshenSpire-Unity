// Core 2 combat domain checks (SPEC.md §0.9 phase 5a) for rules the JS oracle families do not reach.
// Run from the repository root: dotnet run --project UnityTests/Core2Combat [-- <repo root>]
// Every expected value below is worked from the JS function cited next to it (at the SPEC §0.2 pin) and the
// exported Core 2 data; the oracle replays live in UnityTests/Core2Content.
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

var root = args.Length > 0 ? args[0] : ".";
var checks = 0;
var failures = 0;
void Check(bool condition, string label)
{
    if (condition) { checks++; Console.WriteLine("PASS: " + label); }
    else { failures++; Console.Error.WriteLine("FAIL: " + label); }
}
void Equal<T>(T actual, T expected, string label) => Check(Equals(actual, expected), label + (Equals(actual, expected) ? "" : " (actual '" + actual + "', expected '" + expected + "')"));
bool Throws<TException>(Action action) where TException : Exception { try { action(); return false; } catch (TException) { return true; } catch (Exception) { return false; } }

Core2Combatant Player(string classId = "reaver", Core2Stance stance = null) => new()
{
    Id = "player", Side = Core2Side.Player, ClassId = classId, Hp = 70, MaxHp = 70, Stance = stance, Level = 1,
    Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["strength"] = 0, ["dexterity"] = 0, ["constitution"] = 0, ["wisdom"] = 0, ["intelligence"] = 0 },
};
Core2Combatant Enemy(Core2Stance stance = null) => new()
{
    Id = "enemy", Side = Core2Side.Enemy, EnemyId = "wanderingSoldier", Hp = 60, MaxHp = 60, Stance = stance, Level = 1,
    Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["dexterity"] = 0 },
};
Core2Carrier Carrier(string json) => Core2Carrier.FromJson(JObject.Parse(json));
var physicalAttack = "{type:'attack',combatProfile:{camp:'physical',maneuver:'attack',range:'melee'}}";
var physicalSmash = "{type:'attack',combatProfile:{camp:'physical',maneuver:'smash',range:'melee'}}";

try
{
    var content = Core2Content.LoadFolder(Path.Combine(root, "GameContent", "Unity", "Core2"));
    var ctx = Core2CombatContext.NewFight(content);
    var rules = ctx.Rules;

    // ---- Rules read from the export (content/source/fixCoreMechanics.json at the pin) -----------------------------
    Equal(rules.StanceEffectivenessMultiplier, 1.25, "matching stance ×1.25");
    Equal(rules.StanceDiscount, 1, "same-stance discount 1");
    Equal(rules.StanceMinimumCost, 1, "own-turn price floor 1");
    Equal(rules.VulnerabilityMultiplier, 1.25, "Vulnerable ×1.25");
    Equal(rules.WeakARMultiplier, 0.75, "Weak AR ×0.75");
    Equal(rules.StatusDecayAmount, 1, "statuses decay one stack");
    Equal(rules.PoiseProtectionMultiplier, 0.75, "Poise lets 75% through (defense.poiseProtection default)");
    Check(!rules.WardEnabled, "Ward is off in Core 2");
    Equal(rules.StaggeredStaminaLoss, 1, "Staggered: -1 SP");
    Equal(rules.StaggeredVulnerableStacks, 1, "Staggered: +1 Vulnerable");
    Equal(rules.Status("staggered").StackMode, "add", "FixCore overrides staggered (adapter: refresh) to add");
    Equal(rules.Status("vulnerable").Decay, Core2StatusDecay.PerTurnEnd, "Vulnerable decays per turn");
    Check(rules.Status("silenced").Decay == Core2StatusDecay.Duration && rules.Status("silenced").Duration == 2, "Silenced is a 2-turn duration status");
    Equal(rules.ManaPerTurn, 0, "Core 2 restores no MP at turn start");
    Check(rules.Includes("sp") && rules.Includes("mp") && rules.Includes("hp"), "resources: HP, SP and MP lanes");

    // ---- Run snapshot and fight stamp (SPEC §0.1; JS §16, combat.js createCombat) ------------------------------
    var snapshot = Core2ProfileSnapshot.Take(content);
    Equal(snapshot.RulesHash, content.Manifest.ProfileHash, "run snapshot records the resolved profile hash");
    Equal(snapshot.Core, "ashen.core@2.0.0", "run snapshot records the core");
    Equal(snapshot.Core2ActionRulesVersion, 1, "run snapshot records core2ActionRulesVersion 1");
    Equal(snapshot.FixCoreVersion, 1, "run snapshot records FixCore version 1");
    var restored = Core2ProfileSnapshot.FromJson(JObject.Parse(snapshot.ToJson().ToString()));
    Check(restored.Matches(content) && restored.JsCommit == content.Manifest.JsCommit, "a saved snapshot round-trips and matches this build");
    var foreign = snapshot.ToJson(); foreign["rulesHash"] = "sha256:" + new string('0', 64);
    var mismatch = Assert(() => Core2ProfileSnapshot.FromJson(foreign).RequireMatches(content));
    Check(mismatch is Core2ProfileMismatchException m1 && m1.Differences.Any(d => d.StartsWith("rules hash")), "a run saved under another rules hash is refused, named");
    var oldVersion = snapshot.ToJson(); oldVersion["snapshotVersion"] = 2;
    Check(Throws<InvalidDataException>(() => Core2ProfileSnapshot.FromJson(oldVersion)), "an unknown snapshot version is refused");
    Check(ctx.Stamp.Core2ActionRulesVersion == 1 && ctx.Stamp.ActionPolicyEnabled, "a new fight stamps core2ActionRulesVersion 1 and runs the action policy");
    Check(!new Core2FightStamp(rules, 0).ActionPolicyEnabled, "a fight without the stamp keeps the pre-policy pricing");
    Check(Throws<InvalidDataException>(() => new Core2FightStamp(rules, 2)), "a fight stamp other than 1 is refused (JS combatSnapshot.js)");
    var future = snapshot.ToJson(); future["core2ActionRulesVersion"] = 2;
    Check(Throws<Core2ProfileMismatchException>(() => Core2FightStamp.NewFight(Core2ProfileSnapshot.FromJson(future), rules)), "a run under another action rules version cannot start a fight");

    // ---- Stance identity of the Core cards (JS stanceOf) --------------------------------------------------------
    string StanceText(string cardId) => Core2Stances.StanceOf(Core2Carrier.FromCard(content.Card(cardId)))?.ToString() ?? "none";
    Equal(StanceText("core.strike"), "physical/attack/melee", "Strike plays in the physical Attack stance");
    Equal(StanceText("core.defend"), "physical/defend/melee", "Defend plays in the Defend stance");
    Equal(StanceText("core.cantrip"), "spell/attack/melee/mirrored", "Arcane Spark mirrors Attack and stays a Spell");
    Equal(StanceText("core.manaSurge"), "spell/casting/melee", "Mana Surge is a casting Spell");
    Equal(StanceText("core.evade"), "physical/evade/melee", "Evade enters the Evade stance (stanceTrigger)");
    Equal(Core2Stances.ConfiguredStance(new Core2Stance("casting", "spell", "reach")), "prepared", "a held casting stance reads as prepared");

    // ---- Pricing beyond the oracle rows -------------------------------------------------------------------------
    var prePolicy = new Core2CombatContext(new Core2FightStamp(rules, 0), content.Characters.ToDictionary(c => c.Id, c => c.Tags));
    var reaction = Carrier("{type:'attack',combatProfile:{camp:'physical',maneuver:'counter',range:'melee'},cardTags:['trait:reaction']}");
    var counterStance = Player("reaver", new Core2Stance("counter"));
    prePolicy.Phase = "enemy";
    Equal(Core2Pricing.StanceStaminaCost(prePolicy, 2, counterStance, reaction), 1, "pre-policy fight: a matching reaction on the enemy turn costs 1 less");
    Equal(Core2Pricing.StanceStaminaCost(prePolicy, 1, counterStance, reaction), 0, "pre-policy fight: that discount floors at 0");
    prePolicy.Phase = "player";
    Equal(Core2Pricing.StanceStaminaCost(prePolicy, 2, counterStance, reaction), 2, "pre-policy fight: no Adept discount on the player's turn");
    var xCard = Carrier("{type:'attack',cost:'X',combatProfile:{camp:'physical',maneuver:'attack',range:'melee'}}");
    var xPlayer = Player("reaver", new Core2Stance("attack")); xPlayer.Stamina = 3; xPlayer.Mana = 2;
    var xPaid = Core2Pools.PayFor(ctx, xPlayer, xCard);
    Check(xPaid.Sp == 3 && xPlayer.Stamina == 0 && xPlayer.Mana == 2, "an X card spends every SP, with no same-stance discount");
    var broke = Player("reaver"); broke.Stamina = 0; broke.Mana = 0;
    var strike = Core2Carrier.FromCard(content.Card("core.strike"));
    Check(Assert(() => Core2Pools.PayFor(ctx, broke, strike)) is Core2PlayRefusedException r1 && r1.Resource == "stamina" && r1.Need == 1 && broke.Stamina == 0, "a card the SP pool cannot pay is refused, named, and nothing is spent");
    broke.Stamina = 5;
    Check(Assert(() => Core2Pools.PayFor(ctx, broke, Core2Carrier.FromCard(content.Card("core.reaverSpecial")))) is Core2PlayRefusedException r2 && r2.Resource == "mana" && broke.Stamina == 5,
        "a card the MP pool cannot pay is refused before any SP is spent");

    // ---- SP/MP turn pools (JS combat.js startPlayerTurn) --------------------------------------------------------
    var pools = Player(); pools.MaxStamina = 3; pools.Stamina = 0; pools.MaxMana = 4; pools.Mana = 1; pools.PendingStaminaLoss = 1;
    Core2Pools.StartTurn(ctx, pools);
    Check(pools.Stamina == 2 && pools.PendingStaminaLoss == 0, "turn start refills SP less the Staggered loss owed to it");
    Equal(pools.Mana, 1, "turn start restores no MP (no resources.manaPerTurn)");
    Core2Pools.StartTurn(ctx, pools);
    Equal(pools.Stamina, 3, "the Staggered loss is owed to one turn only");

    // ---- Status stacking and FixCore decay (JS statuses.js) -----------------------------------------------------
    var holder = Player();
    Core2Statuses.Apply(ctx, holder, "vulnerable", 1); Core2Statuses.Apply(ctx, holder, "vulnerable", 1);
    Equal(holder.StatusStacks("vulnerable"), 2, "Vulnerable stacks add");
    Core2Statuses.Apply(ctx, holder, "silenced", 1); Core2Statuses.Apply(ctx, holder, "silenced", 1);
    Check(holder.StatusStacks("silenced") == 1 && holder.Statuses["silenced"].Duration == 2, "a refresh status keeps the larger stack and refreshes its duration");
    Core2Statuses.Apply(ctx, holder, "weak", 1);
    Core2Statuses.Apply(ctx, holder, "evade", 2);
    Core2Statuses.Apply(ctx, holder, "precision", 3);
    ctx.Turn = 1;
    Core2Statuses.DecayAtTurnStart(ctx, holder);
    Check(holder.StatusStacks("vulnerable") == 1 && !holder.Statuses.ContainsKey("weak") && holder.StatusStacks("evade") == 1 && holder.StatusStacks("precision") == 3
        && holder.Statuses["silenced"].Duration == 1, "turn start: -1 Vulnerable, Weak, Evade; Precision untouched; durations tick");
    Core2Statuses.DecayAtTurnStart(ctx, holder);
    Check(holder.StatusStacks("vulnerable") == 1 && holder.StatusStacks("evade") == 1, "decay runs once per round per owner");
    ctx.Turn = 2;
    Core2Statuses.DecayAtTurnStart(ctx, holder);
    Check(holder.StatusStacks("vulnerable") == 0 && holder.StatusStacks("evade") == 0 && !holder.Statuses.ContainsKey("silenced"), "the next round's decay removes the last stacks and the expired duration");
    var focus = Player();
    Core2Statuses.Apply(ctx, focus, "precision", 2, persistent: true);
    Core2Statuses.Apply(ctx, focus, "precision", 1);
    Core2Statuses.DecayAtTurnEnd(ctx, focus);
    Equal(focus.StatusStacks("precision"), 2, "turn end: temporary Precision expires, persistent Precision stays");

    // ---- Weak and stance ratings (JS combatRatings.js ratingValue, stanceRules.js stanceRatingBonus) --------------
    var rated = Player(); rated.Ratings["ar"] = 5; rated.Ratings["dr"] = 1;
    Equal(Core2Statuses.RatingValue(rules, rated, "ar"), 5, "AR without modifiers");
    rated.Statuses["weak"] = new Core2StatusInstance { Stacks = 1 };
    Equal(Core2Statuses.RatingValue(rules, rated, "ar"), 3, "Weak: floor(5 × 0.75) = 3");
    rated.Stance = new Core2Stance("attack");
    Equal(Core2Statuses.RatingValue(rules, rated, "ar"), 4, "Weak applies after the Attack stance's +1: floor(6 × 0.75) = 4");
    rated.Stance = new Core2Stance("defend");
    Equal(Core2Statuses.RatingValue(rules, rated, "dr"), 4, "Defend stance: +3 DR");
    rated.Stance = new Core2Stance("defend", "spell");
    Check(Core2Statuses.RatingValue(rules, rated, "dr") == 1 && Core2Stances.StanceRatingBonus(rules, rated, "ward") == 3, "a spell-camp defensive stance raises Ward, not DR");

    // ---- d20 + Precision against fixed Evasion (JS stanceHit.js) --------------------------------------------------
    var attack = Carrier(physicalAttack);
    Core2HitPreview Odds(int bonus, int dex, bool adv = false, bool dis = false)
    {
        var s = Player(); s.Level = 0; s.BasePrecisionBonus = bonus; s.AccuracyAdvantage = adv; s.AccuracyDisadvantage = dis;
        var t = Enemy(); t.Attributes["dexterity"] = dex;
        return Core2Accuracy.Preview(rules, s, t, attack);
    }
    var even = Odds(0, 11);
    Check(even.Need == 11 && even.HitChance == 50, "need 11 on a d20: 50%");
    Equal(Odds(0, 11, adv: true).HitChance, 75, "advantage enumerates 400 pairs: 75%");
    Equal(Odds(0, 11, dis: true).HitChance, 25, "disadvantage: 25%");
    Equal(Odds(0, 11, adv: true, dis: true).Mode, "normal", "advantage and disadvantage cancel");
    Equal(Odds(30, 0).HitChance, 95, "natural 1 always misses: 95% at best");
    Equal(Odds(-30, 6).HitChance, 5, "natural 20 always hits: 5% at worst");
    Equal(Odds(-30, 6, adv: true).HitChance, 9, "natural 20 with advantage: floor(39/400) = 9%");
    HitResultFor(new[] { 20, 5 }, 11, dis: true, expectHit: false, label: "disadvantage keeps the lower die: a 20 beside a 5 misses need 11");
    HitResultFor(new[] { 1, 19 }, 11, adv: true, expectHit: true, label: "advantage keeps the higher die: a 1 beside a 19 hits need 11");
    void HitResultFor(int[] faces, int dex, bool adv = false, bool dis = false, bool expectHit = false, string label = "")
    {
        var s = Player(); s.Level = 0; s.AccuracyAdvantage = adv; s.AccuracyDisadvantage = dis;
        var t = Enemy(); t.Attributes["dexterity"] = dex;
        var r = Core2Accuracy.Resolve(rules, s, t, attack, new Dice(faces));
        Check(r.Hit == expectHit && r.Dice.Count == 2, label);
    }
    var dodger = Enemy(); Core2Statuses.Apply(ctx, dodger, "evade", 1);
    var miss = Core2Accuracy.Resolve(rules, Player(), dodger, attack, new Dice(1));
    Check(!miss.Hit && miss.MissReason == "accuracy" && dodger.StatusStacks("evade") == 0 && miss.Label == "Missed", "a miss against Evade spends one Evade stack");
    var dodger2 = Enemy(); Core2Statuses.Apply(ctx, dodger2, "evade", 1);
    var through = Core2Accuracy.Resolve(rules, Player(), dodger2, attack, new Dice(20));
    Check(through.Hit && through.Label == "Hit — Evade Failed" && dodger2.StatusStacks("evade") == 1, "a hit through Evade reads 'Hit — Evade Failed' and keeps the stack");
    var far = Enemy(new Core2Stance("attack", "physical", "ranged"));
    var outOfReach = Core2Accuracy.Resolve(rules, Player(), far, attack, new Dice());
    Check(!outOfReach.Hit && outOfReach.MissReason == "range" && outOfReach.Dice.Count == 0 && outOfReach.Preview.HitChance == 0, "a melee hit at a ranged stance is out of range: 0%, no die rolled");
    var caster = Enemy(new Core2Stance("casting", "spell", "reach"));
    Check(Core2Accuracy.Preview(rules, Player(), caster, attack).InRange, "a spell stance at Reach is delivery: melee still connects");

    // ---- Damage chain, flooring, Block, Poise, Staggered (JS gameProfileCombat.js, actions.js) ------------------
    var striker = Player("reaver", new Core2Stance("attack"));
    var exposed = Enemy(); exposed.Statuses["vulnerable"] = new Core2StatusInstance { Stacks = 1 };
    var floored = Core2Damage.Receipt(rules, striker, exposed, 3, Carrier("{skipRatingBonus:true,type:'attack',combatProfile:{camp:'physical',maneuver:'attack',range:'melee'}}"));
    Equal(floored.HpLoss, 4, "the chain floors once: 3 × 1.25 (stance) × 1.25 (Vulnerable) = 4.6875 → 4, not 3");
    Equal(Core2Damage.Receipt(rules, striker, exposed, 3, attack).HpLoss, 6, "the Attack stance's +1 AR joins the chain first: (3 + 1) × 1.5625 = 6.25 → 6");
    var guard = Enemy(new Core2Stance("defend")); guard.Block = 8; guard.Poise = new Core2Pool(10, 10);
    var smash = Core2Damage.Receipt(rules, Player(), guard, 10, Carrier(physicalSmash));
    Check(smash.SmashVsDefensive && smash.SmashBlockBonus == 2 && smash.Incoming == 14 && smash.Blocked == 8 && smash.PoiseAbsorbed == 0 && smash.PoiseDamage == 7 && smash.HpLoss == 6,
        "Smash vs Defend: floor(10 × 1.25) + floor(25% × 8 Block) = 14; Block 8; no absorption; Poise -floor(50% × 14) = 7");
    var smashFight = Core2CombatContext.NewFight(content);
    var guard2 = Enemy(new Core2Stance("defend")); guard2.Block = 8; guard2.Poise = new Core2Pool(10, 10);
    var landed = Core2Damage.ApplyAttack(smashFight, Player(), guard2, 10, Carrier(physicalSmash), new Dice(20));
    Check(landed.Interrupted && !landed.PoiseBroken && guard2.Poise.Value == 3 && guard2.Stance.Maneuver == "staggered" && guard2.StatusStacks("vulnerable") == 1 && guard2.PendingStaminaLoss == 1,
        "an advantaged Smash staggers the enemy (staggerOnAdvantage) without breaking its Poise");
    Check(!smashFight.ClaimStagger(guard2), "one stagger per target per round");
    var fight = Core2CombatContext.NewFight(content);
    var hero = Player(); hero.Poise = new Core2Pool(2, 2); hero.MaxStamina = 3;
    var brute = Enemy(); brute.Ratings["ar"] = 0;
    var broken = Core2Damage.ApplyAttack(fight, brute, hero, 8, attack, new Dice(20));
    Check(broken.PoiseBroken && broken.Receipt.PoiseAbsorbed == 2 && broken.Receipt.HpLoss == 6 && hero.Hp == 64, "Poise absorbs 25% but never more than it holds: 8 → 2 absorbed, 6 HP");
    Check(hero.Stance?.Maneuver == "staggered" && hero.StatusStacks("vulnerable") == 1 && hero.PendingStaminaLoss == 1, "a Poise break: Staggered stance, +1 Vulnerable, -1 SP owed");
    Core2Pools.StartTurn(fight, hero);
    Equal(hero.Stamina, 2, "the staggered player's next turn has 1 SP less");
    var empty = Player(); empty.Poise = new Core2Pool(0, 2);
    var noBreak = Core2Damage.ApplyAttack(fight, brute, empty, 8, attack, new Dice(20));
    Check(!noBreak.PoiseBroken && empty.Stance == null && noBreak.Receipt.HpLoss == 8, "an empty Poise pool absorbs nothing and cannot break again");
    var frail = Player(); frail.Hp = 3; frail.Poise = new Core2Pool(1, 2);
    Core2Damage.ApplyAttack(fight, brute, frail, 8, attack, new Dice(20));
    Check(!frail.Alive && frail.Stance == null, "a hit that kills does not stagger");

    // ---- Intent damage range over several hits (JS CombatIntentProjectionModel.js) ------------------------------
    var mantis = Enemy();
    var target = Player(); target.Poise = new Core2Pool(1, 1);
    var twice = Core2IntentProjection.Project(rules, mantis, target, attack, 4, 2);
    Check(twice.Min == 0 && twice.Max == 8 && twice.Label == "0–8" && twice.Uncertain && twice.Tone == "red",
        "two hits of 4: the first breaks Poise (3 HP), the second lands Vulnerable (5 HP): 0–8");
    Check(Math.Abs(twice.HitChance - (1 - 0.05 * 0.05)) < 1e-12, "the range's hit chance is any landed hit across the reachable states");
    Check(target.Poise.Value == 1 && target.Stance == null, "the projection never changes the live combatants");
    var sure = Core2IntentProjection.Project(rules, mantis, target, attack, 4, 1, options: new Core2HitOptions { Unavoidable = true });
    Check(sure.Min == 3 && sure.Max == 3 && sure.Label == "3" && !sure.Uncertain, "an unavoidable hit shows one number");
    Equal(Core2IntentProjection.Project(rules, mantis, target, attack, 4, 1, proposal: new Core2HitProposal { Allowed = false }).Label, "0", "a refused hit shows 0");
    var multi = Core2IntentProjection.Project(rules, mantis, target, attack, 4, 1, damageEffects: 2);
    Check(multi.Label == "?" && multi.Reason == "multipleDamageEffects" && multi.Min == null, "a move with two damage effects promises no range");
    var defender = Player("reaver", new Core2Stance("defend"));
    var spell = Carrier("{type:'attack',tags:['camp:spell','source:spell'],combatProfile:{camp:'spell',maneuver:null,range:'reach',stanceTrigger:false},magicShare:1}");
    var against = Core2IntentProjection.Project(rules, mantis, defender, spell, 8, 1);
    Check(against.Max == 10 && against.Baseline.Max == 8 && against.Tone == "red", "a prepared Spell beats the Defend stance (×1.25): the shown range rises and stays red");
}
catch (Exception error)
{
    failures++;
    Console.Error.WriteLine("FAIL: " + error);
}

if (failures > 0) { Console.Error.WriteLine("Core2Combat: " + failures + " failed, " + checks + " passed"); return 1; }
Console.WriteLine("Core2Combat: " + checks + " checks passed");
return 0;

static Exception Assert(Action action)
{
    try { action(); return null; }
    catch (Exception error) { return error; }
}

sealed class Dice : ICore2Dice
{
    private readonly Queue<int> _faces;
    public Dice(params int[] faces) { _faces = new Queue<int>(faces); }
    public int Roll(string stream, int min, int max)
    {
        if (stream != Core2Accuracy.Stream || min != 1 || max != 20) throw new InvalidOperationException("unexpected roll " + stream + " " + min + ".." + max);
        if (_faces.Count == 0) throw new InvalidOperationException("no die was expected");
        return _faces.Dequeue();
    }
}
