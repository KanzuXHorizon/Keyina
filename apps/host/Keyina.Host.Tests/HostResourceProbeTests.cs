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
