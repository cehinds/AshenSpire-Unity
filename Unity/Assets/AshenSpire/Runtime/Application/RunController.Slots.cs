// RunController.Slots.cs — three native run slots and the result archive, part of RunController.
// ATTACH: no additional MonoBehaviour. OnEnable calls InitSaveSlots once after the view exists.
// STORAGE: OriginalSaveSlots over PlayerPrefs (per channel keys; see docs/Unity-Save-Slots.md).
// BOOT: MigrateLegacy copies the one-run save into slot 0 once; the legacy key is never written.
// DEFAULT PATH: title Continue resumes the most recently saved slot; title New starts in the
// first empty slot with no extra step, and only opens the slot picker when all three are full.
// SAVE: every checkpoint goes to the active slot with the running playtime. A save that does
// not read back keeps the slot's previous record and is reported on the next title screen.
// RESULTS: a finished run is recorded once through RecordResult (FIFO archive of 20).
using System;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private OriginalSaveSlots _slotSaves;
        private int _activeSlot = -1;
        private long _playtimeBase;
        private float _playtimeSince;
        private string _slotNotice;
        private void InitSaveSlots(string channel)
        {
            var storage = new OriginalDelegateSaveStorage(key => PlayerPrefs.GetString(key, ""), (key, value) => PlayerPrefs.SetString(key, value), PlayerPrefs.Save, PlayerPrefs.DeleteKey);
            _slotSaves = new OriginalSaveSlots(storage, channel, UnityEngine.Application.version);
            try
            {
                var outcome = _slotSaves.MigrateLegacy();
                if (outcome != OriginalLegacyMigration.AlreadyDone) Debug.Log("ASHENSPIRE_SAVE_MIGRATION " + outcome);
            }
            catch (Exception error) { Debug.LogWarning("The earlier native save was not moved into slot 1: " + error.Message); }
            _view.SlotsRequested += ShowSaveSlots;
        }
        private static bool Loadable(OriginalSaveSlotInfo slot) => slot.State == OriginalSaveSlotState.Ready || slot.State == OriginalSaveSlotState.RecoveredBackup;
        private bool HasNativeSlotSave() => _slotSaves != null && _slotSaves.List().Any(Loadable);
        // The slot Continue resumes: newest last-saved time, lowest slot on a tie.
        private int LatestSlot() => _slotSaves.List().Where(Loadable)
            .OrderByDescending(s => s.Meta?.LastSavedUtc ?? "", StringComparer.Ordinal).ThenBy(s => s.Slot)
            .Select(s => s.Slot).DefaultIfEmpty(-1).First();
        private void ResumeOriginal() => ResumeSlot(LatestSlot());
        private void ResumeSlot(int slot)
        {
            try
            {
                if (slot < 0) throw new InvalidOperationException("No saved native climb to continue.");
                LoadOriginalProfile();
                var snapshot = _slotSaves.Load(slot, value => OriginalGameSession.Restore(value), out var meta, out var recovered);
                var game = OriginalGameSession.Restore(snapshot);
                _activeSlot = slot; _playtimeBase = meta?.PlaytimeSeconds ?? 0; _playtimeSince = Time.realtimeSinceStartup;
                BindOriginal(game);
                if (recovered) Debug.LogWarning("Recovered the previous native run checkpoint in slot " + (slot + 1) + ".");
                RefreshOriginal();
            }
            catch (Exception error)
            {
                Debug.LogWarning(error.Message);
                _view.NativeSaveAvailable = HasNativeSlotSave();
                _view.Title(_content, _saves.HasSave, "The native save could not be restored. Existing records are preserved.");
            }
        }
        // Title "New": the first empty slot, straight into creation (the pre-slot flow).
        private void NewOriginal()
        {
            var empty = _slotSaves.List().FirstOrDefault(s => s.State == OriginalSaveSlotState.Empty);
            if (empty != null) CreateOriginal(empty.Slot);
            else ShowSaveSlots("Every slot holds a climb. Overwrite one, or delete one first.");
        }
        // Called when creation commits: an overwritten slot is cleared so its old run is not kept as the new run's backup.
        private void BeginSlot(int slot)
        {
            if (_slotSaves.List()[slot].State != OriginalSaveSlotState.Empty) _slotSaves.Delete(slot);
            _activeSlot = slot; _playtimeBase = 0; _playtimeSince = Time.realtimeSinceStartup;
        }
        private void SaveOriginalSlot()
        {
            if (_originalGame == null || _slotSaves == null || _activeSlot < 0) return;
            var playtime = _playtimeBase + (long)Math.Max(0f, Time.realtimeSinceStartup - _playtimeSince);
            if (!_slotSaves.Save(_activeSlot, _originalGame.Snapshot(), playtime))
            {
                Debug.LogWarning("Native save to slot " + (_activeSlot + 1) + " did not verify; the slot keeps its previous save.");
                _slotNotice = "The last save to slot " + (_activeSlot + 1) + " could not be verified. The slot keeps its previous save; free some storage and keep playing to retry.";
            }
        }
        private string TakeSlotNotice() { var notice = _slotNotice; _slotNotice = null; return notice; }
        private void RecordOriginalResult(JObject run, bool victory)
        {
            var receipt = _slotSaves.RecordResult(_profile, run, victory);
            if (!(bool)receipt["saved"]) Debug.LogWarning("The finished climb was recorded but the profile save did not verify.");
        }
        private void ShowSaveSlots() => ShowSaveSlots(null);
        private void ShowSaveSlots(string notice)
        {
            LoadOriginalProfile();
            _view.Slots(_slotSaves.List(), ClassName, notice ?? TakeSlotNotice(), ResumeSlot, CreateOriginal, DeleteSlot, CopySlot);
        }
        private string ClassName(string id)
        {
            try { return (string)_originalContent.Record("classes", id)?["name"] ?? id; }
            catch { return id; }
        }
        private void DeleteSlot(int slot)
        {
            _slotSaves.Delete(slot);
            if (slot == _activeSlot) _activeSlot = -1; // the bound run must not write itself back
            ShowSaveSlots("Slot " + (slot + 1) + " deleted.");
        }
        private void CopySlot(int from, int to)
        {
            string notice;
            try { notice = _slotSaves.Copy(from, to) ? "Slot " + (from + 1) + " copied to slot " + (to + 1) + "." : "The copy could not be verified. Slot " + (to + 1) + " is unchanged."; }
            catch (Exception error) { notice = "The copy failed: " + error.Message; }
            ShowSaveSlots(notice);
        }
    }
}
