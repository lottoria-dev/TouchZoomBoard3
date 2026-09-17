using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchZoomBoard
{
    // The current external app is longer-lived than an individual restore request.
    // Pure state: callers validate HWND visibility/PID before observing/restoring.
    internal sealed class ExternalFocusState
    {
        internal struct Request
        {
            internal IntPtr Target;
            internal uint ProcessId;
            internal long Revision;
        }

        private readonly uint ownProcessId;
        private IntPtr target;
        private uint processId;
        private long revision;

        internal ExternalFocusState(uint ownProcessId) { this.ownProcessId = ownProcessId; }

        internal bool Observe(IntPtr window, uint owner)
        {
            if (window == IntPtr.Zero || owner == 0 || owner == ownProcessId) return false;
            if (window != target || owner != processId)
            {
                target = window;
                processId = owner;
                revision++;
            }
            return true;
        }

        internal Request BeginRequest()
        {
            return new Request { Target = target, ProcessId = processId, Revision = ++revision };
        }

        internal bool IsCurrent(Request request)
        {
            return request.Target != IntPtr.Zero && request.Revision == revision &&
                request.Target == target && request.ProcessId == processId;
        }

        internal void Complete(Request request)
        {
            if (IsCurrent(request)) revision++; // Keep the proven app for the next touch.
        }

        internal void Clear()
        {
            revision++;
            target = IntPtr.Zero;
            processId = 0;
        }

        internal void CancelRequests() { revision++; }

        internal static bool CanRestoreFrom(IntPtr foreground, uint foregroundProcessId,
            uint ownProcessId, bool settingsVisible, IEnumerable<IntPtr> inputWindows)
        {
            return !settingsVisible && foreground != IntPtr.Zero &&
                foregroundProcessId == ownProcessId && inputWindows.Contains(foreground);
        }
    }
}
