using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Keyina.Host.Benchmarks;

internal sealed record BenchmarkCase(
    string Name,
    double MedianNanoseconds,
    double P95Nanoseconds,
    double P99Nanoseconds,
    double MaxNanoseconds,
    double AllocatedBytesPerOperation,
    long Checksum);

internal sealed record BenchmarkEnvironment(
    string OsDescription,
    string FrameworkDescription,
    string ProcessArchitecture,
    int ProcessorCount,
    int WarmupIterations,
    int MeasuredIterations);

internal sealed record BenchmarkDocument(
    int SchemaVersion,
    BenchmarkEnvironment Environment,
    IReadOnlyList<BenchmarkCase> Cases);

internal static class BenchmarkReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static BenchmarkCase Measure(
        string name,
        int warmupIterations,
        int measuredIterations,
        Func<long> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warmupIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredIterations);
        ArgumentNullException.ThrowIfNull(operation);

        long checksum = 0;
        for (var index = 0; index < warmupIterations; index++)
        {
            checksum += operation();
        }

        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < measuredIterations; index++)
        {
            checksum += operation();
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;

        var samples = new long[measuredIterations];
        for (var index = 0; index < measuredIterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            checksum += operation();
            samples[index] = Stopwatch.GetTimestamp() - started;
        }

        Array.Sort(samples);
        static double ToNanoseconds(long ticks) => ticks * (1_000_000_000d / Stopwatch.Frequency);
        static long Percentile(long[] sorted, int numerator, int denominator)
        {
            var index = ((sorted.Length - 1L) * numerator + denominator - 1) / denominator;
            return sorted[index];
        }

        return new BenchmarkCase(
            name,
            ToNanoseconds(Percentile(samples, 1, 2)),
            ToNanoseconds(Percentile(samples, 95, 100)),
            ToNanoseconds(Percentile(samples, 99, 100)),
            ToNanoseconds(samples[^1]),
            allocatedBytes / (double)measuredIterations,
            checksum);
    }

    internal static void Write(string outputDirectory, BenchmarkDocument document)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "managed.json"),
            JsonSerializer.Serialize(document, JsonOptions),
            Encoding.UTF8);

        var csv = new StringBuilder();
        csv.AppendLine("name,median_ns,p95_ns,p99_ns,max_ns,allocated_bytes_per_operation,checksum");
        foreach (var item in document.Cases)
        {
            csv.Append(item.Name).Append(',')
                .Append(item.MedianNanoseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.P95Nanoseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.P99Nanoseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.MaxNanoseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.AllocatedBytesPerOperation.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(item.Checksum.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }
        File.WriteAllText(Path.Combine(outputDirectory, "managed.csv"), csv.ToString(), Encoding.UTF8);

        var markdown = new StringBuilder("# Managed benchmark report\n\n")
            .AppendLine("| Case | Median ns | P95 ns | P99 ns | Alloc B/op |")
            .AppendLine("|---|---:|---:|---:|---:|");
        foreach (var item in document.Cases)
        {
            markdown.Append("| ").Append(item.Name).Append(" | ")
                .Append(item.MedianNanoseconds.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(item.P95Nanoseconds.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(item.P99Nanoseconds.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(item.AllocatedBytesPerOperation.ToString("F1", CultureInfo.InvariantCulture)).AppendLine(" |");
        }
        File.WriteAllText(Path.Combine(outputDirectory, "managed.md"), markdown.ToString(), Encoding.UTF8);
    }
}
