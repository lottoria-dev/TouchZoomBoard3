using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TouchZoomBoard
{
    /// <summary>
    /// 확대 화면의 좌표를 실제 데스크톱 좌표로 변환한 뒤 일반 마우스 메시지로 전달한다.
    /// UIAccess나 외부 드라이버가 없는 포터블 배포를 위한 경량 입력 경로이다.
    /// </summary>
    internal sealed class InteractiveInputRouter
    {
        private const uint MkLeftButton = 0x0001;
        private const uint CwpSkipInvisible = 0x0001;
        private const uint CwpSkipDisabled = 0x0002;
        private const uint CwpSkipTransparent = 0x0004;

        private IntPtr capturedTarget;
        private IntPtr capturedTopLevel;
        private bool pointerDown;

        internal bool IsPointerDown => pointerDown;

        internal bool Click(NativeMethods.POINT screenPoint)
        {
            if (!BeginPointer(screenPoint))
            {
                return false;
            }

            return EndPointer(screenPoint);
        }

        internal bool BeginPointer(NativeMethods.POINT screenPoint)
        {
            CancelPointer(screenPoint);
            capturedTarget = FindUnderlyingWindow(screenPoint, out capturedTopLevel);
            if (capturedTarget == IntPtr.Zero)
            {
                return false;
            }

            if (capturedTopLevel != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(capturedTopLevel);
            }

            pointerDown = PostMouseMessage(
                capturedTarget,
                NativeMethods.WM_LBUTTONDOWN,
                MkLeftButton,
                screenPoint);
            return pointerDown;
        }

        internal bool MovePointer(NativeMethods.POINT screenPoint)
        {
            return pointerDown && capturedTarget != IntPtr.Zero &&
                   PostMouseMessage(capturedTarget, NativeMethods.WM_MOUSEMOVE, MkLeftButton, screenPoint);
        }

        internal bool EndPointer(NativeMethods.POINT screenPoint)
        {
            if (!pointerDown || capturedTarget == IntPtr.Zero)
            {
                ResetPointerState();
                return false;
            }

            PostMouseMessage(capturedTarget, NativeMethods.WM_MOUSEMOVE, MkLeftButton, screenPoint);
            var result = PostMouseMessage(capturedTarget, NativeMethods.WM_LBUTTONUP, 0, screenPoint);
            ResetPointerState();
            return result;
        }

        internal void CancelPointer(NativeMethods.POINT screenPoint)
        {
            if (pointerDown && capturedTarget != IntPtr.Zero)
            {
                PostMouseMessage(capturedTarget, NativeMethods.WM_LBUTTONUP, 0, screenPoint);
            }
            ResetPointerState();
        }

        internal bool Scroll(NativeMethods.POINT screenPoint, int wheelDelta)
        {
            IntPtr topLevel;
            var target = FindUnderlyingWindow(screenPoint, out topLevel);
            if (target == IntPtr.Zero)
            {
                return false;
            }

            var wheelParameter = unchecked((IntPtr)((long)(wheelDelta & 0xFFFF) << 16));
            return NativeMethods.PostMessage(
                target,
                NativeMethods.WM_MOUSEWHEEL,
                wheelParameter,
                PackPoint(screenPoint.X, screenPoint.Y));
        }

        private static IntPtr FindUnderlyingWindow(NativeMethods.POINT screenPoint, out IntPtr topLevel)
        {
            var currentProcessId = unchecked((uint)Process.GetCurrentProcess().Id);
            IntPtr result = IntPtr.Zero;
            IntPtr resultTop = IntPtr.Zero;

            NativeMethods.EnumWindows((window, parameter) =>
            {
                if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
                {
                    return true;
                }

                uint processId;
                NativeMethods.GetWindowThreadProcessId(window, out processId);
                if (processId == currentProcessId)
                {
                    return true;
                }

                NativeMethods.RECT rectangle;
                if (!NativeMethods.GetWindowRect(window, out rectangle) ||
                    screenPoint.X < rectangle.Left || screenPoint.X >= rectangle.Right ||
                    screenPoint.Y < rectangle.Top || screenPoint.Y >= rectangle.Bottom)
                {
                    return true;
                }

                resultTop = window;
                result = FindDeepestChild(window, screenPoint);
                return false;
            }, IntPtr.Zero);

            topLevel = resultTop;
            return result;
        }

        private static IntPtr FindDeepestChild(IntPtr parent, NativeMethods.POINT screenPoint)
        {
            var current = parent;
            for (var depth = 0; depth < 12; depth++)
            {
                var clientPoint = screenPoint;
                if (!NativeMethods.ScreenToClient(current, ref clientPoint))
                {
                    break;
                }

                var child = NativeMethods.ChildWindowFromPointEx(
                    current,
                    clientPoint,
                    CwpSkipInvisible | CwpSkipDisabled | CwpSkipTransparent);
                if (child == IntPtr.Zero || child == current)
                {
                    break;
                }
                current = child;
            }
            return current;
        }

        private static bool PostMouseMessage(IntPtr target, int message, uint keys, NativeMethods.POINT screenPoint)
        {
            var clientPoint = screenPoint;
            if (!NativeMethods.ScreenToClient(target, ref clientPoint))
            {
                return false;
            }

            return NativeMethods.PostMessage(
                target,
                message,
                new IntPtr((long)keys),
                PackPoint(clientPoint.X, clientPoint.Y));
        }

        private static IntPtr PackPoint(int x, int y)
        {
            var packed = unchecked((uint)((ushort)x | ((uint)(ushort)y << 16)));
            return new IntPtr(unchecked((int)packed));
        }

        private void ResetPointerState()
        {
            pointerDown = false;
            capturedTarget = IntPtr.Zero;
            capturedTopLevel = IntPtr.Zero;
        }
    }
}
