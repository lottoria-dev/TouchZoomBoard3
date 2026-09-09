using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Win32;

namespace TouchZoomBoard
{
    internal sealed class UserSettings
    {
        private const string SettingsPath = @"Software\lottoria-dev\TouchZoomBoard3";
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "TouchZoomBoard3";
        private const int CurrentVisualDesignVersion = 22;

        internal bool PanelVisible { get; set; } = true;
        internal bool TooltipsEnabled { get; set; } = true;
        internal double PanelXRatio { get; set; } = 1.0;
        internal double PanelYRatio { get; set; } = 0.08;
        internal string TargetDeviceName { get; set; } = string.Empty;
        internal double DefaultZoom { get; set; } = 1.5;
        internal InterfaceThemeMode ThemeMode { get; set; } = InterfaceThemeMode.Light;
        internal PastelThemeColor PastelTheme { get; set; } = PastelThemeColor.Neutral;
        internal bool UseCustomGlassLightColor { get; set; }
        internal int CustomGlassLightColorArgb { get; set; } = unchecked((int)0xFFD8E8F7);
        // 내부값은 0.00~0.20 유리 농도이며 설정창의 0~100% 배경 농도에 정비례한다.
        internal double GlassTintOpacity { get; set; } = 0.0;
        internal bool VisualDesignResetApplied { get; private set; }
        internal int PenColorArgb { get; set; } = unchecked((int)0xFFF03246);
        internal int HighlighterColorArgb { get; set; } = unchecked((int)0xFFFFD622);
        internal int ShapeColorArgb { get; set; } = unchecked((int)0xFF2D76F0);
        internal double PenWidth { get; set; } = 4.0;
        internal double HighlighterWidth { get; set; } = 18.0;
        internal double ShapeWidth { get; set; } = 4.0;

        internal static UserSettings Load()
        {
            var settings = new UserSettings();
            settings.ThemeMode = ResolveSystemThemeMode();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsPath))
                {
                    if (key == null)
                    {
                        return settings;
                    }

                    settings.PanelVisible = ReadBoolean(key, "PanelVisible", true);
                    settings.TooltipsEnabled = ReadBoolean(key, "TooltipsEnabled", true);
                    settings.PanelXRatio = ReadRatio(key, "PanelXRatio", 1.0);
                    settings.PanelYRatio = ReadRatio(key, "PanelYRatio", 0.08);
                    settings.TargetDeviceName = Convert.ToString(key.GetValue("TargetDeviceName", string.Empty));

                    PastelThemeColor savedPastel;
                    var savedPastelText = Convert.ToString(
                        key.GetValue("PastelTheme", PastelThemeColor.Neutral.ToString()));
                    if (Enum.TryParse(savedPastelText, true, out savedPastel) &&
                        Enum.IsDefined(typeof(PastelThemeColor), savedPastel))
                    {
                        settings.PastelTheme = savedPastel;
                    }
                    settings.UseCustomGlassLightColor = ReadBoolean(
                        key, "UseCustomGlassLightColor", false);
                    settings.CustomGlassLightColorArgb = Convert.ToInt32(
                        key.GetValue("CustomGlassLightColorArgb", unchecked((int)0xFFD8E8F7)),
                        CultureInfo.InvariantCulture);

                    double zoom;
                    if (double.TryParse(Convert.ToString(key.GetValue("DefaultZoom", "1.5")), out zoom))
                    {
                        settings.DefaultZoom = Math.Max(1.25, Math.Min(3.0, zoom));
                    }

                    var visualDesignVersion = Convert.ToInt32(
                        key.GetValue("VisualDesignVersion", 0),
                        CultureInfo.InvariantCulture);
                    if (visualDesignVersion >= 18)
                    {
                        double opacity;
                        if (double.TryParse(
                            Convert.ToString(key.GetValue("GlassTintOpacity", "0.06")),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out opacity))
                        {
                            settings.GlassTintOpacity = Math.Max(0.0,
                                Math.Min(LiquidGlassTheme.MaximumPanelGlassStrength, opacity));
                        }
                        if (visualDesignVersion == 19)
                        {
                            // Beta 19 r01/r02는 슬라이더 의미가 반대였다. 당시 사용자가 고른
                            // 숫자를 유지하도록 저장 농도를 한 번 반전한다(당시 0% -> 새 0%).
                            settings.GlassTintOpacity =
                                LiquidGlassTheme.MaximumPanelGlassStrength - settings.GlassTintOpacity;
                        }
                        if (visualDesignVersion < CurrentVisualDesignVersion)
                            settings.VisualDesignResetApplied = true;
                    }
                    else
                    {
                        // 오래된 디자인은 정식 후보의 기본값인 완전 투명 패널로 초기화한다.
                        settings.GlassTintOpacity = 0.0;
                        settings.VisualDesignResetApplied = true;
                    }

                    var legacyDrawingColor = Convert.ToInt32(
                        key.GetValue("DrawingColorArgb", unchecked((int)0xFFF03246)),
                        CultureInfo.InvariantCulture);
                    settings.PenColorArgb = Convert.ToInt32(
                        key.GetValue("PenColorArgb", legacyDrawingColor),
                        CultureInfo.InvariantCulture);
                    settings.HighlighterColorArgb = Convert.ToInt32(
                        key.GetValue("HighlighterColorArgb", unchecked((int)0xFFFFD622)),
                        CultureInfo.InvariantCulture);
                    settings.ShapeColorArgb = Convert.ToInt32(
                        key.GetValue("ShapeColorArgb", unchecked((int)0xFF2D76F0)),
                        CultureInfo.InvariantCulture);

                    double penWidth;
                    if (double.TryParse(
                        Convert.ToString(key.GetValue("PenWidth", "4.0")),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out penWidth))
                    {
                        settings.PenWidth = Math.Max(2.0, Math.Min(12.0, penWidth));
                    }

                    double highlighterWidth;
                    if (double.TryParse(
                        Convert.ToString(key.GetValue("HighlighterWidth", "18.0")),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out highlighterWidth))
                    {
                        settings.HighlighterWidth = Math.Max(8.0, Math.Min(32.0, highlighterWidth));
                    }

                    double shapeWidth;
                    if (double.TryParse(
                        Convert.ToString(key.GetValue("ShapeWidth", "4.0")),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out shapeWidth))
                    {
                        settings.ShapeWidth = Math.Max(2.0, Math.Min(8.0, shapeWidth));
                    }
                }
            }
            catch
            {
                // 설정을 읽지 못해도 기본값으로 수업을 계속할 수 있어야 한다.
            }

            return settings;
        }

        internal void Save()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsPath))
                {
                    if (key == null)
                    {
                        return;
                    }

                    key.SetValue("PanelVisible", PanelVisible ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("TooltipsEnabled", TooltipsEnabled ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("PanelXRatio", PanelXRatio.ToString("0.0000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("PanelYRatio", PanelYRatio.ToString("0.0000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("TargetDeviceName", TargetDeviceName ?? string.Empty, RegistryValueKind.String);
                    key.SetValue("DefaultZoom", DefaultZoom.ToString("0.00", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("ThemeMode", ThemeMode.ToString(), RegistryValueKind.String);
                    key.SetValue("PastelTheme", PastelTheme.ToString(), RegistryValueKind.String);
                    key.SetValue("UseCustomGlassLightColor", UseCustomGlassLightColor ? 1 : 0,
                        RegistryValueKind.DWord);
                    key.SetValue("CustomGlassLightColorArgb", CustomGlassLightColorArgb,
                        RegistryValueKind.DWord);
                    key.SetValue("VisualDesignVersion", CurrentVisualDesignVersion, RegistryValueKind.DWord);
                    key.SetValue("GlassTintOpacity", GlassTintOpacity.ToString("0.000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    // Beta 14 이하에서 같은 설정을 다시 읽을 수 있도록 기존 이름도 보존한다.
                    key.SetValue("InterfaceOpacity", GlassTintOpacity.ToString("0.000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("PanelOpacity", GlassTintOpacity.ToString("0.000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("DrawingColorArgb", PenColorArgb, RegistryValueKind.DWord);
                    key.SetValue("PenColorArgb", PenColorArgb, RegistryValueKind.DWord);
                    key.SetValue("HighlighterColorArgb", HighlighterColorArgb, RegistryValueKind.DWord);
                    key.SetValue("ShapeColorArgb", ShapeColorArgb, RegistryValueKind.DWord);
                    key.SetValue("PenWidth", PenWidth.ToString("0.0", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("HighlighterWidth", HighlighterWidth.ToString("0.0", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    key.SetValue("ShapeWidth", ShapeWidth.ToString("0.0", CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
                VisualDesignResetApplied = false;
            }
            catch
            {
            }
        }

        internal static InterfaceThemeMode ResolveSystemThemeMode()
        {
            // 투명 유리는 Windows 다크/라이트 모드에 종속되지 않는다.
            return InterfaceThemeMode.Light;
        }

        internal bool IsStartWithWindowsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunPath))
                {
                    return key?.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        internal bool SetStartWithWindows(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunPath, true))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        var executable = Process.GetCurrentProcess().MainModule?.FileName
                            ?? Assembly.GetExecutingAssembly().Location;
                        key.SetValue(RunValueName, "\"" + executable + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadBoolean(RegistryKey key, string name, bool defaultValue)
        {
            var value = key.GetValue(name);
            if (value == null)
            {
                return defaultValue;
            }

            int number;
            return int.TryParse(Convert.ToString(value), out number) ? number != 0 : defaultValue;
        }

        private static double ReadRatio(RegistryKey key, string name, double defaultValue)
        {
            var text = Convert.ToString(key.GetValue(name, defaultValue.ToString(CultureInfo.InvariantCulture)));
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return defaultValue;
            }

            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
