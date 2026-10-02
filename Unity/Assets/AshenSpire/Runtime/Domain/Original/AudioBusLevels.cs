// AudioBusLevels.cs — the effective gain of each audio bus for one OriginalPlayerSettings.
// ENTRY POINT: AudioBusLevels.From(settings). RunController.Settings.cs ApplyPlayerSettings
// hands Sfx and Ui to GameAudio and Music (with MusicOn) to MusicPlayer, so the master
// multiply, mute and music on/off rules live here and are tested (UnityTests/Music).
// Rules: every bus = master × its own level; Mute → every bus 0; music off → music 0.
// Campaign sound tuning and the music catalog's context gain still apply on top.
using System;

namespace AshenSpire.Domain.Original
{
    public readonly struct AudioBusLevels
    {
        /// <summary>Master after mute (0..1).</summary>
        public readonly double Master;
        /// <summary>Effective 0..1 gains: master × bus, 0 while muted; Music is also 0 while music is off.</summary>
        public readonly double Music, Sfx, Ui;
        /// <summary>False when music should stop (music off or muted): MusicPlayer fades out instead of ramping to 0.</summary>
        public readonly bool MusicOn;

        public AudioBusLevels(double master, double music, double sfx, double ui, bool musicOn)
        {
            Master = Clamp01(master); Music = Clamp01(music); Sfx = Clamp01(sfx); Ui = Clamp01(ui); MusicOn = musicOn;
        }

        /// <summary>Music as the 0..100 integer MusicDirector takes (MusicPlayer passes master as 100).</summary>
        public int MusicPercent => (int)Math.Round(Music * 100, MidpointRounding.AwayFromZero);

        public static AudioBusLevels From(OriginalPlayerSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Muted) return new AudioBusLevels(0, 0, 0, 0, false);
            var master = Clamp01(settings.MasterVolume);
            var musicOn = settings.MusicEnabled;
            return new AudioBusLevels(master,
                musicOn ? master * Clamp01(settings.MusicVolume) : 0,
                master * Clamp01(settings.SfxVolume),
                master * Clamp01(settings.UiVolume),
                musicOn);
        }

        private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Min(1, Math.Max(0, value));
    }
}
