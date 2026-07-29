using System.Diagnostics;

namespace Keyina.Host.Diagnostics;

public sealed record HostResourceSnapshot(
    double DurationMilliseconds,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedHeapBytes,
    int ThreadCount,
    int HandleCount,
    int ProcessorCount,
    double CpuTimeMilliseconds,
    double AverageCpuPercent);

public static class HostResourceProbe
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(30);

    public static async Task<HostResourceSnapshot> CaptureAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (duration < MinimumDuration || duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"Resource probe duration must be between {MinimumDuration.TotalMilliseconds} ms and {MaximumDuration.TotalSeconds} s.");
        }

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var startCpu = process.TotalProcessorTime;
        var startTimestamp = Stopwatch.GetTimestamp();

        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);

        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        process.Refresh();
        var cpuDelta = process.TotalProcessorTime - startCpu;
        var processorCount = Environment.ProcessorCount;
        var averageCpuPercent = elapsed <= TimeSpan.Zero
            ? 0
            : cpuDelta.TotalMilliseconds /
                (elapsed.TotalMilliseconds * processorCount) * 100.0;

        return new HostResourceSnapshot(
            elapsed.TotalMilliseconds,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false),
            process.Threads.Count,
            process.HandleCount,
            processorCount,
            cpuDelta.TotalMilliseconds,
            averageCpuPercent);
    }
}
