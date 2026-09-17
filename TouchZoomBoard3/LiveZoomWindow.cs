using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class LiveZoomWindow : Window, IDisposable
    {
        private const double NavigationMinimumIntervalMilliseconds = 50.0;
        private const double NavigationMinimumDistancePixels = 2.0;
        private readonly MagnifierHost magnifierHost;
        private Forms.Screen targetScreen;
        private double focusX;
        private double focusY;
        private double zoomFactor = 1.0;
        private IntPtr windowHandle;
        private NativeMethods.RECT sourceRectangle;
        private bool renderingSubscribed;
        private double effectiveZoomX = 1.0;
        private double effectiveZoomY = 1.0;
        private bool pendingViewportCenter;
        private bool pendingViewportImmediate;
        private double pendingViewportX;
        private double pendingViewportY;
        private long lastNavigationAppliedTimestamp;

        // 100%까지 핀치해도 세션을 유지해야 다시 손가락을 벌려 확대할 수 있다.
        internal bool IsZoomVisible => IsVisible;
        internal double ZoomFactor => zoomFactor;
        internal IntPtr WindowHandle => windowHandle;
        internal IntPtr MagnifierHandle => magnifierHost.MagnifierHandle;
        internal NativeMethods.RECT SourceRectangle => sourceRectangle;
        internal NativeMethods.RECT ScreenRectangle => targetScreen == null
            ? new NativeMethods.RECT()
            : new NativeMethods.RECT(
                targetScreen.Bounds.Left,
                targetScreen.Bounds.Top,
                targetScreen.Bounds.Right,
                targetScreen.Bounds.Bottom);

        internal event Action<NativeMethods.RECT, NativeMethods.RECT> ViewChanged;

        internal LiveZoomWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            Background = Brushes.Black;

            magnifierHost = new MagnifierHost();
            Content = magnifierHost;

            SourceInitialized += (sender, args) =>
            {
                windowHandle = new WindowInteropHelper(this).Handle;
                NativeMethods.EnsureToolWindowStyle(windowHandle);
            };
        }

        internal void ShowForScreen(Forms.Screen screen, double initialZoom)
        {
            targetScreen = screen ?? Forms.Screen.PrimaryScreen;
            var bounds = targetScreen.Bounds;
            focusX = bounds.Left + bounds.Width / 2.0;
            focusY = bounds.Top + bounds.Height / 2.0;

            if (!IsVisible)
            {
                Show();
            }

            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = new WindowInteropHelper(this).Handle;
            }

            NativeMethods.PositionTopmostWindow(windowHandle, bounds.Left, bounds.Top, bounds.Width, bounds.Height, false);
            DebugLog.WriteDiagnostic("LIVEZOOM", "확대창 표시 handle=0x" + windowHandle.ToInt64().ToString("X") +
                ", screen=" + targetScreen.DeviceName + ", bounds=" + bounds +
                ", dpiScale=" + NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1).ToString("0.000") +
                ", initialZoom=" + initialZoom);
            SetZoom(initialZoom);
            StartRendering();
        }

        internal void SetZoom(double zoom)
        {
            zoomFactor = Math.Max(1.0, Math.Min(5.0, zoom));
            DebugLog.WriteDiagnostic("LIVEZOOM", "SetZoom zoom=" + zoomFactor);
            UpdateSourceRectangle();
        }

        internal void ZoomBy(double factor)
        {
            if (factor <= 0)
            {
                return;
            }

            SetZoom(zoomFactor * factor);
        }

        internal void ZoomAtViewportPoint(double zoom, double xDip, double yDip)
        {
            if (targetScreen == null || !IsVisible)
            {
                return;
            }

            var bounds = targetScreen.Bounds;
            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var destinationX = bounds.Left + xDip * scale;
            var destinationY = bounds.Top + yDip * scale;
            var xRatio = Math.Max(0.0, Math.Min(1.0,
                (destinationX - bounds.Left) / Math.Max(1.0, bounds.Width)));
            var yRatio = Math.Max(0.0, Math.Min(1.0,
                (destinationY - bounds.Top) / Math.Max(1.0, bounds.Height)));
            var oldWidth = Math.Max(1.0, sourceRectangle.Right - sourceRectangle.Left);
            var oldHeight = Math.Max(1.0, sourceRectangle.Bottom - sourceRectangle.Top);
            var anchorX = sourceRectangle.Left + xRatio * oldWidth;
            var anchorY = sourceRectangle.Top + yRatio * oldHeight;

            zoomFactor = Math.Max(1.0, Math.Min(5.0, zoom));
            var newWidth = Math.Max(1.0, bounds.Width / zoomFactor);
            var newHeight = Math.Max(1.0, bounds.Height / zoomFactor);
            focusX = anchorX + (0.5 - xRatio) * newWidth;
            focusY = anchorY + (0.5 - yRatio) * newHeight;
            DebugLog.WriteDiagnostic("LIVEZOOM", "ZoomAt zoom=" + zoomFactor +
                ", viewport=" + xDip.ToString("0.0") + "," + yDip.ToString("0.0"));
            UpdateSourceRectangle();
        }

        internal void PanBy(double viewportDeltaX, double viewportDeltaY)
        {
            if (targetScreen == null || zoomFactor <= 1.0)
            {
                return;
            }

            var scale = NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1);
            focusX -= viewportDeltaX * scale / zoomFactor;
            focusY -= viewportDeltaY * scale / zoomFactor;
            UpdateSourceRectangle();
        }

        internal void SetViewportCenter(double xRatio, double yRatio, bool immediate)
        {
            if (targetScreen == null)
            {
                return;
            }

            pendingViewportX = Math.Max(0.0, Math.Min(1.0, xRatio));
            pendingViewportY = Math.Max(0.0, Math.Min(1.0, yRatio));
            pendingViewportImmediate = pendingViewportImmediate || immediate;
            pendingViewportCenter = true;
        }

        internal bool TryMapViewportPoint(double xDip, double yDip, out NativeMethods.POINT sourcePoint)
        {
            sourcePoint = new NativeMethods.POINT();
            if (targetScreen == null || !IsVisible)
            {
                return false;
            }

            var bounds = targetScreen.Bounds;
            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var destinationX = bounds.Left + xDip * scale;
            var destinationY = bounds.Top + yDip * scale;
            var xRatio = (destinationX - bounds.Left) / Math.Max(1.0, bounds.Width);
            var yRatio = (destinationY - bounds.Top) / Math.Max(1.0, bounds.Height);
            xRatio = Math.Max(0.0, Math.Min(1.0, xRatio));
            yRatio = Math.Max(0.0, Math.Min(1.0, yRatio));
            sourcePoint = new NativeMethods.POINT(
                sourceRectangle.Left + (int)Math.Round(xRatio * Math.Max(1, sourceRectangle.Right - sourceRectangle.Left - 1)),
                sourceRectangle.Top + (int)Math.Round(yRatio * Math.Max(1, sourceRectangle.Bottom - sourceRectangle.Top - 1)));
            return true;
        }

        internal void SetExcludedWindows(params IntPtr[] handles)
        {
            magnifierHost.SetExcludedWindows(handles);
        }

        internal void StopZoom()
        {
            StopRendering();
            pendingViewportCenter = false;
            pendingViewportImmediate = false;
            zoomFactor = 1.0;
            Hide();
            DebugLog.WriteDiagnostic("LIVEZOOM", "StopZoom 완료");
        }

        internal void EnsureToolWindowStyle()
        {
            NativeMethods.EnsureToolWindowStyle(windowHandle);
        }

        private bool UpdateSourceRectangle()
        {
            if (targetScreen == null)
            {
                return false;
            }

            var bounds = targetScreen.Bounds;
            var sourceWidth = Math.Max(1, (int)Math.Round(bounds.Width / zoomFactor));
            var sourceHeight = Math.Max(1, (int)Math.Round(bounds.Height / zoomFactor));
            var nextEffectiveZoomX = bounds.Width / (double)sourceWidth;
            var nextEffectiveZoomY = bounds.Height / (double)sourceHeight;

            var minimumX = bounds.Left;
            var maximumX = bounds.Right - sourceWidth;
            var minimumY = bounds.Top;
            var maximumY = bounds.Bottom - sourceHeight;

            var left = (int)Math.Round(focusX - sourceWidth / 2.0);
            var top = (int)Math.Round(focusY - sourceHeight / 2.0);
            left = Math.Max(minimumX, Math.Min(maximumX, left));
            top = Math.Max(minimumY, Math.Min(maximumY, top));

            focusX = left + sourceWidth / 2.0;
            focusY = top + sourceHeight / 2.0;

            var nextRectangle = new NativeMethods.RECT(left, top, left + sourceWidth, top + sourceHeight);
            var rectangleChanged = !RectEquals(sourceRectangle, nextRectangle) ||
                Math.Abs(effectiveZoomX - nextEffectiveZoomX) > 0.0000001 ||
                Math.Abs(effectiveZoomY - nextEffectiveZoomY) > 0.0000001;
            effectiveZoomX = nextEffectiveZoomX;
            effectiveZoomY = nextEffectiveZoomY;
            if (!rectangleChanged)
            {
                return false;
            }

            sourceRectangle = nextRectangle;
            // 정수 픽셀인 원본 사각형과 출력 크기가 정확히 맞도록 X/Y 유효 배율을 사용한다.
            // 비정수 요청 배율에서 발생하던 한 픽셀 늘어짐과 좌표 오차를 줄인다.
            magnifierHost.SetView(sourceRectangle, effectiveZoomX, effectiveZoomY);
            ViewChanged?.Invoke(ScreenRectangle, sourceRectangle);
            return true;
        }

        private void StartRendering()
        {
            if (renderingSubscribed) return;
            renderingSubscribed = true;
            lastNavigationAppliedTimestamp = 0;
            CompositionTarget.Rendering += HandleRendering;
        }

        private void StopRendering()
        {
            if (!renderingSubscribed) return;
            CompositionTarget.Rendering -= HandleRendering;
            renderingSubscribed = false;
        }

        private void HandleRendering(object sender, EventArgs args)
        {
            var now = Stopwatch.GetTimestamp();
            var viewUpdated = ApplyPendingViewportCenter(now);
            if (!viewUpdated)
            {
                magnifierHost.RefreshFrame();
            }
        }

        private bool ApplyPendingViewportCenter(long now)
        {
            if (!pendingViewportCenter || targetScreen == null)
            {
                return false;
            }

            var immediate = pendingViewportImmediate;
            if (!immediate && lastNavigationAppliedTimestamp != 0)
            {
                var elapsedMilliseconds = (now - lastNavigationAppliedTimestamp) *
                    1000.0 / Stopwatch.Frequency;
                if (elapsedMilliseconds < NavigationMinimumIntervalMilliseconds)
                {
                    return false;
                }
            }

            var bounds = targetScreen.Bounds;
            var xRatio = pendingViewportX;
            var yRatio = pendingViewportY;
            pendingViewportCenter = false;
            pendingViewportImmediate = false;
            var nextFocusX = bounds.Left + xRatio * bounds.Width;
            var nextFocusY = bounds.Top + yRatio * bounds.Height;
            if (!immediate &&
                Math.Abs(nextFocusX - focusX) < NavigationMinimumDistancePixels &&
                Math.Abs(nextFocusY - focusY) < NavigationMinimumDistancePixels)
            {
                return false;
            }

            focusX = nextFocusX;
            focusY = nextFocusY;
            var changed = UpdateSourceRectangle();
            if (changed)
            {
                lastNavigationAppliedTimestamp = now;
            }
            return changed;
        }

        private static bool RectEquals(NativeMethods.RECT left, NativeMethods.RECT right)
        {
            return left.Left == right.Left &&
                   left.Top == right.Top &&
                   left.Right == right.Right &&
                   left.Bottom == right.Bottom;
        }

        public void Dispose()
        {
            StopRendering();
            magnifierHost.Dispose();
            Close();
        }
    }
}
