// GameAudio.cs — cached procedural combat and interface feedback, on independent sources.
// ATTACH: RunController adds this component to ExpeditionRoot if absent.
// INSPECTOR: no references required; Configure receives campaign sound tuning.
// LIFECYCLE: Awake creates an owned AudioSource. Clips are cached once and destroyed
// with this component. Play is called after successful commands, never as a rule trigger.
// EDIT: campaign.json/Audio.Volume and Feedback.Cues for sweeps, noise and duration.
// VERIFY: unmute, play an attack/guard, end a turn, claim reward; mute stops all feedback.
// WEB: browser user interaction is required before audio playback is available.
// PAUSE: SetSuspended stops transient cues without changing the player's mute choice.
// VOLUME: master × SFX and master × UI are independent; mute/pause stop both sources.
using System;
using System.Collections.Generic;
using AshenSpire.Domain;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed class GameAudio : MonoBehaviour
    {
        private AudioSource _source;
        private AudioSource _interfaceSource;
        private AudioClip _interfaceClip;
        private bool _suspended;
        public bool IsPlaying => (_source != null && _source.isPlaying) || (_interfaceSource != null && _interfaceSource.isPlaying);
        public bool IsMuted => _source != null && _source.mute;
        private AudioListener _ownedListener;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private void Awake()
        {
            // The UI-only scene has a camera but no listener. Preserve an owner-provided
            // listener when one exists; otherwise this feedback component owns exactly one.
            if (FindAnyObjectByType<AudioListener>() == null)
                _ownedListener = gameObject.AddComponent<AudioListener>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0;
            _interfaceSource = gameObject.AddComponent<AudioSource>();
            _interfaceSource.playOnAwake = false;
            _interfaceSource.spatialBlend = 0;
            var samples = FeedbackSound.InterfaceClick();
            _interfaceClip = AudioClip.Create("AshenSpire_interface", samples.Length, 1, 22050, false);
            _interfaceClip.SetData(samples, 0);
        }
        public void Configure(SoundDefinition tuning, FeedbackDefinition feedback, bool diagnostics)
        {
            _diagnostics = diagnostics;
            _source.Stop();
            _interfaceSource.Stop();
            foreach (var existing in _clips.Values) Destroy(existing);
            _clips.Clear();
            _baseVolume = tuning.Volume;
            _source.volume = _baseVolume * _volumeScale;
            _interfaceSource.volume = _baseVolume * _interfaceVolumeScale;
            if (feedback != null)
            {
                foreach (var cue in feedback.Cues)
                {
                    var samples = FeedbackSound.Synthesize(cue);
                    var clip = AudioClip.Create("AshenSpire_" + cue.Id, samples.Length, 1, 22050, false);
                    clip.SetData(samples, 0);
                    _clips.Add(cue.Id, clip);
                }
                return;
            }
            Add("attack", tuning.AttackFrequency, tuning.Duration);
            Add("guard", tuning.GuardFrequency, tuning.Duration);
            Add("hit", tuning.HitFrequency, tuning.Duration);
            Add("reward", tuning.RewardFrequency, tuning.Duration * 2);
        }
        private float _baseVolume = 1, _volumeScale = 1, _interfaceVolumeScale = 1;
        /// <summary>Player bus gain (OriginalPlayerSettings master × SFX) on top of campaign tuning; 1 leaves tuning unchanged.</summary>
        public void SetVolumeScale(float scale)
        {
            _volumeScale = Mathf.Clamp01(scale);
            if (_source != null) _source.volume = _baseVolume * _volumeScale;
        }
        public void SetMuted(bool muted)
        {
            _source.mute = muted;
            _interfaceSource.mute = muted;
            if (muted)
            {
                _source.Stop();
                _interfaceSource.Stop();
            }
        }
        public void SetInterfaceVolumeScale(float scale)
        {
            _interfaceVolumeScale = Mathf.Clamp01(scale);
            if (_interfaceSource != null) _interfaceSource.volume = _baseVolume * _interfaceVolumeScale;
        }
        public void PlayInterface()
        {
            if (_suspended || !isActiveAndEnabled || _interfaceSource == null || _interfaceSource.mute || _interfaceSource.volume <= 0) return;
            // Bound rapid taps without cutting off the separate combat effect source.
            _interfaceSource.Stop();
            _interfaceSource.PlayOneShot(_interfaceClip);
            if (_diagnostics) Debug.Log("ASHENSPIRE_UI_SOUND " + _interfaceSource.volume.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        public void Play(string cue)
        {
            if (!_suspended && isActiveAndEnabled && _source != null && !_source.mute && _source.volume > 0 && _clips.TryGetValue(cue, out var clip))
            {
                // Bound overlap during rapid inputs; a new action replaces its predecessor.
                _source.Stop();
                _source.PlayOneShot(clip);
                if (_diagnostics) Debug.Log("ASHENSPIRE_SOUND " + cue);
            }
        }
        public void SetSuspended(bool suspended)
        {
            _suspended = suspended;
            if (suspended && _source != null) _source.Stop();
            if (suspended && _interfaceSource != null) _interfaceSource.Stop();
        }
        private bool _diagnostics;
        private void OnDisable() { if (_source != null) _source.Stop(); if (_interfaceSource != null) _interfaceSource.Stop(); }
        private void Add(string id, float frequency, float seconds)
        {
            if (_clips.ContainsKey(id))
                return;
            const int rate = 22050;
            var samples = new float[(int)(rate * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / rate;
                var envelope = Math.Min(1, t * 100) * Math.Exp(-7 * t / seconds);
                samples[i] = (float)((Math.Sin(2 * Math.PI * frequency * t) + 0.25 * Math.Sin(2 * Math.PI * frequency * 2 * t)) * envelope * 0.35);
            }
            var clip = AudioClip.Create("AshenSpire_" + id, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            _clips.Add(id, clip);
        }
        private void OnDestroy()
        {
            foreach (var clip in _clips.Values)
                Destroy(clip);
            if (_source != null)
                Destroy(_source);
            if (_interfaceSource != null) Destroy(_interfaceSource);
            if (_interfaceClip != null) Destroy(_interfaceClip);
            if (_ownedListener != null)
                Destroy(_ownedListener);
        }
    }
}
