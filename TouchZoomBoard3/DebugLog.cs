using System;
using System.Diagnostics;

namespace TouchZoomBoard
{
    /// <summary>
    /// 정식판은 사용자 PC에 로그 파일을 만들지 않는다.
    /// 개발 빌드의 Visual Studio 출력 창에서만 필요한 메시지를 확인한다.
    /// </summary>
    internal static class DebugLog
    {
        [Conditional("DEBUG")]
        internal static void Write(string message, Exception exception = null)
        {
            Debug.WriteLine(Format(message, exception));
        }

        [Conditional("FULL_DIAGNOSTICS")]
        internal static void WriteDiagnostic(string source, string message)
        {
            Debug.WriteLine("[" + (source ?? "DIAGNOSTIC") + "] " + (message ?? string.Empty));
        }

        [Conditional("INK_DIAGNOSTICS")]
        internal static void BeginInkDiagnosticSession(string version)
        {
            Debug.WriteLine("[INK-SESSION] TouchZoomBoard3 " + version + " 시작");
        }

        [Conditional("INK_DIAGNOSTICS")]
        internal static void WriteInkDiagnostic(string source, string message)
        {
            Debug.WriteLine("[" + (source ?? "INK") + "] " + (message ?? string.Empty));
        }

        [Conditional("INK_DIAGNOSTICS")]
        internal static void EndInkDiagnosticSession()
        {
            Debug.WriteLine("[INK-SESSION] 종료");
        }

        private static string Format(string message, Exception exception)
        {
            var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                "  " + (message ?? string.Empty);
            return exception == null ? text : text + Environment.NewLine + exception;
        }
    }
}
