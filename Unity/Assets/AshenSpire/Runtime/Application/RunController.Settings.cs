// RunController.Settings.cs — OriginalPlayerSettings persistence and optional content mods.
// HOOKS (RunController.cs): OnEnable calls InstallPlayerSettings() after the view exists,
// OnDisable calls UninstallPlayerSettings(), and both original catalog builders try
// ModdedCatalog() first. With "Load content mods" off, ModdedCatalog returns null without
// touching the file system, so the shipped catalog is built exactly as before.
// SAVE: AshenSpire.Settings.v1 (OriginalPlayerSettings.ToJson) plus the three legacy ints
// AshenSpire.ReducedMotion / FastMotion / Muted, which older builds and code paths read.
// APPLY: ApplyPanelScale runs the JS layout decider (ViewportLayout.Resolve, CSS px) with the UI size as
// its zoom cap (JS: a named size is a ceiling), picks the wide or narrow PanelSettings, and sets that
// panel's scale so 1 panel px shows as zoom x Interface-size-slider CSS px. Re-fitted on screen
// changes; both panels' scales are restored on disable. SFX/UI/music buses from
// AudioBusLevels.From (Domain: master multiplied, mute -> 0, music off -> 0).
// MODS: StreamingAssets/Mods via OriginalModDirectorySource on desktop and in the editor.
// WebGL/Android use a build-generated resource containing those same validated pack files.
// CO-OP: always the shipped content (CoopContent), whatever the toggle says, so seats match.
using System;
using System.Collections.Generic;
using System.Linq;
using AshenSpire.Domain.Original;
using AshenSpire.Presentation;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
namespace AshenSpire.Application
{
    public sealed partial class RunController
    {
        private OriginalPlayerSettings _playerSettings;
        private float _wideScaleBase = float.NaN, _narrowScaleBase = float.NaN;
        private bool _ownsNarrowPanel;
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
            _view.PlayerSettings = _playerSettings;
            _view.ContentModStatus = ModStatus();
            _view.PlayerSettingsChanged += SavePlayerSettings;
            _view.ContentModsChanged += ToggleContentMods;
            _view.InterfaceSoundRequested += PlayInterfaceSound;
            _view.SoundPreviewRequested += PreviewSound;
            ApplyPlayerSettings();
        }
        private void UninstallPlayerSettings()
        {
            if (_view != null)
            {
                _view.PlayerSettingsChanged -= SavePlayerSettings;
                _view.ContentModsChanged -= ToggleContentMods;
                _view.InterfaceSoundRequested -= PlayInterfaceSound;
                _view.SoundPreviewRequested -= PreviewSound;
            }
            RestorePanels();
        }
        private void ApplyPlayerSettings()
        {
            if (_playerSettings == null) return;
            ApplyPanelScale();
            var levels = AudioBusLevels.From(_playerSettings);
            _audio?.SetVolumeScale((float)levels.Sfx);
            _audio?.SetInterfaceVolumeScale((float)levels.Ui);
            _audio?.SetMuted(_playerSettings.Muted);
            // Mute goes first so the director fades the track out from its current level. While muted the
            // zero bus level is not pushed, so unmuting resumes at the level the player last set.
            // The music bus is already master × music; the director's own master stays at 100.
            _music?.SetMuted(_playerSettings.Muted);
            if (!_playerSettings.Muted) _music?.ApplySettings(100, levels.MusicPercent, levels.MusicOn);
        }
        /// <summary>The narrow panel (a runtime copy of the wide one when the scene has none) and the scales to restore.</summary>
        private void PreparePanels()
        {
            if (_panelSettings == null) return;
            if (_narrowPanelSettings == null)
            {
                _narrowPanelSettings = Instantiate(_panelSettings);
                _narrowPanelSettings.name = "ExpeditionPanelNarrow (runtime)";
                _narrowPanelSettings.referenceResolution = new Vector2Int((int)JsUiTokens.UiScale.NarrowWidth, (int)JsUiTokens.UiScale.NarrowHeight);
                _ownsNarrowPanel = true;
            }
            _wideScaleBase = _panelSettings.scale; _narrowScaleBase = _narrowPanelSettings.scale;
        }
        /// <summary>PanelSettings are shared assets: never leave the player's size or a swapped panel behind.</summary>
        private void RestorePanels()
        {
            if (_panelSettings != null && !float.IsNaN(_wideScaleBase)) _panelSettings.scale = _wideScaleBase;
            if (_narrowPanelSettings != null && !float.IsNaN(_narrowScaleBase)) _narrowPanelSettings.scale = _narrowScaleBase;
            if (_ownsNarrowPanel && _narrowPanelSettings != null)
            {
                var document = GetComponent<UIDocument>();
                if (document != null && document.panelSettings == _narrowPanelSettings) document.panelSettings = _panelSettings;
                Destroy(_narrowPanelSettings); _narrowPanelSettings = null; _ownsNarrowPanel = false;
            }
        }
        /// <summary>
        /// The JS layout decision (src/main.js applyUiScale), in CSS px: ViewportLayout.Resolve picks wide or
        /// narrow and the zoom, with the UI size as the zoom's cap (Auto uncapped). The matching PanelSettings
        /// (Scale With Screen Size at the JS reference size) gets scale = zoom × slider / its own fit, so one
        /// panel px shows as zoom × slider CSS px — 1 USS px = 1 JS CSS px at the reference sizes. Safe-area
        /// insets convert to panel px with the same factor. Called on load, on settings change and when the
        /// screen, canvas or safe area changes.
        /// </summary>
        private void ApplyPanelScale()
        {
            if (_panelSettings == null || float.IsNaN(_wideScaleBase)) return;
            var devicePixels = DisplayViewport.DevicePixelRatio; // browser devicePixelRatio; dpi/160 on phones; 1 on desktop
            var cssHeight = Math.Max(1, Screen.height) / devicePixels;
            var cssWidth = Math.Max(1, Screen.width) / devicePixels;
            var size = _playerSettings?.UiSize ?? "Auto";
            var cap = string.Equals(size, "Auto", StringComparison.OrdinalIgnoreCase) ? double.PositiveInfinity : OriginalDisplayOptions.UiSizeFactor(size);
            var layout = ViewportLayout.Resolve(cssWidth, cssHeight, cap);
            // The numeric Interface size slider (Unity-only) multiplies the JS zoom; Auto adds no factor.
            var slider = _playerSettings == null ? 1 : OriginalDisplayOptions.PanelScale(_playerSettings.UiScale, "Auto", 0, 0);
            var panel = layout.Narrow && _narrowPanelSettings != null ? _narrowPanelSettings : _panelSettings;
            var document = GetComponent<UIDocument>();
            if (document != null && document.panelSettings != panel) document.panelSettings = panel; // AshenedSpire's wide/narrow swap
            var fit = ViewportLayout.PanelFit(cssWidth, cssHeight, panel.referenceResolution.x, panel.referenceResolution.y, (int)panel.screenMatchMode, panel.match);
            var displayScale = layout.Zoom * slider;
            var scale = (float)(displayScale / Math.Max(1e-6, fit));
            if (panel.scale != scale) panel.scale = scale;
            _view?.SetLayout(layout, displayScale);
            // Safe area: screen pixels -> panel px (physical px per panel px = displayScale × devicePixels).
            var perPanel = (float)Math.Max(1e-6, displayScale * devicePixels);
            var safe = Screen.safeArea;
            _view?.SetSafeArea((Screen.height - safe.yMax) / perPanel, (Screen.width - safe.xMax) / perPanel, safe.yMin / perPanel, safe.xMin / perPanel);
            if (_diagnosticsEnabled && (_lastLayout == null || !_lastLayout.SameAs(layout)))
                Debug.Log("ASHENSPIRE_LAYOUT " + new Newtonsoft.Json.Linq.JObject { ["zoom"] = layout.Zoom, ["narrow"] = layout.Narrow, ["compact"] = layout.Compact, ["short"] = layout.Short, ["formFactor"] = layout.FormFactor, ["cssWidth"] = Math.Round(cssWidth, 1), ["cssHeight"] = Math.Round(cssHeight, 1), ["slider"] = slider }.ToString(Formatting.None));
            _lastLayout = layout;
        }
        private UiLayout _lastLayout;
        private void PlayInterfaceSound() => _audio?.PlayInterface();
        private void PreviewSound() => _audio?.Play("guard");
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
            try
            {
                var content = Resources.Load<TextAsset>("Original/content");
                if (content == null) throw new InvalidOperationException("Import native game content using the AshenSpire menu.");
#if (UNITY_WEBGL || UNITY_ANDROID) && !UNITY_EDITOR
                var bundled = Resources.Load<TextAsset>("Original/mod-packs");
                var files = new OriginalModMemorySource();
                if (bundled != null) foreach (var row in Newtonsoft.Json.Linq.JObject.Parse(bundled.text).Properties()) files.Add(row.Name, (string)row.Value);
                _modResult = OriginalModPacks.Load(content.text, files);
                _modNotice = "This player uses packs included when it was built. Changes to the Mods folder require a new build.";
#else
                _modResult = OriginalModPacks.Load(content.text, new OriginalModDirectorySource(UnityEngine.Application.streamingAssetsPath));
#endif
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
