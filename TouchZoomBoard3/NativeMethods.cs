using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchZoomBoard
{
    internal static class NativeMethods
    {
        internal enum PointerInputType : uint
        {
            Pointer = 1,
            Touch = 2,
            Pen = 3,
            Mouse = 4,
            TouchPad = 5
        }

        internal const int GWL_EXSTYLE = -20;
        internal const int GWLP_HWNDPARENT = -8;
        internal const int WS_CHILD = 0x40000000;
        internal const int WS_VISIBLE = 0x10000000;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_APPWINDOW = 0x00040000;
        internal const int MW_FILTERMODE_EXCLUDE = 0;
        internal const int WM_HOTKEY = 0x0312;
        internal const int WM_ACTIVATE = 0x0006;
        internal const int WM_SETFOCUS = 0x0007;
        internal const int WM_KILLFOCUS = 0x0008;
        internal const int WM_MOUSEACTIVATE = 0x0021;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_NCRBUTTONUP = 0x00A5;
        internal const int WM_TOUCH = 0x0240;
        internal const int WM_POINTERUPDATE = 0x0245;
        internal const int WM_POINTERDOWN = 0x0246;
        internal const int WM_POINTERUP = 0x0247;
        internal const int WM_POINTERCAPTURECHANGED = 0x024C;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_MOUSEWHEEL = 0x020A;
        internal const int WM_CAPTURECHANGED = 0x0215;
        internal const int WM_DEVICECHANGE = 0x0219;
        internal const int WM_EXITSIZEMOVE = 0x0232;
        internal const int SM_DIGITIZER = 94;
        internal const int SM_MAXIMUMTOUCHES = 95;
        internal const int HTCAPTION = 2;
        internal const int HTTRANSPARENT = -1;
        internal const int MA_NOACTIVATE = 3;
        internal const uint MOD_ALT = 0x0001;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const uint VK_ESCAPE = 0x1B;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_FRAMECHANGED = 0x0020;
        internal const uint MONITOR_DEFAULTTONEAREST = 2;
        internal const int ENUM_CURRENT_SETTINGS = -1;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_COLOR_NONE = -2;
        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_TRANSIENTWINDOW = 3;
        private const int MinimumSystemBackdropBuild = 22621;
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_UPDATENOW = 0x0100;
        private const uint RDW_ERASENOW = 0x0200;
        private const uint RDW_FRAME = 0x0400;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        internal static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;

            internal POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        {
            internal int AccentState;
            internal int AccentFlags;
            internal int GradientColor;
            internal int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA
        {
            internal int Attribute;
            internal IntPtr Data;
            internal int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MAGTRANSFORM
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            internal float[] Matrix;

            internal static MAGTRANSFORM Create(float scale)
            {
                return Create(scale, scale);
            }

            internal static MAGTRANSFORM Create(float scaleX, float scaleY)
            {
                return new MAGTRANSFORM
                {
                    Matrix = new[]
                    {
                        scaleX, 0f, 0f,
                        0f, scaleY, 0f,
                        0f, 0f, 1f
                    }
                };
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string dmDeviceName;
            internal short dmSpecVersion;
            internal short dmDriverVersion;
            internal short dmSize;
            internal short dmDriverExtra;
            internal int dmFields;
            internal int dmPositionX;
            internal int dmPositionY;
            internal int dmDisplayOrientation;
            internal int dmDisplayFixedOutput;
            internal short dmColor;
            internal short dmDuplex;
            internal short dmYResolution;
            internal short dmTTOption;
            internal short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string dmFormName;
            internal short dmLogPixels;
            internal int dmBitsPerPel;
            internal int dmPelsWidth;
            internal int dmPelsHeight;
            internal int dmDisplayFlags;
            internal int dmDisplayFrequency;
            internal int dmICMMethod;
            internal int dmICMIntent;
            internal int dmMediaType;
            internal int dmDitherType;
            internal int dmReserved1;
            internal int dmReserved2;
            internal int dmPanningWidth;
            internal int dmPanningHeight;
        }

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagSetWindowFilterList(
            IntPtr hwnd,
            int filterMode,
            int count,
            [In] IntPtr[] windows);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRoundRectRgn(
            int left,
            int top,
            int right,
            int bottom,
            int ellipseWidth,
            int ellipseHeight);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(
            IntPtr hwnd,
            ref WINDOWCOMPOSITIONATTRIBDATA data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmFlush();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(
            IntPtr hwnd,
            ref RECT updateRectangle,
            IntPtr updateRegion,
            uint flags);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplaySettings(
            string deviceName,
            int modeNumber,
            ref DEVMODE deviceMode);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT point, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPointerType(uint pointerId, out PointerInputType pointerType);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        internal static string GetWindowClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "none";
            var className = new StringBuilder(128);
            return GetClassName(hwnd, className, className.Capacity) > 0
                ? className.ToString()
                : "unknown";
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        internal static double GetScaleForPoint(int x, int y)
        {
            try
            {
                var monitor = MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST);
                uint dpiX;
                uint dpiY;
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0)
                {
                    return Math.Max(1.0, dpiX / 96.0);
                }
            }
            catch (DllNotFoundException)
            {
                // Windows 8 이전 환경은 지원 대상이 아니지만 안전하게 기본값을 사용한다.
            }
            catch (EntryPointNotFoundException)
            {
            }

            return 1.0;
        }

        internal static int GetDisplayRefreshRate(string deviceName)
        {
            try
            {
                var mode = new DEVMODE
                {
                    dmDeviceName = new string('\0', 32),
                    dmFormName = new string('\0', 32),
                    dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
                };
                if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode) &&
                    mode.dmDisplayFrequency > 1)
                {
                    return mode.dmDisplayFrequency;
                }
            }
            catch
            {
                // 진단 정보 조회 실패는 확대 기능에 영향을 주지 않는다.
            }
            return 0;
        }

        internal static void PositionTopmostWindow(IntPtr hwnd, int x, int y, int width, int height, bool activate)
        {
            var flags = SWP_SHOWWINDOW;
            if (!activate)
            {
                flags |= SWP_NOACTIVATE;
            }

            SetWindowPos(hwnd, HWND_TOPMOST, x, y, Math.Max(1, width), Math.Max(1, height), flags);
        }

        internal static void SetOwnerWindow(IntPtr hwnd, IntPtr owner)
        {
            if (hwnd != IntPtr.Zero)
            {
                SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, owner);
            }
        }

        internal static void EnsureToolWindowStyle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var corrected = (style | WS_EX_TOOLWINDOW) & ~((long)WS_EX_APPWINDOW);
            if (corrected != style)
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(corrected));
            }

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        internal static void AddExtendedWindowStyle(IntPtr hwnd, long styles)
        {
            SetExtendedWindowStyle(hwnd, styles, true);
        }

        internal static void SetExtendedWindowStyle(IntPtr hwnd, long styles, bool enabled)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var current = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var updated = enabled ? current | styles : current & ~styles;
            if (updated != current)
            {
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(updated));
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
        }

        internal static void ApplyRoundedWindowRegion(IntPtr hwnd, int width, int height, int radius)
        {
            if (hwnd == IntPtr.Zero || width <= 0 || height <= 0)
            {
                return;
            }

            var diameter = Math.Max(2, radius * 2);
            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == IntPtr.Zero)
            {
                return;
            }

            // SetWindowRgn 성공 뒤에는 Windows가 region의 소유권을 가진다.
            if (SetWindowRgn(hwnd, region, true) == 0)
            {
                DeleteObject(region);
            }
        }

        internal static void ApplyRoundedWindowRegionFromCurrentBounds(IntPtr hwnd, double radiusDip)
        {
            RECT rectangle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out rectangle))
            {
                return;
            }

            var width = Math.Max(1, rectangle.Right - rectangle.Left);
            var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
            var scale = GetScaleForPoint(rectangle.Left + 1, rectangle.Top + 1);
            ApplyRoundedWindowRegion(
                hwnd,
                width,
                height,
                Math.Max(2, (int)Math.Round(radiusDip * scale)));
        }

        internal static void ClearWindowRegion(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
            }
        }

        internal static bool TryApplyAntialiasedRoundedCorners(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            // DWM 안티앨리어싱을 요청하되 실제 투명 모서리를 만드는 창 영역은 해제하지 않는다.
            try
            {
                var preference = DWMWCP_ROUND;
                return DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref preference,
                    sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryApplyLightAcrylic(
            IntPtr hwnd,
            byte whiteTintAlpha,
            out int result,
            out string detail)
        {
            result = -1;
            detail = "not-requested";
            if (hwnd == IntPtr.Zero)
            {
                detail = "invalid-hwnd";
                return false;
            }

            var version = Environment.OSVersion.Version;
            if (version.Major < 10)
            {
                detail = "unsupported-build-" + version.Build;
                return false;
            }

            try
            {
                // Windows 다크 모드와 무관하게 항상 흰 저알파 Acrylic을 합성한다.
                var darkMode = 0;
                var darkResult = DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref darkMode,
                    sizeof(int));

                var borderColor = DWMWA_COLOR_NONE;
                var borderResult = DwmSetWindowAttribute(
                    hwnd,
                    DWMWA_BORDER_COLOR,
                    ref borderColor,
                    sizeof(int));

                if (version.Build >= MinimumSystemBackdropBuild)
                {
                    var noBackdrop = DWMSBT_NONE;
                    DwmSetWindowAttribute(
                        hwnd,
                        DWMWA_SYSTEMBACKDROP_TYPE,
                        ref noBackdrop,
                        sizeof(int));
                }

                var policy = new ACCENT_POLICY
                {
                    AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 0,
                    // GradientColor는 AABBGGRR 순서이다.
                    GradientColor = unchecked((int)((uint)whiteTintAlpha << 24 | 0x00FFFFFFu)),
                    AnimationId = 0
                };
                var policySize = Marshal.SizeOf(typeof(ACCENT_POLICY));
                var policyPointer = Marshal.AllocHGlobal(policySize);
                try
                {
                    Marshal.StructureToPtr(policy, policyPointer, false);
                    var data = new WINDOWCOMPOSITIONATTRIBDATA
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = policyPointer,
                        SizeOfData = policySize
                    };
                    var accentResult = SetWindowCompositionAttribute(hwnd, ref data);
                    if (accentResult != 0)
                    {
                        result = 0;
                        detail = "accent=True, tintAlpha=" + whiteTintAlpha +
                            ", dark=0x" + unchecked((uint)darkResult).ToString("X8") +
                            ", borderNone=0x" + unchecked((uint)borderResult).ToString("X8");
                        return true;
                    }
                    result = Marshal.GetLastWin32Error();
                }
                finally
                {
                    Marshal.FreeHGlobal(policyPointer);
                }

                // Accent API가 차단된 Windows 11에서는 밝은 Transient Acrylic을 사용한다.
                if (version.Build >= MinimumSystemBackdropBuild)
                {
                    var backdrop = DWMSBT_TRANSIENTWINDOW;
                    result = DwmSetWindowAttribute(
                        hwnd,
                        DWMWA_SYSTEMBACKDROP_TYPE,
                        ref backdrop,
                        sizeof(int));
                    detail = "accent=False, transient=" + (result == 0) +
                        ", tintAlpha=" + whiteTintAlpha +
                        ", dark=0x" + unchecked((uint)darkResult).ToString("X8") +
                        ", borderNone=0x" + unchecked((uint)borderResult).ToString("X8");
                    return result == 0;
                }

                detail = "accent=False, tintAlpha=" + whiteTintAlpha +
                    ", win32=" + result;
                return false;
            }
            catch (DllNotFoundException)
            {
                detail = "dwmapi-unavailable";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                detail = "composition-entry-unavailable";
                return false;
            }
            catch (Exception exception)
            {
                detail = "exception-" + exception.GetType().Name;
                return false;
            }
        }

        internal static bool TryApplyClearTransparent(IntPtr hwnd, out int result, out string detail)
        {
            result = -1;
            detail = "not-requested";
            if (hwnd == IntPtr.Zero)
            {
                detail = "invalid-hwnd";
                return false;
            }

            try
            {
                var darkMode = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref darkMode, sizeof(int));
                var borderColor = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,
                    ref borderColor, sizeof(int));

                if (Environment.OSVersion.Version.Build >= MinimumSystemBackdropBuild)
                {
                    var noBackdrop = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,
                        ref noBackdrop, sizeof(int));
                }

                var policy = new ACCENT_POLICY
                {
                    AccentState = ACCENT_ENABLE_TRANSPARENTGRADIENT,
                    AccentFlags = 0,
                    GradientColor = unchecked((int)0x00FFFFFFu),
                    AnimationId = 0
                };
                var policySize = Marshal.SizeOf(typeof(ACCENT_POLICY));
                var policyPointer = Marshal.AllocHGlobal(policySize);
                try
                {
                    Marshal.StructureToPtr(policy, policyPointer, false);
                    var data = new WINDOWCOMPOSITIONATTRIBDATA
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = policyPointer,
                        SizeOfData = policySize
                    };
                    result = SetWindowCompositionAttribute(hwnd, ref data);
                    detail = "clearTransparent=" + (result != 0) + ", blur=False, tintAlpha=0";
                    return result != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(policyPointer);
                }
            }
            catch (DllNotFoundException)
            {
                detail = "dwmapi-unavailable";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                detail = "composition-entry-unavailable";
                return false;
            }
            catch (Exception exception)
            {
                detail = "exception-" + exception.GetType().Name;
                return false;
            }
        }

        internal static int SynchronizeComposition()
        {
            return TryDwmFlush();
        }

        internal static bool FlushAndRedrawDesktopRegion(
            RECT screenRectangle,
            out int firstDwmResult,
            out int secondDwmResult,
            out int redrawError,
            out double elapsedMilliseconds)
        {
            var stopwatch = Stopwatch.StartNew();
            firstDwmResult = TryDwmFlush();
            var redrawSucceeded = false;
            redrawError = 0;
            try
            {
                var desktop = GetDesktopWindow();
                if (desktop != IntPtr.Zero)
                {
                    redrawSucceeded = RedrawWindow(
                        desktop,
                        ref screenRectangle,
                        IntPtr.Zero,
                        RDW_INVALIDATE |
                        RDW_ERASE |
                        RDW_ALLCHILDREN |
                        RDW_UPDATENOW |
                        RDW_ERASENOW |
                        RDW_FRAME);
                    if (!redrawSucceeded) redrawError = Marshal.GetLastWin32Error();
                }
            }
            catch (DllNotFoundException)
            {
                redrawSucceeded = false;
                redrawError = -1;
            }
            catch (EntryPointNotFoundException)
            {
                redrawSucceeded = false;
                redrawError = -2;
            }

            secondDwmResult = TryDwmFlush();
            stopwatch.Stop();
            elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            return redrawSucceeded;
        }

        private static int TryDwmFlush()
        {
            try
            {
                return DwmFlush();
            }
            catch (DllNotFoundException)
            {
                return int.MinValue;
            }
            catch (EntryPointNotFoundException)
            {
                return int.MinValue;
            }
        }
    }
}
