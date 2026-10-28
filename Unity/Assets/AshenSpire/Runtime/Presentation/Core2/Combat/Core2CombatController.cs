// Core2CombatController.cs — the Core 2 combat screen's state machine, engine-free: it drives session.Fight only through
// its public commands (PlayCard, EndTurn, UseFlask, ChooseReaction, SetReactions) plus the screen's Auto switch, plays each
// command's events back as paced beats over a display copy of the numbers, keeps every other input out while an offer
// is pending, answers offers itself while Auto is on, and reports the fight over exactly once after its last beat.
// The UI Toolkit screen calls Tick with the clock and paints Model(); the console suite does the same with a fake clock.
//
// JS at the pin:
//   src/ui/screens/combat.js   dispatch → afterDispatch → playTimeline (busy while beats play), playCard / useFlask /
//                              answerReaction (manual picks teach Auto), showPendingReaction, scheduleAutoCombat (an
//                              offer waits one gap, then Auto answers; it never plays cards or ends the turn), the
//                              selection and one-tap play rules, the key handler (cardHotkeyAction, E, Escape),
//                              onEnd after the fight's end delay
//   src/model/autoCombatPolicy.js resolveCombatAutomation, chooseReaction (Core2AutoCombat)
//   src/ui/animationPace.js    actionPacing (Core2CombatPacing.Pace)
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Presentation.Core2
{
    /// <summary>The combat settings the screen reads (JS settings keys), with the JS defaults until the host supplies its own.</summary>
    public sealed class Core2CombatSettings
    {
        /// <summary>autoCombat, combatSpeed, autoCombatSpeed, combatStepPause, animationStepDuration, combatPacingPreset, oneTapPlay.</summary>
        public JObject Values { get; set; } = new JObject();
        /// <summary>The animation speed choice (slow, normal, fast, instant).</summary>
        public string AnimSpeed { get; set; } = "normal";
        public bool ReducedMotion { get; set; }
        /// <summary>Raised after a change the screen made (the Auto pill), so the host can persist it.</summary>
        public Action<Core2CombatSettings> Changed { get; set; }

        public bool Auto => Values["autoCombat"]?.Type == JTokenType.Boolean && (bool)Values["autoCombat"];
        public bool OneTapPlay => Values["oneTapPlay"]?.Type == JTokenType.Boolean && (bool)Values["oneTapPlay"];
        public string Preset => (string)Values["combatPacingPreset"] ?? "custom";

        /// <summary>JS combatPacingChanges: choosing mobile-fast writes both values; editing either returns to Custom.</summary>
        public void Set(Core2CombatPacing pacing, string key, JToken value)
        {
            foreach (var change in pacing.Changes(key, value).Properties()) Values[change.Name] = change.Value;
            Changed?.Invoke(this);
        }
    }

    public sealed class Core2CombatController
    {
        public Core2RunSession Session { get; }
        public Core2Fight Fight { get; }
        public Core2Content Content => Fight.Content;
        public Core2CombatSettings Settings { get; }
        public Core2CombatPacing Pacing { get; }
        public Core2CombatLogDrawer Log { get; }
        public IReadOnlyDictionary<string, string> CardArt { get; }
        public Core2ManualReactionPicks Picks { get; } = new Core2ManualReactionPicks();

        /// <summary>The numbers on screen (a beat's state while one plays; the fight's own when idle).</summary>
        public Core2CombatDisplay Display { get; private set; }
        public Core2Timeline Timeline { get; private set; }
        public bool Busy => Timeline != null;
        /// <summary>The armed card (JS selected).</summary>
        public string Selected { get; private set; }
        public string ReactionSelected { get; private set; }
        /// <summary>The enemy the inspector shows, or null.</summary>
        public string Inspecting { get; private set; }
        public string LastRefusal { get; private set; }
        public double NowMs { get; private set; }
        public bool FightOverReported { get; private set; }

        /// <summary>A stage reached its time: the view shows its callouts and floaters (and moves the actor).</summary>
        public event Action<Core2TimelineStep> StagePlayed;
        /// <summary>Something the model reads changed: repaint.</summary>
        public event Action Changed;

        private readonly Action _onFightOver;
        private double _timelineStart;
        private int _nextStep;
        private readonly HashSet<Core2FightEvent> _applied = new HashSet<Core2FightEvent>();
        private double? _autoAt;
        private string _autoOffer;
        private double? _fightOverAt;
        private readonly double _windup;
        private readonly double _fightEndDelayMs;

        public Core2CombatController(Core2RunSession session, Action onFightOver, Core2CombatSettings settings = null, IReadOnlyDictionary<string, string> cardArt = null, Core2Fight fight = null)
        {
            Session = session;
            Fight = fight ?? session?.Fight ?? throw new InvalidOperationException("No fight in progress");
            _onFightOver = onFightOver;
            Settings = settings ?? new Core2CombatSettings();
            Pacing = new Core2CombatPacing(Fight.Content);
            Log = new Core2CombatLogDrawer(Fight.Content);
            CardArt = cardArt ?? new Dictionary<string, string>();
            Display = Core2CombatDisplay.Snapshot(Fight);
            var timing = (JObject)Fight.Content.Table("ui/combat.json")["combatTiming"];
            _windup = (double?)timing?["windupFraction"] ?? 0.55;
            _fightEndDelayMs = (double?)timing?["fightEndDelayMs"] ?? 210;
            ScheduleFollowUps();
        }

        public Core2CombatScreenModel Model() => Core2CombatViewModel.Build(new Core2CombatViewInputs
        {
            Session = Session, Fight = Fight, Display = Display, Selected = Selected, Busy = Busy, Auto = Settings.Auto,
            ReactionSelected = ReactionSelected, CardArt = CardArt, FlaskArmed = ArmedFlask != null,
        });

        public Core2ActionPace Pace() => Pacing.Pace(Settings.AnimSpeed, Settings.Values, Settings.ReducedMotion);

        // ================================================================================================================
        // Input gates
        // ================================================================================================================

        /// <summary>Card, End Turn, flask and switch commands are open: idle, the player's turn, no offer pending, fight on.</summary>
        public bool CanAct => !Busy && Fight.Result == null && Fight.PendingReaction == null && Fight.Phase == "player";

        private bool Refuse(string why) { LastRefusal = why; Changed?.Invoke(); return false; }

        // ================================================================================================================
        // Selection and targeting (JS selected / armSelf / oneTapPlays)
        // ================================================================================================================

        /// <summary>A tap or click on a hand card: select it; tapping the selected card plays it when its target is unambiguous.</summary>
        public bool TapCard(string instanceId)
        {
            if (!CanAct) return Refuse("busy");
            if (!Fight.Hand.Any(c => c.InstanceId == instanceId)) return Refuse("notInHand");
            var target = UnambiguousTarget(instanceId, out var ambiguous);
            if (Selected == instanceId || Settings.OneTapPlay && !ambiguous)
            {
                if (ambiguous) return Refuse("chooseTarget");
                return PlayCard(instanceId, target);
            }
            Selected = instanceId;
            Inspecting = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The target a play needs when it is beyond doubt: a hostile card's only living enemy, or no target at all.</summary>
        public string UnambiguousTarget(string instanceId, out bool ambiguous)
        {
            ambiguous = false;
            if (!Fight.CardTargetsEnemy(instanceId)) return null;
            var living = Fight.Enemies.Where(e => e.Alive).ToList();
            if (living.Count == 1) return living[0].Id;
            ambiguous = true;
            return null;
        }

        /// <summary>A tap on an enemy: with a hostile card or flask armed, use it there; otherwise open that enemy's inspector.</summary>
        public bool TapEnemy(string enemyId)
        {
            var enemy = Fight.Enemies.FirstOrDefault(e => e.Id == enemyId);
            if (enemy == null || !enemy.Alive) return Refuse("noTarget");
            if (ArmedFlask != null && CanAct) { var slot = ArmedFlask.Value; ArmedFlask = null; return UseFlask(null, slot, enemyId); }
            if (Selected != null && CanAct && Fight.CardTargetsEnemy(Selected)) return PlayCard(Selected, enemyId);
            return Inspect(enemyId);
        }

        /// <summary>The utility flask waiting for an enemy (JS selectedFlask), or null.</summary>
        public int? ArmedFlask { get; private set; }

        /// <summary>JS openPotions → a targeted flask: one living enemy takes it at once, otherwise it waits for a target.</summary>
        public bool ArmFlask(int slot)
        {
            if (!CanAct) return Refuse("busy");
            var living = Fight.Enemies.Where(e => e.Alive).ToList();
            if (living.Count == 1) return UseFlask(null, slot, living[0].Id);
            ArmedFlask = slot; Selected = null;
            Changed?.Invoke();
            return true;
        }

        public bool Inspect(string enemyId)
        {
            if (Fight.PendingReaction != null) return Refuse("reactionPending");
            Inspecting = enemyId != null && Fight.Enemies.Any(e => e.Id == enemyId) ? enemyId : null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Escape / Back: drop the selection, close the inspector.</summary>
        public void Cancel()
        {
            Selected = null; Inspecting = null; ArmedFlask = null;
            Changed?.Invoke();
        }

        // ================================================================================================================
        // Commands (the only ways the fight is changed)
        // ================================================================================================================

        public bool PlayCard(string instanceId, string targetId = null)
        {
            if (!CanAct) return Refuse("busy");
            Selected = null;
            return Dispatch(() => Fight.PlayCard(instanceId, targetId));
        }

        public bool EndTurn()
        {
            if (!CanAct) return Refuse("busy");
            Selected = null;
            return Dispatch(() => Fight.EndTurn());
        }

        /// <summary>JS End Turn row (model/secondbeat.js): a hold is owed while a card is still playable; else a tap ends the turn.</summary>
        public bool EndTurnNeedsHold => Fight.Hand.Any(c => Fight.CheckPlay(c.InstanceId, Fight.CardTargetsEnemy(c.InstanceId) ? Fight.Enemies.FirstOrDefault(e => e.Alive)?.Id : null) == null);

        public bool UseFlask(string chargeKind, int slot = -1, string targetId = null)
        {
            if (!CanAct) return Refuse("busy");
            if (Session == null) return Refuse("noRun");
            Selected = null;
            return Dispatch(() => Session.UseFlask(chargeKind, slot, targetId));
        }

        /// <summary>The Reaction switch: only between actions on the player turn (JS setReactionsEnabled).</summary>
        public bool SetReactions(bool enabled)
        {
            if (!CanAct) return Refuse("busy");
            try { Fight.SetReactions(enabled); }
            catch (InvalidOperationException error) { return Refuse(error.Message); }
            Changed?.Invoke();
            return true;
        }

        /// <summary>The Auto pill: Auto answers reaction prompts only (×1.5 pacing while on); it never plays or ends a turn.</summary>
        public void SetAuto(bool enabled)
        {
            Settings.Values["autoCombat"] = enabled;
            Settings.Changed?.Invoke(Settings);
            ScheduleFollowUps();
            Changed?.Invoke();
        }

        public void SelectReactionOption(string optionId)
        {
            if (Fight.PendingReaction == null || Busy) return;
            ReactionSelected = Fight.PendingReaction.Options.Any(o => o.Id == optionId) ? optionId : null;
            Changed?.Invoke();
        }

        /// <summary>Play (the selected option) or Back/Resolve (null). Manual picks teach Auto (JS recordManualReaction).</summary>
        public bool AnswerReaction(string optionId, bool manual = true)
        {
            var offer = Fight.PendingReaction;
            if (offer == null || Busy) return Refuse("noOffer");
            if (manual && optionId != null)
            {
                var option = offer.Options.FirstOrDefault(o => o.Id == optionId);
                var instance = option == null ? null : Fight.Hand.FirstOrDefault(c => c.InstanceId == option.Play.CardInstanceId);
                if (instance != null) Picks.Record(Core2AutoCombat.ReactionKind(Fight.CardCarrier(instance)));
            }
            ReactionSelected = null;
            _autoAt = null; _autoOffer = null;
            return Dispatch(() => Fight.ChooseReaction(offer.Id, optionId));
        }

        private bool Dispatch(Action command)
        {
            if (Busy) return Refuse("busy");
            var before = Core2CombatDisplay.Snapshot(Fight);
            var start = Fight.Events.Count;
            try { command(); }
            catch (InvalidOperationException error)
            {
                LastRefusal = error.Message;
                Display = Core2CombatDisplay.Snapshot(Fight);
                Changed?.Invoke();
                return false;
            }
            LastRefusal = null;
            Display = before;
            var events = Fight.Events.Skip(start).ToList();
            Timeline = Core2CombatBeats.Plan(events, Content, Core2BeatTiming.From(Pace(), _windup), MaxHp);
            _applied.Clear();
            _timelineStart = NowMs;
            _nextStep = 0;
            Changed?.Invoke();
            Advance();
            return true;
        }

        private int MaxHp(string id) => Fight.Find(id)?.MaxHp ?? 0;

        // ================================================================================================================
        // The clock
        // ================================================================================================================

        /// <summary>Advance to <paramref name="nowMs"/>: play every stage that is due, then Auto and the fight-over report.</summary>
        public void Tick(double nowMs)
        {
            NowMs = Math.Max(NowMs, nowMs);
            Advance();
        }

        /// <summary>JS skip (a tap during playback): every remaining stage at once, then the fight as it stands.</summary>
        public void Skip()
        {
            if (Timeline == null) return;
            _timelineStart = double.NegativeInfinity;
            Advance();
        }

        private void Advance()
        {
            if (Timeline != null)
            {
                while (_nextStep < Timeline.Steps.Count && _timelineStart + Timeline.Steps[_nextStep].AtMs <= NowMs)
                {
                    var step = Timeline.Steps[_nextStep++];
                    if (step.Stage == "hud" || step.Stage == "resolution")
                        foreach (var e in (step.Stage == "hud" ? step.Events : Timeline.Beats[step.Beat].Events).Where(e => _applied.Add(e))) Display.Apply(e);
                    StagePlayed?.Invoke(step);
                    if (step.Stage == "hud" || step.Stage == "resolution") Changed?.Invoke();
                }
                if (_nextStep >= Timeline.Steps.Count && _timelineStart + Timeline.DurationMs <= NowMs)
                {
                    Timeline = null;
                    Display = Core2CombatDisplay.Snapshot(Fight);
                    ScheduleFollowUps();
                    Changed?.Invoke();
                }
            }
            if (Timeline != null) return;
            if (_autoAt != null && NowMs >= _autoAt.Value && Fight.PendingReaction?.Id == _autoOffer && Settings.Auto)
            {
                _autoAt = null;
                var answer = Core2AutoCombat.Answer(Fight, Picks.Snapshot());
                AnswerReaction(answer, manual: false);
                return;
            }
            if (_fightOverAt != null && NowMs >= _fightOverAt.Value && !FightOverReported)
            {
                FightOverReported = true;
                _fightOverAt = null;
                _onFightOver?.Invoke();
            }
        }

        /// <summary>After a playback: arm Auto on a fresh offer (one gap, at least the JS 50 ms poll) and the end of a won or lost fight.</summary>
        private void ScheduleFollowUps()
        {
            if (Fight.Result != null)
            {
                if (_fightOverAt == null && !FightOverReported) _fightOverAt = NowMs + _fightEndDelayMs * (Pace().Scale > 0 ? 1 : 0);
                return;
            }
            var offer = Fight.PendingReaction;
            if (offer != null && Settings.Auto)
            {
                if (_autoOffer != offer.Id) { _autoOffer = offer.Id; _autoAt = NowMs + Math.Max(50, Pace().GapMs); }
            }
            else { _autoAt = null; _autoOffer = null; }
        }

        // ================================================================================================================
        // Keyboard (JS keyHandler): 1–9 and Q play or target, E ends the turn, Escape cancels
        // ================================================================================================================

        /// <summary>
        /// A key press. Returns true when the screen handled it. E only requests the End Turn: the view runs its hold while
        /// EndTurnNeedsHold (holding E commits) and calls EndTurn; here a tap ends the turn only when nothing is playable.
        /// </summary>
        public bool Key(string key)
        {
            if (key == "Escape") { Cancel(); return true; }
            if (Fight.PendingReaction != null || Busy || Fight.Result != null || Fight.Phase != "player") return false;
            if (key == "e" || key == "E") return !EndTurnNeedsHold && EndTurn();
            var living = Fight.Enemies.Where(e => e.Alive).ToList();
            var hotkey = Core2CardHotkey.For(key, ArmedFlask != null || Selected != null && Fight.CardTargetsEnemy(Selected), Fight.Hand.Count, living.Count);
            if (hotkey == null) return false;
            if (hotkey.Kind == "none") return true;
            if (hotkey.Kind == "target") return TapEnemy(living[hotkey.Index].Id);
            var instance = Fight.Hand[hotkey.Index];
            var check = Fight.CheckPlay(instance.InstanceId, Fight.CardTargetsEnemy(instance.InstanceId) ? living.FirstOrDefault()?.Id : null);
            if (check != null) return Refuse(check.Reason);
            if (Fight.CardTargetsEnemy(instance.InstanceId)) { Selected = instance.InstanceId; Changed?.Invoke(); return true; }
            // JS armSelf: a self-targeted card arms first; the same key again plays it.
            if (Selected == instance.InstanceId) return PlayCard(instance.InstanceId);
            Selected = instance.InstanceId; Changed?.Invoke();
            return true;
        }
    }
}
