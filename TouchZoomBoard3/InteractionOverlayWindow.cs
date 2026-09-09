using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class InteractionOverlayWindow : Window
    {
        private static readonly Brush InputSurfaceBrush =
            new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        private sealed class UndoItem
        {
            internal Stroke Stroke { get; set; }
            internal UIElement Shape { get; set; }
        }

        private enum ShapeInputKind
        {
            None,
            Mouse,
            Touch,
            Stylus
        }

        private readonly Grid root;
        private readonly AdaptiveInkCanvas inkCanvas;
        private readonly Canvas shapeCanvas;
        private readonly Stack<UndoItem> undoStack = new Stack<UndoItem>();
        private AppMode mode = AppMode.Pointer;
        private Color penColor = Color.FromRgb(240, 50, 70);
        private Color highlighterColor = Color.FromRgb(255, 214, 34);
        private Color shapeColor = Color.FromRgb(45, 118, 240);
        private double penWidth = 4.0;
        private double highlighterWidth = 18.0;
        private double shapeWidth = 4.0;
        private IntPtr windowHandle;
        private Point lastMousePoint;
        private bool mousePanning;
        private bool allowClose;
        private Point shapeStart;
        private Shape previewShape;
        private ShapeInputKind shapeInputKind;
        private TouchDevice activeTouchDevice;
        private readonly Dictionary<int, Point> contentTouchPoints = new Dictionary<int, Point>();
        private TouchDevice contentPrimaryTouch;
        private Point contentTouchStart;
        private Point contentTouchLast;
        private bool contentTouchDragging;
        private bool contentMultiGesture;
        private double contentScrollAccumulator;
        private DateTime suppressPromotedMouseUntil = DateTime.MinValue;
        private bool contentMouseDragging;
        private bool cleanRevealPending;
        private bool inputPassthrough;
        private HwndSource windowSource;
        private double diagnosticZoom = 1.0;
        private double diagnosticDpiScale = 1.0;
        private bool physicalMouseCursorConfirmed;

        internal event Action<double, double> PanRequested;
        internal event Action<double> ZoomRequested;
        internal event Action<double, double, double> ZoomAtRequested;
        internal event Action ExitRequested;
        internal event Action PanelNeedsFront;
        internal event Action<ContentPointerAction, double, double> ContentPointerRequested;
        internal event Action<double, double, int> ContentScrollRequested;

        internal IntPtr WindowHandle => windowHandle;
        internal bool HasAnnotations => inkCanvas.Strokes.Count > 0 || shapeCanvas.Children.Count > 0;
        internal bool InputPassthrough => inputPassthrough;

        internal InteractionOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = InputSurfaceBrush;
            Focusable = false;

            root = new Grid
            {
                Background = InputSurfaceBrush,
                IsManipulationEnabled = true,
                Focusable = false
            };
            DisableWindowsPenFeedback(root);

            inkCanvas = new AdaptiveInkCanvas
            {
                Background = Brushes.Transparent,
                EditingMode = InkCanvasEditingMode.None,
                UseCustomCursor = true,
                Cursor = Cursors.Arrow
            };
            DisableWindowsPenFeedback(inkCanvas);
            ApplyDrawingAttributes();

            shapeCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            DisableWindowsPenFeedback(shapeCanvas);

            root.Children.Add(inkCanvas);
            root.Children.Add(shapeCanvas);
            Content = root;

            inkCanvas.StrokeCollected += (sender, args) =>
            {
                undoStack.Push(new UndoItem { Stroke = args.Stroke });
                InkStrokeDiagnostic diagnostic;
                if (inkCanvas.TryTakeStrokeDiagnostic(out diagnostic))
                {
                    DebugLog.WriteInkDiagnostic("INK-STROKE", "mode=" + mode +
                        ", zoom=" + diagnosticZoom.ToString("0.000") +
                        ", dpiScale=" + diagnosticDpiScale.ToString("0.000") +
                        ", filter=adaptive-v8, " + diagnostic.ToLogText());
                }
                else
                {
                    DebugLog.WriteInkDiagnostic("INK-STROKE", "diagnostic=missing, finalPoints=" +
                        (args.Stroke?.StylusPoints?.Count ?? 0));
                }
            };

            root.ManipulationStarting += HandleManipulationStarting;
            root.ManipulationDelta += HandleManipulationDelta;
            root.PreviewMouseLeftButtonDown += HandleMouseDown;
            root.PreviewMouseMove += HandleMouseMove;
            root.PreviewMouseLeftButtonUp += HandleMouseUp;
            root.PreviewMouseWheel += HandleMouseWheel;
            root.PreviewTouchDown += HandleTouchDown;
            root.PreviewTouchMove += HandleTouchMove;
            root.PreviewTouchUp += HandleTouchUp;
            root.PreviewStylusDown += HandleStylusDown;
            root.PreviewStylusMove += HandleStylusMove;
            root.PreviewStylusUp += HandleStylusUp;
            PreviewKeyDown += HandleKeyDown;
            Activated += (sender, args) => PanelNeedsFront?.Invoke();

            SourceInitialized += (sender, args) =>
            {
                windowHandle = new WindowInteropHelper(this).Handle;
                NativeMethods.EnsureToolWindowStyle(windowHandle);
                NativeMethods.AddExtendedWindowStyle(windowHandle, NativeMethods.WS_EX_NOACTIVATE);
                NativeMethods.SetExtendedWindowStyle(
                    windowHandle,
                    NativeMethods.WS_EX_TRANSPARENT,
                    inputPassthrough);
                windowSource = HwndSource.FromHwnd(windowHandle);
                windowSource?.AddHook(WindowProcedure);
            };
            Closing += (sender, args) =>
            {
                if (!allowClose)
                {
                    args.Cancel = true;
                    Hide();
                }
            };
        }

        internal void ShowForScreen(Forms.Screen screen)
        {
            var bounds = (screen ?? Forms.Screen.PrimaryScreen).Bounds;
            if (cleanRevealPending)
            {
                Opacity = 0.0;
            }
            if (!IsVisible)
            {
                Show();
            }

            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = new WindowInteropHelper(this).Handle;
            }

            NativeMethods.PositionTopmostWindow(windowHandle, bounds.Left, bounds.Top, bounds.Width, bounds.Height, false);
            RevealCleanSurfaceAfterRender();
        }

        internal void ActivateInputSurface()
        {
            if (!IsVisible)
            {
                return;
            }

            // 필기 오버레이는 프레젠테이션의 키보드 포커스를 가져가지 않는다.
            // 마우스·터치·펜 입력은 비활성 도구창에서도 직접 수신한다.
            PanelNeedsFront?.Invoke();
        }

        internal void SetMode(AppMode newMode)
        {
            CancelShapeDrawing();
            CancelContentInput();
            if (mode != newMode)
            {
                inkCanvas.CancelActiveFiltering("mode-change:" + mode + "->" + newMode);
            }
            mode = newMode;
            physicalMouseCursorConfirmed = false;
            inkCanvas.ConfigureAdaptiveFiltering(newMode,
                newMode == AppMode.Highlighter ? highlighterWidth : penWidth);
            inkCanvas.IsHitTestVisible =
                newMode == AppMode.Pen ||
                newMode == AppMode.Highlighter ||
                newMode == AppMode.Eraser;

            switch (newMode)
            {
                case AppMode.Pen:
                case AppMode.Highlighter:
                    ApplyDrawingAttributes();
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    // 전자칠판 터치는 포인터를 만들지 않지만 실제 마우스·터치패드는
                    // 판서 모드에서도 즉시 보이는 화살표를 사용한다.
                    inkCanvas.UseCustomCursor = true;
                    inkCanvas.Cursor = Cursors.Arrow;
                    Cursor = Cursors.Arrow;
                    break;
                case AppMode.Eraser:
                    inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                    inkCanvas.UseCustomCursor = true;
                    inkCanvas.Cursor = Cursors.Cross;
                    Cursor = Cursors.Cross;
                    break;
                case AppMode.Rectangle:
                case AppMode.Ellipse:
                case AppMode.Line:
                case AppMode.Arrow:
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    Cursor = Cursors.Cross;
                    break;
                case AppMode.Hand:
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    Cursor = Cursors.Hand;
                    break;
                case AppMode.Pointer:
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    Cursor = Cursors.Arrow;
                    break;
                default:
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    Cursor = Cursors.Arrow;
                    break;
            }
        }

        internal void SetInputPassthrough(bool enabled)
        {
            if (inputPassthrough == enabled)
            {
                NativeMethods.SetExtendedWindowStyle(
                    windowHandle,
                    NativeMethods.WS_EX_TRANSPARENT,
                    enabled);
                return;
            }

            if (enabled)
            {
                CancelShapeDrawing();
                CancelContentInput();
                ReleaseInputCaptures();
            }

            inputPassthrough = enabled;
            root.IsHitTestVisible = !enabled;
            NativeMethods.SetExtendedWindowStyle(
                windowHandle,
                NativeMethods.WS_EX_TRANSPARENT,
                enabled);
            DebugLog.WriteDiagnostic("OVERLAY-VISIBILITY",
                "inputPassthrough=" + enabled +
                ", mode=" + mode +
                ", visible=" + IsVisible +
                ", annotations=" + HasAnnotations);
        }

        internal void SetDrawingColor(DrawingStyleKind kind, Color color)
        {
            var normalized = Color.FromRgb(color.R, color.G, color.B);
            switch (kind)
            {
                case DrawingStyleKind.Pen:
                    penColor = normalized;
                    break;
                case DrawingStyleKind.Highlighter:
                    highlighterColor = normalized;
                    break;
                default:
                    shapeColor = normalized;
                    break;
            }
            ApplyDrawingAttributes();
        }

        internal void SetPenWidth(double width)
        {
            var normalized = Math.Max(2.0, Math.Min(12.0, width));
            if (mode == AppMode.Pen && Math.Abs(penWidth - normalized) > 0.001)
                inkCanvas.CancelActiveFiltering("pen-width-change");
            penWidth = normalized;
            if (mode == AppMode.Pen)
                inkCanvas.ConfigureAdaptiveFiltering(mode, penWidth);
            ApplyDrawingAttributes();
        }

        internal void SetHighlighterWidth(double width)
        {
            var normalized = Math.Max(8.0, Math.Min(32.0, width));
            if (mode == AppMode.Highlighter && Math.Abs(highlighterWidth - normalized) > 0.001)
                inkCanvas.CancelActiveFiltering("highlighter-width-change");
            highlighterWidth = normalized;
            if (mode == AppMode.Highlighter)
                inkCanvas.ConfigureAdaptiveFiltering(mode, highlighterWidth);
            ApplyDrawingAttributes();
        }

        internal void SetShapeWidth(double width)
        {
            shapeWidth = Math.Max(2.0, Math.Min(8.0, width));
        }

        internal void SetDiagnosticContext(double zoom, double dpiScale)
        {
            var normalizedZoom = Math.Max(1.0, Math.Min(5.0, zoom));
            if (Math.Abs(diagnosticZoom - normalizedZoom) > 0.001)
            {
                inkCanvas.CancelActiveFiltering("zoom-change:" +
                    diagnosticZoom.ToString("0.000") + "->" + normalizedZoom.ToString("0.000"));
            }
            diagnosticZoom = normalizedZoom;
            diagnosticDpiScale = Math.Max(1.0, dpiScale);
            inkCanvas.SetZoomFactor(diagnosticZoom);
        }

        internal void SetOwnerWindow(IntPtr owner)
        {
            NativeMethods.SetOwnerWindow(windowHandle, owner);
        }

        internal void Undo()
        {
            while (undoStack.Count > 0)
            {
                var item = undoStack.Pop();
                if (item.Stroke != null && inkCanvas.Strokes.Contains(item.Stroke))
                {
                    inkCanvas.Strokes.Remove(item.Stroke);
                    return;
                }

                if (item.Shape != null && shapeCanvas.Children.Contains(item.Shape))
                {
                    shapeCanvas.Children.Remove(item.Shape);
                    return;
                }
            }
        }

        internal void ClearAnnotations()
        {
            CancelShapeDrawing();
            inkCanvas.CancelActiveFiltering("clear-annotations");
            inkCanvas.Strokes.Clear();
            shapeCanvas.Children.Clear();
            undoStack.Clear();
        }

        internal void EndSession()
        {
            mousePanning = false;
            ReleaseInputCaptures();
            Opacity = 0.0;
            Hide();
            ClearAnnotations();
            cleanRevealPending = true;
        }

        internal bool EmergencyReset()
        {
            mousePanning = false;
            var succeeded = true;
            try
            {
                ReleaseInputCaptures();
            }
            catch (Exception exception)
            {
                succeeded = false;
                DebugLog.Write("입력 캡처 해제 중 오류가 발생했습니다.", exception);
            }

            try
            {
                Opacity = 0.0;
                Hide();
            }
            catch (Exception exception)
            {
                succeeded = false;
                DebugLog.Write("입력 오버레이 숨김 중 오류가 발생했습니다.", exception);
            }

            try
            {
                ClearAnnotations();
                cleanRevealPending = true;
            }
            catch (Exception exception)
            {
                succeeded = false;
                DebugLog.Write("필기 화면 초기화 중 오류가 발생했습니다.", exception);
            }

            return succeeded;
        }

        internal void Shutdown()
        {
            allowClose = true;
            windowSource?.RemoveHook(WindowProcedure);
            Close();
        }

        internal void EnsureToolWindowStyle()
        {
            NativeMethods.EnsureToolWindowStyle(windowHandle);
            NativeMethods.AddExtendedWindowStyle(windowHandle, NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetExtendedWindowStyle(
                windowHandle,
                NativeMethods.WS_EX_TRANSPARENT,
                inputPassthrough);
        }

        private void RevealCleanSurfaceAfterRender()
        {
            if (!cleanRevealPending)
            {
                Opacity = 1.0;
                return;
            }

            inkCanvas.InvalidateVisual();
            shapeCanvas.InvalidateVisual();
            root.InvalidateVisual();
            UpdateLayout();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (IsVisible)
                    {
                        Opacity = 1.0;
                    }
                    cleanRevealPending = false;
                }));
            }));
        }

        private static void DisableWindowsPenFeedback(DependencyObject element)
        {
            Stylus.SetIsPressAndHoldEnabled(element, false);
            Stylus.SetIsFlicksEnabled(element, false);
            Stylus.SetIsTapFeedbackEnabled(element, false);
            Stylus.SetIsTouchFeedbackEnabled(element, false);
        }

        private IntPtr WindowProcedure(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (inputPassthrough && message == NativeMethods.WM_NCHITTEST)
            {
                handled = true;
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            }

            if (message == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        private void ApplyDrawingAttributes()
        {
            var highlighter = mode == AppMode.Highlighter;
            inkCanvas.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = highlighter ? highlighterColor : penColor,
                Width = highlighter ? highlighterWidth : penWidth,
                Height = highlighter ? highlighterWidth : penWidth,
                // 입력 중 필터 결과와 펜을 뗀 뒤 최종 획의 모양을 일치시킨다.
                FitToCurve = false,
                // 굵은 판서에서는 전자칠판 압력 노이즈가 폭 변화로 크게 증폭된다.
                IgnorePressure = highlighter || (!highlighter && penWidth >= 8.0),
                IsHighlighter = highlighter,
                StylusTip = highlighter ? StylusTip.Rectangle : StylusTip.Ellipse
            };
        }

        private void HandleManipulationStarting(object sender, ManipulationStartingEventArgs args)
        {
            PanelNeedsFront?.Invoke();
            if (mode != AppMode.Hand)
            {
                return;
            }

            args.ManipulationContainer = root;
            args.Mode = ManipulationModes.Translate | ManipulationModes.Scale;
            args.Handled = true;
        }

        private void HandleManipulationDelta(object sender, ManipulationDeltaEventArgs args)
        {
            if (mode != AppMode.Hand)
            {
                return;
            }

            var translation = args.DeltaManipulation.Translation;
            PanRequested?.Invoke(translation.X, translation.Y);
            var scale = args.DeltaManipulation.Scale.X;
            if (Math.Abs(scale - 1.0) > 0.005)
            {
                ZoomRequested?.Invoke(scale);
            }
            args.Handled = true;
        }

        private void HandleMouseDown(object sender, MouseButtonEventArgs args)
        {
            PanelNeedsFront?.Invoke();
            var point = args.GetPosition(root);
            if (mode == AppMode.Hand)
            {
                mousePanning = true;
                lastMousePoint = point;
                root.CaptureMouse();
                args.Handled = true;
            }
            else if (mode == AppMode.Pointer)
            {
                if (DateTime.UtcNow < suppressPromotedMouseUntil)
                {
                    args.Handled = true;
                    return;
                }
                contentMouseDragging = true;
                contentTouchLast = point;
                root.CaptureMouse();
                ContentPointerRequested?.Invoke(ContentPointerAction.Begin, point.X, point.Y);
                args.Handled = true;
            }
            else if (mode == AppMode.Eraser && EraseShapeAt(point))
            {
                args.Handled = true;
            }
            else if (IsShapeMode(mode) && shapeInputKind == ShapeInputKind.None)
            {
                BeginShape(point, ShapeInputKind.Mouse);
                root.CaptureMouse();
                args.Handled = true;
            }
        }

        private void HandleMouseMove(object sender, MouseEventArgs args)
        {
            if (args.StylusDevice == null)
            {
                // Touch/Stylus 뒤에 실제 마우스가 들어오면 InkCanvas의 숨은 펜 커서를
                // 그대로 쓰지 않고 즉시 눈에 보이는 포인터로 복구한다.
                inkCanvas.UseCustomCursor = true;
                inkCanvas.Cursor = Cursors.Arrow;
                Cursor = Cursors.Arrow;
                if (!physicalMouseCursorConfirmed)
                {
                    physicalMouseCursorConfirmed = true;
                    DebugLog.WriteDiagnostic("CURSOR",
                        "physicalMouse=True, restored=Arrow, mode=" + mode);
                }
            }
            var point = args.GetPosition(root);
            if (mousePanning && mode == AppMode.Hand)
            {
                PanRequested?.Invoke(point.X - lastMousePoint.X, point.Y - lastMousePoint.Y);
                lastMousePoint = point;
                args.Handled = true;
            }
            else if (contentMouseDragging && mode == AppMode.Pointer)
            {
                contentTouchLast = point;
                ContentPointerRequested?.Invoke(ContentPointerAction.Move, point.X, point.Y);
                args.Handled = true;
            }
            else if (shapeInputKind == ShapeInputKind.Mouse)
            {
                UpdateShape(point);
                args.Handled = true;
            }
        }

        private void HandleMouseUp(object sender, MouseButtonEventArgs args)
        {
            if (mousePanning)
            {
                mousePanning = false;
                root.ReleaseMouseCapture();
                args.Handled = true;
            }
            else if (contentMouseDragging)
            {
                contentMouseDragging = false;
                ContentPointerRequested?.Invoke(ContentPointerAction.End,
                    args.GetPosition(root).X, args.GetPosition(root).Y);
                root.ReleaseMouseCapture();
                args.Handled = true;
            }
            else if (shapeInputKind == ShapeInputKind.Mouse)
            {
                CompleteShape(args.GetPosition(root));
                root.ReleaseMouseCapture();
                args.Handled = true;
            }
        }

        private void HandleMouseWheel(object sender, MouseWheelEventArgs args)
        {
            if (mode == AppMode.Pointer &&
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var point = args.GetPosition(root);
                ContentScrollRequested?.Invoke(point.X, point.Y, args.Delta);
                args.Handled = true;
                return;
            }
            if (mode != AppMode.Hand && mode != AppMode.Pointer)
            {
                return;
            }
            var zoomPoint = args.GetPosition(root);
            ZoomAtRequested?.Invoke(
                args.Delta > 0 ? 1.12 : 1.0 / 1.12,
                zoomPoint.X,
                zoomPoint.Y);
            args.Handled = true;
        }

        private void HandleTouchDown(object sender, TouchEventArgs args)
        {
            PanelNeedsFront?.Invoke();
            var point = args.GetTouchPoint(root).Position;
            if (mode == AppMode.Pointer)
            {
                HandleContentTouchDown(args, point);
                return;
            }
            if (mode == AppMode.Eraser && EraseShapeAt(point))
            {
                args.Handled = true;
                return;
            }
            if (!IsShapeMode(mode) || shapeInputKind != ShapeInputKind.None)
            {
                return;
            }
            activeTouchDevice = args.TouchDevice;
            activeTouchDevice.Capture(root);
            BeginShape(point, ShapeInputKind.Touch);
            args.Handled = true;
        }

        private void HandleTouchMove(object sender, TouchEventArgs args)
        {
            if (mode == AppMode.Pointer)
            {
                HandleContentTouchMove(args);
                return;
            }
            if (shapeInputKind != ShapeInputKind.Touch || args.TouchDevice != activeTouchDevice)
            {
                return;
            }
            UpdateShape(args.GetTouchPoint(root).Position);
            args.Handled = true;
        }

        private void HandleTouchUp(object sender, TouchEventArgs args)
        {
            if (mode == AppMode.Pointer)
            {
                HandleContentTouchUp(args);
                return;
            }
            if (shapeInputKind != ShapeInputKind.Touch || args.TouchDevice != activeTouchDevice)
            {
                return;
            }
            CompleteShape(args.GetTouchPoint(root).Position);
            activeTouchDevice.Capture(null);
            activeTouchDevice = null;
            args.Handled = true;
        }

        private void HandleStylusDown(object sender, StylusDownEventArgs args)
        {
            PanelNeedsFront?.Invoke();
            var point = args.GetPosition(root);
            if (mode == AppMode.Pointer && args.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Stylus)
            {
                suppressPromotedMouseUntil = DateTime.UtcNow.AddMilliseconds(900);
                root.CaptureStylus();
                ContentPointerRequested?.Invoke(ContentPointerAction.Begin, point.X, point.Y);
                args.Handled = true;
                return;
            }
            if (mode == AppMode.Eraser && EraseShapeAt(point))
            {
                args.Handled = true;
                return;
            }
            if (!IsShapeMode(mode) || shapeInputKind != ShapeInputKind.None)
            {
                return;
            }
            root.CaptureStylus();
            BeginShape(point, ShapeInputKind.Stylus);
            args.Handled = true;
        }

        private void HandleStylusMove(object sender, StylusEventArgs args)
        {
            if (mode == AppMode.Pointer && root.IsStylusCaptured &&
                args.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Stylus)
            {
                var point = args.GetPosition(root);
                ContentPointerRequested?.Invoke(ContentPointerAction.Move, point.X, point.Y);
                args.Handled = true;
                return;
            }
            if (shapeInputKind != ShapeInputKind.Stylus)
            {
                return;
            }
            UpdateShape(args.GetPosition(root));
            args.Handled = true;
        }

        private void HandleStylusUp(object sender, StylusEventArgs args)
        {
            if (mode == AppMode.Pointer && root.IsStylusCaptured &&
                args.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Stylus)
            {
                var point = args.GetPosition(root);
                ContentPointerRequested?.Invoke(ContentPointerAction.End, point.X, point.Y);
                root.ReleaseStylusCapture();
                suppressPromotedMouseUntil = DateTime.UtcNow.AddMilliseconds(900);
                args.Handled = true;
                return;
            }
            if (shapeInputKind != ShapeInputKind.Stylus)
            {
                return;
            }
            CompleteShape(args.GetPosition(root));
            root.ReleaseStylusCapture();
            args.Handled = true;
        }

        private void HandleKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape)
            {
                ExitRequested?.Invoke();
                args.Handled = true;
            }
            else if (args.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Undo();
                args.Handled = true;
            }
        }

        private void BeginShape(Point start, ShapeInputKind inputKind)
        {
            shapeStart = start;
            shapeInputKind = inputKind;
            previewShape = CreateShape();
            shapeCanvas.Children.Add(previewShape);
            UpdateShape(start);
        }

        private void HandleContentTouchDown(TouchEventArgs args, Point point)
        {
            suppressPromotedMouseUntil = DateTime.UtcNow.AddMilliseconds(900);
            contentTouchPoints[args.TouchDevice.Id] = point;
            args.TouchDevice.Capture(root);

            if (contentTouchPoints.Count == 1)
            {
                contentPrimaryTouch = args.TouchDevice;
                contentTouchStart = point;
                contentTouchLast = point;
                contentTouchDragging = false;
                contentMultiGesture = false;
                contentScrollAccumulator = 0;
            }
            else
            {
                if (contentTouchDragging)
                {
                    ContentPointerRequested?.Invoke(
                        ContentPointerAction.Cancel,
                        contentTouchLast.X,
                        contentTouchLast.Y);
                    contentTouchDragging = false;
                }
                contentMultiGesture = true;
            }
            args.Handled = true;
        }

        private void HandleContentTouchMove(TouchEventArgs args)
        {
            Point previous;
            if (!contentTouchPoints.TryGetValue(args.TouchDevice.Id, out previous))
            {
                return;
            }

            var current = args.GetTouchPoint(root).Position;
            if (contentTouchPoints.Count >= 2)
            {
                var ordered = contentTouchPoints.Keys.OrderBy(id => id).Take(2).ToArray();
                var firstPrevious = contentTouchPoints[ordered[0]];
                var secondPrevious = contentTouchPoints[ordered[1]];
                contentTouchPoints[args.TouchDevice.Id] = current;
                var firstCurrent = contentTouchPoints[ordered[0]];
                var secondCurrent = contentTouchPoints[ordered[1]];
                var previousDistance = (firstPrevious - secondPrevious).Length;
                var currentDistance = (firstCurrent - secondCurrent).Length;
                var scale = previousDistance > 8.0 ? currentDistance / previousDistance : 1.0;
                var previousCenter = new Point(
                    (firstPrevious.X + secondPrevious.X) / 2.0,
                    (firstPrevious.Y + secondPrevious.Y) / 2.0);
                var currentCenter = new Point(
                    (firstCurrent.X + secondCurrent.X) / 2.0,
                    (firstCurrent.Y + secondCurrent.Y) / 2.0);

                if (Math.Abs(scale - 1.0) > 0.008)
                {
                    ZoomRequested?.Invoke(scale);
                    contentScrollAccumulator = 0;
                }
                else
                {
                    contentScrollAccumulator += currentCenter.Y - previousCenter.Y;
                    while (Math.Abs(contentScrollAccumulator) >= 34.0)
                    {
                        var wheelDelta = contentScrollAccumulator > 0 ? 120 : -120;
                        ContentScrollRequested?.Invoke(currentCenter.X, currentCenter.Y, wheelDelta);
                        contentScrollAccumulator += contentScrollAccumulator > 0 ? -34.0 : 34.0;
                    }
                }
            }
            else if (!contentMultiGesture && args.TouchDevice == contentPrimaryTouch)
            {
                contentTouchPoints[args.TouchDevice.Id] = current;
                if (!contentTouchDragging && (current - contentTouchStart).Length >= 6.0)
                {
                    ContentPointerRequested?.Invoke(
                        ContentPointerAction.Begin,
                        contentTouchStart.X,
                        contentTouchStart.Y);
                    contentTouchDragging = true;
                }
                if (contentTouchDragging)
                {
                    ContentPointerRequested?.Invoke(ContentPointerAction.Move, current.X, current.Y);
                }
                contentTouchLast = current;
            }

            args.Handled = true;
        }

        private void HandleContentTouchUp(TouchEventArgs args)
        {
            Point point;
            if (!contentTouchPoints.TryGetValue(args.TouchDevice.Id, out point))
            {
                return;
            }
            point = args.GetTouchPoint(root).Position;
            var wasLastTouch = contentTouchPoints.Count == 1;
            contentTouchPoints.Remove(args.TouchDevice.Id);
            args.TouchDevice.Capture(null);

            if (wasLastTouch && !contentMultiGesture)
            {
                ContentPointerRequested?.Invoke(
                    contentTouchDragging ? ContentPointerAction.End : ContentPointerAction.Click,
                    point.X,
                    point.Y);
            }

            if (contentTouchPoints.Count == 0)
            {
                contentPrimaryTouch = null;
                contentTouchDragging = false;
                contentMultiGesture = false;
                contentScrollAccumulator = 0;
            }
            suppressPromotedMouseUntil = DateTime.UtcNow.AddMilliseconds(900);
            args.Handled = true;
        }

        private Shape CreateShape()
        {
            Shape shape;
            switch (mode)
            {
                case AppMode.Rectangle:
                    shape = new Rectangle { Fill = Brushes.Transparent };
                    break;
                case AppMode.Ellipse:
                    shape = new System.Windows.Shapes.Ellipse { Fill = Brushes.Transparent };
                    break;
                case AppMode.Line:
                    shape = new Line();
                    break;
                default:
                    shape = new Path();
                    break;
            }
            shape.Stroke = new SolidColorBrush(shapeColor);
            shape.StrokeThickness = shapeWidth;
            shape.StrokeStartLineCap = PenLineCap.Round;
            shape.StrokeEndLineCap = PenLineCap.Round;
            return shape;
        }

        private void UpdateShape(Point end)
        {
            if (previewShape == null)
            {
                return;
            }

            if (mode == AppMode.Rectangle || mode == AppMode.Ellipse)
            {
                Canvas.SetLeft(previewShape, Math.Min(shapeStart.X, end.X));
                Canvas.SetTop(previewShape, Math.Min(shapeStart.Y, end.Y));
                previewShape.Width = Math.Abs(end.X - shapeStart.X);
                previewShape.Height = Math.Abs(end.Y - shapeStart.Y);
            }
            else if (mode == AppMode.Line)
            {
                var line = (Line)previewShape;
                line.X1 = shapeStart.X;
                line.Y1 = shapeStart.Y;
                line.X2 = end.X;
                line.Y2 = end.Y;
            }
            else if (mode == AppMode.Arrow)
            {
                ((Path)previewShape).Data = CreateArrowGeometry(shapeStart, end);
            }
        }

        private void CompleteShape(Point end)
        {
            UpdateShape(end);
            var distance = Math.Abs(end.X - shapeStart.X) + Math.Abs(end.Y - shapeStart.Y);
            var completed = previewShape;
            previewShape = null;
            shapeInputKind = ShapeInputKind.None;
            if (completed == null)
            {
                return;
            }
            if (distance < 4)
            {
                shapeCanvas.Children.Remove(completed);
                return;
            }
            undoStack.Push(new UndoItem { Shape = completed });
        }

        private void CancelShapeDrawing()
        {
            if (previewShape != null)
            {
                shapeCanvas.Children.Remove(previewShape);
                previewShape = null;
            }
            shapeInputKind = ShapeInputKind.None;
            if (activeTouchDevice != null)
            {
                activeTouchDevice.Capture(null);
                activeTouchDevice = null;
            }
            root?.ReleaseMouseCapture();
            root?.ReleaseStylusCapture();
        }

        private void ReleaseInputCaptures()
        {
            inkCanvas.CancelActiveFiltering("release-input-captures");
            CancelShapeDrawing();
            CancelContentInput();
            Mouse.Capture(null);
            Stylus.Capture(null);
            root.ReleaseAllTouchCaptures();
        }

        private void CancelContentInput()
        {
            if (contentTouchDragging || contentMouseDragging)
            {
                ContentPointerRequested?.Invoke(
                    ContentPointerAction.Cancel,
                    contentTouchLast.X,
                    contentTouchLast.Y);
            }
            contentMouseDragging = false;
            contentTouchDragging = false;
            contentMultiGesture = false;
            contentScrollAccumulator = 0;
            contentTouchPoints.Clear();
            contentPrimaryTouch = null;
        }

        private static bool IsShapeMode(AppMode value)
        {
            return value == AppMode.Rectangle ||
                   value == AppMode.Ellipse ||
                   value == AppMode.Line ||
                   value == AppMode.Arrow;
        }

        private bool EraseShapeAt(Point point)
        {
            for (var index = shapeCanvas.Children.Count - 1; index >= 0; index--)
            {
                var shape = shapeCanvas.Children[index] as Shape;
                if (shape == null)
                {
                    continue;
                }

                var bounds = VisualTreeHelper.GetDescendantBounds(shape);
                if (bounds.IsEmpty)
                {
                    continue;
                }
                var origin = shape.TranslatePoint(new Point(0, 0), shapeCanvas);
                bounds.Offset(origin.X, origin.Y);
                bounds.Inflate(12, 12);
                if (bounds.Contains(point))
                {
                    shapeCanvas.Children.RemoveAt(index);
                    return true;
                }
            }
            return false;
        }

        private static Geometry CreateArrowGeometry(Point start, Point end)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false, false);
                context.LineTo(end, true, false);
                var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
                const double headLength = 18;
                const double headAngle = Math.PI / 7.0;
                var first = new Point(
                    end.X - headLength * Math.Cos(angle - headAngle),
                    end.Y - headLength * Math.Sin(angle - headAngle));
                var second = new Point(
                    end.X - headLength * Math.Cos(angle + headAngle),
                    end.Y - headLength * Math.Sin(angle + headAngle));
                context.BeginFigure(end, false, false);
                context.LineTo(first, true, false);
                context.BeginFigure(end, false, false);
                context.LineTo(second, true, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
