// CampaignView.Audio.cs — interface feedback for actual control activation.
// Bind during the existing control refresh, including release builds and co-op panels.
// Button.clicked supports pointer, touch and submit navigation; it also survives a
// command rebuilding the screen. Raw pointer events would sound on cancelled drags.
// Weak keys do not retain discarded screens; each live control is bound only once.
using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace AshenSpire.Presentation
{
    public sealed partial class CampaignView
    {
        public event Action InterfaceSoundRequested, SoundPreviewRequested;
        private readonly ConditionalWeakTable<VisualElement, object> _audioControls = new ConditionalWeakTable<VisualElement, object>();

        private void InterfaceActivated()
        {
            if (!_disposed) InterfaceSoundRequested?.Invoke();
        }
        private void BindInterfaceSounds()
        {
            foreach (var button in _root.Query<Button>().ToList())
            {
                if (button.ClassListContains("no-interface-sound") || _audioControls.TryGetValue(button, out _)) continue;
                _audioControls.Add(button, new object());
                button.clicked += InterfaceActivated;
            }
            foreach (var toggle in _root.Query<Toggle>().ToList())
            {
                if (_audioControls.TryGetValue(toggle, out _)) continue;
                _audioControls.Add(toggle, new object());
                toggle.RegisterValueChangedCallback(_ => InterfaceActivated());
            }
            foreach (var dropdown in _root.Query<DropdownField>().ToList())
            {
                if (_audioControls.TryGetValue(dropdown, out _)) continue;
                _audioControls.Add(dropdown, new object());
                dropdown.RegisterValueChangedCallback(_ => InterfaceActivated());
            }
        }
    }
}
