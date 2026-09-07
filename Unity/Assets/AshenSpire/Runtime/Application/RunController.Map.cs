// RunController.Map.cs — local map-camera preferences, part of RunController.
// ATTACH: no additional MonoBehaviour. CampaignView binds these callbacks once.
// MODIFY: display preferences here; visibility/route rules remain in the domain.
// SAVE: two bounded profile-settings slots (solo/co-op), no credentials or run
// mutation. Update memory immediately; debounce journal writes and flush on Save.
using System;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private JObject _mapViewerSettings;
        private bool _mapViewerDirty;
        private JObject ReadMapView(string scope)
        {
            if (scope != "solo" && scope != "coop") throw new ArgumentException("Unknown map viewer scope");
            LoadOriginalProfile();
            if (_mapViewerSettings == null) _mapViewerSettings = _profile.Snapshot()["settings"]?["mapViewer"] is JObject saved ? (JObject)saved.DeepClone() : new JObject();
            return _mapViewerSettings[scope] is JObject state ? (JObject)state.DeepClone() : new JObject();
        }
        private void WriteMapView(string scope, JObject value)
        {
            ReadMapView(scope);
            _mapViewerSettings[scope] = value.DeepClone(); _mapViewerDirty = true;
            CancelInvoke(nameof(FlushMapView)); Invoke(nameof(FlushMapView), .25f);
        }
        private void FlushMapView()
        {
            CancelInvoke(nameof(FlushMapView));
            if (!_mapViewerDirty || _profile == null || _profileSaves == null) return;
            try
            {
                _profile.SetSettings(new JObject { ["mapViewer"] = _mapViewerSettings.DeepClone() });
                _profileSaves.Save(_profile.Snapshot()); _mapViewerDirty = false;
            }
            catch (Exception error)
            {
                // Display preferences must not prevent the run checkpoint or shutdown.
                // Keep the dirty snapshot so the next normal save can retry.
                UnityEngine.Debug.LogWarning("Map preferences were not saved: " + error.Message);
            }
        }
    }
}
