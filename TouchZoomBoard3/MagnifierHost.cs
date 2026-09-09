using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TouchZoomBoard
{
    internal sealed class MagnifierHost : HwndHost
    {
        private static readonly object RuntimeLock = new object();
        private static int runtimeUsers;
        private IntPtr magnifierHandle;
        private NativeMethods.RECT sourceRectangle;
        private float scaleX = 1f;
        private float scaleY = 1f;
        private float appliedScaleX = 1f;
        private float appliedScaleY = 1f;
        private bool transformInitialized;
        private bool sourceInitialized;
        private bool sourceDirty;
        private IntPtr[] lastExcludedWindows = new IntPtr[0];
        private bool runtimeReleased;

        internal IntPtr MagnifierHandle => magnifierHandle;
        internal static int RuntimeUsers
        {
            get
            {
                lock (RuntimeLock) return runtimeUsers;
            }
        }
        internal event Action<int, IntPtr, IntPtr> NativeMessageReceived;

        internal MagnifierHost()
        {
            lock (RuntimeLock)
            {
                if (runtimeUsers == 0 && !NativeMethods.MagInitialize())
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 라이브 줌 기능을 초기화하지 못했습니다.");
                }

                runtimeUsers++;
                DebugLog.WriteDiagnostic("MAG", "MagInitialize 완료 runtimeUsers=" + runtimeUsers);
            }
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            magnifierHandle = NativeMethods.CreateWindowEx(
                0,
                "Magnifier",
                "TouchZoomBoard3Magnifier",
                NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE,
                0,
                0,
                Math.Max(1, (int)ActualWidth),
                Math.Max(1, (int)ActualHeight),
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (magnifierHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "라이브 줌 화면을 만들지 못했습니다.");
            }

            DebugLog.WriteDiagnostic("MAG", "Magnifier HWND 생성 handle=0x" +
                magnifierHandle.ToInt64().ToString("X") + ", parent=0x" +
                hwndParent.Handle.ToInt64().ToString("X"));
            ApplyTransform();
            appliedScaleX = scaleX;
            appliedScaleY = scaleY;
            transformInitialized = true;
            sourceDirty = sourceInitialized;
            lastExcludedWindows = new IntPtr[0];
            return new HandleRef(this, magnifierHandle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (hwnd.Handle != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(hwnd.Handle);
            }

            magnifierHandle = IntPtr.Zero;
            transformInitialized = false;
            sourceDirty = sourceInitialized;
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            if (magnifierHandle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    magnifierHandle,
                    IntPtr.Zero,
                    0,
                    0,
                    Math.Max(1, (int)rcBoundingBox.Width),
                    Math.Max(1, (int)rcBoundingBox.Height),
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
        }

        protected override IntPtr WndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            NativeMessageReceived?.Invoke(message, wParam, lParam);
            return base.WndProc(hwnd, message, wParam, lParam, ref handled);
        }

        internal bool SetView(NativeMethods.RECT source, double zoom)
        {
            return SetView(source, zoom, zoom);
        }

        internal bool SetView(NativeMethods.RECT source, double requestedScaleX, double requestedScaleY)
        {
            // 미니맵은 전체 화면을 작은 영역에 축소해야 하므로 1 미만의 배율도 허용한다.
            var nextScaleX = (float)Math.Max(0.02, Math.Min(5.0, requestedScaleX));
            var nextScaleY = (float)Math.Max(0.02, Math.Min(5.0, requestedScaleY));
            var transformChanged = !transformInitialized ||
                Math.Abs(nextScaleX - appliedScaleX) > 0.000001f ||
                Math.Abs(nextScaleY - appliedScaleY) > 0.000001f;
            var sourceChanged = !sourceInitialized || !RectEquals(sourceRectangle, source);

            if (!transformChanged && !sourceChanged)
            {
                return false;
            }

            if (sourceChanged)
            {
                sourceRectangle = source;
                sourceInitialized = true;
                sourceDirty = true;
            }

            if (transformChanged)
            {
                scaleX = nextScaleX;
                scaleY = nextScaleY;
                ApplyTransform();
                appliedScaleX = scaleX;
                appliedScaleY = scaleY;
                transformInitialized = true;
            }

            return RefreshFrame();
        }

        internal void SetExcludedWindows(params IntPtr[] windowHandles)
        {
            if (magnifierHandle == IntPtr.Zero || windowHandles == null)
            {
                return;
            }

            var validHandles = windowHandles
                .Where(handle => handle != IntPtr.Zero)
                .Distinct()
                .OrderBy(handle => handle.ToInt64())
                .ToArray();
            if (HandleArraysEqual(lastExcludedWindows, validHandles))
            {
                return;
            }

            var applied = NativeMethods.MagSetWindowFilterList(
                magnifierHandle,
                NativeMethods.MW_FILTERMODE_EXCLUDE,
                validHandles.Length,
                validHandles);
            if (applied)
            {
                lastExcludedWindows = validHandles;
            }
            else
            {
                DebugLog.WriteDiagnostic("MAG-FILTER", "적용 실패 count=" + validHandles.Length +
                    ", error=" + Marshal.GetLastWin32Error());
            }
        }

        internal bool RefreshFrame()
        {
            var applied = true;
            if (magnifierHandle == IntPtr.Zero)
            {
                return false;
            }

            if (sourceDirty)
            {
                applied = NativeMethods.MagSetWindowSource(magnifierHandle, sourceRectangle);
                if (applied)
                {
                    sourceDirty = false;
                }
            }

            var invalidated = NativeMethods.InvalidateRect(magnifierHandle, IntPtr.Zero, false);
            return applied && invalidated;
        }

        private void ApplyTransform()
        {
            if (magnifierHandle == IntPtr.Zero)
            {
                return;
            }

            var transform = NativeMethods.MAGTRANSFORM.Create(scaleX, scaleY);
            if (!NativeMethods.MagSetWindowTransform(magnifierHandle, ref transform))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "확대 배율을 적용하지 못했습니다.");
            }
            DebugLog.WriteDiagnostic("MAG", "MagSetWindowTransform 완료 scale=" +
                scaleX.ToString("0.000000") + "x" + scaleY.ToString("0.000000"));
        }

        private static bool RectEquals(NativeMethods.RECT left, NativeMethods.RECT right)
        {
            return left.Left == right.Left &&
                   left.Top == right.Top &&
                   left.Right == right.Right &&
                   left.Bottom == right.Bottom;
        }

        private static bool HandleArraysEqual(IntPtr[] left, IntPtr[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index]) return false;
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            lock (RuntimeLock)
            {
                if (!runtimeReleased && runtimeUsers > 0)
                {
                    runtimeReleased = true;
                    runtimeUsers--;
                    if (runtimeUsers == 0)
                    {
                        NativeMethods.MagUninitialize();
                    }
                }
            }
        }
    }
}
