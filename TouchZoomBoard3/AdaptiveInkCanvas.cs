using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        internal const string FilterRevision = "adaptive-v10-replay-safe";
        private const int MaximumReplayStrokes = 4;
        private const int MaximumReplaySamples = 8192;
        private const int MaximumReplayPackets = 256;
        private readonly object packetGate = new object();
        private readonly List<ReplayHistory> replayHistories = new List<ReplayHistory>();
        private ReplayHistory currentHistory;

        // WPF may deliver a UI-thread coalesced copy after the pen-thread input.
        // Keep output coordinates, rather than running that copy through the live
        // velocity/position state again. Only X/Y are restored; pressure and all
        // other properties of the incoming StylusPoint remain untouched.
        private struct SampleKey : IEquatable<SampleKey>
        {
            private long x;
            private long y;
            private float pressure;

            internal SampleKey(StylusPoint point)
            {
                // Repeated WPF transforms can differ below a millionth of a DIP.
                // Quantization is for lookup only; output coordinates are exact.
                x = (long)Math.Round(point.X * 1000000.0);
                y = (long)Math.Round(point.Y * 1000000.0);
                pressure = point.PressureFactor;
            }

            public bool Equals(SampleKey other) =>
                x == other.x && y == other.y && pressure.Equals(other.pressure);
            public override bool Equals(object other) => other is SampleKey && Equals((SampleKey)other);
            public override int GetHashCode()
            {
                unchecked { return ((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ pressure.GetHashCode(); }
            }
        }

        private sealed class ReplaySample
        {
            internal SampleKey Key;
            internal int Timestamp;
            internal long Ordinal;
            internal double X;
            internal double Y;
        }

        private sealed class ReplayPacket
        {
            internal string Action;
            internal int Timestamp;
            internal SampleKey[] Keys;
            internal double[] Coordinates;

            internal bool Matches(string action, int timestamp, StylusPointCollection points)
            {
                if (Action != action || Timestamp != timestamp || Keys.Length != points.Count) return false;
                for (int index = 0; index < points.Count; index++)
                    if (!Keys[index].Equals(new SampleKey(points[index]))) return false;
                return true;
            }

            internal void Apply(StylusPointCollection points)
            {
                for (int index = 0; index < points.Count; index++)
                {
                    var point = points[index];
                    point.X = Coordinates[index * 2];
                    point.Y = Coordinates[index * 2 + 1];
                    points[index] = point;
                }
            }
        }

        private sealed class ReplayHistory
        {
            internal int TabletId;
            internal int StylusId;
            internal int DownTimestamp;
            internal int LastTimestamp;
            internal int CancellationGeneration;
            internal AppMode Mode;
            internal double Width;
            internal double Zoom;
            internal int AcceptedPackets;
            internal bool Closed;
            private long nextOrdinal;
            private int cachedPacketSamples;
            private readonly Dictionary<SampleKey, LinkedList<ReplaySample>> samples =
                new Dictionary<SampleKey, LinkedList<ReplaySample>>();
            private readonly Queue<ReplaySample> sampleOrder = new Queue<ReplaySample>();
            private readonly Queue<ReplayPacket> packets = new Queue<ReplayPacket>();
            private ReplayPacket downPacket;

            internal bool TryPacket(string action, int timestamp, StylusPointCollection points)
            {
                if (downPacket != null && downPacket.Matches(action, timestamp, points))
                {
                    downPacket.Apply(points);
                    return true;
                }
                foreach (var packet in packets)
                {
                    if (!packet.Matches(action, timestamp, points)) continue;
                    packet.Apply(points);
                    return true;
                }
                return false;
            }

            internal void Remember(string action, int timestamp,
                StylusPointCollection raw, StylusPointCollection filtered)
            {
                var packet = new ReplayPacket
                {
                    Action = action, Timestamp = timestamp,
                    Keys = new SampleKey[raw.Count], Coordinates = new double[raw.Count * 2]
                };
                for (int index = 0; index < raw.Count; index++)
                {
                    var key = new SampleKey(raw[index]);
                    packet.Keys[index] = key;
                    packet.Coordinates[index * 2] = filtered[index].X;
                    packet.Coordinates[index * 2 + 1] = filtered[index].Y;
                    var sample = new ReplaySample
                    {
                        Key = key, Timestamp = timestamp, Ordinal = nextOrdinal++,
                        X = filtered[index].X, Y = filtered[index].Y
                    };
                    LinkedList<ReplaySample> positions;
                    if (!samples.TryGetValue(key, out positions))
                    {
                        positions = new LinkedList<ReplaySample>();
                        samples.Add(key, positions);
                    }
                    positions.AddLast(sample);
                    sampleOrder.Enqueue(sample);
                    while (sampleOrder.Count > MaximumReplaySamples)
                    {
                        var expired = sampleOrder.Dequeue();
                        var oldPositions = samples[expired.Key];
                        oldPositions.RemoveFirst();
                        if (oldPositions.Count == 0) samples.Remove(expired.Key);
                    }
                }
                // A large packet still reaches InkCanvas intact, but need not be
                // retained in the bounded exact-packet cache.
                if (raw.Count <= MaximumReplaySamples)
                {
                    if (action == "down") downPacket = packet;
                    else
                    {
                        packets.Enqueue(packet);
                        cachedPacketSamples += raw.Count;
                        while (packets.Count > MaximumReplayPackets || cachedPacketSamples > MaximumReplaySamples)
                            cachedPacketSamples -= packets.Dequeue().Keys.Length;
                    }
                }
                LastTimestamp = timestamp;
                AcceptedPackets++;
            }

            internal int ReuseHistoricalPoints(int timestamp, StylusPointCollection points)
            {
                int reused = 0;
                long minimumOrdinal = 0;
                for (int index = 0; index < points.Count; index++)
                {
                    var point = points[index];
                    LinkedList<ReplaySample> positions;
                    if (!samples.TryGetValue(new SampleKey(point), out positions)) continue;
                    ReplaySample match = null;
                    long closestTime = long.MaxValue;
                    foreach (var candidate in positions)
                    {
                        if (candidate.Ordinal < minimumOrdinal) continue;
                        // The batch timestamp denotes its start, not the time of
                        // each sample. Prefer the first chronological occurrence
                        // at/after it, so repeated crossings of a loop stay separate.
                        var gap = unchecked(candidate.Timestamp - timestamp);
                        if (gap >= 0) { match = candidate; break; }
                        var distance = -(long)gap;
                        if (distance < closestTime) { closestTime = distance; match = candidate; }
                    }
                    if (match == null) continue;
                    point.X = match.X;
                    point.Y = match.Y;
                    points[index] = point;
                    minimumOrdinal = match.Ordinal + 1;
                    reused++;
                }
                // Unmatched historical points are passed through unchanged.
                // Never drop samples or rewind the live filter to guess them.
                return reused;
            }
        }

        internal sealed class PacketResult
        {
            internal string Disposition;
            internal int Packet;
            internal int InputGapMilliseconds;
            internal int LastAcceptedTimestamp;
            internal int CachedPoints;
            internal int PassthroughPoints;
            internal bool StateAdvanced;
            internal bool StrokeCompleted;
            internal AppMode Mode;
            internal double Width;
            internal double Zoom;
        }

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
        private int lastPacketInputTimestamp;
        private double lastValidPacketSeconds = 1.0 / 120.0;
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
            ProcessRawPacket(input, "down");
            base.OnStylusDown(input);
        }

        protected override void OnStylusMove(RawStylusInput input)
        {
            ProcessRawPacket(input, "move");
            base.OnStylusMove(input);
        }

        protected override void OnStylusUp(RawStylusInput input)
        {
            ProcessRawPacket(input, "up");
            base.OnStylusUp(input);
        }

        // Kept separate from RawStylusInput so the regression executable can
        // exercise exactly the production state machine with synthetic packets.
        internal PacketResult ProcessInputPacket(string action, int tabletId, int stylusId,
            int inputTimestamp, StylusPointCollection points)
        {
            lock (packetGate)
            {
                if (action != "down" && action != "move" && action != "up")
                    throw new ArgumentException("Unknown stylus action", nameof(action));
                if (points == null || points.Count == 0)
                    return MakeResult("empty-packet", null, 0, 0, 0, false);

                CancelIfRequested();
                if (!FilteringEnabled)
                {
                    SuspendFiltering(action);
                    return MakeResult("filter-disabled", null, 0, 0, points.Count, false);
                }

                var history = FindReplayHistory(tabletId, stylusId, inputTimestamp);
                int gap = history == null ? 0 : unchecked(inputTimestamp - history.LastTimestamp);
                if (history != null && history.TryPacket(action, inputTimestamp, points))
                    return MakeResult("cached-packet", history, gap, points.Count, 0, false);

                if (action == "down")
                {
                    // A replayed Down must not reset an active or completed stroke.
                    if (history != null && (inputTimestamp == history.DownTimestamp || gap < 0))
                    {
                        int reused = history.ReuseHistoricalPoints(inputTimestamp, points);
                        return MakeResult("historical-down", history, gap, reused, points.Count - reused, false);
                    }
                    if (active)
                    {
                        WriteCancellationDiagnostic("unexpected-new-stylus-down");
                        if (currentHistory != null) currentHistory.Closed = true;
                    }
                    ignoreUntilStylusUp = false;
                    ResetStroke();
                    active = true;
                    history = new ReplayHistory
                    {
                        TabletId = tabletId, StylusId = stylusId,
                        DownTimestamp = inputTimestamp, LastTimestamp = inputTimestamp,
                        CancellationGeneration = activeCancellationGeneration,
                        Mode = activeMode,
                        Width = activeStrokeWidth, Zoom = activeZoom
                    };
                    currentHistory = history;
                    replayHistories.Add(history);
                    while (replayHistories.Count > MaximumReplayStrokes) replayHistories.RemoveAt(0);
                    gap = 0;
                }
                else if (history == null || history != currentHistory || history.Closed || !active || ignoreUntilStylusUp)
                {
                    // A late Move/Up can reuse the preceding stroke's geometry,
                    // but can never open a new stroke or finish the current one.
                    int reused = history == null ? 0 : history.ReuseHistoricalPoints(inputTimestamp, points);
                    return MakeResult(history == null ? "no-matching-down" : "closed-stroke-replay",
                        history, gap, reused, points.Count - reused, false);
                }

                if (gap < 0 || (gap == 0 && history.AcceptedPackets > 0 && action != "down"))
                {
                    int reused = history.ReuseHistoricalPoints(inputTimestamp, points);
                    // A same-timestamp batch may contain new samples. If none
                    // were seen before, process it normally using the last valid
                    // interval. A mixed replay stays intact without rewinding.
                    if (gap < 0 || reused > 0)
                    {
                        var result = MakeResult(gap < 0 ? "out-of-order-replay" : "same-time-replay",
                            history, gap, reused, points.Count - reused, false);
                        // A first real Up still closes this contact even if the driver
                        // gives it an older timestamp. Its coordinates do not rewind
                        // the state; subsequent copies are handled by Closed above.
                        if (action == "up")
                        {
                            CompleteStroke();
                            history.Closed = true;
                            result.StrokeCompleted = true;
                        }
                        return result;
                    }
                }

                var raw = points.Clone();
                FilterPoints(points, inputTimestamp, action == "up");
                history.Remember(action, inputTimestamp, raw, points);
                var accepted = MakeResult("filtered", history, gap, 0, 0, true);
                if (action == "up")
                {
                    CompleteStroke();
                    history.Closed = true;
                    accepted.StrokeCompleted = true;
                }
                return accepted;
            }
        }

        private ReplayHistory FindReplayHistory(int tabletId, int stylusId, int timestamp)
        {
            int generation = Volatile.Read(ref cancellationGeneration);
            for (int index = replayHistories.Count - 1; index >= 0; index--)
            {
                var history = replayHistories[index];
                if (history.TabletId == tabletId && history.StylusId == stylusId &&
                    history.CancellationGeneration == generation &&
                    unchecked(timestamp - history.DownTimestamp) >= 0)
                    return history;
            }
            return null;
        }

        // Called under packetGate. Non-ink callbacks do not copy or filter points.
        private void SuspendFiltering(string action)
        {
            if (active) WriteCancellationDiagnostic("filter-disabled");
            active = false;
            if (currentHistory != null) currentHistory.Closed = true;
            replayHistories.Clear();
            currentHistory = null;
            ignoreUntilStylusUp = action != "up";
        }

        private PacketResult MakeResult(string disposition, ReplayHistory history,
            int gap, int cachedPoints, int passthroughPoints, bool stateAdvanced)
        {
            return new PacketResult
            {
                Disposition = disposition,
                Packet = history == null ? 0 : history.AcceptedPackets,
                InputGapMilliseconds = gap,
                LastAcceptedTimestamp = history == null ? 0 : history.LastTimestamp,
                CachedPoints = cachedPoints, PassthroughPoints = passthroughPoints,
                StateAdvanced = stateAdvanced,
                Mode = history == null ? (AppMode)Volatile.Read(ref configuredMode) : history.Mode,
                Width = history == null ? BitConverter.Int64BitsToDouble(
                    Interlocked.Read(ref configuredStrokeWidthBits)) : history.Width,
                Zoom = history == null ? BitConverter.Int64BitsToDouble(
                    Interlocked.Read(ref configuredZoomBits)) : history.Zoom
            };
        }

        private void ResetStroke()
        {
            active = false;
            startTimestamp = Stopwatch.GetTimestamp();
            lastPacketInputTimestamp = 0;
            lastValidPacketSeconds = 1.0 / 120.0;
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
            if (currentHistory != null) currentHistory.Closed = true;
            replayHistories.Clear();
            currentHistory = null;
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

        private void ProcessRawPacket(RawStylusInput input, string action)
        {
            try
            {
                if (!FilteringEnabled)
                {
                    lock (packetGate) { CancelIfRequested(); SuspendFiltering(action); }
                    return;
                }
                var points = input.GetStylusPoints();
                if (points == null || points.Count == 0) return;
                var result = ProcessInputPacket(action, input.TabletDeviceId, input.StylusDeviceId,
                    input.Timestamp, points);
                if (result.StateAdvanced || result.CachedPoints > 0) input.SetStylusPoints(points);
            }
            catch (Exception exception)
            {
                DebugLog.Write("적응형 필기 입력 처리 중 오류가 발생했습니다.", exception);
            }
        }

        private void FilterPoints(StylusPointCollection points, int inputTimestamp, bool anchorFinalPoint)
        {
            if (points == null || points.Count == 0) return;
            var hasPreviousPacket = packetCount > 0;
            // Input timestamps remain stable when magnification delays delivery.
            // Stopwatch measures processing time, not the sampling interval.
            var signedGap = hasPreviousPacket ? unchecked(inputTimestamp - lastPacketInputTimestamp) : 0;
            // Signed subtraction handles the 32-bit tick rollover and distinguishes
            // a historical report from a genuine long sampling interval.
            if (hasPreviousPacket && signedGap < 0) return;
            var measuredPacketSeconds = signedGap / 1000.0;
            if (hasPreviousPacket && measuredPacketSeconds > 0.0 && measuredPacketSeconds < 1.0)
            {
                var intervalMilliseconds = measuredPacketSeconds * 1000.0;
                packetIntervalMillisecondsTotal += intervalMilliseconds;
                packetIntervalMillisecondsMaximum = Math.Max(
                    packetIntervalMillisecondsMaximum,
                    intervalMilliseconds);
                measuredPacketIntervals++;
            }

            var validInterval = measuredPacketSeconds > 0.0 && measuredPacketSeconds < 1.0;
            if (hasPreviousPacket && validInterval) lastValidPacketSeconds = measuredPacketSeconds;
            var packetSeconds = Math.Max(0.001, Math.Min(0.1, lastValidPacketSeconds));
            var pointSeconds = GetPointIntervalSeconds(packetSeconds, points.Count);
            var reanchorAtFirstPoint = hasPreviousPacket && validInterval &&
                measuredPacketSeconds >= ReanchorThresholdSeconds;
            lastPacketInputTimestamp = inputTimestamp;
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

                // InkCanvas collects in screen DIPs in both normal and zoomed
                // views. Apply one profile: scaling the filter by background zoom
                // permanently changes the saved stroke's geometry.
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

        }

        internal static double GetPointIntervalSeconds(double packetSeconds, int points)
        {
            // A batched report must share its interval among all samples. The old
            // 2 ms minimum per point stretched 8 ms / 16 points into 32 ms.
            return Math.Max(0.0001, Math.Min(0.1, packetSeconds / Math.Max(1, points)));
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
                    ? "highlighter-input-replay-safe-v10"
                    : "pen-input-replay-safe-v10"
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
