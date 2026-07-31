using System.Diagnostics;

namespace Keyina.Host.Benchmarks;

internal static class ResidentBenchmarks
{
    internal static IReadOnlyList<BenchmarkCase> Run(
        string residentExecutable,
        int warmupIterations,
        int measuredIterations)
    {
        if (!File.Exists(residentExecutable))
        {
            throw new FileNotFoundException("Native resident executable was not found.", residentExecutable);
        }

        return
        [
            MeasureProcess(
                "resident_resource_self_test",
                residentExecutable,
                "--resource-self-test",
                Math.Min(warmupIterations, 3),
                Math.Min(measuredIterations, 20)),
            MeasureProcess(
                "resident_profile_reload_self_test",
                residentExecutable,
                "--profile-reload-self-test",
                Math.Min(warmupIterations, 3),
                Math.Min(measuredIterations, 20)),
        ];
    }

    private static BenchmarkCase MeasureProcess(
        string name,
        string executable,
        string arguments,
        int warmupIterations,
        int measuredIterations)
    {
        for (var index = 0; index < warmupIterations; index++)
        {
            RunOnce(executable, arguments);
        }

        var samples = new long[measuredIterations];
        long checksum = 0;
        for (var index = 0; index < measuredIterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = RunOnce(executable, arguments);
            samples[index] = Stopwatch.GetTimestamp() - started;
            checksum += result.ExitCode + result.PeakWorkingSetBytes + result.PeakPagedMemoryBytes;
        }
        Array.Sort(samples);

        static long Percentile(long[] sorted, int numerator, int denominator)
        {
            var index = ((sorted.Length - 1L) * numerator + denominator - 1) / denominator;
            return sorted[index];
        }
        static double ToNanoseconds(long ticks) => ticks * (1_000_000_000d / Stopwatch.Frequency);

        return new BenchmarkCase(
            name,
            ToNanoseconds(Percentile(samples, 1, 2)),
            ToNanoseconds(Percentile(samples, 95, 100)),
            ToNanoseconds(Percentile(samples, 99, 100)),
            ToNanoseconds(samples[^1]),
            0,
            checksum);
    }

    private static ProcessResult RunOnce(string executable, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Resident benchmark process failed to start.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Resident benchmark timed out: {arguments}");
        }
        _ = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Resident benchmark failed ({process.ExitCode}): {arguments}. {error}");
        }

        long peakWorkingSet = 0;
        long peakPagedMemory = 0;
        try
        {
            process.Refresh();
            peakWorkingSet = process.PeakWorkingSet64;
            peakPagedMemory = process.PeakPagedMemorySize64;
        }
        catch (InvalidOperationException)
        {
            // Very short self-tests may release their process handle before peak counters can be queried.
        }

        return new ProcessResult(process.ExitCode, peakWorkingSet, peakPagedMemory);
    }

    private readonly record struct ProcessResult(
        int ExitCode,
        long PeakWorkingSetBytes,
        long PeakPagedMemoryBytes);
}
