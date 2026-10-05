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
                if (result["error"] != null) { ShowWebImport((string)result["error"]); return; }
                _previewWebImport((string)result["save"]);
            }
            catch (Exception) { ShowWebImport("The selected save could not be read. Your saves are unchanged."); }
        }
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
                _activeSlot = slot; _playtimeBase = meta?.PlaytimeSeconds ?? 0; _playtimeSince = Time.realtimeSinceStartup; Unsaved(false);
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
        private void QuickStartOriginal()
        {
            var empty = _slotSaves.List().FirstOrDefault(s => s.State == OriginalSaveSlotState.Empty);
            if (empty == null) { ShowSaveSlots("Every slot holds a climb. Choose a slot before beginning."); return; }
            if (!TryLoadOriginalProfile()) return;
            try
            {
                var data = _originalContent.Data(); var quick = data["characterCreation"]?["quickStart"] as JObject;
                var classId = (string)quick?["classId"] ?? "reaver";
                var mode = (string)quick?["attributeMode"] ?? (string)data["attributeRules"]?["defaultMode"];
                var progression = new AttributeProgression(OriginalRules("progression")); var mechanics = OriginalRules("mechanics");
                var creation = new CreationModel(_originalContent,classId,mode,progression);
                if (!creation.CanBegin) { CreateOriginal(empty.Slot); return; }
                var player = new OriginalCharacterBuilder(_originalContent,progression,mechanics).Build(creation,meta:_profile.Snapshot());
                player["runId"] = Guid.NewGuid().ToString("N"); player["profileMeta"] = _profile.Snapshot(); player["keepsakeId"] = (string)quick?["keepsakeId"] ?? "none";
                var supplemental = OriginalRules("event-choices"); supplemental["mapShapeLimits"] = OriginalRules("custom-run-options")["mapShape"]["limits"].DeepClone();
                var game = OriginalGameSession.Start(_originalContent,supplemental,mechanics,player,unchecked((uint)DateTime.UtcNow.Ticks));
                BeginSlot(empty.Slot); BindOriginal(game); RefreshOriginal();
            }
            catch (Exception error)
            { Debug.LogWarning(error.Message); _view.Title(_content,_saves.HasSave,"Quick start could not begin. Your existing climbs are preserved."); }
        }
        private void ShowOriginalHistory()
        { if (TryLoadOriginalProfile()) { _view.Profile(_profile,true); _view.PersistenceNotice(_profileNotice); } }
        // Called when creation commits: an overwritten slot is cleared so its old run is not kept as the new run's backup.
        private void BeginSlot(int slot)
        {
            if (_slotSaves.List()[slot].State != OriginalSaveSlotState.Empty) _slotSaves.Delete(slot);
            _activeSlot = slot; _playtimeBase = 0; _playtimeSince = Time.realtimeSinceStartup; Unsaved(false);
        }
        // US-13.3: while true, loading a slot would discard the climb in memory, so the view
        // makes Continue hold-to-confirm (action.loadSlot).
        private void Unsaved(bool value) => _view.NativeUnsavedProgress = value;
        private void SaveOriginalSlot()
        {
            if (_originalGame == null || _slotSaves == null || _activeSlot < 0) return;
            var playtime = _playtimeBase + (long)Math.Max(0f, Time.realtimeSinceStartup - _playtimeSince);
            if (!_slotSaves.Save(_activeSlot, _originalGame.Snapshot(), playtime))
            {
                Debug.LogWarning("Native save to slot " + (_activeSlot + 1) + " did not verify; in-memory progress is retained for retry.");
                _slotNotice = "The last save to slot " + (_activeSlot + 1) + " could not be verified. Keep this game open, free some storage and keep playing to retry.";
                Unsaved(true);
            }
            else { _slotNotice = null; Unsaved(false); }
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
            if (!TryLoadOriginalProfile()) return;
            _previewWebImport = text =>
            {
                try
                {
                    if (OriginalWebProfileImport.IsProfile(text)) { PreviewWebProfile(text); return; }
                    var target = _slotSaves.List().FirstOrDefault(s => s.State == OriginalSaveSlotState.Empty);
                    if (target == null) throw new InvalidOperationException("All slots are occupied. Free a slot from Saved climbs before importing.");
                    // An imported checkpoint freezes the shipped original catalog;
                    // currently selected desktop mods must not retune it implicitly.
                    var importCatalog = new OriginalContentCatalog(OriginalRules("content").ToString());
                    var snapshot = OriginalWebSaveImport.Convert(text, importCatalog, OriginalRules("event-choices"), OriginalRules("mechanics"), OriginalRules("progression"));
                    var run = snapshot["run"];
                    var summary = ClassName((string)run["classId"]) + " · Act " + run["actNumber"] + " · Floor " + run["floor"] + "\nHP " + run["hp"] + "/" + run["maxHp"] + " · " + run["deck"].Count() + " cards\n" + OriginalWebRoomImport.Resumes(snapshot) + "\nDestination: Slot " + (target.Slot + 1);
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
        // Original profile (history, unlocks, discoveries, settings) merges into the native
        // profile; settings are applied only when the player asks. Nothing is replaced.
        private void PreviewWebProfile(string text)
        {
            var preview = OriginalWebProfileImport.Merge(text, _originalContent, _profile, _playerSettings);
            _previewWebImport = null;
            Action Commit(bool withSettings) => () =>
            {
                try
                {
                    // Merge again against the live profile so nothing recorded since the preview is lost.
                    var merge = OriginalWebProfileImport.Merge(text, _originalContent, _profile, _playerSettings);
                    if (merge.Changed)
                    {
                        var previous = _profile; _profile = merge.Profile;
                        if (!SaveOriginalProfile()) { _profile = previous; ShowWebImport("The profile import could not be saved. Free some storage and try again. Your original profile is unchanged."); return; }
                    }
                    if (withSettings) { _playerSettings = merge.Settings; _view.PlayerSettings = _playerSettings; SavePlayerSettings(); }
                    ShowSaveSlots("Original profile imported: " + merge.ImportedResults + " run results, " + (merge.AddedUnlocks.Count + merge.EarnedUnlocks.Count) + " unlocks" + (withSettings ? ", " + merge.MappedSettings.Count + " settings." : "."));
                }
                catch (Exception error) { ShowWebImport("Import refused: " + error.Message); }
            };
            var unmapped = preview.UnmappedSettings.Count == 0 ? "" : "Not carried over: " + string.Join("; ", preview.UnmappedSettings.Select(u => u.Split(':')[0]));
            _view.WebProfileImportPreview(preview.Summary(), unmapped, preview.Changed ? Commit(false) : null, preview.MappedSettings.Count > 0 ? Commit(true) : null, ShowSaveSlots);
        }
        private void ShowSaveSlots(string notice)
        {
            _previewWebImport = null;
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
