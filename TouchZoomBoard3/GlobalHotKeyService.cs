using System;
using System.Windows.Interop;

namespace TouchZoomBoard
{
    internal sealed class GlobalHotKeyService : IDisposable
    {
        private const int EmergencyHotKeyId = 0x545A;
        private readonly HwndSource source;
        private bool disposed;

        internal event Action EmergencyRecoveryRequested;
        internal event Action DeviceChangeObserved;

        internal bool IsRegistered { get; }

        internal GlobalHotKeyService()
        {
            var parameters = new HwndSourceParameters("TouchZoomBoard3EmergencyHotKey")
            {
                // 메시지 전용 창은 WM_DEVICECHANGE 브로드캐스트를 받지 못한다.
                // 보이지 않는 최상위 HWND로 만들어 패널이 숨겨져 있어도 장치 변경을 감지한다.
                ParentWindow = IntPtr.Zero,
                WindowStyle = 0,
                Width = 0,
                Height = 0
            };

            source = new HwndSource(parameters);
            source.AddHook(WindowProcedure);
            IsRegistered = NativeMethods.RegisterHotKey(
                source.Handle,
                EmergencyHotKeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT |
                NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_ESCAPE);

            if (!IsRegistered)
            {
                DebugLog.Write("Ctrl+Alt+Shift+Esc 전역 긴급 복구 단축키를 등록하지 못했습니다.");
            }
        }

        private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == EmergencyHotKeyId)
            {
                EmergencyRecoveryRequested?.Invoke();
                handled = true;
            }
            else if (message == NativeMethods.WM_DEVICECHANGE)
            {
                DeviceChangeObserved?.Invoke();
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (IsRegistered)
            {
                NativeMethods.UnregisterHotKey(source.Handle, EmergencyHotKeyId);
            }

            source.RemoveHook(WindowProcedure);
            source.Dispose();
        }
    }
}
