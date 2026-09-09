using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class SettingsWindow : Window
    {
        private sealed class ScreenChoice
        {
            internal Forms.Screen Screen { get; set; }
            public override string ToString()
            {
                if (Screen == null)
                {
                    return "화면 없음";
                }
                var name = Screen.DeviceName ?? string.Empty;
                const string prefix = @"\\.\";
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    name = name.Substring(prefix.Length);
                }
                return name.ToLowerInvariant();
            }
        }

        private sealed class DialogOwner : Forms.IWin32Window
        {
            internal DialogOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; private set; }
        }

        private readonly UserSettings settings;
        private readonly ComboBox screenComboBox;
        private readonly Slider opacitySlider;
        private readonly TextBlock opacityValue;
        private readonly Slider zoomSlider;
        private readonly TextBlock zoomValue;
        private readonly CheckBox tooltipsCheckBox;
        private readonly CheckBox startupCheckBox;
        private readonly Dictionary<PastelThemeColor, Button> glassLightButtons =
            new Dictionary<PastelThemeColor, Button>();
        private PastelThemeColor selectedPastelTheme;
        private bool useCustomGlassLightColor;
        private Color selectedCustomGlassLightColor;
        private Button customGlassLightButton;
        private Border customGlassLightPreview;
        private IntPtr windowHandle;

        internal event Action Applied;
        internal event Action ResetPositionRequested;
        internal IntPtr WindowHandle => windowHandle;

        internal SettingsWindow(UserSettings settings)
        {
            this.settings = settings;
            selectedPastelTheme = settings.PastelTheme;
            useCustomGlassLightColor = settings.UseCustomGlassLightColor;
            selectedCustomGlassLightColor = ColorFromArgb(settings.CustomGlassLightColorArgb);
            Title = "TouchZoomBoard3 환경 설정";
            Width = 520;
            Height = 610;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(LiquidGlassTheme.FallbackWindowColor);
            Foreground = LiquidGlassTheme.PrimaryTextBrush;
            Stylus.SetIsPressAndHoldEnabled(this, false);

            var root = new Grid
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(18, 12, 18, 16)
            };
            root.Resources[typeof(ScrollBar)] = CreateGlassScrollBarStyle();
            Stylus.SetIsPressAndHoldEnabled(root, false);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock
            {
                Text = "프로그램 환경 설정",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var settingsPage = new StackPanel { Margin = new Thickness(8, 10, 8, 8) };

            screenComboBox = CreateChoiceComboBox();
            var screens = Forms.Screen.AllScreens.Select(screen => new ScreenChoice { Screen = screen }).ToArray();
            foreach (var choice in screens)
            {
                screenComboBox.Items.Add(choice);
            }
            screenComboBox.SelectedItem = screens.FirstOrDefault(choice =>
                string.Equals(choice.Screen.DeviceName, settings.TargetDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? screens.FirstOrDefault(choice => !choice.Screen.Primary)
                ?? screens.FirstOrDefault();
            settingsPage.Children.Add(CreateSettingBlock("대상 화면", "패널과 확대 화면을 표시할 전자칠판을 선택합니다.", screenComboBox));

            opacityValue = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            opacitySlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Round(settings.GlassTintOpacity /
                    LiquidGlassTheme.MaximumPanelGlassStrength * 100),
                TickFrequency = 10,
                SmallChange = 1,
                LargeChange = 10,
                IsSnapToTickEnabled = false,
                Width = 300,
                Style = CreateGlassSliderStyle()
            };
            opacitySlider.ValueChanged += (sender, args) => UpdateValueLabels();
            settingsPage.Children.Add(CreateSettingBlock(
                "패널 유리판 농도",
                "0%는 패널판이 완전히 사라져 버튼만 남고, 100%는 뒤 화면이 선명하게 보이는 얇은 투명 유리판입니다. 배경 흐림 없이 유리판 밝기만 조절하며 버튼과 이동 손잡이는 모든 값에서 터치할 수 있습니다.",
                CreateSliderRow(opacitySlider, opacityValue)));

            settingsPage.Children.Add(CreateSettingBlock(
                "유리 굴절광 색상",
                "선택한 빛은 투명 패널의 옅은 표면색, 상·좌측 내부 굴절대와 돌출 버튼에 함께 반영됩니다. 프리셋을 고르거나 직접 원하는 색을 지정한 뒤 저장하면 패널이 즉시 다시 만들어집니다.",
                CreateGlassLightPalette()));

            zoomValue = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            zoomSlider = new Slider
            {
                Minimum = 125,
                Maximum = 300,
                Value = Math.Round(settings.DefaultZoom * 100),
                TickFrequency = 5,
                SmallChange = 5,
                LargeChange = 10,
                IsSnapToTickEnabled = true,
                Width = 300,
                Style = CreateGlassSliderStyle()
            };
            zoomSlider.ValueChanged += (sender, args) => UpdateValueLabels();
            settingsPage.Children.Add(CreateSettingBlock("기본 확대 배율", "빠른 확대 버튼을 누를 때 적용되는 배율입니다. 125~300% 범위에서 5% 단위로 설정할 수 있습니다.", CreateSliderRow(zoomSlider, zoomValue)));

            tooltipsCheckBox = new CheckBox
            {
                Content = "버튼 도움말 표시",
                IsChecked = settings.TooltipsEnabled,
                FontSize = 14,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Margin = new Thickness(4, 7, 0, 7),
                Style = CreateGlassCheckBoxStyle()
            };
            startupCheckBox = new CheckBox
            {
                Content = "Windows 시작 시 자동 실행",
                IsChecked = settings.IsStartWithWindowsEnabled(),
                FontSize = 14,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Margin = new Thickness(4, 7, 0, 12),
                Style = CreateGlassCheckBoxStyle()
            };
            settingsPage.Children.Add(tooltipsCheckBox);
            settingsPage.Children.Add(startupCheckBox);

            var tabs = new TabControl
            {
                Background = Brushes.Transparent,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                ItemContainerStyle = CreateGlassTabItemStyle(),
                Template = CreateGlassTabControlTemplate(),
                Padding = new Thickness(2)
            };
            tabs.Items.Add(new TabItem
            {
                Header = "환경",
                Content = new ScrollViewer
                {
                    Content = settingsPage,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                }
            });
            tabs.Items.Add(new TabItem
            {
                Header = "사용법 안내",
                Content = CreateHelpPage()
            });
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            var buttons = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var resetButton = CreateButton("패널 위치 초기화", false);
            resetButton.Click += (sender, args) => ResetPositionRequested?.Invoke();
            Grid.SetColumn(resetButton, 0);
            resetButton.HorizontalAlignment = HorizontalAlignment.Left;
            buttons.Children.Add(resetButton);

            var cancelButton = CreateButton("취소", false);
            cancelButton.Click += (sender, args) => Close();
            Grid.SetColumn(cancelButton, 1);
            buttons.Children.Add(cancelButton);

            var saveButton = CreateButton("저장", true);
            saveButton.Click += (sender, args) => SaveAndClose();
            Grid.SetColumn(saveButton, 2);
            buttons.Children.Add(saveButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            var chromeRoot = new Grid();
            chromeRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            chromeRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var titleBar = CreateTitleBar();
            Grid.SetRow(titleBar, 0);
            chromeRoot.Children.Add(titleBar);
            Grid.SetRow(root, 1);
            chromeRoot.Children.Add(root);

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
                windowHandle = new WindowInteropHelper(this).Handle;
                NativeMethods.EnsureToolWindowStyle(windowHandle);
                var source = HwndSource.FromHwnd(windowHandle);
                LiquidGlassTheme.ApplyWindow(this, source, windowHandle, "settings-window");
                ApplyWindowRegion();
            };
            SizeChanged += (sender, args) => Dispatcher.BeginInvoke(
                DispatcherPriority.Render, new Action(ApplyWindowRegion));
            UpdateValueLabels();
        }

        internal void EnsureToolWindowStyle()
        {
            NativeMethods.EnsureToolWindowStyle(windowHandle);
            ApplyWindowRegion();
        }

        private Border CreateTitleBar()
        {
            var grid = new Grid { Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.Children.Add(new TextBlock
            {
                Text = "TouchZoomBoard3  ·  환경 설정",
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
                Padding = new Thickness(0),
                Margin = new Thickness(0, 6, 7, 6),
                FontSize = 18,
                FontWeight = FontWeights.Light,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Template = CreateTitleButtonTemplate()
            };
            closeButton.Click += (sender, args) => Close();
            Stylus.SetIsPressAndHoldEnabled(closeButton, false);
            AttachDirectTouchActivation(closeButton);
            Grid.SetColumn(closeButton, 1);
            grid.Children.Add(closeButton);

            grid.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                if (args.OriginalSource is DependencyObject source &&
                    FindVisualParent<Button>(source) != null) return;
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

        private void ApplyWindowRegion()
        {
            if (windowHandle == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0) return;
            NativeMethods.TryApplyAntialiasedRoundedCorners(windowHandle);
            var screen = Forms.Screen.FromHandle(windowHandle) ?? Forms.Screen.PrimaryScreen;
            var scale = NativeMethods.GetScaleForPoint(screen.Bounds.Left + 1, screen.Bounds.Top + 1);
            NativeMethods.ApplyRoundedWindowRegion(
                windowHandle,
                Math.Max(1, (int)Math.Round(ActualWidth * scale)),
                Math.Max(1, (int)Math.Round(ActualHeight * scale)),
                Math.Max(8, (int)Math.Round(22 * scale)));
        }

        private static T FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static ScrollViewer CreateHelpPage()
        {
            var stack = new StackPanel { Margin = new Thickness(8, 10, 8, 8) };
            stack.Children.Add(CreateHelpIntro());
            stack.Children.Add(CreateHelpBlock(
                "1. 작은 터치 패널 열기",
                "프로그램을 처음 실행하거나 트레이에서 패널을 켜면 작은 터치 패널이 나타납니다. 오른쪽의 터치·확대 아이콘을 짧게 누르면 전체 도구 패널이 열립니다. 왼쪽의 4방향 화살표 부분을 끌면 위치를 옮길 수 있습니다.\n\n전체 패널 상단 가운데의 같은 아이콘을 짧게 누르면 다시 작은 패널로 접힙니다."));
            stack.Children.Add(CreateHelpBlock(
                "2. 상호작용 줌과 미니맵",
                "상단 배율 버튼을 짧게 누르면 환경설정에 저장한 기본 배율로 바로 확대됩니다. 길게 누르면 100~500% 배율 목록이 열립니다. 확대 중에는 작은 미니맵이 조작 패널 아래에 붙고, 공간이 부족하면 위에 표시됩니다.\n\n확대 화면의 버튼이나 조작점은 한 손가락으로 직접 조작합니다. 확대 영역을 옮기려면 미니맵을 누르거나 끌어주세요. 두 손가락을 벌리거나 오므리면 배율이 바뀌고, 두 손가락을 같은 방향으로 움직이면 문서가 스크롤됩니다."));
            stack.Children.Add(CreateHelpBlock(
                "3. 필기하기",
                "◀ ▶로 ‘필기’ 메뉴를 선택한 뒤 펜·형광펜·모양을 누릅니다. 각 버튼 아래의 짧은 색상 막대를 누르면 16색 팔레트가 열립니다.\n\n펜과 형광펜 버튼을 길게 누르면 각각의 굵기를 선택할 수 있습니다. 모양 버튼은 짧게 누르면 사각형·타원·선분·화살표를 길게 누르면 테두리 굵기를 선택할 수 있습니다."));
            stack.Children.Add(CreateHelpBlock(
                "4. 지우고 되돌리기",
                "‘지우기’ 메뉴에는 지우개·되돌리기·전체 지움이 있습니다. 지우개를 선택하고 지울 선이나 도형을 터치합니다. 되돌리기는 마지막 작업 하나만 취소하며, 전체 지움은 현재 필기를 모두 지웁니다."));
            stack.Children.Add(CreateHelpBlock(
                "5. 수업 자료를 다시 조작하기",
                "‘조작’ 메뉴의 자료 조작 버튼을 누르면 확대 상태를 유지하면서 브라우저·프레젠테이션을 터치할 수 있습니다. 기존 필기는 보존되므로 다시 펜을 선택하면 이어서 사용할 수 있습니다.\n\n미니맵 위치 버튼은 미니맵을 패널의 위쪽과 아래쪽 사이에서 전환합니다. 수업 화면 종료 버튼은 확대와 필기를 모두 종료합니다."));
            stack.Children.Add(CreateHelpBlock(
                "6. Windows 마우스 휠",
                "패널이나 미니맵 위에서는 마우스 휠만으로 확대·축소할 수 있습니다. 화면 이동 모드에서도 휠은 확대·축소로 작동합니다.\n\n자료 조작 모드에서는 일반 휠을 웹페이지나 문서의 스크롤로 전달합니다. 커서가 가리키는 곳을 중심으로 확대·축소하려면 Ctrl 키를 누른 채 휠을 사용하세요."));
            stack.Children.Add(CreateHelpBlock(
                "7. 설정·종료와 긴급 복구",
                "패널을 마우스 오른쪽 버튼으로 누르면 환경 설정과 TouchZoomBoard3 종료 메뉴가 열립니다. 작은 패널의 터치·확대 아이콘 또는 전체 패널 상단 가운데 아이콘을 약 0.75초간 길게 눌러도 설정창을 열 수 있습니다. 전자칠판 터치에서는 수업을 가리지 않도록 버튼 툴팁을 표시하지 않으며, 실제 마우스를 올렸을 때만 표시합니다.\n\n패널과 필기 화면은 프레젠테이션의 키보드 포커스를 가져가지 않습니다. 화면이 이상하거나 입력이 막히면 Ctrl+Alt+Shift+Esc를 눌러 확대·필기 화면을 강제로 정상화하거나 트레이의 긴급 화면 복구를 사용하세요."));
            return new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private static Border CreateHelpIntro()
        {
            return new Border
            {
                Background = LiquidGlassTheme.CreateCardBrush(),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock
                {
                    Text = "빠른 시작: 작은 패널 터치 → 상단 배율 터치 → 본문 자료 조작 / 패널 결합 미니맵 이동 / 두 손가락 핀치",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LiquidGlassTheme.PrimaryTextBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        private static Border CreateHelpBlock(string title, string description)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = LiquidGlassTheme.PrimaryTextBrush
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12.5,
                LineHeight = 18,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            return new Border
            {
                Child = stack,
                Background = LiquidGlassTheme.CreateCardBrush(),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13),
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static Border CreateSettingBlock(string title, string description, UIElement control)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = LiquidGlassTheme.PrimaryTextBrush
            });
            stack.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = LiquidGlassTheme.SecondaryTextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 8)
            });
            stack.Children.Add(control);
            return new Border
            {
                Child = stack,
                Background = Brushes.Transparent,
                BorderBrush = LiquidGlassTheme.CreatePopupSeparatorBrush(),
                BorderThickness = new Thickness(0, 0, 0, 0.65),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(5, 10, 5, 13),
                Margin = new Thickness(0, 0, 0, 2)
            };
        }

        private static Grid CreateSliderRow(Slider slider, TextBlock value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            Grid.SetColumn(slider, 0);
            Grid.SetColumn(value, 1);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(slider);
            grid.Children.Add(value);
            return grid;
        }

        private UIElement CreateGlassLightPalette()
        {
            var panel = new WrapPanel { Margin = new Thickness(-3, -2, 0, 0) };
            AddGlassLightChoice(panel, PastelThemeColor.Neutral, "중성");
            AddGlassLightChoice(panel, PastelThemeColor.Sky, "하늘");
            AddGlassLightChoice(panel, PastelThemeColor.Mint, "민트");
            AddGlassLightChoice(panel, PastelThemeColor.Lavender, "라벤더");
            AddGlassLightChoice(panel, PastelThemeColor.Rose, "로즈");
            AddGlassLightChoice(panel, PastelThemeColor.Peach, "피치");

            customGlassLightPreview = CreateLightSwatch(selectedCustomGlassLightColor);
            customGlassLightButton = CreateGlassLightChoiceButton(
                "직접 선택", customGlassLightPreview);
            customGlassLightButton.Click += (sender, args) => ChooseCustomGlassLightColor();
            panel.Children.Add(customGlassLightButton);
            UpdateGlassLightSelection();
            return panel;
        }

        private void AddGlassLightChoice(
            Panel panel,
            PastelThemeColor theme,
            string name)
        {
            var button = CreateGlassLightChoiceButton(
                name, CreateLightSwatch(LiquidGlassTheme.GetPastelPreview(theme)));
            button.Click += (sender, args) =>
            {
                selectedPastelTheme = theme;
                useCustomGlassLightColor = false;
                UpdateGlassLightSelection();
            };
            glassLightButtons[theme] = button;
            panel.Children.Add(button);
        }

        private static Button CreateGlassLightChoiceButton(string name, Border swatch)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var button = new Button
            {
                Content = content,
                Width = 104,
                Height = 38,
                Margin = new Thickness(3),
                Padding = new Thickness(7, 3, 7, 3),
                Background = LiquidGlassTheme.CreateButtonBrush(false),
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(0.65),
                Template = CreateGlassButtonTemplate(),
                Cursor = Cursors.Hand
            };
            Stylus.SetIsPressAndHoldEnabled(button, false);
            AttachDirectTouchActivation(button);
            return button;
        }

        private static Border CreateLightSwatch(Color color)
        {
            return new Border
            {
                Width = 25,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(72, 62, 74, 88)),
                BorderThickness = new Thickness(0.65)
            };
        }

        private void ChooseCustomGlassLightColor()
        {
            using (var dialog = new Forms.ColorDialog
            {
                AnyColor = true,
                FullOpen = true,
                SolidColorOnly = true,
                Color = System.Drawing.Color.FromArgb(
                    selectedCustomGlassLightColor.R,
                    selectedCustomGlassLightColor.G,
                    selectedCustomGlassLightColor.B)
            })
            {
                var result = windowHandle == IntPtr.Zero
                    ? dialog.ShowDialog()
                    : dialog.ShowDialog(new DialogOwner(windowHandle));
                if (result != Forms.DialogResult.OK) return;
                selectedCustomGlassLightColor = Color.FromRgb(
                    dialog.Color.R, dialog.Color.G, dialog.Color.B);
                customGlassLightPreview.Background =
                    new SolidColorBrush(selectedCustomGlassLightColor);
                useCustomGlassLightColor = true;
                UpdateGlassLightSelection();
            }
        }

        private void UpdateGlassLightSelection()
        {
            foreach (var pair in glassLightButtons)
                SetGlassLightChoiceSelected(
                    pair.Value, !useCustomGlassLightColor && pair.Key == selectedPastelTheme);
            SetGlassLightChoiceSelected(customGlassLightButton, useCustomGlassLightColor);
        }

        private static void SetGlassLightChoiceSelected(Button button, bool selected)
        {
            if (button == null) return;
            button.Background = selected
                ? LiquidGlassTheme.CreatePopupActiveItemBrush()
                : LiquidGlassTheme.CreateButtonBrush(false);
            button.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(52, 67, 82))
                : LiquidGlassTheme.HairlineBrush;
            button.BorderThickness = new Thickness(selected ? 1.7 : 0.65);
        }

        private static Color ColorFromArgb(int argb)
        {
            return Color.FromRgb(
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
        }

        private static int ToArgb(Color color)
        {
            return unchecked((int)(0xFF000000u |
                ((uint)color.R << 16) |
                ((uint)color.G << 8) |
                color.B));
        }

        private static Button CreateButton(string text, bool accent)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 76,
                Height = 34,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(12, 4, 12, 4),
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Background = LiquidGlassTheme.CreateButtonBrush(accent),
                BorderBrush = accent ? LiquidGlassTheme.AccentBrush : LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(accent ? 1.0 : 0.8),
                Template = CreateGlassButtonTemplate()
            };
            Stylus.SetIsPressAndHoldEnabled(button, false);
            AttachDirectTouchActivation(button);
            return button;
        }

        private static ComboBox CreateChoiceComboBox()
        {
            var comboBox = new ComboBox
            {
                Height = 36,
                Foreground = LiquidGlassTheme.PrimaryTextBrush,
                Background = LiquidGlassTheme.CreateButtonBrush(false),
                BorderBrush = LiquidGlassTheme.HairlineBrush,
                BorderThickness = new Thickness(0.85),
                ItemContainerStyle = CreateGlassComboBoxItemStyle(),
                Padding = new Thickness(10, 3, 34, 3),
                Template = CreateGlassComboBoxTemplate()
            };
            comboBox.DropDownOpened += (sender, args) => comboBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    var popup = comboBox.Template.FindName("PART_Popup", comboBox) as Popup;
                    var visual = popup?.Child as Visual;
                    var source = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                    if (source == null || source.Handle == IntPtr.Zero) return;
                    NativeMethods.EnsureToolWindowStyle(source.Handle);
                    NativeMethods.TryApplyAntialiasedRoundedCorners(source.Handle);
                    LiquidGlassTheme.ApplyPopup(source, "settings-combobox");
                    NativeMethods.ApplyRoundedWindowRegionFromCurrentBounds(source.Handle, 18);
                }));
            return comboBox;
        }

        private static ControlTemplate CreateGlassButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "ButtonChrome");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(17));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                LiquidGlassTheme.CreatePopupHoverItemBrush(), "ButtonChrome"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                LiquidGlassTheme.CreatePopupPressedItemBrush(), "ButtonChrome"));
            template.Triggers.Add(pressed);
            return template;
        }

        private static Style CreateGlassComboBoxItemStyle()
        {
            var chrome = new FrameworkElementFactory(typeof(Border), "ItemChrome");
            chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            chrome.SetValue(Border.MarginProperty, new Thickness(3, 1, 3, 1));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            chrome.AppendChild(presenter);

            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            style.Setters.Add(new Setter(Control.TemplateProperty,
                new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = chrome }));
            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty,
                LiquidGlassTheme.CreatePopupHoverItemBrush()));
            style.Triggers.Add(highlighted);
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty,
                LiquidGlassTheme.CreatePopupActiveItemBrush()));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(selected);
            return style;
        }

        private static ControlTemplate CreateGlassComboBoxTemplate()
        {
            var chrome = new FrameworkElementFactory(typeof(Border), "ComboChrome");
            chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            chrome.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var layers = new FrameworkElementFactory(typeof(Grid));
            var selection = new FrameworkElementFactory(typeof(ContentPresenter));
            selection.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            selection.SetValue(ContentPresenter.ContentTemplateProperty,
                new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            selection.SetValue(ContentPresenter.ContentTemplateSelectorProperty,
                new TemplateBindingExtension(ComboBox.ItemTemplateSelectorProperty));
            selection.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            selection.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            selection.SetValue(ContentPresenter.MarginProperty, new Thickness(11, 0, 36, 0));
            selection.SetValue(UIElement.IsHitTestVisibleProperty, false);
            layers.AppendChild(selection);

            var toggle = new FrameworkElementFactory(typeof(ToggleButton), "DropDownToggle");
            toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggle.SetValue(Control.BorderBrushProperty, Brushes.Transparent);
            toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggle.SetValue(Control.FocusableProperty, false);
            toggle.SetValue(Control.TemplateProperty, CreateGlassComboToggleTemplate());
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            });
            layers.AppendChild(toggle);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, false);
            popup.SetValue(Popup.StaysOpenProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.None);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            });

            var popupChrome = new FrameworkElementFactory(typeof(Border));
            popupChrome.SetValue(Border.BackgroundProperty, LiquidGlassTheme.CreatePopupGlassBrush());
            popupChrome.SetValue(Border.BorderBrushProperty, LiquidGlassTheme.CreatePopupBorderBrush());
            popupChrome.SetValue(Border.BorderThicknessProperty, new Thickness(0.85));
            popupChrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            popupChrome.SetValue(Border.PaddingProperty, new Thickness(3));
            popupChrome.SetValue(FrameworkElement.MaxHeightProperty, 300.0);
            popupChrome.SetValue(UIElement.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = LiquidGlassTheme.IsDark ? 0.28 : 0.19,
                Color = Colors.Black
            });
            popupChrome.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(items);
            popupChrome.AppendChild(scroll);
            popup.AppendChild(popupChrome);
            layers.AppendChild(popup);
            chrome.AppendChild(layers);

            var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = chrome };
            var open = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            open.Setters.Add(new Setter(Border.BorderBrushProperty,
                LiquidGlassTheme.AccentBrush, "ComboChrome"));
            template.Triggers.Add(open);
            return template;
        }

        private static ControlTemplate CreateGlassComboToggleTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "ToggleChrome");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M0,0 L4,4 L8,0"));
            arrow.SetValue(Shape.StrokeProperty, LiquidGlassTheme.IconBrush);
            arrow.SetValue(Shape.StrokeThicknessProperty, 1.15);
            arrow.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(FrameworkElement.WidthProperty, 8.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 4.0);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 13, 0));
            border.AppendChild(arrow);
            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                LiquidGlassTheme.CreatePopupHoverItemBrush(), "ToggleChrome"));
            template.Triggers.Add(hover);
            return template;
        }

        private static Style CreateGlassSliderStyle()
        {
            var railColor = ToXamlColor(Color.FromArgb(
                LiquidGlassTheme.IsDark ? (byte)74 : (byte)58,
                LiquidGlassTheme.HairlineColor.R,
                LiquidGlassTheme.HairlineColor.G,
                LiquidGlassTheme.HairlineColor.B));
            var thumbColor = ToXamlColor(Color.FromArgb(
                LiquidGlassTheme.IsDark ? (byte)196 : (byte)224,
                LiquidGlassTheme.AccentColor.R,
                LiquidGlassTheme.AccentColor.G,
                LiquidGlassTheme.AccentColor.B));
            var accentColor = ToXamlColor(Color.FromArgb(
                230,
                LiquidGlassTheme.AccentColor.R,
                LiquidGlassTheme.AccentColor.G,
                LiquidGlassTheme.AccentColor.B));
            var xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type Slider}\">" +
                "<Grid Height=\"24\">" +
                "<Border Height=\"3\" VerticalAlignment=\"Center\" CornerRadius=\"2\" Background=\"" + railColor + "\"/>" +
                "<Track x:Name=\"PART_Track\" Orientation=\"Horizontal\" IsDirectionReversed=\"False\" " +
                "Minimum=\"{TemplateBinding Minimum}\" Maximum=\"{TemplateBinding Maximum}\" " +
                "Value=\"{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\">" +
                "<Track.DecreaseRepeatButton><RepeatButton Command=\"{x:Static Slider.DecreaseLarge}\" Background=\"Transparent\">" +
                "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb Width=\"15\" Height=\"15\" Background=\"" + thumbColor + "\" BorderBrush=\"" + accentColor + "\" BorderThickness=\"0.9\">" +
                "<Thumb.Template><ControlTemplate TargetType=\"{x:Type Thumb}\"><Border Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"7.5\"/></ControlTemplate></Thumb.Template>" +
                "</Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command=\"{x:Static Slider.IncreaseLarge}\" Background=\"Transparent\">" +
                "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton></Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate>";
            var template = (ControlTemplate)XamlReader.Parse(xaml);
            var style = new Style(typeof(Slider));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 24.0));
            return style;
        }

        private static Style CreateGlassScrollBarStyle()
        {
            var railColor = ToXamlColor(Color.FromArgb(
                LiquidGlassTheme.IsDark ? (byte)28 : (byte)22,
                LiquidGlassTheme.HairlineColor.R,
                LiquidGlassTheme.HairlineColor.G,
                LiquidGlassTheme.HairlineColor.B));
            var thumbColor = ToXamlColor(Color.FromArgb(
                LiquidGlassTheme.IsDark ? (byte)132 : (byte)104,
                LiquidGlassTheme.HairlineColor.R,
                LiquidGlassTheme.HairlineColor.G,
                LiquidGlassTheme.HairlineColor.B));
            var xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ScrollBar}\">" +
                "<Grid><Border Background=\"" + railColor + "\" CornerRadius=\"4\"/>" +
                "<Track x:Name=\"PART_Track\" Orientation=\"Vertical\" IsDirectionReversed=\"True\" " +
                "Minimum=\"{TemplateBinding Minimum}\" Maximum=\"{TemplateBinding Maximum}\" ViewportSize=\"{TemplateBinding ViewportSize}\" " +
                "Value=\"{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}\">" +
                "<Track.DecreaseRepeatButton><RepeatButton Command=\"{x:Static ScrollBar.PageUpCommand}\" Background=\"Transparent\">" +
                "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb MinHeight=\"28\" Background=\"" + thumbColor + "\">" +
                "<Thumb.Template><ControlTemplate TargetType=\"{x:Type Thumb}\"><Border Background=\"{TemplateBinding Background}\" CornerRadius=\"4\"/></ControlTemplate></Thumb.Template>" +
                "</Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command=\"{x:Static ScrollBar.PageDownCommand}\" Background=\"Transparent\">" +
                "<RepeatButton.Template><ControlTemplate TargetType=\"{x:Type RepeatButton}\"><Border Background=\"Transparent\"/></ControlTemplate></RepeatButton.Template>" +
                "</RepeatButton></Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate>";
            var template = (ControlTemplate)XamlReader.Parse(xaml);
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 8.0));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4, 2, 1, 2)));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateGlassCheckBoxStyle()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var markHost = new FrameworkElementFactory(typeof(Border), "CheckChrome");
            markHost.SetValue(FrameworkElement.WidthProperty, 18.0);
            markHost.SetValue(FrameworkElement.HeightProperty, 18.0);
            markHost.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 9, 0));
            markHost.SetValue(Border.BackgroundProperty, LiquidGlassTheme.CreateCardBrush());
            markHost.SetValue(Border.BorderBrushProperty, LiquidGlassTheme.HairlineBrush);
            markHost.SetValue(Border.BorderThicknessProperty, new Thickness(0.75));
            markHost.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            var mark = new FrameworkElementFactory(typeof(TextBlock), "CheckMark");
            mark.SetValue(TextBlock.TextProperty, "✓");
            mark.SetValue(TextBlock.FontSizeProperty, 12.0);
            mark.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            mark.SetValue(TextBlock.ForegroundProperty, LiquidGlassTheme.PrimaryTextBrush);
            mark.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            markHost.AppendChild(mark);
            row.AppendChild(markHost);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty,
                new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            row.AppendChild(content);

            var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = row };
            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                LiquidGlassTheme.CreateButtonBrush(true), "CheckChrome"));
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty,
                Visibility.Visible, "CheckMark"));
            template.Triggers.Add(checkedTrigger);
            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static ControlTemplate CreateGlassTabControlTemplate()
        {
            var dock = new FrameworkElementFactory(typeof(DockPanel));
            var headers = new FrameworkElementFactory(typeof(TabPanel));
            headers.SetValue(Panel.IsItemsHostProperty, true);
            headers.SetValue(DockPanel.DockProperty, Dock.Top);
            headers.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
            dock.AppendChild(headers);
            var contentBorder = new FrameworkElementFactory(typeof(Border));
            contentBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            contentBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(
                LiquidGlassTheme.IsDark ? (byte)70 : (byte)54,
                LiquidGlassTheme.HairlineColor.R,
                LiquidGlassTheme.HairlineColor.G,
                LiquidGlassTheme.HairlineColor.B)));
            contentBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            contentBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));
            var content = new FrameworkElementFactory(typeof(ContentPresenter), "PART_SelectedContentHost");
            content.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(TabControl.SelectedContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty,
                new TemplateBindingExtension(TabControl.SelectedContentTemplateProperty));
            content.SetValue(ContentPresenter.ContentTemplateSelectorProperty,
                new TemplateBindingExtension(TabControl.SelectedContentTemplateSelectorProperty));
            content.SetValue(ContentPresenter.MarginProperty, new Thickness(3));
            contentBorder.AppendChild(content);
            dock.AppendChild(contentBorder);
            return new ControlTemplate(typeof(TabControl)) { VisualTree = dock };
        }

        private static string ToXamlColor(Color color)
        {
            return "#" + color.A.ToString("X2") + color.R.ToString("X2") +
                   color.G.ToString("X2") + color.B.ToString("X2");
        }

        private static ControlTemplate CreateTitleButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "TitleButtonChrome");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
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

        private static Style CreateGlassTabItemStyle()
        {
            var border = new FrameworkElementFactory(typeof(Border), "TabChrome");
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0.65));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            border.SetValue(Border.MarginProperty, new Thickness(1, 1, 3, 1));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(HeaderedContentControl.HeaderProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty,
                new TemplateBindingExtension(HeaderedContentControl.HeaderTemplateProperty));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);

            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, LiquidGlassTheme.PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, LiquidGlassTheme.HairlineBrush));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 6, 14, 6)));
            style.Setters.Add(new Setter(Control.TemplateProperty,
                new ControlTemplate(typeof(TabItem)) { VisualTree = border }));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, LiquidGlassTheme.CreateCardBrush()));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, LiquidGlassTheme.CreateButtonBrush(true)));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, LiquidGlassTheme.AccentBrush));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(selected);
            return style;
        }

        private static void AttachDirectTouchActivation(Button button)
        {
            TouchDevice activeDevice = null;
            var pressOrigin = new Point();
            var moved = false;
            var lastDirectActivationUtc = DateTime.MinValue;

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
                button.CaptureTouch(activeDevice);
                args.Handled = true;
            };
            button.PreviewTouchMove += (sender, args) =>
            {
                if (activeDevice != args.TouchDevice) return;
                var point = args.GetTouchPoint(button).Position;
                var deltaX = point.X - pressOrigin.X;
                var deltaY = point.Y - pressOrigin.Y;
                if (deltaX * deltaX + deltaY * deltaY > 144) moved = true;
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
                if (activate && button.IsEnabled)
                {
                    lastDirectActivationUtc = DateTime.UtcNow;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                }
            };
            button.PreviewMouseLeftButtonDown += (sender, args) =>
            {
                // 일부 전자칠판 드라이버는 처리한 TouchUp 뒤에 MouseDown/Click을 다시 승격한다.
                // 직접 실행 직후의 승격 입력만 막고 실제 마우스 클릭은 그대로 허용한다.
                var elapsed = (DateTime.UtcNow - lastDirectActivationUtc).TotalMilliseconds;
                if (args.StylusDevice != null && elapsed >= 0 && elapsed < 250)
                {
                    args.Handled = true;
                    DebugLog.WriteDiagnostic("SETTINGS-ONCE", "승격된 후속 클릭 차단 elapsedMs=" + elapsed.ToString("0"));
                }
            };
            button.LostTouchCapture += (sender, args) =>
            {
                if (activeDevice == args.TouchDevice) activeDevice = null;
            };
        }

        private void UpdateValueLabels()
        {
            opacityValue.Text = string.Format("{0:0}%", opacitySlider.Value);
            zoomValue.Text = string.Format("{0:0}%", zoomSlider.Value);
        }

        private void SaveAndClose()
        {
            var choice = screenComboBox.SelectedItem as ScreenChoice;
            if (choice?.Screen != null)
            {
                settings.TargetDeviceName = choice.Screen.DeviceName;
            }
            settings.ThemeMode = UserSettings.ResolveSystemThemeMode();
            settings.PastelTheme = selectedPastelTheme;
            settings.UseCustomGlassLightColor = useCustomGlassLightColor;
            settings.CustomGlassLightColorArgb = ToArgb(selectedCustomGlassLightColor);
            settings.GlassTintOpacity = LiquidGlassTheme.MaximumPanelGlassStrength *
                opacitySlider.Value / 100.0;
            settings.DefaultZoom = zoomSlider.Value / 100.0;
            settings.TooltipsEnabled = tooltipsCheckBox.IsChecked == true;

            var startupEnabled = startupCheckBox.IsChecked == true;
            if (!settings.SetStartWithWindows(startupEnabled))
            {
                MessageBox.Show(
                    "Windows 시작 프로그램 설정을 변경하지 못했습니다.",
                    "TouchZoomBoard3",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            settings.Save();
            Applied?.Invoke();
            Close();
        }
    }
}
