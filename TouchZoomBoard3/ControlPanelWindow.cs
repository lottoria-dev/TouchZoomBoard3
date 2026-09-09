using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class ControlPanelWindow : Window
    {
        private enum PanelIcon
        {
            Settings, Hide, Pointer, Hand, ZoomIn, ZoomOut, Reset, Power,
            Pen, Highlighter, Eraser, Rectangle, Ellipse, Line, Arrow,
            Shapes, Undo, Clear, ChevronLeft, ChevronRight, MoveGrip, TouchZoom, MiniMap
        }

        private const double PanelWidthDip = 182.0;
        private const double CompactPanelWidthDip = 64.0;
        private const double ScreenMarginDip = 12.0;
        private const double ToolButtonSizeDip = 34.0;
        private const double ToolSlotWidthDip = 38.0;
        private const double ToolOptionHeightDip = 10.0;
        private const double CarouselHeightDip = 46.0;
        // 적외선 전자칠판은 짧은 탭에도 좌표가 수 DIP씩 흔들릴 수 있다.
        // 일반 터치 버튼과 같은 허용치를 사용해 탭을 드래그로 오판하지 않는다.
        private const double LongPressMovementToleranceDip = 12.0;
        private static readonly DependencyProperty GlassPopupButtonProperty =
            DependencyProperty.RegisterAttached(
                "GlassPopupButton",
                typeof(bool),
                typeof(ControlPanelWindow),
                new PropertyMetadata(false));
        private readonly Dictionary<AppMode, List<Button>> modeButtons = new Dictionary<AppMode, List<Button>>();
        private readonly List<UIElement> carouselPages = new List<UIElement>();
        private readonly Dictionary<DrawingStyleKind, Button> colorSelectorButtons = new Dictionary<DrawingStyleKind, Button>();
        private readonly Dictionary<Button, ToolTip> helpToolTips = new Dictionary<Button, ToolTip>();
        private readonly HashSet<Button> touchSuppressedToolTips = new HashSet<Button>();
        private readonly Dictionary<DependencyObject, IntPtr> popupWindowHandles = new Dictionary<DependencyObject, IntPtr>();
        private readonly Dictionary<AppMode, Button> shapePopupButtons = new Dictionary<AppMode, Button>();
        private readonly Dictionary<double, Button> penThicknessMenuItems = new Dictionary<double, Button>();
        private readonly Dictionary<double, Button> highlighterThicknessMenuItems = new Dictionary<double, Button>();
        private readonly Dictionary<double, Button> shapeThicknessMenuItems = new Dictionary<double, Button>();
        private readonly Dictionary<double, Button> zoomMenuItems = new Dictionary<double, Button>();
        private readonly List<ToolTip> themedToolTips = new List<ToolTip>();
        private readonly List<Popup> themedPopups = new List<Popup>();
        private readonly List<ContextMenu> themedContextMenus = new List<ContextMenu>();
        private TextBlock zoomLabel;
        private Button zoomButton;
        private FrameworkElement expandedPanelRoot;
        private FrameworkElement compactPanelRoot;
        private Border expandedDragHandle;
        private Border compactDragHandle;
        private Grid carouselHost;
        private Button shapeButton;
        private Popup activeColorPalette;
        private Popup activeShapePalette;
        private Popup activeOptionPopup;
        private string activeOptionPopupKind;
        private FrameworkElement panelVisualRoot;
        private Forms.Screen currentScreen;
        private HwndSource windowSource;
        private bool tooltipsEnabled;
        private IntPtr windowHandle;
        private bool allowClose;
        private bool zoomSafeMoveEnabled;
        private bool zoomSafeMoveActive;
        private bool moveCompositionWarningLogged;
        private FrameworkElement zoomSafeMoveSource;
        private TouchDevice zoomSafeMoveTouchDevice;
        private Point zoomSafeMoveStartScreen;
        private NativeMethods.RECT zoomSafeMoveStartRectangle;
        private readonly uint currentProcessId;
        private IntPtr lastExternalForegroundWindow;
        private bool focusRestoreQueued;
        private double glassTintOpacity;
        private int carouselPageIndex;
        private DateTime lastSettingsRequestUtc = DateTime.MinValue;
        private DateTime lastBoardToolTipSuppressionUtc = DateTime.MinValue;

        internal event Action PanelMoved;
        internal event Action PanelBoundsChanged;
        internal event Action SettingsRequested;
        internal event Action QuitRequested;
        internal event Action<AppMode> ModeRequested;
        internal event Action<DrawingStyleKind, Color> DrawingColorRequested;
        internal event Action<double> PenWidthRequested;
        internal event Action<double> HighlighterWidthRequested;
        internal event Action<double> ShapeWidthRequested;
        internal event Action<double> ZoomLevelRequested;
        internal event Action QuickZoomRequested;
        internal event Action ResetZoomRequested;
        internal event Action MiniMapDockRequested;
        internal event Action<double> WheelZoomRequested;
        internal event Action UndoRequested;
        internal event Action ClearRequested;
        internal event Action EndSessionRequested;
        internal event Action HidePanelRequested;
        internal event Action MagnificationExclusionsChanged;
        internal event Action NativeTouchObserved;
        internal event Action WpfTouchObserved;
        internal event Action DeviceChangeObserved;
        internal event Action<IntPtr, string> FocusRestoreRequested;
        internal event Action<string> FocusStateChanged;

        internal IntPtr WindowHandle => windowHandle;
        internal bool IsCompactMode => compactPanelRoot?.Visibility == Visibility.Visible;

        internal ControlPanelWindow(bool initialTooltipsEnabled)
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                currentProcessId = (uint)process.Id;
            }
            Title = "TouchZoomBoard3";
            Width = PanelWidthDip;
            SizeToContent = System.Windows.SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            // 적외선 전자칠판의 터치 장치는 활성 창에서만 WPF Touch/Stylus
            // 이벤트로 승격되는 경우가 있다. 패널 자체는 활성화를 허용한다.
            ShowActivated = true;
            Focusable = true;
            Topmost = true;
            // DWM의 투명 그라데이션은 Windows 11에서 알파 0이어도 옅은 판을 남길 수
            // 있다. 작은 패널은 픽셀 단위 투명 WPF 창으로 만들어 실제 0%를 보장한다.
            AllowsTransparency = true;
            // 패널 HWND가 만들어지는 첫 프레임부터 불투명 사각형이 번쩍이지 않도록 한다.
            Background = Brushes.Transparent;
            DisableWindowsPressAndHold(this);

            var root = new Grid();
            panelVisualRoot = root;
            DisableWindowsPressAndHold(root);
            expandedPanelRoot = CreateExpandedPanel();
            compactPanelRoot = CreateCompactPanel();
            root.Children.Add(expandedPanelRoot);
            root.Children.Add(compactPanelRoot);
            Content = root;

            SourceInitialized += (sender, args) =>
            {
                windowHandle = new WindowInteropHelper(this).Handle;
                NativeMethods.EnsureToolWindowStyle(windowHandle);
                windowSource = HwndSource.FromHwnd(windowHandle);
                windowSource?.AddHook(WindowProcedure);
                LiquidGlassTheme.ApplyWindow(this, windowSource, windowHandle, "control-panel");
                ApplyWindowRegion();
                DebugLog.WriteDiagnostic("PANEL", "일반 HWND 생성 handle=0x" +
                    windowHandle.ToInt64().ToString("X") +
                    ", AllowsTransparency=" + AllowsTransparency +
                    ", Topmost=" + Topmost);
            };
            PreviewTouchDown += (sender, args) =>
            {
                SuppressAllToolTipsForBoardInput();
                WpfTouchObserved?.Invoke();
                DebugLog.WriteDiagnostic("PANEL-WPF", "PreviewTouchDown id=" + args.TouchDevice.Id +
                    ", activeBefore=" + IsActive + ", source=" + DescribeInputSource(args.OriginalSource));
                if (!IsActive)
                {
                    Activate();
                    Focus();
                }
            };
            PreviewTouchUp += (sender, args) =>
            {
                SuppressAllToolTipsForBoardInput();
                DebugLog.WriteDiagnostic("PANEL-WPF",
                    "PreviewTouchUp id=" + args.TouchDevice.Id +
                    ", active=" + IsActive + ", source=" + DescribeInputSource(args.OriginalSource));
                if (!zoomSafeMoveActive) QueueExternalFocusRestore("PreviewTouchUp");
            };
            PreviewStylusDown += (sender, args) =>
            {
                SuppressAllToolTipsForBoardInput();
                WpfTouchObserved?.Invoke();
                DebugLog.WriteDiagnostic("PANEL-WPF",
                    "PreviewStylusDown device=" + DescribeStylus(args) +
                    ", source=" + DescribeInputSource(args.OriginalSource));
            };
            PreviewStylusUp += (sender, args) =>
            {
                SuppressAllToolTipsForBoardInput();
                DebugLog.WriteDiagnostic("PANEL-WPF",
                    "PreviewStylusUp device=" + DescribeStylus(args) +
                    ", source=" + DescribeInputSource(args.OriginalSource));
                QueueExternalFocusRestore("PreviewStylusUp");
            };
            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    EndSessionRequested?.Invoke();
                    args.Handled = true;
                }
            };
            PreviewMouseRightButtonUp += (sender, args) =>
            {
                OpenPanelContextMenu();
                args.Handled = true;
            };
            PreviewMouseLeftButtonDown += (sender, args) => CloseTouchToolTip();
            PreviewMouseLeftButtonUp += (sender, args) =>
            {
                if (!zoomSafeMoveActive) QueueExternalFocusRestore("PreviewMouseLeftButtonUp");
            };
            PreviewMouseWheel += (sender, args) =>
            {
                WheelZoomRequested?.Invoke(args.Delta > 0 ? 1.12 : 1.0 / 1.12);
                args.Handled = true;
            };
            Closing += (sender, args) =>
            {
                if (!allowClose)
                {
                    args.Cancel = true;
                    HidePanelRequested?.Invoke();
                }
            };
            IsVisibleChanged += (sender, args) =>
            {
                if (!IsVisible) CloseActiveToolPopups();
            };
            Deactivated += (sender, args) => CloseTouchToolTip();
            SizeChanged += (sender, args) =>
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(ApplyWindowRegion));

            ShowCarouselPage(0);
            SetMode(AppMode.Pointer);
            SetZoom(1.0);
            SetTooltipsEnabled(initialTooltipsEnabled);
            SetCompactMode(true);
        }

        private FrameworkElement CreateExpandedPanel()
        {
            var body = new StackPanel();
            body.Children.Add(CreateDragHeader());
            var carousel = CreateCarousel();
            carousel.Margin = new Thickness(0, 5, 0, 0);
            body.Children.Add(carousel);
            return CreateGlassContainer(body, new CornerRadius(22), new Thickness(7, 6, 7, 9));
        }

        private FrameworkElement CreateCompactPanel()
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(17) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(41) });

            compactDragHandle = new Border
            {
                Width = 17,
                Height = 42,
                Background = LiquidGlassTheme.CreateButtonBrush(false),
                CornerRadius = new CornerRadius(20, 3, 3, 20),
                Cursor = Cursors.SizeAll,
                Child = CreateIcon(PanelIcon.MoveGrip, CreateSilverIconBrush(), 12)
            };
            DisableWindowsPressAndHold(compactDragHandle);
            AttachZoomSafeMove(compactDragHandle);
            Grid.SetColumn(compactDragHandle, 0);
            grid.Children.Add(compactDragHandle);

            var launcherButton = new Button
            {
                Tag = "Touch Zoom 런처",
                Width = 40,
                Height = 42,
                Margin = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(4),
                Background = CreateLensButtonBrush(),
                BorderBrush = LiquidGlassTheme.CreateButtonRefractionBrush(false),
                BorderThickness = new Thickness(0.75),
                Cursor = Cursors.Hand,
                Template = CreateRaisedButtonTemplate(new CornerRadius(3, 20, 20, 3)),
                Content = CreateIcon(PanelIcon.TouchZoom, CreateSilverIconBrush(), 21),
                Effect = null
            };
            DisableWindowsPressAndHold(launcherButton);
            AttachHelpToolTip(launcherButton, "Touch Zoom", "짧게 누르면 도구 패널이 열리고, 길게 누르면 설정이 열립니다.", false);
            AttachIdentityGesture(launcherButton, () => SetCompactMode(false));
            Grid.SetColumn(launcherButton, 1);
            grid.Children.Add(launcherButton);

            return CreateGlassContainer(grid, new CornerRadius(21), new Thickness(2));
        }

        private static Grid CreateGlassContainer(UIElement child, CornerRadius radius, Thickness padding)
        {
            var layers = new Grid { Background = null };
            layers.Children.Add(new Border
            {
                CornerRadius = radius,
                Background = CreateGlassBrush(),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsHitTestVisible = false
            });
            // 선명한 테두리 대신 1px보다 가는 반투명 림으로 패널 경계만 부드럽게 잡는다.
            // 비정수 두께를 유지해 고배율 DPI에서도 단단한 흰 선처럼 보이지 않게 한다.
            layers.Children.Add(new Border
            {
                CornerRadius = radius,
                Background = Brushes.Transparent,
                BorderBrush = LiquidGlassTheme.CreatePanelRefractionHighlightBrush(),
                BorderThickness = new Thickness(0.62),
                SnapsToDevicePixels = false,
                IsHitTestVisible = false
            });
            // 안쪽 입사광과 반대쪽 음영은 서로 분리되지 않을 정도로 얇게 겹친다.
            layers.Children.Add(new Border
            {
                Margin = new Thickness(0.82),
                CornerRadius = radius,
                Background = Brushes.Transparent,
                BorderBrush = LiquidGlassTheme.CreatePanelInnerLightBrush(),
                BorderThickness = new Thickness(0.32, 0.32, 0, 0),
                SnapsToDevicePixels = false,
                IsHitTestVisible = false
            });
            layers.Children.Add(new Border
            {
                Margin = new Thickness(0.82),
                CornerRadius = radius,
                Background = Brushes.Transparent,
                BorderBrush = LiquidGlassTheme.CreatePanelRefractionShadeBrush(),
                BorderThickness = new Thickness(0, 0, 0.32, 0.36),
                SnapsToDevicePixels = false,
                IsHitTestVisible = false
            });
            // 국부 반사광은 테두리와 분리된 선이 되지 않도록 짧고 옅게 유지한다.
            layers.Children.Add(new Border
            {
                Height = 0.78,
                Margin = new Thickness(17, 0.7, 36, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(0.39),
                Background = LiquidGlassTheme.CreatePanelTopGlintBrush(),
                SnapsToDevicePixels = false,
                IsHitTestVisible = false
            });
            layers.Children.Add(new Border
            {
                Width = 0.78,
                Margin = new Thickness(0.7, 17, 0, 32),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = new CornerRadius(0.39),
                Background = LiquidGlassTheme.CreatePanelSideGlintBrush(),
                SnapsToDevicePixels = false,
                IsHitTestVisible = false
            });
            layers.Children.Add(new Border
            {
                Child = child,
                Background = null,
                BorderBrush = null,
                BorderThickness = new Thickness(0),
                Padding = padding
            });
            return layers;
        }

        internal void ShowForScreen(Forms.Screen screen, double xRatio, double yRatio)
        {
            CaptureExternalForeground("ShowForScreen");
            currentScreen = screen ?? Forms.Screen.PrimaryScreen;
            if (!IsVisible) Show();
            if (windowHandle == IntPtr.Zero) windowHandle = new WindowInteropHelper(this).Handle;

            UpdateLayout();
            var bounds = currentScreen.WorkingArea;
            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var width = Math.Max(1, (int)Math.Round(ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Round(ActualHeight * scale));
            var margin = Math.Max(4, (int)Math.Round(ScreenMarginDip * scale));
            var availableWidth = Math.Max(0, bounds.Width - width - margin * 2);
            var availableHeight = Math.Max(0, bounds.Height - height - margin * 2);
            var left = bounds.Left + margin + (int)Math.Round(ClampRatio(xRatio) * availableWidth);
            var top = bounds.Top + margin + (int)Math.Round(ClampRatio(yRatio) * availableHeight);
            NativeMethods.PositionTopmostWindow(windowHandle, left, top, width, height, true);
            Activate();
            Focus();
            QueueExternalFocusRestore("ShowForScreen");
            ApplyWindowRegion();
            UpdateToolTipPlacement(left + width / 2, bounds);
            PanelBoundsChanged?.Invoke();
        }

        internal void SetCompactMode(bool compact)
        {
            CloseTouchToolTip();
            CloseActiveToolPopups();
            expandedPanelRoot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            compactPanelRoot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            Width = compact ? CompactPanelWidthDip : PanelWidthDip;
            if (!IsVisible) return;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ResizeAtCurrentPosition));
        }

        private void ResizeAtCurrentPosition()
        {
            if (!IsVisible || windowHandle == IntPtr.Zero ||
                !NativeMethods.GetWindowRect(windowHandle, out var rectangle)) return;

            UpdateLayout();
            var target = currentScreen ?? Forms.Screen.FromHandle(windowHandle) ?? Forms.Screen.PrimaryScreen;
            var bounds = target.WorkingArea;
            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var width = Math.Max(1, (int)Math.Round(ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Round(ActualHeight * scale));
            var anchoredToRight = rectangle.Left + (rectangle.Right - rectangle.Left) / 2 > bounds.Left + bounds.Width / 2;
            var left = anchoredToRight ? rectangle.Right - width : rectangle.Left;
            NativeMethods.PositionTopmostWindow(windowHandle, left, rectangle.Top, width, height, false);
            ClampToCurrentScreen();
        }

        internal void BringToFrontWithoutActivate()
        {
            if (windowHandle == IntPtr.Zero || !NativeMethods.GetWindowRect(windowHandle, out var rectangle)) return;
            NativeMethods.PositionTopmostWindow(windowHandle, rectangle.Left, rectangle.Top,
                rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top, false);
        }

        internal void SetOwnerWindow(IntPtr owner) { NativeMethods.SetOwnerWindow(windowHandle, owner); }

        private void CaptureExternalForeground(string reason)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            uint processId;
            if (foreground == IntPtr.Zero || foreground == windowHandle ||
                NativeMethods.GetWindowThreadProcessId(foreground, out processId) == 0 ||
                processId == currentProcessId)
            {
                return;
            }

            lastExternalForegroundWindow = foreground;
            DebugLog.WriteDiagnostic("FOCUS", "captured reason=" + reason +
                ", target=0x" + foreground.ToInt64().ToString("X") +
                ", pid=" + processId +
                ", class=" + NativeMethods.GetWindowClassName(foreground));
        }

        private void QueueExternalFocusRestore(string reason)
        {
            if (focusRestoreQueued || lastExternalForegroundWindow == IntPtr.Zero) return;
            focusRestoreQueued = true;
            var target = lastExternalForegroundWindow;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                focusRestoreQueued = false;
                FocusRestoreRequested?.Invoke(target, reason);
            }));
        }

        internal void ClampToCurrentScreen()
        {
            if (currentScreen == null || windowHandle == IntPtr.Zero ||
                !NativeMethods.GetWindowRect(windowHandle, out var rectangle)) return;

            var bounds = currentScreen.WorkingArea;
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var margin = Math.Max(4, (int)Math.Round(ScreenMarginDip * scale));
            var minimumLeft = bounds.Left + margin;
            var maximumLeft = Math.Max(minimumLeft, bounds.Right - width - margin);
            var minimumTop = bounds.Top + margin;
            var maximumTop = Math.Max(minimumTop, bounds.Bottom - height - margin);
            var left = Math.Max(minimumLeft, Math.Min(maximumLeft, rectangle.Left));
            var top = Math.Max(minimumTop, Math.Min(maximumTop, rectangle.Top));
            NativeMethods.PositionTopmostWindow(windowHandle, left, top, width, height, false);
            UpdateToolTipPlacement(left + width / 2, bounds);
            PanelBoundsChanged?.Invoke();
        }

        internal void GetPositionRatios(Forms.Screen screen, out double xRatio, out double yRatio)
        {
            var target = screen ?? currentScreen ?? Forms.Screen.PrimaryScreen;
            var bounds = target.WorkingArea;
            xRatio = 1.0;
            yRatio = 0.08;
            if (windowHandle == IntPtr.Zero || !NativeMethods.GetWindowRect(windowHandle, out var rectangle)) return;

            var scale = NativeMethods.GetScaleForPoint(bounds.Left + 1, bounds.Top + 1);
            var margin = Math.Max(4, (int)Math.Round(ScreenMarginDip * scale));
            var availableWidth = Math.Max(1, bounds.Width - (rectangle.Right - rectangle.Left) - margin * 2);
            var availableHeight = Math.Max(1, bounds.Height - (rectangle.Bottom - rectangle.Top) - margin * 2);
            xRatio = ClampRatio((rectangle.Left - bounds.Left - margin) / (double)availableWidth);
            yRatio = ClampRatio((rectangle.Top - bounds.Top - margin) / (double)availableHeight);
        }

        internal void SetMode(AppMode activeMode)
        {
            CloseActiveToolPopups();
            foreach (var button in modeButtons.Values.SelectMany(buttons => buttons).Distinct()) SetButtonActive(button, false);
            if (modeButtons.TryGetValue(activeMode, out var activeButtons))
            {
                foreach (var button in activeButtons) SetButtonActive(button, true);
            }
            foreach (var pair in shapePopupButtons) SetButtonActive(pair.Value, pair.Key == activeMode);
            if (IsShapeMode(activeMode) && shapeButton != null)
                shapeButton.Content = CreateDropDownIcon(CreateShapeIcon(activeMode, LiquidGlassTheme.IconBrush));
        }

        internal void SetZoom(double zoom)
        {
            zoomLabel.Text = string.Format("{0:0}%", zoom * 100.0);
            UpdateOptionButtonChecks(zoomMenuItems, zoom);
        }

        internal void SetScreenName(Forms.Screen screen)
        {
            currentScreen = screen ?? currentScreen;
        }

        internal void SetTooltipsEnabled(bool enabled)
        {
            tooltipsEnabled = enabled;
            foreach (var pair in helpToolTips)
                ToolTipService.SetIsEnabled(pair.Key, enabled && !touchSuppressedToolTips.Contains(pair.Key));
            if (!enabled) CloseTouchToolTip();
        }

        internal void SetGlassTintOpacity(double opacity)
        {
            glassTintOpacity = Math.Max(0.0,
                Math.Min(LiquidGlassTheme.MaximumPanelGlassStrength, opacity));
            // 유리 알파는 표면 브러시에만 반영한다. 글자·아이콘·터치 영역은
            // 항상 100%로 두어 전자칠판 입력과 가독성을 보존한다.
            Opacity = 1.0;
            if (panelVisualRoot != null) panelVisualRoot.Opacity = 1.0;
            foreach (var tip in themedToolTips) tip.Opacity = 1.0;
            foreach (var popup in themedPopups)
            {
                if (popup.Child != null) popup.Child.Opacity = 1.0;
            }
            foreach (var menu in themedContextMenus) menu.Opacity = 1.0;
            DebugLog.WriteDiagnostic("GLASS", "surface=control-panel, glassTintOpacity=" +
                glassTintOpacity.ToString("0.000") +
                ", panelGlassPercent=" + Math.Round(
                    glassTintOpacity / LiquidGlassTheme.MaximumPanelGlassStrength * 100.0).ToString("0") +
                ", panelSurfaceAlpha=" + LiquidGlassTheme.PanelSurfaceAlpha +
                ", panelHeaderAlpha=" + LiquidGlassTheme.PanelHeaderAlpha +
                ", rootUiOpacity=1.00, hitSurface=buttons-and-handles");
        }

        internal void EnsureToolWindowStyle()
        {
            NativeMethods.EnsureToolWindowStyle(windowHandle);
            ApplyWindowRegion();
        }

        internal void SetDrawingColor(DrawingStyleKind kind, Color color)
        {
            if (colorSelectorButtons.TryGetValue(kind, out var button))
            {
                button.Content = CreateColorSelectorContent(color, button.Width);
            }
        }

        internal void SetPenWidth(double width)
        {
            UpdateThicknessChecks(penThicknessMenuItems, width);
        }

        internal void SetHighlighterWidth(double width)
        {
            UpdateThicknessChecks(highlighterThicknessMenuItems, width);
        }

        internal void SetShapeWidth(double width)
        {
            UpdateThicknessChecks(shapeThicknessMenuItems, width);
        }

        internal void SetZoomSafeMoveEnabled(bool enabled)
        {
            if (zoomSafeMoveEnabled == enabled) return;
            if (!enabled && zoomSafeMoveActive) CompleteZoomSafeMove("mode-disabled");
            zoomSafeMoveEnabled = enabled;
            DebugLog.WriteDiagnostic("PANEL-MOVE", "zoomSafeMove=" + enabled);
        }

        internal void Shutdown()
        {
            allowClose = true;
            CloseActiveToolPopups();
            windowSource?.RemoveHook(WindowProcedure);
            Close();
        }

        internal IntPtr[] GetPopupWindowHandles()
        {
            return popupWindowHandles.Values.Where(handle => handle != IntPtr.Zero).Distinct().ToArray();
        }

        private FrameworkElement CreateCarousel()
        {
            var root = new Grid
            {
                Width = 26 + ToolSlotWidthDip * 3 + 26,
                Height = CarouselHeightDip,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ToolSlotWidthDip * 3) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            var previous = CreateCarouselArrowButton(PanelIcon.ChevronLeft, "이전 메뉴", -1);
            Grid.SetColumn(previous, 0);
            root.Children.Add(previous);

            carouselHost = new Grid { Width = ToolSlotWidthDip * 3, Height = CarouselHeightDip };
            carouselPages.Add(CreateInkCarouselPage());
            carouselPages.Add(CreateEraseCarouselPage());
            carouselPages.Add(CreateControlCarouselPage());
            foreach (var page in carouselPages) carouselHost.Children.Add(page);
            Grid.SetColumn(carouselHost, 1);
            root.Children.Add(carouselHost);

            var next = CreateCarouselArrowButton(PanelIcon.ChevronRight, "다음 메뉴", 1);
            Grid.SetColumn(next, 2);
            root.Children.Add(next);
            return root;
        }

        private UIElement CreateInkCarouselPage()
        {
            var row = CreateCarouselPageRow();
            row.Children.Add(CreateToolSlot(
                CreatePenModeButton(),
                CreateColorSelectorButton(DrawingStyleKind.Pen, "펜 색상", "현재 펜 색상입니다. 누르면 16색 팔레트가 열립니다.", ToolButtonSizeDip)));
            row.Children.Add(CreateToolSlot(
                CreateHighlighterModeButton(),
                CreateColorSelectorButton(DrawingStyleKind.Highlighter, "형광펜 색상", "현재 형광펜 색상입니다. 누르면 16색 팔레트가 열립니다.", ToolButtonSizeDip)));
            row.Children.Add(CreateToolSlot(
                CreateShapeDropDownButton(),
                CreateColorSelectorButton(DrawingStyleKind.Shape, "도형 색상", "현재 도형 색상입니다. 누르면 16색 팔레트가 열립니다.", ToolButtonSizeDip)));
            return row;
        }

        private UIElement CreateEraseCarouselPage()
        {
            var row = CreateCarouselPageRow();
            row.Children.Add(CreateToolSlot(CreateModeButton(PanelIcon.Eraser, "지우개", "지우려는 필기 선이나 도형을 터치합니다.", AppMode.Eraser, ToolButtonSizeDip)));
            row.Children.Add(CreateToolSlot(CreateIconButton(PanelIcon.Undo, "되돌리기", "마지막으로 추가한 필기 또는 도형을 취소합니다.", (s, e) => UndoRequested?.Invoke(), false, ToolButtonSizeDip)));
            row.Children.Add(CreateToolSlot(CreateIconButton(PanelIcon.Clear, "전체 지움", "현재 화면의 필기와 도형을 모두 지웁니다.", (s, e) => ClearRequested?.Invoke(), false, ToolButtonSizeDip)));
            return row;
        }

        private UIElement CreateControlCarouselPage()
        {
            var row = CreateCarouselPageRow();
            row.Children.Add(CreateToolSlot(CreateModeButton(PanelIcon.Pointer, "자료 조작", "판서 표시와 확대 상태를 유지한 채 브라우저·프레젠테이션을 직접 조작합니다. 일부 윈도우 요소는 조작에 제한이 있습니다.", AppMode.Pointer, ToolButtonSizeDip)));
            row.Children.Add(CreateToolSlot(CreateIconButton(PanelIcon.MiniMap, "미니맵 위치", "미니맵을 패널의 위쪽과 아래쪽 사이에서 전환합니다. 미니맵을 직접 누르거나 끌면 확대 영역이 이동합니다.", (s, e) => MiniMapDockRequested?.Invoke(), false, ToolButtonSizeDip)));
            row.Children.Add(CreateToolSlot(CreateIconButton(PanelIcon.Power, "수업 화면 종료", "확대와 필기를 모두 끝내고 정상 화면으로 돌아갑니다. 긴급 복구: Ctrl+Alt+Shift+Esc", (s, e) => EndSessionRequested?.Invoke(), false, ToolButtonSizeDip)));
            return row;
        }

        private Button CreateCarouselArrowButton(PanelIcon icon, string title, int direction)
        {
            var button = CreateIconButton(icon, title, "세 개씩 묶인 도구 메뉴를 순환합니다.", (s, e) => ShowCarouselPage(carouselPageIndex + direction), false, 24);
            button.Background = LiquidGlassTheme.CreateButtonBrush(false);
            button.BorderBrush = LiquidGlassTheme.CreateButtonRefractionBrush(false);
            button.BorderThickness = new Thickness(0.75);
            button.Template = CreateRaisedButtonTemplate(new CornerRadius(12));
            return button;
        }

        private static StackPanel CreateCarouselPageRow()
        {
            return new StackPanel
            {
                Width = ToolSlotWidthDip * 3,
                Height = CarouselHeightDip,
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        private static StackPanel CreateToolSlot(Button mainButton, Button optionButton = null)
        {
            var slot = new StackPanel
            {
                Width = ToolSlotWidthDip,
                Height = CarouselHeightDip,
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
            slot.Children.Add(mainButton);
            UIElement option = optionButton != null
                ? (UIElement)optionButton
                : new Border { Height = ToolOptionHeightDip };
            slot.Children.Add(option);
            return slot;
        }

        private void ShowCarouselPage(int requestedIndex)
        {
            if (carouselPages.Count == 0) return;
            CloseActiveToolPopups();
            carouselPageIndex = (requestedIndex % carouselPages.Count + carouselPages.Count) % carouselPages.Count;
            for (var index = 0; index < carouselPages.Count; index++)
                carouselPages[index].Visibility = index == carouselPageIndex ? Visibility.Visible : Visibility.Collapsed;
            Dispatcher.BeginInvoke(new Action(ClampToCurrentScreen));
        }

        private Border CreateDragHeader()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            expandedDragHandle = new Border
            {
                Width = 28,
                Height = 18,
                Background = LiquidGlassTheme.CreateButtonBrush(false),
                CornerRadius = new CornerRadius(9),
                Cursor = Cursors.SizeAll,
                Child = CreateIcon(PanelIcon.MoveGrip, CreateSilverIconBrush(), 13)
            };
            DisableWindowsPressAndHold(expandedDragHandle);
            AttachZoomSafeMove(expandedDragHandle);
            Grid.SetColumn(expandedDragHandle, 0);
            grid.Children.Add(expandedDragHandle);

            var identityButton = new Button
            {
                Tag = "패널 접기",
                Width = 30,
                Height = 18,
                Padding = new Thickness(2),
                Background = LiquidGlassTheme.CreateButtonBrush(false),
                BorderBrush = LiquidGlassTheme.CreateButtonRefractionBrush(false),
                BorderThickness = new Thickness(0.55),
                Cursor = Cursors.Hand,
                Template = CreateRaisedButtonTemplate(new CornerRadius(9)),
                Content = CreateIcon(PanelIcon.TouchZoom, CreateSilverIconBrush(), 14)
            };
            DisableWindowsPressAndHold(identityButton);
            AttachHelpToolTip(identityButton, "패널 접기", "짧게 누르면 작은 터치 패널로 접히고, 길게 누르면 설정이 열립니다.", false);
            AttachIdentityGesture(identityButton, () => SetCompactMode(true));
            Grid.SetColumn(identityButton, 1);
            grid.Children.Add(identityButton);
            zoomLabel = new TextBlock
            {
                Text = "100%", FontSize = 10, FontWeight = FontWeights.Normal,
                Foreground = CreateSilverIconBrush(), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            zoomButton = new Button
            {
                Tag = "화면 배율",
                Width = 48,
                Height = 18,
                Padding = new Thickness(2, 0, 2, 0),
                Background = LiquidGlassTheme.CreateButtonBrush(false),
                BorderBrush = LiquidGlassTheme.CreateButtonRefractionBrush(false),
                BorderThickness = new Thickness(0.55),
                Cursor = Cursors.Hand,
                Template = CreateRaisedButtonTemplate(new CornerRadius(9)),
                Content = CreateZoomButtonContent()
            };
            DisableWindowsPressAndHold(zoomButton);
            AttachHelpToolTip(zoomButton, "빠른 화면 확대", "짧게 누르면 기본 배율로 확대하거나 100%로 복귀합니다. 길게 누르면 배율 목록이 열립니다.", false);
            var zoomMenu = CreateZoomPopup();
            AttachLongPressPopup(zoomButton, zoomMenu, () =>
            {
                CloseTouchToolTip();
                DebugLog.WriteDiagnostic("PANEL-ZOOM", "배율 버튼 짧게 누름; QuickZoomRequested");
                QuickZoomRequested?.Invoke();
            }, "zoom");
            Grid.SetColumn(zoomButton, 2);
            grid.Children.Add(zoomButton);
            return new Border
            {
                Child = grid,
                Background = LiquidGlassTheme.CreatePanelHeaderBrush(),
                BorderBrush = LiquidGlassTheme.CreatePanelGroupBorderBrush(),
                BorderThickness = new Thickness(0.8), CornerRadius = new CornerRadius(15),
                Padding = new Thickness(5, 3, 7, 3)
            };
        }

        private UIElement CreateZoomButtonContent()
        {
            var content = new Grid { Width = 42, Height = 14 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            Grid.SetColumn(zoomLabel, 0);
            content.Children.Add(zoomLabel);
            var arrow = new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(5, 0), new Point(2.5, 3) },
                Fill = LiquidGlassTheme.IconBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(arrow, 1);
            content.Children.Add(arrow);
            return content;
        }

        private Popup CreateZoomPopup()
        {
            var popup = CreatePersistentOptionPopup();
            var stack = new StackPanel { Margin = new Thickness(4) };
            AddZoomPopupButton(stack, popup, 1.0, "100%  ·  확대 종료");
            stack.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(4, 3, 4, 3),
                Background = LiquidGlassTheme.CreatePopupSeparatorBrush()
            });
            AddZoomPopupButton(stack, popup, 1.25, "125%");
            AddZoomPopupButton(stack, popup, 1.5, "150%");
            AddZoomPopupButton(stack, popup, 1.75, "175%");
            AddZoomPopupButton(stack, popup, 2.0, "200%");
            AddZoomPopupButton(stack, popup, 2.5, "250%");
            AddZoomPopupButton(stack, popup, 3.0, "300%");
            AddZoomPopupButton(stack, popup, 4.0, "400%");
            AddZoomPopupButton(stack, popup, 5.0, "500%");
            popup.Child = CreateGlassPopupSurface(stack);
            popup.Closed += (sender, args) => DebugLog.WriteDiagnostic("PANEL-ZOOM", "popupClosed");
            AttachPopupTracking(popup);
            return popup;
        }

        private void AddZoomPopupButton(Panel panel, Popup popup, double zoom, string title)
        {
            var button = CreatePersistentOptionButton(title, 124, 30);
            button.Tag = "배율 " + title;
            Action activate = () =>
            {
                popup.IsOpen = false;
                ClearActiveOptionPopup(popup);
                if (Math.Abs(zoom - 1.0) < 0.01)
                    ResetZoomRequested?.Invoke();
                else
                    ZoomLevelRequested?.Invoke(zoom);
                DebugLog.WriteDiagnostic("PANEL-ZOOM", "selected=" + zoom.ToString("0.##"));
            };
            var activateOnce = CreateSingleActivation(activate, DescribeInputSource(button));
            button.Click += (sender, args) => activateOnce();
            AttachDirectTouchActivation(button, activateOnce);
            zoomMenuItems[zoom] = button;
            panel.Children.Add(button);
        }

        private static Popup CreatePersistentOptionPopup()
        {
            return new Popup
            {
                AllowsTransparency = false,
                // 전자칠판의 TouchUp 뒤 프레젠터로 포커스가 복원되어도 선택창을 유지한다.
                StaysOpen = true,
                PopupAnimation = PopupAnimation.None
            };
        }

        private static Border CreateGlassPopupSurface(UIElement child)
        {
            var layers = new Grid();
            layers.Children.Add(new Border
            {
                Height = 22,
                VerticalAlignment = VerticalAlignment.Top,
                Background = CreatePopupHighlightBrush(),
                CornerRadius = new CornerRadius(17, 17, 7, 7),
                IsHitTestVisible = false
            });
            layers.Children.Add(child);

            return new Border
            {
                Child = layers,
                Opacity = 1.0,
                Background = CreatePopupGlassBrush(),
                BorderBrush = CreatePopupBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(3),
                SnapsToDevicePixels = true,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 4,
                    Direction = 270,
                    Opacity = 0.08,
                    Color = Colors.Black
                }
            };
        }

        private static Button CreatePersistentOptionButton(string title, double width, double height)
        {
            var button = new Button
            {
                Content = title,
                Width = width,
                Height = height,
                Margin = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Background = CreatePopupItemBrush(),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Template = CreateGlassPopupButtonTemplate(new CornerRadius(height / 2.0))
            };
            button.SetValue(GlassPopupButtonProperty, true);
            DisableWindowsPressAndHold(button);
            return button;
        }

        private void AttachIdentityGesture(Button target, Action shortAction)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            var pressActive = false;
            var longPressTriggered = false;
            var pressOrigin = new Point();
            TouchDevice activeTouchDevice = null;
            var invokeShortOnce = CreateSingleActivation(shortAction, DescribeInputSource(target));

            Action<Point> beginPress = point =>
            {
                pressOrigin = point;
                pressActive = true;
                longPressTriggered = false;
                timer.Stop();
                timer.Start();
            };
            Action<Point> updatePress = point =>
            {
                if (!pressActive || !MovedBeyondThreshold(
                        pressOrigin, point, LongPressMovementToleranceDip)) return;
                pressActive = false;
                timer.Stop();
            };
            Action endPress = () =>
            {
                pressActive = false;
                timer.Stop();
                if (longPressTriggered)
                    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => longPressTriggered = false));
            };

            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                if (!pressActive) return;
                pressActive = false;
                longPressTriggered = true;
                DebugLog.WriteDiagnostic("PANEL-LONG", DescribeInputSource(target) + " 길게 누르기 실행");
                target.ReleaseAllTouchCaptures();
                if (target.IsMouseCaptured) target.ReleaseMouseCapture();
                CloseTouchToolTip();
                RequestSettings();
            };
            target.PreviewTouchDown += (sender, args) =>
            {
                if (activeTouchDevice != null)
                {
                    args.Handled = true;
                    return;
                }

                activeTouchDevice = args.TouchDevice;
                beginPress(args.GetTouchPoint(target).Position);
                var captured = target.CaptureTouch(activeTouchDevice);
                DebugLog.WriteDiagnostic("PANEL-LONG", DescribeInputSource(target) +
                    " TouchDown id=" + activeTouchDevice.Id + ", capture=" + captured);
                args.Handled = true;
            };
            target.PreviewTouchMove += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;
                updatePress(args.GetTouchPoint(target).Position);
                args.Handled = true;
            };
            target.PreviewTouchUp += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;

                var invokeShortAction = pressActive && !longPressTriggered;
                activeTouchDevice = null;
                endPress();
                target.ReleaseAllTouchCaptures();
                args.Handled = true;
                DebugLog.WriteDiagnostic("PANEL-LONG", DescribeInputSource(target) +
                    " TouchUp short=" + invokeShortAction);
                if (invokeShortAction) invokeShortOnce();
            };
            target.LostTouchCapture += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;
                activeTouchDevice = null;
                endPress();
            };
            target.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (args.StylusDevice == null)
                {
                    beginPress(args.GetPosition(target));
                }
            };
            target.PreviewMouseMove += (sender, args) =>
            {
                if (args.StylusDevice == null && args.LeftButton == MouseButtonState.Pressed)
                    updatePress(args.GetPosition(target));
            };
            target.PreviewMouseLeftButtonUp += (sender, args) =>
            {
                if (args.StylusDevice == null)
                {
                    endPress();
                }
            };
            target.Click += (sender, args) =>
            {
                if (longPressTriggered)
                {
                    longPressTriggered = false;
                    return;
                }
                invokeShortOnce();
            };
        }

        private void RequestSettings()
        {
            var now = DateTime.UtcNow;
            if ((now - lastSettingsRequestUtc).TotalMilliseconds < 900) return;
            lastSettingsRequestUtc = now;
            CloseActiveToolPopups();
            SettingsRequested?.Invoke();
        }

        private void OpenPanelContextMenu()
        {
            CloseTouchToolTip();
            CloseActiveToolPopups();

            var menu = CreatePopupMenu();
            var settingsItem = new MenuItem
            {
                Tag = "패널 우클릭 환경 설정",
                Header = "환경 설정",
                Padding = new Thickness(12, 6, 22, 6),
                Style = CreateGlassMenuItemStyle()
            };
            var quitItem = new MenuItem
            {
                Tag = "패널 우클릭 종료",
                Header = "TouchZoomBoard3 종료",
                Padding = new Thickness(12, 6, 22, 6),
                Style = CreateGlassMenuItemStyle()
            };

            var openSettingsOnce = CreateSingleActivation(RequestSettings, DescribeInputSource(settingsItem));
            settingsItem.Click += (sender, args) => openSettingsOnce();
            AttachDirectTouchActivation(settingsItem, () =>
            {
                openSettingsOnce();
                menu.IsOpen = false;
            });

            var quitOnce = CreateSingleActivation(() => QuitRequested?.Invoke(), DescribeInputSource(quitItem));
            quitItem.Click += (sender, args) => quitOnce();
            AttachDirectTouchActivation(quitItem, () =>
            {
                quitOnce();
                menu.IsOpen = false;
            });

            menu.Items.Add(settingsItem);
            menu.Items.Add(new Separator { Style = CreateGlassSeparatorStyle() });
            menu.Items.Add(quitItem);
            menu.PlacementTarget = panelVisualRoot;
            menu.Placement = PlacementMode.MousePoint;
            AttachPopupTracking(menu);
            menu.IsOpen = true;
        }


        private Button CreateModeButton(PanelIcon icon, string title, string tooltip, AppMode buttonMode, double size = 34)
        {
            var button = CreateIconButton(icon, title, tooltip, (s, e) => ModeRequested?.Invoke(buttonMode), false, size);
            AddModeButton(buttonMode, button);
            return button;
        }

        private void AddModeButton(AppMode mode, Button button)
        {
            if (!modeButtons.TryGetValue(mode, out var buttons))
            {
                buttons = new List<Button>();
                modeButtons[mode] = buttons;
            }
            if (!buttons.Contains(button)) buttons.Add(button);
        }

        private Button CreateShapeDropDownButton()
        {
            shapeButton = CreateIconButton(PanelIcon.Shapes, "모양", "짧게 누르면 모양을 선택하고, 길게 누르면 테두리 두께를 선택할 수 있습니다.", null, false, ToolButtonSizeDip);
            shapeButton.Content = CreateDropDownIcon(CreateIcon(PanelIcon.Shapes, CreateSilverIconBrush(), 15));
            var shapePopup = CreateShapeSelectorPopup();
            var thicknessMenu = CreateThicknessPopup(
                new[] { 2.0, 3.0, 4.0, 6.0, 8.0 },
                width => ShapeWidthRequested?.Invoke(width),
                shapeThicknessMenuItems,
                "Shape");
            AttachLongPressPopup(shapeButton, thicknessMenu, () =>
            {
                CloseTouchToolTip();
                var shouldOpen = !shapePopup.IsOpen;
                CloseActiveToolPopups();
                shapePopup.PlacementTarget = shapeButton;
                shapePopup.Placement = PlacementMode.Bottom;
                shapePopup.IsOpen = shouldOpen;
                activeShapePalette = shouldOpen ? shapePopup : null;
                DebugLog.WriteDiagnostic("PANEL-SHAPE", "selectorOpen=" + shapePopup.IsOpen);
            }, "thickness", 850);
            AddModeButton(AppMode.Rectangle, shapeButton);
            AddModeButton(AppMode.Ellipse, shapeButton);
            AddModeButton(AppMode.Line, shapeButton);
            AddModeButton(AppMode.Arrow, shapeButton);
            return shapeButton;
        }

        private Popup CreateShapeSelectorPopup()
        {
            var popup = CreatePersistentOptionPopup();
            var stack = new StackPanel { Margin = new Thickness(4) };
            AddShapeSelectorButton(stack, popup, AppMode.Rectangle, "사각형");
            AddShapeSelectorButton(stack, popup, AppMode.Ellipse, "타원");
            AddShapeSelectorButton(stack, popup, AppMode.Line, "선분");
            AddShapeSelectorButton(stack, popup, AppMode.Arrow, "화살표");
            popup.Child = CreateGlassPopupSurface(stack);
            AttachPopupTracking(popup);
            return popup;
        }

        private void AddShapeSelectorButton(Panel panel, Popup popup, AppMode shapeMode, string title)
        {
            var button = new Button
            {
                Tag = "모양 " + title,
                Content = CreateShapePopupContent(shapeMode, title),
                Width = 122,
                Height = 32,
                Margin = new Thickness(1),
                Padding = new Thickness(5, 3, 8, 3),
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Background = CreatePopupItemBrush(),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Template = CreateGlassPopupButtonTemplate(new CornerRadius(6))
            };
            button.SetValue(GlassPopupButtonProperty, true);
            DisableWindowsPressAndHold(button);
            Action activate = () =>
            {
                popup.IsOpen = false;
                if (activeShapePalette == popup) activeShapePalette = null;
                ModeRequested?.Invoke(shapeMode);
                DebugLog.WriteDiagnostic("PANEL-SHAPE", "selected=" + shapeMode);
            };
            var activateOnce = CreateSingleActivation(activate, DescribeInputSource(button));
            button.Click += (sender, args) => activateOnce();
            AttachDirectTouchActivation(button, activateOnce);
            shapePopupButtons[shapeMode] = button;
            panel.Children.Add(button);
        }

        private Button CreatePenModeButton()
        {
            var button = CreateIconButton(
                PanelIcon.Pen,
                "펜",
                "누르면 펜을 선택합니다. 길게 누르면 펜 굵기를 선택할 수 있습니다.",
                null,
                false,
                ToolButtonSizeDip);
            var menu = CreatePenThicknessPopup();
            AttachLongPressPopup(button, menu, () => ModeRequested?.Invoke(AppMode.Pen), "thickness");
            AddModeButton(AppMode.Pen, button);
            return button;
        }

        private Button CreateHighlighterModeButton()
        {
            var button = CreateIconButton(
                PanelIcon.Highlighter,
                "형광펜",
                "짧게 누르면 형광펜을 선택하고, 길게 누르면 두께를 선택할 수 있습니다.",
                null,
                false,
                ToolButtonSizeDip);
            var menu = CreateThicknessPopup(
                new[] { 8.0, 12.0, 18.0, 24.0, 32.0 },
                width => HighlighterWidthRequested?.Invoke(width),
                highlighterThicknessMenuItems,
                "Highlighter");
            AttachLongPressPopup(button, menu, () => ModeRequested?.Invoke(AppMode.Highlighter), "thickness");
            AddModeButton(AppMode.Highlighter, button);
            return button;
        }

        private Popup CreatePenThicknessPopup()
        {
            return CreateThicknessPopup(
                new[] { 2.0, 4.0, 6.0, 8.0, 12.0 },
                width => PenWidthRequested?.Invoke(width),
                penThicknessMenuItems,
                "Pen");
        }

        private Popup CreateThicknessPopup(
            IEnumerable<double> widths,
            Action<double> selected,
            IDictionary<double, Button> targetItems,
            string toolKind)
        {
            var popup = CreatePersistentOptionPopup();
            var stack = new StackPanel { Margin = new Thickness(4) };
            foreach (var width in widths)
            {
                var selectedWidth = width;
                var item = CreatePersistentOptionButton(null, 126, 32);
                item.Tag = "두께 " + selectedWidth;
                item.Content = CreateThicknessPopupContent(selectedWidth);
                item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                Action activate = () =>
                {
                    popup.IsOpen = false;
                    ClearActiveOptionPopup(popup);
                    selected?.Invoke(selectedWidth);
                    DebugLog.WriteDiagnostic("PANEL-THICKNESS", "selected tool=" + toolKind +
                        ", width=" + selectedWidth.ToString("0.##"));
                };
                var activateOnce = CreateSingleActivation(activate, DescribeInputSource(item));
                item.Click += (sender, args) => activateOnce();
                AttachDirectTouchActivation(item, activateOnce);
                targetItems[selectedWidth] = item;
                stack.Children.Add(item);
            }
            popup.Child = CreateGlassPopupSurface(stack);
            popup.Closed += (sender, args) => DebugLog.WriteDiagnostic("PANEL-THICKNESS",
                "popupClosed tool=" + toolKind);
            AttachPopupTracking(popup);
            return popup;
        }

        private void AttachLongPressPopup(Button button, Popup popup, Action shortAction,
            string popupKind, int longPressMilliseconds = 650)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(longPressMilliseconds) };
            var pressActive = false;
            var longPressTriggered = false;
            var pressOrigin = new Point();
            TouchDevice activeTouchDevice = null;
            var invokeShortOnce = CreateSingleActivation(shortAction, DescribeInputSource(button));

            Action<Point> beginPress = point =>
            {
                pressOrigin = point;
                pressActive = true;
                longPressTriggered = false;
                timer.Stop();
                timer.Start();
            };
            Action<Point> updatePress = point =>
            {
                if (!pressActive || !MovedBeyondThreshold(
                        pressOrigin, point, LongPressMovementToleranceDip)) return;
                pressActive = false;
                timer.Stop();
            };
            Action endPress = () =>
            {
                pressActive = false;
                timer.Stop();
                if (longPressTriggered)
                    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => longPressTriggered = false));
            };

            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                if (!pressActive) return;
                pressActive = false;
                longPressTriggered = true;
                var popupName = popupKind == "zoom" ? "배율 팝업" : "두께 팝업";
                DebugLog.WriteDiagnostic("PANEL-LONG", DescribeInputSource(button) + " " + popupName + " 길게 누르기 실행");
                CloseTouchToolTip();
                CloseActiveToolPopups();
                button.ReleaseAllTouchCaptures();
                if (button.IsMouseCaptured) button.ReleaseMouseCapture();
                popup.PlacementTarget = button;
                popup.Placement = PlacementMode.Bottom;
                activeOptionPopup = popup;
                activeOptionPopupKind = popupKind;
                popup.IsOpen = true;
                DebugLog.WriteDiagnostic(popupKind == "zoom" ? "PANEL-ZOOM" : "PANEL-THICKNESS",
                    DescribeInputSource(button) + " popupOpen=" + popup.IsOpen);
            };
            button.PreviewTouchDown += (sender, args) =>
            {
                if (activeTouchDevice != null)
                {
                    args.Handled = true;
                    return;
                }

                activeTouchDevice = args.TouchDevice;
                beginPress(args.GetTouchPoint(button).Position);
                var captured = button.CaptureTouch(activeTouchDevice);
                DebugLog.WriteDiagnostic("PANEL-LONG", DescribeInputSource(button) +
                    " TouchDown id=" + activeTouchDevice.Id + ", capture=" + captured);
                args.Handled = true;
            };
            button.PreviewTouchMove += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;
                updatePress(args.GetTouchPoint(button).Position);
                args.Handled = true;
            };
            button.PreviewTouchUp += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;

                var invokeShortAction = pressActive && !longPressTriggered;
                activeTouchDevice = null;
                endPress();
                button.ReleaseAllTouchCaptures();
                args.Handled = true;
                DebugLog.WriteDiagnostic("PANEL-LONG", DescribeInputSource(button) +
                    " TouchUp short=" + invokeShortAction);
                if (invokeShortAction) invokeShortOnce();
            };
            button.LostTouchCapture += (sender, args) =>
            {
                if (activeTouchDevice != args.TouchDevice) return;
                activeTouchDevice = null;
                endPress();
            };
            button.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (args.StylusDevice == null) beginPress(args.GetPosition(button));
            };
            button.PreviewMouseMove += (sender, args) =>
            {
                if (args.StylusDevice == null && args.LeftButton == MouseButtonState.Pressed)
                    updatePress(args.GetPosition(button));
            };
            button.PreviewMouseLeftButtonUp += (sender, args) =>
            {
                if (args.StylusDevice == null) endPress();
            };
            button.Click += (sender, args) =>
            {
                if (longPressTriggered)
                {
                    longPressTriggered = false;
                    return;
                }
                invokeShortOnce();
            };
        }

        private Button CreateColorSelectorButton(DrawingStyleKind kind, string title, string tooltip, double buttonWidth)
        {
            var button = new Button
            {
                Tag = title,
                Width = buttonWidth,
                Height = ToolOptionHeightDip,
                Margin = new Thickness(1, 0, 1, 0),
                Padding = new Thickness(1),
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Template = CreateRoundedButtonTemplate(new CornerRadius(2)),
                Content = CreateColorSelectorContent(Colors.White, buttonWidth)
            };
            DisableWindowsPressAndHold(button);
            AttachHelpToolTip(button, title, tooltip, false);
            var popup = CreateColorPalettePopup(kind);
            Action togglePalette = () =>
            {
                CloseTouchToolTip();
                var shouldOpen = !popup.IsOpen;
                CloseActiveToolPopups();
                popup.PlacementTarget = button;
                popup.Placement = PlacementMode.Bottom;
                popup.IsOpen = shouldOpen;
                activeColorPalette = shouldOpen ? popup : null;
                DebugLog.WriteDiagnostic("PANEL-PALETTE", DescribeInputSource(button) +
                    " paletteOpen=" + popup.IsOpen);
            };
            var togglePaletteOnce = CreateSingleActivation(togglePalette, DescribeInputSource(button));
            button.Click += (sender, args) => togglePaletteOnce();
            AttachDirectTouchActivation(button, togglePaletteOnce);
            colorSelectorButtons[kind] = button;
            return button;
        }

        private Popup CreateColorPalettePopup(DrawingStyleKind kind)
        {
            // 터치 호환성과 지속형 선택 동작은 다른 옵션 팝업과 동일하게 유지한다.
            var popup = CreatePersistentOptionPopup();
            var palette = new UniformGrid { Columns = 4, Rows = 4, Margin = new Thickness(5) };
            AddPopupColor(palette, popup, kind, 0x19, 0x19, 0x19, "검정");
            AddPopupColor(palette, popup, kind, 0x55, 0x5B, 0x66, "진회색");
            AddPopupColor(palette, popup, kind, 0xA7, 0xAF, 0xBC, "회색");
            AddPopupColor(palette, popup, kind, 0xFA, 0xFA, 0xFA, "흰색");
            AddPopupColor(palette, popup, kind, 0xF0, 0x32, 0x46, "빨강");
            AddPopupColor(palette, popup, kind, 0xFF, 0x79, 0x20, "주황");
            AddPopupColor(palette, popup, kind, 0xFF, 0xD6, 0x22, "노랑");
            AddPopupColor(palette, popup, kind, 0x9D, 0xDC, 0x28, "연두");
            AddPopupColor(palette, popup, kind, 0x18, 0xA8, 0x68, "초록");
            AddPopupColor(palette, popup, kind, 0x18, 0xB8, 0xAF, "청록");
            AddPopupColor(palette, popup, kind, 0x29, 0xC6, 0xF2, "하늘");
            AddPopupColor(palette, popup, kind, 0x2D, 0x76, 0xF0, "파랑");
            AddPopupColor(palette, popup, kind, 0x22, 0x3F, 0xA3, "남색");
            AddPopupColor(palette, popup, kind, 0x78, 0x4D, 0xD6, "보라");
            AddPopupColor(palette, popup, kind, 0xE5, 0x3E, 0xA8, "분홍");
            AddPopupColor(palette, popup, kind, 0x9A, 0x5A, 0x32, "갈색");
            popup.Child = CreateGlassPopupSurface(palette);
            AttachPopupTracking(popup);
            return popup;
        }

        private void AddPopupColor(Panel palette, Popup popup, DrawingStyleKind kind,
            byte red, byte green, byte blue, string name)
        {
            var color = Color.FromRgb(red, green, blue);
            var lightColor = color.R > 225 && color.G > 225 && color.B > 225;
            Brush swatchBorder = lightColor
                ? (Brush)new SolidColorBrush(Color.FromArgb(150, 90, 101, 115))
                : Brushes.Transparent;
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                Background = new SolidColorBrush(color),
                BorderBrush = swatchBorder,
                BorderThickness = lightColor ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 3,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.32,
                    Color = Colors.Black
                }
            };
            var button = new Button
            {
                Tag = "색상 " + name,
                Content = swatch,
                Width = 28,
                Height = 28,
                Margin = new Thickness(1),
                Padding = new Thickness(3),
                Background = CreatePopupItemBrush(),
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Template = CreateGlassPopupButtonTemplate(new CornerRadius(6))
            };
            button.SetValue(GlassPopupButtonProperty, true);
            DisableWindowsPressAndHold(button);
            Action selectColor = () =>
            {
                popup.IsOpen = false;
                if (activeColorPalette == popup) activeColorPalette = null;
                DrawingColorRequested?.Invoke(kind, color);
                DebugLog.WriteDiagnostic("PANEL-PALETTE", DescribeInputSource(button) + " 선택 완료");
            };
            var selectColorOnce = CreateSingleActivation(selectColor, DescribeInputSource(button));
            button.Click += (sender, args) => selectColorOnce();
            AttachDirectTouchActivation(button, selectColorOnce);
            palette.Children.Add(button);
        }

        private Button CreateIconButton(PanelIcon icon, string title, string tooltip, RoutedEventHandler click,
            bool accent = false, double size = 34)
        {
            var button = new Button
            {
                Tag = title,
                Content = CreateIcon(icon, CreateSilverIconBrush(), size <= 31 ? 15 : 17),
                Width = size, Height = size, Margin = new Thickness(1), Padding = new Thickness(3),
                Foreground = CreateSilverIconBrush(),
                Background = LiquidGlassTheme.CreateButtonBrush(accent),
                BorderBrush = LiquidGlassTheme.CreateButtonRefractionBrush(accent),
                BorderThickness = new Thickness(0.75),
                Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Template = CreateRaisedButtonTemplate(new CornerRadius(size / 2.0)),
                Effect = null
            };
            DisableWindowsPressAndHold(button);
            AttachHelpToolTip(button, title, tooltip, false);
            if (click != null)
            {
                Action activate = () => click(button, new RoutedEventArgs(Button.ClickEvent, button));
                var activateOnce = CreateSingleActivation(activate, DescribeInputSource(button));
                button.Click += (sender, args) => activateOnce();
                AttachDirectTouchActivation(button, activateOnce);
            }
            return button;
        }

        private static void SetButtonActive(Button button, bool active)
        {
            if ((bool)button.GetValue(GlassPopupButtonProperty))
            {
                button.Background = active ? CreatePopupActiveItemBrush() : CreatePopupItemBrush();
                button.BorderBrush = active
                    ? LiquidGlassTheme.AccentBrush
                    : LiquidGlassTheme.HairlineBrush;
                button.BorderThickness = active ? new Thickness(1.15) : new Thickness(0);
                button.Effect = null;
                return;
            }

            button.Background = LiquidGlassTheme.CreateButtonBrush(active);
            button.BorderBrush = LiquidGlassTheme.CreateButtonRefractionBrush(active);
            button.BorderThickness = new Thickness(active ? 1.05 : 0.75);
            // Effect는 Content까지 래스터화해 작은 벡터 아이콘을 흐리게 만든다.
            // 활성 상태는 테두리와 배경으로만 표현해 아이콘을 원본 벡터로 렌더링한다.
            button.Effect = null;
        }

        private static Brush CreateGlassBrush()
        {
            return LiquidGlassTheme.CreatePanelGlassBrush();
        }

        private static Brush CreatePopupGlassBrush()
        {
            return LiquidGlassTheme.CreatePopupGlassBrush();
        }

        private static Brush CreatePopupBorderBrush()
        {
            return LiquidGlassTheme.CreatePopupBorderBrush();
        }

        private static Brush CreatePopupHighlightBrush()
        {
            return LiquidGlassTheme.CreatePopupHighlightBrush();
        }

        private static Brush CreatePopupItemBrush()
        {
            return LiquidGlassTheme.CreatePopupItemBrush();
        }

        private static Brush CreatePopupHoverItemBrush()
        {
            return LiquidGlassTheme.CreatePopupHoverItemBrush();
        }

        private static Brush CreatePopupPressedItemBrush()
        {
            return LiquidGlassTheme.CreatePopupPressedItemBrush();
        }

        private static Brush CreatePopupActiveItemBrush()
        {
            return LiquidGlassTheme.CreatePopupActiveItemBrush();
        }

        private static Brush CreatePopupSeparatorBrush()
        {
            return LiquidGlassTheme.CreatePopupSeparatorBrush();
        }

        private static Brush CreateLensButtonBrush()
        {
            return LiquidGlassTheme.CreateButtonBrush(false);
        }

        private static Brush CreateSilverIconBrush()
        {
            return LiquidGlassTheme.IconBrush;
        }

        private void ApplyWindowRegion()
        {
            if (windowHandle == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            if (AllowsTransparency)
            {
                // 둥근 모서리는 WPF의 투명 픽셀이 직접 만든다. DWM/GDI 영역을 겹치면
                // 가장자리가 다시 거칠어질 수 있으므로 창 영역을 적용하지 않는다.
                NativeMethods.ClearWindowRegion(windowHandle);
                DebugLog.WriteDiagnostic("PANEL-GLASS",
                    "corner=wpf-per-pixel, gdiRegion=False, outerShadow=False" +
                    ", panelSurfaceAlpha=" + LiquidGlassTheme.PanelSurfaceAlpha +
                    ", panelHeaderAlpha=" + LiquidGlassTheme.PanelHeaderAlpha);
                return;
            }

            if (NativeMethods.TryApplyAntialiasedRoundedCorners(windowHandle))
            {
                // Windows 11에서는 DWM의 안티앨리어싱 곡면만 사용한다. GDI SetWindowRgn을
                // 함께 적용하면 좌측 곡면에 이진 픽셀 계단선이 생길 수 있다.
                NativeMethods.ClearWindowRegion(windowHandle);
                DebugLog.WriteDiagnostic("PANEL-GLASS",
                    "corner=dwm-antialiased, gdiRegion=False, outerShadow=False");
                return;
            }

            var target = currentScreen ?? Forms.Screen.FromHandle(windowHandle) ?? Forms.Screen.PrimaryScreen;
            var scale = NativeMethods.GetScaleForPoint(target.Bounds.Left + 1, target.Bounds.Top + 1);
            var width = Math.Max(1, (int)Math.Round(ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Round(ActualHeight * scale));
            var radius = Math.Max(8, (int)Math.Round((expandedPanelRoot != null &&
                expandedPanelRoot.Visibility == Visibility.Visible ? 22.0 : 21.0) * scale));
            NativeMethods.ApplyRoundedWindowRegion(windowHandle, width, height, radius);
            DebugLog.WriteDiagnostic("PANEL-GLASS",
                "corner=gdi-fallback, gdiRegion=True, outerShadow=False");
        }

        private static ControlTemplate CreateRoundedButtonTemplate(CornerRadius cornerRadius)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, cornerRadius);
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        private static ControlTemplate CreateRaisedButtonTemplate(CornerRadius cornerRadius)
        {
            var layers = new FrameworkElementFactory(typeof(Grid));
            layers.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            // 시각 레이어의 미세한 오프셋과 무관하게 원래 버튼 사각형 전체를
            // 동일한 터치 표면으로 유지한다.
            layers.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var contactShadow = new FrameworkElementFactory(typeof(Border), "ButtonContactShadow");
            contactShadow.SetValue(Border.BackgroundProperty,
                LiquidGlassTheme.CreateButtonContactShadowBrush());
            contactShadow.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            contactShadow.SetValue(Border.CornerRadiusProperty, cornerRadius);
            contactShadow.SetValue(FrameworkElement.MarginProperty,
                new Thickness(1.05, 1.25, 0, 0));
            contactShadow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            layers.AppendChild(contactShadow);

            var chrome = new FrameworkElementFactory(typeof(Border), "RaisedButtonChrome");
            chrome.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            chrome.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(Control.BorderThicknessProperty));
            chrome.SetValue(Border.CornerRadiusProperty, cornerRadius);
            chrome.SetValue(FrameworkElement.MarginProperty,
                new Thickness(0, 0, 0.85, 0.9));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,
                new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.MarginProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            chrome.AppendChild(presenter);
            layers.AppendChild(chrome);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = layers };
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.18, "ButtonContactShadow"));
            pressed.Setters.Add(new Setter(FrameworkElement.MarginProperty,
                new Thickness(0.65, 0.7, 0.25, 0.25), "RaisedButtonChrome"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46, "RaisedButtonChrome"));
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.0, "ButtonContactShadow"));
            template.Triggers.Add(disabled);
            return template;
        }

        private static ControlTemplate CreateGlassPopupButtonTemplate(CornerRadius cornerRadius)
        {
            var border = new FrameworkElementFactory(typeof(Border), "GlassButtonChrome");
            border.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, cornerRadius);
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(Stylus.IsPressAndHoldEnabledProperty, false);
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,
                new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, CreatePopupHoverItemBrush(), "GlassButtonChrome"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(Color.FromArgb(160, 242, 247, 255)), "GlassButtonChrome"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, CreatePopupPressedItemBrush(), "GlassButtonChrome"));
            pressed.Setters.Add(new Setter(Border.BorderBrushProperty,
                new SolidColorBrush(Color.FromArgb(210, 225, 235, 248)), "GlassButtonChrome"));
            template.Triggers.Add(pressed);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46, "GlassButtonChrome"));
            template.Triggers.Add(disabled);
            return template;
        }

        private static void DisableWindowsPressAndHold(DependencyObject element)
        {
            if (element != null)
            {
                // 적외선 전자칠판은 손가락과 터치펜 입력을 Stylus 계열로 보내는 경우가 많다.
                // Windows의 길게 누르기=우클릭 판정을 끄면 TouchDown이 즉시 발생하여
                // 앱 자체의 짧은 터치와 길게 누르기 타이머가 정상적으로 동작한다.
                Stylus.SetIsPressAndHoldEnabled(element, false);
            }
        }

        private static string DescribeInputSource(object source)
        {
            var element = source as FrameworkElement;
            if (element == null) return source == null ? "null" : source.GetType().Name;
            var tag = element.Tag as string;
            return string.IsNullOrWhiteSpace(tag) ? element.GetType().Name : element.GetType().Name + "/" + tag;
        }

        private static string DescribeStylus(StylusEventArgs args)
        {
            try
            {
                var tablet = args.StylusDevice?.TabletDevice;
                return tablet == null ? "unknown" : tablet.Type + "/" + tablet.Name;
            }
            catch
            {
                return "unknown";
            }
        }

        private static Action CreateSingleActivation(Action action, string source)
        {
            var lastActivationUtc = DateTime.MinValue;
            return () =>
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - lastActivationUtc).TotalMilliseconds;
                if (elapsed < 250)
                {
                    DebugLog.WriteDiagnostic("PANEL-ONCE", source +
                        " 중복 실행 차단 elapsedMs=" + elapsed.ToString("0"));
                    return;
                }

                lastActivationUtc = now;
                action?.Invoke();
            };
        }

        private static void AttachDirectTouchActivation(Button button, Action action)
        {
            TouchDevice activeDevice = null;
            var pressOrigin = new Point();
            var moved = false;

            button.PreviewTouchDown += (sender, args) =>
            {
                if (activeDevice != null)
                {
                    args.Handled = true;
                    return;
                }

                activeDevice = args.TouchDevice;
                pressOrigin = args.GetTouchPoint(button).Position;
                moved = false;
                var captured = button.CaptureTouch(activeDevice);
                DebugLog.WriteDiagnostic("PANEL-BUTTON", DescribeInputSource(button) +
                    " TouchDown id=" + activeDevice.Id + ", capture=" + captured);
                args.Handled = true;
            };
            button.PreviewTouchMove += (sender, args) =>
            {
                if (activeDevice != args.TouchDevice) return;
                if (!moved && MovedBeyondThreshold(pressOrigin, args.GetTouchPoint(button).Position, 12))
                {
                    moved = true;
                    DebugLog.WriteDiagnostic("PANEL-BUTTON", DescribeInputSource(button) + " 이동으로 실행 취소");
                }
                args.Handled = true;
            };
            button.PreviewTouchUp += (sender, args) =>
            {
                if (activeDevice != args.TouchDevice) return;

                var point = args.GetTouchPoint(button).Position;
                var activate = !moved && point.X >= 0 && point.X <= button.ActualWidth &&
                               point.Y >= 0 && point.Y <= button.ActualHeight;
                activeDevice = null;
                button.ReleaseAllTouchCaptures();
                args.Handled = true;

                DebugLog.WriteDiagnostic("PANEL-BUTTON", DescribeInputSource(button) +
                    " TouchUp activate=" + activate + ", point=" + point);

                if (activate && button.IsEnabled)
                {
                    action?.Invoke();
                    DebugLog.WriteDiagnostic("PANEL-BUTTON", DescribeInputSource(button) + " 직접 실행 요청");
                }
            };
            button.LostTouchCapture += (sender, args) =>
            {
                if (activeDevice == args.TouchDevice)
                {
                    DebugLog.WriteDiagnostic("PANEL-BUTTON", DescribeInputSource(button) +
                        " LostTouchCapture id=" + args.TouchDevice.Id);
                    activeDevice = null;
                }
            };
        }

        private static void AttachDirectTouchActivation(MenuItem item, Action action)
        {
            TouchDevice activeDevice = null;
            var pressOrigin = new Point();
            var moved = false;

            item.PreviewTouchDown += (sender, args) =>
            {
                if (activeDevice != null)
                {
                    args.Handled = true;
                    return;
                }

                activeDevice = args.TouchDevice;
                pressOrigin = args.GetTouchPoint(item).Position;
                moved = false;
                var captured = item.CaptureTouch(activeDevice);
                DebugLog.WriteDiagnostic("PANEL-MENU", DescribeInputSource(item) +
                    " TouchDown id=" + activeDevice.Id + ", capture=" + captured);
                args.Handled = true;
            };
            item.PreviewTouchMove += (sender, args) =>
            {
                if (activeDevice != args.TouchDevice) return;
                if (MovedBeyondThreshold(pressOrigin, args.GetTouchPoint(item).Position, 12)) moved = true;
                args.Handled = true;
            };
            item.PreviewTouchUp += (sender, args) =>
            {
                if (activeDevice != args.TouchDevice) return;

                var point = args.GetTouchPoint(item).Position;
                var activate = !moved && point.X >= 0 && point.X <= item.ActualWidth &&
                               point.Y >= 0 && point.Y <= item.ActualHeight;
                activeDevice = null;
                item.ReleaseAllTouchCaptures();
                args.Handled = true;
                DebugLog.WriteDiagnostic("PANEL-MENU", DescribeInputSource(item) +
                    " TouchUp activate=" + activate + ", point=" + point);
                if (activate && item.IsEnabled)
                {
                    action?.Invoke();
                    DebugLog.WriteDiagnostic("PANEL-MENU", DescribeInputSource(item) + " 직접 실행");
                }
            };
            item.LostTouchCapture += (sender, args) =>
            {
                if (activeDevice == args.TouchDevice) activeDevice = null;
            };
        }

        private static ContextMenu CreatePopupMenu()
        {
            var menu = new ContextMenu
            {
                Opacity = 1.0,
                Background = CreatePopupGlassBrush(),
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                BorderBrush = CreatePopupBorderBrush(),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                HasDropShadow = false,
                Template = CreateGlassContextMenuTemplate()
            };
            DisableWindowsPressAndHold(menu);
            return menu;
        }

        private static Style CreateGlassMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Stylus.IsPressAndHoldEnabledProperty, false));
            style.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateGlassMenuItemTemplate()));
            var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, CreatePopupHoverItemBrush()));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.PrimaryTextBrush));
            style.Triggers.Add(highlighted);
            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, CreatePopupActiveItemBrush()));
            checkedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(checkedTrigger);
            return style;
        }

        private static ControlTemplate CreateGlassContextMenuTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(Border.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = LiquidGlassTheme.IsDark ? 0.28 : 0.19,
                Color = Colors.Black
            });
            var layers = new FrameworkElementFactory(typeof(Grid));
            var highlight = new FrameworkElementFactory(typeof(Border));
            highlight.SetValue(FrameworkElement.HeightProperty, 22.0);
            highlight.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            highlight.SetValue(Border.BackgroundProperty, CreatePopupHighlightBrush());
            highlight.SetValue(Border.CornerRadiusProperty, new CornerRadius(17, 17, 7, 7));
            highlight.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            layers.AppendChild(highlight);
            layers.AppendChild(presenter);
            border.AppendChild(layers);
            return new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
        }

        private static ControlTemplate CreateGlassMenuItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "MenuItemChrome");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.MarginProperty, new Thickness(1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(HeaderedItemsControl.HeaderProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty,
                new TemplateBindingExtension(HeaderedItemsControl.HeaderTemplateProperty));
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46, "MenuItemChrome"));
            template.Triggers.Add(disabled);
            return template;
        }

        private static Style CreateGlassSeparatorStyle()
        {
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(FrameworkElement.HeightProperty, 1.0);
            line.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 4, 7, 4));
            line.SetValue(Border.BackgroundProperty, CreatePopupSeparatorBrush());
            var style = new Style(typeof(Separator));
            style.Setters.Add(new Setter(Control.TemplateProperty,
                new ControlTemplate(typeof(Separator)) { VisualTree = line }));
            return style;
        }

        private static UIElement CreateShapePopupContent(AppMode shapeMode, string title)
        {
            var grid = new Grid { Width = 106, Height = 22 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = CreateShapeIcon(shapeMode, CreateSilverIconBrush());
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);
            var label = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            return grid;
        }

        private static UIElement CreateThicknessPopupContent(double width)
        {
            var grid = new Grid { Width = 92, Height = 22 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sample = CreateMenuThicknessSample(width, CreateSilverIconBrush());
            Grid.SetColumn(sample, 0);
            grid.Children.Add(sample);
            var label = new TextBlock
            {
                Text = string.Format("{0:0} px", width),
                FontSize = 13,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            return grid;
        }

        private static UIElement CreateColorSelectorContent(Color color, double buttonWidth)
        {
            var contentWidth = Math.Max(12, buttonWidth - 4);
            var grid = new Grid { Width = contentWidth, Height = 7 };
            var lightColor = color.R > 225 && color.G > 225 && color.B > 225;
            Brush swatchBorder = lightColor
                ? (Brush)new SolidColorBrush(Color.FromRgb(105, 118, 134))
                : Brushes.Transparent;
            var swatch = new Border
            {
                Width = contentWidth,
                Height = 7,
                Background = new SolidColorBrush(color),
                BorderBrush = swatchBorder,
                BorderThickness = lightColor ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(swatch);
            var arrow = new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(4, 0), new Point(2, 2.5) },
                Fill = color.R + color.G + color.B > 520
                    ? new SolidColorBrush(Color.FromArgb(220, 25, 35, 48))
                    : new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0)
            };
            grid.Children.Add(arrow);
            return grid;
        }

        private static UIElement CreateDropDownIcon(UIElement mainIcon)
        {
            var grid = new Grid { Width = 21, Height = 18 };
            var mainElement = mainIcon as FrameworkElement;
            if (mainElement != null)
            {
                mainElement.HorizontalAlignment = HorizontalAlignment.Left;
                mainElement.VerticalAlignment = VerticalAlignment.Center;
            }
            grid.Children.Add(mainIcon);
            grid.Children.Add(new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(5, 0), new Point(2.5, 3) },
                Fill = LiquidGlassTheme.IconBrush,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 1)
            });
            return grid;
        }

        private static UIElement CreateMenuThicknessSample(double width, Brush brush)
        {
            return new Line
            {
                X1 = 0, X2 = 24, Y1 = 8, Y2 = 8, Width = 25, Height = 16,
                Stroke = brush, StrokeThickness = Math.Min(10, width),
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
        }

        private static UIElement CreateShapeIcon(AppMode shapeMode, Brush brush)
        {
            switch (shapeMode)
            {
                case AppMode.Rectangle: return CreateIcon(PanelIcon.Rectangle, brush, 16);
                case AppMode.Ellipse: return CreateIcon(PanelIcon.Ellipse, brush, 16);
                case AppMode.Line: return CreateIcon(PanelIcon.Line, brush, 16);
                default: return CreateIcon(PanelIcon.Arrow, brush, 16);
            }
        }

        private static UIElement CreateIcon(PanelIcon icon, Brush brush, double size)
        {
            var scale = size / 18.0;
            var canvas = new Canvas
            {
                Width = size,
                Height = size,
                UseLayoutRounding = false,
                SnapsToDevicePixels = false
            };
            Action<Geometry, double> addPath = (geometry, thickness) =>
            {
                // Geometry 자체를 복제한 뒤 Transform을 지정한다. .NET Framework 4.8 WPF에는
                // TransformedGeometry 형식이 없으므로 이 방식으로 목적 크기의 벡터를 만든다.
                var transformedGeometry = geometry.Clone();
                transformedGeometry.Transform = new ScaleTransform(scale, scale);
                canvas.Children.Add(new Path
                {
                    Data = transformedGeometry,
                    Stroke = brush,
                    StrokeThickness = Math.Max(0.65, thickness * 0.64 * scale),
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round, Fill = Brushes.Transparent,
                    SnapsToDevicePixels = false,
                    UseLayoutRounding = false
                });
            };
            switch (icon)
            {
                case PanelIcon.Settings:
                    addPath(Geometry.Parse("M9,5 A4,4 0 1 0 9,13 A4,4 0 1 0 9,5 M9,1 L9,4 M9,14 L9,17 M1,9 L4,9 M14,9 L17,9 M3.3,3.3 L5.4,5.4 M12.6,12.6 L14.7,14.7 M14.7,3.3 L12.6,5.4 M5.4,12.6 L3.3,14.7"), 1.5); break;
                case PanelIcon.Hide:
                    addPath(Geometry.Parse("M4,4 L14,14 M14,4 L4,14"), 2.0); break;
                case PanelIcon.Pointer:
                    addPath(Geometry.Parse("M3,2 L14,10 L9.5,11.2 L12.5,16 L9.8,17 L7,12.2 L3.5,15 Z"), 1.25); break;
                case PanelIcon.Hand:
                    addPath(Geometry.Parse("M5.2,8.8 L5.2,5.6 C5.2,4.5 6.7,4.4 6.8,5.6 L6.8,8 M6.8,5 L6.8,3.8 C6.8,2.6 8.4,2.6 8.4,3.8 L8.4,7.8 M8.4,4.3 C8.4,3.1 10,3.1 10,4.3 L10,8 M10,5.2 C10,4.1 11.6,4.1 11.6,5.3 L11.6,9.1 L13,7.8 C14,6.9 15.1,8.1 14.3,9.1 L11.3,13.7 C10.5,14.9 9.4,15.6 7.8,15.6 C5.2,15.6 3.7,14.1 3.1,11.7 L2.6,9.8 C2.3,8.7 3.8,8.2 4.3,9.2 L5.2,11"), 1.35); break;
                case PanelIcon.ZoomIn:
                case PanelIcon.ZoomOut:
                    addPath(Geometry.Parse("M7.5,2 A5.5,5.5 0 1 0 7.5,13 A5.5,5.5 0 1 0 7.5,2 M11.5,11.5 L16.5,16.5"), 1.6);
                    addPath(Geometry.Parse(icon == PanelIcon.ZoomIn ? "M4.5,7.5 L10.5,7.5 M7.5,4.5 L7.5,10.5" : "M4.5,7.5 L10.5,7.5"), 1.6); break;
                case PanelIcon.Reset:
                    addPath(Geometry.Parse("M4,5 A6,6 0 1 1 3.5,12 M4,5 L1.5,4.5 M4,5 L3.5,2.5"), 1.6);
                    canvas.Children.Add(new TextBlock { Text = "1", FontSize = 8 * scale, FontWeight = FontWeights.Normal, Foreground = brush, Margin = new Thickness(7 * scale, 5 * scale, 0, 0) }); break;
                case PanelIcon.Power:
                    addPath(Geometry.Parse("M2.5,3.5 L10.5,3.5 L10.5,14.5 L2.5,14.5 Z M7.5,9 L16,9 M13.2,6.2 L16,9 L13.2,11.8"), 1.55); break;
                case PanelIcon.Pen:
                    addPath(Geometry.Parse("M3,15 L4.2,10.8 L12.8,2.2 L15.8,5.2 L7.2,13.8 Z M11.5,3.5 L14.5,6.5"), 1.4); break;
                case PanelIcon.Highlighter:
                    addPath(Geometry.Parse("M4,12 L11.5,2.5 L15.5,5.7 L8,15 Z M3,16 L10,16"), 1.6); break;
                case PanelIcon.Eraser:
                    addPath(Geometry.Parse("M2.5,11.2 L10.2,2.8 L15.6,7.6 L7.9,16 L3.8,16 L1.5,14 Z M2.5,11.2 L7.9,16 M10.2,2.8 L10.8,6.5 L15.6,7.6"), 1.4); break;
                case PanelIcon.Rectangle:
                    addPath(Geometry.Parse("M2.5,4 L15.5,4 L15.5,14 L2.5,14 Z"), 1.5); break;
                case PanelIcon.Ellipse:
                    addPath(new EllipseGeometry(new Point(9, 9), 6.5, 5), 1.5); break;
                case PanelIcon.Line:
                    addPath(Geometry.Parse("M3,15 L15,3"), 1.8); break;
                case PanelIcon.Arrow:
                    addPath(Geometry.Parse("M2.5,15.5 L15,3 M9.5,3 L15,3 L15,8.5"), 1.7); break;
                case PanelIcon.Shapes:
                    addPath(Geometry.Parse("M2,2.8 L11.7,2.8 L11.7,12.5 L2,12.5 Z"), 1.4);
                    addPath(new EllipseGeometry(new Point(11.7, 11.5), 5.1, 4.8), 1.4); break;
                case PanelIcon.Undo:
                    addPath(Geometry.Parse("M7,5 L3,9 L7,13 M3,9 L11,9 C14,9 16,11 16,14"), 1.7); break;
                case PanelIcon.Clear:
                    addPath(Geometry.Parse("M4,5 L14,5 M7,2.5 L11,2.5 L12,5 M5.5,5 L6.2,16 L11.8,16 L12.5,5 M8,8 L8.3,13 M10,8 L9.7,13"), 1.5); break;
                case PanelIcon.MoveGrip:
                    addPath(Geometry.Parse("M9,1.5 L9,16.5 M6.5,4 L9,1.5 L11.5,4 M6.5,14 L9,16.5 L11.5,14 M1.5,9 L16.5,9 M4,6.5 L1.5,9 L4,11.5 M14,6.5 L16.5,9 L14,11.5"), 1.25); break;
                case PanelIcon.TouchZoom:
                    addPath(Geometry.Parse("M10.5,2.5 A4.5,4.5 0 1 0 10.5,11.5 A4.5,4.5 0 1 0 10.5,2.5 M13.7,10.2 L17,13.5"), 1.45);
                    addPath(Geometry.Parse("M8.2,7 L12.8,7 M10.5,4.7 L10.5,9.3 M2,14.8 A3,3 0 0 1 5,11.8 M1.5,11 A6,6 0 0 1 5.2,8.4"), 1.25);
                    canvas.Children.Add(new Ellipse { Width = 2.8 * scale, Height = 2.8 * scale, Fill = brush, Margin = new Thickness(0.6 * scale, 13.2 * scale, 0, 0) });
                    break;
                case PanelIcon.MiniMap:
                    addPath(Geometry.Parse("M2,3 L16,3 L16,13 L2,13 Z M5,6 L13,6 L13,11 L5,11 Z"), 1.35);
                    canvas.Children.Add(new Ellipse { Width = 2.6 * scale, Height = 2.6 * scale, Fill = brush, Margin = new Thickness(13.4 * scale, 13.2 * scale, 0, 0) });
                    break;
                case PanelIcon.ChevronLeft:
                    addPath(Geometry.Parse("M12,3 L6,9 L12,15"), 1.55); break;
                case PanelIcon.ChevronRight:
                    addPath(Geometry.Parse("M6,3 L12,9 L6,15"), 1.55); break;
            }
            return canvas;
        }

        private void AttachHelpToolTip(Button button, string title, string tooltip, bool alwaysTooltip)
        {
            var content = new StackPanel { MaxWidth = 205 };
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10.5,
                FontWeight = FontWeights.Normal,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = tooltip,
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 9.5,
                LineHeight = 13.5,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 205
            });
            var helpToolTip = new ToolTip
            {
                Content = content,
                PlacementTarget = button,
                Placement = PlacementMode.Left,
                Padding = new Thickness(7, 5, 7, 5),
                MaxWidth = 225,
                Background = CreatePopupGlassBrush(),
                BorderBrush = CreatePopupBorderBrush(),
                BorderThickness = new Thickness(1),
                Opacity = 1.0,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Direction = 270,
                    Opacity = LiquidGlassTheme.IsDark ? 0.27 : 0.18,
                    Color = Colors.Black
                },
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = CreateCompactToolTipTemplate()
            };
            button.ToolTip = helpToolTip;
            ToolTipService.SetInitialShowDelay(button, 350);
            ToolTipService.SetBetweenShowDelay(button, 50);
            ToolTipService.SetShowDuration(button, 3000);
            ToolTipService.SetIsEnabled(button, alwaysTooltip || tooltipsEnabled);
            AttachPopupTracking(helpToolTip);
            if (!alwaysTooltip) helpToolTips[button] = helpToolTip;

            Action suppressForBoardInput = () =>
            {
                SuppressAllToolTipsForBoardInput();
            };
            button.PreviewTouchDown += (sender, args) => suppressForBoardInput();
            button.PreviewTouchMove += (sender, args) => suppressForBoardInput();
            button.PreviewTouchUp += (sender, args) => suppressForBoardInput();
            button.PreviewStylusDown += (sender, args) => suppressForBoardInput();
            button.PreviewStylusMove += (sender, args) => suppressForBoardInput();
            button.PreviewStylusUp += (sender, args) => suppressForBoardInput();
            button.PreviewMouseMove += (sender, args) =>
            {
                var elapsed = (DateTime.UtcNow - lastBoardToolTipSuppressionUtc).TotalMilliseconds;
                if (args.StylusDevice != null)
                {
                    suppressForBoardInput();
                    return;
                }
                if (elapsed < 1200)
                {
                    helpToolTip.IsOpen = false;
                    ToolTipService.SetIsEnabled(button, false);
                    return;
                }

                if (touchSuppressedToolTips.Remove(button))
                    ToolTipService.SetIsEnabled(button, alwaysTooltip || tooltipsEnabled);
            };
        }

        private static ControlTemplate CreateCompactToolTipTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);
            return new ControlTemplate(typeof(ToolTip)) { VisualTree = border };
        }

        private void AttachPopupTracking(ToolTip toolTip)
        {
            if (!themedToolTips.Contains(toolTip)) themedToolTips.Add(toolTip);
            toolTip.Opened += (sender, args) => TrackPopupWindow(toolTip);
            toolTip.Closed += (sender, args) => UntrackPopupWindow(toolTip);
        }

        private void AttachPopupTracking(ContextMenu menu)
        {
            if (!themedContextMenus.Contains(menu)) themedContextMenus.Add(menu);
            menu.Opacity = 1.0;
            menu.Opened += (sender, args) => TrackPopupWindow(menu);
            menu.Closed += (sender, args) => UntrackPopupWindow(menu);
        }

        private void AttachPopupTracking(Popup popup)
        {
            if (!themedPopups.Contains(popup)) themedPopups.Add(popup);
            if (popup.Child != null) popup.Child.Opacity = 1.0;
            popup.Opened += (sender, args) => TrackPopupWindow(popup, popup.Child as Visual);
            popup.Closed += (sender, args) => UntrackPopupWindow(popup);
        }

        private void TrackPopupWindow(DependencyObject popup, Visual popupVisual = null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var visual = popupVisual ?? popup as Visual;
                var source = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                if (source != null && source.Handle != IntPtr.Zero)
                {
                    popupWindowHandles[popup] = source.Handle;
                    NativeMethods.EnsureToolWindowStyle(source.Handle);
                    NativeMethods.AddExtendedWindowStyle(source.Handle, NativeMethods.WS_EX_NOACTIVATE);
                    NativeMethods.TryApplyAntialiasedRoundedCorners(source.Handle);
                    var surface = popup is ToolTip
                        ? "tooltip"
                        : popup is ContextMenu ? "context-menu" : "option-popup";
                    LiquidGlassTheme.ApplyPopup(source, surface);
                    NativeMethods.ApplyRoundedWindowRegionFromCurrentBounds(source.Handle, 18);
                    MagnificationExclusionsChanged?.Invoke();
                }
            }));
        }

        private void UntrackPopupWindow(DependencyObject popup)
        {
            var menu = popup as ContextMenu;
            if (menu != null) themedContextMenus.Remove(menu);
            if (popupWindowHandles.Remove(popup)) MagnificationExclusionsChanged?.Invoke();
        }

        private void UpdateToolTipPlacement(int panelCenterX, System.Drawing.Rectangle screenBounds)
        {
            var placement = panelCenterX > screenBounds.Left + screenBounds.Width / 2 ? PlacementMode.Left : PlacementMode.Right;
            foreach (var tip in helpToolTips.Values) tip.Placement = placement;
        }

        private void CloseTouchToolTip()
        {
            foreach (var tip in helpToolTips.Values)
                if (tip.IsOpen) tip.IsOpen = false;
        }

        private void SuppressAllToolTipsForBoardInput()
        {
            lastBoardToolTipSuppressionUtc = DateTime.UtcNow;
            foreach (var pair in helpToolTips)
            {
                touchSuppressedToolTips.Add(pair.Key);
                pair.Value.IsOpen = false;
                ToolTipService.SetIsEnabled(pair.Key, false);
            }
        }

        private void CloseActiveToolPopups()
        {
            if (activeOptionPopup != null)
            {
                var optionPopup = activeOptionPopup;
                var optionKind = activeOptionPopupKind ?? "unknown";
                activeOptionPopup = null;
                activeOptionPopupKind = null;
                if (optionPopup.IsOpen) optionPopup.IsOpen = false;
                DebugLog.WriteDiagnostic(optionKind == "zoom" ? "PANEL-ZOOM" : "PANEL-THICKNESS",
                    "activePopupClosed kind=" + optionKind);
            }
            if (activeColorPalette != null)
            {
                var colorPopup = activeColorPalette;
                activeColorPalette = null;
                if (colorPopup.IsOpen) colorPopup.IsOpen = false;
                DebugLog.WriteDiagnostic("PANEL-PALETTE", "활성 색상 팔레트 닫기");
            }
            if (activeShapePalette != null)
            {
                var shapePopup = activeShapePalette;
                activeShapePalette = null;
                if (shapePopup.IsOpen) shapePopup.IsOpen = false;
                DebugLog.WriteDiagnostic("PANEL-SHAPE", "활성 도형 선택창 닫기");
            }
        }

        private void ClearActiveOptionPopup(Popup popup)
        {
            if (activeOptionPopup != popup) return;
            activeOptionPopup = null;
            activeOptionPopupKind = null;
        }

        private void AttachZoomSafeMove(FrameworkElement handle)
        {
            handle.PreviewTouchDown += (sender, args) =>
            {
                if (!TryBeginZoomSafeMove(handle, PointToScreen(args.GetTouchPoint(this).Position), args.TouchDevice))
                    return;

                var captured = handle.CaptureTouch(args.TouchDevice);
                DebugLog.WriteDiagnostic("PANEL-MOVE", "directCapture input=touch-" +
                    args.TouchDevice.Id + ", success=" + captured);
                if (!captured)
                {
                    CompleteZoomSafeMove("touch-capture-failed");
                    return;
                }
                args.Handled = true;
            };
            handle.PreviewTouchMove += (sender, args) =>
            {
                if (!zoomSafeMoveActive || zoomSafeMoveTouchDevice != args.TouchDevice) return;
                UpdateZoomSafeMove(PointToScreen(args.GetTouchPoint(this).Position));
                args.Handled = true;
            };
            handle.PreviewTouchUp += (sender, args) =>
            {
                if (!zoomSafeMoveActive || zoomSafeMoveTouchDevice != args.TouchDevice) return;
                UpdateZoomSafeMove(PointToScreen(args.GetTouchPoint(this).Position));
                CompleteZoomSafeMove("touch-up");
                args.Handled = true;
            };
            handle.LostTouchCapture += (sender, args) =>
            {
                if (zoomSafeMoveActive && zoomSafeMoveTouchDevice == args.TouchDevice)
                    CompleteZoomSafeMove("touch-capture-lost");
            };

            handle.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (zoomSafeMoveActive)
                {
                    args.Handled = true;
                    return;
                }
                if (!TryBeginZoomSafeMove(handle, PointToScreen(args.GetPosition(this)), null)) return;

                var captured = handle.CaptureMouse();
                DebugLog.WriteDiagnostic("PANEL-MOVE", "directCapture input=mouse, success=" + captured);
                if (!captured)
                {
                    CompleteZoomSafeMove("mouse-capture-failed");
                    return;
                }
                args.Handled = true;
            };
            handle.PreviewMouseMove += (sender, args) =>
            {
                if (!zoomSafeMoveActive || zoomSafeMoveTouchDevice != null ||
                    args.LeftButton != MouseButtonState.Pressed) return;
                UpdateZoomSafeMove(PointToScreen(args.GetPosition(this)));
                args.Handled = true;
            };
            handle.PreviewMouseLeftButtonUp += (sender, args) =>
            {
                if (!zoomSafeMoveActive || zoomSafeMoveTouchDevice != null) return;
                UpdateZoomSafeMove(PointToScreen(args.GetPosition(this)));
                CompleteZoomSafeMove("mouse-up");
                args.Handled = true;
            };
            handle.LostMouseCapture += (sender, args) =>
            {
                if (zoomSafeMoveActive && zoomSafeMoveSource == handle && zoomSafeMoveTouchDevice == null)
                    CompleteZoomSafeMove("mouse-capture-lost");
            };
        }

        private bool TryBeginZoomSafeMove(FrameworkElement source, Point screenPoint, TouchDevice touchDevice)
        {
            if (zoomSafeMoveActive || windowHandle == IntPtr.Zero ||
                !NativeMethods.GetWindowRect(windowHandle, out zoomSafeMoveStartRectangle)) return false;

            CloseActiveToolPopups();
            CloseTouchToolTip();
            zoomSafeMoveActive = true;
            zoomSafeMoveSource = source;
            zoomSafeMoveTouchDevice = touchDevice;
            zoomSafeMoveStartScreen = screenPoint;
            moveCompositionWarningLogged = false;
            DebugLog.WriteDiagnostic("PANEL-MOVE", "directBegin input=" +
                (touchDevice == null ? "mouse" : "touch-" + touchDevice.Id) +
                ", zoomSession=" + zoomSafeMoveEnabled +
                ", origin=" + zoomSafeMoveStartRectangle.Left + "," + zoomSafeMoveStartRectangle.Top);
            return true;
        }

        private void UpdateZoomSafeMove(Point screenPoint)
        {
            if (!zoomSafeMoveActive) return;
            var deltaX = (int)Math.Round(screenPoint.X - zoomSafeMoveStartScreen.X);
            var deltaY = (int)Math.Round(screenPoint.Y - zoomSafeMoveStartScreen.Y);
            var width = zoomSafeMoveStartRectangle.Right - zoomSafeMoveStartRectangle.Left;
            var height = zoomSafeMoveStartRectangle.Bottom - zoomSafeMoveStartRectangle.Top;
            NativeMethods.PositionTopmostWindow(
                windowHandle,
                zoomSafeMoveStartRectangle.Left + deltaX,
                zoomSafeMoveStartRectangle.Top + deltaY,
                width,
                height,
                false);
            var compositionResult = NativeMethods.SynchronizeComposition();
            if (compositionResult != 0 && !moveCompositionWarningLogged)
            {
                moveCompositionWarningLogged = true;
                DebugLog.WriteDiagnostic("PANEL-MOVE", "DwmFlush result=0x" +
                    unchecked((uint)compositionResult).ToString("X8"));
            }
            PanelBoundsChanged?.Invoke();
        }

        private void CompleteZoomSafeMove(string reason)
        {
            if (!zoomSafeMoveActive) return;
            var source = zoomSafeMoveSource;
            zoomSafeMoveActive = false;
            zoomSafeMoveSource = null;
            zoomSafeMoveTouchDevice = null;
            source?.ReleaseAllTouchCaptures();
            if (source?.IsMouseCaptured == true) source.ReleaseMouseCapture();

            FinishPanelMove();
            QueueExternalFocusRestore("ZoomSafeMove-" + reason);
            if (NativeMethods.GetWindowRect(windowHandle, out var rectangle))
            {
                DebugLog.WriteDiagnostic("PANEL-MOVE", "directEnd reason=" + reason +
                    ", final=" + rectangle.Left + "," + rectangle.Top);
            }
        }

        private void FinishPanelMove()
        {
            ClampToCurrentScreen();
            PanelMoved?.Invoke();
        }

        private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (message)
            {
                case NativeMethods.WM_POINTERDOWN:
                    var pointerType = GetPointerType(wParam);
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_POINTERDOWN id=" + LowWord(wParam) +
                        ", type=" + pointerType +
                        ", point=" + DescribePackedPoint(lParam));
                    if (pointerType == NativeMethods.PointerInputType.Touch)
                    {
                        NativeTouchObserved?.Invoke();
                    }
                    break;
                case NativeMethods.WM_POINTERUP:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_POINTERUP id=" + LowWord(wParam) +
                        ", type=" + DescribePointerType(wParam) +
                        ", point=" + DescribePackedPoint(lParam));
                    break;
                case NativeMethods.WM_TOUCH:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_TOUCH contacts=" + LowWord(wParam));
                    break;
                case NativeMethods.WM_LBUTTONDOWN:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_LBUTTONDOWN");
                    break;
                case NativeMethods.WM_LBUTTONUP:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_LBUTTONUP");
                    break;
                case NativeMethods.WM_MOUSEACTIVATE:
                    CaptureExternalForeground("WM_MOUSEACTIVATE");
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_MOUSEACTIVATE priorForeground=0x" +
                        lastExternalForegroundWindow.ToInt64().ToString("X"));
                    // 기본 처리를 허용해야 전자칠판 터치가 WPF Touch/Stylus로 승격된다.
                    break;
                case NativeMethods.WM_ACTIVATE:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_ACTIVATE state=" + LowWord(wParam));
                    FocusStateChanged?.Invoke("WM_ACTIVATE-" + LowWord(wParam));
                    break;
                case NativeMethods.WM_SETFOCUS:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_SETFOCUS");
                    FocusStateChanged?.Invoke("WM_SETFOCUS");
                    break;
                case NativeMethods.WM_KILLFOCUS:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_KILLFOCUS");
                    FocusStateChanged?.Invoke("WM_KILLFOCUS");
                    break;
                case NativeMethods.WM_CAPTURECHANGED:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_CAPTURECHANGED");
                    break;
                case NativeMethods.WM_POINTERCAPTURECHANGED:
                    DebugLog.WriteDiagnostic("PANEL-WIN32", "WM_POINTERCAPTURECHANGED id=" + LowWord(wParam));
                    break;
                case NativeMethods.WM_DEVICECHANGE:
                    DeviceChangeObserved?.Invoke();
                    break;
            }

            if (message == NativeMethods.WM_NCRBUTTONUP)
            {
                Dispatcher.BeginInvoke(new Action(OpenPanelContextMenu));
                handled = true;
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WM_EXITSIZEMOVE)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    FinishPanelMove();
                    QueueExternalFocusRestore("WM_EXITSIZEMOVE");
                }));
            }
            return IntPtr.Zero;
        }

        private static int LowWord(IntPtr value)
        {
            return unchecked((ushort)(value.ToInt64() & 0xFFFF));
        }

        private static string DescribePointerType(IntPtr wParam)
        {
            return GetPointerType(wParam).ToString();
        }

        private static NativeMethods.PointerInputType GetPointerType(IntPtr wParam)
        {
            NativeMethods.PointerInputType pointerType;
            return NativeMethods.GetPointerType((uint)LowWord(wParam), out pointerType)
                ? pointerType
                : NativeMethods.PointerInputType.Pointer;
        }

        private static string DescribePackedPoint(IntPtr packedPoint)
        {
            var packed = packedPoint.ToInt64();
            var x = (short)(packed & 0xFFFF);
            var y = (short)((packed >> 16) & 0xFFFF);
            return x + "," + y;
        }

        private bool IsPointInDragHeader(IntPtr packedScreenPoint)
        {
            var packed = packedScreenPoint.ToInt64();
            var screenX = (short)(packed & 0xFFFF);
            var screenY = (short)((packed >> 16) & 0xFFFF);
            var point = PointFromScreen(new Point(screenX, screenY));
            return IsPointInElement(expandedDragHandle, point) ||
                   IsPointInElement(compactDragHandle, point);
        }

        private bool IsPointInElement(FrameworkElement element, Point point)
        {
            if (element == null || !element.IsVisible) return false;
            var origin = element.TranslatePoint(new Point(0, 0), this);
            return point.X >= origin.X && point.X <= origin.X + element.ActualWidth &&
                   point.Y >= origin.Y && point.Y <= origin.Y + element.ActualHeight;
        }

        private static bool MovedBeyondThreshold(Point origin, Point current, double threshold)
        {
            var deltaX = current.X - origin.X;
            var deltaY = current.Y - origin.Y;
            return deltaX * deltaX + deltaY * deltaY > threshold * threshold;
        }

        private static void UpdateThicknessChecks(IDictionary<double, Button> items, double width)
        {
            UpdateOptionButtonChecks(items, width);
        }

        private static void UpdateOptionButtonChecks(IDictionary<double, Button> items, double value)
        {
            if (items.Count == 0) return;
            var normalized = items.Keys.OrderBy(candidate => Math.Abs(candidate - value)).First();
            foreach (var pair in items)
                SetButtonActive(pair.Value, Math.Abs(pair.Key - normalized) < 0.01);
        }

        private static bool IsShapeMode(AppMode value)
        {
            return value == AppMode.Rectangle || value == AppMode.Ellipse || value == AppMode.Line || value == AppMode.Arrow;
        }

        private static double ClampRatio(double value) { return Math.Max(0.0, Math.Min(1.0, value)); }
    }
}
