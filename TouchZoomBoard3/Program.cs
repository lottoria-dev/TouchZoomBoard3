using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;

namespace TouchZoomBoard
{
    internal static class Program
    {
        private const string MutexName = "Local\\lottoria-dev.TouchZoomBoard3";
        private const string TouchRefreshRestartArgument = "--touch-refresh-restart";
        private const string WaitForProcessArgument = "--wait-for-pid";

        [STAThread]
        private static void Main(string[] args)
        {
            var startedByTouchRefresh = args.Any(argument =>
                string.Equals(argument, TouchRefreshRestartArgument, StringComparison.OrdinalIgnoreCase));
            WaitForPreviousProcess(args);

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (startedByTouchRefresh)
                    {
                        DebugLog.WriteDiagnostic("INPUT-REFRESH", "자동 재시작이 기존 프로세스 종료를 기다리지 못했습니다.");
                        return;
                    }
                    MessageBox.Show("TouchZoomBoard3가 이미 실행 중입니다.", "TouchZoomBoard3",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                AppController controller = null;
                var handlingUnhandledError = false;
                application.DispatcherUnhandledException += (sender, eventArgs) =>
                {
                    if (handlingUnhandledError)
                    {
                        return;
                    }

                    handlingUnhandledError = true;
                    try
                    {
                        controller?.RecoverFromUnhandledError(eventArgs.Exception);
                        MessageBox.Show(
                            "예기치 않은 오류가 발생하여 정상 화면 복구를 시도했습니다.",
                            "TouchZoomBoard3 복구",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        eventArgs.Handled = true;
                    }
                    finally
                    {
                        handlingUnhandledError = false;
                    }
                };

                try
                {
                    controller = new AppController(application, startedByTouchRefresh);
                    controller.Start();
                    application.Run();
                }
                catch (Exception exception)
                {
                    DebugLog.Write("프로그램 시작 또는 메시지 루프에서 오류가 발생했습니다.", exception);
                    MessageBox.Show(
                        "프로그램을 시작하지 못했습니다.\n\n" + exception.Message,
                        "TouchZoomBoard3 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    controller?.Dispose();
                }
            }
        }

        private static void WaitForPreviousProcess(string[] args)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (!string.Equals(args[index], WaitForProcessArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int processId;
                if (!int.TryParse(args[index + 1], out processId) || processId <= 0)
                {
                    return;
                }

                try
                {
                    using (var previous = Process.GetProcessById(processId))
                    {
                        previous.WaitForExit(10000);
                    }
                }
                catch (ArgumentException)
                {
                    // 이미 종료된 경우에는 바로 시작한다.
                }
                catch (Exception exception)
                {
                    DebugLog.Write("입력 장치 자동 재시작 대기 중 오류가 발생했습니다.", exception);
                }
                return;
            }
        }
    }
}
