using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class TypingLatencyProfilerTests
{
    [KeyinaTest("typing latency profiler is inert while disabled")]
    private static void DisabledProfilerRecordsNothing()
    {
        TypingLatencyProfiler.SetEnabled(false);
        TypingLatencyProfiler.Clear();

        var startedAt = TypingLatencyProfiler.Start();
        TypingLatencyProfiler.Record(TypingLatencyStage.CallbackTotal, startedAt);

        var callback = Find(TypingLatencyStage.CallbackTotal);
        AssertEx.Equal(0L, startedAt);
        AssertEx.Equal(0L, callback.SampleCount);
    }

    [KeyinaTest("typing latency profiler reports monotonic percentiles")]
    private static void EnabledProfilerReportsMonotonicPercentiles()
    {
        TypingLatencyProfiler.Clear();
        TypingLatencyProfiler.SetEnabled(true);
        try
        {
            for (var index = 0; index < 8; index++)
            {
                var startedAt = TypingLatencyProfiler.Start();
                Thread.SpinWait(64 << index);
                TypingLatencyProfiler.Record(TypingLatencyStage.EngineProcess, startedAt);
            }

            var engine = Find(TypingLatencyStage.EngineProcess);
            AssertEx.Equal(8L, engine.SampleCount);
            AssertEx.True(engine.MedianNanoseconds > 0, "Median latency must be positive.");
            AssertEx.True(engine.MedianNanoseconds <= engine.P95Nanoseconds, "Median must not exceed p95.");
            AssertEx.True(engine.P95Nanoseconds <= engine.P99Nanoseconds, "p95 must not exceed p99.");
            AssertEx.True(engine.P99Nanoseconds <= engine.MaximumNanoseconds, "p99 must not exceed maximum.");
            AssertEx.True(engine.MeanNanoseconds > 0, "Mean latency must be positive.");
        }
        finally
        {
            TypingLatencyProfiler.SetEnabled(false);
            TypingLatencyProfiler.Clear();
        }
    }

    [KeyinaTest("clearing typing latency keeps the selected profiler state")]
    private static void ClearKeepsProfilerEnabled()
    {
        TypingLatencyProfiler.Clear();
        TypingLatencyProfiler.SetEnabled(true);
        try
        {
            var startedAt = TypingLatencyProfiler.Start();
            Thread.SpinWait(128);
            TypingLatencyProfiler.Record(TypingLatencyStage.SafetyGuard, startedAt);
            AssertEx.Equal(1L, Find(TypingLatencyStage.SafetyGuard).SampleCount);

            TypingLatencyProfiler.Clear();

            AssertEx.True(TypingLatencyProfiler.IsEnabled, "Clear must not disable profiling.");
            AssertEx.Equal(0L, Find(TypingLatencyStage.SafetyGuard).SampleCount);
        }
        finally
        {
            TypingLatencyProfiler.SetEnabled(false);
            TypingLatencyProfiler.Clear();
        }
    }

    private static TypingLatencyStageSnapshot Find(TypingLatencyStage stage) =>
        TypingLatencyProfiler.Snapshot().Single(item => item.Stage == stage);
}
