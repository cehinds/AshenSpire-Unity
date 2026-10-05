// Arithmetic shared by character, skill and class tracks in published test 898.
// Callers own curve snapshots and zero-based step selection; this changes no save.
using System;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public static class OriginalXpCurve
    {
        public static double StepCost(JObject curve, int step, double exponentialEpsilon = 1e-9)
        {
            if (curve == null || step < 0) throw new ArgumentException("XP curve and a nonnegative step are required.");
            double Number(string key)
            {
                var token = curve[key];
                if (token == null || token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                    throw new ArgumentException("XP curve " + key + " must be numeric.");
                var number = (double)token;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("XP curve " + key + " must be finite.");
                return number;
            }
            var basis = Number("base");
            var linear = curve["linear"]?.Type == JTokenType.Boolean && (bool)curve["linear"];
            var raw = linear ? basis + step * basis * Number("multScaler") : basis * Math.Pow(Number("growth"), step);
            if (double.IsNaN(raw) || double.IsInfinity(raw)) throw new ArgumentException("XP step is not finite.");
            var rounded = curve["roundTo"];
            var unit = rounded != null && (rounded.Type == JTokenType.Integer || rounded.Type == JTokenType.Float)
                && !double.IsInfinity((double)rounded) && !double.IsNaN((double)rounded)
                && (double)rounded > 0 && (double)rounded == Math.Floor((double)rounded) ? (double)rounded : 1;
            // JavaScript Math.round rounds positive halfway values upward, not to even.
            var epsilon = linear ? 1e-9 : exponentialEpsilon;
            return Math.Max(unit, Math.Floor(raw / unit + epsilon + .5) * unit);
        }
    }
}
