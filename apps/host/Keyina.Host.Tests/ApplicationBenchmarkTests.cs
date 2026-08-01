using Keyina.Host.Benchmarks;

namespace Keyina.Host.Tests;

internal static class ApplicationBenchmarkTests
{
    [KeyinaTest("application benchmark suite covers settings speech and translation")]
    private static void ApplicationBenchmarkSuiteCoversLocalPaths()
    {
        var cases = ApplicationBenchmarks.RunAsync(
                warmupIterations: 1,
                measuredIterations: 1)
            .GetAwaiter()
            .GetResult();
        var names = cases.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[]
                 {
                     "application_settings_construct_sample",
                     "application_settings_construct_1000_snippets",
                     "application_settings_apply_1000_snippets",
                     "application_settings_apply_unchanged_1000_snippets",
                     "application_speech_start_stop_stub",
                     "application_translation_preview_stub",
                 })
        {
            AssertEx.True(names.Contains(expected), $"Missing application benchmark: {expected}.");
        }

        AssertEx.True(
            cases.All(item => item.MedianNanoseconds >= 0 && item.Checksum != 0),
            "Application benchmark cases did not produce valid timing evidence.");
        var snippetPopulation = cases.Single(item => string.Equals(
            item.Name,
            "application_settings_apply_1000_snippets",
            StringComparison.Ordinal));
        AssertEx.True(
            snippetPopulation.Checksum >= 1_000,
            "Snippet population benchmark did not materialize the configured rows.");
    }

    [KeyinaTest("asynchronous benchmark probes propagate operation failures")]
    private static void AsynchronousBenchmarkProbesPropagateFailures()
    {
        var propagated = false;
        try
        {
            _ = BenchmarkReport.MeasureAsync(
                    "failing_async_probe",
                    warmupIterations: 1,
                    measuredIterations: 1,
                    async () =>
                    {
                        await Task.Yield();
                        throw new InvalidOperationException("probe failure");
                    })
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidOperationException exception) when (
            string.Equals(exception.Message, "probe failure", StringComparison.Ordinal))
        {
            propagated = true;
        }

        AssertEx.True(propagated, "Async benchmark probe swallowed or replaced the operation failure.");
    }
}
