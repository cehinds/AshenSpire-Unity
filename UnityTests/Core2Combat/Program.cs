// Core 2 combat domain checks (SPEC.md §0.9 phases 5a-5c) for rules the JS oracle families do not reach.
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

    // =============================================================================================================
    // Phase 5b. Expected values below were read from the JS game at the pin: the pure functions called directly
    // (magicShareOf, stanceEntryBonuses, counterCoverageMatches, rangeActionPlan, movedEngagementState) and live
    // Core 2 fights (createRunCombat, reaver, seed 511; dispatch playCard / endTurn) whose events are quoted.
    // =============================================================================================================

    // ---- Damage-type table (JS combatRatings.js magicShareOf over src/content/combatRules.js) ------------------
    var shareCases = new (string Profile, double Share)[]
    {
        ("{camp:'physical',maneuver:'attack',damageType:'slashing'}", 0), ("{camp:'spell',school:'fire',damageType:'fire'}", 1),
        ("{camp:'spell',school:'force'}", 0.5), ("{camp:'spell',school:'alteration',damageType:'piercing'}", 0), ("{camp:'spell',school:'illusion'}", 1),
        ("{camp:'spell',reach:'melee',damageType:'blunt'}", 0.7), ("{camp:'physical',maneuver:'attack',school:'lightning'}", 0.3),
        ("{camp:'physical',maneuver:'smash',damageType:'fire'}", 0.3), ("{camp:'physical',maneuver:'attack',damageType:'arcane'}", 0.3),
        ("{camp:'physical',damageType:'arcane'}", 0.5), ("{camp:'physical',maneuver:'attack',damageComponents:[{type:'slashing',weight:3},{type:'fire',weight:1}]}", 0.25),
        ("{camp:'spell',damageComponents:[{type:'frost',weight:1},{type:'earth',weight:1}]}", 0.5), ("{camp:'spell'}", 1), ("{camp:'physical'}", 0),
        ("{camp:'spell',damageType:'earth'}", 0), ("{camp:'physical',maneuver:'attack',magicShare:0.4,school:'fire'}", 0.4), ("{camp:'spell',damageType:'cold'}", 1),
        ("{camp:'physical',maneuver:'attack',riderSpell:true}", 0.3),
    };
    var shareMisses = shareCases.Where(c => Math.Abs(rules.DamageTypes.MagicShareOf(Core2CombatProfile.FromJson(JObject.Parse(c.Profile))) - c.Share) > 1e-12)
        .Select(c => c.Profile + " → " + rules.DamageTypes.MagicShareOf(Core2CombatProfile.FromJson(JObject.Parse(c.Profile))) + " (JS " + c.Share + ")").ToList();
    Check(shareMisses.Count == 0, "magicShareOf precedence: own share, components, sub-1 school, melee spell 0.7, technique 0.3, school, type, camp (18 JS cases)" + (shareMisses.Count == 0 ? "" : ": " + string.Join("; ", shareMisses)));
    var forceShare = Core2Damage.Receipt(rules, Player(), Enemy(), 10, Carrier("{type:'spell',combatProfile:{camp:'spell',school:'force',range:'melee'}}"));
    Check(forceShare.Physical == 5 && forceShare.Magical == 5, "a Force hit splits its card damage 50:50 between Block and Barrier/Poise");

    // ---- Entry grants (JS stanceEntryBonuses, FixCore floor) ---------------------------------------------------
    string Grants(string profile) => Core2Stances.EntryBonuses(rules, Carrier("{combatProfile:" + profile + "}")).ToString();
    Equal(Grants("{camp:'spell',maneuver:'defend',school:'frost'}"), "precision 0, evade 0, block 1, barrier 2", "magical Defense entry: Frost 50:50 floors Block, Barrier takes the rest");
    Equal(Grants("{camp:'spell',maneuver:'defend',school:'force'}"), "precision 0, evade 0, block 2, barrier 1", "magical Defense entry: Force 70:30");
    Equal(Grants("{camp:'spell',maneuver:'defend'}"), "precision 0, evade 0, block 0, barrier 3", "magical Defense entry: default 30:70 floors to 0:3");
    Equal(Grants("{camp:'spell',maneuver:'counter',mirrors:'counter'}"), "precision 1, evade 0, block 0, barrier 3", "a mirrored spell Counter grants both offensive Precision and the magical split");
    Equal(Grants("{camp:'physical',maneuver:'attack',evade:{}}"), "precision 1, evade 1, block 0, barrier 0", "an Attack that authors Evade adds the auxiliary Evade stack");
    Equal(Grants("{camp:'physical',maneuver:'defend',evade:{whileStatus:'hidden'}}"), "precision 0, evade 0, block 3, barrier 0", "a conditional Evade grants nothing until its status holds");
    Equal(Grants("{camp:'physical',maneuver:'counter',evade:{}}"), "precision 1, evade 1, block 3, barrier 0", "an Evade Counter keeps its Counter offense (counterStance)");

    // ---- Entering stances in a live fight (JS setProfileStance; reaver, seed 511) -----------------------------
    Core2Combatant Reaver()
    {
        var reaver = Player("reaver");
        reaver.Hp = reaver.MaxHp = 71;
        reaver.Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["strength"] = 4, ["dexterity"] = 1, ["constitution"] = 1, ["wisdom"] = 1, ["intelligence"] = 1 };
        reaver.Ratings = new Dictionary<string, int>(StringComparer.Ordinal) { ["ar"] = 5, ["dr"] = 1, ["pr"] = 1, ["ward"] = 2, ["poise"] = 8 };
        reaver.Poise = new Core2Pool(8, 8);
        return reaver;
    }
    Core2Carrier Card(string id) => Core2Carrier.FromCard(content.Card(id));
    var live = Core2CombatContext.NewFight(content);
    var hero2 = Reaver();
    var strikeEntry = Core2Stances.EnterStance(live, hero2, Card("core.strike"));
    Check(strikeEntry.Changed && strikeEntry.Precision == 3 && hero2.StatusStacks("precision") == 3 && hero2.Stance.ToString() == "physical/attack/melee",
        "Strike enters Attack: +1 Precision + the stat mix 2 = 3 (JS: precision 3)");
    var secondStrike = Card("core.strike");
    var again = Core2Stances.EnterStance(live, hero2, secondStrike);
    Check(!again.Changed && again.SameStance && secondStrike.FixCoreSameStance == true && hero2.StatusStacks("precision") == 3, "a second Strike repeats no grant and is priced as same-stance");
    var defendCard = Card("core.defend");
    Core2Stances.EnterStance(live, hero2, defendCard);
    hero2.Poise.Value = 3;
    var defendGain1 = Core2Stances.GainBlock(live, hero2, 10, defendCard);
    Check(defendGain1.Block == 14 && defendGain1.PoiseRestored == 5 && hero2.Poise.Value == 8 && hero2.StatusStacks("precision") == 3,
        "Defend: 10 + DR (1 + Defend 3) = 14 Block, and the Block refills Poise 3 → 8 (JS: block 14, poise 8)");
    var defend2 = Card("core.defend");
    Core2Stances.EnterStance(live, hero2, defend2);
    var defendGain2 = Core2Stances.GainBlock(live, hero2, 10, defend2);
    Check(defendGain2.Block == 17 && defendGain2.PoiseRestored == 0 && hero2.Block == 31, "a second Defend: floor(14 × 1.25) = 17 Block; a full Poise takes no overflow (JS: block 31)");
    Check(rules.DefenseRestore == 5 && hero2.Barrier == 0, "the 5-point Defense restore is data the configured Core 2 fight does not apply (JS setProfileStance)");
    var dodger3 = Reaver();
    var evadeEntry = Core2Stances.EnterStance(live, dodger3, Card("core.evade"));
    Check(evadeEntry.Changed && evadeEntry.Evade == 0 && dodger3.Stance.Maneuver == "evade" && dodger3.StatusStacks("evade") == 0,
        "Evade enters the Evade stance; its own applyStatus is the stack, so entry adds none (JS: evade 1 after its effect)");
    var caster2 = Reaver();
    Core2Stances.EnterStance(live, caster2, Card("core.cantrip"));
    Check(caster2.Stance.ToString() == "spell/attack/melee/mirrored" && caster2.StatusStacks("precision") == 3, "Arcane Spark: the mirrored Attack stance in the spell camp, +3 Precision");
    Core2Stances.EnterStance(live, caster2, Card("core.manaSurge"));
    Check(caster2.Stance.Maneuver == "prepared" && caster2.Stance.Camp == "spell" && caster2.StatusStacks("precision") == 6, "Mana Surge then enters Prepared (spell): a new stance, Precision 6 (JS)");
    Check(Core2Stances.StanceRatingBonus(rules, caster2, "pr") == 1 && Core2Stances.StanceRatingBonus(rules, caster2, "ar") == 0, "Prepared raises PR, not AR");

    // ---- Counter: one exchange (JS: core.counter, then wanderingSoldier's Slash 8; enemy d20 10, return d20 14) -------
    Core2Combatant Soldier()
    {
        var soldier = Enemy(new Core2Stance("attack"));
        soldier.Id = "e1"; soldier.Hp = soldier.MaxHp = 33; soldier.Poise = new Core2Pool(5, 5);
        soldier.Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["strength"] = 2, ["dexterity"] = 1, ["constitution"] = 3, ["wisdom"] = -1, ["intelligence"] = 0 };
        soldier.Ratings = new Dictionary<string, int>(StringComparer.Ordinal) { ["ar"] = 4, ["dr"] = 3, ["pr"] = 0, ["poise"] = 5, ["ward"] = 5 };
        return soldier;
    }
    Core2Combatant ArmedReaver(Core2CombatContext fightCtx)
    {
        var reaver = Reaver();
        var counterCard = Card("core.counter");
        reaver.CardsPlayed += 1;
        Core2Stances.EnterStance(fightCtx, reaver, counterCard);
        Core2Stances.GainBlock(fightCtx, reaver, 5, counterCard);
        Core2Counters.Arm(fightCtx, reaver, counterCard);
        Core2Statuses.DecayAtTurnEnd(fightCtx, reaver);
        return reaver;
    }
    var slash = Carrier("{type:'attack',tags:['camp:physical','maneuver:attack','damage:slashing','source:weapon','delivery:melee'],combatProfile:{camp:'physical',maneuver:'attack',damageType:'slashing',range:'melee',targeting:'single'}}");
    var duel = Core2CombatContext.NewFight(content);
    var countering = ArmedReaver(duel);
    Check(countering.Block == 9 && countering.Counter?.Serial == 1 && countering.Counter.Mode == "melee" && countering.Counter.Coverage.Maneuvers.SequenceEqual(new[] { "attack", "smash" })
        && countering.StatusStacks("precision") == 0, "Counter: 5 + DR 4 = 9 Block, a melee Counter (serial 1) covering Attack and Smash; entry Precision expires at turn end");
    var soldier1 = Soldier();
    var exchange = Core2Contacts.ResolveAttack(duel, soldier1, countering, slash, new[] { 8 }, new Dice(10, 14));
    var first = exchange.Single();
    Check(first.Covered && first.CounterConsumed && first.CounterBlock == 3 && first.Hit.Receipt.Incoming == 12 && first.Hit.Amount == 12 && first.Hit.Receipt.Blocked == 12
        && first.Hit.Receipt.HpLoss == 0 && countering.Hp == 71 && countering.Block == 0,
        "Slash (8 + AR 5) × 1.25 × Counter mitigation 0.75 = 12; the advantaged Counter adds floor(25% × 12) = 3 Block first: 12 blocked (JS)");
    Check(first.Return != null && first.Return.Payload.Hp == 9 && first.Return.Payload.Poise == 4 && first.Return.Accuracy.Hit
        && first.Return.Hit.Receipt.HpLoss == 6 && first.Return.Hit.Receipt.PoiseAbsorbed == 3 && first.Return.Hit.Receipt.Advantage && first.Return.PoiseSpent == 2,
        "the return: 50% + 25% of 12 = 9 HP (flat: no ×1.25 though it is advantaged), Poise floor(50% × 9) = 4; Poise absorbs 3 of the 9, the drain takes the last 2 (JS)");
    Check(soldier1.Hp == 27 && soldier1.Poise.Value == 0 && soldier1.Stance.Maneuver == "staggered" && soldier1.StatusStacks("vulnerable") == 1 && countering.CounterReturnedCycle == 0,
        "the drain breaks the soldier's Poise: Staggered, +1 Vulnerable; the return is spent for the cycle (JS: enemy hp 27, poise 0)");

    // ---- Counter against a multi-hit move (JS: fellWarden Twin Daggers 3 × 4; d20 10, return 14, then 4, 10, 7) -------
    var warden = Enemy(new Core2Stance("attack"));
    warden.Id = "e1"; warden.Hp = warden.MaxHp = 120; warden.Level = 5; warden.Poise = new Core2Pool(27, 27);
    warden.Attributes = new Dictionary<string, int>(StringComparer.Ordinal) { ["strength"] = 8, ["dexterity"] = 2, ["constitution"] = 1, ["wisdom"] = 1, ["intelligence"] = 0 };
    warden.Ratings = new Dictionary<string, int>(StringComparer.Ordinal) { ["ar"] = 9, ["dr"] = 6, ["pr"] = 0, ["poise"] = 27, ["ward"] = 15 };
    var daggers = Carrier("{type:'attack',tags:['camp:physical','maneuver:attack','damage:piercing','source:weapon','delivery:melee'],combatProfile:{camp:'physical',maneuver:'attack',damageType:'piercing',range:'melee',targeting:'single'}}");
    var flurryFight = Core2CombatContext.NewFight(content);
    var flurried = ArmedReaver(flurryFight);
    var contacts4 = Core2Contacts.ResolveAttack(flurryFight, warden, flurried, daggers, new[] { 3, 3, 3, 3 }, new Dice(10, 14, 4, 10, 7));
    Check(contacts4.Count == 4 && contacts4[0].Covered && contacts4[0].Return?.Hit != null && contacts4.Skip(1).All(c => !c.Covered && c.Return == null),
        "each of the 4 contacts rolls its own d20; the Counter intercepts and answers only the first (one charge, one return per cycle)");
    Check(warden.Hp == 114 && warden.Poise.Value == 20, "the first contact's return: 6 HP through the warden's Poise (3 absorbed), then 4 Poise (JS: hp 114, poise 20)");
    Check(contacts4.Select(c => c.Hit.Receipt.HpLoss).SequenceEqual(new[] { 0, 9, 9, 10 }) && flurried.Hp == 43 && flurried.Poise.Value == 0,
        "the later contacts land 12 each into Poise: 9, 9, then 10 as the last 2 Poise go (JS: player hp 43)");
    Check(flurried.Stance.Maneuver == "staggered" && flurried.StatusStacks("vulnerable") == 1 && flurried.PendingStaminaLoss == 1 && flurried.Stance.ExpiresOnOwnerCycle == 1,
        "the fourth contact breaks the player's Poise: Staggered until the next turn, +1 Vulnerable, -1 SP owed");
    Core2Counters.StartTacticalTurn(flurried);
    Check(flurried.Stance == null && flurried.Counter == null && flurried.OwnerCycle == 1 && !Core2Counters.ReturnSpent(flurried), "the owner's next turn ends the Staggered stance and the spent Counter; a new cycle may return again");

    // ---- Counter rules the exchange does not reach -------------------------------------------------------------
    var missFight = Core2CombatContext.NewFight(content);
    var dodgedCounter = ArmedReaver(missFight);
    var missed = Core2Contacts.ResolveAttack(missFight, Soldier(), dodgedCounter, slash, new[] { 8 }, new Dice(1)).Single();
    Check(missed.Covered && !missed.CounterConsumed && missed.Hit == null && missed.Return == null && dodgedCounter.Counter.Charges == 1,
        "a covered attack that misses neither consumes the Counter nor draws a return");
    var smashFight2 = Core2CombatContext.NewFight(content);
    var smashed = ArmedReaver(smashFight2);
    var smashMove = Carrier("{type:'attack',combatProfile:{camp:'physical',maneuver:'smash',range:'melee',targeting:'single'}}");
    var smashHit = Core2Contacts.ResolveAttack(smashFight2, Soldier(), smashed, smashMove, new[] { 8 }, new Dice(10, 14)).Single();
    Check(smashHit.Covered && smashHit.CounterBlock > 0 && smashHit.Return?.Payload.Hp > 0, "Counter beats Smash too: advantaged Block and return");
    var sweepFight = Core2CombatContext.NewFight(content);
    var sweptCounter = ArmedReaver(sweepFight);
    var sweepMove = Carrier("{type:'attack',combatProfile:{camp:'physical',maneuver:'sweep',range:'melee'}}");
    var swept = Core2Contacts.ResolveAttack(sweepFight, Soldier(), sweptCounter, sweepMove, new[] { 8 }, new Dice(10)).Single();
    Check(!swept.Covered && swept.Return == null && sweptCounter.Counter.Charges == 1 && swept.Hit.Receipt.Advantage,
        "a Sweep is area targeting: no Counter covers it, and Sweep beats the Counter stance (×1.25)");
    var cover = rules.CounterDefaultCoverage["melee"];
    Core2CombatProfile Prof(string json) => Core2CombatProfile.FromJson(JObject.Parse(json));
    Check(Core2Counters.CoverageMatches(cover, Prof("{camp:'physical',range:'melee',targeting:'single',maneuver:'attack'}"))
        && !Core2Counters.CoverageMatches(cover, Prof("{camp:'physical',range:'reach',targeting:'single',maneuver:'attack'}"))
        && !Core2Counters.CoverageMatches(cover, Prof("{camp:'spell',range:'melee',targeting:'single',maneuver:'attack'}"))
        && Core2Counters.CoverageMatches(cover, Prof("{camp:'physical',range:'melee',targeting:'single',maneuver:'counter',counterReturnAttack:true}"))
        && !Core2Counters.CoverageMatches(cover, Prof("{camp:'physical',range:'melee',targeting:'single',maneuver:'attack'}"), "status"),
        "default melee coverage: physical melee single Attack/Smash damage; a return reads as an Attack (JS counterCoverageMatches)");
    var lapsed = ArmedReaver(Core2CombatContext.NewFight(content));
    Core2Counters.StartTacticalTurn(lapsed);
    Check(lapsed.Counter == null && Core2Counters.Prepared(live, lapsed) == null, "a Counter lapses at its owner's next turn");
    var leftStance = ArmedReaver(Core2CombatContext.NewFight(content));
    leftStance.Stance = new Core2Stance("attack");
    Check(Core2Counters.Prepared(live, leftStance) == null, "a Counter answers only while its owner stands in Counter");

    // ---- Spell Counters (JS profileStance.js isSpellCounter / armsAsCoreCounter; preparedCounter) ------------------
    var frostCounter = Carrier("{type:'spell',tags:['camp:spell','maneuver:counter','school:frost'],combatProfile:{camp:'spell',maneuver:'counter',school:'frost',range:'reach',counterCoverage:{camps:['spell'],ranges:['melee','reach','ranged'],targeting:['single'],effects:['damage','status']}}}");
    Check(Core2Counters.IsSpellCounter(frostCounter) && Core2Counters.ArmsAsCoreCounter(rules, frostCounter) && Core2Stances.ConfiguredStance(frostCounter) == "prepared",
        "a spell-camp counter maneuver with coverage is a spell Counter: it arms, standing in Prepared");
    Check(!Core2Counters.IsSpellCounter(Carrier("{type:'spell',combatProfile:{camp:'spell',maneuver:'counter'}}")), "without authored coverage a spell is not a Counter");
    var spellFight = Core2CombatContext.NewFight(content);
    var mage = Reaver();
    mage.CardsPlayed = 1;
    Core2Stances.EnterStance(spellFight, mage, frostCounter);
    var spellArmed = Core2Counters.Arm(spellFight, mage, frostCounter);
    Check(mage.Stance.Maneuver == "prepared" && spellArmed.Mode == "spell" && spellArmed.Coverage.Camps.SequenceEqual(new[] { "spell" }) && Core2Counters.Prepared(spellFight, mage) != null,
        "a spell Counter arms in spell mode with its authored coverage while its owner stands in Prepared");
    var bolt = Carrier("{type:'attack',combatProfile:{camp:'spell',maneuver:'casting',school:'fire',damageType:'fire',range:'ranged',targeting:'single'}}");
    var caster3 = Enemy(new Core2Stance("casting", "spell", "ranged"));
    caster3.Hp = caster3.MaxHp = 40; caster3.Poise = new Core2Pool(4, 4);
    var spellExchange = Core2Contacts.ResolveAttack(spellFight, caster3, mage, bolt, new[] { 8 }, new Dice(10, 14)).Single();
    Check(spellExchange.Covered && spellExchange.CounterBlock == 0 && spellExchange.Return?.Payload.Hp == spellExchange.Incoming / 2 && spellExchange.Return.Payload.Poise == (int)Math.Floor(0.2 * spellExchange.Return.Payload.Hp),
        "a spell Counter answers a covered spell: not advantaged (Counter beats only Attack and Smash), so 50% HP and 20% of that as Poise, no Block");
    mage.CardsPlayed += 1;
    mage.Counter = spellArmed; spellArmed.Charges = 1;
    Check(Core2Counters.Prepared(spellFight, mage) == null, "any later accepted card disarms a spell Counter, whatever stance it leaves");

    // ---- Reactive Sweep riders (JS prepareTacticalCard) ---------------------------------------------------------
    var sweepCard = Card("core.sweep");
    Check(Core2Counters.ReactiveSweepRiders(rules, sweepCard, Prof("{camp:'physical',maneuver:'attack',range:'melee'}"), true) == (3, 3)
        && Core2Counters.ReactiveSweepRiders(rules, sweepCard, Prof("{camp:'physical',maneuver:'ranged'}"), true) == null
        && Core2Counters.ReactiveSweepRiders(rules, sweepCard, Prof("{camp:'spell',maneuver:'casting',range:'melee'}"), true) == null
        && Core2Counters.ReactiveSweepRiders(rules, sweepCard, Prof("{camp:'physical',maneuver:'attack',range:'melee'}"), false) == null,
        "a Sweep played as a reaction to a physical melee attack adds 3 Poise and 3 Ward damage; nothing otherwise");

    // ---- Range and Advance (JS model/combatEngagement.js, quoted plans) ---------------------------------------------
    Check(rules.RangePolicy.Enabled && rules.RangePolicy.AdvanceSpPerBand == 1 && rules.RangePolicy.ReachPrecisionBonus == 1 && rules.RangePolicy.ThrowPrecisionPenalty == 1,
        "Core 2 range policy: on, 1 SP per band, Reach +1, Throw -1");
    var bands = Core2RangeState.Create(rules.RangePolicy, new[] { new Core2RangeActor("player:player", "melee"), new Core2RangeActor("enemy:e1", "ranged"), new Core2RangeActor("enemy:e2", "reach"), new Core2RangeActor("enemy:e3", "melee") });
    Check(bands.Band("player:player", "enemy:e1") == 2 && bands.Band("enemy:e2", "player:player") == 1 && bands.Band("player:player", "enemy:e3") == 0 && bands.Engagements.Count == 3,
        "each opposing pair starts at the greater starting band (JS: e1 2, e2 1, e3 0)");
    Core2RangeSource Src(string target, string coverage, bool weapon = false, bool throwCapable = false, string weaponId = null, bool bypass = false, string id = null) =>
        new Core2RangeSource { TargetKey = target, Coverage = coverage, Weapon = weapon, ThrowCapable = throwCapable, SourceWeaponId = weaponId, Bypass = bypass, ContactId = id };
    string PlanText(Core2RangePlan plan) => plan.Route + " " + plan.Available + " " + plan.AdvanceSp + " [" + string.Join(" ", plan.Targets.Select(t => t.TargetKey + ":" + t.ActualBand + "/" + t.Band + "/" + t.Coverage + "/" + t.MissingBands + "/" + t.EffectiveBand + "/" + t.PrecisionDelta)) + "]";
    Equal(PlanText(Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e1", "melee"), Src("enemy:e2", "melee"), Src("enemy:e3", "melee") })),
        "advance True 2 [enemy:e1:2/2/0/2/0/0 enemy:e2:1/1/0/1/0/0 enemy:e3:0/0/0/0/0/0]", "Advance pays the greatest gap once (2 SP) and closes each pair by its own gap");
    var thrown = Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e2", "melee", true, true, "dagger") }, "throw");
    Equal(PlanText(thrown), "throw True 0 [enemy:e2:1/1/0/1/1/-1]", "a Throw-capable dagger reaches one band further at -1 Precision, no SP");
    Equal(PlanText(Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e1", "melee", true, true) }, "throw")), "throw False 0 [enemy:e1:2/2/0/2/2/-1]", "Throw cannot cover two bands");
    Equal(PlanText(Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e3", "reach", true, false, "halberd"), Src("enemy:e2", "reach", true) })),
        "advance True 0 [enemy:e3:0/0/1/0/0/1 enemy:e2:1/1/1/0/1/0]", "a Reach weapon gains +1 Precision only at actual Melee engagement");
    Equal(PlanText(Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e1", "melee", bypass: true) })), "advance True 0 [enemy:e1:2/2/0/0/2/0]", "a range bypass waives approach and does not move the pair");
    Equal(PlanText(Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e1", "ranged", true, false, "shortbow"), Src("enemy:e1", "melee", true, false, "dagger", id: "b") })),
        "advance True 2 [enemy:e1:2/2/2/0/0/0 enemy:e1:2/2/0/2/0/0]", "a bow and a dagger at one opponent share one approach: both meet the closed distance");
    var rangeOff = Core2RangeState.Create(new Core2RangePolicy(enabled: false), new[] { new Core2RangeActor("player:player"), new Core2RangeActor("enemy:e1", "ranged") });
    Check(PlanText(Core2Range.ActionPlan(rangeOff, "player:player", new[] { Src("enemy:e1", "melee") })) == "advance True 0 [enemy:e1:2/2/0/0/2/0]"
        && !Core2Range.ActionPlan(rangeOff, "player:player", new[] { Src("enemy:e1", "melee") }, "throw").Available
        && Core2Range.HitContact(rangeOff, "player:player", Src("enemy:e1", "melee")).InRange, "range off: no gaps, no Throw route, everything connects");
    var moved = bands.Moved("player:player", "enemy:e1", -1).Moved("player:player", "enemy:e3", -1);
    Check(moved.Band("player:player", "enemy:e1") == 1 && moved.Band("player:player", "enemy:e3") == 0 && bands.Band("player:player", "enemy:e1") == 2 && rangeOff.Moved("player:player", "enemy:e1", -1) == rangeOff,
        "Advance closes one band (clamped at Melee); the old state is untouched; range off moves nothing (JS movedEngagementState)");
    Check(Core2Range.MovementBands(content.Card("core.advance")).SequenceEqual(new[] { -1 }) && Core2Range.MovementBands(content.Card("core.backstep")).SequenceEqual(new[] { 1 })
        && Core2Range.PureEngagementMovement(content.Card("core.advance")) && !Core2Range.PureEngagementMovement(content.Card("core.closingDodge"))
        && !Core2Range.RangeCardPlayable(rangeOff, content.Card("core.advance")) && Core2Range.RangeCardPlayable(rangeOff, content.Card("core.closingDodge")),
        "Advance and Closing Dodge close a band, Backstep retreats; a movement-only card refuses play with range off, Closing Dodge keeps its Evade");
    var incomingRoot = Core2Range.ActionPlan(bands, "enemy:e1", new[] { Src("player:player", "melee") });
    var nested = Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e1", "melee", true) }, "advance", incomingRoot);
    Check(incomingRoot.AdvanceSp == 2 && PlanText(nested) == "advance True 0 [enemy:e1:2/0/0/0/0/0]", "a nested return meets the incoming root's closed distance and pays no Advance (JS)");
    Check(!Core2Range.HitContact(bands, "player:player", Src("enemy:e1", "melee")).InRange && Core2Range.HitContact(bands, "player:player", Src("enemy:e1", "melee"), Core2Range.ActionPlan(bands, "player:player", new[] { Src("enemy:e1", "melee") })).InRange
        && Core2Range.HitContact(bands, "player:player", Src("enemy:e2", "melee", true, true, "dagger"), thrown).InRange,
        "unplanned, a melee attack at Ranged is out of reach; its Advance plan (or a Throw plan one band out) connects");
    var initial = Core2Range.Initialize(rules, Player(), "melee", new[] { (Enemy(), content.Enemy("graveWisp").Raw.Value<string>("startingRange")) });
    Equal(initial.Band("player:player", "enemy:enemy"), 1, "a fight starts each pair at the greater of the player's weapon band and the enemy's authored startingRange (Grave Wisp: reach)");
    var reachMan = Player(); reachMan.Level = 0;
    var halberdHit = Core2Accuracy.Preview(rules, reachMan, Enemy(), attack, new Core2HitOptions { Contact = Core2Range.HitContact(bands, "player:player", Src("enemy:e3", "reach", true, false, "halberd")) });
    Check(halberdHit.Accuracy == Core2Accuracy.Preview(rules, reachMan, Enemy(), attack).Accuracy + 1, "the Reach bonus reaches the hit preview's Accuracy (+1)");

    // ---- A Counter return that must Advance (RangeAdvance.md: the defender pays its own approach, or no return) -------
    Core2CounterExchange ReachDuel(int stamina)
    {
        var fightCtx = Core2CombatContext.NewFight(content);
        var defender = ArmedReaver(fightCtx);
        defender.Stamina = stamina;
        var pike = Soldier(); pike.Id = "e2";
        var state = Core2RangeState.Create(rules.RangePolicy, new[] { new Core2RangeActor("player:player"), new Core2RangeActor("enemy:e2", "reach") });
        var halberd = Src("player:player", "reach", true, false, "halberd");
        var plan = Core2Range.ActionPlan(state, "enemy:e2", new[] { halberd });
        var result = Core2Contacts.ResolveAttack(fightCtx, pike, defender, slash, new[] { 8 }, new Dice(10, 14),
            new Core2ActionOptions { Range = state, Plan = plan, AttackSource = _ => halberd, ReturnSource = Src("enemy:e2", "melee", true, false, "greatsword") }).Single();
        Check(plan.AdvanceSp == 0 && result.Covered && result.CounterConsumed && defender.Stamina == Math.Max(0, stamina - 1) && pike.Hp == (stamina >= 1 ? 27 : 33),
            "a halberd at Reach needs no Advance; the defender's melee return needs 1 band (" + stamina + " SP held)");
        return result.Return;
    }
    var refused = ReachDuel(0);
    Check(refused != null && refused.RangeRefused && refused.Hit == null, "an unaffordable return Advance skips the return; the interception (Block) stands");
    var paidReturn = ReachDuel(2);
    Check(paidReturn.Plan.AdvanceSp == 1 && paidReturn.Hit.Receipt.HpLoss == 6, "with SP to spare the defender pays 1 SP to close and the return lands");

    // ---- Phase 5c: seeded streams (src/engine/rng.js createRng, values printed by JS at the pin) ------------------
    var rng = new Core2Rng(511);
    Equal(rng.Float("enemyAI"), 0.2570719097275287, "rng.js: seed 511 enemyAI draw 1");
    Equal(rng.Float("enemyAI"), 0.008900965796783566, "rng.js: seed 511 enemyAI draw 2");
    Equal(rng.Int("combatAvoidance", 1, 20), 10, "rng.js: seed 511 first d20 on combatAvoidance");
    Equal(string.Join(",", rng.Shuffle("shuffle", new[] { 1, 2, 3, 4, 5 })), "2,1,5,3,4", "rng.js: Fisher-Yates shuffle of five draws four times");
    Check(rng.Counter("enemyAI") == 2 && rng.Counter("combatAvoidance") == 1 && rng.Counter("shuffle") == 4, "rng.js: each stream counts only its own draws");
    var detached = rng.Detached();
    detached.Float("enemyAI");
    Equal(rng.Counter("enemyAI"), 2, "a detached copy (JS createRng(seed, counters)) never advances the fight's streams");
    Check(Throws<ArgumentException>(() => rng.Float("noSuchStream")) || Throws<InvalidOperationException>(() => rng.Float("noSuchStream")) || Throws<KeyNotFoundException>(() => rng.Float("noSuchStream")), "an unknown stream is refused");

    // ---- Phase 5c: Perception (JS rollKnowledgeRead, fixCore.perception baseDC 5) -------------------------------
    Equal(rules.PerceptionBaseDC, 5, "Perception base DC 5");
    Equal(rules.EnemyHealBlockCost, 3, "enemy heal+Block moves cost 3 SP (fixCore.enemyHealBlockCost)");
    var observer = Player();
    observer.Attributes["wisdom"] = 2;
    var watched = Enemy();
    watched.Attributes["intelligence"] = 3;
    watched.Attributes["wisdom"] = 4;
    var read = Core2Knowledge.RollRead(rules, new Core2Rng(1), observer, watched);
    Check(read.Roll == 11 && read.Dc == 12 && read.Total == 13 && read.Passed && read.Visibility == "exact", "Perception: d20 11 + WIS 2 = 13 meets DC 5 + INT 3 + WIS 4 = 12 (exact read)");
    watched.Attributes["wisdom"] = 6;
    var unread = Core2Knowledge.RollRead(rules, new Core2Rng(1), observer, watched);
    Check(!unread.Passed && unread.Label == "?" && unread.Dc == 14, "Perception: 13 against DC 14 fails and reads '?'");
    var reads = new Core2Rng(1);
    Core2Knowledge.RollRead(rules, reads, observer, watched);
    Check(reads.Counter("enemyIntentVisibility") == 1 && reads.Counter("enemyIntentClue") == 1, "a read draws one visibility roll and one clue draw");

    // ---- Phase 5c: enemy SP pools, multi-hit floor, live plans (JS enemyMoveSp, extendEnemyQueue) ----------------
    var oracleStart = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(root, "UnityTests", "Core2Content", "Oracles", "enemy-planning.json")))["starts"]["511"];
    Core2Fight Fight(string enemyId, uint seed = 511) => Core2Fight.Create(content, Core2FightSetup.FromJson(oracleStart), new List<string> { enemyId }, seed);
    var wardenFight = Fight("fellWarden");
    var wardenEnemy = wardenFight.Enemies[0];
    var hammer = content.Enemy("fellWarden").Move("hammerToss");
    Check((int?)hammer.Raw["sp"] == 1 && hammer.Hits > 1, "fellWarden hammerToss is an authored 1 SP multi-hit move");
    Equal(Core2EnemyMoves.MoveSp(wardenFight.Ctx, wardenEnemy, hammer.Raw, hammer.Id), 2, "a multi-hit move costs at least 2 SP");
    Equal(wardenEnemy.MaxStamina, 3, "fellWarden's SP pool is 3 at seed 511");
    var wardenPlan = Core2EnemyPlanning.PlanActions(wardenFight, wardenEnemy).Select(a => a.MoveId).ToList();
    Equal(string.Join(",", wardenPlan), "caneStrike,caneStrike,heldBlade", "fellWarden opens caneStrike, caneStrike, then the charging heldBlade");
    Check(content.Enemy("fellWarden").Move(wardenPlan.Last()).Raw["delay"] != null, "a plan stops at its first delayed (charging) move");
    Check(wardenPlan.Sum(id => Core2EnemyMoves.MoveSp(wardenFight.Ctx, wardenEnemy, content.Enemy("fellWarden").Move(id).Raw, id)) <= wardenEnemy.MaxStamina, "the opening plan fits the SP pool");
    Equal(wardenEnemy.Stamina, wardenEnemy.MaxStamina, "planning commits moves without spending SP before the enemy acts");

    // ---- Phase 5c: concealment (JS concealKnowledgeIntent) and the enemy inspector -------------------------------
    Core2Fight hiddenFight = null;
    for (uint seed = 1; seed < 200 && hiddenFight == null; seed++)
    {
        var candidate = Fight("graveWisp", seed);
        if (Core2Knowledge.Project(candidate.Enemies[0]).Hidden) hiddenFight = candidate;
    }
    Check(hiddenFight != null, "some seed fails the opening Perception read against graveWisp");
    if (hiddenFight != null)
    {
        var wisp = hiddenFight.Enemies[0];
        var view = Core2Knowledge.Project(wisp);
        Check(view.MoveId == null && view.Damage == null && view.Label == "?" && view.Stance == "unknown", "a failed read shows '?' and nothing about the move");
        var hidden = Core2EnemyInspector.Inspect(hiddenFight, wisp);
        Check(hidden.CurrentHidden && hidden.Current == null && hidden.Stance == null && hidden.Moves.All(m => !m.Active) && hidden.Deck.All(c => !c.Active), "the inspector never reveals a concealed current action");
    }
    var shown = Fight("graveWisp");
    var shownWisp = shown.Enemies[0];
    var inspection = Core2EnemyInspector.Inspect(shown, shownWisp);
    Equal(string.Join(",", inspection.Resources.Select(r => r.Label)), "HP,SP,Poise,Block", "inspector pools: HP, SP, Poise, Block");
    var sp = inspection.Resources.First(r => r.Label == "SP");
    Check(sp.Value == shownWisp.Stamina && sp.Max == shownWisp.MaxStamina, "inspector SP reads current of maximum");
    Equal(string.Join(",", inspection.Deck.Select(c => c.CardId + "x" + c.Copies + ">" + c.MoveId)), "graveWisp.cursex2>curse,graveWisp.drainx1>drain,graveWisp.advancex1>advance", "inspector deck: authored identities and copies joined by legacyMove");
    Check(inspection.Deck.All(c => c.ActionSp == inspection.Moves.First(m => m.MoveId == c.MoveId).Sp), "deck rows quote the live action SP");
    Equal(string.Join(",", inspection.Moves.Select(m => m.MoveId)), string.Join(",", content.Enemy("graveWisp").Moves.Select(m => m.Id)), "move set lists every authored move in authored order");
    Check(inspection.History.Count == 0 && inspection.Last == null, "no history before the enemy has acted");
    shown.ReactionsEnabled = false;
    shown.EndTurn();
    var after = Core2EnemyInspector.Inspect(shown, shownWisp);
    var started = shown.Events.Where(e => e.Type == "enemyMoveStarted").ToList();
    Check(after.History.SequenceEqual(started.Select(e => (string)e.Data["moveId"])), "history lists the moves that started, oldest first");
    Check(started.Count == 0 || (after.Last != null && after.Last.PaidSp == (int?)started.Last().Data["totalSp"]), "last played carries its paid SP receipt");
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
