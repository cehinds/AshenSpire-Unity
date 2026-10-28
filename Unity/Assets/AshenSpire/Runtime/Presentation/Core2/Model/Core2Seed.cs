// Core2Seed.cs — the JS seed vocabulary (src/engine/rng.js seedToString / seedFromString / seedProblem), so the seed a
// player reads and types on the Review step is the JS one: base 35 over 0–9 and A–Z without O (O reads as 0).
// Engine-free.
using System;
using System.Linq;

namespace AshenSpire.Presentation.Core2
{
    public static class Core2Seed
    {
        public const string Alphabet = "0123456789ABCDEFGHIJKLMNPQRSTUVWXYZ";
        /// <summary>JS SEED_MAX_LEN: what a seed field accepts.</summary>
        public const int MaxLength = 10;

        public static string ToText(uint seed)
        {
            if (seed == 0) return "0";
            var n = seed;
            var text = "";
            while (n > 0) { text = Alphabet[(int)(n % 35)] + text; n /= 35; }
            return text;
        }

        private static string Clean(string text) => (text ?? "").Trim().ToUpperInvariant().Replace('O', '0');

        /// <summary>JS seedVocabulary: the sentence derived from the alphabet.</summary>
        public static string Vocabulary()
        {
            var digits = Alphabet.Where(char.IsDigit).ToArray();
            var letters = Alphabet.Where(c => c >= 'A' && c <= 'Z').ToArray();
            return "seeds are " + digits.First() + "–" + digits.Last() + " and " + letters.First() + "–" + letters.Last() + ", and O reads as 0";
        }

        /// <summary>JS seedProblem: null for a seed, else one sentence naming the character that stops it.</summary>
        public static string Problem(string text)
        {
            foreach (var c in Clean(text))
                if (Alphabet.IndexOf(c) < 0) return "A seed cannot contain “" + c + "” — " + Vocabulary() + ".";
            return null;
        }

        /// <summary>JS seedFromString (uint32 wrap): throws the Problem sentence for a bad seed.</summary>
        public static uint FromText(string text)
        {
            var why = Problem(text);
            if (why != null) throw new FormatException(why);
            uint n = 0;
            foreach (var c in Clean(text)) n = unchecked(n * 35 + (uint)Alphabet.IndexOf(c));
            return n;
        }
    }
}
