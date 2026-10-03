using System.Windows;

namespace TouchZoomBoard
{
    // One state per physical press: rapid independent taps are never debounced.
    internal sealed class ToolPressState
    {
        private Point origin;
        private bool selectedOnDown;
        internal bool Active { get; private set; }
        internal bool LongPressTriggered { get; private set; }

        internal bool Begin(Point point, bool selectOnPress)
        {
            origin = point;
            Active = true;
            LongPressTriggered = false;
            selectedOnDown = selectOnPress;
            return selectOnPress;
        }

        internal void Move(Point point, double tolerance)
        {
            if ((point - origin).Length > tolerance) Active = false;
        }

        internal bool TryLongPress()
        {
            if (!Active || LongPressTriggered) return false;
            LongPressTriggered = true;
            return true;
        }

        internal bool End(Point point, Rect bounds)
        {
            var invoke = Active && !LongPressTriggered && !selectedOnDown && bounds.Contains(point);
            Active = false;
            return invoke;
        }

        internal void Cancel() { Active = false; }
    }
}
