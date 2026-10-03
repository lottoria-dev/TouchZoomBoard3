using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TouchZoomBoard
{
    internal static class RegressionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int passed;

        [STAThread]
        private static int Main()
        {
            try
            {
                Run("Clear restores mixed ink and all shape types in order", ClearRestoresMixedAnnotations);
                Run("Undo continues through history before clear", UndoContinuesBeforeClear);
                Run("Empty/repeated clears and post-clear drawing", RepeatedClearAndNewDrawing);
                Run("Session teardown and emergency reset discard history", SessionResetDiscardsHistory);
                Run("Highlighter defaults, limits and one-time migration", HighlighterMigration);
                Run("Drawing attributes and width menu agree", HighlighterDrawingAndMenu);
                Run("Input dismisses options without consuming mouse down", InputDismissesOptions);
                Run("Known presentation survives NULL and own-window activation", FocusRetainsKnownTarget);
                Run("Settings corner alpha at 100/125/150 percent DPI", SettingsCornerAlpha);
                Run("Compact grid preserves manual center alignment", CompactCenterAlignment);
                Run("Erased pen/highlighter restores identity and original order", ErasedStrokesRestore);
                Run("Cancelled stroke erasing creates no undo entry", CancelledErase);
                Run("Erased shape restores original stacking order", ErasedShapesRestore);
                Run("Eraser, clear-all and earlier drawing history compose", EraserAndClearHistory);
                Run("New external app/PID cancels old focus requests", FocusRequestInvalidation);
                Run("Only known own input windows may return focus", FocusRestoreWindowPolicy);
                Run("Tooltip/context/option corners clip every painted layer", PopupCornerAlpha);
                Run("Popup HWND retains per-pixel transparent background", PopupNativeAlpha);
                Run("Circle drag keeps center and radius in every direction", CircleDragGeometry);
                Run("Circle cancellation and small taps leave no undo entries", CircleCancellation);
                Run("Circle selection and shared shape style remain connected", CircleSelectionAndStyle);
                Run("Color tool selection defaults to line and remembers each shape", ColorSelectsMatchingTool);
                Run("Tool order restores invalid, missing and duplicate entries", ToolOrderNormalization);
                Run("Carousel reorders existing controls with their colors and mode", CarouselToolOrder);
                Run("Settings order is staged until save and cancels independently", SettingsToolOrderStaging);
                Run("First press selects pen once and rapid taps remain independent", FirstPressSelection);
                Run("Identical ink input stays identical at every background zoom", InkZoomInvariant);
                Run("Delayed callback delivery cannot reshape sampled ink", InkDeliveryDelayInvariant);
                Run("Batched samples, timestamp rollover and lag bounds", InkPacketTimingAndBounds);
                Run("Settings drag uses screen pixels without drift and ends cleanly", SettingsScreenDrag);
                Run("Order icons match live panel vectors and survive reordering", ToolOrderIconsMatchPanel);
                Console.WriteLine("PASS: " + passed + " regression groups");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error);
                return 1;
            }
        }

        private static void Run(string name, Action test)
        {
            test();
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static T Field<T>(object owner, string name)
        {
            return (T)owner.GetType().GetField(name, PrivateInstance).GetValue(owner);
        }

        private static void SetField(object owner, string name, object value)
        {
            owner.GetType().GetField(name, PrivateInstance).SetValue(owner, value);
        }

        private static object Call(object owner, string name, params object[] args)
        {
            return owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, args);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static Stroke AddStroke(InteractionOverlayWindow window, AppMode mode)
        {
            window.SetMode(mode);
            var canvas = Field<AdaptiveInkCanvas>(window, "inkCanvas");
            var stroke = new Stroke(new StylusPointCollection(new[]
            {
                new StylusPoint(20, 30), new StylusPoint(45, 60), new StylusPoint(80, 50)
            }), canvas.DefaultDrawingAttributes.Clone());
            canvas.Strokes.Add(stroke);
            canvas.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke)
            {
                RoutedEvent = InkCanvas.StrokeCollectedEvent
            });
            return stroke;
        }

        private static void AddShape(InteractionOverlayWindow window, AppMode mode)
        {
            window.SetMode(mode);
            var inputKind = typeof(InteractionOverlayWindow).GetNestedType("ShapeInputKind", BindingFlags.NonPublic);
            Call(window, "BeginShape", new Point(20, 30), Enum.Parse(inputKind, "Mouse"));
            Call(window, "CompleteShape", new Point(100, 120));
        }

        private static void ClearRestoresMixedAnnotations()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                var pen = AddStroke(window, AppMode.Pen);
                var highlighter = AddStroke(window, AppMode.Highlighter);
                foreach (var mode in new[] { AppMode.Rectangle, AppMode.Ellipse, AppMode.Circle, AppMode.Line, AppMode.Arrow })
                    AddShape(window, mode);
                var canvas = Field<AdaptiveInkCanvas>(window, "inkCanvas");
                var shapes = Field<Canvas>(window, "shapeCanvas");
                var originalShapes = shapes.Children.Cast<UIElement>().ToArray();
                window.ClearAnnotations();
                Check(!window.HasAnnotations, "Clear must remove all annotations");
                window.Undo();
                Check(canvas.Strokes.SequenceEqual(new[] { pen, highlighter }), "Stroke identity/order changed");
                Check(shapes.Children.Cast<UIElement>().SequenceEqual(originalShapes), "Shape identity/order changed");
                Check(highlighter.DrawingAttributes.IsHighlighter && highlighter.DrawingAttributes.Width == 36,
                    "Highlighter style was not preserved");
            }
            finally { window.Shutdown(); }
        }

        private static void UndoContinuesBeforeClear()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                AddStroke(window, AppMode.Pen);
                AddShape(window, AppMode.Line);
                window.ClearAnnotations();
                window.Undo();
                window.Undo();
                Check(Field<Canvas>(window, "shapeCanvas").Children.Count == 0, "Old shape undo lost");
                Check(Field<AdaptiveInkCanvas>(window, "inkCanvas").Strokes.Count == 1, "Old ink lost");
                window.Undo();
                Check(!window.HasAnnotations, "Old stroke undo lost");
            }
            finally { window.Shutdown(); }
        }

        private static void RepeatedClearAndNewDrawing()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                var first = AddStroke(window, AppMode.Pen);
                window.ClearAnnotations();
                window.ClearAnnotations(); // Must not push an empty snapshot.
                var second = AddStroke(window, AppMode.Highlighter);
                window.ClearAnnotations();
                window.Undo();
                var strokes = Field<AdaptiveInkCanvas>(window, "inkCanvas").Strokes;
                Check(strokes.Count == 1 && strokes[0] == second, "Most recent clear did not restore");
                window.Undo();
                Check(strokes.Count == 0, "Post-clear drawing did not undo");
                window.Undo();
                Check(strokes.Count == 1 && strokes[0] == first, "Older snapshot did not restore");
                window.Undo();
                Check(!window.HasAnnotations, "Remaining history invalid");
                window.Undo(); // Empty undo must be safe.
            }
            finally { window.Shutdown(); }
        }

        private static void SessionResetDiscardsHistory()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                AddStroke(window, AppMode.Pen);
                window.ClearAnnotations();
                window.EndSession();
                window.Undo();
                Check(!window.HasAnnotations, "EndSession resurrected old notes");
                AddStroke(window, AppMode.Pen);
                window.ClearAnnotations();
                Check(window.EmergencyReset(), "EmergencyReset failed");
                window.Undo();
                Check(!window.HasAnnotations, "EmergencyReset resurrected old notes");
            }
            finally { window.Shutdown(); }
        }

        private static void HighlighterMigration()
        {
            var oldWidths = new[] { 8.0, 12.0, 18.0, 24.0, 32.0 };
            Check(new UserSettings().HighlighterWidth == 36, "New default must be 36 DIP");
            for (int i = 0; i < oldWidths.Length; i++)
            {
                double migrated = HighlighterWidthPolicy.Restore(oldWidths[i], 0);
                Check(migrated == HighlighterWidthPolicy.Presets[i], "Wrong migration");
                Check(HighlighterWidthPolicy.Restore(migrated, HighlighterWidthPolicy.SettingsVersion) == migrated,
                    "Repeated startup changed saved width again");
            }
            Check(HighlighterWidthPolicy.Normalize(-1) == 16, "Minimum mismatch");
            Check(HighlighterWidthPolicy.Normalize(1000) == 64, "Maximum mismatch");
            Check(HighlighterWidthPolicy.Normalize(double.NaN) == 36, "NaN must use default");
            Check(HighlighterWidthPolicy.Restore(double.PositiveInfinity, 0) == 36, "Infinity must use default");
        }

        private static void HighlighterDrawingAndMenu()
        {
            var window = new InteractionOverlayWindow();
            var panel = new ControlPanelWindow(false);
            try
            {
                window.SetMode(AppMode.Highlighter);
                foreach (var width in HighlighterWidthPolicy.Presets)
                {
                    window.SetHighlighterWidth(width);
                    var attributes = Field<AdaptiveInkCanvas>(window, "inkCanvas").DefaultDrawingAttributes;
                    Check(attributes.Width == width && attributes.Height == width, "Actual width mismatch");
                    Check(attributes.IsHighlighter && attributes.IgnorePressure && attributes.StylusTip == StylusTip.Rectangle,
                        "Marker semantics changed");
                }
                var menu = Field<Dictionary<double, Button>>(panel, "highlighterThicknessMenuItems");
                Check(menu.Keys.OrderBy(x => x).SequenceEqual(HighlighterWidthPolicy.Presets), "Menu mismatch");
                window.SetMode(AppMode.Pen);
                Check(Field<AdaptiveInkCanvas>(window, "inkCanvas").DefaultDrawingAttributes.Width == 4,
                    "Pen width must not change");
            }
            finally { window.Shutdown(); panel.Shutdown(); }
        }

        private static void InputDismissesOptions()
        {
            var window = new InteractionOverlayWindow();
            var panel = new ControlPanelWindow(false);
            try
            {
                window.SetMode(AppMode.Pen);
                window.InputStarted += panel.DismissToolPopupsForInput;
                var popup = new Popup { Child = new Border { Width = 60, Height = 60 }, StaysOpen = true };
                SetField(panel, "activeOptionPopup", popup);
                SetField(panel, "activeOptionPopupKind", "thickness");
                popup.IsOpen = true;
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
                };
                Field<Grid>(window, "root").RaiseEvent(args);
                Check(!popup.IsOpen && Field<Popup>(panel, "activeOptionPopup") == null, "Popup stayed open");
                Check(!args.Handled, "The initial ink event was consumed");
                Check(Field<AdaptiveInkCanvas>(window, "inkCanvas").EditingMode == InkCanvasEditingMode.Ink,
                    "Dismiss changed the selected drawing tool");
            }
            finally { panel.DismissToolPopupsForInput(); window.Shutdown(); panel.Shutdown(); }
        }

        private static void FocusRetainsKnownTarget()
        {
            var panel = new ControlPanelWindow(false);
            try
            {
                var focus = Field<ExternalFocusState>(panel, "externalFocus");
                uint own = Field<uint>(panel, "currentProcessId");
                focus.Observe(new IntPtr(123), own + 1);
                for (int i = 0; i < 3; i++)
                {
                    Call(panel, "ObserveActivation", i, IntPtr.Zero);
                    focus.Observe(new IntPtr(456), own); // Our overlay/popup does not replace the app.
                    var request = focus.BeginRequest();
                    Check(request.Target == new IntPtr(123) && focus.IsCurrent(request), "Presentation was forgotten");
                    focus.Complete(request);
                    Check(!focus.IsCurrent(request), "Completed request reused");
                }
                focus.CancelRequests(); // Hiding/recreating the panel preserves the target.
                Check(focus.BeginRequest().Target == new IntPtr(123), "Panel lifecycle lost presentation");
            }
            finally { panel.Shutdown(); }
        }

        private static void EraseStroke(InteractionOverlayWindow window, Stroke stroke, bool cancel = false)
        {
            window.SetMode(AppMode.Eraser);
            var canvas = Field<AdaptiveInkCanvas>(window, "inkCanvas");
            // WPF exposes no public constructor for these event arguments.
            // Create them only in this test harness, then exercise the real
            // StrokeErasing/StrokeErased handlers, including cancellation.
            var constructor = typeof(InkCanvasStrokeErasingEventArgs).GetConstructor(
                PrivateInstance | BindingFlags.Public, null, new[] { typeof(Stroke) }, null);
            if (constructor == null)
                throw new InvalidOperationException(
                    "This WPF runtime does not expose the expected stroke-erasing test constructor.");
            var args = (InkCanvasStrokeErasingEventArgs)constructor.Invoke(new object[] { stroke });
            args.Cancel = cancel;
            typeof(InkCanvas).GetMethod("OnStrokeErasing", PrivateInstance).Invoke(canvas, new object[] { args });
            if (args.Cancel) return;
            canvas.Strokes.Remove(stroke);
            canvas.RaiseEvent(new RoutedEventArgs(InkCanvas.StrokeErasedEvent));
        }

        private static void ErasedStrokesRestore()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                var a = AddStroke(window, AppMode.Pen);
                var b = AddStroke(window, AppMode.Highlighter);
                var c = AddStroke(window, AppMode.Pen);
                EraseStroke(window, b);
                EraseStroke(window, a);
                window.Undo();
                window.Undo();
                var strokes = Field<AdaptiveInkCanvas>(window, "inkCanvas").Strokes;
                Check(strokes.SequenceEqual(new[] { a, b, c }), "Restored stroke order/identity changed");
                Check(b.DrawingAttributes.IsHighlighter && b.DrawingAttributes.Width == 36, "Marker attributes lost");
                window.Undo();
                Check(strokes.SequenceEqual(new[] { a, b }), "Earlier add history was lost");
            }
            finally { window.Shutdown(); }
        }

        private static void CancelledErase()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                var stroke = AddStroke(window, AppMode.Pen);
                EraseStroke(window, stroke, true);
                Check(window.HasAnnotations, "Cancelled erase removed stroke");
                window.Undo();
                Check(!window.HasAnnotations, "Cancelled erase consumed an undo step");
            }
            finally { window.Shutdown(); }
        }

        private static void AddShapeAt(InteractionOverlayWindow window, AppMode mode, double x)
        {
            window.SetMode(mode);
            var kind = typeof(InteractionOverlayWindow).GetNestedType("ShapeInputKind", BindingFlags.NonPublic);
            Call(window, "BeginShape", new Point(x, 30), Enum.Parse(kind, "Mouse"));
            Call(window, "CompleteShape", new Point(x + 40, 100));
        }

        private static void ErasedShapesRestore()
        {
            foreach (var mode in new[] { AppMode.Rectangle, AppMode.Ellipse, AppMode.Circle, AppMode.Line, AppMode.Arrow })
            {
                var window = new InteractionOverlayWindow();
                try
                {
                    AddShapeAt(window, AppMode.Rectangle, 20);
                    AddShapeAt(window, mode, 120);
                    AddShapeAt(window, AppMode.Ellipse, 220);
                    var shapes = Field<Canvas>(window, "shapeCanvas");
                    var original = shapes.Children.Cast<UIElement>().ToArray();
                    var root = Field<Grid>(window, "root");
                    root.Measure(new Size(360, 220));
                    root.Arrange(new Rect(0, 0, 360, 220));
                    root.UpdateLayout();
                    window.SetMode(AppMode.Eraser);
                    Check((bool)Call(window, "EraseShapeAt", new Point(140, 65)), "Shape was not erased: " + mode);
                    window.Undo();
                    Check(shapes.Children.Cast<UIElement>().SequenceEqual(original), "Shape order changed: " + mode);
                    window.Undo();
                    Check(shapes.Children.Count == 2, "Earlier shape add cannot be undone");
                }
                finally { window.Shutdown(); }
            }
        }

        private static void EraserAndClearHistory()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                var a = AddStroke(window, AppMode.Pen);
                var b = AddStroke(window, AppMode.Highlighter);
                EraseStroke(window, a);
                window.ClearAnnotations();
                window.Undo();
                var strokes = Field<AdaptiveInkCanvas>(window, "inkCanvas").Strokes;
                Check(strokes.SequenceEqual(new[] { b }), "Clear restored already-erased stroke");
                window.Undo();
                Check(strokes.SequenceEqual(new[] { a, b }), "Eraser history lost after clear");
                EraseStroke(window, a);
                window.EndSession();
                window.Undo();
                Check(!window.HasAnnotations, "Session teardown retained eraser history");
            }
            finally { window.Shutdown(); }
        }

        private static void FocusRequestInvalidation()
        {
            var state = new ExternalFocusState(10);
            Check(!state.IsCurrent(state.BeginRequest()), "Unknown target allowed");
            state.Observe(new IntPtr(100), 20); // Browser.
            var browser = state.BeginRequest();
            state.Observe(new IntPtr(200), 30); // OneNote selected later.
            Check(!state.IsCurrent(browser), "Old browser request survived app switch");
            var first = state.BeginRequest();
            var second = state.BeginRequest();
            Check(!state.IsCurrent(first) && state.IsCurrent(second), "Promoted duplicate requests not superseded");
            state.Observe(new IntPtr(200), 31); // Same HWND, different process.
            Check(!state.IsCurrent(second), "Reused handle accepted");
            var current = state.BeginRequest();
            state.Observe(new IntPtr(200), 31);
            Check(state.IsCurrent(current), "Duplicate native observation cancelled valid restoration");
            state.Clear();
            Check(!state.IsCurrent(current), "Cleared target still valid");
        }

        private static void FocusRestoreWindowPolicy()
        {
            var inputs = new[] { new IntPtr(1), new IntPtr(2), new IntPtr(3), new IntPtr(4) };
            foreach (var input in inputs)
                Check(ExternalFocusState.CanRestoreFrom(input, 10, 10, false, inputs), "Input HWND rejected");
            Check(!ExternalFocusState.CanRestoreFrom(new IntPtr(1), 20, 10, false, inputs), "External app overridden");
            Check(!ExternalFocusState.CanRestoreFrom(new IntPtr(9), 10, 10, false, inputs), "Unknown own dialog overridden");
            Check(!ExternalFocusState.CanRestoreFrom(new IntPtr(1), 10, 10, true, inputs), "Settings entry interrupted");
            Check(!ExternalFocusState.CanRestoreFrom(IntPtr.Zero, 10, 10, false, inputs), "NULL foreground accepted");
        }

        private static object CallStatic(Type type, string name, params object[] args)
        {
            return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        }

        private static void CheckCornerAlpha(FrameworkElement visual, double width, double height)
        {
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            visual.UpdateLayout();
            foreach (double scale in new[] { 1.0, 1.25, 1.5 })
            {
                int w = (int)Math.Ceiling(width * scale), h = (int)Math.Ceiling(height * scale);
                var image = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                image.Render(visual);
                var pixels = new byte[w * h * 4];
                image.CopyPixels(pixels, w * 4, 0);
                foreach (int index in new[] { 0, w - 1, (h - 1) * w, h * w - 1 })
                    Check(pixels[index * 4 + 3] == 0, "Popup corner is opaque at scale " + scale);
                Check(pixels[((h / 2) * w + w / 2) * 4 + 3] > 0, "Popup body missing");
            }
        }

        private static void PopupCornerAlpha()
        {
            var tooltip = new ToolTip
            {
                Width = 180, Height = 70, Background = Brushes.White, HasDropShadow = false,
                Content = new Border { Background = Brushes.White },
                Template = (ControlTemplate)CallStatic(typeof(ControlPanelWindow), "CreateCompactToolTipTemplate")
            };
            tooltip.ApplyTemplate();
            CheckCornerAlpha(tooltip, 180, 70);
            var menu = (ContextMenu)CallStatic(typeof(ControlPanelWindow), "CreatePopupMenu");
            menu.Items.Add(new MenuItem { Header = "환경 설정" });
            menu.Items.Add(new MenuItem { Header = "TouchZoomBoard3 종료" });
            menu.ApplyTemplate();
            CheckCornerAlpha(menu, 190, 90);
            var option = (Border)CallStatic(typeof(ControlPanelWindow), "CreateGlassPopupSurface",
                new Border { Background = Brushes.White });
            CheckCornerAlpha(option, 180, 80);
        }

        private static void PopupNativeAlpha()
        {
            var popup = (Popup)CallStatic(typeof(ControlPanelWindow), "CreatePersistentOptionPopup");
            popup.Child = new RoundedSurfaceBorder
            {
                Width = 120, Height = 60, CornerRadius = new CornerRadius(18), Background = Brushes.White
            };
            popup.Placement = PlacementMode.AbsolutePoint;
            popup.HorizontalOffset = -10000;
            try
            {
                popup.IsOpen = true;
                var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                Check(source != null && source.UsesPerPixelOpacity, "Popup HWND has no per-pixel alpha");
                Check(LiquidGlassTheme.ApplyPopup(source, "regression-popup"), "Popup transparency setup failed");
                Check(source.CompositionTarget.BackgroundColor.A == 0, "Popup native background is opaque");
            }
            finally { popup.IsOpen = false; }
        }

        private static void SettingsCornerAlpha()
        {
            var window = new SettingsWindow(new UserSettings());
            try
            {
                Check(window.AllowsTransparency, "Settings must use per-pixel transparency");
                Check(((SolidColorBrush)window.Background).Color.A == 0, "HWND background is opaque");
                var chrome = (Border)window.Content;
                chrome.Measure(new Size(window.Width, window.Height));
                chrome.Arrange(new Rect(0, 0, window.Width, window.Height));
                chrome.UpdateLayout();
                Call(window, "UpdateChromeClip");
                foreach (double scale in new[] { 1.0, 1.25, 1.5 })
                {
                    int width = (int)Math.Ceiling(window.Width * scale);
                    int height = (int)Math.Ceiling(window.Height * scale);
                    var image = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    image.Render(chrome);
                    var pixels = new byte[width * height * 4];
                    image.CopyPixels(pixels, width * 4, 0);
                    foreach (int offset in new[] { 0, (width - 1) * 4, (height - 1) * width * 4,
                        ((height - 1) * width + width - 1) * 4 })
                        Check(pixels[offset + 3] == 0, "Opaque corner at DPI scale " + scale);
                    Check(pixels[((height / 2) * width + width / 2) * 4 + 3] > 0, "Window body vanished");
                }
            }
            finally { window.Close(); }
        }

        private static void CompactCenterAlignment()
        {
            var panel = new ControlPanelWindow(false);
            try
            {
                var root = Field<FrameworkElement>(panel, "compactPanelRoot");
                Check(FindCenteredGrid(root), "Manual compact centering was lost");
            }
            finally { panel.Shutdown(); }
        }

        private static void CircleDragGeometry()
        {
            // Exercise the shared geometry lifecycle used by all three input
            // routes. This does not simulate physical devices or WPF promotion.
            foreach (string input in new[] { "Mouse", "Touch", "Stylus" })
            {
                var window = new InteractionOverlayWindow();
                try
                {
                    window.SetMode(AppMode.Circle);
                    var kind = typeof(InteractionOverlayWindow).GetNestedType("ShapeInputKind", BindingFlags.NonPublic);
                    var center = new Point(200, 160);
                    Call(window, "BeginShape", center, Enum.Parse(kind, input));
                    var circle = Field<System.Windows.Shapes.Path>(window, "previewShape");
                    // Expands, shrinks, crosses the center, and moves along axes.
                    foreach (var offset in new[] { new Vector(30, 40), new Vector(-3, 4),
                        new Vector(-30, -40), new Vector(30, -40), new Vector(0, 0),
                        new Vector(0, -50), new Vector(-50, 0) })
                    {
                        Call(window, "UpdateShape", center + offset);
                        var geometry = (EllipseGeometry)circle.Data;
                        double radius = offset.Length;
                        Check(geometry.Center == center, "Circle center moved: " + input);
                        Check(Math.Abs(geometry.RadiusX - radius) < 0.0001 &&
                              Math.Abs(geometry.RadiusY - radius) < 0.0001, "Unequal or wrong circle radius");
                    }
                    Call(window, "CompleteShape", center + new Vector(30, 40));
                    var finalGeometry = (EllipseGeometry)circle.Data;
                    Check(finalGeometry.Bounds == new Rect(150, 110, 100, 100), "Wrong completed circle bounds");
                    var shapes = Field<Canvas>(window, "shapeCanvas");
                    Check(shapes.Children.Count == 1 && ReferenceEquals(shapes.Children[0], circle),
                        "Completion duplicated or replaced the preview");
                    Check(Field<object>(window, "previewShape") == null, "Preview remained active after release");
                    window.ClearAnnotations();
                    window.Undo();
                    Check(shapes.Children.Count == 1 && ReferenceEquals(shapes.Children[0], circle),
                        "Clear did not restore the circle");
                    window.Undo();
                    Check(!window.HasAnnotations, "Completed circle did not occupy one undo step");
                }
                finally { window.Shutdown(); }
            }
        }

        private static void CircleCancellation()
        {
            var window = new InteractionOverlayWindow();
            try
            {
                var kind = typeof(InteractionOverlayWindow).GetNestedType("ShapeInputKind", BindingFlags.NonPublic);
                var mouse = Enum.Parse(kind, "Mouse");
                var center = new Point(100, 100);
                foreach (var end in new[] { center, new Point(102, 102) })
                {
                    AddStroke(window, AppMode.Pen);
                    window.SetMode(AppMode.Circle);
                    Call(window, "BeginShape", center, mouse);
                    Call(window, "CompleteShape", end);
                    Check(Field<Canvas>(window, "shapeCanvas").Children.Count == 0, "Small tap made a circle");
                    window.Undo();
                    Check(!window.HasAnnotations, "Small tap consumed an undo step");
                }

                AddStroke(window, AppMode.Pen);
                window.SetMode(AppMode.Circle);
                Call(window, "BeginShape", center, mouse);
                Call(window, "UpdateShape", new Point(140, 130));
                window.SetMode(AppMode.Pen);
                Call(window, "CompleteShape", new Point(140, 130)); // Late up after cancellation.
                Check(Field<Canvas>(window, "shapeCanvas").Children.Count == 0, "Mode switch retained circle preview");
                window.Undo();
                Check(!window.HasAnnotations, "Cancelled preview consumed an undo step");

                AddStroke(window, AppMode.Pen);
                window.SetMode(AppMode.Circle);
                Call(window, "BeginShape", center, mouse);
                Call(window, "UpdateShape", new Point(140, 130));
                window.ClearAnnotations();
                window.Undo();
                Check(Field<Canvas>(window, "shapeCanvas").Children.Count == 0 &&
                      Field<AdaptiveInkCanvas>(window, "inkCanvas").Strokes.Count == 1,
                    "Clear snapshot included an unfinished circle");
                AddShape(window, AppMode.Circle);
                window.EndSession();
                window.Undo();
                Check(!window.HasAnnotations, "Session teardown retained circle history");
            }
            finally { window.Shutdown(); }
        }

        private static void CircleSelectionAndStyle()
        {
            var panel = new ControlPanelWindow(false);
            var window = new InteractionOverlayWindow();
            try
            {
                var options = Field<Dictionary<AppMode, Button>>(panel, "shapePopupButtons");
                Check(options.Count == 5 && options.ContainsKey(AppMode.Ellipse) && options.ContainsKey(AppMode.Circle),
                    "Circle must be separate from ellipse");
                AppMode selected = AppMode.Pointer;
                panel.ModeRequested += value => selected = value;
                options[AppMode.Circle].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(selected == AppMode.Circle, "Circle menu did not select the circle tool");
                panel.SetMode(selected);
                var icon = (Grid)Field<Button>(panel, "shapeButton").Content;
                var vector = (Canvas)icon.Children[0];
                var outline = ((System.Windows.Shapes.Path)vector.Children[0]).Data as EllipseGeometry;
                Check(outline != null && outline.RadiusX == outline.RadiusY, "Circle button did not display round icon");

                SetField(window, "shapeColor", Colors.Crimson);
                window.SetShapeWidth(8);
                AddShape(window, selected);
                var circle = (System.Windows.Shapes.Path)Field<Canvas>(window, "shapeCanvas").Children[0];
                Check(circle.StrokeThickness == 8 && ((SolidColorBrush)circle.Stroke).Color == Colors.Crimson,
                    "Circle ignored the shared shape style");
                var geometry = (EllipseGeometry)circle.Data;
                Check(Math.Abs(geometry.RadiusX - Math.Sqrt(14500)) < 0.0001,
                    "Stroke width changed the geometric radius");
            }
            finally { window.Shutdown(); panel.Shutdown(); }
        }

        private static void ColorSelectsMatchingTool()
        {
            Check(DrawingToolSelection.Resolve(DrawingStyleKind.Shape, AppMode.Pointer) == AppMode.Line,
                "First shape color must choose line");
            foreach (var shape in new[] { AppMode.Line, AppMode.Arrow, AppMode.Rectangle, AppMode.Ellipse, AppMode.Circle })
            {
                Check(DrawingToolSelection.Resolve(DrawingStyleKind.Shape, shape) == shape, "Last shape was lost");
                Check(DrawingToolSelection.Resolve(DrawingStyleKind.Pen, shape) == AppMode.Pen, "Pen color selected shape");
                Check(DrawingToolSelection.Resolve(DrawingStyleKind.Highlighter, shape) == AppMode.Highlighter,
                    "Highlighter color retained pen");
            }
            Check(new UserSettings().LastShapeMode == AppMode.Line, "New settings default to another shape");
            Check(DrawingToolSelection.NormalizeShape((AppMode)999) == AppMode.Line, "Invalid saved shape accepted");
        }

        private static void ToolOrderNormalization()
        {
            var order = PanelToolLayout.Parse("Undo,Pen,Undo,unknown,999,Eraser");
            Check(order.Take(3).SequenceEqual(new[] { PanelToolKind.Undo, PanelToolKind.Pen, PanelToolKind.Eraser }),
                "Valid custom order was lost");
            Check(order.Length == 9 && order.Distinct().Count() == 9, "Missing or duplicate tool");
            Check(PanelToolLayout.Parse(PanelToolLayout.Serialize(order)).SequenceEqual(order), "Order did not round trip");
            Check(PanelToolLayout.Parse(null).SequenceEqual(PanelToolLayout.DefaultOrder), "Legacy settings lost default");
        }

        private static void CarouselToolOrder()
        {
            var panel = new ControlPanelWindow(false);
            try
            {
                panel.SetDrawingColor(DrawingStyleKind.Pen, Colors.Crimson);
                panel.SetMode(AppMode.Pen);
                var slots = Field<Dictionary<PanelToolKind, UIElement>>(panel, "toolSlots");
                var original = slots[PanelToolKind.Pen];
                var swatch = ((StackPanel)original).Children[1];
                var order = PanelToolLayout.Normalize(new[] { PanelToolKind.Pen, PanelToolKind.Eraser, PanelToolKind.Undo });
                panel.SetToolOrder(order);
                var pages = Field<List<UIElement>>(panel, "carouselPages");
                var first = (StackPanel)pages[0];
                Check(pages.Count == 3 && first.Children.Count == 3, "Page geometry changed");
                Check(ReferenceEquals(first.Children[0], original) && ReferenceEquals(first.Children[1], slots[PanelToolKind.Eraser]) &&
                    ReferenceEquals(first.Children[2], slots[PanelToolKind.Undo]), "Favorite tools not grouped");
                Check(ReferenceEquals(((StackPanel)first.Children[0]).Children[1], swatch), "Color selector was rebuilt");
                Check(first.HorizontalAlignment == HorizontalAlignment.Center, "Custom page is not centered");
                panel.SetToolOrder(order.Reverse());
                panel.SetToolOrder(PanelToolLayout.DefaultOrder);
                Check(ReferenceEquals(((StackPanel)pages[0]).Children[0], original), "Reset duplicated a tool");
                Check(slots.Count == 9, "Repeated reorder added controls");
            }
            finally { panel.Shutdown(); }
        }

        private static void SettingsToolOrderStaging()
        {
            var settings = new UserSettings();
            var original = settings.ToolOrder.ToArray();
            var window = new SettingsWindow(settings);
            try
            {
                var list = Field<ListBox>(window, "toolOrderListBox");
                list.SelectedIndex = 3;
                Call(window, "MoveToolOrder", -1);
                var staged = Field<List<PanelToolKind>>(window, "editableToolOrder");
                Check(staged[2] == PanelToolKind.Eraser && list.SelectedIndex == 2, "Move lost selection");
                Check(settings.ToolOrder.SequenceEqual(original), "Unsaved editor changed live settings");
                list.SelectedIndex = 0;
                Call(window, "MoveToolOrder", -1);
                Check(staged[0] == PanelToolKind.Pen, "First tool moved outside list");
            }
            finally { window.Close(); }
            Check(settings.ToolOrder.SequenceEqual(original), "Cancel persisted staged order");
        }

        private static void SettingsScreenDrag()
        {
            var drag = new ScreenWindowDragState();
            var bounds = new NativeMethods.RECT { Left = -1600, Top = 100, Right = -1080, Bottom = 710 };
            int left, top;
            Check(!drag.TryGetPosition(new Point(0, 0), out left, out top), "Idle gesture moved a window");
            var press = new Point(-1500.25, 120.25);
            Check(drag.Begin(bounds, press), "Screen drag did not begin");
            Check(!drag.Begin(bounds, new Point(0, 0)), "Second contact replaced the active drag");
            for (int index = 1; index <= 100; index++)
            {
                Check(drag.TryGetPosition(new Point(press.X + index * 0.25, press.Y - index * 0.25),
                    out left, out top), "Active drag stopped");
            }
            Check(left == -1575 && top == 75, "Fractional-DPI moves accumulated rounding drift");
            drag.TryGetPosition(press, out left, out top);
            Check(left == bounds.Left && top == bounds.Top, "Returning to press did not restore the origin");
            drag.End();
            drag.End();
            Check(!drag.IsActive && !drag.TryGetPosition(press, out left, out top), "Cancelled drag kept moving");
            Check(drag.Begin(bounds, press), "A later drag could not begin after capture loss");
            drag.TryGetPosition(new Point(press.X + 2100, press.Y + 40), out left, out top);
            Check(left == 500 && top == 140, "Cross-monitor screen coordinates were scaled by zoom");
            drag.End();
            Check(!drag.Begin(new NativeMethods.RECT(), press), "Invalid native bounds accepted");
        }

        private static string IconPaths(DependencyObject root)
        {
            var path = root as System.Windows.Shapes.Path;
            var result = path == null ? string.Empty : path.Data + ":" + path.StrokeThickness +
                ":" + path.StrokeStartLineCap + ":" + path.StrokeLineJoin + ";";
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                result += IconPaths(VisualTreeHelper.GetChild(root, index));
            return result;
        }

        private static void ToolOrderIconsMatchPanel()
        {
            var settings = new UserSettings { LastShapeMode = AppMode.Circle };
            var window = new SettingsWindow(settings);
            var panel = new ControlPanelWindow(false);
            try
            {
                panel.SetMode(AppMode.Circle);
                var slots = Field<Dictionary<PanelToolKind, UIElement>>(panel, "toolSlots");
                var list = Field<ListBox>(window, "toolOrderListBox");
                Action verify = () =>
                {
                    foreach (ListBoxItem item in list.Items)
                    {
                        var kind = (PanelToolKind)item.Tag;
                        var row = (Grid)item.Content;
                        var preview = ((Border)row.Children[0]).Child;
                        var slot = (StackPanel)slots[kind];
                        var live = (UIElement)((Button)slot.Children[0]).Content;
                        var expected = IconPaths(live);
                        Check(expected.Length > 0 && IconPaths(preview) == expected,
                            "Order icon geometry or manual stroke weight differs: " + kind);
                        Check(!row.IsHitTestVisible, "Icon consumes row selection input");
                    }
                };
                verify();
                list.SelectedIndex = 3;
                Call(window, "MoveToolOrder", -1);
                verify();
                Check((PanelToolKind)((ListBoxItem)list.Items[2]).Tag == PanelToolKind.Eraser,
                    "Moved icon did not stay with its tool");
                Check(settings.ToolOrder.SequenceEqual(PanelToolLayout.DefaultOrder), "Preview persisted unsaved order");
            }
            finally { window.Close(); panel.Shutdown(); }
        }

        private static void FirstPressSelection()
        {
            var press = new ToolPressState();
            var point = new Point(10, 10);
            var bounds = new Rect(0, 0, 34, 34);
            for (var index = 0; index < 5; index++)
            {
                Check(press.Begin(point, true), "First down failed to select pen");
                Check(!press.End(point, bounds), "Up selected pen a second time");
            }
            Check(press.Begin(point, true), "Selection depends on capture");
            press.Cancel();
            Check(!press.End(point, bounds), "Lost capture caused repeated selection");
            Check(!press.Begin(point, false) && press.End(point, bounds), "Short shape/zoom press failed");
            press.Begin(point, true);
            Check(press.TryLongPress() && !press.TryLongPress(), "Long press invoked twice");
            Check(!press.End(point, bounds), "Long press also invoked short action");
            press.Begin(point, false);
            press.Move(new Point(30, 10), 12);
            Check(!press.TryLongPress() && !press.End(point, bounds), "Moved press opened a popup");
        }

        private static StylusPointCollection FilterInk(double zoom, bool delayed = false,
            AppMode mode = AppMode.Pen, double width = 4.0, int initialTimestamp = 1000)
        {
            var filter = new AdaptiveStylusFilter();
            filter.SetMode(mode);
            filter.SetStrokeWidth(width);
            filter.SetZoomFactor(zoom);
            Call(filter, "ResetStroke");
            var result = new StylusPointCollection();
            for (var packet = 0; packet < 32; packet++)
            {
                if (delayed && (packet == 10 || packet == 22)) System.Threading.Thread.Sleep(100);
                var points = new StylusPointCollection();
                for (var sample = 0; sample < 4; sample++)
                {
                    var phase = (packet * 4 + sample) * 0.075;
                    points.Add(new StylusPoint(200 + 60 * Math.Cos(phase), 200 + 45 * Math.Sin(phase), 0.5f));
                }
                Call(filter, "FilterPoints", points, unchecked(initialTimestamp + packet * 8), packet == 31);
                foreach (var point in points) result.Add(point);
            }
            return result;
        }

        private static void SameInk(StylusPointCollection expected, StylusPointCollection actual, string message)
        {
            Check(expected.Count == actual.Count, message + ": count");
            for (var index = 0; index < expected.Count; index++)
                Check(Math.Abs(expected[index].X - actual[index].X) < 1e-9 &&
                    Math.Abs(expected[index].Y - actual[index].Y) < 1e-9 &&
                    expected[index].PressureFactor == actual[index].PressureFactor, message + ": point " + index);
        }

        private static void InkZoomInvariant()
        {
            foreach (var mode in new[] { AppMode.Pen, AppMode.Highlighter })
            {
                var width = mode == AppMode.Highlighter ? 36.0 : 4.0;
                var expected = FilterInk(1.0, mode: mode, width: width);
                foreach (var zoom in new[] { 1.25, 1.5, 2.0, 3.0, 5.0 })
                    SameInk(expected, FilterInk(zoom, mode: mode, width: width), "Zoom changed saved geometry");
            }
            var window = new InteractionOverlayWindow();
            try
            {
                var canvas = Field<AdaptiveInkCanvas>(window, "inkCanvas");
                var filter = Field<AdaptiveStylusFilter>(canvas, "filter");
                var generation = Field<int>(filter, "cancellationGeneration");
                window.SetDiagnosticContext(3.0, 1.0);
                window.SetDiagnosticContext(1.0, 1.0);
                Check(Field<int>(filter, "cancellationGeneration") == generation, "Background zoom cancels active smoothing");
                var stroke = new Stroke(FilterInk(3.0));
                var saved = stroke.StylusPoints.Clone();
                canvas.Strokes.Add(stroke);
                window.SetDiagnosticContext(3.0, 1.5);
                window.SetDiagnosticContext(1.0, 1.5);
                SameInk(saved, stroke.StylusPoints, "Zoom reset altered existing ink");
            }
            finally { window.Shutdown(); }
        }

        private static void InkDeliveryDelayInvariant()
        {
            SameInk(FilterInk(1.0), FilterInk(3.0, delayed: true), "Callback delay changed geometry");
        }

        private static void InkPacketTimingAndBounds()
        {
            Check(Math.Abs(AdaptiveStylusFilter.GetPointIntervalSeconds(0.008, 16) - 0.0005) < 1e-12,
                "Batched packet was stretched");
            SameInk(FilterInk(2.0), FilterInk(2.0, initialTimestamp: int.MaxValue - 40), "Timestamp rollover changed ink");
            foreach (var mode in new[] { AppMode.Pen, AppMode.Highlighter })
            {
                foreach (var width in new[] { 4.0, 12.0, 36.0 })
                {
                    var filtered = FilterInk(5.0, mode: mode, width: width);
                    var maxLag = mode == AppMode.Highlighter ? 10.0 : width >= 12 ? 2.0 : 6.0;
                    for (var index = 0; index < filtered.Count; index++)
                    {
                        var phase = index * 0.075;
                        var raw = new Point(200 + 60 * Math.Cos(phase), 200 + 45 * Math.Sin(phase));
                        var corrected = new Point(filtered[index].X, filtered[index].Y);
                        Check((raw - corrected).Length <= maxLag + 1e-8, "Correction exceeded the lag bound");
                    }
                }
            }
        }

        private static bool FindCenteredGrid(DependencyObject root)
        {
            var grid = root as Grid;
            if (grid != null && grid.ColumnDefinitions.Count == 2 &&
                grid.HorizontalAlignment == HorizontalAlignment.Center) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindCenteredGrid(VisualTreeHelper.GetChild(root, i))) return true;
            return false;
        }
    }
}
