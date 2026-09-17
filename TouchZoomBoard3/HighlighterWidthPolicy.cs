using System;

namespace TouchZoomBoard
{
    internal static class HighlighterWidthPolicy
    {
        internal const double Minimum = 16.0;
        internal const double Maximum = 64.0;
        internal const double Default = 36.0;
        internal const int SettingsVersion = 2;

        internal static double[] Presets => new[] { 16.0, 24.0, 36.0, 48.0, 64.0 };

        internal static double Normalize(double width)
        {
            if (double.IsNaN(width) || double.IsInfinity(width)) return Default;
            return Math.Max(Minimum, Math.Min(Maximum, width));
        }

        internal static double Restore(double storedWidth, int storedVersion)
        {
            if (double.IsNaN(storedWidth) || double.IsInfinity(storedWidth)) return Default;
            // Upgrade the old 8/12/18/24/32 scale exactly once; subsequent
            // saves carry the new scale version alongside the selected width.
            return storedVersion < SettingsVersion
                ? Normalize(Math.Max(8.0, Math.Min(32.0, storedWidth)) * 2.0)
                : Normalize(storedWidth);
        }
    }
}
