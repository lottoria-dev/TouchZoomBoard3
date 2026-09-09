using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class AppController : IDisposable
    {
        private readonly Application application;
        private readonly UserSettings settings;
        private ControlPanelWindow panel;
        private InteractionOverlayWindow overlay;
        private LiveZoomWindow liveZoom;
        private MiniMapWindow miniMap;
        private readonly InteractiveInputRouter inputRouter = new InteractiveInputRouter();
        private SettingsWindow settingsWindow;
        private TrayService tray;
        private GlobalHotKeyService globalHotKey;
        private DispatcherTimer inputRefreshTimer;
        private DispatcherTimer nativeTouchVerificationTimer;
        private DispatcherTimer idleZoomReleaseTimer;
        private Forms.Screen targetScreen;
        private AppMode mode = AppMode.Pointer;
        private AppMode lastShapeMode = AppMode.Rectangle;
        private readonly bool startedByTouchRefresh;
        private readonly HashSet<string> pendingInputRefreshReasons =
            new HashSet<string>(StringComparer.Ordinal);
        private int pendingInputRefreshSignalCount;
        private long nativeTouchProbe;
        private long wpfTouchProbe;
        private int panelGeneration;
        private int lastNativeTouchContacts = -1;
        private int lastWpfInputDeviceCount = -1;
        private bool softInputRefreshPerformed;
        private bool inputRefreshInProgress;
        private bool touchRefreshRestartRequested;
        private bool touchRefreshRestartGuarded;
        private bool recovering;
        private bool environmentEventsSubscribed;
        private bool disposed;

        internal AppController(Application application, bool startedByTouchRefresh)
        {
            this.application = application;
            this.startedByTouchRefresh = startedByTouchRefresh;
            touchRefreshRestartGuarded = startedByTouchRefresh;
            softInputRefreshPerformed = startedByTouchRefresh;
            settings = UserSettings.Load();
            targetScreen = ResolveInitialScreen();

            inputRefreshTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            inputRefreshTimer.Tick += HandleInputRefreshTimer;

            nativeTouchVerificationTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            nativeTouchVerificationTimer.Tick += HandleNativeTouchVerification;

            idleZoomReleaseTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            idleZoomReleaseTimer.Tick += HandleIdleZoomResourceRelease;
        }

        internal void Start()
        {
            if (settings.VisualDesignResetApplied)
            {
                DebugLog.WriteDiagnostic(
                    "THEME",
                    "3.0 visual settings migration applied; glass strength=" +
                    settings.GlassTintOpacity.ToString("0.000") +
                    ", panelGlassPercent=" + Math.Round(
                        settings.GlassTintOpacity /
                            LiquidGlassTheme.MaximumPanelGlassStrength * 100.0).ToString("0"));
                settings.Save();
            }
            LiquidGlassTheme.Configure(
                settings.ThemeMode,
                settings.PastelTheme,
                settings.GlassTintOpacity,
                settings.UseCustomGlassLightColor,
                settings.CustomGlassLightColorArgb);
            LogInputDeviceSnapshot("startup");
            panel = CreateControlPanel();

            tray = new TrayService(settings.PanelVisible);
            tray.TogglePanelRequested += TogglePanel;
            tray.EndSessionRequested += EndSession;
            tray.EmergencyRecoveryRequested += EmergencyRecover;
            tray.SettingsRequested += ShowSettings;
            tray.AboutRequested += ShowAbout;
            tray.QuitRequested += Quit;

            if (startedByTouchRefresh)
            {
                tray.ShowTouchRefreshRestartedMessage(lastNativeTouchContacts > 0);
            }

            try
            {
                globalHotKey = new GlobalHotKeyService();
                globalHotKey.EmergencyRecoveryRequested += EmergencyRecover;
                globalHotKey.DeviceChangeObserved += () => QueueInputDeviceRefresh("WM_DEVICECHANGE-Monitor");
                tray.SetEmergencyHotKeyAvailable(globalHotKey.IsRegistered);
            }
            catch (Exception exception)
            {
                DebugLog.Write("전역 긴급 복구 단축키 초기화 중 오류가 발생했습니다.", exception);
                tray.SetEmergencyHotKeyAvailable(false);
            }

            if (settings.PanelVisible)
            {
                ShowPanel();
            }
            else
            {
                tray.ShowStartupMessage();
            }
            SystemEvents.PowerModeChanged += HandlePowerModeChanged;
            SystemEvents.DisplaySettingsChanged += HandleDisplaySettingsChanged;
            environmentEventsSubscribed = true;

        }

        private ControlPanelWindow CreateControlPanel()
        {
            var createdPanel = new ControlPanelWindow(settings.TooltipsEnabled);
            panelGeneration++;
            createdPanel.PanelMoved += SavePanelPosition;
            createdPanel.PanelBoundsChanged += () => miniMap?.SetAnchorWindow(createdPanel.WindowHandle);
            createdPanel.SettingsRequested += ShowSettings;
            createdPanel.QuitRequested += Quit;
            createdPanel.ModeRequested += SetMode;
            createdPanel.DrawingColorRequested += SetDrawingColor;
            createdPanel.PenWidthRequested += SetPenWidth;
            createdPanel.HighlighterWidthRequested += SetHighlighterWidth;
            createdPanel.ShapeWidthRequested += SetShapeWidth;
            createdPanel.ZoomLevelRequested += SetZoomLevel;
            createdPanel.QuickZoomRequested += ToggleQuickZoom;
            createdPanel.ResetZoomRequested += ResetZoom;
            createdPanel.MiniMapDockRequested += ToggleMiniMapDock;
            createdPanel.WheelZoomRequested += ZoomBy;
            createdPanel.UndoRequested += UndoAnnotation;
            createdPanel.ClearRequested += ClearAnnotations;
            createdPanel.EndSessionRequested += EndSession;
            createdPanel.HidePanelRequested += HidePanel;
            createdPanel.MagnificationExclusionsChanged += ApplyMagnificationExclusions;
            createdPanel.NativeTouchObserved += HandleNativePanelTouch;
            createdPanel.WpfTouchObserved += HandleWpfPanelTouch;
            createdPanel.DeviceChangeObserved += () => QueueInputDeviceRefresh("WM_DEVICECHANGE");
            createdPanel.FocusRestoreRequested += RestoreExternalForegroundWindow;
            createdPanel.SetScreenName(targetScreen);
            createdPanel.SetGlassTintOpacity(settings.GlassTintOpacity);
            createdPanel.SetDrawingColor(DrawingStyleKind.Pen, ColorFromArgb(settings.PenColorArgb));
            createdPanel.SetDrawingColor(DrawingStyleKind.Highlighter, ColorFromArgb(settings.HighlighterColorArgb));
            createdPanel.SetDrawingColor(DrawingStyleKind.Shape, ColorFromArgb(settings.ShapeColorArgb));
            createdPanel.SetPenWidth(settings.PenWidth);
            createdPanel.SetHighlighterWidth(settings.HighlighterWidth);
            createdPanel.SetShapeWidth(settings.ShapeWidth);
            DebugLog.WriteDiagnostic("INPUT-REFRESH", "panelCreated generation=" + panelGeneration);
            return createdPanel;
        }

        private Forms.Screen ResolveInitialScreen()
        {
            var screens = Forms.Screen.AllScreens;
            var saved = screens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, settings.TargetDeviceName, StringComparison.OrdinalIgnoreCase));

            if (saved != null)
            {
                return saved;
            }

            return screens.FirstOrDefault(screen => !screen.Primary) ?? Forms.Screen.PrimaryScreen;
        }

        private void TogglePanel()
        {
            if (panel.IsVisible)
            {
                HidePanel();
            }
            else
            {
                ShowPanel();
            }
        }

        private void ShowPanel()
        {
            panel.SetCompactMode(true);
            panel.SetScreenName(targetScreen);
            panel.ShowForScreen(targetScreen, settings.PanelXRatio, settings.PanelYRatio);
            settings.PanelVisible = true;
            settings.Save();
            tray.SetPanelVisible(true);
        }

        private void HidePanel()
        {
            EndSession();
            panel.Hide();
            settings.PanelVisible = false;
            settings.Save();
            tray.SetPanelVisible(false);
        }

        private void CycleScreen()
        {
            var screens = Forms.Screen.AllScreens;
            if (screens.Length == 0)
            {
                return;
            }

            EndSession();
            var currentIndex = Array.FindIndex(screens, screen => screen.DeviceName == targetScreen.DeviceName);
            targetScreen = screens[(currentIndex + 1 + screens.Length) % screens.Length];
            settings.TargetDeviceName = targetScreen.DeviceName;
            settings.Save();
            panel.SetScreenName(targetScreen);
            panel.ShowForScreen(targetScreen, settings.PanelXRatio, settings.PanelYRatio);
        }

        private void ResetPanelPosition()
        {
            settings.PanelXRatio = 1.0;
            settings.PanelYRatio = 0.08;
            settings.Save();
            if (panel.IsVisible)
            {
                panel.ShowForScreen(targetScreen, settings.PanelXRatio, settings.PanelYRatio);
            }
        }

        private void SavePanelPosition()
        {
            panel.GetPositionRatios(targetScreen, out var xRatio, out var yRatio);
            settings.PanelXRatio = xRatio;
            settings.PanelYRatio = yRatio;
            settings.Save();
        }

        private void EnsureOverlay()
        {
            if (overlay != null)
            {
                return;
            }

            overlay = new InteractionOverlayWindow();
            overlay.SetDrawingColor(DrawingStyleKind.Pen, ColorFromArgb(settings.PenColorArgb));
            overlay.SetDrawingColor(DrawingStyleKind.Highlighter, ColorFromArgb(settings.HighlighterColorArgb));
            overlay.SetDrawingColor(DrawingStyleKind.Shape, ColorFromArgb(settings.ShapeColorArgb));
            overlay.SetPenWidth(settings.PenWidth);
            overlay.SetHighlighterWidth(settings.HighlighterWidth);
            overlay.SetShapeWidth(settings.ShapeWidth);
            overlay.SetDiagnosticContext(
                liveZoom?.IsZoomVisible == true ? liveZoom.ZoomFactor : 1.0,
                NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1));
            overlay.PanRequested += (x, y) => liveZoom?.PanBy(x, y);
            overlay.ZoomRequested += ZoomBy;
            overlay.ZoomAtRequested += ZoomAt;
            overlay.ExitRequested += EndSession;
            overlay.PanelNeedsFront += KeepPanelAboveOverlay;
            overlay.ContentPointerRequested += RouteContentPointer;
            overlay.ContentScrollRequested += RouteContentScroll;
        }

        private void EnsureLiveZoom()
        {
            if (liveZoom == null)
            {
                liveZoom = new LiveZoomWindow();
                liveZoom.ViewChanged += HandleZoomViewChanged;
            }
        }

        private void EnsureMiniMap()
        {
            if (miniMap != null)
            {
                return;
            }

            miniMap = new MiniMapWindow();
            miniMap.NavigateRequested += (x, y, immediate) =>
            {
                liveZoom?.SetViewportCenter(x, y, immediate);
            };
            miniMap.ZoomRequested += ZoomBy;
        }

        private void SetMode(AppMode newMode)
        {
            var previousMode = mode;
            if (IsShapeMode(newMode)) lastShapeMode = newMode;
            DebugLog.WriteDiagnostic("TOOL-MODE", "requested=" + newMode +
                ", previous=" + previousMode + ", lastShape=" + lastShapeMode);
            if (newMode == AppMode.Pointer)
            {
                mode = AppMode.Pointer;
                panel.SetMode(mode);
                UpdatePointerOverlayPresentation("SetMode-Pointer");
                return;
            }

            if (newMode == AppMode.Hand && (liveZoom == null || !liveZoom.IsZoomVisible))
            {
                StartZoom(settings.DefaultZoom);
            }

            EnsureOverlay();
            mode = newMode;
            overlay.SetMode(mode);
            overlay.SetInputPassthrough(false);
            ShowOverlay();
            panel.SetMode(mode);
        }

        private void UndoAnnotation()
        {
            overlay?.Undo();
            if (mode == AppMode.Pointer)
                UpdatePointerOverlayPresentation("Undo");
        }

        private void ClearAnnotations()
        {
            overlay?.ClearAnnotations();
            if (mode == AppMode.Pointer)
                UpdatePointerOverlayPresentation("Clear");
        }

        private void UpdatePointerOverlayPresentation(string reason)
        {
            var zoomVisible = liveZoom?.IsZoomVisible == true;
            var hasAnnotations = overlay?.HasAnnotations == true;

            if (zoomVisible)
            {
                EnsureOverlay();
                overlay.SetMode(AppMode.Pointer);
                overlay.SetInputPassthrough(false);
                ShowOverlay();
            }
            else if (hasAnnotations)
            {
                overlay.SetMode(AppMode.Pointer);
                overlay.SetInputPassthrough(true);
                ShowOverlay();
                panel.SetZoom(1.0);
            }
            else
            {
                DetachWindowLayerChain();
                overlay?.SetInputPassthrough(false);
                overlay?.Hide();
                panel.SetZoom(1.0);
                panel.SetScreenName(targetScreen);
                panel.ClampToCurrentScreen();
                panel.BringToFrontWithoutActivate();
            }

            DebugLog.WriteDiagnostic("OVERLAY-VISIBILITY",
                "pointerPresentation reason=" + reason +
                ", zoomVisible=" + zoomVisible +
                ", annotations=" + hasAnnotations +
                ", overlayVisible=" + (overlay?.IsVisible == true) +
                ", inputPassthrough=" + (overlay?.InputPassthrough == true));
        }

        private void SetZoomLevel(double zoom)
        {
            DebugLog.WriteDiagnostic("APP-ZOOM", "배율 메뉴 요청 zoom=" + zoom);
            StartZoom(Math.Max(1.25, Math.Min(5.0, zoom)));
        }

        private void ToggleQuickZoom()
        {
            if (liveZoom?.IsZoomVisible == true)
            {
                DebugLog.WriteDiagnostic("APP-ZOOM", "빠른 확대 버튼: 100% 복귀 요청");
                ResetZoom();
                return;
            }

            var zoom = Math.Max(1.25, Math.Min(5.0, settings.DefaultZoom));
            DebugLog.WriteDiagnostic("APP-ZOOM", "빠른 확대 버튼: 기본 배율 시작 요청 zoom=" + zoom);
            StartZoom(zoom);
        }

        private void ZoomBy(double factor)
        {
            if (liveZoom == null || !liveZoom.IsZoomVisible)
            {
                if (factor > 1.0)
                {
                    StartZoom(factor);
                }
                return;
            }

            var target = liveZoom.ZoomFactor * factor;
            liveZoom.SetZoom(Math.Max(1.0, Math.Min(5.0, target)));
            overlay?.SetDiagnosticContext(
                liveZoom.ZoomFactor,
                NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1));
            panel.SetZoom(liveZoom.ZoomFactor);
            KeepPanelAboveOverlay();
        }

        private void ZoomAt(double factor, double xDip, double yDip)
        {
            if (liveZoom == null || !liveZoom.IsZoomVisible)
            {
                if (factor > 1.0)
                {
                    StartZoom(factor);
                }
                return;
            }

            var target = Math.Max(1.0, Math.Min(5.0, liveZoom.ZoomFactor * factor));
            liveZoom.ZoomAtViewportPoint(target, xDip, yDip);
            overlay?.SetDiagnosticContext(
                liveZoom.ZoomFactor,
                NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1));
            panel.SetZoom(liveZoom.ZoomFactor);
            KeepPanelAboveOverlay();
        }

        private void StartZoom(double zoom)
        {
            idleZoomReleaseTimer.Stop();
            DebugLog.WriteDiagnostic("APP-ZOOM", "StartZoom 진입 zoom=" + zoom +
                ", screen=" + targetScreen.DeviceName + ", mode=" + mode);
            try
            {
                EnsureLiveZoom();
                EnsureOverlay();
                EnsureMiniMap();
                liveZoom.ShowForScreen(targetScreen, zoom);
                miniMap.ShowForScreen(targetScreen, panel.WindowHandle);
                miniMap.UpdateViewport(liveZoom.ScreenRectangle, liveZoom.SourceRectangle);
                panel.SetZoomSafeMoveEnabled(true);
            }
            catch (Exception exception)
            {
                DebugLog.Write("라이브 줌을 시작하지 못했습니다.", exception);
                panel.SetZoomSafeMoveEnabled(false);
                miniMap?.Hide();
                liveZoom?.StopZoom();
                MessageBox.Show(
                    "라이브 줌을 시작하지 못했습니다.\n\n" + exception.Message,
                    "TouchZoomBoard3",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            overlay.SetMode(mode);
            overlay.SetInputPassthrough(false);
            overlay.SetDiagnosticContext(
                liveZoom.ZoomFactor,
                NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1));
            ShowOverlay();
            panel.SetMode(mode);
            panel.SetZoom(liveZoom.ZoomFactor);
            ApplyMagnificationExclusions();
            DebugLog.WriteDiagnostic("APP-ZOOM", "StartZoom 완료 zoom=" + liveZoom.ZoomFactor +
                ", liveZoomVisible=" + liveZoom.IsZoomVisible + ", overlayVisible=" + overlay.IsVisible);
        }

        private void ResetZoom()
        {
            DebugLog.WriteDiagnostic("APP-ZOOM", "ResetZoom 진입 mode=" + mode);
            panel.SetZoomSafeMoveEnabled(false);
            overlay?.SetOwnerWindow(IntPtr.Zero);
            inputRouter.CancelPointer(new NativeMethods.POINT());
            miniMap?.Hide();
            liveZoom?.StopZoom();
            overlay?.SetDiagnosticContext(
                1.0,
                NativeMethods.GetScaleForPoint(targetScreen.Bounds.Left + 1, targetScreen.Bounds.Top + 1));
            panel.SetZoom(1.0);

            if (mode == AppMode.Hand || mode == AppMode.Pointer)
            {
                panel.SetOwnerWindow(IntPtr.Zero);
                mode = AppMode.Pointer;
                panel.SetMode(mode);
                UpdatePointerOverlayPresentation("ResetZoom");
            }
            else if (overlay?.IsVisible == true)
            {
                panel.SetOwnerWindow(overlay.WindowHandle);
                KeepPanelAboveOverlay();
            }
            DebugLog.WriteDiagnostic("APP-ZOOM", "ResetZoom 완료 mode=" + mode);
            ScheduleIdleZoomResourceRelease();
        }

        private void ScheduleIdleZoomResourceRelease()
        {
            idleZoomReleaseTimer.Stop();
            if (disposed || recovering || mode != AppMode.Pointer ||
                liveZoom?.IsZoomVisible == true ||
                (liveZoom == null && miniMap == null))
            {
                return;
            }

            idleZoomReleaseTimer.Start();
            DebugLog.WriteDiagnostic("ZOOM-RESOURCE", "idleReleaseScheduled seconds=60");
        }

        private void HandleIdleZoomResourceRelease(object sender, EventArgs args)
        {
            idleZoomReleaseTimer.Stop();
            if (disposed || recovering || mode != AppMode.Pointer ||
                liveZoom?.IsZoomVisible == true)
            {
                DebugLog.WriteDiagnostic("ZOOM-RESOURCE", "idleReleaseCancelled stateChanged=true");
                return;
            }

            var released = DisposeSessionZoomResources();
            ApplyMagnificationExclusions();
            DebugLog.WriteDiagnostic("ZOOM-RESOURCE", "idleReleaseCompleted=" + released);
        }

        private void ShowOverlay()
        {
            overlay.ShowForScreen(targetScreen);
            panel.SetScreenName(targetScreen);
            panel.ClampToCurrentScreen();
            ApplyWindowLayerChain();
            overlay.ActivateInputSurface();
            KeepPanelAboveOverlay();
            ApplyMagnificationExclusions();
        }

        private void ApplyWindowLayerChain()
        {
            if (overlay?.IsVisible != true)
            {
                return;
            }

            var zoomOwner = liveZoom?.IsZoomVisible == true
                ? liveZoom.WindowHandle
                : IntPtr.Zero;
            overlay.SetOwnerWindow(zoomOwner);
            if (miniMap?.IsVisible == true)
            {
                miniMap.SetOwnerWindow(overlay.WindowHandle);
                panel.SetOwnerWindow(miniMap.InputHandle);
            }
            else
            {
                panel.SetOwnerWindow(overlay.WindowHandle);
            }
        }

        private void DetachWindowLayerChain()
        {
            if (settingsWindow?.IsVisible == true)
            {
                NativeMethods.SetOwnerWindow(settingsWindow.WindowHandle, IntPtr.Zero);
            }
            panel?.SetOwnerWindow(IntPtr.Zero);
            miniMap?.SetOwnerWindow(IntPtr.Zero);
            overlay?.SetOwnerWindow(IntPtr.Zero);
        }

        private void KeepPanelAboveOverlay()
        {
            if (panel?.IsVisible != true)
            {
                return;
            }

            panel.ClampToCurrentScreen();
            miniMap?.SetAnchorWindow(panel.WindowHandle);
            miniMap?.BringToFront();
            panel.BringToFrontWithoutActivate();
        }

        private void ApplyMagnificationExclusions()
        {
            if (liveZoom?.IsZoomVisible == true)
            {
                var excludedWindows = new List<IntPtr>
                {
                    panel.WindowHandle,
                    overlay?.WindowHandle ?? IntPtr.Zero,
                    miniMap?.PreviewHandle ?? IntPtr.Zero,
                    miniMap?.InputHandle ?? IntPtr.Zero
                };
                excludedWindows.AddRange(panel.GetPopupWindowHandles());
                if (settingsWindow?.IsVisible == true)
                {
                    excludedWindows.Add(settingsWindow.WindowHandle);
                }
                liveZoom.SetExcludedWindows(excludedWindows.ToArray());
                var miniMapExclusions = new List<IntPtr>(excludedWindows)
                {
                    liveZoom.WindowHandle
                };
                miniMap?.SetExcludedWindows(miniMapExclusions.ToArray());
            }
        }

        private void HandleZoomViewChanged(NativeMethods.RECT screenBounds, NativeMethods.RECT sourceBounds)
        {
            miniMap?.UpdateViewport(screenBounds, sourceBounds);
        }

        private void ToggleMiniMapDock()
        {
            miniMap?.ToggleDock();
            KeepPanelAboveOverlay();
        }

        private void RouteContentPointer(ContentPointerAction action, double xDip, double yDip)
        {
            if (liveZoom?.TryMapViewportPoint(xDip, yDip, out var sourcePoint) != true)
            {
                return;
            }

            switch (action)
            {
                case ContentPointerAction.Click:
                    inputRouter.Click(sourcePoint);
                    break;
                case ContentPointerAction.Begin:
                    inputRouter.BeginPointer(sourcePoint);
                    break;
                case ContentPointerAction.Move:
                    inputRouter.MovePointer(sourcePoint);
                    break;
                case ContentPointerAction.End:
                    inputRouter.EndPointer(sourcePoint);
                    break;
                default:
                    inputRouter.CancelPointer(sourcePoint);
                    break;
            }
        }

        private void RouteContentScroll(double xDip, double yDip, int wheelDelta)
        {
            if (liveZoom?.TryMapViewportPoint(xDip, yDip, out var sourcePoint) == true)
            {
                inputRouter.Scroll(sourcePoint, wheelDelta);
            }
        }

        private void EndSession()
        {
            RecoverToNormalScreen(false);
        }

        private void EmergencyRecover()
        {
            RecoverToNormalScreen(true);
        }

        internal void RecoverFromUnhandledError(Exception exception)
        {
            DebugLog.Write("처리되지 않은 UI 오류가 발생했습니다.", exception);
            RecoverToNormalScreen(false);
        }

        private void RecoverToNormalScreen(bool showConfirmation)
        {
            if (recovering || disposed)
            {
                return;
            }

            idleZoomReleaseTimer.Stop();
            recovering = true;
            var hadError = false;
            var retiredHandles = CaptureSessionWindowHandles();
            DebugLog.WriteDiagnostic("COMPOSITION", "teardownBegin handles=" + FormatHandles(retiredHandles));
            try
            {
                DetachWindowLayerChain();
                panel.SetZoomSafeMoveEnabled(false);
                try
                {
                    inputRouter.CancelPointer(new NativeMethods.POINT());
                    miniMap?.Hide();
                    liveZoom?.StopZoom();
                }
                catch (Exception exception)
                {
                    hadError = true;
                    DebugLog.Write("라이브 확대 화면 종료 중 오류가 발생했습니다.", exception);
                }

                var retiredOverlay = overlay;
                overlay = null;
                if (retiredOverlay != null)
                {
                    try
                    {
                        if (!retiredOverlay.EmergencyReset())
                        {
                            hadError = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        hadError = true;
                        DebugLog.Write("입력 화면 복구 중 오류가 발생했습니다.", exception);
                    }
                    finally
                    {
                        try
                        {
                            // 투명 WPF 창의 이전 합성 표면까지 폐기한다. 다음 판서 진입은
                            // 새 HWND와 InkCanvas를 만들기 때문에 종료 전 획이 다시 나타나지 않는다.
                            retiredOverlay.Shutdown();
                        }
                        catch (Exception exception)
                        {
                            hadError = true;
                            DebugLog.Write("입력 화면 폐기 중 오류가 발생했습니다.", exception);
                        }
                    }
                }

                if (!DisposeSessionZoomResources())
                {
                    hadError = true;
                }

                RefreshDesktopAfterSessionTeardown(retiredHandles);

                mode = AppMode.Pointer;

                try
                {
                    panel.SetMode(mode);
                    panel.SetZoom(1.0);
                    panel.SetCompactMode(true);
                    if (panel.IsVisible)
                    {
                        panel.SetScreenName(targetScreen);
                        panel.ClampToCurrentScreen();
                        panel.BringToFrontWithoutActivate();
                    }
                }
                catch (Exception exception)
                {
                    hadError = true;
                    DebugLog.Write("컨트롤 패널 복구 중 오류가 발생했습니다.", exception);
                }
            }
            finally
            {
                recovering = false;
            }

            if (showConfirmation)
            {
                tray?.ShowRecoveryMessage(hadError);
            }
        }

        private IntPtr[] CaptureSessionWindowHandles()
        {
            return new[]
            {
                overlay?.WindowHandle ?? IntPtr.Zero,
                liveZoom?.WindowHandle ?? IntPtr.Zero,
                liveZoom?.MagnifierHandle ?? IntPtr.Zero,
                miniMap?.PreviewHandle ?? IntPtr.Zero,
                miniMap?.InputHandle ?? IntPtr.Zero,
                miniMap?.MagnifierHandle ?? IntPtr.Zero
            };
        }

        private void RefreshDesktopAfterSessionTeardown(IntPtr[] retiredHandles)
        {
            try
            {
                // Close/Dispose가 예약한 WPF 렌더링 및 HWND 정리 작업을 먼저 처리한다.
                application.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));

                var bounds = (targetScreen ?? Forms.Screen.PrimaryScreen).Bounds;
                var redrawRectangle = new NativeMethods.RECT(
                    bounds.Left,
                    bounds.Top,
                    bounds.Right,
                    bounds.Bottom);
                int firstDwmResult;
                int secondDwmResult;
                int redrawError;
                double elapsedMilliseconds;
                var redrawSucceeded = NativeMethods.FlushAndRedrawDesktopRegion(
                    redrawRectangle,
                    out firstDwmResult,
                    out secondDwmResult,
                    out redrawError,
                    out elapsedMilliseconds);
                var aliveHandles = retiredHandles.Count(handle =>
                    handle != IntPtr.Zero && NativeMethods.IsWindow(handle));

                DebugLog.WriteDiagnostic("COMPOSITION", "teardownRefresh redraw=" + redrawSucceeded +
                    ", redrawError=" + redrawError +
                    ", dwmBefore=0x" + firstDwmResult.ToString("X8") +
                    ", dwmAfter=0x" + secondDwmResult.ToString("X8") +
                    ", elapsedMs=" + elapsedMilliseconds.ToString("0.00") +
                    ", retiredAlive=" + aliveHandles + "/" +
                    retiredHandles.Count(handle => handle != IntPtr.Zero) +
                    ", bounds=" + bounds);
            }
            catch (Exception exception)
            {
                // 다시 그리기 실패가 정상 화면 복구 자체를 막아서는 안 된다.
                DebugLog.Write("확대 종료 후 화면 합성 정리 중 오류가 발생했습니다.", exception);
                DebugLog.WriteDiagnostic("COMPOSITION", "teardownRefresh exception=" + exception.GetType().Name);
            }
        }

        private static string FormatHandles(IEnumerable<IntPtr> handles)
        {
            return string.Join(",", handles
                .Where(handle => handle != IntPtr.Zero)
                .Select(handle => "0x" + handle.ToInt64().ToString("X")));
        }

        private bool DisposeSessionZoomResources()
        {
            idleZoomReleaseTimer.Stop();
            var succeeded = true;
            var runtimeUsersBefore = MagnifierHost.RuntimeUsers;
            var retiredMiniMap = miniMap;
            var retiredLiveZoom = liveZoom;
            miniMap = null;
            liveZoom = null;

            if (retiredLiveZoom != null)
            {
                retiredLiveZoom.ViewChanged -= HandleZoomViewChanged;
            }

            try
            {
                retiredMiniMap?.Dispose();
            }
            catch (Exception exception)
            {
                succeeded = false;
                DebugLog.Write("미니맵 자원 폐기 중 오류가 발생했습니다.", exception);
            }

            try
            {
                retiredLiveZoom?.Dispose();
            }
            catch (Exception exception)
            {
                succeeded = false;
                DebugLog.Write("확대 자원 폐기 중 오류가 발생했습니다.", exception);
            }

            DebugLog.WriteDiagnostic("ZOOM-RESOURCE", "sessionDispose=" + succeeded +
                ", runtimeUsers=" + runtimeUsersBefore + "->" + MagnifierHost.RuntimeUsers);
            return succeeded;
        }

        private void SetDrawingColor(DrawingStyleKind kind, Color color)
        {
            var argb = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
            switch (kind)
            {
                case DrawingStyleKind.Pen:
                    settings.PenColorArgb = argb;
                    break;
                case DrawingStyleKind.Highlighter:
                    settings.HighlighterColorArgb = argb;
                    break;
                default:
                    settings.ShapeColorArgb = argb;
                    break;
            }
            panel.SetDrawingColor(kind, color);
            overlay?.SetDrawingColor(kind, color);
            settings.Save();
            SelectDrawingStyleTool(kind, "color");
        }

        private void SetPenWidth(double width)
        {
            settings.PenWidth = Math.Max(2.0, Math.Min(12.0, width));
            panel.SetPenWidth(settings.PenWidth);
            overlay?.SetPenWidth(settings.PenWidth);
            settings.Save();
            SelectDrawingStyleTool(DrawingStyleKind.Pen, "width");
        }

        private void SetHighlighterWidth(double width)
        {
            settings.HighlighterWidth = Math.Max(8.0, Math.Min(32.0, width));
            panel.SetHighlighterWidth(settings.HighlighterWidth);
            overlay?.SetHighlighterWidth(settings.HighlighterWidth);
            settings.Save();
            SelectDrawingStyleTool(DrawingStyleKind.Highlighter, "width");
        }

        private void SetShapeWidth(double width)
        {
            settings.ShapeWidth = Math.Max(2.0, Math.Min(8.0, width));
            panel.SetShapeWidth(settings.ShapeWidth);
            overlay?.SetShapeWidth(settings.ShapeWidth);
            settings.Save();
            SelectDrawingStyleTool(DrawingStyleKind.Shape, "width");
        }

        private void SelectDrawingStyleTool(DrawingStyleKind kind, string reason)
        {
            AppMode requestedMode;
            switch (kind)
            {
                case DrawingStyleKind.Pen:
                    requestedMode = AppMode.Pen;
                    break;
                case DrawingStyleKind.Highlighter:
                    requestedMode = AppMode.Highlighter;
                    break;
                default:
                    requestedMode = lastShapeMode;
                    break;
            }

            DebugLog.WriteDiagnostic("TOOL-STYLE", "kind=" + kind +
                ", reason=" + reason + ", select=" + requestedMode);
            SetMode(requestedMode);
        }

        private static bool IsShapeMode(AppMode value)
        {
            return value == AppMode.Rectangle || value == AppMode.Ellipse ||
                   value == AppMode.Line || value == AppMode.Arrow;
        }

        private void ShowSettings()
        {
            if (settingsWindow?.IsVisible == true)
            {
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new SettingsWindow(settings);
            settingsWindow.Applied += ApplySettings;
            settingsWindow.ResetPositionRequested += ResetPanelPosition;
            settingsWindow.Closed += (sender, args) =>
            {
                settingsWindow = null;
                ApplyMagnificationExclusions();
            };
            settingsWindow.Show();
            if (panel?.IsVisible == true)
            {
                NativeMethods.SetOwnerWindow(settingsWindow.WindowHandle, panel.WindowHandle);
            }
            ApplyMagnificationExclusions();
        }

        private void ApplySettings()
        {
            settings.ThemeMode = UserSettings.ResolveSystemThemeMode();
            var themeChanged = settings.ThemeMode != LiquidGlassTheme.Mode ||
                               settings.PastelTheme != LiquidGlassTheme.Pastel ||
                               settings.UseCustomGlassLightColor !=
                                   LiquidGlassTheme.UseCustomGlassLightColor ||
                               (settings.UseCustomGlassLightColor &&
                                ColorFromArgb(settings.CustomGlassLightColorArgb) !=
                                    LiquidGlassTheme.GlassLightColor);
            var glassTintChanged = Math.Abs(
                settings.GlassTintOpacity - LiquidGlassTheme.GlassTintOpacity) > 0.001;
            var selectedScreen = ResolveInitialScreen();
            var screenChanged = !string.Equals(
                selectedScreen.DeviceName,
                targetScreen.DeviceName,
                StringComparison.OrdinalIgnoreCase);
            if (screenChanged)
            {
                EndSession();
                targetScreen = selectedScreen;
            }

            LiquidGlassTheme.Configure(
                settings.ThemeMode,
                settings.PastelTheme,
                settings.GlassTintOpacity,
                settings.UseCustomGlassLightColor,
                settings.CustomGlassLightColorArgb);
            tray?.ApplyTheme();
            miniMap?.ApplyTheme(settings.GlassTintOpacity);
            if (themeChanged || glassTintChanged)
            {
                RecreatePanelForThemeChange();
            }
            else
            {
                panel.SetGlassTintOpacity(settings.GlassTintOpacity);
            }

            panel.SetTooltipsEnabled(settings.TooltipsEnabled);
            panel.SetScreenName(targetScreen);
            if (panel.IsVisible)
            {
                if (screenChanged)
                {
                    panel.ShowForScreen(targetScreen, settings.PanelXRatio, settings.PanelYRatio);
                }
                else
                {
                    panel.ClampToCurrentScreen();
                    panel.BringToFrontWithoutActivate();
                }
            }
            DebugLog.WriteDiagnostic("THEME", "applied design=MinimalClearLiquidGlass" +
                ", mode=fixed-light" +
                ", glassLight=" + (settings.UseCustomGlassLightColor
                    ? "custom"
                    : settings.PastelTheme.ToString()) +
                ", panelGlassStrength=" + settings.GlassTintOpacity.ToString("0.000") +
                ", panelGlassPercent=" + Math.Round(
                    settings.GlassTintOpacity /
                        LiquidGlassTheme.MaximumPanelGlassStrength * 100.0).ToString("0") +
                ", rootUiOpacity=1.00" +
                ", panelRecreated=" + (themeChanged || glassTintChanged));
        }

        private void RecreatePanelForThemeChange()
        {
            var oldPanel = panel;
            var wasVisible = oldPanel?.IsVisible == true || settings.PanelVisible;
            var wasCompact = oldPanel?.IsCompactMode != false;
            var oldHandle = oldPanel?.WindowHandle ?? IntPtr.Zero;

            try
            {
                if (oldPanel?.IsVisible == true) SavePanelPosition();
                DetachWindowLayerChain();
                oldPanel?.Shutdown();
                application.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));

                panel = CreateControlPanel();
                panel.SetCompactMode(wasCompact);
                panel.SetMode(mode);
                panel.SetZoom(liveZoom?.IsZoomVisible == true ? liveZoom.ZoomFactor : 1.0);
                if (wasVisible)
                {
                    panel.ShowForScreen(targetScreen, settings.PanelXRatio, settings.PanelYRatio);
                    settings.PanelVisible = true;
                }

                if (settingsWindow?.IsVisible == true)
                    NativeMethods.SetOwnerWindow(settingsWindow.WindowHandle, panel.WindowHandle);
                ApplyWindowLayerChain();
                ApplyMagnificationExclusions();
                DebugLog.WriteDiagnostic("THEME", "panelRecreated oldHandle=0x" +
                    oldHandle.ToInt64().ToString("X") +
                    ", newHandle=0x" + panel.WindowHandle.ToInt64().ToString("X") +
                    ", visible=" + wasVisible +
                    ", compact=" + wasCompact +
                    ", mode=" + mode);
            }
            catch (Exception exception)
            {
                DebugLog.Write("유리 재질 적용을 위한 패널 재생성 중 오류가 발생했습니다.", exception);
            }
        }

        private void HandlePowerModeChanged(object sender, PowerModeChangedEventArgs args)
        {
            DebugLog.WriteDiagnostic("ENVIRONMENT", "PowerModeChanged=" + args.Mode);
            if (args.Mode == PowerModes.Resume)
            {
                QueueWindowEnvironmentRepair();
                QueueInputDeviceRefresh("PowerResume");
            }
        }

        private void HandleDisplaySettingsChanged(object sender, EventArgs args)
        {
            DebugLog.WriteDiagnostic("ENVIRONMENT", "DisplaySettingsChanged");
            QueueWindowEnvironmentRepair();
            QueueInputDeviceRefresh("DisplaySettingsChanged");
        }

        private int LogInputDeviceSnapshot(string reason)
        {
            var wpfDeviceCount = 0;
            try
            {
                foreach (TabletDevice device in Tablet.TabletDevices)
                {
                    wpfDeviceCount++;
                    DebugLog.WriteDiagnostic("INPUT-DEVICE", "reason=" + reason +
                        ", name=" + device.Name + ", type=" + device.Type);
                }
            }
            catch (Exception exception)
            {
                DebugLog.Write("입력 장치 진단 정보를 읽지 못했습니다.", exception);
            }

            int digitizerFlags;
            int nativeTouchContacts;
            try
            {
                digitizerFlags = NativeMethods.GetSystemMetrics(NativeMethods.SM_DIGITIZER);
                nativeTouchContacts = NativeMethods.GetSystemMetrics(NativeMethods.SM_MAXIMUMTOUCHES);
            }
            catch (Exception exception)
            {
                digitizerFlags = -1;
                nativeTouchContacts = -1;
                DebugLog.Write("네이티브 터치 장치 정보를 읽지 못했습니다.", exception);
            }

            lastNativeTouchContacts = nativeTouchContacts;
            lastWpfInputDeviceCount = wpfDeviceCount;
            DebugLog.WriteDiagnostic("INPUT-REFRESH", "snapshot reason=" + reason +
                ", wpfDevices=" + wpfDeviceCount +
                ", nativeContacts=" + nativeTouchContacts +
                ", digitizerFlags=0x" + digitizerFlags.ToString("X") +
                ", panelGeneration=" + panelGeneration +
                ", restarted=" + startedByTouchRefresh);
            return wpfDeviceCount;
        }

        private void QueueInputDeviceRefresh(string reason)
        {
            if (disposed || touchRefreshRestartRequested) return;
            if (!application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.BeginInvoke(new Action(() => QueueInputDeviceRefresh(reason)));
                return;
            }

            var startsNewBurst = pendingInputRefreshSignalCount == 0;
            pendingInputRefreshReasons.Add(string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
            pendingInputRefreshSignalCount++;
            inputRefreshTimer.Stop();
            inputRefreshTimer.Start();
            if (startsNewBurst)
            {
                DebugLog.WriteDiagnostic("INPUT-REFRESH", "changeBurstStarted reason=" + reason);
            }
        }

        private void HandleInputRefreshTimer(object sender, EventArgs args)
        {
            inputRefreshTimer.Stop();
            var reason = pendingInputRefreshReasons.Count == 0
                ? "unknown"
                : string.Join("+", pendingInputRefreshReasons.OrderBy(value => value));
            var signalCount = pendingInputRefreshSignalCount;
            pendingInputRefreshReasons.Clear();
            pendingInputRefreshSignalCount = 0;
            var previousNativeTouchContacts = lastNativeTouchContacts;
            var previousWpfInputDeviceCount = lastWpfInputDeviceCount;
            LogInputDeviceSnapshot(reason + "; signals=" + signalCount);
            var deviceStateChanged = previousNativeTouchContacts != lastNativeTouchContacts ||
                previousWpfInputDeviceCount != lastWpfInputDeviceCount;
            var forceRefresh = reason.IndexOf("PowerResume", StringComparison.Ordinal) >= 0;

            // 화면 연결, 절전 복귀, USB 장치 변경 뒤에 터치 하드웨어가 보이면
            // 오래 살아 있던 WPF 패널 HWND를 먼저 새로 만들어 입력 승격 경로를 갱신한다.
            if (lastNativeTouchContacts > 0 && (deviceStateChanged || forceRefresh))
            {
                softInputRefreshPerformed = true;
                RecreatePanelForInputRefresh(reason);
            }
            else if (lastNativeTouchContacts > 0)
            {
                DebugLog.WriteDiagnostic("INPUT-REFRESH", "stableDeviceState; panel recreation skipped" +
                    ", signals=" + signalCount +
                    ", wpfDevices=" + lastWpfInputDeviceCount +
                    ", nativeContacts=" + lastNativeTouchContacts);
            }
            else
            {
                DebugLog.WriteDiagnostic("INPUT-REFRESH", "panel recreation deferred; native touch not reported");
            }
        }

        private void HandleNativePanelTouch()
        {
            if (disposed || touchRefreshRestartRequested) return;
            nativeTouchProbe++;
            nativeTouchVerificationTimer.Stop();
            nativeTouchVerificationTimer.Start();
            DebugLog.WriteDiagnostic("INPUT-REFRESH", "nativeTouch probe=" + nativeTouchProbe +
                ", panelGeneration=" + panelGeneration +
                ", softRefresh=" + softInputRefreshPerformed);
        }

        private void HandleWpfPanelTouch()
        {
            var shouldLog = softInputRefreshPerformed || wpfTouchProbe < nativeTouchProbe;
            wpfTouchProbe = nativeTouchProbe;
            nativeTouchVerificationTimer.Stop();
            softInputRefreshPerformed = false;
            touchRefreshRestartGuarded = false;
            if (shouldLog)
            {
                DebugLog.WriteDiagnostic("INPUT-REFRESH", "wpfPromotionConfirmed probe=" + wpfTouchProbe +
                    ", panelGeneration=" + panelGeneration);
            }
        }

        private void HandleNativeTouchVerification(object sender, EventArgs args)
        {
            nativeTouchVerificationTimer.Stop();
            if (disposed || touchRefreshRestartRequested || wpfTouchProbe >= nativeTouchProbe) return;

            DebugLog.WriteDiagnostic("INPUT-REFRESH", "nativeWithoutWpf probe=" + nativeTouchProbe +
                ", panelGeneration=" + panelGeneration +
                ", softRefresh=" + softInputRefreshPerformed +
                ", restarted=" + startedByTouchRefresh);

            if (!softInputRefreshPerformed)
            {
                softInputRefreshPerformed = true;
                RecreatePanelForInputRefresh("NativeTouchWithoutWpf");
                tray?.ShowTouchRefreshPanelMessage();
                return;
            }

            if (touchRefreshRestartGuarded)
            {
                DebugLog.WriteDiagnostic("INPUT-REFRESH", "restartBlocked loopGuard=true");
                tray?.ShowTouchRefreshFailedMessage();
                return;
            }

            RequestTouchRefreshRestart();
        }

        private void RecreatePanelForInputRefresh(string reason)
        {
            if (disposed || inputRefreshInProgress || touchRefreshRestartRequested) return;
            inputRefreshInProgress = true;
            var oldPanel = panel;
            var wasVisible = oldPanel?.IsVisible == true || settings.PanelVisible;
            var wasCompact = oldPanel?.IsCompactMode != false;
            var oldHandle = oldPanel?.WindowHandle ?? IntPtr.Zero;

            try
            {
                if (oldPanel?.IsVisible == true)
                {
                    SavePanelPosition();
                }
                if (overlay != null || liveZoom != null || miniMap != null)
                {
                    RecoverToNormalScreen(false);
                    wasCompact = true;
                }

                DetachWindowLayerChain();
                oldPanel?.Shutdown();
                application.Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));

                panel = CreateControlPanel();
                panel.SetCompactMode(wasCompact);
                if (wasVisible)
                {
                    panel.ShowForScreen(targetScreen, settings.PanelXRatio, settings.PanelYRatio);
                    settings.PanelVisible = true;
                }

                if (settingsWindow?.IsVisible == true)
                {
                    NativeMethods.SetOwnerWindow(settingsWindow.WindowHandle, panel.WindowHandle);
                }
                tray?.SetPanelVisible(wasVisible);
                settings.Save();
                ApplyWindowLayerChain();
                ApplyMagnificationExclusions();
                DebugLog.WriteDiagnostic("INPUT-REFRESH", "panelRecreated reason=" + reason +
                    ", oldHandle=0x" + oldHandle.ToInt64().ToString("X") +
                    ", newHandle=0x" + panel.WindowHandle.ToInt64().ToString("X") +
                    ", visible=" + wasVisible +
                    ", compact=" + wasCompact +
                    ", generation=" + panelGeneration);
            }
            catch (Exception exception)
            {
                DebugLog.Write("입력 장치 갱신을 위한 패널 재생성 중 오류가 발생했습니다.", exception);
            }
            finally
            {
                inputRefreshInProgress = false;
            }
        }

        private void RequestTouchRefreshRestart()
        {
            if (disposed || touchRefreshRestartRequested) return;
            touchRefreshRestartRequested = true;
            nativeTouchVerificationTimer.Stop();
            inputRefreshTimer.Stop();

            try
            {
                if (panel?.IsVisible == true) SavePanelPosition();
                if (overlay != null || liveZoom != null || miniMap != null) RecoverToNormalScreen(false);
                settings.Save();

                int currentProcessId;
                string executablePath;
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    currentProcessId = currentProcess.Id;
                    executablePath = currentProcess.MainModule.FileName;
                }

                DebugLog.WriteDiagnostic("INPUT-REFRESH", "processRestartRequested pid=" + currentProcessId +
                    ", executable=" + executablePath);
                tray?.ShowTouchRefreshRestartMessage();
                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "--touch-refresh-restart --wait-for-pid " + currentProcessId,
                    UseShellExecute = false
                });
                application.Shutdown();
            }
            catch (Exception exception)
            {
                touchRefreshRestartRequested = false;
                DebugLog.Write("입력 장치 복구를 위한 자동 재시작에 실패했습니다.", exception);
                tray?.ShowTouchRefreshFailedMessage();
            }
        }

        private void RestoreExternalForegroundWindow(IntPtr target, string reason)
        {
            if (disposed || target == IntPtr.Zero || !NativeMethods.IsWindow(target))
            {
                DebugLog.WriteDiagnostic("FOCUS", "restoreSkipped reason=" + reason +
                    ", invalidTarget=0x" + target.ToInt64().ToString("X"));
                return;
            }

            if (settingsWindow?.IsVisible == true)
            {
                DebugLog.WriteDiagnostic("FOCUS", "restoreSkipped reason=" + reason +
                    ", settingsVisible=true");
                return;
            }

            uint targetProcessId;
            NativeMethods.GetWindowThreadProcessId(target, out targetProcessId);
            uint currentProcessId;
            using (var process = Process.GetCurrentProcess())
            {
                currentProcessId = (uint)process.Id;
            }
            if (targetProcessId == currentProcessId)
            {
                DebugLog.WriteDiagnostic("FOCUS", "restoreSkipped reason=" + reason +
                    ", targetIsSelf=true");
                return;
            }

            var before = NativeMethods.GetForegroundWindow();
            var requested = before == target || NativeMethods.SetForegroundWindow(target);
            var after = NativeMethods.GetForegroundWindow();
            DebugLog.WriteDiagnostic("FOCUS", "restore reason=" + reason +
                ", target=0x" + target.ToInt64().ToString("X") +
                ", targetPid=" + targetProcessId +
                ", targetClass=" + NativeMethods.GetWindowClassName(target) +
                ", before=0x" + before.ToInt64().ToString("X") +
                ", requested=" + requested +
                ", after=0x" + after.ToInt64().ToString("X") +
                ", success=" + (after == target) +
                ", mode=" + mode);
        }

        private void QueueWindowEnvironmentRepair()
        {
            if (disposed) return;
            application.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(RepairWindowEnvironment));
        }

        private void RepairWindowEnvironment()
        {
            if (disposed) return;

            DebugLog.WriteDiagnostic("ENVIRONMENT", "창 환경 복구 시작");

            panel?.EnsureToolWindowStyle();
            overlay?.EnsureToolWindowStyle();
            liveZoom?.EnsureToolWindowStyle();
            miniMap?.EnsureToolWindowStyle();
            settingsWindow?.EnsureToolWindowStyle();

            if (panel?.IsVisible == true)
            {
                panel.SetScreenName(targetScreen);
                panel.ClampToCurrentScreen();
                panel.BringToFrontWithoutActivate();
            }

            ApplyWindowLayerChain();
            ApplyMagnificationExclusions();
            DebugLog.WriteDiagnostic("ENVIRONMENT", "창 환경 복구 완료");
        }

        private static Color ColorFromArgb(int argb)
        {
            return Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
        }

        private static void ShowAbout()
        {
            new AboutWindow().ShowDialog();
        }

        private void Quit()
        {
            Dispose();
            application.Shutdown();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            inputRefreshTimer?.Stop();
            nativeTouchVerificationTimer?.Stop();
            idleZoomReleaseTimer?.Stop();
            if (environmentEventsSubscribed)
            {
                SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
                SystemEvents.DisplaySettingsChanged -= HandleDisplaySettingsChanged;
                environmentEventsSubscribed = false;
            }
            settings.Save();
            DetachWindowLayerChain();
            panel?.SetZoomSafeMoveEnabled(false);
            settingsWindow?.Close();
            globalHotKey?.Dispose();
            tray?.Dispose();
            liveZoom?.Dispose();
            miniMap?.Dispose();
            overlay?.Shutdown();
            panel?.Shutdown();
        }
    }
}
