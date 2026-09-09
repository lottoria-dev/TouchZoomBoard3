using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class AboutWindow : Window
    {
        private const string DistributionPage = "https://mathtime.kr/?page=touchzoomboard";
        private const string DeveloperEmail = "mathtime.ai@gmail.com";

        internal AboutWindow()
        {
            Title = "TouchZoomBoard3 정보";
            Width = 470;
            Height = 420;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(LiquidGlassTheme.FallbackWindowColor);
            Foreground = LiquidGlassTheme.PrimaryTextBrush;

            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "TouchZoomBoard3 3.0.2",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            root.Children.Add(new TextBlock
            {
                Text = "전자칠판 수업용 상호작용 줌·필기 도구",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 0, 18)
            });

            root.Children.Add(CreateInfoLine("화면 복구", "Ctrl+Alt+Shift+Esc"));
            root.Children.Add(CreateLinkLine("배포 페이지", DistributionPage, DistributionPage));
            root.Children.Add(CreateLinkLine("개발자 연락처", DeveloperEmail, "mailto:" + DeveloperEmail));

            root.Children.Add(new Border
            {
                Height = 1,
                Background = LiquidGlassTheme.HairlineBrush,
                Margin = new Thickness(0, 16, 0, 14)
            });
            root.Children.Add(new TextBlock
            {
                Text = "외부 라이브러리 없이 Windows 기본 기능만 사용합니다.\n" +
                       "사용자 자료와 화면 내용을 수집하거나 전송하지 않습니다.\n\n" +
                       "Copyright © 2026 lottoria-dev",
                FontSize = 12.5,
                LineHeight = 19,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                TextWrapping = TextWrapping.Wrap
            });

            var chromeRoot = new Grid();
            chromeRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            chromeRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var titleBar = CreateTitleBar();
            Grid.SetRow(titleBar, 0);
            chromeRoot.Children.Add(titleBar);
            var body = new Border
            {
                Child = root,
                Padding = new Thickness(22, 18, 22, 16),
                Background = Brushes.Transparent
            };
            Grid.SetRow(body, 1);
            chromeRoot.Children.Add(body);
            Content = new Border
            {
                Child = chromeRoot,
                Opacity = 1.0,
                CornerRadius = new CornerRadius(22),
                Background = LiquidGlassTheme.CreateWindowVeilBrush(),
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(1)
            };
            SourceInitialized += (sender, args) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                NativeMethods.EnsureToolWindowStyle(handle);
                var source = HwndSource.FromHwnd(handle);
                LiquidGlassTheme.ApplyWindow(this, source, handle, "about-window");
                ApplyWindowRegion(handle);
            };
            SizeChanged += (sender, args) => Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() => ApplyWindowRegion(new WindowInteropHelper(this).Handle)));
        }

        private Border CreateTitleBar()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.Children.Add(new TextBlock
            {
                Text = "TouchZoomBoard3  ·  정보",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var closeButton = new Button
            {
                Content = "×",
                Width = 32,
                Height = 28,
                Margin = new Thickness(0, 6, 7, 6),
                Padding = new Thickness(0),
                FontSize = 18,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = CreateTitleButtonTemplate()
            };
            closeButton.Click += (sender, args) => Close();
            Grid.SetColumn(closeButton, 1);
            grid.Children.Add(closeButton);
            grid.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                var source = args.OriginalSource as DependencyObject;
                if (source != null && FindVisualParent<Button>(source) != null) return;
                if (args.ClickCount == 1) DragMove();
            };
            return new Border
            {
                Child = grid,
                Background = LiquidGlassTheme.CreateHeaderBrush(),
                BorderBrush = new SolidColorBrush(Color.FromArgb(
                    LiquidGlassTheme.IsDark ? (byte)64 : (byte)52,
                    LiquidGlassTheme.HairlineColor.R,
                    LiquidGlassTheme.HairlineColor.G,
                    LiquidGlassTheme.HairlineColor.B)),
                BorderThickness = new Thickness(0, 0, 0, 0.75),
                CornerRadius = new CornerRadius(21, 21, 0, 0)
            };
        }

        private static ControlTemplate CreateTitleButtonTemplate()
        {
            var chrome = new FrameworkElementFactory(typeof(Border), "TitleButtonChrome");
            chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                LiquidGlassTheme.CreatePopupHoverItemBrush(), "TitleButtonChrome"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                LiquidGlassTheme.CreatePopupPressedItemBrush(), "TitleButtonChrome"));
            template.Triggers.Add(pressed);
            return template;
        }

        private void ApplyWindowRegion(IntPtr handle)
        {
            if (handle == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0) return;
            NativeMethods.TryApplyAntialiasedRoundedCorners(handle);
            var screen = Forms.Screen.FromHandle(handle) ?? Forms.Screen.PrimaryScreen;
            var scale = NativeMethods.GetScaleForPoint(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
            NativeMethods.ApplyRoundedWindowRegion(
                handle,
                Math.Max(1, (int)Math.Round(ActualWidth * scale)),
                Math.Max(1, (int)Math.Round(ActualHeight * scale)),
                Math.Max(8, (int)Math.Round(22 * scale)));
        }

        private static T FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                var match = current as T;
                if (match != null) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static FrameworkElement CreateInfoLine(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            var valueBlock = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            return row;
        }

        private static FrameworkElement CreateLinkLine(string label, string text, string target)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });

            var link = new Hyperlink(new Run(text))
            {
                NavigateUri = new Uri(target),
                // 테두리용 AccentBrush는 흰색 그라데이션 정지점을 포함하므로
                // 밝은 정보창에서 링크 글자가 사라진다. 텍스트 전용 단색을 사용한다.
                Foreground = LiquidGlassTheme.LinkBrush,
                FontWeight = FontWeights.SemiBold,
                TextDecorations = TextDecorations.Underline
            };
            link.RequestNavigate += OpenLink;
            var valueBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
            valueBlock.Inlines.Add(link);
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            return row;
        }

        private static void OpenLink(object sender, RequestNavigateEventArgs args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                DebugLog.Write("정보창 링크를 열지 못했습니다.", exception);
                MessageBox.Show(
                    "기본 브라우저 또는 메일 프로그램에서 링크를 열지 못했습니다.\n\n" + args.Uri.AbsoluteUri,
                    "TouchZoomBoard3",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            args.Handled = true;
        }
    }
}
