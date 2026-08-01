using System.Diagnostics;
using System.Text.Json;

namespace Keyina.Host.Tests;

internal static class BenchmarkComparisonScriptTests
{
    [KeyinaTest("benchmark comparison accepts improvements neutral noise and absolute tolerance")]
    private static void BenchmarkComparisonAcceptsExpectedVariation()
    {
        using var fixture = new BenchmarkFixture();

        var improvement = fixture.Run(
            baselineMedian: 1_000_000,
            baselineP95: 2_000_000,
            currentMedian: 800_000,
            currentP95: 1_700_000);
        AssertEx.Equal(0, improvement.ExitCode, improvement.Error);

        var neutralNoise = fixture.Run(
            baselineMedian: 1_000_000,
            baselineP95: 2_000_000,
            currentMedian: 1_150_000,
            currentP95: 2_350_000);
        AssertEx.Equal(0, neutralNoise.ExitCode, neutralNoise.Error);

        var absoluteTolerance = fixture.Run(
            baselineMedian: 1_000,
            baselineP95: 2_000,
            currentMedian: 9_000,
            currentP95: 11_000);
        AssertEx.Equal(0, absoluteTolerance.ExitCode, absoluteTolerance.Error);
    }

    [KeyinaTest("benchmark comparison rejects material median and p95 regressions")]
    private static void BenchmarkComparisonRejectsMaterialRegression()
    {
        using var fixture = new BenchmarkFixture();
        var result = fixture.Run(
            baselineMedian: 1_000_000,
            baselineP95: 2_000_000,
            currentMedian: 1_500_000,
            currentP95: 2_800_000);

        AssertEx.Equal(1, result.ExitCode);
        AssertEx.True(
            result.Error.Contains("fixture_case", StringComparison.Ordinal) &&
            result.Error.Contains("MedianNanoseconds", StringComparison.Ordinal) &&
            result.Error.Contains("P95Nanoseconds", StringComparison.Ordinal),
            $"Regression output did not identify the case and both failed metrics. stderr: {result.Error}");
    }

    [KeyinaTest("publish benchmark gate is opt in and validates supplied baseline")]
    private static void PublishScriptExposesBenchmarkGateContract()
    {
        var publish = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "publish.ps1"));

        foreach (var required in new[]
                 {
                     "BenchmarkBaseline",
                     "BenchmarkCurrent",
                     "BenchmarkThresholds",
                     "RequireBenchmarkGate",
                     "compare-benchmarks.ps1",
                 })
        {
            AssertEx.True(
                publish.Contains(required, StringComparison.Ordinal),
                $"Publish script omitted benchmark gate token: {required}.");
        }
    }

    private sealed class BenchmarkFixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(),
            $"Keyina.BenchmarkComparison.{Guid.NewGuid():N}");
        private readonly string script = Path.Combine(
            RepositoryPaths.Root,
            "scripts",
            "windows",
            "compare-benchmarks.ps1");

        public BenchmarkFixture()
        {
            Directory.CreateDirectory(directory);
        }

        public ProcessResult Run(
            double baselineMedian,
            double baselineP95,
            double currentMedian,
            double currentP95)
        {
            var baseline = Path.Combine(directory, "baseline.json");
            var current = Path.Combine(directory, "current.json");
            var thresholds = Path.Combine(directory, "thresholds.json");
            WriteDocument(baseline, baselineMedian, baselineP95);
            WriteDocument(current, currentMedian, currentP95);
            File.WriteAllText(
                thresholds,
                JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    Defaults = new
                    {
                        Median = new
                        {
                            RelativeTolerance = 0.20,
                            AbsoluteToleranceNanoseconds = 10_000,
                        },
                        P95 = new
                        {
                            RelativeTolerance = 0.20,
                            AbsoluteToleranceNanoseconds = 10_000,
                        },
                    },
                    Cases = new { },
                }));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("-Baseline");
            startInfo.ArgumentList.Add(baseline);
            startInfo.ArgumentList.Add("-Current");
            startInfo.ArgumentList.Add(current);
            startInfo.ArgumentList.Add("-Thresholds");
            startInfo.ArgumentList.Add(thresholds);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Benchmark comparison process did not start.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Benchmark comparison process did not exit.");
            }
            return new ProcessResult(process.ExitCode, output, error);
        }

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void WriteDocument(string path, double median, double p95)
        {
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    Cases = new[]
                    {
                        new
                        {
                            Name = "fixture_case",
                            MedianNanoseconds = median,
                            P95Nanoseconds = p95,
                        },
                    },
                }));
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
