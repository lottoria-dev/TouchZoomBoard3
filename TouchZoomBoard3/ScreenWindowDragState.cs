using System;
using System.Windows;

namespace TouchZoomBoard
{
    // Input and HWND positions are physical screen pixels, independent of the
    // background magnifier and of WPF's current monitor DPI.
    internal sealed class ScreenWindowDragState
    {
        private Point startScreen;
        private int startLeft;
        private int startTop;
        internal bool IsActive { get; private set; }

        internal bool Begin(NativeMethods.RECT bounds, Point screenPoint)
        {
            if (IsActive || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return false;
            startLeft = bounds.Left;
            startTop = bounds.Top;
            startScreen = screenPoint;
            IsActive = true;
            return true;
        }

        internal bool TryGetPosition(Point screenPoint, out int left, out int top)
        {
            left = startLeft;
            top = startTop;
            if (!IsActive) return false;
            // Always measure from the press, rather than rounding each small
            // move. This avoids cumulative drift at fractional DPI scales.
            left += (int)Math.Round(screenPoint.X - startScreen.X);
            top += (int)Math.Round(screenPoint.Y - startScreen.Y);
            return true;
        }

        internal void End() { IsActive = false; }
    }
}
