using System.Diagnostics;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Runtime;

namespace Keyina.Host.Benchmarks;

internal static class CommandOutputBenchmarks
{
    internal static IReadOnlyList<BenchmarkCase> Run(int warmupIterations, int measuredIterations)
    {
        var runner = new SnippetCommandOutputRunner();
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var cmd = Path.Combine(systemDirectory, "cmd.exe");
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var cases = new List<BenchmarkCase>
        {
            MeasureCapture(
                "command_cmd_small_stdout",
                runner,
                new SnippetExecutionConfiguration(cmd, "/d /s /c \"echo keyina\"", systemDirectory, 5_000),
                warmupIterations,
                measuredIterations),
            MeasureCapture(
                "command_cmd_nonzero_exit",
                runner,
                new SnippetExecutionConfiguration(cmd, "/d /s /c \"exit /b 7\"", systemDirectory, 5_000),
                warmupIterations,
                measuredIterations),
        };

        if (File.Exists(powershell))
        {
            cases.Add(MeasureCapture(
                "command_powershell_small_stdout",
                runner,
                new SnippetExecutionConfiguration(
                    powershell,
                    "-NoLogo -NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); 'keyina'\"",
                    Path.GetDirectoryName(powershell)!,
                    10_000),
                Math.Min(warmupIterations, 2),
                Math.Min(measuredIterations, 10)));
        }

        return cases;
    }

    private static BenchmarkCase MeasureCapture(
        string name,
        SnippetCommandOutputRunner runner,
        SnippetExecutionConfiguration execution,
        int warmupIterations,
        int measuredIterations)
    {
        for (var index = 0; index < warmupIterations; index++)
        {
            _ = runner.CaptureAsync(execution, CancellationToken.None).GetAwaiter().GetResult();
        }

        var samples = new long[measuredIterations];
        long checksum = 0;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        for (var index = 0; index < measuredIterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = runner.CaptureAsync(execution, CancellationToken.None).GetAwaiter().GetResult();
            samples[index] = Stopwatch.GetTimestamp() - started;
            checksum += result.Code.Length + (result.Output?.Length ?? 0) + (result.Success ? 1 : 0);
        }
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
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
            allocated / (double)measuredIterations,
            checksum);
    }
}
