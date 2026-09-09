using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;

namespace TouchZoomBoard
{
    /// <summary>
    /// DynamicRenderer 앞에서 입력 중에만 속도 적응형 필터를 적용한다.
    /// 펜을 뗀 뒤 획을 다시 바꾸지 않으므로 판서 잔상과 모양 변화를 줄인다.
    /// </summary>
    internal sealed class AdaptiveInkCanvas : InkCanvas
    {
        private readonly AdaptiveStylusFilter filter;

        internal AdaptiveInkCanvas()
        {
            filter = new AdaptiveStylusFilter();
            var rendererIndex = StylusPlugIns.IndexOf(DynamicRenderer);
            if (rendererIndex >= 0) StylusPlugIns.Insert(rendererIndex, filter);
            else StylusPlugIns.Add(filter);
        }

        internal bool TryTakeStrokeDiagnostic(out InkStrokeDiagnostic diagnostic)
        {
            return filter.TryTakeCompleted(out diagnostic);
        }

        internal void ConfigureAdaptiveFiltering(AppMode mode, double strokeWidth)
        {
            filter.SetMode(mode);
            filter.SetStrokeWidth(strokeWidth);
            filter.FilteringEnabled = mode == AppMode.Pen || mode == AppMode.Highlighter;
        }

        internal void SetZoomFactor(double zoomFactor)
        {
            filter.SetZoomFactor(zoomFactor);
        }

        internal void CancelActiveFiltering(string reason)
        {
            filter.RequestCancel(reason);
        }
    }

    internal sealed class InkStrokeDiagnostic
    {
        internal int PacketCount { get; set; }
        internal int PointCount { get; set; }
        internal double DurationMilliseconds { get; set; }
        internal double AverageSpeed { get; set; }
        internal double MaximumSpeed { get; set; }
        internal double AverageCorrection { get; set; }
        internal double MaximumCorrection { get; set; }
        internal double RawPathLength { get; set; }
        internal double FilteredPathLength { get; set; }
        internal double AveragePacketIntervalMilliseconds { get; set; }
        internal double MaximumPacketIntervalMilliseconds { get; set; }
        internal double CallbackRate { get; set; }
        internal int ReanchorCount { get; set; }
        internal int SoftLimitCount { get; set; }
        internal int HardClampCount { get; set; }
        internal int EndpointEasePointCount { get; set; }
        internal double EndpointResidualCorrection { get; set; }
        internal double StrokeWidth { get; set; }
        internal string FilterProfile { get; set; }

        internal string ToLogText()
        {
            return "profile=" + FilterProfile +
                   ", packets=" + PacketCount +
                   ", points=" + PointCount +
                   ", strokeWidthDip=" + StrokeWidth.ToString("0.0") +
                   ", durationMs=" + DurationMilliseconds.ToString("0.0") +
                   ", avgSpeedDipSec=" + AverageSpeed.ToString("0.0") +
                   ", maxSpeedDipSec=" + MaximumSpeed.ToString("0.0") +
                   ", avgCorrectionDip=" + AverageCorrection.ToString("0.000") +
                   ", maxCorrectionDip=" + MaximumCorrection.ToString("0.000") +
                   ", rawLengthDip=" + RawPathLength.ToString("0.0") +
                   ", filteredLengthDip=" + FilteredPathLength.ToString("0.0") +
                   ", callbackHz=" + CallbackRate.ToString("0.0") +
                   ", avgPacketIntervalMs=" + AveragePacketIntervalMilliseconds.ToString("0.00") +
                   ", maxPacketIntervalMs=" + MaximumPacketIntervalMilliseconds.ToString("0.00") +
                   ", avgPointSpacingDip=" +
                       (FilteredPathLength / Math.Max(1, PointCount - 1)).ToString("0.000") +
                   ", reanchors=" + ReanchorCount +
                   ", softLimits=" + SoftLimitCount +
                   ", hardClamps=" + HardClampCount +
                   ", endpointEasePoints=" + EndpointEasePointCount +
                   ", endpointResidualDip=" + EndpointResidualCorrection.ToString("0.000");
        }
    }

    internal sealed class AdaptiveStylusFilter : StylusPlugIn
    {
        // Beta 1의 평균 보정 거리가 7~9 DIP에 달해 손끝보다 선이 늦게 따라왔다.
        // 저속 떨림 억제는 유지하면서 위치 컷오프와 속도 반응을 높여 지연을 줄인다.
        private const double PenMinimumCutoff = 18.0;
        private const double PenSpeedCoefficient = 0.050;
        private const double PenMaximumLagDip = 6.0;
        private const double ThickPenMinimumCutoff = 42.0;
        private const double ThickPenSpeedCoefficient = 0.120;
        private const double ThickPenMaximumLagDip = 2.0;
        private const double HighlighterMinimumCutoff = 22.0;
        private const double HighlighterSpeedCoefficient = 0.075;
        private const double HighlighterMaximumLagDip = 10.0;
        private const double DerivativeCutoff = 2.0;
        private const double ReanchorThresholdSeconds = 0.080;
        private readonly ConcurrentQueue<InkStrokeDiagnostic> completed =
            new ConcurrentQueue<InkStrokeDiagnostic>();

        private bool active;
        private long startTimestamp;
        private long lastPacketTimestamp;
        private double previousRawX;
        private double previousRawY;
        private double filteredX;
        private double filteredY;
        private double filteredVelocityX;
        private double filteredVelocityY;
        private int packetCount;
        private int pointCount;
        private double speedSum;
        private double maximumSpeed;
        private double correctionSum;
        private double maximumCorrection;
        private double rawPathLength;
        private double filteredPathLength;
        private double packetIntervalMillisecondsTotal;
        private double packetIntervalMillisecondsMaximum;
        private int measuredPacketIntervals;
        private int reanchorCount;
        private int softLimitCount;
        private int hardClampCount;
        private int endpointEasePointCount;
        private double endpointResidualCorrection;
        private int configuredMode = (int)AppMode.Pen;
        private long configuredStrokeWidthBits = BitConverter.DoubleToInt64Bits(4.0);
        private long configuredZoomBits = BitConverter.DoubleToInt64Bits(1.0);
        private int cancellationGeneration;
        private int activeCancellationGeneration;
        private string cancellationReason = "state-change";
        private AppMode activeMode = AppMode.Pen;
        private double activeStrokeWidth = 4.0;
        private double activeZoom = 1.0;
        private bool ignoreUntilStylusUp;

        internal volatile bool FilteringEnabled;

        internal void SetMode(AppMode mode)
        {
            Volatile.Write(ref configuredMode, (int)mode);
        }

        internal void SetStrokeWidth(double width)
        {
            var normalized = Math.Max(1.0, Math.Min(64.0, width));
            Interlocked.Exchange(ref configuredStrokeWidthBits,
                BitConverter.DoubleToInt64Bits(normalized));
        }

        internal void SetZoomFactor(double zoomFactor)
        {
            var normalized = Math.Max(1.0, Math.Min(5.0, zoomFactor));
            Interlocked.Exchange(ref configuredZoomBits,
                BitConverter.DoubleToInt64Bits(normalized));
        }

        internal void RequestCancel(string reason)
        {
            cancellationReason = string.IsNullOrWhiteSpace(reason) ? "state-change" : reason;
            Interlocked.Increment(ref cancellationGeneration);
        }

        internal bool TryTakeCompleted(out InkStrokeDiagnostic diagnostic)
        {
            return completed.TryDequeue(out diagnostic);
        }

        protected override void OnStylusDown(RawStylusInput input)
        {
            if (!FilteringEnabled)
            {
                if (active) WriteCancellationDiagnostic("filter-disabled-before-stylus-down");
                active = false;
                ignoreUntilStylusUp = false;
                base.OnStylusDown(input);
                return;
            }
            try
            {
                // 이전 입력에서 StylusUp을 받지 못했더라도 새 획과 연결하지 않는다.
                if (!CancelIfRequested() && active)
                {
                    WriteCancellationDiagnostic("unexpected-new-stylus-down");
                    active = false;
                }
                ignoreUntilStylusUp = false;
                ResetStroke();
                active = true;
                FilterPacket(input);
            }
            catch (Exception exception)
            {
                DebugLog.Write("적응형 필기 시작 처리 중 오류가 발생했습니다.", exception);
            }
            base.OnStylusDown(input);
        }

        protected override void OnStylusMove(RawStylusInput input)
        {
            try
            {
                if (CancelIfRequested() || ignoreUntilStylusUp)
                {
                    base.OnStylusMove(input);
                    return;
                }
                if (!FilteringEnabled)
                {
                    active = false;
                    base.OnStylusMove(input);
                    return;
                }
                if (!active)
                {
                    ResetStroke();
                    active = true;
                }
                FilterPacket(input);
            }
            catch (Exception exception)
            {
                DebugLog.Write("적응형 필기 이동 처리 중 오류가 발생했습니다.", exception);
            }
            base.OnStylusMove(input);
        }

        protected override void OnStylusUp(RawStylusInput input)
        {
            try
            {
                if (CancelIfRequested() || ignoreUntilStylusUp)
                {
                    active = false;
                    ignoreUntilStylusUp = false;
                    base.OnStylusUp(input);
                    return;
                }
                if (!FilteringEnabled)
                {
                    active = false;
                    base.OnStylusUp(input);
                    return;
                }
                if (active)
                {
                    FilterPacket(input, true);
                    CompleteStroke();
                }
            }
            catch (Exception exception)
            {
                DebugLog.Write("적응형 필기 종료 처리 중 오류가 발생했습니다.", exception);
                active = false;
            }
            base.OnStylusUp(input);
        }

        private void ResetStroke()
        {
            active = false;
            startTimestamp = Stopwatch.GetTimestamp();
            lastPacketTimestamp = startTimestamp;
            previousRawX = previousRawY = filteredX = filteredY = 0.0;
            filteredVelocityX = filteredVelocityY = 0.0;
            packetCount = pointCount = 0;
            speedSum = maximumSpeed = correctionSum = 0.0;
            maximumCorrection = 0.0;
            rawPathLength = filteredPathLength = 0.0;
            packetIntervalMillisecondsTotal = 0.0;
            packetIntervalMillisecondsMaximum = 0.0;
            measuredPacketIntervals = 0;
            reanchorCount = 0;
            softLimitCount = 0;
            hardClampCount = 0;
            endpointEasePointCount = 0;
            endpointResidualCorrection = 0.0;
            activeMode = (AppMode)Volatile.Read(ref configuredMode);
            activeStrokeWidth = BitConverter.Int64BitsToDouble(
                Interlocked.Read(ref configuredStrokeWidthBits));
            activeZoom = BitConverter.Int64BitsToDouble(
                Interlocked.Read(ref configuredZoomBits));
            activeCancellationGeneration = Volatile.Read(ref cancellationGeneration);
        }

        private bool CancelIfRequested()
        {
            if (!active || activeCancellationGeneration == Volatile.Read(ref cancellationGeneration))
            {
                return false;
            }

            WriteCancellationDiagnostic(cancellationReason);
            active = false;
            ignoreUntilStylusUp = true;
            return true;
        }

        private void WriteCancellationDiagnostic(string reason)
        {
            DebugLog.WriteInkDiagnostic("INK-SESSION",
                "cancelled=True, reason=" + reason +
                ", packets=" + packetCount +
                ", points=" + pointCount +
                ", strokeWidthDip=" + activeStrokeWidth.ToString("0.0") +
                ", zoom=" + activeZoom.ToString("0.000"));
        }

        private void FilterPacket(RawStylusInput input, bool anchorFinalPoint = false)
        {
            var points = input.GetStylusPoints();
            if (points == null || points.Count == 0) return;

            var now = Stopwatch.GetTimestamp();
            var hasPreviousPacket = packetCount > 0;
            var measuredPacketSeconds = (now - lastPacketTimestamp) / (double)Stopwatch.Frequency;
            if (hasPreviousPacket && measuredPacketSeconds > 0.0 && measuredPacketSeconds < 1.0)
            {
                var intervalMilliseconds = measuredPacketSeconds * 1000.0;
                packetIntervalMillisecondsTotal += intervalMilliseconds;
                packetIntervalMillisecondsMaximum = Math.Max(
                    packetIntervalMillisecondsMaximum,
                    intervalMilliseconds);
                measuredPacketIntervals++;
            }

            // 실제 콜백 간격을 사용한다. Beta 1처럼 30 Hz 입력을 60 Hz로 잘라 계산하지 않는다.
            var packetSeconds = hasPreviousPacket ? measuredPacketSeconds : 1.0 / 120.0;
            packetSeconds = Math.Max(1.0 / 500.0, Math.Min(1.0 / 10.0, packetSeconds));
            var pointSeconds = Math.Max(1.0 / 500.0, Math.Min(1.0 / 10.0, packetSeconds / points.Count));
            var reanchorAtFirstPoint = hasPreviousPacket &&
                measuredPacketSeconds >= ReanchorThresholdSeconds;
            lastPacketTimestamp = now;
            packetCount++;

            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                var rawX = point.X;
                var rawY = point.Y;
                if (pointCount == 0)
                {
                    previousRawX = filteredX = rawX;
                    previousRawY = filteredY = rawY;
                    pointCount++;
                    continue;
                }

                if (reanchorAtFirstPoint && index == 0)
                {
                    var rawJumpX = rawX - previousRawX;
                    var rawJumpY = rawY - previousRawY;
                    var filteredJumpX = rawX - filteredX;
                    var filteredJumpY = rawY - filteredY;
                    rawPathLength += Math.Sqrt(rawJumpX * rawJumpX + rawJumpY * rawJumpY);
                    filteredPathLength += Math.Sqrt(
                        filteredJumpX * filteredJumpX + filteredJumpY * filteredJumpY);
                    previousRawX = filteredX = rawX;
                    previousRawY = filteredY = rawY;
                    filteredVelocityX = filteredVelocityY = 0.0;
                    pointCount++;
                    reanchorCount++;
                    continue;
                }

                var rawDeltaX = rawX - previousRawX;
                var rawDeltaY = rawY - previousRawY;
                var velocityX = rawDeltaX / pointSeconds;
                var velocityY = rawDeltaY / pointSeconds;
                var derivativeAlpha = Alpha(DerivativeCutoff, pointSeconds);
                filteredVelocityX = Lerp(filteredVelocityX, velocityX, derivativeAlpha);
                filteredVelocityY = Lerp(filteredVelocityY, velocityY, derivativeAlpha);
                var speed = Math.Sqrt(filteredVelocityX * filteredVelocityX + filteredVelocityY * filteredVelocityY);
                var highlighterProfile = activeMode == AppMode.Highlighter;
                // 굵은 펜은 같은 보정 거리도 훨씬 무겁고 늦게 느껴진다. 6~12 DIP에서
                // 필터를 연속적으로 원본 좌표 쪽으로 열어 굵기 변경 시 반응성이 꺾이지 않게 한다.
                var thickPenFactor = highlighterProfile
                    ? 0.0
                    : Math.Max(0.0, Math.Min(1.0, (activeStrokeWidth - 6.0) / 6.0));
                var minimumCutoff = highlighterProfile
                    ? HighlighterMinimumCutoff
                    : Lerp(PenMinimumCutoff, ThickPenMinimumCutoff, thickPenFactor);
                var speedCoefficient = highlighterProfile
                    ? HighlighterSpeedCoefficient
                    : Lerp(PenSpeedCoefficient, ThickPenSpeedCoefficient, thickPenFactor);

                // 확대 화면에서는 작은 완만한 곡선의 각짐이 더 쉽게 드러난다. 화면 좌표를
                // 배율로 변환하지 않고, 6 DIP 이하의 가는 펜에만 보정 강도를 소폭 높인다.
                var zoomCurveFactor = highlighterProfile || activeStrokeWidth > 6.0
                    ? 0.0
                    : Math.Max(0.0, Math.Min(1.0, (activeZoom - 1.0) / 2.0));
                minimumCutoff *= Lerp(1.0, 0.92, zoomCurveFactor);
                speedCoefficient *= Lerp(1.0, 0.82, zoomCurveFactor);
                var positionAlpha = Alpha(
                    minimumCutoff + speedCoefficient * speed,
                    pointSeconds);
                var oldFilteredX = filteredX;
                var oldFilteredY = filteredY;
                filteredX = Lerp(filteredX, rawX, positionAlpha);
                filteredY = Lerp(filteredY, rawY, positionAlpha);

                // 넓은 형광펜과 빠른 판서에서 필터가 손끝을 수십 DIP 뒤따르던 현상을 막는다.
                // 저속 흔들림 보정은 유지하되 원본 좌표와의 최대 거리를 제한한다.
                var maximumLag = highlighterProfile
                    ? HighlighterMaximumLagDip
                    : Lerp(PenMaximumLagDip, ThickPenMaximumLagDip, thickPenFactor);
                var lagX = rawX - filteredX;
                var lagY = rawY - filteredY;
                var lag = Math.Sqrt(lagX * lagX + lagY * lagY);
                // 최대 지연선에서 좌표를 갑자기 잘라 곡선이 꺾이던 하드 클램프를
                // 연속 함수로 바꾼다. softStart에서는 위치와 기울기가 이어지고,
                // 지연이 커져도 maximumLag에 점진적으로 가까워진다.
                var softStart = maximumLag * 0.72;
                if (lag > softStart)
                {
                    var softRange = Math.Max(0.10, maximumLag - softStart);
                    var compressedLag = softStart + softRange *
                        (1.0 - Math.Exp(-(lag - softStart) / softRange));
                    var lagScale = compressedLag / lag;
                    filteredX = rawX - lagX * lagScale;
                    filteredY = rawY - lagY * lagScale;
                    softLimitCount++;

                    // 부동 소수점 오차나 비정상 좌표에 대한 최종 안전장치다.
                    lagX = rawX - filteredX;
                    lagY = rawY - filteredY;
                    lag = Math.Sqrt(lagX * lagX + lagY * lagY);
                    if (lag > maximumLag)
                    {
                        lagScale = maximumLag / lag;
                        filteredX = rawX - lagX * lagScale;
                        filteredY = rawY - lagY * lagScale;
                        hardClampCount++;
                    }
                }

                if (anchorFinalPoint)
                {
                    // Alpha 2는 마지막 한 점을 원시 좌표에 100% 고정해 최대 6~10 DIP의
                    // 보정량이 한 번에 풀렸다. 그 결과 획 끝이나 짧은 곡선이 바깥으로
                    // 돌출되어 보일 수 있었다. 마지막 패킷 전체에서 SmoothStep으로
                    // 보정량 일부만 점진적으로 줄여 곡률과 끝단 반응을 함께 보존한다.
                    var progress = (index + 1.0) / points.Count;
                    var smoothProgress = progress * progress * (3.0 - 2.0 * progress);
                    var maximumEndpointEase = highlighterProfile
                        ? 0.24
                        : Lerp(0.38, 0.28, thickPenFactor);
                    var endpointEase = maximumEndpointEase * smoothProgress;
                    filteredX = Lerp(filteredX, rawX, endpointEase);
                    filteredY = Lerp(filteredY, rawY, endpointEase);
                    endpointEasePointCount++;

                    if (index == points.Count - 1)
                    {
                        var residualX = rawX - filteredX;
                        var residualY = rawY - filteredY;
                        endpointResidualCorrection = Math.Sqrt(
                            residualX * residualX + residualY * residualY);
                    }
                }

                point.X = filteredX;
                point.Y = filteredY;
                points[index] = point;

                var filteredDeltaX = filteredX - oldFilteredX;
                var filteredDeltaY = filteredY - oldFilteredY;
                var correctionX = rawX - filteredX;
                var correctionY = rawY - filteredY;
                var correction = Math.Sqrt(correctionX * correctionX + correctionY * correctionY);
                rawPathLength += Math.Sqrt(rawDeltaX * rawDeltaX + rawDeltaY * rawDeltaY);
                filteredPathLength += Math.Sqrt(filteredDeltaX * filteredDeltaX + filteredDeltaY * filteredDeltaY);
                correctionSum += correction;
                maximumCorrection = Math.Max(maximumCorrection, correction);
                speedSum += speed;
                maximumSpeed = Math.Max(maximumSpeed, speed);
                previousRawX = rawX;
                previousRawY = rawY;
                pointCount++;
            }

            input.SetStylusPoints(points);
        }

        private void CompleteStroke()
        {
            var elapsed = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            var measuredPoints = Math.Max(1, pointCount - 1);
            completed.Enqueue(new InkStrokeDiagnostic
            {
                PacketCount = packetCount,
                PointCount = pointCount,
                DurationMilliseconds = elapsed,
                AverageSpeed = speedSum / measuredPoints,
                MaximumSpeed = maximumSpeed,
                AverageCorrection = correctionSum / measuredPoints,
                MaximumCorrection = maximumCorrection,
                RawPathLength = rawPathLength,
                FilteredPathLength = filteredPathLength,
                AveragePacketIntervalMilliseconds = measuredPacketIntervals == 0
                    ? 0.0
                    : packetIntervalMillisecondsTotal / measuredPacketIntervals,
                MaximumPacketIntervalMilliseconds = packetIntervalMillisecondsMaximum,
                CallbackRate = elapsed <= 0.0 ? 0.0 : packetCount * 1000.0 / elapsed,
                ReanchorCount = reanchorCount,
                SoftLimitCount = softLimitCount,
                HardClampCount = hardClampCount,
                EndpointEasePointCount = endpointEasePointCount,
                EndpointResidualCorrection = endpointResidualCorrection,
                StrokeWidth = activeStrokeWidth,
                FilterProfile = activeMode == AppMode.Highlighter
                    ? "highlighter-balanced-a2"
                    : "pen-curve-stable-a2"
            });
            active = false;
        }

        private static double Alpha(double cutoff, double seconds)
        {
            var timeConstant = 1.0 / (2.0 * Math.PI * Math.Max(0.01, cutoff));
            return 1.0 / (1.0 + timeConstant / Math.Max(0.0001, seconds));
        }

        private static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }
    }
}
