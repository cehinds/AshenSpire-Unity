// FeelCurves.cs — CSS timing functions and keyframe sampling for the F07 feel profile.
// Pure C#: no UnityEngine, no RNG, no allocation per sample. Deterministic for a given input.
// SOURCE: the browser algorithm for cubic-bezier() (WebKit UnitBezier / Chromium
// gfx::CubicBezier): Newton-Raphson on x(t), then bisection, then y(t). Named easings are
// the CSS Easing Functions Level 1 constants (ease, ease-in, ease-out, ease-in-out, linear).
// CSS applies an animation's timing function to EACH keyframe interval, not to the whole
// animation; SampleTrack does the same, so a Unity tween reads exactly like the browser.
using System;
using System.Globalization;

namespace AshenSpire.Domain
{
    /// <summary>One CSS cubic-bezier(x1, y1, x2, y2) timing function.</summary>
    public readonly struct CubicBezier
    {
        private const double Epsilon = 1e-7;
        private const int NewtonIterations = 8;
        private const int BisectionIterations = 64;
        public readonly double X1, Y1, X2, Y2;
        private readonly double _ax, _bx, _cx, _ay, _by, _cy;

        public CubicBezier(double x1, double y1, double x2, double y2)
        {
            if (x1 < 0 || x1 > 1 || x2 < 0 || x2 > 1) throw new ArgumentOutOfRangeException(nameof(x1), "cubic-bezier x values must be in [0, 1].");
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
            // Polynomial coefficients with implicit P0 = (0,0) and P3 = (1,1).
            _cx = 3.0 * x1; _bx = 3.0 * (x2 - x1) - _cx; _ax = 1.0 - _cx - _bx;
            _cy = 3.0 * y1; _by = 3.0 * (y2 - y1) - _cy; _ay = 1.0 - _cy - _by;
        }

        public bool IsLinear => X1 == Y1 && X2 == Y2;
        private double SampleX(double t) => ((_ax * t + _bx) * t + _cx) * t;
        private double SampleY(double t) => ((_ay * t + _by) * t + _cy) * t;
        private double SampleDerivativeX(double t) => (3.0 * _ax * t + 2.0 * _bx) * t + _cx;

        /// <summary>The curve parameter t whose x(t) equals x (browser SolveCurveX).</summary>
        public double SolveX(double x)
        {
            var t = x;
            for (var i = 0; i < NewtonIterations; i++)
            {
                var error = SampleX(t) - x;
                if (Math.Abs(error) < Epsilon) return t;
                var derivative = SampleDerivativeX(t);
                if (Math.Abs(derivative) < 1e-6) break;
                t -= error / derivative;
            }
            double lo = 0, hi = 1;
            t = x;
            for (var i = 0; i < BisectionIterations && lo < hi; i++)
            {
                var sample = SampleX(t);
                if (Math.Abs(sample - x) < Epsilon) return t;
                if (x > sample) lo = t; else hi = t;
                t = (hi - lo) * 0.5 + lo;
            }
            return t;
        }

        /// <summary>Eased output for input progress x. Input is clamped to [0, 1] (animation progress never leaves it).</summary>
        public double Evaluate(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            if (IsLinear) return x;
            return SampleY(SolveX(x));
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "cubic-bezier({0}, {1}, {2}, {3})", X1, Y1, X2, Y2);
    }

    public static class FeelCurves
    {
        // CSS Easing Functions Level 1, §2.2 — the named keywords ARE these beziers.
        public static readonly CubicBezier Linear = new CubicBezier(0, 0, 1, 1);
        public static readonly CubicBezier Ease = new CubicBezier(0.25, 0.1, 0.25, 1);
        public static readonly CubicBezier EaseIn = new CubicBezier(0.42, 0, 1, 1);
        public static readonly CubicBezier EaseOut = new CubicBezier(0, 0, 0.58, 1);
        public static readonly CubicBezier EaseInOut = new CubicBezier(0.42, 0, 0.58, 1);

        /// <summary>True when Parse would accept the text.</summary>
        public static bool TryParse(string text, out CubicBezier curve)
        {
            curve = Ease;
            if (text == null) return false;
            var s = text.Trim().ToLowerInvariant();
            switch (s)
            {
                case "": case "ease": curve = Ease; return true; // CSS initial value is `ease`
                case "linear": curve = Linear; return true;
                case "ease-in": curve = EaseIn; return true;
                case "ease-out": curve = EaseOut; return true;
                case "ease-in-out": curve = EaseInOut; return true;
            }
            if (!s.StartsWith("cubic-bezier(", StringComparison.Ordinal) || !s.EndsWith(")", StringComparison.Ordinal)) return false;
            var parts = s.Substring(13, s.Length - 14).Split(',');
            if (parts.Length != 4) return false;
            var v = new double[4];
            for (var i = 0; i < 4; i++)
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            if (v[0] < 0 || v[0] > 1 || v[2] < 0 || v[2] > 1) return false;
            curve = new CubicBezier(v[0], v[1], v[2], v[3]);
            return true;
        }

        /// <summary>A CSS timing function: a keyword or cubic-bezier(a, b, c, d). Empty means `ease`.</summary>
        public static CubicBezier Parse(string text)
        {
            if (!TryParse(text, out var curve)) throw new FormatException("Unsupported CSS timing function: " + text);
            return curve;
        }

        /// <summary>Eased value of `easing` at progress x in [0, 1].</summary>
        public static double Evaluate(string easing, double x) => Parse(easing).Evaluate(x);

        /// <summary>
        /// Value of a keyframed property at overall progress p in [0, 1]. The timing function
        /// eases each interval between consecutive keys (CSS keyframe semantics). Keys must be
        /// ascending in T; a track with no keys returns `fallback`.
        /// </summary>
        public static double SampleTrack(FeelKey[] keys, double p, CubicBezier easing, double fallback)
        {
            if (keys == null || keys.Length == 0) return fallback;
            if (p <= keys[0].T) return keys[0].V;
            var last = keys[keys.Length - 1];
            if (p >= last.T) return last.V;
            for (var i = 1; i < keys.Length; i++)
            {
                var b = keys[i];
                if (p > b.T) continue;
                var a = keys[i - 1];
                var span = b.T - a.T;
                if (span <= 0) return b.V;
                var local = easing.Evaluate((p - a.T) / span);
                return a.V + (b.V - a.V) * local;
            }
            return last.V;
        }
    }
}
