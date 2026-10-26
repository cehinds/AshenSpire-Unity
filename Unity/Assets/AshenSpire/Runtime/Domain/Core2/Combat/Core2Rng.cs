// Core2Rng.cs — the JS game's seeded RNG: mulberry32 with named streams and saved counters (SPEC §3.11).
//
// JS at the pin: src/engine/rng.js createRng / mulberry32 / hashString / valueAt. Every stream is derived from
// (seed XOR FNV-1a(stream name)) and its draw counter, so the value of draw i is computable directly and a saved
// counter restores a stream exactly. A Core 2 fight draws on enemyHP (enemy HP rolls), shuffle (deck and round
// refresh), enemyAI (weighted move picks), enemyIntentVisibility and enemyIntentClue (Perception reads),
// combatAvoidance (the d20 Accuracy roll), statusRecovery and statusPressure (status chances) and enemyReaction.
// Doubles are IEEE-754 in both languages, so float, int, pick, shuffle and chance return what JS returns.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Core2
{
    public sealed class Core2Rng : ICore2Dice
    {
        /// <summary>JS STREAM_NAMES, in order (getCounters lists them in this order).</summary>
        public static readonly IReadOnlyList<string> StreamNames = new[]
        {
            "map", "shuffle", "cardRewards", "relicRewards", "flaskRewards", "armaments", "enemyAI", "enemyHP", "events", "shop", "misc",
            "smith", "combatProcs", "seats", "rewardRolls", "shopOffers", "sigils", "enemyIntentVisibility", "combatAvoidance",
            "statusRecovery", "statusPressure", "ashenBlight", "enemyIntentClue", "enemyReaction", "combatRounds",
        };
        /// <summary>JS OMITTED_WHEN_ZERO: a zero counter of these streams is left out of getCounters().</summary>
        private static readonly HashSet<string> OmittedWhenZero = new HashSet<string>(new[] { "combatRounds" }, StringComparer.Ordinal);
        private const uint MulberryInc = 0x6d2b79f5;

        public uint Seed { get; }
        private readonly Dictionary<string, uint> _state = new Dictionary<string, uint>(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> _bases = new Dictionary<string, uint>(StringComparer.Ordinal);

        /// <summary>Optional observer of every draw (stream, value, counter after the draw): a trace for parity checks.</summary>
        public Action<string, double, uint> OnDraw { get; set; }

        public Core2Rng(uint seed, IReadOnlyDictionary<string, int> counters = null)
        {
            Seed = seed;
            foreach (var name in StreamNames)
            {
                _state[name] = counters != null && counters.TryGetValue(name, out var count) ? (uint)count : 0u;
                _bases[name] = seed ^ HashString(name);
            }
        }

        /// <summary>JS hashString: FNV-1a over UTF-16 code units.</summary>
        public static uint HashString(string text)
        {
            var h = 0x811c9dc5u;
            foreach (var c in text) { h ^= c; h = unchecked(h * 0x01000193u); }
            return h;
        }

        /// <summary>JS scramble: the mulberry32 output function on an accumulator value.</summary>
        private static double Scramble(uint a)
        {
            unchecked
            {
                var t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>JS valueAt(base, i): the stream's i-th draw (1-based).</summary>
        private static double ValueAt(uint b, uint i) => Scramble(unchecked(b + i * MulberryInc));

        private void Require(string stream)
        {
            if (!_state.ContainsKey(stream)) throw new ArgumentException("Unknown RNG stream '" + stream + "'. Valid streams: " + string.Join(", ", StreamNames));
        }

        /// <summary>JS float(stream): a number in [0, 1).</summary>
        public double Float(string stream)
        {
            Require(stream);
            var next = unchecked(_state[stream] + 1u);
            _state[stream] = next;
            var value = ValueAt(_bases[stream], next);
            OnDraw?.Invoke(stream, value, next);
            return value;
        }

        /// <summary>JS int(stream, min, max): an integer in [min, max].</summary>
        public int Int(string stream, int min, int max)
        {
            if (max < min) throw new ArgumentException("rng.int: max (" + max + ") < min (" + min + ")");
            return min + (int)Math.Floor(Float(stream) * (max - min + 1));
        }

        int ICore2Dice.Roll(string stream, int min, int max) => Int(stream, min, max);

        /// <summary>JS pick(stream, array).</summary>
        public T Pick<T>(string stream, IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0) throw new ArgumentException("rng.pick: empty or non-array input");
            return items[(int)Math.Floor(Float(stream) * items.Count)];
        }

        /// <summary>JS shuffle(stream, array): a new list, Fisher–Yates from the end.</summary>
        public List<T> Shuffle<T>(string stream, IEnumerable<T> items)
        {
            var output = items.ToList();
            for (var i = output.Count - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(Float(stream) * (i + 1));
                var tmp = output[i]; output[i] = output[j]; output[j] = tmp;
            }
            return output;
        }

        /// <summary>JS chance(stream, pct).</summary>
        public bool Chance(string stream, double pct) => Float(stream) * 100 < pct;

        /// <summary>JS getCounters(): every stream's draw count, in stream order (combatRounds omitted at zero).</summary>
        public IReadOnlyDictionary<string, int> Counters()
        {
            var output = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in StreamNames)
            {
                if (_state[name] == 0 && OmittedWhenZero.Contains(name)) continue;
                output[name] = (int)_state[name];
            }
            return output;
        }

        public int Counter(string stream) { Require(stream); return (int)_state[stream]; }

        /// <summary>JS createRng(seed, getCounters()): a detached copy that draws the same values without moving this one.</summary>
        public Core2Rng Detached() => new Core2Rng(Seed, StreamNames.ToDictionary(n => n, n => (int)_state[n], StringComparer.Ordinal));

        /// <summary>JS restoreCounters.</summary>
        public void Restore(IReadOnlyDictionary<string, int> counters)
        {
            foreach (var name in StreamNames)
            {
                var value = counters != null && counters.TryGetValue(name, out var count) ? count : OmittedWhenZero.Contains(name) ? 0 : -1;
                if (value < 0) throw new ArgumentException("Invalid RNG counter: " + name);
            }
            foreach (var name in StreamNames) _state[name] = counters.TryGetValue(name, out var count) ? (uint)count : 0u;
        }

        /// <summary>JS ToUint32 of an arithmetic result held in a double (wraps as JS `x >>> 0` does).</summary>
        public static uint ToUint32(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            var t = Math.Truncate(value) % 4294967296.0;
            if (t < 0) t += 4294967296.0;
            return (uint)t;
        }
    }
}
