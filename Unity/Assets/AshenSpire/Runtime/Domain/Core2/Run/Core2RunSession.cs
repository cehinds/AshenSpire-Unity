// Core2RunSession.cs — one Core 2 run in play: the screen it stands at and every move between screens (port plan
// phase 8). Engine-free; the presentation that drives it is a later phase.
//
// JS at the pin (src/main.js, the orchestration between screens):
//   newRun / startClimb           Core2RunCreation.Create, then the act 1 map
//   enterNode                     travel: path, floor; a fogged node opens its pre-rolled outcome
//   startFightNow / enterCombat   the encounter (rolled on `enemyAI` for normal and elite pools, the node's own for a
//                                 boss), the fight drawn on a copy of the run's RNG whose counters the run then adopts
//   onCombatEnd (configured)      runCombatEnd, character XP, cinders on arrival (rollRuneReward on `misc`), the spoils;
//                                 a defeat, or the last act's boss, finishes the run
//   beginGameProfileReward /      offer `act:node:source`; Continue rolls one level offer per level the fight earned,
//   showGameProfileReward         then the map, or the next act after a boss
//   showShop                      stock `shop:act:node`, purchases at the flat price
//   showRest                      the visit heals on arrival; Smith and Level Up once each
//   showEvent                     choices committed through the quest door; a startCombat effect fights after Continue
//   advanceAct                    act + 1, full HP, a new map on the next seat
// Phase 9 and 10 (Core2RunDeck, Core2RunArmoury, Core2RunSkills, Core2ProfileSave):
//   onCombatEnd (configured)      class and used-weapon XP settled once (settleConfiguredSkillProgression), level notices
//                                 (skillLevelNotices), character XP only while progression.characterLevels is on, the
//                                 victory receipt; a defeat with notices, and the final victory, open a notice-only spoils
//   showGameProfileReward         the victory receipt, then the level notices, each acknowledged once, then the offer;
//                                 a notice-only spoils ends the run when both are acknowledged
//   showArmoury / showDeckEditor  looking clears the Armoury glow; Replace / Unequip re-stamp the deck; the deck editor
//                                 session; both refused while a fight is unresolved (armouryRules editingLocked)
//   claimGameProfileRewardToDeck  a won card straight into the deck
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Core2
{
    public enum Core2Screen { Map, Fight, Reward, Shop, Rest, Event, Finished }

    public sealed class Core2RunSession
    {
        public Core2RunData Data { get; }
        public Core2Content Content => Data.Content;
        public Core2Run Run { get; }
        public Core2Rng Rng { get; private set; }
        public Core2Fight Fight { get; private set; }
        /// <summary>The encounter of the fight in progress (or just ended, until it is settled).</summary>
        public JObject FightEncounter { get; private set; }
        public (double Hp, double Damage) FightScale { get; private set; } = (1, 1);
        /// <summary>What the run handed the fight in progress (its player, deck, faces and mounts).</summary>
        public Core2FightSetup FightSetup { get; private set; }
        /// <summary>Called when a won or lost fight is settled (XP, cinders), before its spoils are rolled: the settlement screen's moment.</summary>
        public Action<Core2RunSession, Core2FightSettlement> OnFightSettled { get; set; }
        /// <summary>The last fight's settlement: XP receipt, level award, cinders.</summary>
        public Core2FightSettlement LastSettlement { get; private set; }
        /// <summary>The last committed event choice (its result text and any fight it opened).</summary>
        public Core2EventReceipt LastEvent { get; private set; }
        /// <summary>The profile's progression settings (characterLevels and the class/weapon XP multipliers).</summary>
        public Core2ProgressionRules Rules { get; set; }
        /// <summary>The player's Deck settings the editor and "Add to deck" read.</summary>
        public Core2DeckSettings DeckSettings { get; set; }
        /// <summary>The skill XP the fight in progress has earned so far (JS combat.skillXp.player).</summary>
        public Core2SkillXpRecorder FightSkillXp { get; private set; }

        private Core2RunSession(Core2RunData data, Core2Run run, Core2Rng rng)
        {
            Data = data; Run = run; Rng = rng;
            Rules = Core2ProgressionRules.From(data);
            DeckSettings = Core2DeckSettings.From(data);
        }
        private int _staminaDeficit;
        private Core2DeckEdit _deckEdit;

        /// <summary>A new run (JS newRun + startClimb): created, the seat order drawn, the act 1 map built.</summary>
        public static Core2RunSession Start(Core2RunData data, string classId, uint seed, Core2UnlockLedger ledger = null, string runId = null, bool ignoreUnlocks = false)
        {
            var (run, rng) = Core2RunCreation.Create(data, classId, seed, ledger, runId, ignoreUnlocks);
            var session = new Core2RunSession(data, run, rng);
            run.MapGraph = Core2ActMap.Build(data, rng, Core2ActMap.SeatAt(run), Core2ActMap.Tier(data, run.ActNumber), run.History);
            session.Checkpoint();
            return session;
        }

        /// <summary>
        /// A loaded run. A fight that was in progress restarts from its entry checkpoint (JS: the encounter receipt), with
        /// the flasks it held at entry. A fight an event chose but has not yet entered (the event still open) waits for
        /// ContinueEvent.
        /// </summary>
        public static Core2RunSession Resume(Core2RunData data, Core2Run run)
        {
            var session = new Core2RunSession(data, run, new Core2Rng(run.Seed, run.StreamCounters));
            if (run.CombatEntered != null && run.Finish == null && run.OpenEvent == null)
            {
                if (run.CombatEntered.FlaskCharges != null) run.FlaskCharges = run.CombatEntered.FlaskCharges.Clone();
                if (run.CombatEntered.Flasks != null) run.Flasks = new List<string>(run.CombatEntered.Flasks);
                session.Rng = new Core2Rng(run.Seed, run.CombatEntered.Counters ?? run.StreamCounters);
                session.OpenFight(run.CombatEntered.NodeId, run.CombatEntered.EncounterId);
            }
            return session;
        }

        /// <summary>Record the RNG position on the run (JS persist writes run.streamCounters).</summary>
        public void Checkpoint() { Run.StreamCounters = Rng.Counters().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal); }

        public Core2Screen Screen
        {
            get
            {
                if (Run.Finish != null) return Core2Screen.Finished;
                if (Fight != null) return Core2Screen.Fight;
                if (Run.PendingReward != null) return Core2Screen.Reward;
                if (Run.OpenShop != null) return Core2Screen.Shop;
                if (Run.OpenEvent != null) return Core2Screen.Event;
                if (Run.RestVisitId != null && Run.RestVisitId == Run.ActNumber + ":" + Run.MapNodeId) return Core2Screen.Rest;
                return Core2Screen.Map;
            }
        }

        private void Require(Core2Screen screen)
        {
            if (Screen != screen) throw new InvalidOperationException("The run is at " + Screen + ", not " + screen);
        }

        // ---- map ---------------------------------------------------------------------------------------------------

        /// <summary>Where the run may travel next: the start row, or the current node's next nodes.</summary>
        public IReadOnlyList<string> TravelOptions() => Screen != Core2Screen.Map ? new List<string>()
            : Run.MapNodeId == null ? Run.MapGraph.StartIds : Run.MapGraph.Node(Run.MapNodeId).Next;

        /// <summary>JS enterNode: travel to a node and open what it holds.</summary>
        public Core2Screen Travel(string nodeId)
        {
            Require(Core2Screen.Map);
            if (!TravelOptions().Contains(nodeId)) throw new InvalidOperationException("Node '" + nodeId + "' cannot be reached from here");
            var node = Run.MapGraph.Node(nodeId);
            Run.MapNodeId = nodeId;
            if (!Run.Path.Contains(nodeId)) Run.Path.Add(nodeId);
            Run.Floor = node.Floor;
            var kind = node.Type;
            if (kind == "event")
            {
                if (node.ResolvedKind == "event")
                {
                    Run.SeenEvents.Add(node.ResolvedEventId);
                    Run.OpenEvent = node.ResolvedEventId; Run.OpenEventChosen = false;
                    Checkpoint();
                    return Screen;
                }
                kind = node.ResolvedKind ?? "fight";
            }
            switch (kind)
            {
                case "monster":
                case "fight": StartFight("normal", nodeId); break;
                case "elite": StartFight("elite", nodeId); break;
                case "boss": StartFight("boss", nodeId); break;
                case "shrine": Core2RunRest.Begin(Data, Run, Run.ActNumber + ":" + nodeId); break;
                case "merchant":
                    Run.OpenShop = Run.ActNumber + ":" + nodeId;
                    Core2RunRewards.ShopStock(Data, Run, Rng, Run.OpenShop);
                    break;
                case "treasure": BeginReward("combat", "map", 0); break;
                default: throw new InvalidOperationException("Unknown node kind '" + kind + "'");
            }
            Checkpoint();
            return Screen;
        }

        // ---- fights ------------------------------------------------------------------------------------------------

        /// <summary>JS rollEncounter: a weighted pick among the seat's pool on `enemyAI`, avoiding the last two normal fights.</summary>
        public static string RollEncounter(Core2RunData data, Core2Rng rng, string pool, string seat, IReadOnlyList<string> exclude)
        {
            bool InSeat(JObject e) => (string)e["pool"] == pool && (string)e["seat"] == seat;
            var all = data.Content.Encounters.Select(e => e.Raw).ToList();
            var candidates = all.Where(e => InSeat(e) && !exclude.Contains((string)e["id"])).ToList();
            if (candidates.Count == 0) candidates = all.Where(InSeat).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException("No encounters in pool '" + pool + "' for seat '" + seat + "'");
            var total = candidates.Sum(e => (double)e["weight"]);
            var r = rng.Float("enemyAI") * total;
            foreach (var e in candidates)
            {
                r -= (double)e["weight"];
                if (r < 0) return (string)e["id"];
            }
            return (string)candidates[candidates.Count - 1]["id"];
        }

        private void StartFight(string pool, string nodeId)
        {
            var seat = Core2ActMap.SeatAt(Run);
            var encounterId = pool == "boss" ? Core2ActMap.BossEncounter(Data, Run.MapGraph, nodeId, seat, Core2ActMap.Tier(Data, Run.ActNumber))
                : RollEncounter(Data, Rng, pool, seat, Run.LastEncounters);
            if (pool == "normal")
            {
                Run.LastEncounters.Add(encounterId);
                if (Run.LastEncounters.Count > 2) Run.LastEncounters.RemoveAt(0);
            }
            EnterCombat(nodeId, encounterId);
        }

        /// <summary>JS enterCombat: the entry checkpoint, then the fight on a copy of the run's RNG.</summary>
        private void EnterCombat(string nodeId, string encounterId)
        {
            Run.CombatEntered = new Core2CombatEntry
            {
                NodeId = nodeId, EncounterId = encounterId, Counters = Rng.Counters().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                FlaskCharges = Run.FlaskCharges?.Clone(), Flasks = new List<string>(Run.Flasks),
            };
            OpenFight(nodeId, encounterId);
        }

        private void OpenFight(string nodeId, string encounterId)
        {
            var encounter = Data.Encounter(encounterId);
            FightEncounter = encounter;
            FightScale = Core2RunFights.EncounterScale(Data, Run, encounter);
            var setup = FightSetup = Core2RunFights.BuildSetup(Data, Run, FightScale.Hp);
            // JS createRunCombat enemyDamageMult: a boss's tier damage scale reaches each enemy's move damage.
            setup.EnemyDamageMult = FightScale.Damage;
            var key = Run.Seed.ToString(CultureInfo.InvariantCulture) + "/" + Run.ActNumber + "/" + Run.Floor + "/" + (Run.MapNodeId ?? string.Join(",", Core2Json.Strings(encounter["enemies"])));
            Fight = Core2Fight.Create(Content, setup, Core2Json.Strings(encounter["enemies"]), Run.Seed, key, null, Rng.Counters());
            // JS combat.equipmentPoolDeficits.stamina: each player turn start records the SP a Stagger withheld.
            var fight = Fight;
            _staminaDeficit = 0;
            fight.OnEvent += ev => { if (ev.Type == "playerTurnStart") _staminaDeficit = Math.Max(0, fight.Player.MaxStamina - fight.Player.Stamina); };
            // JS attachSkillXp: the skill hooks listen on the fight's bus; the run is paid once, when the fight is settled.
            var skills = FightSkillXp = new Core2SkillXpRecorder(Data, Run, fight);
            fight.OnEvent += skills.Handle;
            // The run adopts the fight's stream (JS: rng.restoreCounters(entryRng.getCounters()); combat.rng = rng).
            Rng = Fight.Rng;
        }

        /// <summary>Settle an ended fight (JS onCombatEnd, configured branch); returns the screen it leads to.</summary>
        public Core2Screen FinishFight()
        {
            if (Fight == null) throw new InvalidOperationException("No fight in progress");
            if (Fight.Result == null) throw new InvalidOperationException("The fight has not ended");
            var fight = Fight;
            var encounter = FightEncounter;
            var pool = (string)encounter["pool"];
            Fight = null;
            Core2RunFights.End(Data, Run, fight, _staminaDeficit);
            var victory = fight.Result == "victory";
            // The skill tracks are paid here, once, win or loss (settleConfiguredSkillProgression), before character XP.
            var xpBefore = Core2RunSkills.XpBefore(Run);
            var skill = Core2RunSkills.Settle(Data, Run, FightSkillXp?.Receipt() ?? new List<KeyValuePair<string, int>>(), victory, pool, Rules);
            skill.UsedArmaments.AddRange(FightSkillXp?.UsedArmaments ?? new List<string>());
            var progression = Core2RunSkills.LevelNotices(Data, skill.Awards, skill.UsedArmaments);
            FightSkillXp = null;
            var enemies = fight.Enemies.Select(e => new Core2XpEnemy { EnemyId = e.EnemyId, Defeated = !e.Alive || e.Hp <= 0, Level = e.Level, MaxHp = e.MaxHp, DamageMult = FightScale.Damage }).ToList();
            var xp = Core2RunProgression.CombatXp(Data, victory, pool, enemies);
            // JS: a configured run whose profile has character levels off banks no character XP (its earned points stay claimable).
            var award = Core2RunProgression.AwardLevelXp(Data, Run, Rules.CharacterLevels ? xp.Total : 0);
            var settlement = new Core2FightSettlement { Result = fight.Result, Pool = pool, EncounterId = (string)encounter["id"], Xp = xp, Award = award, Skill = skill, Progression = progression };
            LastSettlement = settlement;
            if (!victory)
            {
                Run.Hp = 0;
                Run.CombatEntered = null;
                OnFightSettled?.Invoke(this, settlement);
                if (progression.Count > 0) BeginNotices("defeat", progression, null);
                else FinishRun(false);
                Checkpoint();
                return Screen;
            }
            Run.FightsWon += 1;
            Run.CombatEntered = null;
            var range = (JArray)Data.Balance["rewardCinders"][pool];
            var cinders = (int)Math.Floor(Math.Floor(Rng.Int("misc", (int)range[0], (int)range[1]) * Data.RelicPassiveMult(Run.Relics, "runeGainMult")));
            Run.Cinders += cinders;
            settlement.Cinders = cinders;
            var boss = pool == "boss";
            if (boss) foreach (var id in Core2Json.Strings(encounter["enemies"])) if (!Run.BossesBeaten.Contains(id)) Run.BossesBeaten.Add(id);
            // The receipt reflects the settled (rate-scaled) awards, never raw combat XP.
            var victoryReceipt = settlement.VictoryReceipt = new JObject
            {
                ["title"] = Core2RunSkills.VictoryTitle(Content, encounter), ["xpBefore"] = xpBefore,
                ["xpGains"] = Core2RunSkills.XpGains(null, skill.Awards, award.Gained),
            };
            OnFightSettled?.Invoke(this, settlement);
            if (boss && Run.ActNumber >= Content.Routes.Acts) BeginNotices("victory", progression, victoryReceipt);
            else BeginReward("combat", boss ? "advanceAct" : "map", award.LevelUps, progression, victoryReceipt);
            Checkpoint();
            return Screen;
        }

        /// <summary>Settle the fight in progress as won without playing it (the auto-resolved oracle runs; a debug skip).</summary>
        public Core2Screen SettleFightAsWon()
        {
            if (Fight == null) throw new InvalidOperationException("No fight in progress");
            Fight.SettleAsWon();
            return FinishFight();
        }

        /// <summary>Drink a flask in the fight in progress: an HP or MP charge (chargeKind), or the utility flask in a potion slot.</summary>
        public void UseFlask(string chargeKind = null, int slot = -1, string targetId = null)
        {
            if (Fight == null) throw new InvalidOperationException("No fight in progress");
            Core2RunFlasks.UseInFight(Data, Run, Fight, chargeKind, slot, targetId);
        }

        /// <summary>Drink an HP or MP charge on the map (JS useRunChargeFlask).</summary>
        public void DrinkFlask(string chargeKind)
        {
            Require(Core2Screen.Map);
            Core2RunFlasks.DrinkOnMap(Data, Run, Rng, chargeKind);
            Checkpoint();
        }

        private void FinishRun(bool victory) => Run.Finish = new Core2RunFinish { Victory = victory };

        /// <summary>JS gameProfileCompletedRunMeta: record this finished run on the profile's class ledger (once) and name what it unlocks.</summary>
        public List<string> RecordFinish(Core2UnlockLedger ledger)
        {
            if (Run.Finish == null) throw new InvalidOperationException("The run has not finished");
            var unlocked = Core2RunProgression.RecordFinish(Content, ledger, Run.RunId, Run.Finish.Victory);
            Run.Finish.Unlocked = unlocked;
            return unlocked;
        }

        // ---- rewards -----------------------------------------------------------------------------------------------

        private void BeginReward(string source, string after, int levelOffers, JArray progression = null, JObject victoryReceipt = null)
        {
            var offerId = Run.ActNumber + ":" + Run.MapNodeId + ":" + source;
            Core2RunRewards.Offer(Data, Run, Rng, offerId, source);
            Run.PendingReward = new Core2PendingReward { OfferId = offerId, Source = source, After = after, LevelOffers = levelOffers,
                Progression = progression != null && progression.Count > 0 ? progression : null, VictoryReceipt = victoryReceipt };
        }

        /// <summary>JS beginGameProfileReward({ noticeOnly }): the receipt and notices with no offer; with neither, straight on.</summary>
        private void BeginNotices(string after, JArray progression, JObject victoryReceipt)
        {
            if ((progression == null || progression.Count == 0) && victoryReceipt == null) { LeaveNotices(after); return; }
            Run.PendingReward = new Core2PendingReward { OfferId = Run.ActNumber + ":" + Run.MapNodeId + ":combat", Source = "combat", After = after, NoticeOnly = true,
                Progression = progression != null && progression.Count > 0 ? progression : null, VictoryReceipt = victoryReceipt };
        }

        private void LeaveNotices(string after)
        {
            Run.PendingReward = null;
            if (after == "victory") FinishRun(true);
            else if (after == "defeat") FinishRun(false);
            else if (after == "advanceAct") AdvanceAct();
        }

        /// <summary>
        /// What the spoils screen shows now (JS showGameProfileReward): "victoryReceipt", then "levelNotices", then "offer";
        /// null when no spoils are pending. A notice-only spoils has no offer stage.
        /// </summary>
        public string RewardStage
        {
            get
            {
                var pending = Run.PendingReward;
                if (pending == null || Screen != Core2Screen.Reward) return null;
                if (pending.VictoryReceipt != null) return "victoryReceipt";
                if (pending.Progression != null && pending.Progression.Count > 0) return "levelNotices";
                return pending.NoticeOnly ? null : "offer";
            }
        }

        private void RequireStage(string stage)
        {
            Require(Core2Screen.Reward);
            if (RewardStage != stage) throw new InvalidOperationException("The spoils show " + (RewardStage ?? "nothing") + ", not " + stage);
        }

        /// <summary>After an acknowledgement: a notice-only spoils with nothing left to show leads on (JS showGameProfileReward).</summary>
        private void AfterAcknowledge()
        {
            var pending = Run.PendingReward;
            if (pending.NoticeOnly && pending.VictoryReceipt == null && (pending.Progression == null || pending.Progression.Count == 0)) LeaveNotices(pending.After);
            Checkpoint();
        }

        /// <summary>JS acknowledgeVictoryReceipt: the receipt's XP was paid already; it is shown once.</summary>
        public Core2Screen AcknowledgeVictoryReceipt()
        {
            RequireStage("victoryReceipt");
            Run.PendingReward.VictoryReceipt = null;
            AfterAcknowledge();
            return Screen;
        }

        /// <summary>JS acknowledgeSkillLevelNotices: only actual crossings were persisted; they are shown once.</summary>
        public Core2Screen AcknowledgeLevelNotices()
        {
            RequireStage("levelNotices");
            Run.PendingReward.Progression = null;
            AfterAcknowledge();
            return Screen;
        }

        public Core2RewardOffer CurrentOffer => Run.PendingReward == null || Run.PendingReward.NoticeOnly ? null : Run.RewardOffers[Run.PendingReward.OfferId];

        /// <summary>Take one option of the current offer (Inspect is the option's data; nothing else is collected).</summary>
        public Core2RewardOption TakeReward(int index)
        {
            RequireStage("offer");
            var draft = Core2RunSave.Clone(Run);
            var option = Core2RunRewards.Claim(Data, draft, Run.PendingReward.OfferId, index);
            Core2RunSave.CopyInto(draft, Run);
            Checkpoint();
            return option;
        }

        /// <summary>JS showGameProfileReward Continue: the next level offer, else the map or the next act.</summary>
        public Core2Screen ContinueReward()
        {
            RequireStage("offer");
            var pending = Run.PendingReward;
            Run.PendingReward = null;
            if (pending.LevelOffers > 0)
            {
                var offerId = pending.OfferId + ":level:" + pending.LevelOffers;
                Core2RunRewards.Offer(Data, Run, Rng, offerId, "level");
                Run.PendingReward = new Core2PendingReward { OfferId = offerId, Source = "level", After = pending.After, LevelOffers = pending.LevelOffers - 1 };
            }
            else if (pending.After == "advanceAct") AdvanceAct();
            Checkpoint();
            return Screen;
        }

        /// <summary>JS advanceAct: the next act on the next seat, HP full, a new map.</summary>
        private void AdvanceAct()
        {
            Run.ActNumber += 1; Run.Floor = 0; Run.MapNodeId = null; Run.Path = new List<string>(); Run.LastEncounters = new List<string>();
            Run.Hp = Run.MaxHp;
            Run.MapGraph = Core2ActMap.Build(Data, Rng, Core2ActMap.SeatAt(Run), Core2ActMap.Tier(Data, Run.ActNumber), Run.History);
        }

        /// <summary>JS rewardDeckAddRefusal: '' when the offered card may go straight into the deck, else notCard, full or limit.</summary>
        public string RewardDeckAddRefusal(int index)
        {
            RequireStage("offer");
            return Core2RunDeck.RewardDeckAddRefusal(Data, Run, Run.PendingReward.OfferId, index, DeckSettings);
        }

        /// <summary>JS claimGameProfileRewardToDeck: claim the card (once) and add it to the deck; a refusal changes nothing.</summary>
        public Core2RewardOption TakeRewardToDeck(int index)
        {
            RequireStage("offer");
            var option = Core2RunDeck.ClaimRewardToDeck(Data, Run, Run.PendingReward.OfferId, index, DeckSettings);
            Checkpoint();
            return option;
        }

        // ---- the Armoury and the deck editor (phase 9) -------------------------------------------------------------

        /// <summary>JS editingLocked: equipment and deck edits wait while a fight is unresolved.</summary>
        public bool EditingLocked => Core2RunArmoury.EditingLocked(Data, Fight);

        private void RequireUnlocked()
        {
            if (Run.Finish != null) throw new InvalidOperationException("The run has finished");
            if (EditingLocked) throw new InvalidOperationException(Core2RunArmoury.CombatChangeDisabled);
        }

        /// <summary>JS armouryHasNew: the Armoury glows for a find not yet looked at.</summary>
        public bool ArmouryHasNew => Core2RunArmoury.HasNew(Data, Run);

        /// <summary>JS showArmoury: looking clears the glow (persisted); returns every position with its Inspect data.</summary>
        public List<Core2ArmouryPosition> OpenArmoury()
        {
            RequireUnlocked();
            var positions = Core2RunArmoury.Positions(Data, Run);
            Core2RunArmoury.MarkSeen(Run);
            Checkpoint();
            return positions;
        }

        public List<Core2ArmouryPosition> ArmouryPositions() => Core2RunArmoury.Positions(Data, Run);
        public List<string> ReplaceCandidates(string slotId) => Core2RunArmoury.ReplaceCandidates(Data, Run, slotId);

        /// <summary>Armoury Replace: put an owned piece in a cell (the replaced armament goes to storage). Null on success, else why.</summary>
        public Core2ArmouryRefusal Equip(string slotId, int setIndex, string itemId)
        {
            if (Run.Finish != null) throw new InvalidOperationException("The run has finished");
            var refusal = Core2RunArmoury.Equip(Data, Run, slotId, setIndex, itemId, Fight);
            if (refusal == null) Checkpoint();
            return refusal;
        }

        /// <summary>Armoury Unequip: the piece leaves the cell (an armament to storage). Null on success, else why.</summary>
        public Core2ArmouryRefusal Unequip(string slotId, int setIndex) => Equip(slotId, setIndex, null);

        /// <summary>JS showDeckEditor / openDeckEdit: refused while a fight is unresolved, or when the Armoury has no deck door.</summary>
        public Core2DeckEdit OpenDeckEdit()
        {
            RequireUnlocked();
            if (!Core2RunArmoury.OpensDeckEditor(Data, DeckSettings)) throw new InvalidOperationException("Deck editing is disabled by the current settings");
            if (_deckEdit != null && !_deckEdit.Closed) throw new InvalidOperationException("A deck edit is already open");
            return _deckEdit = new Core2DeckEdit(Data, Run, DeckSettings, Checkpoint);
        }

        // ---- shops -------------------------------------------------------------------------------------------------

        public Core2RewardOffer ShopStock => Run.OpenShop == null ? null : Run.RewardOffers["shop:" + Run.OpenShop];

        public Core2RewardOption Buy(int index)
        {
            Require(Core2Screen.Shop);
            var option = Core2RunRewards.Purchase(Data, Run, Run.OpenShop, index);
            Checkpoint();
            return option;
        }

        public void LeaveShop() { Require(Core2Screen.Shop); Run.OpenShop = null; Checkpoint(); }

        // ---- rest --------------------------------------------------------------------------------------------------

        public Core2RestVisit RestVisit => Run.RestVisitId != null && Run.RestVisits.TryGetValue(Run.RestVisitId, out var v) ? v : null;
        public List<string> RestServices() => Core2RunRest.OpenServices(Data, Run);
        public List<Core2SmithCandidate> SmithCandidates() => Core2RunRest.SmithPlan(Data, Run);
        public void Smith(string itemRef) { Require(Core2Screen.Rest); Core2RunRest.Commit(Data, Run, "smith", itemRef: itemRef); Checkpoint(); }
        public void LevelUp(string attributeId) { Require(Core2Screen.Rest); Core2RunRest.Commit(Data, Run, "levelUp", attributeId: attributeId, characterLevels: Rules.CharacterLevels); Checkpoint(); }
        public void LeaveRest() { Require(Core2Screen.Rest); Run.RestVisitId = null; Checkpoint(); }

        // ---- events ------------------------------------------------------------------------------------------------

        /// <summary>The open event's choices the run's history opens, with whether each is affordable and runnable in Core 2.</summary>
        public List<(Core2EventChoice Choice, bool Affordable, bool Runnable)> EventChoices()
        {
            if (Run.OpenEvent == null) return new List<(Core2EventChoice, bool, bool)>();
            return Core2RunEvents.Available(Content, Run, Run.OpenEvent).Select(c => (c, Core2RunEvents.Affordable(c, Run), Core2RunEvents.Runnable(c))).ToList();
        }

        public Core2EventReceipt ChooseEvent(string choiceId)
        {
            Require(Core2Screen.Event);
            if (Run.OpenEventChosen) throw new InvalidOperationException("This event's response is already taken");
            var choice = Core2RunEvents.Choices(Content, Run.OpenEvent).FirstOrDefault(c => c.Id == choiceId);
            if (choice != null && !Core2RunEvents.Runnable(choice)) throw new InvalidOperationException("Configured classes stay fixed for the run; this choice cannot be taken in Core 2");
            LastEvent = Core2RunEvents.Commit(Data, Run, Rng, Run.OpenEvent, choiceId);
            Run.OpenEventChosen = true;
            if (LastEvent.Encounter != null) Run.CombatEntered = new Core2CombatEntry { NodeId = Run.MapNodeId, EncounterId = LastEvent.Encounter };
            Checkpoint();
            return LastEvent;
        }

        /// <summary>JS showEvent onDone: a stored fight starts now, else back to the map.</summary>
        public Core2Screen ContinueEvent()
        {
            Require(Core2Screen.Event);
            if (!Run.OpenEventChosen) throw new InvalidOperationException("Choose a response first");
            Run.OpenEvent = null; Run.OpenEventChosen = false;
            if (Run.CombatEntered != null)
            {
                var encounterId = Run.CombatEntered.EncounterId;
                Run.CombatEntered = null;
                EnterCombat(Run.MapNodeId, encounterId);
            }
            Checkpoint();
            return Screen;
        }
    }

    public sealed class Core2FightSettlement
    {
        public string Result { get; set; }
        public string Pool { get; set; }
        public string EncounterId { get; set; }
        public Core2XpReceipt Xp { get; set; }
        public Core2LevelAward Award { get; set; }
        public int Cinders { get; set; }
        /// <summary>The class and weapon XP this fight paid (phase 10).</summary>
        public Core2SkillSettlement Skill { get; set; }
        /// <summary>The level notices this fight earned (JS skillLevelNotices rows).</summary>
        public JArray Progression { get; set; }
        /// <summary>A won fight's receipt: { title, xpBefore, xpGains }.</summary>
        public JObject VictoryReceipt { get; set; }
    }
}
