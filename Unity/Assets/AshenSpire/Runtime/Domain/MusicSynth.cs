// MusicSynth.cs — pure-C# port of the HTML procedural music beds (F08).
// SOURCE: src/ui/audio.js playProcedural() + drone(), data in src/content/music.js
// (read through the catalog's bed rows: Root, Scale, CadenceMs, Wave, Lift, Drone, Pulse).
// OUTPUT: mono float PCM at GameAudio's 22050 Hz, RAW level — the context gain,
// music bus, master and mute are applied by the adapter as AudioSource.volume
// (MusicDirector.VolumeFor), exactly where the HTML applies them (bedGain → musicBus → master).
//
// WHAT IS REPRODUCED (WebAudio semantics, approximated where noted):
//   melody   one note per cadence; degree = scale[(step*lift + (step odd ? 2 : 0)) % len],
//            octave ×2 on every 4th step; oscillator = variant wave (sine/triangle/
//            square/sawtooth, band-limited with PolyBLEP like WebAudio's periodic waves);
//            envelope 0.0001 →exp 0.16 @0.08 s →exp 0.0001 @1.8 s, stop @1.9 s.
//   harmony  on step % 3 == 1: sine at ×1.4983, 0.0001 →exp 0.07 @0.12 s →exp 0.0001 @1.6 s, stop 1.7 s.
//   pulse    battle beds: every max(420, cadence/2) ms, sine root/2 →exp root/3 over 0.18 s,
//            0.0001 →exp 0.22 @0.02 s →exp 0.0001 @0.32 s, stop 0.36 s.
//   drone    two sawtooths at root/2 and ×1.005 into a lowpass (Q 1 dB, WebAudio BiquadFilter
//            formula) whose cutoff is 700 ± 260 Hz from a 0.07 Hz sine LFO, level 0.12.
//            The drone's 1.5 s ramp-in is the director's bed FadeInSeconds (AudioSource volume).
// LOOP: the HTML never loops; its note pattern simply repeats every PatternSteps notes
//   (lcm of the melodic period, the octave period 4 and the harmony period 3 = 84 for
//   every shipped bed). One loop = PatternSteps × cadence. To make the loop seamless the
//   drone and LFO frequencies are nudged to a whole number of cycles per loop (< 0.02 Hz),
//   and notes that ring past the loop end wrap into its start. A capped loop (WebGL
//   pre-render) uses the longest whole melodic+octave period (28 steps) that fits.
// DETERMINISM: no random numbers at all; output depends only on the track row and rate.
// Never reads RandomStreams, System.Random or UnityEngine.Random.
using System;

namespace AshenSpire.Domain
{
    /// <summary>One procedural bed, resolved from a catalog track row.</summary>
    public sealed class MusicBedSpec
    {
        public string TrackId;
        public double Root;
        public int[] Scale;
        public int CadenceMs;
        public string Wave;
        public int Lift;
        public bool Drone;
        public bool Pulse;

        public static MusicBedSpec From(MusicCatalog catalog, string trackId)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var t = catalog.Track(trackId) ?? throw new ArgumentException("Unknown music track: " + trackId);
            if (t.Kind != MusicCatalog.KindBed) throw new ArgumentException("Track is not a procedural bed: " + trackId);
            MusicScaleDefinition scale = null;
            foreach (var s in catalog.Scales ?? new MusicScaleDefinition[0]) if (s.Id == t.Scale) scale = s;
            if (scale == null || scale.Steps == null || scale.Steps.Length == 0) throw new ArgumentException("Unknown scale " + t.Scale + " for " + trackId);
            if (t.Root <= 0 || t.CadenceMs <= 0) throw new ArgumentException("Bed needs a positive root and cadence: " + trackId);
            return new MusicBedSpec
            {
                TrackId = t.Id, Root = t.Root, Scale = (int[])scale.Steps.Clone(), CadenceMs = t.CadenceMs,
                Wave = string.IsNullOrEmpty(t.Wave) ? "triangle" : t.Wave, Lift = t.Lift > 0 ? t.Lift : 3,
                Drone = t.Drone, Pulse = t.Pulse,
            };
        }

        /// <summary>Notes before the melody, octave and harmony pattern all repeat (84 for every shipped bed).</summary>
        public int PatternSteps => MusicSynth.Lcm(MelodicSteps, 12);
        /// <summary>Melody + octave period: the shortest loop that keeps the tune intact (28).</summary>
        public int MelodicSteps => MusicSynth.Lcm(MusicSynth.Lcm(Scale.Length / MusicSynth.Gcd(Lift, Scale.Length), 2), 4);
        public double CadenceSeconds => CadenceMs / 1000.0;
        /// <summary>audio.js: setInterval(thump, Math.max(420, cadence / 2)).</summary>
        public double PulseSeconds => Math.Max(420, CadenceMs / 2.0) / 1000.0;

        /// <summary>audio.js playNote(): scale degree, octave and frequency of a step.</summary>
        public double NoteFrequency(int step)
        {
            var deg = Scale[(step * Lift + (step % 2 != 0 ? 2 : 0)) % Scale.Length];
            var oct = step % 4 == 0 ? 2 : 1;
            return Root * oct * Math.Pow(2, deg / 12.0);
        }
    }

    public static class MusicSynth
    {
        public const int SampleRate = 22050; // GameAudio's clip rate; highest harmony (~1.9 kHz) is far below Nyquist
        public const double Floor = 0.0001;
        public const double NoteAttack = 0.08, NotePeak = 0.16, NoteDecayEnd = 1.8, NoteStop = 1.9;
        public const double HarmonyRatio = 1.4983, HarmonyAttack = 0.12, HarmonyPeak = 0.07, HarmonyDecayEnd = 1.6, HarmonyStop = 1.7;
        public const double ThumpSweep = 0.18, ThumpAttack = 0.02, ThumpPeak = 0.22, ThumpDecayEnd = 0.32, ThumpStop = 0.36;
        public const double DroneLevel = 0.12, DroneDetune = 1.005, DroneCutoff = 700, DroneSweep = 260, DroneLfoHz = 0.07, DroneQdB = 1;
        /// <summary>WebGL pre-render cap: loops longer than this use MelodicSteps instead of PatternSteps.</summary>
        public const double DefaultMaxPrerenderSeconds = 120;

        /// <summary>Notes in one loop: the full pattern, or the longest whole melodic period that fits maxSeconds.</summary>
        public static int LoopSteps(MusicBedSpec spec, double maxSeconds = double.PositiveInfinity)
        {
            var full = spec.PatternSteps;
            if (full * spec.CadenceSeconds <= maxSeconds) return full;
            var unit = spec.MelodicSteps;
            var multiples = full / unit;
            for (var m = multiples - 1; m > 1; m--)
                if (multiples % m == 0 && m * unit * spec.CadenceSeconds <= maxSeconds) return m * unit;
            return unit;
        }

        public static double LoopSeconds(MusicBedSpec spec, int steps) => steps * spec.CadenceSeconds;
        public static int LoopSamples(MusicBedSpec spec, int steps, int sampleRate = SampleRate) => (int)Math.Round(steps * spec.CadenceMs * (double)sampleRate / 1000.0);

        /// <summary>A streaming renderer positioned at sample 0 of the loop.</summary>
        public static MusicBedRenderer Open(MusicBedSpec spec, int steps = 0, int sampleRate = SampleRate) =>
            new MusicBedRenderer(spec, steps > 0 ? steps : spec.PatternSteps, sampleRate);

        /// <summary>One full loop of PCM (length LoopSamples), ready for AudioClip.SetData with loop = true.</summary>
        public static float[] Render(MusicBedSpec spec, int steps = 0, int sampleRate = SampleRate)
        {
            var r = Open(spec, steps, sampleRate);
            var data = new float[r.LoopSamples];
            r.Read(data, 0, data.Length);
            return data;
        }

        /// <summary>FNV-1a 64 over 16-bit quantized samples: a stable fingerprint of rendered audio.</summary>
        public static string Hash(float[] samples, int offset = 0, int count = -1)
        {
            if (count < 0) count = samples.Length - offset;
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (var i = offset; i < offset + count; i++)
                {
                    var q = (short)Math.Max(-32767, Math.Min(32767, Math.Round(samples[i] * 32767.0)));
                    h = (h ^ (byte)q) * 1099511628211UL;
                    h = (h ^ (byte)(q >> 8)) * 1099511628211UL;
                }
                return h.ToString("x16");
            }
        }

        public static int Gcd(int a, int b) { a = Math.Abs(a); b = Math.Abs(b); while (b != 0) { var t = a % b; a = b; b = t; } return Math.Max(1, a); }
        public static int Lcm(int a, int b) => a / Gcd(a, b) * b;
    }


    /// <summary>Stateful generator for one bed loop, rendered in 256-sample blocks aligned to the loop so
    /// the output never depends on how callers chunk their reads. Not thread-safe: one reader per
    /// instance (a Unity PCMReaderCallback clip owns its own). Position wraps at LoopSamples.</summary>
    public sealed class MusicBedRenderer
    {
        public const int BlockSamples = 256;
        private const int CoefficientBlock = 16;
        private const double TwoPi = 2 * Math.PI;
        private readonly MusicBedSpec _spec;
        private readonly int _rate, _steps;
        private readonly double _loopSeconds, _cadence, _pulse;
        private readonly double[] _noteFreq;
        private readonly double _droneHz, _droneHz2, _lfoHz, _qLinear;
        private readonly double[] _block = new double[BlockSamples];
        private int _blockStart = -1, _filterAt = -1;
        private bool _coefficientsStale = true;
        private double _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public int SampleRate => _rate;
        public int Steps => _steps;
        public int LoopSamples { get; }
        public int Position { get; private set; }
        public MusicBedSpec Spec => _spec;

        internal MusicBedRenderer(MusicBedSpec spec, int steps, int rate)
        {
            if (rate < 8000) throw new ArgumentOutOfRangeException(nameof(rate));
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _rate = rate; _steps = steps;
            LoopSamples = MusicSynth.LoopSamples(spec, steps, rate);
            _loopSeconds = LoopSamples / (double)rate;
            _cadence = spec.CadenceSeconds;
            _pulse = _loopSeconds / Math.Max(1, (int)Math.Round(_loopSeconds / spec.PulseSeconds)); // == PulseSeconds for every shipped bed
            _noteFreq = new double[steps];
            for (var i = 0; i < steps; i++) _noteFreq[i] = spec.NoteFrequency(i);
            // Whole cycles per loop so the drone and its filter sweep never click at the seam.
            _droneHz = Whole(spec.Root / 2);
            _droneHz2 = Whole(spec.Root / 2 * MusicSynth.DroneDetune);
            _lfoHz = Math.Max(1, Math.Round(MusicSynth.DroneLfoHz * _loopSeconds)) / _loopSeconds;
            _qLinear = Math.Pow(10, MusicSynth.DroneQdB / 20.0);
            SetPosition(0);
        }

        private double Whole(double hz) => Math.Max(1, Math.Round(hz * _loopSeconds)) / _loopSeconds;

        /// <summary>Unity PCMSetPositionCallback: jump; the drone filter is re-warmed from just before.</summary>
        public void SetPosition(int position)
        {
            Position = Mod(position, LoopSamples);
            _blockStart = -1;
            PrimeFilter(Position - Position % BlockSamples);
        }

        /// <summary>Unity PCMReaderCallback: fill the whole buffer (mono).</summary>
        public void Read(float[] data) => Read(data, 0, data.Length);

        public void Read(float[] data, int offset, int count)
        {
            var i = 0;
            while (i < count)
            {
                var start = Position - Position % BlockSamples;
                if (_blockStart != start) RenderBlock(start);
                var n = Math.Min(count - i, Math.Min(start + BlockSamples, LoopSamples) - Position);
                for (var j = 0; j < n; j++)
                {
                    var v = _block[Position - start + j];
                    data[offset + i + j] = (float)(v > 1 ? 1 : v < -1 ? -1 : v);
                }
                i += n;
                Position += n;
                if (Position >= LoopSamples) Position = 0;
            }
        }

        private void RenderBlock(int start)
        {
            var end = Math.Min(start + BlockSamples, LoopSamples);
            Array.Clear(_block, 0, BlockSamples);
            if (_spec.Drone)
            {
                if (_filterAt != start) PrimeFilter(start);
                for (var n = start; n < end; n++) _block[n - start] = Drone(n);
                _filterAt = end == LoopSamples ? 0 : end;
            }
            // Melody and harmony: every note whose voice overlaps the block, wrapping across the seam.
            var t0 = start / (double)_rate; var t1 = (end - 1) / (double)_rate;
            var kMax = (int)Math.Floor(t1 / _cadence);
            for (var k = (int)Math.Ceiling((t0 - MusicSynth.NoteStop) / _cadence); k <= kMax; k++)
            {
                var step = Mod(k, _steps);
                var f = _noteFreq[step];
                Voice(start, end, k * _cadence, f, _spec.Wave, MusicSynth.NoteAttack, MusicSynth.NotePeak, MusicSynth.NoteDecayEnd, MusicSynth.NoteStop);
                if (step % 3 == 1)
                    Voice(start, end, k * _cadence, f * MusicSynth.HarmonyRatio, "sine", MusicSynth.HarmonyAttack, MusicSynth.HarmonyPeak, MusicSynth.HarmonyDecayEnd, MusicSynth.HarmonyStop);
            }
            if (_spec.Pulse)
            {
                var jMax = (int)Math.Floor(t1 / _pulse);
                for (var j = (int)Math.Ceiling((t0 - MusicSynth.ThumpStop) / _pulse); j <= jMax; j++) Thump(start, end, j * _pulse);
            }
            _blockStart = start;
        }

        /// <summary>One oscillator with the audio.js envelope: setValueAtTime(0.0001) → exponentialRamp(peak,
        /// attack) → exponentialRamp(0.0001, decayEnd), held until stop. Exact at segment starts, geometric
        /// steps (what an exponential ramp is) in between.</summary>
        private void Voice(int start, int end, double onset, double f, string wave, double attack, double peak, double decayEnd, double stop)
        {
            var n = Math.Max(start, (int)Math.Floor(onset * _rate));
            var tau = n / (double)_rate - onset;
            while (tau < 0 && n < end) tau = ++n / (double)_rate - onset;
            if (n >= end || tau >= stop) return;
            var segment = tau < attack ? 0 : tau < decayEnd ? 1 : 2;
            var env = Envelope(tau, attack, peak, decayEnd);
            var rise = Math.Exp(Math.Log(peak / MusicSynth.Floor) / (attack * _rate));
            var fall = Math.Exp(Math.Log(MusicSynth.Floor / peak) / ((decayEnd - attack) * _rate));
            var dp = f / _rate;
            var phase = Frac(f * tau);
            var sine = wave == "sine";
            var w = TwoPi * dp; var k2 = 2 * Math.Cos(w);
            var y1 = Math.Sin(TwoPi * phase); var y0 = Math.Sin(TwoPi * phase - w);
            for (; n < end; n++)
            {
                tau = n / (double)_rate - onset;
                if (tau >= stop) break;
                if (segment == 0 && tau >= attack) { segment = 1; env = Envelope(tau, attack, peak, decayEnd); }
                else if (segment == 1 && tau >= decayEnd) { segment = 2; env = MusicSynth.Floor; }
                double osc;
                if (sine) { osc = y1; var next = k2 * y1 - y0; y0 = y1; y1 = next; }
                else { osc = Shape(wave, phase, dp); phase += dp; if (phase >= 1) phase -= 1; }
                _block[n - start] += osc * env;
                if (segment == 0) env *= rise; else if (segment == 1) env *= fall;
            }
        }

        /// <summary>audio.js thump: sine root/2 →exp root/3 over 0.18 s with a 0.02/0.32 s envelope.</summary>
        private void Thump(int start, int end, double onset)
        {
            var n = Math.Max(start, (int)Math.Floor(onset * _rate));
            for (; n < end; n++)
            {
                var tau = n / (double)_rate - onset;
                if (tau < 0) continue;
                if (tau >= MusicSynth.ThumpStop) break;
                _block[n - start] += Math.Sin(TwoPi * Frac(ThumpCycles(tau))) * Envelope(tau, MusicSynth.ThumpAttack, MusicSynth.ThumpPeak, MusicSynth.ThumpDecayEnd);
            }
        }

        /// <summary>Cycles elapsed for frequency root/2 →exp root/3 over 0.18 s, then held.</summary>
        private double ThumpCycles(double tau)
        {
            var f0 = _spec.Root / 2; var f1 = _spec.Root / 3; var T = MusicSynth.ThumpSweep;
            var lnR = Math.Log(f1 / f0);
            if (tau <= T) return f0 * T * (Math.Exp(lnR * tau / T) - 1) / lnR;
            return f0 * T * (f1 / f0 - 1) / lnR + f1 * (tau - T);
        }

        private static double Envelope(double tau, double attack, double peak, double end)
        {
            if (tau <= 0) return MusicSynth.Floor;
            if (tau < attack) return MusicSynth.Floor * Math.Exp(Math.Log(peak / MusicSynth.Floor) * tau / attack);
            if (tau < end) return peak * Math.Exp(Math.Log(MusicSynth.Floor / peak) * (tau - attack) / (end - attack));
            return MusicSynth.Floor;
        }

        private void PrimeFilter(int start)
        {
            _x1 = _x2 = _y1 = _y2 = 0; _coefficientsStale = true;
            _filterAt = start;
            if (!_spec.Drone) return;
            var warm = Math.Min(LoopSamples, _rate / 4); // the filter settles in a few ms; 0.25 s is ample
            for (var i = warm; i > 0; i--) Drone(Mod(start - i, LoopSamples));
        }

        /// <summary>The drone voice at loop sample n; advances the lowpass state (call in order).</summary>
        private double Drone(int n)
        {
            if (_coefficientsStale || n % CoefficientBlock == 0)
            {
                var at = (n - n % CoefficientBlock) / (double)_rate;
                SetLowpass(MusicSynth.DroneCutoff + MusicSynth.DroneSweep * Math.Sin(TwoPi * Frac(_lfoHz * at)));
                _coefficientsStale = false;
            }
            var t = n / (double)_rate;
            var x = Shape("sawtooth", Frac(_droneHz * t), _droneHz / _rate) + Shape("sawtooth", Frac(_droneHz2 * t), _droneHz2 / _rate);
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y * MusicSynth.DroneLevel;
        }

        /// <summary>WebAudio BiquadFilterNode lowpass (Audio EQ Cookbook with Q in dB).</summary>
        private void SetLowpass(double cutoff)
        {
            var w0 = TwoPi * Math.Min(cutoff, _rate * 0.45) / _rate;
            var alpha = Math.Sin(w0) / (2 * _qLinear);
            var cos = Math.Cos(w0);
            var a0 = 1 + alpha;
            _b0 = (1 - cos) / 2 / a0; _b1 = (1 - cos) / a0; _b2 = _b0;
            _a1 = -2 * cos / a0; _a2 = (1 - alpha) / a0;
        }

        /// <summary>WebAudio oscillator shapes at phase p (square +1 first half, sawtooth 0↑ wrapping at half
        /// cycle, triangle 0↑ peaking at a quarter), PolyBLEP-smoothed where they jump.</summary>
        private static double Shape(string wave, double p, double dt)
        {
            switch (wave)
            {
                case "sine": return Math.Sin(TwoPi * p);
                case "square": return (p < 0.5 ? 1.0 : -1.0) + Blep(p, dt) - Blep(Frac(p + 0.5), dt);
                case "sawtooth": { var q = Frac(p + 0.5); return 2 * q - 1 - Blep(q, dt); }
                default: return p < 0.25 ? 4 * p : p < 0.75 ? 2 - 4 * p : 4 * p - 4; // triangle
            }
        }

        private static double Blep(double t, double dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1; }
            if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
            return 0;
        }

        private static double Frac(double x) => x - Math.Floor(x);
        private static int Mod(int a, int m) { var r = a % m; return r < 0 ? r + m : r; }
    }
}
