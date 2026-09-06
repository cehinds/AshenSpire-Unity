// GameAudio.cs — procedural combat feedback; no external recordings or simulation rules.
// ATTACH: RunController adds this component to ExpeditionRoot if absent.
// INSPECTOR: no references required; Configure receives campaign sound tuning.
// LIFECYCLE: Awake creates an owned AudioSource. Clips are cached once and destroyed
// with this component. Play is called after successful commands, never as a rule trigger.
// EDIT: campaign.json/Audio.Volume and Feedback.Cues for sweeps, noise and duration.
// VERIFY: unmute, play an attack/guard, end a turn, claim reward; mute stops all feedback.
// WEB: browser user interaction is required before audio playback is available.
using System;
using System.Collections.Generic;
using AshenSpire.Domain;
using UnityEngine;
namespace AshenSpire.Application
{
    public sealed class GameAudio : MonoBehaviour
    {
        private AudioSource _source;
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
        }
        public void Configure(SoundDefinition tuning, FeedbackDefinition feedback, bool diagnostics)
        {
            _diagnostics = diagnostics;
            _source.Stop();
            foreach (var existing in _clips.Values) Destroy(existing);
            _clips.Clear();
            _source.volume = tuning.Volume;
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
        public void SetMuted(bool muted)
        {
            _source.mute = muted;
            if (muted)
                _source.Stop();
        }
        public void Play(string cue)
        {
            if (isActiveAndEnabled && _source != null && !_source.mute && _clips.TryGetValue(cue, out var clip))
            {
                // Bound overlap during rapid inputs; a new action replaces its predecessor.
                _source.Stop();
                _source.PlayOneShot(clip);
                if (_diagnostics) Debug.Log("ASHENSPIRE_SOUND " + cue);
            }
        }
        private bool _diagnostics;
        private void OnDisable() { if (_source != null) _source.Stop(); }
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
            if (_ownedListener != null)
                Destroy(_ownedListener);
        }
    }
}
