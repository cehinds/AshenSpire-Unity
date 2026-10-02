// ExplainGesture.cs — pure long-press and popup-placement rules for the explanation panel
// (EnemyTelegraphView.Explain; US-4.4, US-13.4). No UnityEngine: Presentation feeds pointer
// positions and layout sizes in, then applies the answers. Tested in UnityTests/Telegraphs.
using System;

namespace AshenSpire.Domain.Original
{
    /// <summary>
    /// One touch/pen press that may become a long press. Any up, cancel or leave of the
    /// tracked pointer, or movement beyond the slop, cancels it; it can fire at most once.
    /// </summary>
    public sealed class LongPressTracker
    {
        private readonly float _slopSquared;
        private float _x, _y;
        public int? PointerId { get; private set; }
        public bool Active => PointerId != null;
        public LongPressTracker(float slop) { if (slop < 0) throw new ArgumentOutOfRangeException(nameof(slop)); _slopSquared = slop * slop; }

        /// <summary>Starts tracking; a press already in progress is replaced.</summary>
        public void Begin(int pointerId, float x, float y) { PointerId = pointerId; _x = x; _y = y; }

        /// <summary>True when this move cancelled the tracked press (moved beyond the slop).</summary>
        public bool Move(int pointerId, float x, float y)
        {
            if (PointerId != pointerId) return false;
            var dx = x - _x; var dy = y - _y;
            if (dx * dx + dy * dy <= _slopSquared) return false;
            PointerId = null; return true;
        }

        /// <summary>Pointer up, cancel or leave. True when it cancelled the tracked press.</summary>
        public bool End(int pointerId)
        {
            if (PointerId != pointerId) return false;
            PointerId = null; return true;
        }

        public void Cancel() => PointerId = null;

        /// <summary>The timer elapsed: true (once) only if the same press is still held in place.</summary>
        public bool Fire(int pointerId)
        {
            if (PointerId != pointerId) return false;
            PointerId = null; return true;
        }
    }

    public readonly struct PopupPlacement
    {
        public float Left { get; }
        public float Top { get; }
        /// <summary>The popup's max height; content taller than this scrolls.</summary>
        public float MaxHeight { get; }
        public bool Above { get; }
        public PopupPlacement(float left, float top, float maxHeight, bool above) { Left = left; Top = top; MaxHeight = maxHeight; Above = above; }

        /// <summary>
        /// Below the anchor when it fits, else above it, else clamped inside the viewport.
        /// The height is bounded to the viewport minus both margins, so every line can be
        /// reached by scrolling the body.
        /// </summary>
        public static PopupPlacement Place(float anchorCenterX, float anchorTop, float anchorBottom, float width, float contentHeight,
            float viewportWidth, float viewportHeight, float margin = 4, float gap = 4)
        {
            var maxHeight = Math.Max(0, viewportHeight - 2 * margin);
            var height = Math.Min(Math.Max(0, contentHeight), maxHeight);
            var left = Math.Max(margin, Math.Min(anchorCenterX - width / 2, viewportWidth - width - margin));
            var below = anchorBottom + gap;
            if (below + height <= viewportHeight - margin) return new PopupPlacement(left, below, maxHeight, false);
            var above = anchorTop - gap - height;
            if (above >= margin) return new PopupPlacement(left, above, maxHeight, true);
            return new PopupPlacement(left, Math.Max(margin, viewportHeight - margin - height), maxHeight, false);
        }
    }
}
