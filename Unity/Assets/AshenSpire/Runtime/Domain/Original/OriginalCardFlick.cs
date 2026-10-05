using System;
namespace AshenSpire.Domain.Original
{
    // Current core defaults: 64 px upward, predominantly vertical, 300 px/s
    // over the last 120 ms. Coordinates use the UI panel's reference pixels.
    public static class OriginalCardFlick
    {
        public static bool DistanceMet(float startX, float startY, float endX, float endY) => startY - endY >= 64 && startY - endY > Math.Abs(endX - startX);
        public static bool Qualifies(float startX, float startY, float endX, float endY, float recentY, double elapsedSeconds) => DistanceMet(startX, startY, endX, endY) && elapsedSeconds > 0 && (recentY - endY) / elapsedSeconds >= 300;
    }
}
