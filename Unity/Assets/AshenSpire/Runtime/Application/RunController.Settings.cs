// RunController.Settings.cs — OriginalPlayerSettings persistence and optional content mods.
// HOOKS (RunController.cs): OnEnable calls InstallPlayerSettings() after the view exists,
// OnDisable calls UninstallPlayerSettings(), and both original catalog builders try
// ModdedCatalog() first. With "Load content mods" off, ModdedCatalog returns null without
// touching the file system, so the shipped catalog is built exactly as before.
// SAVE: AshenSpire.Settings.v1 (OriginalPlayerSettings.ToJson) plus the three legacy ints
// AshenSpire.ReducedMotion / FastMotion / Muted, which older builds and code paths read.
// APPLY: UI size -> PanelSettings.scale (restored on disable), master × SFX -> GameAudio.
// MODS: StreamingAssets/Mods via OriginalModDirectorySource on desktop and in the editor.
// WebGL (and Android, whose StreamingAssets sit inside the APK) need web requests, which are
// not wired; the player sees why in Settings and the shipped content is used.
// CO-OP: always the shipped content (CoopContent), whatever the toggle says, so seats match.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using Newtonsoft.Json;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private OriginalPlayerSettings _playerSettings;
        private float _panelScaleBase = float.NaN;
        private OriginalModLoadResult _modResult;
        private bool _modsAttempted;
        private string _modNotice;

        private void InstallPlayerSettings()
        {
            _playerSettings = OriginalPlayerSettings.LoadOrMigrate(PlayerPrefs.GetString(OriginalPlayerSettings.StorageKey, ""), (key, fallback) => PlayerPrefs.GetInt(key, fallback), out var notes);
            // The legacy flags are still written by older builds on the same device, so they
            // win for the three settings they cover. The view then starts exactly as before.
            _playerSettings.ReducedMotion = PlayerPrefs.GetInt(OriginalPlayerSettings.LegacyReducedMotionKey, 0) == 1;
            _playerSettings.Muted = PlayerPrefs.GetInt(OriginalPlayerSettings.LegacyMutedKey, 0) == 1;
            var quick = PlayerPrefs.GetInt(OriginalPlayerSettings.LegacyFastMotionKey, 0) == 1;
            if (quick != _playerSettings.QuickAnimations)
            {
                _playerSettings.InstantAnimations = false;
                _playerSettings.AnimationSpeed = quick ? OriginalPlayerSettings.LegacyFastAnimationSpeed : 1;
            }
            if (_diagnosticsEnabled) foreach (var note in notes) Debug.Log("ASHENSPIRE_SETTINGS " + note);
            if (_panelSettings != null) _panelScaleBase = _panelSettings.scale;
            _view.PlayerSettings = _playerSettings;
            _view.ContentModStatus = ModStatus();
            _view.PlayerSettingsChanged += SavePlayerSettings;
            _view.ContentModsChanged += ToggleContentMods;
            ApplyPlayerSettings();
        }
        private void UninstallPlayerSettings()
        {
            if (_view != null)
            {
                _view.PlayerSettingsChanged -= SavePlayerSettings;
                _view.ContentModsChanged -= ToggleContentMods;
            }
            // PanelSettings is a shared asset; never leave the player's size in it.
            if (_panelSettings != null && !float.IsNaN(_panelScaleBase)) _panelSettings.scale = _panelScaleBase;
        }
        private void ApplyPlayerSettings()
        {
            if (_playerSettings == null) return;
            if (_panelSettings != null && !float.IsNaN(_panelScaleBase))
            {
                var scale = _panelScaleBase * (float)_playerSettings.UiScale;
                if (_panelSettings.scale != scale) _panelSettings.scale = scale;
            }
            _audio?.SetVolumeScale((float)(_playerSettings.MasterVolume * _playerSettings.SfxVolume));
        }
        private void SavePlayerSettings()
        {
            if (_playerSettings == null) return;
            PlayerPrefs.SetString(OriginalPlayerSettings.StorageKey, _playerSettings.ToJson().ToString(Formatting.None));
            PlayerPrefs.SetInt(OriginalPlayerSettings.LegacyReducedMotionKey, _playerSettings.ReducedMotion ? 1 : 0);
            PlayerPrefs.SetInt(OriginalPlayerSettings.LegacyFastMotionKey, _playerSettings.QuickAnimations ? 1 : 0);
            PlayerPrefs.SetInt(OriginalPlayerSettings.LegacyMutedKey, _playerSettings.Muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyPlayerSettings();
        }

        private void ToggleContentMods(bool enabled)
        {
            SavePlayerSettings();
            // Settings is reached from the title screen only, and every route back into play
            // (new run, continue, profile, co-op) reloads the catalog and profile. A run in
            // progress keeps the content stored in its own snapshot.
            _originalContent = null; _profile = null;
            _modResult = null; _modsAttempted = false; _modNotice = null;
            if (enabled) ReadContentMods();
            _view.ContentModStatus = ModStatus();
        }
        /// <summary>The modded catalog when "Load content mods" is on and a pack source is readable; otherwise null.</summary>
        private OriginalContentCatalog ModdedCatalog()
        {
            if (_playerSettings == null || !_playerSettings.ContentModsActive(coop: false)) return null;
            if (!_modsAttempted)
            {
                ReadContentMods();
                if (_view != null) _view.ContentModStatus = ModStatus();
            }
            return _modResult?.Catalog;
        }
        private OriginalContentCatalog _coopContent;
        /// <summary>Co-op always uses the shipped content, whatever LoadContentMods says (ContentModsActive(coop: true) is false).</summary>
        private OriginalContentCatalog CoopContent()
        {
            // Mods off: the solo catalog is already the shipped one, exactly as before this change.
            if (_originalContent != null && (_playerSettings == null || !_playerSettings.LoadContentMods)) return _originalContent;
            return _coopContent ?? (_coopContent = new OriginalContentCatalog(OriginalRules("content").ToString()));
        }
        private void ReadContentMods()
        {
            _modsAttempted = true; _modResult = null; _modNotice = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            _modNotice = "Content mods are not available in the browser build: StreamingAssets can only be read there with web requests. The shipped content is used.";
#else
            var root = UnityEngine.Application.streamingAssetsPath;
            if (string.IsNullOrEmpty(root) || root.Contains("://"))
            {
                _modNotice = "Content mods are not available on this platform yet: StreamingAssets is inside the app package and needs web requests to read. The shipped content is used.";
                return;
            }
            try
            {
                var content = Resources.Load<TextAsset>("Original/content");
                if (content == null) throw new InvalidOperationException("Import native game content using the AshenSpire menu.");
                _modResult = OriginalModPacks.Load(content.text, new OriginalModDirectorySource(root));
                if (_modResult.Catalog == null) _modNotice = "The shipped content failed its own check, so no packs were applied.";
                if (_diagnosticsEnabled)
                    Debug.Log("ASHENSPIRE_MODS " + new Newtonsoft.Json.Linq.JObject
                    {
                        ["loaded"] = new Newtonsoft.Json.Linq.JArray(_modResult.Loaded.Select(m => m.Id)),
                        ["rejected"] = new Newtonsoft.Json.Linq.JArray(_modResult.Rejected),
                        ["errors"] = new Newtonsoft.Json.Linq.JArray(_modResult.Errors.Select(e => e.ToString())),
                    }.ToString(Formatting.None));
                foreach (var error in _modResult.Errors) Debug.LogWarning("Content mod: " + error);
            }
            catch (Exception error)
            {
                _modResult = null;
                _modNotice = "Content mods could not be read: " + error.Message + " The shipped content is used.";
                Debug.LogWarning(_modNotice);
            }
#endif
        }
        private IReadOnlyList<string> ModStatus()
        {
            var lines = new List<string>();
            if (_playerSettings == null || !_playerSettings.LoadContentMods)
            {
                lines.Add("Off. The shipped content is used and StreamingAssets/Mods is not read.");
                return lines;
            }
            lines.Add("Mods are disabled in co-op. Shared climbs always use the shipped content.");
            if (!_modsAttempted)
            {
                lines.Add("On. Packs are read when a new solo run or the profile next loads content.");
                return lines;
            }
            if (_modNotice != null) lines.Add(_modNotice);
            if (_modResult == null) return lines;
            if (_modResult.Loaded.Count == 0 && _modResult.Rejected.Count == 0 && _modResult.Errors.Count == 0)
                lines.Add("No packs found in StreamingAssets/Mods. The shipped content is used.");
            foreach (var pack in _modResult.Loaded)
                lines.Add("Loaded: " + pack.Name + " " + pack.Version + " (" + pack.Id + ")");
            foreach (var id in _modResult.Rejected)
                lines.Add("Refused: " + id);
            const int shown = 12;
            foreach (var error in _modResult.Errors.Take(shown))
                lines.Add(error.ToString());
            if (_modResult.Errors.Count > shown)
                lines.Add("[more] " + (_modResult.Errors.Count - shown) + " more errors are in the player log.");
            if (_modResult.Loaded.Count > 0)
                lines.Add("Packs apply to new solo runs. A run in progress keeps the content it started with.");
            return lines;
        }
    }
}
