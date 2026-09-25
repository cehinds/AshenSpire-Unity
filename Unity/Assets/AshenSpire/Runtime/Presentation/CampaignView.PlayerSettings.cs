// CampaignView.PlayerSettings.cs — the settings screen sections for OriginalPlayerSettings,
// grouped like the HTML game (Game, Audio, Accessibility, Controls, Content mods).
// HOOKS (CampaignView.cs): Settings() calls ExtendSettings(motion, fast, mute) after building
// its original toggles, and RefreshTouchTargets() calls ScheduleTextScale(). The original
// toggles keep their names (reduced-motion / fast-motion / mute-sound), labels and callbacks
// for the browser playtests; they are only moved under the group headings.
// OWNERSHIP: RunController (RunController.Settings.cs) loads, saves and passes the settings
// object in; this file edits it and raises PlayerSettingsChanged / ContentModsChanged.
// APPLIED HERE: text size (per text element, from its resolved USS size), colorblind palette
// (root class palette-*, Resources/OriginalPalette.uss), map key bindings (MapView.KeyAction)
// and the reduced/quick/mute flags that CombatFeedback already reads.
// VERIFY IN EDITOR: open Settings, move each slider, pick a palette, rebind a map key.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AshenSpire.Domain.Original;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        private static readonly string[] PaletteClasses = { "palette-protanopia", "palette-deuteranopia", "palette-tritanopia" };
        private static readonly List<string> PaletteChoices = new List<string> { "None", "Protanopia", "Deuteranopia", "Tritanopia" };
        private OriginalPlayerSettings _playerSettings;
        private IReadOnlyList<string> _modStatusLines = Array.Empty<string>();
        private VisualElement _modStatusBox;
        private bool _paletteLoaded, _textScaled;
        private int _textScaleAttempts;
        private IVisualElementScheduledItem _textScaleJob;
        private string _capturingAction;
        private sealed class BaseFontSize { public float Value; public bool Skip; }
        private readonly ConditionalWeakTable<TextElement, BaseFontSize> _baseFontSizes = new ConditionalWeakTable<TextElement, BaseFontSize>();

        /// <summary>Raised after the player changes any setting; the settings object is already updated.</summary>
        public event Action PlayerSettingsChanged;
        /// <summary>Raised when "Load content mods" changes; PlayerSettings.LoadContentMods is already updated.</summary>
        public event Action<bool> ContentModsChanged;
        public OriginalPlayerSettings PlayerSettings
        {
            get => _playerSettings;
            set { _playerSettings = value; ApplyPlayerSettings(); }
        }
        /// <summary>Lines shown under Content mods: loaded packs, refused packs and loader errors.</summary>
        public IReadOnlyList<string> ContentModStatus
        {
            get => _modStatusLines;
            set { _modStatusLines = value ?? Array.Empty<string>(); RenderModStatus(); }
        }

        private void ApplyPlayerSettings()
        {
            var settings = _playerSettings;
            if (settings == null) return;
            _reducedMotion = settings.ReducedMotion;
            _fast = settings.QuickAnimations;
            _muted = settings.Muted;
            MapView.KeyAction = code => OriginalKeyBindings.Action(_playerSettings?.KeyBindings, code);
            ApplyPalette();
            ScheduleTextScale();
        }

        private void ApplyPalette()
        {
            var palette = _playerSettings?.ColorblindPalette ?? ColorblindPalette.None;
            if (palette != ColorblindPalette.None && !_paletteLoaded)
            {
                var sheet = Resources.Load<StyleSheet>("OriginalPalette");
                if (sheet != null) _root.styleSheets.Add(sheet);
                _paletteLoaded = true;
            }
            foreach (var name in PaletteClasses) _root.RemoveFromClassList(name);
            if (palette != ColorblindPalette.None) _root.AddToClassList("palette-" + OriginalPlayerSettings.PaletteName(palette));
        }

        // Text size: USS sizes are absolute, so each text element is scaled from its own
        // resolved size. At the default (1.0) nothing is touched until the player changes it.
        private void ScheduleTextScale()
        {
            if (_disposed) return;
            var scale = (float)(_playerSettings?.TextScale ?? 1);
            if (Mathf.Approximately(scale, 1) && !_textScaled) return;
            _textScaleAttempts = 0;
            _textScaleJob?.Pause();
            _textScaleJob = _root.schedule.Execute(ApplyTextScale).StartingIn(30);
        }
        private void ApplyTextScale()
        {
            if (_disposed) return;
            var scale = (float)(_playerSettings?.TextScale ?? 1);
            var pending = false;
            foreach (var text in _root.Query<TextElement>().ToList())
            {
                if (!_baseFontSizes.TryGetValue(text, out var size))
                {
                    // Elements that size their own text in code (the title wordmark) are left alone.
                    if (text.style.fontSize.keyword != StyleKeyword.Null) { _baseFontSizes.Add(text, new BaseFontSize { Skip = true }); continue; }
                    if (float.IsNaN(text.layout.width) || !(text.resolvedStyle.fontSize > 0)) { pending = true; continue; }
                    size = new BaseFontSize { Value = text.resolvedStyle.fontSize };
                    _baseFontSizes.Add(text, size);
                }
                if (size.Skip) continue;
                text.style.fontSize = Mathf.Approximately(scale, 1) ? new StyleLength(StyleKeyword.Null) : new StyleLength(size.Value * scale);
            }
            _textScaled = true;
            if (pending && ++_textScaleAttempts < 10) _textScaleJob = _root.schedule.Execute(ApplyTextScale).StartingIn(60);
        }

        private void ExtendSettings(Toggle motion, Toggle fast, Toggle mute)
        {
            var s = _playerSettings;
            if (s == null) return; // No settings object: keep the original three toggles only.
            _capturingAction = null;
            motion.RemoveFromHierarchy(); fast.RemoveFromHierarchy(); mute.RemoveFromHierarchy();

            // GAME
            _body.Add(Text("GAME", "heading"));
            _body.Add(fast);
            SliderInt speed = null; Toggle instant = null;
            void SyncQuick() { _fast = s.QuickAnimations; fast.SetValueWithoutNotify(_fast); speed?.SetValueWithoutNotify(Percent(s.AnimationSpeed)); instant?.SetValueWithoutNotify(s.InstantAnimations); }
            fast.RegisterValueChangedCallback(e =>
            {
                s.InstantAnimations = false;
                s.AnimationSpeed = e.newValue ? OriginalPlayerSettings.LegacyFastAnimationSpeed : 1;
                SyncQuick(); if (speed != null) speed.label = "Animation speed · " + Percent(s.AnimationSpeed) + "%";
                Changed();
            });
            speed = SettingSlider("animation-speed", "Animation speed", 50, 200, Percent(s.AnimationSpeed), v => { s.AnimationSpeed = v / 100.0; SyncQuick(); });
            instant = SettingToggle("instant-animations", "Instant animations", s.InstantAnimations, v => { s.InstantAnimations = v; SyncQuick(); });
            _body.Add(Text("Combat feedback plays quick at 200% or with Instant on. Other speeds are saved and apply when feedback timing supports them.", "caption"));
            SettingSlider("ui-scale", "Interface size", 75, 150, Percent(s.UiScale), v => s.UiScale = v / 100.0);
            SliderInt intensity = null;
            SettingToggle("screen-shake", "Screen shake", s.ScreenShake, v => { s.ScreenShake = v; intensity?.SetEnabled(v); });
            intensity = SettingSlider("screen-shake-intensity", "Shake intensity", 0, 100, Percent(s.ScreenShakeIntensity), v => s.ScreenShakeIntensity = v / 100.0);
            intensity.SetEnabled(s.ScreenShake);
            SettingToggle("hit-stop", "Hit-stop", s.HitStop, v => s.HitStop = v);
            _body.Add(Text("Screen shake and hit-stop are saved now and used when those combat effects are added.", "caption"));

            // AUDIO
            _body.Add(Text("AUDIO", "heading"));
            _body.Add(mute);
            mute.RegisterValueChangedCallback(e => { s.Muted = e.newValue; Changed(); });
            SettingSlider("volume-master", "Master volume", 0, 100, Percent(s.MasterVolume), v => s.MasterVolume = v / 100.0);
            SettingSlider("volume-sfx", "Sound effects", 0, 100, Percent(s.SfxVolume), v => s.SfxVolume = v / 100.0);
            SettingSlider("volume-music", "Music volume", 0, 100, Percent(s.MusicVolume), v => s.MusicVolume = v / 100.0);
            SettingSlider("volume-ui", "Interface sounds", 0, 100, Percent(s.UiVolume), v => s.UiVolume = v / 100.0);
            _body.Add(Text("Combat sounds follow Master × Sound effects. Music and interface volumes apply when those sounds are added.", "caption"));

            // ACCESSIBILITY
            _body.Add(Text("ACCESSIBILITY", "heading"));
            _body.Add(motion);
            motion.RegisterValueChangedCallback(e => { s.ReducedMotion = e.newValue; Changed(); });
            SettingSlider("text-scale", "Text size", 80, 160, Percent(s.TextScale), v => s.TextScale = v / 100.0);
            var palette = new DropdownField("Colorblind palette", PaletteChoices, (int)s.ColorblindPalette) { name = "colorblind-palette" };
            palette.AddToClassList("setting");
            palette.RegisterValueChangedCallback(e =>
            {
                if (!OriginalPlayerSettings.TryParsePalette(e.newValue, out var chosen)) return;
                s.ColorblindPalette = chosen; ApplyPalette(); Changed();
            });
            _body.Add(palette);

            // CONTROLS
            _body.Add(Text("CONTROLS", "heading"));
            _body.Add(Text("Keyboard keys for the map. Choose an action, then press a key. Esc cancels.", "caption"));
            var keyMessage = Text("", "caption");
            var keyButtons = new Dictionary<string, Button>();
            void ShowKeys()
            {
                foreach (var pair in keyButtons)
                    pair.Value.text = OriginalKeyBindings.Label(pair.Key) + " · " + (pair.Key == _capturingAction ? "press a key…" : s.KeyBindings.TryGetValue(pair.Key, out var key) ? key : "unbound");
            }
            foreach (var action in OriginalKeyBindings.MapActions)
            {
                var id = action;
                var button = AddButton("key-" + id, "", () => { _capturingAction = id; keyMessage.text = "Press a key for " + OriginalKeyBindings.Label(id) + ". Esc cancels."; ShowKeys(); keyButtons[id].Focus(); });
                button.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (_capturingAction != id || e.keyCode == KeyCode.None) return;
                    e.StopImmediatePropagation();
                    _capturingAction = null;
                    if (e.keyCode == KeyCode.Escape) keyMessage.text = "Unchanged.";
                    else if (s.TryBind(id, e.keyCode.ToString(), out var holder))
                    { keyMessage.text = OriginalKeyBindings.Label(id) + " is now " + e.keyCode + "."; Changed(); }
                    else keyMessage.text = e.keyCode + " is already used by " + OriginalKeyBindings.Label(holder) + ". Choose another key, or reset the keys.";
                    ShowKeys();
                });
                keyButtons[id] = button;
            }
            var conflicts = s.KeyConflicts();
            keyMessage.text = conflicts.Count == 0 ? "" : string.Join("\n", conflicts.Select(c => c.Key + " is bound to " + string.Join(" and ", c.Actions.Select(OriginalKeyBindings.Label)) + ". Rebind one of them."));
            if (conflicts.Count > 0) keyMessage.AddToClassList("notice");
            _body.Add(keyMessage);
            AddButton("keys-reset", "Reset keys", () => { _capturingAction = null; s.ResetKeyBindings(); keyMessage.text = "Map keys reset to PageUp, PageDown, Home and End."; ShowKeys(); Changed(); });
            ShowKeys();

            // CONTENT MODS (the HTML game files these under Advanced)
            _body.Add(Text("CONTENT MODS", "heading"));
            var mods = new Toggle("Load content mods") { value = s.LoadContentMods, name = "load-content-mods" };
            mods.AddToClassList("setting");
            mods.RegisterValueChangedCallback(e => { s.LoadContentMods = e.newValue; ContentModsChanged?.Invoke(e.newValue); Report(); });
            _body.Add(mods);
            _body.Add(Text("Off by default. When on, packs in StreamingAssets/Mods are checked and merged into the content of new runs.", "caption"));
            _modStatusBox = new VisualElement { name = "content-mod-status" };
            _body.Add(_modStatusBox);
            RenderModStatus();
        }

        private void RenderModStatus()
        {
            if (_modStatusBox == null || _modStatusBox.panel == null) return;
            _modStatusBox.Clear();
            foreach (var line in _modStatusLines)
                _modStatusBox.Add(Text(line, line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith("Refused", StringComparison.Ordinal) ? "notice" : "caption"));
            Report();
        }

        private void Changed()
        {
            PlayerSettingsChanged?.Invoke();
            Report();
        }
        private static int Percent(double value) => (int)Math.Round(value * 100);
        private Toggle SettingToggle(string id, string label, bool value, Action<bool> changed)
        {
            var toggle = new Toggle(label) { value = value, name = id };
            toggle.AddToClassList("setting");
            toggle.RegisterValueChangedCallback(e => { changed(e.newValue); Changed(); });
            _body.Add(toggle);
            return toggle;
        }
        private SliderInt SettingSlider(string id, string label, int min, int max, int value, Action<int> changed)
        {
            var slider = new SliderInt(label + " · " + value + "%", min, max) { name = id, value = value };
            slider.AddToClassList("setting");
            slider.style.minHeight = Mathf.Max(52, ViewportLayout.MinimumTouchHeight(_displayHeight));
            slider.RegisterValueChangedCallback(e => { slider.label = label + " · " + e.newValue + "%"; changed(e.newValue); Changed(); });
            _body.Add(slider);
            return slider;
        }
    }
}
