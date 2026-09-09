using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TouchZoomBoard
{
    internal enum InterfaceThemeMode
    {
        Dark,
        Light
    }

    // 유리 가장자리와 버튼 굴절광에만 적용하는 저채도 파스텔 프리셋이다.
    internal enum PastelThemeColor
    {
        Neutral,
        Sky,
        Mint,
        Lavender,
        Rose,
        Peach
    }

    internal static class LiquidGlassTheme
    {
        internal const double MaximumPanelGlassStrength = 0.20;
        private const byte MaximumPanelSurfaceAlpha = 66;
        private const byte MaximumPanelHeaderAlpha = 76;
        private static double glassTintOpacity;
        private static PastelThemeColor pastel = PastelThemeColor.Neutral;
        private static bool useCustomGlassLightColor;
        private static Color customGlassLightColor = Color.FromRgb(216, 232, 247);

        internal static InterfaceThemeMode Mode => InterfaceThemeMode.Light;
        internal static PastelThemeColor Pastel => pastel;
        internal static bool UseCustomGlassLightColor => useCustomGlassLightColor;
        internal static Color GlassLightColor => useCustomGlassLightColor
            ? customGlassLightColor
            : GetPastelPreview(pastel);
        internal static bool IsDark => false;
        internal static double GlassTintOpacity => glassTintOpacity;
        internal static byte PanelSurfaceAlpha => ScalePanelAlpha(MaximumPanelSurfaceAlpha);
        internal static byte PanelHeaderAlpha => ScalePanelAlpha(MaximumPanelHeaderAlpha);

        // 시스템 테마와 무관한 고정 밝은 투명 유리 팔레트.
        internal static Color TintColor => Colors.White;
        internal static Color FallbackWindowColor => Color.FromRgb(248, 250, 252);
        internal static Color PrimaryTextColor => Color.FromRgb(22, 29, 37);
        internal static Color SecondaryTextColor => Color.FromRgb(58, 69, 80);
        internal static Color MutedTextColor => Color.FromRgb(96, 108, 120);
        internal static Color IconColor => Color.FromRgb(24, 34, 45);
        internal static Color LinkColor => Color.FromRgb(16, 74, 112);
        internal static Color HairlineColor => Color.FromRgb(111, 122, 134);
        internal static Color AccentColor => Color.FromRgb(36, 46, 57);
        internal static Color TrayBackgroundColor => Color.FromRgb(246, 248, 250);
        internal static Color TrayHoverColor => Color.FromRgb(232, 236, 240);
        internal static Color TrayPressedColor => Color.FromRgb(219, 224, 229);

        internal static Brush PrimaryTextBrush => CreateFrozenBrush(PrimaryTextColor);
        internal static Brush SecondaryTextBrush => CreateFrozenBrush(SecondaryTextColor);
        internal static Brush MutedTextBrush => CreateFrozenBrush(MutedTextColor);
        internal static Brush IconBrush => CreateFrozenBrush(IconColor);
        internal static Brush LinkBrush => CreateFrozenBrush(LinkColor);
        internal static Brush HairlineBrush => CreateRimBrush(false);
        internal static Brush AccentBrush => CreateRimBrush(true);

        internal static void Configure(
            InterfaceThemeMode selectedMode,
            PastelThemeColor selectedPastel,
            double tintOpacity,
            bool useCustomColor,
            int customColorArgb)
        {
            glassTintOpacity = Math.Max(0.0, Math.Min(MaximumPanelGlassStrength, tintOpacity));
            pastel = Enum.IsDefined(typeof(PastelThemeColor), selectedPastel)
                ? selectedPastel
                : PastelThemeColor.Neutral;
            useCustomGlassLightColor = useCustomColor;
            customGlassLightColor = Color.FromRgb(
                (byte)((customColorArgb >> 16) & 0xFF),
                (byte)((customColorArgb >> 8) & 0xFF),
                (byte)(customColorArgb & 0xFF));
            DebugLog.WriteDiagnostic(
                "THEME",
                "design=MinimalClearLiquidGlass" +
                ", mode=fixed-light" +
                ", glassLight=" + (useCustomGlassLightColor
                    ? "custom-#" + customGlassLightColor.R.ToString("X2") +
                        customGlassLightColor.G.ToString("X2") +
                        customGlassLightColor.B.ToString("X2")
                    : pastel.ToString()) +
                ", panelGlassStrength=" + glassTintOpacity.ToString("0.000") +
                ", panelSurfaceAlpha=" + PanelSurfaceAlpha +
                ", panelHeaderAlpha=" + PanelHeaderAlpha +
                ", rootUiOpacity=1.00");
        }

        internal static Brush CreateWindowVeilBrush()
        {
            // 환경설정·정보창은 흰 스마트 글래스: 읽을 수 있지만 뒤 화면이 조금 비친다.
            return CreateGradient(
                new Point(0, 0),
                new Point(1, 1),
                new[]
                {
                    Stop(218, Colors.White, 0.00),
                    Stop(184, Color.FromRgb(252, 253, 255), 0.44),
                    Stop(166, Color.FromRgb(241, 245, 249), 1.00)
                });
        }

        internal static Brush CreatePanelGlassBrush()
        {
            if (glassTintOpacity <= 0.0) return Brushes.Transparent;
            // 넓은 면은 선택한 굴절광을 아주 옅게 섞은 단색 저알파로 합성한다.
            // 넓은 그라데이션은 사용하지 않아 모니터 감마·색심도 차이의 밴딩을 피한다.
            var surface = Mix(GlassLightColor, Colors.White, 0.62);
            return new SolidColorBrush(Color.FromArgb(
                PanelSurfaceAlpha, surface.R, surface.G, surface.B));
        }

        internal static Brush CreatePanelHeaderBrush()
        {
            if (glassTintOpacity <= 0.0) return Brushes.Transparent;
            var surface = Mix(GlassLightColor, Colors.White, 0.54);
            return new SolidColorBrush(Color.FromArgb(
                PanelHeaderAlpha, surface.R, surface.G, surface.B));
        }

        internal static Brush CreatePanelRefractionHighlightBrush()
        {
            var light = Mix(GlassLightColor, Colors.White, 0.12);
            var middle = GlassLightColor;
            var shade = Mix(GlassLightColor, Color.FromRgb(62, 76, 91), 0.24);
            return CreateGradient(
                new Point(0, 0),
                new Point(1, 1),
                new[]
                {
                    PanelStop(112, light, 0.00),
                    PanelStop(84, middle, 0.30),
                    PanelStop(38, middle, 0.66),
                    PanelStop(68, shade, 1.00)
                });
        }

        internal static Brush CreatePanelRefractionShadeBrush()
        {
            var color = Mix(GlassLightColor, Color.FromRgb(43, 56, 70), 0.42);
            return new SolidColorBrush(Color.FromArgb(
                ScalePanelAlpha(54), color.R, color.G, color.B));
        }

        internal static Brush CreatePanelInnerLightBrush()
        {
            var color = Mix(GlassLightColor, Colors.White, 0.10);
            return new SolidColorBrush(Color.FromArgb(
                ScalePanelAlpha(72), color.R, color.G, color.B));
        }

        internal static Brush CreatePanelTopGlintBrush()
        {
            var color = Mix(GlassLightColor, Colors.White, 0.08);
            return CreateGradient(
                new Point(0, 0.5),
                new Point(1, 0.5),
                new[]
                {
                    PanelStop(0, color, 0.00),
                    PanelStop(76, color, 0.20),
                    PanelStop(42, color, 0.58),
                    PanelStop(0, color, 1.00)
                });
        }

        internal static Brush CreatePanelSideGlintBrush()
        {
            var color = Mix(GlassLightColor, Colors.White, 0.06);
            return CreateGradient(
                new Point(0.5, 0),
                new Point(0.5, 1),
                new[]
                {
                    PanelStop(0, color, 0.00),
                    PanelStop(64, color, 0.22),
                    PanelStop(34, color, 0.62),
                    PanelStop(0, color, 1.00)
                });
        }

        internal static Brush CreatePanelGroupBorderBrush()
        {
            // 0%에서는 버튼 사이의 그룹 외곽선도 사라져 실제 조작 요소만 남는다.
            var strength = glassTintOpacity / MaximumPanelGlassStrength;
            var alpha = (byte)Math.Max(0, Math.Min(40, Math.Round(40 * strength)));
            var color = Mix(GlassLightColor, Color.FromRgb(67, 80, 94), 0.28);
            return new SolidColorBrush(Color.FromArgb(
                alpha, color.R, color.G, color.B));
        }

        internal static Brush CreateHeaderBrush()
        {
            return CreateGradient(
                new Point(0, 0),
                new Point(0, 1),
                new[]
                {
                    SurfaceStop(136, Colors.White, 0.00),
                    SurfaceStop(54, Colors.White, 0.55),
                    SurfaceStop(28, Color.FromRgb(229, 236, 243), 1.00)
                });
        }

        internal static Brush CreateCardBrush()
        {
            return CreateGradient(
                new Point(0, 0),
                new Point(0, 1),
                new[]
                {
                    SurfaceStop(82, Colors.White, 0.00),
                    SurfaceStop(36, Colors.White, 0.58),
                    SurfaceStop(44, Color.FromRgb(236, 241, 246), 1.00)
                });
        }

        internal static Brush CreateButtonBrush(bool accent)
        {
            var top = accent ? (byte)238 : (byte)192;
            var middle = accent ? (byte)126 : (byte)92;
            var bottom = accent ? (byte)96 : (byte)64;
            var middleColor = Mix(GlassLightColor, Colors.White, accent ? 0.34 : 0.48);
            var bottomColor = Mix(GlassLightColor, Color.FromRgb(151, 169, 187),
                accent ? 0.28 : 0.40);
            return CreateGradient(
                new Point(0, 0),
                new Point(0, 1),
                new[]
                {
                    ButtonStop(top, Colors.White, 0.00),
                    ButtonStop(middle, middleColor, 0.52),
                    ButtonStop(bottom, bottomColor, 1.00)
                });
        }

        internal static Brush CreateButtonRefractionBrush(bool active)
        {
            var light = Mix(GlassLightColor, Colors.White, active ? 0.10 : 0.18);
            var shade = Mix(GlassLightColor, Color.FromRgb(58, 72, 87), 0.38);
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(
                active ? (byte)232 : (byte)206, light.R, light.G, light.B), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(
                active ? (byte)142 : (byte)104, GlassLightColor.R,
                GlassLightColor.G, GlassLightColor.B), 0.52));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(
                active ? (byte)174 : (byte)142, shade.R, shade.G, shade.B), 1.00));
            return brush;
        }

        internal static Brush CreateButtonContactShadowBrush()
        {
            var shade = Mix(GlassLightColor, Color.FromRgb(31, 42, 54), 0.76);
            return new SolidColorBrush(Color.FromArgb(54, shade.R, shade.G, shade.B));
        }

        internal static Brush CreatePopupGlassBrush()
        {
            // 호출 메뉴는 배경이 충분히 비치는 약한 백색 간유리다.
            return CreateGradient(
                new Point(0, 0),
                new Point(1, 1),
                new[]
                {
                    Stop(112, Colors.White, 0.00),
                    Stop(86, Color.FromRgb(249, 251, 253), 0.46),
                    Stop(68, Color.FromRgb(232, 238, 244), 1.00)
                });
        }

        internal static Brush CreatePopupBorderBrush()
        {
            return CreateRimBrush(false);
        }

        internal static Brush CreatePopupHighlightBrush()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(112, 255, 255, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(36, 255, 255, 255), 0.48));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.00));
            return brush;
        }

        internal static Brush CreatePopupItemBrush()
        {
            return CreateItemGradient(12, 4, 8);
        }

        internal static Brush CreatePopupHoverItemBrush()
        {
            return CreateItemGradient(72, 36, 44);
        }

        internal static Brush CreatePopupPressedItemBrush()
        {
            return CreateItemGradient(112, 62, 74);
        }

        internal static Brush CreatePopupActiveItemBrush()
        {
            // 현재 선택 항목만 흰 유리 캡슐로 반전한다.
            return CreateItemGradient(242, 220, 198);
        }

        internal static Brush CreatePopupSeparatorBrush()
        {
            var color = Color.FromArgb(64, HairlineColor.R, HairlineColor.G, HairlineColor.B);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.00));
            brush.GradientStops.Add(new GradientStop(color, 0.50));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.00));
            return brush;
        }

        internal static Color GetPastelPreview(PastelThemeColor value)
        {
            switch (value)
            {
                case PastelThemeColor.Sky:
                    return Color.FromRgb(126, 193, 238);
                case PastelThemeColor.Mint:
                    return Color.FromRgb(127, 210, 177);
                case PastelThemeColor.Lavender:
                    return Color.FromRgb(181, 158, 232);
                case PastelThemeColor.Rose:
                    return Color.FromRgb(234, 157, 183);
                case PastelThemeColor.Peach:
                    return Color.FromRgb(239, 177, 139);
                default:
                    return Color.FromRgb(181, 198, 214);
            }
        }

        internal static bool ApplyWindow(Window window, HwndSource source, IntPtr hwnd, string surface)
        {
            if (window == null || source == null || hwnd == IntPtr.Zero)
            {
                WriteResult(surface, hwnd, false, -1, "missing-window-source", 0);
                return false;
            }

            var tintAlpha = ResolveWindowTintAlpha(surface);
            var clearPanel = string.Equals(
                surface, "control-panel", StringComparison.OrdinalIgnoreCase);
            try
            {
                window.Background = Brushes.Transparent;
                if (source.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;

                // AllowsTransparency 패널은 DWM Accent를 적용하지 않는다. D-Day 위젯과
                // 같은 픽셀 단위 알파 창이므로 투명 픽셀이 실제 바탕화면까지 통과한다.
                int result;
                string detail;
                bool accepted;
                if (clearPanel && window.AllowsTransparency)
                {
                    result = 0;
                    detail = "perPixelLayered=True, blur=False, tintAlpha=0";
                    accepted = true;
                }
                else
                {
                    accepted = clearPanel
                        ? NativeMethods.TryApplyClearTransparent(hwnd, out result, out detail)
                        : NativeMethods.TryApplyLightAcrylic(hwnd, tintAlpha, out result, out detail);
                }
                // 투명 패널 실패 시 Acrylic으로 되돌리면 tintAlpha=0이어도 배경이
                // 뿌옇게 흐려진다. 패널은 투명 합성을 유지하고 다른 창만 불투명
                // 안전 배경으로 대체한다.
                if (!accepted && !clearPanel)
                {
                    window.Background = new SolidColorBrush(FallbackWindowColor);
                    if (source.CompositionTarget != null)
                        source.CompositionTarget.BackgroundColor = FallbackWindowColor;
                }
                WriteResult(surface, hwnd, accepted, result, detail, tintAlpha);
                return accepted;
            }
            catch (Exception exception)
            {
                window.Background = clearPanel
                    ? Brushes.Transparent
                    : new SolidColorBrush(FallbackWindowColor);
                try
                {
                    if (source.CompositionTarget != null)
                        source.CompositionTarget.BackgroundColor = clearPanel
                            ? Colors.Transparent
                            : FallbackWindowColor;
                }
                catch
                {
                }
                DebugLog.WriteDiagnostic("GLASS", "surface=" + surface +
                    ", hwnd=0x" + hwnd.ToInt64().ToString("X") +
                    ", accepted=False, exception=" + exception.GetType().Name);
                return false;
            }
        }

        internal static bool ApplyPopup(HwndSource source, string surface)
        {
            if (source == null || source.Handle == IntPtr.Zero)
            {
                WriteResult(surface, IntPtr.Zero, false, -1, "missing-popup-source", 0);
                return false;
            }

            const byte tintAlpha = 72;
            try
            {
                if (source.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var accepted = NativeMethods.TryApplyLightAcrylic(
                    source.Handle, tintAlpha, out var result, out var detail);
                if (!accepted && source.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = FallbackWindowColor;
                WriteResult(surface, source.Handle, accepted, result, detail, tintAlpha);
                return accepted;
            }
            catch (Exception exception)
            {
                try
                {
                    if (source.CompositionTarget != null)
                        source.CompositionTarget.BackgroundColor = FallbackWindowColor;
                }
                catch
                {
                }
                DebugLog.WriteDiagnostic("GLASS", "surface=" + surface +
                    ", hwnd=0x" + source.Handle.ToInt64().ToString("X") +
                    ", accepted=False, exception=" + exception.GetType().Name);
                return false;
            }
        }

        internal static bool ApplyRawWindow(IntPtr hwnd, string surface)
        {
            const byte tintAlpha = 176;
            var accepted = NativeMethods.TryApplyLightAcrylic(
                hwnd, tintAlpha, out var result, out var detail);
            WriteResult(surface, hwnd, accepted, result, detail, tintAlpha);
            return accepted;
        }

        private static byte ResolveWindowTintAlpha(string surface)
        {
            if (string.Equals(surface, "control-panel", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (!string.IsNullOrEmpty(surface) && surface.StartsWith("minimap", StringComparison.OrdinalIgnoreCase)) return 34;
            if (string.Equals(surface, "settings-window", StringComparison.OrdinalIgnoreCase)) return 118;
            if (string.Equals(surface, "about-window", StringComparison.OrdinalIgnoreCase)) return 118;
            return 36;
        }

        private static Brush CreateItemGradient(byte topAlpha, byte middleAlpha, byte bottomAlpha)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(topAlpha, 255, 255, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(middleAlpha, 255, 255, 255), 0.50));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(bottomAlpha, 225, 232, 239), 1.00));
            return brush;
        }

        private static Color Mix(Color first, Color second, double secondRatio)
        {
            var ratio = Math.Max(0.0, Math.Min(1.0, secondRatio));
            return Color.FromRgb(
                (byte)Math.Round(first.R * (1.0 - ratio) + second.R * ratio),
                (byte)Math.Round(first.G * (1.0 - ratio) + second.G * ratio),
                (byte)Math.Round(first.B * (1.0 - ratio) + second.B * ratio));
        }

        private static Brush CreateRimBrush(bool strong)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.00));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(strong ? (byte)220 : (byte)174, 244, 248, 252), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(strong ? (byte)154 : (byte)104, 93, 105, 118), 1.00));
            return brush;
        }

        private static Brush CreateGradient(Point start, Point end, GradientStop[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            foreach (var stop in stops) brush.GradientStops.Add(stop);
            return brush;
        }

        private static GradientStop Stop(byte alpha, Color color, double offset)
        {
            return new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), offset);
        }

        private static GradientStop SurfaceStop(byte alpha, Color color, double offset)
        {
            return Stop(ScaleGlassAlpha(alpha), color, offset);
        }

        private static GradientStop PanelStop(byte alpha, Color color, double offset)
        {
            return Stop(ScalePanelAlpha(alpha), color, offset);
        }

        private static byte ScalePanelAlpha(byte alpha)
        {
            var normalized = MaximumPanelGlassStrength <= 0.0
                ? 0.0
                : glassTintOpacity / MaximumPanelGlassStrength;
            return (byte)Math.Max(0, Math.Min(255, Math.Round(alpha * normalized)));
        }

        internal static byte ScaleGlassAlpha(byte alpha)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(alpha * glassTintOpacity)));
        }

        private static GradientStop ButtonStop(byte alpha, Color color, double offset)
        {
            // 패널 유리판 농도와 버튼 재질을 분리한다. 유리판 농도를 0%로 만들어도
            // 조작 버튼은 같은 대비와 터치 표식을 유지한다.
            const double buttonStrength = 0.24;
            var scaled = (byte)Math.Max(0, Math.Min(255, Math.Round(alpha * buttonStrength)));
            return Stop(scaled, color, offset);
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static void WriteResult(
            string surface,
            IntPtr hwnd,
            bool accepted,
            int result,
            string detail,
            byte tintAlpha)
        {
            DebugLog.WriteDiagnostic(
                "GLASS",
                "surface=" + (surface ?? "unknown") +
                ", hwnd=0x" + hwnd.ToInt64().ToString("X") +
                ", requested=" + (string.Equals(surface, "control-panel", StringComparison.OrdinalIgnoreCase)
                    ? detail != null && detail.StartsWith("perPixelLayered", StringComparison.Ordinal)
                        ? "PerPixelLayeredWindow"
                        : "ClearTransparent"
                    : "LightAcrylic") +
                ", accepted=" + accepted +
                ", design=MinimalClearLiquidGlass" +
                ", mode=fixed-light" +
                ", glassLight=" + (useCustomGlassLightColor ? "custom" : pastel.ToString()) +
                ", tintAlpha=" + tintAlpha +
                ", panelGlassStrength=" + glassTintOpacity.ToString("0.000") +
                ", panelGlassPercent=" +
                    Math.Round(glassTintOpacity / MaximumPanelGlassStrength * 100.0).ToString("0") +
                ", panelSurfaceAlpha=" + PanelSurfaceAlpha +
                ", panelHeaderAlpha=" + PanelHeaderAlpha +
                ", rootUiOpacity=1.00" +
                ", osBuild=" + Environment.OSVersion.Version.Build +
                ", fallbackUsed=" + (!accepted &&
                    !string.Equals(surface, "control-panel", StringComparison.OrdinalIgnoreCase)) +
                ", corner=" + (string.Equals(surface, "control-panel", StringComparison.OrdinalIgnoreCase)
                    ? detail != null && detail.StartsWith("perPixelLayered", StringComparison.Ordinal)
                        ? "wpf-per-pixel"
                        : "dwm-antialiased-with-gdi-fallback"
                    : "dwm-plus-region") +
                ", windowBackground=" + (accepted
                    ? string.Equals(surface, "control-panel", StringComparison.OrdinalIgnoreCase) &&
                        detail != null && detail.StartsWith("perPixelLayered", StringComparison.Ordinal)
                            ? "per-pixel-transparent"
                            : "transparent-composition"
                    : string.Equals(surface, "control-panel", StringComparison.OrdinalIgnoreCase)
                        ? "transparent-no-acrylic-fallback"
                        : "opaque-fallback") +
                ", result=0x" + unchecked((uint)result).ToString("X8") +
                ", detail=" + (detail ?? string.Empty));
        }
    }
}
