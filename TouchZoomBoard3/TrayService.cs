using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TouchZoomBoard
{
    internal sealed class TrayService : IDisposable
    {
        private sealed class GlassTrayColorTable : Forms.ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => ToDrawingColor(LiquidGlassTheme.TrayBackgroundColor);
            public override Color ImageMarginGradientBegin => Lighten(ToolStripDropDownBackground, 0.16);
            public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
            public override Color ImageMarginGradientEnd => Darken(ToolStripDropDownBackground, 0.13);
            public override Color MenuItemSelected => ToDrawingColor(LiquidGlassTheme.TrayHoverColor);
            public override Color MenuItemBorder => ToDrawingColor(LiquidGlassTheme.HairlineColor, 205);
            public override Color MenuItemPressedGradientBegin => Lighten(ToDrawingColor(LiquidGlassTheme.TrayPressedColor), 0.10);
            public override Color MenuItemPressedGradientMiddle => ToDrawingColor(LiquidGlassTheme.TrayPressedColor);
            public override Color MenuItemPressedGradientEnd => Darken(ToDrawingColor(LiquidGlassTheme.TrayPressedColor), 0.10);
            public override Color SeparatorDark => ToDrawingColor(LiquidGlassTheme.HairlineColor, 105);
            public override Color SeparatorLight => ToDrawingColor(LiquidGlassTheme.HairlineColor, 52);
            public override Color ToolStripBorder => ToDrawingColor(LiquidGlassTheme.HairlineColor, 205);
        }

        private sealed class GlassTrayRenderer : Forms.ToolStripProfessionalRenderer
        {
            internal GlassTrayRenderer() : base(new GlassTrayColorTable())
            {
                RoundedEdges = true;
            }

            protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
            {
                var bounds = e.ToolStrip.ClientRectangle;
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                var background = ToDrawingColor(LiquidGlassTheme.TrayBackgroundColor);
                using (var brush = new LinearGradientBrush(
                    bounds,
                    Lighten(background, LiquidGlassTheme.IsDark ? 0.14 : 0.08),
                    Darken(background, LiquidGlassTheme.IsDark ? 0.14 : 0.06),
                    LinearGradientMode.ForwardDiagonal))
                {
                    e.Graphics.FillRectangle(brush, bounds);
                }

                using (var highlight = new LinearGradientBrush(
                    new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Max(1, bounds.Height / 3)),
                    Color.FromArgb(LiquidGlassTheme.IsDark ? 96 : 148, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(highlight,
                        new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Max(1, bounds.Height / 3)));
                }
            }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected && !e.Item.Pressed) return;
                var bounds = new Rectangle(4, 2, Math.Max(1, e.Item.Width - 8), Math.Max(1, e.Item.Height - 4));
                var selected = ToDrawingColor(e.Item.Pressed
                    ? LiquidGlassTheme.TrayPressedColor
                    : LiquidGlassTheme.TrayHoverColor);
                using (var path = CreateRoundedRectangle(bounds, 6))
                using (var brush = new LinearGradientBrush(
                    bounds,
                    Lighten(selected, 0.14),
                    Darken(selected, 0.10),
                    LinearGradientMode.Vertical))
                using (var pen = new Pen(ToDrawingColor(LiquidGlassTheme.HairlineColor, 190)))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
            }

            protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
            {
                var y = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2;
                using (var pen = new Pen(ToDrawingColor(LiquidGlassTheme.HairlineColor, 104)))
                    e.Graphics.DrawLine(pen, 9, y, Math.Max(9, e.Item.Width - 9), y);
            }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                // 시스템 메뉴색이나 Acrylic 합성색을 상속하지 않고 항상 짙은 글자를 그린다.
                var textColor = e.Item.Enabled
                    ? Color.FromArgb(255, 18, 25, 33)
                    : Color.FromArgb(255, 96, 108, 120);
                var left = Math.Max(12, e.Item.ContentRectangle.Left);
                var textBounds = new Rectangle(
                    left,
                    0,
                    Math.Max(1, e.Item.Width - left - 14),
                    e.Item.Height);
                Forms.TextRenderer.DrawText(
                    e.Graphics,
                    e.Text,
                    e.TextFont,
                    textBounds,
                    textColor,
                    Forms.TextFormatFlags.Left |
                    Forms.TextFormatFlags.VerticalCenter |
                    Forms.TextFormatFlags.SingleLine |
                    Forms.TextFormatFlags.NoPrefix |
                    Forms.TextFormatFlags.EndEllipsis |
                    Forms.TextFormatFlags.PreserveGraphicsClipping);
            }

            protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
            {
                var bounds = e.ToolStrip.ClientRectangle;
                if (bounds.Width <= 1 || bounds.Height <= 1) return;
                using (var pen = new Pen(ToDrawingColor(LiquidGlassTheme.HairlineColor, 205)))
                    e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            }

            private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
            {
                var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
                var path = new GraphicsPath();
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        private readonly Forms.NotifyIcon notifyIcon;
        private readonly Forms.ContextMenuStrip trayMenu;
        private readonly Forms.ToolStripMenuItem panelItem;
        private readonly Forms.ToolStripMenuItem emergencyRecoveryItem;
        private Icon ownedIcon;

        internal event Action TogglePanelRequested;
        internal event Action EndSessionRequested;
        internal event Action EmergencyRecoveryRequested;
        internal event Action SettingsRequested;
        internal event Action AboutRequested;
        internal event Action QuitRequested;

        internal TrayService(bool panelVisible)
        {
            panelItem = new Forms.ToolStripMenuItem();
            panelItem.Click += (sender, args) => TogglePanelRequested?.Invoke();

            var endSessionItem = new Forms.ToolStripMenuItem("확대·필기 종료");
            endSessionItem.Click += (sender, args) => EndSessionRequested?.Invoke();

            emergencyRecoveryItem = new Forms.ToolStripMenuItem("긴급 화면 복구 (Ctrl+Alt+Shift+Esc)");
            emergencyRecoveryItem.Click += (sender, args) => EmergencyRecoveryRequested?.Invoke();

            var settingsItem = new Forms.ToolStripMenuItem("환경 설정...");
            settingsItem.Click += (sender, args) => SettingsRequested?.Invoke();

            var aboutItem = new Forms.ToolStripMenuItem("정보...");
            aboutItem.Click += (sender, args) => AboutRequested?.Invoke();

            var quitItem = new Forms.ToolStripMenuItem("종료");
            quitItem.Click += (sender, args) => QuitRequested?.Invoke();

            trayMenu = new Forms.ContextMenuStrip
            {
                BackColor = ToDrawingColor(LiquidGlassTheme.TrayBackgroundColor),
                ForeColor = ToDrawingColor(LiquidGlassTheme.PrimaryTextColor),
                Font = new System.Drawing.Font(
                    "Segoe UI",
                    9.5f,
                    System.Drawing.FontStyle.Regular,
                    System.Drawing.GraphicsUnit.Point),
                Padding = new Forms.Padding(5),
                // WinForms 메뉴는 per-pixel 유리가 아니므로 아주 약한 창 투명도로 뒤 배경을 비친다.
                Opacity = 0.93,
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Renderer = new GlassTrayRenderer()
            };
            trayMenu.Items.Add(panelItem);
            trayMenu.Items.Add(endSessionItem);
            trayMenu.Items.Add(emergencyRecoveryItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add(settingsItem);
            trayMenu.Items.Add(aboutItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add(quitItem);
            foreach (Forms.ToolStripItem item in trayMenu.Items)
            {
                if (item is Forms.ToolStripMenuItem)
                {
                    item.ForeColor = Color.FromArgb(255, 18, 25, 33);
                    item.Padding = new Forms.Padding(12, 6, 18, 6);
                    item.TextAlign = ContentAlignment.MiddleLeft;
                }
            }
            trayMenu.Opened += (sender, args) =>
            {
                NativeMethods.TryApplyAntialiasedRoundedCorners(trayMenu.Handle);
                // Layered WinForms 메뉴에 WCA Acrylic을 겹치면 일부 환경에서 글자가
                // 흰색으로 합성된다. 흰 메뉴 자체의 Opacity만 사용해 가독성을 고정한다.
                NativeMethods.ApplyRoundedWindowRegionFromCurrentBounds(trayMenu.Handle, 14);
                DebugLog.WriteDiagnostic("GLASS",
                    "surface=tray-menu, design=ReadableFrostedWhite, acrylic=False" +
                    ", opacity=0.93, text=#121921");
            };

            notifyIcon = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "TouchZoomBoard3 3.0.2",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            notifyIcon.DoubleClick += (sender, args) => TogglePanelRequested?.Invoke();

            SetPanelVisible(panelVisible);
        }

        internal void ApplyTheme()
        {
            trayMenu.BackColor = ToDrawingColor(LiquidGlassTheme.TrayBackgroundColor);
            trayMenu.ForeColor = ToDrawingColor(LiquidGlassTheme.PrimaryTextColor);
            trayMenu.Opacity = 0.93;
            foreach (Forms.ToolStripItem item in trayMenu.Items)
                item.ForeColor = Color.FromArgb(255, 18, 25, 33);
            trayMenu.Renderer = new GlassTrayRenderer();
            trayMenu.Invalidate(true);
            DebugLog.WriteDiagnostic("THEME", "trayApplied design=ReadableFrostedWhite" +
                ", mode=fixed-light" +
                ", glassLight=" + (LiquidGlassTheme.UseCustomGlassLightColor
                    ? "custom"
                    : LiquidGlassTheme.Pastel.ToString()) +
                ", panelGlassStrength=" + LiquidGlassTheme.GlassTintOpacity.ToString("0.000") +
                ", trayOpacity=0.93, text=#121921");
        }

        internal void SetPanelVisible(bool visible)
        {
            panelItem.Text = visible ? "컨트롤 패널 끄기" : "컨트롤 패널 켜기";
        }

        private static Color ToDrawingColor(System.Windows.Media.Color color, int alpha = 255)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
        }

        private static Color Lighten(Color color, double amount)
        {
            var normalized = Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromArgb(
                color.A,
                (int)Math.Round(color.R + (255 - color.R) * normalized),
                (int)Math.Round(color.G + (255 - color.G) * normalized),
                (int)Math.Round(color.B + (255 - color.B) * normalized));
        }

        private static Color Darken(Color color, double amount)
        {
            var factor = 1.0 - Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromArgb(
                color.A,
                (int)Math.Round(color.R * factor),
                (int)Math.Round(color.G * factor),
                (int)Math.Round(color.B * factor));
        }

        internal void SetEmergencyHotKeyAvailable(bool available)
        {
            emergencyRecoveryItem.Text = available
                ? "긴급 화면 복구 (Ctrl+Alt+Shift+Esc)"
                : "긴급 화면 복구";
        }

        internal void ShowRecoveryMessage(bool hadError)
        {
            notifyIcon.BalloonTipTitle = "TouchZoomBoard3";
            notifyIcon.BalloonTipText = hadError
                ? "정상 화면 복구를 시도했지만 일부 단계가 완료되지 않았습니다. 프로그램을 다시 실행해 주세요."
                : "확대와 입력 상태를 초기화하고 정상 화면으로 돌아왔습니다.";
            notifyIcon.BalloonTipIcon = hadError ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
            notifyIcon.ShowBalloonTip(2500);
        }

        internal void ShowStartupMessage()
        {
            notifyIcon.BalloonTipTitle = "TouchZoomBoard3";
            notifyIcon.BalloonTipText = "트레이에서 컨트롤 패널을 켜거나 끌 수 있습니다.";
            notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            notifyIcon.ShowBalloonTip(2500);
        }

        internal void ShowTouchRefreshPanelMessage()
        {
            ShowTouchRefreshMessage(
                "전자칠판 터치 입력을 갱신했습니다. 패널을 한 번 더 터치해 확인하세요.",
                Forms.ToolTipIcon.Info);
        }

        internal void ShowTouchRefreshRestartMessage()
        {
            ShowTouchRefreshMessage(
                "전자칠판 터치 장치를 다시 등록하기 위해 프로그램을 한 번 재시작합니다.",
                Forms.ToolTipIcon.Info);
        }

        internal void ShowTouchRefreshRestartedMessage(bool nativeTouchAvailable)
        {
            ShowTouchRefreshMessage(
                nativeTouchAvailable
                    ? "전자칠판 터치 장치 갱신 후 다시 시작했습니다. 패널 터치를 확인하세요."
                    : "입력 갱신 후 다시 시작했지만 Windows의 터치 장치 정보가 아직 보이지 않습니다.",
                nativeTouchAvailable ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
        }

        internal void ShowTouchRefreshFailedMessage()
        {
            ShowTouchRefreshMessage(
                "자동 갱신 후에도 WPF 터치 입력이 확인되지 않았습니다. USB Touch 연결과 Windows 터치 설정을 확인해 주세요.",
                Forms.ToolTipIcon.Warning);
        }

        private void ShowTouchRefreshMessage(string message, Forms.ToolTipIcon icon)
        {
            notifyIcon.BalloonTipTitle = "TouchZoomBoard3 입력 장치 갱신";
            notifyIcon.BalloonTipText = message;
            notifyIcon.BalloonTipIcon = icon;
            notifyIcon.ShowBalloonTip(3500);
        }

        private Icon LoadIcon()
        {
            try
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/TouchZoomBoard3.ico"));
                if (resource?.Stream != null)
                {
                    using (var memory = new MemoryStream())
                    {
                        resource.Stream.CopyTo(memory);
                        memory.Position = 0;
                        using (var loadedIcon = new Icon(memory))
                        {
                            ownedIcon = (Icon)loadedIcon.Clone();
                        }
                        return ownedIcon;
                    }
                }
            }
            catch
            {
            }

            return SystemIcons.Application;
        }

        public void Dispose()
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            trayMenu.Dispose();
            ownedIcon?.Dispose();
        }
    }
}
