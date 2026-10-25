// OriginalCardRulesFit.cs — how the illustrated face fits resolved rules into its parchment
// without ever silently clipping them. Escalation, in order:
//   1 FaceLines: one clause per line (the JS face), document size max → min;
//   2 Joined: the same clauses as running prose (no forced breaks), max → min;
//   3 Floor: running prose below the document minimum, down to a readable floor;
//   4 Overflow: still too long at the floor. The face shows the floor size and an
//     "Inspect card" cue; card inspection (OriginalCardInspection.Content) always prints
//     the complete resolved rules in its details, so nothing is lost.
// The measure is supplied by the caller: the face uses UI Toolkit's MeasureTextSize;
// UnityTests/CardText uses a conservative estimate from the bundled font's metrics.
// No UnityEngine dependency.
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AshenSpire.Presentation
{
    public static class OriginalCardRulesFit
    {
        /// <summary>The readable floor, in 360-wide template units (the 1815 minimum is 22). Set
        /// for the face's Liberation Serif, whose x-height (0.459em) is taller than the former
        /// Cormorant's (0.386em): lower case at 14 here (6.4) is larger than Cormorant's at 15 (5.8).</summary>
        public const float FloorFontSize = 14f;
        private const int Steps = 8;

        public enum Step { FaceLines = 1, Joined = 2, Floor = 3, Overflow = 4 }

        public readonly struct Plan
        {
            public readonly Step Step; public readonly string Text; public readonly float Size;
            public Plan(Step step, string text, float size) { Step = step; Text = text; Size = size; }
            public bool Clipped => Step == Step.Overflow;
        }

        /// <summary>One clause per line, no leading "Deal", no full stops (the JS face lines).</summary>
        public static string FaceLines(string rules)
        {
            if (string.IsNullOrWhiteSpace(rules)) return "";
            var lines = Regex.Split(rules.Trim(), @"(?<=[.!?])\s+")
                .Select(s => Regex.Replace(s.Trim().TrimEnd('.'), @"^Deal (?=\d)", ""))
                .Where(s => s.Length > 0);
            return string.Join("\n", lines);
        }

        /// <summary>The same clauses as running prose: the forced breaks dropped.</summary>
        public static string Joined(string faceLines)
            => string.IsNullOrEmpty(faceLines) ? "" : string.Join(". ", faceLines.Split('\n')) + ".";

        /// <summary>
        /// The face's rank gem (JS illustratedCard.js .card-rank, "Rank N"): an upgraded card is rank
        /// 1 + its upgrades. Unity marks each upgrade with a trailing "+" on the name; the upgraded flag
        /// alone counts as one. 0 means no gem.
        /// </summary>
        public static int Rank(string name, bool upgraded)
        {
            var plus = 0; name = name ?? "";
            for (var i = name.Length - 1; i >= 0 && name[i] == '+'; i--) plus++;
            if (plus == 0 && upgraded) plus = 1;
            return plus == 0 ? 0 : 1 + plus;
        }

        /// <summary>A rule moved the shown SP price off the printed one: -1 cheaper, 1 dearer, 0 unchanged
        /// (JS combat.js costShift; never for an X cost).</summary>
        public static int CostShift(int shown, int printed, bool variable) => variable ? 0 : Math.Sign(shown - printed);

        /// <summary>
        /// Rich text for the face's rules, or null when plain text says it all. Numbers that rose read
        /// green and bold, numbers that fell red (JS .ic-text .val.up/.down; needs baseText with the same
        /// count of numbers). With underlineDamage, the numbers on damage clauses are underlined, as the
        /// JS hand marks the inspectable damage figures ([data-damage-receipt], a dotted underline).
        /// </summary>
        public static string Markup(string text, string baseText, bool underlineDamage, string up = "#205e2b", string down = "#8d2424")
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') >= 0) return null;
            var now = Regex.Matches(text, @"\d+");
            var was = baseText == null ? null : Regex.Matches(baseText, @"\d+");
            if (was != null && was.Count != now.Count) was = null;
            if (now.Count == 0 || was == null && !underlineDamage) return null;
            var output = new StringBuilder(); var at = 0; var changed = false;
            for (var i = 0; i < now.Count; i++)
            {
                var number = now[i];
                output.Append(text, at, number.Index - at); at = number.Index + number.Length;
                var delta = 0;
                if (was != null && long.TryParse(number.Value, out var a) && long.TryParse(was[i].Value, out var b)) delta = Math.Sign(a - b);
                var underline = underlineDamage && DamageClause(text, number.Index);
                var copy = number.Value;
                if (underline) copy = "<u>" + copy + "</u>";
                if (delta != 0) copy = "<color=" + (delta > 0 ? up : down) + "><b>" + copy + "</b></color>";
                changed |= underline || delta != 0;
                output.Append(copy);
            }
            output.Append(text, at, text.Length - at);
            return changed ? output.ToString() : null;
        }

        // The face line (or, in running prose, the sentence) holding index mentions damage.
        private static bool DamageClause(string text, int index)
        {
            var start = Math.Max(text.LastIndexOf('\n', Math.Max(0, index - 1)), text.LastIndexOf(". ", Math.Max(0, index - 1), StringComparison.Ordinal)) + 1;
            var ends = new[] { text.IndexOf('\n', index), text.IndexOf(". ", index, StringComparison.Ordinal) }.Where(x => x >= 0).ToArray();
            var end = ends.Length == 0 ? text.Length : ends.Min();
            return text.Substring(start, end - start).IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Choose the first step whose text fits the box. measure(text, size, width) returns the
        /// wrapped height at that font size within that width. Sizes and box share one unit.
        /// </summary>
        public static Plan Choose(string rules, float width, float height, float maxSize, float minSize, float floorSize,
            Func<string, float, float, float> measure)
        {
            if (measure == null) throw new ArgumentNullException(nameof(measure));
            var lines = FaceLines(rules); var joined = Joined(lines);
            minSize = Math.Min(minSize, maxSize); floorSize = Math.Min(floorSize, minSize);
            bool Fits(string text, float size) => measure(text, size, width) <= height + .5f;
            float? Search(string text, float from, float to)
            {
                for (var i = 0; i <= Steps; i++)
                {
                    var size = from - (from - to) * i / Steps;
                    if (Fits(text, size)) return size;
                }
                return null;
            }
            if (Search(lines, maxSize, minSize) is float a) return new Plan(Step.FaceLines, lines, a);
            if (Search(joined, maxSize, minSize) is float b) return new Plan(Step.Joined, joined, b);
            if (Search(joined, minSize, floorSize) is float c) return new Plan(Step.Floor, joined, c);
            return new Plan(Step.Overflow, joined, floorSize);
        }
    }
}
