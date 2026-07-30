using Keyina.Host.Diagnostics;

namespace Keyina.Host.Tests;

internal static class HostResourceProbeTests
{
    [KeyinaTest("host resource probe reports bounded non-negative process metrics")]
    private static void ResourceProbeReportsProcessMetrics()
    {
        var snapshot = HostResourceProbe.CaptureAsync(
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.True(snapshot.DurationMilliseconds >= 75, "Probe duration was unexpectedly short.");
        AssertEx.True(snapshot.WorkingSetBytes > 0, "Working set was not reported.");
        AssertEx.True(snapshot.PrivateMemoryBytes > 0, "Private memory was not reported.");
        AssertEx.True(snapshot.ManagedHeapBytes >= 0, "Managed heap was negative.");
        AssertEx.True(snapshot.ThreadCount > 0, "Thread count was not reported.");
        AssertEx.True(snapshot.ProcessorCount > 0, "Processor count was not reported.");
        AssertEx.True(double.IsFinite(snapshot.AverageCpuPercent), "CPU percentage was not finite.");
        AssertEx.True(snapshot.AverageCpuPercent >= 0, "CPU percentage was negative.");
    }

    [KeyinaTest("resident input resource budget enforces ten MiB and one thread")]
    private static void ResidentInputBudgetIsStrict()
    {
        AssertEx.True(
            ResidentInputResourceBudget.IsSatisfied(
                privateMemoryBytes: 10 * 1024 * 1024,
                threadCountDelta: 1,
                typingHookRunning: true,
                measurementContaminatedByInput: false),
            "The documented resident budget boundary was rejected.");
        AssertEx.False(
            ResidentInputResourceBudget.IsSatisfied(
                privateMemoryBytes: (10 * 1024 * 1024) + 1,
                threadCountDelta: 1,
                typingHookRunning: true,
                measurementContaminatedByInput: false),
            "Private memory above ten MiB passed the resident budget.");
        AssertEx.False(
            ResidentInputResourceBudget.IsSatisfied(
                privateMemoryBytes: 9 * 1024 * 1024,
                threadCountDelta: 2,
                typingHookRunning: true,
                measurementContaminatedByInput: false),
            "A second resident input thread passed the budget.");
        AssertEx.False(
            ResidentInputResourceBudget.IsSatisfied(
                privateMemoryBytes: 9 * 1024 * 1024,
                threadCountDelta: 1,
                typingHookRunning: true,
                measurementContaminatedByInput: true),
            "A contaminated resource measurement passed the budget.");
    }

    [KeyinaTest("host resource probe rejects misleading duration windows")]
    private static void InvalidDurationsAreRejected()
    {
        AssertThrows<ArgumentOutOfRangeException>(() =>
            HostResourceProbe.CaptureAsync(
                    TimeSpan.FromMilliseconds(49),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
        AssertThrows<ArgumentOutOfRangeException>(() =>
            HostResourceProbe.CaptureAsync(
                    TimeSpan.FromSeconds(31),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
