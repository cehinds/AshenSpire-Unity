// Core2RunFlow.cs — the Core 2 run host's brain (JS src/main.js between screens): it owns the Core2RunSession, saves
// after every accepted command (JS persist), resumes a saved slot onto the same screen, and records a finished run on
// the profile ledger once (JS finishRun: completedRunMeta, then clearRun). Engine-free: the Unity host
// (Core2RunHost) draws whatever Screen says, and UnityTests/Core2RunView plays whole runs through it.
//
// A refused command (the domain throws InvalidOperationException naming why) changes nothing, saves nothing and
// returns the sentence for the screen's status line, as the JS screens print error.message.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Core2;

namespace AshenSpire.Presentation.Core2
{
    public sealed class Core2RunFlow
    {
        public Core2RunData Data { get; }
        public Core2Content Content => Data.Content;
        public Core2SaveSlots Slots { get; }
        public Core2Profile Profile { get; private set; }
        /// <summary>Why the stored profile could not be read (then it is never overwritten); null when it loaded.</summary>
        public string ProfileRefusal { get; private set; }
        public Core2RunSession Session { get; private set; }
        public int Slot { get; private set; } = -1;
        /// <summary>The player's Deck settings, handed to every session (Settings → Advanced → Deck in JS).</summary>
        public Core2DeckSettings DeckSettings { get; set; }
        /// <summary>Class names a finished run unlocked (shown on the finished-run screen as Earned).</summary>
        public List<string> Earned { get; } = new List<string>();
        /// <summary>The last refusal or failure sentence (null after an accepted command).</summary>
        public string Notice { get; private set; }
        /// <summary>Raised after every accepted command and every screen change.</summary>
        public event Action Changed;
        private readonly Func<DateTime> _now;

        public Core2RunFlow(Core2RunData data, Core2SaveSlots slots, Func<DateTime> now = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _now = now ?? (() => DateTime.UtcNow);
            Profile = Slots.LoadProfile(out var refusal);
            ProfileRefusal = refusal;
            DeckSettings = Core2DeckSettings.From(data);
        }

        public Core2UnlockLedger Ledger => Profile.Ledger(Content);
        public Core2Screen Screen => Session?.Screen ?? Core2Screen.Map;
        public bool HasRun => Session != null;

        /// <summary>JS gameProfileClassAvailable: a class the profile's ledger opens.</summary>
        public bool ClassAvailable(string classId) => Core2RunProgression.ClassAvailable(Content, Ledger, classId);

        // ---- begin, continue -------------------------------------------------------------------------------------

        /// <summary>JS newRun + startClimb: a Core 2 run of the class on the typed seed, in the slot, saved at once.</summary>
        public string Begin(string classId, string seedText, int slot, string runId = null)
        {
            var why = Core2Seed.Problem(seedText);
            if (why != null) return Notice = why;
            if (!ClassAvailable(classId)) return Notice = "Class '" + classId + "' is locked in this profile";
            if (slot < 0 || slot >= Core2SaveSlots.SlotCount) return Notice = "Choose a slot first.";
            Bind(Core2RunSession.Start(Data, classId, Core2Seed.FromText(seedText), Ledger, runId ?? Guid.NewGuid().ToString("N")), slot);
            Slots.Delete(slot); // an overwritten slot's old run is not kept as the new run's backup
            return Accept();
        }

        /// <summary>JS resumeRun: the slot's run on the screen it was saved at; a refusal lands nowhere and says why.</summary>
        public string Continue(int slot = -1)
        {
            if (slot < 0) slot = Slots.Latest();
            if (slot < 0) return Notice = "No saved climb to continue.";
            try { Bind(Core2RunSession.Resume(Data, Slots.Load(slot)), slot); }
            catch (Core2SaveRefused refused) { return Notice = "THIS SAVE COULD NOT BE RESTORED · " + refused.Message; }
            Notice = null;
            AfterScreenChange();
            Changed?.Invoke();
            return null;
        }

        private void Bind(Core2RunSession session, int slot)
        {
            Session = session;
            Session.DeckSettings = DeckSettings;
            Slot = slot;
            Earned.Clear();
        }

        /// <summary>Leave the run in memory (back to the title); the slot keeps its last save.</summary>
        public void Release() { Session = null; Slot = -1; }

        // ---- the command door ------------------------------------------------------------------------------------

        private string Accept()
        {
            Notice = null;
            AfterScreenChange();
            Save();
            Changed?.Invoke();
            return null;
        }

        /// <summary>Run one command: accepted → saved and Changed; refused → the sentence, nothing saved.</summary>
        public string Do(Action command)
        {
            if (Session == null) return Notice = "No climb in progress.";
            try { command(); }
            catch (InvalidOperationException error) { Notice = error.Message; Changed?.Invoke(); return Notice; }
            return Accept();
        }

        /// <summary>Persist the run (JS persist). A finished run is not saved: its slot is cleared once it is recorded. While a
        /// deck edit is open the run holds provisional piles (Confirm validates, Cancel restores), so the slot keeps the last
        /// committed state until the edit closes.</summary>
        public void Save()
        {
            if (Session == null || Slot < 0 || Session.Run.Finish != null || DeckEdit != null) return;
            Slots.Save(Slot, Session.Run, _now());
        }

        /// <summary>JS finishRun: the ledger records the finished run once (completedRunMeta), then the slot is cleared.</summary>
        private void AfterScreenChange()
        {
            if (Session?.Run.Finish == null) return;
            var run = Session.Run;
            if (!Ledger.CompletedRunIds.Contains(run.RunId))
            {
                var unlocked = Session.RecordFinish(Ledger);
                Earned.Clear();
                Earned.AddRange(unlocked.Select(id => Content.Classes.FirstOrDefault(c => c.Id == id)?.Name ?? id));
                if (ProfileRefusal == null) Slots.SaveProfile(Profile);
            }
            if (Slot >= 0) Slots.Delete(Slot);
        }

        // ---- the screens' commands (each one is a JS screen callback) --------------------------------------------

        public string Travel(string nodeId) => Do(() => Session.Travel(nodeId));
        /// <summary>The fight screen reports Fight.Result set (ICore2FightScreen onFightOver): settle it.</summary>
        public string FightOver() => Do(() => Session.FinishFight());
        public string AcknowledgeReceipt() => Do(() => Session.AcknowledgeVictoryReceipt());
        public string AcknowledgeNotices() => Do(() => Session.AcknowledgeLevelNotices());
        public string TakeReward(int index) => Do(() => Session.TakeReward(index));
        public string TakeRewardToDeck(int index) => Do(() => Session.TakeRewardToDeck(index));
        public string ContinueReward() => Do(() => Session.ContinueReward());
        public string Buy(int index) => Do(() => Session.Buy(index));
        public string LeaveShop() => Do(() => Session.LeaveShop());
        public string Smith(string itemRef) => Do(() => Session.Smith(itemRef));
        public string LevelUp(string attributeId) => Do(() => Session.LevelUp(attributeId));
        public string LeaveRest() => Do(() => Session.LeaveRest());
        public string ChooseEvent(string choiceId) => Do(() => Session.ChooseEvent(choiceId));
        public string ContinueEvent() => Do(() => Session.ContinueEvent());
        public string DrinkFlask(string kind) => Do(() => Session.DrinkFlask(kind));
        public string MarkArmourySeen() => Do(() => Session.OpenArmoury());

        /// <summary>Armoury Replace / Unequip: null on success, else the refusal sentence (nothing changes).</summary>
        public string Equip(string slotId, int setIndex, string itemId)
        {
            if (Session == null) return Notice = "No climb in progress.";
            Core2ArmouryRefusal refusal;
            try { refusal = Session.Equip(slotId, setIndex, itemId); }
            catch (InvalidOperationException error) { return Notice = error.Message; }
            if (refusal != null) return Notice = refusal.Text;
            return Accept();
        }

        // ---- the deck editor (a modal: the run waits until Confirm or Cancel) ------------------------------------

        public Core2DeckEdit DeckEdit { get; private set; }

        public string OpenDeckEdit()
        {
            if (Session == null) return Notice = "No climb in progress.";
            try { DeckEdit = Session.OpenDeckEdit(); }
            catch (InvalidOperationException error) { return Notice = error.Message; }
            Notice = null;
            Changed?.Invoke();
            return null;
        }

        public string ConfirmDeckEdit()
        {
            if (DeckEdit == null) return null;
            var (ok, refusal) = DeckEdit.Confirm();
            if (!ok) return Notice = refusal;
            DeckEdit = null;
            return Accept();
        }

        public void CancelDeckEdit()
        {
            if (DeckEdit == null) return;
            DeckEdit.Cancel();
            DeckEdit = null;
            Accept();
        }
    }
}
