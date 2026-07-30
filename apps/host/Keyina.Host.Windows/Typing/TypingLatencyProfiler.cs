using System.Diagnostics;
using System.Numerics;

namespace Keyina.Host.Windows.Typing;

public enum TypingLatencyStage
{
    CallbackTotal,
    ForegroundContext,
    SafetyGuard,
    EngineProcess,
    InputInjection,
}

public readonly record struct TypingLatencyStageSnapshot(
    TypingLatencyStage Stage,
    long SampleCount,
    long MedianNanoseconds,
    long P95Nanoseconds,
    long P99Nanoseconds,
    long MaximumNanoseconds,
    double MeanNanoseconds);

public static class TypingLatencyProfiler
{
    private static ProfilerState state = new();
    private static int enabled;

    public static bool IsEnabled => Volatile.Read(ref enabled) != 0;

    public static void SetEnabled(bool value) =>
        Volatile.Write(ref enabled, value ? 1 : 0);

    public static long Start() => IsEnabled ? Stopwatch.GetTimestamp() : 0;

    public static void Record(TypingLatencyStage stage, long startedAt)
    {
        if (startedAt == 0)
        {
            return;
        }

        var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
        if (elapsedTicks <= 0)
        {
            elapsedTicks = 1;
        }

        Volatile.Read(ref state).Record(stage, elapsedTicks);
    }

    public static IReadOnlyList<TypingLatencyStageSnapshot> Snapshot() =>
        Volatile.Read(ref state).Snapshot();

    public static void Clear() =>
        Interlocked.Exchange(ref state, new ProfilerState());

    private sealed class ProfilerState
    {
        private const int StageCount = (int)TypingLatencyStage.InputInjection + 1;
        private readonly StageHistogram[] histograms = CreateHistograms();

        public void Record(TypingLatencyStage stage, long elapsedTicks)
        {
            var index = (int)stage;
            if ((uint)index >= (uint)histograms.Length)
            {
                return;
            }

            histograms[index].Record(elapsedTicks);
        }

        public TypingLatencyStageSnapshot[] Snapshot()
        {
            var snapshots = new TypingLatencyStageSnapshot[histograms.Length];
            for (var index = 0; index < histograms.Length; index++)
            {
                snapshots[index] = histograms[index].Snapshot((TypingLatencyStage)index);
            }
            return snapshots;
        }

        private static StageHistogram[] CreateHistograms()
        {
            var result = new StageHistogram[StageCount];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = new StageHistogram();
            }
            return result;
        }
    }

    private sealed class StageHistogram
    {
        private const int BucketCount = 64;
        private readonly long[] buckets = new long[BucketCount];
        private long sampleCount;
        private long totalTicks;
        private long maximumTicks;

        public void Record(long elapsedTicks)
        {
            var bucket = Math.Min(
                BucketCount - 1,
                BitOperations.Log2((ulong)elapsedTicks));
            Interlocked.Increment(ref buckets[bucket]);
            Interlocked.Increment(ref sampleCount);
            Interlocked.Add(ref totalTicks, elapsedTicks);
            UpdateMaximum(elapsedTicks);
        }

        public TypingLatencyStageSnapshot Snapshot(TypingLatencyStage stage)
        {
            var count = Volatile.Read(ref sampleCount);
            if (count <= 0)
            {
                return new TypingLatencyStageSnapshot(stage, 0, 0, 0, 0, 0, 0);
            }

            var maximum = Volatile.Read(ref maximumTicks);
            var medianTicks = Math.Min(maximum, PercentileTicks(count, 50));
            var p95Ticks = Math.Min(maximum, PercentileTicks(count, 95));
            var p99Ticks = Math.Min(maximum, PercentileTicks(count, 99));
            var total = Volatile.Read(ref totalTicks);
            return new TypingLatencyStageSnapshot(
                stage,
                count,
                ToNanoseconds(medianTicks),
                ToNanoseconds(p95Ticks),
                ToNanoseconds(p99Ticks),
                ToNanoseconds(maximum),
                ToNanoseconds(total) / (double)count);
        }

        private long PercentileTicks(long count, int percentile)
        {
            var target = Math.Max(1, checked((count * percentile + 99) / 100));
            long cumulative = 0;
            for (var index = 0; index < buckets.Length; index++)
            {
                cumulative += Volatile.Read(ref buckets[index]);
                if (cumulative >= target)
                {
                    return BucketUpperBound(index);
                }
            }

            return Math.Max(1, Volatile.Read(ref maximumTicks));
        }

        private void UpdateMaximum(long elapsedTicks)
        {
            var observed = Volatile.Read(ref maximumTicks);
            while (elapsedTicks > observed)
            {
                var previous = Interlocked.CompareExchange(
                    ref maximumTicks,
                    elapsedTicks,
                    observed);
                if (previous == observed)
                {
                    return;
                }
                observed = previous;
            }
        }

        private static long BucketUpperBound(int index)
        {
            if (index >= 62)
            {
                return long.MaxValue;
            }
            return (1L << (index + 1)) - 1;
        }
    }

    private static long ToNanoseconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0;
        }

        var nanoseconds = ticks * (1_000_000_000D / Stopwatch.Frequency);
        return nanoseconds >= long.MaxValue
            ? long.MaxValue
            : Math.Max(1, checked((long)Math.Ceiling(nanoseconds)));
    }
}
