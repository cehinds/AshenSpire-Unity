// Profile failures must not erase records, strand a title action or disconnect
// co-op. One OriginalSaveSlots journal owns load, backup recovery and every write.
using System;
using AshenSpire.Domain.Original;
using UnityEngine;

namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private const string ProfileWriteFailure = "Your progress could not be saved. Keep this game open, free some storage, and try again. Saving will retry as you play.";
        private string _profileNotice;
        private bool _profileWritePending;

        private bool TryLoadOriginalProfile()
        {
            try
            {
                if (_originalContent == null) _originalContent = ModdedCatalog() ?? new OriginalContentCatalog(OriginalRules("content").ToString());
                if (_profile != null) return true;
                _profile = _slotSaves.LoadProfile(_originalContent, out var recovered);
                if (recovered) _profileNotice = "Recovered your previous profile backup. More recent progress may be missing; the original record is preserved for recovery.";
                return true;
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                const string message = "Your profile could not be opened. Its saved records have been preserved. Try a compatible build or restore a working backup before continuing.";
                Debug.LogWarning("Native profile load failed; existing records are preserved.");
                _view.Title(_content, false, message);
                return false;
            }
        }

        private bool SaveOriginalProfile()
        {
            if (_profile == null) return true;
            var saved = _slotSaves.SaveProfile(_profile);
            ProfileSaveResult(saved);
            return saved;
        }

        private void ProfileSaveResult(bool saved)
        {
            if (!saved) _profileNotice = ProfileWriteFailure;
            else if (_profileWritePending) _profileNotice = null;
            _profileWritePending = !saved;
            _view?.PersistenceNotice(_profileNotice);
            if (!saved) Debug.LogWarning("Native profile write did not verify; in-memory progress is retained for retry.");
        }
    }
}
