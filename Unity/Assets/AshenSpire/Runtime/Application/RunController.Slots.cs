// RunController.Slots.cs — three native run slots and the result archive, part of RunController.
// ATTACH: no additional MonoBehaviour. OnEnable calls InitSaveSlots once after the view exists.
// STORAGE: OriginalSaveSlots over PlayerPrefs (per channel keys; see docs/Unity-Save-Slots.md).
// BOOT: MigrateLegacy copies the one-run save into slot 0 once; the legacy key is never written.
// DEFAULT PATH: title Continue resumes the most recently saved slot; title New starts in the
// first empty slot with no extra step, and only opens the slot picker when all three are full.
// SAVE: every checkpoint goes to the active slot with the running playtime. A save that
// fails retains in-memory progress for retry and reports the failure in the game.
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
        private Action<string> _previewWebImport;
        private bool _profileImportVisible;
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void AshenedSpire_ChooseOriginalSave(string owner);
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void AshenedSpire_ReadOriginalSlot(string owner, int slot);
#endif
        // The browser bridge returns only after an explicit file/slot selection.
        // Ignore delayed file reads after the player leaves the import screen.
        public void OnOriginalSaveRead(string envelope)
        {
            if (_previewWebImport == null || !_view.WebSaveImportVisible) return;
            try
            {
                var result = JObject.Parse(envelope);
                if (result["error"] != null) { ShowImportError((string)result["error"]); return; }
                _previewWebImport((string)result["save"]);
            }
            catch (Exception) { ShowImportError("The selected save could not be read. Your saves are unchanged."); }
        }
        private void ShowImportError(string message) { if (_profileImportVisible) ShowWebProfileImport(message); else ShowWebImport(message); }
        private void InitSaveSlots(string channel)
        {
            IOriginalSaveStorage storage = new OriginalDelegateSaveStorage(key => PlayerPrefs.GetString(key, ""), (key, value) => PlayerPrefs.SetString(key, value), PlayerPrefs.Save, PlayerPrefs.DeleteKey);
#if UNITY_WEBGL && !UNITY_EDITOR
            storage = new OriginalCompressedSaveStorage(storage);
#endif
            _slotSaves = new OriginalSaveSlots(storage, channel, UnityEngine.Application.version);
            try
            {
                if (!_slotSaves.CompactLegacyRecords(value => OriginalGameSession.Restore(value)))
                    Debug.LogWarning("Historical native records could not be compacted; existing and recovery records are retained.");
                var outcome = _slotSaves.MigrateLegacy();
                if (outcome != OriginalLegacyMigration.AlreadyDone) Debug.Log("ASHENSPIRE_SAVE_MIGRATION " + outcome);
            }
            catch (Exception error) { Debug.LogWarning("The earlier native save was not moved into slot 1: " + error.Message); }
            _view.SlotsRequested += ShowSaveSlots;
            _view.WebImportRequested += ShowWebImport;
            _view.WebProfileImportRequested += () => ShowWebProfileImport(null);
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
                if (!TryLoadOriginalProfile()) return;
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
                Debug.LogWarning("Native save to slot " + (_activeSlot + 1) + " did not verify; in-memory progress is retained for retry.");
                _slotNotice = "The last save to slot " + (_activeSlot + 1) + " could not be verified. Keep this game open, free some storage and keep playing to retry.";
            }
            else _slotNotice = null;
        }
        private string TakeSlotNotice() { var notice = _slotNotice; _slotNotice = null; return notice; }
        private JObject RecordOriginalResult(JObject run, bool victory)
        {
            var receipt = _slotSaves.RecordResult(_profile, run, victory);
            ProfileSaveResult((bool)receipt["saved"]);
            return receipt;
        }
        private void ShowSaveSlots() => ShowSaveSlots(null);
        private void ShowWebImport() => ShowWebImport(null);
        private void ShowWebImport(string notice)
        {
            _profileImportVisible = false;
            if (!TryLoadOriginalProfile()) return;
            _previewWebImport = text =>
            {
                try
                {
                    var target = _slotSaves.List().FirstOrDefault(s => s.State == OriginalSaveSlotState.Empty);
                    if (target == null) throw new InvalidOperationException("All slots are occupied. Free a slot from Saved climbs before importing.");
                    // An imported checkpoint freezes the shipped original catalog;
                    // currently selected desktop mods must not retune it implicitly.
                    var importCatalog = new OriginalContentCatalog(OriginalRules("content").ToString());
                    var snapshot = OriginalWebSaveImport.Convert(text, importCatalog, OriginalRules("event-choices"), OriginalRules("mechanics"), OriginalRules("progression"));
                    var run = snapshot["run"];
                    var summary = ClassName((string)run["classId"]) + " · Act " + run["actNumber"] + " · Floor " + run["floor"] + "\nHP " + run["hp"] + "/" + run["maxHp"] + " · " + run["deck"].Count() + " cards\nDestination: Slot " + (target.Slot + 1);
                    _previewWebImport = null;
                    _view.WebSaveImportPreview(summary, () =>
                    {
                        try
                        {
                            if (!_slotSaves.ImportWebRun(target.Slot, snapshot)) { ShowWebImport("The import could not be saved. Free some storage and try again. Your original file is unchanged."); return; }
                            ShowSaveSlots("Original save imported into slot " + (target.Slot + 1) + ". Choose Continue when ready.");
                        }
                        catch (Exception error) { ShowWebImport("Import refused: " + error.Message); }
                    }, ShowSaveSlots);
                }
                catch (Exception error) { ShowWebImport("Import refused: " + error.Message); }
            };
            Action chooseFile = null; Action<int> browserSlot = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            chooseFile = () => AshenedSpire_ChooseOriginalSave(gameObject.name);
            browserSlot = slot => AshenedSpire_ReadOriginalSlot(gameObject.name, slot);
#endif
            _view.WebSaveImport(notice, _previewWebImport, ShowSaveSlots, chooseFile, browserSlot);
        }
        private void ShowWebProfileImport(string notice)
        {
            _profileImportVisible = true;
            if (!TryLoadOriginalProfile()) return;
            _previewWebImport = text =>
            {
                try
                {
                    var catalog = new OriginalContentCatalog(OriginalRules("content").ToString());
                    var expected = _profile.Snapshot();
                    // Memory may include camera preferences awaiting a save retry.
                    // Guard both baselines separately; do not discard local changes.
                    var expectedStored = _slotSaves.LoadProfile(catalog, out _).Snapshot();
                    var imported = OriginalWebProfileImport.Convert(text, catalog, _profile);
                    var source = imported.Snapshot()["originalProfileImport"]["original"];
                    var progress = imported.Snapshot()["progress"];
                    var summary = source["results"].Count() + " recorded climbs · " + (source["unlocked"]?.Count() ?? 0) + " earned unlocks\nCombined totals: " + progress["runs"] + " climbs · " + progress["wins"] + " victories\nRun slots will not change.";
                    _previewWebImport = null;
                    _view.WebSaveImportPreview(summary, () =>
                    {
                        try
                        {
                            if (!JToken.DeepEquals(_profile.Snapshot(), expected)) throw new InvalidOperationException("Your profile changed. Check the import again.");
                            if (!_slotSaves.ImportWebProfile(catalog, expectedStored, imported)) { ShowWebProfileImport("Profile import could not be saved. Free some storage and retry. Your existing progress is unchanged."); return; }
                            _profile = imported;
                            ShowSaveSlots("Original profile progress imported. Open Chronicle or Collection to see it.");
                        }
                        catch (Exception error) { ShowWebProfileImport("Import refused: " + error.Message); }
                    }, ShowSaveSlots, true);
                }
                catch (Exception error) { ShowWebProfileImport("Import refused: " + error.Message); }
            };
            Action chooseFile = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            chooseFile = () => AshenedSpire_ChooseOriginalSave(gameObject.name);
#endif
            _view.WebSaveImport(notice, _previewWebImport, ShowSaveSlots, chooseFile, null, true);
        }
        private void ShowSaveSlots(string notice)
        {
            _previewWebImport = null;
            _profileImportVisible = false;
            if (!TryLoadOriginalProfile()) return;
            _view.Slots(_slotSaves.List(), ClassName, notice ?? TakeSlotNotice(), ResumeSlot, CreateOriginal, DeleteSlot, CopySlot);
            _view.PersistenceNotice(_profileNotice);
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
