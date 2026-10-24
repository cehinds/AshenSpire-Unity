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
using System.Text.RegularExpressions;

namespace AshenSpire.Presentation
{
    public static class OriginalCardRulesFit
    {
        /// <summary>The readable floor, in 360-wide template units (the 1815 minimum is 22).</summary>
        public const float FloorFontSize = 15f;
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
