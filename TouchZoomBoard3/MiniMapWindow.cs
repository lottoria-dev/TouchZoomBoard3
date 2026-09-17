using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    /// <summary>
    /// 확대 중 패널에 붙어 표시되는 작은 탐색 지도이다. 네이티브 축소판 위에
    /// WPF 입력 계층을 두어 마우스, 터치, 터치펜을 같은 좌표 경로로 처리한다.
    /// </summary>
    internal sealed class MiniMapWindow : IDisposable
    {
        private const double MapWidthDip = 138.0;
        private const double MapPaddingDip = 5.0;
        private const double DockGapDip = 3.0;
        private const double ScreenMarginDip = 8.0;

        private readonly Window previewWindow;
        private readonly Window inputWindow;
        private readonly MagnifierHost previewHost;
        private readonly Grid inputRoot;
        private Border previewFrame;
        private Border mapShade;
        private readonly Border viewportIndicator;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer fadeTimer;
        private Forms.Screen targetScreen;
        private IntPtr previewHandle;
        private IntPtr inputHandle;
        private IntPtr anchorHandle;
        private bool preferBelow = true;
        private bool nativeDragging;
        private bool mouseDragging;
        private TouchDevice activeTouchDevice;
        private DateTime suppressPromotedMouseUntil = DateTime.MinValue;
        private bool thumbnailAvailable;
        private bool disposed;

        internal event Action<double, double, bool> NavigateRequested;
        internal event Action<double> ZoomRequested;

        internal bool IsVisible => inputWindow.IsVisible;
        internal IntPtr PreviewHandle => previewHandle;
        internal IntPtr InputHandle => inputHandle;
        internal IntPtr MagnifierHandle => previewHost.MagnifierHandle;

        internal MiniMapWindow()
        {
            previewHost = new MagnifierHost();
            previewHost.Cursor = Cursors.SizeAll;
            Stylus.SetIsPressAndHoldEnabled(previewHost, false);
            previewHost.NativeMessageReceived += HandlePreviewNativeMessage;
            previewWindow = CreatePreviewWindow();

            inputRoot = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Cursor = Cursors.SizeAll
            };
            Stylus.SetIsPressAndHoldEnabled(inputRoot, false);
            viewportIndicator = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 232, 234, 237)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    ShadowDepth = 1,
                    Opacity = 0.35
                }
            };
            inputRoot.Children.Add(viewportIndicator);
            AttachNavigationInput();
            inputWindow = CreateInputWindow();

            refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            refreshTimer.Tick += (sender, args) => previewHost.RefreshFrame();

            fadeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            fadeTimer.Tick += (sender, args) =>
            {
                fadeTimer.Stop();
                SetActiveAppearance(false);
            };
            ApplyTheme(LiquidGlassTheme.GlassTintOpacity);
        }

        internal void ShowForScreen(Forms.Screen screen, IntPtr panelHandle)
        {
            targetScreen = screen ?? Forms.Screen.PrimaryScreen;
            anchorHandle = panelHandle;
            var aspect = targetScreen.Bounds.Height / (double)Math.Max(1, targetScreen.Bounds.Width);
            var heightDip = Math.Max(62.0, Math.Min(92.0, MapWidthDip * aspect));
            previewWindow.Width = MapWidthDip;
            previewWindow.Height = heightDip;
            inputWindow.Width = MapWidthDip;
            inputWindow.Height = heightDip;

            if (!previewWindow.IsVisible) previewWindow.Show();
            if (!inputWindow.IsVisible) inputWindow.Show();
            EnsureHandles();
            PositionWindows();
            NativeMethods.ApplyRoundedWindowRegionFromCurrentBounds(previewHandle, 17);
            NativeMethods.ApplyRoundedWindowRegionFromCurrentBounds(inputHandle, 17);

            var scale = NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1);
            var contentWidthPixels = Math.Max(1.0, (MapWidthDip - MapPaddingDip * 2) * scale);
            var thumbnailScale = contentWidthPixels / Math.Max(1.0, targetScreen.Bounds.Width);
            try
            {
                previewHost.SetView(
                    new NativeMethods.RECT(
                        targetScreen.Bounds.Left,
                        targetScreen.Bounds.Top,
                        targetScreen.Bounds.Right,
                        targetScreen.Bounds.Bottom),
                    thumbnailScale);
                thumbnailAvailable = true;
                refreshTimer.Start();
            }
            catch (Exception exception)
            {
                // 일부 그래픽 드라이버가 1 미만 배율을 거부해도 탐색 지도는 유지한다.
                thumbnailAvailable = false;
                refreshTimer.Stop();
                DebugLog.Write("미니맵 화면 축소판을 표시하지 못해 영역 지도만 사용합니다.", exception);
            }

            SetActiveAppearance(false);
        }

        internal void UpdateViewport(NativeMethods.RECT screenBounds, NativeMethods.RECT sourceBounds)
        {
            if (!inputWindow.IsVisible)
            {
                return;
            }

            var innerWidth = Math.Max(1.0, inputRoot.ActualWidth);
            var innerHeight = Math.Max(1.0, inputRoot.ActualHeight);
            var screenWidth = Math.Max(1.0, screenBounds.Right - screenBounds.Left);
            var screenHeight = Math.Max(1.0, screenBounds.Bottom - screenBounds.Top);
            var left = (sourceBounds.Left - screenBounds.Left) / screenWidth * innerWidth;
            var top = (sourceBounds.Top - screenBounds.Top) / screenHeight * innerHeight;
            var width = Math.Max(6.0, (sourceBounds.Right - sourceBounds.Left) / screenWidth * innerWidth);
            var height = Math.Max(6.0, (sourceBounds.Bottom - sourceBounds.Top) / screenHeight * innerHeight);

            viewportIndicator.Width = Math.Min(innerWidth, width);
            viewportIndicator.Height = Math.Min(innerHeight, height);
            viewportIndicator.Margin = new Thickness(left, top, 0, 0);
        }

        internal void ToggleDock()
        {
            preferBelow = !preferBelow;
            PositionWindows();
            Pulse();
        }

        internal void SetAnchorWindow(IntPtr panelHandle)
        {
            anchorHandle = panelHandle;
            PositionWindows();
        }

        internal void SetExcludedWindows(params IntPtr[] handles)
        {
            previewHost.SetExcludedWindows(handles);
        }

        internal void SetOwnerWindow(IntPtr owner)
        {
            EnsureHandles();
            NativeMethods.SetOwnerWindow(previewHandle, owner);
            NativeMethods.SetOwnerWindow(inputHandle, previewHandle);
        }

        internal void ApplyTheme(double opacity)
        {
            var glassTintOpacity = Math.Max(0.0,
                Math.Min(LiquidGlassTheme.MaximumPanelGlassStrength, opacity));
            if (previewHandle != IntPtr.Zero)
            {
                LiquidGlassTheme.ApplyWindow(
                    previewWindow,
                    HwndSource.FromHwnd(previewHandle),
                    previewHandle,
                    "minimap-preview");
            }
            else
            {
                previewWindow.Background = new SolidColorBrush(LiquidGlassTheme.FallbackWindowColor);
            }
            if (previewFrame != null)
            {
                previewFrame.Opacity = 1.0;
                previewFrame.Background = LiquidGlassTheme.CreatePanelGlassBrush();
                previewFrame.BorderBrush = LiquidGlassTheme.HairlineBrush;
            }
            if (mapShade != null)
            {
                mapShade.Opacity = 1.0;
                mapShade.Background = LiquidGlassTheme.CreatePanelGlassBrush();
                mapShade.BorderBrush = LiquidGlassTheme.HairlineBrush;
            }
            DebugLog.WriteDiagnostic("GLASS", "surface=minimap-ui, glassTintOpacity=" +
                glassTintOpacity.ToString("0.000") +
                ", panelGlassPercent=" + Math.Round(
                    glassTintOpacity /
                        LiquidGlassTheme.MaximumPanelGlassStrength * 100.0).ToString("0") +
                ", rootUiOpacity=1.00, semantics=surface-tint");
            SetActiveAppearance(false);
        }

        internal void BringToFront()
        {
            if (!IsVisible || targetScreen == null)
            {
                return;
            }
            PositionWindows();
        }

        internal void EnsureToolWindowStyle()
        {
            NativeMethods.EnsureToolWindowStyle(previewHandle);
            NativeMethods.EnsureToolWindowStyle(inputHandle);
        }

        internal void Hide()
        {
            refreshTimer.Stop();
            fadeTimer.Stop();
            nativeDragging = false;
            mouseDragging = false;
            if (inputRoot.IsMouseCaptured) inputRoot.ReleaseMouseCapture();
            inputRoot.ReleaseAllTouchCaptures();
            activeTouchDevice = null;
            inputWindow.Hide();
            previewWindow.Hide();
        }

        private Window CreatePreviewWindow()
        {
            previewFrame = new Border
            {
                Padding = new Thickness(MapPaddingDip),
                Background = LiquidGlassTheme.CreatePanelGlassBrush(),
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(17),
                Child = previewHost
            };
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Topmost = true,
                Background = new SolidColorBrush(LiquidGlassTheme.FallbackWindowColor),
                Content = previewFrame
            };
            window.SourceInitialized += (sender, args) =>
            {
                previewHandle = new WindowInteropHelper(window).Handle;
                NativeMethods.EnsureToolWindowStyle(previewHandle);
                var source = HwndSource.FromHwnd(previewHandle);
                LiquidGlassTheme.ApplyWindow(window, source, previewHandle, "minimap-preview");
                NativeMethods.TryApplyAntialiasedRoundedCorners(previewHandle);
            };
            return window;
        }

        private Window CreateInputWindow()
        {
            mapShade = new Border
            {
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(17),
                // 이 계층은 터치를 받기 위한 창이며 화면을 가리는 짙은 판이 아니다.
                Background = LiquidGlassTheme.CreatePanelGlassBrush(),
                Padding = new Thickness(MapPaddingDip),
                Child = inputRoot,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 4,
                    Direction = 270,
                    Opacity = 0.09
                }
            };
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Topmost = true,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Content = mapShade
            };
            window.SourceInitialized += (sender, args) =>
            {
                inputHandle = new WindowInteropHelper(window).Handle;
                NativeMethods.EnsureToolWindowStyle(inputHandle);
                NativeMethods.AddExtendedWindowStyle(
                    inputHandle,
                    NativeMethods.WS_EX_NOACTIVATE);
            };
            return window;
        }

        private void EnsureHandles()
        {
            if (previewHandle == IntPtr.Zero)
                previewHandle = new WindowInteropHelper(previewWindow).Handle;
            if (inputHandle == IntPtr.Zero)
                inputHandle = new WindowInteropHelper(inputWindow).Handle;
        }

        private void PositionWindows()
        {
            if (targetScreen == null || !previewWindow.IsVisible || !inputWindow.IsVisible)
            {
                return;
            }

            EnsureHandles();
            previewWindow.UpdateLayout();
            inputWindow.UpdateLayout();
            var bounds = targetScreen.WorkingArea;
            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var width = Math.Max(1, (int)Math.Round(MapWidthDip * scale));
            var height = Math.Max(1, (int)Math.Round(inputWindow.Height * scale));
            var margin = Math.Max(4, (int)Math.Round(ScreenMarginDip * scale));
            var gap = Math.Max(2, (int)Math.Round(DockGapDip * scale));
            var left = bounds.Right - width - margin;
            var top = bounds.Bottom - height - margin;

            if (anchorHandle != IntPtr.Zero &&
                NativeMethods.GetWindowRect(anchorHandle, out var anchorRectangle))
            {
                left = anchorRectangle.Left +
                    ((anchorRectangle.Right - anchorRectangle.Left) - width) / 2;
                var below = anchorRectangle.Bottom + gap;
                var above = anchorRectangle.Top - height - gap;
                var belowFits = below + height <= bounds.Bottom - margin;
                var aboveFits = above >= bounds.Top + margin;

                if (preferBelow)
                    top = belowFits || !aboveFits ? below : above;
                else
                    top = aboveFits || !belowFits ? above : below;
            }

            left = Math.Max(bounds.Left + margin, Math.Min(bounds.Right - width - margin, left));
            top = Math.Max(bounds.Top + margin, Math.Min(bounds.Bottom - height - margin, top));

            NativeMethods.PositionTopmostWindow(previewHandle, left, top, width, height, false);
            NativeMethods.PositionTopmostWindow(inputHandle, left, top, width, height, false);
        }

        private void AttachNavigationInput()
        {
            inputRoot.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (DateTime.UtcNow < suppressPromotedMouseUntil)
                {
                    args.Handled = true;
                    return;
                }

                mouseDragging = true;
                inputRoot.CaptureMouse();
                NavigateInputPoint(args.GetPosition(inputRoot), true);
                Pulse();
                args.Handled = true;
            };
            inputRoot.PreviewMouseMove += (sender, args) =>
            {
                if (!mouseDragging) return;
                if (args.LeftButton != MouseButtonState.Pressed)
                {
                    mouseDragging = false;
                    inputRoot.ReleaseMouseCapture();
                    return;
                }
                NavigateInputPoint(args.GetPosition(inputRoot), false);
                args.Handled = true;
            };
            inputRoot.PreviewMouseLeftButtonUp += (sender, args) =>
            {
                if (!mouseDragging) return;
                NavigateInputPoint(args.GetPosition(inputRoot), true);
                mouseDragging = false;
                inputRoot.ReleaseMouseCapture();
                Pulse();
                args.Handled = true;
            };
            inputRoot.LostMouseCapture += (sender, args) => mouseDragging = false;
            inputRoot.PreviewMouseWheel += (sender, args) =>
            {
                ZoomRequested?.Invoke(args.Delta > 0 ? 1.12 : 1.0 / 1.12);
                Pulse();
                args.Handled = true;
            };
            inputRoot.PreviewTouchDown += (sender, args) =>
            {
                if (activeTouchDevice != null)
                {
                    args.Handled = true;
                    return;
                }

                activeTouchDevice = args.TouchDevice;
                suppressPromotedMouseUntil = DateTime.UtcNow.AddMilliseconds(900);
                activeTouchDevice.Capture(inputRoot);
                NavigateInputPoint(args.GetTouchPoint(inputRoot).Position, true);
                Pulse();
                args.Handled = true;
            };
            inputRoot.PreviewTouchMove += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;
                NavigateInputPoint(args.GetTouchPoint(inputRoot).Position, false);
                args.Handled = true;
            };
            inputRoot.PreviewTouchUp += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;
                NavigateInputPoint(args.GetTouchPoint(inputRoot).Position, true);
                activeTouchDevice.Capture(null);
                activeTouchDevice = null;
                suppressPromotedMouseUntil = DateTime.UtcNow.AddMilliseconds(900);
                Pulse();
                args.Handled = true;
            };
            inputRoot.LostTouchCapture += (sender, args) =>
            {
                if (activeTouchDevice == args.TouchDevice)
                    activeTouchDevice = null;
            };
        }

        private void NavigateInputPoint(Point point, bool immediate)
        {
            var width = Math.Max(1.0, inputRoot.ActualWidth);
            var height = Math.Max(1.0, inputRoot.ActualHeight);
            var xRatio = Math.Max(0.0, Math.Min(1.0, point.X / width));
            var yRatio = Math.Max(0.0, Math.Min(1.0, point.Y / height));
            NavigateRequested?.Invoke(xRatio, yRatio, immediate);
        }

        private void HandlePreviewNativeMessage(int message, IntPtr wParam, IntPtr lParam)
        {
            var x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
            if (message == NativeMethods.WM_LBUTTONDOWN)
            {
                nativeDragging = true;
                NavigatePreviewPoint(x, y, true);
                Pulse();
            }
            else if (message == NativeMethods.WM_MOUSEMOVE && nativeDragging)
            {
                NavigatePreviewPoint(x, y, false);
            }
            else if (message == NativeMethods.WM_LBUTTONUP && nativeDragging)
            {
                NavigatePreviewPoint(x, y, true);
                nativeDragging = false;
                Pulse();
            }
        }

        private void NavigatePreviewPoint(double x, double y, bool immediate)
        {
            NativeMethods.RECT rectangle;
            var hasRectangle = NativeMethods.GetWindowRect(previewHost.MagnifierHandle, out rectangle);
            var width = hasRectangle
                ? Math.Max(1.0, rectangle.Right - rectangle.Left)
                : Math.Max(1.0, previewHost.ActualWidth);
            var height = hasRectangle
                ? Math.Max(1.0, rectangle.Bottom - rectangle.Top)
                : Math.Max(1.0, previewHost.ActualHeight);
            var xRatio = Math.Max(0.0, Math.Min(1.0, x / width));
            var yRatio = Math.Max(0.0, Math.Min(1.0, y / height));
            NavigateRequested?.Invoke(xRatio, yRatio, immediate);
        }

        private void Pulse()
        {
            SetActiveAppearance(true);
            fadeTimer.Stop();
            fadeTimer.Start();
        }

        private void SetActiveAppearance(bool active)
        {
            previewWindow.Opacity = 1.0;
            inputWindow.Opacity = 1.0;
            if (previewFrame != null) previewFrame.Opacity = 1.0;
            mapShade.Opacity = 1.0;
            var baseColor = LiquidGlassTheme.FallbackWindowColor;
            var surfaceAlpha = thumbnailAvailable
                ? (active ? (byte)36 : (byte)68)
                : (byte)150;
            mapShade.Background = new SolidColorBrush(Color.FromArgb(
                LiquidGlassTheme.ScaleGlassAlpha(surfaceAlpha),
                baseColor.R,
                baseColor.G,
                baseColor.B));
            var indicator = LiquidGlassTheme.AccentColor;
            viewportIndicator.BorderBrush = new SolidColorBrush(Color.FromArgb(
                active ? (byte)225 : (byte)112,
                indicator.R,
                indicator.G,
                indicator.B));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            refreshTimer.Stop();
            fadeTimer.Stop();
            previewHost.Dispose();
            inputWindow.Close();
            previewWindow.Close();
        }
    }
}
