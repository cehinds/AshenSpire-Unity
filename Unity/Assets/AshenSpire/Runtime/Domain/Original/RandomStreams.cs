// RandomStreams.cs — exact uint32 mulberry32/FNV stream port of original engine/rng.js.
// OWNERSHIP: one instance per run; save Seed and Snapshot() after resolved commands.
// CONTENT: stream names are part of the save contract. Add a new stream without
// renaming old ones. Never substitute UnityEngine.Random or System.Random.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class RandomStreams
    {
        private const uint Increment = 0x6d2b79f5;
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNPQRSTUVWXYZ";
        private static readonly string[] Names = { "map", "shuffle", "cardRewards", "relicRewards", "flaskRewards", "armaments", "enemyAI", "enemyHP", "events", "shop", "misc", "smith" };
        private readonly Dictionary<string, uint> _counters = new Dictionary<string, uint>(StringComparer.Ordinal);
        public uint Seed { get; }
        public RandomStreams(uint seed, IReadOnlyDictionary<string, uint> counters = null)
        {
            Seed = seed;
            if (counters != null && counters.Keys.Any(name => !Names.Contains(name))) throw new ArgumentException("Unknown saved RNG stream.");
            foreach (var name in Names) _counters[name] = counters != null && counters.TryGetValue(name, out var value) ? value : 0;
        }
        public Dictionary<string, uint> Snapshot() => new Dictionary<string, uint>(_counters, StringComparer.Ordinal);
        public double Float(string stream)
        {
            if (!_counters.TryGetValue(stream, out var count)) throw new ArgumentException("Unknown RNG stream: " + stream);
            unchecked
            {
                _counters[stream] = ++count;
                uint hash = 0x811c9dc5;
                foreach (var ch in stream) hash = (hash ^ ch) * 0x01000193;
                var t = (Seed ^ hash) + count * Increment;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296d;
            }
        }
        public int Int(string stream, int minimum, int maximum)
        {
            if (maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
            return (int)(minimum + Math.Floor(Float(stream) * ((double)maximum - minimum + 1)));
        }
        public List<T> Shuffle<T>(string stream, IEnumerable<T> values)
        {
            var result = values.ToList();
            for (var i = result.Count - 1; i > 0; i--)
            {
                var j = Int(stream, 0, i); var value = result[i]; result[i] = result[j]; result[j] = value;
            }
            return result;
        }
        public static uint ParseSeed(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            uint value = 0;
            foreach (var ch in text.Trim().ToUpperInvariant().Replace('O', '0'))
            {
                var digit = Alphabet.IndexOf(ch);
                if (digit < 0) throw new ArgumentException("A seed cannot contain '" + ch + "'. Use letters and numbers; O reads as 0.");
                unchecked { value = value * 35 + (uint)digit; }
            }
            return value;
        }
        public static string DisplaySeed(uint value)
        {
            if (value == 0) return "0";
            var text = "";
            while (value > 0) { text = Alphabet[(int)(value % 35)] + text; value /= 35; }
            return text;
        }
    }
}
