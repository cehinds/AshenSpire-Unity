// MusicPlayback.cs — pure-C# half of the music adapter (F08): turns MusicDirector
// commands into per-deck (A/B AudioSource) start/stop actions and volume curves,
// and maps run phases to MusicScene. Application/MusicPlayer.cs only executes
// the actions and copies VolumeAt() into AudioSource.volume each frame.
// No UnityEngine, no run RNG.
using System;
using System.Collections.Generic;

namespace AshenSpire.Domain
{
    public static class MusicDeckActionKind
    {
        public const string Start = "start"; // create/assign the clip for TrackId on Deck and play it
        public const string Stop = "stop";   // stop Deck and release its clip
    }

    public sealed class MusicDeckAction
    {
        public string Kind;
        public int Deck;
        public string TrackId;
        public string ResourcePath;
        public bool Loop;
        public override string ToString() => Kind + " deck " + Deck + " " + (TrackId ?? "-");
    }

    /// <summary>Two decks for crossfades. Every fade starts from the deck's actual level at the
    /// command time, so a command that interrupts a running fade never jumps.</summary>
    public sealed class MusicDecks
    {
        private sealed class Deck
        {
            public string TrackId;
            public float From, To, Floor;
            public double At, Duration;
            public string Curve;
            public bool FadingOut;
        }

        private readonly Deck[] _decks = { new Deck(), new Deck() };

        /// <summary>Deck carrying the current track, or -1 when music is stopped.</summary>
        public int ActiveDeck { get; private set; } = -1;
        public string TrackOn(int deck) => _decks[deck].TrackId;
        public bool IsFadingOut(int deck) => _decks[deck].FadingOut;

        public float VolumeAt(int deck, double now)
        {
            var d = _decks[deck];
            if (d.TrackId == null) return 0;
            return MusicCurve.Gain(d.Curve, d.From, d.To, now - d.At, d.Duration, d.Floor);
        }

        public IReadOnlyList<MusicDeckAction> Apply(MusicCommand c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var actions = new List<MusicDeckAction>();
            switch (c.Kind)
            {
                case MusicCommandKind.Play:
                case MusicCommandKind.Crossfade:
                {
                    if (ActiveDeck >= 0) FadeOut(ActiveDeck, c.AtSeconds, c.FadeOutSeconds > 0 ? c.FadeOutSeconds : 0, c, actions);
                    var target = ActiveDeck >= 0 ? 1 - ActiveDeck : _decks[0].TrackId == null ? 0 : _decks[1].TrackId == null ? 1 : 0;
                    if (_decks[target].TrackId != null) { actions.Add(new MusicDeckAction { Kind = MusicDeckActionKind.Stop, Deck = target, TrackId = _decks[target].TrackId }); Clear(target); }
                    var d = _decks[target];
                    d.TrackId = c.TrackId; d.From = 0; d.To = c.TargetVolume; d.At = c.AtSeconds; d.Duration = c.FadeInSeconds;
                    d.Curve = c.Curve ?? MusicCurve.Exponential; d.Floor = c.FloorGain > 0 ? c.FloorGain : 0.0001f; d.FadingOut = false;
                    ActiveDeck = target;
                    actions.Add(new MusicDeckAction { Kind = MusicDeckActionKind.Start, Deck = target, TrackId = c.TrackId, ResourcePath = c.ResourcePath, Loop = c.Loop });
                    break;
                }
                case MusicCommandKind.Stop:
                    if (ActiveDeck >= 0) FadeOut(ActiveDeck, c.AtSeconds, c.FadeOutSeconds, c, actions);
                    ActiveDeck = -1;
                    break;
                case MusicCommandKind.SetVolume:
                    if (ActiveDeck >= 0)
                    {
                        var d = _decks[ActiveDeck];
                        d.From = VolumeAt(ActiveDeck, c.AtSeconds); d.To = c.TargetVolume; d.At = c.AtSeconds; d.Duration = c.FadeInSeconds;
                        d.Curve = c.Curve ?? MusicCurve.Linear;
                    }
                    break;
                default:
                    throw new ArgumentException("Unknown music command: " + c.Kind);
            }
            return actions;
        }

        /// <summary>Stops decks whose fade-out has finished. Call every frame.</summary>
        public IReadOnlyList<MusicDeckAction> Tick(double now)
        {
            var actions = new List<MusicDeckAction>();
            for (var i = 0; i < 2; i++)
            {
                var d = _decks[i];
                if (d.TrackId != null && d.FadingOut && now - d.At >= d.Duration - 1e-9) // same tolerance as MusicCurve.Gain
                {
                    actions.Add(new MusicDeckAction { Kind = MusicDeckActionKind.Stop, Deck = i, TrackId = d.TrackId });
                    Clear(i);
                }
            }
            return actions;
        }

        /// <summary>A deck's clip only became ready now (WebGL pre-render): restart its fade-in from here.</summary>
        public void Reanchor(int deck, double now)
        {
            var d = _decks[deck];
            if (d.TrackId != null && !d.FadingOut) d.At = now;
        }

        /// <summary>A non-looping clip finished on its own; the adapter reports TrackEnded to the director next.</summary>
        public void Ended(int deck)
        {
            if (ActiveDeck == deck) ActiveDeck = -1;
            Clear(deck);
        }

        private void FadeOut(int deck, double at, double seconds, MusicCommand c, List<MusicDeckAction> actions)
        {
            var d = _decks[deck];
            if (d.TrackId == null) return;
            if (seconds <= 0) { actions.Add(new MusicDeckAction { Kind = MusicDeckActionKind.Stop, Deck = deck, TrackId = d.TrackId }); Clear(deck); return; }
            d.From = VolumeAt(deck, at); d.To = 0; d.At = at; d.Duration = seconds;
            d.Curve = c.Curve ?? MusicCurve.Exponential; d.Floor = c.FloorGain > 0 ? c.FloorGain : 0.0001f; d.FadingOut = true;
        }

        private void Clear(int deck) => _decks[deck] = new Deck();
    }

    /// <summary>Run phase → MusicScene, following the HTML call sites listed in MusicDirector.cs.</summary>
    public static class MusicSceneMap
    {
        /// <summary>Native run (OriginalRunPhase name + the room's encounter pool).</summary>
        public static MusicScene ForNativePhase(string phase, string pool)
        {
            switch (phase)
            {
                case "Map": return MusicScene.Map;
                case "Combat": return MusicDirector.SceneForEncounter(pool);
                case "Rewards": return MusicScene.Rewards;
                case "Shop": return MusicScene.Shop;
                case "Shrine": return MusicScene.Shrine;
                case "Victory": return MusicScene.Victory; // set only after the act-3 boss outside Endless
                case "Defeat": return MusicScene.Death;
                default: return MusicScene.Event;           // Event, EventResult, Draft: hold the current bed
            }
        }

        /// <summary>Foundation campaign (RunPhase name).</summary>
        public static MusicScene ForCampaignPhase(string phase)
        {
            switch (phase)
            {
                case "Map": return MusicScene.Map;
                case "Combat": return MusicScene.Combat;
                case "Victory": return MusicScene.Victory;
                case "Defeat": return MusicScene.Death;
                default: return MusicScene.Rewards;
            }
        }
    }
}
